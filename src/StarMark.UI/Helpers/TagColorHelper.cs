#nullable enable
using Microsoft.UI;

namespace StarMark.UI.Helpers;

/// <summary>
/// 根据标签名生成稳定颜色。移植自浏览器扩展 tagColor() 函数。
/// </summary>
public static class TagColorHelper
{
    public static Windows.UI.Color GetTagColor(string tag)
    {
        // 浅色模式下背景偏白，亮度取低值保证可读性；深色模式取高值。
        var light = ThemeManager.IsAppDark() ? 0.58 : 0.42;
        uint h = 0;
        foreach (var c in tag) h = (h * 31 + c) & 0xFFFFFFFF;
        var hue = h % 360;
        return HslToColor(hue, 0.72, light);
    }

    public static Windows.UI.Color HslToColor(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = l - c / 2;
        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return Windows.UI.Color.FromArgb(
            255,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
