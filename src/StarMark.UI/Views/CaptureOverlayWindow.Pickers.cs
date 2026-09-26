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
        if (_pickerFlyout is not null) HidePicker();
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
                $"{Annotation.ThicknessNames[index]}（{WeightToolName} 上约 {Annotation.ThicknessFor(_tool ?? AnnotationTool.Pen, index)} 像素）");
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

    private void ShowPicker(FrameworkElement anchor, UIElement content)
    {
        _pickerFlyout = new Flyout { Content = content };
        _pickerFlyout.ShowAt(anchor);
    }

    private void HidePicker()
    {
        _pickerFlyout?.Hide();
        _pickerFlyout = null;
        SyncTools();
    }

}
