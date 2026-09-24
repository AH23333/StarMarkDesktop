#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;

namespace StarMark.Core.Widgets;

/// <summary>
/// 快照「组件数据」的读写编排（#53-L2）。只承担与 <c>items</c> 表交互的部分：
/// 把某实例的本地待办/随记读成快照片段，以及把快照片段写回目标实例。
/// <para>
/// 布局 / 几何 / 窗口显隐与「类型+序号」到实例的匹配，都是 UI 侧 <c>WidgetManager</c> 的职责；
/// 本服务纯数据、无窗口依赖，故可脱离 UI 直接单元测试。
/// </para>
/// </summary>
public sealed class WidgetSnapshotService
{
    private readonly IItemRepository _repo;

    public WidgetSnapshotService(IItemRepository repo) => _repo = repo;

    /// <summary>
    /// 一次问出"哪些实例名下真的有本地条目"。<b>快照两侧都靠它决定谁需要各开一次库</b>：
    /// 捕获时不在名单上的实例直接给空列表（与逐台查出来的结果完全相同，只是不白跑）；
    /// 还原时"快照里这台是空的、库里它也确实是空的"就整个跳过。
    /// </summary>
    public async Task<HashSet<string>> GetInstancesWithLocalItemsAsync(CancellationToken ct = default)
        => new(await _repo.GetInstancesWithLocalItemsAsync(ct), StringComparer.Ordinal);

    /// <summary>读某实例当前全部本地条目（待办 + 随记）为快照片段，忠实保留标签/置顶/隐藏/笔记等用户状态。</summary>
    public async Task<List<SnapshotLocalItem>> CaptureLocalItemsAsync(string instanceId, CancellationToken ct = default)
    {
        var result = new List<SnapshotLocalItem>();
        if (string.IsNullOrEmpty(instanceId)) return result;

        // 按实例前缀精确读取（不再受 GetBySourceAsync 的跨实例 limit 窗口截断）。
        var items = await _repo.GetLocalItemsForInstanceAsync(instanceId, ct);
        foreach (var it in items)
        {
            result.Add(new SnapshotLocalItem
            {
                Type = it.Type,
                Title = it.Title,
                ExtraJson = string.IsNullOrEmpty(it.ExtraJson) ? null : it.ExtraJson,
                Subtitle = string.IsNullOrEmpty(it.Subtitle) ? null : it.Subtitle,
                Uri = string.IsNullOrEmpty(it.Uri) ? null : it.Uri,
                Description = string.IsNullOrEmpty(it.Description) ? null : it.Description,
                Notes = string.IsNullOrEmpty(it.Notes) ? null : it.Notes,
                Tags = it.Tags.Count > 0 ? new List<string>(it.Tags) : null,
                Hidden = it.Hidden,
                Pinned = it.Pinned,
                CreatedAt = it.CreatedAt,
                UpdatedAt = it.UpdatedAt,
            });
        }
        return result;
    }

    /// <summary>
    /// 把快照片段忠实还原到目标实例：交给仓储层在<b>单个事务</b>里按前缀删除本实例既有条目、
    /// 再逐条插回（含 隐藏/置顶/子标题/URI/描述/笔记），并按标签名重新挂接 <c>item_tags</c>。
    /// <para>
    /// 「整实例替换」= 应用快照要这一刻严格覆盖的 Replace 语义，不多不少。<c>source_id</c> 用<b>新生成</b>的
    /// localId 按目标 instanceId 重编码：既保证跨实例可移植（快照里没存原 instanceId），又避免与既有行撞
    /// <c>UNIQUE(source, source_id)</c>。删除与插入同事务，任一步失败整体回滚，绝不留下「删了没插回」的空实例。
    /// </para>
    /// </summary>
    public async Task RestoreLocalItemsAsync(
        string targetInstanceId,
        IReadOnlyList<SnapshotLocalItem> items,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(targetInstanceId)) return;

        var toInsert = new List<Item>(items.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in items)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string sourceId;
            do { sourceId = LocalItemState.EncodeSourceId(targetInstanceId, WidgetStorage.NewId()); }
            while (!used.Add(sourceId));   // NewId 含随机位，理论上仍可能同刻相同 → 重取直到批内唯一

            toInsert.Add(new Item
            {
                Type = s.Type,
                Source = ItemSources.Local,
                SourceId = sourceId,
                Title = s.Title,
                Subtitle = s.Subtitle ?? string.Empty,
                Uri = s.Uri ?? string.Empty,
                Description = s.Description,
                Notes = s.Notes,
                Tags = s.Tags is { Count: > 0 } ? new List<string>(s.Tags) : new List<string>(),
                Hidden = s.Hidden,
                Pinned = s.Pinned,
                ExtraJson = s.ExtraJson,   // 原样带回：done/color/due/order 全在此，故待办状态不丢
                CreatedAt = s.CreatedAt > 0 ? s.CreatedAt : now,
                UpdatedAt = now,
            });
        }

        await _repo.ReplaceLocalItemsForInstanceAsync(targetInstanceId, toInsert, ct);
    }
}
