namespace MediaMTX_Yuki_GUI.Models;

/// <summary>
/// 总览页右下角那张提示卡在说什么。
/// 三种情况共用同一张卡：一屏只提一件事，所以按要紧程度排优先级，谁更急说谁。
/// </summary>
public enum TipKind
{
    /// <summary>没什么要提的，卡收起来。</summary>
    None,

    /// <summary>引擎没装，转流根本跑不起来。</summary>
    EngineMissing,

    /// <summary>引擎装着，但远端有新版本。</summary>
    EngineUpdate,

    /// <summary>本程序自己有新版本。</summary>
    AppUpdate,
}
