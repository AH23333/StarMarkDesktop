#nullable enable
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace StarMark.UI.Helpers;

/// <summary>
/// 标签选中态画笔：选中 → 主题强调色，未选中 → 默认卡片/分割线。
/// parameter 为 "border" 时用于 BorderBrush，否则用于 Background。
/// </summary>
public sealed class TagSelectedBrushConverter : IValueConverter
{
    private static Brush? Resolve(string key, Brush? fallback)
    {
        if (Microsoft.UI.Xaml.Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
            return brush;
        return fallback;
    }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var selected = value is bool b && b;
        if (!selected)
        {
            return parameter as string == "border"
                ? Resolve("DividerStrokeColorDefaultBrush", new SolidColorBrush(Colors.Gray))!
                : Resolve("CardBackgroundFillColorDefaultBrush", new SolidColorBrush(Colors.Transparent))!;
        }

        return parameter as string == "border"
            ? Resolve("AccentFillColorDefaultBrush", new SolidColorBrush(Colors.CornflowerBlue))!
            : Resolve("AccentFillColorSecondaryBrush", new SolidColorBrush(Colors.CornflowerBlue))!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}