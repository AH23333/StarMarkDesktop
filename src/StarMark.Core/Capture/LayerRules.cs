#nullable enable
namespace StarMark.Core.Capture;

/// <summary>
/// 参与 Z 序排法的<b>四类窗</b>（架构方案 §4）。角色是"这扇窗在屏幕标注系统里干什么"，
/// 不是"它是哪个类"——截图遮罩与贴图都住 <see cref="CaptureOverlayWindow"/>，角色却不同。
/// </summary>
public enum SurfaceRole
{
    /// <summary>工具条 / 快捷键面板 / 文本编辑浮窗：任何态都要点得到，它是唯一的鼠标出口。</summary>
    Strip,

    /// <summary>截图那块冻帧全屏（Sheet 态才有）。</summary>
    Sheet,

    /// <summary>贴在桌面上的图（可多张）。</summary>
    Pin,

    /// <summary>画布玻璃（每屏一块）。</summary>
    Board,
}

/// <summary>
/// 一叠笔迹<b>住在哪一块面上</b>（方案 §7：历史栈全局化、按 surface 记录归属）。
/// <para>
/// 角色不够用：<see cref="SurfaceRole.Board"/> 有 N 块屏、<see cref="SurfaceRole.Pin"/> 有 N 张贴图，
/// 撤销要能说清"退的是哪一块上的最后那一笔"，所以带上序号。<see cref="Index"/> 的含义由角色决定——
/// Board＝第几块屏（<c>Screens</c> 里的下标），Pin＝第几张贴图，Sheet＝0（同屏只有一块冻帧面）。</para>
/// <para>它是值类型而不是引用：<b>归属是这一叠数据的属性，不是它住在哪个对象里</b>——
/// 贴图被关掉、屏被重建时，那份历史该跟着走，而不是跟着某个窗的句柄一起失效。</para>
/// </summary>
public readonly record struct InkSurface(SurfaceRole Role, int Index);

/// <summary>
/// 全机 Z 序的<b>唯一排法</b>（方案 §4 的规则表）。<see cref="LayerDirector"/> 按它写窗口，别的任何地方不许提层。
/// <para>
/// 数字越小越靠上。之所以要有一张表而不是各处"顺手提一下自己"：同屏多块 topmost 窗时
/// <b>谁最后被交互谁在上</b>，于是"我以为该谁在上"的小算盘会在两家之间来回拉锯
/// （批次 WD-2/WD-7/WO 的真机教训，也是 C1/C8 的成因）。收成一处之后，拉扯在结构上不成立。
/// </para>
/// </summary>
public static class LayerRules
{
    /// <summary>
    /// 这一角色在当前会话态里的排名。
    /// <para><b>玻璃与贴图的相对次序按会话态翻面，这两臂都有真机代价</b>：
    /// 绘制态玻璃必须在贴图<b>之上</b>，否则"想贴图上圈个注"点下去动的是贴图本身（用例 5）；
    /// 穿透态玻璃必须在贴图<b>之下</b>，否则那块透明玻璃虽然不吃鼠标却盖着贴图，
    /// 而截图冻帧之上还会出现一坬"看得见却点不到"的图（C8）。</para>
    /// </summary>
    public static int Rank(SurfaceRole role, AnnotationStage stage) => role switch
    {
        SurfaceRole.Strip => 1,
        SurfaceRole.Sheet => 2,
        SurfaceRole.Board => stage == AnnotationStage.BoardDrawing ? 3 : 4,
        SurfaceRole.Pin => stage == AnnotationStage.BoardDrawing ? 4 : 3,
        _ => 9,
    };

    /// <summary>
    /// 绘制态的<b>让位</b>判据：光标那一层被"别的应用"占走了才主动交出鼠标。
    /// <para>分界只看进程归属（批次 WD-8：自家条子的内容住在子窗里，比句柄必然不相等，
    /// 拿句柄判会把"鼠标停在工具条上"误判成别人占了那一层，于是永远退不出穿透态）。
    /// 自家窗拿走这一按不构成"一次按下两家用"，所以不让位，只走 <see cref="ShouldReorderFor"/>。</para>
    /// </summary>
    public static bool ShouldYieldPointer(AnnotationStage stage, bool hitBelongsToOurProcess)
        => stage == AnnotationStage.BoardDrawing && !hitBelongsToOurProcess;

    /// <summary>
    /// 自家窗（桌面组件那类）压在画布玻璃之上时，要不要重排一次。
    /// <para>这是"工具条说绘制中、点下去却在动组件"那一类：那一次按下并没有被两家用，
    /// 只是<b>根本没到画布</b>，所以不能和让位混成一条判据（混了就变成"画布自己把鼠标交回去了"的错觉）。</para>
    /// </summary>
    public static bool ShouldReorderFor(AnnotationStage stage, bool hitBelongsToOurProcess)
        => stage == AnnotationStage.BoardDrawing && hitBelongsToOurProcess;

    /// <summary>
    /// 穿透态要不要"让下层应用干活"。Sheet 态不谈这一位：那时玻璃整块不可见，
    /// 返回 false 会让调用方误以为该把样式位清掉，所以显式跟着"不可见"走。
    /// </summary>
    public static bool ShouldGlassBeClickThrough(AnnotationStage stage) => !stage.GlassTakesPointer();

    /// <summary>
    /// 能不能把 <paramref name="insertAfterHint"/> 递给 Win32 当 <c>hwndInsertAfter</c>。
    /// <para><b>必须是 topmost 窗</b>：Win32 明写"a topmost window repositioned after any non-topmost
    /// window is no longer topmost"，递错一个就把整块画布拽出 topmost 带（批次 WD-2 半对造成 WD-7 回归的真因）。
    /// 带成员资格只能靠 <c>HWND_TOPMOST</c> 建立，不靠口头保证。</para>
    /// </summary>
    public static bool IsSafeInsertAfter(bool insertAfterIsTopmost, bool insertAfterIsUsable)
        => insertAfterIsTopmost && insertAfterIsUsable;

    /// <summary>
    /// 这个角色的窗此刻应当插到<b>哪一个角色之下</b>：在场角色里，排名严格高于它、且离它最近的那一个。
    /// <para>取"最近"而不是"最高"是因为 Win32 只能把窗插到某个具体窗的<b>紧邻下方</b>：
    /// 递最上面那一条会把玻璃塞进工具条与快捷键面板之间，面板就掉到玻璃下面去了（画布上的面板点不动）。</para>
    /// <para>返回 null＝没有人在它上面，此时应当自己两步提层（先进带、再带内重排到顶）。</para>
    /// </summary>
    public static SurfaceRole? NearestAbove(
        SurfaceRole role, AnnotationStage stage, IEnumerable<SurfaceRole> present)
    {
        var mine = Rank(role, stage);
        SurfaceRole? best = null;
        var bestRank = int.MinValue;
        foreach (var other in present)
        {
            if (other == role) continue;
            var rank = Rank(other, stage);
            if (rank >= mine || rank <= bestRank) continue;
            best = other;
            bestRank = rank;
        }
        return best;
    }
}
