using CommunityToolkit.Mvvm.ComponentModel;
using MediaMTX_Yuki_GUI.Models;


using L = MediaMTX_Yuki_GUI.Services.Localization;

namespace MediaMTX_Yuki_GUI.ViewModels;

/// <summary>账号表里的一行。一个账号既能推流（按输入源上的勾选）也能观看（看 CanView）。</summary>
public partial class AuthAccountItemViewModel : ObservableObject
{
    public AuthAccountItemViewModel(AuthAccount account)
    {
        Id = string.IsNullOrWhiteSpace(account.Id) ? Guid.NewGuid().ToString("N") : account.Id;
        _user = account.User;
        _password = account.Password;
        _canView = account.CanView;
    }

    public string Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUsable))]
    private string _user = "";

    /// <summary>密码在表里只显示长度，不显示原文。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUsable))]
    [NotifyPropertyChangedFor(nameof(PasswordMask))]
    private string _password = "";

    [ObservableProperty]
    private bool _canView;

    /// <summary>用户名与密码都填了才算能用的账号，缺一个引擎那侧就推不动。</summary>
    public bool IsUsable
        => !string.IsNullOrWhiteSpace(User) && !string.IsNullOrEmpty(Password);

    /// <summary>表里显示的密码占位：填了就是几个圆点，没填就是一条短横线。</summary>
    public string PasswordMask
        => Password.Length == 0 ? "—" : new string('\u25CF', Math.Min(Password.Length, 12));

    /// <summary>「能否观看」那一格显示成文字，空勾选框在只读表里不好认。</summary>
    public string CanViewText
        => CanView ? L.T("c027-004") : L.T("c027-011");

    public AuthAccount ToModel()
        => new()
        {
            Id = Id,
            User = User.Trim(),
            Password = Password,
            CanView = CanView,
        };

    public AuthAccountItemViewModel Clone()
        => new(ToModel());

    /// <summary>编辑窗确认后把改动写回原行，避免整张表重建、丢掉选中态。</summary>
    public void CopyFrom(AuthAccountItemViewModel other)
    {
        User = other.User;
        Password = other.Password;
        CanView = other.CanView;
    }
}
