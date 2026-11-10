namespace MediaMTX_Yuki_GUI.Services.Rtmp;

/// <summary>
/// 按 chunk 读 RTMP 消息。
///
/// RTMP 的块头有四种格式（fmt 0-3），区别只在于「哪些字段可以省略」：
/// - fmt 0：时间戳、长度、类型、流号全给，绝对时间戳；
/// - fmt 1：省掉流号，时间戳是相对上一帧的差值；
/// - fmt 2：再省掉长度与类型；
/// - fmt 3：全省，连块头都没有，直接接上一块的后续数据。
///
/// 分片时每片最多 <see cref="ChunkSize"/> 字节，除了第一片之外都带 fmt 3 头。
/// 块大小会被对端用 SetChunkSize 改掉，所以这个字段是可变的。
/// </summary>
public sealed class RtmpChunkReader
{
    private readonly Stream _stream;

    private readonly Dictionary<uint, ChunkState> _states = [];

    private byte[] _buffer = new byte[8 * 1024];

    /// <summary>对端告诉我们的块大小，默认 128。</summary>
    public int ChunkSize { get; private set; } = 128;

    /// <summary>包一层流。</summary>
    /// <param name="stream">RTMP 数据流。</param>
    public RtmpChunkReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>读下一条完整消息。</summary>
    /// <param name="ct">取消令牌。</param>
    public async Task<RtmpMessage?> ReadMessageAsync(CancellationToken ct)
    {
        ChunkState? current = null;
        int need = 0;

        while (true)
        {
            byte first = await ReadByteAsync(ct).ConfigureAwait(false);

            int format = first >> 6;
            uint chunkStreamId = (uint)(first & 0x3F);

            if (chunkStreamId == 0)
            {
                byte b = await ReadByteAsync(ct).ConfigureAwait(false);
                chunkStreamId = (uint)(64 + b);
            }
            else if (chunkStreamId == 1)
            {
                byte b0 = await ReadByteAsync(ct).ConfigureAwait(false);
                byte b1 = await ReadByteAsync(ct).ConfigureAwait(false);
                chunkStreamId = (uint)(64 + b0 + (b1 << 8));
            }

            if (!_states.TryGetValue(chunkStreamId, out ChunkState? state))
            {
                state = new ChunkState();
                _states[chunkStreamId] = state;
            }

            if (format == 0)
            {
                byte[] head = new byte[11];
                await _stream.ReadExactlyAsync(head, ct).ConfigureAwait(false);

                // fmt 0 的头固定 11 字节：时间戳 3 + 长度 3 + 类型 1 + 流号 4
                state.Timestamp = ReadUInt24(head, 0);
                state.Length = ReadUInt24(head, 3);
                state.Type = head[6];
                state.StreamId = (uint)(head[7] | (head[8] << 8) | (head[9] << 16) | (head[10] << 24));
                state.ExtendedTimestamp = state.Timestamp == 0xFFFFFF;

                if (state.ExtendedTimestamp)
                {
                    state.Timestamp = await ReadUInt32Async(ct).ConfigureAwait(false);
                }

                state.Payload = new byte[state.Length];
                state.Received = 0;
                state.TimestampDelta = 0;
            }
            else if (format == 1 || format == 2)
            {
                int headSize = format == 1 ? 7 : 3;
                byte[] head = new byte[headSize];

                await _stream.ReadExactlyAsync(head, ct).ConfigureAwait(false);

                uint delta = ReadUInt24(head, 0);

                state.TimestampDelta = delta;
                state.ExtendedTimestamp = delta == 0xFFFFFF;

                if (state.ExtendedTimestamp) state.TimestampDelta = await ReadUInt32Async(ct).ConfigureAwait(false);

                state.Timestamp += state.TimestampDelta;

                // fmt 1 带消息长度与类型；fmt 2 省略这两项，沿用上一条的。
                // 但**两者都是「新消息的头」**，都必须开始一条新消息——
                // 早先只给 fmt 1 建 Payload，导致引擎用 fmt 2 打头时报「RTMP 块头不完整」。
                if (format == 1)
                {
                    state.Length = ReadUInt24(head, 3);
                    state.Type = head[6];
                }

                state.Payload = new byte[state.Length];
                state.Received = 0;
            }
            else
            {
                // fmt 3：沿用上一次的时长与类型；跨消息时才算新的一帧
                if (state.Payload is null || state.Received >= state.Payload.Length)
                {
                    state.Payload = new byte[state.Length];
                    state.Received = 0;
                    state.Timestamp += state.TimestampDelta;
                }

                if (state.ExtendedTimestamp) await ReadUInt32Async(ct).ConfigureAwait(false);
            }

            if (state.Payload is null) throw new IOException("RTMP 块头不完整");

            current = state;

            int remaining = state.Payload.Length - state.Received;
            need = Math.Min(ChunkSize, remaining);

            if (need <= 0) return TakeMessage(state);

            if (_buffer.Length < need) _buffer = new byte[need];

            await _stream.ReadExactlyAsync(_buffer.AsMemory(0, need), ct).ConfigureAwait(false);

            Array.Copy(_buffer, 0, state.Payload, state.Received, need);

            state.Received += need;

            if (state.Received >= state.Payload.Length)
            {
                RtmpMessage message = new(state.Type, state.StreamId, state.Timestamp, state.Payload);

                // 把 Payload 交出去之后清空，避免下一帧沿用旧缓冲
                state.Payload = null;

                return message;
            }
        }
    }

    /// <summary>对端改了块大小，下次读分片就按新的来。</summary>
    /// <param name="size">新的块大小。</param>
    public void SetChunkSize(int size)
    {
        if (size > 0) ChunkSize = size;
    }

    // ====== 内部 ======

    private static RtmpMessage TakeMessage(ChunkState state)
    {
        byte[] payload = state.Payload ?? [];

        state.Payload = null;
        state.Received = 0;

        return new RtmpMessage(state.Type, state.StreamId, state.Timestamp, payload);
    }

    private async Task<byte> ReadByteAsync(CancellationToken ct)
    {
        byte[] single = new byte[1];

        await _stream.ReadExactlyAsync(single, ct).ConfigureAwait(false);

        return single[0];
    }

    private async Task<uint> ReadUInt32Async(CancellationToken ct)
    {
        byte[] head = new byte[4];

        await _stream.ReadExactlyAsync(head, ct).ConfigureAwait(false);

        return (uint)((head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3]);
    }

    private static uint ReadUInt24(byte[] buffer, int offset)
    {
        return (uint)((buffer[offset] << 16) | (buffer[offset + 1] << 8) | buffer[offset + 2]);
    }

    private sealed class ChunkState
    {
        public uint Timestamp;
        public uint TimestampDelta;
        public uint Length;
        public byte Type;
        public uint StreamId;
        public bool ExtendedTimestamp;
        public byte[]? Payload;
        public int Received;
    }
}
