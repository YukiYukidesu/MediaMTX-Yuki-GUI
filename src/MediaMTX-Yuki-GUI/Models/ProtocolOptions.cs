namespace MediaMTX_Yuki_GUI.Models;

/// <summary>
/// RTMP 之外的对外协议。RTMP 的端口单独放在 <see cref="EngineOptions.RtmpPort"/>，
/// 因为它是本程序的推流入口，地位不同。
/// </summary>
public sealed class ProtocolOptions
{
    /// <summary>RTSP，专业播放器与移动端常用。</summary>
    public bool Rtsp { get; set; }

    public int RtspPort { get; set; } = 8554;

    /// <summary>HLS，浏览器和手机不装插件就能看。</summary>
    public bool Hls { get; set; }

    public int HlsPort { get; set; } = 8888;

    /// <summary>WebRTC，延迟能压到一秒以内。</summary>
    public bool Webrtc { get; set; }

    public int WebrtcPort { get; set; } = 8889;

    /// <summary>SRT，抗丢包，适合长距离或弱网。</summary>
    public bool Srt { get; set; }

    public int SrtPort { get; set; } = 8890;

    /// <summary>MoQ，基于 QUIC 的实验性协议，生态还不成熟。</summary>
    public bool Moq { get; set; }

    public int MoqPort { get; set; } = 8892;
}
