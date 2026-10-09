#nullable enable
using System;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 今日速览（Glance）的农历 / 节日计算测试。
/// 农历底层是 .NET 内置 <see cref="System.Globalization.ChineseLunisolarCalendar"/>，
/// 闰月处理容易差一位，故用若干「已知阳历↔农历」的锚点日钉死，防止算法回归。
/// <para>
/// 批次 WI-56（P-186）起，<b>节日那颗 API 交出的是键而⛔ 名字</b>——所以这里钉的是
/// <c>glance.festival.*</c> 那颗键＋日期，⛔「春节」这种表里的措辞（钉措辞＝把"只改说法"判成缺陷，
/// 规则 #130；而这一族连"名字"都不再由 Core 交出，Core 交键是它的契约）。
/// 三张清单（中／美／日）各钉一格：只钉中文档＝另两档排错了也没人知道。
/// </para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class GlanceCalendarTests : IDisposable
{
    private readonly AppLanguage _before = Locale.Current;

    public void Dispose() => Locale.TrySet(_before);

    [Theory]
    // 2026 春节：正月初一 = 2026-02-17
    [InlineData(2026, 2, 17, "正月初一")]
    // 2025 春节：正月初一 = 2025-01-29
    [InlineData(2025, 1, 29, "正月初一")]
    // 2024 春节：正月初一 = 2024-02-10
    [InlineData(2024, 2, 10, "正月初一")]
    public void LunarText_SpringFestival_IsFirstDayOfFirstMonth(int y, int m, int d, string expected)
        => Assert.Equal(expected, GlanceCalendar.LunarText(new DateOnly(y, m, d)));

    [Theory]
    [InlineData(2026, 2, 17, "glance.festival.lunarNewYear")]
    [InlineData(2025, 1, 29, "glance.festival.lunarNewYear")]
    [InlineData(2026, 10, 1, "glance.festival.nationalDay")]
    [InlineData(2026, 1, 1, "glance.festival.newYear")]
    [InlineData(2026, 12, 25, "glance.festival.christmas")]
    public void Festival_KnownDates(int y, int m, int d, string expectedKey)
    {
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal(expectedKey, GlanceCalendar.FestivalMessage(new DateOnly(y, m, d))?.Key);
    }

    /// <summary>同一颗概念在三档清单里各落在哪一天：<b>名字一样不代表日期一样</b>（儿童节＝中国 6/1、日本 5/5），
    /// 而"另一档念什么"由表里那一行负责——这里钉的是"排在哪天"。</summary>
    [Theory]
    [InlineData(AppLanguage.Chinese, 2026, 6, 1, "glance.festival.childrensDay")]
    [InlineData(AppLanguage.Japanese, 2026, 5, 5, "glance.festival.childrensDay")]
    [InlineData(AppLanguage.Chinese, 2026, 8, 19, "glance.festival.qixi")]   // 农历七月初七
    [InlineData(AppLanguage.Japanese, 2026, 7, 7, "glance.festival.qixi")]    // 公历七月七日
    [InlineData(AppLanguage.English, 2026, 9, 7, "glance.festival.labourDay")]  // 九月第一个周一
    [InlineData(AppLanguage.Chinese, 2026, 5, 1, "glance.festival.labourDay")]  // 五一
    public void SameConceptLandsOnEachCulturesOwnDay(AppLanguage language, int y, int m, int d, string expectedKey)
    {
        Locale.TrySet(language);
        Assert.Equal(expectedKey, GlanceCalendar.FestivalMessage(new DateOnly(y, m, d))?.Key);
    }

    /// <summary>"第几个星期几"与"最后一个星期几"那两类：钉的是<b>某一年的真日历</b>＋该格形状自证，
    /// ⛔ 在测试里再实现一遍规则（那样实现与判据会一起算错）。</summary>
    [Theory]
    [InlineData(AppLanguage.English, 2026, 1, 19, "glance.festival.mlkDay", DayOfWeek.Monday, 3)]
    [InlineData(AppLanguage.English, 2026, 2, 16, "glance.festival.presidentsDay", DayOfWeek.Monday, 3)]
    [InlineData(AppLanguage.English, 2026, 5, 25, "glance.festival.memorialDay", DayOfWeek.Monday, 0)]  // 0＝最后一个
    [InlineData(AppLanguage.English, 2026, 5, 10, "glance.festival.mothersDay", DayOfWeek.Sunday, 2)]
    [InlineData(AppLanguage.English, 2026, 11, 26, "glance.festival.thanksgiving", DayOfWeek.Thursday, 4)]
    [InlineData(AppLanguage.Japanese, 2026, 1, 12, "glance.festival.comingOfAgeDay", DayOfWeek.Monday, 2)]
    [InlineData(AppLanguage.Japanese, 2026, 7, 20, "glance.festival.marineDay", DayOfWeek.Monday, 3)]
    [InlineData(AppLanguage.Japanese, 2026, 9, 21, "glance.festival.respectForAgedDay", DayOfWeek.Monday, 3)]
    public void MovableHolidays_HitTheAnchorAndItsOwnShapeOnly(
        AppLanguage language, int y, int m, int d, string expectedKey, DayOfWeek weekday, int occurrence)
    {
        var date = new DateOnly(y, m, d);
        // 用例自己的形状先自证（锚点写错就该红在这里，而不是被读成"实现错了"）
        Assert.Equal(weekday, date.DayOfWeek);
        Assert.Equal(m, date.Month);
        if (occurrence > 0) Assert.Equal(occurrence, (date.Day - 1) / 7 + 1);
        else Assert.True(date.Day > DateTime.DaysInMonth(y, m) - 7, "这一格钉的是最后一个，日期必须在最后七天里");

        Locale.TrySet(language);
        Assert.Equal(expectedKey, GlanceCalendar.FestivalMessage(date)?.Key);

        // 同一个月里不许有第二格也念这颗键（"第 3 个周一"写成"每个周一"就是这一格抓的）
        var hits = 0;
        for (var day = 1; day <= DateTime.DaysInMonth(y, m); day++)
            if (GlanceCalendar.FestivalMessage(new DateOnly(y, m, day))?.Key == expectedKey) hits++;
        Assert.Equal(1, hits);
    }

    [Fact]
    public void Festival_Qingming_IsInApril()
    {
        Locale.TrySet(AppLanguage.Chinese);
        // 清明按寿星公式浮动在 4/4~4/6，只能落在 4 月且必须是该年算出的那一天。
        for (var year = 2024; year <= 2030; year++)
        {
            var hit = -1;
            for (var day = 1; day <= 30; day++)
            {
                if (GlanceCalendar.FestivalMessage(new DateOnly(year, 4, day))?.Key == "glance.festival.qingming") hit = day;
            }
            Assert.InRange(hit, 4, 6);
        }
    }

    [Fact]
    public void Festival_OrdinaryDay_IsNull()
    {
        Locale.TrySet(AppLanguage.Chinese);
        // 2026-03-10（农历正月廿二）：既非农历节日、也非公历节日、更不在清明所在的 4 月。
        // 注意别挑 3/3——那天是正月十五元宵节。
        Assert.Null(GlanceCalendar.FestivalMessage(new DateOnly(2026, 3, 10)));
    }

    /// <summary>英文档里挑一天三档都⛔ 节日的（2026-03-17：美国那档没有这一项，中国那档也没有）——
    /// "换档就读到东西了"这种假命中要能被读出来（月历上的圆点就是这个判据的屏幕形状）。</summary>
    [Fact]
    public void EnglishList_DoesNotCarryTheChineseOnlyFestivals()
    {
        Locale.TrySet(AppLanguage.English);
        Assert.Null(GlanceCalendar.FestivalMessage(new DateOnly(2026, 10, 1)));   // 中国国庆节⛔ 是美国的
        Assert.Null(GlanceCalendar.FestivalMessage(new DateOnly(2026, 3, 17)));
        Assert.Equal("glance.festival.newYear", GlanceCalendar.FestivalMessage(new DateOnly(2026, 1, 1))?.Key);
    }

    [Fact]
    public void NextFestival_ReturnsNonPastAndMatchingDay()
    {
        Locale.TrySet(AppLanguage.Chinese);
        var from = new DateOnly(2026, 3, 3);
        var next = GlanceCalendar.NextFestival(from);

        Assert.NotNull(next);
        Assert.True(next!.Days >= 0, "倒计时不能为负");
        Assert.Equal(next.Days, next.Date.DayNumber - from.DayNumber);
        Assert.Equal(next.Name.Key, GlanceCalendar.FestivalMessage(next.Date)?.Key);
    }

    [Fact]
    public void NextFestival_OnFestivalDay_IsSameDay()
    {
        Locale.TrySet(AppLanguage.Chinese);
        var newYear = new DateOnly(2026, 1, 1);
        var next = GlanceCalendar.NextFestival(newYear);

        Assert.NotNull(next);
        Assert.Equal(0, next!.Days);
        Assert.Equal("glance.festival.newYear", next.Name.Key);
    }

    [Fact]
    public void LunarText_OutOfCalendarRange_ReturnsEmpty()
    {
        Locale.TrySet(AppLanguage.Chinese);
        // ChineseLunisolarCalendar 只覆盖约 1901~2100，越界必须静默兜底而不是抛异常
        Assert.Equal(string.Empty, GlanceCalendar.LunarText(new DateOnly(1800, 1, 1)));
        Assert.Null(GlanceCalendar.FestivalMessage(new DateOnly(1800, 1, 1)));
    }

    /// <summary>
    /// 批次 SF 之前这里钉的是 <c>GlanceCalendar.WeekdayText</c>——那颗<b>没有任何读者</b>（界面走的是
    /// <c>GlanceWidget</c> 自己的全称表），是一张没人看的抄本，已删。星期名的逐字覆盖面搬去
    /// <see cref="DateTimeTextTests"/>（判据七格全称+七格短名）与 <see cref="WeekdayNameTests"/>（三个真读者）。
    /// </summary>
    [Fact]
    public void GlanceCalendarHoldsNoWeekdayTableAnymore()
    {
        var code = FormatScanner.Scan(SourceGate.ReadRepoFile("src/StarMark.Core/Widgets/GlanceCalendar.cs")).Code;
        Assert.DoesNotContain("WeekdayText", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 回归 AT：本类契约「所有入口绝不把异常冒到 UI 线程」。NextFestival 的 AddDays(i) 在 from
    /// 逼近 DateOnly.MaxValue 时抛 ArgumentOutOfRangeException（旧实现未钳窗口）。修后须返回 null 而非抛。
    /// </summary>
    [Fact]
    public void NextFestival_NearDateOnlyMaxValue_ReturnsNullNotThrow()
    {
        Assert.Null(GlanceCalendar.NextFestival(DateOnly.MaxValue));
        Assert.Null(GlanceCalendar.NextFestival(new DateOnly(9999, 12, 30), 5));
    }

    /// <summary>
    /// 契约护栏（DC）：除夕按「次日即正月初一」反推，刻意<b>不</b>硬编码「腊月三十」——
    /// 因农历十二月有 29 天（无年三十）与 30 天两种，硬编码会在 29 天年份<b>静默漏掉除夕</b>。
    /// 该分支此前无测（<see cref="Festival_KnownDates"/> 仅覆盖春节/公历节日）。以本文件已钉死的三个春节
    /// （正月初一）锚点日各减一天即除夕，跨年验证反推恒命中，且再前一天不得是除夕（排除把窗口算宽）；
    /// 春节锚点自洽复核。⚠ 除夕这一颗<b>只存在于中文档</b>（那档才排了农历规则），所以整颗 Theory 钉死语言。
    /// </summary>
    [Theory]
    [InlineData(2026, 2, 17)] // 正月初一 → 除夕 02-16
    [InlineData(2025, 1, 29)] // 正月初一 → 除夕 01-28
    [InlineData(2024, 2, 10)] // 正月初一 → 除夕 02-09
    public void Festival_Chuxi_IsDerivedFromDayBeforeSpringFestival(int y, int m, int d)
    {
        Locale.TrySet(AppLanguage.Chinese);
        var springFestival = new DateOnly(y, m, d);
        var chuxi = springFestival.AddDays(-1);

        Assert.Equal("glance.festival.chuxi", GlanceCalendar.FestivalMessage(chuxi)?.Key);     // 次日即正月初一 → 命中
        Assert.NotEqual("glance.festival.chuxi", GlanceCalendar.FestivalMessage(chuxi.AddDays(-1))?.Key);
        Assert.Equal("glance.festival.lunarNewYear", GlanceCalendar.FestivalMessage(springFestival)?.Key);
    }

    /// <summary>
    /// 批次 DH：承 DB→DG 透镜（审到<b>分支进入 + 输出值</b>粒度）。本类文件头注释白纸黑字称
    /// 「闰月处理容易差一位，故用若干已知阳历↔农历锚点日钉死，防止算法回归」——但当时的全部锚点
    /// <b>都在任何闰月之前</b>，于是 <c>GetChineseDate</c> 的闰月判据 <c>isLeap</c> 与「闰月当月及其后月份号
    /// <c>-1</c>」的调整、以及 <c>LunarText</c> 的「闰」前缀此前<b>从未被任何用例进入</b>。
    /// 回归面正是注释担心的那一类且<b>静默</b>：把 <c>calendarMonth &gt;= leapMonth</c> 误写成 <c>&gt;</c>
    /// （闰月当月漏 <c>-1</c>）就会把闰二月初一画成「闰三月」；整个 <c>-1</c> 调整被删则闰月之后
    /// <b>每个月份号整体 +1</b>、农历文本逐月错位——现有测全绿无感。
    /// 用经 .NET <c>ChineseLunisolarCalendar</c> 与权威历法事实核对的<b>两个不同闰月年</b>（2023 闰二月 lm=3 /
    /// 2025 闰六月 lm=7）钉死三段语义：闰月前（cm&lt;lm 不调整）、闰月当月（cm==lm 触发 isLeap + <c>-1</c>）、
    /// 闰月次月（cm&gt;lm 仍 <c>-1</c>）；取不同 lm 值证明该调整不依赖闰月的具体位置。真值经一次性探针实测、非手算。
    /// </summary>
    [Theory]
    [InlineData(2023, 3, 21, "二月三十")]    // cm=2 < lm=3：闰月前，月号不调整
    [InlineData(2023, 3, 22, "闰二月初一")]  // cm=3 == lm：isLeap=true 且 3-1=2 →「闰二月」
    [InlineData(2023, 4, 19, "闰二月廿九")]  // 闰月末（day=29→「廿九」，同时校验闰月内的日名派生）
    [InlineData(2023, 4, 20, "三月初一")]    // cm=4 > lm=3：4-1=3 → 次月正确续号
    [InlineData(2025, 7, 24, "六月三十")]    // cm=6 < lm=7：另一闰月位置前，不调整
    [InlineData(2025, 7, 25, "闰六月初一")]  // cm=7 == lm：isLeap + 7-1=6 →「闰六月」
    [InlineData(2025, 8, 23, "七月初一")]    // cm=8 > lm=7：8-1=7 → 次月正确续号
    public void LunarText_LeapMonth_RenderingAndMonthNumbering(int y, int m, int d, string expected)
        => Assert.Equal(expected, GlanceCalendar.LunarText(new DateOnly(y, m, d)));

    /// <summary>
    /// 农历节日一律要求<b>非闰月</b>（批次 DH 的另一段语义，WI-56 起它是一条规则的形状）：
    /// 2025 闰六月初一⛔ 是端午、也⛔ 是任何别的农历节日——闰月那一格必须读成"没节日"。
    /// </summary>
    [Fact]
    public void LeapMonth_NeverCountsAsALunarFestival()
    {
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal("glance.festival.duanwu", GlanceCalendar.FestivalMessage(new DateOnly(2025, 5, 31))?.Key); // 五月初一
        Assert.Null(GlanceCalendar.FestivalMessage(new DateOnly(2025, 6, 29)));                                  // 閏六月初一（同日号、闰月）
    }

    /// <summary>
    /// 批次 EK（潜在缺陷修复 + 回归）：本类契约「越界日期一律兜底、绝不冒异常」——测试
    /// <see cref="LunarText_OutOfCalendarRange_ReturnsEmpty"/> 已断言 <c>Festival(1800-01-01)==null</c>。
    /// 旧实现把清明判定放在 <c>GetChineseDate</c> <b>之前</b>：清明是纯公历寿星公式、不读农历、不抛越界异常，
    /// 于是「范围外(约 1901~2100)又恰撞清明公式那天」的 4 月日期会漏过越界闸门、错误返回那颗键。
    /// 反证：范围内清明照旧命中，闸门未矫枉过正。
    /// ⚠ 批次 WI-56 把这条闸门写成<b>分档</b>的：只有排了农历规则的中文档越界一律 null（就是这一颗），
    /// 英／日两档没有农历规则，也就没有那把闸门（同一颗⛔ 在两档里被读成两种结果）。
    /// </summary>
    [Fact]
    public void Festival_QingmingFormulaDateOutsideCalendarRange_IsNullLikeOtherFestivals()
    {
        Locale.TrySet(AppLanguage.Chinese);
        foreach (var year in new[] { 1800, 2200 })   // 1800 低于范围下界、2200 高于上界
        {
            for (var day = 1; day <= 30; day++)
            {
                var d = new DateOnly(year, 4, day);
                Assert.Equal(string.Empty, GlanceCalendar.LunarText(d));                     // 越界：农历文本空（既有契约）
                Assert.Null(GlanceCalendar.FestivalMessage(d));                              // 越界：节日须一律 null（清明不再漏网）
            }
        }
        Assert.Equal("glance.festival.qingming", GlanceCalendar.FestivalMessage(new DateOnly(2026, 4, 5))?.Key);
    }

    /// <summary>
    /// 三档清单都不许是空的（0 颗＝那一档的节日行与月历圆点会静默消失，界面上"看不出漏了什么"）。
    /// 颗数只当下读数、⛔ 当判据——判据是"非空"，写死的数字改一次就要修一次。
    /// </summary>
    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    [InlineData(AppLanguage.Japanese)]
    public void EveryLanguageListIsNonEmpty(AppLanguage language)
        => Assert.True(GlanceCalendar.FestivalRuleCount(language) > 0, $"{language} 那一档一颗节日都没排");
}
