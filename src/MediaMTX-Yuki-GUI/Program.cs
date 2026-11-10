using Avalonia;

namespace MediaMTX_Yuki_GUI;

/// <summary>进程入口。Avalonia 走经典桌面生命周期，和 WPF 的 App.xaml 建窗时机基本一致。</summary>
internal static class Program
{
    /// <summary>WPF 的 STAThread 由 App.xaml 生成，Avalonia 要自己标。</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        // 这几个开关要在配置正式加载之前就知道，所以单独摸一遍 app.json 的 debug 段。
        // 拿不到就全默认，绝不因此挡住启动。
        Models.DebugOptions debug = Services.EarlyConfig.Config.Debug;

        // 已经有实例在跑：叫它把窗口亮出来，本进程立刻退出。
        // 两个实例会抢同一个 9997 控制口和同一个 runtime.yml，看着像各转各的，实际互相踩。
        // debug.allowMultipleInstances 只在验证单实例守卫本身时该打开。
        if (!debug.AllowMultipleInstances && !Services.SingleInstance.TryAcquire())
        {
            Services.SingleInstance.RequestShow();

            return;
        }

        // 单文件发布时 native 库是解压到 %TEMP%\.net\<应用名>\<哈希>\ 才加载的，
        // 每换一版 exe 就多留一份十几 MB 的缓存。后台扫一遍把旧的删掉，不挡启动。
        // 排查「新版到底解到哪儿去了」时要留住旧目录对照，这时用 debug.skipBundleCacheClean 跳过。
        if (!debug.SkipBundleCacheClean) Services.BundleCacheCleaner.CleanInBackground();

        // 上次更新引擎时新旧文件替换成功，但旧的 mediamtx.exe.old 常常因为杀软
        // 正扫着新文件而删不掉，白白占几十 MB。趁启动这会儿没人占，收个尾。
        Services.EngineUpdater.SweepLeftovers();

        // 开机自启登记的那条命令带 --tray：登录后只留托盘图标，不弹主窗口。
        // 这里就把意图告诉外壳，窗口的建与不建由它决定（它才管得住托盘）。
        Services.AppShell.StartInTray = Array.Exists(args, arg =>
            string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));

        // 自动化测试用：把主窗口直接摆到副屏上，主屏一点不占，人还能接着干别的。
        // 只在带这个开关时才生效，正常双击启动照旧居中。
        Services.AppShell.OpenOnSecondary = Array.Exists(args, arg =>
            string.Equals(arg, "--on-secondary", StringComparison.OrdinalIgnoreCase));

        try
        {
            BuildAvaloniaApp(Services.EarlyConfig.Ui.HardwareAcceleration)
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) when (Services.EarlyConfig.Ui.HardwareAcceleration)
        {
            // GPU 这条路起不来（驱动不支持、远程桌面、ANGLE 库缺失都算）就别硬撑：
            // 把失败记进日志，改用软件渲染再起一次。用户宁可看到界面，也不要一个闪退。
            Services.AppLog.Write("渲染后端 GPU 启动失败，退回软件渲染：" + ex.Message);
            BuildAvaloniaApp(hardwareAcceleration: false).StartWithClassicDesktopLifetime(args);
        }
    }

    /// <summary>给设计器与单元测试复用的构建器。</summary>
    /// <remarks>
    /// 默认按配置里那项硬件加速开关走；设计器与单元测试没有配置，会走默认值（开）。
    /// 真正需要强制软件渲染时传 false。
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp(bool hardwareAcceleration = true)
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                // 列表是按序尝试的：AngleEgl 失败时框架自己落到 Software，不会直接崩。
                //
                // 为什么默认走 GPU：软件光栅在 330Hz 屏上肉眼可见地顿，而这个界面
                // 每一帧都要重画整窗。代价是 ANGLE 会经 D3D11 拉起显卡上下文，
                // NVIDIA 驱动偶尔会把它当成游戏启动、弹一次覆盖层提示——
                // 那条提示只在第一次出现，关掉开关就能彻底避免。
                RenderingMode = hardwareAcceleration
                    ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
                    : [Win32RenderingMode.Software],
            });
}
