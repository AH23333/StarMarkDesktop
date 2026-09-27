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
    private const string WidthKey = "clipWidth";
    private const string HeightKey = "clipHeight";
    private const string BytesKey = "clipBytes";
    private const string FileKey = "clipFile";
    private const string ThumbKey = "clipThumb";
    private const string MissingKey = "clipMissing";

    /// <summary>条目格式：纯文本。与 <see cref="FormatFiles"/> 一起构成此前的两种负载。</summary>
    public const string FormatText = "text";

    /// <summary>条目格式：文件列表（CF_HDROP）。Title 为首个文件名，正文是所有路径。</summary>
    public const string FormatFiles = "files";

    /// <summary>
    /// 条目格式：图片。<b>正文一律空</b>——内容在 <c>%LOCALAPPDATA%\StarMark\clip</c> 下的那个文件里，
    /// 库里再存一份字节就回到"同一张图两处真值"（§3-Q1 磁盘有界与 Q6 可见性都要求只有一份）。
    /// </summary>
    public const string FormatImage = "image";

    /// <summary>图片条目的负载：文件名（相对 clip 目录）与三个显示要用的数。</summary>
    public readonly record struct ImageMeta(string MainName, string ThumbName, int Width, int Height, long Bytes);

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

    /// <summary>
    /// 把一次"复制了图片"映射成待 upsert 的条目。
    /// <para><paramref name="meta"/> 里的文件名是<b>候选名</b>：同图再复制时仓储会把旧行的名字合并回来
    /// （见 <see cref="MergeForReplay"/>），因为名字一旦出现在用户目录里就是用户看得见的事实，
    /// 每次回放都换个新时间戳＝攒孤儿。</para>
    /// </summary>
    public static Item BuildImage(string sourceId, ImageMeta meta, string? sourceApp, DateTimeOffset now)
        => new()
        {
            Type = ItemType.Clipboard,
            Source = ItemSources.Clipboard,
            SourceId = sourceId,
            Title = ClipAssets.DescribeTitle(meta.Width, meta.Height),
            Subtitle = BuildImageSubtitle(sourceApp, meta),
            Uri = string.Empty,                                     // 图片条目没有可打开的 URI；点击=复制回剪贴板
            Description = string.Empty,                             // 正文在文件里，库里不存第二份
            CreatedAt = now.ToUnixTimeSeconds(),
            UpdatedAt = now.ToUnixTimeSeconds(),
            ExtraJson = Write(null, sourceApp, FormatImage, 1, false, 0, meta),
        };

    private static string BuildImageSubtitle(string? sourceApp, ImageMeta meta)
    {
        var where = string.IsNullOrWhiteSpace(sourceApp) ? "未知来源" : ClipboardPolicy.CollapseControlChars(sourceApp!);
        return $"{where} · {meta.Width}×{meta.Height} · {ClipAssets.DescribeBytes(meta.Bytes)}";
    }

    private static string Write(string? existingJson, string? sourceApp, string format, long copyCount,
        bool truncated, int fullLength, ImageMeta? image = null)
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
        if (image is { } m)
        {
            // 文件名以<b>已存在的那一行</b>为准（见 BuildImage 的注释）；字节数与尺寸按最新一次写，
            // clipMissing 一律清掉：走到这里意味着采集侧正要（重新）把文件写到那个名字上。
            if (node[FileKey]?.GetValue<string>() is not { Length: > 0 }) node[FileKey] = m.MainName;
            if (node[ThumbKey]?.GetValue<string>() is not { Length: > 0 }) node[ThumbKey] = m.ThumbName;
            node[WidthKey] = m.Width;
            node[HeightKey] = m.Height;
            node[BytesKey] = m.Bytes;
            node.Remove(MissingKey);
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
    public static long CopyCount(Item item) => CopyCountOf(item.ExtraJson);

    /// <summary>
    /// 次数读取的字符串入口（仓储层手里只有 extra_json 文本，不必为读一个数造一个 <see cref="Item"/>）。
    /// 0/负数/类型错乱一律按 1 返回——不能让一条坏数据在"按次数排序"上变成无穷小。
    /// </summary>
    public static long CopyCountOf(string? extraJson)
    {
        try
        {
            var v = Parse(extraJson)[CopyCountKey]?.GetValue<long>();
            return v is null or <= 0 ? 1 : v.Value;
        }
        catch { return 1; }
    }

    /// <summary>
    /// 同一条内容再次复制时，在<b>旧</b> extra 上累加次数并刷新来源应用（其余键原样保留，
    /// 例如以后可能加进来的用户标记）。次数由调用方（仓储层）从旧值算好后传进来，这里只做拼装。
    /// <para>图片条目要把"<b>旧行已存的文件名</b>"保住（<see cref="Write"/> 里以 existing 为准），
    /// 否则每次回放都写一个新时间戳的名字，旧文件立刻变孤儿——而 §3-Q6 说得很清楚：
    /// 用户目录里的东西不许我们悄悄留下一堆没人认领。</para>
    /// </summary>
    public static string MergeForReplay(string? existingExtraJson, Item draft, long copyCount)
        => Write(existingExtraJson, App(draft), Format(draft), copyCount, IsTruncated(draft), FullLength(draft),
            ImageOf(draft));

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

    // ==================== 图片条目的负载 ====================

    /// <summary>图片尺寸（宽或高为 0＝这不是图片条目，UI 要退回文本行模板）。</summary>
    public static (int Width, int Height) ImageSize(Item item)
    {
        try
        {
            var n = Parse(item.ExtraJson);
            return (n[WidthKey]?.GetValue<int>() ?? 0, n[HeightKey]?.GetValue<int>() ?? 0);
        }
        catch { return (0, 0); }
    }

    /// <summary>落盘字节数（未记录/坏数据按 0；设置页的占用预估按它算，不 stat 单个文件）。</summary>
    public static long ImageBytes(Item item)
    {
        try { return Parse(item.ExtraJson)?[BytesKey]?.GetValue<long>() ?? 0; }
        catch { return 0; }
    }

    /// <summary>主图文件名（相对 clip 目录）。空＝这一行没有对应的文件记录。</summary>
    public static string? FileName(Item item) => NameOf(item.ExtraJson, FileKey);

    /// <summary>缩略图文件名（相对 clip 目录）。与主图同生同死，但缺失时分开报。</summary>
    public static string? ThumbFileName(Item item) => NameOf(item.ExtraJson, ThumbKey);

    private static string? NameOf(string? extraJson, string key)
    {
        try
        {
            var v = Parse(extraJson)?[key]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { return null; }
    }

    /// <summary>
    /// <b>行有图无</b>的标记（§3-Q6 第一类：用户手删文件、旧备份恢复过来）。
    /// 条目一律保留——"文件不见了"不是"这条历史没价值"，删行等于替用户决定他不再需要它。
    /// </summary>
    public static bool IsMissing(Item item)
    {
        try { return Parse(item.ExtraJson)?[MissingKey]?.GetValue<bool>() ?? false; }
        catch { return false; }
    }

    /// <summary>置/清 <c>clipMissing</c>。清掉只在"文件真的又被写出来了"那一刻由采集侧调用。</summary>
    public static string WithMissing(Item item, bool missing)
    {
        var node = Parse(item.ExtraJson);
        if (missing) node[MissingKey] = true;
        else node.Remove(MissingKey);
        item.ExtraJson = node.ToJsonString();
        return item.ExtraJson;
    }

    /// <summary>这条图片的负载（无文件名＝null，采集侧就不会以为自己有名字可以写）。</summary>
    public static ImageMeta? ImageOf(Item item)
    {
        var main = FileName(item);
        if (main is null) return null;
        var (w, h) = ImageSize(item);
        // 缩略图名字缺了就回落到主图名（坏数据的旧行）：UI 拿主图当缩略图用只是费点解码，
        // 编一个"看着像临时件"的名字反而会误导对账去删它。
        return new ImageMeta(main, ThumbFileName(item) ?? main, w, h, ImageBytes(item));
    }
}
