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
}
