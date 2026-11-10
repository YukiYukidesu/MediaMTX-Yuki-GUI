using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MediaMTX_Yuki_GUI.Controls;
using MediaMTX_Yuki_GUI.ViewModels;
using L = MediaMTX_Yuki_GUI.Services.Localization;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>输出目标页：工具栏 + 空状态 + 转发目标列表。</summary>
public partial class TargetsPage : UserControl
{
    public TargetsPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>行内删除先弹一句确认，理由同输入源页那边。</summary>
    private void OnRemoveTargetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: TargetItemViewModel item }) return;
        if (DataContext is not MainViewModel viewModel) return;

        Popconfirm.Show(
            (Control)sender,
            L.T("t003-013").Replace("{name}", item.DisplayName),
            L.T("t006-001"),
            L.T("t006-002"),
            () => viewModel.RemoveTargetCommand.Execute(item));
    }
}
