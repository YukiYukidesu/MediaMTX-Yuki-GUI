using System.Globalization;
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
            L.T("c004-019").Replace("{name}", item.DisplayName),
            L.T("l001-005"),
            L.T("b001-008"),
            () => viewModel.RemoveTargetCommand.Execute(item));
    }

    /// <summary>批量绑定：开个小窗选输入源，点了确定才动列表。</summary>
    private void OnBindTargetsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        int count = viewModel.SelectedCount;

        if (count == 0) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        string headline = L.T("l004-034").Replace("{n}", count.ToString(CultureInfo.InvariantCulture));
        BindTargetsWindow window = new(headline, viewModel.BindInputChoices);

        // 关窗后照 Confirmed 判断，不走 await —— 这一页没有异步事件处理器
        window.Closed += (_, _) =>
        {
            if (!window.Confirmed) return;
            if (window.Selected is not InputChoice choice) return;

            viewModel.BindSelectedTargets(choice.Id);
        };

        window.ShowDialog(owner);
    }
}
