#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// 每组件外观编辑浮层（组件右键菜单「外观…」打开）。
/// 返回编辑后的 <see cref="WidgetAppearanceOverride"/>（全部字段为 null 视为清除覆盖、返回 null）；
/// 用户取消 / 关闭窗口返回 null。
/// </summary>
public static class WidgetAppearanceEditor
{
    // 预设调色板（#AARRGGBB）
    private static readonly (string Name, string Hex)[] Palette =
    {
        ("白", "#FFFFFFFF"), ("黑", "#FF000000"), ("深灰", "#FF2B2B2B"), ("浅灰", "#FFF3F3F3"),
        ("蓝", "#FF3B82F6"), ("青", "#FF06B6D4"), ("绿", "#FF16A34A"), ("黄", "#FFEAB308"),
        ("红", "#FFDC2626"), ("紫", "#FF8B5CF6"), ("粉", "#FFF472B6"),
    };

    public static Task<WidgetAppearanceOverride?> ShowAsync(Window owner, WidgetInstanceConfig config)
    {
        var tcs = new TaskCompletionSource<WidgetAppearanceOverride?>();
        var current = config.Appearance;

        var win = new Window { Title = "组件外观" };
        try { ThemeManager.Apply(win, new SettingsStore().LoadTheme()); } catch { }

        // ── 工作副本（编辑中实时改写，确定时一次性落盘）──
        WidgetBackdropKind? wBackdrop = current?.Backdrop;
        string? wBg = current?.BackgroundColor;
        string? wFg = current?.ForegroundColor;
        string? wBorder = current?.BorderColor;
        double? wThick = current?.BorderThickness;
        double? wRadius = current?.CornerRadius;
        double? wScale = current?.TextScale;

        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(4) };

        // 材质
        panel.Children.Add(MakeLabel("材质"));
        var backdropCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        backdropCombo.Items.Add(new ComboItem { Text = "跟随全局", Value = null });
        backdropCombo.Items.Add(new ComboItem { Text = "亚克力", Value = WidgetBackdropKind.Acrylic });
        backdropCombo.Items.Add(new ComboItem { Text = "云母", Value = WidgetBackdropKind.Mica });
        backdropCombo.Items.Add(new ComboItem { Text = "不透明", Value = WidgetBackdropKind.None });
        backdropCombo.SelectedIndex = IndexOfBackdrop(wBackdrop);
        backdropCombo.SelectionChanged += (_, _) =>
            wBackdrop = (backdropCombo.SelectedItem as ComboItem)?.Value;
        panel.Children.Add(backdropCombo);

        // 颜色三件套
        panel.Children.Add(BuildColorField("背景色", wBg, v => wBg = v));
        panel.Children.Add(BuildColorField("文本色", wFg, v => wFg = v));
        panel.Children.Add(BuildColorField("边框色", wBorder, v => wBorder = v));

        // 滑杆三件套
        panel.Children.Add(MakeSlider("边框粗细", wThick ?? 1, 0, 6, 0.5, v => wThick = v));
        panel.Children.Add(MakeSlider("圆角半径", wRadius ?? 8, 0, 24, 1, v => wRadius = v));
        panel.Children.Add(MakeSlider("文本缩放", wScale ?? 1, 0.7, 1.5, 0.05, v => wScale = v));

        // 按钮
        var reset = MakeButton("恢复全局", false);
        var ok = MakeButton("确定", true);
        var cancel = MakeButton("取消", false);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { reset, cancel, ok },
        };
        panel.Children.Add(buttons);

        void Finish(WidgetAppearanceOverride? result)
        {
            if (tcs.Task.IsCompleted) return;
            tcs.TrySetResult(result);
            SafeClose(win);
        }

        ok.Click += (_, _) =>
        {
            var ov = new WidgetAppearanceOverride
            {
                Backdrop = wBackdrop,
                BackgroundColor = wBg,
                ForegroundColor = wFg,
                BorderColor = wBorder,
                BorderThickness = wThick,
                CornerRadius = wRadius,
                TextScale = wScale,
            };
            // 全部为空 → 视为清除覆盖
            Finish(AllNull(ov) ? null : ov);
        };
        cancel.Click += (_, _) => Finish(null);
        reset.Click += (_, _) => Finish(null); // 「恢复全局」= 清除覆盖
        // Esc 取消（Window 无 KeyDown，挂到根面板，按键从按钮冒泡上来）
        panel.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; Finish(null); }
        };

        // ── 挂载为屏幕中央顶层窗口（复用 CenteredDialog 的居中 + 置顶套路，避免被小组件边界裁掉）──
        // 内容包进 ScrollViewer：选项较多时窗口固定高度也能滚动，「确定/取消/恢复全局」始终可点，
        // 避免按钮被挤到窗口外导致「进入后无法退出、无法保存外观」。（Issue 3）
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel,
        };
        var card = new Border
        {
            Background = Brush("CardBackgroundFillColorDefaultBrush", Colors.White),
            BorderBrush = Brush("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Child = scroll,
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
        const double W = 380, H = 620;
        var w = (int)(W * scale);
        var h = (int)(H * scale);
        var work = owner is null ? WindowInterop.GetWorkArea(win) : WindowInterop.GetWorkArea(owner);
        var x = work.X + Math.Max(0, (work.Width - w) / 2);
        var y = work.Y + Math.Max(0, (work.Height - h) / 2);
        win.AppWindow.MoveAndResize(new RectInt32(x, y, w, h));

        win.Closed += (_, _) => Finish(null);
        win.Activate();
        WindowInterop.SetTopmost(win, true);
        try { WindowInterop.SetForegroundWindow(WindowInterop.GetHwnd(win)); } catch { }
        WindowInterop.SetTopmost(win, false);

        return tcs.Task;
    }

    private sealed class ComboItem
    {
        public string Text { get; set; } = "";
        public WidgetBackdropKind? Value { get; set; }
        public override string ToString() => Text;
    }

    private static bool AllNull(WidgetAppearanceOverride ov) =>
        ov.Backdrop is null && ov.BackgroundColor is null && ov.ForegroundColor is null &&
        ov.BorderColor is null && ov.BorderThickness is null && ov.CornerRadius is null && ov.TextScale is null;

    private static int IndexOfBackdrop(WidgetBackdropKind? v) => v switch
    {
        WidgetBackdropKind.Acrylic => 1,
        WidgetBackdropKind.Mica => 2,
        WidgetBackdropKind.None => 3,
        _ => 0,
    };

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = Brush("TextFillColorPrimaryBrush", Colors.Black),
    };

    private static StackPanel BuildColorField(string label, string? current, Action<string?> onChange)
    {
        var wrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        wrap.Children.Add(ColorSwatch("跟随全局", null, current is null, onChange));
        foreach (var (name, hex) in Palette)
            wrap.Children.Add(ColorSwatch(name, hex, current == hex, onChange));
        return new StackPanel { Spacing = 4, Children = { MakeLabel(label), wrap } };
    }

    private static Button ColorSwatch(string name, string? hex, bool selected, Action<string?> onChange)
    {
        var btn = new Button
        {
            Width = 60,
            Height = 30,
            CornerRadius = new CornerRadius(6),
            Content = name,
            FontSize = 11,
        };
        ToolTipService.SetToolTip(btn, name);
        if (hex is not null && WidgetAppearance.ParseColorBrush(hex) is { } b)
            btn.Background = b;
        if (selected)
        {
            btn.BorderBrush = Brush("AccentFillColorDefaultBrush", Colors.RoyalBlue);
            btn.BorderThickness = new Thickness(2);
        }
        btn.Click += (_, _) => onChange(hex);
        return btn;
    }

    private static StackPanel MakeSlider(string label, double value, double min, double max, double step, Action<double> onChange)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            StepFrequency = step,
            Value = value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var tb = new TextBlock
        {
            Text = $"{value:0.##}",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("TextFillColorSecondaryBrush", Colors.Gray),
        };
        slider.ValueChanged += (_, e) =>
        {
            onChange(e.NewValue);
            tb.Text = $"{e.NewValue:0.##}";
        };
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(46) },
            },
            Children = { slider, tb },
        };
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(tb, 1);
        return new StackPanel { Spacing = 2, Children = { MakeLabel(label), row } };
    }

    private static Button MakeButton(string text, bool accent)
    {
        var btn = new Button
        {
            Content = text,
            MinWidth = 84,
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
        catch { /* 已关闭 */ }
    }

    private static Brush Brush(string key, Color fallback) =>
        ThemeBrush.For(ElementTheme.Default, key) ?? new SolidColorBrush(fallback);
}
