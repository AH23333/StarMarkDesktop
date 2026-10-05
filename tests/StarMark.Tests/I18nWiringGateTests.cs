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
    private const string LocTextCs = "src/StarMark.UI/Helpers/LocText.cs";
    private const string SettingsI18nCs = "src/StarMark.UI/Views/SettingsPage.I18n.cs";
    private const string SettingsHotkeysCs = "src/StarMark.UI/Views/SettingsPage.Hotkeys.cs";
    private const string GuideUiCs = "src/StarMark.UI/Helpers/GuideUi.cs";
    private const string AppCs = "src/StarMark.UI/App.xaml.cs";

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

    /// <summary>UI 里被问到的所有键（三种附带属性＋代码里 <c>Locale.T</c>／<c>Fmt</c> 的字面量实参）。</summary>
    private static IEnumerable<(string Where, string Key)> AskedKeys()
    {
        foreach (var (path, text) in XamlUnder("src/StarMark.UI"))
        {
            foreach (Match m in Regex.Matches(Markup(text), @"i18n:LocText\.\w*Key=""([^""]+)"""))
                yield return (path, m.Groups[1].Value);
        }
        foreach (var (path, text) in ReadRepoUnder("src/StarMark.UI"))
        {
            foreach (Match m in Regex.Matches(Code(text), @"Locale\.(?:T|Fmt)\(""([^""]+)"""))
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
        Assert.True(asked.Count >= 40, $"UI 里只认出 {asked.Count} 处取词（现场量过是 46 处），抽取器大概没跑对");
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
        var askedLiterals = XamlUnder("src/StarMark.UI").Select(s => Markup(s.Text))
            .Concat(ReadRepoUnder("src/StarMark.UI").Select(s => Code(s.Text)))
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
    /// </summary>
    [Fact]
    public void TheWidgetMaterialListIsNotASecondCopy()
    {
        var page = Markup(ReadRepoFile(SettingsXaml));
        var block = Between(page, "i18n:LocText.Key=\"settings.widgets.materialLabel\"", "</ComboBox>");
        var asked = Regex.Matches(block, @"settings\.[a-z]+\.material\.\w+").Cast<Match>()
            .Select(m => m.Value).ToArray();

        Assert.Equal(6, asked.Length);
        Assert.All(asked, k => Assert.StartsWith("settings.general.material.", k, StringComparison.Ordinal));
        Assert.DoesNotContain("settings.widgets.material.", page, StringComparison.Ordinal);
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
    /// "此刻还没"，不是"以后才有"，所以那些词刻意不在清单里（把它们钉进去会把闸门拗成背字符串）。</para>
    /// </summary>
    [Fact]
    public void ShippedUiTextNeverPromisesAFutureBatch()
    {
        var offenders = (from e in UiStrings.All
                         from side in new[] { ("中文列", e.Zh), ("英文列", e.En) }
                         where Regex.IsMatch(side.Item2,
                             @"下一批|下一个版本|下批|后续版本|未来的版本|即将|待实现|暂未|尚未提供"
                             + @"|arrives? with the next|in a (?:later|future) (?:release|batch)|coming soon"
                             + @"|not (?:yet )?implemented|planned for",
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
        const string Attributes = @"(?:Text|Content|Header|OnContent|OffContent|ToolTip|PlaceholderText)=""([^""]*)""";
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
    /// <para>三个数字是现场量出来的（主窗：正文键 31／提示键 9／占位符键 2）。把它们钉住不是为了一次改文案
    /// 就要来改判据，而是因为"覆盖面"这件事本身只有数字能证——<c>settings.languageNote</c> 那句
    /// "这一版只覆盖主界面骨架"是产品对用户的承诺，它得有一个对应的数。</para>
    /// <para>剩下那一颗 <c>Text="索引就绪"</c> 是<b>登记在案的缺口</b>（不是漏翻）：那颗状态是代码在写的
    /// （同步中／取消中／失败都往同一颗 <c>TextBlock</c> 上推），给它挂键的后果不是"少翻一句"，
    /// 而是<b>换语言那一刻把"同步中…"覆盖成"索引就绪"</b>——一句谎话。要翻它得先把状态改成按键推，
    /// 那一刀在 WI-2。</para>
    /// </summary>
    [Fact]
    public void FirstMilestoneCoversTheMainWindowAndDeliberatelyLeavesOneDynamicString()
    {
        var xaml = ReadRepoFile(MainWindowXaml);
        Assert.Equal(31, Count(xaml, "i18n:LocText.Key=\""));
        Assert.Equal(9, Count(xaml, "i18n:LocText.ToolTipKey=\""));
        Assert.Equal(2, Count(xaml, "i18n:LocText.PlaceholderKey=\""));
        Assert.Equal(1, Count(xaml, "xmlns:i18n="));

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
        // 正文／提示／开关两侧各一条＝现场数过 5 颗（批次 WI-2d 给开关那两颗开的口）。
        // 这条计数<b>不是</b>为了让改动便宜：它是"每颗附带属性都得有自己的取词路"的证人——
        // 少一颗就是有一类元素贴不上而日志里不出声。
        Assert.Equal(5, Count(loc, "DependencyProperty.RegisterAttached"));
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
}
