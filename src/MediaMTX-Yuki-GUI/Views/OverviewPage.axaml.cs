using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MediaMTX_Yuki_GUI.ViewModels;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>总览页：运行状态卡 + 转发关系图。</summary>
public partial class OverviewPage : UserControl
{
    public OverviewPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // 关系图要铺满这块面板：面板尺寸一变就把可用宽高告诉视图模型，由它重排整张图。
    // 宽度预留 16 给纵向滚动条——它出现时会吃掉内容区右侧，不预留就会把最右边的目标顶出可视区。
    private void OnMapHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SetMapViewport(Math.Max(0, e.NewSize.Width - 16), e.NewSize.Height);
        }
    }
}
