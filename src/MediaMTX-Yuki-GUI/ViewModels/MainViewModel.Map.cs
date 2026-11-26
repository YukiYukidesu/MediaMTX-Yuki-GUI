using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia;
using MediaMTX_Yuki_GUI.Controls;
using MediaMTX_Yuki_GUI.Services;

namespace MediaMTX_Yuki_GUI.ViewModels;

// ====== 总览页的转发关系图 ======
// 左列源、右列目标，坐标全在这里算好，页面上的 RelationCanvas 只照着摆。
// 节点按 Id 复用：指标每两秒回填一次，重建时只改状态与坐标，不换对象，界面就不会闪。
public sealed partial class MainViewModel
{
    // 布局常量：节点是一颗胶囊（图标 + 名字），不是卡片，所以高度按胶囊算
    private const double MapNodeHeight = 24;
    private const double MapRowGap = 8;
    private const double MapBlockGap = 20;
    // 簇内：源右缘到目标左缘的距离；簇之间再拉开，才看得出「哪些是一组」
    private const double MapClusterGap = 72;
    private const double MapGridGap = 32;
    private const double MapPadding = 4;

    // 胶囊宽度的估算：字号 12，中文按整字、拉丁按半个字，再加图标、间距、两侧内边距与一点余量
    private const double MapNodeFontSize = 12;
    private const double MapNodeChrome = 36;
    private const double MapMinNodeWidth = 96;

    /// <summary>胶囊的最大宽度：名字再长也只到这里，多出来的用省略号截掉。</summary>
    private const double MapMaxNodeWidth = 240;

    /// <summary>一个簇最窄能到多少：低于这个宽度就不再多分一列，宁可少放一簇。</summary>
    private const double MapMinClusterWidth = 320;

    // 孤儿源右边那截断头虚线：够看出「它没接东西」，又不会让人以为连到了远处
    private const double MapLinkStub = 28;

    /// <summary>节点缓存。键是「s:源Id」或「t:目标Id」，用来在重建时找回同一个节点对象。</summary>
    private readonly Dictionary<string, RelationNode> _mapNodes = new(StringComparer.Ordinal);

    private bool _mapDirty;

    /// <summary>关系图区域的实际宽度：页面量到就写进来，图按它铺满整块画布。</summary>
    private double _mapViewportWidth;

    /// <summary>关系图区域的实际高度：内容比它矮时整张图垂直居中。</summary>
    private double _mapViewportHeight;

    /// <summary>左列：输入源节点，顺序与输入源列表一致。</summary>
    public ObservableCollection<RelationNode> MapSources { get; } = [];

    /// <summary>右列：输出目标节点，按所属源分块排列，孤儿目标接在末尾。</summary>
    public ObservableCollection<RelationNode> MapTargets { get; } = [];

    /// <summary>连线层的数据，整体替换一次就重绘一次。</summary>
    [ObservableProperty]
    private IReadOnlyList<RelationLinkShape> _mapLinks = [];

    /// <summary>画布宽度，等于右列最宽那个节点加留白。</summary>
    [ObservableProperty]
    private double _mapWidth = 520;

    /// <summary>画布高度，等于最后一个节点块的下边缘加留白。</summary>
    [ObservableProperty]
    private double _mapHeight = 200;

    /// <summary>布局版本号：坐标有变化就加一，通知画布重新排布。</summary>
    [ObservableProperty]
    private int _mapVersion;

    /// <summary>每簇一圈淡边的矩形，只用来观察布局边界。</summary>
    [ObservableProperty]
    private IReadOnlyList<Rect> _mapBoxes = [];

    /// <summary>图里一个节点都没有：页面显示空状态。</summary>
    public bool IsMapEmpty => MapSources.Count == 0 && MapTargets.Count == 0;

    // ====== 重建 ======

    /// <summary>页面把关系图区域的实际尺寸告诉视图模型，图据此铺满整块画布。</summary>
    public void SetMapViewport(double width, double height)
    {
        if (Math.Abs(width - _mapViewportWidth) < 0.5 && Math.Abs(height - _mapViewportHeight) < 0.5) return;

        _mapViewportWidth = width;
        _mapViewportHeight = height;
        RebuildMap();
    }


    /// <summary>按当前的源、目标与运行状态重算整张关系图。</summary>
    /// <remarks>
    /// 一个输入源连同它绑定的目标算一簇，孤儿目标自己算一簇；簇按网格横排，一行放不下就换行。
    /// 只做增量：已有节点改坐标与状态，新节点追加，消失的节点移除，界面不会闪一下。
    /// </remarks>
    private void RebuildMap()
    {
        bool engineRunning = IsEngineRunning;

        Dictionary<string, InputItemViewModel> byId = new(StringComparer.Ordinal);

        foreach (InputItemViewModel source in Inputs)
        {
            byId[source.Id] = source;
        }

        // 先把整张图拆成簇：普通簇是「一个源 + 它绑定的目标」，孤儿目标是只有右侧一颗的簇
        List<(InputItemViewModel? Source, List<TargetItemViewModel> Targets)> clusters = [];

        foreach (InputItemViewModel source in Inputs)
        {
            clusters.Add((source, [.. source.Outputs]));
        }

        foreach (TargetItemViewModel target in Targets)
        {
            if (!string.IsNullOrEmpty(target.InputSourceId) && byId.ContainsKey(target.InputSourceId)) continue;

            clusters.Add((null, [target]));
        }

        // 纯数学：胶囊宽度只由名字决定，每簇宽度 = 源宽 + 连线间隙 + 该簇最宽的目标
        double viewportWidth = _mapViewportWidth >= 200 ? _mapViewportWidth : 1200;
        double viewportHeight = _mapViewportHeight >= 120 ? _mapViewportHeight : 640;

        double[] sourceWidths = new double[clusters.Count];
        double[] targetWidths = new double[clusters.Count];
        double[] clusterWidths = new double[clusters.Count];
        double[] blockHeights = new double[clusters.Count];

        for (int index = 0; index < clusters.Count; index++)
        {
            (InputItemViewModel? source, List<TargetItemViewModel> targets) = clusters[index];
            double widest = MapMinNodeWidth;

            foreach (TargetItemViewModel target in targets)
            {
                widest = Math.Max(widest, EstimateNodeWidth(target.DisplayName));
            }

            sourceWidths[index] = source is null ? 0 : EstimateNodeWidth(source.DisplayName);
            targetWidths[index] = widest;
            clusterWidths[index] = source is null ? widest : sourceWidths[index] + MapClusterGap + widest;
            blockHeights[index] = ClusterHeight(targets.Count);
        }

        // 摆法只服务缩放比：高的先放，每簇塞进当前最矮的一列（列宽随该列最宽的簇走）
        List<int> order = [.. Enumerable.Range(0, clusters.Count).OrderByDescending(index => blockHeights[index])];

        List<PackPlacement> placements = [];
        double contentWidth = 0;
        double contentHeight = 0;
        double bestScale = 0;

        // 列数从 1 到 n 全试一遍：不预设任何上限，谁让包围盒的缩放比最大就选谁
        for (int candidate = 1; candidate <= Math.Max(1, clusters.Count); candidate++)
        {
            (double width, double height, List<PackPlacement> arranged) = StackWide(
                candidate,
                order,
                clusterWidths,
                blockHeights,
                MapGridGap);

            double scale = Math.Min(viewportWidth / width, viewportHeight / height);

            if (scale <= bestScale) continue;

            bestScale = scale;
            contentWidth = width;
            contentHeight = height;
            placements = arranged;
        }

        // 画布只包住内容：外层 Viewbox 负责等比缩放与居中，永远不滚动
        double mapWidth = contentWidth + MapPadding * 2;
        double mapHeight = contentHeight + MapPadding * 2;
        double left = MapPadding;
        double top = MapPadding;

        List<Rect> boxes = [];
        List<RelationLinkShape> links = [];
        HashSet<string> alive = new(StringComparer.Ordinal);

        foreach (PackPlacement placement in placements)
        {
            (InputItemViewModel? source, List<TargetItemViewModel> targets) = clusters[placement.Id];

            double clusterX = left + placement.X;
            double clusterY = top + placement.Y;
            double clusterWidth = clusterWidths[placement.Id];
            double sourceRight = clusterX + sourceWidths[placement.Id];
            double targetX = sourceRight + MapClusterGap;
            double blockHeight = blockHeights[placement.Id];
            double sourceY = clusterY + (blockHeight - MapNodeHeight) / 2;

            boxes.Add(new Rect(clusterX - MapPadding, clusterY - MapPadding, clusterWidth, blockHeight));

            if (source is not null)
            {
                // 胶囊宽度随名字走，到列宽上限就停（再长就靠省略号）
                double sourceNodeWidth = sourceWidths[placement.Id];

                RelationNode sourceNode = UpsertNode(
                    "s:" + source.Id,
                    true,
                    sourceRight - sourceNodeWidth,
                    sourceY,
                    sourceNodeWidth,
                    alive);
                FillSourceNode(sourceNode, source, engineRunning);

                // 一个目标都没绑：右边伸一小截断头虚线，一眼看出这条源没接东西
                if (targets.Count == 0)
                {
                    links.Add(new RelationLinkShape(
                        sourceRight,
                        sourceY + MapNodeHeight / 2,
                        sourceRight + MapLinkStub,
                        sourceY + MapNodeHeight / 2,
                        MapLinkState.Off));
                }
            }

            double targetY = clusterY;

            foreach (TargetItemViewModel target in targets)
            {
                double targetNodeWidth = Math.Max(MapMinNodeWidth, EstimateNodeWidth(target.DisplayName));

                RelationNode targetNode = UpsertNode(
                    "t:" + target.Id,
                    false,
                    targetX,
                    targetY,
                    targetNodeWidth,
                    alive);

                FillTargetNode(targetNode, target, engineRunning, source is null);

                if (source is not null)
                {
                    links.Add(new RelationLinkShape(
                        sourceRight,
                        sourceY + MapNodeHeight / 2,
                        targetX,
                        targetY + MapNodeHeight / 2,
                        LinkState(source, target, engineRunning)));
                }

                targetY += MapNodeHeight + MapRowGap;
            }
        }

        DropStaleNodes(alive);
        SyncNodeColumns();

        MapLinks = links;
        MapBoxes = boxes;
        MapWidth = mapWidth;
        MapHeight = mapHeight;

        if (_mapDirty)
        {
            _mapDirty = false;
            MapVersion++;
        }

        OnPropertyChanged(nameof(IsMapEmpty));
    }


    /// <summary>一簇的高度：目标竖排，源跟着这一块居中。</summary>
    private static double ClusterHeight(int targetCount)
    {
        int rows = Math.Max(1, targetCount);

        return rows * MapNodeHeight + (rows - 1) * MapRowGap;
    }

    /// <summary>按名字估一颗胶囊的宽度：中文按整字、拉丁按半字，再加图标与两侧留白。</summary>
    /// <summary>
    /// 把簇依次塞进当前最矮的一列，列宽取该列最宽的簇。
    /// 返回包围盒宽高与每簇的左上角；列宽不等，所以 X 要按前面各列的宽度累加。
    /// </summary>
    private static (double Width, double Height, List<PackPlacement> Placements) StackWide(
        int columns,
        List<int> order,
        double[] widths,
        double[] heights,
        double gap)
    {
        double[] columnWidths = new double[columns];
        double[] columnHeights = new double[columns];
        List<(int Id, int Column, double Y)> pending = [];

        foreach (int index in order)
        {
            int target = 0;

            for (int column = 1; column < columns; column++)
            {
                if (columnHeights[column] < columnHeights[target]) target = column;
            }

            pending.Add((index, target, columnHeights[target]));
            columnWidths[target] = Math.Max(columnWidths[target], widths[index]);
            columnHeights[target] += heights[index] + gap;
        }

        double[] columnX = new double[columns];
        double cursor = 0;

        for (int column = 0; column < columns; column++)
        {
            columnX[column] = cursor;
            cursor += columnWidths[column] + gap;
        }

        double totalHeight = 0;

        foreach (double height in columnHeights)
        {
            totalHeight = Math.Max(totalHeight, height);
        }

        List<PackPlacement> placements = [];

        foreach ((int Id, int Column, double Y) item in pending)
        {
            placements.Add(new PackPlacement(item.Id, columnX[item.Column], item.Y));
        }

        return (Math.Max(0, cursor - gap), Math.Max(0, totalHeight - gap), placements);
    }

    private static double EstimateNodeWidth(string title)
    {
        double textWidth = 0;

        foreach (char ch in title)
        {
            textWidth += ch > 0x2E7F ? MapNodeFontSize * 1.09 : MapNodeFontSize * 0.61;
        }

        return Math.Clamp(Math.Ceiling(textWidth) + MapNodeChrome, MapMinNodeWidth, MapMaxNodeWidth);
    }

    /// <summary>取一个节点：没有就新建并入列，有就把坐标挪过去；位置变了记一笔脏。</summary>
    private RelationNode UpsertNode(string key, bool isSource, double x, double y, double width, HashSet<string> alive)
    {
        alive.Add(key);

        if (!_mapNodes.TryGetValue(key, out RelationNode? node))
        {
            // 这里只登记、不入列：入列会触发绑定集合的变更回调，
            // 而重建过程正在遍历源、目标列表，边遍历边改会直接抛「集合已被修改」
            node = new RelationNode(key, isSource);
            _mapNodes[key] = node;
            _mapDirty = true;
        }
        else if (node.X != x || node.Y != y || node.Width != width)
        {
            _mapDirty = true;
        }

        node.X = x;
        node.Y = y;
        node.Width = width;
        node.Height = MapNodeHeight;

        return node;
    }

    /// <summary>把本轮的节点并进两列绑定集合：只在重建收尾做一次，避开重建过程中的遍历。</summary>
    private void SyncNodeColumns()
    {
        foreach (RelationNode node in _mapNodes.Values)
        {
            ObservableCollection<RelationNode> column = node.IsSource ? MapSources : MapTargets;

            if (column.Contains(node)) continue;

            column.Add(node);
        }
    }

    /// <summary>把这一轮没有出现的节点从两列里摘掉。</summary>
    private void DropStaleNodes(HashSet<string> alive)
    {
        if (_mapNodes.Count == alive.Count) return;

        List<string> stale = [];

        foreach (string key in _mapNodes.Keys)
        {
            if (!alive.Contains(key)) stale.Add(key);
        }

        foreach (string key in stale)
        {
            RelationNode node = _mapNodes[key];

            (node.IsSource ? MapSources : MapTargets).Remove(node);
            _mapNodes.Remove(key);
            _mapDirty = true;
        }
    }

    // ====== 节点内容 ======

    /// <summary>源节点：程序禁用灰叉、手动停用红叉、推流中绿点，其余蓝点（待机）。</summary>
    private static void FillSourceNode(RelationNode node, InputItemViewModel source, bool engineRunning)
    {
        node.Title = source.DisplayName;
        node.Tip = source.ProtocolText + " · " + source.PushOrPullUrl
                   + (source.HasRuntimeText ? "\n" + source.RuntimeText : string.Empty);

        NodeState state;

        if (source.RuntimeDisabled)
        {
            state = NodeState.Plain;
        }
        else if (!source.Enabled)
        {
            state = NodeState.Off;
        }
        else if (engineRunning && source.IsOnline)
        {
            state = NodeState.On;
        }
        else
        {
            state = NodeState.Idle;
        }

        ApplyState(node, state);
    }

    /// <summary>目标节点：孤儿目标照样按启用状态上色，未绑源这件事写在悬停提示里。</summary>
    private static void FillTargetNode(RelationNode node, TargetItemViewModel target, bool engineRunning, bool orphan)
    {
        node.Title = target.DisplayName;
        node.Tip = target.PlatformText + " · " + target.Server + "\n" + target.StateText;

        NodeState state;

        if (target.RuntimeDisabled)
        {
            state = NodeState.Plain;
        }
        else if (!target.Enabled)
        {
            state = NodeState.Off;
        }
        else if (engineRunning && target.IsErrored)
        {
            state = NodeState.Error;
        }
        else if (engineRunning && target.IsStreaming)
        {
            state = NodeState.On;
        }
        else
        {
            state = NodeState.Idle;
        }

        ApplyState(node, state);
    }

    /// <summary>把一档状态落到节点上：三个开关决定底色，未启用的两档同时把图标换成叉。</summary>
    private static void ApplyState(RelationNode node, NodeState state)
    {
        node.IsOn = state is NodeState.On;
        node.IsIdle = state is NodeState.Idle;
        node.IsError = state is NodeState.Error;
        node.IsOff = state is NodeState.Off;
        node.Glyph = state is NodeState.Off or NodeState.Plain ? "✕" : "●";
    }

    /// <summary>一条链路的状态：两端都通才算通，一端停用就算断。</summary>
    private static MapLinkState LinkState(InputItemViewModel source, TargetItemViewModel target, bool engineRunning)
    {
        if (!source.EffectiveEnabled || !target.EffectiveEnabled) return MapLinkState.Off;

        if (engineRunning && target.IsErrored) return MapLinkState.Error;

        if (engineRunning && source.IsOnline && target.IsStreaming) return MapLinkState.Active;

        return MapLinkState.Pending;
    }
}