#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace StarMark.Abstractions.Clipboard;

/// <summary>
/// 一条剪贴板历史 ↔ 统一 <see cref="Item"/> 的映射，以及它自己的 <c>extra_json</c> 键读写。
/// <para>
/// 与 <see cref="LocalItemState"/> 同一范式：状态塞进 <c>extra_json</c>、缺字段即默认值 ⇒
/// <b>零迁移、零新表</b>，并白拿 FTS / 标签 / 置顶 / 隐藏 / 笔记 / 备份 / 组件刷新这一整套既有能力。
/// </para>
/// <para>
/// 正文放 <see cref="Item.Description"/> 而不是只放 Title：仓储层重算 <c>search_text</c> 的口径是
/// "title + description + notes + 标签"，只塞 Title 会让多行内容的第二行搜不到（Ditto 源踩过同一坑）。
/// </para>
/// </summary>
public static class ClipboardEntry
{
    private const string AppKey = "clipApp";
    private const string FormatKey = "clipFormat";
    private const string CopyCountKey = "clipCopyCount";
    private const string TruncatedKey = "clipTruncated";
    private const string FullLengthKey = "clipFullLength";

    /// <summary>条目格式：纯文本。与 <see cref="FormatFiles"/> 一起构成当前支持的两种负载。</summary>
    public const string FormatText = "text";

    /// <summary>条目格式：文件列表（CF_HDROP）。Title 为首个文件名，正文是所有路径。</summary>
    public const string FormatFiles = "files";

    /// <summary>
    /// 把一次复制动作映射成待 upsert 的条目。
    /// <para>正文为归一（CRLF→LF、去首尾空白）后再按 <see cref="ClipboardPolicy.MaxStoredChars"/>
    /// 截断的结果；<c>clipFullLength</c> 记下<b>截断前</b>的长度，UI 据此说"共 N 字，已截断展示前 32 KB"，
    /// 而不是让用户以为原文就这么长。</para>
    /// </summary>
    public static Item Build(string? rawText, string? sourceApp, string format, DateTimeOffset now)
    {
        var normalized = ClipboardPolicy.NormalizeText(rawText);
        var body = ClipboardPolicy.Truncate(normalized, out var truncated);

        return new Item
        {
            Type = ItemType.Clipboard,
            Source = ItemSources.Clipboard,
            SourceId = ClipboardPolicy.BuildSourceId(normalized),   // 键用<b>全文</b>归一结果，不含截断
            Title = ClipboardPolicy.BuildTitle(body),
            Subtitle = BuildSubtitle(sourceApp, normalized, truncated),
            Uri = string.Empty,                                     // 本地内容无可打开 URI；点击=复制回剪贴板
            Description = body,
            CreatedAt = now.ToUnixTimeSeconds(),
            UpdatedAt = now.ToUnixTimeSeconds(),
            ExtraJson = Write(null, sourceApp, format, 1, truncated, normalized.Length),
        };
    }

    private static string BuildSubtitle(string? sourceApp, string text, bool truncated)
    {
        var lines = 1;
        foreach (var c in text) if (c == '\n') lines++;
        var where = string.IsNullOrWhiteSpace(sourceApp) ? "未知来源" : ClipboardPolicy.CollapseControlChars(sourceApp!);
        var size = truncated ? $"{text.Length}+ 字" : $"{text.Length} 字";
        return lines > 1 ? $"{where} · {lines} 行 · {size}" : $"{where} · {size}";
    }

    private static string Write(string? existingJson, string? sourceApp, string format, long copyCount,
        bool truncated, int fullLength)
    {
        var node = Parse(existingJson);
        if (!string.IsNullOrWhiteSpace(sourceApp)) node[AppKey] = ClipboardPolicy.CollapseControlChars(sourceApp!);
        node[FormatKey] = string.IsNullOrEmpty(format) ? FormatText : format;
        node[CopyCountKey] = copyCount;
        if (truncated)
        {
            node[TruncatedKey] = true;
            node[FullLengthKey] = fullLength;
        }
        else
        {
            // 没截断就把旧标记清掉（而不是写成 JSON null）：留着"已截断"会让 UI 永远挂着半句假话。
            node.Remove(TruncatedKey);
            node.Remove(FullLengthKey);
        }
        return node.ToJsonString();
    }

    private static JsonObject Parse(string? json)
    {
        // 坏 extra_json（用户手改、旧备份）不能把"记一条历史"这条日常路径炸掉：丢坏内容重开一个对象。
        // 与 LocalItemState.ParseObject 同口径。
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                if (JsonNode.Parse(json) is JsonObject obj) return obj;
            }
            catch { /* 落到下面重建 */ }
        }
        return new JsonObject();
    }

    /// <summary>该条目被复制过几次（旧数据无此字段 ⇒ 1）。</summary>
    public static long CopyCount(Item item)
    {
        try
        {
            var v = Parse(item.ExtraJson)[CopyCountKey]?.GetValue<long>();
            return v is null or <= 0 ? 1 : v.Value;
        }
        catch { return 1; }
    }

    /// <summary>
    /// 同一条文本再次复制时，在<b>旧</b>条目的 extra 上累加次数并刷新来源应用（其余键原样保留，
    /// 例如以后可能加进来的用户标记）。次数由调用方（仓储层）从旧值算好后传进来，这里只做拼装。
    /// </summary>
    public static string MergeForReplay(Item existing, Item draft, long copyCount)
        => Write(existing.ExtraJson, App(draft), Format(draft), copyCount, IsTruncated(draft), FullLength(draft));

    /// <summary>条目格式（<see cref="FormatText"/> / <see cref="FormatFiles"/>；未知旧数据按文本）。</summary>
    public static string Format(Item item)
    {
        try
        {
            return Parse(item.ExtraJson)?[FormatKey]?.GetValue<string>() is { Length: > 0 } f ? f : FormatText;
        }
        catch { return FormatText; }
    }

    /// <summary>复制时所处的前台应用进程名（可能为 null＝当时没取到）。</summary>
    public static string? App(Item item)
    {
        try
        {
            var v = Parse(item.ExtraJson)?[AppKey]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { return null; }
    }

    /// <summary>正文是否被截断（决定了 UI 要不要说"内容过长，仅存前 32 KB"）。</summary>
    public static bool IsTruncated(Item item)
    {
        try
        {
            return Parse(item.ExtraJson)?[TruncatedKey]?.GetValue<bool>() ?? false;
        }
        catch { return false; }
    }

    /// <summary>截断前的原始长度；未被截断的条目返回 0。</summary>
    public static int FullLength(Item item)
    {
        if (!IsTruncated(item)) return 0;
        try
        {
            return Parse(item.ExtraJson)?[FullLengthKey]?.GetValue<int>() ?? 0;
        }
        catch { return 0; }
    }
}
