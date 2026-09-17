#nullable enable
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace StarMark.UI.Controls;

/// <summary>
/// 自适应多列流式面板：按可用宽度自动决定列数（宽屏两列、窄屏一列），
/// 并把子项依次投放到「当前最短的那一列」，避免一列很长一列很短。
/// <para>
/// 设置页原先统一用 <c>StackPanel MaxWidth=560 HorizontalAlignment=Left</c>，
/// 导致卡片全挤在左侧、右侧大片空白。WinUI 3 没有内置 WrapPanel / UniformGrid，
/// 这里自绘一个只做「分列 + 竖向堆叠」的轻量面板即可覆盖设置页场景。
/// </para>
/// </summary>
public sealed class ColumnFlowPanel : Panel
{
    /// <summary>每列的最小宽度（低于该宽度就减少列数，最少 1 列）。</summary>
    public double MinColumnWidth { get; set; } = 420;

    /// <summary>最多几列。</summary>
    public int MaxColumns { get; set; } = 2;

    /// <summary>列间距（DIP）。</summary>
    public double ColumnSpacing { get; set; } = 14;

    /// <summary>同列内子项的竖向间距（DIP）。</summary>
    public double RowSpacing { get; set; } = 10;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            // 宽度未知（例如放在可无限拉伸的容器里）：退化成单列
            foreach (var child in Children)
                child.Measure(availableSize);
            return MeasureAsSingleColumn(availableSize);
        }

        var columns = ComputeColumnCount(width);
        var columnWidth = Math.Max(0, (width - (ColumnSpacing * (columns - 1))) / columns);

        foreach (var child in Children)
            child.Measure(new Size(columnWidth, double.PositiveInfinity));

        return new Size(width, EstimateHeight(columns));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = finalSize.Width;
        var columns = ComputeColumnCount(width);
        var columnWidth = Math.Max(0, (width - (ColumnSpacing * (columns - 1))) / columns);
        var x = new double[columns];
        var y = new double[columns];
        for (var i = 0; i < columns; i++) x[i] = i * (columnWidth + ColumnSpacing);

        foreach (var child in Children)
        {
            // 投放到当前累计高度最小的列（让两列视觉上等高）
            var target = 0;
            for (var i = 1; i < columns; i++)
                if (y[i] < y[target] - 0.5) target = i;

            var h = child.DesiredSize.Height;
            child.Arrange(new Rect(x[target], y[target], columnWidth, h));
            y[target] += h + RowSpacing;
        }

        var totalHeight = 0d;
        for (var i = 0; i < columns; i++) totalHeight = Math.Max(totalHeight, y[i] - RowSpacing);
        return new Size(width, Math.Max(0, totalHeight));
    }

    private int ComputeColumnCount(double width)
    {
        if (MaxColumns <= 1 || MinColumnWidth <= 0) return 1;
        var fit = (int)Math.Floor((width + ColumnSpacing) / (MinColumnWidth + ColumnSpacing));
        return Math.Clamp(fit, 1, Math.Max(1, MaxColumns));
    }

    private double EstimateHeight(int columns)
    {
        var y = new double[columns];
        foreach (var child in Children)
        {
            var target = 0;
            for (var i = 1; i < columns; i++)
                if (y[i] < y[target] - 0.5) target = i;
            y[target] += child.DesiredSize.Height + RowSpacing;
        }
        var max = 0d;
        for (var i = 0; i < columns; i++) max = Math.Max(max, y[i] - RowSpacing);
        return Math.Max(0, max);
    }

    private Size MeasureAsSingleColumn(Size availableSize)
    {
        var height = 0d;
        var width = 0d;
        foreach (var child in Children)
        {
            height += child.DesiredSize.Height + RowSpacing;
            width = Math.Max(width, child.DesiredSize.Width);
        }
        var w = double.IsInfinity(availableSize.Width) ? width : availableSize.Width;
        return new Size(w, Math.Max(0, height - RowSpacing));
    }
}
