#nullable enable
using System.Text.Json.Serialization;

namespace StarMark.Abstractions;

/// <summary>
/// 条目类型。对应原 StarMark 浏览器扩展中的 <c>Source</c>，桌面端扩展为四类。
/// </summary>
public enum ItemType
{
    File,
    Bookmark,
    GitHubStar,
    Clipboard,
}

/// <summary>条目来源标识。每个来源对应一个 IItemSource 实现。</summary>
public static class ItemSources
{
    public const string FileSystem = "filesystem";
    public const string Chrome = "chrome";
    public const string Firefox = "firefox";
    public const string Edge = "edge";
    public const string GitHub = "github";
    public const string Ditto = "ditto";
}

/// <summary>
/// 统一条目。对应 SQLite items 表的一行，也是 UI、规则引擎、所有源适配器的统一数据形态。
/// 移植自浏览器扩展 <c>StarItem</c>，新增 <c>Type</c>/<c>SourceId</c> 以支撑多源条目模型。
/// </summary>
public sealed class Item
{
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public ItemType Type { get; set; }

    /// <summary>来源标识（filesystem / chrome / github / ditto ...）。见 <see cref="ItemSources"/>。</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>源内唯一标识（GitHub repo id、书签 URL、文件路径哈希等）。与 Source 组成唯一键。</summary>
    [JsonPropertyName("source_id")]
    public string SourceId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>副标题：文件路径 / 书签域名 / repo 全名。</summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>可打开的统一标识（file:// / https:// / starmark://item/{id}）。</summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>拼接后的可搜索文本（title + description + tags + notes）。供 FTS5 索引。</summary>
    public string SearchText { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>GitHub Star 数。仅 GitHubStar 类型有值。供数值过滤。</summary>
    public long? StarsCount { get; set; }

    /// <summary>文件大小字节。仅 File 类型有值。供数值过滤。</summary>
    public long? FileSize { get; set; }

    /// <summary>Unix 时间戳（秒）。</summary>
    public long CreatedAt { get; set; }

    public long UpdatedAt { get; set; }

    public long? SyncedAt { get; set; }

    /// <summary>扩展字段（语言、Topic、浏览器、剪贴板类型等）。JSON 文本。</summary>
    public string? ExtraJson { get; set; }

    /// <summary>敏感条目隐藏。对应原扩展 <c>StarItem.hidden</c>。用户状态：同步不覆盖。</summary>
    public bool Hidden { get; set; }

    /// <summary>用户置顶（deskbox 式快速访问）。用户状态：同步不覆盖。</summary>
    public bool Pinned { get; set; }

    public string? Notes { get; set; }

    /// <summary>标签集合。UI 展示时由 JOIN 聚合得到。</summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// GitHub 仓库的 Star 元信息。对应原扩展 <c>StarMeta</c>。存入 <see cref="Item.ExtraJson"/>。
/// </summary>
public sealed class GitHubStarMeta
{
    public string FullName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string? Language { get; set; }
    public long Stars { get; set; }
    public List<string> Topics { get; set; } = new();
    public bool Archived { get; set; }
    public string? Homepage { get; set; }
    public string Url { get; set; } = string.Empty;
    public long? StarredAt { get; set; }
}

/// <summary>书签来源元信息。对应原扩展 <c>BookmarkMeta</c>。</summary>
public sealed class BookmarkMeta
{
    public List<string> FolderPaths { get; set; } = new();
    public List<string> FolderIds { get; set; } = new();
    public long? BookmarkedAt { get; set; }
    public string? FaviconUrl { get; set; }
}
