#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using StarMark.Abstractions;

namespace StarMark.Core.Widgets;

/// <summary>
/// 快照里的一条本地条目（待办 / 随记）。忠实保存重建 <c>items</c> 行所需的全部用户状态字段。
/// <para>
/// 刻意**不**存 <c>source_id</c>：其格式为 <c>instanceId|localId</c>，而应用快照时目标实例的 instanceId 可能不同，
/// 故在应用阶段按目标实例 id 重新编码（见 <c>LocalItemState.EncodeSourceId</c>），从而跨实例可移植、且不撞唯一键。
/// </para>
/// <para>
/// 除标题/状态(extra_json)外，还捕获 隐藏/置顶/子标题/URI/描述/笔记 与 标签名——
/// 因还原走「整实例先删后插」，<c>item_tags</c> 随旧行级联删除，不捕获这些就会在应用后把用户贴在待办上的
/// 标签、置顶、隐藏、附注悄悄抹平（#53 V1 忠实还原）。source_id 之外的一切可移植字段都进快照。
/// </para>
/// </summary>
public sealed class SnapshotLocalItem
{
    public ItemType Type { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>待办状态（done/color/due/order）所在的 extra_json；随记一般为 null。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExtraJson { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subtitle { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uri { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>items.notes 列（用户给该条目写的便签正文），同步不覆盖的用户状态。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }

    /// <summary>条目上的标签名（还原时按名取或建并重新挂接 item_tags）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Tags { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Hidden { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Pinned { get; set; }

    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}

/// <summary>
/// 快照里的单个组件条目：位置/尺寸/置顶（布局部分） + 实例配置（外观/标题/外壳/隐私） + 组件数据（快捷入口/钉选/待办/随记）。
/// 与「纯模板」<see cref="WidgetLayoutEntry"/> 的**边界**正是后半段数据字段——模板绝不含，快照专有（#52/#53）。
/// </summary>
public sealed class WidgetSnapshotEntry : IWidgetGeometryEntry
{
    public WidgetKind Kind { get; set; }
    public int Index { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Topmost { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }
    public WidgetChromeMode ChromeMode { get; set; }
    public bool PrivacyMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetAppearanceOverride? Appearance { get; set; }

    // ── 组件数据（#53 专有；快照与模板的分水岭） ──
    public List<LinkItem> Links { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GridTag { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GridQuery { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? GridTags { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GridSort { get; set; }

    public List<SnapshotLocalItem> LocalItems { get; set; } = new();
}

/// <summary>一次「布局 + 组件数据」的**不可变**快照点。应用它 = 回到这一刻（Replace，另附自动回滚点）。</summary>
public sealed class WidgetSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public List<WidgetSnapshotEntry> Entries { get; set; } = new();

    /// <summary>快照摘要（列表展示用）：组件数 / 待办·随记条数 / 快捷入口数。</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            var widgets = Entries.Count;
            var local = Entries.Sum(e => e.LocalItems.Count);
            var links = Entries.Sum(e => e.Links.Count);
            return widgets == 0 ? "空快照" : $"{widgets} 个组件 · {local} 条待办/随记 · {links} 个入口";
        }
    }
}

/// <summary>快照集合的纯逻辑（规范排序、命名去重），便于单元测试。</summary>
public static class WidgetSnapshotCollection
{
    /// <summary>规范化：补 Id、条目按类型内序号排序、名称去重、整体按时间倒序（新点在前）。</summary>
    public static List<WidgetSnapshot> Normalize(IEnumerable<WidgetSnapshot>? snapshots)
    {
        var result = new List<WidgetSnapshot>();
        if (snapshots is null) return result;

        foreach (var s in snapshots)
        {
            if (s is null) continue;
            if (string.IsNullOrWhiteSpace(s.Id)) s.Id = Guid.NewGuid().ToString("N");
            s.Entries ??= new List<WidgetSnapshotEntry>();
            s.Entries = s.Entries
                .Where(e => e is not null)
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.Index)
                .ToList();
            // 反序列化遇到显式 null 会用 null 覆盖属性初始化器；统一兜底为空集合，
            // 否则 Summary/Apply/捕获路径上的 .Count / foreach 会 NRE（OneDrive 截断/手改 JSON 可达）。
            foreach (var e in s.Entries)
            {
                e.Links ??= new List<LinkItem>();
                e.LocalItems ??= new List<SnapshotLocalItem>();
            }
            result.Add(s);
        }

        // 名称去重：与 WidgetLayoutCollection 同策略——以「最终名」为准防碰撞，空名兜底。
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in result)
        {
            var desired = string.IsNullOrWhiteSpace(s.Name) ? "未命名快照" : s.Name.Trim();
            var name = desired;
            if (used.Contains(name))
            {
                for (var n = 2; ; n++)
                {
                    name = $"{desired} ({n})";
                    if (!used.Contains(name)) break;
                }
            }
            used.Add(name);
            s.Name = name;
        }

        // 时间倒序：最新快照点排在最前（符合"历史/活动"直觉）；同刻按名称稳定。
        return result
            .OrderByDescending(s => s.CreatedAt)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>为新快照生成一个不与现有点重名的名字。</summary>
    public static string MakeUniqueName(IEnumerable<WidgetSnapshot> existing, string desired)
    {
        desired = string.IsNullOrWhiteSpace(desired) ? "未命名快照" : desired.Trim();
        var names = new HashSet<string>(existing.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(desired)) return desired;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{desired} ({i})";
            if (!names.Contains(candidate)) return candidate;
        }
        return $"{desired} ({Guid.NewGuid().ToString("N")[..4]})";
    }
}
