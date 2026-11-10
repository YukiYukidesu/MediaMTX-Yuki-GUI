namespace MediaMTX_Yuki_GUI.Models;

/// <summary>
/// 调试开关：只给开发和临时验证用，界面上一处都不显示。
///
/// 分界线很简单——**用户能在界面上改到的东西，不许放这里**。自动起引擎、内存回收档位、
/// 界面语言、模式开关这些都有正经入口，写进这段等于给它们开了第二个真相源。
///
/// 这段存在的理由是一批「只在我们自己验东西时想改、用户永远不该看到」的项：
/// 拿替身引擎换掉真引擎、临时改引擎启动参数、同时开两个实例看互相踩不踩、别清解压缓存。
/// 这些以前靠改 `bin` 下的文件或临时注释代码实现，测完常常忘了收回去。
///
/// 改完要重启程序才生效（这两个开关在配置被读出来之前就得用）。
/// </summary>
public sealed class DebugOptions
{
    /// <summary>
    /// 引擎可执行文件的替代路径。空串表示用正常的 `MediaMTX\mediamtx.exe`。
    /// 相对路径相对程序根目录。指到别处时，引擎的工作目录也跟着换成那个文件所在目录，
    /// 这样替身引擎的日志、相对路径录制目录都落在它自己旁边，不会污染真实的 MediaMTX\。
    /// </summary>
    public string EngineExe { get; set; } = "";

    /// <summary>
    /// 引擎启动参数。空串表示用正常的「runtime.yml 绝对路径」。
    /// **只在 <see cref="EngineExe"/> 非空时生效**——拿它替换正式引擎的参数会绕开 runtime.yml，
    /// 那等于悄悄改了产品行为，不该发生。
    /// </summary>
    public string EngineArgs { get; set; } = "";

    /// <summary>
    /// 允许同时开多个实例。默认关。
    /// 开着的代价很直接：两个实例会抢同一个控制口（9997）和同一份 runtime.yml，
    /// 各自看着都在跑，实际互相踩。只在验证「单实例守卫本身」时才该打开。
    /// </summary>
    public bool AllowMultipleInstances { get; set; } = false;

    /// <summary>
    /// 跳过单文件解压缓存的清理。默认关（即正常清理）。
    /// 排查「新版 exe 的 native 库到底解到哪儿去了」时需要留着旧目录对照。
    /// </summary>
    public bool SkipBundleCacheClean { get; set; } = false;
}
