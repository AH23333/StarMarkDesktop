#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
/// <para>
/// 返回 <see cref="WidgetAppearanceEditorResult"/>：<c>Saved=true</c> 表示用户点「确定」、调用方应持久化
/// <c>Override</c>；<c>Saved=false</c> 表示取消（编辑器内部已把实时预览还原为打开前的外观）。
/// 编辑过程中通过 <paramref name="livePreview"/> 回调把临时覆盖实时套到组件窗口，实现「调即所见」。
/// </para>
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

    /// <summary>编辑结果：Saved=true 确定保存（Override 为最终覆盖，全 null=清除）；Saved=false 取消（已还原预览）。</summary>
    public readonly struct WidgetAppearanceEditorResult
    {
        public bool Saved { get; }
        public WidgetAppearanceOverride? Override { get; }
        public WidgetAppearanceEditorResult(bool saved, WidgetAppearanceOverride? ov) { Saved = saved; Override = ov; }
    }

    public static Task<WidgetAppearanceEditorResult> ShowAsync(
        Window owner, WidgetInstanceConfig config, Action<WidgetAppearanceOverride?> livePreview)
    {
        var tcs = new TaskCompletionSource<WidgetAppearanceEditorResult>();
        var current = config.Appearance;
        var original = current;   // 取消时按此还原实时预览

        var win = new Window { Title = "组件外观" };
        try { ThemeManager.Apply(win, new SettingsStore().LoadTheme()); } catch { }

        // ── 工作副本（编辑中实时改写，确定时一次性落盘；null 字段=跟随全局）──
        WidgetBackdropKind? wBackdrop = current?.Backdrop;
        string? wBg = current?.BackgroundColor;
        string? wFg = current?.ForegroundColor;
        string? wBorder = current?.BorderColor;
        double? wThick = current?.BorderThickness;
        double? wRadius = current?.CornerRadius;
        double? wScale = current?.TextScale;

        // 选中的「对象」（背景色 / 文本色 / 边框色）
        string selObj = "bg";

        // 把当前工作副本套到组件（实时预览）；每个字段为 null 即「跟随全局」
        void Preview() => livePreview(BuildWorking());
        WidgetAppearanceOverride BuildWorking() => new()
        {
            Backdrop = wBackdrop,
            BackgroundColor = wBg,
            ForegroundColor = wFg,
            BorderColor = wBorder,
            BorderThickness = wThick,
            CornerRadius = wRadius,
            TextScale = wScale,
        };
        bool IsDirty()
        {
            var o = original;
            return (wBackdrop != o?.Backdrop)
                || (wBg != o?.BackgroundColor)
                || (wFg != o?.ForegroundColor)
                || (wBorder != o?.BorderColor)
                || (wThick != o?.BorderThickness)
                || (wRadius != o?.CornerRadius)
                || (wScale != o?.TextScale);
        }
        string? GetColor(string obj) => obj switch
        {
            "fg" => wFg,
            "border" => wBorder,
            _ => wBg,
        };
        void SetColor(string obj, string? hex)
        {
            if (obj == "fg") wFg = hex;
            else if (obj == "border") wBorder = hex;
            else wBg = hex;
            Preview();
            RefreshColorHighlights();
            RefreshCurrentColor();
        }

        // 颜色按钮引用（用于高亮）
        var objButtons = new Dictionary<string, Button>();
        var leftButtons = new List<(string? Hex, Button Btn)>();
        var rightButtons = new List<(string? Hex, Button Btn)>();
        WrapPanel? presetWrap = null;
        Border? currentColorSwatch = null;
        TextBlock? currentColorText = null;

        void Highlight(Button btn, bool on)
        {
            if (on)
            {
                btn.BorderBrush = Brush("AccentFillColorDefaultBrush", Colors.RoyalBlue);
                btn.BorderThickness = new Thickness(2);
            }
            else
            {
                btn.BorderBrush = new SolidColorBrush(Colors.Transparent);
                btn.BorderThickness = new Thickness(1);
            }
        }

        void RefreshColorHighlights()
        {
            var cur = GetColor(selObj);
            foreach (var (hex, btn) in leftButtons) Highlight(btn, hex == cur);
            foreach (var (hex, btn) in rightButtons) Highlight(btn, hex == cur);
            foreach (var (obj, btn) in objButtons) Highlight(btn, obj == selObj);
        }

        void RefreshCurrentColor()
        {
            if (currentColorSwatch is null || currentColorText is null) return;
            var cur = GetColor(selObj);
            if (cur is null)
            {
                currentColorSwatch.Background = null;
                currentColorSwatch.BorderBrush = Brush("TextFillColorSecondaryBrush", Colors.Gray);
                currentColorText.Text = "跟随全局";
            }
            else if (WidgetAppearance.ParseColorBrush(cur) is { } b)
            {
                currentColorSwatch.Background = b;
                currentColorSwatch.BorderBrush = Brush("TextFillColorSecondaryBrush", Colors.Gray);
                currentColorText.Text = cur.ToUpper();
            }
        }

        void SelectObject(string obj)
        {
            selObj = obj;
            BuildPresetStrip();      // 左侧颜色按钮随对象重建
            RefreshColorHighlights();
            RefreshCurrentColor();
        }

        // 左侧颜色按钮（随选中对象重建）：跟随全局 + 预设
        void BuildPresetStrip()
        {
            if (presetWrap is null) return;
            presetWrap.Children.Clear();
            leftButtons.Clear();

            var follow = MakeColorButton(null, "跟随全局", () => SetColor(selObj, null));
            presetWrap.Children.Add(follow);
            leftButtons.Add((null, follow));

            foreach (var (name, hex) in Palette)
            {
                var btn = MakeColorButton(hex, name, () => SetColor(selObj, hex));
                presetWrap.Children.Add(btn);
                leftButtons.Add((hex, btn));
            }
        }

        Button MakeColorButton(string? hex, string name, Action onClick)
        {
            var btn = new Button
            {
                Width = 64,
                Height = 32,
                CornerRadius = new CornerRadius(6),
                Content = name,
                FontSize = 11,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
            };
            ToolTipService.SetToolTip(btn, name);
            if (hex is not null && WidgetAppearance.ParseColorBrush(hex) is { } b)
                btn.Background = b;
            btn.Click += (_, _) => onClick();
            return btn;
        }

        // ───────────────────────── 构建 UI ─────────────────────────

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(4) };

        // 材质（顶部，保持不变）
        root.Children.Add(MakeLabel("材质"));
        var backdropCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        backdropCombo.Items.Add(new ComboItem { Text = "跟随全局", Value = null });
        backdropCombo.Items.Add(new ComboItem { Text = "亚克力", Value = WidgetBackdropKind.Acrylic });
        backdropCombo.Items.Add(new ComboItem { Text = "云母", Value = WidgetBackdropKind.Mica });
        backdropCombo.Items.Add(new ComboItem { Text = "不透明", Value = WidgetBackdropKind.None });
        backdropCombo.SelectedIndex = IndexOfBackdrop(wBackdrop);
        backdropCombo.SelectionChanged += (_, _) =>
        {
            wBackdrop = (backdropCombo.SelectedItem as ComboItem)?.Value;
            Preview();
        };
        root.Children.Add(backdropCombo);

        // 对象选择栏 + 调色板（左右两列）
        var cols = new Grid { ColumnSpacing = 16 };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左列：对象选择 + 颜色按钮
        var leftCol = new StackPanel { Spacing = 10 };
        leftCol.Children.Add(MakeLabel("对象"));
        var objList = new StackPanel { Spacing = 6 };
        foreach (var (key, title) in new[] { ("bg", "背景色"), ("fg", "文本色"), ("border", "边框色") })
        {
            var ob = new Button
            {
                Content = title,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
            };
            var k = key;
            ob.Click += (_, _) => SelectObject(k);
            objButtons[key] = ob;
            objList.Children.Add(ob);
        }
        leftCol.Children.Add(objList);
        leftCol.Children.Add(MakeLabel("颜色"));
        presetWrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        leftCol.Children.Add(presetWrap);

        // 右列：调色板
        var rightCol = new StackPanel { Spacing = 10 };
        rightCol.Children.Add(MakeLabel("调色板（点击选取颜色）"));
        var paletteGrid = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        foreach (var (name, hex) in Palette)
        {
            var btn = MakeColorButton(hex, name, () => SetColor(selObj, hex));
            paletteGrid.Children.Add(btn);
            rightButtons.Add((hex, btn));
        }
        rightCol.Children.Add(paletteGrid);
        rightCol.Children.Add(MakeLabel("当前颜色"));
        var curRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        currentColorSwatch = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
        };
        currentColorText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush", Colors.Gray) };
        curRow.Children.Add(currentColorSwatch);
        curRow.Children.Add(currentColorText);
        rightCol.Children.Add(curRow);

        Grid.SetColumn(leftCol, 0);
        Grid.SetColumn(rightCol, 1);
        cols.Children.Add(leftCol);
        cols.Children.Add(rightCol);
        root.Children.Add(cols);

        // 滑杆三件套（底部，阈值放宽）
        root.Children.Add(MakeSlider("边框粗细", wThick ?? 1, 0, 12, 0.5, v => { wThick = v; Preview(); }));
        root.Children.Add(MakeSlider("圆角半径", wRadius ?? 8, 0, 48, 1, v => { wRadius = v; Preview(); }));
        root.Children.Add(MakeSlider("文本缩放", wScale ?? 1, 0.6, 1.8, 0.05, v => { wScale = v; Preview(); }));

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
        root.Children.Add(buttons);

        void FinishOk()
        {
            if (tcs.Task.IsCompleted) return;
            var ov = BuildWorking();
            tcs.TrySetResult(new WidgetAppearanceEditorResult(true, AllNull(ov) ? null : ov));
            SafeClose(win);
        }
        void FinishCancel()
        {
            if (tcs.Task.IsCompleted) return;
            livePreview(original);   // 还原实时预览为打开前的外观
            tcs.TrySetResult(new WidgetAppearanceEditorResult(false, null));
            SafeClose(win);
        }
        async Task RequestCancel()
        {
            if (tcs.Task.IsCompleted) return;
            if (IsDirty())
            {
                var yes = await CenteredDialog.ConfirmAsync(
                    "取消外观编辑", "有未保存的外观更改，确定取消？\n（取消后组件将恢复为打开前的样子）", "确定取消", "继续编辑", owner: win);
                if (!yes) return;   // 继续编辑
            }
            FinishCancel();
        }

        ok.Click += (_, _) => FinishOk();
        cancel.Click += (_, _) => _ = RequestCancel();
        reset.Click += (_, _) =>
        {
            // 恢复全局：清空所有覆盖（颜色 + 滑杆回到「跟随全局」），实时预览回退
            wBg = wFg = wBorder = null;
            wThick = wRadius = wScale = null;
            wBackdrop = current?.Backdrop;   // 材质保持打开时的值，不强制改
            backdropCombo.SelectedIndex = IndexOfBackdrop(wBackdrop);
            Preview();
            BuildPresetStrip();
            RefreshColorHighlights();
            RefreshCurrentColor();
        };
        // Esc 取消（带确认）；窗口关闭等同取消
        root.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; await RequestCancel(); }
        };
        win.Closed += (_, _) => { if (!tcs.Task.IsCompleted) FinishCancel(); };

        // ── 挂载为屏幕中央顶层窗口（复用 CenteredDialog 的居中 + 置顶套路）──
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root,
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
        var grid = new Grid
        {
            Background = Brush("ApplicationPageBackgroundThemeBrush", Colors.White),
            Children = { card },
        };
        win.Content = grid;
        WindowInterop.RemoveDefaultWindowFrame(win);
        WindowInterop.ApplyRoundedCorners(win);

        var scale = WindowInterop.GetScale(win);
        const double W = 720, H = 640;
        var w = (int)(W * scale);
        var h = (int)(H * scale);
        var work = owner is null ? WindowInterop.GetWorkArea(win) : WindowInterop.GetWorkArea(owner);
        var x = work.X + Math.Max(0, (work.Width - w) / 2);
        var y = work.Y + Math.Max(0, (work.Height - h) / 2);
        win.AppWindow.MoveAndResize(new RectInt32(x, y, w, h));

        win.Activate();
        WindowInterop.SetTopmost(win, true);
        try { WindowInterop.SetForegroundWindow(WindowInterop.GetHwnd(win)); } catch { }
        WindowInterop.SetTopmost(win, false);

        // 初始高亮
        BuildPresetStrip();
        SelectObject(selObj);

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
