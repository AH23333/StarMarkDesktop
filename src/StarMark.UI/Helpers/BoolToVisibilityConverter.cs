#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace StarMark.UI.Helpers;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var invert = parameter is string s && s == "Invert";
        var boolValue = value is bool b && b;
        if (invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visible = value is Visibility v && v == Visibility.Visible;
        return parameter is string s && s == "Invert" ? !visible : visible;
    }
}
