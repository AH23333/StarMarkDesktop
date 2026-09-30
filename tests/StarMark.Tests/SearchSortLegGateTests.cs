#nullable enable
using System;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 搜索两条腿的<b>形状</b>闸门（P-33，批次 SO）。
/// <para>
/// 行为测（<see cref="SearchSortTruncationTests"/>）量的是真库里的结果集与顺序；这里量的是三件
/// 行为测看不见的事：① 谁把截断又搬回 CTE 里（写死一份无条件 LIMIT，两腿一起中招）；
/// ② 谁把判据在 Data 层再判一遍（UI 认为"这是默认序"而 SQL 认为不是＝又一份分岔）；
/// ③ 谁在非相关度腿上重新引用 <c>rank</c>——那会让 SQLite 平面化后的语句直接抛
/// "unable to use function bm25 in the requested context"（本批第一遍就是这样响的，见 §二百零四）。
/// </para>
/// </summary>
public sealed class SearchSortLegGateTests
{
    private const string Search = "src/StarMark.Data/ItemRepository.Search.cs";
    private const string Judge = "src/StarMark.Abstractions/SearchSortPolicy.cs";

    [Fact]
    public void CteNeverCarriesAnUnconditionalLimit()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Search));

        // CTE 那一段（从 WITH 到它的右括号）里只许出现"腿相关的尾巴"这个变量，不许出现写死的 LIMIT。
        var cte = SourceGate.Between(code, "WITH fts_hits AS (", "\n            )");
        Assert.Contains("innerTail", cte, StringComparison.Ordinal);
        Assert.DoesNotContain("LIMIT", cte, StringComparison.Ordinal);

        // 整段 SQL 里 LIMIT 只该出现在两条腿上，各一次。
        Assert.Equal(2, SourceGate.Count(code, "LIMIT @limit"));
    }

    [Fact]
    public void TheTwoTailsAreMutuallyExclusiveLegs()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Search));

        // 两条腿各自"要么在里面截、要么在外面截"，永不同时截（双截＝非默认排序少返回），
        // 也永不都不截（＝相关度腿丢掉 P-49 换来的短路）。
        Assert.Contains("var innerTail = relevanceFirst ?", code, StringComparison.Ordinal);
        Assert.Contains("var outerTail = relevanceFirst ? string.Empty", code, StringComparison.Ordinal);
        Assert.Contains("var cteHead = relevanceFirst", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RankIsOnlyEverAskedOnTheRelevanceLeg()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Search));

        // bm25() 与 rank 各只有一处出处，且都挂在相关度那一臂上：非相关度腿引用 rank 就会撞上
        // CTE 平面化后的 "unable to use function bm25 in the requested context"。
        Assert.Equal(1, SourceGate.Count(code, "bm25(items_fts)"));
        Assert.Equal(1, SourceGate.Count(code, "MIN(f.rank)"));
        Assert.Contains("var tieBreak = relevanceFirst ? \"MIN(f.rank)\" : \"i.id\";", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DataNeverReJudgesWhatCountsAsRelevance()
    {
        // 判据只有一颗，住在 Abstractions（Data 引用得到、UI 也引用得到）；Data 里再出现
        // "relevance" 字面＝同一个判断写两处，早晚分岔（#188/#193 那一族）。
        var dataFiles = SourceGate.ReadRepoUnder("src/StarMark.Data");
        var offenders = dataFiles
            .Where(f => SourceGate.Code(f.Text).Contains("\"relevance\"", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToList();
        Assert.Empty(offenders);

        var judge = SourceGate.Code(SourceGate.ReadRepoFile(Judge));
        Assert.Equal(1, SourceGate.Count(judge, "bool IsRelevanceFirst("));
        Assert.Equal(1, dataFiles.Sum(f => SourceGate.Count(SourceGate.Code(f.Text), "SearchSortPolicy.IsRelevanceFirst(")));
    }
}
