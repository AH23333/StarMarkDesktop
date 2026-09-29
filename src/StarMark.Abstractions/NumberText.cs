#nullable enable
using System.Globalization;

namespace StarMark.Abstractions;

/// <summary>
/// 给人看的<b>数字</b>读数，<b>全应用唯一出处</b>（批次 RZ，P-122 的另一半 / 与 <see cref="DateTimeText"/> 平级）。
/// <para>
/// RY 收了日期那半，这条收数字那半，而<b>前提不是照搬 RY 的结论</b>：本批先拿七种文化实测了一遍
/// （<c>NumberTextTests.ImplicitNumericFormatting_…</c>），结果是——
/// <b>只有会出现"小数点 / 千分位 / 百分号"的格式才会变</b>：
/// <code>de-DE</code> 把 1234.5 打成 <c>1234,5</c>、把 1234567 打成 <c>1.234.567</c>（点当分组）、把 15% 打成 <c>15 %</c>；
/// <code>ar-SA</code>/<code>ar-EG</code> 用 <c>٫</c> 与 <c>٬</c> 还把百分号换成 <c>٪</c>；
/// <code>hi-IN</code> 按 lakh 分组打成 <c>12,34,567</c>。
/// 而 <c>{7:0}</c>、<c>{7:00}</c>、<c>5.ToString()</c> 这类<b>纯整数</b>形状<b>七种文化逐字相同</b>——
/// 所以本类<b>不接管补零</b>，把不该收的写进判据等于给下一批留一条"为什么要走这颗"的悬案。
/// </para>
/// <para>
/// 为什么宁可统一成拉丁分隔符也不要"跟随系统"：本产品界面是中文的，同一个数字在卡片里写
/// <c>★ 1.234</c>（德式分组）而在剪贴板占用里写 <c>★ 1,234</c>（本仓早有 3 处锁了不变文化）时，
/// 用户读到的是两个数。<b>同屏只有一个口径</b>比"符合某个区域习惯"更重要，也与文件名/URL 那条口径一致。
/// </para>
/// </summary>
public static class NumberText
{
    /// <summary>千分位分组：<c>1,234,567</c>。用于 token 数、星数这类"要一眼看出量级"的整数。</summary>
    public static string Grouped(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>千分位分组并四舍五入到整数：<c>1,234,567</c>。给"小时数"这类天然是小数、但只想看到整数的量。</summary>
    public static string Grouped(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>最多一位小数、末尾零不写：<c>1234.5</c>／<c>2</c>（不是 <c>2.0</c>）。体积与时长读数用。</summary>
    public static string UpTo1(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>最多两位小数、末尾零不写：<c>1.25</c>／<c>1.5</c>／<c>2</c>。缩放倍数、不透明度这类比例用。</summary>
    public static string UpTo2(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// <see cref="UpTo2(double)"/> 的十进制版。计算器算的就是 <see cref="decimal"/>——
    /// 为它单开一颗，是为了让调用点不必先 <c>(double)</c> 一下：<b> widening 到二进制浮点会掉精度 </b>，
    /// 而这颗判据存在的理由恰恰是"读数不许因为写法而变化"。
    /// </summary>
    public static string UpTo2(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>固定一位小数：<c>1234.5</c>。日志里的毫秒读数用（"0.0"比"0"更诚实——它确实是量出来的）。</summary>
    public static string Fixed1(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>固定两位小数：<c>0.85</c>。材质不透明度这类要看得见精度的读数用。</summary>
    public static string Fixed2(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>
    /// 固定三位小数：<c>31.230</c>。<b>这一颗不只管显示</b>——天气的经纬度缓存键就是它
    /// （<c>WeatherWidget</c> 的 <c>{lat:F3},{lon:F3}</c>）：德式小数逗号会把键打成 <c>52,371,4,868</c>，
    /// 与同一个键里那个真逗号<b>混成一串</b>，于是切城市时"键变了没有"这件事不再可靠。
    /// </summary>
    public static string Fixed3(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>
    /// 整百分比：<c>15%</c>。<b>这一颗不是 <c>"P0"</c> 的包装，而且必须是现在这个样子</b>：
    /// 不变文化的 <c>P0</c> 打的是 <c>15 %</c>（带空格），而中文界面上今天显示的是 <c>15%</c>——
    /// 照搬 <c>P0</c> 会为了"收一处"而改掉用户已经看惯的读数，那正是本批要避免的事。
    /// 所以这里自己乘 100、自己拼百分号：<b>小数点走不变文化，符号走界面上那一种</b>。
    /// </summary>
    public static string Percent(double fraction)
        => (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
