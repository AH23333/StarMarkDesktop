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
        // 现场读数（2026-10-05 为批次 WI-2f 重测：全应用的开关标签 22 对，全住在设置页——已进表 11 对
        // ＝常规 5＋健康 2＋数据 4，还写死在 XAML 里 11 对＝AI 增强／桌面工具／拓展功能那三栏；
        // 组件窗与条目卡片上没有开关标签）：只钉下界——往后的每一批要把它往上推。
        Assert.True(Count(Markup(ReadRepoFile(SettingsXaml)), "i18n:LocText.OnContentKey=\"") >= 11,
            "开关两侧的标签要住在表里，不是写在 XAML 里");
    }

    /// <summary>
    /// 已经铺过的五栏（常规／使用说明／快捷键／数据／健康与诊断）<b>自己那份标记</b>里一句中文都不许剩
    /// （批次 WI-2d 立的收口判据，WI-2e 推到四栏，批次 WI-2f 推到五栏）。
    /// <para>判据分两头：① 这五栏的 <c>Text</c>／<c>Content</c>／<c>Header</c>／开关两侧／提示里不能再有汉字；
    /// ② 整页剩余量只钉<b>上界</b>——下一批要把它往下推，往上加才是事故。把等号钉在剩余量上，
    /// 等于让每一批改完都得先来改一次判据，那正是"闸门替改动让路"的形状。</para>
    /// <para>⚠ <b>这一格的射程止于标记</b>：同一屏上还有中文是<b>程序算出来或拼出来</b>的（顶栏那颗索引状态、
    /// 「关于与更新」里那行检查结果、快捷键这一栏每一行的动作名与键位、数据这一栏那几行状态读数与备份列表
    /// 每行的「恢复前快照」标签）——第一版覆盘照片拍到它们之后，名字就从"没有中文正文"改成了"自己那份标记"。
    /// 那几处要翻，得先把状态改成按键推（P-169 的一格），不是往这里再挂一颗键（坑表 #276）。</para>
    /// <para>⚠ 数的时候<b>跳过 <c>&lt;Run&gt;</c> 那一行</b>：那是"半个句子＋一个读数"的胶水，
    /// 它有自己的登记格（下面那一颗证人），把它算进这一格就等于宣布它已经翻了。</para>
    /// </summary>
    [Fact]
    public void TheFiveKeyedTabsHoldNoChineseMarkupAndTheRestOnlyShrinks()
    {
        const string Attributes = @"(?:Text|Content|Header|OnContent|OffContent|ToolTip|PlaceholderText)=""([^""]*)""";
        var page = Markup(ReadRepoFile(SettingsXaml));
        foreach (var (name, tag) in new[]
                 {
                     ("TabGeneral", "general"), ("TabGuide", "guide"),
                     ("TabHotkeys", "hotkeys"), ("TabData", "data"), ("TabHealth", "health"),
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
        Assert.True(whole <= 133, $"整页中文正文从 133 涨到了 {whole}（这一族只许往下减）");
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
