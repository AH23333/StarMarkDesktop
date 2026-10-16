#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace StarMark.UI.Helpers;

/// <summary>
/// 主题感知的画笔解析。
/// 应用级 RequestedTheme 在任何窗口创建后即冻结、运行期不可更改；运行期深浅色切换
/// 实际依赖窗口根元素的 RequestedTheme（子元素继承）。因此代码侧解析画笔必须按
/// 目标元素的 ActualTheme 从 ThemeDictionaries（"Light" / "Dark"）取值，
/// 而不是 <c>Application.Current.Resources[key]</c>（其跟随冻结的应用级主题，
/// 浅色模式下会解析出深色画笔 → 白字白底）。
/// 调色板画笔定义在 Themes/StarMarkTheme.xaml 的 ThemeDictionaries 中（AppXxx 前缀）。
/// <para>
/// <b>批次 WI-81 多出来的那一层：主题色＝自定义时，"强调色家族"那几颗画笔由它当场派生</b>
/// （<see cref="AccentKeys"/> 那一族）。<b>判据只住在这里</b>：设置页里那颗颜色一换，
/// 走代码这条路的选中标签、置顶按钮、拖放提示、速览那圈与整窗材质染的必须同时跟——从前它们各读各的
/// （XAML 那两颗写死的蓝 vs <c>WidgetAppearance.AccentColor()</c> 读的系统强调色），
/// 这正是"改了颜色只有一半跟着变"的形状。取色算法在 <c>AppPalette</c>（Core，可测）。
/// ⚠ 这一层管的是<b>代码侧按键取画笔的那几处</b>；XAML 里按 <c>{ThemeResource}</c> 直接取的那几处
/// 与控件内部样式那颗蓝今天仍⛔ 跟着自定义色（射程逐颗钉在 <c>ThemeAccentWordGateTests</c>，
/// 剩下的那一半记在 <c>docs/待决策事项.md</c> 的 <b>P-213</b>）。
/// </para>
/// </summary>
public static class ThemeBrush
{
    /// <summary>
    /// 由「主题色」派生的那四颗：XAML 里那两颗淡底各自的 alpha 不同（#1A 浅／#2E 深／#33 拖放），
    /// 所以派生时<b>保留各自的 alpha、只换 RGB</b>——⛔ 让自定义色把淡底变成实色。
    /// <para>射程只到<b>走这颗门的代码侧</b>：前三颗全仓只有代码问它们（XAML 里只定义、没人按键取），
    /// 所以那一族是覆盖满的。第四颗 <c>AccentFillColorDefaultBrush</c> ⛔ 覆盖满——XAML 里还有三处按
    /// <c>{ThemeResource}</c> 直接取它（卡片选中那条边框／主按钮样式的底色／健康那一排柱子），那条路不经过这里；
    /// 控件内部样式（开关／勾选／滑杆／进度条）同理。剩下的那一半记在 <c>docs/待决策事项.md</c> 的
    /// <b>P-213</b>，⛔ 在这里把"只改颜色"说成已全部跟上（批次 WI-81／坑表 #589 同一族：射程要写清楚）。</para>
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> AccentKeys =
        new(System.StringComparer.Ordinal)
        { "AppAccentBrush", "AppAccentSoftBrush", "WidgetDropHintBrush", "AccentFillColorDefaultBrush" };

    /// <summary>自定义主题色生效时的那几颗画笔；没设自定义（＝随系统）时返回 null，一切照旧。</summary>
    private static Brush? AccentDerived(bool dark, string key)
    {
        if (ThemeAccent.Custom is not { } accent) return null;
        var color = key switch
        {
            "AppAccentSoftBrush" => StarMark.Core.Appearance.AppPalette.Soft(accent, dark),
            "WidgetDropHintBrush" => StarMark.Core.Appearance.AppPalette.DropHint(accent),
            _ => accent,
        };
        return new SolidColorBrush(color);
    }

    /// <summary>按元素主题解析画笔；找不到时回退到应用级资源。</summary>
    public static Brush? For(ElementTheme theme, string key)
    {
        var dark = theme == ElementTheme.Dark
                   || (theme == ElementTheme.Default && ThemeManager.IsSystemDark());
        return Resolve(dark, key);
    }

    public static Brush? Resolve(bool dark, string key)
    {
        // 主题色＝自定义时，那一族先问它自己（⛔ 让 XAML 里写死的两颗蓝压在用户点定的颜色上）。
        if (AccentKeys.Contains(key) && AccentDerived(dark, key) is { } custom) return custom;

        // 深色先查自定义调色板的 "Dark" 桶，再回退到 WinUI 内置资源（XamlControlsResources /
        // generic.xaml）实际使用的 "Default" 桶——内置主题字典只有 "Light" / "Default"，没有 "Dark"，
        // 只查 "Dark" 会让 ApplicationPageBackgroundThemeBrush / TextFillColor* / SystemFillColor* /
        // AccentFillColor* 等内置画笔在深色下全部落空 → 回退到冻结的应用级资源 → 深色误判为浅色。
        var themeKeys = dark ? DarkKeys : LightKeys;
        var app = Application.Current.Resources;

        foreach (var themeKey in themeKeys)
        {
            if (FindInDict(app, themeKey, key, out var brush)) return brush;
            foreach (var md in app.MergedDictionaries)
                if (FindInDict(md, themeKey, key, out brush)) return brush;
        }

        // 兜底：应用级资源（跟随冻结的应用主题，仅作最后手段）
        if (app.TryGetValue(key, out var fallback) && fallback is Brush fb) return fb;
        return null;
    }

    private static readonly string[] LightKeys = { "Light" };
    private static readonly string[] DarkKeys = { "Dark", "Default" };

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
