#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.UI.Helpers;

namespace StarMark.UI.Services;

/// <summary>
/// 屏幕标注系统全机 <b>Z 序与窗口样式的唯一写入点</b>（架构方案 §3.5）。
/// 工具条、快捷键面板、截图冻帧、贴图、画布玻璃——这些窗的层序只从这里改。
/// <para>
/// <b>为什么要收这一处</b>：同屏多块 topmost 窗时"谁在上"取决于谁最后被交互过一次，于是三家各自
/// 抱着"我以为该谁在上"的小算盘互相提层（批次 WD-2/WD-7/WO 的真机教训，也是 C1/C8 的成因）。
/// 自愈逻辑散在三处时，每加一个宿主就要多一处特判；一处写入点之后，特判没有落脚的地方。
/// </para>
/// <para>
/// <b>边界（S1 的分期兑现）</b>：分层窗自己的原生原语（<c>SetClickThrough</c> 改样式位、
/// <c>SetVisible</c> 显隐、<c>Present</c> 提交像素）仍留在 <c>LayeredCanvasWindow</c> 里——
/// 那是"机制"，不是"决定"。这一层管的是<b>什么时候、按哪张表</b>去按那个按钮（WF 定下的口径：
/// 判据与决定在一处，接线层只准调用）。
/// </para>
/// </summary>
public static class LayerDirector
{
    private readonly record struct Entry(
        IntPtr Hwnd, SurfaceRole Role, Func<bool>? ReadStyle, Action<bool>? ApplyStyle);

    /// <summary>注册顺序就是"同角色内谁更新"的顺序，取锚点时用最后注册那一个（面板比工具条晚出现 ⇒ 锚点是面板）。</summary>
    private static readonly List<Entry> Entries = new();

    public static int Registered => Entries.Count;

    /// <summary>
    /// 窗建起来就登记；<b>登记与置顶在同一个调用点做完</b>，否则新窗有半秒不在名册里，定序会漏掉它。
    /// <para><paramref name="readStyle"/> 读的是<b>窗口此刻真的带着穿透位吗</b>（样式位本身，不是宿主
    /// 自己记的旗标），<paramref name="applyStyle"/> 是按状态改回去的动作。只有玻璃这类"状态与样式
    /// 可能分岔"的窗需要给这两句；给不了（返回 null）就当没有这件事要对账。</para>
    /// </summary>
    public static void Register(
        SurfaceRole role, IntPtr hwnd, Func<bool>? readStyle = null, Action<bool>? applyStyle = null)
    {
        if (hwnd == IntPtr.Zero) return;
        Unregister(hwnd);                       // 同一个句柄不许在名册里出现两次
        Entries.Add(new Entry(hwnd, role, readStyle, applyStyle));
    }

    public static void Unregister(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        for (var i = Entries.Count - 1; i >= 0; i--)
            if (Entries[i].Hwnd == hwnd) Entries.RemoveAt(i);
    }

    /// <summary>
    /// 把某一角色的窗<b>几何与进带一次做完</b>（两步提层配方：① <c>HWND_TOPMOST</c> 建立带成员资格
    /// ② <c>HWND_TOP</c> 带内重排）。WinUI 窗生下来不在 topmost 带里，只传 TOP 的窗永远留在普通层，
    /// 任何应用一激活就把它盖住；而对已在带里的窗再传 TOPMOST 只换带不重排＝什么都没做（批次 WD-2）。
    /// </summary>
    public static void ShowAt(SurfaceRole role, IntPtr hwnd, IntRect bounds)
    {
        if (hwnd == IntPtr.Zero) return;
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST, 0, 0, 0, 0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOP,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            WindowInterop.SWP_SHOWWINDOW | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.ShowWindow(hwnd, WindowInterop.SW_SHOWNOACTIVATE);
        Register(role, hwnd);
        // 任何一扇自己的窗出现后都立刻按角色表归一次位：让"谁在上"这件事由名册决定，
        // 而不是"谁最后被创建"——多一处"记得叫上定序"就多一处会被忘掉的地方（批次 WL 的同类教训）。
        EnforceOrder(AnnotationHub.Stage);
    }

    /// <summary>
    /// 只改几何、<b>不动层序</b>（贴图拖动与收边用的那发 <c>SWP_NOZORDER</c>）。
    /// <para>这里"不提层"是有意的：贴图拖动时顺带提层会让整叠贴图按拖动的先后重新洗牌，
    /// 用户看到的是"我没碰它，它自己跑到别人底下去了"（批次 MZ 的抖动教训同族）。</para>
    /// </summary>
    public static void Relocate(IntPtr hwnd, IntRect bounds)
    {
        if (hwnd == IntPtr.Zero) return;
        WindowInterop.SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>只挪位置、不改尺寸也不动层序（贴图拖着手走的那一发）。</summary>
    public static void MoveTo(IntPtr hwnd, int x, int y)
    {
        if (hwnd == IntPtr.Zero) return;
        WindowInterop.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>只重排、不动几何（条子每次刷新自己提一次，防被画布盖回去）。</summary>
    public static void RaiseWithinBand(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOPMOST, 0, 0, 0, 0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOP, 0, 0, 0, 0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>
    /// <b>每帧核一次"状态说的穿透位"与"窗口实际带的穿透位"是否同一件事</b>，不一致就按状态改回来，
    /// 返回改了几块（调用方据此决定是否重排与是否让状态行跟着改口）。
    /// <para>读的是 <c>WS_EX_TRANSPARENT</c> 那一位本身，不是宿主自己记的旗标：两者可以分岔
    /// （某条路只改了一边、部分系统/远程会话改样式半生效），分岔的样子是"工具条说绘制中、点下去却画不上"
    /// ——界面读旗标，鼠标归谁读的是那一位（批次 WO 的真机反馈）。</para>
    /// <para><b>不查原因，只兜结果</b>：真机上能造成分岔的路径不止一条，逐条堵会永远堵不完；
    /// 而这一处兜住之后，任何一条新路写坏了样式位最多活一帧。</para>
    /// </summary>
    public static int ReconcileStyles(AnnotationStage stage)
    {
        if (!stage.NeedsFrameAudit()) return 0;
        var expect = !stage.GlassTakesPointer();          // 穿透位：绘制态关，穿透态开
        var repaired = 0;
        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            if (entry.Role != SurfaceRole.Board || entry.ReadStyle is null) continue;
            if (entry.ReadStyle() == expect) continue;
            StarLog.WarnThrottled("layer:style",
                $"[Layer] 穿透状态与窗口样式不一致（状态={stage}，期望穿透={expect}），已按状态改回",
                windowMs: 5_000);
            entry.ApplyStyle?.Invoke(expect);
            repaired++;
        }
        return repaired;
    }

    /// <summary>光标归谁：取不到窗＝不下判断（宁可不作为，也不要凭一个 0 句柄把鼠标交出去）。</summary>
    public enum LayerOwnership { Unknown, Ours, Foreign }

    /// <summary>
    /// <b>逐像素问"这一层归谁"</b>——判据只看进程（批次 WD-8 的教训：<c>WindowFromPoint</c> 给的是那一点上
    /// <b>最深</b>的 HWND，WinUI 3 的条子内容住在子窗里、tooltip 与浮层是另开的顶层窗，
    /// 拿它跟 <c>GetHwnd()</c> 比相等必然不等 ⇒ 光标一停在条子上就被判成"别人占了画布"，永远退不出穿透态）。
    /// </summary>
    public static LayerOwnership Classify(int cursorX, int cursorY, out IntPtr hit)
    {
        hit = StarMark.Integrations.Canvas.LayeredCanvasWindow.WindowAt(cursorX, cursorY);
        if (hit == IntPtr.Zero) return LayerOwnership.Unknown;
        if (WindowInterop.GetWindowThreadProcessId(hit, out var pid) == 0) return LayerOwnership.Unknown;
        return pid == (uint)Environment.ProcessId ? LayerOwnership.Ours : LayerOwnership.Foreign;
    }

    /// <summary>命中窗的顶层祖先（认"是不是自家 chrome"只能比根窗，不能比子窗句柄）。</summary>
    public static IntPtr RootOf(IntPtr hwnd)
    {
        var root = WindowInterop.GetAncestor(hwnd, WindowInterop.GA_ROOT);
        return root == IntPtr.Zero ? hwnd : root;      // 取不到根就拿命中窗本身比，绝不折成"是 chrome"
    }

    /// <summary>
    /// 按 <see cref="LayerRules"/> 把画布玻璃归到它该在的位置。
    /// <para><b>只有玻璃会被移动</b>：条子/面板/冻帧/贴图谁在上面是由"出现与交互的先后"自然决定的，
    /// 那正是用户对 Snipaste 的期待（新贴的一张在最上面）；只有玻璃的相对次序需要<b>随会话态翻面</b>
    /// （绘制态压住贴图才能圈注，穿透态让到贴图之下贴图才可点）。把整排窗按表重排一遍反而会每帧
    /// 打乱用户自己排好的贴图顺序。</para>
    /// </summary>
    public static void EnforceOrder(AnnotationStage stage)
    {
        var anchor = AnchorForBoard(stage);
        for (var i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Role != SurfaceRole.Board) continue;
            var glass = Entries[i].Hwnd;
            if (anchor == IntPtr.Zero) { RaiseWithinBand(glass); continue; }
            WindowInterop.SetWindowPos(glass, anchor, 0, 0, 0, 0,
                WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// 玻璃应当插到哪一个窗之下。<b>锚点必须自己是 topmost 且还活着</b>：递给 Win32 一个非 topmost 的窗
    /// 会按"a topmost window repositioned after any non-topmost window is no longer topmost"把玻璃
    /// 连人带桌拽出带（批次 WD-2 半对造成 WD-7 回归的真因），所以宁可不排。
    /// </summary>
    public static IntPtr AnchorForBoard(AnnotationStage stage)
    {
        var present = new List<SurfaceRole>();
        for (var i = 0; i < Entries.Count; i++)
            if (Entries[i].Role != SurfaceRole.Board && !present.Contains(Entries[i].Role))
                present.Add(Entries[i].Role);
        if (LayerRules.NearestAbove(SurfaceRole.Board, stage, present) is not { } above) return IntPtr.Zero;

        // 同角色里取<b>最后注册</b>那一个：锚点必须是紧邻玻璃上方的那条，递最上面那条
        // （工具条）会把玻璃塞进工具条与快捷键面板之间，面板就掉到玻璃下面去了。
        IntPtr candidate = IntPtr.Zero;
        for (var i = 0; i < Entries.Count; i++)
            if (Entries[i].Role == above) candidate = Entries[i].Hwnd;
        if (candidate == IntPtr.Zero || !WindowInterop.IsWindow(candidate)) return IntPtr.Zero;
        return LayerRules.IsSafeInsertAfter(IsTopmost(candidate), true) ? candidate : IntPtr.Zero;
    }

    private static bool IsTopmost(IntPtr hwnd)
        => (WindowInterop.GetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE).ToInt64()
            & WindowInterop.WS_EX_TOPMOST) != 0;

    /// <summary>
    /// 把某个窗插到指定窗的<b>紧邻下方</b>（同角色内部排队用：快捷键面板排在工具条之下）。
    /// 同样受"锚点必须 topmost"的护栏约束——递错一个就把两个窗一起拽出 topmost 带。
    /// <para><b>参数方向别看反</b>：Win32 说 <c>hwndInsertAfter</c> 是"the window to <b>precede</b>"，
    /// 而 Z 序自上而下数 ⇒ 递进去的那一个在<b>上面</b>，本窗在它下面。读反就是把整条链倒过来，
    /// 症状是工具条又点不动了（批次 WD-2 的原地教训）。</para>
    /// </summary>
    public static void InsertBelow(IntPtr hwnd, IntPtr above)
    {
        if (hwnd == IntPtr.Zero || above == IntPtr.Zero || hwnd == above) return;
        if (!LayerRules.IsSafeInsertAfter(IsTopmost(above), WindowInterop.IsWindow(above))) return;
        WindowInterop.SetWindowPos(hwnd, above, 0, 0, 0, 0,
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 用户拖动某条窗时：挪位置并顺手在带内重排到顶（拖到哪条就该压在哪条之上）。
    /// 这里传 <c>HWND_TOP</c> 而不是 TOPMOST——对已在带里的窗再传 TOPMOST 只换带不重排（批次 WD-2）。
    /// </summary>
    public static void MoveWithinBand(IntPtr hwnd, int x, int y)
    {
        if (hwnd == IntPtr.Zero) return;
        WindowInterop.SetWindowPos(hwnd, WindowInterop.HWND_TOP, x, y, 0, 0,
            WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
    }

    /// <summary>名册里有这个窗吗（认"是不是自家的"只能比顶层祖先，不能比命中句柄——批次 WD-8）。</summary>
    public static bool IsKnown(IntPtr hwnd)
    {
        for (var i = 0; i < Entries.Count; i++)
            if (Entries[i].Hwnd == hwnd) return true;
        return false;
    }

    /// <summary>名册里这个窗是干什么用的（没登记＝null）。</summary>
    public static SurfaceRole? RoleOf(IntPtr hwnd)
    {
        for (var i = 0; i < Entries.Count; i++)
            if (Entries[i].Hwnd == hwnd) return Entries[i].Role;
        return null;
    }

    /// <summary>
    /// 这一点落在<b>自家那两条要点的 chrome</b>（工具条／快捷键面板，名册里同为 Strip）上吗。
    /// <para>抢按之前必须问这一句：抢按是轮询发现的，它不看光标在哪儿，于是"去点工具条那颗按钮"
    /// 会被画布当成一次落笔吃掉（真机原话："绘制态下点击菜单栏依旧是绘制在菜单栏上"）。
    /// 贴图（Pin）不算 chrome——穿透态下在贴图上 Ctrl+Alt 圈注是要保留的能力（§13 用例 5）。</para>
    /// </summary>
    public static bool IsChromeUnder(int cursorX, int cursorY)
        => Classify(cursorX, cursorY, out var hit) == LayerOwnership.Ours
            && RoleOf(RootOf(hit)) == SurfaceRole.Strip;

    /// <summary>名册清空（退出画布、程序收尾）。逐个退出不整批擦，避免把还在的贴图一起忘掉。</summary>
    public static void ForgetRole(SurfaceRole role)
    {
        for (var i = Entries.Count - 1; i >= 0; i--)
            if (Entries[i].Role == role) Entries.RemoveAt(i);
    }
}
