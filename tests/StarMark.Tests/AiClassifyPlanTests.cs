#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Ai;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 预览与确认阶段的方案运算。<b>这一段决定"用户点头之后库里会变成什么"</b>，
/// 所以分组怎么切、一组被否掉时掉多少，都要能指名断言。
/// </summary>
public sealed class AiClassifyPlanTests
{
    private static TagProposal P(long id, params string[] tags) => new(id, tags);

    [Fact]
    public void AnEmptyPlanCarriesNoGroups()
    {
        var plan = new ClassifyPlan(Array.Empty<TagProposal>(), DateTimeOffset.UtcNow);
        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Groups());
        Assert.True(plan.WithoutItem(1).IsEmpty);     // 空方案上删东西仍然是空，但不能抛
    }

    [Fact]
    public void TagOrderDoesNotSplitAGroup()
    {
        var plan = new ClassifyPlan(new[] { P(1, "前端", "工具"), P(2, "工具", "前端") }, DateTimeOffset.UtcNow);

        var group = Assert.Single(plan.Groups());
        Assert.Equal(new long[] { 1, 2 }, group.Ids);
    }

    [Fact]
    public void OnlyCaseDiffersMeansOneGroup()
    {
        var plan = new ClassifyPlan(new[] { P(1, "Tools"), P(2, "tools") }, DateTimeOffset.UtcNow);

        // 分组的键大小写不敏感：模型一会儿给 Tools 一会儿给 tools 时不该裂成两组让用户各点一次
        Assert.Equal(2, Assert.Single(plan.Groups()).Ids.Count);
    }

    [Fact]
    public void ChineseAndLatinSpellingsStaySeparateGroups()
    {
        var plan = new ClassifyPlan(new[] { P(1, "前端"), P(2, "FrontEnd") }, DateTimeOffset.UtcNow);

        // "前端"与 "FrontEnd" 是两个词，不是同一个词的大小写变体——合成一组就是把决定权抢过来
        var groups = plan.Groups();
        Assert.Equal(new[] { "frontend", "前端" }, groups.Select(group => group.Key).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void GroupsAreOrderedBySizeWithADeterministicTieBreak()
    {
        var plan = new ClassifyPlan(new[]
        {
            P(1, "工具"), P(2, "工具"),
            P(3, "读书"), P(4, "读书"),
            P(5, "前端"),
        }, DateTimeOffset.UtcNow);

        // 条数多的在前；平局按 key 的码点定序——同一份方案两次打开显示顺序必须一样，
        // 否则用户会以为方案变了。Assert.Collection 同时钉住"三组、这个次序、各几条"。
        Assert.Collection(plan.Groups(),
            first => { Assert.Equal("工具", first.Tags.Single()); Assert.Equal(new long[] { 1, 2 }, first.Ids); },
            second => { Assert.Equal("读书", second.Tags.Single()); Assert.Equal(new long[] { 3, 4 }, second.Ids); },
            third => { Assert.Equal("前端", third.Tags.Single()); Assert.Equal(new long[] { 5 }, third.Ids); });
    }

    [Fact]
    public void RejectingOneGroupRemovesExactlyItsItems()
    {
        var plan = new ClassifyPlan(new[]
        {
            P(1, "工具"), P(2, "工具"), P(3, "读书"),
        }, DateTimeOffset.UtcNow);
        var unwanted = plan.Groups().Single(group => group.Tags.Contains("工具"));

        var left = plan.WithoutGroup(unwanted);

        Assert.Equal(new long[] { 3 }, left.Proposals.Select(proposal => proposal.Id));
        Assert.Equal("读书", Assert.Single(left.Groups()).Tags.Single());
    }

    [Fact]
    public void RejectingOneItemLeavesItsGroupmatesAlone()
    {
        var plan = new ClassifyPlan(new[] { P(1, "工具"), P(2, "工具") }, DateTimeOffset.UtcNow);

        var left = plan.WithoutItem(1);

        Assert.Equal(new long[] { 2 }, left.Proposals.Select(proposal => proposal.Id));
    }

    [Fact]
    public void AlreadyTaggedWordsAreNotProposedAgain()
    {
        var plan = new ClassifyPlan(new[] { P(1, "工具", "前端"), P(2, "工具") }, DateTimeOffset.UtcNow);

        var trimmed = plan.WithoutAlreadyTagged(id => id == 1 ? new[] { "前端" } : new[] { "工具" });

        // 第 1 条只剩"工具"；第 2 条要加的正是它已有的，整条撤掉
        Assert.Equal(new long[] { 1 }, trimmed.Proposals.Select(proposal => proposal.Id));
        Assert.Equal("工具", trimmed.Proposals[0].Tags.Single());
    }

    [Fact]
    public void AllTagsAreDistinctIgnoringCase()
    {
        var plan = new ClassifyPlan(new[] { P(1, "前端", "工具"), P(2, "前端") }, DateTimeOffset.UtcNow);
        Assert.Equal(new[] { "前端", "工具" }, plan.AllTags());
    }
}
