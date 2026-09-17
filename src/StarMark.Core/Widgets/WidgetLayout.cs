#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace StarMark.Core.Widgets;

/// <summary>
/// 布局中的单个组件条目：按「类型 + 该类型内的序号」匹配实例，而不是写死实例 ID。
/// 这样换一批实例（例如删除重建后）仍能套用同一套布局。
/// </summary>
public sealed class WidgetLayoutEntry
{
    public WidgetKind Kind { get; set; }

    /// <summary>在该类型实例列表中的序号（从 0 起），用于套用布局时挑选实例。</summary>
    public int Index { get; set; }

    /// <summary>位置与尺寸（物理像素）。</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public bool Topmost { get; set; }
}

/// <summary>
/// 一套用户保存的组件布局快照。
/// 语义约定：**同一时刻只显示一套布局**——应用某布局时会隐藏不属于该布局的组件实例
/// （实例本身保留，内容不丢），因此用户可以在多套布局间切换而不会数据丢失。
/// </summary>
public sealed class WidgetLayout
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public long CreatedAt { get; set; }

    public List<WidgetLayoutEntry> Entries { get; set; } = new();

    /// <summary>布局摘要（设置页列表展示用），如「4 个组件」。</summary>
    [JsonIgnore]
    public string Summary => Entries.Count == 0 ? "空布局" : $"{Entries.Count} 个组件";
}

/// <summary>布局集合的纯逻辑工具（去重命名、规范排序），便于单元测试。</summary>
public static class WidgetLayoutCollection
{
    /// <summary>规范化：去空名、按类型内序号排序、为每个命名补全唯一名。</summary>
    public static List<WidgetLayout> Normalize(IEnumerable<WidgetLayout>? layouts)
    {
        var result = new List<WidgetLayout>();
        if (layouts is null) return result;

        foreach (var l in layouts)
        {
            if (l is null) continue;
            if (string.IsNullOrWhiteSpace(l.Id)) l.Id = Guid.NewGuid().ToString("N");
            l.Entries ??= new List<WidgetLayoutEntry>();
            l.Entries = l.Entries
                .Where(e => e is not null)
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.Index)
                .ToList();
            result.Add(l);
        }

        // 名称去重：同名追加 (2)(3)…；空名兜底
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in result)
        {
            var name = string.IsNullOrWhiteSpace(l.Name) ? "未命名布局" : l.Name.Trim();
            if (seen.TryGetValue(name, out var count))
            {
                count++;
                seen[name] = count;
                name = $"{name} ({count})";
            }
            else
            {
                seen[name] = 1;
            }
            l.Name = name;
        }

        return result.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>为新布局生成一个不与现有重名的名字。</summary>
    public static string MakeUniqueName(IEnumerable<WidgetLayout> existing, string desired)
    {
        desired = string.IsNullOrWhiteSpace(desired) ? "未命名布局" : desired.Trim();
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
