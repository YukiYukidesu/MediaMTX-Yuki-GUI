using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using MediaMTX_Yuki_GUI.Controls;
using FluentAvalonia.UI.Windowing;
using MediaMTX_Yuki_GUI.Services;
using MediaMTX_Yuki_GUI.ViewModels;

namespace MediaMTX_Yuki_GUI.Views;

using L = Services.Localization;

/// <summary>
/// 看某一路的录像段，顺便删掉不要的那几段。
/// 引擎按录制目录扫盘，所以关掉录制之后历史片段照样列得出来。
/// </summary>
public sealed partial class RecordingsWindow : FAAppWindow, INotifyPropertyChanged
{
    private readonly string _pathName;

    private readonly string _displayName;

    private readonly Func<CancellationToken, Task<IReadOnlyList<RecordingSegmentInfo>?>> _loader;

    private readonly Func<string, string, CancellationToken, Task<bool>> _deleter;

    private string _statusText = "";

    private bool _isLoading;

    private string _emptyTitle = "";

    private string _emptyText = "";

    private CancellationTokenSource? _cts;

    public RecordingsWindow(
        string pathName,
        string displayName,
        string inputId,
        string address,
        Func<CancellationToken, Task<IReadOnlyList<RecordingSegmentInfo>?>> loader,
        Func<string, string, CancellationToken, Task<bool>> deleter)
    {
        InitializeComponent();

        // 小窗也上材质，跟主窗保持一致
        WindowBackdrop.Attach(this);

        _pathName = pathName;
        _displayName = displayName;
        _loader = loader;
        _deleter = deleter;

        // 窗口顶上一整条印全：备注、完整 Id、完整地址。列表里那列「编号」只截前 6 位，
        // 要核对是哪一条时得到这儿看全文。
        WindowTitle = L.T("c010-005") + " · " + displayName;
        Subtitle = L.T("c010-006")
            .Replace("{name}", displayName)
            .Replace("{id}", inputId)
            .Replace("{address}", address);
        EmptyTitle = L.T("c010-011");
        EmptyText = L.T("l010-001");

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        DeleteSegmentCommand = new AsyncRelayCommand<RecordingRow>(DeleteSegmentAsync);
        CloseCommand = new RelayCommand(() => Close());

        DataContext = this;

        CanMaximize = false;
        Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) => _cts?.Cancel();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string WindowTitle { get; }

    public string Subtitle { get; }

    public ObservableCollection<RecordingRow> Rows { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<RecordingRow> DeleteSegmentCommand { get; }

    public IRelayCommand CloseCommand { get; }

    /// <summary>列表里有一条都没有。空提示比空列表更能说明发生了什么。</summary>
    public bool IsEmpty => Rows.Count == 0 && !IsLoading;

    /// <summary>有录像片段时才画列头。无状态下列头没有可表的对象，留着只是噪音。</summary>
    public bool HasRows => Rows.Count > 0;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!Set(ref _isLoading, value)) return;

            Raise(nameof(IsEmpty));
        }
    }

    /// <summary>底部那行：段数、刚删成功、删失败都说在这里。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    /// <summary>
    /// 空状态那两行的第一行：没录过说「还没有录像」，读不到说「读不到录像列表」。
    /// 与 <see cref="EmptyText"/> 拆成两行，是照输入源页、输出目标页的空状态来的。
    /// </summary>
    public string EmptyTitle
    {
        get => _emptyTitle;
        private set => Set(ref _emptyTitle, value);
    }

    /// <summary>空列表时中间那句提示。读不到和没录过不是一回事，文案得分开。</summary>
    public string EmptyText
    {
        get => _emptyText;
        private set => Set(ref _emptyText, value);
    }

    // ====== 读写 ======

    private async Task LoadAsync()
    {
        // 连点刷新时把上一轮取消掉，免得两个结果互相覆盖
        _cts?.Cancel();
        _cts?.Dispose();

        CancellationTokenSource cts = new();
        _cts = cts;

        IsLoading = true;
        StatusText = "";

        try
        {
            IReadOnlyList<RecordingSegmentInfo>? segments = await _loader(cts.Token);

            if (cts.IsCancellationRequested) return;

            Rows.Clear();

            if (segments is null)
            {
                // null 是「读不到」，不是「没有」，别让人误以为一段都没录
                EmptyTitle = L.T("c010-012");
                EmptyText = L.T("l010-007");
                Raise(nameof(IsEmpty));
                Raise(nameof(HasRows));

                return;
            }

            foreach (RecordingSegmentInfo segment in segments)
            {
                Rows.Add(RecordingRow.From(segment.Start, segment.Duration));
            }

            EmptyTitle = L.T("c010-011");
            EmptyText = L.T("l010-001");
            StatusText = Rows.Count == 0 ? "" : L.T("l010-003").Replace("{n}", Rows.Count.ToString(CultureInfo.InvariantCulture));
        }
        catch (OperationCanceledException)
        {
            // 窗口关掉或又点了刷新，什么都不用做
        }
        finally
        {
            if (ReferenceEquals(_cts, cts)) IsLoading = false;

            Raise(nameof(IsEmpty));
            Raise(nameof(HasRows));
        }
    }

    /// <summary>
    /// 行内删一段录像：先贴按钮问一句，确认了才真删。
    /// 这窗里一次只删一段，气泡确认够用，不必再开一个模态窗。
    /// </summary>
    private void OnDeleteSegmentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: RecordingRow row } button) return;

        Popconfirm.Show(
            button,
            L.T("l010-004"),
            L.T("t006-001"),
            L.T("t006-002"),
            () => _ = DeleteSegmentAsync(row));
    }

    private async Task DeleteSegmentAsync(RecordingRow? row)
    {
        if (row is null) return;

        bool ok = await _deleter(_pathName, row.Start, CancellationToken.None);

        if (ok)
        {
            Rows.Remove(row);
            StatusText = L.T("l010-005");
            Raise(nameof(IsEmpty));
            Raise(nameof(HasRows));

            return;
        }

        StatusText = L.T("l010-006");
    }

    // ====== 通知 ======

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        Raise(name);

        return true;
    }

    private void Raise(string? name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
