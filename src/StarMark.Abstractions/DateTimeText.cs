#nullable enable
using System;
using System.Globalization;

namespace StarMark.Abstractions;

/// <summary>
/// 给人看的日期/时间文本，<b>全应用唯一出处</b>（批次 RY，P-122）。
/// <para>
/// 为什么要收在这里：文件名那一路早就逐条锁了 <see cref="CultureInfo.InvariantCulture"/>
/// （<c>StarLog</c>／<c>BackupService</c>／<c>ClipAssets</c> 等 18 处，见 <c>CultureInvariantNamingTests</c>），
/// 理由写在 <c>th-TH</c>（佛历）与 <c>ar-SA</c>（希吉来历）下隐式 <c>yyyyMMdd</c> 会得到 <b>2569／1447</b> 这种年份。
/// 可<b>显示侧漏了</b>：本产物没开 <c>InvariantGlobalization</c>（<c>StarMark.UI.runtimeconfig.json</c> 里根本没有
/// <c>System.Globalization.Invariant</c> 这个键，<c>StarMark.UI.csproj</c> 里那条注释还写着"待 XAML 本地化验证后再开"），
/// ⇒ ICU 生效、隐式 <c>ToString(格式串)</c> 跟着<b>当前文化</b>走。于是佛历区的时钟会写"2569-09-29"，
/// 希吉来历区会写"1447-…"，阿语区还会把拉丁数字换成阿拉伯-印度数字——<b>用户在一个全中文界面里看见别的历法</b>。
/// </para>
/// <para>
/// 两类收口法是本批的判据核心：
/// ① <b>纯数字</b>日期/时刻 ⇒ 锁 <see cref="CultureInfo.InvariantCulture"/>（与文件名同口径；
/// 在 <c>zh-CN</c> 下输出的字面串与今天<b>逐字相同</b>，所以没动用户已经看惯的样子）；
/// ② <b>星期名</b>（旧代码用 <c>dddd</c> 取本地化全称）⇒ <b>不许</b>照搬"锁 InvariantCulture"，
/// 那会把"星期日"打成英文 <c>Sunday</c>——中文界面里那是另一种坏。改为走 <see cref="Weekday"/> 这张表
/// （它和 <see cref="WeekdayShort"/>、月历表头共用 <see cref="WeekdayStem"/> 一颗词根）。
/// </para>
/// <para>
/// <b>为什么不叫 <c>DateText</c></b>：时钟组件的 VM 已经有一颗叫 <c>DateText</c> 的显示属性
/// （<c>ClockWidgetViewModel</c> 的 <c>_dateText</c>）。类与它同名的话，那个类里的每个调用点都会解析到
/// <c>string</c> 属性而不是判据——编译直接报错还好，怕的是将来在某处恰好解析对、读起来又像在说属性。
/// </para>
/// <para>
/// <b>批次 SF 补的一刀</b>：RY 当时在这里写下"全称在这里只写一遍"，那句话<b>不成立</b>——
/// 星期名一共<b>六处</b>各有抄本（账本 P-128 只数到"短名三处"）：短名整表三处
/// （<c>GlanceCalendar.WeekdayText</c>／<c>AlarmPolicy.DaysLabel</c>／<c>WidgetWindow.Alarms.DayOrder</c>）、
/// "周＋切片"一处（<c>LocalItemState.DescribeDue</c>）、只印词根的表一处（<c>MonthGrid.WeekHeaders</c>）、
/// 全称又一处（UI 的 <c>GlanceWidget.WeekdayFull</c>）。多出来那两处的原因不是运气：
/// 测试工程不引用 <c>StarMark.UI</c>（#184），所以"数有几处"这一步从来没照过 UI 那一侧。
/// 现在词根只有 <see cref="WeekdayStem"/> 一张表，<see cref="Weekday"/>／<see cref="WeekdayShort"/>／
/// 月历表头都从它拼出来；<b>阅读顺序（周一在前）仍留在各宿主</b>，那是排列习惯、不是同一份真值。
/// </para>
/// </summary>
public static class DateTimeText
{
    /// <summary>只到日：<c>2026-09-29</c>。</summary>
    public static string Day(DateTime value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>只到日：<c>2026-09-29</c>。</summary>
    public static string Day(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>到分：<c>2026-09-29 14:05</c>。列表里"什么时候"一律这一颗（调用方负责先换成本地时间）。</summary>
    public static string Minute(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>到分：<c>2026-09-29 14:05</c>。</summary>
    public static string Minute(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// 钟点。<paramref name="withSeconds"/> 为真给 <c>14:05:07</c>（时钟与世界时钟要看见秒在走），
    /// 为假给 <c>14:05</c>（"下一次 15:35"这类预告不必到秒）。
    /// </summary>
    public static string Clock(DateTime value, bool withSeconds = false)
        => value.ToString(withSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);

    /// <summary>钟点，同 <see cref="Clock(DateTime,bool)"/>。</summary>
    public static string Clock(DateTimeOffset value, bool withSeconds = false)
        => value.ToString(withSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);

    /// <summary>跨天的预告：<c>9-29 14:05</c>（不带年——休息提醒只看最近那一次，带年反而像远期计划）。</summary>
    public static string MonthDayClock(DateTimeOffset value)
        => value.ToString("M-d HH:mm", CultureInfo.InvariantCulture);

    /// <summary>不带年的"月-日 时:分"：<c>09-29 14:05</c>。倒计时行与"下一次约"那一类文案用（旧写法是内插 <c>{x:MM-dd HH:mm}</c>）。</summary>
    public static string MonthDayMinute(DateTimeOffset value)
        => value.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// 备份档时间戳：<c>20260929-140507</c>。<b>这是文件名，不是文案</b>——
    /// 但形状只有一个主人：桌面里"自动备份/恢复前快照/手动导出"三处名字都走这一颗（批次 BK 与 R4 各自内联写的那两行已并进来）。
    /// </summary>
    public static string BackupStamp(DateTimeOffset value)
        => value.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    /// <summary>截图文件名那一串：<c>2026-09-29 140507</c>（本地墙上时间；同一秒连拍由调用方加 collisionIndex）。</summary>
    public static string CaptureStamp(DateTimeOffset value)
        => value.LocalDateTime.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);

    /// <summary>取证日志的 tick：<c>14:05:07.382</c>。毫秒要留着——界面卡顿的判据就是它（性能纪律：微秒级用 Stopwatch，这里只是打点）。</summary>
    public static string TickStamp(DateTime value)
        => value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>时钟那一行：<c>2026-09-29 星期日</c>。日期部分与 <see cref="Day(DateTime)"/> 同一口径。</summary>
    public static string DayWithWeekday(DateTime value) => $"{Day(value)} {Weekday(value.DayOfWeek)}";

    /// <summary>
    /// 时长读数：<c>5:07</c>，超过一小时才升到 <c>52:14:07</c>（番茄钟与音乐进度共用这一颗）。
    /// <para>冒号前的 <c>\</c> 是 <b>TimeSpan 格式串的转义</b>（<c>@"h\:mm\:ss"</c> 的写法），不是路径分隔符；
    /// 之所以仍然要过一遍不变文化：文化影响的是<b>数字与分隔符</b>——阿语区不加锁会把 <c>5:07</c> 打成阿拉伯-印度数字。</para>
    /// </summary>
    public static string Duration(TimeSpan value, bool withHours = false)
        => value.ToString(withHours ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// 中文<b>全称</b>星期：<c>星期日…星期六</c>。位序跟 <see cref="DayOfWeek"/> 走（Sunday＝0），
    /// 与 <c>AlarmPolicy.BitFor</c> 同一套位序口径——按"周一是第一天"的直觉排这张表就会整体错一天。
    /// </summary>
    public static string Weekday(DayOfWeek day) => "星期" + WeekdayStem(day);

    /// <summary>中文<b>短名</b>星期：<c>周日…周六</c>（组件里那些窄列用这一颗）。</summary>
    public static string WeekdayShort(DayOfWeek day) => "周" + WeekdayStem(day);

    /// <summary>
    /// 星期名里那个<b>词根</b>（<c>日 / 一 / 二 / 三 / 四 / 五 / 六</c>）——全称、短名、月历表头三副面孔共用的唯一一张表。
    /// <para>
    /// 越界（库里存着本二进制不认识的序号）落到 <c>日</c>：宁可错印成周日，也不许什么都不印——
    /// 空字符串在界面上表现为"那一格凭空消失"，比错一天更难报障。
    /// </para>
    /// <para>
    /// 表的次序就是 <c>(int)DayOfWeek</c>（Sunday＝0）。<c>MonthGrid.FirstSlot</c> 用同一个序号决定网格开头补几天，
    /// 所以"第 0 列是周日"不是排版习惯而是硬约束：这张表哪天被谁改成周一在前，月历就会行行错位。
    /// </para>
    /// </summary>
    public static string WeekdayStem(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => "日",
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        _ => "日",
    };

    // ==================== 「多久以前」那四档人话 ====================

    /// <summary>
    /// 「多久以前」这一族话里<b>措辞</b>的唯一出处（批次 SJ，P-131 清单 #5）。
    /// <para>登记时写的是"两份实现"（卡片与活动列表各一份 if 链，措辞逐字相同）。按<b>后果</b>重扫＝
    /// 四份 if 链（另加热榜缓存年龄、RSS 源状态那一句"上次抓取 …"），而<b>没有任何一条用例钉过其中任何一份</b>。
    /// 两份逐字相同的那对已经真分岔出一天：活动列表把 <c>FromUnixTimeSeconds</c> 得到的<b>零偏移</b>直接交
    /// <see cref="Day(DateTimeOffset)"/>，卡片交的是 <c>.LocalDateTime</c> ⇒ 本地 00:00–08:00 那段里，
    /// 同一个超过 30 天的时间在两处差一天（<see cref="Relative"/> 把这一条收在本地那一侧）。</para>
    /// <para><b>并的是措辞，不是档位</b>："多旧还算刚刚"（60 秒还是 2 分钟）、"超过一天要不要落回日期"
    /// 都是宿主自己的判断，留在那一句的调用点；这里只保证"分钟／小时／天"这三档<b>怎么写、怎么取整</b>
    /// 只有一份。取整一律<b>向下截断</b>：热榜那份原本走 <c>NumberText.Grouped(double)</c>（"N0"＝四舍五入），
    /// 于是 3 小时 59 分在那儿写"4 小时前"、在 RSS 那儿写"3 小时前"——同一句年龄两处差一小时（本批统一成截断）。</para>
    /// </summary>
    public const string JustNow = "刚刚";

    /// <summary>「N 分钟前」这一档（<paramref name="count"/> 由调用点向下截断后交出）。</summary>
    public static string MinutesAgo(long count) => $"{count} 分钟前";

    /// <summary>「N 小时前」这一档。</summary>
    public static string HoursAgo(long count) => $"{count} 小时前";

    /// <summary>「N 天前」这一档。</summary>
    public static string DaysAgo(long count) => $"{count} 天前";

    /// <summary>
    /// 一个本机时刻（Unix 秒）离 <paramref name="now"/> 多久：<b>刚刚／分钟前／小时前／天前</b>四档，
    /// 超过 30 天落回 <see cref="Day(DateTimeOffset)"/> 那个日期（<b>按 <paramref name="zone"/> 换算</b>）。
    /// <para>档位写死在这里：60 秒／60 分／24 小时／30 天。比这更宽的那两处（"2 分钟内都算刚刚"）
    /// 用的是上面三颗档 builder，不是这一颗——差别是宿主的判断，不是两份真值。</para>
    /// <para>时刻在 <paramref name="now"/> 之后（机器时钟被改过、或库里存了未来值）落进"刚刚"那一档：
    /// 负数不印，也不为此猜一个数。日期兜底要把时区交进来——同量级的事实在 <c>TrendingCacheCodec</c> 的
    /// "跨日"判据里同样靠偏移算，不靠这台机器的默认设置，用例才能钉死跨日那一格。</para>
    /// </summary>
    public static string Relative(long unixSeconds, DateTimeOffset now, TimeZoneInfo zone)
    {
        var diff = now.ToUnixTimeSeconds() - unixSeconds;
        if (diff < 60) return JustNow;
        if (diff < 3_600) return MinutesAgo(diff / 60);
        if (diff < 86_400) return HoursAgo(diff / 3_600);
        if (diff < AppConstants.ThirtyDaysInSeconds) return DaysAgo(diff / 86_400);
        return Day(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), zone));
    }
}
