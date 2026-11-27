using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 表头与每一行的中段共用一条横向偏移。
///
/// 老做法是每个中段各放一个隐藏滚动条的 ScrollViewer，再靠 ColumnScroll 把偏移
/// 广播到同组的所有实例：四十个 ScrollViewer、四十份布局、四十次偏移写入，滚一下
/// 要挨个通知，行一多就是滚轮卡一下的来源。这里改成一块「裁剪窗口」：中段是一个
/// ClipToBounds 的 Border，里面装一份比窗口宽的内容 Grid，整组共享一个
/// TranslateTransform。一次偏移写入，表头与所有行的列一起动，既没有 ScrollViewer
/// 的布局开销，也不会出现「表头与行差一像素」的对不齐。
/// </summary>
public static class ColumnPan
{
    /// <summary>组名：同一个组下的所有中段共享偏移与滚动条。</summary>
    public static readonly AttachedProperty<string?> GroupProperty =
        AvaloniaProperty.RegisterAttached<Border, string?>("Group", typeof(ColumnPan));

    /// <summary>把一条常驻横向滚动条挂到这个组；组名与 <see cref="GroupProperty"/> 一致即成对。</summary>
    public static readonly AttachedProperty<string?> BarGroupProperty =
        AvaloniaProperty.RegisterAttached<ScrollBar, string?>("BarGroup", typeof(ColumnPan));

    /// <summary>拖过这么多像素才算拖动，否则当成一次普通点击交给内容。</summary>
    private const double DragThreshold = 4;

    private static readonly Dictionary<string, PanGroup> Groups = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, WeakReference<ScrollBar>> Bars = new(StringComparer.Ordinal);


    static ColumnPan()
    {
        GroupProperty.Changed.AddClassHandler<Border>((border, args) => OnGroupChanged(border, args));
        BarGroupProperty.Changed.AddClassHandler<ScrollBar>((bar, args) => OnBarGroupChanged(bar, args));
    }


    public static string? GetGroup(Border border) => border.GetValue(GroupProperty);

    public static void SetGroup(Border border, string? value) => border.SetValue(GroupProperty, value);

    public static string? GetBarGroup(ScrollBar bar) => bar.GetValue(BarGroupProperty);

    public static void SetBarGroup(ScrollBar bar, string? value) => bar.SetValue(BarGroupProperty, value);


    /// <summary>
    /// 列定义变过（收起高级列）之后把内容宽重算一遍。
    /// 同一组的成员列定义一致，谁先算都行，<see cref="Relayout"/> 自己会挑一个有尺寸的当基准。
    /// </summary>
    internal static void Refresh(Border border)
    {
        if (GetGroup(border) is not { Length: > 0 } name) return;
        if (!Groups.TryGetValue(name, out PanGroup? group)) return;

        Relayout(group);
    }


    /// <summary>沿事件源往上找挂了这个组的中段。</summary>
    internal static bool TryFindGroup(Visual source, [NotNullWhen(true)] out PanGroup? group)
    {
        for (Visual? visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is not Border border) continue;
            if (GetGroup(border) is not { Length: > 0 } name) continue;
            if (!Groups.TryGetValue(name, out PanGroup? found)) continue;

            group = found;
            return true;
        }

        group = null;
        return false;
    }


    /// <summary>把这一组整体平移到指定偏移；滚轮与滚动条都从这里进。</summary>
    internal static void SetOffset(PanGroup group, double offset)
    {
        double clamped = Math.Clamp(offset, 0, group.Limit);
        group.Offset = clamped;

        double shift = -clamped;
        if (Math.Abs(group.Shift.X - shift) >= 0.01) group.Shift.X = shift;

        SyncBarValue(group, clamped);
    }


    /// <summary>
    /// 只把滚动条的当前位置拨到跟偏移一致。
    /// 滚轮每帧都会进来，所以这里不重算 Maximum 与 ViewportSize，只写 Value 这一个数。
    /// </summary>
    private static void SyncBarValue(PanGroup group, double offset)
    {
        if (group.Bar is not { } reference) return;
        if (!reference.TryGetTarget(out ScrollBar? bar)) return;
        if (Math.Abs(bar.Value - offset) < 0.01) return;

        group.Syncing = true;
        bar.Value = offset;
        group.Syncing = false;
    }


    /// <summary>
    /// 重算这一组的内容宽与偏移上限。每一步都幂等，重复调用只花几个比较，
    /// 所以尺寸变化、切页、收列之后都可以直接喊它。
    /// </summary>
    private static void Relayout(PanGroup group)
    {
        group.Members.RemoveAll(entry => !entry.TryGetTarget(out _));

        Border? lead = null;
        for (int index = 0; index < group.Members.Count; index++)
        {
            if (!group.Members[index].TryGetTarget(out Border? member)) continue;
            if (member.Bounds.Width <= 1) continue;

            lead = member;
            break;
        }

        if (lead?.Child is not Grid grid) return;

        double spacing = grid.ColumnDefinitions.Count > 1
            ? grid.ColumnSpacing * (grid.ColumnDefinitions.Count - 1)
            : 0;

        double minimum = spacing;
        foreach (ColumnDefinition column in grid.ColumnDefinitions) minimum += column.MinWidth;

        double margin = grid.Margin.Left + grid.Margin.Right;
        double viewport = lead.Bounds.Width;
        double width = Math.Max(minimum, viewport - margin);

        group.Viewport = viewport;
        group.Limit = Math.Max(0, width + margin - viewport);
        group.Lead = lead;

        foreach (WeakReference<Border> entry in group.Members)
        {
            if (!entry.TryGetTarget(out Border? member)) continue;
            if (member.Child is not Grid content) continue;

            if (!SameWidth(content.Width, width)) content.Width = width;
            if (!ReferenceEquals(content.RenderTransform, group.Shift)) content.RenderTransform = group.Shift;
        }

        SetOffset(group, group.Offset);
        SyncBar(group);
    }


    /// <summary>宽相等判断：NaN 表示「跟随内容」，一律当不相等，好让第一次赋值落下去。</summary>
    private static bool SameWidth(double left, double right)
        => !double.IsNaN(left) && Math.Abs(left - right) < 0.5;


    private static void OnGroupChanged(Border border, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.OldValue is string previous && Groups.TryGetValue(previous, out PanGroup? oldGroup))
        {
            oldGroup.Members.RemoveAll(entry => !entry.TryGetTarget(out Border? member) || ReferenceEquals(member, border));
        }

        if (args.NewValue is not string name || name.Length == 0) return;

        border.ClipToBounds = true;

        // 中段大片是空的（列间距、胶囊之外的余量）：Border 没有背景就不参与命中测试，
        // 滚轮会穿到外层行卡上，横向那一趟就找不到这个组。铺一层透明底把整片接住。
        border.Background ??= Brushes.Transparent;

        PanGroup group = GetOrCreate(name);
        group.Members.Add(new WeakReference<Border>(border));

        border.SizeChanged += (_, _) => Relayout(group);
        border.AttachedToVisualTree += (_, _) => Relayout(group);
        AttachDrag(border, group);

        Relayout(group);
    }


    private static PanGroup GetOrCreate(string name)
    {
        if (Groups.TryGetValue(name, out PanGroup? existing)) return existing;

        PanGroup created = new(name);
        Groups[name] = created;

        return created;
    }


    private static void OnBarGroupChanged(ScrollBar bar, AvaloniaPropertyChangedEventArgs args)
    {
        bar.Scroll -= OnBarScroll;
        bar.Scroll += OnBarScroll;

        if (args.NewValue is not string name || name.Length == 0) return;

        Bars[name] = new WeakReference<ScrollBar>(bar);

        if (!Groups.TryGetValue(name, out PanGroup? group)) return;

        group.Bar = new WeakReference<ScrollBar>(bar);
        SyncBar(group);
    }


    private static void OnBarScroll(object? sender, ScrollEventArgs args)
    {
        if (sender is not ScrollBar bar) return;
        if (GetBarGroup(bar) is not { Length: > 0 } name) return;
        if (!Groups.TryGetValue(name, out PanGroup? group)) return;
        if (group.Syncing) return;

        SetOffset(group, bar.Value);
    }


    /// <summary>把组的偏移与上限写回滚动条。写 Value 之前先抬 Syncing，免得自己触发滚回去。</summary>
    private static void SyncBar(PanGroup group)
    {
        if (group.Bar is null || !group.Bar.TryGetTarget(out ScrollBar? bar)) return;

        group.Syncing = true;
        bar.Minimum = 0;
        bar.Maximum = group.Limit;
        bar.ViewportSize = Math.Max(1, group.Viewport);
        bar.IsEnabled = group.Limit >= 1;
        bar.Value = group.Offset;
        group.Syncing = false;
    }


    /// <summary>
    /// 中段按住横向拖。左段的勾选框、右段的按钮都在裁剪窗口外面，不会被这里抢走；
    /// 超过阈值才捕获指针，所以单击仍然落到内容上。
    /// </summary>
    private static void AttachDrag(Border border, PanGroup group)
    {
        DragState state = new();

        border.AddHandler(InputElement.PointerPressedEvent, (_, args) =>
        {
            state.Pointer = args.Pointer;
            state.StartX = args.GetPosition(border).X;
            state.StartOffset = group.Offset;
            state.Dragging = false;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        border.AddHandler(InputElement.PointerMovedEvent, (_, args) =>
        {
            if (state.Pointer is null || !ReferenceEquals(args.Pointer, state.Pointer)) return;

            double delta = state.StartX - args.GetPosition(border).X;

            if (!state.Dragging)
            {
                if (Math.Abs(delta) < DragThreshold) return;

                state.Dragging = true;
                args.Pointer.Capture(border);
                border.Cursor = new Cursor(StandardCursorType.SizeAll);
            }

            SetOffset(group, state.StartOffset + delta);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        border.AddHandler(InputElement.PointerReleasedEvent, (_, _) => EndDrag(border, state), RoutingStrategies.Tunnel, handledEventsToo: true);
        border.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => EndDrag(border, state), RoutingStrategies.Tunnel, handledEventsToo: true);
    }


    private static void EndDrag(Border border, DragState state)
    {
        state.Pointer = null;

        if (!state.Dragging) return;

        state.Dragging = false;
        border.Cursor = null;
    }


    /// <summary>一组共享横向偏移的中段：表头算一个，每一行各一个。</summary>
    internal sealed class PanGroup(string name)
    {
        public string Name { get; } = name;

        public List<WeakReference<Border>> Members { get; } = new();

        /// <summary>整组共用的平移：写一次，所有成员一起动。</summary>
        public TranslateTransform Shift { get; } = new();

        /// <summary>当前横向偏移，0 表示最左。</summary>
        public double Offset { get; set; }

        /// <summary>最大偏移，0 表示这一组不需要横向滚动。</summary>
        public double Limit { get; set; }

        /// <summary>视口宽，喂给滚动条的 ViewportSize。</summary>
        public double Viewport { get; set; }

        /// <summary>第一个已经排出尺寸的成员，滚轮动画靠它找 TopLevel。</summary>
        public Border? Lead { get; set; }

        public WeakReference<ScrollBar>? Bar { get; set; }

        /// <summary>正在把偏移写回滚动条：这段时间里 Value 的变化不算用户操作。</summary>
        public bool Syncing { get; set; }
    }


    /// <summary>一次横向拖动的中间状态。</summary>
    private sealed class DragState
    {
        public IPointer? Pointer;

        public double StartX;

        public double StartOffset;

        public bool Dragging;
    }
}
