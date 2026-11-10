using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 底部那颗气泡：左边一个图标、右边一句话，整块按语气上色。
/// </summary>
/// <remarks>
/// 继承 <see cref="Border"/> 而不是另起一个模板控件，是因为它本来就只有「一块底 + 一横排内容」，
/// 自己那层 Border 的底色与圆角正好够用，内容在构造时拼一次就再也不变。
///
/// 颜色不写死：按 <see cref="Kind"/> 去资源里取 Toast{语气}Background / Border / Foreground 三支笔刷，
/// 浅色与深色各一套取值，换主题时自己跟着换。取色与失败兜底的做法照抄 PillBadge。
/// </remarks>
public sealed class ToastBar : Border
{
    /// <summary>气泡在说什么，决定底色、边框与图标。</summary>
    public static readonly StyledProperty<ToastKind> KindProperty =
        AvaloniaProperty.Register<ToastBar, ToastKind>(nameof(Kind));

    /// <summary>气泡里的那行字。</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ToastBar, string?>(nameof(Text));

    /// <summary>该不该显示。置 true 播「浮上来 + 渐显」，置 false 播「沉下去 + 渐隐」。</summary>
    public static readonly StyledProperty<bool> IsShownProperty =
        AvaloniaProperty.Register<ToastBar, bool>(nameof(IsShown));

    /// <summary>
    /// 每触发一次气泡就自增。**同一句话连着弹两次时 <see cref="IsShown"/> 一直是 true、
    /// 属性值没变就通知不到**，靠这个计数器强制重播整段出现动画。
    /// </summary>
    public static readonly StyledProperty<int> ShowTokenProperty =
        AvaloniaProperty.Register<ToastBar, int>(nameof(ShowToken));

    /// <summary>气泡从多低的位子浮上来（也沉回这儿）。</summary>
    private const double RiseOffset = 10;

    /// <summary>
    /// 出现与消失各走多久。位移与不透明度共用同一个进度值，天然同步。
    /// </summary>
    private const double MotionMilliseconds = 200;

    private static readonly IBrush FallbackBackground = new SolidColorBrush(Color.Parse("#F1F1F1"));

    private static readonly IBrush FallbackBorder = new SolidColorBrush(Color.Parse("#DDDDDD"));

    private static readonly IBrush FallbackForeground = new SolidColorBrush(Color.Parse("#4A4A4A"));

    private readonly TextBlock _glyph;

    private readonly TextBlock _label;

    /// <summary>动画进度：0 = 藏在下面且全透明，1 = 抬到位且不透明。</summary>
    private double _progress;

    private double _from;

    private double _to;

    private Stopwatch? _clock;

    private bool _running;

    static ToastBar()
    {
        KindProperty.Changed.AddClassHandler<ToastBar>((bar, _) => bar.RefreshBrushes());
        TextProperty.Changed.AddClassHandler<ToastBar>((bar, _) => bar.RefreshText());
        IsShownProperty.Changed.AddClassHandler<ToastBar>((bar, e) => bar.OnIsShownChanged(e));
        ShowTokenProperty.Changed.AddClassHandler<ToastBar>((bar, _) => bar.OnShowTokenChanged());
    }

    public ToastBar()
    {
        _glyph = new TextBlock
        {
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            // 图标字形有的墨迹会探出 em 框，留一点内边距免得被削平
            Padding = new Thickness(1, 0),
        };

        _label = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _glyph, _label },
        };

        Child = row;

        SetOffset(RiseOffset);
        Opacity = 0;
    }

    public ToastKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsShown
    {
        get => GetValue(IsShownProperty);
        set => SetValue(IsShownProperty, value);
    }

    public int ShowToken
    {
        get => GetValue(ShowTokenProperty);
        set => SetValue(ShowTokenProperty, value);
    }

    /// <summary>光有字形没有字的图标在无障碍树里是个空白块，给个兜底名字。</summary>
    private static string GlyphOf(ToastKind kind)
        => kind switch
        {
            ToastKind.Success => "\uE73E",
            ToastKind.Warn => "\uE7BA",
            ToastKind.Error => "\uEA39",
            _ => "\uE946",
        };

    private static string NameOf(ToastKind kind)
        => kind switch
        {
            ToastKind.Success => "Success",
            ToastKind.Warn => "Warn",
            ToastKind.Error => "Error",
            _ => "Info",
        };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        RefreshBrushes();
        RefreshText();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
        => RefreshBrushes();

    private void OnIsShownChanged(AvaloniaPropertyChangedEventArgs e)
        => Play(e.NewValue is true ? 1 : 0);

    /// <summary>再来一条：不管是新话还是同一句话，都从「藏在下面」重新浮一次。</summary>
    private void OnShowTokenChanged()
    {
        if (!IsShown) return;

        Play(1);
    }

    /// <summary>
    /// 往 <paramref name="target"/> 播一段，起点是当前进度。
    /// **新气泡永远从 0 起**：<see cref="OnShowTokenChanged"/> 里先 <c>Play</c>，
    /// 那时上一段的进度不管是 0.3 还是 1，都会先被打回起点再往上走，
    /// 所以连点两次时第二条气泡是真从底部浮起来的，不是接着前一条的位置往上一顶。
    /// </summary>
    private void Play(double target)
    {
        // 出现动画一律从头走：掐掉旧气泡时上一段可能刚走到一半，
        // 接着它的进度往上补，看着就像「第二条只浮了一点点」
        _from = target > 0 ? 0 : _progress;
        _to = target;

        if (Math.Abs(_to - _from) < 0.001)
        {
            Apply(_to);

            return;
        }

        _clock = Stopwatch.StartNew();

        if (_running) return;

        _running = true;

        Run();
    }

    private void Run()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            // 还没挂进可视树（窗口重建时会先设一次 IsShown），直接落位，别在这儿等
            Apply(_to);
            _running = false;

            return;
        }

        top.RequestAnimationFrame(_ => Step());
    }

    private void Step()
    {
        double elapsed = _clock?.Elapsed.TotalMilliseconds ?? MotionMilliseconds;
        double t = Math.Min(1, elapsed / MotionMilliseconds);

        // 缓出：起步快、收尾慢。位移与不透明度共用这一个 t，天然同步。
        double eased = 1 - Math.Pow(1 - t, 3);

        Apply(_from + (_to - _from) * eased);

        if (t < 1)
        {
            Run();

            return;
        }

        _running = false;
        _clock = null;
    }

    /// <summary>把进度摊成两条属性：越靠近 1 越不透明、也越靠上。</summary>
    private void Apply(double progress)
    {
        _progress = progress;

        Opacity = progress;
        SetOffset(RiseOffset * (1 - progress));
    }

    private void SetOffset(double dy)
    {
        // 必须先把浮点尾巴抹掉。补间末尾算出来的可能是 1.2E-05 这种极小数，
        // 而 TransformOperations.Parse 不认「1.2E-05px」这个单位写法，会抛
        // 「Invalid unit: E-05px」——那一下发生在渲染循环里，异常冒上去直接把进程带走。
        // 界面表现为气泡还在，几秒后整个程序没了。
        double rounded = Math.Round(dy, 3);

        if (rounded == 0)
        {
            RenderTransform = null;

            return;
        }

        RenderTransform = TransformOperations.Parse(
            "translateY(" + rounded.ToString("0.###", CultureInfo.InvariantCulture) + "px)");
    }

    private void RefreshText()
    {
        _glyph.Text = GlyphOf(Kind);
        _label.Text = Text;
    }

    /// <summary>按语气名拼资源键取三支笔刷；资源还没加载时退回中性灰。</summary>
    private void RefreshBrushes()
    {
        string name = NameOf(Kind);

        Background = Find("Toast" + name + "Background", FallbackBackground);
        BorderBrush = Find("Toast" + name + "Border", FallbackBorder);

        IBrush foreground = Find("Toast" + name + "Foreground", FallbackForeground);

        _glyph.Foreground = foreground;
        _label.Foreground = foreground;
    }

    private IBrush Find(string key, IBrush fallback)
    {
        ThemeVariant theme = ActualThemeVariant;

        if (this.TryGetResource(key, theme, out object? value) && value is IBrush own) return own;

        if (Application.Current is { } app && app.TryGetResource(key, theme, out object? global) && global is IBrush shared) return shared;

        return fallback;
    }
}
