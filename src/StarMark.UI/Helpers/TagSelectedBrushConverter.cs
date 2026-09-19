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
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var selected = value is bool b && b;
        // 按「窗口当前实际主题」解析（应用级主题启动后冻结，直接读 Application.Current.Resources
        // 会在浅色模式下解析出深色画笔 → 标签/卡片浅底变黑）。
        var dark = ThemeManager.IsAppDark();
        if (!selected)
        {
            // 未选中：卡片底/分割线在深浅色间差异很小，但仍须按当前主题取，避免浅色下取到深色底
            return parameter as string == "border"
                ? ThemeBrush.Resolve(dark, "DividerStrokeColorDefaultBrush") ?? new SolidColorBrush(Colors.Gray)
                : ThemeBrush.Resolve(dark, "CardBackgroundFillColorDefaultBrush") ?? new SolidColorBrush(Colors.Transparent);
        }

        // 选中：半透明强调色底 + 强调色边框，统一用 ThemeBrush 调色板按实际主题解析
        return parameter as string == "border"
            ? ThemeBrush.Resolve(dark, "AppAccentBrush") ?? new SolidColorBrush(Colors.CornflowerBlue)
            : ThemeBrush.Resolve(dark, "AppAccentSoftBrush") ?? new SolidColorBrush(Colors.CornflowerBlue);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}