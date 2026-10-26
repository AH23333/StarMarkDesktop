#nullable enable
using System;
using System.Globalization;
using StarMark.Abstractions.I18n;

namespace StarMark.Core.Widgets;

/// <summary>
/// 「今日速览（Glance）」组件的日历计算：农历、节日、下一个节日倒计时。
/// <para>
/// 纯逻辑、不依赖 WinUI，便于单测。农历与清明节算法照搬 DeskBox 的
/// <c>Services/GlanceFestivalService.cs</c>——底层直接用 .NET 内置的
/// <see cref="ChineseLunisolarCalendar"/>，因此<b>不需要自建闰月表 / 节气表</b>。
/// </para>
/// <para>
/// <b>批次 WI-56（P-186）改的是"节日按语言各排一张清单"</b>（2026-10-09 用户原话：
/// 「只放公历，或者按照美国/日本的日历和传统节日编排」，取第二条）。这一族从此分两层：
/// <b>名字住在表里</b>（一颗键＝一个概念，三列都是那一概念在该语言里的正确写法），
/// <b>哪天算节日住在下面这三张清单里</b>（各文化自己排，⛔ 一张表配三种翻法）。
/// 本类交出的永远是那颗<b>键</b>（<see cref="LocMessage"/>），⛔ 取好的词——取词发生在界面读它的那一刻。
/// </para>
/// <para>
/// "第几个星期几"那一类（美国 Memorial Day／日本 成人の日）是<b>规则</b>，⛔ 抄某一年的日历。
/// ⚠ 反过来，日本的春分／秋分<b>就是</b>抄那一年日历的那一类：那两天的日号由天文时刻定（1948 年那部法律
/// 写的是「春分の日／秋分の日」而不是一颗日期），所以它们走 <see cref="JapanEquinoxDays"/> 那张官制年表，
/// 年表覆盖不到的年份一律不出——写错了屏幕上就是"距某节日还差一天"的假话（口径见账本 P-189）。
/// <b>節分仍欠</b>：它＝「立春前一日」，而本仓拿不到立春那颗的第二处读数（寿星公式的立春系数连 2020・2024
/// 这几年自己就读错），故⛔ 排・⛔ 用裸公式顶上。
/// </para>
/// </summary>
public static class GlanceCalendar
{
    /// <summary>
    /// 「农历那一行」（"八月廿六"这类农历日序）是否在界面上出现。<b>只有中文档出现</b>：
    /// 农历日序是中文文化的成品词，en/ja 没有能一一对上的口径。
    /// <para>⚠ 这一颗管的是<b>农历日序那一行</b>，⛔ 整个农历区——批次 WI-31 时它是整区折叠，
    /// P-186 落地后改成"节日与倒计时三语都有、各念自己那张清单"（节日那一行由
    /// <see cref="FestivalRuleCount"/> 决定，三档都非空）。</para>
    /// </summary>
    public static bool ShowsLunarDayText(AppLanguage language) => language == AppLanguage.Chinese;

    /// <summary>下一个节日的倒计时信息。</summary>
    /// <param name="Name">节日<b>那颗键</b>（还没渲染的那句名字）。⛔ 念出来的名字：
    /// 名字一旦被当身份存走，换语言后屏幕上就是旧语言那句
    /// （✅WI-46c／WI-49／WI-54／WI-55 同一条界线）。</param>
    /// <param name="Date">节日日期。</param>
    /// <param name="Days">距今天数（0 = 就是今天）。</param>
    public sealed record FestivalCountdown(LocMessage Name, DateOnly Date, int Days);

    // ChineseLunisolarCalendar 只覆盖约 1901-02-19 ~ 2100-01-29，超出会抛
    // ArgumentOutOfRangeException——本类的契约是"所有入口绝不把异常冒到 UI 线程"。
    // ⚠ 由此产生一条分档口径：<b>排了农历规则的那一档</b>（中文）越界一律返回 null（批次 EK 立的契约，
    // 连公历节日与清明也一起兜住）；<b>没排农历规则的档</b>（英文／日文）没有那把闸门——元旦在 1800 年还是元旦。
    // 这是清单形状的必然结果，界面侧读不到差别。
    private static readonly ChineseLunisolarCalendar Calendar = new();

    /// <summary>
    /// 农历文本（如「八月初五」「闰四月初一」）；不支持的日期返回空串。
    /// <para>⚠ 这颗交出的是<b>中文成品词</b>：农历日序是数据（与「正／二／…／腊」那张月名表一起），
    /// 只在 <see cref="ShowsLunarDayText"/> 为真的那一档被念到，因此⛔ 进表（同 <c>LocOption.SelfName</c>
    /// 那一族的口径：本来就不该翻的字）。「农历」那三个字本身住表里（<c>widget.glance.lunarDay</c>）。</para>
    /// </summary>
    public static string LunarText(DateOnly date)
    {
        try
        {
            var (month, day, isLeap) = GetChineseDate(date);
            if (month <= 0 || day <= 0) return string.Empty;
            var monthName = month is >= 1 and <= 12 ? LunarMonthNames[month] : month.ToString();
            return (isLeap ? "闰" : string.Empty) + monthName + "月" + LunarDayName(day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 该日期的节日（农历节日／公历固定节日／第几个星期几／清明）；无节日返回 null。
    /// <para>同一天撞多个节日时<b>按清单里的先后</b>取第一个——顺序就是优先级，改顺序＝改屏幕上念哪一个。</para>
    /// </summary>
    public static LocMessage? FestivalMessage(DateOnly date)
    {
        var rules = RulesOf(Locale.Current);
        try
        {
            // ⚠ 先求农历（只有排了农历规则的那一档才求）：越界日期在这里抛、由方法末尾 catch 统一兜底 null。
            // 清明虽属纯公历寿星公式、不读农历、不触发此抛，但中文档里它必须与其它节日同受越界闸门约束——
            // 否则受支持范围外(约 1901~2100)又恰撞清明公式那天的 4 月日期会漏网返回那颗键（批次 EK 立的契约）。
            var needLunar = UsesChineseCalendar(rules);
            var lunar = (Month: 0, Day: 0, IsLeap: false);
            var nextLunar = lunar;
            if (needLunar)
            {
                lunar = GetChineseDate(date);
                nextLunar = GetChineseDate(date.AddDays(1));
            }

            foreach (var rule in rules)
                if (Matches(rule, date, lunar, nextLunar)) return LocMessage.Of(rule.Key);

            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// 从 <paramref name="from"/> 起（含当天）最近的下一个节日；400 天内找不到返回 null。
    /// </summary>
    public static FestivalCountdown? NextFestival(DateOnly from, int maxDays = 400)
    {
        // 兜底本类「所有入口绝不把异常冒到 UI 线程」的契约：from 逼近 DateOnly.MaxValue 时 AddDays(i) 会抛
        // ArgumentOutOfRangeException，故把窗口钳到距上限的剩余天数（负 maxDays 仍 0 次迭代返回 null）。
        maxDays = Math.Min(maxDays, DateOnly.MaxValue.DayNumber - from.DayNumber);
        for (var i = 0; i <= maxDays; i++)
        {
            var date = from.AddDays(i);
            var name = FestivalMessage(date);
            if (name is not null) return new FestivalCountdown(name, date, i);
        }
        return null;
    }

    /// <summary>这一档排了几颗节日规则（0＝那一档的节日行会是空的；界面不许靠"看不出来"发现漏排）。</summary>
    public static int FestivalRuleCount(AppLanguage language) => RulesOf(language).Length;

    // ─────────────────── 规则表：名字住在表里，这里只排"哪天算" ───────────────────

    private enum RuleKind { Fixed, NthWeekday, LastWeekday, Lunar, EveOfLunarNewYear, Qingming, JapanEquinox }

    /// <summary>
    /// 一颗节日规则。<see cref="Month"/>／<see cref="Day"/> 在 <see cref="RuleKind.Lunar"/> 那一支装的是
    /// <b>农历</b>月与日——同一对字段两种读法，靠 <see cref="Kind"/> 分流
    /// （所以工厂只有下面这几颗，界面与测试都不许自己拼一颗规则）。
    /// </summary>
    private readonly record struct FestivalRule(string Key, RuleKind Kind, int Month, int Day, DayOfWeek Weekday, int Occurrence)
    {
        public static FestivalRule Fixed(string key, int month, int day)
            => new(key, RuleKind.Fixed, month, day, DayOfWeek.Sunday, 0);

        /// <summary>该月<b>第 occurrence 个</b> weekday（1＝第一个）。occurrence 越界那一格自然永不命中。</summary>
        public static FestivalRule Nth(string key, int month, DayOfWeek weekday, int occurrence)
            => new(key, RuleKind.NthWeekday, month, 0, weekday, occurrence);

        public static FestivalRule Last(string key, int month, DayOfWeek weekday)
            => new(key, RuleKind.LastWeekday, month, 0, weekday, 0);

        public static FestivalRule Lunar(string key, int lunarMonth, int lunarDay)
            => new(key, RuleKind.Lunar, lunarMonth, lunarDay, DayOfWeek.Sunday, 0);

        /// <summary>除夕：农历十二月有 29 天（无年三十）与 30 天两种，硬编码「腊月三十」会<b>静默漏掉</b>
        /// 29 天的年份，所以用"次日是正月初一"反推（DeskBox 同思路）。</summary>
        public static FestivalRule EveOfLunarNewYear(string key)
            => new(key, RuleKind.EveOfLunarNewYear, 0, 0, DayOfWeek.Sunday, 0);

        /// <summary>清明：寿星公式，每年日期浮动，所以它是一条<b>规则</b>而不是一颗固定日期。</summary>
        public static FestivalRule Qingming(string key) => new(key, RuleKind.Qingming, 4, 0, DayOfWeek.Sunday, 0);

        /// <summary>日本の春分の日／秋分の日：月＝3 或 9，日号由 <see cref="TryJapanEquinoxDay"/> 查官制年表。
        /// 它⛔ 走寿星公式（那条路 2022 春分与 2041 秋分各差一天，见 <c>GlanceCalendarTests</c> 那颗对照格），
        /// 年表覆盖不到的年份一律<b>永不命中</b>＝宁缺不说假话。</summary>
        public static FestivalRule JapanEquinox(string key, int month)
            => new(key, RuleKind.JapanEquinox, month, 0, DayOfWeek.Sunday, 0);
    }


    /// <summary>中文档：清明 → 农历节日 → 除夕 → 公历固定节日。<b>顺序＝优先级</b>，
    /// 与批次 WI-56 之前的行为逐颗对齐（⛔ 顺手重排：重排就是改屏幕上念哪一句）。
    /// 农历节日一律要求<b>非闰月</b>（旧实现那个 switch 也是如此：闰五月初一⛔ 算端午）。</summary>
    private static readonly FestivalRule[] ChineseRules =
    [
        FestivalRule.Qingming("glance.festival.qingming"),
        FestivalRule.Lunar("glance.festival.lunarNewYear", 1, 1),
        FestivalRule.Lunar("glance.festival.lantern", 1, 15),
        FestivalRule.Lunar("glance.festival.duanwu", 5, 5),
        FestivalRule.Lunar("glance.festival.qixi", 7, 7),
        FestivalRule.Lunar("glance.festival.zhongyuan", 7, 15),
        FestivalRule.Lunar("glance.festival.midAutumn", 8, 15),
        FestivalRule.Lunar("glance.festival.doubleNinth", 9, 9),
        FestivalRule.Lunar("glance.festival.labu", 12, 8),
        FestivalRule.EveOfLunarNewYear("glance.festival.chuxi"),
        FestivalRule.Fixed("glance.festival.newYear", 1, 1),
        FestivalRule.Fixed("glance.festival.valentines", 2, 14),
        FestivalRule.Fixed("glance.festival.womensDay", 3, 8),
        FestivalRule.Fixed("glance.festival.arborDay", 3, 12),
        FestivalRule.Fixed("glance.festival.foolsDay", 4, 1),
        FestivalRule.Fixed("glance.festival.labourDay", 5, 1),
        FestivalRule.Fixed("glance.festival.youthDay", 5, 4),
        FestivalRule.Fixed("glance.festival.childrensDay", 6, 1),
        FestivalRule.Fixed("glance.festival.partyFounding", 7, 1),
        FestivalRule.Fixed("glance.festival.armyFounding", 8, 1),
        FestivalRule.Fixed("glance.festival.teachersDay", 9, 10),
        FestivalRule.Fixed("glance.festival.nationalDay", 10, 1),
        FestivalRule.Fixed("glance.festival.shoppingDay", 11, 11),
        FestivalRule.Fixed("glance.festival.christmasEve", 12, 24),
        FestivalRule.Fixed("glance.festival.christmas", 12, 25),
    ];

    /// <summary>英文档＝美国公共日历与传统节日。</summary>
    private static readonly FestivalRule[] EnglishRules =
    [
        FestivalRule.Fixed("glance.festival.newYear", 1, 1),
        FestivalRule.Nth("glance.festival.mlkDay", 1, DayOfWeek.Monday, 3),
        FestivalRule.Fixed("glance.festival.valentines", 2, 14),
        FestivalRule.Nth("glance.festival.presidentsDay", 2, DayOfWeek.Monday, 3),
        FestivalRule.Nth("glance.festival.mothersDay", 5, DayOfWeek.Sunday, 2),
        FestivalRule.Last("glance.festival.memorialDay", 5, DayOfWeek.Monday),
        FestivalRule.Nth("glance.festival.fathersDay", 6, DayOfWeek.Sunday, 3),
        FestivalRule.Fixed("glance.festival.juneteenth", 6, 19),
        FestivalRule.Fixed("glance.festival.independenceDay", 7, 4),
        FestivalRule.Nth("glance.festival.labourDay", 9, DayOfWeek.Monday, 1),
        FestivalRule.Fixed("glance.festival.halloween", 10, 31),
        FestivalRule.Fixed("glance.festival.veteransDay", 11, 11),
        FestivalRule.Nth("glance.festival.thanksgiving", 11, DayOfWeek.Thursday, 4),
        FestivalRule.Fixed("glance.festival.christmasEve", 12, 24),
        FestivalRule.Fixed("glance.festival.christmas", 12, 25),
        FestivalRule.Fixed("glance.festival.newYearsEve", 12, 31),
    ];

    /// <summary>日文档＝日本年中行事与国民祝日（含 Happy Monday 那一类）。缺的那几天见文件头那句 ⚠。</summary>
    private static readonly FestivalRule[] JapaneseRules =
    [
        FestivalRule.Fixed("glance.festival.newYear", 1, 1),
        FestivalRule.Nth("glance.festival.comingOfAgeDay", 1, DayOfWeek.Monday, 2),
        FestivalRule.Fixed("glance.festival.nationFoundingDay", 2, 11),
        FestivalRule.Fixed("glance.festival.emperorsBirthday", 2, 23),
        FestivalRule.Fixed("glance.festival.dollFestival", 3, 3),
        FestivalRule.JapanEquinox("glance.festival.springEquinoxDay", 3),
        FestivalRule.Fixed("glance.festival.constitutionDay", 5, 3),
        FestivalRule.Fixed("glance.festival.greenDay", 5, 4),
        FestivalRule.Fixed("glance.festival.childrensDay", 5, 5),
        FestivalRule.Nth("glance.festival.mothersDay", 5, DayOfWeek.Sunday, 2),
        FestivalRule.Nth("glance.festival.fathersDay", 6, DayOfWeek.Sunday, 3),
        FestivalRule.Nth("glance.festival.marineDay", 7, DayOfWeek.Monday, 3),
        FestivalRule.Fixed("glance.festival.qixi", 7, 7),
        FestivalRule.Fixed("glance.festival.mountainDay", 8, 11),
        FestivalRule.Fixed("glance.festival.obon", 8, 15),
        FestivalRule.Nth("glance.festival.respectForAgedDay", 9, DayOfWeek.Monday, 3),
        FestivalRule.JapanEquinox("glance.festival.autumnEquinoxDay", 9),
        FestivalRule.Nth("glance.festival.sportsDay", 10, DayOfWeek.Monday, 2),
        FestivalRule.Fixed("glance.festival.cultureDay", 11, 3),
        FestivalRule.Fixed("glance.festival.labourThanksgivingDay", 11, 23),
        FestivalRule.Fixed("glance.festival.newYearsEve", 12, 31),
    ];

    private static FestivalRule[] RulesOf(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => ChineseRules,
        AppLanguage.Japanese => JapaneseRules,
        _ => EnglishRules,
    };

    private static bool UsesChineseCalendar(FestivalRule[] rules)
    {
        foreach (var rule in rules)
            if (rule.Kind is RuleKind.Lunar or RuleKind.EveOfLunarNewYear) return true;
        return false;
    }

    private static bool Matches(
        in FestivalRule rule, DateOnly date,
        (int Month, int Day, bool IsLeap) lunar,
        (int Month, int Day, bool IsLeap) nextLunar) => rule.Kind switch
    {
        RuleKind.Fixed => date.Month == rule.Month && date.Day == rule.Day,
        // "第 n 个星期几"：日子从 1 起算，(day-1)/7 就是它落在本月第几周。
        RuleKind.NthWeekday => date.Month == rule.Month && date.DayOfWeek == rule.Weekday
                               && (date.Day - 1) / 7 + 1 == rule.Occurrence,
        // "最后一个星期几"：该星期几只剩 7 天以内的窗口，之后再没有同星期几。
        RuleKind.LastWeekday => date.Month == rule.Month && date.DayOfWeek == rule.Weekday
                                && date.Day > DateTime.DaysInMonth(date.Year, date.Month) - 7,
        RuleKind.Lunar => !lunar.IsLeap && lunar.Month == rule.Month && lunar.Day == rule.Day,
        RuleKind.EveOfLunarNewYear => !nextLunar.IsLeap && nextLunar.Month == 1 && nextLunar.Day == 1,
        RuleKind.Qingming => date.Month == 4 && date.Day == GetQingmingDay(date.Year),
        RuleKind.JapanEquinox => date.Month == rule.Month
                                 && TryJapanEquinoxDay(date.Year, rule.Month, out var day) && date.Day == day,
        _ => false,
    };

    private static (int Month, int Day, bool IsLeap) GetChineseDate(DateOnly date)
    {
        var value = date.ToDateTime(TimeOnly.MinValue);
        var year = Calendar.GetYear(value);
        var calendarMonth = Calendar.GetMonth(value);
        var leapMonth = Calendar.GetLeapMonth(year);
        var isLeap = leapMonth > 0 && calendarMonth == leapMonth;
        var month = leapMonth > 0 && calendarMonth >= leapMonth
            ? calendarMonth - 1
            : calendarMonth;
        return (month, Calendar.GetDayOfMonth(value), isLeap);
    }

    /// <summary>清明节在当年的日号（寿星公式，照搬 DeskBox）。</summary>
    private static int GetQingmingDay(int year)
    {
        var shortYear = year % 100;
        var centuryConstant = year < 2000 ? 5.59 : 4.81;
        return (int)Math.Floor(shortYear * 0.2422 + centuryConstant) - (shortYear / 4);
    }

    /// <summary>国立天文台「何年後かの春分の日・秋分の日」那张予報表（2020～2050，按 JST 那天）。
    /// 这两颗是日本<b>国民の祝日</b>里唯一日期不固定的，故交<b>年表</b>而不交公式：寿星公式在同一张表上
    /// 2022 年春分与 2041 年秋分各差一天（那一格由 <c>GlanceCalendarTests</c> 钉着）。</summary>
    private static readonly (int Year, int SpringDay, int AutumnDay)[] JapanEquinoxDays =
    [
        (2020, 20, 22), (2021, 20, 23), (2022, 21, 23), (2023, 21, 23), (2024, 20, 22),
        (2025, 20, 23), (2026, 20, 23), (2027, 21, 23), (2028, 20, 22), (2029, 20, 23),
        (2030, 20, 23), (2031, 21, 23), (2032, 20, 22), (2033, 20, 23), (2034, 20, 23),
        (2035, 21, 23), (2036, 20, 22), (2037, 20, 23), (2038, 20, 23), (2039, 21, 23),
        (2040, 20, 22), (2041, 20, 23), (2042, 20, 23), (2043, 21, 23), (2044, 20, 22),
        (2045, 20, 22), (2046, 20, 23), (2047, 21, 23), (2048, 20, 22), (2049, 20, 22),
        (2050, 20, 23),
    ];

    /// <summary>那两颗浮动祝日在当年的日号。<b>年表覆盖不到的年份交 false</b>——界面上就那一行不出，
    /// ⛔ 拿公式外推到 2051 年以后（P-189 的「寧缺毋滥」口径）。</summary>
    public static bool TryJapanEquinoxDay(int year, int month, out int day)
    {
        foreach (var row in JapanEquinoxDays)
        {
            if (row.Year != year) continue;
            day = month == 3 ? row.SpringDay : month == 9 ? row.AutumnDay : 0;
            return day > 0;
        }
        day = 0;
        return false;
    }

    private static readonly string[] LunarMonthNames =
        { string.Empty, "正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊" };

    private static string LunarDayName(int day) => day switch
    {
        10 => "初十",
        20 => "二十",
        30 => "三十",
        < 10 => "初" + Num(day),
        < 20 => "十" + Num(day - 10),
        < 30 => "廿" + Num(day - 20),
        _ => day.ToString(),
    };

    private static string Num(int n) => n switch
    {
        1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五",
        6 => "六", 7 => "七", 8 => "八", 9 => "九",
        _ => string.Empty,
    };
}
