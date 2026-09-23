#nullable enable

namespace StarMark.Abstractions.SystemMonitor;

/// <summary>
/// 一块网卡自己的类型位与<b>累计</b>收发字节数（原生侧原样报上来，不在那边做任何判断或算术）。
/// <para>
/// 放 <c>Abstractions</c> 是为了让"哪些网卡该计入网速"这条判定留在 <c>Core</c>（可单测）：
/// <c>Integrations</c> 不许引用 <c>Core</c>，若把判定写在那边就只剩真机能验，
/// 而这条判定的后果是"把下载速度翻倍显示"。
/// </para>
/// </summary>
/// <param name="IfType">IFTYPE（24＝软件回环）。</param>
/// <param name="TunnelType">TUNNEL_TYPE（0＝非隧道）。</param>
/// <param name="OperStatus">IF_OPER_STATUS（1＝链路已起）。</param>
/// <param name="HardwareInterface">该接口是不是真硬件接口（<c>InterfaceAndOperStatusFlags.HardwareInterface</c>）。
/// 托管网络/Wi-Fi Direct/虚拟交换机这类接口会把底层网卡的计数<b>再记一遍</b>，求和时必须能分辨它们。</param>
/// <param name="InBytes">该网卡自开机/驱动载入以来的累计接收字节数。</param>
/// <param name="OutBytes">该网卡累计发送字节数。</param>
public readonly record struct NetworkAdapterCounters(
    int IfType,
    int TunnelType,
    int OperStatus,
    bool HardwareInterface,
    long InBytes,
    long OutBytes);
