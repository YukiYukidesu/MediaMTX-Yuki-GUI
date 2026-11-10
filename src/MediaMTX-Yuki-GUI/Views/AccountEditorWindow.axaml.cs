using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Windowing;
using MediaMTX_Yuki_GUI.Services;
using MediaMTX_Yuki_GUI.ViewModels;


using L = MediaMTX_Yuki_GUI.Services.Localization;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>添加 / 编辑一个推流账号。模态小窗，不切走主界面。</summary>
public sealed partial class AccountEditorWindow : FAAppWindow, INotifyPropertyChanged
{
    private readonly AuthAccountItemViewModel _item;
    private readonly bool _isNew;
    private readonly IReadOnlyCollection<string> _otherUsers;

    private string _user = "";
    private string _password = "";
    private bool _canView;

    public AccountEditorWindow(
        AuthAccountItemViewModel item,
        bool isNew,
        IReadOnlyCollection<string> otherUsers)
    {
        InitializeComponent();

        // 小窗也上材质，跟主窗保持一致
        WindowBackdrop.Attach(this);

        _item = item;
        _isNew = isNew;
        _otherUsers = otherUsers;

        User = item.User;
        Password = item.Password;
        CanView = item.CanView;

        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        CancelCommand = new RelayCommand(Cancel);

        // 窗口自己就是 ViewModel，绑定全靠这一句
        DataContext = this;
    }

    /// <summary>
    /// 自己实现的通知事件。基类 AvaloniaObject 也有一个同名成员，但那是它对
    /// AvaloniaProperty 体系的通知；这里要的是 INotifyPropertyChanged 那一套。
    /// </summary>
    public new event PropertyChangedEventHandler? PropertyChanged;

    public string WindowTitle
        => _isNew ? L.T("c027-007") : L.T("c027-008");

    public string ConfirmText
        => _isNew ? L.T("c008-015") : L.T("c008-016");

    public IRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>用户名跟别的账号撞了。只给黄字提示，照样能保存。</summary>
    public string NameWarning
    {
        get
        {
            string trimmed = User.Trim();

            if (trimmed.Length == 0) return "";

            foreach (string other in _otherUsers)
            {
                if (string.Equals(other, trimmed, StringComparison.OrdinalIgnoreCase))
                    return L.T("c027-009").Replace("{name}", trimmed);
            }

            return "";
        }
    }

    public bool HasNameWarning
        => NameWarning.Length > 0;

    /// <summary>用户名不能空、密码也不能空，缺一个引擎那侧就推不动。</summary>
    public bool CanConfirm
        => User.Trim().Length > 0 && Password.Length > 0;

    public string User
    {
        get => _user;
        set
        {
            if (!Set(ref _user, value)) return;

            Raise(nameof(NameWarning));
            Raise(nameof(HasNameWarning));
            Raise(nameof(CanConfirm));
            ConfirmCommand?.NotifyCanExecuteChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (!Set(ref _password, value)) return;

            Raise(nameof(CanConfirm));
            ConfirmCommand?.NotifyCanExecuteChanged();
        }
    }

    public bool CanView
    {
        get => _canView;
        set => Set(ref _canView, value);
    }

    private void Confirm()
    {
        _item.User = User.Trim();
        _item.Password = Password;
        _item.CanView = CanView;

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

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

}
