using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using MediaMTX_Yuki_GUI.ViewModels;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 应用级外壳：活得和进程一样久的那些东西都挂在这儿——主 ViewModel、托盘图标、空闲回收。
/// 主窗口反而是次抛的：关掉就真关，界面对象交给 GC；从托盘叫回来时重建一个，接回同一个 ViewModel。
/// 这么绕一圈是为了让那几十兆界面内存真能还回去（做法照 Filee 抄的）。
/// </summary>
public static class AppShell
{
    private static readonly object Gate = new();

    private static IClassicDesktopStyleApplicationLifetime? _desktop;
    private static MainViewModel? _viewModel;
    private static MemoryTrimmer? _trimmer;
    private static TrayIcon? _tray;
    private static MainWindow? _window;
    private static bool _started;
    private static bool _shuttingDown;

    /// <summary>
    /// 启动时是否直接躺进托盘（命令行带 <c>--tray</c>，开机自启就是这么登记的）。
    /// 设成 true 时 <see cref="Initialize"/> 不建主窗口，但引擎照常起来——
    /// 用户从托盘图标点一下就能把窗口叫出来。
    /// </summary>
    public static bool StartInTray { get; set; }

    /// <summary>
    /// 主窗口开在副屏上（命令行带 <c>--on-secondary</c>）。
    /// 只给自动化测试用：截图与合成点击会把窗口拉到前台，
    /// 它要是压在主屏上，人就没法一边看一边干别的了。
    /// </summary>
    public static bool OpenOnSecondary { get; set; }

    /// <summary>主 ViewModel。窗口是反复重建的，数据与引擎都必须挂在它身上。</summary>
    public static MainViewModel ViewModel => _viewModel ??= CreateViewModel();

    /// <summary>
    /// 托盘图标建出来了吗。建不出来就不能收托盘——
    /// 窗口一关就再也没东西能把程序叫回来，那是个连任务管理器都不好找的隐身状态。
    /// </summary>
    public static bool CanHideToTray => _tray is not null;

    // ====== 启动 ======

    /// <summary>启动：定生命周期、建 ViewModel 与托盘、挂单实例监听，最后亮出主窗口。</summary>
    public static void Initialize(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;

        // 窗口会反复关掉再建，退出时机不能交给「最后一个窗口关闭」去判断
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.Exit += (_, _) => Release();

        _viewModel = CreateViewModel();

        CreateTrimmer();
        CreateTray();

        // 再有人双击 exe 时，那个进程会立刻退出，只给我们发一次信号——
        // 收到就把窗口亮出来，省得用户以为程序没反应
        SingleInstance.Listen(() => Dispatcher.UIThread.Post(ShowWindow));

        // 开机自启（--tray）时不建窗口：只挂托盘与引擎。
        // StartOnce 必须照跑——引擎、主题、更新检查都挂在它上面，跳过就等于没启动。
        if (StartInTray)
        {
            StartOnce();

            return;
        }

        ShowWindow();
    }

    private static MainViewModel CreateViewModel()
    {
        MainViewModel viewModel = new()
        {
            // 窗口关掉之后 AppHost 会是 null，但引擎还在转、日志还得继续收
            Post = action => Dispatcher.UIThread.Post(action),
        };

        return viewModel;
    }

    // ====== 主窗口 ======

    /// <summary>亮出主窗口：关过就重建一个，还在就拉到前台。</summary>
    public static void ShowWindow()
    {
        if (_shuttingDown) return;

        MainWindow? existing = _window;

        if (existing is not null)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;

            existing.Show();
            existing.Activate();

            return;
        }

        MainWindow window = new(ViewModel);

        _window = window;

        if (_desktop is not null) _desktop.MainWindow = window;

        PlaceOnSecondary(window);

        window.Show();
        window.Activate();

        StartOnce();
    }

    /// <summary>
    /// 测试专用：把窗口摆到副屏上。没有副屏、或者副屏装不下，就什么都不做、照旧居中。
    /// </summary>
    /// <remarks>
    /// 判主副屏用 <see cref="Screen.IsPrimary"/>，不看任务栏——任务栏可以挪到副屏，
    /// 那套判据立刻就错。要在 Show 之前摆好：Show 之后再改会让窗口先从主屏闪一下。
    /// </remarks>
    private static void PlaceOnSecondary(MainWindow window)
    {
        if (!OpenOnSecondary) return;

        Screens? screens = window.Screens;

        if (screens is null) return;

        // 优先挑装得下的那一块：副屏通常跟主屏一样大，但这不保证。
        // 注意 window.Width 是 DIP、WorkingArea 是物理像素，两个得先换算到一起再比，
        // 否则 125% 缩放下会算偏一百多像素（实测窗口落成 3260 而不是 3106）。
        Screen? target = screens.All
            .Where(screen => !screen.IsPrimary)
            .Where(screen => screen.WorkingArea.Width >= (int)(window.Width * screen.Scaling)
                             && screen.WorkingArea.Height >= (int)(window.Height * screen.Scaling))
            .OrderBy(screen => screen.Bounds.X)
            .FirstOrDefault();

        if (target is null) return;

        PixelRect area = target.WorkingArea;
        int width = (int)Math.Round(window.Width * target.Scaling);
        int height = (int)Math.Round(window.Height * target.Scaling);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - width) / 2),
            area.Y + Math.Max(0, (area.Height - height) / 2));
    }

    /// <summary>
    /// 首次显示时该做的事：版本探测（会起一个 mediamtx.exe --version 子进程）、
    /// 更新检查、自动起引擎。窗口重建时绝不能再跑一遍。
    /// </summary>
    private static void StartOnce()
    {
        if (_started) return;

        _started = true;

        ViewModel.AttachTheme();

        _ = ViewModel.StartupAsync();
    }

    /// <summary>窗口关掉之后由窗口回调。收进托盘就安排一次空闲回收，否则直接收摊。</summary>
    internal static void OnWindowClosed(MainWindow window, bool toTray, bool isExiting)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_window, window)) _window = null;
        }

        if (!toTray || isExiting)
        {
            Shutdown();

            return;
        }

        // 界面对象这会儿已经没人引用了，等 30 秒没人叫醒就把工作集也交还系统
        _trimmer?.Request();
    }

    // ====== 退出 ======

    /// <summary>托盘菜单「退出程序」。窗口还在就让它走「真退出」那条路，不在就直接收摊。</summary>
    public static void Exit()
    {
        MainWindow? window = _window;

        if (window is not null)
        {
            window.CloseForExit();

            return;
        }

        Shutdown();
    }

    private static void Shutdown()
    {
        lock (Gate)
        {
            if (_shuttingDown) return;

            _shuttingDown = true;
        }

        _desktop?.Shutdown();
    }

    /// <summary>
    /// 重开程序：起一个新进程，然后自己退出。
    /// </summary>
    /// <remarks>
    /// 顺序不能反。单实例锁还攥在手里的时候起新进程，新进程 TryAcquire 会失败、
    /// 直接把自己当成第二个实例退出——看上去就是「点了重启，程序没了」。
    /// 所以先 Release 把命名内核对象放掉（只放锁不够：句柄还在，命名对象就还在），再起新进程。
    ///
    /// 引擎要在调用这里之前就停好：新进程一起来也会去抢 9997 控制口，
    /// 旧引擎还在跑就会互相踩。
    /// </remarks>
    public static void RestartProcess()
    {
        string? exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe))
        {
            AppLog.Write("重启失败：拿不到自身可执行文件路径");

            Exit();

            return;
        }

        SingleInstance.Release();

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 起不来就只能退出了，至少别留一个卡死的空壳
            AppLog.Write("重启失败，直接退出：" + ex.Message);
        }

        Shutdown();
    }

    /// <summary>真正释放资源，由生命周期退出事件触发，只会跑一次。</summary>
    private static void Release()
    {
        Localization.Instance.PropertyChanged -= OnLanguageChanged;

        _trimmer?.Dispose();
        _trimmer = null;

        _tray?.Dispose();
        _tray = null;

        _viewModel?.Dispose();
        _viewModel = null;
    }

    // ====== 空闲回收 ======

    private static void CreateTrimmer()
    {
        // 「忙」的判据是窗口又回来了。窗口开着的时候别去动堆。
        // 引擎在跑不算忙：它是独立进程，回收只暂停本进程的托管线程，转流一秒都不受影响。
        _trimmer = new MemoryTrimmer(() => _window is not null, MemoryTrimmer.TrimAndRelease);

        // 档位存在 app.json 里，这里只是把它翻译成等待时长
        ApplyTrimPolicy(ViewModel.MemoryTrim);
    }

    /// <summary>按设置页的档位换回收节奏。值来自 app.json 的 ui.memoryTrim。</summary>
    public static void ApplyTrimPolicy(string? mode)
    {
        MemoryTrimmer? trimmer = _trimmer;

        if (trimmer is null) return;

        switch (mode)
        {
            case "instant":
                trimmer.SetPolicy(true, TimeSpan.FromSeconds(1));

                break;

            case "fast":
                trimmer.SetPolicy(true, TimeSpan.FromSeconds(10));

                break;

            case "off":
                trimmer.SetPolicy(false, MemoryTrimmer.IdleDelay);

                break;

            default:
                trimmer.SetPolicy(true, MemoryTrimmer.IdleDelay);

                break;
        }
    }

    // ====== 托盘 ======

    /// <summary>
    /// 建托盘图标，图标与 exe 用的是同一张。
    /// 图标取不到就整个托盘降级：关窗照常退出（见 <see cref="CanHideToTray"/>）。
    /// </summary>
    private static void CreateTray()
    {
        WindowIcon? icon = TryLoadIcon();

        if (icon is null) return;

        _tray = new TrayIcon
        {
            Icon        = icon,
            ToolTipText = "MediaMTX-Yuki-GUI",
            Menu        = BuildTrayMenu(),
            IsVisible   = true,
        };

        _tray.Clicked += (_, _) => ShowWindow();

        // Avalonia 12 的托盘图标靠 Application 上的附加属性集合注册：光 new 出来是不建原生图标的
        TrayIcon.SetIcons(Application.Current!, [_tray]);

        // 菜单文字在构建那一刻就定死了，换语言得整份重建
        Localization.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>程序集资源里的那枚 .ico，走 avares 路径取。</summary>
    private static WindowIcon? TryLoadIcon()
    {
        try
        {
            using Stream stream = AssetLoader.Open(new Uri("avares://MediaMTX-Yuki-GUI/Assets/app.ico"));

            return new WindowIcon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>托盘菜单：叫回主窗口、退出程序。</summary>
    private static NativeMenu BuildTrayMenu()
    {
        NativeMenuItem show = new() { Header = Localization.T("c014-009") };
        show.Click += (_, _) => ShowWindow();

        NativeMenuItem exit = new() { Header = Localization.T("c014-010") };
        exit.Click += (_, _) => Exit();

        NativeMenu menu = new();

        menu.Items.Add(show);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);

        return menu;
    }

    private static void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Localization.Culture)) return;

        TrayIcon? tray = _tray;

        if (tray is null) return;

        tray.Menu = BuildTrayMenu();
    }
}
