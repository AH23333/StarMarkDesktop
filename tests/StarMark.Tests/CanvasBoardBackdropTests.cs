#nullable enable
using System;
using System.Linq;
using StarMark.Core.Canvas;
using StarMark.Core.Capture;
using StarMark.Integrations.Canvas;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 白板底（批次 S4-①）的<b>判据与像素</b>那一半：背景态的纯函数、把预乘墨叠在不透明底上的算术、
/// 以及"白板不许与鼠标穿透并存"这条会话不变量。
/// <para>
/// 为什么值得单独钉：<b>底只能落在提交那一步</b>。把它写进托管缓冲（"空白像素换成白色"）会让取大混合当场失效
/// ——白与任何墨逐通道取大＝白＝整块板子什么都看不见；而修法若是给每帧几百万次的热循环加一条分臂，
/// 违反的是批次 WG／S2-d 两条纪律。所以这里钉的是"分臂只在提交路径上、算术只有那一处"。
/// </para>
/// </summary>
public sealed class CanvasBoardBackdropTests
{
    // ────────── 背景态的纯函数 ──────────

    [Fact]
    public void TransparentSubmitsNothingWhileWhiteboardIsOpaqueWhite()
    {
        // 0 是"什么都不叠"的记号（提交路径靠它走逐行快路），不是"叠一块黑底"
        Assert.Equal(0u, CanvasBackdropMath.ArgbOf(CanvasBackdrop.Transparent));
        Assert.False(CanvasBackdropMath.IsOpaque(CanvasBackdrop.Transparent));
        Assert.Equal(0xFFFFFFFFu, CanvasBackdropMath.WhiteboardArgb);           // 不透明白
        Assert.Equal(CanvasBackdropMath.WhiteboardArgb, CanvasBackdropMath.ArgbOf(CanvasBackdrop.Whiteboard));
        Assert.True(CanvasBackdropMath.IsOpaque(CanvasBackdrop.Whiteboard));
    }

    /// <summary>"不透明"与"不许穿透"必须同进同退：二者一旦分岔，就会出现一整面点不动的白墙。</summary>
    [Fact]
    public void OpaqueBackdropIsExactlyTheOneThatRefusesClickThrough()
    {
        foreach (var backdrop in Enum.GetValues<CanvasBackdrop>())
            Assert.Equal(CanvasBackdropMath.IsOpaque(backdrop), !CanvasBackdropMath.AllowsClickThrough(backdrop));
    }

    [Fact]
    public void ToggleTurnsTheSameBackdropOffAndNeverLeavesTheTable()
    {
        // 「点的正是当前这块」＝关掉它（与三条笔"再点当前这支＝收笔"同一交互语言）
        Assert.Equal(CanvasBackdrop.Transparent, CanvasBackdropMath.Toggle(CanvasBackdrop.Whiteboard, CanvasBackdrop.Whiteboard));
        Assert.Equal(CanvasBackdrop.Whiteboard, CanvasBackdropMath.Toggle(CanvasBackdrop.Transparent, CanvasBackdrop.Whiteboard));
        // 两块底互斥：幕布开着点「白板」＝直接换成白板，不会留下"既暗又白"这种没人见过的组合
        Assert.Equal(CanvasBackdrop.Whiteboard, CanvasBackdropMath.Toggle(CanvasBackdrop.Curtain, CanvasBackdrop.Whiteboard));
        Assert.Equal(CanvasBackdrop.Curtain, CanvasBackdropMath.Toggle(CanvasBackdrop.Whiteboard, CanvasBackdrop.Curtain));
        Assert.Equal(CanvasBackdrop.Transparent, CanvasBackdropMath.Toggle(CanvasBackdrop.Curtain, CanvasBackdrop.Curtain));
        foreach (var backdrop in Enum.GetValues<CanvasBackdrop>())
            Assert.Contains(CanvasBackdropMath.Toggle(backdrop, CanvasBackdrop.Curtain), Enum.GetValues<CanvasBackdrop>());
    }

    /// <summary>幕布是<b>半透明</b>的：压暗着还能穿透（那正是它的主要用法）；不许穿透的那条闸门只认"不透明"。</summary>
    [Fact]
    public void CurtainDimsButStillLetsThePointerThrough()
    {
        Assert.Equal(0x80000000u, CanvasBackdropMath.ArgbOf(CanvasBackdrop.Curtain));   // 预乘黑，alpha=128
        Assert.False(CanvasBackdropMath.IsOpaque(CanvasBackdrop.Curtain));
        Assert.True(CanvasBackdropMath.AllowsClickThrough(CanvasBackdrop.Curtain));
        Assert.True(CanvasBackdropMath.HasFocusHole(CanvasBackdrop.Curtain));
        Assert.False(CanvasBackdropMath.HasFocusHole(CanvasBackdrop.Whiteboard));       // 白板没有"露出桌面"这回事
        Assert.False(CanvasBackdropMath.HasFocusHole(CanvasBackdrop.Transparent));
    }

    /// <summary>
    /// 事件→背景态的映射只有一份（与"哪块底不许穿透"是同一条知识，分两处写迟早一处没更新）。
    /// 别的动作一律返回 null：否则「交出鼠标」会被当成换底，鼠标归属就没人改了。
    /// </summary>
    [Fact]
    public void OnlyTheTwoBackdropEventsNameABackdrop()
    {
        Assert.Equal(CanvasBackdrop.Whiteboard, AnnotationSessions.BackdropTargetOf(SessionEvent.ToggleWhiteboard));
        Assert.Equal(CanvasBackdrop.Curtain, AnnotationSessions.BackdropTargetOf(SessionEvent.ToggleCurtain));
        foreach (var what in Enum.GetValues<SessionEvent>())
            if (what is not (SessionEvent.ToggleWhiteboard or SessionEvent.ToggleCurtain))
                Assert.Null(AnnotationSessions.BackdropTargetOf(what));
    }

    // ────────── 会话不变量（判据在 Core，接线只调它）──────────

    [Fact]
    public void OnlyGivingThePointerBackIsBlockedByTheWhiteboard()
    {
        // 三臂各钉：被拦的那一条、同事件的另一态、以及"反过来那半下"与别的动作都不该被拦
        Assert.True(AnnotationSessions.IsBlockedByBackdrop(SessionEvent.GivePointerBack, CanvasBackdrop.Whiteboard));
        Assert.False(AnnotationSessions.IsBlockedByBackdrop(SessionEvent.GivePointerBack, CanvasBackdrop.Transparent));
        Assert.False(AnnotationSessions.IsBlockedByBackdrop(SessionEvent.TakePointer, CanvasBackdrop.Whiteboard));
        Assert.False(AnnotationSessions.IsBlockedByBackdrop(SessionEvent.ToggleBoard, CanvasBackdrop.Whiteboard));
        Assert.False(AnnotationSessions.IsBlockedByBackdrop(SessionEvent.ToggleWhiteboard, CanvasBackdrop.Whiteboard));
        Assert.False(AnnotationSessions.IsBlockedByBackdrop(SessionEvent.ToggleCurtain, CanvasBackdrop.Whiteboard));
    }

    /// <summary>被拦住的那一下<b>必须给得出原因</b>；没被拦时不许有话（状态行不是垃圾桶）。</summary>
    [Fact]
    public void TheBlockedArmIsTheOnlyOneWithAReason()
    {
        foreach (var what in Enum.GetValues<SessionEvent>())
            foreach (var backdrop in Enum.GetValues<CanvasBackdrop>())
            {
                var reason = AnnotationSessions.ReasonBlockedByBackdrop(what, backdrop);
                Assert.Equal(AnnotationSessions.IsBlockedByBackdrop(what, backdrop), reason is not null);
                if (reason is not null) Assert.Contains("白板", reason);
            }
    }

    /// <summary>上白板那一刻要把鼠标收回给画布；下白板不动归属（用户原来在画就继续画，原来要交就继续交）。</summary>
    [Fact]
    public void EnteringWhiteboardTakesThePointer_LeavingItDoesNot()
    {
        Assert.Equal(SessionEvent.TakePointer, AnnotationSessions.PointerConsequenceOf(CanvasBackdrop.Whiteboard));
        Assert.Null(AnnotationSessions.PointerConsequenceOf(CanvasBackdrop.Transparent));
    }

    /// <summary>
    /// 白板态与穿透态<b>不能并存</b>这条不变量在转移表层面的形状：交出鼠标那条转移会被闸门挡住，
    /// 所以会话里根本不会出现"BoardPenetrating + Whiteboard"——每帧对账因此不需要知道背景态。
    /// </summary>
    [Fact]
    public void TheStageTableNeverNeedsToKnowAboutTheBackdrop()
    {
        // 反过来钉住"为什么不用改 LayerRules"：这条链上多一个自变量就多一处可能写反的方向（批次 WF-1）
        Assert.Equal(AnnotationStage.BoardDrawing,
            AnnotationSessions.Move(AnnotationStage.BoardPenetrating, SessionEvent.TakePointer, null));
        Assert.Equal(AnnotationStage.BoardPenetrating,
            AnnotationSessions.Move(AnnotationStage.BoardDrawing, SessionEvent.GivePointerBack, null));
    }

    // ────────── 叠底算术 ──────────

    [Fact]
    public void BlankAndUnpaintedPixelsBecomeTheBoardExactly_NotALittleGrey()
    {
        var white = CanvasBackdropMath.WhiteboardArgb;
        Assert.Equal(white, BackdropBlend.Over(0u, white));                                   // a=0：这一帧这块没画过
        // a=1 是空白像素的命中测试哨兵（BlankPixel），不是"一层极淡的墨"。
        // 走混合式的话整块白板会停在 254——差得看不见，但"这是一张干净的白纸"正是这块底的全部意义。
        Assert.Equal(0x01000000u, LayeredCanvasWindow.BlankPixel);
        Assert.Equal(white, BackdropBlend.Over(LayeredCanvasWindow.BlankPixel, white));
    }

    [Fact]
    public void SolidInkKeepsItsOwnColour_OverAnyBoard()
    {
        // 打包是 B|G<<8|R<<16|A<<24（与分层窗缓冲同一份字节序），所以"实心红"是 0xFFFF0000 而不是 0xFF0000FF
        Assert.Equal(0xFFFF0000u, BackdropBlend.Over(0xFFFF0000u, 0xFFFFFFFFu));   // 实心红：一点底都不掺
        Assert.Equal(0xFF123456u, BackdropBlend.Over(0xFF123456u, 0xFF806040u));
    }

    /// <summary>半透明墨叠在白底上的逐通道结论（钉死数字＝"换算法要看得见"，不是"跑起来再说"）。</summary>
    [Fact]
    public void HalfAlphaBlackAndRedOverWhiteArePinnedToTheByte()
    {
        Assert.Equal(0xFF7F7F7Fu, BackdropBlend.Over(0x80000000u, 0xFFFFFFFFu));   // 五成黑＝127 灰
        // 预乘五成红：R 通道（第 16–23 位）＝128 → 0x80800000。掺白底之后红通道被推满、G/B 各留 127。
        Assert.Equal(0xFFFF7F7Fu, BackdropBlend.Over(0x80800000u, 0xFFFFFFFFu));   // 三条通道共用同一个 alpha
    }

    /// <summary>底色是参数而不是常量：换一块有色底（S4 后面的幕布）不该再写一份混合式。</summary>
    [Fact]
    public void MidAlphaOverAColouredBoardIsThePreMultipliedSum()
        => Assert.Equal(0xFF605040u, BackdropBlend.Over(0x80202020u, 0xFF806040u));

    /// <summary>
    /// 浓度从淡到浓，结果必须<b>一路单调地离开底色</b>且不溢出字节。
    /// <para>用"逐档比大小"而不是"取一个中点"：整数近似 <c>(t+(t&gt;&gt;8)+127)&gt;&gt;8</c> 写错（少加那个 127）
    /// 会让整块板子系统性偏暗，而偏暗在任何单个取值上都看不出来。</para>
    /// </summary>
    [Fact]
    public void RisingInkAlphaNeverOverflows_AndAlwaysMovesAwayFromTheBoard()
    {
        var white = CanvasBackdropMath.WhiteboardArgb;
        var previous = 255L;                             // a=0 时通道就是底色 255
        for (var a = 2; a <= 254; a++)
        {
            var mixed = BackdropBlend.Over((uint)a << 24, white);   // 纯黑、预乘：通道恒 0
            var channel = mixed & 0xFFL;
            Assert.True(channel is >= 0 and <= 255);
            Assert.True(channel < previous);
            previous = channel;
        }
        // 三条通道一起看：预乘的黑在任何 alpha 上都该是灰（B==G==R），偏色＝通道各拿了不同的 alpha
        foreach (var a in new[] { 8, 64, 128, 200, 250 })
        {
            var mixed = BackdropBlend.Over((uint)a << 24, white);
            Assert.Equal(mixed & 0xFF, mixed >> 8 & 0xFF);
            Assert.Equal(mixed >> 8 & 0xFF, mixed >> 16 & 0xFF);
            Assert.Equal(0xFFu, mixed >> 24);            // 交出去的一层永远不透明
        }
    }

    /// <summary>同一条式子也要算半透明的底：幕布上没墨的那片就是那块帘子本身（alpha=128 仍是"这块玻璃的像素"）。</summary>
    [Fact]
    public void SemiTransparentBackdropKeepsTheInkPremultiplied()
    {
        var curtain = CanvasBackdropMath.ArgbOf(CanvasBackdrop.Curtain);
        Assert.Equal(curtain, BackdropBlend.Over(LayeredCanvasWindow.BlankPixel, curtain));
        Assert.Equal(0xFF808080u, BackdropBlend.Over(0xFF808080u, curtain));      // 实心灰：底一点不掺
        var mixed = BackdropBlend.Over(0x80000000u, curtain);                      // 五成墨叠在五成帘上
        Assert.Equal(0u, mixed & 0xFFFFFFu);
        Assert.Equal(192u, mixed >>> 24);                                          // 128 + 128×127/255
    }

    /// <summary>
    /// "叠一块 0 底"是个陷阱：它会把空白像素的 alpha 抹成 0，而 alpha=0 在鼠标眼里不属于这块玻璃
    /// ⇒ 亮区里点不动也画不上（批次 WO 那条坑的另一个入口）。所以两个调用方对亮区都走<b>原样拷贝</b>，
    /// 这条断言钉的就是"别改成传 0"。
    /// </summary>
    [Fact]
    public void ZeroBackdropWouldEraseTheHitTestableBlankPixel()
        => Assert.Equal(0u, BackdropBlend.Over(LayeredCanvasWindow.BlankPixel, 0u));
}
