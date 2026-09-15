#nullable enable
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace StarMark.UI.Helpers;

/// <summary>
/// 将标签字符串转换为颜色（对应浏览器扩展 tagColor）。用于 ItemsRepeater 内标签按钮。
/// </summary>
public sealed class ContainedTagColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var tag = value as string ?? string.Empty;
        var color = TagColorHelper.GetTagColor(tag);
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}