#nullable enable
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;
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

    /// <summary>
    /// 所有贴图的鼠标穿透开关。<b>默认绑裸 F5</b>（理由写在 <see cref="HotkeyBindings.Defaults"/> 里那一行旁的注释：
    /// 穿透中的贴图收不到任何鼠标消息，必须留一颗全局键能把这一组交还鼠标）。托盘里有同名勾选项，设置页可自行改绑。
    /// </summary>
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
    /// 画布内的那批动作（发起人点名要"全部带修饰键的全局热键"）。
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

    /// <summary>
    /// 白板底（整屏换成白底，墨画在白底上）。与「交出鼠标」并列而不是排在笔后面：
    /// 它改的是<b>这块玻璃底下是什么</b>，不是手上拿哪支笔。
    /// </summary>
    public const string CanvasBoard = "canvas.board";

    /// <summary>
    /// 幕布（压暗整屏、只留鼠标那一圈亮）。与 <see cref="CanvasBoard"/> 同一族：改的是<b>这块玻璃底下是什么</b>，
    /// 不是手上拿哪支笔。两块底互斥（背景只有一个真值），所以点这颗会关掉白板。
    /// </summary>
    public const string CanvasCurtain = "canvas.curtain";

    public const string CanvasUndo = "canvas.undo";
    public const string CanvasRedo = "canvas.redo";
    public const string CanvasClear = "canvas.clear";
    public const string CanvasSave = "canvas.save";
    public const string CanvasCopy = "canvas.copy";
    public const string CanvasPin = "canvas.pin";

    /// <summary>
    /// 截屏这一族的前缀（分类与总开关闸都按它判，见 <see cref="IsScreenAction"/>）。
    /// <para><b>刻意保持 private</b>：动作表的公开常量会被"每一颗动作都得有人接"那枚闸门当成动作去查 handler，
    /// 前缀不是动作，公开出去就是让那枚闸门莫名其妙地红一次（同类：上面的 <c>CanvasPrefix</c>）。</para>
    /// </summary>
    private const string ScreenPrefix = "screen.";

    /// <summary>
    /// 这条动作属于截屏这一族吗（截图 / 贴图 / 识字，外加两条贴图组管理）。
    /// <para>分类（<see cref="CategoryOf"/>）与"关掉总开关时整族不注册"（<see cref="CaptureGate.RegistersHotkey"/>）
    /// 用的<b>必须是同一个条件</b>：两处各判一次前缀，将来加一条 <c>screen.xxx</c> 就会只被其中一处认到。</para>
    /// </summary>
    public static bool IsScreenAction(string action) => action.StartsWith(ScreenPrefix, StringComparison.Ordinal);

    /// <summary>画布动作的前缀（分类与"总开关关掉时整批不注册"都按它判，见 <see cref="IsCanvasAction"/>）。</summary>
    private const string CanvasPrefix = "canvas.";

    /// <summary>这条动作属于屏幕画布吗（关掉画布总开关时整批不注册，也不在设置页出现）。</summary>
    public static bool IsCanvasAction(string action) => action.StartsWith(CanvasPrefix);

    private const string LayoutPrefix = "layout.apply:";

    /// <summary>
    /// 屏幕画布的全部动作，一处定序：<b>开关在最前，之后是"交回鼠标 → 三支笔 → 背景态 → 撤销/清屏 → 三个产出"</b>。
    /// 设置页那节与画布上的快捷键面板都枚举这份表——两处各写一遍，迟早和注册表分岔（那时症状是
    /// "面板上写着 Ctrl+Alt+R，按了没反应"）。
    /// </summary>
    public static IReadOnlyList<string> Canvas { get; } = new[]
    {
        CanvasToggle, CanvasClickThrough, CanvasPen, CanvasHighlighter, CanvasEraser,
        CanvasBoard, CanvasCurtain,
        CanvasUndo, CanvasRedo, CanvasClear, CanvasSave, CanvasCopy, CanvasPin,
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

    /// <summary>
    /// 动作所属的分类<b>身份</b>（设置页按分类折叠成多级菜单，消除扁平长列表的重复感）。
    /// <para>⚠ 这里交的是 ASCII 身份，<b>不是</b>说给人听的那句话：UI 把返回值同时当分组的字典键与渲染顺序用
    /// （<c>SettingsPage.Hotkeys.cs</c> 里 <c>Dictionary&lt;string, List&lt;HotkeyRow&gt;&gt;</c> 的键），
    /// 一句"换语言"就会让同一批动作跑到另一个组名底下。那句话由 <see cref="CategoryTitleKey"/> 交键，词只住在表里（坑表 #353）。</para>
    /// <para>组件那一族从前返回 <c>WidgetStorage.KindTitle</c>——那颗串里既有图标字形又有<b>已按当下语言取过词</b>的名字，
    /// 所以它是同一个缺陷的第二张脸（字形⛔ 是语言内容，名字却是）。</para>
    /// </summary>
    public static string CategoryOf(string action, IReadOnlyList<WidgetLayout>? layouts = null)
    {
        if (action is MainToggle or MainShow or MainHide) return Category.Main;
        if (action is WidgetsToggleAll or WidgetsShowAll or WidgetsHideAll or WidgetsToggleTopmostAll) return Category.Widgets;
        if (IsScreenAction(action)) return Category.Screen;
        if (IsCanvasAction(action)) return Category.Canvas;
        if (IsLayoutAction(action)) return Category.Layout;
        foreach (var k in WidgetStorage.AllKinds)
            if (action == WidgetCreate(k) || action == WidgetShow(k) || action == WidgetHide(k) || action == WidgetToggle(k))
                return Category.Of(k);
        return Category.Other;
    }

    /// <summary>
    /// 分组的<b>身份</b>（不是给人看的那句话）。
    /// <para>⚠ 刻意做成嵌套类而不是 <c>HotkeyActions</c> 上的 <c>public const string</c>：那颗"每一颗动作常量都得有人接 handler"
    /// 的证人是按反射扫 <c>HotkeyActions</c> 的公开静态字符串字段的（<c>CanvasWiringGateTests</c>），
    /// 把非动作的身份混进那一族＝让闸门去要求"给一个分组名注册热键"，而绕过它的唯一办法是松那把尺。</para>
    /// </summary>
    public static class Category
    {
        public const string Main = "main";
        public const string Widgets = "widgets";
        public const string Screen = "screen";
        public const string Canvas = "canvas";
        public const string Layout = "layout";
        public const string Other = "other";
        public const string WidgetPrefix = "widget:";

        /// <summary>某一类组件自成一组的身份。</summary>
        public static string Of(WidgetKind kind) => WidgetPrefix + kind;
    }

    /// <summary>
    /// 那一组的标题取<b>哪颗键</b>：六颗固定的⛔ 现拼（<c>$"hotkey.cat.{id}"</c> 对死键闸门隐身，坑表 #349），
    /// 组件那一族交回注册表那颗 <c>KindTitleKey</c>（名字的唯一真源，托盘与组件窗都读它）⇒ ⛔ 在这一栏再造第二个名字出处。
    /// </summary>
    public static string CategoryTitleKey(string category)
    {
        switch (category)
        {
            case Category.Main: return "hotkey.cat.main";
            case Category.Widgets: return "hotkey.cat.widgets";
            case Category.Screen: return "hotkey.cat.screen";
            case Category.Canvas: return "hotkey.cat.canvas";
            case Category.Layout: return "hotkey.cat.layout";
            case Category.Other: return "hotkey.cat.other";
            default:
                return category.StartsWith(Category.WidgetPrefix, StringComparison.Ordinal)
                       && Enum.TryParse(category[Category.WidgetPrefix.Length..], out WidgetKind kind)
                    ? WidgetStorage.KindTitleKey(kind)
                    : "hotkey.cat.other";   // 认不得的身份：露出"其它"那颗，⛔ 这里替它编一句
        }
    }

    /// <summary>分类的固定展示顺序（主界面 → 组件总控 → 截图 / 贴图 → 各组件 → 布局方案）。交的是<b>身份</b>。</summary>
    public static IReadOnlyList<string> CategoryOrder { get; } = BuildCategoryOrder();

    private static List<string> BuildCategoryOrder()
    {
        var list = new List<string> { Category.Main, Category.Widgets, Category.Screen, Category.Canvas };
        foreach (var k in WidgetStorage.AllKinds) list.Add(Category.Of(k));
        list.Add(Category.Layout);
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

    /// <summary>
    /// 动作的展示名（成品串）。批次 WI-39 起它只是 <see cref="DisplayMessage"/> 上面薄薄一层 <c>Render()</c>：
    /// 「哪颗键说这个名字」只许有一份，而"什么时候取词"归拿它的那一处宿主决定（常驻的那三处要能重贴）。
    /// </summary>
    public static string DisplayName(string action, IReadOnlyList<WidgetLayout>? layouts = null)
        => DisplayMessage(action, layouts)?.Render() ?? action;

    /// <summary>
    /// 某一动作的名字怎么说：<b>交「键＋读数」，不交成品串</b>（批次 WI-39）。
    /// <para>布局名与组件名是<b>实参</b>（用户数据／已键化的种类名），不是第二句语言；
    /// 整句留在 <c>hotkey.act.layout</c> 那一行里，换语言重贴时外层与实参一起按当下语言重算。</para>
    /// <para>返回 <c>null</c>＝这颗 ID 表里没有对应的一行（<see cref="DisplayName"/> 于是原样交出 ID，
    /// ⛔ 不在这里替它编一句）。说明族列的那六颗都有行，由 <c>GuideGateTests</c> 逐颗钉着。</para>
    /// <para>⚠ 键<b>逐颗写字面</b>（<c>hotkey.act.</c>＋动作 ID），不许 <c>$"hotkey.act.{action}"</c> 内插——
    /// 对账闸门认字面量（坑表 #331，WI-2q 首跑的教训）。</para>
    /// </summary>
    public static LocMessage? DisplayMessage(string action, IReadOnlyList<WidgetLayout>? layouts = null)
    {
        switch (action)
        {
            case MainToggle: return LocMessage.Of("hotkey.act.main.toggle");
            case MainShow: return LocMessage.Of("hotkey.act.main.show");
            case MainHide: return LocMessage.Of("hotkey.act.main.hide");
            case WidgetsShowAll: return LocMessage.Of("hotkey.act.widgets.showall");
            case WidgetsHideAll: return LocMessage.Of("hotkey.act.widgets.hideall");
            case WidgetsToggleAll: return LocMessage.Of("hotkey.act.widgets.toggleall");
            case WidgetsToggleTopmostAll: return LocMessage.Of("hotkey.act.widgets.topmostall");
            case ScreenCapture: return LocMessage.Of("hotkey.act.screen.capture");
            case ScreenPin: return LocMessage.Of("hotkey.act.screen.pin");
            case ScreenPinToggleHidden: return LocMessage.Of("hotkey.act.screen.pinhidden");
            case ScreenPinClickThrough: return LocMessage.Of("hotkey.act.screen.pinthrough");
            case ScreenOcr: return LocMessage.Of("hotkey.act.screen.ocr");
            case CanvasToggle: return LocMessage.Of("hotkey.act.canvas.toggle");
            case CanvasClickThrough: return LocMessage.Of("hotkey.act.canvas.through");
            case CanvasPen: return LocMessage.Of("hotkey.act.canvas.pen");
            case CanvasHighlighter: return LocMessage.Of("hotkey.act.canvas.highlighter");
            case CanvasEraser: return LocMessage.Of("hotkey.act.canvas.eraser");
            case CanvasBoard: return LocMessage.Of("hotkey.act.canvas.board");
            case CanvasCurtain: return LocMessage.Of("hotkey.act.canvas.curtain");
            case CanvasUndo: return LocMessage.Of("hotkey.act.canvas.undo");
            case CanvasRedo: return LocMessage.Of("hotkey.act.canvas.redo");
            case CanvasClear: return LocMessage.Of("hotkey.act.canvas.clear");
            case CanvasSave: return LocMessage.Of("hotkey.act.canvas.save");
            case CanvasCopy: return LocMessage.Of("hotkey.act.canvas.copy");
            case CanvasPin: return LocMessage.Of("hotkey.act.canvas.pin");
        }

        if (IsLayoutAction(action))
        {
            var id = LayoutIdOf(action);
            var name = layouts?.FirstOrDefault(l => l.Id == id)?.Name;
            return string.IsNullOrEmpty(name)
                ? LocMessage.Of("hotkey.act.layoutGone")
                : LocMessage.Of("hotkey.act.layout", name);
        }

        foreach (var k in WidgetStorage.AllKinds)
        {
            // 组件名交的是<b>键</b>（<c>KindTitleKey</c>），⛔ 不是 <c>KindTitle</c> 那颗成品串：
            // 实参一旦已经拼好，外层换语言重贴时这一颗还是旧语言＝半中半英（NestedReadingsArePassedUnrendered 守的就是这一条）。
            var title = LocMessage.Of(WidgetStorage.KindTitleKey(k));
            if (action == WidgetToggle(k)) return LocMessage.Of("hotkey.act.widget.toggle", title);
            if (action == WidgetCreate(k)) return LocMessage.Of("hotkey.act.widget.create", title);
            if (action == WidgetShow(k)) return LocMessage.Of("hotkey.act.widget.show", title);
            if (action == WidgetHide(k)) return LocMessage.Of("hotkey.act.widget.hide", title);
        }
        return null;
    }
}
