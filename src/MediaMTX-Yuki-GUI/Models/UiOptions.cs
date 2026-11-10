namespace MediaMTX_Yuki_GUI.Models;

/// <summary>界面层的用户偏好。</summary>
public sealed class UiOptions
{
    /// <summary>简洁模式：只显示管转发的那几栏。</summary>
    public const string SimpleMode = "simple";

    /// <summary>高级模式：多出六组接管引擎能力的栏。</summary>
    public const string AdvancedMode = "advanced";

    /// <summary>zh-CN / en-US。</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>system / light / dark，默认跟随系统。</summary>
    public string Theme { get; set; } = "system";

    public bool StartWithWindows { get; set; } = false;

    /// <summary>关闭窗口时收进托盘而不是退出。</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// 界面关掉之后多久回收一次内存：instant（1 秒）/ fast（10 秒）/ default（30 秒）/ off（不回收）。
    /// 回收会把工作集交还系统，代价是下次打开界面要缺页拉回，窗口开得越勤越不划算。
    /// </summary>
    public string MemoryTrim { get; set; } = "default";

    /// <summary>
    /// simple / advanced。只决定设置页多显示哪几栏，不影响引擎行为；
    /// 默认简洁——刚拿到手的用户只需要把流转出去。
    /// </summary>
    public string Mode { get; set; } = SimpleMode;

    /// <summary>
    /// 输入源列表的排序方式：manual（手动）/ name（备注名）/ protocol（协议）/ address（推流地址）。
    /// 手动顺序下只做「已启用排前面」的分组，顺序本身由用户拖出来。
    /// </summary>
    public string InputSort { get; set; } = "manual";

    public bool InputSortDescending { get; set; } = false;

    /// <summary>已启用的条目排在前面；默认开启，拖动只在同一组内生效。</summary>
    public bool InputEnabledFirst { get; set; } = true;

    /// <summary>
    /// 输出目标列表的排序方式：manual（手动）/ name（备注名）/ platform（平台）/ protocol（协议）。
    /// 顺序即转发顺序——引擎按这个顺序排 forward 列表，改动不会断流。
    /// </summary>
    public string TargetSort { get; set; } = "manual";

    public bool TargetSortDescending { get; set; } = false;

    public bool TargetEnabledFirst { get; set; } = true;

    /// <summary>
    /// 硬件加速：开时渲染后端按 AngleEgl 走 GPU，关时退回纯软件光栅。
    /// 默认开——这台机器上软件渲染在 330Hz 屏上肉眼可见地顿。
    /// 改它要重启程序才生效（渲染后端只在启动时读一次）。
    /// </summary>
    public bool HardwareAcceleration { get; set; } = true;

    /// <summary>
    /// 窗口背景材质：mica（云母）/ acrylic（亚克力）/ solid（纯色）。
    /// 只有开硬件加速时才提得动这些效果；系统不支持时自动退回纯色。
    /// </summary>
    public string WindowBackdrop { get; set; } = "mica";

    /// <summary>
    /// 日志保留天数：0 表示不清理，其余是保留最近几天。
    /// 只清 logs 目录里的文件；界面日志页那份滚动记录不受影响。
    /// </summary>
    public int LogRetentionDays { get; set; } = 7;
}
