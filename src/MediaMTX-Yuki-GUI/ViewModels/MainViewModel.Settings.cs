using CommunityToolkit.Mvvm.Input;
using MediaMTX_Yuki_GUI.Models;
using L = MediaMTX_Yuki_GUI.Services.Localization;
using System.Runtime.CompilerServices;

namespace MediaMTX_Yuki_GUI.ViewModels;

/// <summary>
/// 设置页的属性。每一项都是「读 app.json 的字段 → 写回 app.json → 触发落盘与热应用」的薄包装，
/// 这样 XAML 里一个 Switch 或输入框就能直接双向绑定，不需要额外的转换层。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>端口与协议开关改了之后，引擎得重启才认，UI 上要提醒用户。</summary>

    /// <summary>设置页顶部的搜索词，空串表示不过滤。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsAppearanceGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsEngineGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsTuningGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsOpsGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsAuthGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsCatalogGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsAboutPanelShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsShellGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsNetworkGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsPullGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsPlaybackGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsRecordGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsGovernanceGroupShown))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(HasNoSettingsMatch))]
    private string _settingsSearch = "";

    /// <summary>外观分组是否过搜索。</summary>
    public bool IsAppearanceGroupShown => MatchesSettingsSearch("界面 外观 主题 语言 深色 浅色 硬件 加速 材质 云母 亚克力 theme language appearance hardware acceleration backdrop mica acrylic");

    /// <summary>引擎分组是否过搜索。</summary>
    public bool IsEngineGroupShown => MatchesSettingsSearch("引擎 启动 重拉 自动 日志 保留 清理 路径 兜底 正则 时间戳 engine autostart log retention path all_others regex");

    /// <summary>调参分组是否过搜索。</summary>
    public bool IsTuningGroupShown => MatchesSettingsSearch("容错 调参 观众 断流 重连 超时 队列 tuning timeout");

    /// <summary>运维分组是否过搜索。</summary>
    public bool IsOpsGroupShown => MatchesSettingsSearch("运维 接口 control api metrics pprof 回放 端口 ops");

    /// <summary>鉴权分组：只在高级模式出现，和「多用户鉴权」并排。</summary>
    public bool IsAuthGroupShown
        => IsAdvancedMode && MatchesSettingsSearch("鉴权 账号 用户名 密码 auth password");

    /// <summary>目录分组是否过搜索：三个「在资源管理器里打开」的入口。</summary>
    public bool IsCatalogGroupShown => MatchesSettingsSearch("目录 路径 打开 folder directory path");

    /// <summary>关于分组是否过搜索：版本号与作者写的那段话。</summary>
    public bool IsAboutPanelShown => MatchesSettingsSearch("关于 版本 about version");

    /// <summary>窗口与启动分组是否过搜索。</summary>
    public bool IsShellGroupShown => MatchesSettingsSearch("窗口 托盘 开机 自启 启动 关闭 内存 回收 tray startup window memory");

    /// <summary>网络与更新分组是否过搜索。</summary>
    public bool IsNetworkGroupShown => MatchesSettingsSearch("网络 更新 代理 镜像 源 下载 检查 通道 正式 预发布 推荐 版本 proxy update source mirror download github channel stable prerelease recommended version");

    /// <summary>拉流源分组：只有高级模式才出现。</summary>
    public bool IsPullGroupShown
        => IsAdvancedMode && MatchesSettingsSearch("拉流 拉取 上游 按需 source pull upstream demand");

    /// <summary>播放协议分组：只在高级模式出现（端口与开启与否属于接管引擎的细节）。</summary>
    public bool IsPlaybackGroupShown
        => IsAdvancedMode && MatchesSettingsSearch("播放 协议 hls webrtc rtsp 端口 playback protocol 观众");

    /// <summary>录制分组：只有高级模式才出现。</summary>
    public bool IsRecordGroupShown
        => IsAdvancedMode && MatchesSettingsSearch("录制 录像 分段 保留 record segment");

    /// <summary>连接治理分组：只有高级模式才出现。</summary>
    public bool IsGovernanceGroupShown
        => IsAdvancedMode && MatchesSettingsSearch("连接 治理 握手 超时 传输 governance handshake transport");

    /// <summary>搜索什么都没命中时给个提示。</summary>
    public bool HasNoSettingsMatch
        => !string.IsNullOrWhiteSpace(SettingsSearch)
           && !IsAppearanceGroupShown
           && !IsEngineGroupShown
           && !IsTuningGroupShown
           && !IsOpsGroupShown
           && !IsAuthGroupShown
           && !IsCatalogGroupShown
           && !IsAboutPanelShown
           && !IsShellGroupShown
           && !IsNetworkGroupShown
           && !IsPullGroupShown
           && !IsPlaybackGroupShown
           && !IsRecordGroupShown
           && !IsGovernanceGroupShown;

    /// <summary>关键词整串里是否含有搜索词，空搜索一律算命中。</summary>
    private bool MatchesSettingsSearch(string keywords)
    {
        string query = SettingsSearch?.Trim() ?? "";

        return query.Length == 0
               || keywords.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(RestartBarVisible))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(RestartBarText))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(RestartBarActionText))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(RestartBarEngineOnly))]
    private bool _needsEngineRestart;

    /// <summary>改了硬件加速这类启动期才读的项，得整个程序重开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(RestartBarVisible))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(RestartBarText))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(RestartBarActionText))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(RestartBarEngineOnly))]
    private bool _needsAppRestart;

    /// <summary>
    /// 顶上那条「改动待重启」横幅露不露。
    /// </summary>
    /// <remarks>
    /// 引擎那条只在引擎真跑着的时候才提——停着的时候本来就没有在转的流会断，
    /// 下次启动自然就读新配置了，再挂条横幅催重启是纯噪音。
    /// </remarks>
    public bool RestartBarVisible
        => NeedsAppRestart || (NeedsEngineRestart && IsEngineRunning);

    /// <summary>横幅正文：按「只有引擎要重启还是整个程序要重启」挑一句。</summary>
    public string RestartBarText
        => NeedsAppRestart ? L.T("t005-003") : L.T("t005-001");

    /// <summary>横幅右侧那颗按钮的字。</summary>
    public string RestartBarActionText
        => L.T(NeedsAppRestart ? "t005-004" : "t005-002");

    /// <summary>true 表示这条横幅只跟引擎有关——重启按钮走「重起引擎」而不是「重开程序」。</summary>
    public bool RestartBarEngineOnly
        => !NeedsAppRestart;

    /// <summary>
    /// 横幅右边那颗按钮：该重起引擎就重起引擎，该重开程序就重开程序。
    /// </summary>
    /// <remarks>
    /// 重开程序这条路必须先把引擎停干净并存盘：新进程一起来就要抢 9997 与 runtime.yml，
    /// 而配置平时是 400ms 防抖后才落盘的，用户改完立刻点重启会丢掉最后一次改动。
    /// </remarks>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task RestartAsync()
    {
        if (IsUpdating)
        {
            ShowToast(L.T("t005-005"), ToastKind.Warn);

            return;
        }

        bool restartApp = NeedsAppRestart;

        if (restartApp && AppHost is not null)
        {
            bool ok = await AppHost.ConfirmAsync(
                L.T("t005-006"),
                L.T("t005-007"),
                L.T("t005-004"));

            if (!ok) return;
        }

        NeedsEngineRestart = false;
        NeedsAppRestart = false;

        if (IsEngineRunning)
        {
            await StopEngineAsync();
        }

        // 防抖可能还没跑，这里直接落一次盘，免得新进程读到旧配置
        _cfg.Inputs = Inputs.Select(item => item.ToModel()).ToList();
        _cfg.Targets = Targets.Select(item => item.ToModel()).ToList();
        _store.Save(_cfg);
        Services.AppShell.ApplyTrimPolicy(_cfg.Ui.MemoryTrim);

        if (restartApp)
        {
            Services.AppShell.RestartProcess();

            return;
        }

        await StartEngineAsync();

        ShowToast(L.T("t001-006"), ToastKind.Success);
    }

    // ====== 引擎端口 ======

    public int RtmpPort
    {
        get => _cfg.Engine.RtmpPort;
        set => ApplySetting(_cfg.Engine.RtmpPort, value, v => _cfg.Engine.RtmpPort = v, needsRestart: true);
    }

    public int ApiPort
    {
        get => _cfg.Engine.ApiPort;
        set => ApplySetting(_cfg.Engine.ApiPort, value, v => _cfg.Engine.ApiPort = v, needsRestart: true);
    }

    public int MetricsPort
    {
        get => _cfg.Engine.MetricsPort;
        set => ApplySetting(_cfg.Engine.MetricsPort, value, v => _cfg.Engine.MetricsPort = v, needsRestart: true);
    }

    // ====== 对外协议 ======

    public bool RtspEnabled
    {
        get => _cfg.Protocols.Rtsp;
        set => ApplySetting(_cfg.Protocols.Rtsp, value, v => _cfg.Protocols.Rtsp = v, needsRestart: true);
    }

    public int RtspPort
    {
        get => _cfg.Protocols.RtspPort;
        set => ApplySetting(_cfg.Protocols.RtspPort, value, v => _cfg.Protocols.RtspPort = v, needsRestart: true);
    }

    public bool HlsEnabled
    {
        get => _cfg.Protocols.Hls;
        set => ApplySetting(_cfg.Protocols.Hls, value, v => _cfg.Protocols.Hls = v, needsRestart: true);
    }

    public int HlsPort
    {
        get => _cfg.Protocols.HlsPort;
        set => ApplySetting(_cfg.Protocols.HlsPort, value, v => _cfg.Protocols.HlsPort = v, needsRestart: true);
    }

    public bool WebrtcEnabled
    {
        get => _cfg.Protocols.Webrtc;
        set => ApplySetting(_cfg.Protocols.Webrtc, value, v => _cfg.Protocols.Webrtc = v, needsRestart: true);
    }

    public int WebrtcPort
    {
        get => _cfg.Protocols.WebrtcPort;
        set => ApplySetting(_cfg.Protocols.WebrtcPort, value, v => _cfg.Protocols.WebrtcPort = v, needsRestart: true);
    }

    public bool SrtEnabled
    {
        get => _cfg.Protocols.Srt;
        set => ApplySetting(_cfg.Protocols.Srt, value, v => _cfg.Protocols.Srt = v, needsRestart: true);
    }

    public int SrtPort
    {
        get => _cfg.Protocols.SrtPort;
        set => ApplySetting(_cfg.Protocols.SrtPort, value, v => _cfg.Protocols.SrtPort = v, needsRestart: true);
    }

    public bool MoqEnabled
    {
        get => _cfg.Protocols.Moq;
        set => ApplySetting(_cfg.Protocols.Moq, value, v => _cfg.Protocols.Moq = v, needsRestart: true);
    }

    public int MoqPort
    {
        get => _cfg.Protocols.MoqPort;
        set => ApplySetting(_cfg.Protocols.MoqPort, value, v => _cfg.Protocols.MoqPort = v, needsRestart: true);
    }

    // ====== 容错与调参 ======

    public int MaxReaders
    {
        get => _cfg.Advanced.MaxReaders;
        set => ApplySetting(_cfg.Advanced.MaxReaders, value, v => _cfg.Advanced.MaxReaders = v);
    }

    public bool AlwaysAvailable
    {
        get => _cfg.Advanced.AlwaysAvailable;
        set => ApplySetting(_cfg.Advanced.AlwaysAvailable, value, v => _cfg.Advanced.AlwaysAvailable = v);
    }

    public bool OverridePublisher
    {
        get => _cfg.Advanced.OverridePublisher;
        set => ApplySetting(_cfg.Advanced.OverridePublisher, value, v => _cfg.Advanced.OverridePublisher = v);
    }

    public int ReadTimeoutSeconds
    {
        get => _cfg.Advanced.ReadTimeoutSeconds;
        set => ApplySetting(_cfg.Advanced.ReadTimeoutSeconds, value, v => _cfg.Advanced.ReadTimeoutSeconds = v);
    }

    public int WriteTimeoutSeconds
    {
        get => _cfg.Advanced.WriteTimeoutSeconds;
        set => ApplySetting(_cfg.Advanced.WriteTimeoutSeconds, value, v => _cfg.Advanced.WriteTimeoutSeconds = v);
    }

    public int WriteQueueSize
    {
        get => _cfg.Advanced.WriteQueueSize;
        set => ApplySetting(_cfg.Advanced.WriteQueueSize, value, v => _cfg.Advanced.WriteQueueSize = v);
    }

    // ====== 运维接口 ======

    public bool PprofEnabled
    {
        get => _cfg.Ops.Pprof;
        set => ApplySetting(_cfg.Ops.Pprof, value, v => _cfg.Ops.Pprof = v, needsRestart: true);
    }

    public int PprofPort
    {
        get => _cfg.Ops.PprofPort;
        set => ApplySetting(_cfg.Ops.PprofPort, value, v => _cfg.Ops.PprofPort = v, needsRestart: true);
    }

    public bool PlaybackEnabled
    {
        get => _cfg.Ops.Playback;
        set => ApplySetting(_cfg.Ops.Playback, value, v => _cfg.Ops.Playback = v, needsRestart: true);
    }

    public int PlaybackPort
    {
        get => _cfg.Ops.PlaybackPort;
        set => ApplySetting(_cfg.Ops.PlaybackPort, value, v => _cfg.Ops.PlaybackPort = v, needsRestart: true);
    }

    // ====== 鉴权总开关 ======
    //
    // 账号本身在侧栏「账号」页维护（见 MainViewModel.Accounts.cs），这里只管引擎要不要验。

    /// <summary>推流鉴权总开关。开着时只有账号表里那些账号能推，关着时全放行。</summary>
    public bool AuthEnabled
    {
        get => _cfg.AuthEnabled;
        set => ApplySetting(_cfg.AuthEnabled, value, v => _cfg.AuthEnabled = v, needsRestart: true);
    }

    // ====== 界面外观 ======

    /// <summary>把配置里保存的主题应用上去，程序启动时调一次。Avalonia 换主题不需要窗口句柄。</summary>
    public void AttachTheme()
    {
        Services.ThemeService.Apply(_cfg.Ui.Theme);
    }

    /// <summary>system / light / dark，改完立刻生效，不用重启引擎。</summary>
    public string ThemeName
    {
        get => _cfg.Ui.Theme;
        set => ApplySetting(_cfg.Ui.Theme, value, v =>
        {
            _cfg.Ui.Theme = v;

            Services.ThemeService.Apply(v);

            OnPropertyChanged(nameof(ThemeIndex));
        });
    }

    /// <summary>
    /// Avalonia 的 ComboBox 没有 SelectedValuePath，这里用索引把下拉框接回 ThemeName。
    /// </summary>
    public int ThemeIndex
    {
        get => ThemeName switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0,
        };
        set => ThemeName = value switch
        {
            1 => "light",
            2 => "dark",
            _ => "system",
        };
    }

    /// <summary>
    /// 硬件加速。改了要重开程序——渲染后端在 BuildAvaloniaApp 里定死，运行时换不了。
    /// </summary>
    public bool HardwareAcceleration
    {
        get => _cfg.Ui.HardwareAcceleration;
        set => ApplySetting(_cfg.Ui.HardwareAcceleration, value, v => _cfg.Ui.HardwareAcceleration = v, needsAppRestart: true);
    }

    /// <summary>
    /// 窗口背景材质：mica / acrylic / solid。当前窗口立刻生效，不用重启。
    /// </summary>
    public string WindowBackdrop
    {
        get => _cfg.Ui.WindowBackdrop;
        set => ApplySetting(_cfg.Ui.WindowBackdrop, value, v =>
        {
            _cfg.Ui.WindowBackdrop = v;

            // 窗口是次抛的，重建时 OnOpened 也会照配置再上一次；这里管「改完马上看见」
            AppHost?.ApplyBackdrop(v);

            OnPropertyChanged(nameof(WindowBackdropIndex));
        });
    }

    /// <summary>ComboBox 没有 SelectedValuePath，用索引把下拉接回 <see cref="WindowBackdrop"/>。</summary>
    public int WindowBackdropIndex
    {
        get => WindowBackdrop switch
        {
            "acrylic" => 1,
            "solid" => 2,
            _ => 0,
        };
        set => WindowBackdrop = value switch
        {
            1 => "acrylic",
            2 => "solid",
            _ => "mica",
        };
    }

    // ====== 窗口与启动 ======

    /// <summary>随 Windows 登录自动打开本程序。写 HKCU 的 Run 键，不需要管理员权限。</summary>
    /// <remarks>
    /// 开关拨不动就说明注册表写失败（比如被安全软件拦下），这时保持旧值让界面回弹，
    /// 而不是把 false 记进配置、下次启动又静默地什么都没做。
    /// </remarks>
    public bool StartWithWindows
    {
        get => _cfg.Ui.StartWithWindows;
        set
        {
            if (_cfg.Ui.StartWithWindows == value) return;

            if (!Services.AutoStart.SetEnabled(value)) return;

            ApplySetting(_cfg.Ui.StartWithWindows, value, v => _cfg.Ui.StartWithWindows = v);
        }
    }

    /// <summary>点关闭按钮时收进托盘而不是退出。真正的退出走托盘菜单。</summary>
    public bool CloseToTray
    {
        get => _cfg.Ui.CloseToTray;
        set => ApplySetting(_cfg.Ui.CloseToTray, value, v => _cfg.Ui.CloseToTray = v);
    }

    /// <summary>
    /// 界面关掉之后多久把内存还回去：instant（1 秒）/ fast（10 秒）/ default（30 秒）/ off（不回收）。
    /// 收得越勤内存数字越好看，代价是下次打开界面要重新缺页拉回，会慢那么一下。
    /// </summary>
    public string MemoryTrim
    {
        get => _cfg.Ui.MemoryTrim;
        set
        {
            ApplySetting(_cfg.Ui.MemoryTrim, value, v => _cfg.Ui.MemoryTrim = v);

            Services.AppShell.ApplyTrimPolicy(value);
        }
    }

    /// <summary>Avalonia 的 ComboBox 没有 SelectedValuePath，这里用索引把下拉框接回档位名。</summary>
    public int MemoryTrimIndex
    {
        get => MemoryTrim switch
        {
            "instant" => 0,
            "fast"    => 1,
            "off"     => 3,
            _         => 2,
        };
        set => MemoryTrim = value switch
        {
            0 => "instant",
            1 => "fast",
            3 => "off",
            _ => "default",
        };
    }

    // ====== 引擎行为 ======

    /// <summary>本程序启动时自动把引擎拉起来。</summary>
    public bool AutoStartEngine
    {
        get => _cfg.Engine.AutoStart;
        set => ApplySetting(_cfg.Engine.AutoStart, value, v => _cfg.Engine.AutoStart = v);
    }

    /// <summary>引擎意外退出后自动重新拉起。</summary>
    public bool AutoRestartEngine
    {
        get => _cfg.Engine.AutoRestart;
        set => ApplySetting(_cfg.Engine.AutoRestart, value, v => _cfg.Engine.AutoRestart = v);
    }

    /// <summary>
    /// 日志保留天数：0 表示不清理。
    /// 只影响下次启动时的清理，引擎正握着当天那份在写，运行期间不动它。
    /// </summary>
    public int LogRetentionDays
    {
        get => _cfg.Ui.LogRetentionDays;
        set
        {
            ApplySetting(_cfg.Ui.LogRetentionDays, value, v => _cfg.Ui.LogRetentionDays = v);

            OnPropertyChanged(nameof(LogRetentionIndex));
        }
    }

    /// <summary>Avalonia 的 ComboBox 没有 SelectedValuePath，这里用索引把下拉框接回天数。</summary>
    public int LogRetentionIndex
    {
        get => LogRetentionDays switch
        {
            7    => 1,
            30   => 2,
            90   => 3,
            _    => 0,
        };
        set => LogRetentionDays = value switch
        {
            1 => 7,
            2 => 30,
            3 => 90,
            _ => 0,
        };
    }

    // ====== 网络与更新 ======

    /// <summary>启动时检查本程序有没有新版本。</summary>
    public bool CheckAppUpdateOnStartup
    {
        get => _cfg.Update.CheckAppOnStartup;
        set => ApplySetting(_cfg.Update.CheckAppOnStartup, value, v => _cfg.Update.CheckAppOnStartup = v);
    }

    /// <summary>启动时检查引擎有没有新版本。</summary>
    public bool CheckEngineUpdateOnStartup
    {
        get => _cfg.Update.CheckEngineOnStartup;
        set => ApplySetting(_cfg.Update.CheckEngineOnStartup, value, v => _cfg.Update.CheckEngineOnStartup = v);
    }

    /// <summary>
    /// 本程序更新查哪个通道：0 正式版 / 1 预发布版。
    /// 默认预发布——本程序自己就是 alpha，只查正式版会永远查不到东西。
    /// </summary>
    public int AppChannelIndex
    {
        get => IsStableAppChannel ? 0 : 1;
        set
        {
            string channel = value == 0 ? Models.UpdateOptions.AppChannelStable : Models.UpdateOptions.AppChannelPrerelease;

            if (string.Equals(_cfg.Update.AppChannel, channel, StringComparison.Ordinal)) return;

            _cfg.Update.AppChannel = channel;
            _store.Save(_cfg);

            // 换通道等于换了一套版本号：上一轮查到的 tag 只代表旧通道，不能再拿来判断
            _latestAppTag = null;
            _appChecked = false;
            HasAppUpdate = false;
            AppUpdateStatus = string.Empty;

            OnPropertyChanged();

            // 顺手查一次。两个通道是两个缓存键，这一下多半会真的打一次网络
            _ = CheckAppUpdateAsync(force: false);
        }
    }

    /// <summary>引擎更新走哪条通道：0 最新版本 / 1 推荐适配版本。默认最新。</summary>
    public int EngineChannelIndex
    {
        get => IsRecommendedEngineChannel ? 1 : 0;
        set
        {
            string channel = value == 1
                ? Models.UpdateOptions.EngineChannelRecommended
                : Models.UpdateOptions.EngineChannelLatest;

            if (string.Equals(_cfg.Update.EngineChannel, channel, StringComparison.Ordinal)) return;

            _cfg.Update.EngineChannel = channel;
            _store.Save(_cfg);

            // 两条通道的目标版本来自完全不同的地方（远端 tag / 本地常量），旧结果一律作废
            _latestEngineTag = null;
            _engineChecked = false;
            HasEngineUpdate = false;
            EngineUpdateStatus = string.Empty;

            OnPropertyChanged();

            // 推荐档根本不联网，切过去立刻就能算出「要不要换」；最新档走缓存
            _ = CheckEngineUpdateAsync(force: false);
        }
    }

    /// <summary>
    /// 引擎更新那行的黄字。带上推荐适配版本号——「未知后果」是相对哪一版说的，
    /// 不写出来用户没法判断该不该升。
    /// </summary>
    public string EngineUpdateCaution
        => L.T("l016-026").Replace("{version}", Services.ProgramVersion.RecommendedEngineVersion);

    /// <summary>关于页里那行「推荐引擎版本」。</summary>
    public string RecommendedEngineVersionText => Services.ProgramVersion.RecommendedEngineVersion;

    /// <summary>下拉框里能选的更新源，顺序与 SourceCatalog.All 严格一致。</summary>
    public IReadOnlyList<string> UpdateSourceNames
        => Services.SourceCatalog.All.Select(source => L.T(source.NameKey)).ToList();

    /// <summary>本程序用哪个源。</summary>
    public int AppSourceIndex
    {
        get => IndexOfSource(_cfg.Update.AppSource);
        set
        {
            string id = Services.SourceCatalog.All[Math.Clamp(value, 0, Services.SourceCatalog.All.Count - 1)].Id;

            ApplySetting(_cfg.Update.AppSource, id, v => _cfg.Update.AppSource = v);
        }
    }

    /// <summary>引擎用哪个源。</summary>
    public int EngineSourceIndex
    {
        get => IndexOfSource(_cfg.Update.EngineSource);
        set
        {
            string id = Services.SourceCatalog.All[Math.Clamp(value, 0, Services.SourceCatalog.All.Count - 1)].Id;

            ApplySetting(_cfg.Update.EngineSource, id, v => _cfg.Update.EngineSource = v);
        }
    }

    /// <summary>源 Id 在下拉框里的下标，认不出来的一律当官方。</summary>
    private static int IndexOfSource(string? id)
    {
        for (int index = 0; index < Services.SourceCatalog.All.Count; index++)
        {
            if (Services.SourceCatalog.All[index].Id == id) return index;
        }

        return 0;
    }

    // ---- 默认推流代理 ----

    /// <summary>
    /// 推流的默认代理：direct 直连 / system 跟系统 / manual 手动。
    /// 国内平台直连最快，被墙的平台才需要挂代理。
    /// </summary>
    public int StreamProxyIndex
    {
        get => ProxyIndex(_cfg.Proxy.Mode);
        set
        {
            string mode = ProxyModeAt(value);

            ApplySetting(_cfg.Proxy.Mode, mode, v =>
            {
                _cfg.Proxy.Mode = v;

                OnPropertyChanged(nameof(IsStreamProxyManual));
                OnPropertyChanged(nameof(StreamProxyRequired));
                OnPropertyChanged(nameof(StreamProxyUrlInvalid));
                OnPropertyChanged(nameof(StreamProxyWarning));
            });
        }
    }

    /// <summary>默认推流代理的地址，只有手动档才用得上。</summary>
    public string StreamProxyUrl
    {
        get => _cfg.Proxy.Url;
        set
        {
            ApplySetting(_cfg.Proxy.Url, value, v => _cfg.Proxy.Url = v);

            // 地址填上之后那句「还没填」的警告要当场消失，不能等用户去别处晃一圈
            OnPropertyChanged(nameof(StreamProxyUrlInvalid));
            OnPropertyChanged(nameof(StreamProxyWarning));
        }
    }

    /// <summary>手动档才让用户填地址，其它档填了也不看。</summary>
    public bool IsStreamProxyManual => _cfg.Proxy.IsManual;

    /// <summary>
    /// 推流代理那一行的红字：手动档却没填地址时才出现。
    /// 设置页没有保存按钮，字段一改就落盘，拦不住用户留空，所以这句话要说明
    /// 「留空会怎样」，而不是像编辑窗那样只说「不能为空」。
    /// </summary>
    public string StreamProxyWarning
        => IsStreamProxyManual && string.IsNullOrWhiteSpace(_cfg.Proxy.Url) ? L.T("l016-019") : string.Empty;

    /// <summary>手动档这个档位下，地址框要标红星并可能亮红框。</summary>
    public bool StreamProxyRequired => IsStreamProxyManual;

    /// <summary>地址框的红框：手动档、而且真的空着。</summary>
    public bool StreamProxyUrlInvalid
        => IsStreamProxyManual && string.IsNullOrWhiteSpace(_cfg.Proxy.Url);

    // ---- 更新下载代理 ----

    /// <summary>查更新与下载安装包的代理档位，与推流代理分开配。</summary>
    public int UpdateProxyIndex
    {
        get => ProxyIndex(_cfg.Update.Proxy.Mode);
        set
        {
            string mode = ProxyModeAt(value);

            ApplySetting(_cfg.Update.Proxy.Mode, mode, v =>
            {
                _cfg.Update.Proxy.Mode = v;

                OnPropertyChanged(nameof(IsUpdateProxyManual));
                OnPropertyChanged(nameof(UpdateProxyRequired));
                OnPropertyChanged(nameof(UpdateProxyUrlInvalid));
                OnPropertyChanged(nameof(UpdateProxyWarning));
            });
        }
    }

    /// <summary>更新下载代理的地址。</summary>
    public string UpdateProxyUrl
    {
        get => _cfg.Update.Proxy.Url;
        set
        {
            ApplySetting(_cfg.Update.Proxy.Url, value, v => _cfg.Update.Proxy.Url = v);

            OnPropertyChanged(nameof(UpdateProxyUrlInvalid));
            OnPropertyChanged(nameof(UpdateProxyWarning));
        }
    }

    /// <summary>手动档才让用户填地址。</summary>
    public bool IsUpdateProxyManual => _cfg.Update.Proxy.IsManual;

    /// <summary>更新下载代理那一行的红字：手动档却没填地址时才出现，说法同推流代理。</summary>
    public string UpdateProxyWarning
        => IsUpdateProxyManual && string.IsNullOrWhiteSpace(_cfg.Update.Proxy.Url) ? L.T("l016-019") : string.Empty;

    /// <summary>手动档这个档位下，地址框要标红星并可能亮红框。</summary>
    public bool UpdateProxyRequired => IsUpdateProxyManual;

    /// <summary>地址框的红框：手动档、而且真的空着。</summary>
    public bool UpdateProxyUrlInvalid
        => IsUpdateProxyManual && string.IsNullOrWhiteSpace(_cfg.Update.Proxy.Url);

    /// <summary>档位名在下拉框里的下标：0 直连 / 1 系统 / 2 手动。</summary>
    private static int ProxyIndex(string? mode) => mode switch
    {
        Models.ProxyOptions.Direct => 0,
        Models.ProxyOptions.Manual => 2,
        _                          => 1,
    };

    /// <summary>下标换回档位名，越界的一律当系统代理。</summary>
    private static string ProxyModeAt(int index) => index switch
    {
        0 => Models.ProxyOptions.Direct,
        2 => Models.ProxyOptions.Manual,
        _ => Models.ProxyOptions.System,
    };

    // ====== 简洁 / 高级模式 ======

    /// <summary>
    /// 高级模式：设置页多出四组接管引擎能力的栏，输入源还能逐路录制。
    /// 只读：切换要重启引擎，不能让双向绑定在用户点一下开关时就改掉配置，
    /// 得先过确认弹窗，见 <see cref="SwitchModeAsync"/>。
    /// </summary>
    public bool IsAdvancedMode
        => string.Equals(_cfg.Ui.Mode, Models.UiOptions.AdvancedMode, StringComparison.Ordinal);

    public bool IsSimpleMode => !IsAdvancedMode;

    /// <summary>侧栏那颗图标随模式换：简洁是一张网格，高级是调节杆。</summary>
    public string ModeIcon => IsAdvancedMode ? "\uE9E9" : "\uE8A9";

    /// <summary>侧栏那一项的文字直接写当前模式，省得用户去猜里面是什么。</summary>
    public string ModeNavText => L.T(IsAdvancedMode ? "c001-009" : "c001-008");


    /// <summary>
    /// 切到另一种工作模式。
    /// 两个方向都要重启引擎：录制键与拉流源都写在 path 里，热更新推不动，
    /// 而且切回简洁模式后那几条 path 会从 runtime.yml 里消失。
    /// 配置一律原样保留，只是简洁模式不再读写高级侧那几组。
    /// </summary>
    /// <param name="mode">目标模式，取 <see cref="Models.UiOptions.AdvancedMode"/> 或 <see cref="Models.UiOptions.SimpleMode"/>。</param>
    [RelayCommand]
    private async Task SwitchModeAsync(string? mode)
    {
        if (string.IsNullOrEmpty(mode)) return;
        if (string.Equals(_cfg.Ui.Mode, mode, StringComparison.Ordinal)) return;
        if (AppHost is null) return;

        bool toAdvanced = string.Equals(mode, Models.UiOptions.AdvancedMode, StringComparison.Ordinal);

        bool ok = await AppHost.ConfirmAsync(
            L.T(toAdvanced ? "c007-006" : "c007-007"),
            L.T(toAdvanced ? "l007-005" : "l007-006"),
            L.T("c007-005"));

        if (!ok) return;

        // 先把正在跑的东西停下来再改配置：引擎运行期间重写 runtime.yml 没有意义，
        // 它只在启动时读一次。
        bool wasRunning = IsEngineRunning;

        if (wasRunning) await StopEngineAsync();

        _cfg.Ui.Mode = mode;
        _store.Save(_cfg);

        NotifyModeChanged();
        RefreshRuntimeDisabled();
        WriteRuntimeYml();

        if (wasRunning) await StartEngineAsync();

        ShowToast(L.T("c007-008").Replace("{name}", L.T(toAdvanced ? "c001-009" : "c001-008")), ToastKind.Success);
    }

    /// <summary>模式相关的派生属性变了一圈，挨个通知一声。</summary>
    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(IsAdvancedMode));
        OnPropertyChanged(nameof(IsSimpleMode));
        OnPropertyChanged(nameof(ModeIcon));
        OnPropertyChanged(nameof(ModeNavText));
        OnPropertyChanged(nameof(IsPullGroupShown));
        OnPropertyChanged(nameof(IsPlaybackGroupShown));

        OnPropertyChanged(nameof(IsAuthGroupShown));
        OnPropertyChanged(nameof(IsRecordGroupShown));
        OnPropertyChanged(nameof(IsGovernanceGroupShown));
        OnPropertyChanged(nameof(HasNoSettingsMatch));
    }

    // ====== 拉流源（高级模式） ======

    /// <summary>按需拉流：有人看才去连上游。</summary>
    public bool PullOnDemand
    {
        get => _cfg.Pull.SourceOnDemand;
        set => ApplySetting(_cfg.Pull.SourceOnDemand, value, v => _cfg.Pull.SourceOnDemand = v, needsRestart: true);
    }

    /// <summary>按需拉流多久没连上算超时。</summary>
    public int PullStartTimeoutSeconds
    {
        get => _cfg.Pull.StartTimeoutSeconds;
        set => ApplySetting(_cfg.Pull.StartTimeoutSeconds, value, v => _cfg.Pull.StartTimeoutSeconds = v, needsRestart: true);
    }

    /// <summary>观众走光后隔多久断上游。</summary>
    public int PullCloseAfterSeconds
    {
        get => _cfg.Pull.CloseAfterSeconds;
        set => ApplySetting(_cfg.Pull.CloseAfterSeconds, value, v => _cfg.Pull.CloseAfterSeconds = v, needsRestart: true);
    }

    // ====== 路径与兜底（高级模式） ======

    /// <summary>兜底路径：没登记过的路径名也收，但不转发。</summary>
    public bool PathAllOthers
    {
        get => _cfg.Paths.AllOthers;
        set => ApplySetting(_cfg.Paths.AllOthers, value, v => _cfg.Paths.AllOthers = v, needsRestart: true);
    }

    /// <summary>时间戳原样透传，不在起点归零。</summary>
    public bool UseAbsoluteTimestamp
    {
        get => _cfg.Paths.UseAbsoluteTimestamp;
        set => ApplySetting(_cfg.Paths.UseAbsoluteTimestamp, value, v => _cfg.Paths.UseAbsoluteTimestamp = v, needsRestart: true);
    }

    // ====== 录制（高级模式） ======

    // 录制是逐路开关（见输入源列表那颗按钮与编辑窗），这里只放所有路共用的参数

    /// <summary>录制目录模板。</summary>
    public string RecordPathTemplate
    {
        get => _cfg.Record.PathTemplate;
        set => ApplySetting(_cfg.Record.PathTemplate, value, v => _cfg.Record.PathTemplate = v, needsRestart: true);
    }

    /// <summary>封装格式名：fmp4 / mpegts。</summary>
    public string RecordFormat
    {
        get => _cfg.Record.Format;
        set => ApplySetting(_cfg.Record.Format, value, v => _cfg.Record.Format = v, needsRestart: true);
    }

    /// <summary>Avalonia 的 ComboBox 没有 SelectedValuePath，这里用下标接回格式名。</summary>
    public int RecordFormatIndex
    {
        get => string.Equals(RecordFormat, "mpegts", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        set => RecordFormat = value == 1 ? "mpegts" : "fmp4";
    }

    /// <summary>一个分段多长。</summary>
    public string RecordSegmentDuration
    {
        get => _cfg.Record.SegmentDuration;
        set => ApplySetting(_cfg.Record.SegmentDuration, value, v => _cfg.Record.SegmentDuration = v, needsRestart: true);
    }

    /// <summary>单个分段文件多大就切。</summary>
    public string RecordMaxPartSize
    {
        get => _cfg.Record.MaxPartSize;
        set => ApplySetting(_cfg.Record.MaxPartSize, value, v => _cfg.Record.MaxPartSize = v, needsRestart: true);
    }

    /// <summary>旧分段留多久，0 表示不删。</summary>
    public string RecordDeleteAfter
    {
        get => _cfg.Record.DeleteAfter;
        set => ApplySetting(_cfg.Record.DeleteAfter, value, v => _cfg.Record.DeleteAfter = v, needsRestart: true);
    }


    // ====== 连接治理（高级模式） ======

    /// <summary>RTSP 允许的传输方式，逗号分隔。</summary>
    public string RtspTransports
    {
        get => _cfg.Governance.RtspTransports;
        set => ApplySetting(_cfg.Governance.RtspTransports, value, v => _cfg.Governance.RtspTransports = v, needsRestart: true);
    }

    /// <summary>WebRTC 握手超时。</summary>
    public int WebrtcHandshakeTimeoutSeconds
    {
        get => _cfg.Governance.WebrtcHandshakeTimeoutSeconds;
        set => ApplySetting(_cfg.Governance.WebrtcHandshakeTimeoutSeconds, value, v => _cfg.Governance.WebrtcHandshakeTimeoutSeconds = v, needsRestart: true);
    }

    /// <summary>WebRTC 轨道收集超时。</summary>
    public int WebrtcTrackGatherTimeoutSeconds
    {
        get => _cfg.Governance.WebrtcTrackGatherTimeoutSeconds;
        set => ApplySetting(_cfg.Governance.WebrtcTrackGatherTimeoutSeconds, value, v => _cfg.Governance.WebrtcTrackGatherTimeoutSeconds = v, needsRestart: true);
    }

    // ====== 入口协议 ======

    /// <summary>主页展示的推流入口：rtmp / srt / rtsp。</summary>
    public string IngestProtocol
    {
        get => _cfg.Engine.Ingest;
        set => ApplySetting(_cfg.Engine.Ingest, value, v =>
        {
            _cfg.Engine.Ingest = v;

            RefreshPushUrl();
        });
    }

    // ====== 统一处理 ======

    /// <summary>
    /// 值真的变了才写回配置、通知界面并安排一次落盘，
    /// 避免用户在输入框里每敲一个字符就重写一遍 runtime.yml。
    /// </summary>
    /// <summary>可选的界面语言，只列 Languages 目录里已完成翻译的语言包。</summary>
    public IReadOnlyList<Services.LanguageInfo> LanguageChoices { get; } =
        Services.Localization.Available();

    /// <summary>当前语言还差多少条没翻；翻完了就不显示。</summary>
    public string MissingText
        => Services.Localization.Instance.MissingCount == 0
            ? string.Empty
            : L.T("l013-003").Replace("{n}", Services.Localization.Instance.MissingCount.ToString());

    public bool HasMissing
        => Services.Localization.Instance.MissingCount > 0;

    /// <summary>当前界面语言。换一种立刻刷新所有文案，并记进配置。</summary>
    public string SelectedLanguage
    {
        get => _cfg.Ui.Language;
        set
        {
            if (string.Equals(_cfg.Ui.Language, value, StringComparison.Ordinal))
            {
                return;
            }

            _cfg.Ui.Language = value;

            Services.Localization.Instance.SetCulture(value);
            _store.Save(_cfg);

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedLanguageChoice));
            OnPropertyChanged(nameof(MissingText));
            OnPropertyChanged(nameof(HasMissing));
            OnPropertyChanged(nameof(UpdateSourceNames));
            OnPropertyChanged(nameof(EngineUpdateCaution));
            OnPropertyChanged(nameof(RecommendedEngineVersionText));
        }
    }

    /// <summary>
    /// Avalonia 的 ComboBox 用 SelectedItem 绑定，这里把语言选项对象和字符串接起来。
    /// </summary>
    public Services.LanguageInfo? SelectedLanguageChoice
    {
        get => LanguageChoices.FirstOrDefault(
            item => string.Equals(item.Culture, SelectedLanguage, StringComparison.Ordinal));
        set
        {
            if (value is null)
            {
                return;
            }

            SelectedLanguage = value.Culture;

            OnPropertyChanged();
        }
    }

    private void ApplySetting<T>(
        T current,
        T next,
        Action<T> assign,
        bool needsRestart = false,
        bool needsAppRestart = false,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, next)) return;

        assign(next);

        OnPropertyChanged(propertyName);

        // 引擎类的改动重启引擎就够；渲染后端这种只在启动时读一次的，得整个程序重开
        if (needsRestart) NeedsEngineRestart = true;
        if (needsAppRestart) NeedsAppRestart = true;

        ScheduleApply();
    }

    /// <summary>界面外观分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isAppearanceGroupExpanded;

    /// <summary>窗口与启动分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isShellGroupExpanded;

    /// <summary>网络与更新分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isNetworkGroupExpanded;

    /// <summary>引擎行为分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isEngineGroupExpanded;

    /// <summary>容错与调参分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isTuningGroupExpanded;

    /// <summary>运维接口分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isOpsGroupExpanded;

    /// <summary>推流鉴权分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isAuthGroupExpanded;

    /// <summary>拉流源分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isPullGroupExpanded;

    /// <summary>播放协议分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isPlaybackGroupExpanded;

    /// <summary>路径与兜底分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isPathGroupExpanded;

    /// <summary>录制分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isRecordGroupExpanded;

    /// <summary>连接治理分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isGovernanceGroupExpanded;

    /// <summary>目录分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isCatalogGroupExpanded;

    /// <summary>关于分组是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isAboutPanelExpanded;

}
