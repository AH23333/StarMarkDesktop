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
            // 关键：Default 必须就地解析为具体的 Light/Dark，绝不能把根元素设成 ElementTheme.Default。
            // ElementTheme.Default 会去「继承」Application.Current.RequestedTheme —— 而应用级主题在首个
            // 窗口创建时即冻结，运行期不再变化。若启动时系统为深色、之后用户在浅色下使用（或运行期切主题），
            // 根元素仍继承冻结的深色 → 所有 {ThemeResource} 内容（卡片 / 标签 / 组件正文）渲染成深色，
            // 而代码侧（MainWindowSurfaceBrush / TargetTheme）按实时系统主题把外层铺成浅色 → 浅底黑块。
            // DeskBox 同款做法：从不依赖继承，每个窗口根都显式盖一个具体主题。
            root.RequestedTheme = ResolveEffectiveTheme(pref);
        }

        // 应用级主题只在启动首个窗口创建前生效一次（用于框架默认模板初值）；运行期由上面的根元素具体主题决定。
        ApplyAppLevelTheme(pref);
    }

    /// <summary>把主题偏好折算成窗口根元素的<b>具体</b> ElementTheme（Default 按实时系统主题解析，绝不返回 Default）。</summary>
    public static ElementTheme ResolveEffectiveTheme(ThemePreference pref) => pref switch
    {
        ThemePreference.Light => ElementTheme.Light,
        ThemePreference.Dark => ElementTheme.Dark,
        _ => IsSystemDark() ? ElementTheme.Dark : ElementTheme.Light,
    };

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