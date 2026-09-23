#nullable enable
using System.Globalization;

namespace StarMark.Core.Widgets;

/// <summary>
/// 一个倒计时/纪念日。
/// <para>
/// <b>存的是"墙上时钟 + 时区 Id"，不是绝对时刻</b>：每年重复的项目要按用户当初输入的那个日历日期 recur，
/// 若只存 UTC 瞬间，北京 01-01 06:00 会被折成上一年的 12-31（UTC 12-31 22:00），纪念日从此差一整天。
/// 绝对时刻在需要时由 <see cref="CountdownPolicy.ToInstant"/> 现算，时区解析失败时退回本机时区。
/// </para>
/// </summary>
public sealed class CountdownItem
{
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>用户输入的本地墙上时钟（Kind=Unspecified）。</summary>
    public DateTime At { get; set; }

    /// <summary>Windows 时区 Id（创建时所在时区）。</summary>
    public string ZoneId { get; set; } = string.Empty;

    /// <summary>true = 每年重复（纪念日/生日）；false = 一次性截止时刻。</summary>
    public bool Yearly { get; set; }

    public long CreatedAt { get; set; }

    /// <summary>已经提醒过的那一次的键（ISO 串）。<b>持久化是为了跨重启不重复弹</b>；
    /// 判"要不要提醒"还要求当前时刻落在 <see cref="CountdownPolicy.NotifyWindow"/> 内，
    /// 所以这条登记不会过期成"永远压住下一次提醒"。
    /// </summary>
    public string? NotifiedOccurrence { get; set; }

    /// <summary>
    /// 深拷贝。<b>快照必须持有自己的副本</b>：倒计时条目上的"已提醒"键会被每秒的刷新循环改写，
    /// 若快照与在用的实例共享同一个对象，存进去的快照会随时间一起变，"回到那一刻"就成了假话。
    /// </summary>
    public CountdownItem Clone() => new()
    {
        Id = Id,
        Title = Title,
        At = At,
        ZoneId = ZoneId,
        Yearly = Yearly,
        CreatedAt = CreatedAt,
        NotifiedOccurrence = NotifiedOccurrence,
    };
}

/// <summary>一个倒计时在本次刷新时的呈现。</summary>
/// <param name="Headline">主行：剩余量（"3 天 05:20"）或"已过 N 天"。</param>
/// <param name="Subline">次行：目标时刻 / 第 N 年。</param>
/// <param name="IsPast">目标（或本年那一次）是否已过。</param>
/// <param name="Occurrence">本轮对应的绝对时刻（用于提醒键与"到点"判定）。</param>
/// <param name="IsDueNow">是否正处在提醒窗口内（界面据此高亮）。</param>
public readonly record struct CountdownView(string Headline, string Subline, bool IsPast, DateTimeOffset Occurrence, bool IsDueNow);

/// <summary>
/// 倒计时的纯逻辑：时刻换算、每年推进、剩余量格式化、到点提醒判定、输入解析。
/// <para>
/// 全部以"传入 now 与传入时区"的形式可测——组件里那个每秒的定时器只负责调它们，
/// 判定本身不在 UI 工程（测试工程引用不到 UI）。
/// </para>
/// </summary>
public static class CountdownPolicy
{
    /// <summary>
    /// 到点提醒的有效期。太短会在应用启动慢的那几分钟里错过；太长会让"上周的截止日"
    /// 在这周开机时突然弹一条。1 小时是这两头的折中，且与"提醒需要进程在跑"这一前提一致。
    /// </summary>
    public static readonly TimeSpan NotifyWindow = TimeSpan.FromHours(1);

    /// <summary>组件内最多几条（再多就超出小窗可读范围）。</summary>
    public const int MaxItems = 12;

    /// <summary>墙上时钟 → 绝对时刻。时区解析不出来（精简系统/换了机器）时退回<b>本机</b>时区：用户输入的是他眼里的几点几分。</summary>
    public static DateTimeOffset ToInstant(DateTime wallClock, string? zoneId)
    {
        var zone = WorldClockPolicy.TryResolve(zoneId) ?? TimeZoneInfo.Local;
        // 夏令时回拨会让本地时间出现两次；GetUtcOffset(DateTime) 取第一次（早的那个），
        // 这与系统对"模糊本地时间"的一致处理相同，不做额外猜测。
        var offset = zone.GetUtcOffset(wallClock);
        return new DateTimeOffset(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), offset);
    }

    /// <summary>
    /// 本轮对应的时刻：一次性项目就是它本身；每年项目取"今年或明年的同一月日同一钟点"。
    /// <para>
    /// 2/29 的纪念年在平年落到<b>2 月最后一天</b>（2/28）——与"31 号到期而该月只有 30 天"时顺延到月末
    /// 是同一套直觉：宁可在这个月里提醒，也不要跳到下一个月。
    /// </para>
    /// </summary>
    public static DateTimeOffset NextOccurrence(CountdownItem item, DateTimeOffset now)
    {
        if (!item.Yearly) return ToInstant(item.At, item.ZoneId);

        var zone = WorldClockPolicy.TryResolve(item.ZoneId) ?? TimeZoneInfo.Local;
        var localNow = now.ToOffset(zone.GetUtcOffset(now.UtcDateTime));
        var wall = item.At;
        for (var year = localNow.Year; year <= localNow.Year + 2; year++)
        {
            var candidate = OccurrenceWallClock(wall, year);
            var at = ToInstant(candidate, zone.Id);
            if (at >= localNow) return at;
        }
        // 循环上界 +2 年必然命中（今年或明年总在范围内），到这里只可能是荒谬年份（year 溢出）
        return ToInstant(OccurrenceWallClock(wall, localNow.Year), zone.Id);
    }

    /// <summary>把原墙钟日期搬到指定年份，月末钳制（2/29 → 平年 2/28）。</summary>
    private static DateTime OccurrenceWallClock(DateTime wall, int year)
    {
        var day = Math.Min(wall.Day, DateTime.DaysInMonth(year, wall.Month));
        return new DateTime(year, wall.Month, day, wall.Hour, wall.Minute, wall.Second, DateTimeKind.Unspecified);
    }

    public static CountdownView Describe(CountdownItem item, DateTimeOffset now)
    {
        var occurrence = NextOccurrence(item, now);
        var until = occurrence - now;
        if (until > TimeSpan.Zero)
            return new CountdownView(FormatRemaining(until), OccurrenceLine(item, occurrence), false, occurrence, false);

        var since = -until;
        return new CountdownView(
            $"已过 {FormatDuration(since)}",
            AnniversaryLine(item, occurrence, now),
            true,
            occurrence,
            since <= NotifyWindow);
    }

    /// <summary>剩余量：<c>3 天 05:20</c>｜<c>05:20</c>｜<c>不足 1 分钟</c>。不到一分钟仍要说，不能显示 00:00 让人以为卡住。</summary>
    public static string FormatRemaining(TimeSpan until)
    {
        if (until < TimeSpan.FromMinutes(1)) return "不足 1 分钟";
        var days = (int)Math.Floor(until.TotalDays);
        var rest = until - TimeSpan.FromDays(days);
        var clock = $"{(int)rest.Hours:00}:{(int)rest.Minutes:00}";
        return days > 0 ? $"{days} 天 {clock}" : clock;
    }

    /// <summary>已过去的时长：分钟级起跳（"42 分钟"），再往上小时/天。</summary>
    public static string FormatDuration(TimeSpan since)
    {
        if (since < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)since.TotalMinutes)} 分钟";
        if (since < TimeSpan.FromDays(1)) return $"{(int)since.TotalHours} 小时";
        return $"{(int)Math.Floor(since.TotalDays)} 天";
    }

    private static string OccurrenceLine(CountdownItem item, DateTimeOffset occurrence)
        => $"{occurrence:yyyy-MM-dd HH:mm}  {(item.Yearly ? YearNumber(item, occurrence) + " 周年" : "到期")}";

    private static string AnniversaryLine(CountdownItem item, DateTimeOffset occurrence, DateTimeOffset now)
    {
        if (!item.Yearly) return $"{occurrence:yyyy-MM-dd HH:mm} 已到期";
        var years = now.Year - item.At.Year;
        return $"{occurrence:MM-dd HH:mm} · 第 {years + 1} 个年头";
    }

    private static string YearNumber(CountdownItem item, DateTimeOffset occurrence)
    {
        var years = occurrence.Year - item.At.Year;
        return years <= 0 ? "今年" : $"{years}";
    }

    /// <summary>提醒键：本轮发生时刻的 UTC ISO 串。同一轮只提醒一次，换一轮（下一年/新时刻）自动失效。</summary>
    public static string OccurrenceKey(DateTimeOffset occurrence) => occurrence.ToUniversalTime().ToString("yy-MM-ddTHHmm", CultureInfo.InvariantCulture);

    /// <summary>该不该为这一轮发提醒（已过、仍在窗口内、且这一轮还没发过）。</summary>
    public static bool ShouldNotify(CountdownItem item, DateTimeOffset occurrence, DateTimeOffset now)
    {
        if (now < occurrence) return false;
        if (now - occurrence > NotifyWindow) return false;
        return !string.Equals(item.NotifiedOccurrence, OccurrenceKey(occurrence), StringComparison.Ordinal);
    }

    /// <summary>标记某轮已提醒。</summary>
    public static void MarkNotified(CountdownItem item, DateTimeOffset occurrence)
        => item.NotifiedOccurrence = OccurrenceKey(occurrence);

    /// <summary>
    /// 解析界面输入。<b>日期与时刻分开传</b>（界面上是两枚控件），任一非法都给可行动的原因：
    /// 只写"格式不对"用户不知道该改哪一半。
    /// </summary>
    public static bool TryParseInput(string? dateText, string? timeText, bool requireTime,
        out DateTime wallClock, out string? error)
    {
        wallClock = default;
        if (!DateTime.TryParseExact((dateText ?? string.Empty).Trim(),
                new[] { "yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            error = "日期请写成 2026-12-31";
            return false;
        }
        var time = (timeText ?? string.Empty).Trim();
        if (time.Length == 0)
        {
            if (requireTime) { error = "还差时刻，如 18:00"; return false; }
            wallClock = date.Date;   // 纪念日只看天：钟点记 00:00
            error = null;
            return true;
        }
        if (!TimeSpan.TryParseExact(time,
                new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss" }, CultureInfo.InvariantCulture, out var span)
            || span < TimeSpan.Zero || span >= TimeSpan.FromDays(1))
        {
            error = "时刻请写成 18:00 或 9:30";
            return false;
        }
        wallClock = date.Date + span;
        error = null;
        return true;
    }

    /// <summary>
    /// 标题兜底。留空不是错误——自动按日期生成一个可辨认的名字，比逼用户起名、或者渲染出一行空白
    /// （读起来像坏了）都好。
    /// </summary>
    public static string TitleOf(string? title, DateTime wallClock, bool yearly)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length > 0) return trimmed;
        return yearly
            ? $"纪念日（{wallClock.Month}-{wallClock.Day}）"
            : $"截止（{wallClock.Year}-{wallClock.Month}-{wallClock.Day} {wallClock.Hour:00}:{wallClock.Minute:00}）";
    }
}
