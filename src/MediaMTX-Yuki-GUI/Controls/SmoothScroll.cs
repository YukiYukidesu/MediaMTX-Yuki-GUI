using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 把滚轮改成「一格一跳、跳得还有点缓」的滚动：系统自带的滚动步长在 120 格滚轮上
/// 一按就飞出去半屏，这里自己接管，一格的位移固定为 <see cref="Step"/>，再补一段指数追赶。
///
/// 轴的优先级照社区通例来：**纵向永远优先，横向只认显式意图**
/// （Shift+滚轮、触控板的横向分量、自己拖滚动条或横向拖动列表）。
/// 垂直到头就停住，不会转成横向——以前那种「滚到底再滚一格，界面突然横着走」就是这么来的。
/// </summary>
public static class SmoothScroll
{
    /// <summary>滚轮一格走多远。</summary>
    private const double Step = 50;

    /// <summary>追赶系数：越大越紧跟，越小越飘。</summary>
    private const double Chase = 24;

    /// <summary>一帧最多算多少秒，免得掉帧之后一步跨过头。</summary>
    private const double MaxFrameSeconds = 0.05;

    /// <summary>两轴都近到这个距离就收尾，别磨那半个像素。</summary>
    private const double Settle = 0.4;

    private static readonly ConditionalWeakTable<object, ScrollAnimation> Animations = new();

    private static bool _installed;


    /// <summary>装上滚轮拦截；进程里调一次就够。</summary>
    public static void Install()
    {
        if (_installed) return;

        _installed = true;
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>(OnWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
    }


    private static void OnWheel(TopLevel top, PointerWheelEventArgs args)
    {
        if (args.Handled) return;
        if (args.Source is not Visual source) return;

        bool shift = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool nativeHorizontal = args.Delta.X != 0 && !shift;

        double vertical = shift ? 0 : -args.Delta.Y;
        double horizontal = nativeHorizontal ? -args.Delta.X : (shift ? -args.Delta.Y : 0);

        if (vertical == 0 && horizontal == 0) return;

        // 触控板斜着滑：两轴同时有量时先看有没有一个容器两轴都能滚，一次驱动它们
        if (vertical != 0 && horizontal != 0 && FindBoth(source, out ScrollViewer? both))
        {
            args.Handled = true;
            Drive(ScrollTarget.For(both!), horizontal * Step, vertical * Step);
            return;
        }

        bool moved = false;

        // 横向只认显式意图。列表那套列是表头与各行共用一份平移（ColumnPan），
        // 所以先看指针下面有没有这样的组，没有才去找真的横向滚动器。
        if (horizontal != 0)
        {
            if (FindPan(source, out ColumnPan.PanGroup? pan))
            {
                Drive(ScrollTarget.For(pan!), horizontal * Step, 0);
                moved = true;
            }
            else if (FindAxis(source, true, out ScrollViewer? sideways))
            {
                Drive(ScrollTarget.For(sideways!), horizontal * Step, 0);
                moved = true;
            }
        }

        // 纵向永远优先，而且滚到头就停住、不换轴：Handle 掉了就不会再落到上面那段
        if (vertical != 0 && FindAxis(source, false, out ScrollViewer? down))
        {
            Drive(ScrollTarget.For(down!), 0, vertical * Step);
            moved = true;
        }

        if (moved) args.Handled = true;
    }


    /// <summary>沿指针的父链找最内层那个「这一轴还能滚」的滚动器。</summary>
    private static bool FindAxis(Visual source, bool horizontal, out ScrollViewer? target)
    {
        for (Visual? v = source; v is not null; v = v.GetVisualParent())
        {
            if (v is not ScrollViewer viewer) continue;
            if (!AxisEnabled(viewer, horizontal)) continue;
            if (Limit(viewer, horizontal) < 1) continue;

            target = viewer;
            return true;
        }

        target = null;
        return false;
    }


    /// <summary>找最内层横竖都还能滚的滚动器（触控板斜滑用）。</summary>
    private static bool FindBoth(Visual source, out ScrollViewer? target)
    {
        for (Visual? v = source; v is not null; v = v.GetVisualParent())
        {
            if (v is not ScrollViewer viewer) continue;
            if (!AxisEnabled(viewer, true)) continue;
            if (!AxisEnabled(viewer, false)) continue;
            if (Limit(viewer, true) < 1) continue;
            if (Limit(viewer, false) < 1) continue;

            target = viewer;
            return true;
        }

        target = null;
        return false;
    }


    /// <summary>沿父链找那只负责横向平移的裁剪窗口（表头与各行共用同一个组）。</summary>
    private static bool FindPan(Visual source, out ColumnPan.PanGroup? target)
    {
        if (ColumnPan.TryFindGroup(source, out ColumnPan.PanGroup? group) && group.Limit >= 1)
        {
            target = group;
            return true;
        }

        target = null;
        return false;
    }


    private static bool AxisEnabled(ScrollViewer viewer, bool horizontal)
    {
        ScrollBarVisibility state = horizontal ? viewer.HorizontalScrollBarVisibility : viewer.VerticalScrollBarVisibility;
        return state != ScrollBarVisibility.Disabled;
    }


    private static double Limit(ScrollViewer viewer, bool horizontal) =>
        horizontal ? Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width) : Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);


    /// <summary>往里攒一格位移；已经在追的话只改终点，不重开一段补间。</summary>
    private static void Drive(ScrollTarget target, double moveX, double moveY)
    {
        ScrollAnimation state = Animations.GetOrCreateValue(target.Key);

        if (!state.Running)
        {
            state.TargetX = target.X;
            state.TargetY = target.Y;
        }

        if (moveX != 0) state.TargetX = Math.Clamp(state.TargetX + moveX, 0, target.LimitX);
        if (moveY != 0) state.TargetY = Math.Clamp(state.TargetY + moveY, 0, target.LimitY);

        if (state.Running) return;

        state.Running = true;
        state.Last = null;
        RequestFrame(target, state);
    }


    private static void RequestFrame(ScrollTarget target, ScrollAnimation state)
    {
        TopLevel? top = TopLevel.GetTopLevel(target.Visual);

        if (top is null)
        {
            state.Stop();
            return;
        }

        top.RequestAnimationFrame(stamp => Advance(target, state, stamp));
    }


    private static void Advance(ScrollTarget target, ScrollAnimation state, TimeSpan stamp)
    {
        double deltaX = state.TargetX - target.X;
        double deltaY = state.TargetY - target.Y;

        if (Math.Abs(deltaX) < Settle && Math.Abs(deltaY) < Settle)
        {
            target.SetPosition(state.TargetX, state.TargetY);
            state.Stop();
            return;
        }

        double seconds = Math.Min(state.Elapsed(stamp), MaxFrameSeconds);
        double follow = 1 - Math.Exp(-Chase * seconds);

        if (target.IsViewer)
        {
            target.SetPosition(target.X + deltaX * follow, target.Y + deltaY * follow);
        }
        else
        {
            target.SetOffsetX(target.X + deltaX * follow);
        }

        RequestFrame(target, state);
    }


    /// <summary>
    /// 滚轮这次要驱动谁：一个真的 ScrollViewer，或者列表那套共享横向平移的组。
    /// 两者的位移读写、边界、动画状态都得按各自的接口走，所以包一层。
    /// </summary>
    private readonly struct ScrollTarget
    {
        private ScrollTarget(ScrollViewer? viewer, ColumnPan.PanGroup? pan)
        {
            Viewer = viewer;
            Pan = pan;
        }

        public ScrollViewer? Viewer { get; }

        public ColumnPan.PanGroup? Pan { get; }

        public bool IsViewer => Viewer is not null;

        /// <summary>动画状态挂在谁身上：一个容器一份，跟滚动器和平移组都能对上。</summary>
        public object Key => Viewer is not null ? Viewer : Pan!;

        public Visual? Visual => Viewer is not null ? Viewer : Pan!.Lead;

        public double X => Viewer is not null ? Viewer.Offset.X : Pan!.Offset;

        public double Y => Viewer is not null ? Viewer.Offset.Y : 0;

        /// <summary>横向还能走多远。</summary>
        public double LimitX => Viewer is not null ? Limit(Viewer, true) : Pan!.Limit;

        /// <summary>纵向还能走多远：平移组只认横向，纵向恒 0。</summary>
        public double LimitY => Viewer is not null ? Limit(Viewer, false) : 0;

        public static ScrollTarget For(ScrollViewer viewer) => new(viewer, null);

        public static ScrollTarget For(ColumnPan.PanGroup pan) => new(null, pan);


        public void SetPosition(double x, double y)
        {
            if (Viewer is not null)
            {
                Viewer.Offset = new Vector(x, y);
                return;
            }

            SetOffsetX(x);
        }


        public void SetOffsetX(double x)
        {
            if (Viewer is not null)
            {
                Viewer.Offset = Viewer.Offset.WithX(x);
                return;
            }

            ColumnPan.SetOffset(Pan!, x);
        }
    }


    private sealed class ScrollAnimation
    {
        public TimeSpan? Last;

        public double TargetX;

        public double TargetY;

        public bool Running;


        public double Elapsed(TimeSpan stamp)
        {
            TimeSpan last = Last ?? stamp;
            Last = stamp;
            return Math.Max(0, (stamp - last).TotalSeconds);
        }


        public void Stop()
        {
            Running = false;
            Last = null;
        }
    }
}
