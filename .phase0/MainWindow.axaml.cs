using System;
using Avalonia.Controls;
using FluentAvalonia.UI.Windowing;
using Avalonia.Threading;

namespace Phase0;

public partial class MainWindow : FAAppWindow
{
    private readonly DispatcherTimer _timer;
    private bool _expanded;

    public MainWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        _timer.Tick += (_, _) => Toggle();

        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void Toggle()
    {
        _expanded = !_expanded;
        AnimHost.Height = _expanded ? 280 : 0;
        StatusText.Text = _expanded
            ? "动画中：第 1 栏高度 0 → 280（0.9 秒），下方四栏正在被推下去"
            : "动画中：第 1 栏高度 280 → 0（0.9 秒），下方四栏正在被推上来";
    }
}
