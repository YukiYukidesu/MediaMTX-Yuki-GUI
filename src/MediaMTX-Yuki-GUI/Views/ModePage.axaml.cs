using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>
/// 工作模式页：两张卡片把简洁与高级各给什么列清楚，切换按钮就在卡片里。
/// 单独成页是因为切换要重启引擎，摆在设置页里点一下太容易误触；
/// 确认弹窗与重启都走 ViewModel 的 SwitchModeCommand，这里只管排版。
/// </summary>
public partial class ModePage : UserControl
{
    public ModePage()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
