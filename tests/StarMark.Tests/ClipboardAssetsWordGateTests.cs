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
/// 设置页「图片占用」与「图片采集状态」那两行的屏幕字只许住在<b>文案表</b>里（批次 WI-64 片1，P-193 的 Abstractions 半句）。
/// <para>这一族的难点不是"有没有把字交进表"，而是<b>一行四段套娃</b>：屏幕上那一行由
/// 占用（<c>clip.usage.full</c> 又套主图／缩略图／临时件三段）、预估、存放路径、孤儿计数<b>四段</b>拼成，
/// 任何一段停在上一门语言，用户就在一行里读到半中半英（＝ P-188 原始缺陷在这一族的形状）。
/// 所以这里既有<b>形状禁项</b>（⛔ 当场取词、回型⛔ 退成 <c>string</c>），也有一格<b>真拼一遍的语言读数</b>——
/// 形状尺对"建议。。点"那种跨格标点叠加完全失明（坑表 #469 立的口径）。</para>
/// <para>⚠ 射程：测试工程⛔ 引用 UI（#184），所以"VM 那一侧按什么顺序递那四段"在这档里量不到，
/// 那一问由 <c>SettingsDataWordGateTests</c> 钉"那两行确实交给宿主贴"＋真机屏幕读数（欠项 #575）回答。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class ClipboardAssetsWordGateTests : IDisposable
{
    private const string Assets = "src/StarMark.Abstractions/Clipboard/ClipAssets.cs";
    private const string Policy = "src/StarMark.Abstractions/Clipboard/ClipboardPolicy.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Clipboard.cs";
    private static readonly string[] CodeFiles = [Assets, Policy, ViewModel];

    /// <summary>本族 17 颗，逐颗点名（计数型判据抓不到"换名"＝台架规则 #164）。</summary>
    private static readonly string[] Keys =
    [
        "clip.usage.empty", "clip.usage.main", "clip.usage.none", "clip.usage.thumbs", "clip.usage.temps",
        "clip.usage.full", "clip.usage.folder", "clip.usage.line", "clip.usage.counting",
        "clip.usage.recountBlocked", "clip.usage.measureFailed",
        "clip.proj.none", "clip.proj.estimate", "clip.orphans.some",
        "clip.intake.off", "clip.intake.paused", "clip.intake.on",
    ];

    /// <summary>两颗<b>纯胶水行</b>：三列同形是刻意的（拼接位在 C# 那一侧，语序由整行决定），
    /// 与 <c>settings.data.backup.lineJoin</c> 同族，⛔ 收进"三语读数必须互异"那一问。</summary>
    private static readonly string[] Glue = ["clip.usage.full", "clip.usage.line"];

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => Code(ReadRepoFile(rel));

    private static string Quoted(string text) => "\"" + text + "\"";

    /// <summary>那一行按槽位顺序填上实参（只用于本档自己的读数，⛔ 拿它当"表里的措辞"）。</summary>
    private static string Filled(string key, params object[] args)
        => LocMessage.Of(key, args.Select(a => (object?)a).ToArray()).Render();

    /// <summary>屏幕上那一行的<b>组装顺序</b>：占用 → 预估 → 存放路径 → 尾句（孤儿或"对账没跑成"）。</summary>
    private static LocMessage UsageLine(ClipAssets.Footprint fp, long tempBytes, int cap, string folder,
        int orphans, long orphanBytes, bool blocked)
        => LocMessage.Of("clip.usage.line",
            ClipAssets.DescribeUsage(fp, tempBytes),
            ClipAssets.DescribeProjection(fp, cap),
            LocMessage.Of("clip.usage.folder", folder),
            blocked ? LocMessage.Of("clip.usage.recountBlocked")
                    : ClipAssets.DescribeOrphans(orphans, orphanBytes));

    // ────────── ① 归零（按方法体）＋分母 ──────────

    /// <summary>
    /// 片1 那四颗供话者的<b>方法体</b>里一颗屏幕字都不剩；同一把尺在同一档的<b>别处</b>必须量得出字。
    /// <para>为什么按方法体而⛔ 整档（与 <c>BackupWordGateTests</c> 那格不同）：✅WI-64 片1 只搬了三颗，
    /// 同档的 <c>CleanupConfirmBody</c> 当时是<b>片2</b> 的射程。✅WI-64 片2 落完后那颗也归了零
    /// （它由 <c>ClipboardCleanupBodyWordGateTests</c> 钉，⛔ 在本格再数一遍＝规则 #86），
    /// 整档剩下的<b>一颗</b>是 <c>DescribeTitle</c> 那颗兜底字——✅WI-64c 判它归 <b>P-199</b>（成品字写进
    /// <c>items.title</c>），⛔ 在本批射程。</para>
    /// <para>归零那颗问⛔ 没有分母就读不成证据（规则 #188：一把静默读成 0 的尺比红更危险）。</para>
    /// </summary>
    [Fact]
    public void TheThreeSentenceFactoriesSayNothingThemselves()
    {
        foreach (var (rel, signature) in new[]
                 {
                     (Assets, "public static LocMessage DescribeUsage("),
                     (Assets, "public static LocMessage DescribeProjection("),
                     (Assets, "public static LocMessage? DescribeOrphans("),
                     (Policy, "public static LocMessage DescribeImageIntake("),
                 })
        {
            var body = MethodBody(CodeOf(rel), signature);
            Assert.Empty(ScreenWords(body));
        }

        // 分母：同一把尺在同一个文件里仍扫得到字。两串都是<b>红字自己报的数</b>而⛔ 手打（规则 #476）。
        // ✅WI-64 片1 第一跑把阈值写成 10、现场报 9；✅WI-64 片2 搬完那七句后 ClipAssets <b>现读 1 片段</b>
        //   （就是上面那段说的 <c>DescribeTitle</c> 兜底字）。那颗 1 是本批<b>搬字搬出来的</b>，⛔ 当成"放宽判据"——
        //   这一问的对照宽度今天由 ClipboardPolicy 那 9 片段出：那四发拒绝理由只进日志（✅WI-64c 判定），
        //   ⛔ 会随哪一批搬字归零，所以它当分母当得稳。两串⛔ 与"整档汉字行"那把尺互读（口径不同＝坑表 #423）。
        var restAssets = ScreenWords(CodeOf(Assets));
        var restPolicy = ScreenWords(CodeOf(Policy));
        Assert.True(restAssets.Count >= 1 && restPolicy.Count >= 9,
            $"对照分母不够宽：ClipAssets {restAssets.Count}／ClipboardPolicy {restPolicy.Count} 片段"
            + "——这把尺⛔ 扫得到东西，上面那些 0 不算证据");
    }

    // ────────── ② 形状禁项：交出身份，⛔ 当场取词 ──────────

    /// <summary>
    /// 供话者只交<b>身份</b>：方法体里不许出现 <c>Locale.T(</c>／<c>Locale.Fmt(</c>／<c>.Render()</c>，
    /// 回型也⛔ 悄悄退成 <c>string</c>（那是"半搬"最先长回来的地方）。
    /// <para>与①那格<b>成对</b>（规则 #124）：只钉字面量的话，有人把取词搬进那颗方法、字照样是表里的；
    /// 只钉形状的话，有人把整句写死成中文字面量，形状尺看不见字面量。</para>
    /// <para>⚠ <c>DescribeOrphans</c>／<c>DescribeImageIntake</c> 的回型另各有主人
    /// （<c>ClipboardOrphanCleanupGateTests</c>／<c>ClipboardIntakeProseGateTests</c>），本格只把它们当作<b>取词禁项</b>的射程，⛔ 在此再数一遍签名（规则 #86）。</para>
    /// </summary>
    [Fact]
    public void TheSuppliersHandIdentitiesAndNeverTakeWords()
    {
        foreach (var (rel, signature) in new[]
                 {
                     (Assets, "public static LocMessage DescribeUsage("),
                     (Assets, "public static LocMessage DescribeProjection("),
                     (Assets, "public static LocMessage? DescribeOrphans("),
                     (Policy, "public static LocMessage DescribeImageIntake("),
                 })
        {
            var body = MethodBody(CodeOf(rel), signature);
            foreach (var banned in new[] { "Locale.T(", "Locale.Fmt(", ".Render()" })
                Assert.Equal(0, Count(body, banned));
        }

        // ⛔ 在 Abstractions／UI 那三档里出现"取词后回 string"的成品串：VM 那一行交的是身份。
        var vm = CodeOf(ViewModel);
        Assert.Equal(0, Count(vm, "Locale.T("));
        Assert.Equal(0, Count(vm, "Locale.Fmt("));
        Assert.Contains("public static LocMessage DescribeUsage(", CodeOf(Assets), StringComparison.Ordinal);
        Assert.Contains("public static LocMessage DescribeProjection(", CodeOf(Assets), StringComparison.Ordinal);
        Assert.Equal(0, Count(CodeOf(Assets), "public static string DescribeUsage("));
        Assert.Equal(0, Count(CodeOf(Assets), "public static string DescribeProjection("));
    }

    // ────────── ③ 名单双向对账 ──────────

    /// <summary>
    /// 17 颗每颗在表里恰好一行、在那三档里恰好点名一次；<b>反方向</b>也钉：表里那一族的每一行都得有人问。
    /// <para>只钉正向时"多加一行没人问"是全绿的——那行字永远上不了屏幕，而没人会 notice 到一句没出现的话；
    /// 只钉颗数时"换名"是全绿的（规则 #164）。⇒ 逐颗点名＋集合双向相等。</para>
    /// </summary>
    [Fact]
    public void EveryKeyIsDeclaredOnceAndNamedExactlyOnce()
    {
        Assert.Equal(17, Keys.Length);
        Assert.Equal(Keys.Length, Keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(11, Keys.Count(k => k.StartsWith("clip.usage.", StringComparison.Ordinal)));
        Assert.Equal(2, Keys.Count(k => k.StartsWith("clip.proj.", StringComparison.Ordinal)));
        Assert.Equal(1, Keys.Count(k => k.StartsWith("clip.orphans.", StringComparison.Ordinal)));
        Assert.Equal(3, Keys.Count(k => k.StartsWith("clip.intake.", StringComparison.Ordinal)));

        var codes = string.Join("\n", CodeFiles.Select(CodeOf));
        foreach (var key in Keys)
        {
            Assert.Single(UiStrings.All, e => e.Key == key);
            Assert.Equal(1, Count(codes, Quoted(key)));
        }

        var asked = Keys.ToHashSet(StringComparer.Ordinal);
        var unasked = UiStrings.All.Select(e => e.Key)
            .Where(k => k.StartsWith("clip.usage.", StringComparison.Ordinal)
                        || k.StartsWith("clip.proj.", StringComparison.Ordinal)
                        || k.StartsWith("clip.orphans.", StringComparison.Ordinal)
                        || k.StartsWith("clip.intake.", StringComparison.Ordinal))
            .Where(k => !asked.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.True(unasked.Count == 0, "表里多了没人问的行：" + string.Join("、", unasked));
    }

    // ────────── ④ 实参颗数＝那一行的槽数 ──────────

    /// <summary>
    /// 每一处 <c>LocMessage.Of(本族的键…)</c> 递出的顶层实参数<b>等于那一行声明的槽数</b>
    /// （共用件 <see cref="SourceGate.KeyedCallSites"/> 按括号深度走，⛔ 用正则切跨行调用＝#155）。
    /// <para>少递一颗时表闸门全绿、编译也过，屏幕上那一格<b>整空白</b>（坑表 #393）。</para>
    /// </summary>
    [Fact]
    public void EveryCallSiteHandsAsManyArgsAsItsRowDeclares()
    {
        var family = Keys.ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();
        var watched = 0;
        foreach (var rel in CodeFiles)
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
        Assert.Equal(17, watched);      // 一键一处：17 颗键各有唯一一位主人（与③那条"逐颗点名一次"同口径）
    }

    // ────────── ⑤ 每一行三语答话、槽填满 ──────────

    /// <summary>
    /// 每一颗：三列都非空、三语互异（两颗胶水行除外，它们三列同形是刻意的），
    /// 且在三语下<b>真取一次词</b>——槽全填满、⛔ 把身份念给用户听、读数进得了槽。
    /// </summary>
    [Fact]
    public void EveryRowAnswersInThreeLanguagesAndFillsItsSlots()
    {
        var withSlots = 0;
        foreach (var key in Keys)
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
                Assert.DoesNotContain("clip.", text, StringComparison.Ordinal);   // ⛔ 把身份念给用户听
                if (slots > 0) Assert.Contains("M0", text, StringComparison.Ordinal);
                said.Add(text);
            }
            Assert.NotEqual(said[0], said[1]);
            Assert.NotEqual(said[1], said[2]);
        }
        Assert.Equal(11, withSlots);      // 2026-10-11 现场（bin/WI64/rows_64.py 的「带槽 11 颗」与本格同一口径）
    }

    // ────────── ⑥ 套娃那一行⛔ 半中半英 ──────────

    /// <summary>
    /// 那一行是<b>三层套娃</b>（整行 → 占用 → 主图／缩略图／临时件）。判据＝同一棵树在<b>三种语言各建一遍</b>、
    /// 又各自在三种语言下<b>贴一次</b>：贴在现在这一拍的两句必须逐字相等，且与"建在同一语言、贴在同一语言"那一句相等。
    /// <para>谁把内层改成<b>算的时候就取词</b>（<c>DescribeUsage</c> 里先 <c>.Render()</c> 再递字符串），
    /// "在中文里算、到英文里贴"那一格就与"在英文里算、在英文里贴"分岔——屏幕上剩下半中半英。
    /// ①②那两格抓的是"字在不在表里"，这一格抓的是<b>什么时候去表里取</b>。</para>
    /// </summary>
    [Fact]
    public void TheNestedUsageLineStandsInTheSameLanguage()
    {
        var languages = new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese };
        var fp = new ClipAssets.Footprint(1_258_291, 122_880, 12, 12);

        foreach (var paste in languages)
        {
            Locale.TrySet(paste);
            Assert.Equal(paste, Locale.Current);
            var said = UsageLine(fp, 0, 200, "D:\\StarMark\\clip", 7, 20_971_520, blocked: false).Render();

            foreach (var build in languages)
            {
                Locale.TrySet(build);
                var froze = UsageLine(fp, 0, 200, "D:\\StarMark\\clip", 7, 20_971_520, blocked: false);
                Locale.TrySet(paste);
                Assert.Equal(said, froze.Render());
            }

            Assert.DoesNotContain("{", said, StringComparison.Ordinal);
            Assert.DoesNotContain("}", said, StringComparison.Ordinal);
            Assert.DoesNotContain("clip.", said, StringComparison.Ordinal);
            Assert.Contains("12", said, StringComparison.Ordinal);       // 主图那颗读数进得去
            Assert.Contains("7", said, StringComparison.Ordinal);        // 孤儿那颗读数进得去
            Assert.Contains("D:\\StarMark\\clip", said, StringComparison.Ordinal);
        }
    }

    // ────────── ⑦ 跨格标点对账（真机屏幕读数的替身） ──────────

    /// <summary>
    /// <b>语言读数</b>：四段拼完之后，屏幕上不许出现"两个标点叠在一起"那种缝。
    /// <para>这颗是坑表 #469 立的口径——形状尺（槽数、键名、每行都有人问）对<b>跨格标点叠加</b>完全失明：
    /// 每一格各自都合法，拼起来是「建议。。点」。这里⛔ 抄表里的措辞，判的是<b>拼完之后</b>那串里有没有禁项子串，
    /// 以及"没有临时件时那一整段⛔ 留下孤零零的分隔符"（旧写法用 <c>orphanSentence.Length &gt; 0</c> 回答"有没有这句话"，
    /// 现在交 <c>null</c>；某一段念起来为空时那一行只剩一个 <c>；</c>）。</para>
    /// </summary>
    [Fact]
    public void TheComposedLineNeverLeavesADoubledPunctuationOrALoneSeparator()
    {
        string[] banned = ["。。", "，。", "。，", "；。", "。；", "，，", "；；", "：：", "（）", " ；", "； "];
        var full = new ClipAssets.Footprint(1_258_291, 122_880, 12, 12);
        var bare = new ClipAssets.Footprint(1_258_291, 0, 12, 0);

        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            Assert.Equal(language, Locale.Current);

            var lines = new[]
            {
                UsageLine(full, 61_440, 200, "D:\\StarMark\\clip", 7, 20_971_520, blocked: false).Render(),
                UsageLine(bare, 0, 200, "D:\\My Docs\\clip", 0, 0, blocked: false).Render(),
                UsageLine(new ClipAssets.Footprint(0, 0, 0, 0), 0, 200, "D:\\x\\clip", 0, 0, blocked: true).Render(),
                UsageLine(full, 61_440, 200, "D:\\StarMark\\clip", 0, 0, blocked: false).Render(),
            };
            foreach (var said in lines)
            {
                foreach (var pair in banned)
                    Assert.False(said.Contains(pair, StringComparison.Ordinal), $"「{pair}」出现在这一句里：{said}");
                Assert.DoesNotContain("{", said, StringComparison.Ordinal);
                Assert.DoesNotContain("}", said, StringComparison.Ordinal);
            }

            // 整行＝四段按序拼起来（⛔ 中间再自己加东西，也⛔ 少一段）
            var composed = UsageLine(full, 61_440, 200, "D:\\StarMark\\clip", 7, 20_971_520, blocked: false).Render();
            Assert.Equal(
                ClipAssets.DescribeUsage(full, 61_440).Render()
                + ClipAssets.DescribeProjection(full, 200).Render()
                + Filled("clip.usage.folder", "D:\\StarMark\\clip")
                + ClipAssets.DescribeOrphans(7, 20_971_520)!.Render(),
                composed);      // Assert.Equal(string,string) 就是 Ordinal（这颗重载⛔ 收 StringComparison＝本档第一跑的 CS1503）

            // 「没有临时件」那一发：整段⛔ 出现，而⛔ 剩一颗孤零零的分隔符——"没话"交的是<b>那颗槽本身</b>为空。
            LocAssert.Silent(ClipAssets.DescribeUsage(full, 0), 2);     // 共用尺：同一问⛔ 在别处再写一遍（规则 #86）
            LocAssert.Silent(ClipAssets.DescribeUsage(bare, 0), 1);     // 缩略图那一格同理（0 张＝那颗槽整颗消失）
            var tempsClause = Filled("clip.usage.temps", ClipAssets.DescribeBytes(61_440));
            Assert.Contains(tempsClause, UsageLine(full, 61_440, 200, "D:\\a\\clip", 0, 0, false).Render(), StringComparison.Ordinal);
            Assert.DoesNotContain(tempsClause, UsageLine(full, 0, 200, "D:\\a\\clip", 0, 0, false).Render(), StringComparison.Ordinal);
            var thumbsClause = Filled("clip.usage.thumbs", 12, ClipAssets.DescribeBytes(122_880));
            Assert.DoesNotContain(thumbsClause, UsageLine(bare, 0, 200, "D:\\a\\clip", 0, 0, false).Render(), StringComparison.Ordinal);

            // 「对账没跑成」与「孤儿计数」是同一颗槽的两种下落：同时在场＝同一句话说两遍
            var both = LocMessage.Of("clip.usage.line",
                ClipAssets.DescribeUsage(full, 0),
                ClipAssets.DescribeProjection(full, 200),
                LocMessage.Of("clip.usage.folder", "D:\\a\\clip"),
                LocMessage.Of("clip.usage.recountBlocked")).Render();
            Assert.DoesNotContain(ClipAssets.DescribeOrphans(7, 20_971_520)!.Render(), both, StringComparison.Ordinal);
        }
    }
}
