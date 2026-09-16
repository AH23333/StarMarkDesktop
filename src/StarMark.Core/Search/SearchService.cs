#nullable enable
using System.Diagnostics;
using StarMark.Abstractions;

namespace StarMark.Core.Search;

/// <summary>
/// 统一搜索编排服务。
/// 负责跨源搜索的混排、去重、相关性归并。
/// 对应技术文档 §2.2 统一搜索数据流：
///   1. SQLite FTS5 先缩小文本范围（已持久化的条目）
///   2. Everything 实时查询补充本地文件
///   3. 合并 + 排序 + 去重
/// </summary>
public sealed class SearchService
{
    private readonly IItemRepository _repository;
    private readonly IReadOnlyList<IItemSource> _realTimeSources;

    public SearchService(IItemRepository repository, IEnumerable<IItemSource> realTimeSources)
    {
        _repository = repository;
        _realTimeSources = realTimeSources.ToList();
    }

    /// <summary>
    /// 执行统一搜索。
    /// 空关键词时返回空结果（浏览器扩展的原行为：空查询进入浏览模式，桌面端 MVP 暂不实现浏览）。
    /// </summary>
    public async Task<SearchResult> SearchAsync(string keyword, SearchFilter filter, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            // 无关键词 + 有标签过滤 → 退化为「按标签浏览」，与扩展浏览态的 tagFilters 行为一致。
            // 否则维持原语义：空查询不返回内容（桌面端浏览由文件夹/标签/动态/隐藏四个页面承担）。
            if (!filter.HasTags)
                return new SearchResult { Items = Array.Empty<Item>(), Total = 0, ElapsedMs = 0 };

            var browsed = await _repository.GetAllAsync(new BrowseFilter
            {
                TagFilters = filter.Tags,
                TypeFilter = filter.Type?.ToString().ToLowerInvariant(),
                IncludeHidden = filter.IncludeHidden,
                Sort = filter.Sort ?? "recent",
                Limit = filter.MaxResults,
            }, ct);
            return new SearchResult { Items = browsed, Total = browsed.Count, ElapsedMs = 0 };
        }

        var sw = Stopwatch.StartNew();

        // 并行：SQLite FTS5 查询 + 所有实时源（Everything 等）查询
        var ftsTask = _repository.SearchAsync(keyword, filter, ct);

        // 标签过滤下必须跳过实时源：Everything 返回的本地文件是「虚拟条目」，未入库因而无标签，
        // 参与合并会让「带 ai 标签」的筛选结果里混进一堆无标签文件。
        var realTimeTasks = filter.HasTags
            ? new List<Task<IReadOnlyList<Item>>>()
            : _realTimeSources
                .Where(s => s.IsAvailable)
                .Select(s => s.SearchAsync(keyword, filter, ct))
                .ToList();

        // 等所有源完成（即使部分失败也返回已成功部分）
        var ftsResult = await SafeAwait(ftsTask, ct);
        var realTimeResults = new List<Item>();
        foreach (var t in realTimeTasks)
        {
            var r = await SafeAwait(t, ct);
            if (r.Count > 0) realTimeResults.AddRange(r);
        }

        // 合并 + 去重（按 URI 或 source+source_id）
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<Item>();
        foreach (var item in ftsResult.Items.Concat(realTimeResults))
        {
            var key = string.IsNullOrEmpty(item.Uri) ? $"{item.Source}|{item.SourceId}" : item.Uri;
            if (seen.Add(key))
            {
                merged.Add(item);
            }
        }

        // 语言筛选（SQL 侧已过滤已入库条目；此处兜底过滤实时源返回的条目）。
        if (!string.IsNullOrEmpty(filter.Language))
        {
            merged = merged.Where(i => string.Equals(GetLanguage(i), filter.Language, StringComparison.OrdinalIgnoreCase))
                           .ToList();
        }

        // 结果分段（对应扩展 selectors.ts:56-107 的 isStrong 规则）：
        //   精确匹配 = title 以关键词开头（不分大小写）或 URI 包含关键词。
        //   精确段在前、相关段在后，段内保持原排序（FTS 相关度 / 用户所选排序）。
        var keywordTrimmed = keyword.Trim();
        var exact = new List<Item>();
        var related = new List<Item>();
        foreach (var item in merged)
        {
            var isStrong = item.Title.StartsWith(keywordTrimmed, StringComparison.OrdinalIgnoreCase)
                           || (!string.IsNullOrEmpty(item.Uri) && item.Uri.Contains(keywordTrimmed, StringComparison.OrdinalIgnoreCase));
            (isStrong ? exact : related).Add(item);
        }
        var ordered = exact.Concat(related).ToList();

        // 截断到 MaxResults
        if (ordered.Count > filter.MaxResults)
        {
            ordered = ordered.Take(filter.MaxResults).ToList();
        }

        sw.Stop();
        return new SearchResult
        {
            Items = ordered,
            Total = ordered.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            ExactCount = Math.Min(exact.Count, ordered.Count),
        };
    }

    /// <summary>取条目主语言（仅 GitHubStar 有值）。供语言筛选兜底使用。</summary>
    private static string? GetLanguage(Item item)
    {
        if (item.Type != ItemType.GitHubStar || string.IsNullOrEmpty(item.ExtraJson)) return null;
        try
        {
            var meta = System.Text.Json.JsonSerializer.Deserialize<GitHubStarMeta>(item.ExtraJson);
            return string.IsNullOrEmpty(meta?.Language) ? null : meta.Language;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static async Task<SearchResult> SafeAwait(Task<SearchResult> task, CancellationToken ct)
    {
        try
        {
            return await task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new SearchResult { Items = Array.Empty<Item>(), Total = 0, ElapsedMs = 0 };
        }
    }

    private static async Task<IReadOnlyList<Item>> SafeAwait(Task<IReadOnlyList<Item>> task, CancellationToken ct)
    {
        try
        {
            return await task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<Item>();
        }
    }
}
