#nullable enable
using Microsoft.UI.Xaml;

namespace StarMark.UI.Helpers;

/// <summary>把主题偏好应用到窗口根元素（ElementTheme 由下级元素继承）。</summary>
public static class ThemeManager
{
    /// <summary>应用级主题：只能在任何窗口创建前调用一次（启动时）。</summary>
    public static void ApplyAppLevelTheme(ThemePreference pref)
    {
        try
        {
            Application.Current.RequestedTheme = pref switch
            {
                ThemePreference.Light => ApplicationTheme.Light,
                ThemePreference.Dark => ApplicationTheme.Dark,
                _ => IsSystemDark() ? ApplicationTheme.Dark : ApplicationTheme.Light,
            };
        }
        catch
        {
            // 窗口创建后再设置会抛 E_ILLEGAL_METHOD_CALL，忽略
        }
    }

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

        // 代码里通过 Application.Current.Resources[key] 解析主题画笔时，
        // 依据的是「应用级主题」而非窗口根元素的 RequestedTheme。
        // 注意：WinUI3 里应用级主题只能在任何窗口创建完成前设置一次，运行期会抛异常，
        // 因此运行期主题切换只能依靠窗口根元素 + 主题预留色解析（见 ResolveThemeBrush）。
        ApplyAppLevelTheme(pref);
    }

    /// <summary>当前系统是否为深色（用于 Default 时决定应用级主题）。</summary>
    public static bool IsSystemDark()
    {
        try
        {
            var bg = new Windows.UI.ViewManagement.UISettings()
                .GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return (bg.R + bg.G + bg.B) / 3.0 < 128;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读取应用当前生效主题（窗口实际渲染主题为浅色返回 false，深色返回 true）。
    /// 注意：不能读 Application.Current.RequestedTheme——它在窗口创建后即冻结，
    /// 运行期切换主题只影响窗口根元素，浅色模式下会误判为深色（标签颜色因此发白）。</summary>
    public static bool IsAppDark()
    {
        if (App.MainWindow?.Content is FrameworkElement root)
            return root.ActualTheme == ElementTheme.Dark;
        return IsSystemDark();
    }
}