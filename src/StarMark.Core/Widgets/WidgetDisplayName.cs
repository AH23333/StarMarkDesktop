#nullable enable
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;

namespace StarMark.Core.Widgets;

/// <summary>
/// 布局／快照<b>名字</b>这一列「库里那一颗 ↔ 屏幕上那一句」的<b>唯一主人</b>（批次 WI-67 片6，落 ✅P-200 他裁 (A)）。
/// <para>
/// 为什么分两侧：<c>Name</c> 是<b>会被写进 widgets.json 的用户数据</b>，而「未命名布局」那种词是<b>界面语言的内容</b>。
/// 把后者存进档＝把<b>建出来那一刻</b>的那门语言冻进用户文件（＝P-199 那颗形状，换语言后它永不改口），
/// 所以库里只存<b>中性形状</b>（用户没起名就是空串），词与序号都在<b>贴上屏幕的那一刻</b>现算。
/// </para>
/// <para>
/// ⚠ 序号也只在这里发、且<b>只发在屏幕上</b>：⛔ 写回 <c>Name</c>。库里两行空白是合法的（它们就是"没名字"），
/// 屏幕上却必须一颗叫「未命名布局」、另一颗叫「未命名布局 (2)」——这件事今天靠 <c>Normalize</c> 把词写进档来防，
/// 从这一版起靠 <see cref="Assign"/> 防（分组键＝解析后的基名，所以空串与用户<b>真打了</b>同一颗词仍落进同一组）。
/// </para>
/// </summary>
public static class WidgetDisplayName
{
    /// <summary>库里那颗空白名字在布局那一列念出来的词。⛔ 在 <see cref="Neutralize"/> 里出现——只出现在这里。</summary>
    public const string LayoutFallbackKey = "widget.name.layoutFallback";

    /// <summary>同上，快照侧。两颗⛔ 合成一颗：那两列本来就各说各的东西（DR 那格钉过这份分化）。</summary>
    public const string SnapshotFallbackKey = "widget.name.snapshotFallback";

    /// <summary>
    /// 同名序号那副括号，三语<b>同形</b>（半角括号＋一个空格）。刻意⛔ 用全角：
    /// 用户档里历史遗留的「X (2)」就是这个形状，屏幕上重排⛔ 换一副括号去和它长得不一样。
    /// </summary>
    public const string DedupSuffixKey = "widget.name.dedupSuffix";

    /// <summary>用户自己打的那颗字面：走表里既有的<b>原样出口</b>（三语都是 <c>{0}</c>），于是它和兜底词同成一个类型、同一套延迟取词。</summary>
    private const string LiteralKey = "common.rawValue";

    /// <summary>
    /// <b>写侧</b>：中性化。空白（含纯空格）一律存<b>空串</b>，其余只 <c>Trim</c>——⛔ 在这里挑语言，也⛔ 在这里编序号。
    /// </summary>
    public static string Neutralize(string? name) => string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();

    /// <summary>
    /// <b>读侧</b>：库里那一颗在屏幕上的那一句。<paramref name="ordinal"/> 是<b>同组内第几颗</b>（1 起＝不带后缀），
    /// 那颗序号⛔ 由调用方自己数——只有 <see cref="Assign"/> 数得对（它得看整列）。
    /// </summary>
    public static LocMessage Of(string? stored, int ordinal, string fallbackKey)
    {
        var neutral = Neutralize(stored);
        var baseName = neutral.Length == 0 ? LocMessage.Of(fallbackKey) : LocMessage.Of(LiteralKey, neutral);
        return ordinal <= 1 ? baseName : LocMessage.Of(DedupSuffixKey, baseName, ordinal);
    }

    /// <summary>
    /// <b>读侧</b>：按<b>显示顺序</b>给出的库里名逐颗发词，同组第二颗起带 (2)、(3)…（＝<see cref="Of"/> 那颗序号的唯一来源）。
    /// 传进来的顺序就是那一列在屏幕上的顺序，所以两颗宿主（设置页那一列与组件右键子菜单）念出来⛔ 会各数一套。
    /// </summary>
    public static List<LocMessage> Assign(IEnumerable<string?> storedNames, string fallbackKey)
    {
        var seen = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        var result = new List<LocMessage>();
        foreach (var stored in storedNames)
        {
            var neutral = Neutralize(stored);
            var groupKey = neutral.Length == 0 ? Locale.T(fallbackKey) : neutral;
            var nth = (seen.TryGetValue(groupKey, out var prior) ? prior : 0) + 1;
            seen[groupKey] = nth;
            result.Add(Of(stored, nth, fallbackKey));
        }
        return result;
    }

    /// <summary>布局那一列（键＝<c>Id</c>——名字从这一版起可以重复，⛔ 再当键用）。</summary>
    public static Dictionary<string, LocMessage> ForLayouts(IEnumerable<WidgetLayout> layouts)
        => For(layouts.Select(l => l.Id), layouts.Select(l => l.Name), LayoutFallbackKey);

    /// <summary>快照那一列。</summary>
    public static Dictionary<string, LocMessage> ForSnapshots(IEnumerable<WidgetSnapshot> snapshots)
        => For(snapshots.Select(s => s.Id), snapshots.Select(s => s.Name), SnapshotFallbackKey);

    /// <summary>
    /// 从那一列里取一颗。<b>认不到的 Id 退回"不带序号"</b>：那一列可能刚被另一个入口改过（保存回执就是这种时刻），
    /// 而⛔ 因此把一次成功的保存抛成一条"失败"提示。
    /// </summary>
    public static LocMessage Word(this IReadOnlyDictionary<string, LocMessage> words, string id, string? stored, string fallbackKey)
        => words.TryGetValue(id, out var word) ? word : Of(stored, 1, fallbackKey);

    private static Dictionary<string, LocMessage> For(IEnumerable<string> ids, IEnumerable<string?> names, string fallbackKey)
    {
        var idList = ids.ToList();
        var words = Assign(names, fallbackKey);
        var map = new Dictionary<string, LocMessage>();
        for (var i = 0; i < idList.Count && i < words.Count; i++) map[idList[i]] = words[i];
        return map;
    }
}
