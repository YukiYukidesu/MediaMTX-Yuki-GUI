using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 关系图两列的定位面板：每个子元素按自己那份 <see cref="IMapNode"/> 的坐标摆放。
/// </summary>
/// <remarks>
/// 没有用 Canvas 加附加属性绑定：节点坐标全部由视图模型算好，这里只要照着摆，
/// 不必在 XAML 里给每个容器挂一对 Canvas.Left / Canvas.Top。
/// <see cref="LayoutVersion"/> 每次重算坐标后自增，用来把「位置变了」告诉面板——
/// 子元素的 DataContext 还是原来那个对象，附加属性不会自己失效。
/// <para>
/// 缩放窗口会触发重新装箱，节点坐标一帧一个样；直接 Arrange 到新位置就是「瞬移」。
/// 所以位移走补偿：布局立刻落到新位置，视觉上先扣住「新旧位置差」的平移，
/// 再交给 <see cref="MapMotion"/> 逐帧放回 0，节点就是飞过去而不是跳过去。
/// 位移只存在那一张表里，连线层查的是同一份，点和线不会各自飞。
/// </para>
/// </remarks>
public sealed class RelationCanvas : Panel
{
    /// <summary>每个子元素上一次被摆到哪儿（布局坐标，不含平移补偿）。</summary>
    private readonly Dictionary<Control, Point> _placed = new();

    /// <summary>子元素 → 它的平移补偿对象：逐帧改数值，不每帧新建。</summary>
    private readonly Dictionary<Control, TranslateTransform> _transforms = new();

    /// <summary>上次清点时的子元素个数：个数没变就不必扫一遍找残留。</summary>
    private int _count = -1;

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
        bool flying = false;

        foreach (Control child in Children)
        {
            if (child.DataContext is not IMapNode node)
            {
                child.Arrange(new Rect(0, 0, child.DesiredSize.Width, child.DesiredSize.Height));
                continue;
            }

            double width = Math.Max(node.Width, child.DesiredSize.Width);
            double height = Math.Max(node.Height, child.DesiredSize.Height);
            Point target = new(node.X, node.Y);

            child.Arrange(new Rect(target.X, target.Y, width, height));

            if (!_placed.TryGetValue(child, out Point previous))
            {
                // 新来的节点没有旧位置：直接落位，不从别处飞进来
                _placed[child] = target;
                continue;
            }

            _placed[child] = target;

            // 上一段还没飞完的补偿从表里接上，否则会跳回布局位置重飞
            Vector shift = new Vector(previous.X - target.X, previous.Y - target.Y) + MapMotion.Shared.Get(node.Key);

            if (Math.Abs(shift.X) < 0.01 && Math.Abs(shift.Y) < 0.01) continue;

            MapMotion.Shared.Set(node.Key, shift);
            flying = true;
        }

        Prune();

        if (flying) MapMotion.Shared.Start(TopLevel.GetTopLevel(this));

        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        MapMotion.Shared.Moved += OnMoved;
    }

    /// <summary>从可视化树上摘下来时把补偿清零：页面切走就没人喂动画帧了，别把节点定在半路。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        MapMotion.Shared.Moved -= OnMoved;
        MapMotion.Shared.Clear();

        foreach (Control child in Children) child.RenderTransform = null;

        _transforms.Clear();
        _count = -1;

        base.OnDetachedFromVisualTree(e);
    }


    /// <summary>位移推进了一帧：把每个节点此刻的补偿写进它的平移变换。</summary>
    private void OnMoved()
    {
        foreach (Control child in Children)
        {
            if (child.DataContext is not IMapNode node) continue;

            Vector shift = MapMotion.Shared.Get(node.Key);

            if (Math.Abs(shift.X) < 0.01 && Math.Abs(shift.Y) < 0.01)
            {
                if (child.RenderTransform is not null) child.RenderTransform = null;
                continue;
            }

            if (!_transforms.TryGetValue(child, out TranslateTransform? transform))
            {
                transform = new TranslateTransform();
                _transforms[child] = transform;
            }

            transform.X = shift.X;
            transform.Y = shift.Y;

            if (!ReferenceEquals(child.RenderTransform, transform)) child.RenderTransform = transform;
        }
    }

    /// <summary>子元素个数变了才扫一遍：把走了的从两个字典和位移表里摘掉。</summary>
    private void Prune()
    {
        if (_count == Children.Count) return;

        _count = Children.Count;

        HashSet<Control> alive = [.. Children];
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (Control child in _placed.Keys.Where(control => !alive.Contains(control)).ToList())
        {
            _placed.Remove(child);
        }

        foreach (Control child in _transforms.Keys.Where(control => !alive.Contains(control)).ToList())
        {
            child.RenderTransform = null;
            _transforms.Remove(child);
        }

        foreach (Control child in Children)
        {
            if (child.DataContext is IMapNode node) keys.Add(node.Key);
        }

        MapMotion.Shared.Prune(keys);
    }
}
