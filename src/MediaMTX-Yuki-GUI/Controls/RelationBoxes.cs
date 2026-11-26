using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>给每簇画一圈淡边：只用来观察装箱边界，不参与交互。</summary>
public sealed class RelationBoxes : Control
{
    public static readonly StyledProperty<IReadOnlyList<Rect>?> BoxesProperty =
        AvaloniaProperty.Register<RelationBoxes, IReadOnlyList<Rect>?>(nameof(Boxes));

    static RelationBoxes()
    {
        AffectsRender<RelationBoxes>(BoxesProperty);
    }

    /// <summary>要画的矩形；坐标与节点同一套画布坐标。</summary>
    public IReadOnlyList<Rect>? Boxes
    {
        get => GetValue(BoxesProperty);
        set => SetValue(BoxesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        IReadOnlyList<Rect>? boxes = Boxes;

        if (boxes is null || boxes.Count == 0) return;

        Pen pen = new(new SolidColorBrush(Color.Parse("#8CFFFFFF")), 2);

        foreach (Rect box in boxes)
        {
            context.DrawRectangle(null, pen, new RoundedRect(box, 8));
        }
    }
}