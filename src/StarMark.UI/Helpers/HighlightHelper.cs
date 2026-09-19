#nullable enable
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using StarMark.Core.Text;

namespace StarMark.UI.Helpers;

/// <summary>
/// 把 <see cref="Highlighter.Split"/> 产出的片段刷进 <see cref="TextBlock.Inlines"/>。
/// 切分逻辑本身在 Core（纯函数、可单测），这里只负责 XAML 侧的呈现。
/// </summary>
public static class HighlightHelper
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.RegisterAttached(
            "Segments",
            typeof(IReadOnlyList<TextSegment>),
            typeof(HighlightHelper),
            new PropertyMetadata(null, OnSegmentsChanged));

    public static void SetSegments(DependencyObject element, IReadOnlyList<TextSegment>? value)
        => element.SetValue(SegmentsProperty, value);

    public static IReadOnlyList<TextSegment>? GetSegments(DependencyObject element)
        => element.GetValue(SegmentsProperty) as IReadOnlyList<TextSegment>;

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;

        tb.Inlines.Clear();
        if (e.NewValue is not IReadOnlyList<TextSegment> segments || segments.Count == 0) return;

        foreach (var seg in segments)
        {
            var run = new Run { Text = seg.Text };
            if (seg.IsMatch)
            {
                run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                // 主题感知解析（§3.1）：TextBlock 元素的 ActualTheme 即当前生效主题
                run.Foreground = ThemeBrush.For(tb.ActualTheme, "SystemFillColorCautionBrush");
            }
            tb.Inlines.Add(run);
        }
    }
}
