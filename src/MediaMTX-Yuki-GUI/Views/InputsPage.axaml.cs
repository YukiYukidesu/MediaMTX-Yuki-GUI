using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MediaMTX_Yuki_GUI.Controls;
using MediaMTX_Yuki_GUI.ViewModels;
using L = MediaMTX_Yuki_GUI.Services.Localization;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>输入源页：工具栏 + 空状态 + 推流入口列表。</summary>
public partial class InputsPage : UserControl
{
    public InputsPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 行内删除先弹一句确认。
    /// </summary>
    /// <remarks>
    /// 用事件而不是 Command，是因为确认气泡要贴着这一颗按钮弹，
    /// 而 Command 拿不到「按的是谁」。删的是一条配置，误触就得重填一遍，
    /// 值得多问一句。
    /// </remarks>
    private void OnRemoveInputClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: InputItemViewModel item }) return;
        if (DataContext is not MainViewModel viewModel) return;

        Popconfirm.Show(
            (Control)sender,
            L.T("t002-011").Replace("{name}", item.DisplayName),
            L.T("t006-001"),
            L.T("t006-002"),
            () => viewModel.RemoveInputCommand.Execute(item));
    }
}
