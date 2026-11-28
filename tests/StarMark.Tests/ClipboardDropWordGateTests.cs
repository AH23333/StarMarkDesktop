#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.I18n;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 拖入文件那一族的屏幕字只许住在<b>文案表</b>里（片292·P-307 乙遗留四件，照 clip-images 的 house pattern
/// 配齐的词门；范式＝<c>ClipboardAssetsWordGateTests</c>，同一颗骨架的第二位住户）。
/// <para>两节：<b>节 A</b>＝「拖入文件占用」那一行的 10 颗（占用两段／存放路径／孤儿计数＋三颗途中话），
/// 语料 [ClipDropAssets, SettingsPageViewModel.Clipboard]；<b>节 B</b>＝备份回执里拖入文件那几句的 9 颗，
/// 语料 [BackupService, SettingsPage.Backup]。同一批配的键分两节而⛔ 两档：占用的主人是设置页那一行、
/// 回执的主人是导出/恢复那两句，射程与对照件各是各的。</para>
/// <para>⚠ 节 B ⛔ 做反向 unasked：<c>settings.data.backup.</c> 那一前缀的正反向名单是 ✅WI-62 的
/// <c>BackupWordGateTests</c> 在管（62 颗），同一问⛔ 写两遍（规则 #86）——本节只做正向逐颗点名。</para>
/// <para>⚠ 键的<b>身份级</b>断言（哪一档说话、槽里是哪颗读数、null 档闭嘴）由行为档在真盘路径上钉
/// （<c>ClipboardDropOrphanCleanupTests</c>／<c>ClipboardDropBackupAttachmentTests</c>）；本档钉词表这一侧：
/// 名单、槽数、三语、套娃什么时候取词、拼完之后的标点——同一颗键的两半问题各归各档（规则 #86）。</para>
/// <para>⚠ ⛔ 把这些问塞进 <c>ClipboardDropWiringGateTests</c>：那是 P-306 WidgetWindow 拖放接线的域
/// （写盘那一侧的十例），与设置页这一族隔着整条链路，合档会让两边的射程说明互相打架。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class ClipboardDropWordGateTests : IDisposable
{
    private const string Assets = "src/StarMark.Abstractions/Clipboard/ClipDropAssets.cs";
    private const string Policy = "src/StarMark.Abstractions/Clipboard/ClipboardPolicy.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Clipboard.cs";
    private const string BackupService = "src/StarMark.Core/Backup/BackupService.cs";
    private const string BackupUi = "src/StarMark.UI/Views/SettingsPage.Backup.cs";

    private static readonly string[] UsageFiles = [Assets, ViewModel];
    private static readonly string[] BackupFiles = [BackupService, BackupUi];

    /// <summary>节 A：占用那一行的 10 颗，逐颗点名（计数型判据抓不到"换名"＝台架规则 #164）。</summary>
    private static readonly string[] UsageKeys =
    [
        "drop.usage.empty", "drop.usage.files", "drop.usage.temps", "drop.usage.full",
        "drop.usage.folder", "drop.usage.line", "drop.usage.counting",
        "drop.usage.recountBlocked", "drop.usage.measureFailed", "drop.orphans.some",
    ];

    /// <summary>节 B：备份里拖入文件那几句的 9 颗（恢复那句套四档、导出那句套两档、
    /// 「只有清单没本体」那句、确认框那句"还带 N 件"）。</summary>
    private static readonly string[] BackupKeys =
    [
        "settings.data.backup.dropCarried", "settings.data.backup.dropSkipped",
        "settings.data.backup.dropFailed", "settings.data.backup.dropRowsMissing",
        "settings.data.backup.dropManifestOnly", "settings.data.backup.exportedDrops",
        "settings.data.backup.exportedDropsMissing", "settings.data.backup.exportedDropsFailed",
        "settings.data.backup.carriesDrops",
    ];

    /// <summary>两颗<b>纯胶水行</b>：三列同形是刻意的（拼接位在 C# 那一侧），已在
    /// <c>I18nContractTests</c> 那份同形册里逐颗登记理由，⛔ 收进"三语互异"那一问。</summary>
    private static readonly string[] Glue = ["drop.usage.full", "drop.usage.line"];

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => Code(ReadRepoFile(rel));

    private static string Quoted(string text) => "\"" + text + "\"";

    /// <summary>那一行按槽位顺序填上实参（只用于本档自己的读数，⛔ 拿它当"表里的措辞"）。</summary>
    private static string Filled(string key, params object[] args)
        => LocMessage.Of(key, args.Select(a => (object?)a).ToArray()).Render();

    // ────────── 节 A ① 归零＋分母 ──────────

    /// <summary>
    /// 那两位供话者的<b>方法体</b>里一颗屏幕字都不剩；整档 ClipDropAssets 也归零——这一档全是
    /// 纯函数与键名，"零"在这里是正判据：再冒出一颗屏幕字就是回归。
    /// <para>归零那颗问⛔ 没有分母就读不成证据（规则 #188：一把静默读成 0 的尺比红更危险）。
    /// 对照分母与 <c>ClipboardAssetsWordGateTests</c>①同源：那四发拒绝理由只进日志（✅WI-64c 判定），
    /// ⛔ 会随哪一批搬字归零，所以它当分母当得稳——同一颗分母两档共用而⛔ 各抄一份定位（规则 #86）。</para>
    /// </summary>
    [Fact]
    public void TheDropSentenceFactoriesSayNothingThemselves()
    {
        foreach (var (rel, signature) in new[]
                 {
                     (Assets, "public static LocMessage DescribeUsage("),
                     (Assets, "public static LocMessage? DescribeOrphans("),
                 })
        {
            var body = MethodBody(CodeOf(rel), signature);
            Assert.Empty(ScreenWords(body));
        }

        Assert.Empty(ScreenWords(CodeOf(Assets)));
        var restPolicy = ScreenWords(CodeOf(Policy));
        Assert.True(restPolicy.Count >= 9,
            $"对照分母不够宽：ClipboardPolicy {restPolicy.Count} 片段"
            + "——这把尺⛔ 扫得到东西，上面那些 0 不算证据");
    }

    // ────────── 节 A ② 形状禁项：交出身份，⛔ 当场取词 ──────────

    /// <summary>
    /// 供话者只交<b>身份</b>：方法体里不许 <c>Locale.T(</c>／<c>Locale.Fmt(</c>／<c>.Render()</c>，
    /// 回型也⛔ 悄悄退成 <c>string</c>（那是"半搬"最先长回来的地方）。与①那格成对（规则 #124）：
    /// 只钉字面量看不见形状，只钉形状看不见字面量。
    /// <para>⚠ VM 整档的取词禁项由 <c>ClipboardAssetsWordGateTests</c>②钉着（同一颗档），
    /// ⛔ 在这里再数一遍（规则 #86）——本格只钉 ClipDropAssets 自己那两位。</para>
    /// </summary>
    [Fact]
    public void TheDropSuppliersHandIdentitiesAndNeverTakeWords()
    {
        foreach (var (rel, signature) in new[]
                 {
                     (Assets, "public static LocMessage DescribeUsage("),
                     (Assets, "public static LocMessage? DescribeOrphans("),
                 })
        {
            var body = MethodBody(CodeOf(rel), signature);
            foreach (var banned in new[] { "Locale.T(", "Locale.Fmt(", ".Render()" })
                Assert.Equal(0, Count(body, banned));
        }

        var assets = CodeOf(Assets);
        Assert.Contains("public static LocMessage DescribeUsage(", assets, StringComparison.Ordinal);
        Assert.Contains("public static LocMessage? DescribeOrphans(", assets, StringComparison.Ordinal);
        Assert.Equal(0, Count(assets, "public static string DescribeUsage("));
        Assert.Equal(0, Count(assets, "public static string DescribeOrphans("));
    }

    // ────────── 节 A ③ 名单双向对账 ──────────

    /// <summary>
    /// 10 颗每颗在表里恰好一行、在那两档里恰好点名一次；<b>反方向</b>也钉：表里 <c>drop.</c> 那一族的
    /// 每一行都得有人问。只钉正向时"多加一行没人问"是全绿的——那行字永远上不了屏幕，
    /// 而没人会 notice 到一句没出现的话；只钉颗数时"换名"是全绿的（规则 #164）。⇒ 逐颗点名＋集合相等。
    /// </summary>
    [Fact]
    public void EveryDropUsageKeyIsDeclaredOnceAndNamedExactlyOnce()
    {
        Assert.Equal(10, UsageKeys.Length);
        Assert.Equal(UsageKeys.Length, UsageKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(9, UsageKeys.Count(k => k.StartsWith("drop.usage.", StringComparison.Ordinal)));
        Assert.Equal(1, UsageKeys.Count(k => k.StartsWith("drop.orphans.", StringComparison.Ordinal)));

        var codes = string.Join("\n", UsageFiles.Select(CodeOf));
        foreach (var key in UsageKeys)
        {
            Assert.Single(UiStrings.All, e => e.Key == key);
            Assert.Equal(1, Count(codes, Quoted(key)));
        }

        var asked = UsageKeys.ToHashSet(StringComparer.Ordinal);
        var unasked = UiStrings.All.Select(e => e.Key)
            .Where(k => k.StartsWith("drop.", StringComparison.Ordinal))
            .Where(k => !asked.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.True(unasked.Count == 0, "表里多了没人问的行：" + string.Join("、", unasked));
    }

    // ────────── 节 A ④ 实参颗数＝那一行的槽数 ──────────

    /// <summary>
    /// 每一处 <c>LocMessage.Of(本族的键…)</c> 递出的顶层实参数<b>等于那一行声明的槽数</b>
    /// （共用件 <see cref="SourceGate.KeyedCallSites"/> 按括号深度走，⛔ 用正则切跨行调用＝#155）。
    /// <para>少递一颗时表闸门全绿、编译也过，屏幕上那一格<b>整空白</b>（坑表 #393）。</para>
    /// </summary>
    [Fact]
    public void EveryDropUsageCallSiteHandsAsManyArgsAsItsRowDeclares()
    {
        var family = UsageKeys.ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();
        var watched = 0;
        foreach (var rel in UsageFiles)
        {
            foreach (var (key, args, line) in KeyedCallSites(CodeOf(rel)))
            {
                if (!family.Contains(key)) continue;
                watched++;
                var slots = RowSlots(key);
                if (args != slots) offenders.Add($"{rel}:{line} 的 {key} 表里 {slots} 颗槽，代码递了 {args} 颗实参");
            }
        }
        Assert.Empty(offenders);
        Assert.Equal(10, watched);      // 一键一处：10 颗键各有唯一一位主人（与③那条"逐颗点名一次"同口径）
    }

    // ────────── 节 A ⑤ 每一行三语答话、槽填满 ──────────

    /// <summary>
    /// 每一颗：三列都非空、三语互异（两颗胶水行除外——它们三列同形是刻意的），
    /// 且在三语下<b>真取一次词</b>——槽全填满、⛔ 把身份念给用户听。
    /// </summary>
    [Fact]
    public void EveryDropUsageRowAnswersInThreeLanguagesAndFillsItsSlots()
    {
        var withSlots = 0;
        foreach (var key in UsageKeys)
        {
            Assert.True(UiStrings.TryGet(key, out var entry), "表里没有 " + key);
            var texts = new[] { entry.Zh, entry.En, entry.Ja };
            foreach (var text in texts) Assert.False(string.IsNullOrWhiteSpace(text), key + " 有一列是空的");

            var slots = RowSlots(key);
            if (slots > 0) withSlots++;

            if (Glue.Contains(key))
            {
                var expected = string.Concat(Enumerable.Range(0, slots).Select(i => "{" + i + "}"));
                foreach (var text in texts) Assert.Equal(expected, text);
                continue;
            }
            Assert.NotEqual(texts[0], texts[1]);
            Assert.NotEqual(texts[1], texts[2]);
            Assert.NotEqual(texts[0], texts[2]);

            var args = Enumerable.Range(0, slots).Select(i => (object)("M" + i)).ToArray();
            var said = new List<string>();
            foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
            {
                Locale.TrySet(language);
                // ⛔ 拿 TrySet 的返回值当"换没换到"的探针：它说的是"变没变"（#112）。
                Assert.Equal(language, Locale.Current);
                var text = slots == 0 ? Locale.T(key) : Locale.Fmt(key, args);
                Assert.DoesNotContain("{", text, StringComparison.Ordinal);
                Assert.DoesNotContain("}", text, StringComparison.Ordinal);
                Assert.DoesNotContain("drop.", text, StringComparison.Ordinal);   // ⛔ 把身份念给用户听
                if (slots > 0) Assert.Contains("M0", text, StringComparison.Ordinal);
                said.Add(text);
            }
            Assert.NotEqual(said[0], said[1]);
            Assert.NotEqual(said[1], said[2]);
        }
        Assert.Equal(7, withSlots);      // 2026-11-28 现场：带槽 7 颗；empty／counting／recountBlocked 三颗无槽
    }

    // ────────── 节 A ⑥ 套娃那一行⛔ 半中半英 ──────────

    /// <summary>
    /// 那一行是<b>两层套娃</b>（整行 → 占用（又套件数与临时件两段）→ 尾句）。判据＝同一棵树在
    /// <b>三种语言各建一遍</b>、又各自在三种语言下<b>贴一次</b>：贴在现在这一拍的两句必须逐字相等，
    /// 且与"建在同一语言、贴在同一语言"那一句相等（范式＝<c>ClipboardAssetsWordGateTests</c>⑥）。
    /// <para>谁把内层改成<b>算的时候就取词</b>（<c>DescribeUsage</c> 里先 <c>.Render()</c> 再递字符串），
    /// "在中文里算、到英文里贴"那一格就与"在英文里算、在英文里贴"分岔——屏幕上剩下半中半英。
    /// ①②那两格抓的是"字在不在表里"，这一格抓的是<b>什么时候去表里取</b>。</para>
    /// </summary>
    [Fact]
    public void TheNestedDropUsageLineStandsInTheSameLanguage()
    {
        var languages = new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese };
        var fp = new ClipDropAssets.DropFootprint(30, 9_000_000);

        foreach (var paste in languages)
        {
            Locale.TrySet(paste);
            Assert.Equal(paste, Locale.Current);
            var said = DropUsageLine(fp, 61_440, 2, "D:\\StarMark\\clip-drops", 7, 20_971_520, blocked: false).Render();

            foreach (var build in languages)
            {
                Locale.TrySet(build);
                var froze = DropUsageLine(fp, 61_440, 2, "D:\\StarMark\\clip-drops", 7, 20_971_520, blocked: false);
                Locale.TrySet(paste);
                Assert.Equal(said, froze.Render());
            }

            Assert.DoesNotContain("{", said, StringComparison.Ordinal);
            Assert.DoesNotContain("}", said, StringComparison.Ordinal);
            Assert.DoesNotContain("drop.", said, StringComparison.Ordinal);
            Assert.Contains("30", said, StringComparison.Ordinal);            // 件数那颗读数进得去
            Assert.Contains("7", said, StringComparison.Ordinal);             // 孤儿那颗读数进得去
            Assert.Contains("D:\\StarMark\\clip-drops", said, StringComparison.Ordinal);
        }
    }

    /// <summary>屏幕上那一行的<b>组装顺序</b>（与 VM 那一处同形）：占用 → 存放路径 → 尾句（孤儿或"对账没跑成"）。</summary>
    private static LocMessage DropUsageLine(ClipDropAssets.DropFootprint fp, long tempBytes, int tempCount,
        string folder, int orphans, long orphanBytes, bool blocked)
        => LocMessage.Of("drop.usage.line",
            ClipDropAssets.DescribeUsage(fp, tempBytes, tempCount),
            LocMessage.Of("drop.usage.folder", folder),
            blocked ? LocMessage.Of("drop.usage.recountBlocked")
                    : ClipDropAssets.DescribeOrphans(orphans, orphanBytes));

    // ────────── 节 A ⑦ 跨格标点对账（真机屏幕读数的替身） ──────────

    /// <summary>
    /// <b>语言读数</b>：三段拼完之后，屏幕上不许出现"两个标点叠在一起"那种缝（坑表 #469 立的口径——
    /// 形状尺对跨格标点叠加完全失明）。这里⛔ 抄表里的措辞，判的是<b>拼完之后</b>那串里有没有禁项子串。
    /// <para>身份级的那半（哪一档说话、槽里是哪颗读数）由 <c>ClipboardDropOrphanCleanupTests</c> 钉着，
    /// 本格⛔ 再数一遍（规则 #86）。</para>
    /// </summary>
    [Fact]
    public void TheComposedDropLineNeverLeavesADoubledPunctuationOrALoneSeparator()
    {
        string[] banned = ["。。", "，。", "。，", "；。", "。；", "，，", "；；", "：：", "（）", " ；", "； "];
        var full = new ClipDropAssets.DropFootprint(30, 9_000_000);

        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            Assert.Equal(language, Locale.Current);

            var lines = new[]
            {
                DropUsageLine(full, 61_440, 2, "D:\\StarMark\\clip-drops", 7, 20_971_520, blocked: false).Render(),
                DropUsageLine(full, 0, 0, "D:\\My Docs\\clip-drops", 0, 0, blocked: false).Render(),
                DropUsageLine(new ClipDropAssets.DropFootprint(0, 0), 0, 0, "D:\\x\\clip-drops", 0, 0, blocked: false).Render(),
                DropUsageLine(full, 61_440, 2, "D:\\StarMark\\clip-drops", 0, 0, blocked: true).Render(),
            };
            foreach (var said in lines)
            {
                foreach (var pair in banned)
                    Assert.False(said.Contains(pair, StringComparison.Ordinal), $"「{pair}」出现在这一句里：{said}");
                Assert.DoesNotContain("{", said, StringComparison.Ordinal);
                Assert.DoesNotContain("}", said, StringComparison.Ordinal);
            }

            // 整行＝三段按序拼起来（⛔ 中间再自己加东西，也⛔ 少一段）
            var composed = DropUsageLine(full, 61_440, 2, "D:\\StarMark\\clip-drops", 7, 20_971_520, blocked: false).Render();
            Assert.Equal(
                ClipDropAssets.DescribeUsage(full, 61_440, 2).Render()
                + Filled("drop.usage.folder", "D:\\StarMark\\clip-drops")
                + ClipDropAssets.DescribeOrphans(7, 20_971_520)!.Render(),
                composed);      // Assert.Equal(string, string) 就是 Ordinal 比较

            // 「没有临时件」那一发：那一整段⛔ 出现，而⛔ 剩一颗孤零零的分隔符（哪一档说话由行为档钉）
            var tempsClause = Filled("drop.usage.temps", 2, ClipAssets.DescribeBytes(61_440));
            Assert.Contains(tempsClause, DropUsageLine(full, 61_440, 2, "D:\\a\\clip-drops", 0, 0, false).Render(), StringComparison.Ordinal);
            Assert.DoesNotContain(tempsClause, DropUsageLine(full, 0, 0, "D:\\a\\clip-drops", 0, 0, false).Render(), StringComparison.Ordinal);

            // 「对账没跑成」与「孤儿计数」是同一颗槽的两种下落：同时在场＝同一句话说两遍
            var both = LocMessage.Of("drop.usage.line",
                ClipDropAssets.DescribeUsage(full, 0, 0),
                LocMessage.Of("drop.usage.folder", "D:\\a\\clip-drops"),
                LocMessage.Of("drop.usage.recountBlocked")).Render();
            Assert.DoesNotContain(ClipDropAssets.DescribeOrphans(7, 20_971_520)!.Render(), both, StringComparison.Ordinal);
        }
    }

    // ────────── 节 B ① 名单正向对账（⛔ 反向，见档头） ──────────

    /// <summary>
    /// 9 颗每颗在表里恰好一行、在那两档里恰好点名一次。needle 用 <see cref="Quoted"/> 形
    /// （带收尾引号）：<c>exportedDrops</c> 是 <c>exportedDropsMissing</c> 的前缀，裸 needle 会把两处数成一处的两倍。
    /// </summary>
    [Fact]
    public void EveryBackupDropKeyIsDeclaredOnceAndNamedExactlyOnce()
    {
        Assert.Equal(9, BackupKeys.Length);
        Assert.Equal(BackupKeys.Length, BackupKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(5, BackupKeys.Count(k => k.StartsWith("settings.data.backup.drop", StringComparison.Ordinal)));
        Assert.Equal(3, BackupKeys.Count(k => k.StartsWith("settings.data.backup.exportedDrops", StringComparison.Ordinal)));
        Assert.Equal(1, BackupKeys.Count(k => k == "settings.data.backup.carriesDrops"));

        var codes = string.Join("\n", BackupFiles.Select(CodeOf));
        foreach (var key in BackupKeys)
        {
            Assert.Single(UiStrings.All, e => e.Key == key);
            Assert.Equal(1, Count(codes, Quoted(key)));
        }
    }

    // ────────── 节 B ② 实参颗数＝那一行的槽数 ──────────

    /// <summary>
    /// 每一处 <c>LocMessage.Of(本族的键…)</c> 递出的顶层实参数<b>等于那一行声明的槽数</b>
    /// （<c>dropCarried</c> 那句嵌套的三个可选档各算各的主人，共用件看得见嵌套）。
    /// <para>少递一颗时表闸门全绿、编译也过，屏幕上那一格<b>整空白</b>（坑表 #393）。</para>
    /// </summary>
    [Fact]
    public void EveryBackupDropCallSiteHandsAsManyArgsAsItsRowDeclares()
    {
        var family = BackupKeys.ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();
        var watched = 0;
        foreach (var rel in BackupFiles)
        {
            foreach (var (key, args, line) in KeyedCallSites(CodeOf(rel)))
            {
                if (!family.Contains(key)) continue;
                watched++;
                var slots = RowSlots(key);
                if (args != slots) offenders.Add($"{rel}:{line} 的 {key} 表里 {slots} 颗槽，代码递了 {args} 颗实参");
            }
        }
        Assert.Empty(offenders);
        Assert.Equal(9, watched);       // 一键一处：9 颗键各有唯一一位主人（与①那条"逐颗点名一次"同口径）
    }

    // ────────── 节 B ③ 每一行三语答话、槽填满 ──────────

    /// <summary>每一颗：三列都非空、三语互异，且在三语下<b>真取一次词</b>——槽全填满、
    /// ⛔ 把身份念给用户听（本节 9 颗全带槽：连最短的"还带 N 件"也是一句带读数的话）。</summary>
    [Fact]
    public void EveryBackupDropRowAnswersInThreeLanguagesAndFillsItsSlots()
    {
        foreach (var key in BackupKeys)
        {
            Assert.True(UiStrings.TryGet(key, out var entry), "表里没有 " + key);
            var texts = new[] { entry.Zh, entry.En, entry.Ja };
            foreach (var text in texts) Assert.False(string.IsNullOrWhiteSpace(text), key + " 有一列是空的");
            Assert.NotEqual(texts[0], texts[1]);
            Assert.NotEqual(texts[1], texts[2]);
            Assert.NotEqual(texts[0], texts[2]);

            var slots = RowSlots(key);
            Assert.True(slots > 0, key + " 没有槽：这一族的键全是带读数的话");
            var args = Enumerable.Range(0, slots).Select(i => (object)("M" + i)).ToArray();
            var said = new List<string>();
            foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
            {
                Locale.TrySet(language);
                Assert.Equal(language, Locale.Current);
                var text = Locale.Fmt(key, args);
                Assert.DoesNotContain("{", text, StringComparison.Ordinal);
                Assert.DoesNotContain("}", text, StringComparison.Ordinal);
                Assert.DoesNotContain("settings.data.", text, StringComparison.Ordinal);   // ⛔ 把身份念给用户听
                Assert.Contains("M0", text, StringComparison.Ordinal);
                said.Add(text);
            }
            Assert.NotEqual(said[0], said[1]);
            Assert.NotEqual(said[1], said[2]);
        }
    }

    // ────────── 节 B ④ 套娃回执⛔ 半中半英 ──────────

    /// <summary>
    /// 恢复回执里那一句（<c>dropCarried</c> 套四个可选档）与导出回执里那一句（<c>exportedDrops</c>
    /// 套两个可选档）都是<b>两层套娃</b>。判据＝同一棵树在<b>三种语言各建一遍</b>、又各自在三种语言下
    /// <b>贴一次</b>（范式＝节 A⑥）：谁把某一档改成"算的时候就取词"，那一格就分岔——屏幕上剩下半中半英。
    /// <para>身份级的那半（哪一档说话、槽里是哪颗读数、null 档闭嘴）由
    /// <c>ClipboardDropBackupAttachmentTests</c> 在真盘路径上钉着，本格⛔ 再数一遍（规则 #86）。</para>
    /// </summary>
    [Fact]
    public void TheNestedBackupDropSentencesStandInTheSameLanguageTheyArePastedIn()
    {
        var languages = new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese };
        foreach (var paste in languages)
        {
            Locale.TrySet(paste);
            Assert.Equal(paste, Locale.Current);
            var carried = DropCarried(12, 9, 2, 1, 3).Render();
            var exported = ExportedDrops(4, ClipAssets.DescribeBytes(2_097_152), 1, 0).Render();

            foreach (var build in languages)
            {
                Locale.TrySet(build);
                var frozeCarried = DropCarried(12, 9, 2, 1, 3);
                var frozeExported = ExportedDrops(4, ClipAssets.DescribeBytes(2_097_152), 1, 0);
                Locale.TrySet(paste);
                Assert.Equal(carried, frozeCarried.Render());
                Assert.Equal(exported, frozeExported.Render());
            }

            foreach (var said in new[] { carried, exported })
            {
                Assert.DoesNotContain("{", said, StringComparison.Ordinal);
                Assert.DoesNotContain("}", said, StringComparison.Ordinal);
                Assert.DoesNotContain("settings.data.", said, StringComparison.Ordinal);   // ⛔ 把身份念给用户听
            }
            Assert.Contains("12", carried, StringComparison.Ordinal);          // 包里带着几件
            Assert.Contains("9", carried, StringComparison.Ordinal);           // 写回几件
            Assert.Contains("4", exported, StringComparison.Ordinal);          // 导出那颗读数
        }
    }

    /// <summary>恢复回执里那一句（与 <c>BackupService.DropPackageSentence</c> 同形）：没出现的那一档交 null。</summary>
    private static LocMessage DropCarried(int entries, int written, int skipped, int failed, int rowsMissing)
        => LocMessage.Of("settings.data.backup.dropCarried", entries, written,
            skipped > 0 ? LocMessage.Of("settings.data.backup.dropSkipped", skipped) : null,
            failed > 0 ? LocMessage.Of("settings.data.backup.dropFailed", failed) : null,
            rowsMissing > 0 ? LocMessage.Of("settings.data.backup.dropRowsMissing", rowsMissing) : null);

    /// <summary>导出回执里那一句（与 SettingsPage.Backup 那处嵌套同形）：两小段没出现的那一档交 null。</summary>
    private static LocMessage ExportedDrops(int files, string bytesReading, int missing, int failed)
        => LocMessage.Of("settings.data.backup.exportedDrops", files, bytesReading,
            missing > 0 ? LocMessage.Of("settings.data.backup.exportedDropsMissing", missing) : null,
            failed > 0 ? LocMessage.Of("settings.data.backup.exportedDropsFailed", failed) : null);
}
