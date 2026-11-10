using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaMTX_Yuki_GUI.Models;
using MediaMTX_Yuki_GUI.Services;

namespace MediaMTX_Yuki_GUI.ViewModels;


using L = MediaMTX_Yuki_GUI.Services.Localization;
/// <summary>主页「输入源」那一块的集合、选择态与批量操作。</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<InputItemViewModel> Inputs { get; } = new();

    public int SelectedInputCount
        => Inputs.Count(item => item.IsSelected);

    public bool HasInputSelection
        => SelectedInputCount > 0;

    public bool IsAllInputsSelected
        => Inputs.Count > 0 && SelectedInputCount == Inputs.Count;

    public string InputSelectionText
        => SelectedInputCount == 0 ? L.T("c003-005") : L.T("c003-006") + SelectedInputCount + L.T("c003-007");

    public string SelectAllInputsText
        => IsAllInputsSelected ? L.T("c003-004") : L.T("c003-003");

    public bool IsInputsEmpty
        => Inputs.Count == 0;

    /// <summary>输入源列表是否非空，用来在空状态与表格之间切换。</summary>
    public bool IsInputsVisible
        => Inputs.Count > 0;

    /// <summary>
    /// 输出编辑页里的「绑定到哪条输入」下拉，第一条永远是「未指定」。
    /// 显示成「短 id:备注」——两条输入的备注可能一样，靠 Id 才能分出是哪一条。
    /// </summary>
    public IReadOnlyList<InputChoice> InputChoices
        => new[] { new InputChoice(string.Empty, L.T("c004-035")) }
            .Concat(Inputs.Select(input => new InputChoice(input.Id, input.ShortId + " : " + input.DisplayName)))
            .ToList();

    /// <summary>把「哪条输入推到哪些输出」重新算一遍，总览页与输出页都靠它。</summary>
    private void RefreshOverview()
    {
        foreach (InputItemViewModel input in Inputs)
        {
            input.Outputs.Clear();

            foreach (TargetItemViewModel target in Targets.Where(t => t.InputSourceId == input.Id))
            {
                input.Outputs.Add(target);
            }
        }

        foreach (TargetItemViewModel target in Targets)
        {
            InputItemViewModel? owner = Inputs.FirstOrDefault(i => i.Id == target.InputSourceId);

            target.InputSourceName = owner == null
                ? L.T("c004-035")
                : owner.ShortId + " : " + owner.DisplayName;
        }

        OnPropertyChanged(nameof(UnboundTargetCount));
        OnPropertyChanged(nameof(HasUnboundTargets));
        OnPropertyChanged(nameof(InputChoices));
    }

    /// <summary>删除某条输入时，把它身上的输出一律退回未指定，免得悄悄推到别处去。</summary>
    private void UnbindTargetsOf(string inputId)
    {
        foreach (TargetItemViewModel target in Targets)
        {
            if (string.Equals(target.InputSourceId, inputId, StringComparison.Ordinal))
            {
                target.InputSourceId = string.Empty;
            }
        }
    }

    /// <summary>把配置里的输入源装进界面集合。</summary>
    private void LoadInputs()
    {
        foreach (InputSource source in _cfg.Inputs)
        {
            Inputs.Add(InputItemViewModel.FromModel(source));
        }
    }

    /// <summary>订阅某一行的属性变化：勾选只刷新选择态，其它改动才落盘。</summary>
    private void AttachInput(InputItemViewModel item)
    {
        item.PropertyChanged += OnInputItemChanged;
    }

    private void DetachInput(InputItemViewModel item)
    {
        item.PropertyChanged -= OnInputItemChanged;
    }

    private void OnInputsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (InputItemViewModel item in e.NewItems.OfType<InputItemViewModel>()) AttachInput(item);
        }

        if (e.OldItems != null)
        {
            foreach (InputItemViewModel item in e.OldItems.OfType<InputItemViewModel>()) DetachInput(item);
        }

        OnPropertyChanged(nameof(IsInputsEmpty));
        OnPropertyChanged(nameof(IsInputsVisible));
        OnPropertyChanged(nameof(InputEnabledBoundary));
        RefreshInputSelection();
        RefreshOverview();
    }

    private void OnInputItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InputItemViewModel.IsSelected))
        {
            RefreshInputSelection();

            return;
        }

        // 轮询回填的显示字段不算配置变更：不挡的话每两秒就要重写一次 runtime.yml
        if (RuntimeProperties.Contains(e.PropertyName ?? string.Empty)) return;

        ApplyInputSort();
        OnPropertyChanged(nameof(InputEnabledBoundary));
        RefreshOverview();
        ScheduleApply();
    }

    private void RefreshInputSelection()
    {
        OnPropertyChanged(nameof(SelectedInputCount));
        OnPropertyChanged(nameof(HasInputSelection));
        OnPropertyChanged(nameof(InputSelectionText));
        OnPropertyChanged(nameof(IsAllInputsSelected));
        OnPropertyChanged(nameof(SelectAllInputsText));

        StartSelectedInputsCommand.NotifyCanExecuteChanged();
        StopSelectedInputsCommand.NotifyCanExecuteChanged();
        RemoveSelectedInputsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 批量置启用态：只有真改动了才落盘，避免无意义的文件写入。
    /// 返回实际改了几条，好让调用方把结果说给用户听。
    /// </summary>
    private int SetEnabledForInputSelection(bool enabled)
    {
        int changed = 0;

        foreach (InputItemViewModel item in Inputs)
        {
            if (!item.IsSelected || item.Enabled == enabled) continue;

            item.Enabled = enabled;
            changed++;
        }

        if (changed > 0) ScheduleApply();

        return changed;
    }

    [RelayCommand]
    private void ToggleSelectAllInputs()
    {
        bool selectAll = !IsAllInputsSelected;

        foreach (InputItemViewModel item in Inputs) item.IsSelected = selectAll;
    }

    [RelayCommand(CanExecute = nameof(HasInputSelection))]
    private void StartSelectedInputs()
    {
        int changed = SetEnabledForInputSelection(true);

        ShowToast(changed > 0
            ? L.T("t002-005").Replace("{n}", changed.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : L.T("t002-014"),
            changed > 0 ? ToastKind.Success : ToastKind.Info);
    }

    [RelayCommand(CanExecute = nameof(HasInputSelection))]
    private void StopSelectedInputs()
    {
        int changed = SetEnabledForInputSelection(false);

        ShowToast(changed > 0
            ? L.T("t002-006").Replace("{n}", changed.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : L.T("t002-015"),
            changed > 0 ? ToastKind.Success : ToastKind.Info);
    }

    /// <summary>批量删除输入源：勾中的会连带把指向它的目标解绑，所以先问一句。</summary>
    [RelayCommand(CanExecute = nameof(HasInputSelection))]
    private async Task RemoveSelectedInputsAsync()
    {
        List<InputItemViewModel> doomed = Inputs.Where(item => item.IsSelected).ToList();

        if (doomed.Count == 0) return;

        if (AppHost is not null)
        {
            bool ok = await AppHost.ConfirmAsync(
                L.T("t006-003"),
                L.T("t002-012").Replace("{n}", doomed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                L.T("t006-001"),
                destructive: true);

            if (!ok) return;
        }

        foreach (InputItemViewModel item in doomed)
        {
            UnbindTargetsOf(item.Id);
            Inputs.Remove(item);
        }

        RefreshOverview();
        ScheduleApply();

        ShowToast(L.T("t002-013").Replace("{n}", doomed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)), ToastKind.Success);
    }

    [RelayCommand]
    private async Task OpenInputAsync(InputItemViewModel? item)
    {
        if (item == null) return;

        await ShowInputEditorAsync(item, isNew: false);
    }

    [RelayCommand]
    private void CloseInput()
        => EditingInput = null;

    /// <summary>弹出输入源小窗；用户点「添加 / 保存」才写回并落盘。</summary>
    private async Task<bool> ShowInputEditorAsync(InputItemViewModel item, bool isNew)
    {
        if (AppHost is null)
        {
            return false;
        }

        // 弹窗期间给主界面盖遮罩，点不到侧栏，也就回不到上级页面
        IsDialogOpen = true;

        bool accepted;

        try
        {
            accepted = await AppHost.ShowInputEditorAsync(
                item,
                isNew,
                CollectOwnPorts(),
                CollectOtherInputNames(item),
                CollectOtherPaths(item),
                IsAdvancedMode,
                Accounts.ToList());
        }
        finally
        {
            IsDialogOpen = false;
        }

        if (!accepted)
        {
            return false;
        }

        if (isNew)
        {
            Inputs.Add(item);
            ApplyInputSort();
        }

        RefreshOverview();
        ScheduleApply();

        ShowToast(L.T(isNew ? "t002-001" : "t002-002").Replace("{name}", item.DisplayName), ToastKind.Success);

        return true;
    }

    /// <summary>
    /// 除自己之外，其它输入源已经在用的显示名。编辑窗拿它判断备注重不重，
    /// 重了只给黄字提示，不拦保存——用户要求允许重名。
    /// </summary>
    private List<string> CollectOtherInputNames(InputItemViewModel item)
        => Inputs
            .Where(other => !ReferenceEquals(other, item))
            .Select(other => other.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

    /// <summary>
    /// 除自己之外，其它输入源已经在用的路径名。
    /// 与备注不同：路径名是 runtime.yml 里 paths 下的键，撞了两个就只能剩一个，所以这个要拦住。
    /// </summary>
    private List<string> CollectOtherPaths(InputItemViewModel item)
        => Inputs
            .Where(other => !ReferenceEquals(other, item))
            .Select(other => other.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();

    /// <summary>本程序自己引擎正在占用的端口，探测时要把它们排除掉。</summary>
    private List<int> CollectOwnPorts()
        =>
        [
            _cfg.Engine.RtmpPort,
            _cfg.Engine.ApiPort,
            _cfg.Engine.MetricsPort,
            .. Inputs.Where(item => item.Enabled).Select(item => item.Port > 0 ? item.Port : InputProtocols.DefaultPort(item.Protocol)),
        ];

    [RelayCommand]
    private void ToggleInputEnabled(InputItemViewModel? item)
    {
        if (item == null) return;

        item.Enabled = !item.Enabled;

        ShowToast(L.T(item.Enabled ? "t002-003" : "t002-004").Replace("{name}", item.DisplayName), ToastKind.Success);
    }

    /// <summary>
    /// 单独开关某一输入的录制。
    /// 录制键写在 path 里、不是 forward，热更新推不动，所以标记一次引擎重启。
    /// </summary>
    [RelayCommand]
    private void ToggleInputRecord(InputItemViewModel? item)
    {
        if (item == null) return;

        item.Record = !item.Record;
        NeedsEngineRestart = true;

        ShowToast(L.T(item.Record ? "t002-007" : "t002-008").Replace("{name}", item.DisplayName), ToastKind.Warn);
    }

    /// <summary>
    /// 看这一路的录像。列表由引擎扫录制目录得出，所以关掉录制之后历史片段照样在。
    /// 删除走 Control API，删之前窗口会自己再问一次。
    /// </summary>
    [RelayCommand]
    private async Task ShowRecordingsAsync(InputItemViewModel? item)
    {
        if (item == null || AppHost is null) return;

        string path = item.Path;

        IsDialogOpen = true;

        try
        {
            await AppHost.ShowRecordingsAsync(
                path, item.DisplayName, item.Id, item.PushOrPullUrl, LoadSegmentsAsync, DeleteSegmentAsync);
        }
        finally
        {
            IsDialogOpen = false;
        }

        // 返回 null 表示这会儿读不到（引擎没在跑），而不是「一段都没录」
        async Task<IReadOnlyList<RecordingSegmentInfo>?> LoadSegmentsAsync(CancellationToken ct)
        {
            if (_api is null || !IsEngineRunning) return null;

            IReadOnlyList<RecordingInfo> recordings = await _api.GetRecordingsAsync(ct);

            RecordingInfo? mine = recordings.FirstOrDefault(
                info => string.Equals(info.Name, path, StringComparison.Ordinal));

            return mine?.Segments ?? [];
        }

        Task<bool> DeleteSegmentAsync(string pathName, string start, CancellationToken ct)
            => _api is null ? Task.FromResult(false) : _api.DeleteRecordingSegmentAsync(pathName, start, ct);
    }

    [RelayCommand]
    private void RemoveInput(InputItemViewModel? item)
    {
        if (item == null) return;

        UnbindTargetsOf(item.Id);

        if (ReferenceEquals(EditingInput, item)) EditingInput = null;

        Inputs.Remove(item);
        RefreshOverview();
        ScheduleApply();

        ShowToast(L.T("t002-009").Replace("{name}", item.DisplayName), ToastKind.Success);
    }

    /// <summary>新增一条输入：先弹窗填，确认了才加进列表。</summary>
    [RelayCommand]
    private async Task AddInputAsync()
    {
        InputItemViewModel item = new(new InputSource
        {
            Name = NextInputName(),
            Path = NextPathName(),
            Protocol = InputProtocols.Rtmp,
            Enabled = false,
        });

        await ShowInputEditorAsync(item, isNew: true);
    }

    /// <summary>
    /// 新增输入源时的默认备注：新建输入源、新建输入源2、新建输入源3…
    /// 只有撞上已有的名字才带数字，第一条不带。
    /// </summary>
    private string NextInputName()
    {
        HashSet<string> used = new(
            Inputs.Select(item => item.Name).Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.Ordinal);

        string baseName = L.T("c003-036");

        if (!used.Contains(baseName)) return baseName;

        for (int i = 2; ; i++)
        {
            string candidate = baseName + i.ToString(CultureInfo.InvariantCulture);

            if (!used.Contains(candidate)) return candidate;
        }
    }

    /// <summary>找一个没被占用的路径名：live、live2、live3…</summary>
    private string NextPathName()
    {
        HashSet<string> used = new(Inputs.Select(item => item.Path), StringComparer.Ordinal);

        string baseName = RuntimeYmlBuilder.PathName;

        if (!used.Contains(baseName)) return baseName;

        for (int i = 2; ; i++)
        {
            string candidate = baseName + i;

            if (!used.Contains(candidate)) return candidate;
        }
    }
}
