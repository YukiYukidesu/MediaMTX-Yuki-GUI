namespace MediaMTX_Yuki_GUI.Models;

/// <summary>引擎的运行参数。端口变了要同时改 runtime.yml 与探活地址。</summary>
public sealed class EngineOptions
{
    public int  RtmpPort    { get; set; } = 1935;
    public int  ApiPort     { get; set; } = 9997;
    public int  MetricsPort { get; set; } = 9998;

    /// <summary>主页展示的推流入口协议：rtmp / srt / rtsp。</summary>
    public string Ingest { get; set; } = "rtmp";

    /// <summary>随本程序启动引擎。</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>引擎意外退出时自动拉起。</summary>
    public bool AutoRestart { get; set; } = true;
}
