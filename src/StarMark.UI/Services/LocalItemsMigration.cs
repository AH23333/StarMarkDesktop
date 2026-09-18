#nullable enable
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.Data;

namespace StarMark.UI.Services;

/// <summary>
/// 本地条目（待办/随记）一次性迁移：把 <c>widgets.json</c> 中各实例的 <c>Todos</c>/<c>Notes</c>
/// 复制进统一 <c>items</c> 表（source = <see cref="ItemSources.Local"/>），保留多实例隔离
/// （source_id 编码 instanceId）。这是相比 DeskBox（TodoWidgetStore 独立 JSON）更优的统一模型落地。
/// 幂等：<see cref="WidgetStoreData.LocalItemsMigrated"/> 为 true 则跳过；
/// 旧 <c>widgets.json</c> 字段保留（向后兼容、测试安全）。
/// </summary>
public sealed class LocalItemsMigration
{
    private readonly WidgetStorage _storage;
    private readonly IItemRepository _repo;

    public LocalItemsMigration(WidgetStorage storage, IItemRepository repo)
    {
        _storage = storage;
        _repo = repo;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var data = _storage.Load();
        if (data.LocalItemsMigrated == true) return;

        // 备份原 widgets.json，便于回滚
        try
        {
            var path = _storage.StorePath;
            if (File.Exists(path))
                File.Copy(path, path + ".pre-local-migration.bak", overwrite: true);
        }
        catch
        {
            // 备份失败不影响迁移
        }

        var items = new List<Item>();
        foreach (var inst in data.Instances)
        {
            if (inst.Kind == WidgetKind.Todo && inst.Todos is { Count: > 0 })
            {
                foreach (var t in inst.Todos)
                {
                    if (t is null || string.IsNullOrWhiteSpace(t.Text)) continue;
                    var item = NewLocal(ItemType.Todo, inst.Id, t.Id, t.Text, t.CreatedAt);
                    LocalItemState.SetDone(item, t.Done);
                    items.Add(item);
                }
            }
            else if (inst.Kind == WidgetKind.QuickNote && inst.Notes is { Count: > 0 })
            {
                foreach (var n in inst.Notes)
                {
                    if (n is null || string.IsNullOrWhiteSpace(n.Text)) continue;
                    items.Add(NewLocal(ItemType.Note, inst.Id, n.Id, n.Text, n.CreatedAt));
                }
            }
        }

        foreach (var it in items)
            await _repo.UpsertLocalItemAsync(it, ct);

        // 标记已完成并持久化（幂等）
        data.LocalItemsMigrated = true;
        _storage.Save(data);
        StarLog.Info($"本地条目迁移完成：{items.Count} 条（待办/随记）已并入统一 items 表");
    }

    private static Item NewLocal(ItemType type, string instanceId, long localId, string text, long createdAt)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new Item
        {
            Type = type,
            Source = ItemSources.Local,
            SourceId = LocalItemState.EncodeSourceId(instanceId, localId),
            Title = text.Trim(),
            CreatedAt = createdAt == 0 ? now : createdAt,
            UpdatedAt = createdAt == 0 ? now : createdAt,
        };
    }
}
