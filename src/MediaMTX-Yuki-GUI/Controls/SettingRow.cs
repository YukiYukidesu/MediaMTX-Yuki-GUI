using Avalonia;
using Avalonia.Controls;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 设置页里的一行：左边图标与标题副标题，右边放控件。
/// </summary>
/// <remarks>
/// 参考 Ghost Downloader 的设置项排版：图标 16px、标题 13px、副标题 11px 灰字，
/// 右侧控件贴右对齐。副标题留空时那一行自动收起来。
/// </remarks>
public class SettingRow : ContentControl
{
    /// <summary>行标题。</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    /// <summary>标题下面的灰字说明。</summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    /// <summary>标题下面那行黄字提醒，留空就不显示；排在红字上面。</summary>
    public static readonly StyledProperty<string?> CautionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Caution));

    /// <summary>标题后面是否跟一颗红色星号，表示这一项在特定档位下必填。</summary>
    public static readonly StyledProperty<bool> RequiredProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(Required));

    /// <summary>标题下面那行红字警告，留空就不显示。</summary>
    public static readonly StyledProperty<string?> WarningProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Warning));

    /// <summary>Segoe Fluent Icons 的字形，例如 &#xE790;。</summary>
    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Glyph));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? Caution
    {
        get => GetValue(CautionProperty);
        set => SetValue(CautionProperty, value);
    }

    /// <summary>为 true 时标题后面显示红色星号。</summary>
    public bool Required
    {
        get => GetValue(RequiredProperty);
        set => SetValue(RequiredProperty, value);
    }

    public string? Warning
    {
        get => GetValue(WarningProperty);
        set => SetValue(WarningProperty, value);
    }

    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}
