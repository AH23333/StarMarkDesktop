#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StarMark.Abstractions.Ai;

/// <summary>
/// 一组"打同样的标签"的条目。<b>预览与确认都以组为单位</b>：三百条逐个看是没人会做的复核，
/// 而"这一组六个标签一样的"一眼就能判断对不对——扩展侧的分批返工最后收敛到这个粒度。
/// </summary>
public sealed record TagGroup(IReadOnlyList<string> Tags, IReadOnlyList<long> Ids)
{
    /// <summary>标签集合作为分组的键。<b>顺序不算数</b>（["a","b"] 与 ["b","a"] 是同一组），
    /// 大小写算同一个词——模型一会儿给"前端"一会儿给"FrontEnd"时不该裂成两组。</summary>
    public string Key => string.Join("|", Tags.Select(t => t.ToLowerInvariant()).OrderBy(t => t, StringComparer.Ordinal));
}

/// <summary>
/// 一份待应用的整理方案。<b>它就是"暂停/续跑"的全部真相</b>：
/// 落盘的是一份还没应用的方案（里面是已经整理好的条目），而不是"跑到第几批了"——
/// 候选集合是由库里当前的未打标签条目算出来的，两次运行之间会变，
/// 按批次号续跑会去问一批已经不属于本次的条目。
/// </summary>
public sealed record ClassifyPlan(IReadOnlyList<TagProposal> Proposals, DateTimeOffset CreatedAt)
{
    public static readonly ClassifyPlan Empty = new(Array.Empty<TagProposal>(), DateTimeOffset.UnixEpoch);

    public bool IsEmpty => Proposals.Count == 0;
    public int ItemCount => Proposals.Count;

    /// <summary>已经带过其中任意一个标签的条目不重复提：<b>整理是追加，但同一个标签追加两次没有意义</b>。
    /// <paramref name="existingTagsOf"/> 由调用方给（界面读库、单测给表），这里只讲规则。</summary>
    public ClassifyPlan WithoutAlreadyTagged(Func<long, IReadOnlyList<string>> existingTagsOf)
    {
        var kept = new List<TagProposal>();
        foreach (var proposal in Proposals)
        {
            var existing = existingTagsOf(proposal.Id) ?? Array.Empty<string>();
            var fresh = proposal.Tags
                .Where(tag => !existing.Any(has => string.Equals(has, tag, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (fresh.Count > 0) kept.Add(new TagProposal(proposal.Id, fresh));
        }
        return new ClassifyPlan(kept, CreatedAt);
    }

    /// <summary>丢掉某一组（用户对这一组说"不对"）。<b>只按标签集合匹配，不按 id</b>：
    /// 一组是一个判断，判断否了就把这一整组的条目一起撤掉，不留半组。</summary>
    public ClassifyPlan WithoutGroup(TagGroup group)
    {
        var unwanted = new HashSet<string>(group.Tags, StringComparer.OrdinalIgnoreCase);
        var dropped = Proposals.Where(proposal => proposal.Tags.Select(t => t.ToLowerInvariant()).OrderBy(t => t, StringComparer.Ordinal)
                              .SequenceEqual(group.Tags.Select(t => t.ToLowerInvariant()).OrderBy(t => t, StringComparer.Ordinal)))
                          .Select(proposal => proposal.Id)
                          .ToHashSet();
        return new ClassifyPlan(Proposals.Where(proposal => !dropped.Contains(proposal.Id)).ToList(), CreatedAt);
    }

    /// <summary>丢掉某一条（用户只看这一条觉得不对）。剩下的同组条目不受影响。</summary>
    public ClassifyPlan WithoutItem(long id)
        => new(Proposals.Where(proposal => proposal.Id != id).ToList(), CreatedAt);

    /// <summary>按标签集合分组，条目多的在前。<b>排序要有确定次序</b>：平局时按 key 排，
    /// 否则每次打开设置页这一列都会换个位置，用户会以为方案变了。</summary>
    public IReadOnlyList<TagGroup> Groups()
    {
        var buckets = new Dictionary<string, (List<string> Tags, List<long> Ids)>();
        foreach (var proposal in Proposals)
        {
            var sorted = proposal.Tags.Select(t => t.ToLowerInvariant()).OrderBy(t => t, StringComparer.Ordinal).ToList();
            var key = string.Join("|", sorted);
            if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = (proposal.Tags.ToList(), new List<long>());
            bucket.Ids.Add(proposal.Id);
        }

        return buckets.Values
            .Select(bucket => new TagGroup(bucket.Tags, bucket.Ids))
            .OrderByDescending(group => group.Ids.Count)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>方案里出现过的全部标签（去重）。</summary>
    public IReadOnlyList<string> AllTags()
        => Proposals.SelectMany(proposal => proposal.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// 一个标签至少要被几条条目共用才算"分类"。<b>硬底线取 2，提示词里要求 3</b>：应用侧只砍
    /// "整个方案里确实只挂了一条"的那种，两三条小而准的组留给用户自己判断——
    /// 程序替他砍掉真分类，比留下一个偏小的组更难被发现。
    /// </summary>
    public const int MinItemsPerTag = 2;

    /// <summary>
    /// 砍掉"整个方案里只出现一次"的标签，连带砍掉因此一个标签都不剩的那些条目。
    /// <para>不做这一步，模型给的每个专有词都会真的建出一个标签，界面上就是几百个各挂一条的标签——
    /// 用户要的"分类"于是变成另一种脏。</para>
    /// <para>计数按<b>整个方案</b>算而不是按批：同一个词在两批里各出现一次，合起来是两条，它够格留下。</para>
    /// </summary>
    public SingletonPruning WithoutSingletonTags(int minItems = MinItemsPerTag)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var proposal in Proposals)
            foreach (var tag in proposal.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
                counts[tag] = counts.GetValueOrDefault(tag) + 1;

        var floor = Math.Max(1, minItems);
        var dropped = counts.Where(pair => pair.Value < floor)
            .Select(pair => pair.Key)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToList();
        if (dropped.Count == 0) return new SingletonPruning(this, Array.Empty<string>(), 0);

        var unwanted = new HashSet<string>(dropped, StringComparer.OrdinalIgnoreCase);
        var kept = new List<TagProposal>();
        var droppedItems = 0;
        foreach (var proposal in Proposals)
        {
            var fresh = proposal.Tags.Where(tag => !unwanted.Contains(tag)).ToList();
            if (fresh.Count == 0)
            {
                droppedItems++;                                 // 这条的标签全是专有词 ⇒ 整条不提
                continue;
            }
            kept.Add(fresh.Count == proposal.Tags.Count
                ? proposal
                : new TagProposal(proposal.Id, fresh));           // 只砍掉不够格的那几个词
        }
        return new SingletonPruning(new ClassifyPlan(kept, CreatedAt), dropped, droppedItems);
    }
}

/// <summary>一次"砍掉不成类标签"的结果。<b>砍了多少必须报得出来</b>：界面上建议条数变少了，
/// 用户有权知道是程序替他砍的，而不是模型少答了。</summary>
public sealed record SingletonPruning(ClassifyPlan Plan, IReadOnlyList<string> DroppedTags, int DroppedItems);
