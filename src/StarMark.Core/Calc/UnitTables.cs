#nullable enable
using System.Globalization;
using StarMark.Abstractions.I18n;

namespace StarMark.Core.Calc;

/// <summary>
/// 一个单位。<see cref="ToBase"/> 是"乘以它得到基准量"；<see cref="BaseOffset"/> 只在仿射单位（温度）非 0。
/// <para><see cref="LabelKey"/> 是<b>身份的那半＋念法的那颗键</b>（批次 WI-55／P-182b）：
/// 这一族以前把「毫米 mm」直接存成 <c>Label</c>，于是换语言之后下拉里、结果句里念的还是旧语言那句——
/// 与 ✅WI-49 那套画笔名、✅WI-46c 那颗贴图组名同一条界线：<b>建表那一刻存成品字＝把念法当身份存了</b>。
/// 念法从此只在读数那一刻现算（<c>Locale.T(unit.LabelKey)</c>）。</para>
/// </summary>
public sealed record CalcUnit(string Key, string LabelKey, decimal ToBase, decimal BaseOffset = 0m);

/// <summary>一类可互转的量。基准单位是表内 ToBase==1 且 BaseOffset==0 的那一个。</summary>
public sealed record CalcUnitCategory(string Key, string LabelKey, IReadOnlyList<CalcUnit> Units)
{
    public CalcUnit? Find(string? key) =>
        Units.FirstOrDefault(u => string.Equals(u.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>含非零偏移＝仿射（温度），"0 不可乘"这类直觉不成立，界面提示要区分。</summary>
    public bool IsAffine => Units.Any(u => u.BaseOffset != 0m);
}

/// <summary>
/// 单位换算静态表（离线，零依赖，零网络）。
/// <para>
/// <b>刻意不含汇率</b>：实时汇率需要外网且有免费额度/失效风险，发起人 2026-09-23 裁决砍掉；
/// 换算表要能在断网、无任何第三方服务时给出确定答案。
/// </para>
/// <para>
/// 换算一律走 <see cref="decimal"/>（<c>1.5</c> 加 <c>2.5</c> 得 <c>4</c> 而不是 3.9999999999999996）；
/// 非十进制关系的英制/市制单位取国际磅等国际定义精确值，"亩""里""斤"这类中国 customary 单位取法定换算率。
/// </para>
/// <para>
/// <b>表里⛔ 一句成品话</b>：58 颗单位与 8 颗类别各交一颗 <c>calc.unit.*</c>／<c>calc.cat.*</c> 键，
/// 界面与结果句按那颗键现取词（键名是 ASCII 身份的一部分，改键名＝改表，改语言＝只改表里那一行）。
/// </para>
/// </summary>
public static class UnitTables
{
    public const string LengthKey = "length";
    public const string MassKey = "mass";
    public const string AreaKey = "area";
    public const string VolumeKey = "volume";
    public const string SpeedKey = "speed";
    public const string DurationKey = "duration";
    public const string StorageKey = "storage";
    public const string TemperatureKey = "temperature";

    /// <summary>展示顺序即界面下拉顺序（常用的在前）。</summary>
    public static IReadOnlyList<CalcUnitCategory> Categories { get; } = new[]
    {
        new CalcUnitCategory(LengthKey, "calc.cat.length", new[]
        {
            new CalcUnit("mm", "calc.unit.mm", 0.001m),
            new CalcUnit("cm", "calc.unit.cm", 0.01m),
            new CalcUnit("m", "calc.unit.m", 1m),
            new CalcUnit("km", "calc.unit.km", 1000m),
            new CalcUnit("in", "calc.unit.in", 0.0254m),
            new CalcUnit("ft", "calc.unit.ft", 0.3048m),
            new CalcUnit("yd", "calc.unit.yd", 0.9144m),
            new CalcUnit("mi", "calc.unit.mi", 1609.344m),
            new CalcUnit("nmi", "calc.unit.nmi", 1852m),
            new CalcUnit("li", "calc.unit.li", 500m),
        }),
        new CalcUnitCategory(MassKey, "calc.cat.mass", new[]
        {
            new CalcUnit("mg", "calc.unit.mg", 0.000001m),
            new CalcUnit("g", "calc.unit.g", 0.001m),
            new CalcUnit("kg", "calc.unit.kg", 1m),
            new CalcUnit("t", "calc.unit.t", 1000m),
            new CalcUnit("oz", "calc.unit.oz", 0.028349523125m),
            new CalcUnit("lb", "calc.unit.lb", 0.45359237m),
            new CalcUnit("jin", "calc.unit.jin", 0.5m),
            new CalcUnit("liang", "calc.unit.liang", 0.05m),
        }),
        new CalcUnitCategory(AreaKey, "calc.cat.area", new[]
        {
            new CalcUnit("cm2", "calc.unit.cm2", 0.0001m),
            new CalcUnit("m2", "calc.unit.m2", 1m),
            // 1 亩 = 60 平方丈 = 2000/3 平方米；decimal 取 28 位精度，显示统一走 G12 收敛。
            new CalcUnit("mu", "calc.unit.mu", 666.6666666666666666666666667m),
            new CalcUnit("ha", "calc.unit.ha", 10000m),
            new CalcUnit("km2", "calc.unit.km2", 1000000m),
            new CalcUnit("ft2", "calc.unit.ft2", 0.09290304m),
            new CalcUnit("acre", "calc.unit.acre", 4046.8564224m),
            new CalcUnit("mi2", "calc.unit.mi2", 2589988.110336m),
        }),
        new CalcUnitCategory(VolumeKey, "calc.cat.volume", new[]
        {
            new CalcUnit("ml", "calc.unit.ml", 0.001m),
            new CalcUnit("l", "calc.unit.l", 1m),
            new CalcUnit("cm3", "calc.unit.cm3", 0.001m),
            new CalcUnit("m3", "calc.unit.m3", 1000m),
            new CalcUnit("galus", "calc.unit.galus", 3.785411784m),
            new CalcUnit("galuk", "calc.unit.galuk", 4.54609m),
            new CalcUnit("ft3", "calc.unit.ft3", 28.316846592m),
            new CalcUnit("in3", "calc.unit.in3", 0.016387064m),
        }),
        new CalcUnitCategory(SpeedKey, "calc.cat.speed", new[]
        {
            new CalcUnit("mps", "calc.unit.mps", 1m),
            new CalcUnit("kmh", "calc.unit.kmh", 0.2777777777777777777777777778m),
            new CalcUnit("mph", "calc.unit.mph", 0.44704m),
            new CalcUnit("kn", "calc.unit.kn", 0.5144444444444444444444444444m),
            new CalcUnit("mach", "calc.unit.mach", 340.29m),
        }),
        new CalcUnitCategory(DurationKey, "calc.cat.duration", new[]
        {
            new CalcUnit("ms", "calc.unit.ms", 0.001m),
            new CalcUnit("s", "calc.unit.s", 1m),
            new CalcUnit("min", "calc.unit.min", 60m),
            new CalcUnit("h", "calc.unit.h", 3600m),
            new CalcUnit("d", "calc.unit.d", 86400m),
            new CalcUnit("week", "calc.unit.week", 604800m),
            // 月/年取平均长度（30 天 / 365 天）：换算表用途是量级估算，日历语义在倒计时组件里。
            new CalcUnit("month", "calc.unit.month", 2592000m),
            new CalcUnit("year", "calc.unit.year", 31536000m),
        }),
        new CalcUnitCategory(StorageKey, "calc.cat.storage", new[]
        {
            new CalcUnit("bit", "calc.unit.bit", 0.125m),
            new CalcUnit("b", "calc.unit.byte", 1m),
            // 1KB=1024B 是系统与用户口径（资源管理器/任务管理器同此），非 SI 的 1000。
            new CalcUnit("kb", "calc.unit.kb", 1024m),
            new CalcUnit("mb", "calc.unit.mb", 1048576m),
            new CalcUnit("gb", "calc.unit.gb", 1073741824m),
            new CalcUnit("tb", "calc.unit.tb", 1099511627776m),
            new CalcUnit("pb", "calc.unit.pb", 1125899906842624m),
        }),
        new CalcUnitCategory(TemperatureKey, "calc.cat.temperature", new[]
        {
            new CalcUnit("k", "calc.unit.kelvin", 1m),
            new CalcUnit("c", "calc.unit.celsius", 1m, 273.15m),
            new CalcUnit("f", "calc.unit.fahrenheit", 5m / 9m, 273.15m - 32m * (5m / 9m)),
            new CalcUnit("r", "calc.unit.rankine", 5m / 9m),
        }),
    };

    public static CalcUnitCategory? Category(string? key) =>
        Categories.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 换算。<b>不抛异常</b>：类别/单位找不到、值不可解析时返回 false 并给出<b>那颗键</b>（界面自己贴，
    /// ⛔ 在这里取词——那等于把语言定死在算出它的那一刻，换语言没人回头重算）。
    /// </summary>
    public static bool TryConvert(string? categoryKey, string? fromKey, string? toKey,
        decimal value, out decimal result, out LocMessage? error)
    {
        result = 0m;
        error = null;
        var cat = Category(categoryKey);
        if (cat is null) { error = LocMessage.Of("calc.convert.noCategory"); return false; }
        var from = cat.Find(fromKey);
        var to = cat.Find(toKey);
        if (from is null || to is null) { error = LocMessage.Of("calc.convert.badUnit"); return false; }

        // 先到基准量，再到目标量。温度的偏移在这一步自然成立（℃→K→℉ 与 ℉→K→℃ 共用同一式子）。
        var inBase = value * from.ToBase + from.BaseOffset;
        if (to.ToBase == 0m) { error = LocMessage.Of("calc.convert.zeroRate"); return false; }
        result = (inBase - to.BaseOffset) / to.ToBase;
        return true;
    }

    /// <summary>
    /// 换算结果显示串。G12 而非 G29：表里 1/3、5/9 这类分数在 decimal 下有 28 位尾巴
    /// （100℃ 转 ℉ 会得到 212.0000000000000000000000000），12 位有效数字把它收干净，
    /// 又不至于把 1 里=0.621371 英里这种有意义的精度抹掉。
    /// </summary>
    public static string Format(decimal value) =>
        value.ToString("G12", CultureInfo.InvariantCulture);
}
