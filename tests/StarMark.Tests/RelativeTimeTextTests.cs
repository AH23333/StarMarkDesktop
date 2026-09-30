#nullable enable
using System;
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「多久以前」那四档的逐值契约（批次 SJ，P-131 清单 #5）。
/// <para>并档之前这四档散在四个宿主里，而且<b>没有一条用例钉过任何一份</b>——所以两份逐字相同的实现
/// 分岔出"活动列表差一天"时，全绿一路放行（与 #179/#194 同族：没人钉的那格就是坏的那格）。</para>
/// <para>时区是参数不是环境：<see cref="TimeZoneInfo.CreateCustomTimeZone(string, TimeSpan, string, string)"/>
/// 造一个固定 +8／−5 的钟面，跨日那一格才能被钉死（用 <c>TimeZoneInfo.Local</c> 写断言＝把结论交给跑测试的这台机器）。</para>
/// </summary>
public sealed class RelativeTimeTextTests
{
    private static readonly TimeZoneInfo Plus8 =
        TimeZoneInfo.CreateCustomTimeZone("测试 +8", TimeSpan.FromHours(8), "+8", "+8");
    private static readonly TimeZoneInfo Minus5 =
        TimeZoneInfo.CreateCustomTimeZone("测试 -5", TimeSpan.FromHours(-5), "-5", "-5");

    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static long SecondsBeforeNow(long delta) => Now.ToUnixTimeSeconds() - delta;

    // ────────── 四档的边界：每一档的两端都要量 ──────────

    [Theory]
    [InlineData(0, "刚刚")]
    [InlineData(59, "刚刚")]
    [InlineData(60, "1 分钟前")]
    [InlineData(119, "1 分钟前")]
    [InlineData(3_600 - 1, "59 分钟前")]
    [InlineData(3_600, "1 小时前")]
    [InlineData(86_400 - 1, "23 小时前")]
    [InlineData(86_400, "1 天前")]
    [InlineData(2 * 86_400 + 3_600, "2 天前")]          // 取整＝截断，不是四舍五入
    [InlineData(AppConstants.ThirtyDaysInSeconds - 1, "29 天前")]
    public void TheFourBucketsBreakAtTheStatedBoundaries(long ageSeconds, string expected)
        => Assert.Equal(expected, DateTimeText.Relative(SecondsBeforeNow(ageSeconds), Now, Plus8));

    /// <summary>未来时刻（机器时钟被改过、库里存了个偏后的时间）落进"刚刚"，绝不印出负数。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3_600)]
    [InlineData(999_999)]
    public void AFutureTimestampNeverPrintsANegativeAge(long secondsInFuture)
        => Assert.Equal("刚刚", DateTimeText.Relative(SecondsBeforeNow(-secondsInFuture), Now, Plus8));

    // ────────── 超过 30 天落回日期：这一格就是本批修掉的那条缺陷 ──────────

    /// <summary>
    /// 跨日那一格：<b>1 月 5 日 17:00 UTC</b> 在 +8 下是 1 月 6 日、在 −5 下还是 1 月 5 日。
    /// <para>并档之前活动列表走的是"零偏移那份"（＝UTC 读数），卡片走本地 ⇒ 本地 00:00–08:00 那八个小时里
    /// 同一个超过 30 天的时间在两处差一天。这里用固定钟面把两边都钉住，跑在哪台机器上都算同一句。</para>
    /// </summary>
    [Fact]
    public void TheDateFallbackFollowsTheZoneThatWasPassedIn()
    {
        var at = new DateTimeOffset(2026, 1, 5, 17, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.Equal("2026-01-06", DateTimeText.Relative(at, Now, Plus8));
        Assert.Equal("2026-01-05", DateTimeText.Relative(at, Now, Minus5));
    }

    /// <summary>30 天整那一刻就换成日期（不再写"30 天前"）——阈值取自 <see cref="AppConstants.ThirtyDaysInSeconds"/>。</summary>
    [Fact]
    public void ExactlyThirtyDaysOldSwitchesToTheDate()
    {
        var readout = DateTimeText.Relative(SecondsBeforeNow(AppConstants.ThirtyDaysInSeconds), Now, Plus8);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", readout);
    }

    // ────────── 三颗档 builder：措辞与取整的落点 ──────────

    [Theory]
    [InlineData(1, "1 分钟前")]
    [InlineData(59, "59 分钟前")]
    public void MinutesAgoIsWrittenExactlyOnce(int count, string expected)
        => Assert.Equal(expected, DateTimeText.MinutesAgo(count));

    [Theory]
    [InlineData(3, "3 小时前")]
    [InlineData(23, "23 小时前")]
    public void HoursAgoIsWrittenExactlyOnce(int count, string expected)
        => Assert.Equal(expected, DateTimeText.HoursAgo(count));

    [Theory]
    [InlineData(1, "1 天前")]
    [InlineData(29, "29 天前")]
    public void DaysAgoIsWrittenExactlyOnce(int count, string expected)
        => Assert.Equal(expected, DateTimeText.DaysAgo(count));

    /// <summary>
     /// 年龄数字<b>不加千分位</b>：这一族要的是"多久以前"，不是可数的金额。
     /// <para>与 <see cref="NumberText.Grouped(long)"/> 刻意相反，所以钉成断言——
     /// 谁将来"顺手统一"给它们套上分组，会在这里红，而不是在屏幕上冒出一个 <c>1,234 天前</c>。</para>
     /// </summary>
    [Fact]
    public void AgesArePlainDigitsWithoutGroupSeparators()
        => Assert.Equal("1234 天前", DateTimeText.DaysAgo(1_234));

    [Fact]
    public void JustNowIsTheTwoCharacterWord() => Assert.Equal("刚刚", DateTimeText.JustNow);
}
