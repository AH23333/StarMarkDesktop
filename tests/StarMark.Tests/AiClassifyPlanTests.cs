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
    // ────────── 不成类的标签（批次 QA-1：用户要消灭的就是"一个标签只有一条"）──────────────────

    /// <summary>整个方案里只挂上一条条目的标签不算分类：砍掉，连同因此一个标签都不剩的那条。
    /// <b>不砍的话界面上就是几百个各挂一条的标签</b>——用户要的"分类"变成另一种脏。</summary>
    [Fact]
    public void ATagOnASingleItemIsNotAClassification()
    {
        var plan = new ClassifyPlan(new[]
        {
            P(1, "前端", "自研框架X"),
            P(2, "前端"),
            P(3, "只此一条的专有词"),
        }, DateTimeOffset.UtcNow);

        var pruned = plan.WithoutSingletonTags();

        Assert.Equal(new long[] { 1, 2 }, pruned.Plan.Proposals.Select(proposal => proposal.Id).ToArray());
        Assert.Equal("前端", pruned.Plan.Proposals[0].Tags.Single());     // 只砍不够格的那个，不是整条丢掉
        Assert.Equal(2, pruned.DroppedTags.Count);
        Assert.Contains("自研框架X", pruned.DroppedTags);
        Assert.DoesNotContain("前端", pruned.DroppedTags);
        Assert.Equal(1, pruned.DroppedItems);                             // 第 3 条标签全被砍 ⇒ 整条不提
    }

    /// <summary>计数按<b>整个方案</b>而不是按批：同一个词在两批里各出现一次，合起来两条，够格留下。
    /// 按批砍会把真分类砍掉，而且"哪批算一条"这种事在界面上完全看不出来。</summary>
    [Fact]
    public void SingletonCountingIsPerPlanNotPerBatch()
    {
        var pruned = new ClassifyPlan(new[] { P(1, "前端"), P(2, "前端") }, DateTimeOffset.UtcNow)
            .WithoutSingletonTags();
        Assert.Equal(2, pruned.Plan.ItemCount);
        Assert.Empty(pruned.DroppedTags);
        Assert.Equal(0, pruned.DroppedItems);
    }

    /// <summary>大小写不同的同一个词算一个标签的两次，否则 "FrontEnd" + "frontend" 各挂一条会双双被砍。</summary>
    [Fact]
    public void CaseVariantsOfOneTagAreCountedTogether()
        => Assert.False(new ClassifyPlan(new[] { P(1, "FrontEnd"), P(2, "frontend") }, DateTimeOffset.UtcNow)
            .WithoutSingletonTags().Plan.IsEmpty);

    /// <summary>门槛调到 1 等于不砍；<b>传 0 或负数不能变成"把所有标签都砍光"</b>（那是静默清空方案）。</summary>
    [Fact]
    public void AFloorOfOneKeepsEverythingAndNonsenseFloorsAreBounded()
    {
        var plan = new ClassifyPlan(new[] { P(1, "独占词") }, DateTimeOffset.UtcNow);
        Assert.Equal(1, plan.WithoutSingletonTags(1).Plan.ItemCount);
        Assert.Empty(plan.WithoutSingletonTags(1).DroppedTags);
        Assert.Equal(1, plan.WithoutSingletonTags(0).Plan.ItemCount);
        Assert.Equal(1, plan.WithoutSingletonTags(-7).Plan.ItemCount);
    }

    /// <summary>硬底线是 2（提示词里要求 3）：<b>程序只确实只挂一条的那种砍，两三条的小组留给用户判断</b>——
    /// 替他砍掉真分类比留下一个偏小的组更难发现。</summary>
    [Fact]
    public void TwoItemGroupsSurviveTheHardFloor()
    {
        Assert.Equal(2, ClassifyPlan.MinItemsPerTag);
        var pruned = new ClassifyPlan(new[] { P(1, "前端"), P(2, "前端") }, DateTimeOffset.UtcNow)
            .WithoutSingletonTags();
        Assert.False(pruned.Plan.IsEmpty);
        // 三条才算类的那一档也要真的能开（提示词改口径时这里先红）
        Assert.True(new ClassifyPlan(new[] { P(1, "前端"), P(2, "前端") }, DateTimeOffset.UtcNow)
            .WithoutSingletonTags(3).Plan.IsEmpty);
    }

    /// <summary>没有专有词时这一刀必须<b>原样返回同一份方案</b>（不是复制一份新的）：
    /// 否则每次读档都白分配一遍列表，也让"砍没砍"这件事变得无从对照。</summary>
    [Fact]
    public void PruningACleanPlanIsANoOp()
    {
        var plan = new ClassifyPlan(new[] { P(1, "前端"), P(2, "前端") }, DateTimeOffset.UtcNow);
        Assert.Same(plan, plan.WithoutSingletonTags().Plan);
    }
}
