#nullable enable
using System.Collections.Generic;
using System.Linq;
using StarMark.Core.Widgets;

namespace StarMark.Core.Hotkeys;

/// <summary>
/// 快捷键动作标识与动作目录（设置页逐行渲染、分类折叠、注册分派都以此为准）。
/// 覆盖：主界面显示/隐藏/切换、组件总开关的显示/隐藏/切换、每种组件的创建/显示/隐藏/切换、
/// 以及用户保存的布局方案切换（动态动作）。
/// <para>
/// 动作目录由 <see cref="WidgetStorage.AllKinds"/> 派生：新增一种组件即自动出现在可绑定动作里，
/// 不必再改这里（与托盘子菜单同一口径）。
/// </para>
/// </summary>
public static class HotkeyActions
{
    public const string MainToggle = "main.toggle";
    public const string MainShow = "main.show";
    public const string MainHide = "main.hide";

    public const string WidgetsShowAll = "widgets.showall";
    /// <summary>
    /// 置顶 / 不置顶<b>所有</b>组件（一个键来回）。与"显示/隐藏所有组件"是两件不同的事：
    /// 前者只改层序（组件都还在，只是压到所有窗口之上或之下），后者改可见性。
    /// </summary>
    public const string WidgetsToggleTopmostAll = "widgets.topmostall";
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

    /// <summary>动作所属的分类（用于设置页按分类折叠成多级菜单，消除扁平长列表的重复感）。</summary>
    public static string CategoryOf(string action, IReadOnlyList<WidgetLayout>? layouts = null)
    {
        if (action is MainToggle or MainShow or MainHide) return "主界面";
        if (action is WidgetsToggleAll or WidgetsShowAll or WidgetsHideAll or WidgetsToggleTopmostAll) return "组件总控";
        if (IsLayoutAction(action)) return "布局方案";
        foreach (var k in WidgetStorage.AllKinds)
            if (action == WidgetCreate(k) || action == WidgetShow(k) || action == WidgetHide(k) || action == WidgetToggle(k))
                return WidgetStorage.KindTitle(k);
        return "其它";
    }

    /// <summary>分类的固定展示顺序（主界面 → 组件总控 → 各组件 → 布局方案）。</summary>
    public static IReadOnlyList<string> CategoryOrder { get; } = BuildCategoryOrder();

    private static List<string> BuildCategoryOrder()
    {
        var list = new List<string> { "主界面", "组件总控" };
        foreach (var k in WidgetStorage.AllKinds) list.Add(WidgetStorage.KindTitle(k));
        list.Add("布局方案");
        return list;
    }

    /// <summary>全部可绑定动作（设置页逐行渲染用）。布局动作随传入的布局列表动态追加。</summary>
    public static IReadOnlyList<string> All(IReadOnlyList<WidgetLayout>? layouts = null)
    {
        var list = new List<string>
        {
            MainToggle, MainShow, MainHide,
            WidgetsToggleAll, WidgetsShowAll, WidgetsHideAll, WidgetsToggleTopmostAll,
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
            case WidgetsToggleTopmostAll: return "切换所有组件置顶（置顶 / 不置顶）";
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
