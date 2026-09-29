#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 显示侧<b>日期/时间</b>的形状闸门（批次 RY，P-122）：把"跟着系统文化跑"这类写法钉死在源码里。
/// <para>
/// <b>六条规则各管一种语法</b>，这是照着 #189 的教训设计的——同一件事有好几种写法，只认一种等于没守：
/// ① 内插格式 <c>{due:HH:mm}</c> 只许出现在判据文件里；
/// ② <c>ToString("&lt;日期格式&gt;"</c> 必须在<b>同一次调用</b>里带 <c>InvariantCulture</c>；
/// ③ <b>UI 工程里一个日期格式串都不许出现</b>（连"已经锁了不变文化"也不行——那样显示口径又有了第二个主人）；
/// ④ 星期名标记 <c>ddd</c> 除判据外一律禁止，<b>即使锁了不变文化也禁</b>（那会打出 Sunday，中文界面里的另一种坏）；
/// ⑤ 判据自己得真的拿着格式串，且扫描器对<b>每种字符串写法</b>都看得见它（否则①～④会"因为扫不到而全绿"）；
/// ⑥ XAML 的 <c>StringFormat</c> 用的是绑定文化的历法，与①同源；今天全仓零命中，属反回归。
/// </para>
/// <para>
/// 为什么这条闸门存在：账本 P-122 的普查用的是 <c>grep ToString("</c>，于是<b>整类内插写法都不在它眼里</b>——
/// 本批把四种写法摊开对 HEAD 重数（凭据脚本 <c>census_ry_head.py</c>，逐条清单 <c>ry_head_census.txt</c>）：
/// 共 <b>29 条命中、26 个调用点</b>，而账本只列了 12 处。漏掉的那一半里还包含<b>截图文件名</b>与<b>备份档名</b>
/// （非公历区域会生成"2569…"这种文件名）。⇒ 普查的语法面＝闸门的语法面，两边都得列全。
/// </para>
/// <para>
/// <b>数字那半在隔壁</b> <see cref="NumberFormatGateTests"/>（批次 RZ）。两份闸门共用
/// <see cref="FormatScanner"/>——扫描器自己也只许有一份（#190 那四个 bug 各写一遍是灾难）。
/// </para>
/// </summary>
public sealed class DisplayDateFormatGateTests
{
    internal const string JudgeFile = "src/StarMark.Abstractions/DateTimeText.cs";
    internal const string UiDir = "src/StarMark.UI";

    // ────────── ① 内插格式：除判据外一律禁止 ──────────

    [Fact]
    public void InterpolatedDateFormatsExistNowhereButTheJudge()
    {
        var hits = new List<string>();
        foreach (var (path, _, literals) in FormatScanner.SourcesUnder("src", Skip(JudgeFile)))
            foreach (var (hole, format) in literals.SelectMany(FormatScanner.HoleFormats))
                if (FormatScanner.IsDateFormatLiteral(format))
                    hits.Add($"{path}: {hole}");

        Assert.True(hits.Count == 0,
            "内插写法又长出日期格式了（这类完全没锁文化，非公历区域会得到 2569/1447 这种年份）：\n"
            + string.Join("\n", hits));
    }

    // ────────── ② ToString 传日期格式必须锁不变文化 ──────────

    [Fact]
    public void EveryDateFormatToStringCarriesInvariantCulture()
    {
        var offenders = new List<string>();
        var pinned = 0;
        foreach (var (path, code, _) in FormatScanner.SourcesUnder("src"))
            foreach (var args in FormatScanner.ToStringArgumentLists(code))
            {
                // 实参表里的字面量识别复用同一次扫描，不再各写一套走法
                if (!FormatScanner.Scan(args).Literals.Any(FormatScanner.IsDateFormatLiteral)) continue;
                if (args.Contains("InvariantCulture", StringComparison.Ordinal)) pinned++;
                else offenders.Add($"{path}: ToString({args.Trim()})");
            }

        // 反空转：一条扫不到任何东西的闸门会冒充绿灯（#161/#189）。文件名那一路本就有十来处锁定，扫不到＝判据错了。
        Assert.True(pinned >= 10,
            $"只认出 {pinned} 处「已锁不变文化的日期格式」（预期 ≥10）——标记表或扫描路径失效了，先修闸门再说结论");
        Assert.True(offenders.Count == 0,
            "有日期格式串没锁 InvariantCulture（显示会随系统文化变历法）：\n" + string.Join("\n", offenders));
    }

    // ────────── ③ UI 工程零日期格式串 ──────────

    /// <summary>
    /// UI 里<b>连"已锁不变文化"的格式串都不许留</b>：显示口径只能有一个主人（<c>DateTimeText</c>）。
    /// 否则同一段文字在卡片里是 <c>HH:mm</c>、在组件里是 <c>DateTimeText.Clock</c>，改一处不会让另一处变红。
    /// </summary>
    [Fact]
    public void UiProjectHoldsNoDateFormatLiteralsAtAll()
    {
        var offenders = FormatScanner.SourcesUnder(UiDir)
            .SelectMany(f => f.Literals
                .Where(FormatScanner.IsDateFormatLiteral)
                .Select(lit => $"{f.Path}: \"{lit}\""))
            .ToList();

        Assert.True(offenders.Count == 0,
            "UI 层又自己拿日期格式串了（该走 DateTimeText）：\n" + string.Join("\n", offenders));
    }

    // ────────── ④ 星期名只从判据那张表来 ──────────

    /// <summary>
    /// <c>ddd</c>/<c>dddd</c> 除判据外一律禁止，<b>即使锁了不变文化也禁</b>——
    /// 锁不变文化只会把"星期日"换成 <c>Sunday</c>，在中文界面里那是另一种坏（P-122 原文特意点出的那一类）。
    /// </summary>
    [Fact]
    public void WeekdayNamesComeOnlyFromTheJudge()
    {
        var offenders = FormatScanner.SourcesUnder("src", Skip(JudgeFile))
            .SelectMany(f => f.Literals
                .Where(lit => FormatScanner.IsDateFormatLiteral(lit) && lit.Contains("ddd", StringComparison.Ordinal))
                .Select(lit => $"{f.Path}: \"{lit}\""))
            .ToList();

        Assert.True(offenders.Count == 0,
            "日期格式串里又出现了 ddd/dddd（星期名该走 DateTimeText.Weekday 那张表）：\n"
            + string.Join("\n", offenders));
    }

    // ────────── ⑤ 判据自己也得有东西可扫 ──────────

    /// <summary>判据不许被掏空：它自己得真的拿着那些格式串（否则上面四条会因为"到处都没有格式串"而假绿）。</summary>
    [Fact]
    public void TheJudgeItselfHoldsTheFormats()
    {
        var (code, literals) = FormatScanner.Scan(ReadRepoFile(JudgeFile));
        var held = literals.Count(FormatScanner.IsDateFormatLiteral);
        Assert.True(held >= 8, $"判据文件里只剩 {held} 个日期格式串——它不该是空壳");
        Assert.Contains("InvariantCulture", code, StringComparison.Ordinal);
    }

    /// <summary>把判据路径换成"跳过"用的尾巴（<c>SourcesUnder</c> 报的是仓根相对路径，按文件名筛最稳）。</summary>
    private static string Skip(string judgeFile) => Path.GetFileName(judgeFile);

    // ────────── ⑥ XAML 的 StringFormat ──────────

    /// <summary>
    /// 第六种写法：XAML 的 <c>StringFormat</c>——绑定格式化用的就是绑定文化的历法，与 C# 侧同一条病。
    /// 今天全仓零命中，所以这条是<b>反回归</b>；只抽 <c>StringFormat</c> 的值而不是整文件找标记，
    /// 因为设置页里有一句人话就写着"时钟上还会多显示一行「休息 HH:mm」"，整文件扫会把它误判成违规（#190 的 RI10）。
    /// <para>同时钉住"扫到了足够多个 xaml"，免得扫描路径写错时靠"什么都没扫到"冒充绿灯（#161）；
    /// 而抽取本身由 <see cref="StringFormatExtractorSeesBothXamlShapes"/> 自证，不靠"全仓为零"担保。</para>
    /// </summary>
    [Fact]
    public void XamlBindingsHoldNoDateFormatEither()
    {
        var xamls = Directory
            .GetFiles(Path.Combine(RepoRoot(), "src"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Replace(Path.DirectorySeparatorChar, '/').Contains("/obj/"))
            .ToList();
        Assert.True(xamls.Count >= 30, $"只扫到 {xamls.Count} 个 xaml（今天是 36）——扫描路径错了，这条绿灯不算数");

        var hits = xamls
            .SelectMany(f => FormatScanner.XamlStringFormats(File.ReadAllText(f))
                .Where(FormatScanner.IsDateFormatLiteral)
                .Select(fmt => $"{Path.GetFileName(f)}: StringFormat=\"{fmt}\""))
            .ToList();

        Assert.True(hits.Count == 0,
            "XAML 里出现了日期格式（StringFormat 会按绑定文化的历法渲染），请改绑 DateTimeText 产出的字符串：\n"
            + string.Join("\n", hits));
    }

    [Fact]
    public void StringFormatExtractorSeesBothXamlShapes()
    {
        const string markup = @"<TextBlock Text=""{x:Bind TimeText, Mode=OneWay, StringFormat={}{0:HH:mm}}"" />";
        const string property = @"Binding StringFormat=""yyyy-MM-dd dddd""";

        Assert.Contains(StringFormatsIn(markup), FormatScanner.IsDateFormatLiteral);      // 标记扩展那一支
        Assert.Contains("yyyy-MM-dd dddd", StringFormatsIn(property));                    // 属性文本那一支
        Assert.Empty(StringFormatsIn(@"<!-- 旧写法 StringFormat=""yyyy-MM-dd"" 已废弃 -->"));  // 注释里的例子不算行为
    }

    private static List<string> StringFormatsIn(string xaml) => FormatScanner.XamlStringFormats(xaml).ToList();
}
