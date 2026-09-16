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
            // 未选中：卡片底/分割线在深浅色间差异很小，走应用级资源即可
            return parameter as string == "border"
                ? Resolve("DividerStrokeColorDefaultBrush", new SolidColorBrush(Colors.Gray))!
                : Resolve("CardBackgroundFillColorDefaultBrush", new SolidColorBrush(Colors.Transparent))!;
        }

        // 选中：半透明强调色底 + 强调色边框，按当前实际主题解析（应用级主题
        // 启动后冻结，浅色模式下会解析出深色画笔）；统一用 ThemeBrush 调色板
        var dark = ThemeManager.IsAppDark();
        return parameter as string == "border"
            ? ThemeBrush.Resolve(dark, "AppAccentBrush") ?? new SolidColorBrush(Colors.CornflowerBlue)
            : ThemeBrush.Resolve(dark, "AppAccentSoftBrush") ?? new SolidColorBrush(Colors.CornflowerBlue);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}