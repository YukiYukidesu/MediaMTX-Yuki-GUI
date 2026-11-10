using System;
using System.Windows.Input;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Styling;

using CommunityToolkit.Mvvm.Input;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 可平滑展开 / 收起的折叠组。
/// 高度用关键帧动画驱动，下面所有内容会被真实地推着走，而不是瞬间跳变。
/// </summary>
public class SmoothExpander : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SmoothExpander, string?>(nameof(Title));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SmoothExpander, bool>(
            nameof(IsExpanded),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> ExpandDurationProperty =
        AvaloniaProperty.Register<SmoothExpander, double>(nameof(ExpandDuration), 280d);

    public static readonly DirectProperty<SmoothExpander, ICommand> ToggleCommandProperty =
        AvaloniaProperty.RegisterDirect<SmoothExpander, ICommand>(
            nameof(ToggleCommand),
            o => o.ToggleCommand);

    private readonly ICommand toggleCommand;

    private Border? host;

    public SmoothExpander()
    {
        toggleCommand = new RelayCommand(Toggle);
    }

    static SmoothExpander()
    {
        IsExpandedProperty.Changed.AddClassHandler<SmoothExpander>((control, e) => control.OnIsExpandedChanged(e));
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public double ExpandDuration
    {
        get => GetValue(ExpandDurationProperty);
        set => SetValue(ExpandDurationProperty, value);
    }

    public ICommand ToggleCommand => toggleCommand;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        host = e.NameScope.Find<Border>("PART_ContentHost");

        if (host is null)
        {
            return;
        }

        host.Height = IsExpanded ? double.NaN : 0d;
        PseudoClasses.Set(":expanded", IsExpanded);
    }

    private void Toggle()
    {
        IsExpanded = !IsExpanded;
    }

    private void OnIsExpandedChanged(AvaloniaPropertyChangedEventArgs e)
    {
        bool expanded = e.NewValue is true;

        PseudoClasses.Set(":expanded", expanded);

        if (host is null)
        {
            return;
        }

        if (expanded)
        {
            Animate(0d, MeasureContentHeight());
        }
        else
        {
            Animate(CurrentHeight(), 0d);
        }
    }

    /// <summary>
    /// 把内容交给布局系统量一次自然高度。
    /// 量完立刻把高度交还给调用方去驱动动画。
    /// </summary>
    private double MeasureContentHeight()
    {
        if (host is null)
        {
            return 0d;
        }

        double width = host.Bounds.Width > 0 ? host.Bounds.Width : Bounds.Width;

        if (width <= 0)
        {
            width = 800d;
        }

        host.Height = double.NaN;
        host.InvalidateMeasure();
        host.Measure(new Size(width, double.PositiveInfinity));

        double height = host.DesiredSize.Height;

        return height > 0 ? height : 0d;
    }

    private double CurrentHeight()
    {
        if (host is null)
        {
            return 0d;
        }

        if (host.Bounds.Height > 0)
        {
            return host.Bounds.Height;
        }

        double height = host.Height;

        return double.IsNaN(height) ? 0d : height;
    }

    private void Animate(double from, double to)
    {
        if (host is null || Math.Abs(to - from) < 0.5d)
        {
            if (host is not null)
            {
                host.Height = to;
            }

            return;
        }

        Animation animation = new()
        {
            Duration = TimeSpan.FromMilliseconds(ExpandDuration),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(HeightProperty, from) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(HeightProperty, to) },
                },
            },
        };

        _ = animation.RunAsync(host);
    }
}
