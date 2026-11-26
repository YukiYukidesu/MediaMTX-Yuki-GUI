namespace MediaMTX_Yuki_GUI.Services;

// ====== 最紧凑装箱 ======
// 关键性质：布局结构与尺度无关，所以每种排布策略的缩放比可以直接算
//     s = min(画布宽 / 包围盒宽, 画布高 / 包围盒高)
// 取所有策略里最大的 s，不需要二分也不需要步进扫描。
// 内层用 MaxRects（固定宽度、高度给足）：矩形可以互相嵌合，不会像按列堆那样留出列间空白。
// 宽度取「让包围盒比例接近画布比例」的估算值再试几个倍数，避免画布被拉成长条。

/// <summary>一种排布的结果：缩放系数、每个矩形的位置、包围盒尺寸（都是 100% 尺度）。</summary>
public sealed record TightResult(
    double Scale,
    IReadOnlyList<PackPlacement> Placements,
    double Width,
    double Height);

public static class TightPacker
{
    /// <summary>把矩形装进画布：包围盒尽量小，画布装不下时给出整体缩放比。</summary>
    public static TightResult Pack(
        double canvasWidth,
        double canvasHeight,
        IReadOnlyList<PackItem> items,
        double gap)
    {
        double area = 0;
        double widest = 0;
        double tallest = 0;

        foreach (PackItem item in items)
        {
            area += (item.Width + gap) * (item.Height + gap);
            widest = Math.Max(widest, item.Width + gap);
            tallest = Math.Max(tallest, item.Height + gap);
        }

        // 让包围盒比例接近画布比例的宽度估算：面积相同、比例一致
        double guess = Math.Max(widest, Math.Sqrt(area * canvasWidth / Math.Max(canvasHeight, 1)));
        double height = Math.Max(tallest, area / Math.Max(guess, 1) * 2 + tallest);

        TightResult? best = null;

        foreach (int[] order in Strategies(items))
        {
            foreach (double factor in new[] { 0.85, 1.0, 1.25, 1.6 })
            {
                double width = Math.Max(widest, guess * factor);
                MaxRectsPacker packer = new(width, height, gap);
                List<PackItem> ordered = [.. order.Select(index => items[index])];
                List<PackPlacement>? placed = packer.Pack(ordered);

                if (placed is null) continue;

                (double boundsWidth, double boundsHeight) = Bounds(placed, items);
                double scale = Math.Min(canvasWidth / boundsWidth, canvasHeight / boundsHeight);

                if (best is null || scale > best.Scale) best = new TightResult(scale, placed, boundsWidth, boundsHeight);
            }
        }

        return best ?? new TightResult(0.1, [.. items.Select(item => new PackPlacement(item.Id, 0, 0))], widest, tallest);
    }

    /// <summary>几种排序：面积降序、高降序、宽降序、原序。</summary>
    private static List<int[]> Strategies(IReadOnlyList<PackItem> items)
    {
        int count = items.Count;
        int[] indices = [.. Enumerable.Range(0, count)];

        return
        [
            [.. indices.OrderByDescending(index => items[index].Width * items[index].Height)],
            [.. indices.OrderByDescending(index => items[index].Height)],
            [.. indices.OrderByDescending(index => items[index].Width)],
            [.. indices],
        ];
    }

    /// <summary>包围盒：最右与最下，末尾不加间距。</summary>
    private static (double Width, double Height) Bounds(
        IReadOnlyList<PackPlacement> placed,
        IReadOnlyList<PackItem> items)
    {
        Dictionary<int, PackItem> byId = [];

        foreach (PackItem item in items)
        {
            byId[item.Id] = item;
        }

        double width = 0;
        double height = 0;

        foreach (PackPlacement placement in placed)
        {
            PackItem item = byId[placement.Id];

            width = Math.Max(width, placement.X + item.Width);
            height = Math.Max(height, placement.Y + item.Height);
        }

        return (Math.Max(width, 1), Math.Max(height, 1));
    }
}
