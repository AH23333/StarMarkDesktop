#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using StarMark.Core.Appearance;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// 取色器面板（片144）：「主题色 · 自定义」那一格与组件「外观」编辑器共用的同一颗。
/// <para>
/// 内容＝预设色板那一排（<see cref="Swatches"/>）＋SV 方图与色相条（<see cref="Gradient"/>）。
/// 宿主各自决定摆位（编辑器分左右两列、设置页上下相叠），这里只交内容⛔ 交版式。
/// </para>
/// <para>
/// 词的口径（WI-44／WI-45 那两族）：这一排色板<b>建一次就常驻</b>在屏幕上 ⇒ 交<b>键</b>走
/// <see cref="LocText"/> 重贴（正文与 tooltip 同键），⛔ 当场取词写死在 Content 上。
/// 面板自己<b>不订阅</b> <c>Locale.Changed</c>——换语言的重贴由 LocText 那份名单负责，这里没有自己的节拍。
/// </para>
/// <para>
/// 状态单向流：外部状态经 <see cref="SyncTo"/> 交进来（换对象／恢复默认／装载设置那一刻）；
/// 用户点／拖经 <see cref="Picked"/> 交出去（null＝「跟随全局」，只有带跟随钮的那一臂会发）。
/// 面板每次 pick 自己更新色板高亮——镜像旧编辑器「SetColorValue→RefreshColorHighlights」那条实时跟色。
/// </para>
/// </summary>
internal sealed class ColorPickerPane
{
    /// <summary>
    /// 预设调色板：每颗＝<b>那颗颜色名在表里的键</b> ＋ <b>#RRGGBB 身份</b>；真正取色在 SV 方图上点击完成。
    /// <para>批次 WI-52（P-183）起名字交<b>键</b>。白／黑／红／黄／绿／蓝那六颗的主人是 ✅P-181 那套词厂
    /// （<c>anno.color.*</c>），这里⛔ 再抄一份中文——抄出来就是"同一句字两个出处"（WI-49 并句之前的真症状）；
    /// 编辑器比词厂多出来的五颗同族补进表。</para>
    /// <para>⚠ <b>身份是那颗 hex，⛔ 是名字</b>：换语言后这一排按钮的念法跟着换，选中的仍是同一颗颜色。
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

    /// <summary>预设色板那一排（首颗可为「跟随全局」钮——仅 showFollowGlobal 那一臂）。</summary>
    public WrapPanel Swatches { get; }

    /// <summary>SV 方图＋色相条（纵向相叠）。</summary>
    public StackPanel Gradient { get; }

    /// <summary>用户在面板上点定了一颗颜色；null＝「跟随全局」（仅带跟随钮的那一臂会发）。</summary>
    public event Action<Color?>? Picked;

    private readonly List<(string? Hex, Button Btn)> _swatchButtons = new();
    private Color? _current;
    private double _pickH = 210, _pickS = 1, _pickV = 1;

    private readonly Canvas _svCanvas;
    private readonly Rectangle _svHueRect;
    private readonly Ellipse _svIndicator;
    private readonly Canvas _hueCanvas;
    private readonly Rectangle _hueIndicator;
    private bool _svPressed, _huePressed;

    /// <param name="showFollowGlobal">要不要带那颗「跟随全局」钮：组件编辑器要（覆盖值 null 那一档
    /// 得有一颗按钮可点）；设置页不要——「随系统」由它上面那颗下拉管，面板只管「自定义」那一臂。</param>
    public ColorPickerPane(bool showFollowGlobal)
    {
        // ── 预设色板 ──
        Swatches = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        if (showFollowGlobal)
        {
            var follow = MakeSwatchButton(null, "widget.appearance.followGlobal");
            follow.Click += (_, _) => PickFromSwatch(null);
            Swatches.Children.Add(follow);
            _swatchButtons.Add((null, follow));
        }
        foreach (var (nameKey, hex) in Palette)
        {
            var btn = MakeSwatchButton(hex, nameKey);
            btn.Click += (_, _) => PickFromSwatch(hex);
            Swatches.Children.Add(btn);
            _swatchButtons.Add((hex, btn));
        }
        // 初建那一刻按钮还没进树（ActualTheme=Default）⇒ 描边等进树这一拍按真实主题铺。
        Swatches.Loaded += (_, _) => RefreshSwatchHighlights();

        // ── SV 方图：底色=当前色相纯色；横向白→透明（饱和度），纵向透明→黑（明度）──
        Gradient = new StackPanel { Spacing = 8 };
        _svCanvas = new Canvas
        {
            Width = 240,
            Height = 150,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _svHueRect = new Rectangle { Width = 240, Height = 150, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
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
        _svIndicator = new Ellipse
        {
            Width = 12,
            Height = 12,
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Colors.Transparent),
            IsHitTestVisible = false,
        };
        _svCanvas.Children.Add(_svHueRect);
        _svCanvas.Children.Add(svWhite);
        _svCanvas.Children.Add(svBlack);
        _svCanvas.Children.Add(_svIndicator);
        _svCanvas.PointerPressed += SvPointer;
        _svCanvas.PointerMoved += SvPointerMove;
        _svCanvas.PointerReleased += (_, e) => { _svPressed = false; try { _svCanvas.ReleasePointerCapture(e.Pointer); } catch { } };
        _svCanvas.PointerCanceled += (_, e) => { _svPressed = false; try { _svCanvas.ReleasePointerCapture(e.Pointer); } catch { } };
        Gradient.Children.Add(_svCanvas);

        // ── 色相条（彩虹渐变）──
        _hueCanvas = new Canvas
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
        _hueCanvas.Children.Add(new Rectangle
        {
            Width = 240,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = hueGrad,
        });
        _hueIndicator = new Rectangle
        {
            Width = 4,
            Height = 22,
            Fill = new SolidColorBrush(Colors.White),
            Stroke = new SolidColorBrush(Colors.Black),
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Margin = new Thickness(0, -2, 0, 0),
        };
        _hueCanvas.Children.Add(_hueIndicator);
        _hueCanvas.PointerPressed += HuePointer;
        _hueCanvas.PointerMoved += HuePointerMove;
        _hueCanvas.PointerReleased += (_, e) => { _huePressed = false; try { _hueCanvas.ReleasePointerCapture(e.Pointer); } catch { } };
        _hueCanvas.PointerCanceled += (_, e) => { _huePressed = false; try { _hueCanvas.ReleasePointerCapture(e.Pointer); } catch { } };
        Gradient.Children.Add(_hueCanvas);
        UpdateHueFill();
        UpdateSvIndicator();
        UpdateHueIndicator();
    }

    /// <summary>
    /// 同步外部颜色；null 只切换「跟随全局」高亮，保留 HSV 位置。
    /// </summary>
    public void SyncTo(Color? color)
    {
        // Picked 的同值回流不重算 HSV，避免黑／灰及 RGB 量化丢失内部选择。
        if (_current == color) return;
        _current = color;
        if (color is { } rgb)
        {
            var (h, s, v) = RgbToHsv(rgb);
            _pickH = h; _pickS = s; _pickV = v;
            UpdateHueFill();
            UpdateSvIndicator();
            UpdateHueIndicator();
        }
        RefreshSwatchHighlights();
    }

    /// <summary>
    /// 高亮／摘亮那一颗色板按钮。编辑器「背景色／文本色／边框色」那三颗<b>对象按钮</b>也走这同一颗
    /// （片144 起描边与高亮性格只有这一处主人）。探针＝按钮自己的 <c>ActualTheme</c>：
    /// 按钮既已上屏，它的实际主题就是权威——⛔ 再由宿主转交它窗口的主题。
    /// </summary>
    internal static void Highlight(Button btn, bool on)
    {
        var theme = btn.ActualTheme;
        if (on)
        {
            btn.BorderBrush = ThemeBrush.For(theme, "AccentFillColorDefaultBrush") ?? new SolidColorBrush(Colors.RoyalBlue);
            btn.BorderThickness = new Thickness(2);
        }
        else
        {
            // 摘掉高亮⛔ 抹成"没有边框"：那颗白色色板在浅色卡片上会连形状一起消失（批次 WI-82 量到的
            // "看不见那一颗"），描边由它自己常驻，高亮只加粗与换强调色。
            btn.BorderBrush = SwatchStroke(theme);
            btn.BorderThickness = new Thickness(1);
        }
    }

    /// <summary>色板那颗按钮的常驻描边：⛔ 用 Transparent——那颗「白」在浅色卡片上会连形状一起看不见。</summary>
    private static Brush SwatchStroke(ElementTheme theme)
        => ThemeBrush.For(theme, "CardStrokeColorDefaultBrush") ?? new SolidColorBrush(Colors.Gray);

    /// <summary>组件外观那一族的 hex 形状（#AARRGGBB、alpha 固定 FF）：色板身份比对与编辑器写值共用同一颗。</summary>
    internal static string ToHex(Color c)
        => $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Button MakeSwatchButton(string? hex, string key)
    {
        var btn = new Button
        {
            Width = 64,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            FontSize = 11,
            BorderThickness = new Thickness(1),
        };
        // 预设名称和提示共用词表键；字色按色板背景选择。
        LocText.SetKey(btn, key);
        LocText.SetToolTipKey(btn, key);
        if (hex is not null && WidgetAppearance.ParseColorBrush(hex) is { } b)
        {
            btn.Background = b;
            btn.Foreground = new SolidColorBrush(SurfaceReadability.ReadableOn(b.Color));
        }
        return btn;
    }

    private void PickFromSwatch(string? hex)
    {
        if (hex is null)
        {
            _current = null;
            RefreshSwatchHighlights();
            Picked?.Invoke(null);
            return;
        }
        if (WidgetAppearance.ParseColorBrush(hex) is not { } b) return;
        var rgb = b.Color;
        var (h, s, v) = RgbToHsv(rgb);
        _pickH = h; _pickS = s; _pickV = v;
        UpdateHueFill();
        UpdateSvIndicator();
        UpdateHueIndicator();
        _current = rgb;
        RefreshSwatchHighlights();
        Picked?.Invoke(rgb);
    }

    private void RefreshSwatchHighlights()
    {
        var cur = _current is { } c ? ToHex(c) : null;
        foreach (var (hex, btn) in _swatchButtons) Highlight(btn, hex == cur);
    }

    private void UpdateHueFill()
    {
        _svHueRect.Fill = new SolidColorBrush(HsvToRgb(_pickH, 1, 1));
    }

    private void UpdateSvIndicator()
    {
        var x = _pickS * _svCanvas.Width - _svIndicator.Width / 2;
        var y = (1 - _pickV) * _svCanvas.Height - _svIndicator.Height / 2;
        Canvas.SetLeft(_svIndicator, x);
        Canvas.SetTop(_svIndicator, y);
    }

    private void UpdateHueIndicator()
    {
        var x = (_pickH / 360.0) * _hueCanvas.Width - _hueIndicator.Width / 2;
        Canvas.SetLeft(_hueIndicator, x);
    }

    private void SvPointer(object sender, PointerRoutedEventArgs e)
    {
        _svCanvas.CapturePointer(e.Pointer);
        _svPressed = true;
        SvUpdate(e);
        e.Handled = true;
    }

    private void SvPointerMove(object sender, PointerRoutedEventArgs e)
    {
        if (_svPressed) { SvUpdate(e); e.Handled = true; }
    }

    private void SvUpdate(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_svCanvas).Position;
        _pickS = Math.Clamp(p.X / _svCanvas.Width, 0, 1);
        _pickV = Math.Clamp(1 - p.Y / _svCanvas.Height, 0, 1);
        UpdateSvIndicator();
        RaisePicked();
    }

    private void HuePointer(object sender, PointerRoutedEventArgs e)
    {
        _hueCanvas.CapturePointer(e.Pointer);
        _huePressed = true;
        HueUpdate(e);
        e.Handled = true;
    }

    private void HuePointerMove(object sender, PointerRoutedEventArgs e)
    {
        if (_huePressed) { HueUpdate(e); e.Handled = true; }
    }

    private void HueUpdate(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_hueCanvas).Position;
        _pickH = Math.Clamp(p.X / _hueCanvas.Width * 360, 0, 360);
        UpdateHueFill();
        UpdateHueIndicator();
        RaisePicked();
    }

    private void RaisePicked()
    {
        var c = HsvToRgb(_pickH, _pickS, _pickV);
        _current = c;
        RefreshSwatchHighlights();
        Picked?.Invoke(c);
    }

    // ── HSV ↔ RGB 互转（取色器用；自旧编辑器整颗迁来，算术一字未动）──

    private static Color HsvToRgb(double h, double s, double v)
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
        return Color.FromArgb(255, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static (double h, double s, double v) RgbToHsv(Color col)
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
}
