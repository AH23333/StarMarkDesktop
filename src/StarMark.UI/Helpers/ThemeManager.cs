#nullable enable
using Microsoft.UI.Xaml;

namespace StarMark.UI.Helpers;

/// <summary>把主题偏好应用到窗口根元素（ElementTheme 由下级元素继承）。</summary>
public static class ThemeManager
{
    public static void Apply(Window window, ThemePreference pref)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = pref switch
            {
                ThemePreference.Light => ElementTheme.Light,
                ThemePreference.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }
}