namespace MediaMTX_Yuki_GUI.Services.Rtmp;

/// <summary>
/// 一条已经谈好的推流会话：读消息、写消息、关连接。
///
/// 服务端与客户端两侧谈完之后拿到的都是它，转发循环只认这个类型，
/// 不必关心自己站在哪一边。
/// </summary>
public sealed class RtmpSession : IDisposable
{
    /// <summary>createStream 应答里回的流编号。我们一次只服务一路推流，固定 1 就够。</summary>
    private const double StreamId = 1;

    /// <summary>
    /// 握手与命令往返的阶段跟踪。默认 null（不产生任何开销），
    /// 排障时由上层挂上，用来区分「平台根本没应答」和「平台在某一具体命令后断开」。
    /// </summary>
    public static Action<string>? Trace { get; set; }

    private readonly Stream _stream;

    private readonly RtmpChunkReader _reader;

    private readonly RtmpChunkWriter _writer;

    private bool _disposed;

    private RtmpSession(Stream stream, RtmpChunkReader reader, RtmpChunkWriter writer, string app, string streamName, uint publishChunkStreamId)
    {
        _stream = stream;
        _reader = reader;
        _writer = writer;
        App = app;
        StreamName = streamName;
        PublishChunkStreamId = publishChunkStreamId;
    }

    /// <summary>发布用的 app 名。</summary>
    public string App { get; }

    /// <summary>发布的流名，也就是我们塞进去的 targetId。</summary>
    public string StreamName { get; }

    /// <summary>对端发命令时用的 chunk 流号，回消息时沿用它最省事。</summary>
    public uint PublishChunkStreamId { get; }

    /// <summary>底层流，转发循环里直接读写用。</summary>
    public Stream Stream => _stream;

    /// <summary>读消息的组件。</summary>
    public RtmpChunkReader Reader => _reader;

    /// <summary>写消息的组件。</summary>
    public RtmpChunkWriter Writer => _writer;

    /// <summary>
    /// 以服务端身份接受一条推流：握手、应答命令、一直走到 publish 成功。
    /// </summary>
    /// <param name="stream">已连上的流（还没握手）。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<RtmpSession> AcceptAsync(Stream stream, CancellationToken ct)
    {
        await RtmpHandshake.AcceptAsync(stream, ct).ConfigureAwait(false);

        RtmpChunkReader reader = new(stream);
        RtmpChunkWriter writer = new(stream);

        string app = string.Empty;
        string streamName = string.Empty;
        uint publishChunkStreamId = 4;

        // 先发一套「参数」，很多推流器在 connect 之前就等着这些
        await writer.WriteMessageAsync(BuildWindowAckSize(5000000), 2, ct).ConfigureAwait(false);
        await writer.WriteMessageAsync(BuildSetPeerBandwidth(5000000, 2), 2, ct).ConfigureAwait(false);
        await writer.WriteMessageAsync(BuildSetChunkSize(4096), 2, ct).ConfigureAwait(false);

        writer.SetChunkSize(4096);

        while (true)
        {
            RtmpMessage? message = await reader.ReadMessageAsync(ct).ConfigureAwait(false);

            if (message is null) throw new IOException("推流方提前断开");

            switch (message.Type)
            {
                case RtmpMessageType.SetChunkSize:
                {
                    reader.SetChunkSize(ReadInt32(message.Payload));
                    break;
                }

                case RtmpMessageType.CommandAmf0:
                {
                    Amf0Reader amf = new(message.Payload);

                    object?[] values = amf.ReadValues(2);

                    string name = values[0] as string ?? string.Empty;
                    double transactionId = values[1] is double id ? id : 0;

                    if (name == "connect")
                    {
                        Amf0Object command = amf.ReadValue() as Amf0Object ?? new Amf0Object();

                        app = command.GetString("app");

                        await writer.WriteMessageAsync(
                            BuildCommand("_result", transactionId,
                                new Amf0Object(
                                    ("fmsVer", "FMS/3,0,1,123"),
                                    ("capabilities", 31d)),
                                new Amf0Object(
                                    ("level", "status"),
                                    ("code", "NetConnection.Connect.Success"),
                                    ("description", "Connection succeeded."),
                                    ("objectEncoding", 0d))),
                            message.Type == RtmpMessageType.CommandAmf0 ? 3u : 3u,
                            ct).ConfigureAwait(false);

                        await writer.WriteMessageAsync(BuildSetChunkSize(4096), 2, ct).ConfigureAwait(false);
                    }
                    else if (name == "createStream")
                    {
                        // 第三个参数必须是流编号（数字）。早先这里回的是一个状态对象，
                        // 引擎那侧（gortmplib）拿不到数字，只会在日志里报一句
                        // "bad result: &{3 0 _result 4 [[...]]}" 然后断开，听起来像平台不认，
                        // 其实是我们的应答格式不对。
                        await writer.WriteMessageAsync(
                            BuildCommand("_result", transactionId, null, StreamId),
                            3,
                            ct).ConfigureAwait(false);
                    }
                    else if (name == "publish")
                    {
                        // publish 的参数是「事务号之后先一个 null，再才是流名与推流类型」
                        amf.ReadValue();

                        streamName = amf.ReadValue() as string ?? string.Empty;

                        publishChunkStreamId = 4;

                        await writer.WriteMessageAsync(
                            BuildCommand("onStatus", 0,
                                new Amf0Object(),
                                new Amf0Object(
                                    ("level", "status"),
                                    ("code", "NetStream.Publish.Start"),
                                    ("description", "Start publishing"))),
                            5,
                            ct).ConfigureAwait(false);

                        // 谈成就能交出去了。再往下读会把第一条音视频消息读进这里，
                        // 那条多半是 AVC 序列头，丢了平台就认不出有轨道。
                        return new RtmpSession(stream, reader, writer, app, streamName, publishChunkStreamId);
                    }

                    break;
                }

                case RtmpMessageType.WindowAckSize:
                case RtmpMessageType.SetPeerBandwidth:
                case RtmpMessageType.Acknowledgement:
                case RtmpMessageType.DataAmf0:
                case RtmpMessageType.CommandAmf3:
                {
                    // 这些不影响握手，直接忽略
                    break;
                }

                default:
                {
                    // 走到音视频数据说明对方已经开始推了，publish 一定谈成了
                    if (message.IsMedia && streamName.Length > 0)
                    {
                        return new RtmpSession(stream, reader, writer, app, streamName, publishChunkStreamId);
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// 以客户端身份连上平台并开始推流。
    /// </summary>
    /// <param name="stream">已连上的流（还没握手，TLS 已由上层做好）。</param>
    /// <param name="app">平台侧的 app 路径。</param>
    /// <param name="streamName">平台侧的流名（含推流码）。</param>
    /// <param name="tcUrl">完整推流地址，平台用它校验。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<RtmpSession> ConnectAsync(Stream stream, string app, string streamName, string tcUrl, CancellationToken ct)
    {
        await RtmpHandshake.ConnectAsync(stream, ct).ConfigureAwait(false);

        RtmpChunkReader reader = new(stream);
        RtmpChunkWriter writer = new(stream);

        await writer.WriteMessageAsync(BuildSetChunkSize(4096), 2, ct).ConfigureAwait(false);

        writer.SetChunkSize(4096);

        Trace?.Invoke("发出 connect（app=" + app + "）");

        // ====== connect ======
        Amf0Writer connect = new();

        connect.WriteString("connect")
            .WriteNumber(1)
            .WriteObject(new Amf0Object(
                ("app", app),
                ("type", "nonprivate"),
                ("flashVer", "FMLE/3.0 (compatible; FMSc/1.0)"),
                ("swfUrl", string.Empty),
                ("tcUrl", tcUrl)));

        await writer.WriteMessageAsync(new RtmpMessage(RtmpMessageType.CommandAmf0, 0, 0, connect.ToArray()), 3, ct).ConfigureAwait(false);

        double createStreamTransaction = 0;

        while (true)
        {
            RtmpMessage? message = await reader.ReadMessageAsync(ct).ConfigureAwait(false);

            if (message is null) throw new IOException("平台提前断开");

            if (message.Type == RtmpMessageType.SetChunkSize)
            {
                reader.SetChunkSize(ReadInt32(message.Payload));
                continue;
            }

            if (message.Type == RtmpMessageType.WindowAckSize)
            {
                // 记住窗口大小，音视频转发时按需回确认
                continue;
            }

            if (message.Type != RtmpMessageType.CommandAmf0) continue;

            Amf0Reader amf = new(message.Payload);

            object?[] values = amf.ReadValues(2);

            if (values[0] is not string name) continue;

            if (name == "_error")
            {
                throw new IOException("平台拒绝了连接");
            }

            if (name == "_result" && createStreamTransaction == 0)
            {
                createStreamTransaction = values[1] is double id ? id : 1;

                break;
            }
        }

        Trace?.Invoke("收到 connect 的 _result");

        // ====== createStream ======
        Amf0Writer createStream = new();

        createStream.WriteString("createStream").WriteNumber(2).WriteNull();

        await writer.WriteMessageAsync(new RtmpMessage(RtmpMessageType.CommandAmf0, 0, 0, createStream.ToArray()), 3, ct).ConfigureAwait(false);

        uint streamId = 1;

        while (true)
        {
            RtmpMessage? message = await reader.ReadMessageAsync(ct).ConfigureAwait(false);

            if (message is null) throw new IOException("平台提前断开");

            if (message.Type == RtmpMessageType.SetChunkSize)
            {
                reader.SetChunkSize(ReadInt32(message.Payload));
                continue;
            }

            if (message.Type != RtmpMessageType.CommandAmf0) continue;

            Amf0Reader amf = new(message.Payload);

            object?[] values = amf.ReadValues(2);

            if (values[0] is not string name) continue;

            if (name == "_error") throw new IOException("平台拒绝创建流");

            if (name != "_result") continue;

            object? third = amf.ReadValue();

            if (third is double id) streamId = (uint)id;

            break;
        }

        Trace?.Invoke("收到 createStream 的 _result，streamId=" + streamId);

        // ====== publish ======
        Amf0Writer publish = new();

        publish.WriteString("publish")
            .WriteNumber(3)
            .WriteNull()
            .WriteString(streamName)
            .WriteString("live");

        await writer.WriteMessageAsync(new RtmpMessage(RtmpMessageType.CommandAmf0, streamId, 0, publish.ToArray()), 4, ct).ConfigureAwait(false);

        Trace?.Invoke("发出 publish（流名=" + streamName + "）");

        // 等 onStatus（有的平台还会先发 _result）
        while (true)
        {
            RtmpMessage? message = await reader.ReadMessageAsync(ct).ConfigureAwait(false);

            if (message is null) throw new IOException("平台提前断开");

            if (message.Type == RtmpMessageType.SetChunkSize)
            {
                reader.SetChunkSize(ReadInt32(message.Payload));
                continue;
            }

            if (message.Type != RtmpMessageType.CommandAmf0) continue;

            Amf0Reader amf = new(message.Payload);

            object?[] values = amf.ReadValues(2);

            if (values[0] is not string name) continue;

            if (name == "onStatus")
            {
                object?[] rest = amf.ReadValues(1);

                if (rest[0] is Amf0Object status && status.GetString("level") == "error")
                {
                    throw new IOException("平台拒绝推流：" + status.GetString("description"));
                }

                break;
            }
        }

        return new RtmpSession(stream, reader, writer, app, streamName, 4);
    }

    /// <summary>发一条元数据（把音频视频参数交给平台）。</summary>
    /// <param name="payload">原始消息体，原样转发最省事。</param>
    /// <param name="timestamp">时间戳。</param>
    /// <param name="ct">取消令牌。</param>
    public Task SendDataAsync(byte[] payload, uint timestamp, CancellationToken ct)
    {
        return _writer.WriteMessageAsync(new RtmpMessage(RtmpMessageType.DataAmf0, 1, timestamp, payload), 4, ct);
    }

    /// <summary>发一帧音视频。</summary>
    /// <param name="message">原消息。</param>
    /// <param name="ct">取消令牌。</param>
    public Task SendMediaAsync(RtmpMessage message, CancellationToken ct)
    {
        uint chunkStreamId = message.Type == RtmpMessageType.Video ? 6u : 5u;

        return _writer.WriteMessageAsync(message with { StreamId = 1 }, chunkStreamId, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            _stream.Dispose();
        }
        catch (Exception)
        {
            // 关连接失败没有补救价值
        }
    }

    // ====== 命令组装 ======

    private static RtmpMessage BuildCommand(string name, double transactionId, params object?[] arguments)
    {
        Amf0Writer writer = new();

        writer.WriteString(name).WriteNumber(transactionId);

        foreach (object? argument in arguments) writer.WriteValue(argument);

        return new RtmpMessage(RtmpMessageType.CommandAmf0, 0, 0, writer.ToArray());
    }

    private static RtmpMessage BuildWindowAckSize(int size)
    {
        byte[] payload = new byte[4];

        payload[0] = (byte)((size >> 24) & 0xFF);
        payload[1] = (byte)((size >> 16) & 0xFF);
        payload[2] = (byte)((size >> 8) & 0xFF);
        payload[3] = (byte)(size & 0xFF);

        return new RtmpMessage(RtmpMessageType.WindowAckSize, 0, 0, payload);
    }

    private static RtmpMessage BuildSetPeerBandwidth(int size, byte limit)
    {
        byte[] payload = new byte[5];

        payload[0] = (byte)((size >> 24) & 0xFF);
        payload[1] = (byte)((size >> 16) & 0xFF);
        payload[2] = (byte)((size >> 8) & 0xFF);
        payload[3] = (byte)(size & 0xFF);
        payload[4] = limit;

        return new RtmpMessage(RtmpMessageType.SetPeerBandwidth, 0, 0, payload);
    }

    private static RtmpMessage BuildSetChunkSize(int size)
    {
        byte[] payload = new byte[4];

        payload[0] = (byte)((size >> 24) & 0xFF);
        payload[1] = (byte)((size >> 16) & 0xFF);
        payload[2] = (byte)((size >> 8) & 0xFF);
        payload[3] = (byte)(size & 0xFF);

        return new RtmpMessage(RtmpMessageType.SetChunkSize, 0, 0, payload);
    }

    private static int ReadInt32(byte[] payload)
    {
        if (payload.Length < 4) return 128;

        return (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3];
    }
}
