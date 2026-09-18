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

    // ── 待办增强：颜色标记与截止日期（同一套 extra_json 键，旧数据零迁移）──

    /// <summary>待办颜色标记在 extra_json 的键。0 = 无颜色（默认）。</summary>
    private const string ColorKey = "color";
    /// <summary>待办截止时间在 extra_json 的键（Unix 秒，对齐到当日 0 点）。</summary>
    private const string DueKey = "due";
    /// <summary>手动排序序号在 extra_json 的键（拖拽排序后写入）。缺失 = 未手动排过。</summary>
    private const string OrderKey = "order";

    /// <summary>颜色标记的可选数量（不含「无」）。</summary>
    public const int ColorCount = 6;

    /// <summary>读待办颜色索引（0 = 无）。旧数据没有该字段时返回 0。</summary>
    public static int GetColor(Item item)
    {
        if (string.IsNullOrEmpty(item.ExtraJson)) return 0;
        try
        {
            var raw = JsonNode.Parse(item.ExtraJson)?[ColorKey]?.GetValue<int>();
            return raw is >= 0 and <= ColorCount ? raw.Value : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>写待办颜色索引（0 = 清除颜色）。</summary>
    public static void SetColor(Item item, int color)
    {
        if (color < 0 || color > ColorCount) color = 0;
        var node = ParseObject(item);
        node[ColorKey] = color;   // 0 也写进去，让「取消颜色」能落盘覆盖旧值
        item.ExtraJson = node.ToJsonString();
    }

    /// <summary>读待办截止时间（Unix 秒，当日 0 点）；未设置返回 null。</summary>
    public static long? GetDue(Item item)
    {
        if (string.IsNullOrEmpty(item.ExtraJson)) return null;
        try
        {
            var raw = JsonNode.Parse(item.ExtraJson)?[DueKey]?.GetValue<long>();
            return raw is > 0 ? raw : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>写待办截止时间（null = 清除）。</summary>
    public static void SetDue(Item item, long? dueUnixSeconds)
    {
        var node = ParseObject(item);
        if (dueUnixSeconds is > 0) node[DueKey] = dueUnixSeconds.Value;
        else node.Remove(DueKey);   // 清除：直接删键，比写 JSON null 干净，旧版读取也天然回落 null
        item.ExtraJson = node.ToJsonString();
    }

    /// <summary>读手动排序序号；未手动排过返回 null。</summary>
    public static int? GetOrder(Item item)
    {
        if (string.IsNullOrEmpty(item.ExtraJson)) return null;
        try
        {
            var raw = JsonNode.Parse(item.ExtraJson)?[OrderKey]?.GetValue<int>();
            return raw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>写手动排序序号。</summary>
    public static void SetOrder(Item item, int order)
    {
        var node = ParseObject(item);
        node[OrderKey] = order;
        item.ExtraJson = node.ToJsonString();
    }

    /// <summary>取某时刻所在「当日 0 点」的 Unix 秒（本地时区）——截止日期按天比较，必须抹掉时分秒。</summary>
    public static long DayStartUnix(DateTimeOffset t)
    {
        var local = t.ToLocalTime();
        // DateTimeOffset(DateTime) 对 Kind=Unspecified 按**本地时区**解释 —— 截止按「本地当天 0 点」比较
        return new DateTimeOffset(local.Date).ToUnixTimeSeconds();
    }

    /// <summary>
    /// 截止时间的中文短描述（组件列表显示用）。
    /// 逾期 / 今天 / 明天 / 本周内星期X / 具体月日 —— 按这个优先级，远的才显示日期。
    /// </summary>
    public static string DescribeDue(long? dueUnixSeconds, DateTimeOffset now)
    {
        if (dueUnixSeconds is not > 0) return string.Empty;
        try
        {
            var due = DateTimeOffset.FromUnixTimeSeconds(dueUnixSeconds.Value).ToLocalTime().Date;
            var today = now.ToLocalTime().Date;
            var days = (due - today).Days;

            if (days < 0) return days == -1 ? "昨天" : $"逾期 {-days} 天";
            if (days == 0) return "今天";
            if (days == 1) return "明天";
            if (days < 7) return "周" + "日一二三四五六"[due.DayOfWeek == DayOfWeek.Sunday ? 0 : (int)due.DayOfWeek];
            return $"{due.Month}月{due.Day}日";
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>该待办是否逾期（未完成且已过截止日）。</summary>
    public static bool IsOverdue(Item item, DateTimeOffset? now = null)
    {
        if (IsDone(item)) return false;
        var due = GetDue(item);
        if (due is not > 0) return false;
        var todayUnix = DayStartUnix(now ?? DateTimeOffset.Now);
        return due.Value < todayUnix;
    }

    private static JsonObject ParseObject(Item item)
    {
        if (string.IsNullOrEmpty(item.ExtraJson)) return new JsonObject();
        try
        {
            return JsonNode.Parse(item.ExtraJson) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }
}
