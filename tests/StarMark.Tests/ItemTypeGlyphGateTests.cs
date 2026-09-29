#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using StarMark.Abstractions;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 条目类型图标（批次 RX，P-123 第一条）：一条映射、三处宿主、外加主窗分段控件那一颗"第四个出处"。
/// <para>
/// 这批要防的分岔<b>已经发生过两次</b>，不是假想：① <c>ItemType.File</c> 在库管理卡片是 📄、
/// 在两张组件的图标表是 📁，同一类条目在两个界面读成两种东西；② 剪贴板条目立项那次只补了卡片那张表，
/// 两张组件表把它渲染成了兜底图标 📌——于是"没打过标记的条目"在界面上看起来像<b>被置顶了</b>
/// （报告 §IG 记的就是这一次）。图标表长在三处，加一类就得改三处，漏一处不会变红。
/// </para>
/// <para>
/// 判据本身在 <see cref="ItemCardPolicy.GlyphOf"/>（Abstractions，测试工程引用得到 ⇒ 这里是真行为测，
/// 不是形状闸门）；形状闸门只负责"不许再长出第二份表"和"分段控件那三颗别自己漂走"。
/// </para>
/// </summary>
public sealed class ItemTypeGlyphGateTests
{
    private const string PolicyFile = "src/StarMark.Abstractions/ItemCardPolicy.cs";
    private const string CardVm = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";
    private const string GridVm = "src/StarMark.UI/ViewModels/ItemGridWidgetViewModel.cs";
    private const string SearchVm = "src/StarMark.UI/ViewModels/SearchWidgetViewModel.cs";
    private const string MainWindowXaml = "src/StarMark.UI/MainWindow.xaml";

    /// <summary>本应用里所有"条目类型图标"的取值集合——从判据本身取，避免闸门写出第二份真值。</summary>
    private static IReadOnlyCollection<string> TypeGlyphs()
        => Enum.GetValues<ItemType>().Select(ItemCardPolicy.GlyphOf).ToList();

    // ────────── ① 判据本身：每个类型都有、互不相同、兜底不许长得像状态 ──────────

    [Theory]
    [InlineData(ItemType.GitHubStar, "⭐")]
    [InlineData(ItemType.Bookmark, "🔖")]
    [InlineData(ItemType.File, "📁")]
    [InlineData(ItemType.Clipboard, "📋")]
    [InlineData(ItemType.Todo, "✅")]
    [InlineData(ItemType.Note, "📝")]
    public void GlyphOf_PinsTheCanonicalIconPerType(ItemType type, string expected)
        => Assert.Equal(expected, ItemCardPolicy.GlyphOf(type));

    /// <summary>
    /// File 的标准形是 <b>📁</b>（不是卡片旧的那颗 📄）：这一类含文件夹——Everything 的结果与
    /// "拖入即登记"都会落目录，「类型」多选里还有"只看文件夹"那一档，而主窗的来源分段控件早就把它标成 📁。
    /// 单独一颗钉住，是因为它是这批唯一改到用户看得见的那处（卡片行）。
    /// </summary>
    [Fact]
    public void GlyphOf_FileIsFolderShapedBecauseTheClassContainsFolders()
        => Assert.Equal("📁", ItemCardPolicy.GlyphOf(ItemType.File));

    /// <summary>
    /// 每加一类都必须给它一颗自己的图标：两类共用一颗＝界面上分不开。
    /// 这里钉的是<b>不变量</b>（逐类型互不相同、且不落到兜底），不钉计数——写死个数的闸门
    /// 只会让"加一种类型"变成"把红测改绿"。
    /// </summary>
    [Fact]
    public void GlyphOf_EveryDeclaredTypeHasItsOwnDistinctIcon()
    {
        var values = Enum.GetValues<ItemType>();
        var glyphs = values.Select(ItemCardPolicy.GlyphOf).ToList();

        Assert.All(glyphs, g => Assert.False(string.IsNullOrWhiteSpace(g)));
        Assert.DoesNotContain(FallbackGlyph(), glyphs);
        Assert.Equal(values.Length, glyphs.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 库内存着本二进制不认识的类型序号（降级打开新库、或有人改了枚举）时，只能给出一个<b>中性</b>标记。
    /// 旧的两张组件表在这里写的是 📌，于是漏补臂的新类型一眼看上去像"用户置顶过它"——
    /// 假状态比空白难发现得多，这条不许被"顺手改回图钉"。
    /// </summary>
    [Fact]
    public void GlyphOf_UnknownTypeValueFallsBackToSomethingThatDoesNotLie()
    {
        var fallback = ItemCardPolicy.GlyphOf((ItemType)999);
        Assert.Equal(FallbackGlyph(), fallback);
        Assert.DoesNotContain(fallback, TypeGlyphs());
        Assert.DoesNotContain("📌", fallback);
    }

    private static string FallbackGlyph() => ItemCardPolicy.GlyphOf((ItemType)999);

    // ────────── ② 形状闸门：全库只许有一张"类型 → 图标"表 ──────────

    /// <summary>
    /// 扫 <c>src/</c> 全部手写源码：任何"<c>ItemType.X =&gt; &lt;图标字面量&gt;</c>"形状的臂都只许出现在
    /// <see cref="ItemCardPolicy"/>。两处细节都是被变异试出来的：
    /// <para>① **按行首匹配会漏**——把表写成一行内联 switch（<c>it.Type switch { ItemType.File => "📁", … }</c>）
    /// 时 <c>ItemType.</c> 不在行首，第一版闸门对这种写法直接绿（M4/M5 变异当场抓到）。所以用正则在整个文件里找，
    /// 不锚行首。</para>
    /// <para>② 判据取"右值是不是已知图标集合"，因此 <c>AiClassifyService</c> 那张
    /// <c>ItemType.X =&gt; "本地文件"</c>（类别名，另一件事）不会被误伤；<c>FolderTreePage</c>／<c>RssPage</c>
    /// 里那两颗 <c>📁</c>（分组头与源头的图标，也不是类型映射）同样放过。</para>
    /// 兜底那颗 📌 也列进禁集合：它是这一次分岔的物证，写回来就红。
    /// <para><b>这条的边界要认清</b>：它认的是"已知图标"，所以别处新写一张<b>换了一颗没见过的图标</b>的表，
    /// 它看不见——那类绕过由上面两条宿主判据（宿主不许有 <c>ItemType.…=&gt;</c> 臂、图标实参必须来自
    /// <see cref="ItemCardPolicy.GlyphOf"/>）兜住。全库范围用"已知图标"是为了不误伤
    /// <c>AiClassifyService</c> 那张类别名表；范围与代价都在这儿，别把这条当成"全库只许一张类型表"。</para>
    /// </summary>
    [Fact]
    public void TypeToIconSwitchArmsExistNowhereButThePolicy()
    {
        var forbidden = new HashSet<string>(TypeGlyphs(), StringComparer.Ordinal) { "📌" };
        var arm = new System.Text.RegularExpressions.Regex(
            "ItemType\\s*\\.\\s*[A-Za-z_]+\\s*=>\\s*\"([^\"]*)\"");
        var offenders = new List<string>();

        foreach (var (path, text) in ReadRepoUnder("src"))
        {
            if (path.EndsWith("ItemCardPolicy.cs", StringComparison.Ordinal)) continue;
            foreach (System.Text.RegularExpressions.Match m in arm.Matches(text))
            {
                if (forbidden.Contains(m.Groups[1].Value))
                    offenders.Add($"{path}: {m.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "类型→图标又长出第二份表了（同一类条目会在两个界面显示成两种东西）：\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// 三处宿主必须都读那一颗判据，而且自己手里什么都不能留。三条各挡一种"绕过写法"，
    /// 每一条都是被变异试出来的（M3/M4/M5/M8）：
    /// <para>① 旧表的名字（<c>EmojiFor</c>）不许复活；② 宿主里不许再出现任何
    /// <c>ItemType.X =&gt;</c> 臂——第一版按"右值是不是已知图标"判，于是换一颗<b>新</b>图标（📄）就绕过去了；
    /// ③ 宿主里不许出现已知图标字面量。</para>
    /// <para><b>★(U+2605) 不在禁列</b>：卡片那行的"★ 星数"是另一件事，一刀切"宿主不许有符号字符"会把它一起打死。</para>
    /// </summary>
    [Theory]
    [InlineData(CardVm)]
    [InlineData(GridVm)]
    [InlineData(SearchVm)]
    public void HostsReadTheSinglePolicyAndKeepNoLocalTable(string file)
    {
        var text = ReadRepoFile(file);
        Assert.DoesNotContain("EmojiFor", text, StringComparison.Ordinal);
        Assert.Contains("ItemCardPolicy.GlyphOf(", text, StringComparison.Ordinal);
        Assert.Empty(System.Text.RegularExpressions.Regex.Matches(
            text, "ItemType\\s*\\.\\s*[A-Za-z_]+\\s*=>"));
        foreach (var glyph in TypeGlyphs())
            Assert.False(text.Contains($"\"{glyph}\"", StringComparison.Ordinal),
                $"{file} 里还留着图标字面量 \"{glyph}\"——它该是转发，不是第二个主人");
    }

    /// <summary>
    /// 行首图标那一<b>格</b>（行记录的第 5 个实参）必须<b>逐字</b>是 <c>ItemCardPolicy.GlyphOf(it.Type)</c>。
    /// <para>为什么不能只判"实参里含不含 GlyphOf"：M6 变异把图标改成
    /// <c>it.Type == ItemType.File ? "📄" : ItemCardPolicy.GlyphOf(it.Type)</c>，那是<b>新</b>图标、
    /// 上面两条宿主判据（禁 <c>ItemType.…=&gt;</c> 臂、禁已知图标字面量）都看不见它，而"含不含"因为 else 分支
    /// 里那句转发也照样通过——于是宿主又成了第二个做决定的人，闸门却绿灯。钉<b>槽位</b>而不是钉"出现过"，
    /// 三元、局部变量、字典都从这一格挤不进去。</para>
    /// </summary>
    [Theory]
    [InlineData(GridVm, "new ItemRowItem(")]
    [InlineData(SearchVm, "new SearchResultItem(")]
    public void EveryRowConstructionFillsTheGlyphSlotFromThePolicy(string file, string ctor)
    {
        var text = ReadRepoFile(file);
        var sites = ArgumentsOfConstructorCalls(text, ctor);
        Assert.True(sites.Count > 0, $"{file} 里没找到 {ctor}——构造点搬走了，闸门要跟上");
        foreach (var args in sites)
        {
            var parts = SplitTopLevelArguments(args);
            Assert.True(parts.Count == 8,
                $"{ctor} 的实参不是 8 个了（现在 {parts.Count} 个）——图标槽位的下标要跟着改，闸门不许默默让路");
            Assert.Equal("ItemCardPolicy.GlyphOf(it.Type)", parts[4]);
        }
    }

    /// <summary>按<b>顶层</b>逗号切实参：括号/中括号内的逗号不算（嵌套调用不能被误切成两段）。</summary>
    private static IReadOnlyList<string> SplitTopLevelArguments(string args)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < args.Length; i++)
        {
            var c = args[i];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(args[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(args[start..].Trim());
        return parts;
    }

    /// <summary>按括号配对切出每一处 <paramref name="ctor"/> 的实参表（跨行也算，构造点换行不许躲过闸门）。</summary>
    private static IReadOnlyList<string> ArgumentsOfConstructorCalls(string text, string ctor)
    {
        var list = new List<string>();
        for (var at = text.IndexOf(ctor, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(ctor, at + 1, StringComparison.Ordinal))
        {
            var i = at + ctor.Length;
            var depth = 1;
            var start = i;
            for (; i < text.Length && depth > 0; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')') depth--;
            }
            list.Add(text[start..(i - 1)]);
        }
        return list;
    }

    /// <summary>卡片那处只许是转发：一旦它变成"先判一颗、否则走别的"，就又有了第二个主人。</summary>
    [Fact]
    public void CardSourceIconIsAPureForwardNotASecondDecisionPoint()
    {
        var body = MethodBody(ReadRepoFile(CardVm), "public string SourceIcon");
        Assert.Equal("ItemCardPolicy.GlyphOf(Type)", body.Split("=>")[1].Trim().TrimEnd(';'));
        Assert.DoesNotContain("switch", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 主窗来源分段控件是这一事实的<b>第四个</b>出处（XAML 里写死的 <c>Content="📁"</c> 那三颗）。
    /// 它不该改成代码生成（一颗筛选按钮而已），但必须与判据逐字相等：
    /// 否则哪天调整图标，行首换了、分段控件还留着旧的，用户按 📁 筛出的条目行首写着 📄。
    /// </summary>
    [Theory]
    [InlineData("SourceStar", ItemType.GitHubStar)]
    [InlineData("SourceBookmark", ItemType.Bookmark)]
    [InlineData("SourceFile", ItemType.File)]
    public void SourceFilterChipMatchesThePolicyGlyphForTheSameClass(string buttonName, ItemType type)
    {
        var xaml = ReadRepoFile(MainWindowXaml);
        var at = xaml.IndexOf($"x:Name=\"{buttonName}\"", StringComparison.Ordinal);
        Assert.True(at >= 0, $"主窗里找不到 {buttonName}（分段控件改名了，闸门要跟上）");

        var content = xaml.IndexOf("Content=\"", at, StringComparison.Ordinal);
        Assert.True(content >= 0 && content - at < 80, $"{buttonName} 之后没在附近找到 Content=\"…\"");
        var start = content + "Content=\"".Length;
        var literal = xaml[start..xaml.IndexOf('"', start)];

        Assert.Equal(ItemCardPolicy.GlyphOf(type), literal);
    }
}
