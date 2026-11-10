using Avalonia;
using Avalonia.Styling;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>把「跟随系统 / 浅色 / 深色」翻译成 Avalonia 的主题切换。</summary>
/// <remarks>
/// WPF 版要挂 SystemThemeWatcher、还要在窗口未 Loaded 时避免摘监听报错；
/// Avalonia 把「跟随系统」做成了 ThemeVariant.Default，系统一变框架自己跟，
/// 所以这里只剩一次赋值，窗口参数也就不需要了。
/// </remarks>
public static class ThemeService
{
    /// <summary>配置里用的键，与 app.json 的 ui.theme 一一对应。</summary>
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    /// <summary>应用主题。跟随系统就交给 Avalonia，选了固定主题就钉死浅色或深色。</summary>
    public static void Apply(string? theme)
    {
        Application? app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Dark
                : ThemeVariant.Default;
    }
}
