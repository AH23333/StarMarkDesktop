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

    /// <summary>本批改交键的那十三颗（一颗都得有人在表里问，⛔ 留一颗没人念的死键进表）。</summary>
    private static readonly string[] Keys =
    {
        "tag.editor.placeholder", "tag.editor.removeTip", "tag.editor.suggestTitle", "tag.editor.addTip",
        "preview.open.browser", "preview.open.local",
        "canvas.hotkey.title", "canvas.hotkey.closeTip", "canvas.hotkey.dragTip",
        "canvas.hotkey.mouseExits", "canvas.hotkey.whereToChange",
        "common.expandMore",
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
        var flat = Regex.Replace(table, @"\s+", " ");
        var asked = string.Concat(ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => Code(f.Text)));
        var markup = string.Concat(MarkupFiles().Select(f => XmlComments(f.Text)));
        Assert.True(markup.Length > 100_000, $"标记语料只有 {markup.Length} 字——这一格不作数");
        foreach (var key in Keys)
        {
            Assert.Equal(1, Count(table, $"\"{key}\", \""));
            Assert.True(Regex.IsMatch(flat, $"\"{Regex.Escape(key)}\", \"[^\"]+\", \"[^\"]+\", \"[^\"]+\""),
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
