namespace MediaMTX_Yuki_GUI.Models;

/// <summary>路径名的兜底规则，写进引擎的 pathDefaults 与 paths.all_others。</summary>
public sealed class PathOptions
{
    /// <summary>
    /// 兜底路径：打开后没在输入源里登记过的路径名也能推上来，引擎照收但不转发。
    /// 关着时只认输入源里列出的那几条。
    /// </summary>
    public bool AllOthers { get; set; } = false;

    /// <summary>
    /// 绝对时间戳：把上游送来的时间戳原样透传，不在起点归零。
    /// 多路画面要对齐时间轴时才需要，单路转流开着没意义。
    /// </summary>
    public bool UseAbsoluteTimestamp { get; set; } = false;
}
