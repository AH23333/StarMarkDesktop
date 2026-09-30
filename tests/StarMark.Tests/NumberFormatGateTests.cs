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
/// 显示侧<b>数字</b>的形状闸门（批次 RZ，P-122 的另一半）：把"会随文化改变分隔符"的数字格式钉死在判据里。
/// <para>
/// 判据面比 RY 的日期闸门窄，而且<b>窄是实测出来的</b>（见 <see cref="NumberTextTests"/> 的前提测）：
/// 只有会出现<b>小数点／千分位／百分号</b>的格式才会随文化变（<c>0.#</c>、<c>0.##</c>、<c>N0</c>、<c>F1</c>、<c>P0</c>…），
/// 而 <c>{7:00}</c>、<c>{1234.5:0}</c>、<c>{5}</c> 这类纯整数形状在 zh/en/de/ar-SA/ar-EG/hi/th 七种文化下<b>逐字相同</b>。
/// ⇒ 本闸门<b>不管补零</b>：把不该收的写进禁项，代价是闸门天天红、最后被人绕过（#144）。
/// </para>
/// <para>
/// <b>规则的骨架照抄 RY 的日期闸门</b>（同一件事有几种写法就管几条，#189），但<b>尺度按数字侧的实际改过</b>：
/// ① 内插孔里的数字格式除判据外一律禁止（孔<b>没法</b>传文化，锁不了就只能收口）；
/// ② <c>ToString("&lt;数字格式&gt;"</c> 在判据外必须<b>同一次调用</b>里带 <c>InvariantCulture</c>——
///    这一条比日期侧宽，而宽是<b>有意分岔</b>：计算器的 <c>G15</c> 管的是"给多少位有效数字"而不是"显示成什么形状"，
///    为它单开一颗 <c>NumberText</c> 出口是假抽象（它只有一个读者，而那读数也不是给人看的形状）；
/// ③ <b>UI 工程里一个数字格式串都不许出现</b>（连已锁不变文化的也不行——界面口径只许有一个主人）；
/// ④ 字节→人话那把梯子只许有一份（单位字面量 ≥2 种出现在同一个非判据文件里＝又长出一份梯子）；
/// ⑤ <c>ClipAssets.DescribeBytes</c> 只能是<b>纯转发</b>，不许长出自己的判断（RX 的"转发不是第二决策点"同一条）；
/// ⑥ 判据不许是空壳 + 扫描器自检（正反两面都撞：该红的红、该放过的放过）。
/// </para>
/// </summary>
public sealed class NumberFormatGateTests
{
    internal const string NumberJudgeFile = "src/StarMark.Abstractions/NumberText.cs";
    internal const string ByteJudgeFile = "src/StarMark.Abstractions/FileSizeText.cs";
    private static readonly string[] UnitLiterals = [" KB", " MB", " GB", " TB"];

    // ────────── ① 内插孔里的数字格式：除判据外一律禁止 ──────────

    [Fact]
    public void InterpolatedNumberFormatsExistNowhereButTheJudge()
    {
        var hits = new List<string>();
        foreach (var f in FormatScanner.SourcesUnder("src", Skip(NumberJudgeFile)))
            foreach (var (hole, format) in f.Literals.SelectMany(FormatScanner.HoleFormats))
                if (FormatScanner.IsNumberFormatLiteral(format))
                    hits.Add($"{f.Path}: {hole}");

        Assert.True(hits.Count == 0,
            "内插孔里又长出数字格式了（孔传不了 CultureInfo，于是跟着系统文化走：德式 1,5 / 1.234.567、阿拉伯式 1٫5 / 15٪）——请改走 NumberText：\n"
            + string.Join("\n", hits));
    }

    // ────────── ② 判据外的数字格式 ToString 必须锁不变文化 ──────────

    /// <summary>
    /// 计算器那几处（<c>G3</c>/<c>G15</c>/<c>#,0.00</c>/<c>0.0000</c>）今天自己锁着不变文化，
    /// 本条<b>放过它们、但要求"当场锁"</b>：把文化参数交给重载默认、只在别处锁的那一种要红。
    /// <para>与日期侧同一枚反空转凭证：认不出"已锁"的那些调用点＝标记表或扫描路径坏了，绿灯不算数（#161）。</para>
    /// </summary>
    [Fact]
    public void EveryNumberFormatToStringCarriesInvariantCulture()
    {
        var offenders = new List<string>();
        var pinned = 0;
        foreach (var (path, code, _, _) in FormatScanner.SourcesUnder("src"))
            foreach (var args in FormatScanner.ToStringArgumentLists(code))
            {
                if (!FormatScanner.Scan(args).Literals.Any(FormatScanner.IsNumberFormatLiteral)) continue;
                if (args.Contains("InvariantCulture", StringComparison.Ordinal)) pinned++;
                else offenders.Add($"{path}: ToString({args.Trim()})");
            }

        Assert.True(pinned >= 8,
            $"只认出 {pinned} 处「已锁不变文化的数字格式」（预期 ≥8）——标记表或扫描路径失效了，先修闸门再说结论");
        Assert.True(offenders.Count == 0,
            "有数字格式串没锁 InvariantCulture（同一个读数会随系统文化换小数点/千分位/百分号），请改走 NumberText：\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// ②b 标记表<b>故意不收</b>「不带位数的裸字母说明符」（<c>ToString("N")</c> 那一种）：
    /// 因为本仓这类写法<b>实测</b>全在 <c>Guid</c> 上（11 处 <c>Guid.NewGuid().ToString("N")</c>），
    /// 而 Guid 的 <c>"N"</c> 打的是十六进制串，七种文化逐字相同（<see cref="NumberTextTests.WhichShapesActuallyMove"/> 钉了反面）。
    /// <para>放过它的代价是"哪天有个 double 也这么写"就漏了 —— 所以把<b>放过的前提本身</b>钉住：
    /// 裸字母说明符的接收者里必须出现 <c>Guid</c>。前提哪天破了，这条会红，而不是悄悄漏。</para>
    /// </summary>
    [Fact]
    public void BareStandardSpecifierIsOnlyEverAGuidShape()
    {
        var offenders = new List<string>();
        var seen = 0;
        foreach (var (path, code, _, _) in FormatScanner.SourcesUnder("src"))
            foreach (Match m in BareStandard.Matches(code))
            {
                seen++;
                if (!m.Groups[1].Value.Contains("Guid", StringComparison.Ordinal))
                    offenders.Add($"{path}: {m.Value}");
            }

        Assert.True(seen >= 9,
            $"只认出 {seen} 处裸字母说明符（今天是 11 处 Guid 的形状串）——扫描路径或正则失效了，这条绿灯不算数");
        Assert.True(offenders.Count == 0,
            "出现了不是 Guid 的裸字母 ToString（double/decimal 的 \"N\"/\"G\" 会随文化换小数点，标记表按 Guid 放过它）：\n"
            + string.Join("\n", offenders));
    }

    private static readonly Regex BareStandard
        = new(@"([A-Za-z_$][\w\.()]*?)\.ToString\(""(N|G|F|P|C|E)""\)");

    // ────────── ③ UI 工程零数字格式串 ──────────

    /// <summary>
    /// UI 里<b>连"已锁不变文化"的格式串都不许留</b>：界面读数口径只许有一个主人（<c>NumberText</c>）。
    /// 否则同一个"2.5 倍"在缩放条上是一种取整、在卡片上是另一种，而两处都自称锁好了文化。
    /// </summary>
    [Fact]
    public void UiProjectHoldsNoNumberFormatLiteralsAtAll()
    {
        var offenders = FormatScanner.SourcesUnder("src/StarMark.UI")
            .SelectMany(f => f.Literals
                .Where(FormatScanner.IsNumberFormatLiteral)
                .Select(lit => $"{f.Path}: \"{lit}\""))
            .ToList();

        Assert.True(offenders.Count == 0,
            "UI 层又自己拿数字格式串了（该走 NumberText）：\n" + string.Join("\n", offenders));
    }

    /// <summary>把判据路径换成 <c>SourcesUnder</c> 的"跳过"尾巴（它报仓根相对路径，按文件名筛最稳）。</summary>
    private static string Skip(string judgeFile) => Path.GetFileName(judgeFile);

    // ────────── ④ 字节梯子只许一份 ──────────

    /// <summary>
    /// 本仓曾有<b>四份</b>"字节 → 人话"的梯子（<c>FileSizeText</c>／<c>ClipAssets</c>／<c>DiagnosticsService</c>／
    /// <c>SettingsPage.Engine</c>），档位一样但一份锁文化一份没锁 ⇒ 同一个体积在两处不同形。
    /// 判"梯子"而不是判"出现过 MB 这个词"：像 <c>"超过 20MB 上限"</c> 这种文案是引用上限，不是第二份换算
    /// ⇒ 规则是<b>同一文件里出现两枚以上单位字面量</b>（那只有在做换算阶梯时才会发生）。
    /// </summary>
    [Fact]
    public void ByteLadderExistsNowhereButTheJudge()
    {
        var offenders = FormatScanner.SourcesUnder("src")
            .Where(f => !f.Path.EndsWith(Path.GetFileName(ByteJudgeFile), StringComparison.Ordinal))
            .Select(f => (f.Path, Units: UnitLiterals.Count(u => f.Literals.Any(lit => lit.Contains(u, StringComparison.Ordinal)))))
            .Where(x => x.Units >= 2)
            .Select(x => $"{x.Path}: 同时出现 {x.Units} 种单位字面量")
            .ToList();

        Assert.True(offenders.Count == 0,
            "又长出一份字节换算梯子（各处取整/进制一旦不同，同一个文件在卡片与设置页就会写成两个体积）——请调 FileSizeText.Human：\n"
            + string.Join("\n", offenders));
    }

    // ────────── ⑤ 转发不许变成第二个决策点 ──────────

    [Fact]
    public void DescribeBytesIsAPureForwardNotASecondLadder()
    {
        var raw = ReadRepoFile("src/StarMark.Abstractions/Clipboard/ClipAssets.cs");
        // 锚点钉**完整签名**，不钉前缀：`public static string DescribeBytes` 是 `DescribeBytesMoved(` 的前缀，
        // 那颗转发哪天改名，守门会"命中一处、读到对的那段"而完全不红——等于把"搬家要跟上"这件事交给运气（变异 RZ12b 教的）。
        var body = MethodBody(raw, "public static string DescribeBytes(long bytes)");

        Assert.Contains("FileSizeText.Human", body, StringComparison.Ordinal);
        Assert.DoesNotContain("1024", body, StringComparison.Ordinal);   // 自己换算＝梯子又长回来了
        Assert.DoesNotContain("switch", body, StringComparison.Ordinal);
    }

    // ────────── ⑥ 判据不空壳 + 扫描器自检（正反两面） ──────────

    /// <summary>判据被掏空时，①②会因为"到处都没有格式串"而假绿——所以先证明判据自己拿着东西。</summary>
    [Fact]
    public void TheJudgeItselfHoldsTheFormats()
    {
        var (code, literals, _) = FormatScanner.Scan(ReadRepoFile(NumberJudgeFile));
        var held = literals.Count(FormatScanner.IsNumberFormatLiteral);
        Assert.True(held >= 6, $"NumberText 里只剩 {held} 个数字格式串——它不该是空壳");
        Assert.Contains("InvariantCulture", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>正面自检</b>：每种"会写分隔符"的数字格式家族都得被认出来。
    /// <para>字符串<b>写法</b>（普通／内插／孔里嵌套／逐字／原始串）那一层不在这里重证——
    /// 扫描器现在只有一份（<see cref="FormatScanner"/>），那五种写法由 RY 的
    /// <c>DisplayDateFormatGateTests.ScannerSeesThroughEveryStringSyntax</c> 统一钉住；
    /// 这里只钉"格式家族"这一层：<c>N0/F1/P0</c> 标准说明符、带小数点的自定义模式、带千分位逗号的模式、
    /// 逐字串里转义过的小数点。少一族，就等于那一族的违规可以大摇大摆走进源码。</para>
    /// </summary>
    [Theory]
    [InlineData(@"var s = value.ToString(""N0"", culture);", "标准说明符：千分位")]
    [InlineData(@"var s = value.ToString(""F1"", culture);", "标准说明符：定点小数")]
    [InlineData(@"var s = value.ToString(""G15"", culture);", "标准说明符：两位位数（只认一位就整族放过 G12/G15）")]
    [InlineData(@"var s = $""{ratio:P0}"";", "标准说明符：百分号（走内插孔）")]
    [InlineData(@"var s = $""约 {due.ToString(""0.#"")} 秒"";", "自定义模式（孔里再套串：RY 的 RI4/RI5 就是这么漏的）")]
    [InlineData(@"var s = value.ToString(""#,##0.00"");", "模式里同时有千分位与小数点")]
    [InlineData(@"var s = value.ToString(@""0\.##"");", "逐字串里转义过的小数点")]
    public void ScannerSeesEveryNumberFormatFamily(string snippet, string because)
    {
        var literals = FormatScanner.Scan(snippet).Literals;
        Assert.True(
            literals.Any(FormatScanner.IsNumberFormatLiteral)
            || literals.SelectMany(FormatScanner.HoleFormats).Any(h => FormatScanner.IsNumberFormatLiteral(h.Format)),
            $"{because}：扫描器/识别表看不见这条写法里的数字格式 → {snippet}");
    }

    /// <summary>
    /// <b>反面自检</b>：实测逐文化不变的写法<b>不该</b>被判据命中——否则闸门会天天红在错的东西上。
    /// 这一条是把"为什么不收补零"从注释里的说法变成会红的断言。
    /// <para><b>两层都要断言</b>（变异 RZ10 教的）：只看"整条字面量"的话，<c>"{minute:00}"</c> 这一串
    /// 本来就因为带着花括号而匹配不上格式表，于是判据被放宽成"任何数字模式都算"它也照样绿——
    /// 必须再把<b>孔里抽出来那段格式</b>单独问一次，反面自检才真的咬得住"补零不收"这条口径。</para>
    /// </summary>
    [Theory]
    [InlineData(@"var s = $""{minute:00}"";")]
    [InlineData(@"var s = $""{mb:0}"";")]
    [InlineData(@"var s = value.ToString(""N"");")]     // Guid 的 "N" 是十六进制形状，与文化无关（实测）
    [InlineData(@"var s = $""{count}"";")]               // 纯整数内插
    public void SeparatorFreeShapesAreNotFlagged(string snippet)
    {
        var literals = FormatScanner.Scan(snippet).Literals;
        Assert.DoesNotContain(literals, FormatScanner.IsNumberFormatLiteral);
        Assert.DoesNotContain(literals.SelectMany(FormatScanner.HoleFormats),
            h => FormatScanner.IsNumberFormatLiteral(h.Format));
    }
}
