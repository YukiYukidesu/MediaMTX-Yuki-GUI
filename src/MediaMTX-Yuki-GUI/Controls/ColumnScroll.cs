using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 列表那套表格的两件小事：把操作列对宽（表头与每行各算一遍难免漂移），
/// 以及按模式收起几列。
///
/// 中段的横向平移已经交给 <see cref="ColumnPan"/>：那边是一块裁剪窗口加一份共享平移，
/// 不再需要逐行同步偏移，所以这里只剩列定义相关的东西。
/// </summary>
public static class ColumnScroll
{
    /// <summary>要随模式收起或展开的列下标，逗号分隔，从 0 数起。</summary>
    public static readonly AttachedProperty<string?> AdvancedColumnsProperty =
        AvaloniaProperty.RegisterAttached<Border, string?>("AdvancedColumns", typeof(ColumnScroll));

    /// <summary>收起还是展开 <see cref="AdvancedColumnsProperty"/> 点名的那些列。</summary>
    public static readonly AttachedProperty<bool> AdvancedProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>("Advanced", typeof(ColumnScroll));

    /// <summary>操作列该多宽；表头与行模板都绑同一个值，省得常量两处各写一份。</summary>
    public static readonly AttachedProperty<double> TailWidthProperty =
        AvaloniaProperty.RegisterAttached<Grid, double>("TailWidth", typeof(ColumnScroll), double.NaN);

    /// <summary>每张 Grid 各列收起前的宽度，展开时要原样还回去。</summary>
    private static readonly ConditionalWeakTable<Grid, Dictionary<int, ColumnMetrics>> OriginalColumns = new();


    static ColumnScroll()
    {
        AdvancedColumnsProperty.Changed.AddClassHandler<Border>((border, args) => ApplyAdvancedColumns(border));
        AdvancedProperty.Changed.AddClassHandler<Border>((border, args) => ApplyAdvancedColumns(border));
        TailWidthProperty.Changed.AddClassHandler<Grid>((grid, args) => ApplyTailWidth(grid));
    }


    public static string? GetAdvancedColumns(Border border) => border.GetValue(AdvancedColumnsProperty);

    public static void SetAdvancedColumns(Border border, string? value) => border.SetValue(AdvancedColumnsProperty, value);

    public static bool GetAdvanced(Border border) => border.GetValue(AdvancedProperty);

    public static void SetAdvanced(Border border, bool value) => border.SetValue(AdvancedProperty, value);

    public static double GetTailWidth(Grid grid) => grid.GetValue(TailWidthProperty);

    public static void SetTailWidth(Grid grid, double value) => grid.SetValue(TailWidthProperty, value);


    /// <summary>
    /// 收起时把列宽与最小宽一起压成 0，展开时按原值还回去。
    /// 只压 Width 不压 MinWidth 的话列还占着位置，看着就是「隐藏了但留了一条空白」。
    /// </summary>
    private static void ApplyAdvancedColumns(Border border)
    {
        if (border.Child is not Grid grid) return;

        string[] columns = (GetAdvancedColumns(border) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (columns.Length == 0) return;

        bool expand = GetAdvanced(border);
        Dictionary<int, ColumnMetrics> saved = OriginalColumns.GetOrCreateValue(grid);

        foreach (string item in columns)
        {
            if (!int.TryParse(item, out int index)) continue;
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

        // 列宽变了，内容宽跟着变：让裁剪窗口那组重算一次偏移上限
        ColumnPan.Refresh(border);
    }


    private static void ApplyTailWidth(Grid grid)
    {
        double width = GetTailWidth(grid);
        if (double.IsNaN(width) || width <= 0) return;
        if (grid.ColumnDefinitions.Count == 0) return;

        ColumnDefinition tail = grid.ColumnDefinitions[grid.ColumnDefinitions.Count - 1];
        if (tail.Width.IsAbsolute && Math.Abs(tail.Width.Value - width) < 0.5) return;

        tail.Width = new GridLength(width);
    }


    /// <summary>一列被收起前的宽度。</summary>
    private readonly struct ColumnMetrics(GridLength width, double minWidth)
    {
        public GridLength Width { get; } = width;

        public double MinWidth { get; } = minWidth;
    }
}
