#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 英文界面上「不该再出现汉字」那一族的接线守门（批次 WI-67 片1，发起人 2026-10-13 逐行报障）。
/// <para>他一次报来十几句，现场分三类：一类<b>早就在册</b>（P-188 的 UI 两档、P-193 的 Core/Abstractions 整层、
/// P-192 的 Integrations），一类<b>刻意保留</b>（下拉闭着那一格抄的是 <c>Content</c> 的快照，只挂静态键会修成
/// "列表翻了闭着那格不翻"＝本批⛔ 动它，见 <c>I18nWiringGateTests</c> 那份豁免名单），
/// 还有一类<b>从没进过任何一份名单</b>——<c>Controls/</c> 目录与画布那块快捷键面板都在历次"按层扫"的射程之外，
/// 所以"还剩多少"这句话在它们身上一直⛔ 有读数。本批把第三类一次清掉，并把它们钉进闸门：
/// ⛔ 只钉"他点到的那一行"，而是<b>整档</b>不许再有汉字字面（下一颗漏网的自己红）。</para>
/// <para>禁项一律读<b>抹掉注释后的代码</b>（<see cref="SourceGate.Code"/>，#195/#200/#201 那一族）：
/// 注释里引用一句旧措辞是为了讲清为什么，不是又开一个出口。</para>
/// </summary>
[Collection("LocaleGlobal")]      // 批次 WI-67 片4 起本档有一格会当场换语言（三语各渲染一遍），⛔ 与别的换语言的证人交错跑
public sealed class EnglishLeftoverGateTests : IDisposable
{
    private readonly AppLanguage _languageBefore = Locale.Current;

    /// <summary>跑完把语言<b>还回去</b>：全工程逐档断言中文的证人不少（<c>TrendingCardPolicyTests</c> 那颗副标题），
    /// 而 <c>DisableTestParallelization</c> 让测试是<b>顺序</b>跑的——上一档留在哪一门，下一档就读哪一门。
    /// <para>⚠ 这一条不是修饰：<c>bench_67d_r1</c> 的基线第一跑就是被我自己这颗没还原的格打红的
    /// （两颗红：那颗串行集合闸门＋热榜副标题），当场证明"换了不还"会污染别人（名册 241 的姊妹面）。</para></summary>
    public void Dispose() => Locale.TrySet(_languageBefore);

    private const string TagEditor = "src/StarMark.UI/Controls/TagEditor.xaml";
    private const string PreviewHostXaml = "src/StarMark.UI/Controls/PreviewHost.xaml";
    private const string PreviewHostCs = "src/StarMark.UI/Controls/PreviewHost.xaml.cs";
    private const string HotkeyPanelXaml = "src/StarMark.UI/Views/CanvasHotkeyPanelWindow.xaml";
    private const string HotkeyPanelCs = "src/StarMark.UI/Views/CanvasHotkeyPanelWindow.xaml.cs";
    private const string FolderTree = "src/StarMark.UI/Views/FolderTreePage.xaml.cs";
    private const string RssPage = "src/StarMark.UI/Views/RssPage.xaml.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string ItemGridVm = "src/StarMark.UI/ViewModels/ItemGridWidgetViewModel.cs";
    private const string ItemGridCs = "src/StarMark.UI/Views/ItemGridWidget.xaml.cs";
    private const string CardPolicy = "src/StarMark.Abstractions/ItemCardPolicy.cs";
    private const string CardVm = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";
    private const string LayoutCore = "src/StarMark.Core/Widgets/WidgetLayout.cs";
    private const string SnapshotCore = "src/StarMark.Core/Widgets/WidgetSnapshot.cs";
    private const string NameOwner = "src/StarMark.Core/Widgets/WidgetDisplayName.cs";
    private const string WidgetMenuCs = "src/StarMark.UI/Views/WidgetWindow.Menu.cs";
    private const string SettingsWidgetsCs = "src/StarMark.UI/Views/SettingsPage.Widgets.cs";
    private const string SnapshotPageCs = "src/StarMark.UI/Views/SnapshotPage.xaml.cs";

    /// <summary>本批这几片改交键的那 42 颗（片1 十二颗＋片3 九颗＋片4 十四颗＋片5 四颗＋片6 三颗；
    /// 颗数＝<c>bin/WI67/keys_67d.txt</c> 那把差集尺对过，名单长度＝同一颗尺逐颗数出来的数组元素数）。
    /// 一颗都得有人在表里问，⛔ 留一颗没人念的死键进表。</summary>
    private static readonly string[] Keys =
    {
        "tag.editor.placeholder", "tag.editor.removeTip", "tag.editor.suggestTitle", "tag.editor.addTip",
        "preview.open.browser", "preview.open.local",
        "canvas.hotkey.title", "canvas.hotkey.closeTip", "canvas.hotkey.dragTip",
        "canvas.hotkey.mouseExits", "canvas.hotkey.whereToChange",
        "common.expandMore",
        // 批次 WI-67 片3（条目格那一扇）：clip 那两颗是「页与格共用一句」，两颗 grid 空态＋配置栏两句是本批新落。
        "clip.empty.off", "clip.empty.none", "clip.empty.noWindow", "clip.empty.pausedCell",
        "widget.grid.emptyActivity", "widget.grid.emptyNone", "widget.grid.needConfig",
        "widget.grid.pinHint", "widget.grid.placeholder",
        // 批次 WI-67 片4（卡片上那两颗动作按钮：他报的「收进收藏」「收藏到文件夹」正是这里的两颗 label）。
        "card.star.label.starred", "card.star.label.unstarred", "card.star.tip",
        "card.star.tip.starred", "card.star.tip.unstarred", "card.star.tip.tokenNote",
        "card.collect.label.collected", "card.collect.label.uncollected",
        "card.collect.tip.collected", "card.collect.tip.uncollected",
        "card.rssCollect.label.collected", "card.rssCollect.label.uncollected",
        "card.rssCollect.tip.collected", "card.rssCollect.tip.uncollected",
        // 批次 WI-67 片5（布局／快照那两颗「每次现算的摘要」交表：问的人在 Core，⛔ 在 Core 里拼成品串）。
        "widget.layout.summaryEmpty", "widget.layout.summaryCount",
        "widget.snapshot.summaryEmpty", "widget.snapshot.summaryBody",
        // 批次 WI-67 片6（✅P-200 他裁 (A)：两颗兜底名＋同名序号——库里只存中性形状，词只在贴上屏幕那一刻念）。
        "widget.name.layoutFallback", "widget.name.snapshotFallback", "widget.name.dedupSuffix",
    };

    private static readonly Regex CjkInAttribute = new("(?<=[\"'])[^\"']*?[㐀-鿿]", RegexOptions.CultureInvariant);

    /// <summary>
    /// 那三档 XAML <b>整档</b>不许再有一个汉字属性值（他点到的只是他看见的那一颗，⛔ 把它当射程）。
    /// <para>反空转：先证明扫描器在那几档里<b>确实读到了属性</b>——读不到东西的判据永远绿，那是最坏的一种绿。</para>
    /// </summary>
    [Fact]
    public void ThoseThreeMarkupFilesHoldNoChineseLiteralAtAll()
    {
        foreach (var path in new[] { TagEditor, PreviewHostXaml, HotkeyPanelXaml })
        {
            var text = XmlComments(ReadRepoFile(path));
            var attrs = Regex.Matches(text, @"\w+=""(?:\{\{[^""]*\}\}|[^""])*""").Cast<Match>()
                .Where(m => !m.Value.Contains("x:Name") && !m.Value.Contains("i18n:LocText."))
                .ToList();
            Assert.True(attrs.Count >= 8, $"{path} 只读到 {attrs.Count} 颗属性值——扫描器没跑起来，这一格不作数");
            var hit = attrs.FirstOrDefault(m => CjkInAttribute.IsMatch(m.Value));
            Assert.True(hit is null, $"{path} 里还留着一句汉字字面：{hit?.Value}");
        }
    }

    /// <summary>表里每颗都要<b>四列齐全</b>，且每颗都要<b>有人在表外问它</b>（死键进表＝下一批以为已经翻好了）。
    /// <para>⚠ 语料<b>必须带上 .xaml</b>：<see cref="ReadRepoUnder"/> 只给 <c>.cs</c>，而这一族里过半的键是
    /// 挂在标记上的（<c>PlaceholderKey</c>／<c>ToolTipKey</c>／<c>Key</c>）——只扫 .cs 会把它们一律判成死键
    /// （本批第一跑就是这么红的，红在闸门自己身上而⛔ 红在屏幕上）。</para></summary>
    [Fact]
    public void EveryNewKeyIsInTheTableAndAskedBySomeone()
    {
        var table = ReadRepoFile(Table);
        // 长句那一族的 Entry 是<b>跨行排</b>的（zh／en／ja 各一行），所以"四列齐不齐"要在一份折平副本上问——
        // ⛔ 拿整档原文做逐行正则，那会把每一颗写得长的键都误判成"没填齐"。
        // ⚠ 每一列都得按<b>C# 那颗字符串字面量自己的语法</b>读（\" 是内容而⛔ 结尾）：WI-67 片3 第一跑红了两颗
        //   写得没问题的键（en 那句里引了一颗按钮的名字），因为旧的那串把引号一律当终止符＝**假红**。
        var flat = Regex.Replace(table, @"\s+", " ");
        const string col = @"""(?:\\.|[^""\\])+(?:\\.|[^""\\])*""";   // 一颗<b>非空</b>的 C# 字符串字面量（含 \" 这类转义）
        var asked = string.Concat(ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => Code(f.Text)));
        var markup = string.Concat(MarkupFiles().Select(f => XmlComments(f.Text)));
        Assert.True(markup.Length > 100_000, $"标记语料只有 {markup.Length} 字——这一格不作数");
        foreach (var key in Keys)
        {
            Assert.Equal(1, Count(table, $"\"{key}\", \""));
            Assert.True(Regex.IsMatch(flat, $"\"{Regex.Escape(key)}\", " + col + ", " + col + ", " + col),
                $"{key} 有四列没填齐（Entry 四参必填）");
            Assert.True(Count(asked, $"\"{key}\"") + Count(markup, $"\"{key}\"") >= 1, $"{key} 在表外没有一处被问起（死键）");
        }
    }

    /// <summary>
    /// 「展开更多（剩余 N 条）」这一句今天起<b>只有一个主人</b>：文件夹树与 RSS 那两页都问同一颗键，
    /// 字面量只在表里活着一份（这一句本批之前在那两个宿主里各写一遍，逐字相同＝随时会分岔）。
    /// </summary>
    [Fact]
    public void TheExpandMoreSentenceHasExactlyOneOwner()
    {
        Assert.Equal(1, Count(Code(ReadRepoFile(Table)), "展开更多"));
        foreach (var path in new[] { FolderTree, RssPage })
            Assert.Equal(1, Count(Code(ReadRepoFile(path)), "\"common.expandMore\""));
        Assert.DoesNotContain("展开更多", Code(ReadRepoFile(FolderTree)), StringComparison.Ordinal);
        Assert.DoesNotContain("展开更多", Code(ReadRepoFile(RssPage)), StringComparison.Ordinal);
    }

    /// <summary>
    /// 抬头与"去哪改键"那两句里嵌着<b>「屏幕画布」那颗名字</b>，而那颗名字自己也是颗键（坑表 #393／P-196 那一族）：
    /// <c>{0}</c> 槽交出去的必须是<b>还没取词的 LocMessage</b>，⛔ 是先 <c>Locale.T</c> 念好的字符串——
    /// 后者会让"外层重贴了、里面那半句还停在旧语言"，正是 P-194 一路在追的那个形状。
    /// </summary>
    [Fact]
    public void TheHotkeyPanelHandsItsNestedNameUnrendered()
    {
        var ctor = Code(ReadRepoFile(HotkeyPanelCs));
        foreach (var key in new[] { "canvas.hotkey.title", "canvas.hotkey.whereToChange" })
            Assert.Contains($"LocMessage.Of(\"{key}\", LocMessage.Of(\"settings.extend.canvasTitle\"))", ctor,
                StringComparison.Ordinal);
        Assert.DoesNotContain("屏幕画布", ctor, StringComparison.Ordinal);   // 那颗名字⛔ 在这档代码里再抄一份
        Assert.Contains("LocText.SetMessage(PanelTitle", ctor, StringComparison.Ordinal);
        Assert.Contains("LocText.SetMessage(WhereToChange", ctor, StringComparison.Ordinal);
    }

    /// <summary>预览那颗主按钮：调用方给的那句与省略时自动选的那两颗，交的都是「键」（⛔ string 走另一条路）。</summary>
    [Fact]
    public void ThePreviewOpenButtonTakesKeysNotWords()
    {
        var code = Code(ReadRepoFile(PreviewHostCs));
        Assert.DoesNotContain("用默认程序打开", code, StringComparison.Ordinal);
        Assert.DoesNotContain("在浏览器中打开", code, StringComparison.Ordinal);
        Assert.Contains("LocMessage? openText", code, StringComparison.Ordinal);
        Assert.Equal(6, Count(code, "LocMessage.Of(\"preview.open.local\")"));   // 六个调用点逐颗交键，⛔ 一处漏
        Assert.Equal(2, Count(code, "preview.open.browser"));
    }

    /// <summary>
    /// 条目格那六臂空态与配置栏两句：VM 交的是<b>键＋实参</b>，宿主走重贴名单（批次 WI-67 片3）。
    /// <para>他点到的两颗（「暂无条目」与「输入标签名…」）本批是<b>删掉标记里那份字面量</b>而⛔ 挂静态键——
    /// 两处都由 code-behind 每次重写，挂键的后果是换语言那一刻把兜底那句盖回带状态的那句（坑表 #276／#295 那一族）。</para>
    /// </summary>
    [Fact]
    public void TheItemGridCellHandsItsEmptyStateUnrendered()
    {
        var vm = Code(ReadRepoFile(ItemGridVm));
        var host = Code(ReadRepoFile(ItemGridCs));
        Assert.Contains("public LocMessage EmptyState => Mode switch", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("EmptyStateText", vm, StringComparison.Ordinal);   // 那颗 string 出口⛔ 回来（它＝第二个主人）
        foreach (var key in new[] { "clip.empty.off", "clip.empty.noWindow", "clip.empty.pausedCell", "clip.empty.none",
                                    "widget.grid.emptyActivity", "widget.grid.needConfig", "widget.grid.emptyNone" })
            Assert.Contains($"LocMessage.Of(\"{key}\")", vm, StringComparison.Ordinal);
        // 取词只许发生在"贴上去那一刻"：VM 里念一次就把语言冻在了"这一格第一次空着"的那一刻。
        Assert.DoesNotContain(".Render()", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.T(", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.Fmt(", vm, StringComparison.Ordinal);
        Assert.Contains("LocText.SetMessage(EmptyHint, ViewModel.EmptyState);", host, StringComparison.Ordinal);
        Assert.Contains("LocText.SetMessage(ConfigHint, LocMessage.Of(\"widget.grid.pinHint\", LocMessage.Of(\"widget.grid.apply\")));",
            host, StringComparison.Ordinal);
        Assert.Contains("LocText.SetPlaceholderKey(ConfigBox, \"widget.grid.placeholder\");", host, StringComparison.Ordinal);
        // 代码再写那颗属性＝两个主人（那颗通用证人的射程要 x:Name＋挂键，这两颗没挂键，所以在这格钉）。
        foreach (var banned in new[] { "EmptyHint.Text =", "ConfigHint.Text =", "ConfigBox.PlaceholderText =" })
            Assert.DoesNotContain(banned, host, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「剪贴板历史没开启」那一句：页与格<b>共用一颗键</b>，而表里那句指的路必须是<b>今天真有的那一栏</b>。
    /// <para>本批之前表里写着「设置 → 剪贴板历史」（WC-2 分类重排之后已不存在那一栏），而组件里另抄了一份
    /// 写着「设置 → 数据 → …」——<b>一句两个主人，其中一个是假话</b>：照着它去点，那里没有那颗开关。</para>
    /// <para>⚠ 下面那两串"表外 0 处"是<b>从改前那一侧现读出来的</b>（`bin/WI67/literal_67c.txt`：改前各 1 处 ↔ 现在 0 处）。
    /// 第一版我写的是「剪贴板历史未开启」——那是<b>表里</b>那句的开头，表外从来没有过它，于是那颗断言改前改后都读 0＝永远绿＝装饰品
    /// （坑表 #515：一句"0 处"只有在一边读到过 ≥1 时才在防事）。</para>
    /// </summary>
    [Fact]
    public void TheClipboardOffSentenceHasOneOwnerAndTheCurrentPath()
    {
        var flat = Regex.Replace(ReadRepoFile(Table), @"\s+", " ");
        var row = Regex.Match(flat, @"new Entry\(""clip\.empty\.off"", ""([^""]*)""");
        Assert.True(row.Success, "表里读不到 clip.empty.off 那一行（那颗键被改名或删了？）");
        Assert.Contains("设置 → 数据 → 剪贴板历史", row.Groups[1].Value, StringComparison.Ordinal);
        var code = string.Concat(ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => Code(f.Text)));
        Assert.Equal(0, Count(code, "剪贴板历史还没开启"));                        // 表外那份抄写的开头（改前现读 1 处）
        Assert.Equal(0, Count(code, "到「设置 → 数据 → 剪贴板历史」打开后"));      // 路径⛔ 在表外再活一份（改前现读 1 处）
        Assert.Equal(2, Count(code, "\"clip.empty.off\""));                      // 两个宿主各问一次：「剪贴板」页与组件那一格
    }

    /// <summary>
    /// 卡片上那两颗动作按钮的字（发起人逐行报障里的<b>「收进收藏」与「收藏到文件夹」</b>）从此只住表里一份（批次 WI-67 片4）。
    /// <para>⚠ 下面那十三串"表外 0 处"是<b>从改前那一侧现读出来的</b>（<c>bin/WI67/literal_67d.txt</c>：十二串在
    /// <c>ItemCardPolicy.cs</c> 里各读 1 处，<b>剩下那一串读 2 处</b>＝成对的那两句共用了同一半句，合计 14 处；
    /// 别处的下落只有两类：<c>RssPageViewModel.cs</c> 里那颗日志行 1 处（刻意保留）、两档 <c>.xaml</c> 的<b>注释</b>里 3 处）。
    /// 这一族本批之前<b>压根没进过表</b>——
    /// 英文界面上它们就是一行汉字，而 5795 格里没有一格看得见（名册 245：一颗"0 处"只有在一边读到过 ≥1 时才在防事）。</para>
    /// <para>另外那两串（「收藏到文件夹」「收进收藏」）问的是<b>整片 src</b>：他点到的那两句⛔ 在别的宿主里再抄一份。</para>
    /// </summary>
    [Fact]
    public void TheCardActionWordsHaveOneHomeEach()
    {
        var policy = Code(ReadRepoFile(CardPolicy));
        foreach (var words in new[]
        {
            "收藏到文件夹", "已收藏", "移出收藏", "收进收藏", "已 Star",
            "作为书签存进本机，并放进", "要移除请到资料库对应那一行", "第一次收藏这个源时会建出这一层文件夹",
            "不会动 GitHub 的 Star 状态", "已在你的 Star 列表中，点击取消 Star", "Star 这个仓库（需 GitHub Token）",
            "当前未配置 Token，点击会提示失败", "判定取自本机已同步的 Star 列表",
        })
            Assert.Equal(0, Count(policy, words));

        var code = string.Concat(ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => Code(f.Text)));
        Assert.Equal(0, Count(code, "收藏到文件夹"));
        Assert.Equal(0, Count(code, "收进收藏"));

        // 十四颗键各有且只有一处出口（工厂那一颗）：宿主⛔ 自己再点一次键名＝第二个主人。
        foreach (var key in Keys.Where(k => k.StartsWith("card.", StringComparison.Ordinal)))
            Assert.Equal(1, Count(code, $"\"{key}\""));

        // 工厂交的是「键＋实参」；那颗套娃的 {0} 槽交的是<b>还没取词的</b> LocMessage（⛔ 先念好再拼）。
        Assert.Equal(8, Count(policy, "public static LocMessage "));
        Assert.Contains("LocMessage.Of(starred ? \"card.star.tip.starred\" : \"card.star.tip.unstarred\")",
            policy, StringComparison.Ordinal);
        Assert.Contains("hasToken ? null : LocMessage.Of(\"card.star.tip.tokenNote\")", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.T(", policy, StringComparison.Ordinal);      // 句厂⛔ 自己取词＝语言冻在算出它那一刻
        Assert.DoesNotContain("Locale.Fmt(", policy, StringComparison.Ordinal);

        // 卡片 VM 那四颗是"当下就要一个字串"的薄包装（与 OpenMenuText 同一口径）：逐字钉住，⛔ 谁把它们换回字面量。
        var vm = Code(ReadRepoFile(CardVm));
        foreach (var line in new[]
        {
            "public string StarLabel => ItemCardPolicy.StarLabel(_isStarred).Render();",
            "public string StarTip => ItemCardPolicy.StarTip(_isStarred, _hasToken).Render();",
            "public string CollectLabel => ItemCardPolicy.CollectLabelFor(IsRssCandidate, _isCollected).Render();",
            "public string CollectTip => ItemCardPolicy.CollectTipFor(IsRssCandidate, _isCollected, RssFolderPath).Render();",
        })
            Assert.Contains(line, vm, StringComparison.Ordinal);
    }

    /// <summary>
    /// 两颗兜底名「未命名布局／未命名快照」从此只住表里一份（批次 WI-67 片6，✅P-200 他裁 (A)）。
    /// <para>这一族的形状与片4 那八颗⛔ 同：问题⛔ 是"屏幕上少一行字"，而是<b>那一颗字会落进 widgets.json</b>——
    /// 存进档＝把"建出来那一刻"的界面语言冻死（换语言之后它永不改口＝P-199 那条界线）。库里因此改存<b>中性形状</b>
    /// （没起名就是空串），词与 (2) 那颗序号都在贴上屏幕那一刻现算，唯一主人＝<c>WidgetDisplayName</c>。</para>
    /// <para>⚠ 那"改前 4 处写进库／7 处念上屏"是<b>从改前那一侧现读出来的</b>（<c>bin/WI67/recon_67e_names.txt</c>：
    /// 写进库 4 处＝Core 两档的 <c>Normalize</c> 各 1 ＋ <c>MakeUniqueName</c> 各 1；念上屏 7 处＝设置页 1／快照页 3／组件右键 3）。
    /// 名册 245 那条口径：一句"0 处"只有在一边读到过 ≥1 时才在防事。</para>
    /// <para>⚠ 而那句"7 处念上屏"里有一颗<b>分堆分错了</b>（坑表 #525）：快照页第三处＝「提取为布局」输入框的默认字，
    /// 它看屏幕上<b>出去</b>的值会被他一字不改地点确定<b>写回来</b>。这一族真正的判据是<b>方向</b>而⛔"有没有出现过"，
    /// 所以本档把那颗单独计数（<c>Neutralize(</c>＝1），⛔ 让它混进 <c>.Word(</c> 的 3·1·2 里。</para>
    /// </summary>
    [Fact]
    public void TheFallbackNamesHaveOneHomeEach()
    {
        var table = Code(ReadRepoFile(Table));
        Assert.Equal(1, Count(table, "未命名布局"));
        Assert.Equal(1, Count(table, "未命名快照"));

        var code = string.Concat(ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => Code(f.Text)));
        Assert.Equal(0, Count(code, "未命名布局"));
        Assert.Equal(0, Count(code, "未命名快照"));

        // 三颗键名一处主人：只在 WidgetDisplayName 里各声明一次；Core 那两档一颗都不问（问＝绕过主人自己取词）。
        var owner = Code(ReadRepoFile(NameOwner));
        foreach (var key in new[] { "widget.name.layoutFallback", "widget.name.snapshotFallback", "widget.name.dedupSuffix" })
            Assert.Equal(1, Count(owner, $"\"{key}\""));
        foreach (var file in new[] { LayoutCore, SnapshotCore })
        {
            var text = Code(ReadRepoFile(file));
            Assert.Equal(0, Count(text, "widget.name."));
            Assert.Contains("WidgetDisplayName", text, StringComparison.Ordinal);
        }

        // 取词必须发生在"贴上屏幕那一刻"：三位宿主各自交出去的实参前缀都是 <c>….Word(</c>（⛔ 直接交 l.Name／s.Name）。
        // 颗数＝改前那 6 处<b>上屏</b>消费点（组件右键 3＝应用布局子菜单＋应用快照子菜单＋保存回执；设置页 1；快照页 2＝行/预览）。
        Assert.Equal(3, Count(Code(ReadRepoFile(WidgetMenuCs)), ".Word("));
        Assert.Equal(1, Count(Code(ReadRepoFile(SettingsWidgetsCs)), ".Word("));
        // ⚠ 快照页那 2 处⛔ 再多一颗：「提取为布局」的<b>输入框预填</b>看着也像"上屏"，但它交出去的值会被他
        // 一字不改地点确定<b>写回库里</b>——那一颗必须走 <c>Neutralize</c>（中性形状），走 <c>.Word(</c> 就是把兜底词冻进档＝(A) 的侧门。
        var snapshotPage = Code(ReadRepoFile(SnapshotPageCs));
        Assert.Equal(2, Count(snapshotPage, ".Word("));
        Assert.Equal(1, Count(snapshotPage, "WidgetDisplayName.Neutralize("));
    }

    /// <summary>
    /// 那十四颗新键<b>真跑得念一遍</b>（三语各一次）：非空、槽全填满、两颗状态 label 在<b>每一门语言里都互不相同</b>。
    /// <para>形状尺只能说"工厂交出的是键"，说不了"那句在屏幕上念得出来"——槽没填满、ja 列漏一格、
    /// 或者"已 Star／Star"在某一档语言里念成同一句（点了看不出翻转），都由这一格先红。</para>
    /// </summary>
    [Fact]
    public void TheCardActionWordsRenderInAllThreeLanguages()
    {
        const string folder = "RSS订阅 / 开源中国";      // 那颗<b>实参</b>是数据（路径），⛔ 表里的措辞
        foreach (var lang in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(lang);
            Assert.Equal(lang, Locale.Current);                       // TrySet 回的是"变没变"，⛔"换没换到"（规则 #112）

            var starred = Locale.Fmt("card.star.label.starred");
            var unstarred = Locale.Fmt("card.star.label.unstarred");
            Assert.True(starred.Length > 0 && unstarred.Length > 0, $"{lang} 那两颗 Star 字是空的");
            Assert.NotEqual(starred, unstarred);                      // 点了必须看得出翻转

            var collected = Locale.Fmt("card.collect.label.collected");
            var uncollected = Locale.Fmt("card.collect.label.uncollected");
            Assert.NotEqual(collected, uncollected);
            var rssOn = Locale.Fmt("card.rssCollect.label.collected");
            var rssOff = Locale.Fmt("card.rssCollect.label.uncollected");
            Assert.NotEqual(rssOn, rssOff);

            foreach (var key in new[] { "card.collect.tip.collected", "card.collect.tip.uncollected" })
                Assert.True(Locale.T(key).Length > 20, $"{key} 在 {lang} 太短＝那一列没填");

            foreach (var key in new[] { "card.rssCollect.tip.collected", "card.rssCollect.tip.uncollected" })
                Assert.Contains(folder, Locale.Fmt(key, folder));     // {0} 真被填上了（⛔ 屏幕上留一个 {0}）
            var token = Locale.T("card.star.tip.tokenNote");
            Assert.Contains(token, Locale.Fmt("card.star.tip", unstarred, token));
            Assert.DoesNotContain(token, Locale.Fmt("card.star.tip", unstarred, (object?)null));   // "没话要说"不许留下半个分隔符
        }
        // ⚠「Star」那颗中英同形是<b>事实</b>（这个专有名词在英文界面里本来就写作 Star）＝不是没翻（与 ⑨ 那格同一口径）；
        // 而它⛔ 与日文同形——那一列写的是「スター」。
        Assert.True(UiStrings.TryGet("card.star.label.unstarred", out var sameName), "表里没有 card.star.label.unstarred");
        Assert.Equal(sameName.Zh, sameName.En);
        Assert.NotEqual(sameName.Zh, sameName.Ja);
    }

    /// <summary>整个 src 下的 <c>.xaml</c>（<see cref="ReadRepoUnder"/> 只 glob <c>.cs</c>，标记档⛔ 在它射程里）。</summary>
    private static IReadOnlyList<(string RelativePath, string Text)> MarkupFiles()
    {
        var root = RepoRoot().Replace(Path.DirectorySeparatorChar, '/').TrimEnd('/');
        var list = new List<(string, string)>();
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.xaml", SearchOption.AllDirectories))
        {
            var normalized = file.Replace(Path.DirectorySeparatorChar, '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/")) continue;
            list.Add((normalized[(root.Length + 1)..], File.ReadAllText(file)));
        }
        Assert.True(list.Count >= 30, $"只扫到 {list.Count} 颗 xaml（扫描路径错了，这一格不作数）");
        return list;
    }

    private static string XmlComments(string text)
        => Regex.Replace(Regex.Replace(text, "<!--.*?-->", " ", RegexOptions.Singleline), @"^\s*//.*$", "",
            RegexOptions.Multiline);
}
