#nullable enable
using System.Globalization;

namespace StarMark.Core.Calc;

/// <summary>一个单位。<see cref="ToBase"/> 是"乘以它得到基准量"；<see cref="BaseOffset"/> 只在仿射单位（温度）非 0。</summary>
public sealed record CalcUnit(string Key, string Label, decimal ToBase, decimal BaseOffset = 0m);

/// <summary>一类可互转的量。基准单位是表内 ToBase==1 且 BaseOffset==0 的那一个。</summary>
public sealed record CalcUnitCategory(string Key, string Label, IReadOnlyList<CalcUnit> Units)
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
        new CalcUnitCategory(LengthKey, "长度", new[]
        {
            new CalcUnit("mm", "毫米 mm", 0.001m),
            new CalcUnit("cm", "厘米 cm", 0.01m),
            new CalcUnit("m", "米 m", 1m),
            new CalcUnit("km", "千米 km", 1000m),
            new CalcUnit("in", "英寸 in", 0.0254m),
            new CalcUnit("ft", "英尺 ft", 0.3048m),
            new CalcUnit("yd", "码 yd", 0.9144m),
            new CalcUnit("mi", "英里 mi", 1609.344m),
            new CalcUnit("nmi", "海里", 1852m),
            new CalcUnit("li", "里", 500m),
        }),
        new CalcUnitCategory(MassKey, "重量", new[]
        {
            new CalcUnit("mg", "毫克 mg", 0.000001m),
            new CalcUnit("g", "克 g", 0.001m),
            new CalcUnit("kg", "千克 kg", 1m),
            new CalcUnit("t", "吨 t", 1000m),
            new CalcUnit("oz", "盎司 oz", 0.028349523125m),
            new CalcUnit("lb", "磅 lb", 0.45359237m),
            new CalcUnit("jin", "斤", 0.5m),
            new CalcUnit("liang", "两", 0.05m),
        }),
        new CalcUnitCategory(AreaKey, "面积", new[]
        {
            new CalcUnit("cm2", "平方厘米", 0.0001m),
            new CalcUnit("m2", "平方米", 1m),
            // 1 亩 = 60 平方丈 = 2000/3 平方米；decimal 取 28 位精度，显示统一走 G12 收敛。
            new CalcUnit("mu", "亩", 666.6666666666666666666666667m),
            new CalcUnit("ha", "公顷", 10000m),
            new CalcUnit("km2", "平方千米", 1000000m),
            new CalcUnit("ft2", "平方英尺", 0.09290304m),
            new CalcUnit("acre", "英亩", 4046.8564224m),
            new CalcUnit("mi2", "平方英里", 2589988.110336m),
        }),
        new CalcUnitCategory(VolumeKey, "体积", new[]
        {
            new CalcUnit("ml", "毫升 mL", 0.001m),
            new CalcUnit("l", "升 L", 1m),
            new CalcUnit("cm3", "立方厘米", 0.001m),
            new CalcUnit("m3", "立方米", 1000m),
            new CalcUnit("galus", "加仑(美)", 3.785411784m),
            new CalcUnit("galuk", "加仑(英)", 4.54609m),
            new CalcUnit("ft3", "立方英尺", 28.316846592m),
            new CalcUnit("in3", "立方英寸", 0.016387064m),
        }),
        new CalcUnitCategory(SpeedKey, "速度", new[]
        {
            new CalcUnit("mps", "米/秒", 1m),
            new CalcUnit("kmh", "千米/时", 0.2777777777777777777777777778m),
            new CalcUnit("mph", "英里/时", 0.44704m),
            new CalcUnit("kn", "节", 0.5144444444444444444444444444m),
            new CalcUnit("mach", "马赫(海平面)", 340.29m),
        }),
        new CalcUnitCategory(DurationKey, "时长", new[]
        {
            new CalcUnit("ms", "毫秒", 0.001m),
            new CalcUnit("s", "秒", 1m),
            new CalcUnit("min", "分钟", 60m),
            new CalcUnit("h", "小时", 3600m),
            new CalcUnit("d", "天", 86400m),
            new CalcUnit("week", "周", 604800m),
            // 月/年取平均长度（30 天 / 365 天）：换算表用途是量级估算，日历语义在倒计时组件里。
            new CalcUnit("month", "月(30天)", 2592000m),
            new CalcUnit("year", "年(365天)", 31536000m),
        }),
        new CalcUnitCategory(StorageKey, "存储", new[]
        {
            new CalcUnit("bit", "比特", 0.125m),
            new CalcUnit("b", "字节 B", 1m),
            // 1KB=1024B 是系统与用户口径（资源管理器/任务管理器同此），非 SI 的 1000。
            new CalcUnit("kb", "KB", 1024m),
            new CalcUnit("mb", "MB", 1048576m),
            new CalcUnit("gb", "GB", 1073741824m),
            new CalcUnit("tb", "TB", 1099511627776m),
            new CalcUnit("pb", "PB", 1125899906842624m),
        }),
        new CalcUnitCategory(TemperatureKey, "温度", new[]
        {
            new CalcUnit("k", "开尔文 K", 1m),
            new CalcUnit("c", "摄氏度 ℃", 1m, 273.15m),
            new CalcUnit("f", "华氏度 ℉", 5m / 9m, 273.15m - 32m * (5m / 9m)),
            new CalcUnit("r", "兰氏度 ℉°R", 5m / 9m),
        }),
    };

    public static CalcUnitCategory? Category(string? key) =>
        Categories.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 换算。<b>不抛异常</b>：类别/单位找不到、值不可解析时返回 false 并给出原因（界面直接显示）。
    /// </summary>
    public static bool TryConvert(string? categoryKey, string? fromKey, string? toKey,
        decimal value, out decimal result, out string? error)
    {
        result = 0m;
        error = null;
        var cat = Category(categoryKey);
        if (cat is null) { error = "不认识这一类单位"; return false; }
        var from = cat.Find(fromKey);
        var to = cat.Find(toKey);
        if (from is null || to is null) { error = "起始或目标单位无效，请重新选择"; return false; }

        // 先到基准量，再到目标量。温度的偏移在这一步自然成立（℃→K→℉ 与 ℉→K→℃ 共用同一式子）。
        var inBase = value * from.ToBase + from.BaseOffset;
        if (to.ToBase == 0m) { error = "目标单位换算率为 0（表内配置错误）"; return false; }
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
