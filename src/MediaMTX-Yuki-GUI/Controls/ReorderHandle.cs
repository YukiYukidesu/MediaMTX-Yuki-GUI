using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 一次换位请求：把第 <paramref name="From"/> 行移动到第 <paramref name="To"/> 行。
/// </summary>
public sealed record ReorderRequest(int From, int To);

/// <summary>
/// 给列表行挂一个拖拽手柄：按住手柄上下拖动即可调整行的顺序。
/// 拖动中被拖的行跟着指针实时移动，其余行用动画让出位置，
/// 松手时被拖的行由动画吸附到目标槽位，全程没有瞬移。
/// </summary>
public static class ReorderHandle
{
    // ====== 附加属性 ======

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("IsEnabled", typeof(ReorderHandle));

    public static readonly AttachedProperty<ICommand?> MoveCommandProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, ICommand?>("MoveCommand", typeof(ReorderHandle));

    public static readonly AttachedProperty<ICommand?> CommitCommandProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, ICommand?>("CommitCommand", typeof(ReorderHandle));

    /// <summary>
    /// 启用中的行在集合里占前多少项；0 表示不分段，拖拽不受限制。
    /// 分段时被拖的行只能落在自己那一段里，避免拖完又被重新分组弹回去。
    /// </summary>
    public static readonly AttachedProperty<int> BoundaryProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, int>("Boundary", typeof(ReorderHandle));

    // ====== 常量 ======

    private const string HandleClass = "drag-handle";
    private const double LiftThreshold = 4;
    private const int MovingZIndex = 100;

    private static readonly TimeSpan ShiftDuration = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan SettleDuration = TimeSpan.FromMilliseconds(170);

    // ====== 状态 ======

    private static readonly ConditionalWeakTable<ItemsControl, ReorderState> States = new();

    static ReorderHandle()
    {
        IsEnabledProperty.Changed.AddClassHandler<ItemsControl>((control, args) => OnIsEnabledChanged(control, args));
    }

    // ====== 附加属性读写 ======

    public static bool GetIsEnabled(ItemsControl control) => control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ItemsControl control, bool value) => control.SetValue(IsEnabledProperty, value);

    public static ICommand? GetMoveCommand(ItemsControl control) => control.GetValue(MoveCommandProperty);

    public static void SetMoveCommand(ItemsControl control, ICommand? value) => control.SetValue(MoveCommandProperty, value);

    public static ICommand? GetCommitCommand(ItemsControl control) => control.GetValue(CommitCommandProperty);

    public static void SetCommitCommand(ItemsControl control, ICommand? value) => control.SetValue(CommitCommandProperty, value);

    public static int GetBoundary(ItemsControl control) => control.GetValue(BoundaryProperty);

    public static void SetBoundary(ItemsControl control, int value) => control.SetValue(BoundaryProperty, value);

    // ====== 事件挂载 ======

    private static void OnIsEnabledChanged(ItemsControl control, AvaloniaPropertyChangedEventArgs args)
    {
        bool enabled = args.NewValue is true;
        ReorderState state = States.GetOrCreateValue(control);
        if (enabled == state.Attached) return;

        state.Attached = enabled;

        if (enabled)
        {
            control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            control.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            control.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            control.PointerCaptureLost += OnPointerCaptureLost;

            return;
        }

        control.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        control.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        control.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        control.PointerCaptureLost -= OnPointerCaptureLost;
        ClearTransforms(state);
        Reset(state);
    }

    // ====== 按下 ======

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not ItemsControl control) return;

        ReorderState state = States.GetOrCreateValue(control);
        if (state.Settling || state.Releasing) return;
        if (state.Item is not null) return;
        if (!args.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        if (FindHandle(args.Source as Visual, control) is not Visual handle) return;
        if (FindOwner(control, handle) is not Control container) return;

        int index = control.IndexFromContainer(container);
        if (index < 0) return;
        if (!CaptureLayout(control, state)) return;
        if (state.Tops.Count != control.ItemCount) return;

        state.Item = container;
        state.Container = container;
        state.Pointer = args.Pointer;
        state.From = index;
        state.Target = index;
        state.StartY = args.GetPosition(control).Y;
        state.Boundary = GetBoundary(control);
        state.Lifted = false;

        args.Pointer.Capture(control);
        args.Handled = true;
    }

    // ====== 拖动 ======

    private static void OnPointerMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not ItemsControl control) return;

        ReorderState state = States.GetOrCreateValue(control);
        if (state.Item is null || state.Container is null) return;

        if (!args.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            Release(control, state);
            return;
        }

        double offset = args.GetPosition(control).Y - state.StartY;

        if (!state.Lifted)
        {
            if (Math.Abs(offset) < LiftThreshold) return;

            state.Lifted = true;
            state.Container.ZIndex = MovingZIndex;
            state.Container.RenderTransform = new TranslateTransform();
            args.Pointer.Capture(control);
        }

        if (state.Container.RenderTransform is TranslateTransform moving)
        {
            moving.Transitions = null;
            moving.Y = offset;
        }

        int target = ComputeTarget(state, offset);
        if (target == state.Target) return;

        state.Target = target;
        Arrange(state);
    }

    // ====== 松手 ======

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not ItemsControl control) return;

        Release(control, States.GetOrCreateValue(control));
    }

    private static void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (sender is not ItemsControl control) return;

        Release(control, States.GetOrCreateValue(control));
    }

    private static void Release(ItemsControl control, ReorderState state)
    {
        if (state.Settling || state.Releasing) return;
        if (state.Item is null) return;

        state.Releasing = true;

        try
        {
            IPointer? pointer = state.Pointer;
            state.Pointer = null;
            pointer?.Capture(null);

            if (!state.Lifted || state.Container is null)
            {
                ClearTransforms(state);
                Reset(state);
                return;
            }

            int from = state.From;

            // 松手这一下才按「优先显示已启用」的边界归位：
            // 停用的行会被推到禁用段的头上，启用的行会被压在启用段的末尾。
            int target = ClampToBoundary(from, state.Target, state.Boundary, state.Tops.Count);

            if (target != state.Target)
            {
                // 目标变了，之前让人让的位就都不对了，重新排一次
                state.Target = target;
                Arrange(state);
            }

            double destination = state.Tops[target] - state.Tops[from];

            if (state.Container.RenderTransform is TranslateTransform transform)
            {
                transform.Transitions = NewTransitions(SettleDuration);
                transform.Y = destination;
            }

            state.Settling = true;

            DispatcherTimer timer = new() { Interval = SettleDuration + TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();

                // 先清掉位移、再真的换位：两步之间不会发生布局，视觉上接不上缝
                ClearTransforms(state);
                state.Settling = false;

                if (target != from) GetMoveCommand(control)?.Execute(new ReorderRequest(from, target));

                GetCommitCommand(control)?.Execute(null);
                Reset(state);
            };
            timer.Start();
        }
        finally
        {
            state.Releasing = false;
        }
    }

    // ====== 让位 ======

    private static int ComputeTarget(ReorderState state, double offset)
    {
        int from = state.From;
        int count = state.Tops.Count;
        double middle = state.Tops[from] + state.Heights[from] * 0.5;
        double center = middle + offset;
        int target = from;

        if (center > middle)
        {
            for (int index = from + 1; index < count; index++)
            {
                if (center <= state.Tops[index] + state.Heights[index] * 0.5) break;

                target = index;
            }
        }
        else
        {
            for (int index = from - 1; index >= 0; index--)
            {
                if (center >= state.Tops[index] + state.Heights[index] * 0.5) break;

                target = index;
            }
        }

        // 拖动过程中不夹取：停用的行也要能随手拖到任何位置，
        // 否则边界外的行一开始就拖不动（target 恒等于 from，画面纹丝不动）。
        // 归位留到松手那一下，见 Release。
        return target;
    }

    private static int ClampToBoundary(int from, int target, int boundary, int count)
    {
        if (boundary <= 0 || boundary >= count) return target;

        return from < boundary ? Math.Min(target, boundary - 1) : Math.Max(target, boundary);
    }

    private static void Arrange(ReorderState state)
    {
        int from = state.From;
        int target = state.Target;
        double step = StepOf(state);

        for (int index = 0; index < state.Containers.Count; index++)
        {
            if (index == from) continue;

            double shift = 0;
            if (target > from && index > from && index <= target) shift = -step;
            else if (target < from && index >= target && index < from) shift = step;

            Shift(state.Containers[index], shift);
        }
    }

    /// <summary>一步的高度：优先取相邻两行的间距，被拖的行在末尾时退回上一行。</summary>
    private static double StepOf(ReorderState state)
    {
        int from = state.From;

        if (from + 1 < state.Tops.Count) return state.Tops[from + 1] - state.Tops[from];
        if (from > 0) return state.Tops[from] - state.Tops[from - 1];

        return state.Heights[from];
    }

    private static void Shift(Control container, double shift)
    {
        if (container.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            container.RenderTransform = transform;
        }

        if (Math.Abs(transform.Y - shift) < 0.5) return;

        if (transform.Transitions is null) transform.Transitions = NewTransitions(ShiftDuration);

        transform.Y = shift;
    }

    // ====== 小工具 ======

    private static bool CaptureLayout(ItemsControl control, ReorderState state)
    {
        state.Containers.Clear();
        state.Tops.Clear();
        state.Heights.Clear();

        for (int index = 0; index < control.ItemCount; index++)
        {
            if (control.ContainerFromIndex(index) is not Control container) return false;
            if (container.TranslatePoint(default, control) is not { } corner) return false;

            state.Containers.Add(container);
            state.Tops.Add(corner.Y);
            state.Heights.Add(container.Bounds.Height);
        }

        return state.Containers.Count >= 2;
    }

    private static Control? FindOwner(ItemsControl control, Visual handle)
    {
        for (int index = 0; index < control.ItemCount; index++)
        {
            if (control.ContainerFromIndex(index) is not Control container) continue;
            if (container.IsVisualAncestorOf(handle)) return container;
        }

        return null;
    }

    private static Visual? FindHandle(Visual? source, Visual stop)
    {
        Visual? current = source;

        while (current is not null && current != stop)
        {
            if (current is StyledElement element && element.Classes.Contains(HandleClass)) return current;

            current = current.GetVisualParent();
        }

        return null;
    }

    private static Transitions NewTransitions(TimeSpan duration)
    {
        return new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = duration,
                Easing = new CubicEaseOut(),
            },
        };
    }

    private static void ClearTransforms(ReorderState state)
    {
        foreach (Control container in state.Containers)
        {
            container.ZIndex = 0;

            if (container.RenderTransform is not TranslateTransform transform) continue;

            transform.Transitions = null;
            container.RenderTransform = null;
        }
    }

    private static void Reset(ReorderState state)
    {
        state.Item = null;
        state.Container = null;
        state.Pointer = null;
        state.From = 0;
        state.Target = 0;
        state.StartY = 0;
        state.Boundary = 0;
        state.Lifted = false;
        state.Containers.Clear();
        state.Tops.Clear();
        state.Heights.Clear();
    }

    // ====== 状态对象 ======

    private sealed class ReorderState
    {
        public bool Attached;
        public bool Lifted;
        public bool Releasing;
        public bool Settling;

        public object? Item;
        public Control? Container;
        public IPointer? Pointer;

        public int From;
        public int Target;
        public int Boundary;
        public double StartY;

        public List<Control> Containers { get; } = new();
        public List<double> Tops { get; } = new();
        public List<double> Heights { get; } = new();
    }
}
