#nullable enable
using System;
using Windows.UI;

namespace StarMark.Core.Appearance;

/// <summary>
/// 「主题色」这一件事的唯一主人：那颗染料色的<b>来源优先级</b>、它在设置档里的<b>写法</b>，
/// 以及由它派生出的那两颗淡底（批次 WI-81，用户裁：只能选两种 ⇒ 加一颗自定义）。
/// <para>
/// <b>住在 Core 的理由与 <see cref="MaterialTint"/> 同一条</b>（坑表 #184）：这批要机检的是
/// 「自定义那颗赢」「没自定义时逐位等于旧行为」「派生淡底只改 alpha」——全是纯算术。
/// </para>
/// <para>
/// ⚠ <b>这颗管的是"整窗共用的染料"，⛔ 管不到某个组件自己点定的那扇窗的底色</b>——后者走
/// <see cref="MaterialTint.BackdropColor"/> 的 <c>tintOverride</c>。两件事⛔ 混成一颗开关。
/// </para>
/// </summary>
public static class AppPalette
{
    /// <summary>浅色下那颗淡底（选中态／悬停那一族）的 alpha＝旧 <c>AppAccentSoftBrush</c> 的 #1A。</summary>
    public const byte SoftAlphaLight = 0x1A;

    /// <summary>深色下那颗淡底的 alpha＝旧 <c>AppAccentSoftBrush</c> 的 #2E。</summary>
    public const byte SoftAlphaDark = 0x2E;

    /// <summary>拖放提示那一层的 alpha＝旧 <c>WidgetDropHintBrush</c> 的 #33（深浅共用）。</summary>
    public const byte DropHintAlpha = 0x33;

    /// <summary>
    /// 主题色的来源：<b>用户自定义 &gt; 系统强调色 &gt; 兜底蓝</b>。
    /// <para><paramref name="systemAccent"/> 的 alpha 为 0＝系统那边没给（<c>UISettings.GetColorValue</c> 失败时的形状），
    /// 这时才用 <see cref="MaterialTint.FallbackAccent"/>——与批次 WI-81 之前 <c>WidgetAppearance.AccentColor()</c>
    /// 里的判据一字不差，只是把"自定义那一档"接在了最前面。</para>
    /// <para>那颗自定义色由设置档里的 hex 经 <see cref="ParseArgb"/> 得来：<b>认不出就当没设过</b>（交 null），
    /// ⛔ 凑一颗错色顶上——那是"改了颜色没反应"与"颜色莫名其妙"两族症状的共同入口。</para>
    /// </summary>
    public static Color ResolveAccent(Color? custom, Color systemAccent)
        => custom ?? (systemAccent.A == 0 ? MaterialTint.FallbackAccent : systemAccent);

    /// <summary>主题色那一颗淡底（<c>AppAccentSoftBrush</c> 那一族的替代品）：同一颗 RGB，只换 alpha。</summary>
    public static Color Soft(Color accent, bool isDark) => WithAlpha(accent, isDark ? SoftAlphaDark : SoftAlphaLight);

    /// <summary>拖放提示那一层：同一颗 RGB，只换 alpha。</summary>
    public static Color DropHint(Color accent) => WithAlpha(accent, DropHintAlpha);

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>
    /// 严格解析 <c>#RRGGBB</c> ／ <c>#AARRGGBB</c>（可选 <c>#</c>）。<b>认不出来就返回 null，⛔ 凑出一颗颜色</b>。
    /// <para>旧的那颗（<c>WidgetAppearance.ParseColorBrush</c>）对 <c>"#GGHHII"</c>、含空格或汉字的串
    /// 会"解析成功"并产出一颗错色，调用方按"非 null"当作有效而不再回退主题——那是
    /// <c>docs/前端主题与材质切换审查报告.md</c> 第 136 行登记过的形状。新出口⛔ 继承那个毛病，
    /// 故这一颗逐字符验；旧那颗本批⛔ 动（改它的宽容度会牵动已有组件覆盖档的观感，那是另一笔账）。</para>
    /// </summary>
    public static Color? ParseArgb(string? text)
    {
        if (text is null) return null;
        var s = text.Trim();
        if (s.Length > 0 && s[0] == '#') s = s[1..];
        if (s.Length is not (6 or 8)) return null;
        byte a = 0xFF, r = 0, g = 0, b = 0;
        var at = 0;
        if (s.Length == 8)
        {
            if (Digit(s, at) is not { } ah) return null;
            a = ah;
            at = 2;
        }
        if (Digit(s, at) is not { } rh) return null;
        r = rh;
        if (Digit(s, at + 2) is not { } gh) return null;
        g = gh;
        if (Digit(s, at + 4) is not { } bh) return null;
        b = bh;
        return Color.FromArgb(a, r, g, b);
    }

    /// <summary>
    /// 写进设置档的形态：<c>#RRGGBB</c> 大写。<b>身份是这颗 hex，⛔ 不是名字</b>（✅WI-52 同一口径）。
    /// <para>这里<b>刻意不落 alpha</b>：那颗染料按定义是实色（取色器那侧也不许调 alpha），
    /// 而 <see cref="ParseArgb"/> 仍收 <c>#AARRGGBB</c>——手改过的设置档不该被读成"没设过"。</para>
    /// </summary>
    public static string ToHexText(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>读一对十六位；认不出任何一个字符就交 null（⛔ 回 0 继续拼）。</summary>
    private static byte? Digit(string s, int at)
    {
        var hi = Nibble(s[at]);
        var lo = Nibble(s[at + 1]);
        return hi is { } h && lo is { } l ? (byte)((h << 4) | l) : null;
    }

    private static int? Nibble(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => null,
    };
}
