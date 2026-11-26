using Avalonia.Interactivity;
using FluentAvalonia.UI.Windowing;
using MediaMTX_Yuki_GUI.ViewModels;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>批量绑定：选一条输入源，把勾选的输出目标全绑过去。</summary>
public partial class BindTargetsWindow : FAAppWindow
{
    public BindTargetsWindow(string headline, IReadOnlyList<InputChoice> choices)
    {
        InitializeComponent();

        HeadlineText.Text = headline;
        InputBox.ItemsSource = choices;

        // 有得选就默认选第一条，省一次点击；一条都没有就让确定键禁用
        if (choices.Count > 0)
        {
            InputBox.SelectedIndex = 0;
        }

        EmptyHint.IsVisible = choices.Count == 0;
        ConfirmButton.IsEnabled = choices.Count > 0;
    }

    /// <summary>弹窗里选中的输入源；取消或没得选时为 null。</summary>
    public InputChoice? Selected => InputBox.SelectedItem as InputChoice;

    /// <summary>点了「确定」才为 true；直接关窗或点取消都是 false。</summary>
    public bool Confirmed { get; private set; }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }
}
