using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Windowing;
using MediaMTX_Yuki_GUI.Models;
using MediaMTX_Yuki_GUI.Services;
using MediaMTX_Yuki_GUI.ViewModels;

namespace MediaMTX_Yuki_GUI.Views;

using L = Services.Localization;

/// <summary>添加 / 编辑一条输入源。模态小窗，不切走主界面。</summary>
public sealed partial class InputEditorWindow : FAAppWindow, INotifyPropertyChanged
{
    private readonly InputItemViewModel _item;
    private readonly bool _isNew;
    private readonly IReadOnlyCollection<string> _otherNames;
    private readonly IReadOnlyCollection<string> _otherPaths;

    private string _sourceName = "";
    private string _protocol = InputProtocols.Rtmp;
    private string _pathName = "live";
    private string _port = "";
    private bool _enabled = true;
    private string _sourceKind = SourceKindPush;
    private string _pullUrl = "";
    private bool _record;
    private readonly bool _advancedMode;
    private readonly IReadOnlyList<AuthAccountItemViewModel> _accounts;

    // 两个必填框各自记着「用户有没有碰过」：没碰过就别一打开窗口就红
    private bool _pathTouched;
    private bool _pullTouched;

    // 构造里那几次赋值不算用户碰过，要等窗口装好之后才算数
    private bool _ready;

    /// <summary>接收推流（MediaMTX 的 source 写成 publisher）。</summary>
    private const string SourceKindPush = "push";

    /// <summary>引擎主动去连（MediaMTX 的 source 写成那个 URL）。</summary>
    private const string SourceKindPull = "pull";

    public InputEditorWindow(
        InputItemViewModel item,
        bool isNew,
        IReadOnlyCollection<int> ownPorts,
        IReadOnlyCollection<string> otherNames,
        IReadOnlyCollection<string> otherPaths,
        bool advancedMode,
        IReadOnlyList<AuthAccountItemViewModel> accounts)
    {
        InitializeComponent();

        // 小窗也上材质，跟主窗保持一致
        WindowBackdrop.Attach(this);

        _item = item;
        _isNew = isNew;
        _otherNames = otherNames;
        _otherPaths = otherPaths;
        OwnPorts = ownPorts;
        _advancedMode = advancedMode;
        _accounts = accounts;

        // 这条 path 允许哪些账号推。空集合 = 不限制，界面上就是一颗标签都没有；
        // 已选的那些显示成带叉的标签，剩下的进「+」弹层里等着被挑
        HashSet<string> allowed = new(item.PublishAccountIds, StringComparer.OrdinalIgnoreCase);

        foreach (AuthAccountItemViewModel account in accounts)
        {
            if (allowed.Contains(account.Id))
                SelectedAccounts.Add(new AccountChip(account.Id, account.User));
        }

        RebuildSelectable();

        ShowAccountPicker = advancedMode || accounts.Count > 0;

        // 简洁模式下不出现录制开关；已经在录的那一路保持原值，不让一次编辑把它抹掉
        _record = item.Record;

        SourceName = item.Name;
        Protocol = item.Protocol;
        PathName = item.Path;
        // 端口框只在用户特意改过端口时才带数字；跟协议默认值一样就留空，靠占位说明
        Port = item.Port > 0 && item.Port != InputProtocols.DefaultPort(item.Protocol)
            ? item.Port.ToString(CultureInfo.InvariantCulture)
            : "";
        Enabled = isNew || item.Enabled;
        _pullUrl = item.SourceUrl;
        _sourceKind = item.SourceUrl.Length > 0 ? SourceKindPull : SourceKindPush;

        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        CancelCommand = new RelayCommand(Cancel);

        DataContext = this;

        // 构造函数里那几次赋值可能全被 Set 判成「没变」而提前返回，这里补一次
        RefreshConfirm();

        // 编辑小窗不给最大化
        CanMaximize = false;

        // 上面的赋值全部发生在 _ready 之前，所以打开窗口时不该有任何红字
        _ready = true;
    }

    /// <summary>
    /// 自己实现的通知事件。基类 AvaloniaObject 也有一个同名成员，但那是它对
    /// AvaloniaProperty 体系的通知；这里要的是 INotifyPropertyChanged 那一套，
    /// 绑定系统会通过接口拿到本事件。加 new 明确表示刻意隐藏，不是漏写。
    /// </summary>
    public new event PropertyChangedEventHandler? PropertyChanged;

    private IReadOnlyCollection<int> OwnPorts { get; }

    public string WindowTitle
        => _isNew ? L.T("b003-001") : L.T("c003-012");

    public string ConfirmText
        => _isNew ? L.T("b001-006") : L.T("b001-009");

    /// <summary>「允许推流的账号」里已经加进来的那些，显示成一颗颗带叉的标签。</summary>
    public ObservableCollection<AccountChip> SelectedAccounts { get; } = [];

    /// <summary>「+」弹层里还能挑的账号：账号表里还没被加进来的。</summary>
    public ObservableCollection<AccountOption> SelectableAccounts { get; } = [];

    /// <summary>账号表为空时整块不显示——没建过账号的时候摆一堆空标签只会让人困惑。
    /// 高级模式下例外：这块要露出来，好让人知道这里能配。</summary>
    public bool ShowAccountPicker { get; }

    /// <summary>高级模式下账号表还是空的：显示一句引导，别只留个空块。</summary>
    public bool IsAccountListEmpty => _accounts.Count == 0;

    /// <summary>一个账号都没建：连「+」按钮都不出现，省得点开是个空列表。</summary>
    public bool HasAccounts => _accounts.Count > 0;

    /// <summary>还能挑的账号不为空；全加完了就在弹层里说一句「账号都在这儿了」。</summary>
    public bool HasSelectableAccounts => SelectableAccounts.Count > 0;

    /// <summary>账号建好了、但这颗标签一颗都没选：框里给一句「没选就等于不限制」。</summary>
    public bool ShowPickHint => _accounts.Count > 0 && SelectedAccounts.Count == 0;

    public ObservableCollection<ProtocolChoice> ProtocolChoices { get; } =
    [
        new(InputProtocols.Rtmp, "RTMP"),
        new(InputProtocols.Rtsp, "RTSP"),
        new(InputProtocols.Srt, "SRT"),
    ];

    public IRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>
    /// 备注跟别的条目撞了。只给黄字提示，照样能保存——用户明确要求允许重名，
    /// 重名本身不是错误，只是提醒别把两条搞混。
    /// </summary>
    public string NameWarning
    {
        get
        {
            string name = SourceName.Trim();

            if (name.Length == 0) return string.Empty;

            return _otherNames.Contains(name, StringComparer.Ordinal)
                ? L.T("l003-024").Replace("{name}", name)
                : string.Empty;
        }
    }

    public bool HasNameWarning
        => !string.IsNullOrEmpty(NameWarning);

    /// <summary>
    /// 路径名哪里不对：空着、字符不合法、跟别的输入源撞名，各给一句话；没问题时是空串。
    /// 与备注不同，这三种都得拦住——路径名是 runtime.yml 里 paths 下的键，
    /// 撞了两个就只能剩一个，字符不对则引擎那边直接对不上。
    /// </summary>
    public string PathErrorText
    {
        get
        {
            string path = PathName.Trim();

            if (path.Length == 0)
            {
                return L.T("l003-030");
            }

            if (!IsValidPathName(path))
            {
                return L.T("l003-031");
            }

            return _otherPaths.Contains(path, StringComparer.Ordinal) ? L.T("l003-032") : string.Empty;
        }
    }

    public bool HasPathError
        => PathErrorText.Length > 0;

    /// <summary>
    /// 路径名的红框与红字：有这个错、而且用户已经碰过这个框（或者已经打进了不合法的内容）。
    /// 开窗时预填的 live 不该一上来就红，所以空着的时候要等失焦。
    /// </summary>
    public bool PathInvalid
        => _pathTouched && HasPathError;

    /// <summary>拉流地址的红框与红字；只在拉流模式下才有意义。</summary>
    public bool PullUrlInvalid
        => _pullTouched && IsPulling && string.IsNullOrWhiteSpace(PullUrl);

    /// <summary>填不全或者端口占着，就不给提交。红框是「碰过才亮」，这里判的是当前值本身。</summary>
    public bool CanConfirm
        => !HasPathError
            && (!IsPulling || !string.IsNullOrWhiteSpace(PullUrl))
            && !HasPortWarning;

    public string SourceName
    {
        get => _sourceName;
        set
        {
            if (!Set(ref _sourceName, value))
            {
                return;
            }

            // 重名是打字当下就该看到的提醒，不用等失焦
            Raise(nameof(NameWarning));
            Raise(nameof(HasNameWarning));
        }
    }

    public string Protocol
    {
        get => _protocol;
        set
        {
            string previous = _protocol;

            if (!Set(ref _protocol, value))
            {
                return;
            }

            // 换了协议，框里那串数字多半是旧协议的默认值，清掉让新协议的默认接管
            if (Port.Length > 0
                && int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out int current)
                && current == InputProtocols.DefaultPort(previous))
            {
                Port = "";
            }

            Raise(nameof(PortPlaceholder));
            Raise(nameof(PortWarning));
            Raise(nameof(HasPortWarning));
            Raise(nameof(SelectedProtocolChoice));
            RefreshConfirm();
        }
    }

    /// <summary>Avalonia 的 ComboBox 没有 SelectedValuePath，用选中项自己折算回协议 id。</summary>
    public ProtocolChoice? SelectedProtocolChoice
    {
        get => ProtocolChoices.FirstOrDefault(choice => choice.Id == Protocol);
        set
        {
            if (value is null)
            {
                return;
            }

            Protocol = value.Id;
        }
    }

    public string PathName
    {
        get => _pathName;
        set
        {
            if (!Set(ref _pathName, value))
            {
                return;
            }

            // 用户一改这个框就算碰过了：清空的时候也要当场见红，不必等他再点别处
            if (_ready)
            {
                _pathTouched = true;
            }

            RefreshConfirm();
        }
    }

    /// <summary>来源方式：push 接收推流 / pull 主动拉流。</summary>
    public string SourceKind
    {
        get => _sourceKind;
        set
        {
            if (!Set(ref _sourceKind, value))
            {
                return;
            }

            // 用户主动切到拉流，等于表态要用这种模式：这个模式下缺地址就当场说清楚，
            // 不必再等他去点一下地址框。切回去则把痕迹清掉，免得下次又提前标红。
            if (IsPulling)
            {
                _pullTouched = true;
            }
            else
            {
                _pullTouched = false;
            }

            Raise(nameof(IsPulling));
            Raise(nameof(IsPushKind));
            Raise(nameof(IsPullKind));
            Raise(nameof(ShowPullCard));
            Raise(nameof(PushCardSpan));
            Raise(nameof(SourceKindHint));
            Raise(nameof(HasPortWarning));
            RefreshConfirm();
        }
    }

    /// <summary>「接收推流」那张卡片要不要画选中态。</summary>
    public bool IsPushKind
        => SourceKind == SourceKindPush;

    /// <summary>「主动拉流」那张卡片要不要画选中态。</summary>
    public bool IsPullKind
        => SourceKind == SourceKindPull;

    /// <summary>
    /// 「主动拉流」那张卡片露不露面。
    /// 简洁模式下不给新建拉流源，所以只有一张；
    /// 例外是这条本来就在拉流——那就把两张都摆出来，让人能改回接收推流。
    /// </summary>
    public bool ShowPullCard
        => _advancedMode || IsPulling;

    /// <summary>
    /// 「接收推流」那张卡片横跨几列：只有一张卡时跨过中间那道缝铺满整行，
    /// 不然右边空出半截，看着像少了东西。
    /// </summary>
    public int PushCardSpan
        => ShowPullCard ? 1 : 3;

    /// <summary>主动拉流时才展开地址框。地址留空但选了拉流也算拉流，只是保存时按接收推流处理。</summary>
    public bool IsPulling
        => SourceKind == SourceKindPull;

    /// <summary>拉流地址。切回「接收推流」时不清空，方便来回试。</summary>
    public string PullUrl
    {
        get => _pullUrl;
        set
        {
            if (!Set(ref _pullUrl, value))
            {
                return;
            }

            // 与路径名同理：一改就算碰过，清空当场见红
            if (_ready)
            {
                _pullTouched = true;
            }

            RefreshConfirm();
        }
    }

    /// <summary>来源方式下面那行小字。</summary>
    public string SourceKindHint
        => IsPulling ? L.T("l003-018") : L.T("l003-012");

    public string Port
    {
        get => _port;
        set
        {
            if (!Set(ref _port, value))
            {
                return;
            }

            Raise(nameof(PortWarning));
            Raise(nameof(HasPortWarning));
            RefreshConfirm();
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
            {
                Raise(nameof(RecordShown));
            }
        }
    }

    /// <summary>这条输入要不要录。开关本身只在高级模式露出来。</summary>
    public bool Record
    {
        get => _record;
        set
        {
            if (Set(ref _record, value))
            {
                Raise(nameof(RecordShown));
            }
        }
    }

    /// <summary>
    /// 界面上那个录制开关的显示值：这一路没启用时一律显示成关。
    /// 置灰只是「点不动」，灰着还亮着绿灯会让人以为在录；
    /// 真值仍留在 <see cref="Record"/> 里，把上一个开关打开就恢复。
    /// </summary>
    public bool RecordShown
    {
        get => Enabled && Record;
        set => Record = value;
    }

    /// <summary>高级模式才显示录制开关这一块。</summary>
    public bool IsAdvancedMode => _advancedMode;

    /// <summary>端口框里的灰字：不填就用协议默认端口。</summary>
    public string PortPlaceholder
        => L.T("l003-042").Replace("{port}", InputProtocols.DefaultPort(Protocol).ToString());

    public string PortWarning
    {
        get
        {
            int port = ResolvePort();

            if (port <= 0)
            {
                return L.T("l003-043");
            }

            return PortProbe.IsFree(port, OwnPorts) ? "" : L.T("l003-044");
        }
    }

    /// <summary>拉流模式下不校验端口：这条路径等引擎去连上游，本机端口与它无关。</summary>
    public bool HasPortWarning
        => !IsPulling && !string.IsNullOrEmpty(PortWarning);

    /// <summary>
    /// 路径名同时是 runtime.yml 的键、推流地址的最后一段和录制目录名，
    /// 所以只放行小写字母、数字与下划线——编辑窗里那句提示就是按这个口径写的。
    /// </summary>
    private static bool IsValidPathName(string path)
        => path.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private int ResolvePort()
    {
        if (string.IsNullOrWhiteSpace(Port))
        {
            return InputProtocols.DefaultPort(Protocol);
        }

        return int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
    }

    /// <summary>路径名、拉流地址、端口任一变化都重算一次能不能提交，顺带刷新字段上的红框与红字。</summary>
    private void RefreshConfirm()
    {
        Raise(nameof(CanConfirm));
        Raise(nameof(PathErrorText));
        Raise(nameof(HasPathError));
        Raise(nameof(PathInvalid));
        Raise(nameof(PullUrlInvalid));

        // 构造函数里属性先于命令赋值，这时候还没有命令可以通知
        ConfirmCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>点「接收推流」那张卡片。</summary>
    private void OnPushKindPressed(object? sender, PointerPressedEventArgs e)
        => SourceKind = SourceKindPush;

    /// <summary>点「主动拉流」那张卡片。</summary>
    private void OnPullKindPressed(object? sender, PointerPressedEventArgs e)
        => SourceKind = SourceKindPull;

    /// <summary>路径名框失焦：从现在起空着就红。</summary>
    private void OnPathNameLostFocus(object? sender, RoutedEventArgs e)
    {
        _pathTouched = true;

        Raise(nameof(PathInvalid));
    }

    /// <summary>拉流地址框失焦：从现在起空着就红。</summary>
    private void OnPullUrlLostFocus(object? sender, RoutedEventArgs e)
    {
        _pullTouched = true;

        Raise(nameof(PullUrlInvalid));
    }

    private void Confirm()
    {
        if (HasPortWarning)
        {
            return;
        }

        _item.Name = SourceName;
        _item.Protocol = Protocol;
        _item.Path = PathName.Trim();
        _item.Port = ResolvePort();
        _item.Enabled = Enabled;
        _item.SourceUrl = IsPulling ? PullUrl.Trim() : "";
        _item.Record = RecordShown;

        // 一颗标签都没加就当成「不限制」，跟空列表语义一致
        _item.PublishAccountIds.Clear();

        foreach (AccountChip chip in SelectedAccounts)
            _item.PublishAccountIds.Add(chip.Id);

        Close(true);
    }

    private void Cancel()
        => Close(false);

    /// <summary>从弹层里挑了一个账号：加进已选，弹层收起来。</summary>
    private void OnPickAccountClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AccountOption option })
            return;

        SelectedAccounts.Add(new AccountChip(option.Id, option.User));
        RebuildSelectable();
        AddAccountButton.Flyout?.Hide();
    }

    /// <summary>点了标签上的叉：从已选里摘掉，回到候选列表。</summary>
    private void OnRemoveAccountClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AccountChip chip })
            return;

        SelectedAccounts.Remove(chip);
        RebuildSelectable();
    }

    /// <summary>按账号表原顺序把候选列表重算一遍。加一个、摘一个都走它，省得两头各维护一份。</summary>
    private void RebuildSelectable()
    {
        SelectableAccounts.Clear();

        HashSet<string> picked = new(SelectedAccounts.Select(chip => chip.Id), StringComparer.OrdinalIgnoreCase);

        foreach (AuthAccountItemViewModel account in _accounts)
        {
            if (!picked.Contains(account.Id))
                SelectableAccounts.Add(new AccountOption(account.Id, account.User));
        }

        Raise(nameof(HasSelectableAccounts));
        Raise(nameof(IsAccountListEmpty));
        Raise(nameof(ShowPickHint));
    }

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

    public sealed record ProtocolChoice(string Id, string Name)
    {
        // 下拉项在无障碍树里拿 ToString 当名字，不覆盖会显示成一长串记录体
        public override string ToString() => Name;
    }

    /// <summary>已选账号标签上的一行。只读快照——「在不在 SelectedAccounts 里」就是勾没勾。</summary>
    public sealed record AccountChip(string Id, string User)
    {
        // 标签在无障碍树里拿 ToString 当名字，不覆盖会显示成一长串记录体
        public override string ToString() => User;
    }

    /// <summary>「+」弹层里的一行候选账号。</summary>
    public sealed record AccountOption(string Id, string User)
    {
        public override string ToString() => User;
    }

}
