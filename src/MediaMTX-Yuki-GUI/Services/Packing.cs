namespace MediaMTX_Yuki_GUI.Services;

// ====== 二维装箱 ======
// 用途：把一组不能旋转的矩形塞进固定画布，尽量紧凑；画布装不下时给出整体缩放比。
// 内层是 MaxRects（最大空闲矩形 + 短边贴合），外层靠「尺度不变」直接算缩放，不跑二分。
public readonly record struct PackItem(int Id, double Width, double Height);

/// <summary>摆放结果：左上角坐标，单位与输入一致。</summary>
public readonly record struct PackPlacement(int Id, double X, double Y);

/// <summary>整体结果；<see cref="Scale"/> 小于 1 表示画布装不下、需要整体缩小。</summary>
public sealed record PackResult(double Scale, IReadOnlyList<PackPlacement> Placements);

/// <summary>最大空闲矩形装箱器：一次实例只管一次布局。</summary>
internal sealed class MaxRectsPacker
{
    private readonly double _canvasWidth;
    private readonly double _canvasHeight;
    private readonly double _gap;
    private readonly List<(double X, double Y, double W, double H)> _free = new();

    public MaxRectsPacker(double canvasWidth, double canvasHeight, double gap)
    {
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        _gap = gap;
        _free.Add((0, 0, canvasWidth, canvasHeight));
    }

    /// <summary>逐个放好；任何一个放不下就返回 null（这一轮策略作废）。</summary>
    public List<PackPlacement>? Pack(IReadOnlyList<PackItem> ordered)
    {
        List<PackPlacement> placed = new(ordered.Count);

        foreach (PackItem item in ordered)
        {
            // 间隙摊到右侧与下侧，簇与簇之间就不会贴死
            double needWidth = item.Width + _gap;
            double needHeight = item.Height + _gap;

            if (!TryFindBest(needWidth, needHeight, out double bestX, out double bestY)) return null;

            placed.Add(new PackPlacement(item.Id, bestX, bestY));
            Carve(bestX, bestY, needWidth, needHeight);
        }

        return placed;
    }

    /// <summary>靠左靠上优先：同 y 取更左的，先铺满一行再往下，画布不会先长高再变宽。</summary>
    private bool TryFindBest(double width, double height, out double bestX, out double bestY)
    {
        bool found = false;
        double bestTop = double.MaxValue;
        double bestLeft = double.MaxValue;
        bestX = 0;
        bestY = 0;

        foreach ((double X, double Y, double W, double H) slot in _free)
        {
            if (slot.W < width || slot.H < height) continue;

            if (slot.Y > bestTop) continue;
            if (slot.Y == bestTop && slot.X >= bestLeft) continue;

            bestTop = slot.Y;
            bestLeft = slot.X;
            bestX = slot.X;
            bestY = slot.Y;
            found = true;
        }

        return found;
    }

    /// <summary>把占掉的区域从每个相交的空闲矩形里切出四块，再清掉被完全包含的碎片。</summary>
    private void Carve(double x, double y, double width, double height)
    {
        List<(double X, double Y, double W, double H)> next = new();

        foreach ((double X, double Y, double W, double H) slot in _free)
        {
            bool overlaps = x < slot.X + slot.W && x + width > slot.X && y < slot.Y + slot.H && y + height > slot.Y;

            if (!overlaps)
            {
                next.Add(slot);
                continue;
            }

            if (y > slot.Y) next.Add((slot.X, slot.Y, slot.W, y - slot.Y));
            if (y + height < slot.Y + slot.H) next.Add((slot.X, y + height, slot.W, slot.Y + slot.H - (y + height)));
            if (x > slot.X) next.Add((slot.X, slot.Y, x - slot.X, slot.H));
            if (x + width < slot.X + slot.W) next.Add((x + width, slot.Y, slot.X + slot.W - (x + width), slot.H));
        }

        // 去掉被别的碎片完全盖住的：留着只会让候选位置越来越多
        List<(double X, double Y, double W, double H)> kept = new();

        for (int i = 0; i < next.Count; i++)
        {
            bool covered = false;

            for (int j = 0; j < next.Count && !covered; j++)
            {
                if (i == j) continue;

                (double X, double Y, double W, double H) a = next[i];
                (double X, double Y, double W, double H) b = next[j];

                covered = a.X >= b.X && a.Y >= b.Y && a.X + a.W <= b.X + b.W && a.Y + a.H <= b.Y + b.H;
            }

            if (!covered) kept.Add(next[i]);
        }

        _free.Clear();
        _free.AddRange(kept);
    }
}

/// <summary>装箱求解：多种排序各排一遍，取能装得最下的那种。</summary>
public static class PackSolver
{
    /// <summary>求解；<paramref name="items"/> 少于两种排序时会自动补上面积序与宽度序。</summary>
    public static PackResult Solve(double canvasWidth, double canvasHeight, IReadOnlyList<PackItem> items, double gap)
    {
        if (items.Count == 0) return new PackResult(1, []);

        List<PackItem> areaOrder = [.. items.OrderByDescending(item => item.Width * item.Height)];
        List<PackItem> heightOrder = [.. items.OrderByDescending(item => item.Height).ThenByDescending(item => item.Width)];
        List<PackItem> widthOrder = [.. items.OrderByDescending(item => item.Width).ThenByDescending(item => item.Height)];

        List<PackPlacement>? best = null;
        double bestScale = 0;

        foreach (List<PackItem> order in new[] { areaOrder, heightOrder, widthOrder })
        {
            List<PackPlacement>? placed = new MaxRectsPacker(canvasWidth, canvasHeight, gap).Pack(order);

            if (placed is null) continue;

            // 尺度不变：这批矩形的包围盒定下来，缩放比就定了，不用二分
            double contentWidth = 0;
            double contentHeight = 0;

            foreach (PackPlacement placement in placed)
            {
                PackItem item = items[placement.Id];
                contentWidth = Math.Max(contentWidth, placement.X + item.Width);
                contentHeight = Math.Max(contentHeight, placement.Y + item.Height);
            }

            if (contentWidth <= 0 || contentHeight <= 0) continue;

            double scale = Math.Min(canvasWidth / contentWidth, canvasHeight / contentHeight);

            if (scale <= bestScale) continue;

            bestScale = scale;
            best = placed;
        }

        return best is null ? new PackResult(0, []) : new PackResult(Math.Min(1, bestScale), best);
    }
}