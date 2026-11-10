namespace MediaMTX_Yuki_GUI.Models;

/// <summary>
/// 底部气泡在说什么。四种语气各配一套底色与图标，看颜色就知道是「成了」还是「没成」。
/// </summary>
/// <remarks>
/// 分类的依据是「这句话在告诉用户什么」，跟它挂在哪个功能下无关：
/// 同样是删东西，删成功了是 <see cref="Success"/>，删失败了是 <see cref="Error"/>。
/// 只报了个事实、没有成功失败之分的走 <see cref="Info"/>；
/// 事情办成了但还有下一步要用户处理（比如「重启引擎后生效」）走 <see cref="Warn"/>。
/// </remarks>
public enum ToastKind
{
    /// <summary>办成了。添加、保存、删除、复制、导出这一类。</summary>
    Success,

    /// <summary>只是告诉用户一件事，无所谓成败。发现新版本、已是最新、引擎已停止。</summary>
    Info,

    /// <summary>办成了，但要用户再做一步才真正生效。</summary>
    Warn,

    /// <summary>没办成。启动失败、复制失败、导出失败、打不开目录。</summary>
    Error,
}
