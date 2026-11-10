namespace MediaMTX_Yuki_GUI.Models;

/// <summary>连接治理：握手与超时这一类细项，出问题时才需要动。</summary>
public sealed class GovernanceOptions
{
    /// <summary>RTSP 允许的传输方式，逗号分隔：tcp / udp / multicast。</summary>
    public string RtspTransports { get; set; } = "tcp,udp";

    /// <summary>WebRTC 握手多久没成算失败，秒。</summary>
    public int WebrtcHandshakeTimeoutSeconds { get; set; } = 10;

    /// <summary>WebRTC 收集轨道多久没结果就放弃，秒。</summary>
    public int WebrtcTrackGatherTimeoutSeconds { get; set; } = 2;
}
