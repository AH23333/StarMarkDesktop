#nullable enable
using Windows.UI;
using StarMark.Core.Appearance;

namespace StarMark.UI.Helpers;

/// <summary>缓存自定义背景色，避免每次刷新主窗口与组件材质时重复读取设置。</summary>
internal static class ThemeAccent
{
    /// <summary>用户指定的背景色；null 表示使用主题或系统默认背景色。</summary>
    public static Color? Custom { get; private set; }

    /// <summary>落盘之后调用：把 hex 认成一颗颜色，认不出就当没设过 ⇒ 读者与设置档永远同一颗。</summary>
    public static void Apply(string? hexFromSettings) => Custom = AppPalette.ParseArgb(hexFromSettings);
}
