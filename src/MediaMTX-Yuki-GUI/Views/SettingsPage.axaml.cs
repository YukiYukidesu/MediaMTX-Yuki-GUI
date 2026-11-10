using System;
using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using MediaMTX_Yuki_GUI.ViewModels;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>设置页：顶部搜索 + 一列可折叠的分组。</summary>
public partial class SettingsPage : UserControl
{
    /// <summary>折叠组展开是 280 毫秒的动画，滚动要等它走完，否则会算到旧高度上。</summary>
    private const int ExpandSettleMilliseconds = 400;

    /// <summary>目标还没进布局时补一次的间隔。</summary>
    private const int RetryMilliseconds = 200;

    /// <summary>滚动动画的时长，与折叠组的展开动画对齐，两段动画一起收尾。</summary>
    private const int ScrollDurationMilliseconds = 280;

    /// <summary>动画的帧间隔，取 60 帧一跳的 16 毫秒。</summary>
    private const int ScrollFrameMilliseconds = 16;

    private MainViewModel? _viewModel;
    private DispatcherTimer? _scrollTimer;
    private double _scrollFrom;
    private double _scrollTo;
    private DateTime _scrollStartedAt;

    public SettingsPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 订阅挂在 DataContext 变化上，不挂 OnAttachedToVisualTree：
    /// 页面是先挂进可视树、后由主窗把 DataContext 传下来的，挂在挂载事件里会拿到空值直接返回。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        StopScroll();

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// 右下角提示卡点「前往下载」时 VM 会把 DownloadFocusTick 加一，这里接住它，
    /// 等网络与更新那一组展开完，再把它的标题滚到最上面。
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.DownloadFocusTick))
        {
            return;
        }

        ScrollToNetworkGroup(ExpandSettleMilliseconds, allowRetry: true);
    }

    private void ScrollToNetworkGroup(int delayMilliseconds, bool allowRetry)
    {
        DispatcherTimer.RunOnce(() => FocusNetworkGroup(allowRetry), TimeSpan.FromMilliseconds(delayMilliseconds));
    }

    /// <summary>
    /// 把「网络与更新」这一组的标题滚到视口最上面。
    /// 不用 BringIntoView：这页外层是 ScrollViewer、里层是折叠组，那套算出来的偏移在这页上不动，
    /// 干脆自己算标题相对视口的位移，再把 Offset 送进动画。
    /// 控件用 FindControl 现取，不依赖 x:Name 的生成字段。
    /// </summary>
    private void FocusNetworkGroup(bool allowRetry)
    {
        ScrollViewer? scroller = this.FindControl<ScrollViewer>("SettingsScroller");
        Control? group = this.FindControl<Control>("NetworkGroup");
        if (scroller is null || group is null)
        {
            return;
        }

        Point? point = group.TranslatePoint(new Point(0, 0), scroller);
        if (point is null)
        {
            if (allowRetry)
            {
                ScrollToNetworkGroup(RetryMilliseconds, allowRetry: false);
            }

            return;
        }

        AnimateScroll(scroller, scroller.Offset.Y + point.Value.Y);
    }

    /// <summary>
    /// 逐帧推 Offset 做缓动。ScrollViewer 自己没有平滑滚动，直接设 Offset 是瞬移，
    /// 跨越大半页时会让人找不到落点。进度按时间算而不是按帧数算，掉帧也不会拖长动画。
    /// </summary>
    private void AnimateScroll(ScrollViewer scroller, double target)
    {
        StopScroll();

        _scrollFrom = scroller.Offset.Y;
        _scrollTo = ClampOffset(scroller, target);

        if (Math.Abs(_scrollTo - _scrollFrom) < 0.5)
        {
            scroller.Offset = new Vector(0, _scrollTo);
            return;
        }

        _scrollStartedAt = DateTime.UtcNow;
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ScrollFrameMilliseconds) };
        _scrollTimer.Tick += (_, _) => StepScroll(scroller);
        _scrollTimer.Start();
    }

    private void StepScroll(ScrollViewer scroller)
    {
        double progress = (DateTime.UtcNow - _scrollStartedAt).TotalMilliseconds / ScrollDurationMilliseconds;
        if (progress >= 1)
        {
            scroller.Offset = new Vector(0, ClampOffset(scroller, _scrollTo));
            StopScroll();
            return;
        }

        // 三次缓出：起步快、收尾慢，和折叠组的动画手感一致
        double eased = 1 - Math.Pow(1 - progress, 3);
        double value = _scrollFrom + ((_scrollTo - _scrollFrom) * eased);
        scroller.Offset = new Vector(0, ClampOffset(scroller, value));
    }

    /// <summary>动画途中组还在展开，可滚范围每帧都在变，所以每帧都要重新夹一次。</summary>
    private static double ClampOffset(ScrollViewer scroller, double value)
    {
        double maximum = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        return Math.Clamp(value, 0, maximum);
    }

    private void StopScroll()
    {
        if (_scrollTimer is null)
        {
            return;
        }

        _scrollTimer.Stop();
        _scrollTimer = null;
    }
}
