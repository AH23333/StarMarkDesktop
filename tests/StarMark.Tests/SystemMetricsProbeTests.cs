#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.SystemMonitor;
using StarMark.Core.Widgets;
using StarMark.Integrations.SystemMonitor;
using Xunit;
using Xunit.Abstractions;

namespace StarMark.Tests;

/// <summary>
/// 系统监控探针与判据（批次 MK）。
/// <para>
/// 分两半：<b>布局自检</b>那几条是确定性的（只依赖 .NET 的布局引擎与我从 SDK 头文件推得的常量，
/// 在任何 x64 Windows 上都应成立）；<b>真机读数</b>那几条刻意写成"要么读到合理值，要么给出原因"，
/// 因为它们会跑在没有网卡的机器上——硬断言只会变成一台机器上过、另一台上红的假缺陷。
/// </para>
/// </summary>
public sealed class SystemMetricsProbeTests
{
    private readonly ITestOutputHelper _out;

    public SystemMetricsProbeTests(ITestOutputHelper output) => _out = output;

    // ────────── 结构体布局：两份独立声明必须互证 ──────────

    /// <summary>
    /// 这条测试是本组件最重要的一条：网速那一行要按字节偏移去读系统分配的接口表，
    /// 偏移/步长算错的结果是"读到一个看起来像网速的内存碎片"，而不是崩溃或异常。
    /// 所以把 SDK 推得的 1352 与逐字段偏移同时钉在两份声明上（常量一份、Sequential 镜像一份）。
    /// </summary>
    [Fact]
    public void IfRow2Layout_SelfCheckPasses()
    {
        Assert.Equal(1352, SystemMonitorNative.RowSize);
        Assert.Equal(8, SystemMonitorNative.TableRowsOffset);
        Assert.Equal(1352, SystemMonitorNative.MirrorRowSize);
        Assert.Null(SystemMonitorNative.LayoutError());
    }

    /// <summary>偏移必须严格递增且各字段不相撞——写重复一个 FieldOffset 会静默读到隔壁字段。</summary>
    [Fact]
    public void IfRow2FieldOffsets_AreOrderedAndDistinct()
    {
        var offsets = new[]
        {
            SystemMonitorNative.OffInterfaceIndex, SystemMonitorNative.OffMtu, SystemMonitorNative.OffType,
            SystemMonitorNative.OffTunnelType, SystemMonitorNative.OffOperStatus,
            SystemMonitorNative.OffInOctets, SystemMonitorNative.OffOutOctets,
        };
        Assert.Equal(offsets.Length, offsets.Distinct().Count());
        Assert.True(offsets.All(o => o is >= 0 and < SystemMonitorNative.RowSize));
        Assert.True(SystemMonitorNative.OffInOctets < SystemMonitorNative.OffOutOctets);
    }

    // ────────── 真机读数：要么合理，要么说明原因 ──────────

    [Fact]
    public void Probe_CpuTicks_IsReadableAndSelfConsistent()
    {
        var probe = new SystemMetricsProbe();
        var first = probe.ReadCpuTicks();
        Assert.True(first.Ok, first.Error);
        // kernel 含 idle ⇒ 恒有 kernel >= idle；不成立就说明三个出参的取值顺序被写坏了
        Assert.True(first.Kernel >= first.Idle, $"kernel={first.Kernel} idle={first.Idle}");
        Assert.True(first.Idle >= 0 && first.User >= 0);
    }

    [Fact]
    public void Probe_CpuTicks_NeverGoesBackwards()
    {
        var probe = new SystemMetricsProbe();
        var first = probe.ReadCpuTicks();
        var second = probe.ReadCpuTicks();
        Assert.True(second.Ok, second.Error);
        var percent = SystemMonitorPolicy.CpuPercent(
            new CpuCounters(first.Idle, first.Kernel, first.User),
            new CpuCounters(second.Idle, second.Kernel, second.User));
        // 连续两次读数的差值必然是可用数据（同一瞬间除外），且必须落在 0..100
        Assert.True(percent is null || (percent.Value >= 0 && percent.Value <= 100), $"CPU 读数越界：{percent}");
    }

    [Fact]
    public void Probe_Memory_ReportsPlausibleTotals()
    {
        var reading = new SystemMetricsProbe().ReadMemory();
        Assert.True(reading.Ok, reading.Error);
        Assert.InRange(reading.TotalBytes, 256L * 1024 * 1024, 1L << 44);
        Assert.InRange(reading.AvailableBytes, 0, reading.TotalBytes);
        var percent = SystemMonitorPolicy.MemoryPercent(reading.TotalBytes, reading.AvailableBytes);
        Assert.NotNull(percent);
        Assert.InRange(percent!.Value, 0, 100);
        _out.WriteLine($"内存：{FileSizeTextProbe(reading.AvailableBytes)} / {FileSizeTextProbe(reading.TotalBytes)} 可用");
    }

    [Fact]
    public void Probe_Network_EitherReturnsSaneRowsOrAReason()
    {
        var reading = new SystemMetricsProbe().ReadNetwork();
        if (!reading.Ok)
        {
            Assert.False(string.IsNullOrWhiteSpace(reading.Error), "不可用必须带原因");
            return;
        }
        Assert.All(reading.Adapters, a =>
        {
            Assert.True(a.OperStatus >= 0 && a.OperStatus <= 8, $"OperStatus={a.OperStatus}");
            Assert.True(a.IfType > 0 && a.IfType <= 400, $"IfType={a.IfType}");
            Assert.True(a.InBytes >= 0 && a.OutBytes >= 0);
        });
        var sum = SystemMonitorPolicy.SumCountedAdapters(reading.Adapters);
        var hardware = reading.Adapters.Count(a => a.HardwareInterface);
        _out.WriteLine($"接口 {reading.Adapters.Count} 块（真硬件 {hardware}），计入 {sum.CountedAdapters} 块" +
                       (sum.CountedViaVirtualFallback ? "（走虚拟接口退路）" : "") +
                       $"，累计 ↓{FileSizeTextProbe(sum.InBytes)} ↑{FileSizeTextProbe(sum.OutBytes)}");
    }

    /// <summary>
    /// 累计计数只会往前走，而且必须真的在动。
    /// <para>
    /// <b>比较的是"所有接口之和"，不是"计入展示的那几个"</b>：这一条要验的是"偏移读到的到底是不是真计数器"，
    /// 而展示口径该包含哪些网卡另有 <see cref="SystemMonitorPolicy"/> 的用例在守。两件判据混进同一个断言，
    /// 就会出现"这台机器恰好走在一块不计入的网卡上"那种误报——偏移读错时所有接口都是常量，换基数照样抓得住。
    /// </para>
    /// </summary>
    [Fact]
    public void Probe_Network_CountersAdvanceWithTraffic()
    {
        var probe = new SystemMetricsProbe();
        var first = probe.ReadNetwork();
        Assert.True(first.Ok, first.Error);
        if (first.Adapters.Count == 0) return;         // 这台机器当时没有任何链路：无话可说，不算失败
        var before = Total(first.Adapters);
        var beforeCounted = SystemMonitorPolicy.SumCountedAdapters(first.Adapters);

        ConsumeNetworkTraffic();
        var second = probe.ReadNetwork();
        Assert.True(second.Ok, second.Error);

        // 中途有网卡上线/掉线（插拔、VPN 切换）时两份清单不可比：这不算读错，也不算通过
        if (second.Adapters.Count != first.Adapters.Count)
        {
            _out.WriteLine($"网络接口在两次读数之间变了（{first.Adapters.Count}→{second.Adapters.Count}），本条按不适用跳过");
            return;
        }

        var after = Total(second.Adapters);
        var counted = SystemMonitorPolicy.SumCountedAdapters(second.Adapters);

        Assert.True(counted.InBytes >= beforeCounted.InBytes && counted.OutBytes >= beforeCounted.OutBytes,
            $"计入展示的合计倒退：↓{beforeCounted.InBytes}→{counted.InBytes} ↑{beforeCounted.OutBytes}→{counted.OutBytes}");
        Assert.True(after.InBytes >= before.InBytes && after.OutBytes >= before.OutBytes,
            $"计数倒退：↓{before.InBytes}→{after.InBytes} ↑{before.OutBytes}→{after.OutBytes}");
        Assert.True(after.OutBytes > before.OutBytes || after.InBytes > before.InBytes,
            $"发过真实查询（{TrafficQueries} 次递归解析）后全机字节数一点没涨：↓{before.InBytes}→{after.InBytes} " +
            $"↑{before.OutBytes}→{after.OutBytes}——先怀疑偏移读错了字段");
    }

    /// <summary>全部接口的累计字节（不含任何"该不该计入"的判断）。</summary>
    private static (long InBytes, long OutBytes) Total(IReadOnlyList<NetworkAdapterCounters> adapters)
        => (adapters.Sum(a => a.InBytes), adapters.Sum(a => a.OutBytes));

    /// <summary>
    /// 制造一点真实流量。<b>必须是真的会离开这块网卡的包</b>：原先解析 "localhost" 由 hosts 文件/缓存
    /// 就地答完，一个字节都不上网线，于是"有网卡却一次没涨"变成了必然失败（本机实测踩过）。
    /// <para>这里查 <c>*.example</c>——<c>.example</c> 是 RFC 2606 Reserved TLD，永远不会被注册，
    /// 每次都是一次确定性的 NXDOMAIN 递归查询，走的正是默认路由那块网卡；结果注定是失败，所以不依赖外网可达。</para>
    /// </summary>
    private static int TrafficQueries;

    private static void ConsumeNetworkTraffic()
    {
        try
        {
            for (TrafficQueries = 0; TrafficQueries < 3; TrafficQueries++)
                System.Net.Dns.GetHostAddresses("starmark-probe-" + Guid.NewGuid().ToString("N") + ".example");
            System.Net.Dns.GetHostEntry("localhost");
            System.Threading.Thread.Sleep(400);
        }
        catch (Exception) { /* 没有流量也无妨：上面的断言在有流量时才要求增长 */ }
    }

    // ────────── 哪些网卡该计入（真机上最容易翻车的一条判定） ──────────

    [Fact]
    public void CountsTowardNetworkRate_RejectsLoopbackTunnelsAndDownLinks()
    {
        Assert.True(SystemMonitorPolicy.CountsTowardNetworkRate(6, 0, 1));       // 以太
        Assert.True(SystemMonitorPolicy.CountsTowardNetworkRate(71, 0, 1));      // Wi-Fi
        Assert.False(SystemMonitorPolicy.CountsTowardNetworkRate(24, 0, 1));     // 软件回环
        Assert.False(SystemMonitorPolicy.CountsTowardNetworkRate(131, 10, 1));   // Teredo 隧道
        Assert.False(SystemMonitorPolicy.CountsTowardNetworkRate(6, 0, 2));      // 链路未起
        Assert.False(SystemMonitorPolicy.CountsTowardNetworkRate(6, 0, 0));      // 状态未知
        // 真硬件与否由 SumCountedAdapters 分趟处理，不在这条判定里
        Assert.True(SystemMonitorPolicy.CountsTowardNetworkRate(6, 0, 1));
    }

    /// <summary>
    /// 真机上实测到 4 块虚拟网卡报着与物理网卡完全相同的计数器（托管网络 / Wi-Fi Direct 镜像），
    /// 全加等于把同一份流量算四遍 ⇒ 求和必须只认真硬件接口，并回报加了几块。
    /// </summary>
    [Fact]
    public void SumCountedAdapters_ExcludesMirroredVirtualAdaptersAndTunnels()
    {
        var rows = new List<NetworkAdapterCounters>
        {
            new(IfType: 71, TunnelType: 0, OperStatus: 1, HardwareInterface: true, InBytes: 1_000, OutBytes: 200),   // Wi-Fi（算）
            new(IfType: 6, TunnelType: 0, OperStatus: 1, HardwareInterface: false, InBytes: 1_000, OutBytes: 200),   // 托管网络镜像（不算）
            new(IfType: 131, TunnelType: 10, OperStatus: 1, HardwareInterface: false, InBytes: 1_000, OutBytes: 200),// Teredo（不算）
            new(IfType: 24, TunnelType: 0, OperStatus: 1, HardwareInterface: true, InBytes: 9_999, OutBytes: 9_999), // 回环（不算）
            new(IfType: 6, TunnelType: 0, OperStatus: 6, HardwareInterface: true, InBytes: 5_000, OutBytes: 5_000),  // 断开的以太（不算）
        };
        var sum = SystemMonitorPolicy.SumCountedAdapters(rows);
        Assert.Equal(1, sum.CountedAdapters);
        Assert.Equal(1_000, sum.InBytes);
        Assert.Equal(200, sum.OutBytes);
        Assert.False(sum.CountedViaVirtualFallback);
    }

    /// <summary>只有虚拟链路在线时（VPN / 纯虚拟网络）宁给虚拟接口的数并标明走了退路，也不要把网速行永久空着。</summary>
    [Fact]
    public void SumCountedAdapters_FallsBackToVirtualAdaptersWhenNoHardwareIsUp()
    {
        var rows = new List<NetworkAdapterCounters>
        {
            new(IfType: 6, TunnelType: 0, OperStatus: 1, HardwareInterface: false, InBytes: 700, OutBytes: 90),
            new(IfType: 6, TunnelType: 0, OperStatus: 1, HardwareInterface: false, InBytes: 300, OutBytes: 10),
        };
        var sum = SystemMonitorPolicy.SumCountedAdapters(rows);
        Assert.Equal(2, sum.CountedAdapters);
        Assert.Equal(1_000, sum.InBytes);
        Assert.Equal(100, sum.OutBytes);
        Assert.True(sum.CountedViaVirtualFallback);
    }

    /// <summary>空表必须报"一块都没计入"，而不是 0 字节——界面靠这个区分"没链路"与"网速是 0"。</summary>
    [Fact]
    public void SumCountedAdapters_EmptyTableIsZeroAdaptersNotZeroSpeed()
    {
        var sum = SystemMonitorPolicy.SumCountedAdapters(Array.Empty<NetworkAdapterCounters>());
        Assert.Equal(0, sum.CountedAdapters);
        Assert.Equal(0, sum.InBytes);
        Assert.False(sum.CountedViaVirtualFallback);
    }

    // ────────── 显示串：与全应用同一份字节规则 ──────────

    [Fact]
    public void FormatRate_DelegatesUnitAndRoundingToSharedByteFormatter()
    {
        Assert.Equal("--", SystemMonitorPolicy.FormatRate(null));
        Assert.Equal("0 B/s", SystemMonitorPolicy.FormatRate(0));
        Assert.Equal("1 KB/s", SystemMonitorPolicy.FormatRate(1024));
        Assert.Equal("13.3 KB/s", SystemMonitorPolicy.FormatRate(13_647));
        Assert.Equal("1 MB/s", SystemMonitorPolicy.FormatRate(1024 * 1024));
        Assert.Equal("0 B/s", SystemMonitorPolicy.FormatRate(-50));           // 夹住，不显示负速率
        Assert.Equal("--", SystemMonitorPolicy.FormatRate(double.NaN));
        Assert.Equal("--", SystemMonitorPolicy.FormatRate(double.PositiveInfinity));
        // 与 FileSizeText 同基数：同一字节数在两处必须只差一个 "/s"
        Assert.Equal(StarMark.Abstractions.FileSizeText.Human(13_647) + "/s", SystemMonitorPolicy.FormatRate(13_647));
    }

    private static string FileSizeTextProbe(long bytes) => StarMark.Abstractions.FileSizeText.Human(bytes);

    private static string FileSizeTextProbe(double bytes) => StarMark.Abstractions.FileSizeText.Human((long)bytes);
}
