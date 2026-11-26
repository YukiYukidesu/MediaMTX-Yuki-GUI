using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>一条连线的几何与状态。视图模型算好端点与状态，控件只负责照着画。</summary>
/// <param name="X1">起点 X，源节点右边缘中点。</param>
/// <param name="Y1">起点 Y。</param>
/// <param name="X2">终点 X，目标节点左边缘中点。</param>
/// <param name="Y2">终点 Y。</param>
/// <param name="State">链路状态，决定线色与虚实。</param>
public readonly record struct RelationLinkShape(double X1, double Y1, double X2, double Y2, MapLinkState State);

/// <summary>关系图的连线层：压在节点下面的一层贝塞尔曲线。</summary>
/// <remarks>
/// 颜色按 <see cref="MapLinkState"/> 去资源里取，与胶囊标签同一套做法：
/// 资源键是 RelationLink{状态}，取不到时退回一组内置色，界面不至于画不出线。
/// </remarks>
public sealed class RelationLinks : Control
{
    /// <summary>要画的连线。整体替换一次就重绘一次。</summary>
    public static readonly StyledProperty<IReadOnlyList<RelationLinkShape>?> LinksProperty =
        AvaloniaProperty.Register<RelationLinks, IReadOnlyList<RelationLinkShape>?>(nameof(Links));

    private static readonly IBrush FallbackActive = new SolidColorBrush(Color.Parse("#2E9E5B"));

    private static readonly IBrush FallbackPending = new SolidColorBrush(Color.Parse("#4C8AC9"));

    private static readonly IBrush FallbackError = new SolidColorBrush(Color.Parse("#C43B36"));

    private static readonly IBrush FallbackOff = new SolidColorBrush(Color.Parse("#B4BAC1"));

    /// <summary>虚线：断掉的链路用，短划比实线更容易看出「没接上」。</summary>
    private static readonly IDashStyle Dash = new DashStyle(new double[] { 5, 4 }, 0);

    static RelationLinks()
    {
        AffectsRender<RelationLinks>(LinksProperty);
    }

    /// <summary>要画的连线。</summary>
    public IReadOnlyList<RelationLinkShape>? Links
    {
        get => GetValue(LinksProperty);
        set => SetValue(LinksProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Links is not { Count: > 0 } links) return;

        Pen active = new(Find("RelationLinkActive", FallbackActive), 2.4);
        Pen pending = new(Find("RelationLinkPending", FallbackPending), 2, Dash);
        Pen error = new(Find("RelationLinkError", FallbackError), 2, Dash);
        Pen off = new(Find("RelationLinkOff", FallbackOff), 1.6, Dash);

        foreach (RelationLinkShape link in links)
        {
            Pen pen = link.State switch
            {
                MapLinkState.Active  => active,
                MapLinkState.Pending => pending,
                MapLinkState.Error   => error,
                _                    => off,
            };

            context.DrawGeometry(null, pen, Curve(link));
        }
    }

    /// <summary>左右两列之间的三次贝塞尔：两端水平进出，中间不用拐直角。</summary>
    private static StreamGeometry Curve(RelationLinkShape link)
    {
        double span = Math.Max(24, (link.X2 - link.X1) * 0.5);

        StreamGeometry geometry = new();

        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(link.X1, link.Y1), false);
            context.CubicBezierTo(
                new Point(link.X1 + span, link.Y1),
                new Point(link.X2 - span, link.Y2),
                new Point(link.X2, link.Y2));
            context.EndFigure(false);
        }

        return geometry;
    }

    /// <summary>按资源键取笔刷；资源还没加载时退回内置色。</summary>
    private IBrush Find(string key, IBrush fallback)
    {
        ThemeVariant theme = ActualThemeVariant;

        if (this.TryGetResource(key, theme, out object? value) && value is IBrush own) return own;

        if (Application.Current is { } app && app.TryGetResource(key, theme, out object? global) && global is IBrush shared) return shared;

        return fallback;
    }
}