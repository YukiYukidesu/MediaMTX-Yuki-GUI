using CommunityToolkit.Mvvm.ComponentModel;
using MediaMTX_Yuki_GUI.Controls;
using MediaMTX_Yuki_GUI.Models;
using MediaMTX_Yuki_GUI.Services;

namespace MediaMTX_Yuki_GUI.ViewModels;


using L = MediaMTX_Yuki_GUI.Services.Localization;
/// <summary>转发列表里的一行，双向映射到 <see cref="ForwardTarget"/>。</summary>
public sealed partial class TargetItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(Preset))]
    [NotifyPropertyChangedFor(nameof(PlatformText))]
    private string _platform = PresetCatalog.CustomId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Protocol))]
    [NotifyPropertyChangedFor(nameof(ProtocolVariant))]
    private string _server = string.Empty;

    [ObservableProperty]
    private string _streamKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveEnabled))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateTip))]
    [NotifyPropertyChangedFor(nameof(StatusVariant))]
    [NotifyPropertyChangedFor(nameof(IsStreaming))]
    [NotifyPropertyChangedFor(nameof(IsErrored))]
    private bool _enabled = true;

    /// <summary>
    /// 程序强制不用这一条，目前只有它绑定的输入源在简洁模式下被禁用这一种。
    /// 推导出来的运行时标记，不进配置文件。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveEnabled))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateTip))]
    [NotifyPropertyChangedFor(nameof(StatusVariant))]
    [NotifyPropertyChangedFor(nameof(IsStreaming))]
    [NotifyPropertyChangedFor(nameof(IsErrored))]
    private bool _runtimeDisabled;

    /// <summary>绑定的输入源 Id，空串表示还没指定。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputSourceName))]
    private string _inputSourceId = string.Empty;

    /// <summary>绑定的输入源显示名，由主视图模型在加载后回填。</summary>
    [ObservableProperty]
    private string _inputSourceName = L.T("l004-038");

    /// <summary>这条目标走哪条代理：follow 跟随默认 / direct 直连 / custom 自定义。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomProxy))]
    [NotifyPropertyChangedFor(nameof(ProxyText))]
    [NotifyPropertyChangedFor(nameof(ProxyVariant))]
    private string _proxyMode = ProxyOptions.Follow;

    /// <summary>自定义代理地址，只有 custom 档才用。</summary>
    [ObservableProperty]
    private string _proxyUrl = string.Empty;

    /// <summary>
    /// 设置页里那一档默认代理（direct / system / manual），只读，由主视图模型在每次重推时同步过来。
    /// 目标自己选了「跟随默认」时，列表里那颗胶囊要写出它到底会走哪条。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProxyText))]
    [NotifyPropertyChangedFor(nameof(ProxyVariant))]
    private string _defaultProxyMode = ProxyOptions.System;

    /// <summary>是否在列表里被勾选，只影响批量操作，不写入配置。</summary>
    [ObservableProperty]
    private bool _isSelected;

    // ====== 运行时状态（由主视图模型轮询引擎指标后回填） ======

    /// <summary>引擎侧这条转发的原始状态，来自 metrics 的 state 标签。</summary>
    private string _runtimeState = string.Empty;

    /// <summary>批量轮询期间只改字段、不发通知，由主视图模型一轮写完统一刷新。</summary>
    public string RuntimeState
    {
        get => _runtimeState;
        set
        {
            if (_runtimeState == value) return;
            _runtimeState = value;
            if (_bulkRuntime) return;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(StateTip));
            OnPropertyChanged(nameof(StatusVariant));
            OnPropertyChanged(nameof(IsStreaming));
            OnPropertyChanged(nameof(IsErrored));
        }
    }

    private string _speedText = "—";

    /// <summary>批量轮询期间只改字段、不发通知，由主视图模型一轮写完统一刷新。</summary>
    public string SpeedText
    {
        get => _speedText;
        set
        {
            if (_speedText == value) return;
            _speedText = value;
            if (_bulkRuntime) return;
            OnPropertyChanged();
        }
    }

    /// <summary>到目标服务器的 TCP 建连耗时，衡量这条线路通不通、快不快。</summary>
    private string _responseText = "—";

    /// <summary>批量轮询期间只改字段、不发通知，由主视图模型一轮写完统一刷新。</summary>
    public string ResponseText
    {
        get => _responseText;
        set
        {
            if (_responseText == value) return;
            _responseText = value;
            if (_bulkRuntime) return;
            OnPropertyChanged();
        }
    }

    /// <summary>这条转发累计已经发出去多少数据。</summary>
    private string _sentText = "—";

    /// <summary>批量轮询期间只改字段、不发通知，由主视图模型一轮写完统一刷新。</summary>
    public string SentText
    {
        get => _sentText;
        set
        {
            if (_sentText == value) return;
            _sentText = value;
            if (_bulkRuntime) return;
            OnPropertyChanged();
        }
    }

    /// <summary>引擎最后一次报的失败原因，只有出错时才有值。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = string.Empty;

    /// <summary>上一次由程序自动填进 Server 的值，用来判断用户有没有手动改过。</summary>
    private string _autoServer = string.Empty;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>列表里显示的短 Id：前 6 位加省略号，完整值放 ToolTip。</summary>
    public string ShortId => InputItemViewModel.ShortenId(Id);

    /// <summary>当前平台模板，找不到时回退到「自定义」。</summary>
    public PlatformPreset Preset => PresetCatalog.Find(Platform);

    /// <summary>列表里显示的名字，用户没取名时回退到平台名。</summary>
    public string DisplayName
        => string.IsNullOrWhiteSpace(Name) ? Preset.DisplayName : Name;

    /// <summary>协议名，从服务器地址的 scheme 推断，取不到时显示占位符。</summary>
    public string Protocol
    {
        get
        {
            int mark = Server.IndexOf("://", StringComparison.Ordinal);
            if (mark <= 0) return "—";

            return Server[..mark].ToUpperInvariant();
        }
    }

    /// <summary>
    /// 这一条此刻是否真的在用。用户自己关掉、或被程序禁用，都算不在用，
    /// 排序分组看的是它，而不是裸的 <see cref="Enabled"/>。
    /// </summary>
    public bool EffectiveEnabled
        => Enabled && !RuntimeDisabled;

    /// <summary>
    /// 列表里展示的状态，只分四档：被禁用 / 已停止 / 有错误 / 启用中。
    /// 引擎侧更细的说法（推流中、重连中…）留给 <see cref="StateTip"/>，不挤在这四个字里。
    /// </summary>
    public string StateText
        => RuntimeDisabled ? L.T("l004-008")
           : !Enabled ? L.T("l004-001")
           : IsErrored ? L.T("l004-050")
           : L.T("l004-009");

    /// <summary>悬停时才看的细分状态，引擎报什么就说什么。</summary>
    public string StateTip
        => Strings.TargetState(Enabled, RuntimeState);

    /// <summary>正在推流，用于状态灯的绿色。</summary>

    // ====== 批量写入（指标轮询用）======

    /// <summary>为 true 时所有运行时属性只落字段、不发通知。</summary>
    private bool _bulkRuntime;

    /// <summary>开始一轮批量写入。</summary>
    public void BeginBulkRuntime() => _bulkRuntime = true;

    /// <summary>结束批量写入并统一刷新：一次通知覆盖整行，避免每行 5 次重排。</summary>
    public void EndBulkRuntime()
    {
        if (!_bulkRuntime) return;

        _bulkRuntime = false;
        OnPropertyChanged(string.Empty);
    }

    public bool IsStreaming
        => Enabled && !RuntimeDisabled && RuntimeState is "running" or "forwarding";

    /// <summary>转发出错，用于状态灯的黄色。</summary>
    public bool IsErrored
        => Enabled && !RuntimeDisabled && RuntimeState == "error";

    /// <summary>有没有失败原因可显示，决定那一行红字露不露面。</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>自定义代理档才让用户填地址。</summary>
    public bool HasCustomProxy => ProxyMode == "custom";

    // ====== 胶囊标签（列表每行把各字段摆成一颗胶囊） ======

    /// <summary>平台名，未命名时列表里显示的就是它。</summary>
    public string PlatformText => Preset.DisplayName;

    /// <summary>状态胶囊配色，与四档文案一一对应：启用中绿、已停止红、有错误黄、被禁用灰。</summary>
    public PillVariant StatusVariant
    {
        get
        {
            if (RuntimeDisabled) return PillVariant.Plain;
            if (!Enabled) return PillVariant.Off;
            if (IsErrored) return PillVariant.Warn;

            return PillVariant.On;
        }
    }

    /// <summary>协议胶囊的配色，与输入源页的取色规则保持一致。</summary>
    public PillVariant ProtocolVariant
        => Protocol switch
        {
            "RTMPS" => PillVariant.Rtmps,
            "RTSP"  => PillVariant.Rtsp,
            "SRT"   => PillVariant.Srt,
            "RTMP"  => PillVariant.Rtmp,
            _       => PillVariant.Plain,
        };

    /// <summary>
    /// 代理胶囊的文案。目标自己是三档，跟随默认时要把设置页那一档也写出来，
    /// 否则光看「跟随默认」猜不到它实际会怎么连。
    /// </summary>
    public string ProxyText
        => ProxyMode switch
        {
            ProxyOptions.Direct => L.T("l004-051"),
            ProxyOptions.System => L.T("l004-052"),
            "custom" or ProxyOptions.Manual => L.T("b004-001"),
            _ => L.T("l004-053").Replace("{mode}", DefaultProxyText),
        };

    /// <summary>代理胶囊的配色：跟着实际生效的那一档取色，跟随默认时按默认档走。</summary>
    public PillVariant ProxyVariant
    {
        get
        {
            string mode = ProxyMode == ProxyOptions.Follow ? DefaultProxyMode : ProxyMode;

            return mode switch
            {
                ProxyOptions.Direct            => PillVariant.Direct,
                "custom" or ProxyOptions.Manual => PillVariant.Custom,
                _                              => PillVariant.Follow,
            };
        }
    }

    /// <summary>「默认：X」里那个 X，只取档位短名，不带上「代理」二字。</summary>
    private string DefaultProxyText
        => DefaultProxyMode switch
        {
            ProxyOptions.Direct => L.T("l004-051"),
            ProxyOptions.Manual => L.T("l004-054"),
            _                   => L.T("l004-055"),
        };

    // ====== 平台切换时自动带出服务器地址 ======

    /// <summary>
    /// 换平台时把预设的服务器地址填进去，但只在用户没手动改过服务器时才覆盖，
    /// 否则会把「自定义地址」这样的手工输入冲掉。
    /// </summary>
    partial void OnPlatformChanged(string value)
    {
        string prefix = Preset.ServerPrefix;

        if (string.IsNullOrWhiteSpace(prefix)) return;

        bool untouched = string.IsNullOrWhiteSpace(Server) || Server == _autoServer;
        if (!untouched) return;

        Server = prefix;
        _autoServer = prefix;
    }

    // ====== 映射 ======

    public ForwardTarget ToModel()
        => new ForwardTarget
        {
            Id        = Id,
            Name      = Name,
            Platform  = Platform,
            Server    = Server,
            StreamKey = StreamKey,
            InputSourceId = InputSourceId,
            Enabled   = Enabled,
            ProxyMode = ProxyMode,
            ProxyUrl  = ProxyUrl,
        };

    public static TargetItemViewModel FromModel(ForwardTarget model)
        => new TargetItemViewModel
        {
            Id        = string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id,
            Name      = model.Name,
            Platform  = string.IsNullOrWhiteSpace(model.Platform) ? PresetCatalog.CustomId : model.Platform,
            Server    = model.Server,
            StreamKey = model.StreamKey,
            InputSourceId = model.InputSourceId,
            Enabled   = model.Enabled,
            ProxyMode = string.IsNullOrWhiteSpace(model.ProxyMode) ? ProxyOptions.Follow : model.ProxyMode,
            ProxyUrl  = model.ProxyUrl,
        };
}
