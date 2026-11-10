using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace MediaMTX_Yuki_GUI;

/// <summary>
/// 让 XAML 里能写 <c>{loc:Loc c001-009}</c>，取的是 Localization 索引器里的当前语言文案。
/// </summary>
/// <remarks>
/// WPF 那版把绑定对象当返回值交给 XAML 引擎，Avalonia 走的是同一套思路：
/// ProvideValue 返回一个已经设好 Source 的 Binding，值随语言切换自动刷新。
/// </remarks>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    /// <summary>语言表里的键，例如 c001-009。</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding("[" + Key + "]")
        {
            Source = Services.Localization.Instance,
            Mode = BindingMode.OneWay,
        };
    }
}
