using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>行内删除的气泡确认：贴在按钮上方问一句，点「删除」才真删。</summary>
/// <remarks>
/// 为什么不用 XAML 内联 Flyout：弹出层的内容不在主视觉树里，<c>$parent[UserControl]</c>
/// 之类往回找 DataContext 的写法在弹出后会失效，绑定全是空。这里整块用代码搭，
/// 回调直接闭包捕获，绕开上下文传递。
///
/// 为什么行内用气泡、批量用模态：行内删的是一条，错了一眼能看见、重加也就几秒；
/// 批量删的是勾中的一整片，误触代价完全不同，得认真挡一下。用 Flyout 而不是 Popup，
/// 是因为 Flyout 自带「点外面就关」与焦点管理，不用自己收拾残局。
/// </remarks>
public static class Popconfirm
{
    /// <summary>在 <paramref name="anchor"/> 上方弹出确认气泡。</summary>
    /// <param name="anchor">贴着哪个控件弹。</param>
    /// <param name="message">问什么，例如「删除这一行？」。</param>
    /// <param name="confirmText">确认按钮的字。</param>
    /// <param name="cancelText">取消按钮的字。</param>
    /// <param name="onConfirm">点了确认之后做什么。</param>
    public static void Show(
        Control anchor,
        string message,
        string confirmText,
        string cancelText,
        Action onConfirm)
    {
        TextBlock text = new()
        {
            Text = message,
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 240,
        };

        Button cancel = new()
        {
            Content = cancelText,
            FontSize = 12,
            Padding = new Thickness(12, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        Button confirm = new()
        {
            Content = confirmText,
            FontSize = 12,
            Padding = new Thickness(12, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        // 全局样式在 App 层，弹出层里的按钮也命中
        confirm.Classes.Add("danger-solid");

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        actions.Children.Add(cancel);
        actions.Children.Add(confirm);

        StackPanel body = new()
        {
            Spacing = 0,
            Children = { text, actions },
        };

        Border frame = new()
        {
            Padding = new Thickness(14, 12),
            CornerRadius = new CornerRadius(8),
            Background = Resource(anchor, "DsSurfaceOverlayBrush"),
            BorderBrush = Resource(anchor, "DsBorderBrush"),
            BorderThickness = new Thickness(1),
            Child = body,
        };

        Flyout flyout = new()
        {
            Content = frame,

            // 右边缘对齐而不是居中：删除按钮永远在每行的最右一列，
            // 居中弹会有一半跑到窗口外面去，右边被裁掉连按钮都摸不到。
            Placement = PlacementMode.TopEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Transient,

            // 宿主容器那层底色 / 边框 / 内边距全部抹掉（样式见 DesignSystem 的 FlyoutPresenter.bare），
            // 只留下面这一层圆角框，不然看着是框外套框
            FlyoutPresenterClasses = { "bare" },
        };

        cancel.Click += (_, _) => flyout.Hide();

        confirm.Click += (_, _) =>
        {
            flyout.Hide();

            onConfirm();
        };

        flyout.ShowAt(anchor);
    }

    /// <summary>从锚点身上取资源字典里的笔刷；取不到就返回 null，让它退回默认底色。</summary>
    private static Avalonia.Media.IBrush? Resource(Control anchor, string key)
        => anchor.TryFindResource(key, anchor.ActualThemeVariant, out object? value) && value is Avalonia.Media.IBrush brush
            ? brush
            : null;
}
