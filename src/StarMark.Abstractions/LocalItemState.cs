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
        // 与 SetColor/SetDue/SetOrder 一致走 ParseObject：corrupt extra_json 时丢弃坏内容重开一个对象，
        // 而不是让 JsonNode.Parse 抛未处理 JsonException——否则「勾选完成」这条日常路径遇到坏数据即崩。
        var node = ParseObject(item);
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
    /// 取「相对<paramref name="now"/> 所在<b>日历日</b>第 <paramref name="dayOffset"/> 天」那天的 0 点（Unix 秒）。
    /// <para>
    /// <b>为什么不用 <c>now.AddDays(n)</c> 再取当日 0 点</b>（P-37）：<c>DateTimeOffset.AddDays</c> 加的是
    /// <b>定长 24 绝对小时</b>并保留当下偏移，而夏令时回拨那天的本地长有 25 小时——
    /// 例：标准偏移 +1、夏令 +2，回拨发生在当地 03:00→02:00 那天 D 的 00:30@+2 点「明天」，
    /// +24 小时落回 <b>同一个日历日 D</b> 的 21:30（已换成 +1 偏移）⇒ 存成 D 的 0 点，"明天"当场变成"今天到期"。
    /// 这里先取该时区的日历日、<b>在日期上</b>加减天数、再按<b>目标那天</b>的偏移还原 0 点，
    /// 23/25 小时那种日子也只会跳到正确的邻居日。
    /// </para>
    /// </summary>
    /// <param name="zone">按哪个时区过日历日；默认本机。<b>测试必须注入自己的时区</b>，
    /// 否则这条判据的成败随构建机的系统设置而变（与"不许靠本机状态活着"同一条纪律）。</param>
    public static long DayOffsetStartUnix(DateTimeOffset now, int dayOffset, TimeZoneInfo? zone = null)
    {
        var z = zone ?? TimeZoneInfo.Local;
        var day = TimeZoneInfo.ConvertTime(now, z).Date.AddDays(dayOffset);
        return new DateTimeOffset(day, z.GetUtcOffset(day)).ToUnixTimeSeconds();
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
            if (days < 7) return DateTimeText.WeekdayShort(due.DayOfWeek);
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
