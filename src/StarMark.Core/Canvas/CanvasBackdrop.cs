#nullable enable
namespace StarMark.Core.Canvas;

/// <summary>
/// 画布玻璃<b>底下那块背景</b>是什么（方案 §5 的"背景三态"里现在要的两态）。
/// <para>
/// 为什么做成"提交那一帧时才叠"而不是"把空白像素换成白色"：
/// 整条渲放管线（圆盘、取大混合、橡皮减 alpha、脏区）都在<b>预乘 + 透明</b>那套语义上，
/// 234 例逐像素参照也钉的是那一份。<b>把底白写进缓冲</b>会让"取大"当场失效
/// （白色与任何墨逐通道取大＝永远白色＝什么都看不见），要改就得给热循环加分臂——
/// 那是批次 WG/S2-d 两条纪律都明确排除的形状。预乘墨<b>叠在不透明底上</b>是一次逐像素的
/// <c>ink + backboard × (1 − a)</c>，只发生在提交脏区那一段，代价与脏区大小成正比。
/// </para>
/// </summary>
public enum CanvasBackdrop
{
    /// <summary>透明：看得见桌面，墨浮在上面（默认，也是画布一直以来的样子）。</summary>
    Transparent,

    /// <summary>白板底：整块屏幕是白底，墨画在白底上（演示/讲解用）。</summary>
    Whiteboard,
}

/// <summary>背景态的判据与像素算术（全是纯函数，没有一处碰窗口）。</summary>
public static class CanvasBackdropMath
{
    /// <summary>白板底那块的色值（不透明白，ARGB 打包成 <see cref="uint"/>）。</summary>
    public const uint WhiteboardArgb = 0xFFFFFFFFu;

    /// <summary>这块底是不是不透明的（透明底要走"什么都不叠"那条快路）。</summary>
    public static bool IsOpaque(CanvasBackdrop backdrop) => backdrop == CanvasBackdrop.Whiteboard;

    /// <summary>底色的 ARGB；透明底没有"底色"可言，返回 0 让调用方自己判 <see cref="IsOpaque"/>。</summary>
    public static uint ArgbOf(CanvasBackdrop backdrop) => backdrop switch
    {
        CanvasBackdrop.Whiteboard => WhiteboardArgb,
        _ => 0u,
    };

    /// <summary>
    /// 白板态下<b>不许把鼠标交给下层</b>：那块玻璃此刻是整屏白，"穿透"等于让用户点一块他看不见的桌面
    /// （看得见的是白墙，点得着的是它后面那些窗）。唯一的例外是"先关白板"，所以调用方要给得出这句话。
    /// </summary>
    public static bool AllowsClickThrough(CanvasBackdrop backdrop) => backdrop != CanvasBackdrop.Whiteboard;

    /// <summary>「再点一次」换到哪一态（工具条那颗与 <c>canvas.board</c> 同一句判据，别处不自己翻）。</summary>
    public static CanvasBackdrop Next(CanvasBackdrop from)
        => from == CanvasBackdrop.Whiteboard ? CanvasBackdrop.Transparent : CanvasBackdrop.Whiteboard;

    // 逐像素那一步（把预乘的墨叠在不透明底上）<b>不在这里</b>：它是提交那一帧的算术，住在
    // Integrations 的 <c>BackdropBlend</c>（Core 反过来依赖 Integrations，方向不能倒）。
    // 为什么"底"只能提交时叠、不能直接写进缓冲：<see cref="CanvasBackdrop"/> 的类注释里写了——
    // 白底会让"取大"混合当场失效（任何墨与白逐通道取大＝白），改混合律就是给每帧几百万次的热循环加分臂。
}
