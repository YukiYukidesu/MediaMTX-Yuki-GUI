using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Windowing;
using MediaMTX_Yuki_GUI.Services;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>
/// 一个只有「确认 / 取消」的小窗。
/// 模式切换这种会重启引擎、还会顺手停掉录制的操作，必须先在这里把代价讲清楚再放行。
/// </summary>
public sealed partial class ConfirmWindow : FAAppWindow
{
    private readonly string _message;

    private readonly string _okText;

    public ConfirmWindow(string title, string message, string okText, bool destructive = false)
    {
        InitializeComponent();

        // 弹窗跟主窗用同一套背景材质，不然主窗是云母、弹出的框是纯白，像两个程序
        WindowBackdrop.Attach(this);

        WindowTitle = title;
        _message = message;
        _okText = okText;
        Destructive = destructive;

        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);

        DataContext = this;

        CanMaximize = false;
    }

    // 这几个值都是构造时定死的，之后不再变，所以不实现 INotifyPropertyChanged：
    // DataContext 在赋值之后才挂上，绑定求值一次就够了。

    public string WindowTitle { get; }

    public string Message => _message;

    public string OkText => _okText;

    /// <summary>true 表示这次确认是有代价的（红底按钮）。</summary>
    public bool Destructive { get; }

    public IRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    private void Confirm()
        => Close(true);

    private void Cancel()
        => Close(false);
}
