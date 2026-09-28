#nullable enable
namespace StarMark.Core.Canvas;

/// <summary>
/// 画布玻璃<b>底下那块背景</b>是什么（方案 §5 的"背景三态"里现在要的两态加一幕布）。
/// <para>
/// 为什么做成"提交那一帧时才叠"而不是"把空白像素换成那块底"：
/// 整条渲放管线（圆盘、取大混合、橡皮减 alpha、脏区）都在<b>预乘 + 透明</b>那套语义上，
/// 234 例逐像素参照也钉的是那一份。<b>把底写进缓冲</b>会让"取大"当场失效
/// （白色与任何墨逐通道取大＝永远白色＝什么都看不见），要改就得给热循环加分臂——
/// 那是批次 WG/S2-d 两条纪律都明确排除的形状（坑表 #164）。逐像素叠底只发生在提交脏区那一段，
/// 代价与脏区大小成正比。
/// </para>
/// </summary>
public enum CanvasBackdrop
{
    /// <summary>透明：看得见桌面，墨浮在上面（默认，也是画布一直以来的样子）。</summary>
    Transparent,

    /// <summary>白板底：整块屏幕是白底，墨画在白底上（演示/讲解用）。</summary>
    Whiteboard,

    /// <summary>
    /// 幕布：整块屏幕被半透明压暗，<b>只留鼠标那一圈亮</b>（讲课时把注意力收到指到的那块地方）。
    /// 与白板底的本质区别是<b>它不不透明</b>——所以它<b>允许穿透</b>（压暗着还能翻下面的 PPT），
    /// 也正是这个区别让"底"必须按预乘 alpha 参与混合，而不是一句"什么都不叠"。
    /// </summary>
    Curtain,
}

/// <summary>背景态的判据（全是纯函数，没有一处碰窗口；逐像素那一步在 Integrations 的 <c>BackdropBlend</c>）。</summary>
public static class CanvasBackdropMath
{
    /// <summary>白板底那块的色值（不透明白，<b>预乘</b> ARGB 打包）。</summary>
    public const uint WhiteboardArgb = 0xFFFFFFFFu;

    /// <summary>
    /// 幕布那块底：<b>预乘</b>的黑、alpha＝128（≈50% 压暗）。
    /// <para>写成 0x80000000 而不是"RGB=0,A=128 再乘一遍"：这条链上所有颜色都是预乘的，
    /// 传一个未预乘的值进混合式就会得到一块<b>偏亮</b>的帘子（同一课：<c>CanvasCompositor</c> 里通道 ≤ alpha）。</para>
    /// </summary>
    public const uint CurtainArgb = 0x80000000u;

    /// <summary>
    /// 焦点圈半径（<b>DIP</b>，不是物理像素）：混屏时按每块屏自己的缩放换算，
    /// 写死像素数就是在 150% 屏上小一半（批次 ⑦ 那条分岔同源）。
    /// </summary>
    public const double FocusRadiusDip = 160d;

    /// <summary>这块底是不是不透明的（不透明的底提交时可以直接把 alpha 顶满）。</summary>
    public static bool IsOpaque(CanvasBackdrop backdrop) => backdrop == CanvasBackdrop.Whiteboard;

    /// <summary>底色的预乘 ARGB；透明底没有"底色"可言，返回 0 让调用方自己判。</summary>
    public static uint ArgbOf(CanvasBackdrop backdrop) => backdrop switch
    {
        CanvasBackdrop.Whiteboard => WhiteboardArgb,
        CanvasBackdrop.Curtain => CurtainArgb,
        _ => 0u,
    };

    /// <summary>
    /// 这块底让不让鼠标穿透。<b>判据只有一条：底不透明就不让</b>。
    /// <para>白板态那块玻璃是整屏白，"穿透"等于让用户在一面看不见的白墙后面点东西（看得见的是墙，
    /// 点得着的是它后面那些窗）；幕布是半透明的，压暗着照样翻下面的 PPT——那正是它的主要用法。</para>
    /// <para>这里刻意<b>不</b>写成"只有白板不许"：加一个不透明的底（比如将来整屏实色）就会自动落进"不许穿透"，
    /// 而不是靠人记得改这一处。</para>
    /// </summary>
    public static bool AllowsClickThrough(CanvasBackdrop backdrop) => !IsOpaque(backdrop);

    /// <summary>这块底要不要那块跟着鼠标走的亮区（只有幕布要：白板没有"露出桌面"这回事）。</summary>
    public static bool HasFocusHole(CanvasBackdrop backdrop) => backdrop == CanvasBackdrop.Curtain;

    /// <summary>
    /// 那颗按钮按下去之后落到哪一态：<b>点的就是当前这块底 ⇒ 关掉它</b>，否则换过去。
    /// <para>"再点一次＝取消"与三条笔、截图那条"再点当前工具＝收笔"是同一套交互语言（用户裁决）。</para>
    /// </summary>
    public static CanvasBackdrop Toggle(CanvasBackdrop from, CanvasBackdrop target)
        => from == target ? CanvasBackdrop.Transparent : target;
}
