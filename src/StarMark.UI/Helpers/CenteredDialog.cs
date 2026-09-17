#nullable enable
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace StarMark.UI.Helpers;

/// <summary>
/// 屏幕中央的独立顶层对话框窗口。
/// <para>
/// 为什么不用 <c>ContentDialog</c>：组件窗口往往只有 200×150 这么大，
/// 挂在组件 <c>XamlRoot</c> 上的 ContentDialog 会被窗口边界裁掉——用户点「保存当前组件布局…」
/// 时看到的是一个几乎看不见的弹窗（DeskBox 在小组件里同样刻意避开 ContentDialog）。
/// 这里改为新建一个无边框小窗口：居中于屏幕、置顶显示、尺寸足够，永远看得见。
/// </para>
/// </summary>
public static class CenteredDialog
{
    private const double DialogWidth = 440;

    /// <summary>
    /// 居中顶层输入对话框。返回用户输入（取消 / 关闭返回 null）。
    /// </summary>
    public static Task<string?> PromptAsync(
        string title,
        string? message = null,
        string placeholder = "",
        string? defaultText = null,
        string primaryText = "确定",
        string cancelText = "取消",
        Window? owner = null)
    {
        var tcs = new TaskCompletionSource<string?>();
        var box = new TextBox
        {
            PlaceholderText = placeholder,
            Text = defaultText ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0),
        };
        box.Select(box.Text.Length, 0);

        double height = 150 + (string.IsNullOrWhiteSpace(message) ? 0 : 48) + 62;
        var win = BuildWindow(title, message, height, owner);

        void Finish(string? result)
        {
            if (tcs.Task.IsCompleted) return;
            tcs.TrySetResult(result);
            SafeClose(win);
        }

        var primary = MakeButton(primaryText, true);
        var cancel = MakeButton(cancelText, false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { cancel, primary },
        };

        var content = new StackPanel
        {
            Children =
            {
                MakeTitle(title),
                MakeMessage(message),
                box,
                buttons,
            },
        };

        primary.Click += (_, _) => Finish(string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim());
        cancel.Click += (_, _) => Finish(null);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Finish(string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim()); }
            else if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; Finish(null); }
        };

        Mount(win, content, height, owner, () => Finish(null));
        DispatcherHelper(() => box.Focus(FocusState.Programmatic));
        return tcs.Task;
    }

    /// <summary>居中顶层确认对话框（确认返回 true）。</summary>
    public static Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryText = "确定",
        string cancelText = "取消",
        Window? owner = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        double height = 150 + (string.IsNullOrWhiteSpace(message) ? 0 : 60);
        var win = BuildWindow(title, message, height, owner);

        void Finish(bool result)
        {
            if (tcs.Task.IsCompleted) return;
            tcs.TrySetResult(result);
            SafeClose(win);
        }

        var primary = MakeButton(primaryText, true);
        var cancel = MakeButton(cancelText, false);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { cancel, primary },
        };

        var content = new StackPanel { Children = { MakeTitle(title), MakeMessage(message), buttons } };
        primary.Click += (_, _) => Finish(true);
        cancel.Click += (_, _) => Finish(false);
        primary.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; Finish(false); } };

        Mount(win, content, height, owner, () => Finish(false));
        DispatcherHelper(() => cancel.Focus(FocusState.Programmatic));
        return tcs.Task;
    }

    /// <summary>居中顶层提示框。</summary>
    public static Task MessageAsync(string title, string message, Window? owner = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        double height = 150 + (string.IsNullOrWhiteSpace(message) ? 0 : 60);
        var win = BuildWindow(title, message, height, owner);

        void Finish()
        {
            if (tcs.Task.IsCompleted) return;
            tcs.TrySetResult(true);
            SafeClose(win);
        }

        var ok = MakeButton("知道了", true);
        var content = new StackPanel
        {
            Children =
            {
                MakeTitle(title),
                MakeMessage(message),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 14, 0, 0),
                    Children = { ok },
                },
            },
        };
        ok.Click += (_, _) => Finish();
        ok.KeyDown += (_, e) => { if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Escape) { e.Handled = true; Finish(); } };

        Mount(win, content, height, owner, Finish);
        DispatcherHelper(() => ok.Focus(FocusState.Programmatic));
        return tcs.Task;
    }

    // ───────────────────────── 构建 ─────────────────────────

    private static Window BuildWindow(string title, string? message, double height, Window? owner)
    {
        var win = new Window { Title = title };
        try { ThemeManager.Apply(win, new SettingsStore().LoadTheme()); } catch { }
        return win;
    }

    private static void Mount(Window win, StackPanel content, double height, Window? owner, System.Action onClosed)
    {
        var card = new Border
        {
            Background = Brush("CardBackgroundFillColorDefaultBrush", Colors.White),
            BorderBrush = Brush("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16, 20, 16),
            Child = content,
        };

        var root = new Grid
        {
            Background = Brush("ApplicationPageBackgroundThemeBrush", Colors.White),
            Children = { card },
        };

        win.Content = root;
        WindowInterop.RemoveDefaultWindowFrame(win);
        WindowInterop.ApplyRoundedCorners(win);

        var scale = WindowInterop.GetScale(win);
        var w = (int)(DialogWidth * scale);
        var h = (int)(height * scale);
        // 以「发起弹窗的窗口所在显示器」为基准居中；无 owner 时取主显示器
        var work = owner is null ? WindowInterop.GetWorkArea(win) : WindowInterop.GetWorkArea(owner);
        var x = work.X + Math.Max(0, (work.Width - w) / 2);
        var y = work.Y + Math.Max(0, (work.Height - h) / 2);
        win.AppWindow.MoveAndResize(new RectInt32(x, y, w, h));

        win.Closed += (_, _) => onClosed();
        win.Activate();
        WindowInterop.SetTopmost(win, true);
        try { WindowInterop.SetForegroundWindow(WindowInterop.GetHwnd(win)); } catch { }
        // 拿到输入焦点后再取消置顶，避免它永远压住其它窗口
        WindowInterop.SetTopmost(win, false);
    }

    private static TextBlock MakeTitle(string title) => new()
    {
        Text = title,
        FontSize = 16,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("TextFillColorPrimaryBrush", Colors.Black),
    };

    /// <summary>按当前系统主题解析画笔（ThemeResource 在代码里拿不到，靠 ThemeBrush 兜底）。</summary>
    private static Brush Brush(string key, Color fallback) => ThemeBrush.For(ElementTheme.Default, key)
        ?? new SolidColorBrush(fallback);

    private static UIElement MakeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return new Grid { Height = 0 };
        return new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Opacity = 0.85,
            FontSize = 13,
            Foreground = Brush("TextFillColorSecondaryBrush", Colors.Gray),
        };
    }

    private static Button MakeButton(string text, bool accent)
    {
        var btn = new Button
        {
            Content = text,
            MinWidth = 92,
            Padding = new Thickness(12, 5, 12, 5),
            CornerRadius = new CornerRadius(6),
        };
        if (accent)
        {
            btn.Background = Brush("AccentFillColorDefaultBrush", Colors.RoyalBlue);
            btn.Foreground = Brush("TextOnAccentFillColorPrimaryBrush", Colors.White);
        }
        return btn;
    }

    private static void SafeClose(Window win)
    {
        try { win.Close(); }
        catch { /* 已经关了 */ }
    }

    private static void DispatcherHelper(System.Action action)
    {
        try { Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => action()); }
        catch { try { action(); } catch { } }
    }
}
