#nullable enable
using System.Globalization;
using System.Text;

namespace StarMark.Core.Calc;

/// <summary>Unix 时间戳的精度。同一段数字既可能是秒也可能是毫秒，靠位数判定（见 <see cref="TimestampConverter.DetectUnit"/>）。</summary>
public enum UnixStampUnit
{
    Seconds,
    Milliseconds,
    Microseconds,
}

/// <summary>
/// Unix 时间戳 ↔ 日期时间（开发者高频需求：日志里的 1727000000 到底是几点）。
/// <para>
/// <b>时区一律作参数传入</b>，内部不读 <see cref="TimeZoneInfo.Local"/>：判定要能被单元测试在任何机器上钉住，
/// 且"同一段文本在两台机器上得到两个时间戳"是必须避免的行为。界面侧显式传本地时区。
/// </para>
/// </summary>
public static class TimestampConverter
{
    /// <summary>
    /// <see cref="DateTimeOffset.FromUnixTimeSeconds"/> 可表示的范围（约 1800-01-01 至 9999-12-31）。
    /// 越界会抛 <c>ArgumentOutOfRangeException</c>，而这里是用户输入路径 ⇒ 必须先挡（与"坏备份里的荒谬
    /// created_at 会崩掉整份健康报告"同一教训，那处的代价已经付过一次）。
    /// </summary>
    public const long MinUnixSeconds = -62_135_596_800L;
    public const long MaxUnixSeconds = 253_402_300_799L;

    /// <summary>秒级时间戳取到 1e11 已是公元 5138 年 ⇒ 达到该量级的输入不可能是"秒"。</summary>
    public const long SecondsUpperBound = 100_000_000_000L;

    public static UnixStampUnit DetectUnit(long value)
    {
        // 与阈值双侧比较，不取绝对值：Math.Abs(long.MinValue) 自身就会溢出
        if (value is > -SecondsUpperBound and < SecondsUpperBound) return UnixStampUnit.Seconds;
        if (value is > -SecondsUpperBound * 1000 and < SecondsUpperBound * 1000) return UnixStampUnit.Milliseconds;
        return UnixStampUnit.Microseconds;
    }

    public static string UnitLabel(UnixStampUnit unit) => unit switch
    {
        UnixStampUnit.Seconds => "秒",
        UnixStampUnit.Milliseconds => "毫秒",
        _ => "微秒",
    };

    /// <summary>把"秒/毫秒/微秒"三种写法折成同一个时刻。判不出/越界时返回 false 并给出可直接显示的原因。</summary>
    public static bool TryParseStamp(string? text, TimeZoneInfo zone,
        out DateTimeOffset at, out UnixStampUnit unit, out string? error)
    {
        at = default;
        unit = UnixStampUnit.Seconds;
        error = null;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) { error = "填入 10 位（秒）或 13 位（毫秒）时间戳"; return false; }
        if (!long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            error = "这不是一个整数时间戳";
            return false;
        }

        unit = DetectUnit(value);
        // 用可空值表示"越界"，不能用 default(DateTimeOffset) 当哨兵：
        // MinUnixSeconds 折出来恰好就是 0001-01-01T00:00:00Z == default ⇒ 合法输入会被误判成越界。
        DateTimeOffset? utc = unit switch
        {
            UnixStampUnit.Seconds => WithinSecondsRange(value)
                ? DateTimeOffset.FromUnixTimeSeconds(value)
                : null,
            // 毫秒/微秒先各自判范围再折回同一刻度：只在"折成秒之后"判的话，13 位串会套错量程。
            UnixStampUnit.Milliseconds => WithinMillisRange(value)
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : null,
            _ => WithinMicrosRange(value)
                // 微秒余量按 100ns 刻度补：截到毫秒会把 .123456 变成 .123
                ? DateTimeOffset.FromUnixTimeSeconds(value / 1_000_000).AddTicks(value % 1_000_000 * 10)
                : null,
        };
        if (utc is null)
        {
            error = $"按「{UnitLabel(unit)}」理解会超出可表示的年份范围";
            return false;
        }
        at = utc.Value.ToOffset((zone ?? TimeZoneInfo.Utc).GetUtcOffset(utc.Value));
        return true;
    }

    private static bool WithinSecondsRange(long v) => v is >= MinUnixSeconds and <= MaxUnixSeconds;
    private static bool WithinMillisRange(long v) =>
        v >= MinUnixSeconds * 1000 && v <= MaxUnixSeconds * 1000;
    private static bool WithinMicrosRange(long v) =>
        v / 1_000_000 >= MinUnixSeconds && v / 1_000_000 <= MaxUnixSeconds;

    /// <summary>某个 UTC 秒在指定时区下的时刻。偏移按"该时区针对这一 UTC 时刻"取，跨夏令时切换点才不误。</summary>
    public static DateTimeOffset FromSeconds(long unixSeconds, TimeZoneInfo zone)
    {
        var utc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        return utc.ToOffset((zone ?? TimeZoneInfo.Utc).GetUtcOffset(utc));
    }

    /// <summary>可读串：带时区的本地时间与 UTC 各给一行，避免用户自己心算时区。</summary>
    public static string Describe(long unixSeconds, TimeZoneInfo zone)
    {
        var local = FromSeconds(unixSeconds, zone);
        var text = Format(local) + "  (" + OffsetText(local.Offset) + ")";
        return local.Offset == TimeSpan.Zero
            ? text
            : text + "　" + Format(local.ToUniversalTime()) + "  UTC";
    }

    /// <summary>
    /// 日期时间 → Unix 秒/毫秒/微秒。<b>全函数、不会失败</b>：`DateTimeOffset` 可表示的最晚年份
    /// （9999-12-31）折成微秒是 2.5e17，仍小于 <see cref="long.MaxValue"/>；1970 之前给负数而非抛异常
    /// （实测 1500 年正常往返，故这里不再留 try/catch——不可达的错误分支只会让调用方去处理一个不存在的失败）。
    /// </summary>
    public static long ToUnix(DateTimeOffset at, UnixStampUnit unit) => unit switch
    {
        UnixStampUnit.Seconds => at.ToUnixTimeSeconds(),
        UnixStampUnit.Milliseconds => at.ToUnixTimeMilliseconds(),
        _ => at.ToUnixTimeMilliseconds() * 1000,
    };

    private static readonly string[] KnownFormats =
    {
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
        "yyyy/M/d H:mm:ss", "yyyy/M/d H:mm", "yyyy/M/d",
        "yyyy-M-d H:mm:ss", "yyyy-M-d H:mm", "yyyy-M-d",
    };

    /// <summary>
    /// 解析人写的日期时间。<b>文本不含显式偏移时按传入时区解释</b>；含 <c>Z</c> / <c>+08:00</c> 时以文本为准。
    /// 先试格式表再退通用解析，最后才试当前区域（中文用户会写「2026年9月23日」）。
    /// </summary>
    public static bool TryParseClock(string? text, TimeZoneInfo zone, out DateTimeOffset at, out string? error)
    {
        at = default;
        error = null;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) { error = "例：2026-09-23 20:48:12"; return false; }

        if (HasExplicitOffset(trimmed)
            && DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var explicitAt))
        {
            at = explicitAt;
            return true;
        }

        if (!DateTime.TryParseExact(trimmed, KnownFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var plain)
            && !DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out plain)
            && !DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out plain))
        {
            error = "无法识别这个日期时间（试试 2026-09-23 20:48:12）";
            return false;
        }
        if (plain.Year < 1000) { error = "年份看起来不完整"; return false; }
        at = new DateTimeOffset(DateTime.SpecifyKind(plain, DateTimeKind.Unspecified),
            (zone ?? TimeZoneInfo.Utc).GetUtcOffset(plain));
        return true;
    }

    /// <summary>文本里是否自带偏移信息（结尾的 Z，或日期段之后出现的 +hh:mm / -hh:mm）。只有这时才该信文本。</summary>
    private static bool HasExplicitOffset(string s)
    {
        if (s.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;
        // 日期本身带 "-"（2026-09-23），所以偏移只可能出现在第二个分隔符之后
        var tail = s.Length > 6 ? s.Substring(s.Length - 6) : string.Empty;
        var sign = tail.IndexOfAny(new[] { '+', '-' });
        return sign == 0 && tail[3] == ':';
    }

    public static string Format(DateTimeOffset at) =>
        $"{at.Year:D4}-{at.Month:D2}-{at.Day:D2} {at.Hour:D2}:{at.Minute:D2}:{at.Second:D2} {WeekName(at.DayOfWeek)}";

    /// <summary>星期名——跨时区核对时星期最容易看错（"周五的日志"换算后可能是周六），故始终带上。</summary>
    public static string WeekName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日",
    };

    public static string OffsetText(TimeSpan offset)
    {
        var negative = offset < TimeSpan.Zero;
        var abs = negative ? offset.Negate() : offset;
        return $"UTC{(negative ? '-' : '+')}{(int)abs.Hours:00}:{abs.Minutes:00}";
    }
}
