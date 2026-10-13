#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
public sealed class EnglishLeftoverGateTests
{
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

    /// <summary>本批这两片改交键的那 21 颗（片1 十二颗＋片3 九颗；颗数＝`bin/WI67/keys_67c.txt` 那把差集尺对过）。一颗都得有人在表里问，⛔ 留一颗没人念的死键进表。</summary>
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
