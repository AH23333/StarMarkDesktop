#nullable enable
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Helpers;

/// <summary>
/// 活动事件三色归类（<see cref="ActivityChange"/>）→ 画刷（#51）：
/// 增=绿(SystemFillColorSuccess)，删=红(SystemFillColorCaution)，改=黄(SystemFillColorAttention)。
/// 主题感知解析：IValueConverter 拿不到元素，按主窗当前暗色态从 ThemeDictionaries 取（同其余主题画笔用法）。
/// </summary>
public sealed class ActivityChangeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var dark = ThemeManager.IsAppDark();
        var key = value is ActivityChange c
            ? c switch
            {
                ActivityChange.Removed => "SystemFillColorCautionBrush",
                ActivityChange.Modified => "SystemFillColorAttentionBrush",
                _ => "SystemFillColorSuccessBrush",
            }
            : "SystemFillColorSuccessBrush";
        return ThemeBrush.Resolve(dark, key) ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
