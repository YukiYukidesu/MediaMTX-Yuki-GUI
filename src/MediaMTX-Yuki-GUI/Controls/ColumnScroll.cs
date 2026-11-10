using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 让多行的中段共用同一条横向偏移：任一行被横向拖动，其余行与表头跟着动。
/// 列表行的左右两段是固定的，中段各自套一个 ScrollViewer；不共享偏移的话
/// 每行会各滚各的，看上去像表格散架。
///</summary>
public static class ColumnScroll
{
    /// <summary>同一个组名下的所有 ScrollViewer 共享横向偏移；空串表示不参与同步。</summary>
    public static readonly AttachedProperty<string?> SyncGroupProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, string?>("SyncGroup", typeof(ColumnScroll));

    /// <summary>
    /// 挂一条常驻的横向滚动条给同一个组：组的横向范围与当前偏移都映射到它身上，
    /// 拖动它时再反向推回组内每个视图。列表本身不显示滚动条，靠这一条统一管。
    /// </summary>
    public static readonly AttachedProperty<string?> BarGroupProperty =
        AvaloniaProperty.RegisterAttached<ScrollBar, string?>("BarGroup", typeof(ColumnScroll));

    /// <summary>
    /// 挂在中段那层 ScrollViewer 上：内容比视口窄时把它撑满，多出来的宽度按各列的星号权重分掉；
    /// 比视口宽时保持各列的最小宽度，让横向滚动照常生效。
    /// 列宽写成「最小宽度*」正是为了让这两种情况共用一套定义，不必按窗口大小换模板。
    /// </summary>
    public static readonly AttachedProperty<bool> FillViewportProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("FillViewport", typeof(ColumnScroll));

    /// <summary>
    /// 挂在中段那层 ScrollViewer 上：哪几列属于高级模式，逗号分隔的下标，例如 "7,8"。
    /// 配合 <see cref="AdvancedProperty"/> 在简洁模式下把这几列收成 0 宽——光把内容藏起来不够，
    /// 列本身还占着位置，会留出两格空白。
    /// </summary>
    public static readonly AttachedProperty<string?> AdvancedColumnsProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, string?>("AdvancedColumns", typeof(ColumnScroll));

    /// <summary>挂在中段那层 ScrollViewer 上：当前是不是高级模式，决定 <see cref="AdvancedColumnsProperty"/> 那几列收不收。</summary>
    public static readonly AttachedProperty<bool> AdvancedProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Advanced", typeof(ColumnScroll));

    /// <summary>拖动超过这个距离才算平移，否则视作点击，避免破坏 ToolTip 与胶囊的悬停。</summary>
    private const double DragThreshold = 4;

    private static readonly Dictionary<string, List<WeakReference<ScrollViewer>>> Groups = new(StringComparer.Ordinal);

    /// <summary>每个组最多一条常驻滚动条，键与 <see cref="SyncGroupProperty"/> 同名。</summary>
    private static readonly Dictionary<string, WeakReference<ScrollBar>> Bars = new(StringComparer.Ordinal);

    /// <summary>每个视图的拖动状态；用弱表挂住，视图回收时自动丢弃，不占 Tag。</summary>
    private static readonly ConditionalWeakTable<ScrollViewer, ColumnScrollState> States = new();

    /// <summary>
    /// 每个中段 Grid 收起来之前各列原本的宽度。
    /// 简洁模式把高级列压成 0 之后，切回高级模式得知道原来是多少，不能写死一个恢复值。
    /// </summary>
    private static readonly ConditionalWeakTable<Grid, Dictionary<int, ColumnMetrics>> OriginalColumns = new();

    /// <summary>正在把偏移广播给别人，用来掐断回环。</summary>
    private static bool _broadcasting;

    static ColumnScroll()
    {
        SyncGroupProperty.Changed.AddClassHandler<ScrollViewer>((viewer, args) => OnSyncGroupChanged(viewer, args));
        BarGroupProperty.Changed.AddClassHandler<ScrollBar>((bar, args) => OnBarChanged(bar, args));
        AdvancedColumnsProperty.Changed.AddClassHandler<ScrollViewer>((viewer, args) => ApplyAdvancedColumns(viewer));
        AdvancedProperty.Changed.AddClassHandler<ScrollViewer>((viewer, args) => ApplyAdvancedColumns(viewer));
        FillViewportProperty.Changed.AddClassHandler<ScrollViewer>((viewer, args) => SyncFillWidth(viewer));
    }

    public static string? GetSyncGroup(ScrollViewer viewer)
        => viewer.GetValue(SyncGroupProperty);

    public static void SetSyncGroup(ScrollViewer viewer, string? value)
        => viewer.SetValue(SyncGroupProperty, value);

    public static bool GetFillViewport(ScrollViewer viewer)
        => viewer.GetValue(FillViewportProperty);

    public static void SetFillViewport(ScrollViewer viewer, bool value)
        => viewer.SetValue(FillViewportProperty, value);

    public static string? GetAdvancedColumns(ScrollViewer viewer)
        => viewer.GetValue(AdvancedColumnsProperty);

    public static void SetAdvancedColumns(ScrollViewer viewer, string? value)
        => viewer.SetValue(AdvancedColumnsProperty, value);

    public static bool GetAdvanced(ScrollViewer viewer)
        => viewer.GetValue(AdvancedProperty);

    public static void SetAdvanced(ScrollViewer viewer, bool value)
        => viewer.SetValue(AdvancedProperty, value);

    public static string? GetBarGroup(ScrollBar bar)
        => bar.GetValue(BarGroupProperty);

    public static void SetBarGroup(ScrollBar bar, string? value)
        => bar.SetValue(BarGroupProperty, value);

    /// <summary>滚动条挂上来时登记进组、接管它的 Value，并先把范围同步过去一次。</summary>
    private static void OnBarChanged(ScrollBar bar, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.OldValue is string oldName) ReleaseBar(bar, oldName);

        if (args.NewValue is not string name) return;

        if (string.IsNullOrEmpty(name)) return;

        Bars[name] = new WeakReference<ScrollBar>(bar);
        bar.Scroll += OnBarScroll;
        SyncBarFromGroup(name, bar);
    }

    /// <summary>滚动条改挂到别的组、或者被回收时，先把旧的那条从组里摘掉。</summary>
    private static void ReleaseBar(ScrollBar bar, string name)
    {
        bar.Scroll -= OnBarScroll;

        if (Bars.TryGetValue(name, out WeakReference<ScrollBar>? reference)
            && reference.TryGetTarget(out ScrollBar? current)
            && ReferenceEquals(current, bar))
        {
            Bars.Remove(name);
        }
    }

    private static void OnBarScroll(object? sender, ScrollEventArgs args)
    {
        if (sender is not ScrollBar bar) return;
        if (_broadcasting) return;
        if (GetBarGroup(bar) is not { Length: > 0 } name) return;
        if (!Groups.TryGetValue(name, out List<WeakReference<ScrollViewer>>? members)) return;

        double x = bar.Value;

        _broadcasting = true;

        try
        {
            foreach (WeakReference<ScrollViewer> reference in members)
            {
                if (!reference.TryGetTarget(out ScrollViewer? view)) continue;
                if (Math.Abs(view.Offset.X - x) < 0.5) continue;

                view.Offset = new Vector(x, view.Offset.Y);
            }
        }
        finally
        {
            _broadcasting = false;
        }
    }

    /// <summary>把一个组的横向范围与当前偏移映射到那条常驻滚动条上。</summary>
    private static void SyncBarFromGroup(string name, ScrollBar bar)
    {
        if (!Groups.TryGetValue(name, out List<WeakReference<ScrollViewer>>? members)) return;

        ScrollViewer? lead = members
            .Select(item => item.TryGetTarget(out ScrollViewer? view) ? view : null)
            .FirstOrDefault(view => view is not null);

        if (lead is null) return;

        double maximum = Math.Max(0, lead.Extent.Width - lead.Viewport.Width);

        _broadcasting = true;

        try
        {
            bar.Minimum = 0;
            bar.Maximum = maximum;
            bar.ViewportSize = Math.Max(0, lead.Viewport.Width);
            bar.IsEnabled = maximum >= 1;
            bar.Value = Math.Clamp(lead.Offset.X, 0, maximum);
        }
        finally
        {
            _broadcasting = false;
        }
    }

    private static void OnSyncGroupChanged(ScrollViewer viewer, AvaloniaPropertyChangedEventArgs args)
    {
        string? oldName = args.OldValue as string;
        string? newName = args.NewValue as string;

        if (!string.IsNullOrEmpty(oldName)) Unregister(oldName, viewer);
        if (string.IsNullOrEmpty(newName)) return;

        Register(newName, viewer);
        Attach(viewer);
    }

    private static void Register(string name, ScrollViewer viewer)
    {
        if (!Groups.TryGetValue(name, out List<WeakReference<ScrollViewer>>? members))
        {
            members = new List<WeakReference<ScrollViewer>>();
            Groups[name] = members;
        }

        members.RemoveAll(item => !item.TryGetTarget(out ScrollViewer? target) || ReferenceEquals(target, viewer));
        members.Add(new WeakReference<ScrollViewer>(viewer));
    }

    private static void Unregister(string name, ScrollViewer viewer)
    {
        if (!Groups.TryGetValue(name, out List<WeakReference<ScrollViewer>>? members)) return;

        members.RemoveAll(item => !item.TryGetTarget(out ScrollViewer? target) || ReferenceEquals(target, viewer));
        if (members.Count == 0) Groups.Remove(name);
    }

    private static void Attach(ScrollViewer viewer)
    {
        if (States.TryGetValue(viewer, out _)) return;

        States.Add(viewer, new ColumnScrollState());
        viewer.ScrollChanged += OnScrollChanged;
        viewer.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.PointerCaptureLost += OnPointerCaptureLost;
        viewer.SizeChanged += OnViewerSizeChanged;
    }

    /// <summary>视口宽度变了（拖窗口、最大化、切模式）就得重算内容宽度，否则撑不满或白留一截。</summary>
    private static void OnViewerSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (sender is not ScrollViewer viewer) return;

        SyncFillWidth(viewer);
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (sender is not ScrollViewer viewer) return;
        if (GetSyncGroup(viewer) is not { Length: > 0 } name) return;

        // 位置变了，常驻滚动条的滑块要跟着挪；范围也可能变（窗口缩放、列宽变化）
        if (Bars.TryGetValue(name, out WeakReference<ScrollBar>? barRef)
            && barRef.TryGetTarget(out ScrollBar? bar))
        {
            SyncBarFromGroup(name, bar);
        }

        SyncFillWidth(viewer);

        Broadcast(name, viewer);
    }

    /// <summary>
    /// 按 <see cref="AdvancedColumnsProperty"/> 那串下标，把对应列的宽度在「本来多少」与「0」之间切换。
    /// 第一次收起时把原宽度记进 <see cref="OriginalColumns"/>，切回高级模式照原样还回去。
    /// </summary>
    private static void ApplyAdvancedColumns(ScrollViewer viewer)
    {
        if (viewer.Content is not Grid grid) return;

        if (GetAdvancedColumns(viewer) is not { Length: > 0 } raw) return;

        bool expand = GetAdvanced(viewer);

        if (!OriginalColumns.TryGetValue(grid, out Dictionary<int, ColumnMetrics>? saved))
        {
            saved = [];
            OriginalColumns.Add(grid, saved);
        }

        foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, out int index)) continue;
            if (index < 0 || index >= grid.ColumnDefinitions.Count) continue;

            ColumnDefinition column = grid.ColumnDefinitions[index];

            if (!saved.TryGetValue(index, out ColumnMetrics metrics))
            {
                metrics = new ColumnMetrics(column.Width, column.MinWidth);
                saved[index] = metrics;
            }

            column.Width = expand ? metrics.Width : new GridLength(0);
            column.MinWidth = expand ? metrics.MinWidth : 0;
        }

        SyncFillWidth(viewer);
    }

    /// <summary>
    /// 内容比视口窄就把它撑到视口宽，多出来的宽度由星号列按权重分掉；
    /// 比视口宽时按各列最小宽度排版，横向滚动照常。
    /// </summary>
    private static void SyncFillWidth(ScrollViewer viewer)
    {
        if (!GetFillViewport(viewer)) return;
        if (viewer.Content is not Grid grid) return;

        double minimum = grid.ColumnSpacing * Math.Max(0, grid.ColumnDefinitions.Count - 1);

        foreach (ColumnDefinition column in grid.ColumnDefinitions)
        {
            minimum += column.MinWidth;
        }

        // 中段自己左右留白（Grid 的 Margin）要减掉：Grid 宽度是按视口算的，
        // 不减就会比视口多出这两截留白，凭空长出一条横向滚动
        double target = Math.Max(minimum, viewer.Viewport.Width - grid.Margin.Left - grid.Margin.Right);

        if (double.IsNaN(target) || target <= 0) return;
        if (Math.Abs(grid.Width - target) < 0.5) return;

        grid.Width = target;
    }

    /// <summary>把 <paramref name="source"/> 的横向偏移推给同组其余视图。</summary>
    private static void Broadcast(string name, ScrollViewer source)
    {
        if (_broadcasting) return;

        double x = source.Offset.X;
        _broadcasting = true;

        try
        {
            if (!Groups.TryGetValue(name, out List<WeakReference<ScrollViewer>>? members)) return;

            foreach (WeakReference<ScrollViewer> reference in members)
            {
                if (!reference.TryGetTarget(out ScrollViewer? view)) continue;
                if (ReferenceEquals(view, source)) continue;
                if (Math.Abs(view.Offset.X - x) < 0.5) continue;

                view.Offset = new Vector(x, view.Offset.Y);
            }
        }
        finally
        {
            _broadcasting = false;
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not ScrollViewer viewer) return;
        if (!States.TryGetValue(viewer, out ColumnScrollState? state)) return;
        if (!args.GetCurrentPoint(viewer).Properties.IsLeftButtonPressed) return;
        if (state.Pointer is not null) return;

        state.Pointer = args.Pointer;
        state.StartX = args.GetPosition(viewer).X;
        state.StartOffset = viewer.Offset.X;
        state.Dragging = false;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not ScrollViewer viewer) return;
        if (!States.TryGetValue(viewer, out ColumnScrollState? state)) return;
        if (state.Pointer is null) return;

        double delta = state.StartX - args.GetPosition(viewer).X;

        if (!state.Dragging)
        {
            if (Math.Abs(delta) < DragThreshold) return;

            state.Dragging = true;
            args.Pointer.Capture(viewer);
            viewer.Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        viewer.Offset = new Vector(Math.Max(0, state.StartOffset + delta), viewer.Offset.Y);
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not ScrollViewer viewer) return;

        EndDrag(viewer);
    }

    private static void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (sender is not ScrollViewer viewer) return;

        EndDrag(viewer);
    }

    private static void EndDrag(ScrollViewer viewer)
    {
        if (!States.TryGetValue(viewer, out ColumnScrollState? state)) return;

        state.Pointer?.Capture(null);
        state.Pointer = null;
        state.Dragging = false;
        viewer.Cursor = null;
    }

    /// <summary>一列被收起之前的宽度。用 struct 存两份值，恢复时不必再读控件。</summary>
    private readonly struct ColumnMetrics(GridLength width, double minWidth)
    {
        public GridLength Width { get; } = width;

        public double MinWidth { get; } = minWidth;
    }

    private sealed class ColumnScrollState
    {
        public IPointer? Pointer;
        public double StartX;
        public double StartOffset;
        public bool Dragging;
    }
}
