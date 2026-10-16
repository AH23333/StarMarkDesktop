#nullable enable
using Windows.UI;
using StarMark.Core.Appearance;

namespace StarMark.UI.Helpers;

/// <summary>
/// 「主题色 · 自定义」这一档的<b>运行期唯一读者入口</b>（批次 WI-81，用户裁：只能选两种 ⇒ 加一颗自定义）。
/// <para>
/// <b>为什么是缓存而不是每次读档</b>：这颗颜色的读者比设置页里任何一颗都多——每一扇组件窗套材质、
/// 主窗每次刷背景、每一颗强调画笔（选中标签／置顶按钮／拖放提示／速览那圈）都要问它，而材质套用是启动
/// 头号嫌疑（日志里 5 秒内出现过 38 次）。让 <see cref="ThemeBrush"/> 去读 settings.json 等于给画笔解析
/// 加一次整档反序列化（性能纪律：这里⛔ 加读数，直接不给它加）。
/// </para>
/// <para>
/// <b>写的人只有两处，且都是"落盘的那一刻"</b>：应用启动（<c>App</c> 建好 DI、首个窗口之前）与设置页保存。
/// 认不出的 hex 一律当<b>没设过</b>（<see cref="AppPalette.ParseArgb"/>），⛔ 凑一颗错色顶上。
/// </para>
/// </summary>
internal static class ThemeAccent
{
    /// <summary>用户点定的那颗；<c>null</c>＝「随系统强调色」，一切按批次 WI-81 之前的旧行为。</summary>
    public static Color? Custom { get; private set; }

    /// <summary>落盘之后调用：把 hex 认成一颗颜色，认不出就当没设过 ⇒ 读者与设置档永远同一颗。</summary>
    public static void Apply(string? hexFromSettings) => Custom = AppPalette.ParseArgb(hexFromSettings);
}
