#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace StarMark.UI.Helpers;

/// <summary>
/// 把布尔「是否为移除类事件」映射为画笔：移除=警示色（Caution），新增=成功色（Success）。
/// 供活动流徽标配色（扩展对比方案 P1-3）。
/// </summary>
public sealed class IsRemovalToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isRemoval = value is bool b && b;
        return isRemoval
            ? (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            : (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
