using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaMTX_Yuki_GUI.Models;
using MediaMTX_Yuki_GUI.Services;

namespace MediaMTX_Yuki_GUI.ViewModels;


using L = MediaMTX_Yuki_GUI.Services.Localization;
/// <summary>主窗口的唯一数据源，负责把界面操作翻译成配置变更与引擎调用。</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigStore _store = new();
    private readonly EngineManager _engine = new();

    /// <summary>引擎意外退出后已经连续重起几次。启动成功或用户手动操作时归零。</summary>
    private int _restartAttempts;

    /// <summary>各路目标当前的出网比特率，按目标 Id 记。统计页的「合计」由它求和。</summary>
    private readonly Dictionary<string, double> _targetBps = new(StringComparer.Ordinal);

    /// <summary>各路输入源当前的入流比特率，按输入源 Id 记。</summary>
    private readonly Dictionary<string, double> _inboundBps = new(StringComparer.Ordinal);

    /// <summary>等待重起期间的取消源。用户点停止/退出程序时取消它，别再自己爬起来。</summary>
    private CancellationTokenSource? _restartCts;

    /// <summary>连续重起几次还起不来就放弃，别无限循环烧 CPU。</summary>
    private const int MaxRestartAttempts = 5;

    private readonly UpdateChecker _updater;

    /// <summary>本机 RTMP 中转站。引擎把流推到这里，我们再换成平台的地址推出去。</summary>
    private readonly RtmpProxyServer _proxyServer;

    /// <summary>引擎的下载与替换。</summary>
    private readonly EngineUpdater _engineUpdater;

    /// <summary>界面能力的出口，由 App 在创建窗口后注入；VM 自己不认识任何窗口类型。</summary>
    public IAppHost? AppHost { get; set; }

    /// <summary>
    /// 切回 UI 线程的出口，由 AppShell 在启动时注入。
    /// 和 <see cref="AppHost"/> 分开是因为窗口会关掉重建：主窗口收进托盘的那段时间里
    /// AppHost 是空的，但引擎还在转、日志还得往集合里收。
    /// </summary>
    public Action<Action>? Post { get; set; }

    private AppConfig _cfg;
    private ControlApi? _api;
    private CancellationTokenSource? _applyDebounce;
    private CancellationTokenSource? _metricsCts;

    /// <summary>底部气泡的淡出计时器。连点复制时用它把上一条顶掉。</summary>
    private CancellationTokenSource? _toastCts;

    /// <summary>上一次查到的远端版本号，下载与展示都用它。</summary>
    private string? _latestAppTag;
    private string? _latestEngineTag;

    /// <summary>这一路到底查过没有：只有查过才敢说「已经是最新」。</summary>
    private bool _appChecked;
    private bool _engineChecked;

    /// <summary>本程序没配更新源仓库。查不了不等于已是最新，得跟用户说清楚原因。</summary>

    /// <summary>上一次提示过「这条目标走不了代理」的那批名字，用来去重。</summary>
    private string _proxySkipNotified = string.Empty;

    /// <summary>上一次采到的累计字节数，用来差分算码率。</summary>
    private readonly Dictionary<string, (long Bytes, DateTime At)> _lastSamples = [];

    /// <summary>指标轮次的计数器，用来把扫录制目录这种重活塞到每五轮（约十秒）一次。</summary>
    private int _recordSizeTick;

    /// <summary>
    /// 轮询回填出来的展示字段。它们变了不代表配置变了——不挡的话，
    /// 每两秒就会重写一次 runtime.yml，再往引擎推一遍 forward。
    /// </summary>
    private static readonly HashSet<string> RuntimeProperties = new(StringComparer.Ordinal)
    {
        nameof(TargetItemViewModel.RuntimeState),
        nameof(TargetItemViewModel.SpeedText),
        nameof(TargetItemViewModel.ResponseText),
        nameof(TargetItemViewModel.SentText),
        nameof(TargetItemViewModel.ErrorText),
        nameof(InputItemViewModel.IsOnline),
        nameof(InputItemViewModel.RuntimeText),

        // 简洁模式下被程序禁用的那几行，是推导出来的标记，也不能算用户改了配置
        nameof(InputItemViewModel.RuntimeDisabled),
        nameof(TargetItemViewModel.RuntimeDisabled),

        // 设置页换了默认代理档，列表里「默认：X」要跟着变，但这不是用户改了这一行，
        // 不能触发落盘与重推
        nameof(TargetItemViewModel.DefaultProxyMode),
    };

    // ====== 界面状态 ======

    /// <summary>0 总览 / 1 输入源 / 2 输出目标 / 3 运行日志 / 4 设置 / 5 统计 / 6 工作模式 / 7 账号。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewVisible))]
    [NotifyPropertyChangedFor(nameof(IsInputsPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsInputEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsTargetsPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsTargetEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsLogsPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsStatsPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsModePageVisible))]
    [NotifyPropertyChangedFor(nameof(IsAccountsPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsOverviewSelected))]
    [NotifyPropertyChangedFor(nameof(IsInputsSelected))]
    [NotifyPropertyChangedFor(nameof(IsTargetsSelected))]
    [NotifyPropertyChangedFor(nameof(IsLogsSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSelected))]
    [NotifyPropertyChangedFor(nameof(IsStatsSelected))]
    [NotifyPropertyChangedFor(nameof(IsModeSelected))]
    [NotifyPropertyChangedFor(nameof(IsAccountsSelected))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageSubtitle))]
    [NotifyPropertyChangedFor(nameof(IsListPage))]
    [NotifyPropertyChangedFor(nameof(IsTipShown))]
    private int _selectedPage;

    /// <summary>有模态小窗开着时，主界面盖一层遮罩，防止点回上级页面。</summary>
    [ObservableProperty]
    private bool _isDialogOpen;

    /// <summary>正在编辑的输入源；非 null 时输入页让位给编辑页。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputsPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsInputEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsListPage))]
    private InputItemViewModel? _editingInput;

    /// <summary>正在编辑的输出目标；非 null 时输出页让位给编辑页。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTargetsPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsTargetEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsListPage))]
    private TargetItemViewModel? _editingTarget;

    /// <summary>总览页：一眼看清哪条输入推到了哪些输出。</summary>
    public bool IsOverviewVisible => SelectedPage == 0;

    public bool IsInputsPageVisible => SelectedPage == 1 && EditingInput is null;

    public bool IsInputEditorVisible => SelectedPage == 1 && EditingInput is not null;

    public bool IsTargetsPageVisible => SelectedPage == 2 && EditingTarget is null;

    public bool IsTargetEditorVisible => SelectedPage == 2 && EditingTarget is not null;

    /// <summary>
    /// 只有三个列表页（总览 / 输入源 / 输出目标）用那行公共标题。
    /// 日志页和设置页自带头部，编辑页也自带，它们都该把公共标题让出来。
    /// </summary>
    public bool IsListPage
        => SelectedPage <= 2 && EditingInput is null && EditingTarget is null;

    public bool IsLogsPageVisible => SelectedPage == 3;

    public bool IsSettingsPageVisible => SelectedPage == 4;

    /// <summary>统计页：入流与各路出网的实时数字，独立成页便于长期观察。</summary>
    public bool IsStatsPageVisible => SelectedPage == 5;

    /// <summary>
    /// 工作模式页：两张大卡片讲清简洁与高级各能管到多深。
    /// 单独成页是因为切换要重启引擎，摆在设置页里点一下太容易被误触。
    /// </summary>
    public bool IsModePageVisible => SelectedPage == 6;

    /// <summary>
    /// 账号页（高级模式）：一张账号表管所有推流身份，每个输入源自己勾允许谁推。
    /// 单开一页是因为「谁能推什么」是一整张关系图，塞进设置页会变成一堆勾选框。
    /// </summary>
    public bool IsAccountsPageVisible => SelectedPage == 7;

    // 侧栏导航项用 TwoWay 绑 IsChecked：写 true 就等于选中该项，
    // 比 RadioButton 自带的 Command 可靠（一次性设置 IsChecked 不会经过命令）。
    public bool IsOverviewSelected
    {
        get => SelectedPage == 0;
        set
        {
            if (!value) return;

            EditingInput = null;
            EditingTarget = null;
            SelectedPage = 0;
        }
    }

    public bool IsInputsSelected
    {
        get => SelectedPage == 1;
        set
        {
            if (!value) return;

            EditingInput = null;
            EditingTarget = null;
            SelectedPage = 1;
        }
    }

    public bool IsTargetsSelected
    {
        get => SelectedPage == 2;
        set
        {
            if (!value) return;

            EditingInput = null;
            EditingTarget = null;
            SelectedPage = 2;
        }
    }

    public bool IsLogsSelected
    {
        get => SelectedPage == 3;
        set
        {
            if (!value) return;

            EditingInput = null;
            EditingTarget = null;
            SelectedPage = 3;
        }
    }

    public bool IsSettingsSelected
    {
        get => SelectedPage == 4;
        set
        {
            if (!value) return;

            EditingInput = null;
            EditingTarget = null;
            SelectedPage = 4;
        }
    }

    public bool IsStatsSelected
    {
        get => SelectedPage == 5;
        set
        {
            if (!value) return;

            EditingInput = null;
            EditingTarget = null;
            SelectedPage = 5;
        }
    }

    public bool IsModeSelected
    {
        get => SelectedPage == 6;
        set
        {
            if (!value) return;

            EditingInput = null;
            EditingTarget = null;
            SelectedPage = 6;
        }
    }

    public bool IsAccountsSelected
    {
        get => SelectedPage == 7;
        set
        {
            if (!value) return;

            EditingInput = null;
            EditingTarget = null;
            SelectedPage = 7;
        }
    }

    // 侧栏导航项用 TwoWay 绑 IsChecked：写 true 就等于选中该项，
    // 比 RadioButton 自带的 Command 可靠（一次性设置 IsChecked 不会经过命令）。
    public bool IsHomeSelected
    {
        get => SelectedPage == 0;
        set
        {
            if (!value) return;

            EditingTarget = null;
            EditingInput = null;
            SelectedPage = 0;
        }
    }

    /// <summary>右侧内容区的标题，随当前页面切换。</summary>
    public string PageTitle => SelectedPage switch
    {
        0 => L.T("c001-001"),
        1 => L.T("c001-002"),
        2 => L.T("c001-003"),
        3 => L.T("c001-006"),
        6 => L.T("c001-007"),
        7 => L.T("c001-004"),
        _ => L.T("c001-010"),
    };

    public string PageSubtitle => SelectedPage switch
    {
        0 => L.T("l001-001"),
        1 => L.T("l001-002"),
        2 => L.T("l001-003"),
        3 => L.T("l001-006"),
        6 => L.T("l001-007"),
        7 => L.T("l001-004"),
        _ => L.T("l001-008"),
    };

    /// <summary>没绑定到任何输入的输出目标，总览页单独提示一下。</summary>
    public int UnboundTargetCount
        => Targets.Count(t => string.IsNullOrEmpty(t.InputSourceId)
                             || Inputs.All(i => i.Id != t.InputSourceId));

    public bool HasUnboundTargets
        => UnboundTargetCount > 0;

    public string VersionText
        => Services.ProgramVersion.Text;

    // ====== 版本号与更新提示 ======

    /// <summary>引擎自己报告的版本，例如 v1.21.3；探测不到留空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EngineVersionText))]
    private string _engineVersion = string.Empty;

    public string EngineVersionText
        => string.IsNullOrEmpty(EngineVersion) ? L.T("c002-001") : L.T("c002-002") + EngineVersion;

    /// <summary>
    /// 引擎现在到底在不在。每次都现查磁盘：用户可能在两次运行之间把 MediaMTX 目录删掉，
    /// 启动时算一次再缓存住的话，界面会一直骗人。
    /// </summary>
    public bool IsEngineInstalled => File.Exists(AppPaths.EngineExe);

    /// <summary>本程序有新版本时点亮版本号后面的小光点。</summary>
    [ObservableProperty]
    private bool _hasAppUpdate;

    partial void OnHasAppUpdateChanged(bool value) => RefreshTip();

    /// <summary>引擎有新版本时点亮版本号后面的小光点。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallEngineUpdateCommand))]
    private bool _hasEngineUpdate;

    partial void OnHasEngineUpdateChanged(bool value) => RefreshTip();

    /// <summary>更新过程的进度文字，界面直接显示；空串表示没在更新。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateStatus))]
    private string _updateStatus = string.Empty;

    /// <summary>有进度文字才显示那一行，空串时整行收起来。</summary>
    public bool HasUpdateStatus => !string.IsNullOrWhiteSpace(UpdateStatus);

    /// <summary>正在下载或安装，这时候按钮要禁用，避免点两次。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallEngineUpdateCommand))]
    private bool _isUpdating;

    // ====== 只读信息（设置页展示） ======

    public string EngineDirText => AppPaths.MediaMTXDir;

    public string ConfigDirText => AppPaths.ConfigDir;

    public string LogsDirText => AppPaths.LogsDir;

    public string RecordDirText => RecordRootDirectory();

    // ====== 列表空状态 ======

    public bool IsTargetsEmpty => Targets.Count == 0;

    /// <summary>列表非空时才显示表头和表格。</summary>
    public bool IsTargetsVisible => Targets.Count > 0;

    /// <summary>窗口底部气泡的文字，空串表示不显示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToast))]
    private string _toastText = string.Empty;

    /// <summary>
    /// 气泡该不该显示。置 true 播「浮上来 + 渐显」，置 false 播「沉下去 + 渐隐」，
    /// 两段的进度与缓动都在 <c>Controls\ToastBar.cs</c> 里，这里只管开与关。
    /// </summary>
    [ObservableProperty]
    private bool _toastShown;

    /// <summary>
    /// 每弹一条就自增。**同一句话连着弹两次时 <see cref="ToastShown"/> 一直是 true，
    /// 值没变就通知不到控件**，靠这个计数器强制重播整段出现动画。
    /// </summary>
    [ObservableProperty]
    private int _toastSerial;

    /// <summary>
    /// 气泡的语气，决定底色与图标。只在这一条显示期间有效。
    /// 属性名不叫 <c>ToastKind</c>：那样会跟同名枚举撞上，C# 在实例上下文里会把
    /// <c>ToastKind.Success</c> 解析成「这个属性上的成员」，编译报 CS0176。
    /// </summary>
    [ObservableProperty]
    private ToastKind _toastTone = ToastKind.Success;

    public bool HasToast => !string.IsNullOrEmpty(ToastText);

    // ====== 引擎状态 ======

    [ObservableProperty]
    private string _engineStatus = L.T("c002-003");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEngineStopped))]
    [NotifyPropertyChangedFor(nameof(RestartBarVisible))]
    private bool _isEngineRunning;

    public bool IsEngineStopped => !IsEngineRunning;

    // ====== 引擎缺失提示 ======

    /// <summary>
    /// 右下角那张提示卡现在该说什么。三种情况共用一张卡，按要紧程度排：
    /// 引擎没装（转流根本跑不起来）＞ 引擎有新版本 ＞ 本程序有新版本。
    /// 只在启动、装完引擎和两路更新检查回来时重算，不做轮询——它不该在用户干活时自己冒出来。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTipShown))]
    [NotifyPropertyChangedFor(nameof(TipTitle))]
    [NotifyPropertyChangedFor(nameof(TipMessage))]
    [NotifyPropertyChangedFor(nameof(TipActionText))]
    [NotifyPropertyChangedFor(nameof(TipGlyph))]
    private TipKind _tip;

    /// <summary>用户对引擎缺失那张点过「以后再说」。这次运行里不再提，下次启动重新判断。</summary>
    private bool _engineTipDismissed;

    /// <summary>用户对「引擎有新版本」点过「以后再说」。</summary>
    private bool _engineUpdateTipDismissed;

    /// <summary>用户对「本程序有新版本」点过「以后再说」。</summary>
    private bool _appUpdateTipDismissed;

    /// <summary>
    /// 卡片只在总览页露面。切到别的页面就当没看见——既不记成「以后再说」，
    /// 等回到总览页该提的还会再提一次。
    /// </summary>
    public bool IsTipShown => Tip != TipKind.None && SelectedPage == 0;

    public string TipTitle
        => Tip switch
        {
            TipKind.EngineMissing => L.T("c011-003"),
            TipKind.EngineUpdate => L.T("c011-006"),
            TipKind.AppUpdate => L.T("c011-007"),
            _ => string.Empty,
        };

    public string TipMessage
        => Tip switch
        {
            TipKind.EngineMissing => L.T("l011-001"),
            TipKind.EngineUpdate => FormatNote("l011-002", _latestEngineTag, EngineVersion),
            TipKind.AppUpdate => FormatNote("l011-003", _latestAppTag, VersionText),
            _ => string.Empty,
        };

    /// <summary>按钮文字跟着说什么变：没装是「前往下载」，有新版是「前往更新」。</summary>
    public string TipActionText
        => Tip == TipKind.EngineMissing ? L.T("c011-004") : L.T("c011-008");

    /// <summary>图标同理：缺引擎是警告三角，有新版本是向下的箭头。</summary>
    public string TipGlyph
        => Tip == TipKind.EngineMissing ? "\uE7BA" : "\uE896";

    /// <summary>
    /// 请求设置页把「检查更新」那一行滚到眼前，每次自增一下。
    /// 用计数而不是 bool：连点两次「前往下载」也得能重新滚一次。
    /// </summary>
    [ObservableProperty]
    private int _downloadFocusTick;

    /// <summary>
    /// 重算卡片该说什么。顺序就是优先级：引擎没装时哪怕远端有新版也别提更新，
    /// 先装起来才是正经事；本程序自己的版本提示排在最后。
    /// </summary>
    private void RefreshTip()
    {
        if (!IsEngineInstalled && !_engineTipDismissed)
        {
            Tip = TipKind.EngineMissing;
            return;
        }

        if (IsEngineInstalled && HasEngineUpdate && !_engineUpdateTipDismissed)
        {
            Tip = TipKind.EngineUpdate;
            return;
        }

        Tip = HasAppUpdate && !_appUpdateTipDismissed ? TipKind.AppUpdate : TipKind.None;
    }

    /// <summary>「以后再说」：只记住这一种提示，这次运行里不再提它，别的不受影响。</summary>
    [RelayCommand]
    private void DismissTip()
    {
        switch (Tip)
        {
            case TipKind.EngineMissing:
                _engineTipDismissed = true;
                break;
            case TipKind.EngineUpdate:
                _engineUpdateTipDismissed = true;
                break;
            case TipKind.AppUpdate:
                _appUpdateTipDismissed = true;
                break;
            default:
                return;
        }

        RefreshTip();
    }

    /// <summary>
    /// 卡片上那颗主按钮：不管说的是哪一种，去处都是设置页「网络与更新」那一行，只是滚到眼前让人自己动手。
    /// 搜索词必须清空——留着上一次的过滤条件时那一组可能整组是隐藏的，滚过去只会看到空白。
    /// 跳过去就不再提这张卡了：用户已经到了该去的地方。
    /// </summary>
    [RelayCommand]
    private void TipAction()
    {
        switch (Tip)
        {
            case TipKind.EngineMissing:
                _engineTipDismissed = true;
                break;
            case TipKind.EngineUpdate:
                _engineUpdateTipDismissed = true;
                break;
            case TipKind.AppUpdate:
                _appUpdateTipDismissed = true;
                break;
        }

        SettingsSearch = string.Empty;

        SelectedPage = 4;

        IsNetworkGroupExpanded = true;

        DownloadFocusTick++;

        RefreshTip();
    }

    [ObservableProperty]
    private string _localPushUrl = "rtmp://127.0.0.1:1935/live";

    [ObservableProperty]
    private string _statusDetail = L.T("l002-003");

    // ====== 数据 ======

    public ObservableCollection<TargetItemViewModel> Targets { get; } = new();

    public ObservableCollection<string> Logs { get; } = new();

    /// <summary>日志页那三颗按钮是否可点。面板里一行都没有时，复制与导出没有意义。</summary>
    public bool HasLogs => Logs.Count > 0;

    // ====== 生命周期 ======

    public MainViewModel()
    {
        _cfg = _store.Load();

        // 日志条数一变，三颗按钮的可用状态就得跟着走
        Logs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLogs));

        // 传引用而不是传值：设置页里改了代理，这里拿到的就是新的那一份
        // 调试覆盖必须在起引擎之前设好：引擎路径与工作目录都从这里取。
        // 正常配置下这两行是空操作（EngineExeOverride 为 ""、ArgumentsOverride 为 ""）。
        AppPaths.EngineExeOverride = AppPaths.ResolveEngineExe(_cfg.Debug.EngineExe);

        if (AppPaths.EngineExeOverride.Length > 0 && _cfg.Debug.EngineArgs.Length > 0)
        {
            _engine.ArgumentsOverride = _cfg.Debug.EngineArgs;
        }

        _updater = new UpdateChecker(() => _cfg.Proxy);

        _proxyServer = new RtmpProxyServer(FindTargetById, () => _cfg.Proxy, line => OnEngineLog(line));

        _engineUpdater = new EngineUpdater(() => _cfg.Update.Proxy);

        Strings.Language = _cfg.Ui.Language;

        _engine.LogLine += OnEngineLog;
        _engine.Exited += OnEngineExited;
        Targets.CollectionChanged += OnTargetsChanged;
        Inputs.CollectionChanged += OnInputsChanged;

        LoadInputs();
        LoadTargets();
        RefreshAccounts();

        // 简洁模式下那几条拉流源一装进来就该显示成「被禁用」
        RefreshRuntimeDisabled();

        RefreshPushUrl();

        LoadSortState();
        ApplySortOnStartup();
    }

    /// <summary>窗口显示后由 App 调用，按配置决定是否自动拉起引擎。</summary>
    public async Task StartupAsync()
    {
        // 没装引擎就先在右下角摆一张卡片，别等用户自己去设置页翻
        RefreshTip();

        // 版本探测与更新检查都放到后台，绝不挡住引擎启动
        _ = DetectEngineVersionAsync();

        if (_cfg.Update.CheckAppOnStartup) _ = CheckAppUpdateAsync(force: false);
        if (_cfg.Update.CheckEngineOnStartup) _ = CheckEngineUpdateAsync(force: false);

        // 程序整个目录搬走之后，注册表里那条自启还指着旧路径，这里顺手换掉
        if (_cfg.Ui.StartWithWindows) AutoStart.EnsureRegistered();

        // 日志清理必须趁引擎还没起来：它正握着当天那份在写
        CleanupLogs();

        if (!_cfg.Engine.AutoStart) return;

        await StartEngineAsync();
    }

    /// <summary>
    /// 按设置清掉超过保留期的日志文件。只动 logs 目录里的文件，
    /// 界面日志页那份滚动记录是内存里的，跟这里无关。
    /// </summary>
    private void CleanupLogs()
    {
        int removed = LogCleaner.Clean(_cfg.Ui.LogRetentionDays);

        if (removed <= 0) return;

        Logs.Add(L.T("c005-002").Replace("{n}", removed.ToString(CultureInfo.InvariantCulture)));

        while (Logs.Count > 500) Logs.RemoveAt(0);
    }

    // ====== 版本与更新 ======

    /// <summary>问引擎自己要版本号，界面左下角展示。</summary>
    private async Task DetectEngineVersionAsync()
    {
        string? version = await EngineVersionProbe.ReadAsync(AppPaths.EngineExe);

        OnPropertyChanged(nameof(IsEngineInstalled));

        // 版本探到了说明引擎在，卡片该收了（反过来引擎被删的情况这里也会跟着亮出来）
        RefreshTip();

        if (string.IsNullOrWhiteSpace(version)) return;

        EngineVersion = version;

        // 版本号是后到的，回来了就得按它重算一次「有没有得升级」
        if (_engineChecked) RefreshEngineUpdate();
    }

    /// <summary>查本程序有没有新版本。仓库没配时让状态行说清原因。</summary>
    /// <param name="force">true 表示绕过 24 小时缓存。</param>
    private async Task CheckAppUpdateAsync(bool force)
    {
        if (string.IsNullOrWhiteSpace(_cfg.Update.AppRepo))
        {
            RefreshUpdateStatus();

            return;
        }

        _latestAppTag = await _updater.GetLatestTagAsync(_cfg.Update.AppRepo, _cfg.Update.AppSource, force);
        _appChecked = true;

        HasAppUpdate = IsNewerThan(_latestAppTag, VersionText);

        RefreshUpdateStatus();
    }

    /// <summary>查引擎有没有新版本。</summary>
    /// <param name="force">true 表示绕过 24 小时缓存。</param>
    private async Task CheckEngineUpdateAsync(bool force)
    {
        _latestEngineTag = await _updater.GetLatestTagAsync(_cfg.Update.EngineRepo, _cfg.Update.EngineSource, force);
        _engineChecked = true;

        RefreshEngineUpdate();

        RefreshUpdateStatus();
    }

    /// <summary>
    /// 重算「引擎有没有得装」：远端拿到 tag，并且本机没装或确实更旧。
    /// 引擎没装时本地版本是空的，严格比版本号永远比不出结果，所以这里单独认一次「没装」。
    /// </summary>
    private void RefreshEngineUpdate()
        => HasEngineUpdate = !string.IsNullOrWhiteSpace(_latestEngineTag)
            && (!IsEngineInstalled || IsNewerThan(_latestEngineTag, EngineVersion));

    /// <summary>本程序有没有配更新仓库。直接看配置，不依赖「这次有没有查过」。</summary>
    private bool IsAppRepoMissing => string.IsNullOrWhiteSpace(_cfg.Update.AppRepo);

    /// <summary>把两路检查的结果收口成一句状态文字，谁先回来都不会互相覆盖。</summary>
    private void RefreshUpdateStatus()
    {
        if (IsUpdating) return;

        List<string> notes = [];

        if (HasAppUpdate) notes.Add(FormatNote("l016-008", _latestAppTag, VersionText));

        // 引擎都没装，这时候提「有新版本」是废话，下面单独给一句「去装」
        if (HasEngineUpdate && IsEngineInstalled)
        {
            notes.Add(FormatNote("l016-009", _latestEngineTag, EngineVersion));
        }

        if (notes.Count > 0)
        {
            UpdateStatus = string.Join("  ", notes);
            return;
        }

        // 引擎不在是最要紧的一件事，这时候说「已是最新版本」是彻头彻尾的假话
        if (_engineChecked && HasEngineUpdate)
        {
            UpdateStatus = L.T("l016-017");
            return;
        }

        bool appFails = _appChecked && string.IsNullOrWhiteSpace(_latestAppTag);
        bool engineFails = _engineChecked && string.IsNullOrWhiteSpace(_latestEngineTag);

        // 有一路连版本号都没拿到，多半是网络或源的问题，别骗用户说已是最新
        if (appFails || engineFails)
        {
            UpdateStatus = L.T("l016-013");
            return;
        }

        if (!_appChecked && !_engineChecked)
        {
            UpdateStatus = string.Empty;
            return;
        }

        // 本程序没配更新源时「已是最新」也是没有依据的，得说清是没源可查
        UpdateStatus = IsAppRepoMissing ? L.T("l016-018") : L.T("l016-007");
    }

    /// <summary>把语言包里的 {latest} / {current} 换成真实版本号。</summary>
    private static string FormatNote(string key, string? latest, string? current)
        => L.T(key)
            .Replace("{latest}", latest ?? string.Empty)
            .Replace("{current}", current ?? string.Empty);

    /// <summary>下载进度是在后台线程回调的，写界面属性得切回 UI 线程。</summary>
    private void PostUpdateStatus(string text)
    {
        Action<Action>? post = Post;

        if (post is null)
        {
            UpdateStatus = text;
            return;
        }

        post(() => UpdateStatus = text);
    }

    // ====== 更新命令 ======

    private bool CanCheckUpdates => !IsUpdating;

    /// <summary>手动查一次更新，绕过本地缓存。</summary>
    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    private async Task CheckUpdatesAsync()
    {
        if (IsUpdating) return;

        UpdateStatus = L.T("l016-006");

        await Task.WhenAll(CheckAppUpdateAsync(force: true), CheckEngineUpdateAsync(force: true));

        RefreshUpdateStatus();

        ShowToast(HasAppUpdate || HasEngineUpdate ? L.T("t004-001") : L.T("t004-002"), ToastKind.Info);
    }

    private bool CanInstallEngineUpdate => !IsUpdating && HasEngineUpdate;

    /// <summary>
    /// 下载并装上最新引擎。装之前必须先停引擎——那个 exe 正被占用时写不进去。
    /// 装完按原来的运行状态决定要不要重新拉起来，用户不该因为更新而丢掉转流。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstallEngineUpdate))]
    private async Task InstallEngineUpdateAsync()
    {
        if (IsUpdating) return;

        if (string.IsNullOrWhiteSpace(_latestEngineTag))
        {
            UpdateStatus = L.T("l016-013");
            return;
        }

        string tag = _latestEngineTag;

        bool wasRunning = IsEngineRunning;

        IsUpdating = true;

        try
        {
            if (wasRunning) await StopEngineAsync();

            UpdateStatus = L.T("l016-010");

            EngineUpdater.Package package = await _engineUpdater.DownloadAsync(
                tag,
                _cfg.Update.EngineSource,
                PostUpdateStatus,
                CancellationToken.None);

            string? error = EngineUpdater.Apply(package);

            if (error is not null)
            {
                UpdateStatus = L.T("l016-012") + error;

                ShowToast(L.T("t004-004"), ToastKind.Error);

                return;
            }

            EngineVersion = tag;
            HasEngineUpdate = false;

            OnPropertyChanged(nameof(IsEngineInstalled));

            // 装上了就别再举着那张「没检测到引擎」的卡片了
            RefreshTip();

            UpdateStatus = FormatNote("l016-011", tag, null);

            ShowToast(L.T("t004-003").Replace("{tag}", tag), ToastKind.Success);
        }
        catch (Exception ex)
        {
            UpdateStatus = L.T("l016-012") + EngineUpdater.DescribeError(ex);

            ShowToast(L.T("t004-004"), ToastKind.Error);
        }
        finally
        {
            IsUpdating = false;
        }

        if (wasRunning) await StartEngineAsync();
    }

    /// <summary>远程版本是否比本地新；任一端解析不出来都按「没有新版本」处理。</summary>
    private static bool IsNewerThan(string? latest, string? current)
    {
        if (!SemanticVersion.TryParse(latest, out SemanticVersion remote)) return false;
        if (!SemanticVersion.TryParse(current, out SemanticVersion local)) return false;

        return remote.CompareTo(local) > 0;
    }

    // ====== 导航 ======

    /// <summary>弹出输出目标小窗。</summary>
    [RelayCommand]
    private async Task OpenTargetAsync(TargetItemViewModel? target)
    {
        if (target is null) return;

        await ShowTargetEditorAsync(target, isNew: false);
    }

    /// <summary>
    /// 除自己之外，其它输出目标已经在用的显示名。重名只提示不拦，理由同输入源那边。
    /// </summary>
    private List<string> CollectOtherTargetNames(TargetItemViewModel item)
        => Targets
            .Where(other => !ReferenceEquals(other, item))
            .Select(other => other.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

    /// <summary>弹出输出目标小窗；用户点「添加 / 保存」才写回并落盘。</summary>
    private async Task<bool> ShowTargetEditorAsync(TargetItemViewModel item, bool isNew)
    {
        if (AppHost is null)
        {
            return false;
        }

        // 弹窗期间给主界面盖遮罩，点不到侧栏，也就回不到上级页面
        IsDialogOpen = true;

        bool accepted;

        try
        {
            accepted = await AppHost.ShowTargetEditorAsync(item, isNew, InputChoices, CollectOtherTargetNames(item));
        }
        finally
        {
            IsDialogOpen = false;
        }

        if (!accepted)
        {
            return false;
        }

        if (isNew)
        {
            Targets.Add(item);
            TrackItem(item);

            // 新增的行按当前排序方式插到该在的位置，而不是永远堆在末尾
            ApplyTargetSort();
        }

        _ = ApplyTargetsAsync();
        RefreshOverview();

        ShowToast(L.T(isNew ? "t003-001" : "t003-002").Replace("{name}", item.DisplayName), ToastKind.Success);

        return true;
    }

    /// <summary>退出二级编辑页，回到首页。</summary>
    [RelayCommand]
    private void CloseTarget()
        => EditingTarget = null;

    // ====== 小工具 ======

    /// <summary>清空日志面板。</summary>
    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        ShowToast(L.T("t004-005"), ToastKind.Success);
    }

    /// <summary>把面板里现有的日志整段复制到剪贴板。</summary>
    [RelayCommand]
    private async Task CopyLogsAsync()
    {
        if (Logs.Count == 0 || AppHost is null) return;

        try
        {
            await AppHost.CopyTextAsync(JoinLogs());

            ShowToast(L.T("t004-007").Replace("{n}", CountText()), ToastKind.Info);
        }
        catch (Exception)
        {
            ShowToast(L.T("c011-002"), ToastKind.Error);
        }
    }

    /// <summary>
    /// 把面板里现有的日志存成文件。文件里带一行生成时间，
    /// 因为面板只留最近 500 行，事后翻的时候得知道这是哪一段。
    /// </summary>
    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        if (Logs.Count == 0 || AppHost is null) return;

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        string body = string.Join(Environment.NewLine,
            L.T("l005-003").Replace("{time}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            string.Empty,
            JoinLogs());

        bool ok = await AppHost.SaveTextFileAsync("logs-" + stamp + ".log", body);

        ShowToast(ok
            ? L.T("t004-008").Replace("{n}", CountText())
            : L.T("t004-009"),
            ok ? ToastKind.Success : ToastKind.Error);
    }

    /// <summary>面板里所有日志，一行一条，换行统一成 Environment.NewLine：Windows 记事本打开也正常。</summary>
    private string JoinLogs() => string.Join(Environment.NewLine, Logs);

    /// <summary>「12 行」这种计数文案，数字转成当前区域格式。</summary>
    private string CountText() => Logs.Count.ToString(CultureInfo.InvariantCulture);

    // ====== 列表批量操作 ======

    /// <summary>当前勾选的行数。</summary>
    public int SelectedCount => Targets.Count(item => item.IsSelected);

    /// <summary>是否勾选了至少一行。</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>是否已经全选。</summary>
    public bool IsAllSelected => Targets.Count > 0 && SelectedCount == Targets.Count;

    /// <summary>工具栏右侧的勾选提示。</summary>
    public string SelectionText => SelectedCount == 0 ? L.T("c004-005") : L.T("c004-006") + SelectedCount + L.T("c004-007");

    /// <summary>全选按钮的文案，全选之后就变成取消全选。</summary>
    public string SelectAllText => IsAllSelected ? L.T("c004-004") : L.T("c004-003");

    /// <summary>全选与取消全选的切换。</summary>
    [RelayCommand]
    private void ToggleSelectAll()
    {
        bool selectAll = !IsAllSelected;

        foreach (TargetItemViewModel item in Targets)
        {
            item.IsSelected = selectAll;
        }
    }

    /// <summary>把勾选的转发目标全部启用。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void StartSelected()
    {
        int changed = SetEnabledForSelection(true);

        ShowToast(changed > 0
            ? L.T("t003-005").Replace("{n}", changed.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : L.T("t003-011"),
            changed > 0 ? ToastKind.Success : ToastKind.Info);
    }

    /// <summary>把勾选的转发目标全部停用。</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void StopSelected()
    {
        int changed = SetEnabledForSelection(false);

        ShowToast(changed > 0
            ? L.T("t003-006").Replace("{n}", changed.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : L.T("t003-012"),
            changed > 0 ? ToastKind.Success : ToastKind.Info);
    }

    /// <summary>
    /// 删除勾选的转发目标。批量删的是一片，误触代价大，走模态确认。
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveSelectedAsync()
    {
        List<TargetItemViewModel> doomed = Targets.Where(item => item.IsSelected).ToList();

        if (doomed.Count == 0) return;

        if (AppHost is not null)
        {
            bool ok = await AppHost.ConfirmAsync(
                L.T("t006-003"),
                L.T("t003-014").Replace("{n}", doomed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                L.T("t006-001"),
                destructive: true);

            if (!ok) return;
        }

        foreach (TargetItemViewModel item in doomed)
        {
            item.PropertyChanged -= OnItemChanged;

            if (ReferenceEquals(EditingTarget, item)) EditingTarget = null;

            Targets.Remove(item);
        }

        _ = ApplyTargetsAsync();

        ShowToast(L.T("t003-015").Replace("{n}", doomed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)), ToastKind.Success);
    }

    /// <summary>
    /// 批量设置启用状态，只有真的改动过才写盘。
    /// 返回实际改了几条，好让调用方把结果说给用户听。
    /// </summary>
    private int SetEnabledForSelection(bool enabled)
    {
        int changed = 0;

        foreach (TargetItemViewModel item in Targets)
        {
            if (!item.IsSelected || item.Enabled == enabled) continue;

            item.Enabled = enabled;
            changed++;
        }

        if (changed > 0) _ = ApplyTargetsAsync();

        return changed;
    }

    /// <summary>列表增删后刷新空状态与勾选计数。</summary>
    private void OnTargetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsTargetsEmpty));
        OnPropertyChanged(nameof(IsTargetsVisible));
        OnPropertyChanged(nameof(TargetEnabledBoundary));
        RefreshSelection();

        // 增删目标会改变「哪条输入推到哪里」的对应关系，总览要跟着重算
        RefreshOverview();
    }

    /// <summary>气泡里最多显示这么多字，地址太长就截断，别糊住半个窗口。</summary>
    private const int ToastMaxLength = 44;

    /// <summary>气泡停留多久再开始淡出。</summary>
    private const int ToastHoldMilliseconds = 1200;

    /// <summary>淡出总时长，最后一帧落到全透明。</summary>
    private const int ToastFadeMilliseconds = 300;

    /// <summary>复制一段文字到剪贴板，底部弹个小气泡说清复制的是什么。</summary>
    [RelayCommand]
    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (AppHost is null) return;

        try
        {
            await AppHost.CopyTextAsync(text);

            // 复制是「照着做了，给你看一眼」，不是办成了某件事，走蓝色的通知
            ShowToast(L.T("c011-001").Replace("{value}", text), ToastKind.Info);
        }
        catch (Exception)
        {
            ShowToast(L.T("c011-002"), ToastKind.Error);
        }
    }

    /// <summary>
    /// 弹一条底部气泡。上一条还没散就立刻掐掉，新的一条从「在下面、看不见」重新浮上来。
    /// </summary>
    /// <param name="text">气泡里那行字，太长会被截断。</param>
    /// <param name="kind">
    /// 这句话在告诉用户什么。四种语气各有底色与图标，调用处必须显式传——
    /// 给个默认值看着省事，实际会让人忘记想「这条算成功还是算失败」，于是一片全绿。
    /// </param>
    private void ShowToast(string text, ToastKind kind)
    {
        _toastCts?.Cancel();

        CancellationTokenSource cts = new();
        _toastCts = cts;

        // 先把上一条收干净：文字立刻没了、状态压回「不显示」。
        ToastText = string.Empty;
        ToastShown = false;

        // 必须跨一帧再摆新内容。同一帧里「设 false 又设 true」会被属性系统并成一次变化，
        // 出现动画根本不触发，新气泡就直接定在那儿了——那样又变回「直接出现」。
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!ReferenceEquals(_toastCts, cts) || cts.IsCancellationRequested) return;

                ToastTone = kind;
                ToastText = ShortenForToast(text);

                // 先自增再置显示：计数器负责「同一句话再弹一次也从底部重来」，
                // 置显示负责第一次从无到有。
                ToastSerial++;
                ToastShown = true;
            },
            DispatcherPriority.Render);

        _ = FadeToastAsync(cts);
    }

    /// <summary>
    /// 先停一会儿，再把 <see cref="ToastShown"/> 一步关掉：气泡一边淡出一边沉下去，
    /// 曲线与时长都在 <c>Controls\ToastBar.cs</c> 里逐帧算。
    /// </summary>
    private async Task FadeToastAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(ToastHoldMilliseconds, cts.Token);

            ToastShown = false;

            // 等动画走完再把文字清掉，否则气泡会在淡出半途先「变空」
            await Task.Delay(ToastFadeMilliseconds, cts.Token);

            ToastText = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // 被新的一条顶掉了，当前气泡归它管，这里什么都不用做
        }
        finally
        {
            if (ReferenceEquals(_toastCts, cts)) _toastCts = null;

            cts.Dispose();
        }
    }

    /// <summary>气泡里的字太长就截一段，末尾用省略号表示还有。</summary>
    private static string ShortenForToast(string text)
        => text.Length <= ToastMaxLength ? text : text[..ToastMaxLength] + "…";
    /// <summary>在资源管理器里打开配置目录、日志目录或引擎目录。</summary>
    [RelayCommand]
    private void OpenFolder(string? kind)
    {
        AppPaths.EnsureCreated();

        // 引擎目录要等装引擎时才建，没装的时候点这里得先把目录建出来再打开
        if (kind == "engine") Directory.CreateDirectory(AppPaths.MediaMTXDir);

        string path = kind switch
        {
            "engine" => AppPaths.MediaMTXDir,
            "logs" => AppPaths.LogsDir,
            "record" => RecordRootDirectory(),
            _ => AppPaths.ConfigDir,
        };

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + path + "\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // 打不开资源管理器不影响主流程，但得说一声，否则点了没反应像坏了
            ShowToast(L.T("t004-006") + ex.Message, ToastKind.Error);
        }
    }

    // ====== 引擎 ======

    [RelayCommand]
    private async Task ToggleEngineAsync()
    {
        if (IsEngineRunning) await StopEngineAsync();
        else await StartEngineAsync();
    }

    private async Task StartEngineAsync()
    {
        EngineStatus = L.T("c002-004");
        StatusDetail = L.T("c002-005");

        // 先把中转站开起来：runtime.yml 里的本地地址要带真实端口
        _proxyServer.Start();

        WriteRuntimeYml();

        if (!_engine.Start())
        {
            EngineStatus = L.T("c002-010");
            StatusDetail = L.T("l002-002");
            ShowToast(L.T("t001-003"), ToastKind.Error);

            return;
        }

        _api ??= new ControlApi(_cfg.Engine.ApiPort);

        bool ready = await _api.WaitReadyAsync(TimeSpan.FromSeconds(15), CancellationToken.None);

        if (!ready)
        {
            EngineStatus = L.T("c002-009");
            StatusDetail = L.T("l002-001");
            ShowToast(L.T("t001-003"), ToastKind.Error);

            return;
        }

        IsEngineRunning = true;
        EngineStatus = L.T("c002-006");
        RefreshStatusDetail();
        ShowToast(L.T("t001-001"), ToastKind.Success);

        _metricsCts?.Cancel();
        _metricsCts = new CancellationTokenSource();
        _ = PollMetricsAsync(_metricsCts.Token);
        _ = PollResponseAsync(_metricsCts.Token);
    }

    private async Task StopEngineAsync()
    {
        EngineStatus = L.T("c002-007");

        // 用户主动停：把还挂在退避里的重起取消掉，否则它会几秒后自己爬起来
        _restartAttempts = 0;

        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = null;

        await _engine.StopAsync();

        // 引擎没了，中转站也没有存在的意义，别留着端口
        _proxyServer.Stop();

        IsEngineRunning = false;
        EngineStatus = L.T("c002-008");
        StatusDetail = L.T("l002-004");

        _metricsCts?.Cancel();
        _metricsCts = null;
        ClearRuntimeMetrics();

        ShowToast(L.T("t001-002"), ToastKind.Info);
    }

    // ====== 引擎意外退出与自动重起 ======

    /// <summary>
    /// 引擎进程没了。这里只判断「要不要自己把它拉回来」，不处理停止——
    /// EngineManager 已经把「主动停」滤掉了，走到这儿的都是意外。
    /// 回调在线程池线程上，所有界面状态改写都要 Post 回 UI 线程。
    /// </summary>
    private void OnEngineExited(int? exitCode)
    {
        Post ??= action => action();

        Post(() =>
        {
            // 用户关掉了自动重起：如实报状态，别偷偷拉起
            if (!_cfg.Engine.AutoRestart)
            {
                IsEngineRunning = false;
                EngineStatus = L.T("c002-008");
                StatusDetail = L.T("l002-004");

                return;
            }

            IsEngineRunning = false;
            EngineStatus = L.T("c002-012");
            StatusDetail = L.T("l002-005");

            _ = RestartEngineAsync();
        });
    }

    /// <summary>
    /// 按退避重起引擎：1s、2s、4s、8s、16s，封顶 30s。
    /// 连续 <see cref="MaxRestartAttempts"/> 次起不来就放弃——端口被占、exe 被删这类问题
    /// 再多试几次也不会好，只会刷屏烧 CPU。
    /// </summary>
    private async Task RestartEngineAsync()
    {
        _restartCts?.Cancel();
        _restartCts?.Dispose();

        CancellationTokenSource cts = new();

        _restartCts = cts;

        try
        {
            while (_restartAttempts < MaxRestartAttempts)
            {
                _restartAttempts++;

                // 退避：1、2、4、8、16 秒，最久等 30 秒
                int delaySeconds = Math.Min(1 << (_restartAttempts - 1), 30);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cts.Token);

                // 等待期间用户可能已经手动停掉，或者程序正在退出
                if (cts.IsCancellationRequested) return;

                // 本方法由 OnEngineExited 在 UI 线程上启动，await 之后仍回到 UI 线程，
                // 所以这里直接调用即可，不需要再 Post（那样会变成 async void，拿不到结果）
                await StartEngineAsync();

                if (cts.IsCancellationRequested) return;

                if (IsEngineRunning)
                {
                    _restartAttempts = 0;

                    return;
                }

                if (_restartAttempts >= MaxRestartAttempts)
                {
                    // 已经在 UI 线程上（本方法由 OnEngineExited 在 UI 线程启动），直接写
                    EngineStatus = L.T("c002-011");
                    StatusDetail = L.T("l002-006");
                    ShowToast(L.T("t001-004"), ToastKind.Error);

                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 被取消（用户停止 / 程序退出），什么都不用做
        }
    }

    /// <summary>运行中时的副标题，只讲转发目标数量，推流地址在下一行单独展示。</summary>
    private void RefreshStatusDetail()
    {
        if (!IsEngineRunning) return;

        int enabled = Targets.Count(item => item.Enabled);

        StatusDetail = enabled == 0
            ? L.T("l002-007")
            : L.T("c002-019") + enabled + L.T("c002-020");
    }

    // ====== 指标轮询 ======

    /// <summary>每两秒抓一次引擎指标，刷新每行的状态与速度。</summary>
    private async Task PollMetricsAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await RefreshMetricsAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 引擎停下来了，轮询正常结束
        }
    }

    private async Task RefreshMetricsAsync(CancellationToken ct)
    {
        if (_api is null || !IsEngineRunning) return;

        string? text = await _api.GetMetricsAsync(ct, _cfg.Engine.MetricsPort);

        if (text is null) return;

        DateTime now = DateTime.UtcNow;

        // 每条输入源在引擎里是一条独立路径，各有自己的 forward 数组与 pos 编号，
        // 所以先按路径名分组、再在组内按序号回填。跨路径按 pos 取下标，会把目标串到别人身上。
        Dictionary<string, List<ForwardDestMetrics>> byPath = GroupByPath(
            MetricsParser.ParseForwardDests(text));

        foreach (InputItemViewModel input in Inputs)
        {
            List<TargetItemViewModel> rows = ForwardRowsOf(input);

            byPath.TryGetValue(input.Path, out List<ForwardDestMetrics>? found);

            ApplyForwardMetrics(rows, found, now);

            await FillForwardErrorsAsync(input, rows, ct);
        }

        await RefreshPathsAsync(now, ct);

        await RefreshRecordSizesAsync();

        RefreshStats();
    }

    /// <summary>
    /// 把每条输入录制目录的大小刷到列表上。扫盘是磁盘 IO，按十秒一次就够了，
    /// 节流用轮次计数（指标两秒一轮），不另起计时器。
    /// </summary>
    private async Task RefreshRecordSizesAsync()
    {
        List<InputItemViewModel> rows = [];

        foreach (InputItemViewModel input in Inputs)
        {
            // 简洁模式根本不显示这一列，录制没开的行也没什么可算的
            if (!IsAdvancedMode || !input.Record)
            {
                input.RecordSizeText = "—";
                continue;
            }

            rows.Add(input);
        }

        if (rows.Count == 0) return;

        _recordSizeTick++;

        if (_recordSizeTick % 5 != 0) return;

        // 目录里可能躺着成千上万个分段，别在 UI 线程上数
        Dictionary<string, long> sizes = await Task.Run(() =>
        {
            Dictionary<string, long> result = new(StringComparer.Ordinal);

            foreach (InputItemViewModel input in rows)
            {
                result[input.Id] = DirectorySize(RecordDirectoryOf(input.Path));
            }

            return result;
        });

        foreach (InputItemViewModel input in rows)
        {
            long bytes = sizes.TryGetValue(input.Id, out long value) ? value : 0;

            input.RecordSizeText = FormatBytes(bytes);
        }
    }

    /// <summary>录制目录的根：模板里 %path 之前那段。目录页那一行显示的就是它。</summary>
    private string RecordRootDirectory()
        => RecordPaths.Root(_cfg.Record?.PathTemplate ?? string.Empty);

    /// <summary>按录制模板推出某条 path 的录制目录。</summary>
    private string RecordDirectoryOf(string path)
        => RecordPaths.ForPath(_cfg.Record?.PathTemplate ?? string.Empty, path);

    /// <summary>递归数一个目录下所有文件加起来多大。读不动的目录按 0 算，别让一颗坏目录毁掉整列。</summary>
    private static long DirectorySize(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return 0;

        long total = 0;

        try
        {
            foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                total += file.Length;
            }
        }
        catch (IOException)
        {
            return total;
        }
        catch (UnauthorizedAccessException)
        {
            return total;
        }

        return total;
    }

    /// <summary>把指标按所属路径名分组。引擎没给 path 标签的那些行归到空串键下，不会被任何路径认领。</summary>
    private static Dictionary<string, List<ForwardDestMetrics>> GroupByPath(
        IReadOnlyList<ForwardDestMetrics> metrics)
    {
        Dictionary<string, List<ForwardDestMetrics>> result = new(StringComparer.Ordinal);

        foreach (ForwardDestMetrics dest in metrics)
        {
            if (!result.TryGetValue(dest.Path, out List<ForwardDestMetrics>? list))
            {
                list = [];
                result[dest.Path] = list;
            }

            list.Add(dest);
        }

        return result;
    }

    /// <summary>
    /// 某条输入源在引擎 forward 数组里的顺序。
    /// 条件与 <see cref="RuntimeYmlBuilder"/> 写 runtime.yml 时完全一致（启用、绑定到这条输入、地址非空），
    /// 少一条这里的序号就整体错位，状态会显示到别的目标头上。
    /// </summary>
    private List<TargetItemViewModel> ForwardRowsOf(InputItemViewModel input)
    {
        List<TargetItemViewModel> rows = [];

        foreach (TargetItemViewModel item in Targets)
        {
            if (!item.Enabled) continue;
            if (!string.Equals(item.InputSourceId, input.Id, StringComparison.Ordinal)) continue;
            if (ResolveDest(item.ToModel()).Length == 0) continue;

            rows.Add(item);
        }

        return rows;
    }

    /// <summary>把一条路径的指标按序号贴回界面上的行，本轮没对上的清空。</summary>
    private void ApplyForwardMetrics(
        List<TargetItemViewModel> rows,
        List<ForwardDestMetrics>? metrics,
        DateTime now)
    {
        HashSet<TargetItemViewModel> filled = [];

        if (metrics is not null)
        {
            foreach (ForwardDestMetrics dest in metrics)
            {
                if (dest.Position < 1 || dest.Position > rows.Count) continue;

                TargetItemViewModel item = rows[dest.Position - 1];

                item.RuntimeState = dest.State;
                item.SpeedText = FormatSpeed(item.Id, dest.OutboundBytes, now, bps => _targetBps[item.Id] = bps);
                item.SentText = FormatBytes(dest.OutboundBytes);

                // 这一轮不报错了，上一轮留下的原因就得撤掉，别让红字一直挂着
                if (!item.IsErrored) item.ErrorText = string.Empty;

                filled.Add(item);
            }
        }

        // 引擎还没为这些目标建出指标时，清掉上一轮的残留显示
        foreach (TargetItemViewModel item in rows)
        {
            if (filled.Contains(item)) continue;

            ClearForwardRuntime(item);

            // 这一路本轮没有指标，汇总里不能还留着上一轮的旧数
            _targetBps.Remove(item.Id);
        }
    }

    /// <summary>
    /// 目标失败时去 API 取那句话。
    /// metrics 里只有 state="error" 这个枚举，为什么失败只存在 API 的 lastError 字段里，
    /// 所以只有真有目标报错时才多问一次，正常运转时一条请求都不加。
    /// </summary>
    private async Task FillForwardErrorsAsync(
        InputItemViewModel input,
        List<TargetItemViewModel> rows,
        CancellationToken ct)
    {
        if (_api is null) return;
        if (!rows.Any(item => item.IsErrored)) return;

        IReadOnlyList<ForwardDestInfo> dests = await _api.GetForwardDestsAsync(input.Path, ct);

        foreach (ForwardDestInfo dest in dests)
        {
            if (dest.Position < 1 || dest.Position > rows.Count) continue;

            TargetItemViewModel item = rows[dest.Position - 1];

            if (!item.IsErrored) continue;

            item.ErrorText = dest.LastError?.Trim() ?? string.Empty;
        }
    }

    /// <summary>刷新每条输入源在引擎侧的在线状态、编码与读端数量。</summary>
    private async Task RefreshPathsAsync(DateTime now, CancellationToken ct)
    {
        if (_api is null) return;

        IReadOnlyList<PathInfo> paths = await _api.GetPathsAsync(ct);

        Dictionary<string, PathInfo> byName = new(StringComparer.Ordinal);

        foreach (PathInfo path in paths)
        {
            byName[path.Name] = path;
        }

        foreach (InputItemViewModel input in Inputs)
        {
            byName.TryGetValue(input.Path, out PathInfo? info);

            input.IsOnline = info is { Online: true };
            input.RuntimeText = DescribePath(input, info, now);
        }
    }

    /// <summary>总览页第二行的说明：在线时给编码、入流码率与读端数量，其余情况给一句状态。</summary>
    private string DescribePath(InputItemViewModel input, PathInfo? info, DateTime now)
    {
        // 停用的输入源引擎里根本没有这条路径，说什么都是废话
        if (!input.Enabled)
        {
            input.InboundSpeedText = "—";
            return string.Empty;
        }

        if (info is null || !info.Online)
        {
            input.InboundSpeedText = "—";
            return L.T("c002-017");
        }

        List<string> parts = [];

        string codecs = CodecText(info.Tracks);

        if (codecs.Length > 0) parts.Add(codecs);

        string speed = FormatInboundSpeed(input.Id, info.InboundBytes, now);

        // 同一份差分结果两处用：总览那句说明里带一份，列表的「流速率」列单独显示
        input.InboundSpeedText = speed.Length > 0 ? speed : "—";

        if (speed.Length > 0) parts.Add(speed);

        int readers = info.Readers?.Count ?? 0;

        parts.Add(L.T("c002-018").Replace("{n}", readers.ToString()));

        return string.Join(" · ", parts);
    }

    /// <summary>把轨道列表压成一句编码说明，例如「H264 + AAC」。</summary>
    private static string CodecText(List<PathTrackInfo>? tracks)
    {
        if (tracks is null || tracks.Count == 0) return string.Empty;

        List<string> codecs = [];

        foreach (PathTrackInfo track in tracks)
        {
            string codec = track.Codec?.Trim() ?? string.Empty;

            if (codec.Length == 0) continue;
            if (codecs.Contains(codec)) continue;

            codecs.Add(codec);
        }

        return string.Join(" + ", codecs);
    }

    /// <summary>入流码率。差分的第一轮没有参照，返回空串让调用方干脆不写这一段。</summary>
    private string FormatInboundSpeed(string inputId, long bytes, DateTime at)
    {
        // 前缀把入流与转发的采样分开：两边都按 Id 记账，不加前缀会互相覆盖
        string text = FormatSpeed("in:" + inputId, bytes, at, bps => _inboundBps[inputId] = bps);

        return text == "—" ? string.Empty : text;
    }

    /// <summary>清掉一行的运行时显示，配置字段一个都不碰。</summary>
    private static void ClearForwardRuntime(TargetItemViewModel item)
    {
        item.RuntimeState = string.Empty;
        item.SpeedText = "—";
        item.SentText = "—";
        item.ErrorText = string.Empty;
    }

    /// <summary>累计字节数转成人看的大小。</summary>
    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

        if (bytes < 1024) return bytes + " B";

        double kb = bytes / 1024.0;

        if (kb < 1024) return kb.ToString("0.0") + " KB";

        double mb = kb / 1024.0;

        if (mb < 1024) return mb.ToString("0.0") + " MB";

        return (mb / 1024.0).ToString("0.00") + " GB";
    }

    /// <summary>
    /// 用累计字节数的差分算实时码率。
    /// <paramref name="onBps"/> 把这一轮的比特率交给调用方，统计页的合计靠它累加；
    /// 第一轮没有参照数据，不回调。</summary>
    /// </summary>
    private string FormatSpeed(string id, long bytes, DateTime at, Action<double>? onBps = null)
    {
        if (!_lastSamples.TryGetValue(id, out (long Bytes, DateTime At) last))
        {
            _lastSamples[id] = (bytes, at);

            return "—";
        }

        double seconds = (at - last.At).TotalSeconds;
        _lastSamples[id] = (bytes, at);

        if (seconds <= 0) return "—";

        // 差分得到的是字节每秒，乘 8 才是比特每秒
        double bps = (bytes - last.Bytes) * 8.0 / seconds;

        onBps?.Invoke(bps);

        return FormatBps(bps);
    }

    /// <summary>比特率转成人看的文字。0 也要显示，别让用户以为没在跑。</summary>
    private static string FormatBps(double bps)
    {
        if (bps < 1) return "0 kbps";

        double kbps = bps / 1000.0;

        return kbps < 1000
            ? kbps.ToString("0.0") + " kbps"
            : (kbps / 1000.0).ToString("0.00") + " Mbps";
    }

    // ====== 统计页 ======

    /// <summary>所有启用目标当前出网码率之和。没有采样时是「—」。</summary>
    public string TotalOutboundText
    {
        get
        {
            double sum = 0;
            int count = 0;

            foreach (TargetItemViewModel item in Targets)
            {
                if (!item.Enabled) continue;
                if (!_targetBps.TryGetValue(item.Id, out double bps)) continue;

                sum += bps;
                count++;
            }

            return count == 0 ? "—" : FormatBps(sum);
        }
    }

    /// <summary>所有在线输入源的入流码率之和。</summary>
    public string TotalInboundText
    {
        get
        {
            double sum = 0;
            int count = 0;

            foreach (InputItemViewModel input in Inputs)
            {
                if (!input.Enabled) continue;
                if (!_inboundBps.TryGetValue(input.Id, out double bps)) continue;

                sum += bps;
                count++;
            }

            return count == 0 ? "—" : FormatBps(sum);
        }
    }

    /// <summary>当前在转的目标条数，给统计页当一句话摘要。</summary>
    public string TargetSummaryText
    {
        get
        {
            int enabled = Targets.Count(item => item.Enabled);
            int reporting = _targetBps.Count;

            return L.T("l002-008")
                .Replace("{on}", enabled.ToString())
                .Replace("{live}", reporting.ToString());
        }
    }

    /// <summary>统计页刷新时把三个汇总一起通知一遍。</summary>
    private void RefreshStats()
    {
        OnPropertyChanged(nameof(TotalOutboundText));
        OnPropertyChanged(nameof(TotalInboundText));
        OnPropertyChanged(nameof(TargetSummaryText));
    }

    private void ClearRuntimeMetrics()
    {
        _lastSamples.Clear();
        _targetBps.Clear();
        _inboundBps.Clear();

        RefreshStats();

        foreach (TargetItemViewModel item in Targets)
        {
            item.RuntimeState = string.Empty;
            item.SpeedText = "—";
            item.ResponseText = "—";
            item.SentText = "—";
            item.ErrorText = string.Empty;
        }

        foreach (InputItemViewModel input in Inputs)
        {
            input.IsOnline = false;
            input.RuntimeText = string.Empty;
        }
    }

    /// <summary>每隔几秒量一次到目标服务器的 TCP 建连耗时，这条路通不通一眼能看出来。</summary>
    private async Task PollResponseAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(10));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await RefreshResponseAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 引擎停了，正常退出
        }
    }

    private async Task RefreshResponseAsync(CancellationToken ct)
    {
        foreach (TargetItemViewModel item in Targets.Where(item => item.Enabled).ToList())
        {
            item.ResponseText = await ProbeResponseAsync(item.Server, ct);
        }
    }

    private static async Task<string> ProbeResponseAsync(string server, CancellationToken ct)
    {
        if (!TryGetEndpoint(server, out string host, out int port)) return "—";

        using System.Net.Sockets.TcpClient client = new();

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await client.ConnectAsync(host, port, timeout.Token);

            watch.Stop();

            return watch.ElapsedMilliseconds + " ms";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return "—";
        }
        catch
        {
            return L.T("c002-013");
        }
    }

    /// <summary>从推流地址里取出主机与端口，没写端口就按协议默认值补。</summary>
    private static bool TryGetEndpoint(string server, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(server)) return false;

        if (!Uri.TryCreate(server, UriKind.Absolute, out Uri? uri)) return false;

        if (string.IsNullOrWhiteSpace(uri.Host)) return false;

        host = uri.Host;
        port = uri.Port > 0 ? uri.Port : DefaultPortFor(uri.Scheme);

        return port > 0;
    }

    private static int DefaultPortFor(string scheme) => scheme.ToLowerInvariant() switch
    {
        "rtmp"  => 1935,
        "rtmps" => 443,
        "rtsp"  => 554,
        "rtsps" => 322,
        "srt"   => 9000,
        _       => 1935,
    };

    // ====== 转发目标 ======

    /// <summary>
    /// 新增输出目标时的默认备注：新建输出目标、新建输出目标2、新建输出目标3…
    /// 只有撞上已有的名字才带数字，第一条不带。
    /// </summary>
    private string NextTargetName()
    {
        HashSet<string> used = new(
            Targets.Select(item => item.Name).Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.Ordinal);

        string baseName = L.T("c004-047");

        if (!used.Contains(baseName)) return baseName;

        for (int i = 2; ; i++)
        {
            string candidate = baseName + i.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!used.Contains(candidate)) return candidate;
        }
    }

    [RelayCommand]
    private async Task AddTargetAsync()
    {
        TargetItemViewModel item = new()
        {
            Name = NextTargetName(),
            InputSourceId = Inputs.FirstOrDefault()?.Id ?? "",
        };

        await ShowTargetEditorAsync(item, isNew: true);
    }

    [RelayCommand]
    private void RemoveTarget(TargetItemViewModel? item)
    {
        if (item == null) return;

        item.PropertyChanged -= OnItemChanged;

        if (ReferenceEquals(EditingTarget, item)) EditingTarget = null;

        Targets.Remove(item);

        _ = ApplyTargetsAsync();

        ShowToast(L.T("t003-009").Replace("{name}", item.DisplayName), ToastKind.Success);
    }

    /// <summary>单条目标的启用/停用开关，表格里的启停按钮直接调它。</summary>
    [RelayCommand]
    private void ToggleTargetEnabled(TargetItemViewModel? item)
    {
        if (item == null) return;

        item.Enabled = !item.Enabled;

        ShowToast(L.T(item.Enabled ? "t003-003" : "t003-004").Replace("{name}", item.DisplayName), ToastKind.Success);
    }

    /// <summary>把界面上的列表落盘，并热更新到正在运行的引擎。</summary>
    public async Task ApplyTargetsAsync()
    {
        // 改完来源方式 / 换了绑定的输入源，都可能改变「谁被程序禁用」，先刷一遍再落盘
        RefreshRuntimeDisabled();

        _cfg.Inputs = Inputs.Select(i => i.ToModel()).ToList();
        _cfg.Targets = Targets.Select(t => t.ToModel()).ToList();
        _store.Save(_cfg);
        WriteRuntimeYml();
        SyncDefaultProxyMode();

        if (!IsEngineRunning || _api == null) return;

        bool ok = true;
        int total = 0;

        // 每条启用的输入源都是独立的 path，只推绑定到它自己的那些目标。
        // 不能把全部目标推给每条 path——那样每条 path 都会转发所有平台的流，
        // 与 runtime.yml 里按 InputSourceId 分的组对不上。
        foreach (InputSource input in _cfg.Inputs.Where(i => i.Enabled))
        {
            List<string> dests = _cfg.Targets
                .Where(t => t.Enabled && string.Equals(t.InputSourceId, input.Id, StringComparison.Ordinal))
                .Select(ResolveDest)
                .Where(dest => dest.Length > 0)
                .ToList();

            total += dests.Count;

            if (!await _api.PatchForwardAsync(input.Path, dests, CancellationToken.None)) ok = false;
        }

        StatusDetail = ok
            ? L.T("c002-021") + total + L.T("c002-020")
            : L.T("l002-009");
    }

    // ====== 变更防抖 ======

    /// <summary>编辑框每敲一个字都会触发，攒 400 毫秒再统一落盘与推送。</summary>
    private void ScheduleApply()
    {
        _applyDebounce?.Cancel();
        _applyDebounce?.Dispose();

        _applyDebounce = new CancellationTokenSource();
        CancellationTokenSource cts = _applyDebounce;

        _ = ApplyAfterDelayAsync(cts);
    }

    private async Task ApplyAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(400, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!cts.IsCancellationRequested) await ApplyTargetsAsync();
    }

    // ====== 内部 ======

    private void LoadTargets()
    {
        Targets.Clear();

        foreach (ForwardTarget model in _cfg.Targets)
        {
            TargetItemViewModel item = TargetItemViewModel.FromModel(model);

            Targets.Add(item);
            TrackItem(item);
        }

        RefreshOverview();
        SyncDefaultProxyMode();
    }

    /// <summary>
    /// 按当前工作模式刷新「被程序禁用」标记。
    /// 简洁模式不读拉流源：主动拉流的那几条整条不写进 runtime.yml，
    /// 绑定它们的输出目标自然也没有流可转，一起标灰。
    /// 这里只改界面上的推导标记，配置文件一个字段都不动，切回高级模式即恢复。
    /// </summary>
    private void RefreshRuntimeDisabled()
    {
        bool simpleMode = !IsAdvancedMode;

        foreach (InputItemViewModel input in Inputs)
        {
            input.RuntimeDisabled = simpleMode && input.IsPulling;
        }

        foreach (TargetItemViewModel target in Targets)
        {
            InputItemViewModel? owner = Inputs.FirstOrDefault(
                candidate => string.Equals(candidate.Id, target.InputSourceId, StringComparison.Ordinal));

            target.RuntimeDisabled = owner is { RuntimeDisabled: true };
        }

        // 禁用项要沉到分组下面去，边界和顺序都得跟着重算
        ApplyInputSort();
        ApplyTargetSort();

        OnPropertyChanged(nameof(InputEnabledBoundary));
        OnPropertyChanged(nameof(TargetEnabledBoundary));
    }

    /// <summary>
    /// 把设置页当前那一档默认代理抄到每个目标行上，列表里「默认：X」才写得对。
    /// 这是回填不是用户操作，OnItemChanged 里已经把它排除在落盘之外。
    /// </summary>
    private void SyncDefaultProxyMode()
    {
        string mode = _cfg.Proxy?.Mode ?? ProxyOptions.System;

        foreach (TargetItemViewModel item in Targets)
        {
            if (item.DefaultProxyMode == mode) continue;

            item.DefaultProxyMode = mode;
        }
    }

    private void TrackItem(TargetItemViewModel item)
    {
        item.PropertyChanged -= OnItemChanged;
        item.PropertyChanged += OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetItemViewModel.IsSelected))
        {
            RefreshSelection();
            return;
        }

        // 轮询回填的展示字段不算配置变更，否则每两秒就要写一次盘、推一遍 forward
        if (RuntimeProperties.Contains(e.PropertyName ?? string.Empty)) return;

        // 改名、切平台、启停都可能改变这一行该待的位置
        ApplyTargetSort();
        OnPropertyChanged(nameof(TargetEnabledBoundary));
        ScheduleApply();
    }

    /// <summary>勾选状态变化后刷新计数与批量按钮的可用性。</summary>
    private void RefreshSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(SelectAllText));
        OnPropertyChanged(nameof(IsAllSelected));

        StartSelectedCommand.NotifyCanExecuteChanged();
        StopSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    private void RefreshPushUrl()
    {
        foreach (InputItemViewModel input in Inputs)
        {
        }

        OnPropertyChanged(nameof(IngestHint));
        OnPropertyChanged(nameof(IngestHintVisible));
    }

    /// <summary>按协议与路径名拼出推流地址；端口取设置页里配的那个。</summary>
    public string BuildPushUrl(string? protocol, string? path)
    {
        string name = string.IsNullOrWhiteSpace(path) ? RuntimeYmlBuilder.PathName : path;

        switch (protocol)
        {
            case "srt":
                return "srt://127.0.0.1:" + _cfg.Protocols.SrtPort + "?streamid=publish:" + name;

            case "rtsp":
                return "rtsp://127.0.0.1:" + _cfg.Protocols.RtspPort + "/" + name;

            default:
                return "rtmp://127.0.0.1:" + _cfg.Engine.RtmpPort + "/" + name;
        }
    }

    /// <summary>所选入口协议还没在设置页打开时给出的提示，正常时为空串。</summary>
    public string IngestHint
        => _cfg.Engine.Ingest switch
        {
            "srt" when !_cfg.Protocols.Srt => L.T("l002-010"),
            "rtsp" when !_cfg.Protocols.Rtsp => L.T("l002-011"),
            _ => "",
        };

    public bool IngestHintVisible
        => IngestHint.Length > 0;

    /// <summary>
    /// 把一条目标折算成写进 forward 的地址。
    ///
    /// 需要走代理的目标不直接给平台地址，改成指向本机中转站：
    /// MediaMTX 自己不会用代理，只有让它连本地、由我们把数据搬出去，代理才生效。
    /// 直连的目标保持原样，少绕一圈就少一个出错的地方。
    /// </summary>
    /// <param name="target">目标。</param>
    private string ResolveDest(ForwardTarget target)
    {
        string dest = target.BuildDest();

        if (dest.Length == 0) return string.Empty;

        if (!NeedsProxy(target)) return dest;

        // 中转站只会说 RTMP：别的协议（SRT、RTSP、WebRTC、MoQ）改成本机地址也搬不动，
        // 那样只会让这条目标彻底推不出去，不如原样直连，至少还能用。
        if (!IsRtmpDest(dest)) return dest;

        // 中转站没开就没法绕，宁可直连也别让引擎连一个不存在的端口
        if (_proxyServer.Port <= 0) return dest;

        return RtmpProxyServer.BuildLocalDest(_proxyServer.Port, target.Id);
    }

    /// <summary>这条目标要不要经过本机中转站。</summary>
    /// <param name="target">目标。</param>
    private bool NeedsProxy(ForwardTarget target)
    {
        // 和 RtmpProxyServer 用同一套解析，否则「这条要不要进中转站」与
        // 「中转站里到底拨哪条代理」会各说各话

        ProxyOptions? proxy = RtmpProxyServer.ResolveTargetProxy(target, _cfg.Proxy);

        if (proxy is null) return false;

        return ProxyDialer.Resolve(proxy).Kind != ProxyKind.Direct;
    }

    /// <summary>这条地址是不是 RTMP 系（中转站只认这两种协议）。</summary>
    /// <param name="dest">拼好的推流地址。</param>
    private static bool IsRtmpDest(string dest)
    {
        return dest.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase)
            || dest.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 有目标选了走代理、协议却不是 RTMP 时，往日志里说一声。
    ///
    /// 每次都刷一遍太吵，只在「这批名字跟上次不一样」时说。
    /// </summary>
    private void ReportUnproxiedTargets()
    {
        List<string> names = [];

        foreach (ForwardTarget target in _cfg.Targets)
        {
            if (!target.Enabled) continue;

            if (!NeedsProxy(target)) continue;

            if (IsRtmpDest(target.BuildDest())) continue;

            names.Add(target.DisplayName);
        }

        string signature = string.Join("、", names);

        if (signature == _proxySkipNotified) return;

        _proxySkipNotified = signature;

        if (names.Count == 0) return;

        OnEngineLog(L.T("l016-015").Replace("{names}", signature));
    }

    /// <summary>中转站按 Id 找回目标配置。</summary>
    /// <param name="id">目标 Id。</param>
    private ForwardTarget? FindTargetById(string id)
    {
        return _cfg.Targets.FirstOrDefault(target => string.Equals(target.Id, id, StringComparison.Ordinal));
    }

    private void WriteRuntimeYml()
    {
        AppPaths.EnsureCreated();

        ReportUnproxiedTargets();

        File.WriteAllText(AppPaths.RuntimeYml, RuntimeYmlBuilder.Build(_cfg, ResolveDest));
    }

    private void OnEngineLog(string line)
    {
        Action<Action>? post = Post;

        if (post is null) return;

        post(() =>
        {
            Logs.Add(line);

            while (Logs.Count > 500) Logs.RemoveAt(0);
        });
    }

    // ====== 释放 ======

    public void Dispose()
    {
        _metricsCts?.Cancel();
        _metricsCts?.Dispose();
        _metricsCts = null;

        _applyDebounce?.Cancel();
        _applyDebounce?.Dispose();
        _applyDebounce = null;

        foreach (TargetItemViewModel item in Targets)
        {
            item.PropertyChanged -= OnItemChanged;
        }

        _engine.LogLine -= OnEngineLog;
        _engine.Exited -= OnEngineExited;

        _toastCts?.Cancel();
        _toastCts = null;

        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = null;

        _engine.Dispose();
        _api?.Dispose();
        _proxyServer.Dispose();
    }
}
