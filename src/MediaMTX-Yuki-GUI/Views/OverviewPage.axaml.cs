using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>总览页：运行状态卡 + 转发关系列表。</summary>
public partial class OverviewPage : UserControl
{
    public OverviewPage()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
