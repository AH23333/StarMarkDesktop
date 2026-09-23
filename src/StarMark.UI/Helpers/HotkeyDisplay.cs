#nullable enable
using System.Collections.Generic;
using StarMark.Core.Hotkeys;
using SysVirtualKey = Windows.System.VirtualKey;

namespace StarMark.UI.Helpers;

/// <summary>
/// 快捷键的用户可读文本。
/// <para>
/// 之所以与手势模型（<see cref="HotkeyGesture"/>，在 Core 里可单测）分家：未列出的键要落到
/// WinRT <c>VirtualKey</c> 的枚举名（如小键盘 <c>NumPad0</c>），那是 UI 的显示口径而非绑定语义，
/// 把它留在 Core 会把 WinRT 依赖一起拖进可机检层。
/// </para>
/// </summary>
public static class HotkeyDisplay
{
    public static string Display(HotkeyGesture g)
    {
        var parts = new List<string>();
        if (g.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (g.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (g.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (g.Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName(g.VirtualKey));
        return string.Join(" + ", parts);
    }

    /// <summary>仅修饰键的展示（录制中回显用户按住的部分）。</summary>
    public static string ModifiersDisplay(HotkeyModifiers m)
    {
        var parts = new List<string>();
        if (m.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (m.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    public static string KeyName(uint vk)
    {
        try { return FriendlyKeyName((SysVirtualKey)vk); }
        catch { return vk == 0 ? "—" : $"0x{vk:X}"; }
    }

    /// <summary>
    /// 把常见虚拟键翻译成用户看得懂的名字。
    /// OEM 标点键用数字字面量分支（部分 Windows SDK 投影里 VirtualKey 枚举不含这些成员，
    /// 否则会落到默认分支被 ToString 成十进制数字 190/191 之类，用户看不懂）。
    /// 未列出的键沿用枚举名（小键盘/媒体键等因此有 <c>NumPad0</c> 这类可读输出）。
    /// </summary>
    private static string FriendlyKeyName(SysVirtualKey key) => (uint)key switch
    {
        0x20 => "Space",
        0x0D => "Enter",
        0x1B => "Esc",
        0x08 => "Backspace",
        0x2E => "Delete",
        0x09 => "Tab",
        0x26 => "↑",
        0x28 => "↓",
        0x25 => "←",
        0x27 => "→",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x24 => "Home",
        0x23 => "End",
        0x2D => "Insert",
        // 数字 0-9
        >= 0x30 and <= 0x39 => ((char)(uint)key).ToString(),
        // 字母 A-Z
        >= 0x41 and <= 0x5A => ((char)(uint)key).ToString(),
        // 功能键 F1-F24
        >= 0x70 and <= 0x87 => $"F{(uint)key - 0x6F}",
        // OEM 标点 / 符号键（避免显示成十进制数字）
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        0xE2 => "\\",
        _ => key.ToString(),
    };
}
