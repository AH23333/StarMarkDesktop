#nullable enable
using System;
using System.Runtime.InteropServices;

namespace StarMark.Integrations.SystemMonitor;

/// <summary>
/// 系统监控组件用到的原生声明：<c>GetSystemTimes</c> / <c>GlobalMemoryStatusEx</c> / <c>GetIfTable2</c>。
/// <para>
/// <b>为什么零 NuGet、也为什么刻意不用 PDH</b>（可行性分析 §2.6 原方案里网络走 PDH）：本机实测
/// <c>typeperf "\Network Interface(_Total)\Bytes Received/sec"</c> 稳定返回 <c>-1</c>（无数据），
/// 只有 <c>(*)</c> 逐网卡实例才有值；而把所有实例加起来会把 <b>Teredo / ISATAP 这类隧道网卡</b>
/// 与它下面的物理网卡<b>各记一遍</b>（同一份流量），PDH 的实例数组里又没有接口类型可供过滤。
/// <c>GetIfTable2</c> 一次调用就能拿到带 <c>Type</c> / <c>TunnelType</c> 的 64 位累计字节数，
/// 于是"哪些网卡该计入"能按接口自己声明的类型位判定（判定本身在 Core，可单测）。
/// </para>
/// </summary>
public static class SystemMonitorNative
{
    public const uint NoError = 0;

    // ── MIB_IF_ROW2 布局（逐字节推导自 SDK：shared/netioapi.h 的结构体 + shared/ifdef.h 的两个长度） ──
    //
    //   0 InterfaceLuid(8, 对齐 8) │ 8 InterfaceIndex(4) │ 12 InterfaceGuid(16, 对齐 4)
    //  28 Alias[257] WCHAR(514)    │ 542 Description[257](514)
    // 1056 PhysicalAddressLength(4)│ 1060 PhysicalAddress[32] │ 1092 PermanentPhysicalAddress[32]
    // 1124 Mtu │ 1128 Type │ 1132 TunnelType │ 1136 MediaType │ 1140 PhysicalMediumType
    // 1144 AccessType │ 1148 DirectionType │ 1152 位域(1 字节) →（补 3）
    // 1156 OperStatus │ 1160 AdminStatus │ 1164 MediaConnectState │ 1168 NetworkGuid(16) │ 1184 ConnectionType
    //     →（补 4，因为下一个是 ULONG64）
    // 1192 TransmitLinkSpeed │ 1200 ReceiveLinkSpeed │ 1208 InOctets │ …8 个 In* │ 1280 OutOctets │ …7 个 Out*
    // 1344 OutQLen → 结构体总长 1352（对齐 8）
    //
    // MIB_IF_TABLE2 = { ULONG NumEntries; MIB_IF_ROW2 Table[]; } ⇒ 行数组起始偏移 8（行要求 8 字节对齐）。

    /// <summary>一行（MIB_IF_ROW2）的字节数与步长。</summary>
    public const int RowSize = 1352;

    /// <summary><c>MIB_IF_TABLE2</c> 里行数组相对表首的偏移。</summary>
    public const int TableRowsOffset = 8;

    public const int OffInterfaceIndex = 8;
    public const int OffMtu = 1124;
    public const int OffType = 1128;
    public const int OffTunnelType = 1132;
    public const int OffOperStatus = 1156;
    public const int OffStatusFlags = 1152;   // 8×BOOLEAN 位域：bit0=HardwareInterface，bit1=FilterInterface，bit2=ConnectorPresent
    public const int OffInOctets = 1208;
    public const int OffOutOctets = 1280;

    /// <summary>接口行数上限。真实机器几十行封顶，超出即认为读数不可信（下面要按它做越界判断）。</summary>
    public const int MaxTrustedRows = 512;

    // IFTYPE / IF_OPER_STATUS / TUNNEL_TYPE 的取值（Core 侧有一份同名常量，两处各钉一次是有意的：
    // 这边是"原生值"，那边是"判定用到的值"，改任何一处都会让交叉检查红）。
    public const uint IfTypeSoftwareLoopback = 24;
    public const uint IfOperStatusUp = 1;
    public const uint TunnelTypeNone = 0;

    /// <summary>InterfaceAndOperStatusFlags.HardwareInterface（位域从低位起排）。</summary>
    public const byte FlagHardwareInterface = 0x01;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out ulong lpIdleTime, out ulong lpKernelTime, out ulong lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>
    /// 唯一被"原生代码写进来"的结构。它自带 <c>dwLength</c> 供系统校验：声明尺寸与本机真实尺寸不符时
    /// API 直接返回失败而不是越界写 ⇒ 这条通道天然是安全失效（fail-closed）。
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    /// <summary>返回 OS 分配的表（须用 <see cref="FreeMibTable"/> 释放）。我们只<b>读</b>这块内存。</summary>
    [DllImport("iphlpapi.dll", EntryPoint = "GetIfTable2")]
    public static extern uint GetIfTable2(out IntPtr pIfTable);

    [DllImport("iphlpapi.dll", EntryPoint = "FreeMibTable")]
    public static extern void FreeMibTable(IntPtr pMemoryAllocated);

    /// <summary>
    /// 与上面那份"偏移常量"完全独立地、再按 <see cref="LayoutKind.Sequential"/> 声明一遍同一结构体，
    /// 让 .NET 的布局引擎替我算一次尺寸与各字段偏移。
    /// <para>
    /// 两份声明各错各的 ⇒ 只要有一处与常量不符，<see cref="LayoutError"/> 就会给出原因，
    /// 网速那一行改说"结构校验未通过"，<b>一次原生内存都不去读</b>。
    /// 这一步不是仪式：结构体步长算大会让最后一行读到表尾之外（越界读），而那种错在开发机上通常表现为"偶尔正常"。
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MibIfRow2Mirror
    {
        public ulong InterfaceLuid;                       // NET_LUID：ULONG64 + 位域联合体，8 字节、对齐 8
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        // ArraySubType 必须显式写 U2：.NET 对 char[] 的 ByValArray 默认按 1 字节封送，
        // 于是这两行各少 257 字节、整份声明算出 840 而不是 1352（自检第一次跑就红了这件事）
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 257, ArraySubType = UnmanagedType.U2)] public char[] Alias;      // IF_MAX_STRING_SIZE + 1
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 257, ArraySubType = UnmanagedType.U2)] public char[] Description;
        public uint PhysicalAddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;               // IF_MAX_PHYS_ADDRESS_LENGTH
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentPhysicalAddress;
        public uint Mtu;
        public uint Type;
        public int TunnelType;
        public int MediaType;
        public int PhysicalMediumType;
        public int AccessType;
        public int DirectionType;
        public byte InterfaceAndOperStatusFlags;           // 8×BOOLEAN 位域共 1 字节
        public int OperStatus;
        public int AdminStatus;
        public int MediaConnectState;
        public Guid NetworkGuid;
        public int ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }

    /// <summary>镜像声明算出来的行字节数（0 表示计算失败）。</summary>
    public static int MirrorRowSize { get; } = SafeSizeOf<MibIfRow2Mirror>();

    /// <summary>
    /// 布局自检：两份独立声明是否一致、且等于从 SDK 头文件推得的常量。
    /// 返回 null 表示通过；否则是给界面显示的中文原因。
    /// </summary>
    public static string? LayoutError()
    {
        if (MirrorRowSize != RowSize)
            return $"接口表结构与预期不符（镜像声明算出 {MirrorRowSize} 字节，应为 {RowSize}）";
        foreach (var (field, expected) in new[]
                 {
                     (nameof(MibIfRow2Mirror.InterfaceIndex), OffInterfaceIndex),
                     (nameof(MibIfRow2Mirror.Mtu), OffMtu),
                     (nameof(MibIfRow2Mirror.Type), OffType),
                     (nameof(MibIfRow2Mirror.TunnelType), OffTunnelType),
                     (nameof(MibIfRow2Mirror.OperStatus), OffOperStatus),
                     (nameof(MibIfRow2Mirror.InterfaceAndOperStatusFlags), OffStatusFlags),
                     (nameof(MibIfRow2Mirror.InOctets), OffInOctets),
                     (nameof(MibIfRow2Mirror.OutOctets), OffOutOctets),
                 })
        {
            var actual = SafeOffsetOf(field);
            if (actual != expected)
                return $"接口表字段 {field} 偏移不符（镜像声明 {actual}，应为 {expected}）";
        }
        return null;
    }

    private static int SafeSizeOf<T>()
    {
        try { return Marshal.SizeOf<T>(); }
        catch (Exception) { return 0; }        // 声明本身不合法（例如数组长度写错）⇒ 由 LayoutError 兜住
    }

    private static int SafeOffsetOf(string field)
    {
        try { return (int)Marshal.OffsetOf<MibIfRow2Mirror>(field); }
        catch (Exception) { return -1; }
    }
}
