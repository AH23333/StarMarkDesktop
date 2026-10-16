#nullable enable
using StarMark.Abstractions.I18n;

namespace StarMark.Core.Widgets;

/// <summary>
/// 世界时钟的一个点位：<b>只带 Windows 时区 Id</b>。
/// <para>批次 WI-76 之前这里还带一颗 <c>Name</c>，而那一句是"给用户看的字"<b>存进了组件档</b>：
/// 屏幕上那一行念什么因此取决于<b>装它那一刻系统的 UI 语言</b>（名字取自 <c>zone.DisplayName</c>），
/// ⛔ 用户选的应用语言，也⛔ 再有机会换——换语言、换系统语言都⛔ 动它（与 ✅P-199 同族：会落盘的成品字）。
/// 现在库里只存身份，名字由 <see cref="WorldClockPolicy.DisplayNameOf"/> <b>当场取</b>；
/// 老档里那颗多出来的 <c>Name</c> 由 <c>System.Text.Json</c> 默认忽略（⛔ 数据迁移）。</para>
/// </summary>
public sealed record WorldClockCity(string ZoneId);

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

    /// <summary>首次使用（配置里从没存过点位）时的默认四城：覆盖亚太/欧洲/北美三个常见协作带。
    /// <b>这一档只列 Id</b>——旧写法在这里手写四颗城名，它们既上屏<b>又落盘</b>，
    /// 于是"库里那句是哪国话"这件事⛔ 由用户定（本仓第一把按成员形状数的尺也⛔ 看得见它＝名册 304）。</summary>
    public static IReadOnlyList<WorldClockCity> DefaultCities { get; } = new[]
    {
        new WorldClockCity("China Standard Time"),
        new WorldClockCity("Tokyo Standard Time"),
        new WorldClockCity("W. Europe Standard Time"),
        new WorldClockCity("Eastern Standard Time"),
    };

    /// <summary>
    /// 从系统的本地化显示名里取出城市名：<c>"(UTC+08:00) 北京"</c> → <c>"北京"</c>。
    /// 无括号时原样返回（Trim 后）；括号在后（个别语言包）也不会把整串吞成空。
    /// </summary>
    public static string ShortName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return Locale.T("widget.clock.unnamedZone");
        var close = displayName.IndexOf(')');
        var tail = close >= 0 ? displayName.Substring(close + 1).Trim() : displayName.Trim();
        return tail.Length == 0 ? displayName.Trim() : tail;
    }

    /// <summary>
    /// 该点位相对本机的日历差标注。世界时钟最容易看错的是"同一个钟点对应哪一天"，
    /// 所以只要不是本机今天就必须说出来（明天/昨天/±N 天），不能让人自己算。
    /// </summary>
    /// <summary>
    /// 点位那一行的<b>名字</b>：<b>当场由 Id 取</b>——取到系统给的那颗显示名就裁掉区号前缀，
    /// 取不到（精简系统／被删的注册表项／手改坏的档）就退回 Id 本身。
    /// <para>⛔ "取不到时编一句未命名"：那一行同时会给一句 <c>widget.clock.noZone</c> 说出
    /// "本机没有这个时区"，两个出口念同一件事＝坑表 #252 那一族；Id 本身是唯一不撒谎的名字。</para>
    /// <para>取词的<b>时机</b>＝装载那一刻＋换语言那一刻（<c>WorldClockWidget.RepasteZoneRows</c>），
    /// ⛔ 每秒一拍去查（每秒一次是 <c>Tick</c> 的活，那颗纪律在类头注释里）。</para>
    /// </summary>
    public static string DisplayNameOf(string zoneId) =>
        TryResolve(zoneId) is { } zone ? ShortName(zone.DisplayName) : zoneId;

    /// <summary>跨日标注的三副面孔只住在语言表里（<c>time.day.*</c>，与待办那颗截止读数同一族，
    /// 批次 WI-76 之前这里与 <c>LocalItemState.DescribeDue</c> 各写一份中文）。</summary>
    public static string DayLabel(int dayDifference) => dayDifference switch
    {
        0 => string.Empty,
        1 => Locale.T("time.day.tomorrow"),
        -1 => Locale.T("time.day.yesterday"),
        > 0 => Locale.Fmt("time.day.ahead", dayDifference),
        _ => Locale.Fmt("time.day.behind", dayDifference),
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
