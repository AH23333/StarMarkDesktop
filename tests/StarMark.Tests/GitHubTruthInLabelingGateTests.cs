#nullable enable
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// star 这一族"<b>说出去的话</b>"必须与实现对得上（批次 VP，账本 P-6／P-57／P-58）。
/// <para>
/// 两处都不是数据面缺陷，而是"数据面已经这样了，话却说得比实现满"：
/// ① 排序档那颗写着「最近 Star」，实际排的是<b>仓库最后一次 push</b>（<c>GitHubSource.MapToItem</c>
///   把 <c>StarredAt</c> 填成 <c>repo.PushedAt</c>）——老仓库被新近 Star 会排到很后面，那句话对它是假话；
/// ② 一轮被 5000 条预算掐断时，旧写法既不说"只导入了前 5000 条"，又把页 1 带回的 ETag 留在原地
///   ⇒ 下一轮 304 短路，第 5001 条<b>永远拉不到</b>（P-57 那个漏臂；P-58 要的正是"修数据面必须同时改文案"）。
/// </para>
/// <para>
/// 判据一律<b>钉形状不钉措辞的具体字</b>的地方就钉形状：标签里必须出现真正的排序键字样、封顶那一臂必须
/// 同时做"复位检查点 + 说一声"两件事、上限那个数全文件只许有一处出处。
/// </para>
/// </summary>
public sealed class GitHubTruthInLabelingGateTests
{
    private const string MainWindowXaml = "src/StarMark.UI/MainWindow.xaml";
    private const string Client = "src/StarMark.Integrations/GitHub/GitHubClient.cs";
    private const string Source = "src/StarMark.Integrations/GitHub/GitHubSource.cs";

    private static string Markup()
        => Regex.Replace(ReadRepoFile(MainWindowXaml), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>
    /// 「那一档叫什么」＝<b>它实际按什么排</b>。这条链有三环，缺一环那句标签就成了假话：
    /// 档名 <c>starred</c> → SQL 按 <c>$.StarredAt</c> 排 → 而 <c>StarredAt</c> 填的是 <c>repo.PushedAt</c>。
    /// <para>三环都当<b>前提</b>钉住：<b>哪天真去取 starred_at（P-6 的选项 A），这一格会红——那是故意的</b>。
    /// 数据变准确的同一天，才轮得到把标签改回"最近 Star"；只改标签不改数据，才是这条闸门要拦的方向。</para>
    /// </summary>
    [Fact]
    public void TheStarSortTierIsNamedAfterWhatItActuallySortsBy()
    {
        var tier = Regex.Matches(Markup(), @"<ComboBoxItem\b[^>]*>", RegexOptions.Singleline)
            .Cast<Match>().Single(m => m.Value.Contains("Tag=\"starred\""));

        Assert.Contains("推送", tier.Value, System.StringComparison.Ordinal);   // 说出真正的排序键
        Assert.DoesNotContain("Content=\"最近 Star\"", tier.Value, System.StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip", tier.Value, System.StringComparison.Ordinal);   // 差异就地解释，不靠人记

        // ② 那一档排的确实是 StarredAt（浏览与搜索两条 SQL 都得是，否则"档名"与"排序键"在两条路径上分岔）
        foreach (var repo in new[] { "src/StarMark.Data/ItemRepository.Search.cs", "src/StarMark.Data/ItemRepository.Items.cs" })
        {
            var sql = Code(ReadRepoFile(repo));
            Assert.Equal(1, Count(sql, "\"starred\" =>"));
            Assert.Equal(1, Count(sql, "'$.StarredAt'"));
        }

        // ③ 而 StarredAt 今天装的是 push 时间（改这条代码的人请连同上面三句一起改，别只改标签）
        var mapped = Code(ReadRepoFile(Source));
        Assert.Contains("var starredAt = ParseUnixTime(repo.PushedAt) ?? now;", mapped, System.StringComparison.Ordinal);
        Assert.Contains("StarredAt = starredAt,", mapped, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// 封顶那一臂必须<b>两件事一起做</b>：复位 ETag 检查点（不然下一轮 304 永久短路）＋ 说一声
    /// （不报成功也不报失败，就报"到上限了、其余没导入"）。少任何一件，用户看到的都是一句不成立的话。
    /// <para>判据<b>不钉措辞也不钉常量名</b>（#33 那条纪律）：那句提醒要"带一个数"就行（措辞随人改），
    /// 上限从哪个标识符读来也不问——把 <c>MaxStarredItems</c> 改成别的名字必须原样放过。</para>
    /// </summary>
    [Fact]
    public void TheItemBudgetCapResetsTheCheckpoint_AndSaysSoOutLoud()
    {
        var body = MethodBody(Code(ReadRepoFile(Client)),
            "public async Task<IReadOnlyList<GitHubStarApiModel>> GetAllStarredAsync");

        // 取消/失败那一臂（catch）＋ 封顶这一臂：两处都得把检查点按回去
        Assert.Equal(2, Count(body, "CachedETag = etagBefore;"));
        Assert.Contains("truncated = true", body, System.StringComparison.Ordinal);
        // "说一声"＝那句提醒必须带上被丢掉的**数量**（内插一个变量），而不是"到上限了"四个字
        Assert.Matches(@"StarLog\.Warn\(\$\s*""[^""\n]*\{\w+", body);
        // 而那个数不许在这一臂里再写一遍字面量（上限全文件只许有一处出处，见下一格）
        Assert.DoesNotContain("5000", body, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// 上限那个数<b>全文件只许有一处出处</b>：写两遍 5000，改了常量没改日志，
    /// 就是"报出来的上限与真正生效的上限不一致"。
    /// <para>钉的是<b>形状</b>（有一处 <c>const int 某名 = 5000;</c>）而不是那颗的名字：
    /// 改名是合理的重构，把名字钉死只会让下一次改名变成一次假事故。</para>
    /// <para>反自证：这一格靠 <see cref="Count"/> 数代码（注释已被 <c>Code()</c> 抹掉），
    /// 注释里那些"5000"是给人读的叙述，不算第二处出处。</para>
    /// </summary>
    [Fact]
    public void TheItemBudgetNumberHasExactlyOneSource()
    {
        var code = Code(ReadRepoFile(Client));
        Assert.Matches(@"const\s+int\s+\w+\s*=\s*5000\s*;", code);
        Assert.Equal(1, Count(code, "5000"));
    }
}
