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
        => L.T("l027-002").Replace("{n}", Accounts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>把配置里的账号搬进列表。启动与切页都会调，重复调是安全的。</summary>
    private void RefreshAccounts()
    {
        Accounts.Clear();

        foreach (AuthAccount account in _cfg.Accounts)
            TrackAccount(new AuthAccountItemViewModel(account));

        OnPropertyChanged(nameof(IsAccountsEmpty));
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(AccountsSummary));
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
        => ScheduleApply();

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
        ShowToast(L.T("t007-001").Replace("{name}", draft.User), ToastKind.Success);
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
        ShowToast(L.T("t007-002").Replace("{name}", item.User), ToastKind.Success);
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
        ShowToast(L.T("t007-003").Replace("{name}", item.User), ToastKind.Success);
    }

    private void RefreshAccountViews()
    {
        OnPropertyChanged(nameof(IsAccountsEmpty));
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(AccountsSummary));
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
