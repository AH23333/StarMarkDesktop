#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;
using StarMark.Core.Hotkeys;

namespace StarMark.Core.Guide;

/// <summary>
/// 一段说明：一个标题加几行正文，外加"这一步能把用户带去哪儿"与"这一步配哪张示意图"。
/// <para>刻意不用富文本/markdown——三处宿主（首启向导、顶栏「？」的浮层、设置页「使用说明」栏）
/// 都只是"标题 + 项目符号"，中间格式只会多出一层要维护的渲染器。</para>
/// <para><b>这里交的是键，不是词</b>（批次 WI-39）：见 <see cref="PageGuides"/> 的说明——
/// 一旦在这里取词，语言就被钉在"算出它那一刻"，而下面那颗静态缓存正是那一刻。</para>
/// <para><paramref name="JumpTag"/> 与 <paramref name="Art"/> 都是<b>名字而不是内容</b>：跳转要落到
/// 真存在的页（对不上就是假按钮，用户点了没反应＝把活推回给他，P-54），示意图要画得出来
/// （对不上就是一片空白）。两者都由 <c>GuideGateTests</c> 对着 UI 那两份清单比，写错当场红。</para>
/// </summary>
public sealed record GuideSection(string TitleKey, IReadOnlyList<string> LineKeys, string? JumpTag = null, string? Art = null);

/// <summary>
/// 一页的说明。<b>tag 与文字写在同一行</b>：原先是"列表按位置映射到 tag"，那正好是"两个槽位对调
/// 而谁也发现不了"的形状（列表顺序一改，说明就串页），所以把 tag 放进条目本身。
/// </summary>
public sealed record PageGuide(string Tag, string TitleKey, IReadOnlyList<string> LineKeys);

/// <summary>
/// 应用内说明的<b>唯一文字出处</b>（批次 VC-3；批次 WI-2q 起字面进语言表，批次 WI-39 起这里<b>只交键</b>）。
/// <para><b>为什么住 Core</b>：同一批话有三个地方要说——首启引导、每页右上角的「？」、设置页的
/// 「使用说明」栏。写三份必然漂移（#123 那一族：一处事实多个出处），而漂移的表现是"三处说法不一样，
/// 用户不知道哪句作数"。住 Core 之后它可被单测：每一页有没有说明、有没有空行、
/// 新加一页而这里没补一条＝当场红（<c>GuideGateTests</c>）。</para>
/// <para>⛔ <b>这里不许调 <see cref="Locale"/>。</b>原先 <see cref="InOrder"/> 当场取词、把成品串交给宿主，
/// 而下面那颗 <see cref="ByTag"/> 是<b>静态</b>字典——第一次被访问时就把那一语言的整份说明钉进了缓存，
/// 之后换语言：顶栏「？」与设置页那一栏都还说旧语言（真机读到的就是这一格，他报的第 5 条：
/// "修改语言后需要重新刷新进入设置页翻译才会生效"）。交键之后，取词发生在<b>贴上去那一刻</b>（<c>GuideUi</c>），
/// 缓存里剩的是与语言无关的名字 ⇒ 换语言由 <c>LocText</c> 那唯一一处订阅重贴，不需要重进任何页。</para>
/// <para><b>刻意不写具体按键</b>："F1 截图"这种句子是热键的第二个出处，用户在设置里改过键之后它就是假话。
/// 要提按键的地方只给<b>动作名清单</b>（<see cref="FirstRunGuide.HotkeyActionsToShow"/>），
/// 由 UI 那侧从绑定表现场渲染（<c>HotkeyDisplay</c> 是按键文本的唯一出处）。</para>
/// </summary>
public static class PageGuides
{
    /// <summary>
    /// 设置页里「使用说明」那一栏的<b>身份</b>，不是屏幕上那几个字。
    /// <para>批次 WI-2 起页签标题进了界面语言表（跟着语言走），而"带我去那一栏"的调用方是编译期写死的：
    /// 拿标题当地址，切了英文之后就是"点了「完整设置」落在默认页"。所以这里只留身份串，
    /// <c>OpenSettingsPage</c>／<c>SelectTab</c> 一律按它匹配；XAML 那八颗 <c>Tag</c> 与 UI 侧那张身份表由闸门两侧对账。</para>
    /// </summary>
    public const string SettingsTabKey = "guide";

    /// <summary>「？」在设置页上要说的那一页的键（与 <c>ViewModel.CurrentPageTag</c> 同一套字面）。</summary>
    public const string SettingsTag = "settings";

    /// <summary>搜索页的键：它不在侧栏里（输入即进），但「？」同样要说得出它是什么。</summary>
    public const string SearchTag = "search";

    /// <summary>
    /// 每页的键表（语言无关）。⚠ 键<b>逐颗写死在这里</b>、不许用 <c>$"guide.page.{tag}"</c> 内插拼出来：
    /// 正向/反向两条对账闸门认的是<b>字面量</b>（#290），内插构键会让 60 颗键全部"没人问"，
    /// 闸门红成一片（WI-2q 首跑的真红）。改页数/行数时这里与 UiStrings 同批改。
    /// </summary>
    private static readonly (string Tag, string TitleKey, string[] LineKeys)[] Entries =
    {
        (SearchTag, "guide.page.search", new[] { "guide.page.search.1", "guide.page.search.2", "guide.page.search.3" }),
        ("tree", "guide.page.tree", new[] { "guide.page.tree.1", "guide.page.tree.2", "guide.page.tree.3" }),
        ("tags", "guide.page.tags", new[] { "guide.page.tags.1", "guide.page.tags.2" }),
        ("activity", "guide.page.activity", new[] { "guide.page.activity.1", "guide.page.activity.2" }),
        ("hidden", "guide.page.hidden", new[] { "guide.page.hidden.1", "guide.page.hidden.2" }),
        ("snapshot", "guide.page.snapshot", new[] { "guide.page.snapshot.1", "guide.page.snapshot.2" }),
        ("clipboard", "guide.page.clipboard", new[] { "guide.page.clipboard.1", "guide.page.clipboard.2", "guide.page.clipboard.3" }),
        ("trending", "guide.page.trending", new[] { "guide.page.trending.1", "guide.page.trending.2" }),
        ("rss", "guide.page.rss", new[] { "guide.page.rss.1", "guide.page.rss.2" }),
        (SettingsTag, "guide.page.settings", new[] { "guide.page.settings.1", "guide.page.settings.2", "guide.page.settings.3" }),
    };

    /// <summary>每页说明（<b>交键不交词</b>）。顺序即设置页「使用说明」里的呈现顺序。</summary>
    public static IReadOnlyList<PageGuide> InOrder() =>
        Entries.Select(e => new PageGuide(e.Tag, e.TitleKey, e.LineKeys)).ToList();

    /// <summary>按页面 tag 取那一页的说明；没有对应说明时返回 null（调用方要说"这一页还没有说明"，不许静默给一个空面板）。</summary>
    public static PageGuide? For(string? tag)
        => tag is null ? null : ByTag.TryGetValue(tag, out var guide) ? guide : null;

    /// <summary>
    /// 「？」浮层与设置页那一栏要说的这一页。<b>取不到时返回一句实话，不返回空</b>：
    /// 空面板的表现是"这颗按钮坏了"，而"这一页还没有说明"至少让用户知道接下来该看哪儿。
    /// 兜底条目<b>不进 <see cref="Tags"/></b>——否则"每页都有说明"那条闸门会被自己骗过去。
    /// </summary>
    public static PageGuide ForOrSaySo(string? tag)
        => For(tag) ?? new PageGuide(tag ?? "—", "guide.missing.title", new[] { "guide.missing.line" });

    private static readonly Dictionary<string, PageGuide> ByTag = ToMap();

    private static Dictionary<string, PageGuide> ToMap()
    {
        var map = new Dictionary<string, PageGuide>(System.StringComparer.Ordinal);
        foreach (var guide in InOrder())
        {
            // 同一个 tag 写两遍＝后面那条悄悄顶掉前面那条（表现是"改了说明没生效"），当场炸比静默好。
            map.Add(guide.Tag, guide);
        }
        return map;
    }

    /// <summary>全部页面 tag（闸门拿它对着「能导航到哪些页」那张表比：多一页少一页都要红）。</summary>
    public static IEnumerable<string> Tags => ByTag.Keys;
}

/// <summary>
/// 首启那一份引导（批次 VC-3 起是文字，批次 WJ-1 起是<b>分步向导</b>的内容源；WI-2q 起字面进语言表）。
/// <para><b>只说"从哪进去"，不教操作细节</b>——细节在每页的「？」与设置页「使用说明」栏里，
/// 把它们全塞进首启那一屏等于没人读。</para>
/// <para><b>标题里不写步数</b>："先知道这四件事"那种句子会把步数变成第二个出处：加一步就得记得改标题，
/// 忘了就是当着用户说假话。步数由向导从 <see cref="Steps"/> 现数（坑表 #272 那条口径的自我兑现）。</para>
/// </summary>
public static class FirstRunGuide
{
    /// <summary>向导标题的那颗键（<b>交键不交词</b>，理由同 <see cref="PageGuides.InOrder"/>）。</summary>
    public const string TitleKey = "guide.first.title";

    /// <summary>向导每一步的键表（标题键＝<c>guide.first.s&lt;n&gt;.title</c>，正文键＝<c>guide.first.s&lt;n&gt;.&lt;m&gt;</c>）。</summary>
    private static readonly (string TitleKey, string[] LineKeys, string? JumpTag, string Art)[] StepKeys =
    {
        ("guide.first.s1.title", new[] { "guide.first.s1.1", "guide.first.s1.2" }, null, GuideArt.Window),
        ("guide.first.s2.title", new[] { "guide.first.s2.1", "guide.first.s2.2", "guide.first.s2.3" }, PageGuides.SearchTag, GuideArt.Search),
        ("guide.first.s3.title", new[] { "guide.first.s3.1", "guide.first.s3.2", "guide.first.s3.3" }, "tree", GuideArt.Pages),
        ("guide.first.s4.title", new[] { "guide.first.s4.1", "guide.first.s4.2", "guide.first.s4.3" }, "snapshot", GuideArt.Widgets),
        ("guide.first.s5.title", new[] { "guide.first.s5.1", "guide.first.s5.2", "guide.first.s5.3" }, PageGuides.SettingsTag, GuideArt.Capture),
        ("guide.first.s6.title", new[] { "guide.first.s6.1", "guide.first.s6.2" }, PageGuides.SettingsTag, GuideArt.Keys),
    };

    /// <summary>向导的步子（<b>交的是键</b>：取词发生在贴上去那一刻，⛔ 不在这里）。
    /// 顺序即呈现顺序；<c>JumpTag</c> 是"带我去这一页"要落到的页（null＝这一步没有页可去）。</summary>
    public static IReadOnlyList<GuideSection> Steps() =>
        StepKeys.Select(s => new GuideSection(s.TitleKey, s.LineKeys, s.JumpTag, s.Art)).ToList();

    /// <summary>
    /// 引导末尾那一行"这几件事此刻的按键是什么"要列的动作，<b>只给动作名</b>：
    /// 按键文本由 UI 从绑定表现场渲染（用户改过键之后引导必须跟着改，写死就是假话）。
    /// </summary>
    public static IReadOnlyList<string> HotkeyActionsToShow { get; } = new[]
    {
        HotkeyActions.MainToggle,
        HotkeyActions.ScreenCapture,
        HotkeyActions.ScreenPin,
        HotkeyActions.ScreenPinToggleHidden,
        HotkeyActions.ScreenPinClickThrough,
        HotkeyActions.CanvasToggle,
    };
}

/// <summary>
/// 首启向导能引用的<b>示意图名字</b>（批次 WJ-1）。
/// <para><b>这里只有名字，没有几何</b>：形状住在 UI 那一侧（<c>GuideArtView</c>，与工具条图标同一族写法），
/// 因为画出来要依赖 XAML 图元。Core 留一张名字表，是为了让闸门能在<b>两侧</b>对账：
/// 某一步引用了一张没画过的图 ⇒ UI 那里少一个 case，当场红；画了一张没人引用的图 ⇒ 另一侧红。
/// "少一张图"在屏幕上的表现是一片空白，而这片空白不会有任何地方报错。</para>
/// <para>⛔ 图内一个汉字都不放（用户裁决）：放了字，图就得跟着界面语言走，
/// 而且 UI 一改字这张图立刻过期——那正是"假截图当引导"的坑。</para>
/// </summary>
public static class GuideArt
{
    public const string Window = "window";
    public const string Search = "search";
    public const string Pages = "pages";
    public const string Widgets = "widgets";
    public const string Capture = "capture";
    public const string Keys = "keys";

    /// <summary>全部画得出来的示意图名字（UI 的绘制器必须逐个有对应形状）。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Window, Search, Pages, Widgets, Capture, Keys };

    public static bool IsReal(string? name) => name is not null && All.Contains(name, StringComparer.Ordinal);
}
