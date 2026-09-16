#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace StarMark.UI.Helpers;

/// <summary>
/// 主题感知的画笔解析。
/// 应用级 RequestedTheme 在任何窗口创建后即冻结、运行期不可更改；运行期深浅色切换
/// 实际依赖窗口根元素的 RequestedTheme（子元素继承）。因此代码侧解析画笔必须按
/// 目标元素的 ActualTheme 从 ThemeDictionaries（"Light" / "Default"）取值，
/// 而不是 <c>Application.Current.Resources[key]</c>（其跟随冻结的应用级主题，
/// 浅色模式下会解析出深色画笔 → 白字白底）。
/// 调色板画笔定义在 Themes/StarMarkTheme.xaml 的 ThemeDictionaries 中（AppXxx 前缀）。
/// </summary>
public static class ThemeBrush
{
    /// <summary>按元素主题解析画笔；找不到时回退到应用级资源。</summary>
    public static Brush? For(ElementTheme theme, string key)
    {
        var dark = theme == ElementTheme.Dark
                   || (theme == ElementTheme.Default && ThemeManager.IsSystemDark());
        return Resolve(dark, key);
    }

    public static Brush? Resolve(bool dark, string key)
    {
        var themeKey = dark ? "Default" : "Light";
        var app = Application.Current.Resources;

        if (FindInDict(app, themeKey, key, out var brush)) return brush;
        foreach (var md in app.MergedDictionaries)
            if (FindInDict(md, themeKey, key, out brush)) return brush;

        // 兜底：应用级资源（跟随冻结的应用主题，仅作最后手段）
        if (app.TryGetValue(key, out var fallback) && fallback is Brush fb) return fb;
        return null;
    }

    private static bool FindInDict(ResourceDictionary dict, string themeKey, string key, out Brush? brush)
    {
        if (dict.ThemeDictionaries.TryGetValue(themeKey, out var themeObj)
            && themeObj is ResourceDictionary themeDict
            && themeDict.TryGetValue(key, out var v)
            && v is Brush b)
        {
            brush = b;
            return true;
        }
        foreach (var md in dict.MergedDictionaries)
            if (FindInDict(md, themeKey, key, out brush)) return true;
        brush = null;
        return false;
    }
}
