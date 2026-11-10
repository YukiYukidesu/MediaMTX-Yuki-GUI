using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaMTX_Yuki_GUI.Controls;
using MediaMTX_Yuki_GUI.Models;


using L = MediaMTX_Yuki_GUI.Services.Localization;

namespace MediaMTX_Yuki_GUI.ViewModels;

/// <summary>两个列表的快捷排序与拖拽手动排序。</summary>
/// <remarks>
/// 顺序不是纯粹的显示问题：目标列表的顺序就是 runtime.yml 里 forward 数组的顺序，
/// 也就是引擎那边每条转发的 pos。视图层若只排显示不排集合，指标就会贴错行（见 §5.5），
/// 所以这里的每一次排序都直接 Move 集合本身，再由 ApplyTargetsAsync 落盘并推给引擎。
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>排序键与 <see cref="Models.UiOptions.InputSort"/> 的取值一一对应。</summary>
    private static readonly string[] InputSortKeys = ["manual", "name", "protocol", "address"];

    private static readonly string[] TargetSortKeys = ["manual", "name", "platform", "protocol"];

    /// <summary>按文本比较的规则：中英文混排时让当前区域决定先后。</summary>
    private static readonly StringComparer TextComparer = StringComparer.CurrentCultureIgnoreCase;

    /// <summary>装载排序状态期间抑制回写配置。</summary>
    private bool _sortLoading;

    /// <summary>正在重排集合；重排会触发集合变更，回调里不要再排一次。</summary>
    private bool _applyingSort;

    [ObservableProperty]
    private int _inputSortIndex;

    /// <summary>排序方向。界面上不再单独给下拉，改由组合索引一起表达（见 <see cref="InputSortChoices"/>）。</summary>
    [ObservableProperty]
    private bool _inputSortDescending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputEnabledBoundary))]
    private bool _inputEnabledFirst = true;

    [ObservableProperty]
    private int _targetSortIndex;

    /// <summary>排序方向，含义同输入源。</summary>
    [ObservableProperty]
    private bool _targetSortDescending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetEnabledBoundary))]
    private bool _targetEnabledFirst = true;

    public IReadOnlyList<string> InputSortChoices => BuildSortChoices(
        L.T("c003-037"), [L.T("c003-009"), L.T("c003-011"), L.T("c003-012")]);

    public IReadOnlyList<string> TargetSortChoices => BuildSortChoices(
        L.T("c004-048"), [L.T("l004-004"), L.T("c004-012"), L.T("c004-013")]);

    /// <summary>
    /// 排序下拉的内容：第一项是手动顺序，其余按「键 × 方向」展开成「备注 ↑ / 备注 ↓ / 协议 ↑…」。
    /// 两个列表各自传自己的那组键——同一句「备注」在输入页和目标页是两个键，不能共用。
    /// </summary>
    private static IReadOnlyList<string> BuildSortChoices(string manual, IReadOnlyList<string> names)
    {
        List<string> choices = [manual];

        foreach (string name in names)
        {
            choices.Add(name + " ↑");
            choices.Add(name + " ↓");
        }

        return choices;
    }

    /// <summary>组合索引拆回「排序键 + 方向」：0 是手动顺序（方向无意义），其余每两格一个键。</summary>
    private static (string Key, bool Descending) SplitSortIndex(string[] keys, int index)
    {
        if (index <= 0 || keys.Length < 2) return ("manual", false);

        int slot = Math.Min(index - 1, (keys.Length - 1) * 2 - 1);

        return (keys[(slot / 2) + 1], slot % 2 == 1);
    }

    /// <summary>反方向：把配置里的键与方向折成下拉要用的那个索引。</summary>
    private static int JoinSortIndex(string[] keys, string? key, bool descending)
    {
        int keyIndex = IndexOfKey(keys, key);

        if (keyIndex <= 0) return 0;

        return ((keyIndex - 1) * 2) + (descending ? 2 : 1);
    }

    // ====== 启用分组边界（拖拽时用，0 表示不分段） ======

    /// <summary>启用中的输入源在列表里占前多少项；未开启「优先显示已启用」时为 0。</summary>
    public int InputEnabledBoundary => InputEnabledFirst ? Inputs.Count(item => item.EffectiveEnabled) : 0;

    /// <summary>启用中的输出目标在列表里占前多少项；未开启「优先显示已启用」时为 0。</summary>
    public int TargetEnabledBoundary => TargetEnabledFirst ? Targets.Count(item => item.EffectiveEnabled) : 0;

    // ====== 装载与回写 ======

    /// <summary>把配置里的排序方式搬到界面上；此时集合还没排过，加载标志挡住回写。</summary>
    private void LoadSortState()
    {
        _sortLoading = true;

        InputSortIndex = JoinSortIndex(InputSortKeys, _cfg.Ui.InputSort, _cfg.Ui.InputSortDescending);
        InputEnabledFirst = _cfg.Ui.InputEnabledFirst;

        TargetSortIndex = JoinSortIndex(TargetSortKeys, _cfg.Ui.TargetSort, _cfg.Ui.TargetSortDescending);
        TargetEnabledFirst = _cfg.Ui.TargetEnabledFirst;

        _sortLoading = false;
    }

    private static int IndexOfKey(string[] keys, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;

        int index = Array.FindIndex(keys, candidate => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? 0 : index;
    }

    /// <summary>启动时按配置排一次；顺序真的变了才回写，省得每次开机都动一次配置文件。</summary>
    private void ApplySortOnStartup()
    {
        bool changed = ApplyInputSort();
        changed |= ApplyTargetSort();

        if (!changed) return;

        SyncOrderToConfig();
    }

    /// <summary>把界面上的顺序写回配置并落盘，下次启动原样还原。</summary>
    private void SyncOrderToConfig()
    {
        _cfg.Inputs = Inputs.Select(item => item.ToModel()).ToList();
        _cfg.Targets = Targets.Select(item => item.ToModel()).ToList();
        _store.Save(_cfg);
    }

    // ====== 属性回调 ======

    partial void OnInputSortIndexChanged(int value)
    {
        if (_sortLoading) return;

        (string key, bool descending) = SplitSortIndex(InputSortKeys, value);

        _cfg.Ui.InputSort = key;
        _cfg.Ui.InputSortDescending = descending;

        // 方向是索引的一部分，这里同步一下字段，免得它和下拉显示的项对不上
        _sortLoading = true;
        InputSortDescending = descending;
        _sortLoading = false;

        ApplyInputSort();
        ScheduleApply();
    }

    partial void OnInputEnabledFirstChanged(bool value)
    {
        if (_sortLoading) return;

        _cfg.Ui.InputEnabledFirst = value;
        ApplyInputSort();
        ScheduleApply();
    }

    partial void OnTargetSortIndexChanged(int value)
    {
        if (_sortLoading) return;

        (string key, bool descending) = SplitSortIndex(TargetSortKeys, value);

        _cfg.Ui.TargetSort = key;
        _cfg.Ui.TargetSortDescending = descending;

        _sortLoading = true;
        TargetSortDescending = descending;
        _sortLoading = false;

        ApplyTargetSort();
        ScheduleApply();
    }

    partial void OnTargetEnabledFirstChanged(bool value)
    {
        if (_sortLoading) return;

        _cfg.Ui.TargetEnabledFirst = value;
        ApplyTargetSort();
        ScheduleApply();
    }

    // ====== 排序 ======

    /// <summary>按当前设置排输入源，返回是否真的挪动过。</summary>
    private bool ApplyInputSort()
    {
        if (_applyingSort) return false;

        string key = _cfg.Ui.InputSort;

        // 纯手动顺序又不分组时，界面顺序就是配置顺序，不必碰
        if (key == "manual" && !InputEnabledFirst) return false;

        Func<InputItemViewModel, string> selector = key switch
        {
            "name" => item => item.DisplayName,
            "protocol" => item => item.Protocol,
            "address" => item => item.PushUrl,
            _ => _ => string.Empty,
        };

        _applyingSort = true;

        try
        {
            return Reorder(Inputs, InputEnabledFirst, key != "manual", InputSortDescending, selector);
        }
        finally
        {
            _applyingSort = false;
        }
    }

    /// <summary>按当前设置排输出目标，返回是否真的挪动过。</summary>
    private bool ApplyTargetSort()
    {
        if (_applyingSort) return false;

        string key = _cfg.Ui.TargetSort;

        if (key == "manual" && !TargetEnabledFirst) return false;

        Func<TargetItemViewModel, string> selector = key switch
        {
            "name" => item => item.DisplayName,
            "platform" => item => item.Preset.DisplayName,
            "protocol" => item => item.Protocol,
            _ => _ => string.Empty,
        };

        _applyingSort = true;

        try
        {
            return Reorder(Targets, TargetEnabledFirst, key != "manual", TargetSortDescending, selector);
        }
        finally
        {
            _applyingSort = false;
        }
    }

    /// <summary>
    /// 先按「已启用排前面」分组，再按选中的键排。分组关掉而排序键又排上时，
    /// 第二级排序是稳定排序，未启用的不会打乱彼此的相对位置。
    /// </summary>
    private static bool Reorder<T>(
        ObservableCollection<T> items,
        bool enabledFirst,
        bool useKey,
        bool descending,
        Func<T, string> selector)
    {
        if (items.Count < 2) return false;

        IOrderedEnumerable<T> ordered = enabledFirst
            ? items.OrderBy(item => EnabledRank(item))
            : items.OrderBy(_ => 0);

        if (useKey)
        {
            ordered = descending
                ? ordered.ThenByDescending(selector, TextComparer)
                : ordered.ThenBy(selector, TextComparer);
        }

        List<T> target = ordered.ToList();
        bool moved = false;

        for (int index = 0; index < target.Count; index++)
        {
            int from = items.IndexOf(target[index]);

            if (from == index || from < 0) continue;

            items.Move(from, index);
            moved = true;
        }

        return moved;
    }

    /// <summary>分组只看「此刻是否真的在用」：用户关掉的和程序禁用的都沉到下面去。</summary>
    private static int EnabledRank<T>(T item)
        => item switch
        {
            InputItemViewModel input => input.EffectiveEnabled ? 0 : 1,
            TargetItemViewModel target => target.EffectiveEnabled ? 0 : 1,
            _ => 0,
        };

    // ====== 拖拽 ======

    /// <summary>拖动输入源：启用项与停用项各自成组，跨组拖动会被夹回本组。</summary>
    [RelayCommand]
    private void MoveInputRow(ReorderRequest? request)
    {
        if (request is null) return;

        int from = request.From;

        if (from < 0 || from >= Inputs.Count) return;

        int to = ClampToGroup(Inputs, from, request.To, InputEnabledFirst);

        if (to == from) return;

        DropInputSortToManual();
        Inputs.Move(from, to);
    }

    /// <summary>拖动输出目标：顺序即转发顺序，松手后立刻推给引擎。</summary>
    [RelayCommand]
    private void MoveTargetRow(ReorderRequest? request)
    {
        if (request is null) return;

        int from = request.From;

        if (from < 0 || from >= Targets.Count) return;

        int to = ClampToGroup(Targets, from, request.To, TargetEnabledFirst);

        if (to == from) return;

        DropTargetSortToManual();
        Targets.Move(from, to);
    }

    /// <summary>松手之后提交一次；只等 400 ms 防抖会漏掉一帧，指标就贴错行了。</summary>
    [RelayCommand]
    private void CommitInputOrder()
        => ScheduleApply();

    [RelayCommand]
    private void CommitTargetOrder()
        => _ = ApplyTargetsAsync();

    /// <summary>把请求的目标位置夹在本行所属的那一组里：启用项占 [0, boundary)。</summary>
    private static int ClampToGroup<T>(ObservableCollection<T> items, int from, int requested, bool enabledFirst)
    {
        int to = Math.Clamp(requested, 0, items.Count - 1);

        if (!enabledFirst) return to;

        int boundary = items.Count(item => EnabledRank(item) == 0);
        bool enabled = EnabledRank(items[from]) == 0;

        return enabled
            ? Math.Min(to, Math.Max(boundary - 1, 0))
            : Math.Max(to, boundary);
    }

    /// <summary>
    /// 一拖动就切回手动顺序。这里必须绕过属性回调：回调会把集合按旧排序键重排，
    /// 刚拖出来的顺序当场被冲掉。
    /// </summary>
    private void DropInputSortToManual()
    {
        if (_cfg.Ui.InputSort != "manual")
        {
            _cfg.Ui.InputSort = "manual";

            _sortLoading = true;
            InputSortIndex = 0;
            InputSortDescending = false;
            _sortLoading = false;
        }
    }

    private void DropTargetSortToManual()
    {
        if (_cfg.Ui.TargetSort != "manual")
        {
            _cfg.Ui.TargetSort = "manual";

            _sortLoading = true;
            TargetSortIndex = 0;
            TargetSortDescending = false;
            _sortLoading = false;
        }
    }
}
