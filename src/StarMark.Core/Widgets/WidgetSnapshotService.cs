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

    /// <summary>读某实例当前全部本地条目（待办 + 随记）为快照片段。</summary>
    public async Task<List<SnapshotLocalItem>> CaptureLocalItemsAsync(string instanceId, CancellationToken ct = default)
    {
        var result = new List<SnapshotLocalItem>();
        if (string.IsNullOrEmpty(instanceId)) return result;

        // source=local 一次取全，再按编码进 source_id 的 instanceId 归到本实例（type=null → 待办与随记都要）。
        var items = await _repo.GetBySourceAsync(ItemSources.Local, null, 1000, ct);
        foreach (var it in items)
        {
            if (LocalItemState.DecodeInstanceId(it.SourceId) != instanceId) continue;
            result.Add(new SnapshotLocalItem
            {
                Type = it.Type,
                Title = it.Title,
                ExtraJson = string.IsNullOrEmpty(it.ExtraJson) ? null : it.ExtraJson,
                CreatedAt = it.CreatedAt,
                UpdatedAt = it.UpdatedAt,
            });
        }
        return result;
    }

    /// <summary>
    /// 把快照片段还原到目标实例：先删除该实例现有本地条目，再按目标 instanceId 重新编码 source_id 写回。
    /// <para>
    /// 采用「整实例先删后插」而非逐条 diff —— 应用快照要的是「这一刻严格覆盖」的 Replace 语义，
    /// 让本实例的本地内容与快照完全一致，不多不少。<c>source_id</c> 用<b>新生成</b>的 localId 重编码：
    /// 既保证跨实例可移植（快照里没存原 instanceId），又避免与既有行撞 <c>UNIQUE(source, source_id)</c>。
    /// </para>
    /// </summary>
    public async Task RestoreLocalItemsAsync(
        string targetInstanceId,
        IReadOnlyList<SnapshotLocalItem> items,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(targetInstanceId)) return;

        // 1) 清掉目标实例现有的本地条目（只动本实例，别的实例的本地内容不受影响）
        var existing = await _repo.GetBySourceAsync(ItemSources.Local, null, 1000, ct);
        foreach (var it in existing)
        {
            if (LocalItemState.DecodeInstanceId(it.SourceId) != targetInstanceId) continue;
            await _repo.DeleteBySourceIdAsync(ItemSources.Local, it.SourceId, ct);
        }

        // 2) 写回快照片段的本地条目（新 source_id；批内自碰撞防护）
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in items)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string sourceId;
            do { sourceId = LocalItemState.EncodeSourceId(targetInstanceId, WidgetStorage.NewId()); }
            while (!used.Add(sourceId));   // NewId 含随机位，理论上仍可能同刻相同 → 重取直到批内唯一

            await _repo.UpsertLocalItemAsync(new Item
            {
                Type = s.Type,
                Source = ItemSources.Local,
                SourceId = sourceId,
                Title = s.Title,
                ExtraJson = s.ExtraJson,   // 原样带回：done/color/due/order 全在此，故待办状态不丢
                CreatedAt = s.CreatedAt > 0 ? s.CreatedAt : now,
                UpdatedAt = now,
            }, ct);
        }
    }
}
