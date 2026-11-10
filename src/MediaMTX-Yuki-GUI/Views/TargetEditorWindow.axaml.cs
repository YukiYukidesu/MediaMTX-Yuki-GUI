using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Windowing;
using MediaMTX_Yuki_GUI.Services;
using MediaMTX_Yuki_GUI.ViewModels;

namespace MediaMTX_Yuki_GUI.Views;

using L = Services.Localization;

/// <summary>添加 / 编辑一个输出目标。和输入源一样走模态小窗。</summary>
public sealed partial class TargetEditorWindow : FAAppWindow, INotifyPropertyChanged
{
    private readonly TargetItemViewModel _item;
    private readonly bool _isNew;
    private readonly IReadOnlyCollection<string> _otherNames;

    private string _targetName = "";
    private string _platform = "custom";
    private string _server = "";
    private string _streamKey = "";
    private string _inputSourceId = "";
    private bool _enabled = true;
    private string _proxyMode = Models.ProxyOptions.Follow;
    private string _proxyUrl = "";

    /// <summary>用户手填过服务器时，换平台就不再覆盖它。</summary>
    private string _autoServer = "";

    // 两个必填框各自记着「用户有没有碰过」：没碰过就别一打开窗口就红
    private bool _serverTouched;
    private bool _proxyUrlTouched;

    public TargetEditorWindow(
        TargetItemViewModel item,
        bool isNew,
        IReadOnlyList<InputChoice> inputChoices,
        IReadOnlyCollection<string> otherNames)
    {
        InitializeComponent();

        // 小窗也上材质，跟主窗保持一致
        WindowBackdrop.Attach(this);

        _item = item;
        _isNew = isNew;
        _otherNames = otherNames;

        TargetName = item.Name;
        Platform = item.Platform;

        // 先把代理档位放好，再写服务器地址：写地址会按协议把不支持的档位收成直连，
        // 顺序反了就会被 item.ProxyMode 覆盖回去。
        ProxyMode = item.ProxyMode;
        ProxyUrl = item.ProxyUrl;
        Server = item.Server;

        StreamKey = item.StreamKey;
        InputSourceId = item.InputSourceId;
        Enabled = isNew || item.Enabled;

        InputChoices = new ObservableCollection<InputChoice>(inputChoices);

        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        CancelCommand = new RelayCommand(Cancel);

        DataContext = this;

        // 构造函数里那几次赋值可能全被 Set 判成「没变」而提前返回，这里补一次
        RefreshConfirm();

        // 编辑小窗不给最大化
        CanMaximize = false;
    }

    /// <summary>
    /// 自己实现的通知事件。基类 AvaloniaObject 也有一个同名成员，但那是它对
    /// AvaloniaProperty 体系的通知；这里要的是 INotifyPropertyChanged 那一套，
    /// 绑定系统会通过接口拿到本事件。加 new 明确表示刻意隐藏，不是漏写。
    /// </summary>
    public new event PropertyChangedEventHandler? PropertyChanged;

    public string WindowTitle
        => _isNew ? L.T("c009-001") : L.T("c009-002");

    public string ConfirmText
        => _isNew ? L.T("c009-026") : L.T("c009-027");

    public ObservableCollection<PlatformPreset> PlatformChoices { get; } =
        new(PresetCatalog.All);

    public ObservableCollection<InputChoice> InputChoices { get; }

    public IRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>
    /// 备注跟别的条目撞了。只给黄字提示，照样能保存——允许重名，
    /// 重名不是错误，只是提醒别把两条搞混。
    /// </summary>
    public string NameWarning
    {
        get
        {
            string name = TargetName.Trim();

            if (name.Length == 0) return string.Empty;

            return _otherNames.Contains(name, StringComparer.Ordinal)
                ? L.T("c009-023").Replace("{name}", name)
                : string.Empty;
        }
    }

    public bool HasNameWarning
        => !string.IsNullOrEmpty(NameWarning);

    /// <summary>
    /// 服务器地址的红框与红字：碰过这个框、而且现在还是空的。
    /// 判空只认输入框本身，不拿平台预设前缀抵账：用户把它删空就是真没填，
    /// 这时候悄悄拿前缀补上再保存，等于把「没填」变成了「填了别的」。
    /// </summary>
    public bool ServerInvalid
        => _serverTouched && string.IsNullOrWhiteSpace(Server);

    /// <summary>自定义代理地址的红框与红字；只在手填档下才有意义。</summary>
    public bool ProxyUrlInvalid
        => _proxyUrlTouched && HasCustomProxy && string.IsNullOrWhiteSpace(ProxyUrl);

    /// <summary>填不全就不给提交，省得点了确认才发现少写东西。红框是「碰过才亮」，这里判的是当前值。</summary>
    public bool CanConfirm
        => !string.IsNullOrWhiteSpace(Server)
            && (!HasCustomProxy || !string.IsNullOrWhiteSpace(ProxyUrl));

    public bool HasNoInput
        => InputChoices.Count <= 1;

    public string TargetName
    {
        get => _targetName;
        set
        {
            if (!Set(ref _targetName, value))
            {
                return;
            }

            // 重名是打字当下就该看到的提醒，不用等失焦
            Raise(nameof(NameWarning));
            Raise(nameof(HasNameWarning));
        }
    }

    public string Platform
    {
        get => _platform;
        set
        {
            if (!Set(ref _platform, value))
            {
                return;
            }

            ApplyPresetServer();
            Raise(nameof(SelectedPlatformChoice));
        }
    }

    /// <summary>Avalonia 的 ComboBox 没有 SelectedValuePath，用选中项自己折算回平台 id。</summary>
    public PlatformPreset? SelectedPlatformChoice
    {
        get => PlatformChoices.FirstOrDefault(preset => preset.Id == Platform);
        set
        {
            if (value is null)
            {
                return;
            }

            Platform = value.Id;
        }
    }

    public string Server
    {
        get => _server;
        set
        {
            if (!Set(ref _server, value))
            {
                return;
            }

            RefreshProxySupport();
            RefreshConfirm();
        }
    }

    public string StreamKey
    {
        get => _streamKey;
        set => Set(ref _streamKey, value);
    }

    public string InputSourceId
    {
        get => _inputSourceId;
        set
        {
            if (!Set(ref _inputSourceId, value))
            {
                return;
            }

            Raise(nameof(SelectedInputChoice));
        }
    }

    /// <summary>绑定输入源下拉框的选中项。</summary>
    public InputChoice? SelectedInputChoice
    {
        get => InputChoices.FirstOrDefault(choice => choice.Id == InputSourceId);
        set
        {
            if (value is null)
            {
                return;
            }

            InputSourceId = value.Id;
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    /// <summary>这条目标走哪条代理：follow 跟随默认 / direct 直连 / system 系统代理 / custom 手动。</summary>
    public string ProxyMode
    {
        get => _proxyMode;
        set
        {
            if (!Set(ref _proxyMode, value))
            {
                return;
            }

            // 用户主动切到「手动代理」，等于表态这条要走手填地址：
            // 这个档位下缺地址就当场说清楚，不必再等他去点一下地址框。
            // 切走则清掉痕迹，免得下次又提前标红。
            if (HasCustomProxy)
            {
                _proxyUrlTouched = true;
            }
            else
            {
                _proxyUrlTouched = false;
            }

            Raise(nameof(ProxyIndex));
            Raise(nameof(HasCustomProxy));
            RefreshConfirm();
        }
    }

    /// <summary>Avalonia 的 ComboBox 没有 SelectedValuePath，用索引接回档位。</summary>
    public int ProxyIndex
    {
        get => ProxyMode switch
        {
            Models.ProxyOptions.Direct => 1,
            Models.ProxyOptions.System => 2,
            "custom"                   => 3,
            _                          => 0,
        };
        set => ProxyMode = value switch
        {
            1 => Models.ProxyOptions.Direct,
            2 => Models.ProxyOptions.System,
            3 => "custom",
            _ => Models.ProxyOptions.Follow,
        };
    }

    /// <summary>自定义档才显示地址输入框。</summary>
    public bool HasCustomProxy => ProxyMode == "custom";

    /// <summary>
    /// 这条目标能不能走代理：中转站只会说 RTMP，别的协议选了也白选。
    ///
    /// 服务器地址还没写或者没带协议头时当作能用，别在用户刚打开窗时就吓他。
    /// </summary>
    public bool CanUseProxy
    {
        get
        {
            string scheme = ReadScheme(Server);

            if (scheme.Length == 0) return true;

            return scheme is "rtmp" or "rtmps";
        }
    }

    /// <summary>自定义代理地址。</summary>
    public string ProxyUrl
    {
        get => _proxyUrl;
        set
        {
            if (!Set(ref _proxyUrl, value))
            {
                return;
            }

            RefreshConfirm();
        }
    }

    /// <summary>
    /// 换了服务器地址之后重算代理档位：协议不支持就自动跳回直连，并让下拉框变灰。
    ///
    /// 只单向收：换回 RTMP 时不会把档位再改回去，用户自己重新选。
    /// </summary>
    private void RefreshProxySupport()
    {
        Raise(nameof(CanUseProxy));

        if (CanUseProxy) return;

        ProxyMode = Models.ProxyOptions.Direct;
    }

    /// <summary>从服务器地址里抠出协议头，抠不出返回空串。</summary>
    /// <param name="server">用户填的服务器地址。</param>
    private static string ReadScheme(string? server)
    {
        string text = (server ?? string.Empty).Trim();

        int mark = text.IndexOf("://", StringComparison.Ordinal);

        if (mark <= 0) return string.Empty;

        return text[..mark].ToLowerInvariant();
    }

    /// <summary>服务器地址、代理档位、代理地址任一变化都重算一次能不能提交，顺带刷新字段上的红框与红字。</summary>
    private void RefreshConfirm()
    {
        Raise(nameof(CanConfirm));
        Raise(nameof(ServerInvalid));
        Raise(nameof(ProxyUrlInvalid));

        // 构造函数里属性先于命令赋值，这时候还没有命令可以通知
        ConfirmCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>服务器地址框失焦：从现在起空着就红。</summary>
    private void OnServerLostFocus(object? sender, RoutedEventArgs e)
    {
        _serverTouched = true;

        Raise(nameof(ServerInvalid));
    }

    /// <summary>自定义代理地址框失焦：从现在起空着就红。</summary>
    private void OnProxyUrlLostFocus(object? sender, RoutedEventArgs e)
    {
        _proxyUrlTouched = true;

        Raise(nameof(ProxyUrlInvalid));
    }

    /// <summary>换平台时把该平台的服务器前缀填进去，但别冲掉用户自己写的地址。</summary>
    private void ApplyPresetServer()
    {
        PlatformPreset preset = PresetCatalog.Find(Platform);

        if (string.IsNullOrWhiteSpace(Server) || Server == _autoServer)
        {
            _autoServer = preset.ServerPrefix;
            Server = preset.ServerPrefix;
        }
    }

    private void Confirm()
    {
        _item.Name = TargetName;
        _item.Platform = Platform;
        _item.Server = string.IsNullOrWhiteSpace(Server) ? _autoServer : Server;
        _item.StreamKey = StreamKey;
        _item.InputSourceId = InputSourceId;
        _item.Enabled = Enabled;
        _item.ProxyMode = ProxyMode;
        _item.ProxyUrl = ProxyUrl;

        Close(true);
    }

    private void Cancel()
        => Close(false);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);

        return true;
    }

    private void Raise(string? name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
