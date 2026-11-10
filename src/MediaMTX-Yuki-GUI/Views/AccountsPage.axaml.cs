using Avalonia.Controls;
using Avalonia.Interactivity;
using MediaMTX_Yuki_GUI.Controls;
using MediaMTX_Yuki_GUI.ViewModels;


using L = MediaMTX_Yuki_GUI.Services.Localization;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>账号页：一张账号表管所有推流身份，行内删除走气泡确认。</summary>
public partial class AccountsPage : UserControl
{
    public AccountsPage()
        => InitializeComponent();

    /// <summary>
    /// 删账号没有批量入口，行内删一次就够了，用气泡确认贴着那颗按钮弹。
    /// 直接删会让已配好的输入源勾选静默失效，所以必须拦一下。
    /// </summary>
    private void OnRemoveAccountClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AuthAccountItemViewModel item }) return;
        if (DataContext is not MainViewModel viewModel) return;

        Popconfirm.Show(
            (Control)sender,
            L.T("t007-004").Replace("{name}", item.User),
            L.T("t006-001"),
            L.T("t006-002"),
            () => viewModel.RemoveAccountCommand.Execute(item));
    }
}
