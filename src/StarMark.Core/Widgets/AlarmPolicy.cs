#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace StarMark.Core.Widgets;

/// <summary>
/// 一星期里哪几天要响。<b>位序跟着 <see cref="DayOfWeek"/> 走</b>（Sunday＝bit0），不跟着"周一是第一天"的直觉走：
/// 这样换算只剩 <c>1 &lt;&lt; (int)day</c> 一处。自己排一遍顺序就得另养一张映射表，而那张表错一位的症状是
/// "闹钟在错误的日子响"——只有真机看得出来（同一件判据两处各写一份必然分岔，记忆 ⑧）。
/// <para>显示顺序（周一在前）另说，见 <see cref="AlarmPolicy.DaysLabel"/>：那是阅读习惯，不是另一份真值。</para>
/// </summary>
[Flags]
public enum AlarmDays
{
    /// <summary>不重复：到点响一次就自动关掉（手机闹钟里"单次"的意思）。</summary>
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,

    /// <summary>周一至周五。</summary>
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    /// <summary>周六与周日。</summary>
    Weekends = Saturday | Sunday,
    /// <summary>每天。</summary>
    Daily = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday,
}

/// <summary>
/// 时钟组件里的一条闹钟。
/// <para>
/// <b>存的是"本机墙上时钟的分钟数"（0–1439），不是绝对时刻</b>：闹钟要跟着人眼里的几点走，
/// 存 UTC 会让换时区／夏令时之后在错误的钟点响。哪一天的判定由 <see cref="AlarmPolicy"/> 现算。
/// </para>
/// </summary>
public sealed class AlarmItem
{
    public long Id { get; set; }

    /// <summary>当天 0 点起算的分钟数（0–1439）。越界＝这坏了的条目<b>永不触发</b>，界面上会写"时间无效"（见 <see cref="AlarmPolicy.HasValidMinute"/>）。</summary>
    public int MinuteOfDay { get; set; }

    /// <summary>开着才会提醒。<b>单次闹钟响过之后由策略自动置回 false</b>——不然明天同一钟点又叫一遍。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>哪几天响。<see cref="AlarmDays.None"/>＝单次。</summary>
    public AlarmDays Days { get; set; } = AlarmDays.Daily;

    /// <summary>用户起的名字，可空（留空时界面上只报钟点，不硬造一个"闹钟"字样）。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 已经提醒过、还没被确认的那一轮的时刻；null＝没有在等确认的。
    /// <para>它是<b>唯一一份"待确认"真值</b>：界面上那一行、菜单里那颗「停止」都从这里来。
    /// 用类型化的时刻而不是字符串键，是为了让"这一轮已经弹过"与"挂太久该收了"用同一个量比较，
    /// 不必把串再解析回来（解析失败的那一支通常静默变成"永远待确认"或"永远不弹"）。</para>
    /// </summary>
    public DateTimeOffset? PendingSince { get; set; }

    /// <summary>深拷贝（理由同 <see cref="CountdownItem.Clone"/>：每秒的刷新会改写待确认标记，快照不许共享）。</summary>
    public AlarmItem Clone() => new()
    {
        Id = Id,
        MinuteOfDay = MinuteOfDay,
        Enabled = Enabled,
        Days = Days,
        Label = Label,
        PendingSince = PendingSince,
    };
}

/// <summary>
/// 闹钟的纯逻辑：哪一天响、今天这一轮是几点、该不该弹、挂着待确认多久、输入怎么解析、那一行怎么说。
/// <para>全部以"传入 now"的形式可测——时钟组件里那张每秒的表只负责调它们（判据不在 UI 工程，
/// 测试工程引用不到 UI）。到点的出口（右下角提示卡）在 UI 侧，本类不碰。</para>
/// </summary>
public static class AlarmPolicy
{
    /// <summary>一个组件实例上最多几条。再多就超出小窗与二级菜单能读的范围，也把"关掉某一条"变成翻找。</summary>
    public const int MaxItems = 8;

    /// <summary>一条标签在菜单里最多显示这么多字，超出截断加省略号（菜单不换行，长标签会把整排挤没）。</summary>
    public const int MaxLabelLength = 24;

    /// <summary>
    /// 到点后还弹的窗口。15 分钟：机器刚从休眠里醒过来、或程序开得晚了几分钟，这一发仍然有用；
    /// 错过一小时再弹"该起床了"只是打扰（倒计时给 1 小时是因为纪念日不存在"过期就没意义"，闹钟是）。
    /// </summary>
    public static readonly TimeSpan NotifyWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// "待确认"在桌面上最长挂多久。超过就安静收掉（下一次到点照常再弹）——
    /// 常驻是为了"别让他错过"，挂上一周就变成"时钟上有一行像是坏了"。
    /// </summary>
    public static readonly TimeSpan PendingExpiry = TimeSpan.FromHours(12);

    /// <summary>分钟数是不是一个能用的钟点。越界只可能来自手改存档或旧版本，此时<b>宁可永不触发也不要按猜出来的钟点响</b>。</summary>
    public static bool HasValidMinute(int minuteOfDay) => minuteOfDay is >= 0 and < 24 * 60;

    /// <summary>
    /// 某一天对应的那一位——<b>全项目只许这一处换算</b>。界面上"按星期勾"那一排也要走这里：
    /// 位序在 UI 里再写一遍（<c>1 &lt;&lt; (int)day</c>）就是第二份真值，而它错的时候的症状是
    /// "勾了周三、响在周四"，只有真机看得出来。
    /// </summary>
    public static AlarmDays BitFor(DayOfWeek day) => (AlarmDays)(1 << (int)day);

    /// <summary>某个星期几在不在这一档里（<see cref="AlarmDays.None"/> 由调用侧单独当"单次"处理，不走这里）。</summary>
    public static bool Matches(AlarmDays days, DayOfWeek day) => (days & BitFor(day)) != 0;

    /// <summary>把某一天加进这一档（菜单上"勾上周三"做的事）。</summary>
    public static AlarmDays TurnOn(AlarmDays days, DayOfWeek day) => days | BitFor(day);

    /// <summary>把某一天从这一档去掉（菜单上"取消勾"做的事）。</summary>
    public static AlarmDays TurnOff(AlarmDays days, DayOfWeek day) => days & ~BitFor(day);

    /// <summary>
    /// 把"这一天"与"当天第几分钟"拼成那一刻，偏移量沿用传进来的那一个（本类的时刻都是"人眼里的钟点"）。
    /// <para>夏令时切换的那一天里，跨切换的钟点会跟着偏一小时——与系统对模糊本地时刻的一致处理相同，
    /// 不做额外猜测；这条限制的代价只是<b>那一天</b>早或晚一小时，不是每天。</para>
    /// </summary>
    private static DateTimeOffset AtMinute(DateTimeOffset thatDay, int minute)
        => new DateTimeOffset(thatDay.Year, thatDay.Month, thatDay.Day, 0, 0, 0, thatDay.Offset).AddMinutes(minute);

    /// <summary>
    /// 今天这一轮的时刻；今天不该响（重复档不含今天，或分钟数无效）时为 null。
    /// <para>单次闹钟不含"今天不响"这一说：它按"下一个还没到的钟点"算，见 <see cref="NextOccurrence"/>。</para>
    /// </summary>
    public static DateTimeOffset? TodayOccurrence(AlarmItem item, DateTimeOffset now)
    {
        if (!HasValidMinute(item.MinuteOfDay)) return null;
        if (item.Days != AlarmDays.None && !Matches(item.Days, now.DayOfWeek)) return null;
        return AtMinute(now, item.MinuteOfDay);
    }

    /// <summary>
    /// 下一次该响的时刻（含"今天还没过的那个钟点"）。找不到＝无效分钟数。
    /// <para>往上找 8 天而不是 7：任何一个非空的星期档都在 7 天内命中，多走一天只为覆盖
    /// "今天已过期而档里只有今天"这一种边角，不靠循环边界凑巧。</para>
    /// </summary>
    public static DateTimeOffset? NextOccurrence(AlarmItem item, DateTimeOffset now)
    {
        if (!HasValidMinute(item.MinuteOfDay)) return null;
        for (var extra = 0; extra <= 7; extra++)
        {
            var day = now.Date.AddDays(extra);
            if (item.Days != AlarmDays.None && !Matches(item.Days, day.DayOfWeek)) continue;
            var at = AtMinute(day, item.MinuteOfDay);
            if (at > now) return at;
        }
        return null;
    }

    /// <summary>该不该为这一轮发提醒：开着、今天该响、已过时刻但还在 <see cref="NotifyWindow"/> 内、且这一轮没弹过。</summary>
    public static bool ShouldNotify(AlarmItem item, DateTimeOffset now)
    {
        if (!item.Enabled) return false;
        if (TodayOccurrence(item, now) is not { } at) return false;
        if (now < at || now - at > NotifyWindow) return false;
        return item.PendingSince != at;                     // 这一轮已经挂着待确认：不再重弹
    }

    /// <summary>
    /// 记下"这一轮弹过了"。<b>单次档顺手关掉自己</b>：响过一次还留着"开着"，
    /// 读起来像"它还会再叫"，而用户要的是就叫这一次。
    /// </summary>
    public static void MarkNotified(AlarmItem item, DateTimeOffset occurrence)
    {
        item.PendingSince = occurrence;
        if (item.Days == AlarmDays.None) item.Enabled = false;
    }

    /// <summary>是否正挂着一条没确认的提醒（超过 <see cref="PendingExpiry"/> 就不再挂着）。</summary>
    public static bool IsPending(AlarmItem item, DateTimeOffset now)
        => item.PendingSince is { } at
            && at <= now
            && now - at <= PendingExpiry;

    /// <summary>确认掉这一轮（菜单里那颗「停止」做的事）。不改动开关：关掉的是"这条还在等确认"，不是"这条以后不叫了"。</summary>
    public static void Confirm(AlarmItem item) => item.PendingSince = null;

    /// <summary>0–1439 → <c>07:30</c>。越界照原样写出来（无效条目要在界面上看得见地无效，而不是被悄悄折成 00:00）。</summary>
    public static string FormatMinute(int minuteOfDay)
        => HasValidMinute(minuteOfDay)
            ? $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}"
            : "时间无效";

    /// <summary>星期档怎么说（阅读顺序周一在前，那是习惯不是另一份真值）。</summary>
    public static string DaysLabel(AlarmDays days)
    {
        if (days == AlarmDays.None) return "单次";
        if (days == AlarmDays.Daily) return "每天";
        if (days == AlarmDays.Weekdays) return "工作日";
        if (days == AlarmDays.Weekends) return "周末";
        var names = new[]
        {
            (DayOfWeek.Monday, "周一"), (DayOfWeek.Tuesday, "周二"), (DayOfWeek.Wednesday, "周三"),
            (DayOfWeek.Thursday, "周四"), (DayOfWeek.Friday, "周五"), (DayOfWeek.Saturday, "周六"),
            (DayOfWeek.Sunday, "周日"),
        };
        var picked = new List<string>();
        foreach (var (day, name) in names)
            if (Matches(days, day)) picked.Add(name);
        return string.Join(" ", picked);
    }

    /// <summary>菜单里一条闹钟的一行：<c>07:30 起床 · 每天</c>（开着）或前面加一个「关」字样的状态由调用侧给。</summary>
    public static string LineOf(AlarmItem item)
    {
        var label = item.Label.Length > 0 ? " " + TrimLabel(item.Label) : string.Empty;
        return $"{FormatMinute(item.MinuteOfDay)}{label} · {DaysLabel(item.Days)}";
    }

    /// <summary>标签截断（只截不换行：菜单不换行，长标签会把整排挤没）。</summary>
    public static string TrimLabel(string? label)
    {
        var trimmed = (label ?? string.Empty).Trim();
        return trimmed.Length <= MaxLabelLength ? trimmed : trimmed[..MaxLabelLength] + "…";
    }

    /// <summary>
    /// 时钟上那一行待确认的话：最早那条的钟点，多于一条时报条数。没有待确认时返回空串（整行不占位）。
    /// </summary>
    public static string PendingLine(IEnumerable<AlarmItem> items, DateTimeOffset now)
    {
        AlarmItem? earliest = null;
        var count = 0;
        foreach (var item in items)
        {
            if (!IsPending(item, now)) continue;
            count++;
            // 取最早那一条（"07:30 待确认"要说的是他最该看到的那一次）。
            // 这里是 DateTimeOffset? 之间的比较，所以两边都要先拆出值——可空类型上没有 < 运算符。
            if (item.PendingSince is { } pending
                && (earliest is null || earliest.PendingSince is not { } head || pending < head)) earliest = item;
        }
        if (earliest is null) return string.Empty;
        var at = earliest.PendingSince?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "--:--";
        return count == 1 ? $"闹钟 {at} 待确认" : $"闹钟 {at} 等 {count} 条待确认";
    }

    /// <summary>
    /// 解析"新建/修改"那一个输入框：<b>时间与名字写在同一行</b>（<c>7:30 起床</c>）。
    /// 拆成两个输入框会让"改名"和"改时间"变成两趟弹窗，而在小窗右键菜单里，多一步就是不做这一步。
    /// <para>钟点是<b>开头连续的数字与冒号</b>，剩下的一律算名字——所以"7:30 起床"与"7:30起床"都要认：
    /// 只按空格切会让后一种直接报错，而"我明明写了时间"却被告知格式不对，是最招烦的一种红。</para>
    /// <para>时刻<b>自己拆两位数字，不交给 <c>TimeSpan.TryParseExact</c></b>：那道闸把"24:00"当成格式不对，
    /// 于是"我写了 24 点"收到的回答是"请写成 7:30"——他照着改还是 24 点，白跑一趟。越界要单独说一句它越了哪一边界。</para>
    /// </summary>
    public static bool TryParseInput(string? text, out int minute, out string label, out string? error)
    {
        minute = 0;
        label = string.Empty;
        var input = (text ?? string.Empty).Trim();
        if (input.Length == 0)
        {
            error = "请写时间，例如 7:30 起床";
            return false;
        }

        var tail = 0;
        while (tail < input.Length && (char.IsAsciiDigit(input[tail]) || input[tail] == ':')) tail++;
        var head = input[..tail];
        const string shape = "开头请写成 7:30 或 07:30（后面可以跟名字）";
        var bits = head.Split(':');
        if (bits.Length != 2
            || bits[0].Length > 2
            || !int.TryParse(bits[0], out var hour)
            || !int.TryParse(bits[1], out var plainMinute))
        {
            error = shape;
            return false;
        }
        if (bits[1].Length != 2)
        {
            error = "分钟要写两位，例如 7:05";
            return false;
        }
        if (hour > 23)
        {
            error = "小时要在 0–23 之间（24:00 请写 0:00）";
            return false;
        }
        if (plainMinute > 59)
        {
            error = "分钟要在 0–59 之间";
            return false;
        }

        minute = hour * 60 + plainMinute;
        label = TrimLabel(input[tail..]);
        error = null;
        return true;
    }
}
