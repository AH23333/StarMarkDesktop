#nullable enable
using System.Text.Json.Nodes;

namespace StarMark.Abstractions;

/// <summary>
/// 本地条目（待办/随记）辅助。本地内容统一存入 <c>items</c> 表（source = <see cref="ItemSources.Local"/>），
/// 多实例隔离靠 <see cref="EncodeSourceId"/> 把 instanceId 编码进 source_id；完成态等状态存于 <c>extra_json</c>，
/// 避免为待办/随记单独建表——这是相比 DeskBox（TodoWidgetStore 独立 JSON）更优的统一模型。
/// </summary>
public static class LocalItemState
{
    private const string DoneKey = "done";

    /// <summary>把组件实例 ID 与本地条目 ID 编码为 source_id（格式 <c>instanceId|localId</c>）。</summary>
    public static string EncodeSourceId(string instanceId, long localId) => $"{instanceId}|{localId}";

    /// <summary>从 source_id 解出组件实例 ID；非本地编码返回 null。</summary>
    public static string? DecodeInstanceId(string sourceId)
    {
        var idx = sourceId.IndexOf('|');
        return idx < 0 ? null : sourceId[..idx];
    }

    /// <summary>该待办是否已完成（读 extra_json.done）。</summary>
    public static bool IsDone(Item item)
    {
        if (string.IsNullOrEmpty(item.ExtraJson)) return false;
        try
        {
            return JsonNode.Parse(item.ExtraJson)?[DoneKey]?.GetValue<bool>() ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>写入待办完成态到 extra_json（保留其它字段）。</summary>
    public static void SetDone(Item item, bool done)
    {
        var node = string.IsNullOrEmpty(item.ExtraJson)
            ? new JsonObject()
            : (JsonNode.Parse(item.ExtraJson) as JsonObject ?? new JsonObject());
        node[DoneKey] = done;
        item.ExtraJson = node.ToJsonString();
    }
}
