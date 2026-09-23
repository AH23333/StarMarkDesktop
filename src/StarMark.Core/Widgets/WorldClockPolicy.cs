#nullable enable

namespace StarMark.Core.Widgets;

/// <summary>世界时钟的一个点位：Windows 时区 Id + 给用户看的名字。</summary>
public sealed record WorldClockCity(string ZoneId, string Name);

/// <summary>
/// 世界时钟的纯逻辑（显示名裁剪、跨日标注、时区解析兜底）。
/// <para>
/// 只用 <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/> 的 <b>Windows Id</b>：
/// 不引 TimeZoneConverter 之类的映射库（依赖纪律），因此点位表里的 Id 必须是 Windows 自带的那套
/// （"China Standard Time"、"W. Europe Standard Time"…）。缺时区的极端环境（精简系统/被删的注册表项）
/// 由 <see cref="TryResolve"/> 显式回落成"这个时区在本机不存在"，而不是静默少一行。
/// </para>
/// </summary>
public static class WorldClockPolicy
{
    /// <summary>一个组件里最多几个点位（再多就超出小窗可读范围，且换屏后信息密度反而下降）。</summary>
    public const int MaxCities = 8;

    /// <summary>首次使用（配置里从没存过点位）时的默认四城：覆盖亚太/欧洲/北美三个常见协作带。</summary>
    public static IReadOnlyList<WorldClockCity> DefaultCities { get; } = new[]
    {
        new WorldClockCity("China Standard Time", "北京"),
        new WorldClockCity("Tokyo Standard Time", "东京"),
        new WorldClockCity("W. Europe Standard Time", "柏林"),
        new WorldClockCity("Eastern Standard Time", "纽约"),
    };

    /// <summary>
    /// 从系统的本地化显示名里取出城市名：<c>"(UTC+08:00) 北京"</c> → <c>"北京"</c>。
    /// 无括号时原样返回（Trim 后）；括号在后（个别语言包）也不会把整串吞成空。
    /// </summary>
    public static string ShortName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "未命名时区";
        var close = displayName.IndexOf(')');
        var tail = close >= 0 ? displayName.Substring(close + 1).Trim() : displayName.Trim();
        return tail.Length == 0 ? displayName.Trim() : tail;
    }

    /// <summary>
    /// 该点位相对本机的日历差标注。世界时钟最容易看错的是"同一个钟点对应哪一天"，
    /// 所以只要不是本机今天就必须说出来（明天/昨天/±N 天），不能让人自己算。
    /// </summary>
    public static string DayLabel(int dayDifference) => dayDifference switch
    {
        0 => string.Empty,
        1 => "明天",
        -1 => "昨天",
        > 0 => $"+{dayDifference} 天",
        _ => $"{dayDifference} 天",
    };

    public static string OffsetLabel(TimeSpan offset)
    {
        var negative = offset < TimeSpan.Zero;
        var abs = negative ? offset.Negate() : offset;
        return $"UTC{(negative ? '-' : '+')}{(int)abs.Hours:00}:{abs.Minutes:00}";
    }

    /// <summary>
    /// 解析时区。<b>不抛</b>：Id 为空、已被系统删除、或是个手改坏的配置文件，都返回 null，
    /// 由界面给出可行动的原因（组件的每秒刷新循环里没有 try，抛出来就是整个应用崩）。
    /// </summary>
    public static TimeZoneInfo? TryResolve(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>去重与上限：同机时区不重复列，超出上限的追加直接拒绝。</summary>
    public static bool CanAdd(IReadOnlyList<WorldClockCity> current, string zoneId) =>
        current.Count < MaxCities
        && !current.Any(c => string.Equals(c.ZoneId, zoneId, StringComparison.OrdinalIgnoreCase));
}
