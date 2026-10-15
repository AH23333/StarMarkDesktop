#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-40-4（✅P-198 落地）：<b>「订阅与摘掉必须成对」这条规矩从今天起有一把认得形状的尺</b>。
/// <para>
/// <c>Locale.Changed</c> 是进程级静态事件，而本仓的宿主窗/页/组件都是可反复创建与销毁的对象
/// ⇒ 挂了不摘就是每进一次多一份订阅（坑表 #386 那条规矩）。今天这一族一共 <b>27 颗点＝挂 12／摘 15</b>，
/// 分在 <b>10 档</b>里（现读 <c>bin/WI404/pair_404.txt</c>，2026-10-11；⛔ 凭记忆，#159/#160）。
/// </para>
/// <para>
/// <b>为什么这一批才装上尺</b>：旧普查按"这一行里含 <c>+=</c> 就算挂"贴标签，而那族形状里<b>同一行有两种符号</b>
/// （<c>Unloaded += (_, _) =&gt; Locale.Changed -= X;</c>）⇒ 把<b>摘</b>读成<b>挂</b>，凭空造出"6 档只见挂不见摘"
/// 这份名单（坑表 <b>#455</b>＝P-198 登记时的读数被就地判掉）。这一把尺只认<b>紧邻的两个记号</b>：
/// <c>Locale.Changed</c> 之后紧跟 <c>+=</c>／<c>-=</c>，中间隔了别的 token 就不算。
/// </para>
/// <para>
/// 判据按<b>档</b>配对而不是按全局数量抵账：全局"挂 12 ≤ 摘 15"看着像没事，其实可以让甲档少摘一颗、
/// 乙档多摘一颗而照样成立（＝#156 那一族：登记颗数要被"逐档相加＝总数"担保）。
/// </para>
/// </summary>
public sealed class LocaleSubscriptionPairGateTests
{
    private const string LocTextCs = "src/StarMark.UI/Helpers/LocText.cs";

    /// <summary>只认紧邻记号（⛔ 拿"行里有没有加号"当判据，那正是坑表 #455 的成因）。</summary>
    private static readonly Regex Point = new(@"Locale\.Changed\s*([+-])=\s*([A-Za-z_][A-Za-z0-9_.]*)?", RegexOptions.Compiled);

    /// <summary>同一行里"外层事件 <c>+=</c> ＋ <c>Locale.Changed</c> 那颗符号"的两种形状（现读：单行挂 <b>3</b> 颗／单行摘 <b>4</b> 颗）。
    /// <para>⚠ 这两个数第一次是<b>从一份被 <c>head</c> 截断的 grep 输出</b>猜成"各 4 颗"的，当场被自己写的闸门红掉（#141/#207 那一族）。</para></summary>
    private static readonly Regex LambdaSub = new(@"(Loaded|Unloaded)\s*\+=[^;]*=>\s*Locale\.Changed\s*\+", RegexOptions.Compiled);

    private static readonly Regex LambdaUnsub = new(@"(Loaded|Unloaded)\s*\+=[^;]*=>\s*Locale\.Changed\s*-", RegexOptions.Compiled);

    /// <summary>
    /// 唯一一处<b>不配对</b>的订阅，按档名＋handler 名点名豁免（⛔ 把豁免住在注释里＝下一批又去猜，#201）。
    /// 豁免必须自带必要性断言：<see cref="TheExemptionIsStillNecessary"/> 会在它被补齐的那一天红。
    /// </summary>
    private static readonly Dictionary<string, string> UnpairedOwners = new()
    {
        [LocTextCs] = "ReapplyAll",
    };

    private readonly record struct Point3(string File, string Line, string Sign, string Handler);

    /// <summary>整仓扫一遍不便宜，而这一族每格都要用同一份点 ⇒ 扫一次共用（⛔ 各格自己再扫＝同一件事各写各的，#177）。</summary>
    private static readonly Lazy<List<Point3>> All = new(Points);

    /// <summary>全部点＝(档，那一行，符号，handler 名)。逐行读<b>抹掉注释后</b>的源码，注释里那句"⛔ 在这里订阅"不算一颗点。</summary>
    private static List<Point3> Points()
    {
        var found = new List<Point3>();
        var scanned = 0;
        foreach (var (rel, text) in SourceGate.ReadRepoUnder("src"))
        {
            scanned++;
            // 在整档文本上扫，⛔ 逐行扫：C# 允许 `Locale.Changed` 与 `+=` 之间换行，按行数就会把那颗点静默漏掉。
            // 行号与那一行的原文只为报"是哪一行"，判符号与否⛔ 依赖行边界。
            var code = SourceGate.Code(text).Replace("\r\n", "\n");
            foreach (Match m in Point.Matches(code))
            {
                var from = m.Index == 0 ? 0 : code.LastIndexOf('\n', m.Index - 1) + 1;
                var to = code.IndexOf('\n', m.Index);
                found.Add(new Point3(rel, code[from..(to < 0 ? code.Length : to)].Trim(),
                                      m.Groups[1].Value, m.Groups[2].Value));
            }
        }
        Assert.True(scanned >= 400, $"src 下只扫到 {scanned} 档 .cs——普查路径错了（守门失效比红测更危险）");
        return found;
    }

    /// <summary>①分母与颗数一起报：一把尺静默读成 0 比红更危险（#191/#201）。</summary>
    [Fact]
    public void TheCensusReportsItsOwnDenominator()
    {
        var pts = All.Value;
        // 36／16／20／13 档＝现读（⛔ 预测，名册 262 那一族：第一次只按"这一批加了第四组手臂"算成 30，被自己写的闸门当场否掉）。
        // 多出来的那 9 颗里有 6 颗是<b>上一笔提交</b>（片9c 第三刀）给活动流那两位宿主新挂的手臂——
        // 那一跑只跑了定向证人 688 格，这颗全局分母没进过滤器 ⇒ 登记为"邻族证人漏重锚"，不是这颗闸门读错（坑表那条同族）。
        Assert.Equal(36, pts.Count);
        Assert.Equal(16, pts.Count(p => p.Sign == "+"));
        Assert.Equal(20, pts.Count(p => p.Sign == "-"));
        Assert.Equal(13, pts.Select(p => p.File).Distinct().Count());
        Assert.All(pts, p => Assert.True(p.Sign is "+" or "-", "符号只可能是这两颗之一"));
    }

    /// <summary>②成对判据（按档）：每档摘的颗数⛔ 少于挂的颗数，且<b>挂多于摘的档必须恰好是豁免名单那一颗</b>。</summary>
    [Fact]
    public void EverySubscriberIsReleasedInTheSameFile()
    {
        var byFile = All.Value.GroupBy(p => p.File)
            .Select(g => (File: g.Key, Subs: g.Count(p => p.Sign == "+"), Uns: g.Count(p => p.Sign == "-")))
            .OrderBy(x => x.File, StringComparer.Ordinal).ToList();

        var offenders = byFile.Where(x => x.Uns < x.Subs).Select(x => x.File).ToList();
        Assert.Equal(UnpairedOwners.Keys.OrderBy(k => k, StringComparer.Ordinal), offenders);
        Assert.DoesNotContain(byFile, x => x.File != LocTextCs && x.Uns < x.Subs);
    }

    /// <summary>③按 handler 名配对（⛔ 拿全局数量抵账）：一颗名字在甲档挂了，就在甲档摘——跨档同名各归各的。</summary>
    [Fact]
    public void EachNamedHandlerIsReleasedWhereItIsSubscribed()
    {
        foreach (var p in All.Value.Where(p => p.Sign == "+" && p.Handler.Length > 0))
        {
            if (UnpairedOwners.TryGetValue(p.File, out var exempt) && exempt == p.Handler) continue;
            var sameFileSameHandler = All.Value.Where(q => q.File == p.File && q.Handler == p.Handler && q.Sign == "-").ToList();
            Assert.True(sameFileSameHandler.Count >= 1,
                $"{p.File} 里挂了「{p.Handler}」而这一档⛔ 一处摘它（挂的那一行：{p.Line}）");
        }
    }

    /// <summary>④豁免的必要性：那颗进程级主人今天确实只挂不摘；谁把它补齐了，这一格就红（把豁免名单拉回去）。</summary>
    [Fact]
    public void TheExemptionIsStillNecessary()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(LocTextCs));
        Assert.Single(Regex.Matches(code, @"Locale\.Changed\s*\+="));
        Assert.Empty(Regex.Matches(code, @"Locale\.Changed\s*-="));
        // 静态构造器是唯一一处挂点：挂在别处（比如某颗实例方法里）＝这一条豁免当场作废
        var ctor = SourceGate.MethodBody(code, "static LocText()");
        Assert.Contains("Locale.Changed += ReapplyAll;", ctor);
    }

    /// <summary>
    /// ⑤<b>这一批的主角</b>：同一行里两种符号那 <b>4</b> 颗必须读成<b>摘</b>，而"挂在 <c>Loaded</c> 那一拍里、与外层 <c>+=</c> 同一行"那 <b>3</b> 颗必须读成<b>挂</b>。
    /// <para>旧普查就是把前者读成挂，于是"6 档只见挂不见摘"这份名单凭空出现（坑表 <b>#455</b>）——
    /// 这一格不给它第二次机会：两种形状各按现读的颗数钉住（<c>bin/WI404/pair_404.txt</c>），且<b>没有一行</b>既算挂又算摘。
    /// ⚠ 第三颗档（<c>WeatherWidget.xaml.cs</c>）是<b>另一形状</b>：<c>Loaded += (_, _) =&gt; { … }</c> 的<b>块体</b>里单行挂，
    /// 摘那颗才是单行 lambda ⇒ 它落在 <see cref="WidgetsThatSubscribeAtLoadedReleaseAtUnloaded"/> 那一格，⛔ 混进这一格的颗数。</para>
    /// </summary>
    [Fact]
    public void TheSameLineCarryingTwoSymbolsCountsOnceAndOnItsOwnSide()
    {
        var pts = All.Value;
        var subLines = pts.Where(p => LambdaSub.IsMatch(p.Line)).ToList();
        var unsubLines = pts.Where(p => LambdaUnsub.IsMatch(p.Line)).ToList();
        Assert.Equal(3, subLines.Count);
        Assert.Equal(4, unsubLines.Count);
        Assert.All(subLines, p => Assert.Equal("+", p.Sign));
        Assert.All(unsubLines, p => Assert.Equal("-", p.Sign));
        // 一行只归一边：同档同行的两颗形状⛔ 混（把"摘"当"挂"用的那次就是这么错的）
        Assert.Empty(subLines.Intersect(unsubLines));
    }

    /// <summary>⑥那些在 <c>Loaded</c> 那一拍才挂上的宿主（<b>两种形状都算</b>），必须在同一档里给出 <c>Unloaded</c> 那颗。</summary>
    [Fact]
    public void WidgetsThatSubscribeAtLoadedReleaseAtUnloaded()
    {
        var pts = All.Value;
        var files = pts.Where(p => LambdaSub.IsMatch(p.Line) || LambdaUnsub.IsMatch(p.Line))
            .Select(p => p.File).Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal(4, files.Count);
        foreach (var file in files)
        {
            var d = pts.Where(p => p.File == file).ToList();
            Assert.True(d.Count(p => p.Sign == "+") >= 1, $"{file} 该有一颗挂在 Loaded 那一拍里的点，现在一颗都没有（这一格该跟着改形状）");
            Assert.True(d.Count(p => p.Sign == "-") >= d.Count(p => p.Sign == "+"),
                $"{file} 在 Loaded 那一拍挂了，却没在 Unloaded 里摘够（挂 {d.Count(p => p.Sign == "+")}／摘 {d.Count(p => p.Sign == "-")}＝每次进这扇窗多一份订阅）");
        }
    }
}
