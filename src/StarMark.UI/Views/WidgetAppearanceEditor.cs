#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
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
    /// <summary>
    /// 预设调色板：每颗＝<b>那颗颜色名在表里的键</b> ＋ <b>#RRGGBB 身份</b>；真正取色在主区渐变图上点击完成。
    /// <para>批次 WI-52（P-183）：名字交<b>键</b>。白／黑／红／黄／绿／蓝那六颗的主人已经是 ✅P-181 那套词厂
    /// （<c>anno.color.*</c>），这里⛔ 再抄一份中文——抄出来就是"同一句字两个出处"（WI-49 并句之前的真症状）；
    /// 编辑器比词厂多出来的五颗同族补进表。</para>
    /// <para>⚠ <b>身份是那颗 hex，⛔ 不是名字</b>：换语言后这一排按钮的念法跟着换，选中的仍是同一颗颜色。
    /// 拿名字当身份＝✅WI-46c 那颗组名的同一缺陷（换语言后"选中的颜色"认不出来）。</para>
    /// </summary>
    private static readonly (string NameKey, string Hex)[] Palette =
    {
        ("anno.color.white", "#FFFFFFFF"), ("anno.color.black", "#FF000000"),
        ("anno.color.darkGray", "#FF2B2B2B"), ("anno.color.lightGray", "#FFF3F3F3"),
        ("anno.color.blue", "#FF3B82F6"), ("anno.color.cyan", "#FF06B6D4"),
        ("anno.color.green", "#FF16A34A"), ("anno.color.yellow", "#FFEAB308"),
        ("anno.color.red", "#FFDC2626"), ("anno.color.purple", "#FF8B5CF6"),
        ("anno.color.pink", "#FFF472B6"),
    };

    /// <summary>编辑结果：Saved=true 确定保存（Override 为最终覆盖，全 null=清除）；Saved=false 取消（已还原预览）。</summary>
    public readonly struct WidgetAppearanceEditorResult
    {
        public bool Saved { get; }
        public WidgetAppearanceOverride? Override { get; }
        public WidgetAppearanceEditorResult(bool saved, WidgetAppearanceOverride? ov) { Saved = saved; Override = ov; }
    }

    /// <param name="livePreview">外观字段（材质 / 颜色 / 边框 / 圆角）变更时的实时预览回调。</param>
    /// <param name="liveTextPreview">
    /// 仅「文本缩放」变更时的预览回调。单独一路是为了不重挂材质：
    /// 拖字号滑杆若走 <paramref name="livePreview"/>，组件会顺带重铺背景，
    /// 表现为「改字号却把背景色改了」。未传则回退到 <paramref name="livePreview"/>。
    /// </param>
    public static Task<WidgetAppearanceEditorResult> ShowAsync(
        Window owner, WidgetInstanceConfig config, Action<WidgetAppearanceOverride?> livePreview,
        Action<WidgetAppearanceOverride?>? liveTextPreview = null)
    {
        var tcs = new TaskCompletionSource<WidgetAppearanceEditorResult>();
        var current = config.Appearance;
        var original = current;   // 取消时按此还原实时预览

        var win = new Window();
        // 窗口标题只许交 LocText 的第二张名单（坑表 #338：Window 不是 DependencyObject，进不了那颗 attached property）
        LocText.SetWindowTitle(win, "widget.appearance.windowTitle");

        // 换语言那一刻要重算的"当场取词"读数（见 RebuildWords）：编辑器可以一直开着，而语言的节拍与它的节拍不同
        var wordRefreshers = new List<Action>();

        // 注意：ThemeManager.Apply 必须等 win.Content 设好后再调用（Apply 仅当 Content 是
        // FrameworkElement 时才设置其 RequestedTheme，Content 为 null 时是空操作）。
        // 过早调用（在 Content 之前）正是「外观编辑器始终为深色」的根因之一，故移到内容挂载之后。

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
        // 只改了文本缩放：走单独通道，避免把材质/背景也重挂一遍
        void PreviewTextOnly()
        {
            var ov = BuildWorking();
            if (liveTextPreview is not null) liveTextPreview(ov);
            else livePreview(ov);
        }
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
                currentColorText.Text = Locale.T("widget.appearance.followGlobal");
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

            var follow = MakeColorButton(null, Locale.T("widget.appearance.followGlobal"), () => SetColorValue(selObj, null));
            presetWrap.Children.Add(follow);
            leftButtons.Add((null, follow));

            // 这一排随选中对象<b>重建</b>，所以当场取词是合法那一路（WI-45 口径）；换语言时由 RebuildWords 整排重铺
            foreach (var (nameKey, hex) in Palette)
            {
                var h = hex;
                var btn = MakeColorButton(hex, Locale.T(nameKey), () =>
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
        root.Children.Add(MakeLabel("widget.appearance.section.material"));
        var backdropCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        // ⚠ 六档材质的名字⛔ 在这档另立一份：主人＝设置页那颗清单（SettingsPage.Options.cs 的 MaterialOptions，
        //   主窗／组件／这里三颗下拉共用同一批话，坑表 #290）。编辑器只多一颗"跟随全局"（覆盖值 null 那一档）。
        var backdropItems = new List<LocOption> { LocOption.T("widget.appearance.followGlobal") };
        backdropItems.AddRange(WidgetMaterialWords.SixTiers);
        backdropCombo.ItemsSource = backdropItems;
        // 「数据项＋模板」是这一族唯一合法的写法（WI-2c）：闭着的那格显示的是选中那一刻的快照，
        // ⛔ 把文字塞进 ComboItem.Text 就等于让那一格永远念旧语言（坑表 #272／#275）。
        // 这里从应用级资源取的是一颗模板（非画笔）：画笔一律走 ThemeBrush，那颗闸门管的是画笔。
        backdropCombo.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources["LocOptionTemplate"];
        backdropCombo.SelectedIndex = WidgetMaterialWords.IndexOf(wBackdrop) + 1;   // +1：这一排第一项是「跟随全局」
        backdropCombo.SelectionChanged += (_, _) =>
        {
            wBackdrop = (backdropCombo.SelectedItem as LocOption)?.Value as WidgetBackdropKind?;
            Preview();
        };
        root.Children.Add(backdropCombo);

        // 对象选择栏 + 调色板（左右两列）
        var cols = new Grid { ColumnSpacing = 16 };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左列：对象选择 + 颜色按钮
        var leftCol = new StackPanel { Spacing = 10 };
        leftCol.Children.Add(MakeLabel("widget.appearance.section.target"));
        var objList = new StackPanel { Spacing = 6 };
        // 这三颗建一次就一直挂在屏幕上（不像左侧那排颜色按钮随选中对象重建）⇒ 常驻那一族，交键走 LocText 重贴。
        // ⚠ 键写成<b>调用行上的字面量</b>，⛔ 藏进 (身份, 键) 那份元组建表：两向对账的那把尺按行读，
        //   藏在数组里的那三颗既拦不住拼错键名，也证明不了"表里这行有人问"（坑表 #375 同族·WI-52 补的那一臂）。
        objList.Children.Add(MakeObjectButton("bg", "widget.appearance.target.bg"));
        objList.Children.Add(MakeObjectButton("fg", "widget.appearance.target.fg"));
        objList.Children.Add(MakeObjectButton("border", "widget.appearance.target.border"));
        leftCol.Children.Add(objList);

        Button MakeObjectButton(string obj, string titleKey)
        {
            var ob = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
            };
            LocText.SetKey(ob, titleKey);
            ob.Click += (_, _) => SelectObject(obj);
            objButtons[obj] = ob;
            return ob;
        }

        leftCol.Children.Add(MakeLabel("widget.appearance.section.swatches"));
        presetWrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        leftCol.Children.Add(presetWrap);

        // 右列：HSV 渐变取色器
        var rightCol = new StackPanel { Spacing = 8 };
        rightCol.Children.Add(MakeLabel("widget.appearance.section.gradient"));

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

        rightCol.Children.Add(MakeLabel("widget.appearance.section.current"));
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
        // suppressSliderReset：以编程方式回退滑杆（如「恢复全局」）时临时屏蔽其 ValueChanged，
        // 避免把 wScale/wThick/wRadius 误写成「跟随全局」对应的默认值（应保持 null = 跟随全局）。
        bool suppressSliderReset = false;
        var cThick = MakeSlider("widget.appearance.slider.borderThickness", wThick ?? 1, 0, 12, 0.5, v => { if (suppressSliderReset) return; wThick = v; Preview(); }, out var sThick, wordRefreshers);
        var cRadius = MakeSlider("widget.appearance.slider.cornerRadius", wRadius ?? 8, 0, 48, 1, v => { if (suppressSliderReset) return; wRadius = v; Preview(); }, out var sRadius, wordRefreshers);
        // 文本缩放：在 1.0（=默认/跟随全局）附近吸附，并把该档显示成「默认」。
        // 不吸附的话拇指停在 0.95 / 1.05 这种临界值是常事，用户会以为「明明调回默认了，字还是不对」。
        var cScale = MakeSlider("widget.appearance.slider.textScale", wScale ?? 1, 0.6, 1.8, 0.05, v =>
        {
            if (suppressSliderReset) return;
            wScale = SnapTextScale(v);
            PreviewTextOnly();   // 实时预览文本缩放（只改 TextBlock.FontSize，不动材质与背景）
        }, out var sScale, wordRefreshers, v =>
        {
            var s = SnapTextScale(v);
            // 那颗"默认"是**读数**，随滑杆当场算 ⇒ 交 Locale.T（⛔ 挂 LocText：同一颗元素还兼着显示数字）
            return Math.Abs(s - 1.0) < 1e-6 ? Locale.T("widget.appearance.scaleDefault") : NumberText.UpTo2(s);
        });
        root.Children.Add(cThick);
        root.Children.Add(cRadius);
        root.Children.Add(cScale);

        // 按钮
        var reset = MakeButton("widget.appearance.resetGlobal", false);
        var ok = MakeButton("dialog.ok", true);
        var cancel = MakeButton("dialog.cancel", false);
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
                    // 交身份⛔ 交成品字（批次 WI-61d 改口）：✅WI-52 那句"⛔ 把键塞进那颗通用工厂——它⛔ 懂语言"
                    // 说的是**当时**的签名；工厂现在收 LocMessage，取词挪到贴上那一刻。
                    // 名单里也不会攒壳：LocText 那份名单是弱引用、每次重贴前先 sweep（读过的实现，⛔ 猜的）。
                    LocMessage.Of("widget.appearance.cancelConfirmTitle"), LocMessage.Of("widget.appearance.cancelConfirmBody"),
                    LocMessage.Of("widget.appearance.cancelConfirmPrimary"), LocMessage.Of("widget.appearance.cancelConfirmSecondary"), owner: win);
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
            backdropCombo.SelectedIndex = WidgetMaterialWords.IndexOf(wBackdrop) + 1;   // +1：这一排第一项是「跟随全局」
            Preview();
            // 同步把滑杆视觉回退到「跟随全局」对应的默认值（不触发 ValueChanged，保持上面的 null = 跟随全局）
            suppressSliderReset = true;
            sThick.Value = 1;
            sRadius.Value = 8;
            sScale.Value = 1;
            suppressSliderReset = false;
            BuildPresetStrip();
            RefreshColorHighlights();
            RefreshCurrentColor();
        };
        // 换语言那一刻：这一族字是**当场算**的（左侧那排颜色按钮、"当前颜色"那一行、三根滑杆行末的读数），
        // 而编辑器自己的节拍里没有"语言变了"这件事 ⇒ 自己订阅 Locale.Changed（✅WI-51 同一口径，坑表 #386）。
        void RebuildWords()
        {
            BuildPresetStrip();
            RefreshColorHighlights();
            RefreshCurrentColor();
            foreach (var paste in wordRefreshers) paste();
        }
        Locale.Changed += RebuildWords;

        // Esc 取消（带确认）；窗口关闭等同取消
        root.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; await RequestCancel(); }
        };
        win.Closed += (_, _) =>
        {
            Locale.Changed -= RebuildWords;   // 与上面成对：编辑器一次性，但订阅挂的是静态事件
            if (!tcs.Task.IsCompleted) FinishCancel();
        };

        // ── 挂载为屏幕中央顶层窗口（复用 CenteredDialog 的居中 + 置顶套路）──
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root,
        };
        // 卡片直接承载 ScrollViewer：此前外层套了 StackPanel（含拖拽标题栏），
        // StackPanel 以「无限高度」度量子元素，ScrollViewer 拿不到有界高度 → 永不滚动，
        // 底部「确定 / 取消 / 恢复全局」被裁切不可达。恢复为其直接宿主即可正常滚动。
        // 卡片直接作为窗口根内容：PopupCard 用 XAML ThemeResource 渲染背景/边框，
        // 跟随窗口根的 RequestedTheme（Apply 在 Content 之后设置），与 WidgetWindow 同款机制，
        // 彻底消除「代码侧 ThemeBrush 解析不到浅色画刷、弹窗始终深色」的隐患；
        // 整窗按同一半径（10）裁成圆角矩形，避免双层叠加的灰色观感。
        var card = new PopupCard { CardContent = scroll };
        win.Content = card;
        // 必须在此（Content 已设）才套用主题：否则 root.RequestedTheme 不会生效，弹窗停留在旧主题。
        try { ThemeManager.Apply(win, new SettingsStore().LoadTheme()); } catch { }
        // 登记进弹窗主题同步表：主界面运行期切深浅色时不会传导到独立顶层窗口，须由 CenteredDialog 统一重刷。
        CenteredDialog.TrackForThemeSync(win);
        // 不再设单独的拖拽标题栏：整个窗口可拖拽移动（自动避开输入控件），避免遮挡被编辑的组件
        MakeWindowDraggable(win, card);
        WindowInterop.RemoveDefaultWindowFrame(win);

        var scale = WindowInterop.GetScale(win);
        const double W = 760, H = 660;
        var w = (int)(W * scale);
        var h = (int)(H * scale);
        var work = owner is null ? WindowInterop.GetWorkArea(win) : WindowInterop.GetWorkArea(owner);
        var x = work.X + Math.Max(0, (work.Width - w) / 2);
        var y = work.Y + Math.Max(0, (work.Height - h) / 2);
        win.AppWindow.MoveAndResize(new RectInt32(x, y, w, h));

        // 必须在 MoveAndResize 之后按真实物理尺寸裁圆角：SetWindowRgn 真正物理裁切无黑角，
        // 取代 DWM 仅靠视觉圆角（无边框窗口会留下四角黑块）。
        WindowInterop.SetRoundedWindowRegion(win, 10);

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

    private static bool AllNull(WidgetAppearanceOverride ov) =>
        ov.Backdrop is null && ov.BackgroundColor is null && ov.ForegroundColor is null &&
        ov.BorderColor is null && ov.BorderThickness is null && ov.CornerRadius is null && ov.TextScale is null;

    /// <summary>一行标题：交的是<b>那颗键</b>（批次 WI-52·P-183）。这些标题建一次就常驻在屏幕上，
    /// 而编辑器可以开着不动、语言却被人换掉 ⇒ 属"常驻"那一族，走 <see cref="LocText"/> 重贴；
    /// ⛔ 当场取词（Locale.T）写死在 Text 上——那样换语言时这一排永远念旧语言。</summary>
    private static TextBlock MakeLabel(string key)
    {
        var tb = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("TextFillColorPrimaryBrush", Colors.Black),
        };
        LocText.SetKey(tb, key);
        return tb;
    }

    /// <summary>
    /// 文本缩放的「默认档」吸附：1.0 附近 ±0.03 内一律算 1.0。
    /// 1.0 就是「跟随全局 / 未缩放」，必须能精确落到它身上，否则基准还原永远差一口气。
    /// </summary>
    private static double SnapTextScale(double v) => Math.Abs(v - 1.0) < 0.03 ? 1.0 : v;

    private static StackPanel MakeSlider(string labelKey, double value, double min, double max, double step, Action<double> onChange, out Slider outSlider, List<Action> wordRefreshers)
        => MakeSlider(labelKey, value, min, max, step, onChange, out outSlider, wordRefreshers, null);

    /// <param name="labelKey">那一行的标题＝<b>那颗键</b>（批次 WI-52）：编辑器开着不动而语言被换掉时由 LocText 重贴。</param>
    /// <param name="wordRefreshers">行末那颗<b>读数</b>的重算动作登记处：它显示的是数字或"默认"那颗词，
    /// ⛔ 不能挂 LocText（同一颗元素还被滑杆改写），于是换语言那一刻由 <c>RebuildWords</c> 现算——
    /// 与✅WI-51 那条口径一致：<b>自己的节拍里没有语言</b>的宿主，得自己订阅 <c>Locale.Changed</c>（坑表 #386）。</param>
    private static StackPanel MakeSlider(string labelKey, double value, double min, double max, double step, Action<double> onChange, out Slider outSlider, List<Action> wordRefreshers, Func<double, string>? format)
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
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("TextFillColorSecondaryBrush", Colors.Gray),
        };
        void PasteReading() => tb.Text = format?.Invoke(slider.Value) ?? NumberText.UpTo2(slider.Value);
        PasteReading();
        wordRefreshers.Add(PasteReading);
        slider.ValueChanged += (_, e) =>
        {
            onChange(e.NewValue);
            PasteReading();
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
        outSlider = slider;
        return new StackPanel { Spacing = 2, Children = { MakeLabel(labelKey), row } };
    }

    /// <summary>底部那三颗按钮：文字交<b>键</b>走 LocText 重贴（与 MakeLabel 同一族——建一次、常驻）。</summary>
    private static Button MakeButton(string key, bool accent)
    {
        var btn = new Button
        {
            MinWidth = 84,
            Padding = new Thickness(12, 5, 12, 5),
            CornerRadius = new CornerRadius(6),
        };
        LocText.SetKey(btn, key);
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

    /// <summary>编辑器顶部的可拖拽标题栏：从标题栏按下拖动即可移动整个编辑器窗口，
    /// 避免它遮挡被编辑的组件（组件右键「外观…」弹出的浮层可能正好盖住组件本身）。</summary>
    /// <summary>
    /// 让整个窗口可拖拽移动（替代单独的拖拽标题栏）。按下点若落在真正的输入控件上则放行，
    /// 让控件正常工作；落在留白 / 纯展示区域（标题、面板、卡片留白、预览缩略图）则可按住拖动窗口。
    /// </summary>
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

    /// <summary>
    /// 判断按下点是否落在输入控件（或其内部）上——是则不劫持为拖拽，让控件正常响应。
    /// 注意刻意不把 ScrollViewer 算进来：这样滚动区内部的留白也可拖动窗口，
    /// 而滚动条（ScrollBar/Thumb）本身已单独排除，拖拽条仍可用。
    /// </summary>
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

    /// <summary>把用户「设置里存的」主题偏好折算成窗口实际渲染的 <see cref="ElementTheme"/>，
    /// 让代码侧解析的画笔与窗口根元素的 RequestedTheme 完全一致（消除弹窗主题错乱/始终深色）。</summary>
    private static ElementTheme EffectiveTheme()
    {
        // 跟随主窗口当前实际主题（运行期切换后立即生效），而非磁盘存储偏好——
        // 否则「主界面已切浅色、弹窗仍按旧存储偏好渲染成深色」会再次出现。
        var pref = App.MainWindow?.CurrentThemePreference ?? new SettingsStore().LoadTheme();
        return pref switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private static Brush Brush(string key, Color fallback) =>
        ThemeBrush.For(EffectiveTheme(), key) ?? new SolidColorBrush(fallback);
}
