using Avalonia;
using Avalonia.Controls;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 关系图两列的定位面板：每个子元素按自己那份 <see cref="IMapNode"/> 的坐标摆放。
/// </summary>
/// <remarks>
/// 没有用 Canvas 加附加属性绑定：节点坐标全部由视图模型算好，这里只要照着摆，
/// 不必在 XAML 里给每个容器挂一对 Canvas.Left / Canvas.Top。
/// <see cref="LayoutVersion"/> 每次重算坐标后自增，用来把「位置变了」告诉面板——
/// 子元素的 DataContext 还是原来那个对象，附加属性不会自己失效。
/// </remarks>
public sealed class RelationCanvas : Panel
{
    /// <summary>布局版本号，每次重算坐标后加一。</summary>
    public static readonly StyledProperty<int> LayoutVersionProperty =
        AvaloniaProperty.Register<RelationCanvas, int>(nameof(LayoutVersion));

    static RelationCanvas()
    {
        AffectsArrange<RelationCanvas>(LayoutVersionProperty);
    }

    /// <summary>布局版本号，每次重算坐标后加一。</summary>
    public int LayoutVersion
    {
        get => GetValue(LayoutVersionProperty);
        set => SetValue(LayoutVersionProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double right = 0;
        double bottom = 0;

        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);

            if (child.DataContext is IMapNode node)
            {
                // 宽度取「算出来的」与「控件量出来的」较大者：估算偏小时不会把字截断
                right = Math.Max(right, node.X + Math.Max(node.Width, child.DesiredSize.Width));
                bottom = Math.Max(bottom, node.Y + Math.Max(node.Height, child.DesiredSize.Height));
                continue;
            }

            right = Math.Max(right, child.DesiredSize.Width);
            bottom = Math.Max(bottom, child.DesiredSize.Height);
        }

        return new Size(right, bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (Control child in Children)
        {
            if (child.DataContext is IMapNode node)
            {
                double width = Math.Max(node.Width, child.DesiredSize.Width);
                double height = Math.Max(node.Height, child.DesiredSize.Height);

                child.Arrange(new Rect(node.X, node.Y, width, height));
                continue;
            }

            child.Arrange(new Rect(0, 0, child.DesiredSize.Width, child.DesiredSize.Height));
        }

        return finalSize;
    }
}