using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MediaMTX_Yuki_GUI;

/// <summary>应用级资源与首窗创建。主题由 App.axaml 的 RequestedThemeVariant 与 ThemeService 共同决定。</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 滚轮统一走缓动。挂在窗口那一级，弹出层里也跟着生效，不用逐页加。
            Controls.SmoothScroll.Install();

            // 语言包要在建窗之前加载，否则第一帧取到的全是键名。
            Services.Localization.Instance.SetCulture(Services.Localization.FallbackCulture);

            // 窗口、托盘、空闲回收都是活得比窗口久的东西，统一交给 AppShell
            Services.AppShell.Initialize(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
