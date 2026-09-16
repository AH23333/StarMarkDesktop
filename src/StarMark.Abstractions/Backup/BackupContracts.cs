#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StarMark.Abstractions.Backup;

/// <summary>
/// 用户不可重建的元数据。条目本体（书签/Star/文件）可重新同步，但这三项不行：
/// 用户手写的笔记、隐藏状态、置顶状态。
/// </summary>
/// <remarks>
/// 以 <c>(Source, SourceId)</c> 为键而非自增 <c>Item.Id</c>——恢复后行 id 会变，
/// 用 id 关联会让整份用户元数据挂到错误的条目上。
/// </remarks>
public sealed record UserStateRecord(string Source, string SourceId, bool Hidden, bool Pinned, string? Notes);

/// <summary>标签定义（不含自增 id，理由同上）。</summary>
public sealed record TagRecord(string Name, string? Color, string? ExtraJson);

/// <summary>条目—标签关联，以 <c>(Source, SourceId, TagName)</c> 表达。</summary>
public sealed record ItemTagLink(string Source, string SourceId, string TagName);

/// <summary>恢复策略。</summary>
public enum RestoreMode
{
    /// <summary>
    /// 合并（默认）：Upsert 条目并覆盖用户元数据，<b>不删除</b>备份中不存在的现有条目。
    /// 幂等、可重复执行，适合「找回丢失的笔记/标签」这一真实场景。
    /// </summary>
    Merge = 0,

    /// <summary>
    /// 覆盖：先清空 items 与标签关联，再导入。精确还原到备份时刻，但会丢掉备份之后新增的条目。
    /// 执行前同样强制落快照，可回滚。
    /// </summary>
    Replace = 1,
}

/// <summary>
/// 备份专用仓储。与 <see cref="IItemRepository"/> 分开，避免为一次性能力污染主接口。
/// </summary>
public interface IBackupRepository
{
    Task<IReadOnlyList<Item>> ExportItemsAsync(CancellationToken ct);
    Task<IReadOnlyList<UserStateRecord>> ExportUserStateAsync(CancellationToken ct);
    Task<IReadOnlyList<TagRecord>> ExportTagsAsync(CancellationToken ct);
    Task<IReadOnlyList<ItemTagLink>> ExportItemTagLinksAsync(CancellationToken ct);

    /// <summary>Upsert 条目（按 source+source_id）。会重算 search_text，CJK 展开自动生效。</summary>
    Task ImportItemsAsync(IReadOnlyList<Item> items, CancellationToken ct);

    /// <summary>按 (source, source_id) 覆盖 hidden / pinned / notes。</summary>
    Task ImportUserStateAsync(IReadOnlyList<UserStateRecord> states, CancellationToken ct);

    /// <summary>写入标签定义（已存在则保留，仅补缺失项）。</summary>
    Task ImportTagsAsync(IReadOnlyList<TagRecord> tags, CancellationToken ct);

    /// <summary>重建条目—标签关联。Merge 模式为「追加」，Replace 模式需先调 ClearItemTagLinksAsync。</summary>
    Task ImportItemTagLinksAsync(IReadOnlyList<ItemTagLink> links, CancellationToken ct);

    /// <summary>清空全部条目（Replace 模式）。FTS 由 items_ad_fts 触发器同步清理。</summary>
    Task ClearItemsAsync(CancellationToken ct);

    /// <summary>清空全部条目—标签关联（Replace 模式）。</summary>
    Task ClearItemTagLinksAsync(CancellationToken ct);
}
