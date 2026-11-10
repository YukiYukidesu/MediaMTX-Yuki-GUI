namespace MediaMTX_Yuki_GUI.Models;

/// <summary>拉流源的默认选项：引擎主动去连平台或摄像头，而不是由推流软件接入。</summary>
public sealed class PullOptions
{
    /// <summary>
    /// 按需拉流：有人看才去连上游，人走光就断开。
    /// 关掉时引擎一启动就去连，适合「一直挂着转流」的场景。
    /// </summary>
    public bool SourceOnDemand { get; set; } = false;

    /// <summary>按需拉流多久没连上算超时，秒。</summary>
    public int StartTimeoutSeconds { get; set; } = 10;

    /// <summary>观众全走光之后再等多久断上游，秒。</summary>
    public int CloseAfterSeconds { get; set; } = 10;

}
