namespace MediaMTX_Yuki_GUI.Services.Rtmp;

/// <summary>RTMP 消息类型 ID。</summary>
public static class RtmpMessageType
{
    /// <summary>设置块大小；默认 128 字节，双方都可以改。</summary>
    public const byte SetChunkSize = 1;

    /// <summary>放弃某条流。</summary>
    public const byte Abort = 2;

    /// <summary>确认窗口大小。</summary>
    public const byte Acknowledgement = 3;

    /// <summary>窗口确认（告诉对端我收到了多少字节）。</summary>
    public const byte WindowAckSize = 5;

    /// <summary>设置对端带宽。</summary>
    public const byte SetPeerBandwidth = 6;

    /// <summary>音频。</summary>
    public const byte Audio = 8;

    /// <summary>视频。</summary>
    public const byte Video = 9;

    /// <summary>AMF3 命令。</summary>
    public const byte CommandAmf3 = 17;

    /// <summary>AMF0 命令。</summary>
    public const byte CommandAmf0 = 20;

    /// <summary>数据（onMetaData 之类）。</summary>
    public const byte DataAmf0 = 18;
}

/// <summary>
/// 一条完整的 RTMP 消息。chunk 分片只是运输方式，拆完就还原成这个。
/// </summary>
/// <param name="Type">消息类型，见 <see cref="RtmpMessageType"/>。</param>
/// <param name="StreamId">流号，命令走 0，音视频走发布时谈好的那条。</param>
/// <param name="Timestamp">绝对时间戳（毫秒）。</param>
/// <param name="Payload">消息体。</param>
public sealed record RtmpMessage(byte Type, uint StreamId, uint Timestamp, byte[] Payload)
{
    /// <summary>是不是音视频数据——这些才需要转发。</summary>
    public bool IsMedia => Type is RtmpMessageType.Audio or RtmpMessageType.Video;

    /// <summary>是不是命令消息。</summary>
    public bool IsCommand => Type is RtmpMessageType.CommandAmf0 or RtmpMessageType.CommandAmf3;
}
