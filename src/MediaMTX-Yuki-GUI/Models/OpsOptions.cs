namespace MediaMTX_Yuki_GUI.Models;

/// <summary>运维接口。API 与 metrics 的端口在 <see cref="EngineOptions"/> 里，这里只放可选的。</summary>
public sealed class OpsOptions
{
    /// <summary>pprof 性能剖析服务，排查卡顿时才需要开。</summary>
    public bool Pprof { get; set; }

    public int PprofPort { get; set; } = 9999;

    /// <summary>回放服务，能按时间区间回看已经录下的内容。没开录制时开着也没用。</summary>
    public bool Playback { get; set; }

    public int PlaybackPort { get; set; } = 9996;
}
