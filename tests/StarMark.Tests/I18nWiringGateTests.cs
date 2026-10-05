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
/// 界面语言在 <b>UI 那一侧</b>的接线证人（批次 WI-1）。
/// <para><c>StarMark.Tests</c> 引用不到 UI 项目，所以这些只能按<b>源码形状</b>钉——好处是它钉的正是
/// "改动会不会让这句话与实现分岔"，而不是运行时行为。认代码、挑语言、换语言那一次出声的真逻辑
/// 住在 <see cref="I18nContractTests"/>（Abstractions 层，能真跑）。</para>
/// <para>这一族坏掉的样子：<b>①</b> XAML 里问一颗表里没有的键 → 屏幕上出现 <c>⟨nav.tree⟩</c>；
/// <b>②</b> 键与字面量并存在同一行 → 换语言只重贴键那一份，字面量变成没人读也不消失的副本；
/// <b>③</b> 启动时定语言的调用排在建窗之后 → 用户看到的是"先一整窗中文、再抖成英文"；
/// <b>④</b> 语言选项在 XAML 里再列一遍 → 补日文时改了表没改这里，界面上多出一颗点了没反应的选项。</para>
/// </summary>
public sealed class I18nWiringGateTests
{
    private const string MainWindowXaml = "src/StarMark.UI/MainWindow.xaml";
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string SettingsOptionsCs = "src/StarMark.UI/Views/SettingsPage.Options.cs";
    private const string TrendingXaml = "src/StarMark.UI/Views/TrendingPage.xaml";
    private const string SnapshotXaml = "src/StarMark.UI/Views/SnapshotPage.xaml";
    private const string RssXaml = "src/StarMark.UI/Views/RssPage.xaml";
    private const string ClipboardXaml = "src/StarMark.UI/Views/ClipboardPage.xaml";
    private const string TagsXaml = "src/StarMark.UI/Views/TagsPage.xaml";
    private const string FolderTreeXaml = "src/StarMark.UI/Views/FolderTreePage.xaml";
    private const string SearchPageXaml = "src/StarMark.UI/Views/SearchPage.xaml";
    private const string MainWindowSearchCs = "src/StarMark.UI/MainWindow.Search.cs";
    private const string ThemeXaml = "src/StarMark.UI/Themes/StarMarkTheme.xaml";
    private const string LocTextCs = "src/StarMark.UI/Helpers/LocText.cs";
    private const string SettingsI18nCs = "src/StarMark.UI/Views/SettingsPage.I18n.cs";
    private const string SettingsHotkeysCs = "src/StarMark.UI/Views/SettingsPage.Hotkeys.cs";
    private const string GuideUiCs = "src/StarMark.UI/Helpers/GuideUi.cs";
    private const string AppCs = "src/StarMark.UI/App.xaml.cs";

    /// <summary>标记上那些"会显示给人看"的属性值（一颗正则，两处收口判据共用＝⛔ 不许各写一份）。</summary>
    const string Attributes = @"(?:Text|Content|Header|OnContent|OffContent|ToolTip|PlaceholderText)=""([^""]*)""";

    /// <summary>XAML 全部（跳过 <c>bin</c>/<c>obj</c> 与生成的 <c>*.g.cs</c> 那一类产物）。</summary>
    private static IReadOnlyList<(string RelativePath, string Text)> XamlUnder(string relativeDir)
    {
        var root = Path.Combine(RepoRoot(), relativeDir.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(root), $"普查目录不存在：{relativeDir}（守门失效比红测更危险，故直接抛）");
        return Directory.GetFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Select(f => f.Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal))
            .Select(f => (f, File.ReadAllText(f)))
            .OrderBy(f => f.Item1, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>抹掉 XML 注释后的标记：注释里出现 <c>Content="</c> 是给人读的叙述，不算一处字面量文案。</summary>
    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>
    /// UI 里被问到的所有键（三种附带属性＋代码里 <c>Locale.T</c>／<c>Fmt</c> 的字面量实参
    /// ＋<b>下拉数据项清单里的那几颗</b>）。
    /// <para>最后一条臂是批次 WI-2c 补的：选项从标记搬进 <c>LocOption.T("…")</c> 之后，
    /// "只看 XAML 与 <c>Locale.T</c>"的那两臂就<b>问不到它们了</b>——拼错一颗键名照样编译、照样跑，
    /// 屏幕上是一串 <c>⟨settings.general.themeLigt⟩</c>。⛔ 提示一律按 <c>tipKey:</c> 具名实参认：
    /// <c>LocOption.T</c> 的第 2 位是身份串（<c>"starred"</c>），按位置认键会把身份当键去查表。</para>
    /// <para>XAML 那一臂<b>只认字面量</b>（<c>(?!{)</c>）：共用模板上那颗 <c>Key="{Binding Key}"</c>
    /// 的取值住在数据项里，把它当一颗键去查表，红的是一句假话。</para>
    /// </summary>
    private static IEnumerable<(string Where, string Key)> AskedKeys()
    {
        foreach (var (path, text) in XamlUnder("src/StarMark.UI"))
        {
            foreach (Match m in Regex.Matches(Markup(text), @"i18n:LocText\.\w*Key=""(?!{)([^""]+)"""))
                yield return (path, m.Groups[1].Value);
        }
        // 批次 WI-2k 起<b>取词的调用点不再只住在 UI</b>：状态读数那一族（"N 分钟前"、节奏那句）
        // 的措辞搬进表之后，问键的地方搬到了 Abstractions／Core。扫描面必须跟着搬到整个 src，
        // 否则"问了一颗没登记的键"与"表里这一行没人问"两臂同时看不见那一半——两向都漏＝假绿。
        foreach (var (path, text) in ReadRepoUnder("src"))
        {
            var code = Code(text);
            foreach (Match m in Regex.Matches(code, @"Locale\.(?:T|Fmt)\(""([^""]+)"""))
                yield return (path, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(code, @"LocMessage\.Of\(\s*""([^""]+)"""))
                yield return (path, m.Groups[1].Value);
            // 键名住在 const 里的那些（`MinutesAgoKey = "time.minutesAgo"`／`FollowSystemKey`）：
            // 取词调用写的是 `Locale.Fmt(MinutesAgoKey, count)`，上面那几臂按**字面量实参**认，一律看不见 ⇒
            // 拼错这种键名今天两臂都不红（台架 WI-K8 量到的就是这一个洞）。按形状补这一臂：
            // 只认"点分小驼峰、至少两段"那颗值——那正是表自己的键形状，
            // ⛔ 不许宽成"任何带 Key 的常量"，否则 `DoneKey = "done"`、`AppKey = "clipApp"` 那些 JSON 字段名全被当界面键。
            foreach (Match m in Regex.Matches(code, @"const string \w*[Kk]ey\w*\s*=\s*""([^""]+)"""))
            {
                var value = m.Groups[1].Value;
                if (Regex.IsMatch(value, @"^[a-z][a-z0-9]*(\.[a-z][A-Za-z0-9]*)+$", RegexOptions.CultureInvariant))
                    yield return (path, value);
            }
            foreach (Match m in Regex.Matches(code, @"LocOption\.T\(\s*""([^""]+)"""))
                yield return (path, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(code, @"tipKey:\s*""([^""]+)"""))
                yield return (path, m.Groups[1].Value);
        }
    }

    /// <summary>
    /// <b>XAML 与代码问的每一颗键都必须在表里</b>。
    /// <para>这是那唯一一条"拼错键名"的出口：运行期它不抛，只把那颗元素显示成 <c>⟨nav.tre⟩</c>——
    /// <see cref="Locale"/> 故意这样设计（看得见、且日志里留一句）。既然如此，就必须有一格在编译期
    /// 就把"问了一颗没登记的键"拦下来，否则那条设计就退化成"错了靠人截图来反馈"。</para>
    /// </summary>
    [Fact]
    public void EveryKeyTheUiAsksForIsRegisteredInTheTable()
    {
        var asked = AskedKeys().ToList();
        // 下界按现场读数留余量（批次 WI-2k 把状态读数那一族接进来之后现场是 130 处上下）：
        // 这一格拦的是"抽取器没跑起来"，不是"改动必须等于某个数"。
        Assert.True(asked.Count >= 100, $"整个 src 只认出 {asked.Count} 处取词（现场量过是 130 处上下），抽取器大概没跑对");
        foreach (var (where, key) in asked)
            Assert.True(UiStrings.TryGet(key, out _), $"{where} 问了表里没有的键：{key}");
    }

    /// <summary>
    /// 反过来：<b>表里每一行都必须真的被人问过</b>。
    /// <para>一行没人问的译文不是"无害的冗余"：它会让下一个人以为那一屏已经铺好了，于是 WI-2 的
    /// 铺开清单里就会漏掉它。这一格是上一条的<b>另一半</b>：只查"问的都登记了"，删掉一处接线而忘了删表
    /// 就永远绿——台架 WI-A1／WI-A9 正是从这里抓出我第一版判据的洞：它当时把<b>注释里提到的键名</b>
    /// 也当成"被问过"（<c>LocText.cs</c> 的用法示例里写着 <c>"nav.tree"</c>），于是把键改错也不会红。
    /// 所以现在两边都比<b>去掉注释之后</b>的源码：只有真的在取词的才算。</para>
    /// <para>这里比的是"整串带引号出现"，所以设置页那颗
    /// <c>const string FollowSystemKey = "settings.languageFollowSystem"</c> 也算被问 ✓。</para>
    /// </summary>
    [Fact]
    public void TheTableHasNoRowNothingAsksFor()
    {
        // 与 AskedKeys 同一扫描面（整个 src）：⛔ 两头用不同目录，那是把"没人问的一行"变成只有 UI 那一半才拦得住。
        // 但必须**跳过文案表自己**：每一颗键都在那一行里带引号出现过，把它算进"问过"就等于这一格永久绿
        // （#293 同一族：判据自己把自己喂绿了）。
        var askedLiterals = XamlUnder("src/StarMark.UI").Select(s => Markup(s.Text))
            .Concat(ReadRepoUnder("src")
                .Where(s => !s.RelativePath.EndsWith("I18n/UiStrings.cs", StringComparison.Ordinal))
                .Select(s => Code(s.Text)))
            .ToList();
        foreach (var key in UiStrings.Keys())
        {
            Assert.True(askedLiterals.Any(text => text.Contains('"' + key + '"', StringComparison.Ordinal)),
                $"表里这一行没人问：{key}（要么接上，要么删掉——留着等于谎报覆盖面）");
        }
    }

    /// <summary>
    /// <b>同一行不许既有键、又留着它该取代的那句字面量</b>。
    /// <para>这是本地化最典型的双写：改动的人怕运行时贴不上，于是把原文留着。它确实"看起来没问题"，
    /// 直到有人换语言——那时键那份跟着变，字面量那份留在原地，屏幕上就是半中半英。
    /// 判据按<b>属性名</b>配对，不按元素：占位符与提示各有自己的键（见 <see cref="LocText"/> 的分工），
    /// 所以 <c>Key</c> 只与正文那六颗属性相冲，不冲 <c>ToolTipService.ToolTip</c>。</para>
    /// </summary>
    [Theory]
    [InlineData(MainWindowXaml)]
    [InlineData(SettingsXaml)]
    public void AKeyNeverSharesItsLineWithTheLiteralItReplaces(string relativePath)
    {
        var lines = Markup(ReadRepoFile(relativePath)).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var literal in new[] { "Text=\"", "Content=\"", "Header=\"", "Title=\"", "Message=\"", "Description=\"" })
            {
                if (line.Contains("i18n:LocText.Key=\"", StringComparison.Ordinal)
                    && line.Contains(literal, StringComparison.Ordinal))
                {
                    Assert.Fail($"{relativePath}:{i + 1} 同时带正文键与 {literal} 字面量：换语言后那份字面量就是没人读的副本\n{line.Trim()}");
                }
            }
            if (line.Contains("i18n:LocText.ToolTipKey=\"", StringComparison.Ordinal)
                && line.Contains("ToolTipService.ToolTip=", StringComparison.Ordinal))
                Assert.Fail($"{relativePath}:{i + 1} 同时带 ToolTipKey 与 ToolTipService.ToolTip 字面量");
            if (line.Contains("i18n:LocText.PlaceholderKey=\"", StringComparison.Ordinal)
                && line.Contains("PlaceholderText=", StringComparison.Ordinal))
                Assert.Fail($"{relativePath}:{i + 1} 同时带 PlaceholderKey 与 PlaceholderText 字面量");
            // 开关两侧是<b>两颗</b>属性、两句字：漏掉任何一侧，换语言后那一排就是半中半英。
            foreach (var (keySide, literalSide) in new[]
                     { ("i18n:LocText.OnContentKey=\"", "OnContent=\""), ("i18n:LocText.OffContentKey=\"", "OffContent=\"") })
            {
                if (line.Contains(keySide, StringComparison.Ordinal)
                    && line.Contains(literalSide, StringComparison.Ordinal))
                    Assert.Fail($"{relativePath}:{i + 1} 同时带 {keySide} 与 {literalSide} 字面量");
            }
        }
    }

    /// <summary>
    /// <b>开关那颗元素只许挂 On／Off 两颗键，而且必须成对</b>。
    /// <para>把 <c>LocText.Key</c> 挂到 <c>ToggleSwitch</c> 上不会报错：那颗元素同时有 <c>Header</c> 与
    /// <c>Content</c>，按 <see cref="LocText"/> 的排除规则正文键会去写 <c>Header</c>——屏幕上开关上方
    /// 多出一行字，而真正该换的 <c>OnContent</c> 一个字不动（批次 WI-2d，与坑表 #274 同一族的反向形状）。
    /// 只挂上一侧同样安静：另一侧继续是旧语言，没人会去对比一颗开关的两种状态。</para>
    /// </summary>
    [Fact]
    public void ASwitchCarriesBothOfItsLabelsAndNeverAPayloadKey()
    {
        var offenders = new List<string>();
        foreach (var (path, text) in XamlUnder("src/StarMark.UI"))
        {
            var markup = Markup(text);
            foreach (Match tag in Regex.Matches(markup, @"<ToggleSwitch[^>]*>", RegexOptions.Singleline))
            {
                var open = tag.Value;
                var at = markup.Take(tag.Index).Count(c => c == '\n') + 1;
                var on = open.Contains("i18n:LocText.OnContentKey=\"", StringComparison.Ordinal);
                var off = open.Contains("i18n:LocText.OffContentKey=\"", StringComparison.Ordinal);
                // 只挂一侧＝换语言后那一颗开关半中半英；挂上正文键＝写进 Header，该换的那个字没换。
                // 这两条是<b>全域</b>判据（还没铺开的栏也必须成立）；"还剩几句中文"另见下一格的上界判据。
                if (on != off) offenders.Add($"{path}:{at} 开关的两侧键没成对（另一侧会留在旧语言）");
                if (open.Contains("i18n:LocText.Key=\"", StringComparison.Ordinal)
                    && !open.Contains("i18n:LocText.OnContentKey=\"", StringComparison.Ordinal))
                    offenders.Add($"{path}:{at} 开关挂了正文键（那会写进 Header，OnContent 一个字都不动）");
            }
        }
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
        // 现场读数（2026-10-06 为批次 WI-2i 重测：整页开关标签 22 对，全住在设置页——<b>已全数进表</b>
        // ＝常规 5＋健康 2＋数据 4＋拓展 8＋桌面工具 2＋AI 增强 1；组件窗与条目卡片上没有开关标签）。
        // ⚠ 这一格从本批起没有余量可推：下界＝现场读数，往后新加的开关也必须走键，否则这里红。
        Assert.True(Count(Markup(ReadRepoFile(SettingsXaml)), "i18n:LocText.OnContentKey=\"") >= 22,
            "开关两侧的标签要住在表里，不是写在 XAML 里");
    }

    /// <summary>
    /// 材质那一族<b>只许有一份措辞</b>：「桌面工具」的下拉复用「常规」页那六行
    /// <c>settings.general.material.*</c>，⛔ 不许为组件再造六行同名文案。
    /// <para>症状形状：同一句字两个出处之后，改一漏一——换语言时主窗口翻了、组件那一栏没翻，
    /// 而两栏的<b>中文</b>看起来都还在（因为键不同、对账的两颗闸门各看各的）。</para>
    /// <para><b>批次 WI-2c 把这六行从标记搬进了 <c>MaterialOptions</c></b>（闭着的那格要跟着语言走，
    /// 只有"数据项＋模板"这一条路），所以这一格的读点从"两颗 <c>&lt;ComboBoxItem&gt;</c> 之间的标记"
    /// 改成了"那份清单本身＋两颗下拉确实都指向它"。<b>判据守的那件事没变</b>，变的是它读的那份文件——
    /// 这正是坑表 #286 那一族：锚点只认旧形状时，改对了反而会红。</para>
    /// </summary>
    [Fact]
    public void TheWidgetMaterialListIsNotASecondCopy()
    {
        var options = Code(ReadRepoFile(SettingsOptionsCs));
        var block = Between(options, "IReadOnlyList<LocOption> MaterialOptions", "];");
        var asked = Regex.Matches(block, @"settings\.[a-z]+\.material\.\w+").Cast<Match>()
            .Select(m => m.Value).ToArray();

        Assert.Equal(6, asked.Length);
        Assert.All(asked, k => Assert.StartsWith("settings.general.material.", k, StringComparison.Ordinal));
        Assert.DoesNotContain("settings.widgets.material.", options, StringComparison.Ordinal);

        // 两颗下拉都得指向这一份清单——只有一份清单、却有人自带列表，就等于回到两个出处。
        var page = Markup(ReadRepoFile(SettingsXaml));
        Assert.Equal(2, Count(page, "ItemsSource=\"{x:Bind MaterialOptions, Mode=OneTime}\""));
    }

    /// <summary>
    /// 下拉里的项数<b>与枚举的成员数必须是同一个数</b>（材质三档／性能三档那两处的"档位表只能有一份"）。
    /// <para>为什么不是写死 6 与 3：写死的数本身就是第二个出处——加一档材质改了枚举没改清单，
    /// 屏幕上就少一格、而设置档里那颗 5 会安静地指到不存在的东西上（坑表 #291：同一个数有两个出处
    /// 比没有出处更坏）。这一格从<b>类型现场</b>抓数再对账，⛔ 不把那个数抄进判据。</para>
    /// </summary>
    [Fact]
    public void TheMaterialOptionListHasOneRowPerBackdropKind()
        => AssertOneRowPerEnumMember("MaterialOptions", typeof(StarMark.Abstractions.WidgetBackdropKind));

    [Fact]
    public void ThePerformanceOptionListHasOneRowPerMode()
        => AssertOneRowPerEnumMember("PerformanceOptions", typeof(StarMark.Core.Performance.PerformanceMode));

    private static void AssertOneRowPerEnumMember(string list, Type enumType)
    {
        var block = Between(Code(ReadRepoFile(SettingsOptionsCs)), $"IReadOnlyList<LocOption> {list}", "];");
        var rows = Regex.Matches(block, @"LocOption\.T\(""[^""\s]+""").Count;
        Assert.Equal(Enum.GetNames(enumType).Length, rows);
    }

    /// <summary>
    /// <b>选项不再躺在标记里</b>：本批扫过的两份标记里不许再有一颗 <c>&lt;ComboBoxItem&gt;</c>，
    /// 而凡是指定了 <c>ItemsSource</c> 的下拉<b>必须同时</b>指定那颗共用的 <c>ItemTemplate</c>。
    /// <para>合取的另一半才是这一格的存在理由：只挂 <c>ItemsSource</c> 不挂模板，WinUI 会去
    /// <c>ToString()</c> 那颗数据项——屏幕上是一整排 <c>LocOption { Key = settings…, … }</c>。
    /// 编译过、测试全绿、启动不抛，只有眼睛看得见（坑表 #180 同族）。</para>
    /// <para><b>射程说清楚</b>：这一格只管设置页与主窗这两份标记。热榜那颗「今日／本周／本月」
    /// （<c>TrendingPage.xaml</c>）与搜索组件那三档（<c>SearchWidget.xaml</c>）今天还是
    /// <c>&lt;ComboBoxItem Content="中文"&gt;</c>，它们的表行还没建，归后面一批（P-169）。</para>
    /// </summary>
    [Theory]
    [InlineData(MainWindowXaml)]
    [InlineData(SettingsXaml)]
    [InlineData(TrendingXaml)]
    public void DropdownOptionsLiveInCodeNotInTheMarkup(string relativePath)
    {
        var page = Markup(ReadRepoFile(relativePath));
        Assert.DoesNotContain("<ComboBoxItem", page, StringComparison.Ordinal);
        var withItems = Regex.Matches(page, @"<ComboBox\b[^>]*ItemsSource=""\{x:Bind (\w+), Mode=OneTime\}""[^>]*>",
            RegexOptions.Singleline).Cast<Match>().ToList();
        Assert.True(withItems.Count >= 1,
            $"{relativePath} 认不出任何一颗按清单填的下拉——抽取器没跑对，这一格就守不住");
        foreach (var box in withItems)
        {
            Assert.Contains("ItemTemplate=\"{StaticResource LocOptionTemplate}\"", box.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("</ComboBox>", box.Value, StringComparison.Ordinal);   // 自封闭＝项不在标记里
        }
    }

    /// <summary>
    /// <b>已扫过的那几页，标记里不许留中文</b>：一颗按页推进的收口判据，扫过的文件逐颗列在 <c>[InlineData]</c>。
    /// <para>为什么不一次扫整棵树：`&lt;Run&gt;` 那种"半个句子＋一个读数"的组合句今天还没有机制
    /// （P-169 那一族），整树扫会在还没轮到那一页时红成噪音，然后被人整颗关掉。⇒ 这一颗<b>按页加</b>：
    /// 加一颗文件就必须<b>同批</b>把那一页的标记全部进表，⛔ 不许先加 InlineData 再补翻译。</para>
    /// <para>每一臂只该红自己那一页（规则 76）：这是 Theory，加一颗文件就多一条测试。</para>
    /// </summary>
    [Theory]
    [InlineData(TrendingXaml)]
    [InlineData(SnapshotXaml)]
    [InlineData(RssXaml)]
    [InlineData(ClipboardXaml)]
    [InlineData(TagsXaml)]
    [InlineData(FolderTreeXaml)]
    [InlineData(SearchPageXaml)]
    public void ScannedPagesHoldNoChineseMarkup(string relativePath)
    {
        var page = Markup(ReadRepoFile(relativePath));
        var left = page.Split('\n')
            .Where(line => !line.Contains("<Run ", StringComparison.Ordinal))
            .SelectMany(line => Regex.Matches(line, Attributes).Cast<Match>())
            .Select(m => m.Groups[1].Value)
            .Where(v => Regex.IsMatch(v, @"[\u4e00-\u9fff]"))
            .ToArray();
        Assert.True(left.Length == 0, $"「{relativePath}」还剩 {left.Length} 句中文标记：\n{string.Join("\n", left)}");
    }

    /// <summary>
    /// 那颗共用模板<b>必须按绑定贴词</b>——整套机制只有这一处落地，它一丢，所有下拉一起退回旧行为，
    /// 而 XAML 与清单的形状看起来都还"对"。
    /// </summary>
    [Fact]
    public void TheSharedOptionTemplatePastesByBinding()
    {
        var theme = Markup(ReadRepoFile(ThemeXaml));
        var template = Between(theme, "<DataTemplate x:Key=\"LocOptionTemplate\">", "</DataTemplate>");
        Assert.Contains("i18n:LocText.Key=\"{Binding Key}\"", template, StringComparison.Ordinal);
        Assert.Contains("i18n:LocText.ToolTipKey=\"{Binding TipKey}\"", template, StringComparison.Ordinal);
        // 正文那颗属性只许由 LocText 写：绑定与重贴同指一处时，赢的是重贴（本地值压过绑定），
        // 但那份绑定就是"没人挂键时显示什么"的出口（语言自名），⛔ 不许顺手删掉。
        Assert.Contains("Text=\"{Binding Text}\"", template, StringComparison.Ordinal);
    }

    /// <summary>
    /// 排序档那五颗<b>身份</b>必须逐个是数据层真认得的键。
    /// <para>坏法：改个名（<c>starred</c>→<c>pushed</c>）只改了 UI 那一侧，SQL 的 switch 落入
    /// <c>default</c>＝"最近更新"——屏幕上的档名与实际的排序顺序就此分岔，而两边都不报错。
    /// 所以这一格从 <c>ItemRepository</c> <b>现场抓出它认得的那几个词</b>再对账（与
    /// <c>GitHubTruthInLabelingGateTests</c> 同一个道理的两端：档名要说出真正按什么排）。</para>
    /// </summary>
    [Fact]
    public void EverySortOptionValueIsAKeyTheRepositoryActuallySortsBy()
    {
        var repo = Code(ReadRepoFile("src/StarMark.Data/ItemRepository.Items.cs"));
        var arms = Between(repo, "filter.Sort switch", "};");
        var known = Regex.Matches(arms, @"""(\w+)""(?: or _)? =>").Cast<Match>()
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.True(known.Count >= 4, $"从浏览那条 switch 只认出 {known.Count} 个排序键，抽取器没跑对");

        var options = Between(Code(ReadRepoFile(MainWindowSearchCs)), "IReadOnlyList<LocOption> SortOptions", "];");
        var values = Regex.Matches(options, @"LocOption\.T\(""[^""\s]+"",\s*""(\w+)""").Cast<Match>()
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(5, values.Count);
        Assert.All(values, v => Assert.True(known.Contains(v), $"排序档 {v} 数据层不认，它会静默落入默认档"));
        Assert.Equal(values.Count, values.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 「大文件（&gt;100 MB）」那颗标签里的数与查询串里的数<b>必须是同一个数</b>（坑表 #289 的第二面：
    /// 这次读出的不是"没有出处"，而是<b>有两个出处</b>——<c>FileKindQuery</c> 写死 <c>size:&gt;100mb</c>，
    /// 标签写死"100 MB"，改一处另一处不会红，屏幕上就成了"指着错阈值的按钮"）。
    /// <para>判据读的是<b>两边各自的现场串</b>：从查询那一支抓出数字，再要求表里那两列逐字等于按它拼出来的标签。</para>
    /// </summary>
    [Fact]
    public void TheLargeFileLabelNamesTheSameThresholdTheQueryUses()
    {
        var query = ReadRepoFile("src/StarMark.Abstractions/FileKindQuery.cs");
        var hit = Regex.Match(query, @"size:>(\d+)mb", RegexOptions.CultureInvariant);
        Assert.True(hit.Success, "查询那一支里找不到 \"size:>Nmb\"——这一格钉的是两个数对不对得上，先要有数");

        var threshold = hit.Groups[1].Value;
        var row = UiStrings.All.Single(e => e.Key == "kind.large");
        Assert.Equal("大文件（>" + threshold + " MB）", row.Zh);
        Assert.Equal("Large files (>" + threshold + " MB)", row.En);
    }

    /// <summary>
    /// 「测试连接」那颗按钮的<b>忙闲两句都走键</b>（坑表 #276 在「AI 增强」这一栏的第二次兑现）。
    /// <para>写成 <c>Content = "取消"</c> 的那一版，红脸的时刻是"抓取期间换一下语言"：
    /// <c>LocText</c> 按标记里那颗"空闲"键把按钮重贴成"测试连接"——一件还在跑的事被界面改了口，
    /// 而那一刻用户最需要知道的恰恰是"这一按能不能中断"。</para>
    /// </summary>
    [Fact]
    public void TheProbeButtonSwitchesKeysNotWords()
    {
        var page = Code(ReadRepoFile("src/StarMark.UI/Views/SettingsPage.Ai.cs"));
        Assert.Contains("LocText.SetKey(AiProbeButton, ProbeCancelKey)", page);
        Assert.Contains("LocText.SetKey(AiProbeButton, ProbeIdleKey)", page);
        // 回来路＝直接改字。这一句读的是整档：注释里出现它不算（Code() 已抹注释）。
        Assert.DoesNotContain("AiProbeButton.Content = \"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// 两颗把<b>当场算出来的值</b>写进标签的按钮（那颗"改用本机有的模型"里是模型名、那颗"继续处理…"里是条数）
    /// <b>留在 code-behind，标记里不许挂静态键</b>：挂了键，换语言那一刻静态那句会盖掉带值的这句，
    /// 用户看到的就是一个和实际要改的东西不一样的名字。
    /// <para>这一格钉的是<b>登记在案的缺口</b>（P-169 的"组合句"那一族，`settings.languageNote` 里点名了这两颗）。
    /// 将来那一族有了"按语言重算"的机制，这里会红——改法是连着把两颗按钮换成"按键＋占位符重算"，
    /// 不是把静态键一挂了事（与坑表 #276 同一件事的镜像面：那边是"别在键之外再写字"，这边是"别给带值的字挂静态键"）。</para>
    /// </summary>
    [Fact]
    public void TheTwoComposedButtonLabelsStayWithTheCode()
    {
        var ai = Between(Markup(ReadRepoFile(SettingsXaml)),
                         "<TabViewItem x:Name=\"TabAi\" Tag=\"ai\"", "</TabViewItem>");
        foreach (var name in new[] { "AiFillModelButton", "AiResumeButton" })
        {
            var tag = Regex.Match(ai, "<Button[^>]*x:Name=\"" + name + "\"[^>]*>", RegexOptions.CultureInvariant);
            Assert.True(tag.Success, $"「AI 增强」那一栏里找不到 {name} 的开标签——搬家了就来这里改地址，别把这一格删掉");
            Assert.DoesNotContain("LocText", tag.Value, StringComparison.Ordinal);
        }

        var page = Code(ReadRepoFile("src/StarMark.UI/Views/SettingsPage.Ai.cs"));
        Assert.Contains("AiFillModelButton.Content = \"模型名改用", page);
        Assert.Contains("继续处理上次整理好的", page);
    }

    /// <summary>
    /// 已经 shipped 的界面文本<b>不许承诺"这一版还没有、以后才有"</b>（坑表 #297）。
    /// <para>这一族最坏的地方是它<b>只会变旧、不会变红</b>：「AI 增强」那句"批量整理标签的入口在下一批接上"
    /// 是批次 A 写的，入口在批次 B 就接上了，而屏幕上那句话一直挂着；到批次 WI-2i 搬字进表时，
    /// 它已经从"标记里的一句过期路线图"变成"表里唯一的出处"——再逐字照抄一次就等于把它锁死。</para>
    /// <para>判据按<b>句式</b>写（中文那几种说法＋英文那几种对译），⛔ 不是只钉今天这一句：
    /// 换个措辞（"下个版本见"／"coming soon"）也在这条射程里——台架按中英两种措辞各咬一次（J1／J2）。
    /// ⚠ 这一格管的是<b>承诺</b>，不管状态读数：<c>settings.ai.notTested</c>「还没测过。」说的是
    /// "此刻还没"，不是"以后才有"，所以那些词刻意不在清单里（把它们钉进去会把闸门拗成背字符串）。
    /// <para>批次 WI-2c 又删掉两句这种承诺（中文那句"那是后面一批的事"、英文那句 "a later pass"），
    /// 而<b>这两种措辞当时都不在词表里</b>——旧射程只认"下一批／in a later release"。
    /// 所以删话的同一批要把词表补宽：只改界面不改闸门，下一次换个词再写一遍承诺照样绿。</para></para>
    /// </summary>
    [Fact]
    public void ShippedUiTextNeverPromisesAFutureBatch()
    {
        var offenders = (from e in UiStrings.All
                         from side in new[] { ("中文列", e.Zh), ("英文列", e.En) }
                         where Regex.IsMatch(side.Item2,
                             @"下一批|下一个版本|下批|后续版本|未来的版本|即将|待实现|暂未|尚未提供"
                             + @"|后面一批|后面再|下一版|以后会|之后会|将来会"
                             + @"|arrives? with the next|in a (?:later|future) (?:release|batch)|coming soon"
                             + @"|not (?:yet )?implemented|planned for"
                             + @"|(?:later|following|next) (?:pass|batch|round)",
                             RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
                         select $"{e.Key}（{side.Item1}）").ToList();

        Assert.True(offenders.Count == 0,
            "界面文本在承诺一件今天还没做的事：" + string.Join("、", offenders)
            + "\n要么那句话本来就是过期的路线图（就地改成今天的事实），要么那件事真没做——"
            + "那它就不该出现在 shipped 的界面里（账本 P-169 的批量里也不许先挂键再留承诺）");
    }

    /// <summary>
    /// 已经铺过的八栏（常规／使用说明／快捷键／数据／健康与诊断／拓展功能／桌面工具／AI 增强）<b>自己那份标记</b>里一句中文都不许剩
    /// （批次 WI-2d 立的收口判据，WI-2e 推到四栏，批次 WI-2f 推到五栏，批次 WI-2g 推到六栏，批次 WI-2h 推到七栏、本批 WI-2i 推到八栏（八栏齐了）。
    /// <para>判据分两头：① 这八栏的 <c>Text</c>／<c>Content</c>／<c>Header</c>／开关两侧／提示里不能再有汉字；
    /// ② 整页剩余量只钉<b>上界</b>——下一批要把它往下推，往上加才是事故。把等号钉在剩余量上，
    /// 等于让每一批改完都得先来改一次判据，那正是"闸门替改动让路"的形状。</para>
    /// <para>⚠ <b>这一格的射程止于标记</b>：同一屏上还有中文是<b>程序算出来或拼出来</b>的（顶栏那颗索引状态、
    /// 「关于与更新」里那行检查结果、快捷键这一栏每一行的动作名与键位、数据这一栏那几行状态读数与备份列表
    /// 每行的「恢复前快照」标签、拓展功能这一栏那五行状态读数（热榜／RSS／本地磁盘搜索／截屏／画布）、
    /// 那行引擎占用大小的「约」字与画布那串快捷键的动作名、桌面工具这一栏每一类组件那行标题与它下面每行两颗按钮的字、开机加载那行说每颗约多少的读数与布局每行的名字与摘要）、AI 增强这一栏那行测试结果与那行用量读数、那三格"这几格的出厂值/上限是多少"的只读展示，以及两颗字里带着当场算出来的值的按钮（那颗"改用本机有的模型"里是模型名、那颗"继续处理…"里是条数）——第一版覆盘照片拍到它们之后，名字就从"没有中文正文"
    /// 改成了"自己那份标记"。
    /// 那几处要翻，得先把状态改成按键推（P-169 的一格），不是往这里再挂一颗键（坑表 #276）。</para>
    /// <para>⚠ 数的时候<b>跳过 <c>&lt;Run&gt;</c> 那一行</b>：那是"半个句子＋一个读数"的胶水，
    /// 它有自己的登记格（下面那一颗证人），把它算进这一格就等于宣布它已经翻了。</para>
    /// </summary>

    [Fact]
    public void TheEightKeyedTabsHoldNoChineseMarkupAndTheRestOnlyShrinks()
    {
        var page = Markup(ReadRepoFile(SettingsXaml));
        foreach (var (name, tag) in new[]
                 {
                     ("TabGeneral", "general"), ("TabGuide", "guide"),
                     ("TabHotkeys", "hotkeys"), ("TabData", "data"), ("TabHealth", "health"),
                     ("TabExtend", "extend"), ("TabWidgets", "widgets"), ("TabAi", "ai"),
                 })
        {
            var tab = Between(page, $"<TabViewItem x:Name=\"{name}\" Tag=\"{tag}\"", "</TabViewItem>");
            var left = tab.Split('\n')
                .Where(line => !line.Contains("<Run ", StringComparison.Ordinal))
                .SelectMany(line => Regex.Matches(line, Attributes).Cast<Match>())
                .Select(m => m.Groups[1].Value)
                .Where(v => Regex.IsMatch(v, @"[\u4e00-\u9fff]"))
                .ToArray();
            Assert.True(left.Length == 0, $"「{name}」栏还剩 {left.Length} 句中文标记：\n{string.Join("\n", left)}");
        }

        var whole = Regex.Matches(page, Attributes).Cast<Match>()
            .Count(m => Regex.IsMatch(m.Groups[1].Value, @"[\u4e00-\u9fff]"));
        Assert.True(whole <= 5, $"整页中文正文从 5 涨到了 {whole}（只剩「健康与诊断」那五行 <Run> 胶水，这一族只许往下减）");
    }

    /// <summary>
    /// 「健康与诊断」里那<b>五句挂在 <c>&lt;Run&gt;</c> 上的胶水</b>＝登记在案的缺口，一格数清、不许悄悄长大。
    /// <para>它们不是漏翻，是<b>这一族还没有能用的机制</b>：每一句都是"半个句子＋一个程序算出来的读数"
    /// 拼起来的（<c>语言 Top：{读数}</c>、<c>{因子}： -{扣分} 分（{明细}）</c>）。
    /// <c>Locale.Fmt</c> 早就有位置式占位符，但 <c>LocText</c> 只会"按键重贴整句"——读数是另一颗绑定的属性，
    /// 两者要一起翻就得先让那一行按语言重算。那一刀属于 P-169 的"组合句"那一族，不在标记这一族里。</para>
    /// <para>钉子＝<b>逐字列出这五句</b>。将来谁把它们翻掉了，这一格会红，那是刻意的：
    /// 翻掉的人要顺带来这里把清单改短，而不是让"缺口"这件事从闸门口径里消失。</para>
    /// </summary>
    [Fact]
    public void TheHealthTabsComposedReadoutsAreTheOnlyChineseMarkupLeftInTheKeyedTabs()
    {
        var page = Markup(ReadRepoFile(SettingsXaml));
        var health = Between(page, "<TabViewItem x:Name=\"TabHealth\" Tag=\"health\"", "</TabViewItem>");
        var glue = health.Split('\n')
            .Where(line => line.Contains("<Run ", StringComparison.Ordinal))
            .SelectMany(line => Regex.Matches(line, @"Text=""([^""]*)""").Cast<Match>())
            .Select(m => m.Groups[1].Value)
            .Where(v => Regex.IsMatch(v, @"[\u4e00-\u9fff]"))
            .ToArray();
        Assert.Equal(new[] { " 分（", "语言 Top：", "独立域名：", "疑似重复 Top：", "标签 Top：" }, glue);
    }

    /// <summary>
    /// 第一里程碑<b>铺到哪一行、故意留在哪一行</b>，都要当场看得见。
    /// <para>三个数字是现场量出来的（主窗标记：正文键 26／提示键 8／占位符键 2；批次 WI-2c 之前是
    /// 31／9／2，少掉的那 5＋1 颗是排序档——选项改成"数据项＋模板"之后它们住在
    /// <c>MainWindow.Search.cs</c> 的 <c>SortOptions</c> 里，所以这一格同批补了一条<b>配对账</b>：
    /// 标记里少的颗数必须等于代码里多的颗数，⛔ 不许"搬着搬着少了一颗"。
    /// 把它们钉住不是为了一次改文案就要来改判据，而是因为"覆盖面"这件事本身只有数字能证——
    /// <c>settings.languageNote</c> 那句"这一版只覆盖主界面骨架"是产品对用户的承诺，它得有一个对应的数。）</para>
    /// <para>剩下那一颗 <c>Text="索引就绪"</c> 是<b>登记在案的缺口</b>（不是漏翻）：那颗状态是代码在写的
    /// （同步中／取消中／失败都往同一颗 <c>TextBlock</c> 上推），给它挂键的后果不是"少翻一句"，
    /// 而是<b>换语言那一刻把"同步中…"覆盖成"索引就绪"</b>——一句谎话。要翻它得先把状态改成按键推，
    /// 那一刀在 WI-2。</para>
    /// </summary>
    [Fact]
    public void FirstMilestoneCoversTheMainWindowAndDeliberatelyLeavesOneDynamicString()
    {
        var xaml = ReadRepoFile(MainWindowXaml);
        Assert.Equal(26, Count(xaml, "i18n:LocText.Key=\""));
        Assert.Equal(8, Count(xaml, "i18n:LocText.ToolTipKey=\""));
        Assert.Equal(2, Count(xaml, "i18n:LocText.PlaceholderKey=\""));
        Assert.Equal(1, Count(xaml, "xmlns:i18n="));

        // 排序档那五颗的配对账：标记里少了 5 颗正文键／1 颗提示键，代码里就必须正好多出这些。
        var sort = Code(ReadRepoFile(MainWindowSearchCs));
        Assert.Equal(5, Count(sort, "LocOption.T(\"sort."));
        Assert.Equal(1, Count(sort, "tipKey: \"sort."));

        var stillLiteral = Markup(xaml).Split('\n')
            .SelectMany(line => Regex.Matches(line, @"(ToolTip|Title|Header|Content|Text|Message|Description|PlaceholderText)=""([^""]*)""")
                .Cast<Match>().Where(m => Regex.IsMatch(m.Groups[2].Value, @"[\u4e00-\u9fff]")))
            .Select(m => m.Value)
            .ToArray();
        Assert.Equal(new[] { "Text=\"索引就绪\"" }, stillLiteral);
    }

    /// <summary>
    /// 启动时定语言的那一行<b>必须排在第一颗窗口之前</b>。
    /// <para>顺序反了不是"晚一点才变英文"：那一晚是<b>整窗按默认档建完</b>再重贴一遍，
    /// 用户看到的是闪一下，而在慢机上是一整屏中文起立之后才改口。这条只能钉顺序，
    /// 因为运行期没有任何日志会说"我建了两次"。</para>
    /// </summary>
    [Fact]
    public void StartupResolvesTheLanguageBeforeTheFirstWindowExists()
    {
        var app = Code(ReadRepoFile(AppCs));
        var resolve = app.IndexOf("Locale.TrySet(Locale.Resolve(", StringComparison.Ordinal);
        var firstWindow = app.IndexOf("new MainWindow(", StringComparison.Ordinal);
        Assert.True(resolve >= 0, $"{AppCs} 里没找到启动定语言那一行");
        Assert.True(firstWindow > resolve,
            $"启动定语言必须排在建窗之前（resolve={resolve} new MainWindow={firstWindow}）");
        Assert.Contains("fileSettings.LoadUiLanguageCode()", app, StringComparison.Ordinal);
        Assert.Contains("AppLanguages.SystemLanguageTags()", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>语言名单只有一处出处</b>：设置页那一颗下拉在 XAML 里必须是<b>空的</b>（自封闭、无子项），
    /// 选项由代码按 <see cref="AppLanguages.Pickable"/> 现场生成。
    /// <para>同时在 XAML 里列一遍的话，"这一版发几门语言"就有两个答案：补日文时只改了表，
    /// 界面会多出一颗选过去仍然满屏中文的选项——而那正是 <c>UiStrings</c> 卷首拒绝过的事。</para>
    /// </summary>
    [Fact]
    public void TheLanguageChoicesHaveExactlyOneSource()
    {
        var xaml = Markup(ReadRepoFile(SettingsXaml));
        Assert.Equal(1, Count(xaml, "x:Name=\"LanguageSelect\""));
        Assert.Matches("x:Name=\"LanguageSelect\"[^>]*/>", xaml);   // 自封闭＝一个选项都没在 XAML 里列
        // 只禁"语言自己的名字"：「跟随系统」在主题那一栏的下拉里合法地存在，它不是这一颗下拉的选项。
        foreach (var name in new[] { "简体中文", "English", "日本語" })
            Assert.DoesNotContain(name, xaml, StringComparison.Ordinal);

        var section = Code(ReadRepoFile(SettingsI18nCs));
        Assert.Equal(1, Count(section, "AppLanguages.Pickable"));
        Assert.Equal(1, Count(section, "AppLanguages.SelfName"));
        Assert.Equal(1, Count(section, "Locale.TrySet(Locale.Resolve("));
    }

    /// <summary>
    /// 改语言那一手：<b>先落盘、再生效</b>。
    /// <para>顺序反过来（先生效再落盘）一旦落盘失败——只读目录、OneDrive 锁文件——用户看到的是一句
    /// "已经改了"，而下次启动仍是旧语言。按今天这个顺序，最坏的情况是"重贴途中抛了，但档里已经是他要的那一版"，
    /// 下一次启动仍然是他选的。</para>
    /// </summary>
    [Fact]
    public void TheLanguageChoiceIsPersistedBeforeItTakesEffect()
    {
        var handler = MethodBody(Code(ReadRepoFile(SettingsI18nCs)), "private void LanguageSelect_SelectionChanged");
        var save = handler.IndexOf("SetUiLanguageCode(", StringComparison.Ordinal);
        var apply = handler.IndexOf("Locale.TrySet(", StringComparison.Ordinal);
        Assert.True(save >= 0 && apply > save, $"必须先落设置档再切换语言：{handler}");
        // 程序化回填选项那一次不算用户改：抑制标志必须排在读写之前，否则每次进设置页都重写一遍档
        Assert.Contains("if (_applyingLanguageChoice) return;", handler, StringComparison.Ordinal);
    }

    /// <summary>
    /// 设置档里那一格<b>存的是语言代码，不是它在下拉里的位置</b>。
    /// <para>按序号存的那一份今天完全能用，坏法是明天的：<c>Pickable</c> 中间插进日文，所有已存的
    /// <c>1</c> 就安静地指向了另一门语言——<b>没有异常、没有日志、界面看着也自洽</b>。
    /// 而这一族住在 UI 层（<c>SettingsStore</c> 在 <c>StarMark.UI</c>），行为测够不到，只能钉形状：
    /// 写入那一行必须经过 <c>AppLanguages.CodeOf</c>，读取那一行必须先过 <c>FromCode</c>。</para>
    /// </summary>
    [Fact]
    public void ThePersistedValueIsALanguageCodeNotAPositionInTheList()
    {
        const string store = "src/StarMark.UI/Helpers/SettingsStore.I18n.cs";
        var code = Code(ReadRepoFile(store));
        Assert.Contains("AppLanguages.CodeOf(parsed.Value)", code, StringComparison.Ordinal);
        Assert.Contains("AppLanguages.FromCode", code, StringComparison.Ordinal);
        Assert.DoesNotContain("(int)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedIndex", code, StringComparison.Ordinal);
    }

    /// <summary>首启那份引导的两颗按键不许自带字面量（它是对用户说的话，也是这一族里唯一在代码里的按键）。</summary>
    [Fact]
    public void TheFirstRunDialogButtonsTakeTheirWordsFromTheTable()
    {
        var code = Code(ReadRepoFile(GuideUiCs));
        Assert.Equal(1, Count(code, "PrimaryButtonText = Locale.T(\"guide.primary\")"));
        Assert.Equal(1, Count(code, "CloseButtonText = Locale.T(\"guide.secondary\")"));
        Assert.Equal(0, Count(code, "PrimaryButtonText = \""));
        Assert.Equal(0, Count(code, "CloseButtonText = \""));
    }

    /// <summary>
    /// 换语言<b>只有一个订阅点</b>，而且那个订阅点只许做"重贴"这一件事。
    /// <para>多一处订阅就多一处"谁去把文案改回来"的分配：这一族的坏样子不是崩，是两处各自记得一部分，
    /// 于是有一处永远贴着旧词。而"重贴"里若混进重建页面／重新查库，切换语言就成了一次全应用刷新
    /// ——那才是真的会卡一下。</para>
    /// </summary>
    [Fact]
    public void ExactlyOneSubscriberRenamesTheStringsAndItRebuildsNothing()
    {
        var files = ReadRepoUnder("src/StarMark.UI");
        Assert.Equal(1, files.Sum(f => Count(Code(f.Text), "Locale.Changed += ")));
        Assert.Contains("src/StarMark.UI/Helpers/LocText.cs",
            files.Where(f => Code(f.Text).Contains("Locale.Changed += ", StringComparison.Ordinal))
                 .Select(f => f.RelativePath.Replace(Path.DirectorySeparatorChar, '/')),
            StringComparer.Ordinal);

        var reapply = MethodBody(Code(ReadRepoFile(LocTextCs)), "private static void ReapplyAll");
        Assert.DoesNotContain("new ", reapply, StringComparison.Ordinal);        // 重贴不等于重建
        Assert.DoesNotContain("Navigate", reapply, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>占位符与正文是两条路</b>：<c>LocText.Key</c> 那一份属性清单里不许有 <c>PlaceholderText</c>。
    /// <para><c>TextBox</c> 同时有 <c>Text</c>（用户正在打的字）与 <c>PlaceholderText</c>（提示）。
    /// 一条路走到黑的最坏结果不是难看，是<b>换语言时把用户输入的那一行覆盖掉</b>——
    /// 他正在搜的那串字没了，而这一下没有任何地方会留话。</para>
    /// </summary>
    [Fact]
    public void PlaceholderTextIsNeverReachedByThePlainContentKey()
    {
        var loc = Code(ReadRepoFile(LocTextCs));
        var table = loc[loc.IndexOf("TextProperties =", StringComparison.Ordinal)..];
        Assert.DoesNotContain("\"PlaceholderText\"", table[..table.IndexOf("};", StringComparison.Ordinal)],
            StringComparison.Ordinal);
        // 正文／提示／开关两侧各一条＝现场数过 5 颗（批次 WI-2d 给开关那两颗开的口），
        // 批次 WI-2k 再加第六颗 `Args`：带读数的那句正文要能"按键＋实参当场重算"，否则状态行只能整句留在 code-behind 里拼（＝换语言不翻）。
        // 这条计数<b>不是</b>为了让改动便宜：它是"每颗附带属性都得有自己的取词路"的证人——
        // 少一颗就是有一类元素贴不上而日志里不出声。
        Assert.Equal(6, Count(loc, "DependencyProperty.RegisterAttached"));
        Assert.Contains("FindProperty(d, \"PlaceholderText\")", loc, StringComparison.Ordinal);
    }

    /// <summary>
    /// 那颗"正文"属性的挑法必须避开两个坑（批次 WI-2 第一次给设置页八栏挂键时真机照出来的）：
    /// <para>① 元素<b>同时</b>有 <c>Content</c> 与 <c>Header</c> 时（<c>TabViewItem</c>／<c>Expander</c>／
    /// <c>ToggleSwitch</c> 这一族），<c>Content</c> 装的是<b>整页正文</b>。把标题写进去，屏幕上的症状
    /// 不是"字被覆盖"而是<b>标题一颗都不出现、整栏空白</b>——XAML 里属性先于子元素解析，
    /// 正文随后又被子元素盖回来，于是没有任何地方留下那句字。当时 <b>5435 格全绿</b>。</para>
    /// <para>② 输入框的 <c>Text</c> 是用户正在打的字，换语言那一刻覆写它等于吃掉输入；
    /// 这类元素真正的标签是 <c>Header</c>。</para>
    /// <para>⚠ 与 <c>TheDependencyPropertyLookupChecksBothFieldAndPropertyShapes</c> 同一档口径：
    /// 这一格钉的是<b>读法本身</b>，挡得住"守卫被删回去"，挡不住"两条守卫都写反"——
    /// 后者只有真机那张照片能看见（坑表 #271 同族）。</para>
    /// </summary>
    [Fact]
    public void ThePlainContentKeyNeverWritesIntoThePayloadOrTheUsersInput()
    {
        var resolve = MethodBody(Code(ReadRepoFile(LocTextCs)), "private static DependencyProperty? ResolveTextProperty");
        Assert.Contains("name == \"Content\" && hasHeader", resolve, StringComparison.Ordinal);
        Assert.Contains("name == \"Text\" && isInputBox", resolve, StringComparison.Ordinal);
        Assert.Contains("FindProperty(target, \"Header\")", resolve, StringComparison.Ordinal);
        Assert.Contains("FindProperty(target, \"PlaceholderText\")", resolve, StringComparison.Ordinal);
    }

    /// <summary>
    /// 重贴那一段<b>不许去敲下拉框的条目集合</b>——这条路已经试过并且证伪了。
    /// <para>背景：下拉框<b>闭着</b>的那一格显示的不是条目本身，而是选中那一刻从
    /// <c>ComboBoxItem.Content</c> 抄走的一份快照，所以重贴完 <c>Content</c> 之后屏幕上会看见
    /// "展开的列表已经是新语言、闭着的那格还写着旧语言"（批次 WI-2 覆盘照片，坑表 #274）。
    /// 第一反应是逼它重抄一次，即 <c>((IList&lt;object&gt;)combo.Items)[i] = item;</c>。真机跑回来的
    /// 不是修好，是<b>那颗下拉只要被打开过一次就抛</b> <c>COMException 0x800F1000
    /// （Element is already the child of another element）</c>——条目那时已经挂在弹出层里。
    /// 更糟的是它抛在重贴循环<b>中间</b>：<c>Locale.Current</c> 已经翻过去、屏幕还停在旧语言，
    /// 比原来那句"闭着的那格慢一步"严重一档（日志里三发 ERROR 就是这么来的）。</para>
    /// <para>所以这一格钉的是<b>别再走这条路</b>：要让闭合显示跟着语言走，只能改条目本身的形状
    /// （数据项＋模板，那一格才渲染得出活元素）——那是 P-169 的一格，不是一次原地 Replace。</para>
    /// </summary>
    [Fact]
    public void ReapplyingNeverTriesToForceTheDropdownToReSnapshotItsItems()
    {
        var loc = Code(ReadRepoFile(LocTextCs));
        var reapply = MethodBody(loc, "private static void ReapplyAll");
        Assert.DoesNotContain("Items", reapply, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionBox", reapply, StringComparison.Ordinal);
        // 那颗 helper 整个不许留下：留着它就算没人调用，下一次也会被人接回循环里。
        Assert.DoesNotContain("RefreshSelectionBox", loc, StringComparison.Ordinal);
    }

    /// <summary>
    /// 找那颗依赖属性标识符必须<b>字段与属性两条路都查</b>。
    /// <para>真机第一跑整窗文案空白（日志里 33 条"贴不上"）就是这么来的：WinUI 3 的控件是 CsWinRT 投影出来的，
    /// <c>TextBlock.TextProperty</c> 是静态<b>属性</b>，只有托管辅助类（<c>ToolTipService</c>）才是静态字段。
    /// 这一族 <c>StarMark.Tests</c> 引用不到 UI、反射又只能在运行期跑，所以只能钉<b>读法本身</b>：
    /// 它挡不住"两条路都写错"，但挡得住"只写了一条"——而那正是今天踩中的那个形状。</para>
    /// </summary>
    [Fact]
    public void TheDependencyPropertyLookupChecksBothFieldAndPropertyShapes()
    {
        var lookup = MethodBody(Code(ReadRepoFile(LocTextCs)), "FindProperty(DependencyObject");
        Assert.Contains("GetField(", lookup, StringComparison.Ordinal);
        Assert.Contains("GetProperty(", lookup, StringComparison.Ordinal);
        Assert.Contains("\"Property\"", lookup, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>贴不上必须出声</b>：上面那些形状判据的兜底。真机上那 33 处空白之所以能一步定位到反射读法，
    /// 靠的就是"找不到可写的那一颗"这条 WARN（连元素类型一起报）。把出声改成静默 return，
    /// 界面就退化成"整片空白而日志里什么都没有"。
    /// </summary>
    [Fact]
    public void AnUnwritableTargetSpeaksInsteadOfStayingBlank()
    {
        var loc = Code(ReadRepoFile(LocTextCs));
        var warn = MethodBody(loc, "private static void WarnUnwritableOnce");
        Assert.Contains("StarLog.Warn(", warn, StringComparison.Ordinal);
        Assert.Contains("GetType().Name", warn, StringComparison.Ordinal);   // 说得出是哪一类元素贴不上
        Assert.Equal(3, Count(loc, "WarnUnwritableOnce(d, "));   // 正文／占位符／开关两侧（那两颗共用一条出口）
        var switchPath = MethodBody(loc, "private static void PasteOntoNamedProperty");
        Assert.Contains("WarnUnwritableOnce(d, ", switchPath, StringComparison.Ordinal);
        Assert.Contains("没有 {propertyName}", loc, StringComparison.Ordinal);   // 说不出哪一颗属性贴不上＝白报
    }

    /// <summary>
    /// 「恢复默认快捷键」那颗确认框的三句字全部<b>按键推</b>，方法体里一句字面量都不许留（批次 WI-2e）。
    /// <para>它是坑表 #276 那一族在这一栏的落点：原先三句话写在代码里，其中一句还<b>抄了键位</b>
    /// （"只保留默认的 Ctrl + Alt + Space（切换主界面）"）。抄键位＝用户改过键之后那句成假话；
    /// 而"只保留一颗"本身就是错的——出厂默认里还有截图／贴图／穿透／画布那一整批，
    /// 恢复默认还原的是<b>每一行</b>。现在两件事都由表里那一行说。</para>
    /// <para>判据读的是<b>抹掉注释之后</b>的方法体：注释里写"原先那句抄了键位"是给人看的理由，
    /// 不是第二条还在用的出口（<c>Code()</c> 那一族的口径）。</para>
    /// </summary>
    [Fact]
    public void TheResetHotkeysConfirmPushesKeysInsteadOfNamingAGesture()
    {
        var body = MethodBody(Code(ReadRepoFile(SettingsHotkeysCs)), "private async void ResetHotkeys_Click");
        Assert.Contains("Locale.T(\"settings.hotkeys.reset\")", body, StringComparison.Ordinal);
        Assert.Contains("Locale.T(\"settings.hotkeys.resetConfirm\")", body, StringComparison.Ordinal);
        Assert.Contains("Locale.T(\"dialog.cancel\")", body, StringComparison.Ordinal);
        // 实参不许再是引号里的那句话（与"跳转实参不许是中文"同一形状）
        Assert.DoesNotContain("primaryText: \"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("cancelText: \"", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"""[^""\r\n]*[一-鿿]", body);
        Assert.DoesNotContain("Ctrl", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>VM 那句"键＋实参"到元素之间只许有一颗门</b>（<c>LocText.SetMessage</c>，批次 WI-2k）。
    /// <para>为什么要有这一格：这一族最坏的坏法不是"没翻"，是<b>翻了一半的两种各自漏法</b>——
    /// ① 页面自己拆 <c>Key</c>／<c>Args</c>，忘了一片就贴歪；② 忘了"没话"那一路，
    /// 留着上一次的注册项，换语言那一刻把<b>已经不该出现的那句</b>又贴回屏幕上。
    /// 这两个洞在十几个页面上各写一遍就会各漏一遍，所以门只许有一颗，且空键必须由那颗门处理。</para>
    /// </summary>
    [Theory]
    [InlineData("src/StarMark.UI/Views/TrendingPage.xaml.cs")]
    [InlineData("src/StarMark.UI/Views/RssPage.xaml.cs")]
    public void APagePushesItsViewModelMessageThroughOneDoor(string relativePath)
    {
        var code = Code(ReadRepoFile(relativePath));
        Assert.Contains("LocText.SetMessage(", code, StringComparison.Ordinal);
        // 拆成 SetKey ＋ Args 自己传＝那一页要自己记得"没话"怎么走；这一族只许走门。
        Assert.DoesNotContain("LocText.SetKey(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LocText.SetArgs(", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>状态行与空态那两颗属性交回的是键，不是拼好的句子</b>（批次 WI-2k 钉住这一族的形状）。
    /// <para>判据读抹掉注释后的代码：<c>StatusText = "…"</c> 这种整句赋值一旦出现，
    /// 语言就被定死在算出它的那一刻，而换语言那一刻<b>没有任何人回去重算</b>（坑表 #276）。
    /// 这一格拦的是"退回字符串"这个方向，不是拦某一句具体的话。</para>
    /// </summary>
    [Theory]
    [InlineData("src/StarMark.UI/ViewModels/TrendingPageViewModel.cs")]
    [InlineData("src/StarMark.UI/ViewModels/RssPageViewModel.cs")]
    public void TheStatusLinesHandBackKeysNotSentences(string relativePath)
    {
        var code = Code(ReadRepoFile(relativePath));
        Assert.Contains("[ObservableProperty] private LocMessage? _statusText;", code, StringComparison.Ordinal);
        Assert.Contains("[ObservableProperty] private LocMessage? _emptyHint;", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(StatusText|EmptyHint) = """, code);
        Assert.DoesNotMatch(@"(StatusText|EmptyHint) = .*\+\s*(ex\.Message|fullName|reason)", code);
    }

    /// <summary>
    /// <b>套娃那句必须一层都没取过词</b>（批次 WI-2k 的机制核心）：状态行的实参交出的还是 <c>LocMessage</c>，
    /// 不是 <c>.Render()</c>／<c>Locale.T(...)</c> 的结果。
    /// <para>一旦有人"顺手先把读数算成字符串"，外层跟着语言翻了、嵌进去那半句留在原地——
    /// 屏幕上就是半中半英，而形状闸门（上一格）看不见，因为它只看属性类型。</para>
    /// </summary>
    [Fact]
    public void NestedReadingsArePassedUnrendered()
    {
        var trending = Code(ReadRepoFile("src/StarMark.UI/ViewModels/TrendingPageViewModel.cs"));
        Assert.Contains(@"LocMessage.Of(""trending.statusCached"", via, age)", trending, StringComparison.Ordinal);
        Assert.DoesNotContain("age.Render()", trending, StringComparison.Ordinal);
        var rss = Code(ReadRepoFile("src/StarMark.UI/ViewModels/RssPageViewModel.cs"));
        Assert.Contains("RssFeedCache.GapMessage", rss, StringComparison.Ordinal);
        Assert.DoesNotContain(".Render()", rss, StringComparison.Ordinal);       // 界面那一路一个都不许先取词
    }
}
