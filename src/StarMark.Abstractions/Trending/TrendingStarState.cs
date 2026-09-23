#nullable enable
using System;
using System.Collections.Generic;

namespace StarMark.Abstractions.Trending;

/// <summary>
/// 「哪些热榜仓库我 star 过」的状态视图：本机已同步的 star 列表 + 本次会话的手动改动。
/// <para>
/// 为什么要有那半边"改动"：<b>已 Star</b> 的判定取自本机已同步列表（不发 HEAD 探测，省 Token 配额），
/// 而用户刚点成功一次 Star 时库里还没有那一行 ⇒ 若只看同步列表，界面会在下一次刷新时又显示"未 Star"，
/// 看起来就像"点了没生效"。会话内改动记下这笔，直到下一次同步（<see cref="ReloadFrom"/>）把权威列表换进来
/// 再清空——那时它已经真的出现在列表里了。
/// </para>
/// </summary>
public sealed class TrendingStarState
{
    private HashSet<string> _synced = new(GitHubRepoId.Comparer);
    private readonly Dictionary<string, bool> _overrides = new(GitHubRepoId.Comparer);

    /// <summary>用一份新的已同步 star 条目重建视图，并丢弃会话改动（新列表已是权威答案，留着会永久掩盖远端变化）。</summary>
    public void ReloadFrom(IEnumerable<Item>? syncedStarItems)
    {
        _synced = TrendingStarIndex.FromItems(syncedStarItems);
        _overrides.Clear();
    }

    public bool IsStarred(string? fullName)
    {
        if (GitHubRepoId.Normalize(fullName) is not { } id) return false;
        if (_overrides.TryGetValue(id, out var manual)) return manual;
        return _synced.Contains(id);
    }

    /// <summary>记一次用户手动 Star / 取消 Star（在远端调用成功之后才调，失败了不该记）。</summary>
    public void Record(string? fullName, bool starred)
    {
        if (GitHubRepoId.Normalize(fullName) is { } id) _overrides[id] = starred;
    }

    /// <summary>有没有未落地的会话改动（页面可据此提示"下次同步后才会出现在收藏列表里"）。</summary>
    public bool HasPendingChanges => _overrides.Count > 0;
}

/// <summary>
/// 「哪些热榜仓库已被我收进收藏（本机书签）」——按 <c>source_id</c> 前缀识别，
/// 与浏览器导入的书签（source = chrome/firefox/edge）和已同步的 star 都不混淆。
/// </summary>
public static class TrendingCollectIndex
{
    public static HashSet<string> FromItems(IEnumerable<Item>? items)
    {
        var set = new HashSet<string>(GitHubRepoId.Comparer);
        if (items is null) return set;
        foreach (var item in items)
        {
            if (item is null || item.Source != ItemSources.Local) continue;
            var id = item.SourceId;
            if (id is null) continue;
            if (!id.StartsWith(TrendingItemDraft.BookmarkSourcePrefix, StringComparison.Ordinal)) continue;
            var fullName = id[TrendingItemDraft.BookmarkSourcePrefix.Length..];
            if (GitHubRepoId.Normalize(fullName) is { } normalized) set.Add(normalized);
        }
        return set;
    }

    public static bool IsCollected(HashSet<string>? collected, string? fullName)
        => collected is not null && GitHubRepoId.Normalize(fullName) is { } id && collected.Contains(id);
}
