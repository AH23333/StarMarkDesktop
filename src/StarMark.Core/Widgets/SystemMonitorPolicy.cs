#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using StarMark.Abstractions;
using StarMark.Abstractions.SystemMonitor;

namespace StarMark.Core.Widgets;

/// <summary>
/// 系统监控组件可展示的指标。
/// <para>
/// <b>持久化契约：按整数落进 widgets.json ⇒ 只能追加、不得重排或改值</b>（与 <c>WidgetKind</c> 同一条）。
/// 这里刻意用 2 的幂：它是 <see cref="FlagsAttribute"/> 集合，插值会让老配置的勾选串位。
/// </para>
/// </summary>
[Flags]
public enum MonitorMetric
{
    /// <summary>用户把所有指标都取消了（与 null＝"从没配过"必须可分辨）。</summary>
    None = 0,
    Cpu = 1,
    Memory = 2,
    Network = 4,
}

/// <summary>CPU 内核时间计数（100 纳秒滴答，直接来自 <c>GetSystemTimes</c> 的三个出参）。</summary>
public readonly record struct CpuCounters(long Idle, long Kernel, long User);

/// <summary>
/// 系统监控组件的全部判据：取哪些数、多久取一次、怎么把两次读数变成一个百分比、怎么显示。
/// <para>
/// 原生采样本身在 <c>StarMark.Integrations/SystemMonitor</c>（那边只负责"把数读回来、读不回来就说原因"），
/// 所有<b>会算错</b>的算术都在这里——测试工程引用不到 <c>StarMark.UI</c>，规则留在界面层就等于没有防线。
/// </para>
/// </summary>
public static class SystemMonitorPolicy
{
    /// <summary>可识别位的掩码：读到的整数里任何一位不认识的都必须剥掉。</summary>
    public const int KnownBits = (int)(MonitorMetric.Cpu | MonitorMetric.Memory | MonitorMetric.Network);

    /// <summary>从未配置时的默认勾选：三项全开（发起人要的是"看一眼这台机器现在忙不忙"）。</summary>
    public const MonitorMetric DefaultMetrics = MonitorMetric.Cpu | MonitorMetric.Memory | MonitorMetric.Network;

    /// <summary>均衡模式采样间隔。</summary>
    public const int BalancedIntervalMs = 1_000;

    /// <summary>省资源模式采样间隔（D7：按性能模式降频）。</summary>
    public const int ResourceSaverIntervalMs = 3_000;

    /// <summary>滑动平均窗下限（D7：CPU 取 2 秒滑动平均——1 秒窗在 PDH/内核计数上抖得读不出趋势）。</summary>
    public const int MinWindowMs = 2_000;

    /// <summary>指标目录：顺序＝界面行序，标签＝界面文案。<b>每加一个指标必须在这里补一行</b>（有守门）。</summary>
    public static IReadOnlyList<(MonitorMetric Metric, string Label, string Glyph)> Catalog { get; } =
        new (MonitorMetric, string, string)[]
        {
            (MonitorMetric.Cpu, "CPU", "⚙"),
            (MonitorMetric.Memory, "内存", "▦"),
            (MonitorMetric.Network, "网速", "⇅"),
        };

    /// <summary>
    /// 把落盘的整数折算成指标集合。<paramref name="wire"/> 为 <b>null</b> 表示"从没配过"⇒ 三项全开；
    /// 显式的 <see cref="MonitorMetric.None"/> 表示"用户自己取消光了"⇒ 保持空（判据是 null 与否，不是位非零）。
    /// 未知高位一律剥掉：将来某版本追加了指标、用户回滚到旧版时不该显示成乱码行。
    /// </summary>
    public static MonitorMetric ResolveMetrics(int? wire)
        => wire is null ? DefaultMetrics : (MonitorMetric)(wire.Value & KnownBits);

    /// <summary>落盘形态（剥掉未知位，避免把脏值写回去）。</summary>
    public static int ToWire(MonitorMetric metrics) => (int)metrics & KnownBits;

    /// <summary>界面要显示的行（按目录顺序，不含 None）。</summary>
    public static IReadOnlyList<(MonitorMetric Metric, string Label, string Glyph)> RowsFor(MonitorMetric metrics)
    {
        var rows = new List<(MonitorMetric, string, string)>();
        foreach (var entry in Catalog)
            if (metrics.HasFlag(entry.Metric)) rows.Add(entry);
        return rows;
    }

    /// <summary>
    /// 采样间隔。<b>不可见时返回 0＝完全不采</b>（D7：只在存在可见实例时采样；
    /// 桌面组件经常整批隐藏，常驻后台采样就是白耗电，而"隐藏了还在跑"是这个仓库踩过的坑）。
    /// </summary>
    public static int SampleIntervalMs(bool windowVisible, bool resourceSaver)
        => !windowVisible ? 0
         : resourceSaver ? ResourceSaverIntervalMs
         : BalancedIntervalMs;

    /// <summary>
    /// 滑动平均窗长。必须 <b>≥ 两拍</b>：窗比采样间隔还短的话，每一拍算出的就是单点值本身，
    /// 所谓"平均"会静默退化成"没有平均"——省资源模式（3 秒一拍）尤其容易踩这条。
    /// </summary>
    public static int WindowMs(int intervalMs)
        => intervalMs <= 0 ? 0 : Math.Max(MinWindowMs, intervalMs * 2);

    /// <summary>
    /// 两次内核计数 → CPU 占用率（0..100）。
    /// <para>
    /// <c>GetSystemTimes</c> 的 <b>kernel 里已经含 idle</b>，所以
    /// 总时间 = kernel + user、繁忙 = (kernel − idle) + user。
    /// 把 kernel 当成"不含 idle 的系统时间"是最常见的写反法，会让同样的读数报出高十几个百分点
    /// （多核机器上还能一路超到 100% 以上）——<c>CpuPercent_KernelAlreadyIncludesIdle</c> 就是钉这一条的。
    /// </para>
    /// </summary>
    /// <returns>算不出时返回 null（计数倒退、两次采样落在同一瞬间、或读数自相矛盾）：
    /// 界面宁显示"--"也不给一个假数。</returns>
    public static double? CpuPercent(CpuCounters previous, CpuCounters current)
    {
        var idle = current.Idle - previous.Idle;
        var kernel = current.Kernel - previous.Kernel;
        var user = current.User - previous.User;
        // 三个计数都是单调递增的：任何一项倒退说明这次读数不可信（异常/被别的实现填错），不编造解释
        if (idle < 0 || kernel < 0 || user < 0) return null;

        var total = kernel + user;
        if (total <= 0) return null;                    // 同一瞬间取到两次：无新数据

        var busy = kernel - idle + user;
        if (busy < 0) return null;                      // busy 不可能大于 total 之外还倒退：读数被破坏
        return Math.Clamp(busy * 100.0 / total, 0.0, 100.0);
    }

    /// <summary>内存占用率（0..100）。总量为 0（读数失败）返回 null，而不是"0%"——两者在界面上必须长得不一样。</summary>
    public static double? MemoryPercent(long totalBytes, long availableBytes)
    {
        if (totalBytes <= 0 || availableBytes < 0 || availableBytes > totalBytes) return null;
        return Math.Clamp((totalBytes - availableBytes) * 100.0 / totalBytes, 0.0, 100.0);
    }

    /// <summary>
    /// 累计字节数 → 速率（字节/秒）。
    /// <b>负差值不能钳成 0，也不能当回绕补 2³²</b>：网卡重置、驱动换接口、32 位计数器回绕三者在
    /// 读数上长得一模一样，而只有第一种和第二种该显示"没有数据"。补 2³² 会把一次拔网线画成
    /// 一根 4 GB 的尖峰，钳 0 会画成"突然断网"——返回 null 让界面明说"这一拍没有可信数据"。
    /// </summary>
    public static double? RatePerSecond(long previousBytes, long currentBytes, double elapsedSeconds)
    {
        if (currentBytes < previousBytes) return null;
        if (elapsedSeconds <= 0) return null;
        return (currentBytes - previousBytes) / elapsedSeconds;
    }

    // ── 接口计数判定：哪些网卡的字节该算进"这台机器的网速" ──
    // 下面三个值抄自 SDK 的 IFTYPE / TUNNEL_TYPE / IF_OPER_STATUS（Integrations 侧只把原始整数交进来）。

    /// <summary>IF_TYPE_SOFTWARE_LOOPBACK：本机回环，流量从没出过机器。</summary>
    public const int IfTypeSoftwareLoopback = 24;

    /// <summary>TUNNEL_TYPE.NONE：非隧道接口。</summary>
    public const int TunnelTypeNone = 0;

    /// <summary>IF_OPER_UP：链路已起的接口。</summary>
    public const int IfOperUp = 1;

    /// <summary>
    /// 这条接口本身有没有资格算作"这台机器的流量"。
    /// <para>
    /// 排除<b>隧道</b>与<b>软件回环</b>不是装饰：Teredo / ISATAP 这类隧道接口会把同一份流量在
    /// "隧道 + 底层物理网卡"上<b>各记一遍</b>，把它们一起加起来就是把下载速度翻倍显示。
    /// 用类型位判定而不是按网卡名（"Teredo Tunneling"、"isatap.{...}"）做字符串匹配——
    /// 名字随语言与驱动变，而类型位是接口自己声明的。
    /// </para>
    /// <para>链路未起（<c>OperStatus != Up</c>）的接口不计：它的数据是上一次连接的残留，
    /// 会把"已经断网"画成"还在跑满速"。</para>
    /// <para>真硬件与否不在这个判定里，由 <see cref="SumCountedAdapters"/> 分两趟处理——
    /// 那一层还要负责"只有虚拟链路在线时宁给虚拟数"的退路。</para>
    /// </summary>
    public static bool CountsTowardNetworkRate(int ifType, int tunnelType, int operStatus)
        => operStatus == IfOperUp
        && tunnelType == TunnelTypeNone
        && ifType != IfTypeSoftwareLoopback;

    /// <summary>一次求和的结果：<see cref="CountedAdapters"/>＝0 表示"没有可显示的链路"，与"网速是 0"必须可分辨。</summary>
    /// <param name="CountedViaVirtualFallback">真硬件网卡一块都没在线，退而统计虚拟接口（界面要说明这一点）。</param>
    public sealed record NetworkTotals(long InBytes, long OutBytes, int CountedAdapters, bool CountedViaVirtualFallback);

    /// <summary>
    /// 把"该计入"的网卡累计字节加起来。
    /// <para>
    /// <b>为什么不能直接全加</b>：真机上一次实测 56 行接口里有 4 块虚拟网卡报着<b>完全相同</b>的计数器
    /// （托管网络 / Wi-Fi Direct 把物理网卡的统计原样镜像了一份），全加等于把同一份流量算四遍。
    /// 所以先只认真硬件接口；<b>若一块硬件接口都不在线</b>（VPN-only、纯虚拟网络）再退回统计虚拟接口，
    /// 并在返回值里标出用了退路——"永不显示"比"数字偏大"更糟，而两者都不该是静默的。
    /// （真机对照：本机 56 块接口里只有 2 块是真硬件，不区分会把同一份流量算成 7 倍。）
    /// </para>
    /// </summary>
    public static NetworkTotals SumCountedAdapters(IReadOnlyList<NetworkAdapterCounters> adapters)
    {
        var hardware = Sum(adapters, hardwareOnly: true);
        if (hardware.CountedAdapters > 0) return hardware;
        var any = Sum(adapters, hardwareOnly: false);
        // "一块都没计入"不是"走了退路"：那时该说的是"没有可用链路"，标成退路会把界面引向错误的解释
        return any.CountedAdapters > 0 ? any with { CountedViaVirtualFallback = true } : any;
    }

    private static NetworkTotals Sum(IReadOnlyList<NetworkAdapterCounters> adapters, bool hardwareOnly)
    {
        long inTotal = 0, outTotal = 0;
        var counted = 0;
        foreach (var adapter in adapters)
        {
            if (hardwareOnly && !adapter.HardwareInterface) continue;
            if (!CountsTowardNetworkRate(adapter.IfType, adapter.TunnelType, adapter.OperStatus)) continue;
            inTotal += adapter.InBytes;
            outTotal += adapter.OutBytes;
            counted++;
        }
        return new NetworkTotals(inTotal, outTotal, counted, false);
    }

    /// <summary>百分比显示：null ⇒ "--"，其余取整（监控数字小数点没有意义，抖一位会让人误以为在读实时值）。</summary>
    public static string FormatPercent(double? percent)
        => percent is null ? "--"
         : ((int)Math.Round(percent.Value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>0..1 的比值（进度条用）：null 视为"无数据"⇒ 0，界面靠 Error 文本说明原因。</summary>
    public static double ToRatio(double? percent)
        => percent is null ? 0 : Math.Clamp(percent.Value / 100.0, 0, 1);

    /// <summary>
    /// 字节速率显示（"13.3 KB/s"）。<b>基数与舍入一律走 <see cref="FileSizeText"/></b>——全应用只有那一份
    /// 字节显示规则（1024 进制，与资源管理器/任务管理器同基数），这里只负责补上 "/s" 与"无数据"的 "--"。
    /// 网络速率按 ISP 习惯其实该用比特，但同屏混两种基数（内存 1024、网速 1000）才是日后
    /// "为什么和另一个窗口对不上"的源头，故一律跟随 FileSizeText。
    /// </summary>
    public static string FormatRate(double? bytesPerSecond)
    {
        if (bytesPerSecond is null) return "--";
        var value = bytesPerSecond.Value;
        if (double.IsNaN(value) || double.IsInfinity(value)) return "--";
        return FileSizeText.Human((long)Math.Max(value, 0)) + "/s";
    }
}

/// <summary>
/// 滑动平均窗（D7 的 2 秒平均）。
/// <para>
/// 时间轴用 <see cref="Environment.TickCount64"/> 那一类单调毫秒，<b>不要</b>用 UTC 墙钟：
/// 用户改系统时间/时区会让样本瞬间全部超窗（或永远不超窗），而这在开发机上看不出来。
/// </para>
/// </summary>
public sealed class AverageWindow
{
    private readonly List<(long AtMs, double Value)> _samples = new();

    public AverageWindow(int windowMs) => WindowMs = Math.Max(1, windowMs);

    /// <summary>窗长（毫秒）。</summary>
    public int WindowMs { get; }

    /// <summary>当前样本数（测试与诊断用）。</summary>
    public int Count => _samples.Count;

    /// <summary>收一个样本。<b>时间戳早于最新样本的直接丢弃</b>——否则一次时钟回退会让驱逐逻辑反向：
    /// 把最新值留在窗里、把最旧的一直当成"还在窗内"。</summary>
    public void Add(long nowMs, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        if (_samples.Count > 0 && nowMs < _samples[^1].AtMs) return;
        Evict(nowMs);
        _samples.Add((nowMs, value));
    }

    /// <summary>窗内均值；空窗返回 null（"还没有数据"与"均值是 0"必须可分辨）。</summary>
    public double? Average(long nowMs)
    {
        Evict(nowMs);
        if (_samples.Count == 0) return null;
        var sum = 0.0;
        foreach (var sample in _samples) sum += sample.Value;
        return sum / _samples.Count;
    }

    public void Clear() => _samples.Clear();

    private void Evict(long nowMs)
    {
        // 边界取"严格早于窗长才丢"：间隔 1000ms / 窗 2000ms 时窗里稳定留两拍，
        // 若写成 >= 就只剩一拍，平均值退化成一帧原始读数
        var drop = 0;
        while (drop < _samples.Count && nowMs - _samples[drop].AtMs > WindowMs) drop++;
        if (drop > 0) _samples.RemoveRange(0, drop);
    }
}
