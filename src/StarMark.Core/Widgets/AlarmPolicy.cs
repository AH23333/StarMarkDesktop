#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;

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

    /// <summary>越界那条钟点在界面上说的那句（键只在这里出现一次，<see cref="MinuteCell"/> 与 <see cref="FormatMinute"/> 共用）。</summary>
    public const string InvalidMinuteKey = "alarm.timeInvalid";

    /// <summary>
    /// 钟点那一格：<b>有效＝数据（<c>07:30</c>，不参与翻译）；无效＝还没取词的那句</b>（<c>alarm.timeInvalid</c>）。
    /// <para>为什么交 <c>object</c> 而不是先把那句渲染成字符串再拼进整行：外层那句（<c>alarm.line</c>）是
    /// <b>常驻元素</b>上要重贴的，实参一旦已成字符串，语言就被定死在算出它的那一刻（坑表 #276／#344 那一族）。</para>
    /// <para>越界照原样写出来：无效条目要在界面上看得见地无效，而不是被悄悄折成 00:00。</para>
    /// </summary>
    public static object MinuteCell(int minuteOfDay)
        => HasValidMinute(minuteOfDay)
            ? $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}"
            : LocMessage.Of(InvalidMinuteKey);

    /// <summary>0–1439 → <c>07:30</c>；越界 → 当时的语言里那句"时间无效"。<b>只给每次重建那一路用</b>（输入框回填、每次右键重建的菜单）。</summary>
    public static string FormatMinute(int minuteOfDay)
        => MinuteCell(minuteOfDay) as string ?? Locale.T(InvalidMinuteKey);

    /// <summary>星期档怎么说（阅读顺序周一在前，那是习惯不是另一份真值）。</summary>
    public static LocMessage DaysMessage(AlarmDays days)
    {
        if (days == AlarmDays.None) return LocMessage.Of("alarm.days.once");
        if (days == AlarmDays.Daily) return LocMessage.Of("alarm.days.daily");
        if (days == AlarmDays.Weekdays) return LocMessage.Of("alarm.days.weekdays");
        if (days == AlarmDays.Weekends) return LocMessage.Of("alarm.days.weekends");
        // 这里只拥有一件事：列出来的次序（周一在前）。那七个名字归 DateTimeText／那张表，⛔ 在这儿再抄一张。
        var readOrder = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
            DayOfWeek.Sunday,
        };
        var picked = new List<object>();
        foreach (var day in readOrder)
            if (Matches(days, day)) picked.Add(DateTimeText.WeekdayShortMessage(day));
        // 勾出来的组合有几格就用几格的那颗键（PickKeys 按下标取，⛔ 拼 `$"alarm.days.pick{n}"`——
        // 拼出来的键在源码里不成串，那颗"表里没有一行没人问"的闸门就看不见它，写错的键名也能静默落到 ⟨键⟩ 之外）。
        return LocMessage.Of(PickKeys[picked.Count - 1], picked.ToArray());
    }

    /// <summary>
    /// 逐天勾出来的组合怎么说：下标＝勾了几格（1…6）。存的是<b>键名</b>不是字（键与语言无关），
    /// 一位一槽、条数对上颗数，⛔ 一条七槽模板拿空格填空（那会在句尾留下看不见的空格）。
    /// <para>只有六颗：<b>勾满七天＝<see cref="AlarmDays.Daily"/>，那句先由上面的预设档接住</b>，
    /// 第七格永远走不到（这条由 <c>AlarmPolicyTests</c> 穷举 128 种组合法当场对账，不靠注释）。</para>
    /// </summary>
    private static readonly string[] PickKeys =
    [
        "alarm.days.pick1", "alarm.days.pick2", "alarm.days.pick3", "alarm.days.pick4",
        "alarm.days.pick5", "alarm.days.pick6",
    ];

    /// <summary>星期档的<b>成品串</b>（每次重建那一路：菜单标题、每次打开的浮层）。常驻元素要拿 <see cref="DaysMessage"/>。</summary>
    public static string DaysLabel(AlarmDays days) => DaysMessage(days).Render();

    /// <summary>
    /// 菜单／表面里一条闹钟的一行：<c>07:30 起床 · 每天</c>。名字是用户打的字，不参与翻译，
    /// 所以"有名字"与"没名字"是两颗键（⛔ 用一句里的条件空格凑出来——那会在没名字时留下一个洞）。
    /// <para>开关状态不在这一句里：那是调用侧的前缀（<c>alarm.row.offTitle</c>），因为表面那一排与右键菜单贴的位置不同。</para>
    /// </summary>
    public static LocMessage LineMessage(AlarmItem item)
    {
        var label = TrimLabel(item.Label);
        return label.Length > 0
            ? LocMessage.Of("alarm.line", MinuteCell(item.MinuteOfDay), label, DaysMessage(item.Days))
            : LocMessage.Of("alarm.lineNoLabel", MinuteCell(item.MinuteOfDay), DaysMessage(item.Days));
    }

    /// <summary>那一行的<b>成品串</b>（不上屏幕的消费者：日志与右下角提示卡正文）。</summary>
    public static string LineOf(AlarmItem item) => LineMessage(item).Render();

    /// <summary>标签截断（只截不换行：菜单不换行，长标签会把整排挤没）。切法在 <see cref="TextTrim"/>。</summary>
    public static string TrimLabel(string? label)
        => TextTrim.Ellipsize((label ?? string.Empty).Trim(), MaxLabelLength);

    /// <summary>
    /// 时钟上那一行待确认的话：最早那条的钟点，多于一条时报条数。没有待确认时返回 <c>null</c>（整行不占位）。
    /// <para>返回 null 而不是空串：''没话要说''与''有一句话但它是空的''在界面上必须是两种状态，
    /// 否则换语言重贴会把上一句又贴回来（坑表 #276 那一族的反向）。</para>
    /// </summary>
    public static LocMessage? PendingMessage(IEnumerable<AlarmItem> items, DateTimeOffset now)
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
        if (earliest is null) return null;
        var at = earliest.PendingSince?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "--:--";
        return count == 1
            ? LocMessage.Of("alarm.pending.one", at)
            : LocMessage.Of("alarm.pending.many", at, count);
    }

    /// <summary>待确认那一行的<b>成品串</b>（时钟每秒刷的那一处：没有时给空串，整行不占位）。</summary>
    public static string PendingLine(IEnumerable<AlarmItem> items, DateTimeOffset now)
        => PendingMessage(items, now)?.Render() ?? string.Empty;

    /// <summary>时钟表面上一行闹钟：给界面画的，不是第二份真值（判据仍在这里，界面只摆盘子）。</summary>
    /// <param name="Id">这一条的身份，点击回来时只认它。</param>
    /// <param name="Text"><c>07:30 起床 · 每天</c>，交的是<b>还没取词的那句</b>（批次 WI-37-C）——这一排是常驻元素，
    /// 只在定义变了时重建，换语言那一刻由 <c>LocText</c> 重贴；在这里就渲染成字符串＝那一行停在旧语言（坑表 #344）。</param>
    /// <param name="Enabled">开着（表面那颗点的实心/空心）。</param>
    /// <param name="Pending">正挂着这一轮没确认（表面把它标出来，出口仍是同一句 <c>ConfirmPending</c>）。</param>
    public sealed record AlarmFaceRow(long Id, LocMessage Text, bool Enabled, bool Pending);

    /// <summary>
    /// 时钟表面上那一排：<b>按钟点升序，无效的那条垫底</b>。
    /// <para>
    /// 无效（分钟数越界，多半是手改存档或旧版本留下的）不藏起来：<see cref="FormatMinute"/> 会把它写成"时间无效"，
    /// 于是表面上看得见"这一条不会响"。藏起来的后果是他以为自己那条还在，到第二天才发现没响。
    /// </para>
    /// <para>关掉的照旧占一行（灰着）：闹钟"关了"与"删了"是两件事，关掉的那条要留得下来才谈得上再开。</para>
    /// </summary>
    public static List<AlarmFaceRow> FaceRows(IEnumerable<AlarmItem> items, DateTimeOffset now)
    {
        var list = items.ToList();
        list.Sort(static (a, b) =>
        {
            // 先按"有效/无效"分组，再按钟点。用显式比较器而不是 OrderBy/ThenBy：
            // 这里要的是"无效一律垫底"这一条规则本身可读，不依赖 LINQ 的稳定排序假设。
            var rankA = HasValidMinute(a.MinuteOfDay) ? 0 : 1;
            var rankB = HasValidMinute(b.MinuteOfDay) ? 0 : 1;
            return rankA != rankB ? rankA.CompareTo(rankB) : a.MinuteOfDay.CompareTo(b.MinuteOfDay);
        });
        return list.Select(item => new AlarmFaceRow(item.Id, LineMessage(item), item.Enabled, IsPending(item, now))).ToList();
    }

    /// <summary>
    /// 那一排要不要重画。<b>只看定义与状态，一次都不看"现在是几点"</b>：
    /// 时钟每秒刷一整张表，签名里若掺进钟点或"还有多久"，表面就变成<b>每秒销毁重建一遍控件</b>——
    /// 那是把"每秒改一次文本"升级成"每秒一次布局重排"，而代价正好由最不该卡的地方（常驻桌面的小窗）付。
    /// 判"要不要重画"住在 Core，是为了让这条纪律有证人（<c>AlarmPolicyTests</c> 钉"时间走一秒不换签名"）。
    /// <para>批次 WI-37-C 起，行内那句是<b>还没取词的</b> <see cref="LocMessage"/>，所以签名取的是它的<b>形状</b>
    /// （键＋嵌套键＋数据实参，见 <see cref="ShapeOf"/>）。⛔ 拿 <c>Render()</c> 的结果当签名——
    /// 那等于"换一次语言就把他正盯着的那一排销毁重建一遍"，而这句话本身是这条判据要防的那一类。</para>
    /// </summary>
    public static string FaceSignature(IReadOnlyList<AlarmFaceRow> rows)
        => string.Join("|", rows.Select(static r => $"{r.Id}{(r.Enabled ? 'o' : 'x')}{(r.Pending ? '!' : '.')}{ShapeOf(r.Text)}"));

    /// <summary>
    /// 把"还没取词的那句"压成<b>与语言无关</b>的指纹：键与嵌套的键照原样进串，数据实参（钟点、用户起的名字、条数）进串。
    /// <para>⛔ 在这里调 <c>Render()</c> 或 <c>Locale.T</c>：那会让同一份定义在三档语言里有三种签名，
    /// 于是"换语言"混进"内容变了"这一判据里——常驻小窗上那几颗控件就会被无故重建。</para>
    /// </summary>
    private static string ShapeOf(LocMessage message)
    {
        if (message.Args is not { Length: > 0 } args) return message.Key;
        var parts = new List<string>(args.Length);
        foreach (var arg in args)
            parts.Add(arg switch { LocMessage nested => ShapeOf(nested), null => string.Empty, _ => arg.ToString() ?? string.Empty });
        return message.Key + "(" + string.Join(",", parts) + ")";
    }

    /// <summary>
    /// 解析"新建/修改"那一个输入框：<b>时间与名字写在同一行</b>（<c>7:30 起床</c>）。
    /// 拆成两个输入框会让"改名"和"改时间"变成两趟弹窗，而在小窗右键菜单里，多一步就是不做这一步。
    /// <para>钟点是<b>开头连续的数字与冒号</b>，剩下的一律算名字——所以"7:30 起床"与"7:30起床"都要认：
    /// 只按空格切会让后一种直接报错，而"我明明写了时间"却被告知格式不对，是最招烦的一种红。</para>
    /// <para>时刻<b>自己拆两位数字，不交给 <c>TimeSpan.TryParseExact</c></b>：那道闸把"24:00"当成格式不对，
    /// 于是"我写了 24 点"收到的回答是"请写成 7:30"——他照着改还是 24 点，白跑一趟。越界要单独说一句它越了哪一边界。</para>
    /// </summary>
    public static bool TryParseInput(string? text, out int minute, out string label, out LocMessage? error)
    {
        minute = 0;
        label = string.Empty;
        var input = (text ?? string.Empty).Trim();
        if (input.Length == 0)
        {
            error = LocMessage.Of("alarm.input.empty");
            return false;
        }

        var tail = 0;
        while (tail < input.Length && (char.IsAsciiDigit(input[tail]) || input[tail] == ':')) tail++;
        var head = input[..tail];
        const string ShapeKey = "alarm.input.shape";
        var bits = head.Split(':');
        if (bits.Length != 2
            || bits[0].Length > 2
            || !int.TryParse(bits[0], out var hour)
            || !int.TryParse(bits[1], out var plainMinute))
        {
            error = LocMessage.Of(ShapeKey);
            return false;
        }
        if (bits[1].Length != 2)
        {
            error = LocMessage.Of("alarm.input.minuteTwo");
            return false;
        }
        if (hour > 23)
        {
            error = LocMessage.Of("alarm.input.hourRange");
            return false;
        }
        if (plainMinute > 59)
        {
            error = LocMessage.Of("alarm.input.minuteRange");
            return false;
        }

        minute = hour * 60 + plainMinute;
        label = TrimLabel(input[tail..]);
        error = null;
        return true;
    }
}
