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
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
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
    // 预设调色板（#RRGGBB），作为快捷按钮；真正取色在主区渐变图上点击完成
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

        // 渐变取色器的当前 HSV 状态（随选中对象切换而同步）
        double pickH = 210, pickS = 1, pickV = 1;

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
        string PickColorHex() => ToHex(HsvToRgb(pickH, pickS, pickV));

        // 仅写值 + 预览（不回写 pick，避免渐变取色时的来回漂移）
        void SetColorValue(string obj, string? hex)
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
        WrapPanel? presetWrap = null;
        Border? currentColorSwatch = null;
        TextBlock? currentColorText = null;

        // 渐变图控件
        Canvas? svCanvas = null;
        Rectangle? svHueRect = null;
        Ellipse? svIndicator = null;
        Canvas? hueCanvas = null;
        Rectangle? hueIndicator = null;
        bool _svPressed = false, _huePressed = false;

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

        // 把「当前对象颜色」同步到渐变取色器（切换对象时调用）
        void UpdatePickerFromObject()
        {
            var hex = GetColor(selObj);
            if (hex is not null && RgbFromHex(hex) is { } rgb)
            {
                var (h, s, v) = RgbToHsv(rgb);
                pickH = h; pickS = s; pickV = v;
            }
            UpdateHueFill();
            UpdateSvIndicator();
            UpdateHueIndicator();
        }

        void UpdateHueFill()
        {
            if (svHueRect is not null) svHueRect.Fill = new SolidColorBrush(HsvToRgb(pickH, 1, 1));
        }
        void UpdateSvIndicator()
        {
            if (svCanvas is not null && svIndicator is not null)
            {
                var x = pickS * svCanvas.Width - svIndicator.Width / 2;
                var y = (1 - pickV) * svCanvas.Height - svIndicator.Height / 2;
                Canvas.SetLeft(svIndicator, x);
                Canvas.SetTop(svIndicator, y);
            }
        }
        void UpdateHueIndicator()
        {
            if (hueCanvas is not null && hueIndicator is not null)
            {
                var x = (pickH / 360.0) * hueCanvas.Width - hueIndicator.Width / 2;
                Canvas.SetLeft(hueIndicator, x);
            }
        }

        void SvPointer(object? s, PointerRoutedEventArgs e)
        {
            if (svCanvas is null) return;
            svCanvas.CapturePointer(e.Pointer);
            _svPressed = true;
            SvUpdate(e);
            e.Handled = true;
        }
        void SvPointerMove(object? s, PointerRoutedEventArgs e)
        {
            if (_svPressed) { SvUpdate(e); e.Handled = true; }
        }
        void SvUpdate(PointerRoutedEventArgs e)
        {
            if (svCanvas is null) return;
            var p = e.GetCurrentPoint(svCanvas).Position;
            pickS = Math.Clamp(p.X / svCanvas.Width, 0, 1);
            pickV = Math.Clamp(1 - p.Y / svCanvas.Height, 0, 1);
            UpdateSvIndicator();
            SetColorValue(selObj, PickColorHex());
        }

        void HuePointer(object? s, PointerRoutedEventArgs e)
        {
            if (hueCanvas is null) return;
            hueCanvas.CapturePointer(e.Pointer);
            _huePressed = true;
            HueUpdate(e);
            e.Handled = true;
        }
        void HuePointerMove(object? s, PointerRoutedEventArgs e)
        {
            if (_huePressed) { HueUpdate(e); e.Handled = true; }
        }
        void HueUpdate(PointerRoutedEventArgs e)
        {
            if (hueCanvas is null) return;
            var p = e.GetCurrentPoint(hueCanvas).Position;
            pickH = Math.Clamp(p.X / hueCanvas.Width * 360, 0, 360);
            UpdateHueFill();
            UpdateHueIndicator();
            SetColorValue(selObj, PickColorHex());
        }

        void SelectObject(string obj)
        {
            selObj = obj;
            BuildPresetStrip();      // 左侧颜色按钮随对象重建
            RefreshColorHighlights();
            RefreshCurrentColor();
            UpdatePickerFromObject();
        }

        // 左侧颜色按钮（随选中对象重建）：跟随全局 + 预设
        void BuildPresetStrip()
        {
            if (presetWrap is null) return;
            presetWrap.Children.Clear();
            leftButtons.Clear();

            var follow = MakeColorButton(null, "跟随全局", () => SetColorValue(selObj, null));
            presetWrap.Children.Add(follow);
            leftButtons.Add((null, follow));

            foreach (var (name, hex) in Palette)
            {
                var h = hex;
                var btn = MakeColorButton(hex, name, () =>
                {
                    SetColorValue(selObj, h);
                    if (RgbFromHex(h) is { } rgb)
                    {
                        var (hh, ss, vv) = RgbToHsv(rgb);
                        pickH = hh; pickS = ss; pickV = vv;
                    }
                    UpdateHueFill(); UpdateSvIndicator(); UpdateHueIndicator();
                });
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
        leftCol.Children.Add(MakeLabel("颜色（按钮快捷选取）"));
        presetWrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        leftCol.Children.Add(presetWrap);

        // 右列：HSV 渐变取色器
        var rightCol = new StackPanel { Spacing = 8 };
        rightCol.Children.Add(MakeLabel("调色板（在渐变色图上点击选取颜色）"));

        // SV 方图：底色=当前色相纯色；横向白→透明（饱和度），纵向透明→黑（明度）
        svCanvas = new Canvas
        {
            Width = 240,
            Height = 150,
            Margin = new Thickness(0, 0, 0, 4),
        };
        svHueRect = new Rectangle { Width = 240, Height = 150, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var svWhite = new Rectangle
        {
            Width = 240,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = { new GradientStop { Color = Colors.White, Offset = 0 }, new GradientStop { Color = Colors.Transparent, Offset = 1 } },
            },
        };
        var svBlack = new Rectangle
        {
            Width = 240,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = { new GradientStop { Color = Colors.Transparent, Offset = 0 }, new GradientStop { Color = Colors.Black, Offset = 1 } },
            },
        };
        svIndicator = new Ellipse
        {
            Width = 12,
            Height = 12,
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Colors.Transparent),
            IsHitTestVisible = false,
        };
        svCanvas.Children.Add(svHueRect);
        svCanvas.Children.Add(svWhite);
        svCanvas.Children.Add(svBlack);
        svCanvas.Children.Add(svIndicator);
        svCanvas.PointerPressed += SvPointer;
        svCanvas.PointerMoved += SvPointerMove;
        svCanvas.PointerReleased += (_, e) => { _svPressed = false; try { svCanvas.ReleasePointerCapture(e.Pointer); } catch { } };
        svCanvas.PointerCanceled += (_, e) => { _svPressed = false; try { svCanvas.ReleasePointerCapture(e.Pointer); } catch { } };
        rightCol.Children.Add(svCanvas);

        // 色相条（彩虹渐变）
        hueCanvas = new Canvas
        {
            Width = 240,
            Height = 18,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var hueGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        hueGrad.GradientStops.Add(new GradientStop { Color = Colors.Red, Offset = 0 });
        hueGrad.GradientStops.Add(new GradientStop { Color = Colors.Yellow, Offset = 0.1667 });
        hueGrad.GradientStops.Add(new GradientStop { Color = Colors.Lime, Offset = 0.3333 });
        hueGrad.GradientStops.Add(new GradientStop { Color = Colors.Cyan, Offset = 0.5 });
        hueGrad.GradientStops.Add(new GradientStop { Color = Colors.Blue, Offset = 0.6667 });
        hueGrad.GradientStops.Add(new GradientStop { Color = Colors.Magenta, Offset = 0.8333 });
        hueGrad.GradientStops.Add(new GradientStop { Color = Colors.Red, Offset = 1 });
        hueCanvas.Children.Add(new Rectangle
        {
            Width = 240,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = hueGrad,
        });
        hueIndicator = new Rectangle
        {
            Width = 4,
            Height = 22,
            Fill = new SolidColorBrush(Colors.White),
            Stroke = new SolidColorBrush(Colors.Black),
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Margin = new Thickness(0, -2, 0, 0),
        };
        hueCanvas.Children.Add(hueIndicator);
        hueCanvas.PointerPressed += HuePointer;
        hueCanvas.PointerMoved += HuePointerMove;
        hueCanvas.PointerReleased += (_, e) => { _huePressed = false; try { hueCanvas.ReleasePointerCapture(e.Pointer); } catch { } };
        hueCanvas.PointerCanceled += (_, e) => { _huePressed = false; try { hueCanvas.ReleasePointerCapture(e.Pointer); } catch { } };
        rightCol.Children.Add(hueCanvas);

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
        root.Children.Add(MakeSlider("文本缩放", wScale ?? 1, 0.6, 1.8, 0.05, v =>
        {
            wScale = v;
            Preview();   // 实时预览文本缩放（直接改 TextBlock.FontSize，文本本身缩放，留在布局内）
        }));

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
        const double W = 760, H = 660;
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

        // 初始高亮 + 取色器同步
        BuildPresetStrip();
        SelectObject(selObj);

        return tcs.Task;
    }

    // ── HSV ↔ RGB 互转（取色器用）──

    private static Windows.UI.Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        var m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return Windows.UI.Color.FromArgb(255, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static (double h, double s, double v) RgbToHsv(Windows.UI.Color col)
    {
        var rn = col.R / 255.0; var gn = col.G / 255.0; var bn = col.B / 255.0;
        var max = Math.Max(rn, Math.Max(gn, bn));
        var min = Math.Min(rn, Math.Min(gn, bn));
        var d = max - min;
        double h = 0;
        if (d != 0)
        {
            if (max == rn) h = 60 * (((gn - bn) / d) % 6);
            else if (max == gn) h = 60 * ((bn - rn) / d + 2);
            else h = 60 * ((rn - gn) / d + 4);
        }
        if (h < 0) h += 360;
        var s = max == 0 ? 0 : d / max;
        return (h, s, max);
    }

    private static Windows.UI.Color? RgbFromHex(string hex)
        => WidgetAppearance.ParseColorBrush(hex)?.Color;

    private static string ToHex(Windows.UI.Color c)
        => $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";

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
