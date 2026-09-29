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
/// 那会把"星期日"打成英文 <c>Sunday</c>——中文界面里那是另一种坏。改为走 <see cref="Weekday"/> 这张表，
/// 全称在这里只写一遍。
/// </para>
/// <para>
/// <b>为什么不叫 <c>DateText</c></b>：时钟组件的 VM 已经有一颗叫 <c>DateText</c> 的显示属性
/// （<c>ClockWidgetViewModel</c> 的 <c>_dateText</c>）。类与它同名的话，那个类里的每个调用点都会解析到
/// <c>string</c> 属性而不是判据——编译直接报错还好，怕的是将来在某处恰好解析对、读起来又像在说属性。
/// </para>
/// <para>
/// 星期<b>短名</b>（"周日…"）今天另有三处出处（<c>GlanceCalendar.WeekdayText</c>／<c>AlarmPolicy</c> 的标签表／
/// <c>LocalItemState</c> 的"周+切片"），本批<b>没有</b>并进来：它们各有逐字钉住的用例，动它是另一批的活。
/// 已登记为新挂账，免得下一次往这里加第四份。
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
    /// 中文<b>全称</b>星期，<b>唯一出处</b>。位序跟 <see cref="DayOfWeek"/> 走（Sunday＝0），
    /// 与 <c>AlarmPolicy.BitFor</c> 同一套位序口径——按"周一是第一天"的直觉排这张表就会整体错一天。
    /// </summary>
    public static string Weekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => "星期日",
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日",   // 越界（库里存着本二进制不认识的序号）不许什么都不印
    };
}
