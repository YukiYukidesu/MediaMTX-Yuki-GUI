using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>
/// 统计页：入流与各路出网的码率合计。数据全靠主 ViewModel 的汇总属性，
/// 这里只负责排版，不额外持有状态。
/// </summary>
public partial class StatsPage : UserControl
{
    public StatsPage()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
