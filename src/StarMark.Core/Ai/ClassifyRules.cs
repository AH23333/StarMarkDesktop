#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions.Ai;

namespace StarMark.Core.Ai;

/// <summary>
/// 高置信规则预分类（§19 O2）。<b>它省的是"发出去的量"，不是用户看一眼的权利</b>：
/// 命中的条目折算成普通 <see cref="TagProposal"/> 进同一份方案、走同一个预览确认闸门——
/// 规则打错一片时用户砍的是"那一组"，与 AI 打错的处理路径完全一样。
/// <para>规则只做"看到来源就能定"的粗类（GitHub→开发），语义标签留给 AI：
/// <b>宁可漏一条进 AI（还有第二次机会），不可错一片进预览（用户的信任按片损失）</b>。</para>
/// </summary>
public sealed record ClassifyRule(string? SourceEquals, string? TitleContains, IReadOnlyList<string> Tags)
{
    /// <summary>一条是否命中：两格条件都给出时必须都满足；只给一格就只判那一格。
    /// <b>空条件（两格都 null）永不命中</b>——那是"用户 JSON 里写了个半成品"的形状，
    /// 命中就等于凭空给全库打标。</summary>
    public bool Matches(ClassifyItem item)
    {
        if (SourceEquals is null && TitleContains is null) return false;
        if (SourceEquals is { } src && !string.Equals(src, item.Source, System.StringComparison.OrdinalIgnoreCase))
            return false;
        if (TitleContains is { } needle &&
            (item.Title is null || !item.Title.Contains(needle, System.StringComparison.OrdinalIgnoreCase)))
            return false;
        return Tags.Count > 0;   // 没有标签输出的规则不算命中（命中却提零标签会让条目既不进 AI 也拿不到建议）
    }
}

public static class ClassifyRules
{
    /// <summary>整理与即时分类共同的类型范围（原住在 UI 服务里；下沉后测试可覆盖、且两处永远读同一份）。
    /// <b>刻意不含 Clipboard</b>：它有自己的轮转生命周期，标签既进不了日常检索又会跟着清空消失。</summary>
    public static readonly StarMark.Abstractions.ItemType[] InScope =
    {
        StarMark.Abstractions.ItemType.Bookmark, StarMark.Abstractions.ItemType.GitHubStar,
        StarMark.Abstractions.ItemType.File, StarMark.Abstractions.ItemType.Todo, StarMark.Abstractions.ItemType.Note,
    };

    /// <summary>收藏即时分类的四关闸门（§19 O5）。<b>四关少一关就不发</b>：这一路最坏的失败形状
    /// 不是"少分一条"，而是用户在没按任何 AI 键时感到外发请求/变慢——每一关都必须能把整件事关掉。
    /// 已有标签的条目不补刀（增量语义，与 WithoutAlreadyTagged 同向）。</summary>
    public static bool ShouldInstant(StarMark.Abstractions.Item item, AiSettings settings, bool instantEnabled)
        => instantEnabled
            && settings.Enabled
            && settings.Problem() is null
            && InScope.Contains(item.Type)
            && item.Tags is not { Count: > 0 };

    /// <summary>内置规则。<b>刻意窄</b>：只钉"来源即类别"与"文档域名"三类，全部取自 §19.2 的例子中
    /// 现有字段可判的部分——语言级判定（github+C#→["开发","C#"]）要等条目带语言字段再补，
    /// 现在用标题猜语言错一片的代价高于省下的 token。</summary>
    public static readonly IReadOnlyList<ClassifyRule> Defaults = new List<ClassifyRule>
    {
        new("GitHub", null, new[] { "开发" }),
        new("本地文件", null, new[] { "本地文件" }),
        new(null, "docs.microsoft.com", new[] { "文档", "微软" }),
        new(null, "learn.microsoft.com", new[] { "文档", "微软" }),
    };

    /// <summary>用户自定义规则（JSON 数组，形状与 <see cref="ClassifyRule"/> 一致）。
    /// <b>解析失败整份按"没有自定义"处理</b>——那是一份用户可手改的文件，
    /// 半份生效比整份作废更难排查；内置规则照常。</summary>
    public static IReadOnlyList<ClassifyRule> Merged(string? userJson)
    {
        var list = new List<ClassifyRule>(Parse(userJson));
        list.AddRange(Defaults);   // 用户规则在前：同一条目谁先命中谁说了算（用户能盖内置，反过来不行）
        return list;
    }

    public static IReadOnlyList<ClassifyRule> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return System.Array.Empty<ClassifyRule>();
        try
        {
            var rows = JsonSerializer.Deserialize<List<ClassifyRule>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return (System.Collections.Generic.IReadOnlyList<ClassifyRule>?)
                       rows?.Where(r => r is { Tags.Count: > 0 }).ToList()
                   ?? System.Array.Empty<ClassifyRule>();   // 两侧类型显式对齐（?? 不自动并 List/数组）
        }
        catch (JsonException) { return System.Array.Empty<ClassifyRule>(); }
    }

    /// <summary>把候选切成两堆：<b>命中的带着标签出 AI 的圈，剩下照常成批</b>。
    /// 一条只有一个归宿（首条命中规则的并集封顶 maxTagsPerItem=3，与 Sanitize 同闸门）。</summary>
    public static (IReadOnlyList<ClassifyItem> ForAi, IReadOnlyList<TagProposal> Ruled)
        Split(IReadOnlyList<ClassifyItem> items, IReadOnlyList<ClassifyRule>? rules = null)
    {
        var active = rules ?? Defaults;
        var forAi = new List<ClassifyItem>();
        var ruled = new List<TagProposal>();
        foreach (var item in items)
        {
            var hit = active.FirstOrDefault(r => r.Matches(item));
            if (hit is null) { forAi.Add(item); continue; }
            var tags = TagText.Sanitize(hit.Tags);
            if (tags.Count == 0) { forAi.Add(item); continue; }   // 规则文本被闸门全清了＝该规则不该存在，条目回 AI 不受牵连
            ruled.Add(new TagProposal(item.Id, tags));
        }
        return (forAi, ruled);
    }
}
