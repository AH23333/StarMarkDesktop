#nullable enable
using System;
using System.Collections.Generic;

namespace StarMark.Core.Hotkeys;

/// <summary>
/// 修饰键虚拟键码表（Win32 VK_*），热键录制的唯一真源。
/// <para>
/// 这张表原先被抄过四份：钩子内部一份 switch、启动钩子时读系统状态一份硬编码三元组、
/// 设置页判定"是不是修饰键"一份（用 WinRT 枚举名）、设置页取修饰位又一份 switch。
/// 四份只需要有一次没同步，就会出现一类很难查的错：
/// 例如"某键被认定为修饰键却取不到修饰位"（当成重复按键吞掉）或反过来
/// "取到了修饰位却没被认定为修饰键"（那个修饰键被录成主键，注册出一个错热键）。
/// </para>
/// </summary>
public static class HotkeyKeys
{
    /// <summary>四个可参与组合键的修饰位（不含 NoRepeat，它不是按键）。</summary>
    public static readonly HotkeyModifiers[] ModifierBits =
    {
        HotkeyModifiers.Control, HotkeyModifiers.Alt, HotkeyModifiers.Shift, HotkeyModifiers.Windows,
    };

    /// <summary>该虚拟键码属于哪个修饰位；<see cref="HotkeyModifiers.None"/> 表示它不是修饰键。</summary>
    public static HotkeyModifiers ModifierOf(uint vk) => vk switch
    {
        0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,   // VK_CONTROL / VK_LCONTROL / VK_RCONTROL
        0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,       // VK_MENU（Alt）/ 左 / 右
        0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,     // VK_SHIFT / 左 / 右
        0x5B or 0x5C => HotkeyModifiers.Windows,           // VK_LWIN / VK_RWIN（只有左右，无通用码）
        _ => HotkeyModifiers.None,
    };

    /// <summary>是否修饰键（含左右两侧与通用码）。判定与取位必须出自同一张表，否则会出现"认得却取不到位"。</summary>
    public static bool IsModifier(uint vk) => ModifierOf(vk) != HotkeyModifiers.None;

    /// <summary>
    /// 某个修饰位对应的全部虚拟键码：通用码在前、左右码在后（Win32 查询接口三种都可能应答）。
    /// <paramref name="modifier"/> 只应传单个修饰位；传入组合时返回其 Control/Alt/Shift/Windows 各码之并。
    /// </summary>
    public static IReadOnlyList<uint> CodesOf(HotkeyModifiers modifier)
    {
        var codes = new List<uint>();
        foreach (var bit in ModifierBits)
            if (modifier.HasFlag(bit)) codes.AddRange(AllCodesOf(bit));
        return codes;
    }

    private static uint[] AllCodesOf(HotkeyModifiers bit) => bit switch
    {
        HotkeyModifiers.Control => new uint[] { 0x11, 0xA2, 0xA3 },
        HotkeyModifiers.Alt => new uint[] { 0x12, 0xA4, 0xA5 },
        HotkeyModifiers.Shift => new uint[] { 0x10, 0xA0, 0xA1 },
        HotkeyModifiers.Windows => new uint[] { 0x5B, 0x5C },
        _ => Array.Empty<uint>(),
    };
}
