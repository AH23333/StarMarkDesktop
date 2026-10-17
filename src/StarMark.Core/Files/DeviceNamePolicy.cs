#nullable enable
using System;

namespace StarMark.Core.Files;

/// <summary>
/// Windows 设备名（⛔ 许当文件名主干）的<b>唯一判据</b>（批次 Phase C 片12）。
/// <para>
/// <b>为什么现在并</b>：这颗判断此前有两份主人——<c>BackupPathPolicy</c> 拿一张 22 行的表逐颗比，
/// 而 <c>UpdateManifestCodec</c> 里那私有算法按"CON/PRN/AUX/NUL/CLOCK$ ＋ COM/LPT 加一位 1–9"算。
/// 两份<b>成员⛔ 同</b>：备份那一侧认不出 <c>CLOCK$</c>，于是那颗名字能通过输入检查、再在真写盘时以奇怪方式失败
/// （<c>BackupPathPolicyTests</c> 那句注释写的正是这一类）。表与算法各写一遍，就是"两扇窗给两段互相认不出的范围"
/// 那一族的老形状（批次 SD／WI-82 片1 撞过的那颗）。
/// </para>
/// <para>
/// <b>成员照旧，⛔ 借收敛之名改判据</b>：<c>COM0</c>／<c>LPT0</c> 与 <c>COM10</c> 这类名字<b>仍然</b>算合法
/// （那是原算法的形状；要扩得另立一批并带上依据）。要扩的那天一并按这里改，⛔ 在调用处各补一颗特例。
/// <b>唯一例外</b>：清单载荷名那一格本批从"整颗 <c>xxx.zip</c> 拿去比"改成"取主干再比"——旧写法永远比不中，
/// 等于那一格从没判过；详见 <c>UpdateManifestCodec.IsSafePackageName</c> 的注释。
/// </para>
/// </summary>
public static class DeviceNamePolicy
{
    /// <summary>
    /// 那颗主干名（<b>不含扩展名</b>）是不是一颗 Windows 设备名。大小写⛔ 敏感。
    /// <para>调用方先取 <c>Path.GetFileNameWithoutExtension</c>：设备名的规矩管的是主干，
    /// <c>nul.json</c> 与 <c>nul</c> 一样非法。</para>
    /// </summary>
    public static bool IsReservedStem(string? stem)
    {
        if (string.IsNullOrEmpty(stem)) return false;      // 空主干由调用方各自判（这里⛔ 替它说"合法"）
        var upper = stem!.ToUpperInvariant();
        if (upper is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$") return true;
        if (upper.Length is not (4 or 5) || (upper[0] != 'C' && upper[0] != 'L')) return false;
        var head = upper[0] == 'C' ? "COM" : "LPT";
        return upper.StartsWith(head, StringComparison.Ordinal)
            && upper.Length == head.Length + 1
            && upper[3] is >= '1' and <= '9';
    }
}
