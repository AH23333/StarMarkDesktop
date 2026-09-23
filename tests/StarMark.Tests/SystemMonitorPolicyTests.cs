#nullable enable
using System;
using System.Linq;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 系统监控组件的可机检层（批次 MJ）。
/// <para>
/// 原生采样（<c>GetSystemTimes</c> / <c>GlobalMemoryStatusEx</c> / PDH）在 <c>StarMark.Integrations</c> 里，
/// 本文件钉的是<b>拿到原始读数之后所有会算错的东西</b>：内核计口语义、计数器倒退、滑动平均的窗边界、
/// 采样与降频策略、显示单位。这些一旦写反，真机上只会表现为"数字看着挺像话，但和任务管理器对不上"。
/// </para>
/// </summary>
public sealed class SystemMonitorPolicyTests
{
    // ────────── 落盘形态：flags 只能追加 ──────────

    [Fact]
    public void MetricWireValuesAreFrozenAndAppendOnly()
    {
        Assert.Equal(0, (int)MonitorMetric.None);
        Assert.Equal(1, (int)MonitorMetric.Cpu);
        Assert.Equal(2, (int)MonitorMetric.Memory);
        Assert.Equal(4, (int)MonitorMetric.Network);
        Assert.Equal(7, SystemMonitorPolicy.KnownBits);
    }

    /// <summary>目录必须逐一对上除 None 外的每个旗标——漏一行会让新指标在界面上根本不出现（且构建全绿）。</summary>
    [Fact]
    public void Catalog_CoversEverySelectableMetricExactlyOnce()
    {
        var selectable = Enum.GetValues<MonitorMetric>().Where(m => m != MonitorMetric.None).ToList();
        Assert.True(selectable.Count >= 3, $"可选指标数异常偏少（{selectable.Count}），扫描是否失效");

        var flags = SystemMonitorPolicy.Catalog.Select(e => e.Metric).ToList();
        Assert.Equal(flags.Count, flags.Distinct().Count());                  // 不得重复
        foreach (var metric in selectable)
            Assert.Contains(metric, flags);                                   // 不得漏
        Assert.DoesNotContain(MonitorMetric.None, flags);                     // None 不是可显示行
        Assert.All(SystemMonitorPolicy.Catalog, e => Assert.False(string.IsNullOrWhiteSpace(e.Label)));
        Assert.All(SystemMonitorPolicy.Catalog, e => Assert.False(string.IsNullOrWhiteSpace(e.Glyph)));
    }

    [Fact]
    public void ResolveMetrics_NullMeansNeverConfigured_AndZeroMeansUserUncheckedAll()
    {
        // 这条 null / 0 的分岔是快捷启动那轮的结论：判据不能是"位是不是零"
        Assert.Equal(SystemMonitorPolicy.DefaultMetrics, SystemMonitorPolicy.ResolveMetrics(null));
        Assert.Equal(MonitorMetric.None, SystemMonitorPolicy.ResolveMetrics(0));
        Assert.Equal(MonitorMetric.Cpu | MonitorMetric.Network, SystemMonitorPolicy.ResolveMetrics(5));
    }

    [Theory]
    [InlineData(8, (int)MonitorMetric.None)]          // 未来版本的位：旧版读到必须整位剥掉
    [InlineData(8 | 2, (int)MonitorMetric.Memory)]
    [InlineData(7, (int)(MonitorMetric.Cpu | MonitorMetric.Memory | MonitorMetric.Network))]
    [InlineData(-1, (int)(MonitorMetric.Cpu | MonitorMetric.Memory | MonitorMetric.Network))]
    public void ResolveMetrics_StripsUnknownBits(int wire, int expected)
        => Assert.Equal((MonitorMetric)expected, SystemMonitorPolicy.ResolveMetrics(wire));

    [Fact]
    public void ToWire_DropsUnknownBitsAndRoundTrips()
    {
        Assert.Equal(1, SystemMonitorPolicy.ToWire(MonitorMetric.Cpu));
        Assert.Equal(0, SystemMonitorPolicy.ToWire((MonitorMetric)8));   // 只带未知高位 ⇒ 落盘归零
        foreach (var metric in new[] { MonitorMetric.None, MonitorMetric.Cpu, MonitorMetric.Memory,
                                        MonitorMetric.Network, MonitorMetric.Cpu | MonitorMetric.Memory })
            Assert.Equal(metric, SystemMonitorPolicy.ResolveMetrics(SystemMonitorPolicy.ToWire(metric)));
    }

    [Fact]
    public void RowsFor_FollowsCatalogOrder_AndSkipsUnchecked()
    {
        var all = SystemMonitorPolicy.RowsFor(SystemMonitorPolicy.DefaultMetrics).Select(r => r.Metric).ToList();
        Assert.Equal(SystemMonitorPolicy.Catalog.Select(c => c.Metric).ToList(), all);   // 顺序＝目录顺序
        Assert.Empty(SystemMonitorPolicy.RowsFor(MonitorMetric.None));
        Assert.Equal(new[] { MonitorMetric.Cpu, MonitorMetric.Network },
            SystemMonitorPolicy.RowsFor(MonitorMetric.Network | MonitorMetric.Cpu).Select(r => r.Metric));
    }

    // ────────── 采样与降频策略（D7） ──────────

    [Fact]
    public void SampleInterval_NotVisibleMeansNoSamplingAtAll()
    {
        Assert.Equal(0, SystemMonitorPolicy.SampleIntervalMs(windowVisible: false, resourceSaver: false));
        Assert.Equal(0, SystemMonitorPolicy.SampleIntervalMs(windowVisible: false, resourceSaver: true));
        Assert.Equal(1_000, SystemMonitorPolicy.SampleIntervalMs(windowVisible: true, resourceSaver: false));
        Assert.Equal(3_000, SystemMonitorPolicy.SampleIntervalMs(windowVisible: true, resourceSaver: true));
        // 省资源必须更慢（"降频"若写成升频就是一行静默的反向改动）
        Assert.True(SystemMonitorPolicy.ResourceSaverIntervalMs > SystemMonitorPolicy.BalancedIntervalMs);
    }

    [Fact]
    public void WindowMs_IsAlwaysAtLeastTwoSamples_AndFloorsAtTwoSeconds()
    {
        Assert.Equal(0, SystemMonitorPolicy.WindowMs(0));
        Assert.Equal(0, SystemMonitorPolicy.WindowMs(-5));
        Assert.Equal(2_000, SystemMonitorPolicy.WindowMs(900));    // 下限兜住
        Assert.Equal(2_000, SystemMonitorPolicy.WindowMs(1_000));
        Assert.Equal(3_000, SystemMonitorPolicy.WindowMs(1_500));  // 超过下限后按两拍走
        Assert.Equal(6_000, SystemMonitorPolicy.WindowMs(3_000));
    }

    /// <summary>窗比间隔短 ⇒ 每拍只剩单点值，"平均"静默退化。逐个间隔都验一遍，防止有人只改了常量。</summary>
    [Fact]
    public void WindowMs_NeverShorterThanTwoIntervals()
    {
        for (var interval = 1; interval <= 20_000; interval += 137)
            Assert.True(SystemMonitorPolicy.WindowMs(interval) >= interval * 2,
                $"间隔 {interval}ms 时窗只有 {SystemMonitorPolicy.WindowMs(interval)}ms，平均已退化");
    }

    // ────────── CPU：内核计口语义 ──────────

    /// <summary>
    /// GetSystemTimes 的 kernel <b>含</b> idle。按"不含"写会得到 total=kernel+user+idle、busy=kernel+user，
    /// 同一组读数下报出的百分比明显偏高（这组数：正确 50%，写反 66.7%）——正是与任务管理器对不上的形状。
    /// </summary>
    [Fact]
    public void CpuPercent_KernelAlreadyIncludesIdle()
    {
        var prev = new CpuCounters(0, 0, 0);
        var cur = new CpuCounters(Idle: 500, Kernel: 600, User: 400);
        Assert.Equal(50.0, SystemMonitorPolicy.CpuPercent(prev, cur)!.Value, 6);
    }

    [Fact]
    public void CpuPercent_Endpoints()
    {
        // 全闲：kernel 全是 idle、无用户时间
        Assert.Equal(0.0, SystemMonitorPolicy.CpuPercent(
            new CpuCounters(0, 0, 0), new CpuCounters(1_000, 1_000, 0))!.Value, 6);
        // 全忙：idle 不增，kernel/user 各占一半
        Assert.Equal(100.0, SystemMonitorPolicy.CpuPercent(
            new CpuCounters(500, 500, 500), new CpuCounters(500, 600, 600))!.Value, 6);
        // 纯用户态负载
        Assert.Equal(40.0, SystemMonitorPolicy.CpuPercent(
            new CpuCounters(0, 0, 0), new CpuCounters(60, 60, 40))!.Value, 6);
    }

    [Theory]
    [InlineData(0, 0, 0, -1, 10, 10)]    // idle 倒退
    [InlineData(0, 0, 0, 10, -1, 10)]    // kernel 倒退
    [InlineData(0, 0, 0, 10, 10, -1)]    // user 倒退
    [InlineData(0, 0, 0, 5, 3, 1)]       // idle > kernel：读数自相矛盾 ⇒ busy 为负
    public void CpuPercent_RejectsImpossibleReadings(long pi, long pk, long pu, long ci, long ck, long cu)
        => Assert.Null(SystemMonitorPolicy.CpuPercent(new CpuCounters(pi, pk, pu), new CpuCounters(ci, ck, cu)));

    [Fact]
    public void CpuPercent_RejectsNoNewData()
        => Assert.Null(SystemMonitorPolicy.CpuPercent(new CpuCounters(7, 7, 7), new CpuCounters(7, 7, 7)));

    [Fact]
    public void CpuPercent_NeverExceedsHundred()
    {
        // 荒谬输入（读数被别的实现填错）也必须落在 0..100：进度条按 0..1 换算，越界会画出格
        var percent = SystemMonitorPolicy.CpuPercent(new CpuCounters(0, 0, 0), new CpuCounters(1, 1_000, 1_000));
        Assert.NotNull(percent);
        Assert.InRange(percent!.Value, 0, 100);
    }

    // ────────── 内存与速率 ──────────

    [Fact]
    public void MemoryPercent_BasicAndClamped()
    {
        Assert.Equal(25.0, SystemMonitorPolicy.MemoryPercent(4_000, 3_000)!.Value, 6);
        Assert.Equal(100.0, SystemMonitorPolicy.MemoryPercent(4_000, 0)!.Value, 6);
        Assert.Equal(0.0, SystemMonitorPolicy.MemoryPercent(4_000, 4_000)!.Value, 6);
    }

    [Theory]
    [InlineData(0, 0)]        // 读数失败（总量 0）不能显示成"0% 占用"
    [InlineData(100, -1)]     // 可用为负
    [InlineData(100, 101)]    // 可用大于总量
    [InlineData(-5, 1)]       // 总量为负
    public void MemoryPercent_ReturnsNullOnUnusableReading(long total, long available)
        => Assert.Null(SystemMonitorPolicy.MemoryPercent(total, available));

    [Fact]
    public void RatePerSecond_HappyPathAndZeroElapsed()
    {
        Assert.Equal(500.0, SystemMonitorPolicy.RatePerSecond(1_000, 2_000, 2.0)!.Value, 6);
        Assert.Equal(0.0, SystemMonitorPolicy.RatePerSecond(1_000, 1_000, 1.0)!.Value, 6);
        Assert.Null(SystemMonitorPolicy.RatePerSecond(1_000, 2_000, 0));       // 同一瞬间：没有"每秒"可言
        Assert.Null(SystemMonitorPolicy.RatePerSecond(1_000, 2_000, -1));
    }

    /// <summary>
    /// 计数倒退必须返回 null，<b>既不钳 0 也不补 2³²</b>：网卡重置与 32 位计数器回绕在读数上无法区分，
    /// 补 2³² 会把一次拔网线画成一根几十 GB 的尖峰，钳 0 会画成"突然断网"——两者都是假数据。
    /// </summary>
    [Fact]
    public void RatePerSecond_CounterDecreaseIsNullNotZeroNorWrapped()
    {
        Assert.Null(SystemMonitorPolicy.RatePerSecond(5_000, 1_000, 1.0));
        Assert.Null(SystemMonitorPolicy.RatePerSecond(long.MaxValue / 2, 0, 1.0));
    }

    // ────────── 显示串 ──────────

    [Fact]
    public void FormatPercent_UsesAwayFromZeroRounding()
    {
        // 天气那次是"半度少 1"（银行家舍入）栽的，这里直接把 0.5 的走向钉死
        Assert.Equal("--", SystemMonitorPolicy.FormatPercent(null));
        Assert.Equal("0%", SystemMonitorPolicy.FormatPercent(0.4));
        Assert.Equal("1%", SystemMonitorPolicy.FormatPercent(0.5));
        Assert.Equal("2%", SystemMonitorPolicy.FormatPercent(1.5));
        Assert.Equal("100%", SystemMonitorPolicy.FormatPercent(99.5));
    }

    [Fact]
    public void FormatPercent_NullIsDistinguishableFromZero()
    {
        Assert.Equal("--", SystemMonitorPolicy.FormatPercent(null));
        Assert.Equal("0%", SystemMonitorPolicy.FormatPercent(0));
    }

    [Fact]
    public void ToRatio_ClampsAndMapsNullToZero()
    {
        Assert.Equal(0, SystemMonitorPolicy.ToRatio(null));
        Assert.Equal(0.5, SystemMonitorPolicy.ToRatio(50), 6);
        Assert.Equal(1, SystemMonitorPolicy.ToRatio(100));
        Assert.Equal(1, SystemMonitorPolicy.ToRatio(180));     // 越界画不出格
        Assert.Equal(0, SystemMonitorPolicy.ToRatio(-20));
    }

    [Fact]
    public void FormatRate_UnitsAreBinaryAndBoundaryIsExact()
    {
        Assert.Equal("--", SystemMonitorPolicy.FormatRate(null));
        Assert.Equal("0 B/s", SystemMonitorPolicy.FormatRate(0));
        Assert.Equal("1023 B/s", SystemMonitorPolicy.FormatRate(1023));
        Assert.Equal("1.0 KB/s", SystemMonitorPolicy.FormatRate(1024));
        Assert.Equal("1.0 MB/s", SystemMonitorPolicy.FormatRate(1024 * 1024));
        Assert.Equal("14 MB/s", SystemMonitorPolicy.FormatRate(15_000_000));   // ≥10 不再留小数
        Assert.Equal("1.0 GB/s", SystemMonitorPolicy.FormatRate(1024d * 1024 * 1024));
        Assert.Equal("1024 PB/s", SystemMonitorPolicy.FormatRate(1024d * 1024 * 1024 * 1024 * 1024 * 1024));   // 单位到顶后不再进位
    }

    [Fact]
    public void FormatRate_NegativeIsClampedWhileNaNAndInfinitySayDashes()
    {
        Assert.Equal("0 B/s", SystemMonitorPolicy.FormatRate(-50));
        Assert.Equal("--", SystemMonitorPolicy.FormatRate(double.NaN));
        Assert.Equal("--", SystemMonitorPolicy.FormatRate(double.PositiveInfinity));
    }

    [Fact]
    public void FormatBytes_DecimalPlacesOnlyBelowTen()
    {
        Assert.Equal("--", SystemMonitorPolicy.FormatBytes(null));
        Assert.Equal("512 B", SystemMonitorPolicy.FormatBytes(512));
        Assert.Equal("1.0 GB", SystemMonitorPolicy.FormatBytes(1024d * 1024 * 1024));
        Assert.Equal("7.4 GB", SystemMonitorPolicy.FormatBytes(1024d * 1024 * 1024 * 7.4));   // <10 才留小数
        Assert.Equal("12 GB", SystemMonitorPolicy.FormatBytes(1024d * 1024 * 1024 * 12.4));
        Assert.Equal("16 GB", SystemMonitorPolicy.FormatBytes(16d * 1024 * 1024 * 1024));
    }

    /// <summary>显示串必须与文化无关（实现里显式走 InvariantCulture），否则某些机器上会出现"12,4 GB"。</summary>
    [Fact]
    public void FormattedNumbers_NeverUseCommaAsDecimalSeparator()
    {
        foreach (var text in new[]
                 {
                     SystemMonitorPolicy.FormatBytes(1024d * 1024 * 1024 * 5.34),
                     SystemMonitorPolicy.FormatRate(1024 * 1024 * 5.5),
                 })
        {
            Assert.DoesNotContain(",", text);
            Assert.Contains(".", text);
        }
    }

    // ────────── 滑动平均窗 ──────────

    [Fact]
    public void AverageWindow_EmptyUntilFirstSample()
    {
        var window = new AverageWindow(2_000);
        Assert.Null(window.Average(0));
        Assert.Equal(0, window.Count);
    }

    [Fact]
    public void AverageWindow_AveragesSamplesInsideWindow()
    {
        var window = new AverageWindow(2_000);
        window.Add(0, 10);
        window.Add(1_000, 20);
        Assert.Equal(15.0, window.Average(1_000)!.Value, 6);
        Assert.Equal(2, window.Count);
    }

    /// <summary>窗边界取"严格超过才丢"：间隔 1000 / 窗 2000 时第 2000ms 这一拍必须还留着两个样本，
    /// 否则平均静默退化成一帧原始读数（D7 要的正是平滑）。</summary>
    [Fact]
    public void AverageWindow_KeepsSampleExactlyOneWindowOld()
    {
        var window = new AverageWindow(2_000);
        window.Add(0, 100);
        Assert.Equal(100.0, window.Average(2_000)!.Value, 6);   // 边界：仍在窗内
        Assert.Null(window.Average(2_001));                     // 超出：整窗清空 ⇒ null，不是 0
    }

    [Fact]
    public void AverageWindow_DropsOlderSamplesOnly()
    {
        var window = new AverageWindow(2_000);
        window.Add(0, 100);
        window.Add(1_500, 10);
        window.Add(3_000, 20);            // 第 0ms 那个已超窗（3000-0 > 2000）
        Assert.Equal(2, window.Count);
        Assert.Equal(15.0, window.Average(3_000)!.Value, 6);
    }

    [Fact]
    public void AverageWindow_IgnoresOutOfOrderAndNonFiniteSamples()
    {
        var window = new AverageWindow(2_000);
        window.Add(5_000, 40);
        window.Add(4_000, 100);                                   // 时间倒退：必须整条丢弃，不能污染均值
        Assert.Equal(40.0, window.Average(5_000)!.Value, 6);
        window.Add(6_000, double.NaN);
        window.Add(7_000, double.PositiveInfinity);
        Assert.Equal(1, window.Count);                            // 三条都被丢弃：只剩 5000 那一拍
        Assert.Equal(40.0, window.Average(7_000)!.Value, 6);
    }

    [Fact]
    public void AverageWindow_ClearResetsAndSmallWindowIsFloored()
    {
        var window = new AverageWindow(0);                        // 0/负窗长不可用 ⇒ 兜到 1ms
        Assert.Equal(1, window.WindowMs);
        window.Add(0, 5);
        Assert.Equal(5.0, window.Average(0)!.Value, 6);
        window.Clear();
        Assert.Null(window.Average(0));
    }

    /// <summary>组件隐藏后重新显示：旧样本必须已经全部出窗，否则"刚回来"会先显示离开前的数字。</summary>
    [Fact]
    public void AverageWindow_GivesNoAnswerAfterLongIdle()
    {
        var window = new AverageWindow(2_000);
        window.Add(0, 55);
        Assert.Null(window.Average(600_000));
        Assert.Equal(0, window.Count);
    }
}
