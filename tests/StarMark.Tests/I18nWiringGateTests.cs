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
[Collection("LocaleGlobal")]
public sealed class I18nWiringGateTests
{
    private const string MainWindowXaml = "src/StarMark.UI/MainWindow.xaml";
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string SettingsOptionsCs = "src/StarMark.UI/Views/SettingsPage.Options.cs";
    private const string WidgetAppearanceEditorCs = "src/StarMark.UI/Views/WidgetAppearanceEditor.cs";
    private const string TrendingXaml = "src/StarMark.UI/Views/TrendingPage.xaml";
    private const string SnapshotXaml = "src/StarMark.UI/Views/SnapshotPage.xaml";
    private const string RssXaml = "src/StarMark.UI/Views/RssPage.xaml";
    private const string ClipboardXaml = "src/StarMark.UI/Views/ClipboardPage.xaml";
    private const string TagsXaml = "src/StarMark.UI/Views/TagsPage.xaml";
    private const string FolderTreeXaml = "src/StarMark.UI/Views/FolderTreePage.xaml";
    private const string SearchPageXaml = "src/StarMark.UI/Views/SearchPage.xaml";
    private const string SearchPageCs = "src/StarMark.UI/Views/SearchPage.xaml.cs";
    private const string SearchWidgetXaml = "src/StarMark.UI/Views/SearchWidget.xaml";
    private const string SearchWidgetCs = "src/StarMark.UI/Views/SearchWidget.xaml.cs";
    private const string MainWindowSearchCs = "src/StarMark.UI/MainWindow.Search.cs";
    private const string MainWindowStatusCs = "src/StarMark.UI/MainWindow.Status.cs";
    private const string SyncCoordinatorCs = "src/StarMark.Core/Sync/SyncCoordinator.cs";
    private const string TrendingRepoCs = "src/StarMark.Abstractions/Trending/TrendingRepo.cs";
    private const string ThemeXaml = "src/StarMark.UI/Themes/StarMarkTheme.xaml";
    private const string LocTextCs = "src/StarMark.UI/Helpers/LocText.cs";
    private const string SettingsI18nCs = "src/StarMark.UI/Views/SettingsPage.I18n.cs";
    private const string SettingsHotkeysCs = "src/StarMark.UI/Views/SettingsPage.Hotkeys.cs";
    private const string GuideUiCs = "src/StarMark.UI/Helpers/GuideUi.cs";
    private const string CanvasToolbarCs = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml.cs";
    private const string WeatherWidgetCs = "src/StarMark.UI/Views/WeatherWidget.xaml.cs";
    private const string CountdownWidgetCs = "src/StarMark.UI/Views/CountdownWidget.xaml.cs";
    private const string CalcWidgetCs = "src/StarMark.UI/Views/CalcWidget.xaml.cs";
    private const string GlanceWidgetCs = "src/StarMark.UI/Views/GlanceWidget.xaml.cs";
    private const string SettingsPageCs = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string AppCs = "src/StarMark.UI/App.xaml.cs";
    // 组件窗自己的三条文字出口（批次 WI-37-A）：菜单本体／外壳两态／快捷启动拖放，加内容工厂的占位那一行。
    private const string WidgetMenuCs = "src/StarMark.UI/Views/WidgetWindow.Menu.cs";
    private const string WidgetChromeCs = "src/StarMark.UI/Views/WidgetWindow.Chrome.cs";
    private const string WidgetDropCs = "src/StarMark.UI/Views/WidgetWindow.QuickLaunchDrop.cs";
    private const string WidgetFactoryCs = "src/StarMark.UI/Services/WidgetContentFactory.cs";

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
            // 批次 WI-2n：提示条与占位符<b>随状态变</b>的那一族，主人只能是代码（代码交键、不交字，见
            // <see cref="AWiredElementNeverGetsItsPropertyWrittenFromCodeToo"/>）。⛔ 不补这一臂，
            // "把键交给 <c>SetToolTipKey</c>"这种写法在正向对账里全程隐身，拼错也没人问。
            // ⚠ 抽法按<b>行</b>收而不按"第二个实参紧跟逗号"收：键可以写成三元式
            // （<c>snapshot.IsPlaying ? "widget.music.pause" : "widget.music.play"</c>——台架 N9 的该红没红就是它），
            // 所以把那一行里所有<b>键形</b>字面量都当取词问一遍；非键形的串（自然语句）过不了那颗形状门。
            foreach (var line in code.Split('\n'))
            {
                if (!line.Contains("LocText.Set", StringComparison.Ordinal)) continue;
                foreach (Match m in Regex.Matches(line, @"""([^""\n]+)"""))
                {
                    var value = m.Groups[1].Value;
                    if (Regex.IsMatch(value, @"^[a-z][a-z0-9]*(\.[A-Za-z0-9]+)+$", RegexOptions.CultureInvariant))
                        yield return (path, value);
                }
            }
            // 键名住在 const 里的那些（`MinutesAgoKey = "time.minutesAgo"`／`FollowSystemKey`）：
            // 取词调用写的是 `Locale.Fmt(MinutesAgoKey, count)` 这类<b>形参不是字面量</b>的形状（✅WI-65 片5c 起
            // 那四颗档出口改成交键，这一臂留给的读者是 <c>FollowSystemKey</c> 那族还没搬完的），上面那几臂按<b>字面量实参</b>认，一律看不见 ⇒
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
            // 批次 WI-52：外观编辑器那一族的私有包装（<c>MakeLabel</c>／<c>MakeButton</c>／<c>MakeSlider</c>／
            // <c>MakeObjectButton</c>）把交键这一步吞进了方法体——调用行上既没有取词出口也没有 <c>LocText.</c>，
            // ⛔ 只认 <c>LocText.Set</c> 那一臂就看不见这几颗键（坑表 #375 的第二面：包装的实参是尺子的盲区）。
            // 与 <c>LocText.Set</c> 那一臂同法：按<b>行</b>收那一行里所有<b>键形</b>字面量（"bg"／"fg" 这类身份串过不了形状门）。
            foreach (var line in code.Split('\n'))
            {
                if (!Regex.IsMatch(line, @"Make(?:Label|Button|Slider|ObjectButton)\(")) continue;
                foreach (Match m in Regex.Matches(line, @"""([^""\n]+)"""))
                {
                    var value = m.Groups[1].Value;
                    if (Regex.IsMatch(value, @"^[a-z][a-z0-9]*(\.[a-z][A-Za-z0-9]*)+$", RegexOptions.CultureInvariant))
                        yield return (path, value);
                }
            }
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
    /// 材质那一族<b>只许有一份措辞</b>：三颗下拉（设置页主窗那一格／设置页组件那一格／「组件外观」编辑器那一格）
    /// 复用同一批 <c>settings.general.material.*</c>，⛔ 再造一份同名行或另抄六个短名。
    /// <para>症状形状：同一句字两个出处之后，改一漏一——换语言时主窗口翻了、组件那一栏没翻，
    /// 而两栏的<b>中文</b>看起来都还在（因为键不同、对账的两颗闸门各看各的）。</para>
    /// <para><b>批次 WI-2c</b> 把这六行从标记搬进 <c>MaterialOptions</c>（闭着的那格要跟着语言走，
    /// 只有"数据项＋模板"这一条路）；<b>批次 WI-52（P-183）</b>又把它们从设置页的<b>实例属性</b>搬进
    /// <c>WidgetMaterialWords.SixTiers</c>——第三颗下拉建在 <c>WidgetAppearanceEditor</c> 里，够不到实例，
    /// 于是它当年自己抄了六个短名（亚克力／云母／不透明…）。判据守的那件事两次都没变，
    /// 变的都是它读的那份文件与那段形状——这正是坑表 #286 那一族：锚点只认旧形状时，改对了反而会红。</para>
    /// </summary>
    [Fact]
    public void TheWidgetMaterialListIsNotASecondCopy()
    {
        var options = Code(ReadRepoFile(SettingsOptionsCs));
        var block = Between(options, "LocOption[] SixTiers", "];");
        var asked = Regex.Matches(block, @"settings\.[a-z]+\.material\.\w+").Cast<Match>()
            .Select(m => m.Value).ToArray();

        Assert.Equal(6, asked.Length);
        Assert.All(asked, k => Assert.StartsWith("settings.general.material.", k, StringComparison.Ordinal));
        Assert.DoesNotContain("settings.widgets.material.", options, StringComparison.Ordinal);

        // 两颗下拉都得指向这一份清单——只有一份清单、却有人自带列表，就等于回到两个出处。
        var page = Markup(ReadRepoFile(SettingsXaml));
        Assert.Equal(2, Count(page, "ItemsSource=\"{x:Bind MaterialOptions, Mode=OneTime}\""));

        // 第三颗下拉（外观编辑器）也收同一份：它⛔ 出现任何一颗材质键，只许点那份清单的名。
        var editor = Code(ReadRepoFile(WidgetAppearanceEditorCs));
        Assert.Equal(0, Count(editor, "settings.general.material."));
        Assert.Contains("WidgetMaterialWords.SixTiers", editor, StringComparison.Ordinal);
    }

    /// <summary>
    /// 下拉里的项数<b>与枚举的成员数必须是同一个数</b>（材质三档／性能三档那两处的"档位表只能有一份"）。
    /// <para>为什么不是写死 6 与 3：写死的数本身就是第二个出处——加一档材质改了枚举没改清单，
    /// 屏幕上就少一格、而设置档里那颗 5 会安静地指到不存在的东西上（坑表 #291：同一个数有两个出处
    /// 比没有出处更坏）。这一格从<b>类型现场</b>抓数再对账，⛔ 不把那个数抄进判据。</para>
    /// </summary>
    [Fact]
    public void TheMaterialOptionListHasOneRowPerBackdropKind()
        => AssertOneRowPerEnumMember("LocOption[] SixTiers", typeof(StarMark.Abstractions.WidgetBackdropKind));

    [Fact]
    public void ThePerformanceOptionListHasOneRowPerMode()
        => AssertOneRowPerEnumMember("IReadOnlyList<LocOption> PerformanceOptions", typeof(StarMark.Core.Performance.PerformanceMode));

    /// <param name="listMarker">那份清单在源文件里的<b>定位串</b>（原文照抄，⛔ 凭记忆打——缩进错一格那一格就没读）。
    /// 材质那六颗住在 <c>LocOption[] SixTiers</c>（批次 WI-52 起的家），性能那三颗还住在实例属性里。</param>
    private static void AssertOneRowPerEnumMember(string listMarker, Type enumType)
    {
        var block = Between(Code(ReadRepoFile(SettingsOptionsCs)), listMarker, "];");
        var rows = Regex.Matches(block, @"LocOption\.T\(""[^""\s]+""").Count;
        Assert.Equal(Enum.GetNames(enumType).Length, rows);
    }

    /// <summary>
    /// <b>选项不再躺在标记里</b>：本批扫过的两份标记里不许再有一颗 <c>&lt;ComboBoxItem&gt;</c>，
    /// 而凡是指定了 <c>ItemsSource</c> 的下拉<b>必须同时</b>指定那颗共用的 <c>ItemTemplate</c>。
    /// <para>合取的另一半才是这一格的存在理由：只挂 <c>ItemsSource</c> 不挂模板，WinUI 会去
    /// <c>ToString()</c> 那颗数据项——屏幕上是一整排 <c>LocOption { Key = settings…, … }</c>。
    /// 编译过、测试全绿、启动不抛，只有眼睛看得见（坑表 #180 同族）。</para>
    /// <para><b>射程说清楚</b>：主窗、设置页、热榜那颗「今日／本周／本月」与搜索组件那颗排序下拉
    /// （✅批次 WI-67 片9b 收掉 <c>SearchWidget.xaml</c> 那三颗 <c>&lt;ComboBoxItem Content="中文"&gt;</c>，
    /// 那份在案豁免同时收回 ⇒ 下面 <see cref="WidgetMarkupIsCoveredAndTheLeftoversAreNamed"/> 的第②类归零）。</para>
    /// </summary>
    [Theory]
    [InlineData(MainWindowXaml)]
    [InlineData(SettingsXaml)]
    [InlineData(TrendingXaml)]
    [InlineData(SearchWidgetXaml)]
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
    [InlineData(SearchWidgetXaml)]   // ✅批次 WI-67 片9b 加进来：那一页的标记（三颗下拉项＋那颗空态字面量）同批改完才配加这一臂
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
    /// 那颗共用模板<b>不许把内容交进列 0</b>——列 0 是 <c>ComboBoxItem</c> 自己的：选中那一行时它把"勾"画在那里。
    /// <para>形状：模板根是一颗裸 <c>TextBlock</c> 时，文字与那颗勾叠在同一列，选中项看起来就是"选项前面多了一个记号、
    /// 和控件自己的那个记号重复"（批次 WI-43，他这轮的另一条反馈报的；⛔ 这里不写引语——那条消息的逐字在本机转写里取不到，
    /// 而这件事⛔ 不需要人证：下面那些位置就是它的全部射程。这颗模板被 <b>10</b> 处下拉共用 ⇒ 一处坏＝
    /// 主窗两颗、设置页七颗、热榜一颗<b>一起坏</b>，而他先看到的是护眼那一颗）。</para>
    /// <para>判据只看结构：模板根必须自己分列，且<b>每一颗</b>会画东西出来的元素都显式站第二列。
    /// ⛔ 用 <c>Margin</c>／<c>Padding</c> 把文字顶开是遮丑——列 0 一被占住，勾照样叠上来，
    /// 所以这里数的是"画得出来的元素颗数 == 显式落第二列的颗数"，不是缩进好不好看。</para>
    /// </summary>
    [Fact]
    public void TheSharedOptionTemplateKeepsTheFirstColumnForTheControlsOwnCheckMark()
    {
        var theme = Markup(ReadRepoFile(ThemeXaml));
        const string open = "<DataTemplate x:Key=\"LocOptionTemplate\">";
        var template = Between(theme, open, "</DataTemplate>")[open.Length..].Trim();
        var root = Regex.Match(template, @"^\s*<(\w+)");
        Assert.True(root.Success, "抽不出这颗模板的根元素（形状变了，这一格先跟上）");
        Assert.Equal("Grid", root.Groups[1].Value);
        Assert.Contains("ColumnDefinitions=\"Auto,*", template, StringComparison.Ordinal);

        var drawn = Regex.Matches(template, @"<(TextBlock|FontIcon|SymbolIcon|ContentPresenter)\b").Cast<Match>()
            .Select(m => m.Groups[1].Value).ToList();
        Assert.True(drawn.Count >= 1, $"模板里一颗可见元素都没认到（抽取器没跑对）");
        var placed = Regex.Matches(template,
            @"<(?:TextBlock|FontIcon|SymbolIcon|ContentPresenter)[^>]*?Grid\.Column=""1""",
            RegexOptions.Singleline).Count;
        Assert.Equal(drawn.Count, placed);
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
    /// <b>标记里不许挂静态键</b>：挂了键，换语言那一刻静态那句会盖掉带值的这句，
    /// 用户看到的就是一个和实际要改的东西不一样的名字。
    /// <para>这一格从前钉的是<b>登记在案的缺口</b>（"组合句"那一族还没有按语言重算的机制，字只能留在 code-behind）。
    /// 批次 WI-41 把机制接上了——两颗现在走 <c>LocText.SetMessage</c> ＋<b>没渲染的</b>实参，
    /// 换语言那一刻按新语言重取词、重填空。⚠ 换的是<b>后半那两条断言</b>⛔ 把这一格删掉：
    /// 前半（标记里⛔ 静态键）今天仍然成立，而且比原来更要紧——表里现在真有那两颗键，挂进标记就成了第二份出处。</para>
    /// </summary>
    [Fact]
    public void TheTwoComposedButtonLabelsPasteWithTheirArgs()
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
        Assert.Contains("LocText.SetMessage(AiFillModelButton, LocMessage.Of(\"settings.ai.fillModel\"", page);
        Assert.Contains("LocText.SetMessage(AiResumeButton, plan.IsEmpty ? LocMessage.Of(\"settings.ai.resumeIdle\")", page);
        Assert.Contains("LocMessage.Of(\"settings.ai.resumeCount\", plan.ItemCount)", page);
        // 回来路＝把值抄进字面量：那一格换语言不再重算，屏幕上留着上一次那个模型名／条数。
        Assert.DoesNotContain("AiFillModelButton.Content = \"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AiResumeButton.Content = \"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// 已经 shipped 的界面文本<b>不许承诺"这一版还没有、以后才有"</b>（坑表 #297）。
    /// <para>这一族最坏的地方是它<b>只会变旧、不会变红</b>：「AI 增强」那句"批量整理标签的入口在下一批接上"
    /// 是批次 A 写的，入口在批次 B 就接上了，而屏幕上那句话一直挂着；到批次 WI-2i 搬字进表时，
    /// 它已经从"标记里的一句过期路线图"变成"表里唯一的出处"——再逐字照抄一次就等于把它锁死。</para>
    /// <para>判据按<b>句式</b>写（中文那几种说法＋英文那几种对译），⛔ 不是只钉今天这一句：
    /// 换个措辞（"下个版本见"／"coming soon"）也在这条射程里——台架按中英两种措辞各咬一次（J1／J2）。
    /// ⚠ 这一格管的是<b>承诺</b>，不管状态读数：状态行那类"此刻还没"的句子（自检没完成、用量还没读出来）
    /// 说的是"此刻还没"，不是"以后才有"，所以那些词刻意不在清单里（把它们钉进去会把闸门拗成背字符串）。
    /// <para>批次 WI-2c 又删掉两句这种承诺（中文那句"那是后面一批的事"、英文那句 "a later pass"），
    /// 而<b>这两种措辞当时都不在词表里</b>——旧射程只认"下一批／in a later release"。
    /// 所以删话的同一批要把词表补宽：只改界面不改闸门，下一次换个词再写一遍承诺照样绿。</para></para>
    /// </summary>
    [Fact]
    public void ShippedUiTextNeverPromisesAFutureBatch()
    {
        var offenders = (from e in UiStrings.All
                         from side in new[] { ("中文列", e.Zh), ("英文列", e.En), ("日文列", e.Ja) }
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
        Assert.True(whole == 0, $"整页中文正文从 0 涨到了 {whole}（组合句一族健康页已全清（WI-32/33）——标记里一个字都不该留）");
    }

    /// <summary>
    /// 「健康与诊断」里<b>扣分那一行</b>的胶水＝仍在案的缺口；四行统计的胶水已交键（批次 WI-32）。
    /// <para>缺口不是漏翻，是"半个句子＋程序读数"的组合句一族要整链翻（扣分行的因子名与明细文案还住在
    /// Core 的成品串里，等组合句的下半刀）；统计四行已改成"整句进表＋读数进实参"（health.stat.*），
    /// 本格同时负账（只剩这一句在案）与正账（四颗键真的被 VM 用着）——翻掉的顺带来改短清单，
    /// 清单里的句子从闸门口径里消失＝缺口这件事被藏起来了，红给你看是刻意。</para>
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
        Assert.Empty(glue);                                       // 负账清零（WI-33）：扣分行整句住 VM 现装
        var vm = ReadRepoFile("src/StarMark.UI/ViewModels/SettingsPageViewModel.Health.cs");
        foreach (var key in new[] { "health.stat.lang", "health.stat.domains", "health.stat.dup", "health.stat.tag", "health.deduction.row", "health.summary.todo", "health.dup.item" })
            Assert.Contains($"Locale.Fmt(\"{key}", vm, StringComparison.Ordinal);   // 正账：整句都从表出（统计＋扣分半链同闸）
    }

    /// <summary>
    /// 第一里程碑<b>铺到哪一行、故意留在哪一行</b>，都要当场看得见。
    /// <para>三个数字是现场量出来的（主窗标记：正文键 <b>26</b>／提示键 8／占位符键 2；批次 WI-2c 之前是
    /// 31／9／2，少掉的那 5＋1 颗是排序档——选项改成"数据项＋模板"之后它们住在
    /// <c>MainWindow.Search.cs</c> 的 <c>SortOptions</c> 里，所以这一格同批补了一条<b>配对账</b>：
    /// 标记里少的颗数必须等于代码里多的颗数，⛔ 不许"搬着搬着少了一颗"。
    /// 批次 WI-2n 又收掉一颗（<c>FileKindButton</c>：它的字面随"选中了几个类型"变，改由代码交键＝
    /// <see cref="AWiredElementNeverGetsItsPropertyWrittenFromCodeToo"/> 抓出来的第 3 颗两个主人）⇒ 27 → 26。
    /// 把它们钉住不是为了一次改文案就要来改判据，而是因为"覆盖面"这件事本身只有数字能证——
    /// <c>settings.languageNote</c> 那句"这一版只覆盖主界面骨架"是产品对用户的承诺，它得有一个对应的数。）</para>
    /// <para>曾经剩下那一颗 <c>Text="索引就绪"</c> 是<b>登记在案的缺口</b>（不是漏翻）：那颗状态是代码在写的
    /// （同步中／取消中／失败都往同一颗 <c>TextBlock</c> 上推），当时给它挂键的后果不是"少翻一句"，
    /// 而是<b>换语言那一刻把"同步中…"覆盖成"索引就绪"</b>——一句谎话。
    /// 批次 WI-2m 按当时写的补法把那三个读数改成按键推（<see cref="TheTopBarStatusLineGoesThroughTheDoor"/>），
    /// 于是标记里那颗静态字面量一起摘掉：主窗标记的中文<b>归零</b>。</para>
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
        // 这一格是<b>零格</b>：主窗标记里再不留一句中文。将来谁写回去，它给的是"哪一句"而不是"少了几个"。
        Assert.Equal(Array.Empty<string>(), stillLiteral);
    }

    /// <summary>
    /// <b>顶栏那一行状态只许走"键＋读数"那一颗门</b>（批次 WI-2m，把 WI-1 留在案上的最后一颗静态状态字摘掉）。
    /// <para>那颗 <c>TextBlock</c> 现在挂着 <c>LocText.Key="top.indexReady"</c>（常态那句）。代码若还像以前那样
    /// 直接写 <c>StatusText.Text = "同步中..."</c>，注册项里留着的仍是<b>标记那颗键</b>：
    /// 换语言那一刻界面会把"同步中…"重贴成"索引就绪"＝界面替一件还在跑的事改口（坑表 #295 的镜像，
    /// 而这一次是<b>静态那句盖掉进行时那句</b>）。所以标记与代码两头都要钉：标记不许留字面量，
    /// 代码不许绕过那颗门，而那颗门在 <c>SetStatusText</c> 里只许出现一次（⛔ 不是数一下"看起来差不多"）。</para>
    /// <para>扫描面是<b>整个 <c>src/StarMark.UI</c></b>，不是主窗那一个 partial：<c>MainWindow</c> 现在有六份
    /// partial，直写落在哪一份都一样坏，而按文件名点名的判据在有人<b>新搬一份 partial</b> 那一刻就失去射程。
    /// ⚠ 那颗正则必须带"前面不是标识符字符"的排除——设置页另有一颗 <c>AiStatusText</c>（它属于 P-172
    /// 那一族、本批没动），按子串认会把它的十几处直写算进这一格，红的是一句假话（坑表 #293 同族）。</para>
    /// </summary>
    [Fact]
    public void TheTopBarStatusLineGoesThroughTheDoor()
    {
        var xaml = Markup(ReadRepoFile(MainWindowXaml));
        Assert.Contains("<TextBlock x:Name=\"StatusText\" Style=\"{StaticResource MutedText}\" i18n:LocText.Key=\"top.indexReady\"/>",
            xaml, StringComparison.Ordinal);

        var direct = new Regex(@"(?<![A-Za-z0-9_])StatusText\.Text");
        var offenders = ReadRepoUnder("src/StarMark.UI")
            .Where(f => direct.IsMatch(Code(f.Text)))
            .Select(f => f.RelativePath)
            .ToArray();
        Assert.Equal(Array.Empty<string>(), offenders);   // 直接改字＝注册项不知情

        var status = Code(ReadRepoFile(MainWindowStatusCs));
        Assert.Equal(1, Count(status, "LocText.SetMessage(StatusText,")); // 那颗门只许一处
        Assert.True(Count(status, "SetStatusText(") >= 5,
            "顶栏状态那一族的出口少于 5 处（现场＝取消中／同步中／汇总／已取消／失败 各一次＋那颗 helper 的定义）");
        Assert.Contains("summary.Message()", status, StringComparison.Ordinal);  // Core 交回的是没取词的那句
    }

    /// <summary>
    /// <b>同步汇总那句得住键，不许在 Core 里拼成品</b>（批次 WI-2m）。
    /// <para>症状不是"没翻"，是<b>那一行的语言被钉死在同步完成的那一刻</b>：拼好字符串之后换语言，
    /// 界面上那句就永远不动（坑表 #276）。读数是实参、句子是键，两件事各归一处；
    /// 旧的 <c>FormatText()</c> 一起删掉——留着它就是给"就地拼一句"留一条回头路。</para>
    /// </summary>
    [Fact]
    public void TheSyncSummaryHandsWithKeysNotWithAComposedSentence()
    {
        var core = Code(ReadRepoFile(SyncCoordinatorCs));
        var message = MethodBody(core, "public LocMessage Message()");
        Assert.Contains("LocMessage.Of(\"top.syncDone\", TotalPulled, ElapsedMs)", message, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"top.syncPartial\", TotalPulled, FailedCount, ElapsedMs)", message,
            StringComparison.Ordinal);
        Assert.Equal(0, Count(core, "FormatText"));
        Assert.DoesNotContain("同步完成:", core, StringComparison.Ordinal);
        Assert.DoesNotContain("同步部分失败:", core, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>周期那一个词只许有一个主人＝表</b>（批次 WI-2m 收掉批次 WI-2l 在屏幕上量到的那一处）。
    /// <para>现场：英文屏读到「JavaScript · ★ 77,054 · ＋4,242 <b>本周</b>」，日文屏读到同一句中文。
    /// 措辞住在 <c>TrendingRepo.cs</c> 的 <c>Label</c> 里，而表里 <c>trending.weekly</c> 另有其话
    /// （<c>This week</c>／<c>今週</c>，那颗周期下拉问的就是它）——<b>同一句字两个主人，两份可各自被改</b>，
    /// 比"没出处"更坏（P-171；坑表 #291）。修法是把那一族退回"只问表"。</para>
    /// </summary>
    [Fact]
    public void ThePeriodWordHasExactlyOneOwner()
    {
        var periods = Code(ReadRepoFile(TrendingRepoCs));
        var label = MethodBody(periods, "public static string Label(TrendingPeriod period)");
        Assert.Contains("Locale.T(period switch", label, StringComparison.Ordinal);
        foreach (var word in new[] { "今日", "本周", "本月" })
            Assert.DoesNotContain(word, label, StringComparison.Ordinal);
        // 存进设置档与缓存的必须是代码（daily／weekly／monthly），⛔ 不是人话——换语言不能改坏存着的东西
        Assert.Contains("\"weekly\"", Code(ReadRepoFile(TrendingRepoCs)), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>组件窗这一族第一次有覆盖面证人</b>（批次 WI-2n）。
    /// <para>为什么非立不可：设置页与主窗各有一颗"数还剩几句中文"的证人，而<b>组件窗这一族一格都没有</b>——
    /// "这一族翻没翻"在三批里只能靠我读码说一句"应该都挂了"，那种话没有数字撑着就是悬空的。</para>
    /// <para><b>射程按文件名形状认</b>（含 <c>Widget</c> 的 <c>.xaml</c>＝现场 15 颗：14 颗组件＋
    /// <c>WidgetWindow.xaml</c> 那颗宿主；其中 <c>ClockWidget.xaml</c> 面上一句可翻的话都没有，见下面那颗断言）。
    /// 新添一颗组件不必改判据就会落进这一格（⛔ 不许改成点文件名单——#318 那条教训的另一面：
    /// 名单式射程会在"搬个家／新加一颗"时静默失效）。</para>
    /// <para>⛔ <b>下面那 3 句是登记在案的缺口，不是漏翻</b>（批次 WI-53 之前是 17 句，天气那 4 句已交键；
    /// 批次 WI-56 收掉速览那 4 句示例字、批次 WI-67 片3 收掉条目格那 2 句、✅批次 WI-67 片9b 收掉搜索组件那 4 句）。
    /// 那个数是<b>这条断言自己数的</b>：尺＝"文件|属性|值"三件一体去重点名。
    /// 两类各有各的坏法：
    /// <b>①</b>3 句是<b>代码当场写过同一个属性</b>的（<c>MusicWidget.xaml</c> 的「未在播放」与「打开任意播放器后…」）——
    /// 给这类挂静态键的后果不是少翻一句，
    /// 是<b>换语言那一刻用默认那句盖掉带值那句</b>（屏幕上写着"2026年9月"而那天是 10 月＝一句当场可见的假话，
    /// 坑表 #276／#295）；要翻它们得先让宿主交出 <c>LocMessage</c>＝P-172。<b>②</b>搜索组件那颗排序下拉的三颗
    /// <c>ComboBoxItem</c> 本批走的是 WI-2c 那条正路（<b>数据项＋模板</b>，见上面
    /// <see cref="DropdownOptionsLiveInCodeNotInTheMarkup"/>）：从前只挂键＝"展开的列表翻了、闭着那格不翻"的半接状态
    /// （#272／#275）⇒ 这一类今天<b>归零</b>，⛔ 有人再把选项写回标记。</para>
    /// <para>名单<b>双向</b>钉（与日文列那颗同一口径）：多一句＝有人写回中文，少一句＝有人翻掉了却没来把这条改短。</para>
    /// </summary>
    [Fact]
    public void WidgetMarkupIsCoveredAndTheLeftoversAreNamed()
    {
        var widgets = XamlUnder("src/StarMark.UI")
            .Where(f => Path.GetFileName(f.RelativePath).Contains("Widget", StringComparison.Ordinal))
            .ToList();
        Assert.True(widgets.Count >= 15,
            $"组件窗标记只扫到 {widgets.Count} 个文件（现场 15 个：14 颗组件＋WidgetWindow.xaml 那颗宿主）");

        var all = string.Join("\n", widgets.Select(f => Markup(f.Text)));
        // 47：批次 WI-67 片9c 第四刀把「快捷启动可以拖」那句提示挂上标记（代码只在提权会话里换它，走同一颗门）。
        // ⚠ 音乐那两句<b>没</b>挂上去——那一行写的是当前曲目，代码要写同一颗属性（见下面那颗成对闸门）。
        Assert.Equal(47, Count(all, "i18n:LocText.Key=\""));
        // 20 而不是 22：批次 WI-2n 收掉两颗<b>随状态变</b>的提示条（播放/暂停、置顶/取消置顶）。
        // 它们在标记上挂静态键＝给同一颗按钮开第二个主人（代码那句带状态的会被静态那句盖掉），
        // 改由代码交键；判据见 <see cref="AWiredElementNeverGetsItsPropertyWrittenFromCodeToo"/>。
        Assert.Equal(20, Count(all, "i18n:LocText.ToolTipKey=\""));
        Assert.Equal(9, Count(all, "i18n:LocText.PlaceholderKey=\""));
        // 用了键的文件必须自己声明那颗命名空间（缺了＝那一颗组件整个不翻，而编得过也跑得起来）。
        // ⛔ 不是"每个文件都要声明"：<c>ClockWidget.xaml</c> 面上只有数字与指针，一句可翻的话都没有，
        // 给它补一颗没人用的 xmlns 是凑数。
        var needing = widgets.Count(f => Markup(f.Text).Contains("i18n:LocText.", StringComparison.Ordinal));
        Assert.Equal(needing, Count(all, "xmlns:i18n="));

        var left = widgets.SelectMany(f => Regex.Matches(Markup(f.Text), @"<(?:[A-Za-z][\w.]*)([^>]*)>",
                    RegexOptions.Singleline).Cast<Match>()
                .SelectMany(tag => Regex.Matches(tag.Groups[1].Value,
                    @"(ToolTipService\.ToolTip|ToolTip|Title|Header|Content|Text|Message|Description|PlaceholderText)=""([^""]*)""")
                    .Cast<Match>().Where(v => Regex.IsMatch(v.Groups[2].Value, @"[\u4e00-\u9fff]"))
                    .Select(v => $"{Path.GetFileName(f.RelativePath)}|{v.Groups[1].Value}|{v.Groups[2].Value}")))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        // 空表＝组件标记那一族⛔ 再有一句写死的正文。批次 WI-67 片9c 第四刀收掉了原先点名的三颗
        // （音乐那两句空态由 <c>MusicWidget.Render()</c> 走 <c>LocText.SetMessage</c> 交键——那两个元素代码要写当前曲目，
        // ⛔ 同时在标记上挂静态键，见上面那颗成对闸门；快捷启动那句提示挂上了标记）。
        Assert.Empty(left);
        // 批次 WI-53（P-182a）收掉 WeatherWidget.xaml 那 4 句：它们每一句都由 code-behind 当场重写
        // （Loaded 的 ApplyToggleLabels／Render／ShowEmpty）⇒ XAML 那份字面量是"没人读的副本"，
        // 删掉它而不是给它挂静态键（挂了键＝换语言把带值那句盖回去，正是上面第①类的那颗假话）。
        Assert.DoesNotContain(left, x => x.StartsWith("WeatherWidget.xaml", StringComparison.Ordinal));
        // 批次 WI-56（P-186）收掉 GlanceWidget.xaml 那 4 句示例字（"2026年9月"×2／"星期五"／"农历八月初八"）：
        // 同一颗形状——四格的值都由 <c>RefreshDate()</c>／<c>RebuildCalendar()</c> 每次重写，
        // 而农历那一行从此<b>按语言当场算</b>（那句「农历…」现在住表里，示例字留着就是第二个主人）。
        Assert.DoesNotContain(left, x => x.StartsWith("GlanceWidget.xaml", StringComparison.Ordinal));
        // 批次 WI-67 片3 收掉 ItemGridWidget 那两句（「暂无条目」与「输入标签名…」）：同一颗形状——两处都由 code-behind
        // 每次重写 ⇒ 删掉标记里那份字面量、由宿主交「键＋实参」走重贴名单（挂了静态键＝换语言把空态那句盖回"暂无条目"）。
        Assert.DoesNotContain(left, x => x.StartsWith("ItemGridWidget.xaml", StringComparison.Ordinal));
        // ✅批次 WI-67 片9b 收掉 SearchWidget 那 4 句（三颗排序下拉项＋那颗空态兜底句）：两类各有正路——
        // 下拉那三颗改「数据项＋模板」（措辞进表、身份留 ASCII 键），空态那句改由宿主交 LocMessage 走重贴名单。
        // 这一格留在这里是防"退回标记里的字面量"：那种写法编得过、跑得起来、展开的列表还翻得好，只有闭着那一格说假话。
        Assert.DoesNotContain(left, x => x.StartsWith("SearchWidget.xaml", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>挂了键的那颗元素，同一颗属性只许一个主人</b>（批次 WI-2n 的第二颗证人）。
    /// <para>这一族的坏法不是"少翻一句"，是<b>两份可各自被改的出处</b>（P-171 那种"有两个出处比没出处更坏"
    /// 的界面版）。现场抓到三颗：组件窗两颗（<c>PlayButton</c> 的提示条由代码按"放没放"交"暂停／播放"，
    /// <c>PinButton</c> 钉着时代码说"取消置顶"＝#323 那颗写入形状普查读出来的），主窗一颗
    /// （<c>FileKindButton</c> 的字面随"选中了几个类型"变——这一颗是判据放宽到词边界之后
    /// <b>净树上自己红出来的</b>，证明"按标记形状解析落点"那一步不能省）。
    /// 后果不是难看：<b>换语言那一刻静态那句盖掉带状态那句</b>——屏幕上那颗按钮在播放中写着"播放"，
    /// 是一句当场可见的假话（坑表 #295 同一族），而 5480 格测试里没有任何一格看得见。</para>
    /// <para><b>射程按"那颗 <c>x:Name</c> 的字段住在谁家里"收</b>：读那颗类的<b>全部 partial</b>，
    /// ⛔ 不按名字扫整个项目——<c>SearchBox</c> 在主窗与搜索组件各有一颗，按名字扫会把邻居算成违规
    /// （#318 的假红那一面：判据必须排除同名邻居）。</para>
    /// <para><b>无名元素不在这一格的射程里</b>：代码引用不到它，写不出第二个主人；那一族由真机照片守。
    /// <b>写"另一颗属性"不算违规</b>：输入框的 <c>PlaceholderKey</c> 与代码写的 <c>.Text</c> 是两回事
    /// （<c>ExprBox.Text = entry.Expression</c> 是历史回填，合法），所以判据按<b>键那一颗键实际会贴的属性</b>配对。</para>
    /// </summary>
    [Fact]
    public void AWiredElementNeverGetsItsPropertyWrittenFromCodeToo()
    {
        var offenders = new List<string>();
        var watched = 0;
        foreach (var (path, text) in XamlUnder("src/StarMark.UI"))
        {
            foreach (Match tag in Regex.Matches(Markup(text), @"<([A-Za-z][\w.]*)([^>]*?i18n:LocText\.\w*Key=""[^""]*""[^>]*?)>",
                         RegexOptions.Singleline))
            {
                var tagName = tag.Groups[1].Value;
                var name = Regex.Match(tag.Groups[2].Value, @"x:Name=""([^""]+)""");
                if (!name.Success) continue;
                // 代码侧读那颗类的全部 partial：方法搬去 Foo.Options.cs 也算它写的（#318 的射程口径）。
                var code = Code(ReadRepoPartials(path + ".cs"));
                foreach (Match key in Regex.Matches(tag.Groups[2].Value, @"i18n:LocText\.(\w*Key)="""))
                {
                    watched++;
                    foreach (var pattern in SecondOwnerPatterns(key.Groups[1].Value, name.Groups[1].Value, tagName, tag.Groups[2].Value))
                    {
                        var hit = Regex.Match(code, pattern);
                        if (hit.Success)
                            offenders.Add($"{Path.GetFileName(path)} 的 {name.Groups[1].Value}" +
                                        $"（i18n:LocText.{key.Groups[1].Value}）← 代码又写了同一颗属性：{hit.Value}");
                    }
                }
            }
        }
        // 下界拦的是"抽取器没跑起来"（现场量过是 89 处上下，逐键数）：这一格红在扫描面缩水时，⛔ 不许红在措辞上。
        Assert.True(watched >= 70, $"只认出 {watched} 处「带名字的键」（现场量过是 89 处上下）——抽取器没跑对，这一格不作数");
        Assert.True(offenders.Count == 0,
            "挂了键的元素被代码写了同一颗属性＝两个主人（换语言那一刻静态那句盖掉带状态那句）：\n" +
            string.Join("\n", offenders.Distinct(StringComparer.Ordinal)));
    }

    /// <summary>
    /// 托盘这一族的措辞<b>只许从表里取</b>（批次 WI-2o）：右键菜单是 Win32 <c>AppendMenuW</c>、
    /// 每次右键重建 ⇒ 取词写在构建那一刻就能跟着语言走；托盘 tooltip 是 Win32 资源（<c>NIM_MODIFY</c> 重发）；
    /// 贴图组四颗标签的方向判据在 <c>PinGrouping</c>（字进表、判据只认键）。
    /// <para>⛔ 直交中文菜单串在这四颗文件里从此是禁项——注释不算（<see cref="Code"/> 已剥）。
    /// 扫描按<b>会产界面串的方法体</b>收口，不扫整文件：日志（<c>StarLog.*</c>）本就不过这把尺子
    /// （Tray.cs 的检查更新兜底日志是中文，与 TrayHost 同一口径）。
    /// ⚠ 这一格从前把 <c>PinGrouping.NamePrefix</c> 当作"持久化数据"放在射程外——那句<b>是错的</b>：
    /// 组名压根不落盘（贴图与贴图组都不进 widgets.json），那颗 const 只是把念法存进了建组那一刻的快照。
    /// 批次 WI-46c 删掉那颗 const、组名改成交表＋每次现算，于是 <c>NameOf</c> 与 <c>LimitProblem</c> 一起进射程。</para>
    /// </summary>
    [Fact]
    public void TrayStringsAreKeysNotLiterals()
    {
        var offenders = new List<string>();

        // TrayHost：只扫会渲染 UI 的两颗方法体（右键菜单 + tooltip）。日志串（StarLog.*）不归这一格：
        // 日志跟语言走是另一族（用户口径里日志本就不过闸），且 Error 那颗的续行会骗过行级过滤。
        var trayHost = Code(ReadRepoFile("src/StarMark.Integrations/SystemTray/TrayHost.cs"));
        foreach (var body in new[] { MethodBody(trayHost, "private void ShowContextMenu()"),
                                     MethodBody(trayHost, "public void SetTip(string tip)") })
            foreach (Match m in Regex.Matches(body, @"""[^""]*[\u4e00-\u9fff][^""]*"""))
                offenders.Add($"TrayHost.cs：{m.Value}");

        // MainWindow 两颗：只扫会产界面串的方法体（菜单构建 + 标签）。日志串不归这一格——
        // Tray.cs 的检查更新兜底日志（StarLog.Error）是中文，与 TrayHost 同一口径。
        var trayPartial = Code(ReadRepoFile("src/StarMark.UI/MainWindow.Tray.cs"));
        foreach (var (fragment, name) in new[]
                 {
                     ("private static TrayHost.TrayCommandItem? GroupMenu()", "GroupMenu"),
                     ("private IReadOnlyList<TrayHost.TrayCommandItem> BuildTrayCommands()", "BuildTrayCommands"),
                     ("private static string PinLabel(int pins, bool hidden)", "PinLabel"),
                 })
            foreach (Match m in Regex.Matches(MethodBody(trayPartial, fragment), @"""[^""]*[\u4e00-\u9fff][^""]*"""))
                offenders.Add($"MainWindow.Tray.cs {name}：{m.Value}");

        var menuPartial = Code(ReadRepoFile("src/StarMark.UI/MainWindow.Menu.cs"));
        foreach (Match m in Regex.Matches(MethodBody(menuPartial, "private MenuFlyout BuildWidgetsMenu()"),
                                          @"""[^""]*[\u4e00-\u9fff][^""]*"""))
            offenders.Add($"MainWindow.Menu.cs BuildWidgetsMenu：{m.Value}");

        // PinGrouping：扫五颗 label/命名方法体（方向判据留 Core、字进表）。批次 WI-46c 起
        // `NameOf` 与 `LimitProblem` 也进了射程——组名从前是存下来的一份快照，现在是每次现算的念法。
        // ✅WI-65 片5b 起那五颗改交「键＋实参」（返回 `LocMessage`）⇒ 锚点跟着改型，⛔ 放宽扫描。
        var grouping = Code(ReadRepoFile("src/StarMark.Core/Capture/PinGrouping.cs"));
        foreach (var (fragment, name) in new[]
                 {
                     ("public static LocMessage GroupLabel(", "GroupLabel"),
                     ("public static LocMessage HideLabel(", "HideLabel"),
                     ("public static LocMessage ThroughLabel(", "ThroughLabel"),
                     ("public static LocMessage CloseLabel(", "CloseLabel"),
                     ("public static LocMessage NameOf(", "NameOf"),
                     ("public static LocMessage? LimitProblem(", "LimitProblem"),
                 })
        {
            var body = MethodBody(grouping, fragment);
            foreach (Match m in Regex.Matches(body, @"""[^""]*[\u4e00-\u9fff][^""]*"""))
                offenders.Add($"PinGrouping.{name}：{m.Value}");
        }

        // 附带的三颗"只要还在就绿"的锚点：四颗文件的取词出口必须真的存在，否则禁项恒真（#189 家族）。
        Assert.Contains("Locale.T(\"tray.show\")", Code(ReadRepoFile("src/StarMark.Integrations/SystemTray/TrayHost.cs")), StringComparison.Ordinal);
        Assert.Contains("Locale.T(\"tray.tip\")", Code(ReadRepoFile("src/StarMark.Integrations/SystemTray/TrayHost.cs")), StringComparison.Ordinal);
        Assert.Contains("LocText.SetKey(item, WidgetStorage.KindTitleKey(kind))",
            Code(ReadRepoFile("src/StarMark.UI/MainWindow.Menu.cs")), StringComparison.Ordinal);

        Assert.True(offenders.Count == 0,
            "托盘/贴图组的菜单串直交了中文（换语言那一刻它不跟着改）：\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// 条目右键族（批次 WI-2p）：主窗卡片 ContextFlyout（XAML 挂键）、共享菜单工厂 <c>ItemContextMenu</c>
    /// （每次右键现建）、卡片动作 <c>ItemCardActions</c> 的对话框/提示（每次现建）、隐藏页还原词（现取）、
    /// <c>StateWriteNotice</c> 的骨架——五处全部只许从表里取词。
    /// <para>扫描按<b>会产界面串的方法体</b>收口（#329 同款），StarLog 的中文日志按行摘掉后再扫
    /// （这些 catch 里的日志都是单行；跨行续行会漏过行级过滤＝假红，人工处置即可）。</para>
    /// <para>批次 WI-2y 起 <c>PinImageMenuText</c> 的灰路也已交键——P-172 的在案豁免正式收回：
    /// 事实从句改成完整可换语言的从句后，中文属格骨架在日英会散架，"灰着整句留原文"失去存在条件。
    /// 亮灰两路都由"必须交键＋旧字面不许回来"两颗断言钉住（见下）。</para>
    /// </summary>
    [Fact]
    public void ItemContextMenusAreKeysNotLiterals()
    {
        var offenders = new List<string>();

        // ① 共享菜单工厂：Build（八颗菜单项）＋ PreviewAsync（预览对话框）。
        var factory = Code(ReadRepoFile("src/StarMark.UI/Helpers/ItemContextMenu.cs"));
        foreach (var fragment in new[]
                 {
                     "private static MenuFlyout Build(ItemCardViewModel vm, XamlRoot root)",
                     "private static async Task PreviewAsync(ItemCardViewModel vm, XamlRoot root)",
                 })
        {
            // factory 的 catch 里也有中文日志（#329 同款：日志不归这把尺子）——按行摘掉再扫。
            var body = MethodBody(factory, fragment);
            var withoutLogs = string.Join("\n", body.Split('\n')
                .Where(l => !l.Contains("StarLog.", StringComparison.Ordinal)));
            foreach (Match m in Regex.Matches(withoutLogs, @"""[^""]*[\u4e00-\u9fff][^""]*"""))
                offenders.Add($"ItemContextMenu {fragment.Split('(')[0].Split(' ')[^1]}：{m.Value}");
        }

        // ② 卡片动作：对话框与提示只在这些方法体里现取词。
        var actions = Code(ReadRepoFile("src/StarMark.UI/Helpers/ItemCardActions.cs"));
        foreach (var fragment in new[]
                 {
                     "private static void ReportOpenFailure(long itemId, (OpenFailure Kind, string? Message) outcome)",
                     "public static async void DeleteFileRow(long itemId)",
                     "public static async void CopyImage(ItemCardViewModel vm)",
                     "public static async void PinImageToDesktop(ItemCardViewModel vm)",
                     "public static async void DeleteClipboard(ItemCardViewModel vm)",
                     "public static async void TogglePin(ItemCardViewModel vm)",
                     "public static async void EditNote(XamlRoot xamlRoot, ItemCardViewModel vm)",
                     "public static async void EditTags(XamlRoot xamlRoot, ItemCardViewModel vm)",
                     "public static async void AddTag(XamlRoot xamlRoot, ItemCardViewModel vm)",
                 })
        {
            var body = MethodBody(actions, fragment);
            var withoutLogs = string.Join("\n", body.Split('\n')
                .Where(l => !l.Contains("StarLog.", StringComparison.Ordinal)));
            foreach (Match m in Regex.Matches(withoutLogs, @"""[^""]*[\u4e00-\u9fff][^""]*"""))
                offenders.Add($"ItemCardActions {fragment.Split('(')[0].Split(' ')[^1]}：{m.Value}");
        }

        // ③ 卡片 VM：方向判据住 VM、字进表（五颗 *MenuText；PinImageMenuText 豁免见方法注释）。
        var vm = Code(ReadRepoFile("src/StarMark.UI/ViewModels/ItemCardViewModel.cs"));
        foreach (var prop in new[]
                 {
                     "public string HideMenuText", "public string PinMenuText", "public string OpenMenuText",
                     "public string CopyMenuText", "public string DeleteMenuText",
                 })
        {
            var line = vm.Split('\n').FirstOrDefault(l => l.Contains(prop, StringComparison.Ordinal));
            if (line is null || Regex.IsMatch(line, @"""[^""]*[\u4e00-\u9fff][^""]*"""))
                offenders.Add($"ItemCardViewModel {prop.Split(' ')[^1]}：字面量回来了或属性不见了");
        }

        // ④ 亮路与灰路都交键（WI-2y 收回 P-172 的在案豁免：事实从句改键后旧属格骨架在日英散架，
        // 半中半英从"不该"升级成"编不过式不该"）。旧字面按"不许回来"钉回原处。
        Assert.Contains("Locale.T(\"card.pinImage.ok\")", vm, StringComparison.Ordinal);
        Assert.Contains("Locale.Fmt(\"card.pinImage.missing\", ClipboardPolicy.MissingFileClause)", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("贴到桌面（这张图的{ClipboardPolicy", vm, StringComparison.Ordinal);

        // ⑤ 隐藏页还原词（StateWriteNotice 的 word 实参要按当前语言取）。
        Assert.Contains("LocMessage.Of(\"card.restore.word\")",
            Code(ReadRepoFile("src/StarMark.UI/ViewModels/HiddenPageViewModel.cs")), StringComparison.Ordinal);

        // ⑥ StateWriteNotice 的骨架走表（"没生效"/"没落上去"两句只许在 UiStrings 里）。
        var notice = Code(ReadRepoFile("src/StarMark.Abstractions/StateWriteNotice.cs"));
        Assert.Contains("LocMessage.Of(\"state.title\"", notice, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"state.rowGone\"", notice, StringComparison.Ordinal);

        Assert.True(offenders.Count == 0,
            "条目右键族的界面串直交了中文（换语言那一刻它不跟着改）：\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// 一颗附带属性<b>实际会贴哪几颗属性</b>→ 那些属性在代码里的"第二个主人"长什么样。
    /// <para>顺序照 UI 那边 <c>LocText.ResolveTextProperty</c> 的分工写：正文键会挑 Text/Content/Header/Title/Message/Description
    /// 里确实存在的那一颗，所以六颗都要问；提示条有两种写法（<c>NAME.ToolTip =</c> 与
    /// <c>ToolTipService.SetToolTip(NAME,</c>）；开关那两侧各点自己那一颗。</para>
    /// <para>⛔ 不许宽成"代码里动过那颗元素就算"：输入框的 <c>.Text</c> 是用户正在打的字或历史回填，
    /// 与占位符键不是同一颗属性。</para>
    /// </summary>
    private static IEnumerable<string> SecondOwnerPatterns(string keyKind, string elementName, string tagName, string tagAttributes)
    {
        // ⛔ 不能写成 `\.{name}\.`：x:Name 生成的字段是直写的（`MonthBlock.Text = …`），前面没有点——
        // 台架 N3 的该红没红就是这一颗点。词边界就够：`SomeOther.MonthBlock.Text` 同样落网。
        var name = Regex.Escape(elementName);
        if (keyKind == "Key")
        {
            // 落点要照 UI 侧 LocText.ResolveTextProperty 那两条让路规则从标记近似（台架 N9 之后的又一课：
            // 第一版把"正文键"宽成 Text|Content|Header|… 全查，结果把 AI 那批输入框装数据的
            // `AiModelBox.Text = stored.Model` 也算成违规——输入框的键写的是 Header，.Text 是用户的数据）。
            return new[] { $@"\b{name}\.({string.Join("|", KeyWriteTargets(tagName, tagAttributes))})\s*=" };
        }
        return keyKind switch
        {
            "ToolTipKey" => new[] { $@"\b{name}\.ToolTip\s*=", $@"SetToolTip\(\s*{name}\b" },
            "PlaceholderKey" => new[] { $@"\b{name}\.PlaceholderText\s*=" },
            "OnContentKey" => new[] { $@"\b{name}\.OnContent\s*=" },
            "OffContentKey" => new[] { $@"\b{name}\.OffContent\s*=" },
            _ => Array.Empty<string>(),
        };
    }

    /// <summary>正文键在这颗元素上<b>实际会贴哪颗属性</b>（照 <c>LocText.ResolveTextProperty</c> 从标记近似）。
    /// <para>① 带 <c>PlaceholderText</c> 的输入框：Text 是用户数据，键写 Header（WinUI 3 的 TextBox 有 Header）；
    /// ② 页签/折叠组/下拉/开关这一族 Content 装的是整页正文，键写 Header；③ 其余按 Text→Content 顺序认。</para></summary>
    private static string[] KeyWriteTargets(string tagName, string tagAttributes)
    {
        if (tagAttributes.Contains("PlaceholderText=", StringComparison.Ordinal)) return new[] { "Header" };
        if (tagName is "PivotItem" or "TabViewItem" or "Expander" or "ComboBox" or "ToggleSwitch"
            or "NumberBox" or "TextBox" or "PasswordBox") return new[] { "Header" };
        if (tagName is "TextBlock" or "Run") return new[] { "Text" };
        if (tagName is "Button" or "ToggleButton" or "CheckBox" or "RadioButton" or "ComboBoxItem"
            or "MenuFlyoutItem" or "RadioMenuFlyoutItem" or "ToggleMenuFlyoutItem") return new[] { "Content" };
        return new[] { "Text", "Content", "Header", "Title", "Message", "Description" };
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
    /// 换语言的订阅点<b>只许有这七处，且各自的活不重叠</b>。
    /// <para><see cref="LocText"/> 是"重贴"那一族的唯一主人：多一处订阅就多一处"谁去把文案改回来"的分配，
    /// 于是有一处永远贴着旧词；而"重贴"里若混进重建页面／重新查库，切换语言就成了一次全应用刷新——那才是真的会卡一下。</para>
    /// <para>第二处是画布那条工具条（批次 WI-51）：它的状态行是<b>每次状态变化现拼</b>的那一族（⛔ 挂 <c>LocText</c>，
    /// 见 <c>CaptureBarWordGateTests</c>／<c>CanvasBarWordGateTests</c> 里那两格同一判据），
    /// 换语言时没人替它取词，屏幕就是半中半英；而它的窗宽按实测内容算，不跟着重摆的话，
    /// 换成长句那一档就把 ✕（唯一的鼠标出口之一）截在窗外。</para>
    /// <para>第三处是「组件外观」那扇编辑器（批次 WI-52／P-183）：同一族理由，不同的宿主——
    /// 它那一排预设色、"当前颜色"那一行与三根滑杆行末的读数都是<b>当场算</b>的，而编辑器可以一直开着不动，
    /// 自己的节拍里没有"语言换了"这件事。那颗 handler 只重取词＋重铺<b>它换对象时本来就会重铺的那一排</b>，
    /// ⛔ 新立一套重贴机制（前两处也都不是：一处贴标记上的字，两处贴自己现拼的那几行）。</para>
    /// <para>第四～第六处是三扇<b>组件窗</b>（天气＝批次 WI-53／P-182a 第一刀，倒计时＝WI-54 同一族剩半边，
    /// 计算器＝WI-55／P-182b）：
    /// 同一族理由，第一次落到"每秒自己就在重算"的宿主上。这类宿主屏上大半是<b>带读数的句子</b>
    /// （"18:42 更新"、"3 天 05:20"），而 <c>LocText.SetKey(el, key, args)</c> 把<b>实参也一起冻住</b>——
    /// 挂上去换语言那一刻贴回来的是<b>上一次那些数字</b>（坑表 #361）。合法性的另一半在三处都是同一句：
    /// 那颗 handler 只把<b>本来就在重算的那几行</b>再算一遍，⛔ 在回调里打接口／读设置档／重建集合。</para>
    /// <para>第七处是「今日速览」（批次 WI-56／P-186）：同一族理由（农历那一行与"距〈节日〉还有 N 天"都是当场算的读数），
    /// 它比前三扇组件多一条要防的：<b>这一格里最像"重贴"的写法是把热榜重新加载一遍</b>——那一趟含读设置档与打接口，
    /// 换语言那一刻就可能把"字改口"变成"热榜没拿到：超时"。所以重贴只许铺<b>已经拿到手的那一份</b>（<c>PaintTrending</c>），
    /// 抓与画分成两颗（这条界线与 ✅WI-39 那颗"重贴⛔ 重新导航"是同一件事的第三种形状）。</para>
    /// <para><b>批次 WI-67 片9c 第三刀（P-188 第七刀）给这张名单添了三位主人</b>：「最近活动」页与桌面那格
    /// （<c>ActivityPage.xaml.cs</c>／<c>ItemGridWidget.xaml.cs</c>，11 档事件名）与世界时钟组件
    /// （<c>WorldClockWidget.xaml.cs</c>，"本机没有这个时区：{0}"）。三处的共同点⛔ 是"当场算读数"，而是<b>行</b>：
    /// 模板上那颗 <c>i18n:LocText.Key</c> 只能带一颗<b>固定</b>键，带不了"这一行自己的那颗键"，
    /// 而逐行 <c>LocText.SetMessage</c> 会把名单塞满会重建的行（✅WI-45 同一颗理由）⇒ 唯一合法形状是 ✅WI-40-3b 机制 (C)：
    /// 行模型持 <c>LocMessage</c>＋显示侧 getter 绑 <c>Mode=OneWay</c>＋宿主逐行 <c>Repaste()</c>。
    /// 合法性的另一半与那三扇组件一样：<b>那颗 handler 只重念屏幕上已有的行</b>，⛔ 在回调里查库／打接口／重建集合
    /// （成对摘掉逐档数在 <c>TemplateRowWordGateTests.EveryNewLocaleOwnerUnsubscribesWhatItSubscribed</c>）。</para>
    /// </summary>
    [Fact]
    public void ExactlyOneSubscriberRenamesTheStringsAndItRebuildsNothing()
    {
        var files = ReadRepoUnder("src/StarMark.UI");
        var subscribers = files
            .Where(f => Code(f.Text).Contains("Locale.Changed += ", StringComparison.Ordinal))
            .Select(f => f.RelativePath.Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        // 每一位主人各点一次名（⛔ 只钉计数：多一处时那一行要红得说出是谁——批次 WI-60 那第九处就是这么被看见的）
        Assert.Equal(new[]
        {
            "src/StarMark.UI/Helpers/LocText.cs",
            "src/StarMark.UI/Views/ActivityPage.xaml.cs",
            "src/StarMark.UI/Views/CalcWidget.xaml.cs",
            "src/StarMark.UI/Views/CanvasToolbarWindow.xaml.cs",
            "src/StarMark.UI/Views/CountdownWidget.xaml.cs",
            "src/StarMark.UI/Views/GlanceWidget.xaml.cs",
            "src/StarMark.UI/Views/ItemGridWidget.xaml.cs",
            "src/StarMark.UI/Views/SettingsPage.xaml.cs",
            "src/StarMark.UI/Views/SystemMonitorWidget.xaml.cs",
            "src/StarMark.UI/Views/WeatherWidget.xaml.cs",
            "src/StarMark.UI/Views/WidgetAppearanceEditor.cs",
            "src/StarMark.UI/Views/WorldClockWidget.xaml.cs",
        }, subscribers);

        var reapply = MethodBody(Code(ReadRepoFile(LocTextCs)), "private static void ReapplyAll");
        Assert.DoesNotContain("new ", reapply, StringComparison.Ordinal);        // 重贴不等于重建
        Assert.DoesNotContain("Navigate", reapply, StringComparison.Ordinal);
        // 另外三处也只许做"重取词＋重摆／重铺自己那一排"：订阅的那颗方法就是它们平时状态变化走的那一颗，
        // 而且必须成对摘掉（事件挂在静态事件上，窗关了不摘＝那一窗的元素永远活着）。
        var bar = Code(ReadRepoFile(CanvasToolbarCs));
        Assert.Contains("Locale.Changed += Refresh;", bar, StringComparison.Ordinal);
        Assert.Contains("Locale.Changed -= Refresh;", bar, StringComparison.Ordinal);
        var editor = Code(ReadRepoFile(WidgetAppearanceEditorCs));
        Assert.Contains("Locale.Changed += RebuildWords;", editor, StringComparison.Ordinal);
        Assert.Contains("Locale.Changed -= RebuildWords;", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("new Window", MethodBody(editor, "void RebuildWords"), StringComparison.Ordinal);
        // 天气这颗组件（批次 WI-53）：⛔ 用 LocText 的那一路走不通——它屏上大半是<b>带读数的句子</b>
        // （"18:42 更新"、"体感 21° · 湿度 60% · 风 12 km/h"），`LocText.SetKey(el, key, args)` 把<b>实参也一起冻住</b>，
        // 换语言那一刻贴回来的是<b>上一次那些数字</b>（坑表 #361 那颗同族）。而那颗"更新失败"是状态：
        // 重画若把它盖回"某时更新"就是一句当场可见的假话 ⇒ 订阅点⛔ 是通用机制的洞，是这一族的读数必须自己重算。
        // 合法性的另一半＝它只重跑 `Loaded` 那两条（标签＋用<b>内存里那份缓存</b>重画），⛔ 在这条回调里再打接口。
        var weather = Code(ReadRepoFile(WeatherWidgetCs));
        Assert.Contains("Locale.Changed += RebuildWords;", weather, StringComparison.Ordinal);
        Assert.Contains("Locale.Changed -= RebuildWords;", weather, StringComparison.Ordinal);
        var weatherRebuild = MethodBody(weather, "private void RebuildWords");
        Assert.DoesNotContain("RefreshAsync", weatherRebuild, StringComparison.Ordinal);   // 重贴⛔ 顺手再查一次网络
        Assert.DoesNotContain("SearchCityAsync", weatherRebuild, StringComparison.Ordinal);
        // 倒计时这颗组件（批次 WI-54，P-182a 那半边的另一半）：同一族理由，第五个宿主。
        // 它屏上每一行都是读数（主行剩多久／次行第几个年头／没起名时那个派生名），所以⛔ 走 LocText 那条冻实参的路；
        // 而"每秒重算"那一拍里从来不含"语言换了"这件事 ⇒ 订阅归宿主。挂在 <c>Loaded</c>、摘在 <c>Unloaded</c>
        // 是这一族与前三处不同的一点：组件窗会被关掉，静态事件上不摘＝那一窗的元素永远活着。
        var countdown = Code(ReadRepoFile(CountdownWidgetCs));
        Assert.Contains("Loaded += (_, _) => Locale.Changed += RebuildWords;", countdown, StringComparison.Ordinal);
        Assert.Contains("Unloaded += (_, _) => Locale.Changed -= RebuildWords;", countdown, StringComparison.Ordinal);
        var countdownRebuild = MethodBody(countdown, "private void RebuildWords");
        Assert.Contains("ViewModel.RebuildWords(", countdownRebuild, StringComparison.Ordinal);   // 只把本来每秒就在算的那几行再算一遍
        Assert.DoesNotContain("PersistAsync", countdownRebuild, StringComparison.Ordinal);        // ⛔ 顺手落一次盘（换语言不该写设置档）
        Assert.DoesNotContain("InitializeComponent", countdownRebuild, StringComparison.Ordinal); // ⛔ 顺手重建整棵可视树
        // 计算器这扇窗（批次 WI-55／P-182b，第六位主人）：三页的读数都是<b>当场算</b>的句子（结果句里嵌着两颗单位名），
        // 同一族理由⛔ 走 LocText 那条冻实参的路。这一处比别人多一条禁项：`ApplyQuickMode` 那颗会**清空四个输入槽**
        // （那是"换速算模式"的活），换语言那一刻调它＝把用户正在打的字吃掉。
        var calc = Code(ReadRepoFile(CalcWidgetCs));
        Assert.Contains("Loaded += (_, _) => Locale.Changed += RebuildWords;", calc, StringComparison.Ordinal);
        Assert.Contains("Unloaded += (_, _) => Locale.Changed -= RebuildWords;", calc, StringComparison.Ordinal);
        var calcRebuild = MethodBody(calc, "private void RebuildWords");
        Assert.Contains("ViewModel.RebuildWords(", calcRebuild, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyQuickMode", calcRebuild, StringComparison.Ordinal);           // ⛔ 抹掉用户已经填进去的输入
        Assert.DoesNotContain("SaveHistoryAsync", calcRebuild, StringComparison.Ordinal);         // ⛔ 顺手落一次盘
        Assert.DoesNotContain("InitializeComponent", calcRebuild, StringComparison.Ordinal);      // ⛔ 顺手重建整棵可视树
        // 今日速览这扇组件（批次 WI-56／P-186，第七位主人）：农历那一行与"距〈节日〉还有 N 天（M月D日）"都是
        // <b>当场算出来的读数</b>（同一族理由⛔ 走 LocText 那条冻实参的路），而月历上那些"哪天算节日"的圆点
        // 从此<b>按语言各排一张清单</b>——换语言不重画，英文界面的月历就会亮着中国农历那些点。
        // 这一处比别人多一条禁项：热榜那几行的字来自<b>已经拿到手的那一份</b>（<c>PaintTrending</c>），
        // ⛔ 在回调里再走 <c>LoadTrendingAsync</c>——那一趟含读设置档与打接口，切语言就可能换来一句"热榜没拿到"。
        var glance = Code(ReadRepoFile(GlanceWidgetCs));
        Assert.Contains("Locale.Changed += RebuildWords", glance, StringComparison.Ordinal);
        Assert.Contains("Locale.Changed -= RebuildWords", glance, StringComparison.Ordinal);
        var glanceRebuild = MethodBody(glance, "private void RebuildWords");
        Assert.Contains("PaintTrending()", glanceRebuild, StringComparison.Ordinal);               // 只重铺缓存里那一份
        Assert.DoesNotContain("LoadTrendingAsync", glanceRebuild, StringComparison.Ordinal);       // ⛔ 顺手再抓一次网络
        Assert.DoesNotContain("InitializeComponent", glanceRebuild, StringComparison.Ordinal);     // ⛔ 顺手重建整棵可视树
        // 设置页那一卡（批次 WI-57／P-187，第八位主人）：诊断那几行是<b>进页时采回来的事实</b>（键＋实参），
        // 语言换了没人会回去重采——那一趟里有 COUNT(*) 全表扫描。所以重贴只许把<b>已经拿到手的那一份</b>再念一遍。
        // 这一处与七扇组件不同的一点：它挂的是<b>本页那份 transient 的 VM</b>，摘掉必须落在 Page_Unloaded 上，
        // 只摘一半（或只订不摘）＝每进一次设置页在静态事件上多挂一份，而那些页面早就不在了（坑表 #386 的代价）。
        var settings = Code(ReadRepoFile(SettingsPageCs));
        Assert.Contains("Locale.Changed += ViewModel.PaintDiagnostics;", settings, StringComparison.Ordinal);
        Assert.Contains("Locale.Changed -= ViewModel.PaintDiagnostics;", settings, StringComparison.Ordinal);
        // 第九处＝系统监控那扇组件窗（批次 WI-60／P-188 第二刀）：它的行名来自 Core 的目录、
        //   行内原因来自 Integrations 的探针——**这两层交的都是键**，"重念"只能发生在这颗宿主上；
        //   而它那一拍是"多久采一次"（省资源模式 3 秒），⛔ 让语言改动等到下一拍 ⇒ 自己订阅。
        var monitor = Code(ReadRepoFile("src/StarMark.UI/Views/SystemMonitorWidget.xaml.cs"));
        Assert.Contains("Loaded += (_, _) => Locale.Changed += RepaintWords;", monitor, StringComparison.Ordinal);
        Assert.Contains("Unloaded += (_, _) => Locale.Changed -= RepaintWords;", monitor, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.Tick", MethodBody(monitor, "private void RepaintWords"),
                              StringComparison.Ordinal);   // 重贴⛔ 顺手再采一次（那会把改口变成"读数失败"）
        Assert.DoesNotContain("LoadDiagnosticsAsync", MethodBody(settings, "void Page_Loaded"), StringComparison.Ordinal);
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
        // ⚠ 批次 WI-2k 曾加出第六颗 `Args`（带读数的那句正文），批次 WI-2l 又把它摘回：
        // 那颗的实参是托管数组，而 WinUI 3 的依赖属性类型必须投得到 WinRT——它换来的不是托管异常，
        // 是<b>启动即原生访问违例</b>（下一格 <see cref="NoUiDependencyPropertyIsRegisteredAsAnArrayType"/>
        // 与坑表 #316）。实参现在住在 <c>LocText</c> 的弱表信箱里，取词路仍然只有一条。
        // ⛔ 窗口标题<b>也没有</b>加成第六颗：WinUI 3 的 <c>Window</c> 不是 <c>DependencyObject</c>，
        // 附带属性根本贴不上去（它走 <c>LocText</c> 的第二张名单，见
        // <see cref="TheWidgetTitleHasOneIconOwnerAndFollowsTheLanguage"/> 与坑表 #338）。
        Assert.Equal(5, Count(loc, "DependencyProperty.RegisterAttached"));
        Assert.Contains("FindProperty(d, \"PlaceholderText\")", loc, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>UI 层每颗依赖属性的类型必须投得到 WinRT；注册成数组＝整扇窗口启动即崩</b>（批次 WI-2l）。
    /// <para>真机读数（三档语言各一跑，全崩）：异常 <c>0xC0000005</c>，落在 <c>coreclr.dll+0x2E5102</c>，
    /// 异常记录里的参数个数是 0，而属性变更回调<b>一次都没进去</b>（临时探针只打出 <c>cctor</c> 那一行）。
    /// 单变量实验：把那颗 <c>RegisterAttached("Args", typeof(object[]))</c> 的类型换成 <c>typeof(string)</c>，
    /// 同一版源码当场起得来（主窗 38 处文案全贴上）。<b>而 5474 格测试全绿</b>——UI 工程引用不进测试工程。</para>
    /// <para>射程<b>只点名数组</b>：<c>IReadOnlyList&lt;TextSegment&gt;</c>（<c>HighlightHelper</c>）与
    /// <c>IEnumerable&lt;string&gt;</c>（<c>TagEditor</c>）现场都活着——泛型接口投成 <c>IIterable&lt;T&gt;</c>，
    /// 死的是托管数组（SZARRAY 没有可调用包装）。⛔ 不许顺手把泛型一起写进拒绝表，那是假话。</para>
    /// </summary>
    [Fact]
    public void NoUiDependencyPropertyIsRegisteredAsAnArrayType()
    {
        const string Shape =
            @"DependencyProperty\.Register(?:Attached)?\(\s*(?:nameof\([^)]*\)|""[^""]*"")\s*,\s*typeof\((?<type>[^)]*)\)";
        var seen = 0;
        foreach (var (path, text) in ReadRepoUnder("src/StarMark.UI"))
        {
            foreach (Match match in Regex.Matches(Code(text), Shape, RegexOptions.Singleline))
            {
                seen++;
                var type = match.Groups["type"].Value;
                Assert.False(type.EndsWith("[]", StringComparison.Ordinal),
                    $"{path} 把依赖属性注册成数组类型 {type}：换语言那一刻不是翻不动，是启动即原生访问违例");
            }
        }
        // 现场数到的注册点：LocText 5＋HighlightHelper 1＋WidgetTextScale 2＋TagEditor 2＋ItemCard 1＋
        // SearchWidget 里那颗 WrapPanel 2＝13。这里是<b>下界</b>而不是精确值：写法一变（比如不再用
        // typeof(...)）扫描面就瞎了，那一格要报"看不见了"，不许悄悄绿。
        Assert.True(seen >= 13, $"依赖属性注册只数到 {seen} 颗（下界 13）——扫描面或写法变了，这一格现在是空的");
    }

    /// <summary>
    /// <b>换语言之前先抹掉上一句的实参</b>（批次 WI-2l：实参从依赖属性搬进弱表信箱之后，这颗证人管的是搬家后的新形状）。
    /// <para>信箱比那颗依赖属性多一个洞：<c>SetKey(element, value)</c> 那一趟（XAML 与"没话"那一路都走它）
    /// 如果不清信箱，元素上<b>上一次带读数那句</b>的实参就一直躺着；下一次这颗元素挂上一颗带 <c>{0}</c> 的键时，
    /// 填空填的是<b>上一个读数的旧值</b>——屏幕上是一句语法正确、数字过期的假话，比半中半英更难报障（#276／#277 同族）。</para>
    /// <para>⛔ 这一格钉的是"清信箱这一行在两处都在"，不是钉方法名：摘掉任一处都会让屏幕读到过期读数，
    /// 而测试工程引用不到 UI，只有这颗形状证人拦得住。</para>
    /// </summary>
    [Fact]
    public void TheReadoutMailboxIsClearedBeforeAPlainKeyIsPasted()
    {
        var loc = ReadRepoFile(LocTextCs);
        var setter = MethodBody(Code(loc), "public static void SetKey(DependencyObject element, string value)");
        Assert.Contains("RememberArgs(element, null);", setter, StringComparison.Ordinal);
        Assert.Contains("element.SetValue(KeyProperty, value);", setter, StringComparison.Ordinal);

        var changed = MethodBody(Code(loc), "private static void OnKeyChanged");
        Assert.Contains("RememberArgs(d, null);", changed, StringComparison.Ordinal);
        // 实参只许有一个读者：搬回依赖属性＝把 WI-2l 那颗启动崩溃请回来（上一格也会同批红）
        Assert.DoesNotContain("GetValue(ArgsProperty", Code(loc), StringComparison.Ordinal);
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
        Assert.Equal(3, Count(loc, "WarnUnwritableOnce(d, "));   // 正文／占位符／开关两侧（两颗共用一条出口）
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
        Assert.Contains("LocMessage.Of(\"settings.hotkeys.reset\")", body, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"settings.hotkeys.resetConfirm\")", body, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"dialog.cancel\")", body, StringComparison.Ordinal);
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
    [InlineData(SearchPageCs)]      // ✅批次 WI-67 片9b：搜索页那四行（段名两颗＋空态＋加载更多那颗按钮）都从这颗门走
    [InlineData(SearchWidgetCs)]    //   组件那颗空态从前是 EmptyHint.Text = 直接写，本批改交这颗门
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
    /// <summary>
    /// 第六族（控件库自带无障碍名，P-169 ㊼/#298）的机制账：应用级覆盖件存在、三档语言码齐全、
    /// 启动与切换两颗接线都在且**跟着 TrySet 走**（写在 TrySet 之前＝覆盖读旧语言，白设）。
    /// 真机那九行是否真改口由 bin/WI34 的树转储给数——本格只钉代码形状（#287：屏幕的字归屏幕的尺）。
    /// </summary>
    [Fact]
    public void TheFrameworkLanguageChainIsAlignedWithOurTable()
    {
        var ov = Code(ReadRepoFile("src/StarMark.UI/Helpers/UiLanguageOverride.cs"));
        Assert.Contains("PrimaryLanguageOverride", ov, StringComparison.Ordinal);
        foreach (var code in new[] { "\"zh-Hans\"", "\"ja\"", "\"en\"" })
            Assert.Contains(code, ov, StringComparison.Ordinal);
        foreach (var file in new[] { "src/StarMark.UI/App.xaml.cs", "src/StarMark.UI/Views/SettingsPage.I18n.cs" })
        {
            var body = ReadRepoFile(file).Replace("\r\n", "\n");
            var set = body.IndexOf("Locale.TrySet(Locale.Resolve", StringComparison.Ordinal);
            var apply = body.IndexOf("UiLanguageOverride.Apply()", StringComparison.Ordinal);
            Assert.True(set >= 0 && apply > set, file + "：必须 TrySet 之后接 Apply（先覆盖后定档＝读旧语言）");
        }
    }

    /// <summary>
    /// 「桌面工具」那一栏由 <b>code-behind 现建</b>的那排字只许交键（批次 WI-35，P-169 ① 点名的最后一格）。
    /// <para>症状不是"没翻"，是<b>那一行的语言被钉在建行那一刻</b>：这些 Expander 摆出来之后不重建，
    /// 换语言回头看那一栏，类型标题、计数与那排按钮还挂着旧语言（坑表 #276 同一件事，只是宿主从 XAML
    /// 换成了 C#）。修法与标记那一路同形：元素挂进 <c>LocText</c> 那棵树，换语言由那颗全局订阅重贴。</para>
    /// <para>两头钉：① 五处挂键逐处点名（少挂一处＝那一行退回钉死，而屏幕上看不出区别）；
    /// ② <b>零格</b>——这一档里再不许出现一句中文字面量，给的判据是"哪一句"而不是"少了几个"。
    /// ⚠ 批次 WI-35 当时只收两个方法体，因为那颗「未设置」还兼着"这行到底绑没绑"的<b>哨兵值</b>，
    /// 直接换掉它的字会让分类计数在换语言那一刻数错（要先拆状态再挂键）。那颗哨兵已在批次 WI-40-1 拆成
    /// <c>HotkeyRow.Bound</c> ⇒ 挡路的理由到期，零格<b>就地收到整档</b>（见 <see cref="TheHotkeyRowsCountByTheFlagNotByTheDisplayedWords"/>）。</para>
    /// <para>③ 类型标题必须挂<b>注册表那颗键</b>：现取 <c>KindTitle(kind)</c> 既把成品串钉进界面，
    /// 又给同一句话开出第二种写法（托盘那一路早已交键）。④ 确认框那两颗按钮复用对话框那对键，
    /// ⛔ 不许在这一栏再造一份「删除／取消」（三个主人的教训）。</para>
    /// </summary>
    [Fact]
    public void TheWidgetRowsHandKeysNotWords()
    {
        const string file = "src/StarMark.UI/Views/SettingsPage.Widgets.cs";
        var page = Code(ReadRepoFile(file));
        foreach (var call in new[]
                 {
                     "LocText.SetKey(addBtn, \"settings.widgets.addRow\")",
                     "LocText.SetKey(showBtn, \"settings.widgets.showOne\")",
                     "LocText.SetKey(removeBtn, \"settings.widgets.removeOne\")",
                     "LocText.SetKey(label, \"settings.widgets.instanceLine\"",
                     "LocText.SetKey(title, WidgetStorage.KindTitleKey(kind))",
                     "LocText.SetKey(counted, \"settings.widgets.noneAdded\")",
                     "LocText.SetKey(counted, \"settings.widgets.addedCount\"",
                 })
            Assert.Contains(call, page, StringComparison.Ordinal);

        Assert.DoesNotContain("WidgetStorage.KindTitle(kind)", page, StringComparison.Ordinal);

        // 置顶那半句是实参不是第二颗键：它得由表里那颗尾巴出，代码只许决定"挂不挂"。
        Assert.Equal(1, Count(page, "Locale.T(\"settings.widgets.instanceTopmost\")"));

        // 零格收到整档（批次 WI-40-1：挡路的那颗哨兵已经拆成 bool，⛔ 再靠"只收两个方法体"躲着）。
        var stillLiteral = Regex.Matches(page, @"(?:Content|Text)\s*=\s*""([^""]*)""|""([^""]*)""")
            .Cast<Match>()
            .Select(m => (m.Groups[1].Success ? m.Groups[1] : m.Groups[2]).Value)
            .Where(v => Regex.IsMatch(v, @"[\u4e00-\u9fff]"))
            .ToArray();
        Assert.Equal(Array.Empty<string>(), stillLiteral);

        var del = MethodBody(page, "private async void DeleteLayout_Click");
        Assert.Contains("LocMessage.Of(\"settings.widgets.deleteLayoutTitle\")", del, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"settings.widgets.deleteLayoutBody\", row?.Name ?? id)", del, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"dialog.delete\")", del, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"dialog.cancel\")", del, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「快捷键」那一栏里<b>"这一行绑没绑"由一颗 bool 说，屏幕上那句话说自己的</b>（批次 WI-40-1，P-175 缺口③）。
    /// <para>从前那颗 <c>const string UnsetText = "未设置"</c> 一人两用：既是按钮上的占位话，又是分类计数读的<b>真值</b>
    /// （<c>rows.Count(r =&gt; !Equals(r.BindingText, UnsetText))</c>）。那句一进表，"绑没绑"就变成"这行是不是中文的『未设置』"
    /// ——切英文那一刻每组的数全归零：<b>界面读起来对、数是错的</b>（坑表 #354）。
    /// 建行时当场算过的那颗 <c>bound</c> 从此留在 <c>HotkeyRow.Bound</c> 上，计数只读它。</para>
    /// <para>⚠ 分类标题那句「（N 项已绑定）」在 WI-40-1 <b>故意没进表</b>：它与同一排的分类名贴在一起，而分类名还住在 Core
    /// （P-175 缺口①）——只翻半排就是半中半英（坑表 #330 那一族）⇒ 那一句与"身份改 ASCII"<b>同一批</b>做，已随批次 WI-40-2 落地
    /// （<see cref="TheHotkeyCategoriesHandAnIdentityWhileTheirWordsLiveInTheTable"/>）。WI-40-1 台架里它是那条<b>必须仍绿</b>的对照臂。</para>
    /// </summary>
    [Fact]
    public void TheHotkeyRowsCountByTheFlagNotByTheDisplayedWords()
    {
        var rows = Code(ReadRepoFile("src/StarMark.UI/Views/SettingsPage.Hotkeys.cs"));
        var model = Code(ReadRepoFile("src/StarMark.UI/Views/SettingsPage.xaml.cs"));

        // ① 计数读那颗 bool；⛔ 再不许按"给人看的那一句"相等来数。
        Assert.Contains("var count = rows.Count(r => r.Bound);", rows, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Equals(r.BindingText", rows, StringComparison.Ordinal);
        // ② 那颗哨兵 const 不许回来——它一回来，①就只能重新靠字符串数。
        Assert.DoesNotContain("UnsetText", rows, StringComparison.Ordinal);
        Assert.DoesNotContain("UnsetText", Code(ReadRepoFile("src/StarMark.UI/Views/SettingsPage.Widgets.cs")), StringComparison.Ordinal);
        Assert.Contains("public bool Bound", model, StringComparison.Ordinal);

        // ③ 每一处改了显示的路径都必须同处改掉那颗 bool：少钉一处＝那一路上 Bound 与文字分家（各自演自己的）。
        //    WI-40-3b 起这里交出的是「键＋实参」：键位那颗走 common.rawValue（它是数据），「未设置」那颗才是表里的句子。
        Assert.Contains("BindingMessage = bound", rows, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"common.rawValue\", HotkeyDisplay.Display(g!))", rows, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"hotkey.unset\")", rows, StringComparison.Ordinal);
        // ⛔ 回到"写进去的那一刻就把词取出来"——那正是缺口②的形状
        Assert.DoesNotContain("Locale.T(\"hotkey.unset\")", rows, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingText = bound", rows, StringComparison.Ordinal);
        Assert.Contains("row.Bound = !g.IsEmpty;", MethodBody(rows, "private void FinishRecording()"), StringComparison.Ordinal);
        Assert.Contains("_recordingRow.Bound = _recordingWasSet;", MethodBody(rows, "private void StopRecording(bool resetText)"), StringComparison.Ordinal);
        Assert.Contains("row.Bound = false;", MethodBody(rows, "private void ClearBinding()"), StringComparison.Ordinal);

        // ④ 冲突那句与它自己的连接号都住表里（顿号换英文要变成 ", "，坑表 #356）：
        //    名单交 LocMessage、连接号交键，由 LocJoin 在<b>被贴上去那一刻</b>才取词（坑表 #362）。
        Assert.Contains("LocMessage.Of(\"hotkey.conflict.shares\", new LocJoin(others, \"hotkey.conflict.join\"))",
            MethodBody(rows, "private void RefreshConflictMarks()"), StringComparison.Ordinal);
        Assert.DoesNotContain("string.Join(Locale.T(\"hotkey.conflict.join\")", rows, StringComparison.Ordinal);
        // ⑤ 钩子装不上那句：原因由 Core 给，后半句"因此收不到你的按键"不再由代码拼。
        Assert.Contains("RecordingAlert = Locale.Fmt(\"hotkey.record.hookRetry\", why);",
            MethodBody(rows, "private void BeginRecording(string action, Button btn, HotkeyRow row)"), StringComparison.Ordinal);
    }

    /// <summary>
    /// 快捷键那一栏的<b>分组身份⛔ 是给人看的那句话，而那句话只许住在表里</b>（批次 WI-40-2，P-175 缺口①）。
    /// <para>从前 <c>HotkeyActions.CategoryOf</c> 直接返回中文串，UI 拿同一颗串三用：字典的键、与 <c>CategoryOrder</c> 配对的渲染序、
    /// 以及 Expander 标题（坑表 #353）。同五个词还在 <c>BuildCategoryOrder</c> 里<b>各写第二遍</b>，两份逐字相同的清单就是两份会各自漂移的实现。
    /// 组件那一族更阴：它交的是 <c>KindTitle</c>——那颗串里既有<b>图标字形</b>（⛔ 语言内容）又有<b>已按当下语言取过词</b>的名字，
    /// 拿它当身份＝换语言那一刻这一组自己搬家；名字的真源本是注册表那颗 <c>KindTitleKey</c>（托盘与组件窗都读它）。</para>
    /// <para>⚠ 那颗"每颗动作常量都得有人接 handler"的证人按反射扫 <c>HotkeyActions</c> 的公开静态字符串字段，
    /// 所以身份刻意做成<b>嵌套类</b> <c>Category</c> 而不是六个 <c>public const string</c>——
    /// 混进去的后果不是误报，是有人为了让它绿去<b>放宽那把尺</b>。</para>
    /// </summary>
    [Fact]
    public void TheHotkeyCategoriesHandAnIdentityWhileTheirWordsLiveInTheTable()
    {
        var core = Code(ReadRepoFile("src/StarMark.Core/Hotkeys/HotkeyActions.cs"));
        var rows = Code(ReadRepoFile("src/StarMark.UI/Views/SettingsPage.Hotkeys.cs"));
        var cjk = new Regex(@"[\u4e00-\u9fff]");

        // ① 身份的三个出口⛔ 一个字都不许带（带一个就还是"身份＝话"）
        foreach (var anchor in new[] { "public static string CategoryOf(string action", "private static List<string> BuildCategoryOrder()" })
        {
            var body = MethodBody(core, anchor);
            Assert.True(body.Length > 0, anchor + "：读不到方法体，这一格就成了空判");
            Assert.Equal(Array.Empty<string>(), WordsIn(body, cjk));
        }
        // ①b 身份住在嵌套类里：拉平成 <c>HotkeyActions</c> 上的 <c>public const string</c> 会让那颗反射证人
        //    去要求"给一个分组名注册热键"（它按"公开静态字符串字段＝动作"扫），所以这一条也钉形状
        Assert.Contains("public static class Category", core, StringComparison.Ordinal);
        // ② 词住在表里：六颗身份各自映射到一颗<b>写字面</b>的键（⛔ 内插——坑表 #349）
        var titleKey = MethodBody(core, "public static string CategoryTitleKey(string category)");
        foreach (var key in new[] { "hotkey.cat.main", "hotkey.cat.widgets", "hotkey.cat.screen",
                                     "hotkey.cat.canvas", "hotkey.cat.layout", "hotkey.cat.other" })
            Assert.Contains("\"" + key + "\"", titleKey, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"hotkey.cat.", titleKey, StringComparison.Ordinal);
        // 组件那一族交回注册表那颗键，⛔ 在这一栏另起一份名字
        Assert.Contains("WidgetStorage.KindTitleKey(kind)", titleKey, StringComparison.Ordinal);
        Assert.DoesNotContain("KindTitle(", core, StringComparison.Ordinal);

        // ③ UI 那一排：标题与计数都挂键（这颗 Expander 建一次就常驻，写成品串＝把语言钉在建那一刻）
        var header = MethodBody(rows, "private Expander BuildCategoryExpander(string category, IList<HotkeyRow> rows)");
        Assert.Contains("LocText.SetKey(title, HotkeyActions.CategoryTitleKey(category))", header, StringComparison.Ordinal);
        Assert.Contains("LocText.SetKey(boundCount, \"hotkey.cat.boundCount\", count)", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = category", header, StringComparison.Ordinal);
        Assert.DoesNotContain("项已绑定", header, StringComparison.Ordinal);
    }

    private static string[] WordsIn(string code, Regex cjk) =>
        Regex.Matches(code, @"""(?:[^""\\\n]|\\.)*""").Cast<Match>()
            .Select(m => m.Value).Where(v => cjk.IsMatch(v)).ToArray();

    /// <summary>
    /// <b>组件窗标题那一排只许画一颗图标，且换语言要跟着走</b>（批次 WI-36，用户真机报「所有组件再次出现两个图标」）。
    /// <para>根因形状：标题栏本来就有一颗 <c>WidgetGlyph</c> 在画图标，而批次 db0306f 把默认标题换成
    /// <c>WidgetStorage.KindTitle</c>（展示名＝"图标 空格 名字"）——图标从此住进名字串，<b>同一排画出两颗</b>。
    /// 带图标那份是<b>托盘菜单</b>的需要（那里没有别的元素画图标），组件窗不是：它该挂 <c>KindTitleKey</c>。
    /// ⚠ 只钉"别再叫 KindTitle"不够——名字里夹 glyph 这件事没有第二条路可查，所以正向也钉：默认名必须来自那颗键。</para>
    /// <para>另一半是这台机器上通用的一条：<b>标题栏文字挂键</b>（切语言由 <c>LocText</c> 重贴），
    /// 而用户重命名过的必须<b>先把键摘掉</b>再写他的名字——挂着键，换语言那一刻会把他的名字盖回类型名
    /// （坑表 #276 的镜像面：带值的字不许由静态键接管）。</para>
    /// <para>OS 那层的 <c>Title</c>（Alt+Tab 读它）走 <c>LocText.SetWindowTitle</c>，<b>不是</b>第六颗附带属性：
    /// WinUI 3 的 <c>Window</c> <b>不是</b> <c>DependencyObject</c>，附带属性贴不上去（坑表 #338——我先前认定
    /// "Title 也是依赖属性，所以进得了那棵树"，编译器直接否了）。它记进 <c>LocText</c> 的第二张弱引用名单，
    /// 由<b>同一个</b> <c>ReapplyAll</c> 重贴，所以"换语言谁改文案"在 UI 里仍然只有一个订阅点
    /// （<see cref="ExactlyOneSubscriberRenamesTheStringsAndItRebuildsNothing"/>），⛔ 组件窗不许自己
    /// <c>Locale.Changed +=</c>。而"只登记不重贴"正是测试工程看不见的那半条失效，所以两头都钉。</para>
    /// </summary>
    [Fact]
    public void TheWidgetTitleHasOneIconOwnerAndFollowsTheLanguage()
    {
        var menu = Code(ReadRepoFile("src/StarMark.UI/Views/WidgetWindow.Menu.cs"));
        Assert.DoesNotContain("WidgetStorage.KindTitle(_kind)", menu, StringComparison.Ordinal);  // 回归本体
        Assert.Contains("WidgetStorage.KindTitleKey(kind)", menu, StringComparison.Ordinal);      // 默认名来自那颗键
        Assert.Contains("LocText.SetKey(WidgetTitle, DefaultKindTitleKey(_kind))", menu, StringComparison.Ordinal);
        Assert.Contains("LocText.SetKey(WidgetTitle, string.Empty)", menu, StringComparison.Ordinal);  // 重命名过＝先摘键
        Assert.DoesNotContain("$\"StarMark 组件", menu, StringComparison.Ordinal);                 // 前缀抄回中文
        // OS 那一层的标题走 LocText 的第二张名单（⛔ 不为它另开一处 Locale.Changed 订阅），且嵌套的名字交的是<b>没渲染的</b>那句
        Assert.Contains("LocText.SetWindowTitle(this, \"widget.win.windowTitle\"", menu, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(DefaultKindTitleKey(_kind))", menu, StringComparison.Ordinal);
        // ⚠ 只扫 <c>ApplyTitle</c> 那一颗方法体，⛔ 不扫整档（批次 WI-37-B 起的口径）：
        // 这份文件里还有每次右键都重建的那一节（<c>BuildEyeRestSection</c>），那里 <c>.Render()</c> 是<b>对的</b>
        // ——构建那一刻现取词正是 WI-2o 的接法①。整档禁这六个字节守的就不是"标题不许提前渲染"，
        // 而是"这个文件里不许出现某个词"，那种判据下一次一定被改松而不是改对（#195 的形状口径）。
        Assert.DoesNotContain(".Render()", MethodBody(menu, "private void ApplyTitle()"), StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.Changed += ", menu, StringComparison.Ordinal);

        // 名单的另一头：ReapplyAll 真去重贴那颗 Title，否则登记再多也只是"记了一句没人读的话"。
        var loc = Code(ReadRepoFile(LocTextCs));
        Assert.Contains("public static void SetWindowTitle(Window window, string key", loc, StringComparison.Ordinal);
        Assert.Contains("window.Title = Text(title.Key, title.Args)", loc, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleKey", loc, StringComparison.Ordinal);   // ⛔ 别把它改回附带属性：Window 进不了那棵树
    }

    /// <summary>
    /// <b>主窗那颗「语言」下拉：全部那一行走表，仓库语言的自名不走表</b>（批次 WI-38，用户真机报
    /// 「主窗口"语言"选择未国际化」）。
    /// <para>根因不是"忘了挂键"，而是<b>那颗下拉根本没有可挂键的东西</b>：它的项是一段中文常量
    /// <c>"语言：全部"</c> 加上一串裸字符串，闭着的格子显示的是选中那一刻从 <c>Content</c> 抄走的快照
    /// ⇒ 换语言那一刻它一个字都不动（WI-2c 立下的那条修法在这里是第一次用到"混合项"上：
    /// 一颗要翻、其余不翻）。⛔ 因此不许退回 <c>SetAt</c>，也不许给"全部"那一句再起第二个键名——
    /// 它与占位文字本来就是同一句话（<c>searchLang.placeholder</c>）。</para>
    /// </summary>
    [Fact]
    public void TheMainWindowLanguageComboCarriesOneTranslatableRowAndSelfNamedRest()
    {
        var xaml = Code(ReadRepoFile("src/StarMark.UI/MainWindow.xaml"));
        var search = Code(ReadRepoFile("src/StarMark.UI/MainWindow.Search.cs"));

        // 数据项＋模板：格子里那颗元素自己带键，重贴才轮得到它。
        // ⚠ 必须<b>切到这颗 ComboBox 身上</b>再断言：主窗那颗排序下拉早就带着同一行模板，
        // 整档 Contains 在"把语言下拉的模板删掉"那一臂上是<b>绿</b>的（台架 C1 第一跑实测，坑表 #340 同族）。
        var at = xaml.IndexOf("<ComboBox x:Name=\"LanguageCombo\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "主窗那颗语言下拉不见了（改名或搬走了，判据要跟上）");
        var combo = xaml[at..(xaml.IndexOf("/>", at, StringComparison.Ordinal) + 2)];
        Assert.Contains("ItemTemplate=\"{StaticResource LocOptionTemplate}\"", combo, StringComparison.Ordinal);
        Assert.Contains("i18n:LocText.PlaceholderKey=\"searchLang.placeholder\"", combo, StringComparison.Ordinal);

        // 「全部」那一行交的是这颗键＋空字符串身份；其余各项是仓库语言自名（Python 在哪一版都叫 Python）
        Assert.Contains("LocOption.T(\"searchLang.placeholder\", LanguageAllValue)", search, StringComparison.Ordinal);
        Assert.Contains("LocOption.SelfName(l, l)", search, StringComparison.Ordinal);
        // ⛔ 中文常量当"全部"的显示文字＝第二份语言钉在代码里；它一回来，那颗格子又不会跟着换语言了
        Assert.DoesNotContain("语言：全部", search, StringComparison.Ordinal);
        // 读的是身份不是那串字：换语言之后显示文字变了，选中的还是同一项
        Assert.Contains("LanguageCombo.SelectedItem is LocOption { Value: string name }", search, StringComparison.Ordinal);
        Assert.Contains("o.Value is string name", search, StringComparison.Ordinal);   // 回灌选中项也按身份找
        Assert.DoesNotContain("items.IndexOf(current)", search, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>「类型筛选」浮出里那两句长说明，宽度上限要各自长在<b>文字那一颗</b>身上</b>（批次 WI-67 片12·P-205）。
    /// <para>为什么要有这一格：屏幕上中文档那两句是 348 宽、英／日各 438 宽，而浮出的可视宽只有 ~422
    /// ⇒ 英／日两语各多出一根<b>横向滚动条</b>（修前读数＝<c>bin/WI67/dump67w/dump7_pre67y_{zh,en,ja}_kindflyout.txt</c>，
    /// 修后同名三档里那根条不再出现）。
    /// 那两句本来就写着 <c>TextWrapping="Wrap"</c> 却不折行——因为浮出的呈现器沿水平方向<b>按无限宽测量</b>，
    /// 外层面板上的宽度约束传不进文字测量里 ⇒ 上限必须落在每一颗 TextBlock 自己身上。</para>
    /// <para>⚠ 这一格是<b>形状</b>判据：它钉"两颗各带 Wrap 与各带同一颗上限"，⛔ 钉"屏幕上那根条不再出现"
    /// ——后者由我在沙盒里三语各量一次，读数写进账本 P-205 与本批报告。</para>
    /// </summary>
    [Fact]
    public void TheKindFilterFlyoutNotesEachCarryTheirOwnWidthCap()
    {
        // 先抹 XML 注释再切：注释里出现 MaxWidth 是给人读的叙述，不算一处属性（同一口径＝Markup）
        var xaml = Markup(Code(ReadRepoFile(MainWindowXaml)));
        var at = xaml.IndexOf("<Button x:Name=\"FileKindButton\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "那颗类型筛选按钮不见了（改名或搬走了，判据要跟上）");
        var end = xaml.IndexOf("</Flyout>", at, StringComparison.Ordinal);
        Assert.True(end > at, "那颗按钮的浮出不见了");
        var flyout = xaml[at..(end + 9)];

        Assert.Equal(2, Count(flyout, "TextWrapping=\"Wrap\""));
        Assert.Equal(2, Count(flyout, "MaxWidth=\"380\""));
        Assert.Contains("MinWidth=\"176\"", flyout, StringComparison.Ordinal);   // 下限管摆位，⛔ 当上限用

        // 两颗上限要落在<b>两颗文字</b>上：都塞到外层面板＝整档计数照样绿，而屏幕上那根条还在（#340 那一族）
        foreach (var key in new[] { "kind.noteScope", "kind.noteSyntax" })
        {
            var k = flyout.IndexOf(key, StringComparison.Ordinal);
            Assert.True(k >= 0, key + " 那颗键不见了，这一格守的东西没人念了");
            var tag = flyout[flyout.LastIndexOf("<TextBlock", k, StringComparison.Ordinal)..(flyout.IndexOf("/>", k, StringComparison.Ordinal) + 2)];
            Assert.Contains("TextWrapping=\"Wrap\"", tag, StringComparison.Ordinal);
            Assert.Contains("MaxWidth=\"380\"", tag, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <b>组件窗自己的三条文字出口只许交键</b>（批次 WI-37-A，用户真机报「组件内容和右键菜单未国际化」）。
    /// <para>三条出口按宿主形状分三种接法（WI-2o 定下的口径，这里逐颗钉住）：
    /// ① <b>右键菜单</b>每次打开都重建（<c>OnContextMenuOpening</c>）⇒ 构建那一刻 <c>Locale.T</c> 就够；
    /// ② <b>外壳模式</b>那两颗项是建好常驻的，改文案由 <c>UpdateChromeControls</c> 现取词（同一形状的两种说法不许各写一份）；
    /// ③ <b>树里的元素</b>（拖放遮罩那句）挂 <c>LocText.SetKey</c>，由唯一的 <c>ReapplyAll</c> 重贴。
    /// 跟随光标的 <c>DragUIOverride.Caption</c> 是系统画的、每次 DragOver 重设 ⇒ 走 ①。</para>
    /// <para>⛔ 只扫这几个方法体、不扫整档：同档的护眼那一节（<c>BuildEyeRestSection</c>）还欠着，
    /// 它的选项字住在 Core（<c>EyeRestPolicy.IntervalLabels</c>／<c>NoticeLabels</c>），要连着设置页那两颗下拉一起改＝WI-37-B；
    /// 闹钟那一节在 <c>AlarmMenu.cs</c>＝WI-37-C。整档扫会把这两刀的债算成这一刀的。</para>
    /// <para>顺带钉两条"一个主人"：<b>同一句话不许第二个键</b>（「添加组件」沿用设置页那颗、「保存／取消」沿用对话框那对、
    /// 布局行与快照行共用 <c>nameWithSummary</c> 一颗）；<b>带图标那份展示名在组件窗里恰好一处</b>（菜单项那颗子菜单），
    /// 标题栏那颗已由 <see cref="TheWidgetTitleHasOneIconOwnerAndFollowsTheLanguage"/> 钉成键。</para>
    /// </summary>
    [Fact]
    public void TheWidgetMenuChromeAndDropHandKeysNotWords()
    {
        var offenders = new List<string>();

        void Scan(string file, string owner, params string[] signatures)
        {
            var source = Code(ReadRepoFile(file));
            foreach (var signature in signatures)
            {
                // StarLog 的中文日志不归这把尺子（#329）——按行摘掉再扫。
                var body = string.Join("\n", MethodBody(source, signature).Split('\n')
                    .Where(l => !l.Contains("StarLog.", StringComparison.Ordinal)));
                foreach (Match m in Regex.Matches(body, @"""[^""]*[\u4e00-\u9fff][^""]*"""))
                    offenders.Add($"{owner} {signature.Split('(')[0].Trim()}：{m.Value}");
            }
        }

        Scan(WidgetMenuCs, "菜单",
            "private void PopulateMenu(MenuFlyout menu)",
            "private async System.Threading.Tasks.Task SaveSnapshotByNameAsync()",
            "private async System.Threading.Tasks.Task SaveLayoutByNameAsync()",
            "private async System.Threading.Tasks.Task RenameAsync()");
        Scan(WidgetChromeCs, "外壳", "private void UpdateChromeControls()");
        Scan(WidgetDropCs, "拖放",
            "private void SetupQuickLaunchDrop()",
            "private async void QuickLaunch_DragOver(object sender, DragEventArgs e)");
        Scan(WidgetFactoryCs, "占位", "public UIElement Build(WidgetKind kind, WidgetWindow host)");

        Assert.True(offenders.Count == 0,
            "组件窗的界面串直交了中文（换语言那一刻它不跟着改）：\n" + string.Join("\n", offenders));

        var menu = Code(ReadRepoFile(WidgetMenuCs));
        // ① 重建那一刻现取词；同一句话不许有第二颗键
        Assert.Contains("Locale.T(\"settings.widgets.addRow\")", menu, StringComparison.Ordinal);
        Assert.Equal(1, Count(menu, "\"settings.widgets.addRow\""));
        // ⚠ 名字那颗从批次 WI-67 片6 起交<b>屏幕上的那一句</b>（库里是中性空串，词与序号由 WidgetDisplayName 现算），
        //    所以这里钉的实参前缀是 layoutNames/snapshotNames 而不是 l.Name/s.Name——那颗钉子只认"这一句走没走名字主人"。
        Assert.Contains("Locale.Fmt(\"widget.menu.nameWithSummary\", layoutNames.Word(", menu, StringComparison.Ordinal);
        Assert.Contains("Locale.Fmt(\"widget.menu.nameWithSummary\", snapshotNames.Word(", menu, StringComparison.Ordinal);
        Assert.Equal(2, Count(menu, "\"widget.menu.nameWithSummary\""));   // 布局行与快照行共用一颗
        Assert.Equal(2, Count(menu, "LocMessage.Of(\"dialog.save\")"));    // 两颗保存对话框，⛔ 不另起一份「保存」
        Assert.Equal(2, Count(menu, "LocMessage.Of(\"dialog.cancel\")"));
        // 带图标那份展示名：组件窗里只剩菜单那一个宿主（标题栏那颗是键，见 WI-36 那格）
        Assert.Equal(1, Count(menu, "WidgetStorage.KindTitle(kind)"));

        // ② 外壳两态各两颗词，且旧中文不许回来（回来就是"第二个人写这句话"）
        var chrome = Code(ReadRepoFile(WidgetChromeCs));
        Assert.Contains("Locale.T(_chromeMode == WidgetChromeMode.Compact", chrome, StringComparison.Ordinal);
        Assert.Contains("Locale.T(_chromeMode == WidgetChromeMode.Hidden", chrome, StringComparison.Ordinal);
        foreach (var back in new[] { "展开组件", "收起为胶囊", "显示外壳", "隐藏外壳" })
            Assert.DoesNotContain(back, chrome, StringComparison.Ordinal);

        // ③ 树里的元素挂键；系统画的那句每次现取
        var drop = Code(ReadRepoFile(WidgetDropCs));
        Assert.Contains("LocText.SetKey(hintText, \"widget.drop.hint\")", drop, StringComparison.Ordinal);
        Assert.Contains("Locale.T(\"widget.drop.caption\")", drop, StringComparison.Ordinal);
        Assert.DoesNotContain("松开以添加到快捷启动", drop, StringComparison.Ordinal);

        var factory = Code(ReadRepoFile(WidgetFactoryCs));
        Assert.Contains("Locale.Fmt(\"widget.notImplemented\", title)", factory, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>会换全局语言的那批证人类必须挂在同一个关并行集合上</b>（批次 WI-61b 附带的一处测试层修复）。
    /// <para>缺陷形状：<c>Locale.Current</c> 是 <c>static</c>，而本工程既没有 <c>xunit.runner.json</c>
    /// 也没有 assembly 级 <c>[CollectionBehavior]</c> ⇒ xunit v2 的两个默认值叠出来是"每个类各成一集合、
    /// 集合之间并发跑"。于是 A 类把语言按成英文时，B 类正拿"这一句在中文里该长什么样"做断言——
    /// 红的那一格指向的是<b>谁先抢到那颗静态</b>，⛔ 指向谁改坏了哪一行（那种红会把人领去改本来正确的判据）。</para>
    /// <para>口径＝抹掉注释后正文里出现 <c>Locale.TrySet(</c> 的那些档，逐档现扫（⛔ 凭记忆列名单＝#122/#141）；
    /// 2026-10-10 现场数到 15 颗，只登记成<b>下限</b>：以后再加主人必须同批挂属性，那颗"没挂"的红才是这一格的本职。
    /// 集合定义住在 <see cref="LocaleGlobalCollection"/>。</para>
    /// </summary>
    [Fact]
    public void EveryLocaleMutatingTestClassIsInTheSerialCollection()
    {
        var mutating = ReadRepoUnder("tests/StarMark.Tests")
            .Where(f => Code(f.Text).Contains("Locale.TrySet(", StringComparison.Ordinal))
            .ToList();
        Assert.True(mutating.Count >= 15,
                    $"只扫到 {mutating.Count} 颗会换语言的证人类（2026-10-10 现场是 15）⇒ 普查路径或口径变了，这一格在空转");
        var unguarded = mutating
            .Where(f => !f.Text.Contains("[Collection(\"LocaleGlobal\")]", StringComparison.Ordinal))
            .Select(f => f.RelativePath)
            .ToList();
        Assert.True(unguarded.Count == 0,
                    "这些证人类会换进程级语言却没进串行集合（并发跑会读到别人那一档的语言）：" + string.Join("、", unguarded));
    }
}
