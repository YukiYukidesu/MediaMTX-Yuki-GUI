namespace MediaMTX_Yuki_GUI.Services.Rtmp;

/// <summary>
/// 按 chunk 写 RTMP 消息。
///
/// 这里刻意只用「fmt 0 打头 + fmt 3 续片」这一种组合：
/// 每条消息都带完整的绝对时间戳与流号，对端不用猜。
/// 代价是每帧多 11 个字节，比音视频数据本身可以忽略。
/// </summary>
public sealed class RtmpChunkWriter
{
    private readonly Stream _stream;

    private byte[] _buffer = new byte[8 * 1024];

    /// <summary>对我们发出的数据生效的块大小，默认 128。</summary>
    public int ChunkSize { get; private set; } = 128;

    /// <summary>包一层流。</summary>
    /// <param name="stream">RTMP 数据流。</param>
    public RtmpChunkWriter(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>改块大小（调用方负责同时给对端发 SetChunkSize 消息）。</summary>
    /// <param name="size">新的块大小。</param>
    public void SetChunkSize(int size)
    {
        if (size > 0) ChunkSize = size;
    }

    /// <summary>写一条完整消息。</summary>
    /// <param name="message">消息。</param>
    /// <param name="chunkStreamId">走哪条 chunk 流，2-63 之间最省字节。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task WriteMessageAsync(RtmpMessage message, uint chunkStreamId, CancellationToken ct)
    {
        byte[] payload = message.Payload;

        // ====== 第一片：fmt 0 ======
        WriteBasicHeader(chunkStreamId, 0);
        WriteUInt24(message.Timestamp);
        WriteUInt24((uint)payload.Length);
        WriteByte(message.Type);
        WriteUInt32LE(message.StreamId);

        int offset = 0;
        int need = Math.Min(ChunkSize, payload.Length);

        await WritePayloadAsync(payload, offset, need, ct).ConfigureAwait(false);

        offset += need;

        // ====== 后续片：fmt 3 ======
        while (offset < payload.Length)
        {
            WriteBasicHeader(chunkStreamId, 3);

            need = Math.Min(ChunkSize, payload.Length - offset);

            await WritePayloadAsync(payload, offset, need, ct).ConfigureAwait(false);

            offset += need;
        }

        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    // ====== 内部 ======

    private async Task WritePayloadAsync(byte[] payload, int offset, int count, CancellationToken ct)
    {
        if (count <= 0) return;

        await _stream.WriteAsync(payload.AsMemory(offset, count), ct).ConfigureAwait(false);
    }

    private void WriteBasicHeader(uint chunkStreamId, int format)
    {
        if (chunkStreamId < 64)
        {
            WriteByte((byte)((format << 6) | (int)chunkStreamId));
        }
        else if (chunkStreamId < 320)
        {
            WriteByte((byte)((format << 6) | 0));
            WriteByte((byte)(chunkStreamId - 64));
        }
        else
        {
            uint value = chunkStreamId - 64;

            WriteByte((byte)((format << 6) | 1));
            WriteByte((byte)(value & 0xFF));
            WriteByte((byte)((value >> 8) & 0xFF));
        }
    }

    private void WriteByte(byte value)
    {
        _buffer[0] = value;

        _stream.Write(_buffer, 0, 1);
    }

    private void WriteUInt24(uint value)
    {
        _buffer[0] = (byte)((value >> 16) & 0xFF);
        _buffer[1] = (byte)((value >> 8) & 0xFF);
        _buffer[2] = (byte)(value & 0xFF);

        _stream.Write(_buffer, 0, 3);
    }

    private void WriteUInt32LE(uint value)
    {
        _buffer[0] = (byte)(value & 0xFF);
        _buffer[1] = (byte)((value >> 8) & 0xFF);
        _buffer[2] = (byte)((value >> 16) & 0xFF);
        _buffer[3] = (byte)((value >> 24) & 0xFF);

        _stream.Write(_buffer, 0, 4);
    }
}
