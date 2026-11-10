using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>胶囊标签的语义变体。名字与 Resources\PillPalette.axaml 的键一一对应。</summary>
public enum PillVariant
{
    /// <summary>中性灰白。备注名、地址、数字这类没有语义的值用它。</summary>
    Plain,

    /// <summary>启用、在线、推流中。</summary>
    On,

    /// <summary>停用、未运行。</summary>
    Off,

    /// <summary>需要注意、还没出错。</summary>
    Warn,

    /// <summary>出错，以及会断流的那类状态。</summary>
    Error,

    /// <summary>RTMP 推流协议。</summary>
    Rtmp,

    /// <summary>RTMPS 推流协议。</summary>
    Rtmps,

    /// <summary>RTSP 推流协议。</summary>
    Rtsp,

    /// <summary>SRT 推流协议。</summary>
    Srt,

    /// <summary>跟随设置页的默认推流代理。</summary>
    Follow,

    /// <summary>这一条目标单独指定了代理地址。</summary>
    Custom,
}

/// <summary>字段值的胶囊外壳：一行短文字，配一层语义底色。</summary>
/// <remarks>
/// 颜色不写死在模板里：按 <see cref="Variant"/> 与当前主题变体去资源里取
/// Pill{变体}Foreground 与 Pill{变体}Background 这一对笔刷，浅色与深色各一套取值，
/// 换主题时颜色自己跟着换。色板取值与实测对比度见 Resources\PillPalette.axaml 与
/// docs\UI参考-设计规范.md 第四节。
///
/// 默认靠左不撑满：胶囊的宽度应当由内容决定，撑满整列会把「短值」画成一条长条。
/// 需要居中时在使用处覆盖 HorizontalAlignment。
/// </remarks>
public sealed class PillBadge : TemplatedControl
{
    /// <summary>胶囊里的文字。</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<PillBadge, string?>(nameof(Text));

    /// <summary>语义变体，决定底色与文字色。</summary>
    public static readonly StyledProperty<PillVariant> VariantProperty =
        AvaloniaProperty.Register<PillBadge, PillVariant>(nameof(Variant));

    /// <summary>左边要不要带一个小圆点。状态类用得上，纯文字的值不用。</summary>
    public static readonly StyledProperty<bool> ShowDotProperty =
        AvaloniaProperty.Register<PillBadge, bool>(nameof(ShowDot));

    /// <summary>由 <see cref="Variant"/> 与主题算出来的文字色，模板只负责绑。</summary>
    public static readonly StyledProperty<IBrush?> VariantForegroundProperty =
        AvaloniaProperty.Register<PillBadge, IBrush?>(nameof(VariantForeground));

    /// <summary>由 <see cref="Variant"/> 与主题算出来的底色。</summary>
    public static readonly StyledProperty<IBrush?> VariantBackgroundProperty =
        AvaloniaProperty.Register<PillBadge, IBrush?>(nameof(VariantBackground));

    /// <summary>点一下要执行的命令。留空时这颗胶囊就是个纯展示标签。</summary>
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<PillBadge, ICommand?>(nameof(Command));

    /// <summary>传给 <see cref="Command"/> 的参数。</summary>
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<PillBadge, object?>(nameof(CommandParameter));

    private static readonly IBrush FallbackForeground = new SolidColorBrush(Color.Parse("#4A4A4A"));

    private static readonly IBrush FallbackBackground = new SolidColorBrush(Color.Parse("#F1F1F1"));

    /// <summary>能点的胶囊共用一只手型光标，别每次按下都新建一个。</summary>
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    static PillBadge()
    {
        VariantProperty.Changed.AddClassHandler<PillBadge>((badge, _) => badge.RefreshBrushes());
        CommandProperty.Changed.AddClassHandler<PillBadge>((badge, _) => badge.RefreshCursor());
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public PillVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public bool ShowDot
    {
        get => GetValue(ShowDotProperty);
        set => SetValue(ShowDotProperty, value);
    }

    public IBrush? VariantForeground
    {
        get => GetValue(VariantForegroundProperty);
        set => SetValue(VariantForegroundProperty, value);
    }

    public IBrush? VariantBackground
    {
        get => GetValue(VariantBackgroundProperty);
        set => SetValue(VariantBackgroundProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    // ====== 点击 ======

    /// <summary>
    /// 点一下就执行命令。没有包一层 Button，是为了不打断列表行的拖拽排序：
    /// 重排只在按到 .drag-handle 握把时才动手（见 <see cref="ReorderHandle"/>），
    /// 胶囊只吃掉自己这一次按下，别的照旧。
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Command is null) return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        e.Handled = true;

        if (Command.CanExecute(CommandParameter)) Command.Execute(CommandParameter);
    }

    /// <summary>有命令时换成手型光标，让人一眼看出这颗胶囊能点。</summary>
    private void RefreshCursor()
        => Cursor = Command is null ? null : HandCursor;

    // ====== 取色 ======

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        RefreshBrushes();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
        => RefreshBrushes();

    /// <summary>按变体名拼资源键取一对笔刷；资源还没加载时退回中性灰白。</summary>
    private void RefreshBrushes()
    {
        string name = Variant.ToString();

        VariantForeground = Find("Pill" + name + "Foreground", FallbackForeground);
        VariantBackground = Find("Pill" + name + "Background", FallbackBackground);
    }

    private IBrush Find(string key, IBrush fallback)
    {
        ThemeVariant theme = ActualThemeVariant;

        if (this.TryGetResource(key, theme, out object? value) && value is IBrush own) return own;

        if (Application.Current is { } app && app.TryGetResource(key, theme, out object? global) && global is IBrush shared) return shared;

        return fallback;
    }
}
