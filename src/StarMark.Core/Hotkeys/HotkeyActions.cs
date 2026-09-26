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

    /// <summary>截图（Snipaste 的默认键 F1）：进选区遮罩，放开后复制或存图（动作条上也能选贴图）。</summary>
    public const string ScreenCapture = "screen.capture";

    /// <summary>贴图（Snipaste 的默认键 F3）：同样框选，但放开后<b>直接钉在桌面上</b>，不再等一次点击。</summary>
    public const string ScreenPin = "screen.pin";

    /// <summary>
    /// 显示 / 隐藏所有贴图（Snipaste 的默认键 F4）。这一条同时是"贴图忽然点不动了"的出口：
    /// 处于鼠标穿透的贴图窗收不到任何点击，只有全局热键与托盘还能把画面叫回来。
    /// </summary>
    public const string ScreenPinToggleHidden = "screen.pinhidden";

    /// <summary>所有贴图的鼠标穿透开关。<b>默认不绑键</b>（少占一个全局键位），托盘里有同名勾选项，设置页可自行绑定。</summary>
    public const string ScreenPinClickThrough = "screen.pinthrough";

    /// <summary>
    /// 识字：框选一块画面，认出其中的文字并复制走。<b>默认不绑键</b>——它比截图低频，
    /// 而入口已经有三处（截图动作条上的「识字」、贴图工具条、托盘菜单），不必再吃一个全局键位。
    /// </summary>
    public const string ScreenOcr = "screen.ocr";

    /// <summary>
    /// 屏幕画布模式的开关（讲解时直接在屏幕上画）。放在 <c>screen.</c> 前缀之外：
    /// <see cref="CategoryOf"/> 用那个前缀归到"截图 / 贴图 / 识字"，而画布是<b>常驻模式</b>不是工具会话。
    /// </summary>
    public const string CanvasToggle = "canvas.toggle";

    /// <summary>
    /// 画布内的九个动作（发起人点名要"全部带修饰键的全局热键"）。
    /// <para>
    /// <b>为什么每个都要占一个全局键位</b>：画布是"不吃键盘的覆盖层"里唯一有键盘诉求的地方——
    /// 讲解时手在翻页笔与键盘之间换来换去，换工具如果必须先去找工具条点一下，讲的东西就断了。
    /// 而 §16.5.4 那套裸键（1–6 换色 / E 橡皮 / X 清屏）在这里<b>刻意不做</b>：穿透态下画布收不到键盘，
    /// 裸键要么得偷偷装全局钩子（与所有应用抢键），要么只在绘制态生效（同一按键两种结果），都更糟。
    /// </para>
    /// </summary>
    public const string CanvasClickThrough = "canvas.through";
    public const string CanvasPen = "canvas.pen";
    public const string CanvasHighlighter = "canvas.highlighter";
    public const string CanvasEraser = "canvas.eraser";
    public const string CanvasUndo = "canvas.undo";
    public const string CanvasClear = "canvas.clear";
    public const string CanvasSave = "canvas.save";
    public const string CanvasCopy = "canvas.copy";
    public const string CanvasPin = "canvas.pin";

    /// <summary>画布动作的前缀（分类与"总开关关掉时整批不注册"都按它判，见 <see cref="IsCanvasAction"/>）。</summary>
    private const string CanvasPrefix = "canvas.";

    /// <summary>这条动作属于屏幕画布吗（关掉画布总开关时整批不注册，也不在设置页出现）。</summary>
    public static bool IsCanvasAction(string action) => action.StartsWith(CanvasPrefix);

    private const string LayoutPrefix = "layout.apply:";

    /// <summary>
    /// 屏幕画布的全部动作，一处定序：<b>开关在最前，之后是"交回鼠标 → 三支笔 → 撤销/清屏 → 三个产出"</b>。
    /// 设置页那节与画布上的快捷键面板都枚举这份表——两处各写一遍，迟早和注册表分岔（那时症状是
    /// "面板上写着 Ctrl+Alt+R，按了没反应"）。
    /// </summary>
    public static IReadOnlyList<string> Canvas { get; } = new[]
    {
        CanvasToggle, CanvasClickThrough, CanvasPen, CanvasHighlighter, CanvasEraser,
        CanvasUndo, CanvasClear, CanvasSave, CanvasCopy, CanvasPin,
    };

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
        if (action.StartsWith("screen.")) return "截图 / 贴图 / 识字";
        if (IsCanvasAction(action)) return "屏幕画布";
        if (IsLayoutAction(action)) return "布局方案";
        foreach (var k in WidgetStorage.AllKinds)
            if (action == WidgetCreate(k) || action == WidgetShow(k) || action == WidgetHide(k) || action == WidgetToggle(k))
                return WidgetStorage.KindTitle(k);
        return "其它";
    }

    /// <summary>分类的固定展示顺序（主界面 → 组件总控 → 截图 / 贴图 → 各组件 → 布局方案）。</summary>
    public static IReadOnlyList<string> CategoryOrder { get; } = BuildCategoryOrder();

    private static List<string> BuildCategoryOrder()
    {
        var list = new List<string> { "主界面", "组件总控", "截图 / 贴图 / 识字", "屏幕画布" };
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
            ScreenCapture, ScreenPin, ScreenPinToggleHidden, ScreenPinClickThrough, ScreenOcr,
        };
        list.AddRange(Canvas);
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
            case ScreenCapture: return "截图（框选区域）";
            case ScreenPin: return "贴图（框选后钉在桌面）";
            case ScreenPinToggleHidden: return "切换贴图显示 / 隐藏（全部）";
            case ScreenPinClickThrough: return "切换贴图鼠标穿透（全部）";
            case ScreenOcr: return "识字（框选区域并复制文字）";
            case CanvasToggle: return "屏幕画布（开启 / 关闭）";
            case CanvasClickThrough: return "画布 · 交出 / 收回鼠标（穿透切换）";
            case CanvasPen: return "画布 · 画笔（留痕，未开画布时先开）";
            case CanvasHighlighter: return "画布 · 荧光笔（按住即画、松开即透）";
            case CanvasEraser: return "画布 · 橡皮";
            case CanvasUndo: return "画布 · 撤销上一笔";
            case CanvasClear: return "画布 · 清空笔迹";
            case CanvasSave: return "画布 · 存为图片（屏幕 + 笔迹）";
            case CanvasCopy: return "画布 · 复制到剪贴板（屏幕 + 笔迹）";
            case CanvasPin: return "画布 · 贴到桌面（屏幕 + 笔迹）";
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
