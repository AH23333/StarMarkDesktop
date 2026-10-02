#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 桌面自己的数字，不许把正当性记在"另一库量过"上而不给出处（批次 UL，结案 P-116）。
/// <para>
/// 起因是 <c>AiClassify.cs</c> 里两处被抓到的假出处：<c>MaxItemsPerBatch = 80</c> 写着"扩展侧实测过的拐点之前"、
/// <c>Sanitize(max = 3)</c> 写着"对齐扩展侧实测"。而扩展用的是 <b>50</b>（<c>classify.ts:50</c>），
/// 80 在它<b>后面</b>；两库里也<b>都没有</b>那次实测——查下来只有一句"漏抄率上升"的定性判断。
/// 这不是措辞不严谨：<b>注释是下一手敢不敢去量这件事的依据</b>，写上"另一库量过"，下一手就不会再量，
/// 一个没量过的数就此变成不可动摇的"事实"（与 R7 那条"文档指针要回写"同一族：话留着，证人没有）。
/// </para>
/// <para>
/// 判据形状刻意很窄——它管的是<b>"把实测归给另一库"这句话有没有出处</b>，不是"注释里能不能出现实测"：
/// 只讲本机的实测（<c>trigram</c> 那条、<c>GDI 实测</c> 那一族）一概不管，放宽到那种范围就会逼人删掉真证据。
/// 第一版我按<b>注释块</b>判，<c>CjkTokenizer.cs</c> 当场假红：那段 remarks 里既讲了本机 <c>trigram</c> 实测、
/// 又另起一段提到扩展的 <c>indexer.ts</c>——两件事不相干，却被算成一次无出处声称。
/// </para>
/// </summary>
public sealed class CrossProjectEvidenceGateTests
{
    /// <summary>"把实测／踩坑／拐点／量过归给扩展／另一库"＝同一行内两者相隔不超过 28 字。</summary>
    private static readonly Regex Claim = new(
        @"(?:扩展|另一库)[\s\S]{0,28}?(?:实测|踩坑|拐点|量过)|(?:实测|踩坑|拐点|量过)[\s\S]{0,28}?(?:扩展|另一库)",
        RegexOptions.Compiled);

    /// <summary>
    /// 算出处的四种形状：另一库的 <c>x.ts:行号</c>、账本 <c>P-数字</c>、设计文档 <c>§</c>、本仓批次号 <c>批次 XX</c>。
    /// <para>⚠ 最后一项必须带批次号：只写"含'批次'两字就算出处"时，<c>MaxItemsPerBatch</c> 那句假依据里的
    /// "<b>批次边界</b>重新交还给…"自己把闸门骗过去了（一次<b>假放行</b>——比假红更难发现，因为它看起来像通过了校验）。</para>
    /// </summary>
    private static readonly Regex Attribution = new(
        @"\.ts:\d+|P-\d+|§|批次\s*[A-Z]{1,3}[0-9\-]?",
        RegexOptions.Compiled);

    /// <summary>普查规模下限：文件数与"提到另一库的注释行数"。低于此说明扫错了地方，此时"零违例"是假的。</summary>
    private const int MinSourceFiles = 300;
    private const int MinMentions = 100;

    /// <summary>至少要有几处跨库声称被扫到（HEAD 实测 4 处）。地板比实测低一格：合法地补一句出处不会把它踩红。</summary>
    private const int MinClaimsFound = 3;

    /// <summary>注释行＝以 <c>//</c> 开头的行（含 <c>///</c>）。产品代码里"扩展"出现在字符串字面量里不算声称。</summary>
    private static IEnumerable<(string Path, int Line, string Text)> CommentLines()
        => SourceGate.ReadRepoUnder("src").SelectMany(f =>
            f.Text.Replace("\r\n", "\n").Split('\n')
                .Select((raw, i) => (f.RelativePath, Line: i + 1, Text: raw.Trim()))
                .Where(x => x.Text.StartsWith("//", StringComparison.Ordinal)));

    /// <summary>扫描器要有牙：两条真实的假出处必须判红，而三种合法写法与"只讲本机实测"必须放行。</summary>
    [Fact]
    public void TheScannerCatchesTheRealFalseBasis()
    {
        // 这两句是从 HEAD 上原样抄来的（就是它们把 80 与 3 挂到了一次不存在的实测上），不是稻草人
        var unattributed = new[]
        {
            "/// <b>上限取 3（对齐扩展侧实测）</b>：一条给四个已经够分类用了",
            "/// 瘦身后 80 条 ≈ 3.5K，批次边界重新交还给\"模型抄录可靠度\"来定（再大漏抄率上升，扩展侧实测过的拐点之前）。",
        };
        Assert.All(unattributed, line =>
        {
            Assert.Matches(Claim, line);                  // 认得出"把实测归给另一库"这句话
            Assert.DoesNotMatch(Attribution, line);       // 而它确实一个出处都没有
        });

        // 两个方向都要认得：先说"扩展"再说"实测"，以及反过来先说"实测"再指向扩展
        Assert.Matches(Claim, "/// 上限取 80，在扩展侧实测过的拐点之前");
        Assert.Matches(Claim, "/// 实测过的拐点在扩展那边之前，所以取 80");

        // 四种出处形状各自成立
        Assert.Matches(Attribution, "（扩展 normalize.ts:42 那条注释写着\"用户实测踩坑\"的原件）");
        Assert.Matches(Attribution, "/// <b>80 这个数没有实测依据</b>（P-116；原先那句\"扩展侧实测过的拐点\"…）");
        Assert.Matches(Attribution, "/// 本机实测见报告 §二百一十二");
        Assert.Matches(Attribution, "原先那句假出处，批次 UL 删掉——");

        // 反例钉在这：光有"批次"两个字不算出处（当年就是被"批次边界"溜过去的）
        Assert.DoesNotMatch(Attribution, "批次边界重新交还给模型抄录可靠度");

        // 只讲本机实测、没扯另一库的，一律不归这条管
        Assert.DoesNotMatch(Claim, "/// <para><b>为什么不用 trigram：</b>实测 <c>tokenize='trigram'</c> 仅对 ≥3 字的");
        Assert.DoesNotMatch(Claim, "/// 文字按 <b>GDI 实测</b>宽高——这块框除了当刷新范围");
    }

    /// <summary>主判据：<c>src/</c> 的注释里不许出现"把实测归给另一库、却不给出处"的行。</summary>
    [Fact]
    public void NoUnattributedClaimThatAnotherProjectMeasuredSomething()
    {
        var bad = CommentLines()
            .Where(x => Claim.IsMatch(x.Text) && !Attribution.IsMatch(x.Text))
            .ToList();

        Assert.True(bad.Count == 0,
            "注释把某个数字的依据挂在\"扩展／另一库实测过\"上，却没写那次量的出处（P-116 的 80 就是这么来的）：\n" +
            string.Join("\n", bad.Take(12).Select(b => $"  {b.Path}:{b.Line}  {b.Text}")));
    }

    /// <summary>
    /// 上面那条不许靠"什么都没扫到"通过：普查范围、词汇命中都要到位，
    /// 而且<strong>已知那几处带出处的声称必须真被扫到</strong>——扫不到就说明是路径或过滤错了，不是"变干净了"。
    /// </summary>
    [Fact]
    public void TheRulerIsActuallyLookingAtTheCode()
    {
        var lines = CommentLines().ToList();

        var files = lines.Select(x => x.Path).Distinct(StringComparer.Ordinal).Count();
        Assert.True(files >= MinSourceFiles, $"src 下只扫到 {files} 颗含注释的源文件（下限 {MinSourceFiles}）⇒ 先怀疑普查路径，别改下限");

        var mentions = lines.Count(x => x.Text.Contains("扩展", StringComparison.Ordinal)
                                        || x.Text.Contains("另一库", StringComparison.Ordinal));
        Assert.True(mentions >= MinMentions, $"注释里提到另一库的只有 {mentions} 行（下限 {MinMentions}）⇒ 扫描没吃到手写源码");

        var claims = lines.Count(x => Claim.IsMatch(x.Text));
        Assert.True(claims >= MinClaimsFound,
            $"全 src 只认出 {claims} 行跨库实测声称（HEAD 上有 4 行）⇒ 声称正则退化了，主判据那条\"零违例\"此刻不作数");
    }
}
