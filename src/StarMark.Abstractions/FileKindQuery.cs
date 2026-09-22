#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StarMark.Abstractions;

/// <summary>搜索栏「类型」多选下拉的选项。UI 语义，与 Everything 语法解耦。</summary>
public enum FileKind
{
    /// <summary>只看文件夹。</summary>
    Folder,
    Document,
    Picture,
    Music,
    Video,
    Archive,
    /// <summary>大于 100 MB。</summary>
    Large,
    /// <summary>7 天内改动过。</summary>
    Recent,
}

/// <summary>
/// 把「类型」多选翻成 Everything 检索式片段，让用户不必学语法也能用出 Everything 的能力；
/// 片段只进查询请求，绝不回写搜索框（用户手敲的语法保持原样直通）。
/// <para>
/// 逐条对照官方 Search Functions 文档锁定写法：扩展名并集 <c>ext:jpg;jpeg;bmp</c>（分号列表，
/// 一次 AND 词条内表达「或」）、体积 <c>size:&gt;100mb</c>、改动时间 <c>dm:7days</c>、
/// 仅文件夹 <c>folder:</c>。刻意不用 <c>(a|b)</c> 括号+竖线组合——官方示例里没有函数级 OR 用法，
/// 与其赌语法，不如把「或」并进单个 <c>ext:</c> 列表。
/// </para>
/// </summary>
public static class FileKindQuery
{
    private static readonly string[] DocumentExts =
        { "doc", "docx", "xls", "xlsx", "ppt", "pptx", "pdf", "txt", "md", "rtf", "csv", "wps", "et", "dps", "odt", "ods", "odp" };

    private static readonly string[] PictureExts =
        { "png", "jpg", "jpeg", "gif", "bmp", "webp", "tif", "tiff", "ico", "svg" };

    private static readonly string[] MusicExts =
        { "mp3", "wav", "flac", "m4a", "aac", "ogg", "wma", "opus" };

    private static readonly string[] VideoExts =
        { "mp4", "mkv", "avi", "mov", "wmv", "webm", "m4v", "mpg", "mpeg", "3gp" };

    private static readonly string[] ArchiveExts =
        { "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso" };

    /// <summary>类别 → 扩展名表；顺序即输出顺序（保证同一组勾选产生逐字稳定的检索式）。</summary>
    private static readonly (FileKind Kind, string[] Exts)[] CategoryExts =
    {
        (FileKind.Document, DocumentExts),
        (FileKind.Picture, PictureExts),
        (FileKind.Music, MusicExts),
        (FileKind.Video, VideoExts),
        (FileKind.Archive, ArchiveExts),
    };

    /// <summary>
    /// 生成附加检索式。勾选的「类别」之间是或（合成一个 <c>ext:</c>），「修饰」之间以及与类别之间是与。
    /// 选了「文件夹」时不产生 <c>ext:</c>：文件夹没有扩展名，两者同 AND 必然零结果。
    /// </summary>
    public static IReadOnlyList<string> Fragments(IReadOnlyCollection<FileKind>? kinds)
    {
        if (kinds is null || kinds.Count == 0) return Array.Empty<string>();

        var selected = kinds as HashSet<FileKind> ?? new HashSet<FileKind>(kinds);
        var parts = new List<string>(4);

        if (selected.Contains(FileKind.Folder))
        {
            parts.Add("folder:");
        }
        else
        {
            var exts = CategoryExts
                .Where(c => selected.Contains(c.Kind))
                .SelectMany(c => c.Exts)
                .ToArray();
            if (exts.Length > 0)
                parts.Add("ext:" + string.Join(";", exts.Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        if (selected.Contains(FileKind.Large)) parts.Add("size:>100mb");
        if (selected.Contains(FileKind.Recent)) parts.Add("dm:7days");

        return parts;
    }

    /// <summary>把检索式片段 AND 到用户关键词后面（Everything 以空格作 AND）。片段为空时逐字返回原词。</summary>
    public static string Compose(string? query, IReadOnlyList<string>? fragments)
    {
        if (fragments is null || fragments.Count == 0) return query ?? string.Empty;
        var kept = fragments.Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
        if (kept.Length == 0) return query ?? string.Empty;

        var head = (query ?? string.Empty).Trim();
        return head.Length == 0 ? string.Join(" ", kept) : head + " " + string.Join(" ", kept);
    }
}
