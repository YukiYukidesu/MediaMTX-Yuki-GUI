using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 滚轮改成缓动：接管所有 ScrollViewer 的滚轮事件，自己算目标偏移、逐帧追上去，
/// 并把默认处理掐掉。Avalonia 自带的实现是直接把 Offset 一赋了事，
/// 一格滚轮就是一次瞬移，在高刷屏上看着很生硬。
///
/// 放在隧道阶段由窗口统一拦：滚轮该落到哪个 ScrollViewer 由这里自己判断，
/// 判断不了的（里面已经滚到头、外面也滚不动）就原样放行，交给内层控件。
/// </summary>
public static class SmoothScroll
{
    /// <summary>一格滚轮的像素数，跟系统默认手感对齐。</summary>
    private const double Step = 50;

    /// <summary>
    /// 朝目标的逼近速率（每秒的衰减指数）。用「按真实时间归一化的指数逼近」：
    /// 连着滚的时候目标一路累加，不用重开补间，速度就不会断；
    /// 关键是每帧走过的距离按真实 dt 换算，帧间隔抖到 19 毫秒也不会忽快忽慢——
    /// 之前写死「每帧 25%」，帧距一抖，位移就跟着抖，看着就是一下一下地顿。
    /// </summary>
    private const double Chase = 24;

    /// <summary>真实时间的最后一帧最多按这么大算，免得卡顿一下后追出很远。</summary>
    private const double MaxFrameSeconds = 0.05;

    /// <summary>离目标不到这么多像素就直接落位，免得无休止地追一个无限小的差值。</summary>
    private const double Settle = 0.4;

    /// <summary>每个视图的滚动动画状态；视图回收时自动丢弃。</summary>
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollAnimation> Animations = new();

    private static bool _installed;

    /// <summary>
    /// 在窗口上挂一次就够：PopupRoot 也是 TopLevel，弹出层里的滚动一样吃得到。
    /// </summary>
    public static void Install()
    {
        if (_installed) return;

        _installed = true;
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>(OnWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnWheel(TopLevel top, PointerWheelEventArgs args)
    {
        if (args.Delta.Y == 0 && args.Delta.X == 0) return;
        if (args.Source is not Visual source) return;

        // 滚轮增量与偏移是反的：向下滚时 Delta 为负，偏移要往前加。
        // 按住 Shift 则把纵向的滚轮量借给横向。
        // 普通鼠标滚轮只有 Delta.Y，Delta.X 是 0——横向那一路得把纵向的量借过来，
        // 否则「纵向没得滚、横向能滚」这个最需要横滚的场面反而纹丝不动。
        bool horizontalOnly = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
        double vertical = -args.Delta.Y;
        double horizontal = horizontalOnly || args.Delta.X == 0 ? -args.Delta.Y : -args.Delta.X;

        bool byPath = ScrollPath(source, horizontalOnly, vertical, horizontal, out ScrollViewer? target, out double move, out bool verticalMove);
        bool byGroup = !byPath && ScrollGroup(top, vertical, out target, out move, out verticalMove);
        if (byPath || byGroup)
        {
            args.Handled = true;
            Drive(target!, move, verticalMove);
        }
    }

    /// <summary>
    /// 沿事件源往上找：纵向优先，纵向到头才转横向。
    /// 一个都找不到就返回 false，让事件继续往里走，不抢内层控件自己的滚轮。
    /// </summary>
    private static bool ScrollPath(Visual source, bool horizontalOnly, double vertical, double horizontal,
        out ScrollViewer? target, out double move, out bool verticalMove)
    {
        target = null;
        move = 0;
        verticalMove = false;

        for (Visual? v = source; v is not null; v = v.GetVisualParent())
        {
            if (v is not ScrollViewer viewer) continue;

            if (!horizontalOnly && CanMove(viewer, vertical, false))
            {
                target = viewer;
                move = vertical * Step;
                verticalMove = true;
                return true;
            }

            if (horizontal != 0 && CanMove(viewer, horizontal, true))
            {
                target = viewer;
                move = horizontal * Step;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 路径上一个能滚的都没有时（典型情形：鼠标停在行首的勾选框、备注那些固定段上，
    /// 它们根本不在中段那层 ScrollViewer 里面），回头去找列表那一组。
    /// 组内每一行的中段偏移都是同步的，滚哪一行看着都一样，所以随便挑一条有横向余量的即可。
    /// </summary>
    private static bool ScrollGroup(TopLevel top, double vertical, out ScrollViewer? target, out double move, out bool verticalMove)
    {
        target = null;
        move = 0;
        verticalMove = false;

        foreach (ScrollViewer viewer in top.GetVisualDescendants().OfType<ScrollViewer>())
        {
            if (ColumnScroll.GetSyncGroup(viewer) is not { Length: > 0 }) continue;
            if (!CanMove(viewer, vertical, true)) continue;

            target = viewer;
            move = vertical * Step;
            return true;
        }

        return false;
    }

    /// <summary>朝这个方向还有没有余量。留 1 像素——滚到底常因舍入差一点点。</summary>
    private static bool CanMove(ScrollViewer viewer, double delta, bool horizontal)
    {
        if (horizontal)
        {
            if (viewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled) return false;
        }
        else if (viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
        {
            return false;
        }

        double limit = Limit(viewer, !horizontal);
        if (limit < 1) return false;

        double current = horizontal ? viewer.Offset.X : viewer.Offset.Y;

        // delta > 0 表示偏移要往大走（内容往下 / 往右），所以要看「还没到底」；
        // delta < 0 才是往回走，看「已经离开起点」。
        return delta > 0 ? current < limit - 1 : current > 1;
    }

    /// <summary>朝这个方向最多能滚多远。参数一律按「是不是纵向」传，别再混着来。</summary>
    private static double Limit(ScrollViewer viewer, bool vertical)
    {
        double extent = vertical ? viewer.Extent.Height : viewer.Extent.Width;
        double viewport = vertical ? viewer.Viewport.Height : viewer.Viewport.Width;

        return Math.Max(0, extent - viewport);
    }

    /// <summary>
    /// 接手一次滚动。连着滚的时候目标值一路累加，不重头来——
    /// 每次从当前位置重新起步的话，越滚越慢，手感发黏。
    /// </summary>
    private static void Drive(ScrollViewer viewer, double move, bool vertical)
    {
        ScrollAnimation state = Animations.GetOrCreateValue(viewer);

        double limit = Limit(viewer, vertical);
        double current = vertical ? viewer.Offset.Y : viewer.Offset.X;
        double anchor = state.Running && state.Vertical == vertical ? state.Target : current;

        state.Vertical = vertical;
        state.Target = Math.Clamp(anchor + move, 0, limit);

        if (state.Running) return;

        state.Running = true;
        state.Last = null;
        RequestFrame(viewer, state);
    }

    /// <summary>
    /// 下一帧再来一次。用 RequestAnimationFrame 而不是 DispatcherTimer：
    /// 定时器的精度只有十几毫秒，还跟渲染帧错开，看着就是一格一格地挪；
    /// 帧回调是随渲染走的，屏幕多少赫兹它就多少帧，位移接在上一次的后面，滑得才连。
    /// </summary>
    private static void RequestFrame(ScrollViewer viewer, ScrollAnimation state)
    {
        if (TopLevel.GetTopLevel(viewer) is not { } top)
        {
            state.Stop();
            return;
        }

        top.RequestAnimationFrame(stamp => Advance(viewer, state, stamp));
    }

    private static void Advance(ScrollViewer viewer, ScrollAnimation state, TimeSpan stamp)
    {
        if (!state.Running) return;

        double limit = Limit(viewer, state.Vertical);
        double target = Math.Clamp(state.Target, 0, limit);
        double current = state.Vertical ? viewer.Offset.Y : viewer.Offset.X;
        double delta = target - current;

        if (Math.Abs(delta) < Settle)
        {
            Set(viewer, state.Vertical, target);
            state.Stop();
            return;
        }

        // 1 - e^(-速率 * dt)：与帧率无关的逼近系数。帧快帧慢走过去的时间一样，
        // 手感才不会被渲染帧的抖动带跑。
        double seconds = Math.Min(state.Elapsed(stamp), MaxFrameSeconds);
        double follow = 1 - Math.Exp(-Chase * seconds);

        Set(viewer, state.Vertical, current + delta * follow);

        RequestFrame(viewer, state);
    }

    private static void Set(ScrollViewer viewer, bool vertical, double value)
    {
        Vector offset = viewer.Offset;
        viewer.Offset = vertical ? new Vector(offset.X, value) : new Vector(value, offset.Y);
    }

    /// <summary>一次滚动的追赶状态。</summary>
    private sealed class ScrollAnimation
    {
        public TimeSpan? Last;
        public double Target;
        public bool Vertical;
        public bool Running;

        /// <summary>距上一帧过去的秒数。首帧没有参照，按 60 帧的回退值算。</summary>
        public double Elapsed(TimeSpan stamp)
        {
            double seconds = Last is { } last ? (stamp - last).TotalSeconds : 1d / 60;
            Last = stamp;

            return seconds > 0 ? seconds : 1d / 240;
        }

        public void Stop()
        {
            Running = false;
            Target = 0;
            Last = null;
        }
    }
}
