using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using MediaMTX_Yuki_GUI.Models;
using MediaMTX_Yuki_GUI.Services.Rtmp;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 本机 RTMP 中转站：引擎把流推到 127.0.0.1:port，这里换成平台的地址再推出去。
///
/// 为什么要绕这一圈：MediaMTX 的 forward 只会裸连平台，既不走代理也不认 SNI，
/// 想让它跟着设置页里那条代理走，唯一不碰内核、不注入的办法就是给它一个假目标，
/// 由我们自己当那个「平台」，再把数据搬到真平台上去。
///
/// 每个目标对应一个 targetId，引擎那边的 dest 写成
/// rtmp://127.0.0.1:port/proxy#&lt;targetId&gt;，app 恒为 proxy，流名就是 targetId。
/// </summary>
public sealed class RtmpProxyServer : IDisposable
{
    /// <summary>引擎连进来时用的 app 名，写死。</summary>
    public const string ProxyApp = "proxy";

    private readonly Func<string, ForwardTarget?> _resolve;

    private readonly Func<ProxyOptions?> _defaultProxy;

    private readonly Action<string>? _log;

    private TcpListener? _listener;

    private CancellationTokenSource? _cts;

    /// <summary>建一个还没开始监听的代理。</summary>
    /// <param name="resolve">按目标 Id 查配置，查不到返回 null。</param>
    /// <param name="defaultProxy">取设置页里「默认推流代理」。</param>
    /// <param name="log">日志出口。</param>
    public RtmpProxyServer(Func<string, ForwardTarget?> resolve, Func<ProxyOptions?> defaultProxy, Action<string>? log = null)
    {
        _resolve = resolve;
        _defaultProxy = defaultProxy;
        _log = log;
    }

    /// <summary>实际监听的端口，Start 之后才有意义。</summary>
    public int Port { get; private set; }

    /// <summary>是不是开着。</summary>
    public bool IsRunning => _listener is not null;

    /// <summary>拼出给引擎用的本地地址。</summary>
    /// <param name="port">本机监听端口。</param>
    /// <param name="targetId">目标 Id。</param>
    public static string BuildLocalDest(int port, string targetId)
    {
        return "rtmp://127.0.0.1:" + port + "/" + ProxyApp + "#" + targetId;
    }

    /// <summary>开始监听，端口由系统分配（避开 1935 这类常用口）。</summary>
    public void Start()
    {
        if (_listener is not null) return;

        TcpListener listener = new(IPAddress.Loopback, 0);

        listener.Start();

        _listener = listener;
        _cts = new CancellationTokenSource();

        Port = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = AcceptLoopAsync(listener, _cts.Token);

        Write("[代理] 已在本机 " + Port + " 端口就位");
    }

    /// <summary>停止监听并断开所有连接。</summary>
    public void Stop()
    {
        TcpListener? listener = _listener;

        _listener = null;

        try
        {
            listener?.Stop();
        }
        catch (Exception)
        {
            // 停监听失败没有补救价值
        }

        try
        {
            _cts?.Cancel();
        }
        catch (Exception)
        {
            // 同上
        }

        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
    }

    // ====== 接入 ======

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;

            try
            {
                await PumpAsync(client, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Write("[代理] " + ex.Message);
            }
        }
    }

    private async Task PumpAsync(TcpClient client, CancellationToken ct)
    {
        NetworkStream incomingStream = client.GetStream();

        RtmpSession incoming = await RtmpSession.AcceptAsync(incomingStream, ct).ConfigureAwait(false);

        string targetId = incoming.StreamName.Trim();

        if (targetId.Length == 0)
        {
            Write("[代理] 流名是空的，认不出是哪个目标");
            return;
        }

        ForwardTarget? target = _resolve(targetId);

        if (target is null)
        {
            Write("[代理] 找不到目标 " + targetId);
            return;
        }

        string dest = target.BuildDest();

        if (dest.Length == 0)
        {
            Write("[代理] 目标 " + target.Name + " 没填地址");
            return;
        }

        string label = target.DisplayName;

        Write("[代理] " + label + " 开始转流");

        ProxyOptions? proxy = ResolveProxy(target);

        // ====== 连真实平台 ======
        if (!TryParseDest(dest, out DestParts parts))
        {
            Write("[代理] " + label + " 的地址拆不开：" + dest);
            return;
        }

        Stream outgoing;

        try
        {
            outgoing = await ProxyDialer.ConnectAsync(parts.Host, parts.Port, proxy, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Write("[代理] " + label + " 连不上平台：" + ex.Message);
            return;
        }

        using Stream outgoingOwned = outgoing;

        Stream channel = outgoing;

        if (parts.Tls)
        {
            SslStream tls = new(outgoing, leaveInnerStreamOpen: false);

            try
            {
                await tls.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions { TargetHost = parts.Host },
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Write("[代理] " + label + " TLS 握手失败：" + ex.Message);
                return;
            }

            channel = tls;
        }

        using (channel)
        {
            // 平台在握手/命令阶段断开时会抛 EndOfStreamException，消息只有一句
            // "Unable to read beyond the end of the stream."，看不出是走到哪一步断的，
            // 所以这里分阶段打点，排障时能直接看出握手过了没有。
            RtmpSession outgoingSession;

            try
            {
                Write("[代理] " + label + " TLS 已建立，开始 RTMP 握手与命令往返");

                // 只在连平台这一段挂跟踪，避免把引擎连进来那半边的阶段也打出来
                RtmpSession.Trace = step => Write("[代理] " + label + " " + step);

                outgoingSession = await RtmpSession.ConnectAsync(channel, parts.App, parts.StreamName, parts.TcUrl, ct).ConfigureAwait(false);

                RtmpSession.Trace = null;
            }
            catch (Exception ex)
            {
                RtmpSession.Trace = null;

                Write("[代理] " + label + " RTMP 握手或命令往返失败：" + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            Write("[代理] " + label + " 平台已接收 publish，开始搬流");

            await ForwardAsync(incoming, outgoingSession, label, ct).ConfigureAwait(false);
        }
    }

    // ====== 搬数据 ======

    private async Task ForwardAsync(RtmpSession incoming, RtmpSession outgoing, string label, CancellationToken ct)
    {
        long bytes = 0;
        long frames = 0;

        while (!ct.IsCancellationRequested)
        {
            RtmpMessage? message = await incoming.Reader.ReadMessageAsync(ct).ConfigureAwait(false);

            if (message is null) break;

            if (message.Type == RtmpMessageType.SetChunkSize)
            {
                incoming.Reader.SetChunkSize(ReadInt32(message.Payload));
                continue;
            }

            if (message.IsMedia)
            {
                await outgoing.SendMediaAsync(message, ct).ConfigureAwait(false);

                bytes += message.Payload.Length;
                frames++;

                if (frames % 600 == 0) Write("[代理] " + label + " 已转发 " + (bytes / 1024 / 1024) + " MB");
            }
            else if (message.Type == RtmpMessageType.DataAmf0)
            {
                await outgoing.SendDataAsync(message.Payload, message.Timestamp, ct).ConfigureAwait(false);
            }
        }

        Write("[代理] " + label + " 结束，共 " + (bytes / 1024 / 1024) + " MB");
    }

    // ====== 工具 ======

    private ProxyOptions? ResolveProxy(ForwardTarget target)
    {
        return ResolveTargetProxy(target, _defaultProxy());
    }

    /// <summary>
    /// 一条目标最终走哪条代理。目标的档位优先，跟随默认时回落设置页那一档。
    /// NeedsProxy 与中转站都调这里，避免两边各写一套判断走岔。
    /// </summary>
    /// <param name="target">输出目标。</param>
    /// <param name="fallback">「跟随默认」时用的默认代理，可以为 null。</param>
    public static ProxyOptions? ResolveTargetProxy(ForwardTarget target, ProxyOptions? fallback)
    {
        if (target.ProxyMode == ProxyOptions.Direct) return new ProxyOptions { Mode = ProxyOptions.Direct };

        if (target.ProxyMode == ProxyOptions.System) return new ProxyOptions { Mode = ProxyOptions.System };

        if (target.HasCustomProxy)
        {
            return new ProxyOptions
            {
                Mode = ProxyOptions.Manual,
                Url = target.ProxyUrl,
            };
        }

        return fallback;
    }

    /// <summary>把一条推流地址拆成连接需要的几段。</summary>
    private static bool TryParseDest(string dest, out DestParts parts)
    {
        parts = default;

        int hash = dest.IndexOf('#');

        string urlPart = hash >= 0 ? dest[..hash] : dest;
        string streamName = hash >= 0 ? dest[(hash + 1)..] : string.Empty;

        if (streamName.Length == 0) return false;

        if (!Uri.TryCreate(urlPart, UriKind.Absolute, out Uri? uri)) return false;

        string app = uri.AbsolutePath.Trim('/');

        if (app.Length == 0) return false;

        bool tls = uri.Scheme.Equals("rtmps", StringComparison.OrdinalIgnoreCase);

        int port = uri.IsDefaultPort ? (tls ? 443 : 1935) : uri.Port;

        parts = new DestParts(tls, uri.Host, port, app, streamName, urlPart);

        return true;
    }

    private static int ReadInt32(byte[] payload)
    {
        if (payload.Length < 4) return 128;

        return (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3];
    }

    private void Write(string text)
    {
        _log?.Invoke(text);
    }

    /// <summary>拆开的推流地址。</summary>
    /// <param name="Tls">是不是 rtmps。</param>
    /// <param name="Host">主机。</param>
    /// <param name="Port">端口。</param>
    /// <param name="App">app 路径。</param>
    /// <param name="StreamName">流名（含推流码）。</param>
    /// <param name="TcUrl">原始地址，# 之前那一段。</param>
    private readonly record struct DestParts(bool Tls, string Host, int Port, string App, string StreamName, string TcUrl);
}
