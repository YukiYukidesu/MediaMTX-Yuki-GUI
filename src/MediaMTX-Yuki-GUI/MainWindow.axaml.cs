using System.ComponentModel;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;
using MediaMTX_Yuki_GUI.Services;
using MediaMTX_Yuki_GUI.ViewModels;

namespace MediaMTX_Yuki_GUI;

/// <summary>
/// 主窗口。Fluent 标题栏由 FAAppWindow 提供，WPF 那边的 ui:FluentWindow 已不存在。
/// 窗口是次抛的：收进托盘时它真的会被关掉（界面对象交给 GC），
/// 从托盘叫回来时由 <see cref="AppShell"/> 重建一个，接的是同一个 MainViewModel。
/// </summary>
public partial class MainWindow : FAAppWindow, IAppHost
{
    private readonly MainViewModel _viewModel;

    /// <summary>true 表示这次关闭是「收进托盘」，不是退出。</summary>
    private bool _toTray;

    /// <summary>true 表示这次关闭是真退出（托盘菜单点的）。</summary>
    private bool _isExiting;

    /// <summary>无参构造给设计器用；正常运行时由 AppShell 传同一个 ViewModel 进来。</summary>
    public MainWindow() : this(AppShell.ViewModel)
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;

        // 窗口会被重建，界面能力得重新挂到这一扇上
        _viewModel.AppHost = this;

        DataContext = _viewModel;

        // 选中项一变就挪滑块。事件挂在整个侧栏上冒泡收，不逐项 hook——
        // 项数是会变的（简洁模式少一个账号），逐个订阅就得跟着维护那张名单，漏一个就有一项不亮。
        SidebarRoot.AddHandler(ToggleButton.IsCheckedChangedEvent, OnNavCheckedChanged, RoutingStrategies.Bubble);

        // 窗口尺寸、模式切换都会让导航项挪位置（简洁模式少一项，下面的项整体上移），滑块得跟着重算
        SidebarRoot.SizeChanged += (_, _) => MoveIndicator();

        // 提示卡是常驻的，露不露靠 VM 说；窗口会被重建，所以订阅挂在每一次的窗口上
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // FluentAvalonia 把客户区扩展到了标题栏，却不接管拖动：Win32 那边 WM_NCHITTEST
        // 在标题栏上返回 HTCLIENT，系统不认为这是能拖的地方。补 ExtendClientAreaTitleBarHeightHint
        // 也没用（FAAppWindow 自己改了装饰策略），只能在托管层自己接一下。
        AddHandler(PointerPressedEvent, OnTitleBarPressed, RoutingStrategies.Tunnel);

        // 材质开着的时候，主题一换侧栏那层薄纱的颜色就得跟着换（刷子是代码赋的，不会自己跟）
        ActualThemeVariantChanged += (_, _) => SyncBackdrop();

        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;

        // 日志滚到底由 Views/LogsPage 自己管，窗口这边不掺和
    }

    // ====== IAppHost ======

    public async Task<bool> ShowInputEditorAsync(
        InputItemViewModel item,
        bool isNew,
        IReadOnlyCollection<int> ownPorts,
        IReadOnlyCollection<string> otherNames,
        IReadOnlyCollection<string> otherPaths,
        bool advancedMode,
        IReadOnlyList<AuthAccountItemViewModel> accounts)
    {
        Views.InputEditorWindow window = new(item, isNew, ownPorts, otherNames, otherPaths, advancedMode, accounts);

        bool? result = await window.ShowDialog<bool?>(this);

        return result == true;
    }

    public async Task<bool> ShowTargetEditorAsync(
        TargetItemViewModel item,
        bool isNew,
        IReadOnlyList<InputChoice> choices,
        IReadOnlyCollection<string> otherNames)
    {
        Views.TargetEditorWindow window = new(item, isNew, choices, otherNames);

        bool? result = await window.ShowDialog<bool?>(this);

        return result == true;
    }

    public async Task<bool> ShowAccountEditorAsync(
        AuthAccountItemViewModel item,
        bool isNew,
        IReadOnlyCollection<string> otherUsers)
    {
        Views.AccountEditorWindow window = new(item, isNew, otherUsers);

        bool? result = await window.ShowDialog<bool?>(this);

        return result == true;
    }

    public async Task ShowRecordingsAsync(
        string pathName,
        string displayName,
        string inputId,
        string address,
        Func<CancellationToken, Task<IReadOnlyList<RecordingSegmentInfo>?>> loader,
        Func<string, string, CancellationToken, Task<bool>> deleter)
    {
        Views.RecordingsWindow window = new(pathName, displayName, inputId, address, loader, deleter);

        await window.ShowDialog(this);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string okText, bool destructive = false)
    {
        Views.ConfirmWindow window = new(title, message, okText, destructive);

        bool? result = await window.ShowDialog<bool?>(this);

        return result == true;
    }

    // ====== 窗口材质 ======

    /// <summary>
    /// 切窗口背景材质。系统不给这个效果时一律退回纯色——全透明又没有模糊会直接看不见字。
    /// </summary>
    /// <remarks>
    /// 只让「底板」与侧栏透：右侧那些卡片本身就是不透明的，
    /// 再让它们透会变成一层层糊在一起，读不清。云母在卡片间隙里露出来就够像样了。
    /// </remarks>
    public void ApplyBackdrop(string? mode)
        => Services.WindowBackdrop.Apply(this, mode, SyncBackdropLayers);

    /// <summary>按「实际拿到了什么材质」决定底板露不露，窗口跟主题一变就重算。</summary>
    private void SyncBackdrop()
    {
        // 窗口自己那一层也得跟着换。纯色档没有 DWM 背景兜着，窗口又是 Transparent，
        // 标题栏那块（在底板 Grid 之外、由 FA 模板自己画）就会透出去变成一条黑；
        // Mica 档之所以没事，是因为后面有 DWM 画的云母在垫着。
        Services.WindowBackdrop.Sync(this);

        SyncBackdropLayers();
    }

    private void SyncBackdropLayers()
    {
        bool translucent = Services.WindowBackdrop.IsTranslucent(this);

        if (BackdropLayer is not null)
        {
            BackdropLayer.Background = translucent
                ? Brushes.Transparent
                : Resource("SolidBackgroundFillColorBaseBrush");
        }

        if (Sidebar is not null)
        {
            Sidebar.Background = translucent
                ? Resource("DsSidebarVeilBrush")
                : Resource("LayerFillColorAltBrush");
        }
    }

    private IBrush? Resource(string key)
        => this.TryFindResource(key, ActualThemeVariant, out object? value) ? value as IBrush : null;

    public async Task CopyTextAsync(string text)
    {
        IClipboard? clipboard = Clipboard;

        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
    }

    /// <summary>
    /// 弹「另存为」把一段文字写到用户挑的位置。日志页的「导出」走这里。
    /// 没有可用的窗口 / 用户取消 / 写盘失败都返回 false，界面那边据此提示。
    /// </summary>
    public async Task<bool> SaveTextFileAsync(string suggestedName, string content)
    {
        IStorageProvider? storage = StorageProvider;

        if (storage is null) return false;

        try
        {
            IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存文本",
                SuggestedFileName = suggestedName,
                DefaultExtension = "log",
                ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    new FilePickerFileType("日志文件") { Patterns = ["*.log", "*.txt"] },
                ],
            });

            if (file is null) return false;

            await using Stream stream = await file.OpenWriteAsync();

            stream.SetLength(0);

            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

            await writer.WriteAsync(content);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>引擎日志来自后台线程，必须切回 UI 线程再动集合。</summary>
    public void Post(Action action)
        => Dispatcher.UIThread.Post(action);

    // ====== 生命周期 ======

    private void OnOpened(object? sender, EventArgs e)
    {
        // 首帧布局还没跑完，等一轮再量位置。
        // 窗口重建后尺寸可能不同，所以这一步每次都要做。
        Dispatcher.UIThread.Post(MoveIndicator, DispatcherPriority.Loaded);

        // 窗口是新建的，卡片这会儿还停在 XAML 里那个「藏起来」的样子，先摆到当前该处的状态。
        // 不走过渡：刚打开就看见它自己飘出来一次很怪。
        ApplyTipState(_viewModel.IsTipShown, animate: false);

        // 窗口是次抛的，材质每次重建都得重新要一次
        ApplyBackdrop(_viewModel.WindowBackdrop);
    }

    /// <summary>
    /// 点 × 时先看要不要收进托盘。系统关机（OSShutdown）必须放行，否则会把 Windows 的关机和注销一起挡住。
    /// 收进托盘不再拿 e.Cancel 拦下来：这里只是记账，窗口照常关掉，界面对象才能被回收。
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExiting) return;
        if (e.CloseReason == WindowCloseReason.OSShutdown) return;
        if (!_viewModel.CloseToTray) return;

        // 托盘图标没建出来就不能收——窗口一关就再也没东西能把程序叫回来
        if (!AppShell.CanHideToTray) return;

        _toTray = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        // 窗口要没了，界面能力先脱钩，免得 VM 还攥着一个死窗口
        if (ReferenceEquals(_viewModel.AppHost, this)) _viewModel.AppHost = null;

        AppShell.OnWindowClosed(this, _toTray, _isExiting);
    }

    /// <summary>托盘菜单「退出程序」走的路径：这次关闭不当收托盘处理。</summary>
    public void CloseForExit()
    {
        _isExiting = true;

        Close();
    }

    // ====== 右下角提示卡 ======

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 模式一换，侧栏「账号」那一项会显/隐，下面的项整体上移或下移——
        // SidebarRoot 自己的高度没变，SizeChanged 不会来，只能在这儿主动补一次
        if (e.PropertyName == nameof(MainViewModel.IsAdvancedMode))
        {
            Dispatcher.UIThread.Post(MoveIndicator, DispatcherPriority.Loaded);

            return;
        }

        // 只关心这一组：内容换了要重画文字，IsTipShown 换了要收放
        if (e.PropertyName is { } name
            && name != nameof(MainViewModel.IsTipShown)
            && name != nameof(MainViewModel.TipTitle)
            && name != nameof(MainViewModel.TipMessage)
            && name != nameof(MainViewModel.TipActionText)
            && name != nameof(MainViewModel.TipGlyph))
        {
            return;
        }

        ApplyTipState(_viewModel.IsTipShown);
    }

    /// <summary>
    /// 把卡片推到该处的样子。显隐不用 IsVisible：元素一隐藏就没机会补间了，
    /// 改成一直是可见的，用透明度和 12px 的位移装出来。
    /// </summary>
    /// <param name="shown">true 表示现在该露出来。</param>
    /// <param name="animate">false 表示直接落位，不播过渡（窗口刚建好时用）。</param>
    private void ApplyTipState(bool shown, bool animate = true)
    {
        if (TipCard is null)
        {
            return;
        }

        if (animate)
        {
            Place();

            return;
        }

        // 临时摘掉过渡，设完再挂回去：留着的话这一次赋值也会被补间
        Transitions? transitions = TipCard.Transitions;

        TipCard.Transitions = null;

        Place();

        TipCard.Transitions = transitions;

        void Place()
        {
            TipCard.Opacity = shown ? 1d : 0d;

            TipCard.RenderTransform = TransformOperations.Parse(shown ? "translateY(0px)" : "translateY(12px)");
        }
    }

    // ====== 标题栏拖动 ======

    /// <summary>
    /// 左上那一小块是 FAAppWindow 自绘的标题栏，系统和它自己都不管拖动，只能在这里补。
    /// 判据是「按在窗口最顶上那一条、且没按在任何按钮上」——窗内坐标 Y 小于标题栏高度就算标题栏。
    /// </summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        double barHeight = TemplateSettings?.TitleBarHeight ?? 0d;

        if (barHeight <= 0d || e.GetPosition(this).Y > barHeight)
        {
            return;
        }

        // 标题栏里除了空白还有最小化 / 最大化 / 关闭三颗按钮，按在它们身上必须放行
        if (e.Source is Visual source && HasButtonAncestor(source))
        {
            return;
        }

        // 双击标题栏 = 最大化 / 还原，这是 Windows 的老习惯，顺手补上
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

            return;
        }

        BeginMoveDrag(e);
    }

    private static bool HasButtonAncestor(Visual source)
    {
        for (Visual? v = source; v is not null; v = v.GetVisualParent())
        {
            if (v is Button)
            {
                return true;
            }
        }

        return false;
    }

    // ====== 侧栏滑块 ======

    /// <summary>侧栏里某一项被选中了，等布局跑完再挪滑块。</summary>
    /// <remarks>
    /// 不能当场量：刚切换的那一帧 Bounds 还是旧的，算出来会停在上一项。
    /// </remarks>
    private void OnNavCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (e.Source is RadioButton { IsChecked: true })
        {
            Dispatcher.UIThread.Post(MoveIndicator, DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// 把蓝色滑块挪到当前选中项左侧的垂直中线。
    /// 位置靠 TranslatePoint 相对 SidebarRoot 算，不依赖任何硬编码高度。
    /// </summary>
    /// <remarks>
    /// 每次现扫一遍可视树，而不是记住一个固定名单：导航项的数量随模式变（简洁模式少「账号」一项），
    /// 名单一硬编码就必然漏——漏掉的那一项点下去滑块不动，看着就是「滑块有 bug」。
    /// 八个项、只在切换时扫，代价可以忽略。
    /// </remarks>
    private void MoveIndicator()
    {
        RadioButton? current = SidebarRoot
            .GetVisualDescendants()
            .OfType<RadioButton>()
            .FirstOrDefault(item => item.IsChecked == true && item.IsEffectivelyVisible);

        if (current is null || NavIndicator is null)
        {
            NavIndicator?.SetValue(IsVisibleProperty, false);

            return;
        }

        NavIndicator.SetValue(IsVisibleProperty, true);

        Point? offset = current.TranslatePoint(new Point(0, 0), SidebarRoot);

        if (offset is null)
        {
            return;
        }

        double y = offset.Value.Y + ((current.Bounds.Height - NavIndicator.Height) / 2);

        NavIndicator.RenderTransform = TransformOperations.Parse($"translateY({y:0.##}px)");
    }

    // 页面切换不在这里做：侧栏 RadioButton 的 IsChecked 双向绑 VM 的 SelectedPage，
    // 五个页面的 IsVisible 也各自绑 VM 的 IsXxxPageVisible，和原项目一个思路。
}
