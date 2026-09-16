#nullable enable

namespace StarMark.Abstractions;

/// <summary>
/// 活动事件类型。对应浏览器扩展 <c>ActivityKind</c>（star_add / star_remove / ...）。
/// 存储时用 <see cref="Enum.ToString()"/> 的小写形式（如 <c>staradd</c>），读取时忽略大小写解析。
/// </summary>
public enum ActivityKind
{
    StarAdd,
    StarRemove,
    BookmarkAdd,
    BookmarkRemove,
    FileAdd,
    FileRemove,
    ClipAdd,
    ClipRemove,
    ItemDelete,
}

/// <summary>
/// 一条活动记录。对应 <c>activity</c> 表的一行；不是条目本身，而是「发生了什么」的事件。
/// 已被同步移除/取消 Star 的主体在 <c>items</c> 里可能已不存在，但事件会保留，
/// 因此活动时间线能回答「这段时间我增删了什么」（见扩展对比方案 P1-3）。
/// </summary>
public sealed class ActivityRecord
{
    public long Id { get; set; }

    /// <summary>Unix 秒时间戳。</summary>
    public long At { get; set; }

    public ActivityKind Kind { get; set; }

    /// <summary>来源键 <c>source:source_id</c>，用于回查条目（若仍存在）。</summary>
    public string? ItemKey { get; set; }

    /// <summary>事件主体标题（取条目标题的快照，主体消失后仍可读）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>可打开的 URI（取条目 URI 的快照）。</summary>
    public string? Uri { get; set; }
}
