using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MediaMTX_Yuki_GUI.ViewModels;

namespace MediaMTX_Yuki_GUI.Views;

/// <summary>运行日志页：引擎输出列表，滚动条自动跟到底部。</summary>
public partial class LogsPage : UserControl
{
    private MainViewModel? _viewModel;

    public LogsPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _viewModel = viewModel;
        _viewModel.Logs.CollectionChanged += OnLogsChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Logs.CollectionChanged -= OnLogsChanged;
            _viewModel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>新行进来就滚到底。放到 Background 优先级，等布局长完再滚，否则会差一行。</summary>
    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(ScrollToEnd, DispatcherPriority.Background);
    }

    private void ScrollToEnd()
    {
        LogScroller?.ScrollToEnd();
    }
}
