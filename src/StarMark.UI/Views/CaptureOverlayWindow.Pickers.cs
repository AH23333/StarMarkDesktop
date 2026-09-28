#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
// "按下这一想改什么"的枚举归模型（Core.Capture）所有：判定与取值同源，界面不再自己列一份
// （原来那份私有 enum 就是让"拖动一行字变成放大字号"测不到的一半原因——政策在界面，测试引不到）。
using Grab = StarMark.Core.Capture.AnnotationGrab;

namespace StarMark.UI.Views;

/// <summary>
/// 选择浮层：图形 / 颜色 / 粗细（项同样按模型生成，不在 XAML 里手写）。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── 选择浮层：图形 / 颜色 / 粗细（项同样按模型生成）──────────────────

    private void ShowShapePicker(FrameworkElement anchor)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Padding = new Thickness(4) };
        foreach (AnnotationTool shape in AnnotationTools.Shapes)
        {
            var wanted = shape;
            var button = IconButton(ToolIcon(shape),
                $"{Annotation.ToolName(shape)}：{Annotation.ToolHint(shape)}");
            button.Background = shape == _tool ? BarChecked : BarNormal;
            button.Click += (_, _) =>
            {
                // 浮层里点图形＝选中它（"收笔"统一走 Esc，与笔类同一口径）
                SetTool(wanted);
                HidePicker();
            };
            row.Children.Add(button);
        }
        ShowPicker(anchor, row);
    }

    private void ToggleBrushPicker(FrameworkElement anchor)
    {
        if (_pickerOpen) HidePicker();
        else ShowBrushPicker(anchor);
    }

    private void ShowBrushPicker(FrameworkElement anchor)
    {
        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Padding = new Thickness(4) };

        var colours = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (var index = 0; index < Annotation.Palette.Count; index++)
        {
            var colour = Annotation.Palette[index];
            var wanted = index;
            var dot = IconButton(DotIcon(colour.Bgra, selected: index == _colourIndex), colour.Name);
            dot.Click += (_, _) =>
            {
                _colourIndex = wanted;
                // 正在输入的那行字跟着换色：否则"选了颜色，字却没变"要用户自己去猜为什么
                if (_editingText) ApplyEditorAccent();
                HidePicker();
            };
            colours.Children.Add(dot);
        }

        var weights = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (var index = 0; index < Annotation.ThicknessSteps.Length; index++)
        {
            var wanted = index;
            var dot = IconButton(DotIcon(SolidWhite, selected: index == _weightIndex, diameter: 3 + index * 3),
                $"{Annotation.ThicknessNames[index]}（{WeightToolName} 的{Annotation.ThicknessUnit(_tool ?? AnnotationTool.Pen)}约 {Annotation.ThicknessFor(_tool ?? AnnotationTool.Pen, index)} 像素）");
            dot.Click += (_, _) =>
            {
                _weightIndex = wanted;
                HidePicker();
            };
            weights.Children.Add(dot);
        }

        rows.Children.Add(colours);
        rows.Children.Add(weights);
        ShowPicker(anchor, rows);
    }

    /// <summary>
    /// 「组」那一栏：新建一组 / 并进已有的一组 / 移出当前组。整排按名册生成，颗上没有一颗是写死的
    /// （批次 WM 的口径：文字与图标一律来自模型，否则加一组就少一颗），组号与张数都从
    /// <see cref="PinManager"/> 现取——并完立刻 <see cref="HidePicker"/>→<c>SyncTools</c>，
    /// 条上那颗的说明跟着换成新组号；留着旧组号会让人以为没生效而再点一次，那一次会真的又多建一组。
    /// </summary>
    private void ToggleGroupPicker(FrameworkElement anchor)
    {
        if (_pickerOpen) { HidePicker(); return; }
        ShowGroupPicker(anchor);
    }

    private void ShowGroupPicker(FrameworkElement anchor)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Padding = new Thickness(4) };
        row.Children.Add(GroupChip("新建一组", "把这一张单独分成一组", () => PinManager.NewGroup(this)));
        foreach (var group in PinManager.Groups)
        {
            var target = group;
            row.Children.Add(GroupChip(target.Name,
                $"把这一张并进{target.Name}（现在 {target.Members.Count} 张）",
                () => PinManager.JoinGroup(this, target.Serial)));
        }
        if (PinManager.GroupOf(this) is not null)
            row.Children.Add(GroupChip("移出", "让这一张不再属于任何一组", () => PinManager.LeaveGroup(this)));
        ShowPicker(anchor, row);
    }

    /// <summary>
    /// 组栏里的一颗文字芯片。这里<b>刻意不画图标</b>：组号本来就是文字（"组 3"画成图形就没人认得出是第几组），
    /// 而"图标排上不许写死文字"那条禁令管的是主排按钮——这一栏是点开才出现的选项列表，与浮层里的颜色名同类。
    /// </summary>
    private Button GroupChip(string text, string tip, Action apply)
    {
        var chip = new Button
        {
            Content = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
            Height = ButtonHeight,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0),
            Background = BarNormal,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(chip, tip);
        chip.Click += (_, _) =>
        {
            apply();
            HidePicker();
        };
        return chip;
    }

    /// <summary>
    /// 选择栏<b>摆在条子自己那一行里</b>，不是 Flyout（批次 WI 的真机结论）：贴图态条子住一扇 33 像素高的
    /// 独立小窗，而 WinUI 3 把弹出层钉在宿主窗边界内——Flyout 出来只有半截，图形与颜色等于选不了。
    /// 条子自己排版能自己长高，但<b>那扇窗不会自己跟着长</b>：条子在窗里是 Stretch 的，父窗不给高度
    /// 它就量不出自己变高了（<c>SizeChanged</c> 不响），所以每一处开关选择栏都必须叫 <see cref="ReflowBar"/>。
    /// </summary>
    private void ShowPicker(FrameworkElement anchor, UIElement content)
    {
        _ = anchor;                       // 摆位不再按锚点，整条第二行就是它的位置
        BarPicker.Content = content;
        BarPicker.Visibility = Visibility.Visible;
        _pickerOpen = true;
        ReflowBar();
    }

    private void HidePicker()
    {
        if (!_pickerOpen) return;
        BarPicker.Content = null;
        BarPicker.Visibility = Visibility.Collapsed;
        _pickerOpen = false;
        SyncTools();
        ReflowBar();
    }

}
