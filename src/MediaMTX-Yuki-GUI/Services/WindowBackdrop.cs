using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 窗口背景材质的共用部分：主窗与所有弹窗都从这里拿同一套判定，
/// 免得主窗是云母、弹出来的小窗却是纯白，看着像两个不同的程序。
/// </summary>
/// <remarks>
/// 材质只跟两件事有关：<see cref="Window.TransparencyLevelHint"/> 是我们想要哪一种，
/// <see cref="Window.ActualTransparencyLevel"/> 是系统最后给了哪一种。
/// 系统不给（Win10、或者辅助功能里关掉了透明效果）时会回到
/// <see cref="WindowTransparencyLevel.None"/>，这时必须自己铺一层纯色底——
/// 全透明又没有模糊，字就糊在壁纸上了。
/// </remarks>
public static class WindowBackdrop
{
    public const string Mica = "mica";

    public const string Acrylic = "acrylic";

    public const string Solid = "solid";

    /// <summary>当前选中的档位。弹窗是临时开的，直接读这里，不各自再去拉一次配置。</summary>
    public static string Mode { get; private set; } = Mica;

    /// <summary>按档位给系统递偏好列表：第一个要不到就问第二个，最后一定要兜住纯色。</summary>
    public static IReadOnlyList<WindowTransparencyLevel> HintOf(string? mode) => Normalize(mode) switch
    {
        Acrylic => [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],

        // 纯色档就是什么都不要，系统会把 ActualTransparencyLevel 报成 None
        Solid => [WindowTransparencyLevel.None],
        _ => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None],
    };

    /// <summary>系统实际给了材质没有。给了就让窗口透，没给就得自己铺底。</summary>
    public static bool IsTranslucent(Window window)
        => window.ActualTransparencyLevel != WindowTransparencyLevel.None;

    /// <summary>给一个窗口按当前档位要材质，并在一帧之后把底色摆正。</summary>
    /// <param name="window">要上材质的窗口。</param>
    /// <param name="mode">mica / acrylic / solid。</param>
    /// <param name="afterSync">底色摆正之后还要做什么（主窗拿它换侧栏笔刷）。</param>
    /// <remarks>
    /// 必须延后一帧：设完 <see cref="Window.TransparencyLevelHint"/> 当场读
    /// <see cref="Window.ActualTransparencyLevel"/> 还是上一次的值。
    /// </remarks>
    public static void Apply(Window window, string? mode, Action? afterSync = null)
    {
        Mode = Normalize(mode);
        window.TransparencyLevelHint = HintOf(Mode);

        Dispatcher.UIThread.Post(
            () =>
            {
                Sync(window);
                afterSync?.Invoke();
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 弹窗用的一句话版：要材质、真正开窗之后再要一次、主题变了再同步一次。
    /// </summary>
    /// <remarks>
    /// 构造里那次调用发生时窗口还没 Show，DWM 还没给它算实际材质，
    /// 所以 <see cref="Window.Opened"/> 里必须再来一遍，不然永远是纯色。
    /// </remarks>
    public static void Attach(Window window)
    {
        Apply(window, Mode);
        window.Opened += (_, _) => Apply(window, Mode);
        window.ActualThemeVariantChanged += (_, _) => Sync(window);
    }

    /// <summary>按「实际拿到了什么」把窗口底色摆正。主题一变也要重来一次。</summary>
    public static void Sync(Window window)
        => window.Background = IsTranslucent(window) ? Brushes.Transparent : SolidBrush(window);

    /// <summary>纯色兜底用的底色，跟着主题走（深色主题下是深灰，不是白）。</summary>
    public static IBrush? SolidBrush(Window window)
        => window.TryFindResource("SolidBackgroundFillColorBaseBrush", window.ActualThemeVariant, out object? value)
            ? value as IBrush
            : null;

    /// <summary>认不出来的档位一律按云母算，与配置默认值一致。</summary>
    private static string Normalize(string? mode) => mode switch
    {
        Acrylic => Acrylic,
        Solid => Solid,
        _ => Mica,
    };
}
