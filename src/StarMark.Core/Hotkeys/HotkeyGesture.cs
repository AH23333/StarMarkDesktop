#nullable enable
using System;

namespace StarMark.Core.Hotkeys;

/// <summary>热键修饰键（数值即 Win32 MOD_* 标志位，注册时原样传给 RegisterHotKey）。</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000,
}

/// <summary>
/// 单个快捷键绑定：修饰键 + Win32 虚拟键码。作为持久化单元（JSON 往返）。
/// <para>
/// 刻意设计为**可变类**（而非 readonly record struct）：System.Text.Json 反序列化
/// readonly record struct 时会走默认无参构造、无法回填只读职位属性，导致组合键
/// 只落得默认值（表现为「只支持单个按键」）。可变属性可确保组合键原样往返。
/// </para>
/// <para>
/// 本类型刻意放在 Core 而非 UI：「默认 + 已保存」的合并语义（含 <see cref="IsEmpty"/>
/// 这一"显式清过"的记号）是快捷键功能的真因所在，必须能在无 WinUI 运行时下单测。
/// 键名的中文/友好显示依赖 WinRT 的 <c>Windows.System.VirtualKey</c>，留在 UI 层
/// （<c>StarMark.UI.Helpers.HotkeyDisplay</c>）。
/// </para>
/// </summary>
public sealed class HotkeyGesture
{
    public HotkeyGesture() { }

    public HotkeyGesture(HotkeyModifiers modifiers, uint virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    public HotkeyModifiers Modifiers { get; set; }

    public uint VirtualKey { get; set; }

    /// <summary>
    /// 是否为空绑定（未录到主键）。它同时是"用户显式清掉了这个动作"的持久化记号：
    /// 清除时写空手势而不是删键，否则「默认 + 已保存」的合并会因为键缺失而把默认值补回来。
    /// </summary>
    public bool IsEmpty => VirtualKey == 0;

    /// <summary>
    /// 归并键：修饰键 + 主键。掩码只保留 Win32 认识的位（MOD_ALT..MOD_NOREPEAT）。
    /// 注意 NoRepeat <b>参与</b>归并——它与 vk 一起构成 RegisterHotKey 的键身份；
    /// 本项目录制与默认值一律带 NoRepeat，故实际不会出现"只差 NoRepeat"的两个手势。
    /// </summary>
    public static string GestureKey(HotkeyGesture g) => $"{(uint)g.Modifiers & 0x400Fu}:{g.VirtualKey}";
}
