#nullable enable
using System;
using System.Collections.Generic;

namespace StarMark.Core.Text;

/// <summary>一段文本片段。<see cref="IsMatch"/> 为 true 表示命中搜索词，需高亮。</summary>
public sealed class TextSegment
{
    public string Text { get; }
    public bool IsMatch { get; }

    public TextSegment(string text, bool isMatch)
    {
        Text = text;
        IsMatch = isMatch;
    }

    public override string ToString() => IsMatch ? $"[{Text}]" : Text;
}

/// <summary>
/// 搜索结果字段高亮。对齐浏览器扩展 <c>highlight()</c>（`sidepanel/App.tsx`）——
/// 只高亮**首个**匹配片段，避免长文本逐词着色的性能与视觉噪音。
/// 纯函数，放 Core 以便单元测试（UI 层只保留把片段刷进 TextBlock.Inlines 的附加属性）。
/// </summary>
public static class Highlighter
{
    /// <summary>
    /// 把 <paramref name="text"/> 按 <paramref name="query"/> 切分为普通/命中片段。
    /// 策略：先试完整查询串，未命中再退化为逐个词（长的优先）——
    /// 这样搜 "rust async" 时两者都能亮，而不是整串找不到就全不亮。
    /// </summary>
    public static IReadOnlyList<TextSegment> Split(string? text, string? query)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<TextSegment>();

        if (string.IsNullOrWhiteSpace(query))
            return new[] { new TextSegment(text, false) };

        var q = query.Trim();

        var idx = text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            // 退化为按空格拆词，长词优先（优先高亮信息量更大的词）
            var parts = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Array.Sort(parts, (a, b) => b.Length.CompareTo(a.Length));
            foreach (var p in parts)
            {
                idx = text.IndexOf(p, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) { q = p; break; }
            }
        }

        if (idx < 0)
            return new[] { new TextSegment(text, false) };

        var len = Math.Min(q.Length, text.Length - idx);
        var result = new List<TextSegment>(3);
        if (idx > 0) result.Add(new TextSegment(text[..idx], false));
        result.Add(new TextSegment(text.Substring(idx, len), true));
        var tail = idx + len;
        if (tail < text.Length) result.Add(new TextSegment(text[tail..], false));
        return result;
    }
}
