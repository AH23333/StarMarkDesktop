#nullable enable
using System.Collections.Generic;
using System.Linq;
using StarMark.Core.Widgets;
using Windows.System;
using SysVirtualKey = Windows.System.VirtualKey;

namespace StarMark.UI.Helpers;

/// <summary>热键修饰键（对齐 Win32 MOD_* 标志位）。</summary>
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
/// 单个快捷键绑定：修饰键 + 虚拟键。JSON 可序列化，作为持久化单元。
/// 同手势允许绑定多个动作（用户可能想一次触发多个动作），冲突仅提示不阻止。
/// <para>
/// 刻意设计为**可变类**（而非 readonly record struct）：System.Text.Json 反序列化
/// readonly record struct 时会走默认无参构造、无法回填只读职位属性，导致组合键
/// 只落得默认值（表现为「只支持单个按键」）。可变属性可确保组合键原样往返。
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

    /// <summary>是否为空绑定（未录制任何主键）。</summary>
    public bool IsEmpty => VirtualKey == 0;

    /// <summary>忽略 NoRepeat 标志的分组键（同名手势才合并触发）。</summary>
    public static string GestureKey(HotkeyGesture g) => $"{(uint)g.Modifiers & 0x400Fu}:{g.VirtualKey}";

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
            parts.Add(KeyName(VirtualKey));
            return string.Join(" + ", parts);
        }
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

    public static HotkeyGesture FromKey(VirtualKey key, bool ctrl, bool alt, bool shift, bool win)
    {
        var m = HotkeyModifiers.NoRepeat;
        if (ctrl) m |= HotkeyModifiers.Control;
        if (alt) m |= HotkeyModifiers.Alt;
        if (shift) m |= HotkeyModifiers.Shift;
        if (win) m |= HotkeyModifiers.Windows;
        return new HotkeyGesture(m, (uint)key);
    }

    public static string KeyName(uint vk)
    {
        try { return FriendlyKeyName((VirtualKey)vk); }
        catch { return vk == 0 ? "—" : $"0x{vk:X}"; }
    }

    /// <summary>
    /// 把常见虚拟键翻译成用户看得懂的名字。
    /// 注意：必须用完全限定名 <c>SysVirtualKey</c>——类内的 VirtualKey 属性会遮蔽同名类型。
    /// OEM 标点键用数字字面量分支（部分 Windows SDK 投影里 VirtualKey 枚举不含这些成员，
    /// 否则会落到默认分支被 ToString 成十进制数字 190/191 之类，用户看不懂）。
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

/// <summary>
/// 快捷键动作标识。
/// 覆盖：主界面显示/隐藏/切换、组件总开关的显示/隐藏/切换、每种组件的创建/显示/隐藏/切换、
/// 以及用户保存的布局方案切换（动态动作）。
/// </summary>
public static class HotkeyActions
{
    public const string MainToggle = "main.toggle";
    public const string MainShow = "main.show";
    public const string MainHide = "main.hide";

    public const string WidgetsShowAll = "widgets.showall";
    public const string WidgetsHideAll = "widgets.hideall";
    public const string WidgetsToggleAll = "widgets.toggleall";

    private const string LayoutPrefix = "layout.apply:";

    public static string WidgetCreate(WidgetKind k) => $"widget.create:{k}";
    public static string WidgetShow(WidgetKind k) => $"widget.show:{k}";
    public static string WidgetHide(WidgetKind k) => $"widget.hide:{k}";

    /// <summary>切换某类组件的显示/隐藏（同一个键既开又关）。</summary>
    public static string WidgetToggle(WidgetKind k) => $"widget.toggle:{k}";

    /// <summary>布局方案切换动作（动态生成，随用户保存的布局增删）。</summary>
    public static string LayoutApply(string layoutId) => LayoutPrefix + layoutId;

    public static bool IsLayoutAction(string action) => action.StartsWith(LayoutPrefix);

    public static string? LayoutIdOf(string action)
        => IsLayoutAction(action) ? action[LayoutPrefix.Length..] : null;

    /// <summary>全部可绑定动作（设置页逐行渲染用）。布局动作随传入的布局列表动态追加。</summary>
    public static IReadOnlyList<string> All(IReadOnlyList<WidgetLayout>? layouts = null)
    {
        var list = new List<string>
        {
            MainToggle, MainShow, MainHide,
            WidgetsToggleAll, WidgetsShowAll, WidgetsHideAll,
        };
        foreach (var k in WidgetStorage.AllKinds)
        {
            list.Add(WidgetToggle(k));
            list.Add(WidgetCreate(k));
            list.Add(WidgetShow(k));
            list.Add(WidgetHide(k));
        }
        if (layouts is not null)
            foreach (var l in layouts)
                list.Add(LayoutApply(l.Id));
        return list;
    }

    /// <summary>动作的中文展示名（layouts 提供布局名称）。</summary>
    public static string DisplayName(string action, IReadOnlyList<WidgetLayout>? layouts = null)
    {
        switch (action)
        {
            case MainToggle: return "切换主界面（显示 / 隐藏）";
            case MainShow: return "显示主界面";
            case MainHide: return "隐藏主界面";
            case WidgetsShowAll: return "显示所有组件";
            case WidgetsHideAll: return "隐藏所有组件";
            case WidgetsToggleAll: return "切换所有组件（显示 / 隐藏）";
        }

        if (IsLayoutAction(action))
        {
            var id = LayoutIdOf(action);
            var name = layouts?.FirstOrDefault(l => l.Id == id)?.Name;
            return string.IsNullOrEmpty(name) ? "切换布局（已删除）" : $"切换布局 · {name}";
        }

        foreach (var k in WidgetStorage.AllKinds)
        {
            var title = WidgetStorage.KindTitle(k);
            if (action == WidgetToggle(k)) return $"切换组件 · {title}（显示 / 隐藏）";
            if (action == WidgetCreate(k)) return $"新建组件 · {title}";
            if (action == WidgetShow(k)) return $"显示组件 · {title}";
            if (action == WidgetHide(k)) return $"隐藏组件 · {title}";
        }
        return action;
    }
}
