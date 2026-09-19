#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
/// <para>
/// 所有窗口都「整窗可拖拽」且支持「按 key 去重（不可重复打开同一功能弹窗）」——
/// 这是统一把主界面/组件内的 ContentDialog 迁出来的硬性要求（参考组件外观编辑器）。
/// 主题统一按用户「设置里存的偏好」套用（<see cref="ThemeManager.Apply"/>），
/// 而非继承宿主窗口的主题，从根本上消除「弹窗始终为深色」的问题。
/// </para>
/// </summary>
public static class CenteredDialog
{
    private const double DialogWidth = 440;

    /// <summary>通用内容弹窗的返回结果：Committed=主按钮；Secondary=次按钮；Cancelled=取消/关闭。</summary>
    public enum HostedDialogResult
    {
        Committed,
        Secondary,
        Cancelled,
    }

    // 去重注册表：同一个 dedupeKey 只允许一个窗口存在，重复打开时置顶已有窗口。
    private static readonly Dictionary<string, Window> _openByKey = new();
    private static readonly object _keyLock = new();

    private static bool IsOpen(string key, out Window? win)
    {
        lock (_keyLock) return _openByKey.TryGetValue(key, out win);
    }
    private static void RegisterOpen(string key, Window win)
    {
        lock (_keyLock) _openByKey[key] = win;
    }
    private static void RemoveOpen(string key)
    {
        lock (_keyLock) _openByKey.Remove(key);
    }

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
        Window? owner = null,
        string? dedupeKey = null)
    {
        // 不可重复：同一 key 已开则置顶并返回 false（重复调用方据此不执行破坏性动作）
        if (dedupeKey != null && IsOpen(dedupeKey, out var existing))
        {
            try { WindowInterop.SetForegroundWindow(WindowInterop.GetHwnd(existing)); } catch { }
            return Task.FromResult(false);
        }

        var tcs = new TaskCompletionSource<bool>();
        double height = 150 + (string.IsNullOrWhiteSpace(message) ? 0 : 60);
        var win = BuildWindow(title, message, height, owner);
        if (dedupeKey != null) RegisterOpen(dedupeKey, win);

        void Finish(bool result)
        {
            if (tcs.Task.IsCompleted) return;
            tcs.TrySetResult(result);
            if (dedupeKey != null) RemoveOpen(dedupeKey);
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

        Mount(win, content, height, owner, () => { if (dedupeKey != null) RemoveOpen(dedupeKey); Finish(false); });
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

    /// <summary>
    /// 居中顶层「通用内容」弹窗：承载任意 <see cref="FrameworkElement"/>（编辑框 / 标签编辑器 / 预览宿主等），
    /// 主界面与组件的所有功能弹窗统一走这里，从而变成外部、以屏幕为中心、可拖动、不可重复的窗口。
    /// <para>
    /// 返回 <see cref="HostedDialogResult"/>：调用方在 <see cref="HostedDialogResult.Committed"/> 时读取
    /// 自己传入的 <paramref name="content"/> 引用（弹窗关闭后该引用仍有效）拿到用户输入。
    /// </para>
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="content">承载的内容元素（调用方持有引用，提交后自行读取其值）。</param>
    /// <param name="owner">发起窗口（用于决定居中显示器）；null 取主显示器。</param>
    /// <param name="dedupeKey">去重键；非空时同 key 只允许一个窗口，重复调用置顶已有窗口并返回 Cancelled。</param>
    /// <param name="width">窗口逻辑宽（px，不含 DPI）。</param>
    /// <param name="height">窗口逻辑高（px，不含 DPI）。</param>
    /// <param name="primaryText">主按钮文字（null=不显示）。</param>
    /// <param name="secondaryText">次按钮文字（null=不显示，用于三选一场景如导入备份）。</param>
    /// <param name="cancelText">取消/关闭按钮文字（null=不显示）。</param>
    public static Task<HostedDialogResult> ShowContentAsync(
        string title,
        FrameworkElement content,
        Window? owner = null,
        string? dedupeKey = null,
        double width = 440,
        double height = 360,
        string? primaryText = "确定",
        string? secondaryText = null,
        string? cancelText = "取消")
    {
        // 不可重复：同一 key 已开则置顶已有窗口，丢弃本次重复请求（返回 Cancelled，调用方据此不落库）
        if (dedupeKey != null && IsOpen(dedupeKey, out var existing))
        {
            try { WindowInterop.SetForegroundWindow(WindowInterop.GetHwnd(existing)); } catch { }
            return Task.FromResult(HostedDialogResult.Cancelled);
        }

        var tcs = new TaskCompletionSource<HostedDialogResult>();
        var win = BuildWindow(title, null, height, owner);
        if (dedupeKey != null) RegisterOpen(dedupeKey, win);

        void Finish(HostedDialogResult r)
        {
            if (tcs.Task.IsCompleted) return;
            tcs.TrySetResult(r);
            if (dedupeKey != null) RemoveOpen(dedupeKey);
            SafeClose(win);
        }

        var root = new StackPanel { Spacing = 12, Margin = new Thickness(4) };
        root.Children.Add(MakeTitle(title));
        root.Children.Add(content);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Button? primary = null, secondary = null, cancel = null;
        if (cancelText != null) { cancel = MakeButton(cancelText, false); buttons.Children.Add(cancel); }
        if (secondaryText != null) { secondary = MakeButton(secondaryText, false); buttons.Children.Add(secondary); }
        if (primaryText != null) { primary = MakeButton(primaryText, true); buttons.Children.Add(primary); }
        if (buttons.Children.Count > 0) root.Children.Add(buttons);

        if (cancel != null) cancel.Click += (_, _) => Finish(HostedDialogResult.Cancelled);
        if (secondary != null) secondary.Click += (_, _) => Finish(HostedDialogResult.Secondary);
        if (primary != null) primary.Click += (_, _) => Finish(HostedDialogResult.Committed);
        // Esc 关闭（无主按钮时也允许关闭）
        root.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; Finish(HostedDialogResult.Cancelled); }
        };

        var grid = BuildCard(root);
        MountCore(win, grid, width, height, owner, () =>
        {
            if (dedupeKey != null) RemoveOpen(dedupeKey);
            Finish(HostedDialogResult.Cancelled);
        });
        DispatcherHelper(() => (primary ?? cancel)?.Focus(FocusState.Programmatic));
        return tcs.Task;
    }

    // ───────────────────────── 构建 ─────────────────────────

    /// <summary>把用户「设置里存的」主题偏好折算成窗口实际渲染的 <see cref="ElementTheme"/>，
    /// 让代码侧解析的画笔与窗口根元素的 RequestedTheme 完全一致（消除弹窗主题错乱）。</summary>
    private static ElementTheme EffectiveTheme()
    {
        var pref = new SettingsStore().LoadTheme();
        return pref switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private static Window BuildWindow(string title, string? message, double height, Window? owner)
    {
        var win = new Window { Title = title };
        return win;
    }

    private static void Mount(Window win, StackPanel content, double height, Window? owner, System.Action onClosed)
    {
        var grid = BuildCard(content);
        MountCore(win, grid, DialogWidth, height, owner, onClosed);
    }

    /// <summary>把卡片（Border 套内容）放进整窗可拖拽的容器，并居中、置顶、去默认边框、圆角。</summary>
    private static void MountCore(Window win, FrameworkElement root, double width, double height, Window? owner, System.Action onClosed)
    {
        win.Content = root;
        // 必须显式把窗口根 RequestedTheme 套成「用户存储主题」：新建窗口默认继承已冻结的应用级主题，
        // 用户运行期切换深浅色后应用级主题不变、且窗口 Content 在 BuildWindow 时尚为 null（Apply 会落空），
        // 不在这里补设就会让弹窗停留在旧主题——这正是「弹窗始终为深色」的根因之一。
        try { ThemeManager.Apply(win, new SettingsStore().LoadTheme()); } catch { }
        WindowInterop.RemoveDefaultWindowFrame(win);
        MakeWindowDraggable(win, root);   // 整窗可拖动（避开输入控件）
        var scale = WindowInterop.GetScale(win);
        var w = (int)(width * scale);
        var h = (int)(height * scale);
        // 以「发起弹窗的窗口所在显示器」为基准居中；无 owner 时取主显示器
        var work = owner is null ? WindowInterop.GetWorkArea(win) : WindowInterop.GetWorkArea(owner);
        var x = work.X + Math.Max(0, (work.Width - w) / 2);
        var y = work.Y + Math.Max(0, (work.Height - h) / 2);
        win.AppWindow.MoveAndResize(new RectInt32(x, y, w, h));

        // 必须在 MoveAndResize 之后按真实物理尺寸裁圆角：先裁再缩放会让区域与窗口不符。
        // SetWindowRgn 真正物理裁切无黑角，取代 DWM 仅靠视觉圆角（无边框窗口会留下四角黑块）。
        WindowInterop.SetRoundedWindowRegion(win, 10);

        win.Closed += (_, _) => onClosed();
        win.Activate();
        WindowInterop.SetTopmost(win, true);
        try { WindowInterop.SetForegroundWindow(WindowInterop.GetHwnd(win)); } catch { }
        // 拿到输入焦点后再取消置顶，避免它永远压住其它窗口
        WindowInterop.SetTopmost(win, false);
    }

    /// <summary>卡片：单张圆角 Border（背景 + 边框 + 圆角），直接作为窗口根内容——
    /// 整窗按同一半径裁成圆角矩形，避免「白色大圆角 + 深灰小圆角」双层叠加的灰色观感。</summary>
    private static Border BuildCard(UIElement content)
    {
        return new Border
        {
            Background = Brush("CardBackgroundFillColorDefaultBrush", Colors.White),
            BorderBrush = Brush("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16, 20, 16),
            Child = content,
        };
    }

    private static TextBlock MakeTitle(string title) => new()
    {
        Text = title,
        FontSize = 16,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("TextFillColorPrimaryBrush", Colors.Black),
    };

    /// <summary>按用户存储主题解析画笔（ThemeResource 在代码里拿不到，靠 ThemeBrush 兜底）。</summary>
    private static Brush Brush(string key, Color fallback) => ThemeBrush.For(EffectiveTheme(), key)
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

    // ───────────────────────── 拖拽 ─────────────────────────
    // 与组件外观编辑器同一套「整窗可拖拽」实现：落在输入控件上放行，落在留白/展示区则可拖动窗口。

    private static void MakeWindowDraggable(Window owner, FrameworkElement surface)
    {
        WindowInterop.POINT start = default;
        RectInt32 rect = default;
        bool dragging = false;

        surface.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(surface).Properties.IsRightButtonPressed) return;  // 右键留给菜单/取色
            if (IsInputControl(e.OriginalSource as DependencyObject)) return;        // 输入控件优先，不劫持
            dragging = true;
            WindowInterop.GetCursorPos(out start);
            rect = WindowInterop.GetWindowRect(owner);
            try { surface.CapturePointer(e.Pointer); } catch { }
            e.Handled = true;
        };
        surface.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            WindowInterop.GetCursorPos(out var p);
            owner.AppWindow.MoveAndResize(new RectInt32(
                rect.X + p.X - start.X, rect.Y + p.Y - start.Y, rect.Width, rect.Height));
            e.Handled = true;
        };
        surface.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            try { surface.ReleasePointerCapture(e.Pointer); } catch { }
        };
        surface.PointerCanceled += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            try { surface.ReleasePointerCapture(e.Pointer); } catch { }
        };
    }

    private static bool IsInputControl(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is Button or RepeatButton or ToggleButton or ToggleSwitch
                or Slider or Thumb or ScrollBar
                or TextBox or RichEditBox or PasswordBox or AutoSuggestBox
                or ComboBox or NumberBox or CheckBox or RadioButton
                or Canvas or ListViewBase or ItemsRepeater or MenuFlyoutPresenter
                or ComboBoxItem or GridViewItem or ListViewItem)
                return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }
}
