#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using StarMark.Abstractions.SystemMonitor;

namespace StarMark.Integrations.SystemMonitor;

/// <summary>CPU 内核时间的一次读数（100 纳秒计）。<see cref="Ok"/> 为 false 时 <see cref="Error"/> 是能直接显示给用户的原因。</summary>
public sealed record CpuTicksReading(bool Ok, long Idle, long Kernel, long User, string? Error);

/// <summary>物理内存的一次读数。</summary>
public sealed record MemoryReading(bool Ok, long TotalBytes, long AvailableBytes, string? Error);

/// <summary>
/// 逐网卡的<b>累计</b>字节数。这里刻意不做差、不算速率、也不判断哪块网卡该计入——
/// 那些是有对错的判定，留在 Core 里可单测（<c>SystemMonitorPolicy</c>）。
/// </summary>
public sealed record NetworkReading(bool Ok, IReadOnlyList<NetworkAdapterCounters> Adapters, string? Error);

/// <summary>
/// 系统指标探针：CPU 时间、物理内存、逐网卡累计字节数。
/// <para>
/// 三条通道<b>各自独立失败并各自带上原因</b>。返回 void 或静默给 0 会让"没测到"和"测到 0"
/// 在界面上长得一模一样——那是这个仓库反复收口过的一类缺陷（LC 的那条谎报成功的出口、MH 的 Ditto 提示）。
/// </para>
/// <para>
/// 探针本身无状态、无后台线程：<b>采样节奏由组件按"窗口是否可见"决定</b>（D7），
/// 所以隐藏起来的组件一次 P/Invoke 都不会发。多个监控组件各采各的（每次都是几个廉价内核调用），
/// 换来的是不必维护"引用计数 + 谁最后退出"这套更容易出错的东西。
/// </para>
/// </summary>
public sealed class SystemMetricsProbe
{
    /// <summary>结构自检只做一次：它查的是本机布局，不会中途变化。</summary>
    private static readonly string? s_layoutError = SystemMonitorNative.LayoutError();

    /// <summary>
    /// CPU 忙闲时间。<c>kernel</c> <b>已包含</b> <c>idle</c>（微软文档里那句话），
    /// 折算成百分比的算术在 <c>SystemMonitorPolicy.CpuPercent</c>。
    /// </summary>
    public CpuTicksReading ReadCpuTicks()
    {
        try
        {
            if (!SystemMonitorNative.GetSystemTimes(out var idle, out var kernel, out var user))
                return new CpuTicksReading(false, 0, 0, 0, $"读取系统时间失败（Win32 {Marshal.GetLastWin32Error()}）");
            return new CpuTicksReading(true, (long)idle, (long)kernel, (long)user, null);
        }
        catch (Exception ex) { return new CpuTicksReading(false, 0, 0, 0, "读取系统时间异常：" + ex.Message); }
    }

    /// <summary>
    /// 物理内存总量与可用量。<c>dwLength</c> 必须按本声明的尺寸填：系统会校验它，
    /// 声明与本机不符时 API 直接返回失败（而不是越界写）——这条通道因此是安全失效的。
    /// </summary>
    public MemoryReading ReadMemory()
    {
        var status = new SystemMonitorNative.MemoryStatusEx
        {
            dwLength = (uint)Marshal.SizeOf<SystemMonitorNative.MemoryStatusEx>(),
        };
        try
        {
            if (!SystemMonitorNative.GlobalMemoryStatusEx(ref status))
                return new MemoryReading(false, 0, 0, $"读取内存状态失败（Win32 {Marshal.GetLastWin32Error()}）");
            if (status.ullTotalPhys == 0)
                return new MemoryReading(false, 0, 0, "系统报告的内存总量为 0");
            return new MemoryReading(true, (long)status.ullTotalPhys, (long)status.ullAvailPhys, null);
        }
        catch (Exception ex) { return new MemoryReading(false, 0, 0, "读取内存状态异常：" + ex.Message); }
    }

    /// <summary>
    /// 逐网卡累计收发字节（<c>GetIfTable2</c>，64 位计数器，因此不需要处理 32 位回绕）。
    /// <para>
    /// 表由系统分配、我们<b>只读</b>，读完立刻拷成托管值再释放——不留悬垂指针。
    /// 释放放在 finally：出错路径上泄漏的会是系统堆里一整张接口表。
    /// </para>
    /// </summary>
    public NetworkReading ReadNetwork()
    {
        if (s_layoutError is not null)
            return new NetworkReading(false, Array.Empty<NetworkAdapterCounters>(), s_layoutError);

        var rows = new List<NetworkAdapterCounters>();
        var seen = new HashSet<int>();
        var table = IntPtr.Zero;
        try
        {
            var code = SystemMonitorNative.GetIfTable2(out table);
            if (code != SystemMonitorNative.NoError || table == IntPtr.Zero)
                return new NetworkReading(false, Array.Empty<NetworkAdapterCounters>(),
                    $"未取到网络接口表（IP Helper 返回 {code}）");

            var count = Marshal.ReadInt32(table, 0);
            if (count is < 0 or > SystemMonitorNative.MaxTrustedRows)
                return Reject($"接口表行数不可信（{count}）");

            for (var i = 0; i < count; i++)
            {
                var row = table + SystemMonitorNative.TableRowsOffset + i * SystemMonitorNative.RowSize;
                var index = Marshal.ReadInt32(row, SystemMonitorNative.OffInterfaceIndex);
                var mtu = Marshal.ReadInt32(row, SystemMonitorNative.OffMtu);
                var type = Marshal.ReadInt32(row, SystemMonitorNative.OffType);
                var tunnel = Marshal.ReadInt32(row, SystemMonitorNative.OffTunnelType);
                var oper = Marshal.ReadInt32(row, SystemMonitorNative.OffOperStatus);
                var flags = Marshal.ReadByte(row, SystemMonitorNative.OffStatusFlags);
                var hardware = (flags & SystemMonitorNative.FlagHardwareInterface) != 0;
                var inbound = Marshal.ReadInt64(row, SystemMonitorNative.OffInOctets);
                var outbound = Marshal.ReadInt64(row, SystemMonitorNative.OffOutOctets);

                // 这道闸门查的是"读得对不对"，不是"网卡正不正常"：步长或偏移错位时读回来的是相邻字节的
                // 碎片，几乎必然落在这些区间外，或让两行报出同一个接口索引。
                // 刻意放过 Mtu/Type 为 0 的接口——回环、WAN Miniport、内核调试网卡本机就有 56 行里的若干条，
                // 把它们当"读错了"会让整张表作废（第一版就栽在要求 Mtu > 0 上）。
                if (index is <= 0 or > 0xFFFF || mtu is < 0 or > 0xFFFF || type is < 0 or > 400 || !seen.Add(index))
                    return Reject("接口表内容与预期不符（已停用网速，避免把内存噪声当成流量）");

                rows.Add(new NetworkAdapterCounters(type, tunnel, oper, hardware, inbound, outbound));
            }
            return new NetworkReading(true, rows, null);
        }
        catch (DllNotFoundException) { return Reject("本机没有 IP Helper（iphlpapi.dll）"); }
        catch (EntryPointNotFoundException) { return Reject("本机 IP Helper 过旧，不支持 GetIfTable2"); }
        catch (Exception ex) { return Reject("读取网络接口表异常：" + ex.Message); }
        finally
        {
            if (table != IntPtr.Zero)
            {
                try { SystemMonitorNative.FreeMibTable(table); }
                catch { /* 释放失败没有可操作的动作，但绝不能让它盖掉真正的读数路径 */ }
            }
        }
    }

    private static NetworkReading Reject(string reason)
        => new(false, Array.Empty<NetworkAdapterCounters>(), reason);
}
