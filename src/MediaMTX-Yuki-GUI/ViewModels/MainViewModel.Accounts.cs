using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaMTX_Yuki_GUI.Models;
using MediaMTX_Yuki_GUI.Services;

namespace MediaMTX_Yuki_GUI.ViewModels;


using L = MediaMTX_Yuki_GUI.Services.Localization;

/// <summary>
/// 账号页：一张账号表管所有推流身份。
/// 每个输入源自己勾「允许谁推」，所以这张表改了不用动输入源，反之亦然。
/// </summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<AuthAccountItemViewModel> Accounts { get; } = new();

    public bool IsAccountsEmpty
        => Accounts.Count == 0;

    /// <summary>账号表非空，用来在空状态与表头之间切换。</summary>
    public bool HasAccounts
        => Accounts.Count > 0;

    /// <summary>表格里那个「允许推流的源数」统计，给页面副标题用。</summary>
    public string AccountsSummary
        => L.T("c005-009").Replace("{n}", Accounts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public int SelectedAccountCount
        => Accounts.Count(item => item.IsSelected);

    public bool HasAccountSelection
        => SelectedAccountCount > 0;

    public bool IsAllAccountsSelected
        => Accounts.Count > 0 && SelectedAccountCount == Accounts.Count;

    /// <summary>工具栏那句「未选择 / 已选 N 项」，与两个列表页共用同一套词。</summary>
    public string AccountSelectionText
        => SelectedAccountCount == 0 ? L.T("c001-014") : L.T("c001-015").Replace("{n}", SelectedAccountCount.ToString());

    public string SelectAllAccountsText
        => IsAllAccountsSelected ? L.T("c001-013") : L.T("c001-012");

    /// <summary>把配置里的账号搬进列表。启动与切页都会调，重复调是安全的。</summary>
    private void RefreshAccounts()
    {
        Accounts.Clear();

        foreach (AuthAccount account in _cfg.Accounts)
            TrackAccount(new AuthAccountItemViewModel(account));

        OnPropertyChanged(nameof(IsAccountsEmpty));
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(AccountsSummary));
        RefreshAccountSelection();
    }

    private void TrackAccount(AuthAccountItemViewModel item)
    {
        item.PropertyChanged += OnAccountChanged;
        Accounts.Add(item);
    }

    private void UntrackAccount(AuthAccountItemViewModel item)
        => item.PropertyChanged -= OnAccountChanged;

    /// <summary>账号任何一个字段被改，都要把整份配置重写一遍，否则重启就丢了。</summary>
    private void OnAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 勾选只是界面态：既不写盘，也不用重建表格，更新一下计数就够
        if (e.PropertyName == nameof(AuthAccountItemViewModel.IsSelected))
        {
            RefreshAccountSelection();
            return;
        }

        ScheduleApply();
    }

    /// <summary>把列表写回配置。删账号时顺手把输入源里指向它的勾选一起摘掉。</summary>
    private void SyncAccountsToConfig()
    {
        _cfg.Accounts = Accounts.Select(item => item.ToModel()).ToList();

        HashSet<string> alive = Accounts.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (InputItemViewModel input in Inputs)
            input.PublishAccountIds.RemoveAll(id => !alive.Contains(id));
    }

    [RelayCommand]
    private async Task AddAccountAsync()
    {
        if (AppHost is null) return;

        AuthAccountItemViewModel draft = new(new AuthAccount { User = NextAccountName() });
        IReadOnlyCollection<string> others = Accounts.Select(item => item.User).ToList();

        if (!await AppHost.ShowAccountEditorAsync(draft, true, others)) return;

        TrackAccount(draft);
        SyncAccountsToConfig();
        EnsureAuthEnabled();
        RefreshAccountViews();
        ScheduleApply();
        ShowToast(L.T("t005-001").Replace("{name}", draft.User), ToastKind.Success);
    }

    /// <summary>
    /// 第一次建出账号时顺手把推流鉴权打开。用户刚配好账号却因为总开关还关着、
    /// 引擎照样全放行，会以为配错了。关着的开关一旦被打开就记进待重启标志，
    /// 走那条既有的黄色横幅。
    /// </summary>
    private void EnsureAuthEnabled()
    {
        if (_cfg.AuthEnabled || Accounts.Count == 0) return;

        AuthEnabled = true;
    }

    [RelayCommand]
    private async Task EditAccountAsync(AuthAccountItemViewModel? item)
    {
        if (item is null || AppHost is null) return;

        AuthAccountItemViewModel draft = item.Clone();
        IReadOnlyCollection<string> others = Accounts
            .Where(other => !ReferenceEquals(other, item))
            .Select(other => other.User)
            .ToList();

        if (!await AppHost.ShowAccountEditorAsync(draft, false, others)) return;

        item.CopyFrom(draft);
        SyncAccountsToConfig();
        ScheduleApply();
        ShowToast(L.T("t005-002").Replace("{name}", item.User), ToastKind.Success);
    }

    [RelayCommand]
    private void RemoveAccount(AuthAccountItemViewModel? item)
    {
        if (item is null) return;

        UntrackAccount(item);
        Accounts.Remove(item);
        SyncAccountsToConfig();
        RefreshAccountViews();
        ScheduleApply();
        ShowToast(L.T("t005-003").Replace("{name}", item.User), ToastKind.Success);
    }

    [RelayCommand]
    private void ToggleSelectAllAccounts()
    {
        bool selectAll = !IsAllAccountsSelected;

        foreach (AuthAccountItemViewModel item in Accounts) item.IsSelected = selectAll;
    }

    /// <summary>批量删除账号：删掉的账号会从各输入源的允许推流名单里一起摘掉，所以先问一句。</summary>
    [RelayCommand(CanExecute = nameof(HasAccountSelection))]
    private async Task RemoveSelectedAccountsAsync()
    {
        List<AuthAccountItemViewModel> doomed = Accounts.Where(item => item.IsSelected).ToList();

        if (doomed.Count == 0) return;

        string count = doomed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (AppHost is not null)
        {
            bool ok = await AppHost.ConfirmAsync(
                L.T("c001-025"),
                L.T("l005-007").Replace("{n}", count),
                L.T("c001-018"),
                destructive: true);

            if (!ok) return;
        }

        foreach (AuthAccountItemViewModel item in doomed)
        {
            UntrackAccount(item);
            Accounts.Remove(item);
        }

        SyncAccountsToConfig();
        RefreshAccountViews();
        ScheduleApply();
        ShowToast(L.T("t005-004").Replace("{n}", count), ToastKind.Success);
    }

    private void RefreshAccountViews()
    {
        OnPropertyChanged(nameof(IsAccountsEmpty));
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(AccountsSummary));
        RefreshAccountSelection();
    }

    private void RefreshAccountSelection()
    {
        OnPropertyChanged(nameof(SelectedAccountCount));
        OnPropertyChanged(nameof(HasAccountSelection));
        OnPropertyChanged(nameof(AccountSelectionText));
        OnPropertyChanged(nameof(IsAllAccountsSelected));
        OnPropertyChanged(nameof(SelectAllAccountsText));

        RemoveSelectedAccountsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>新建账号时给一个不重名的默认用户名。</summary>
    private string NextAccountName()
    {
        for (int index = 1; ; index++)
        {
            string candidate = "user" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (Accounts.All(item => !string.Equals(item.User, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }
}
