using System.Linq;
using StarMark.Core.Capture;
using StarMark.Core.Hotkeys;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 屏幕标注系统整合方案 S1 的<b>仲裁判据表</b>（架构方案 §4/§5/§6.1）。
/// <para>
/// 这一批把"此刻谁在吃鼠标"的答案从三处收到一处，所以这里逐值钉的不是实现细节，
/// 而是<b>那张表本身</b>：玻璃该不该显、读态该不该跑、谁压在谁上面、九条键该不该摘、Esc 收哪一层。
/// 判据在 Core 纯函数里，UI 只是它的执行者——反过来（UI 自己现写布尔）就是本项目已定案禁止的那种写法。
/// </para>
/// </summary>
public class AnnotationSessionTests
{
    private static readonly AnnotationStage[] All = System.Enum.GetValues<AnnotationStage>();

    // ────────── 转移表 ──────────

    // 2026-09-27 用户改判：换工具不再翻穿透态，所以这里没有 Pick* 那两臂了——
    // "选一支笔"不再产生迁移（见 CanvasService.SelectTool：它只重放当前态）。
    [Theory]
    [InlineData(AnnotationStage.Idle, SessionEvent.ToggleBoard, AnnotationStage.BoardPenetrating)]
    [InlineData(AnnotationStage.BoardPenetrating, SessionEvent.ToggleBoard, AnnotationStage.Idle)]
    [InlineData(AnnotationStage.BoardDrawing, SessionEvent.ToggleBoard, AnnotationStage.Idle)]
    [InlineData(AnnotationStage.Sheet, SessionEvent.ToggleBoard, AnnotationStage.Sheet)]
    [InlineData(AnnotationStage.BoardPenetrating, SessionEvent.GivePointerBack, AnnotationStage.BoardPenetrating)]
    [InlineData(AnnotationStage.BoardDrawing, SessionEvent.GivePointerBack, AnnotationStage.BoardPenetrating)]
    [InlineData(AnnotationStage.BoardPenetrating, SessionEvent.TakePointer, AnnotationStage.BoardDrawing)]
    [InlineData(AnnotationStage.BoardDrawing, SessionEvent.TakePointer, AnnotationStage.BoardDrawing)]
    [InlineData(AnnotationStage.Idle, SessionEvent.TakePointer, AnnotationStage.Idle)]
    [InlineData(AnnotationStage.Idle, SessionEvent.GivePointerBack, AnnotationStage.Idle)]
    public void EachEventMovesToTheStagedTarget(
        AnnotationStage from, SessionEvent what, AnnotationStage expected)
        => Assert.Equal(expected, AnnotationSessions.Move(from, what, boardBeforeSheet: null));

    [Fact]
    public void OpeningTheBoardLandsOnPenetratingNotDrawing()
    {
        // 默认态是穿透：一开就吃掉全屏鼠标，在用户眼里等于"电脑死了"（PPT 翻不动、下层点不动）
        Assert.Equal(AnnotationStage.BoardPenetrating,
            AnnotationSessions.Move(AnnotationStage.Idle, SessionEvent.ToggleBoard, null));
        // 改判之后没有第二条了：板子没开时按工具热键走的也是这一臂（先开板、停在穿透态），
        // 想画再由那颗「穿透」按钮或 canvas.through 关掉——换工具本身永远不改鼠标归属。
        Assert.Equal(AnnotationStage.BoardDrawing,
            AnnotationSessions.Move(AnnotationStage.BoardPenetrating, SessionEvent.TakePointer, null));
    }

    [Theory]
    [InlineData(AnnotationStage.Idle)]
    [InlineData(AnnotationStage.BoardPenetrating)]
    [InlineData(AnnotationStage.BoardDrawing)]
    public void OnlyBeginSheetEntersSheet(AnnotationStage from)
    {
        Assert.Equal(AnnotationStage.Sheet, AnnotationSessions.Move(from, SessionEvent.BeginSheet, null));
        // 反向：除了 BeginSheet 与 Sheet 内部自身，任何事件都不许把别的态"顺手变成截图"
        foreach (SessionEvent what in System.Enum.GetValues<SessionEvent>())
        {
            var to = AnnotationSessions.Move(from, what, AnnotationStage.BoardDrawing);
            if (what == SessionEvent.BeginSheet) continue;
            Assert.False(from != AnnotationStage.Sheet && to == AnnotationStage.Sheet,
                $"{from} + {what} 不该进 Sheet（截图只能由冻帧那一步发起）");
        }
    }

    [Fact]
    public void SheetSubstatesDoNotChangeTheGlobalStage()
    {
        // 截图里换笔／交出鼠标／开关画板：全局态必须还站着 Sheet，否则"截图中把遮罩弄没了"
        Assert.Equal(AnnotationStage.Sheet,
            AnnotationSessions.Move(AnnotationStage.Sheet, SessionEvent.TakePointer, null));
        Assert.Equal(AnnotationStage.Sheet,
            AnnotationSessions.Move(AnnotationStage.Sheet, SessionEvent.GivePointerBack, null));
        Assert.Equal(AnnotationStage.Sheet,
            AnnotationSessions.Move(AnnotationStage.Sheet, SessionEvent.ToggleBoard, null));
        // 重入冻帧（另一扇屏也 BeginSheet）仍是 Sheet，且不会把自己关掉
        Assert.Equal(AnnotationStage.Sheet,
            AnnotationSessions.Move(AnnotationStage.Sheet, SessionEvent.BeginSheet, null));
    }

    [Theory]
    [InlineData(AnnotationStage.BoardPenetrating)]
    [InlineData(AnnotationStage.BoardDrawing)]
    public void LeavingSheetRestoresTheFrozenSubstateSnapshot(AnnotationStage before)
        => Assert.Equal(before,
            AnnotationSessions.Move(AnnotationStage.Sheet, SessionEvent.EndSheet, before));

    [Fact]
    public void LeavingSheetWithoutABoardGoesToIdle()
        => Assert.Equal(AnnotationStage.Idle,
            AnnotationSessions.Move(AnnotationStage.Sheet, SessionEvent.EndSheet, boardBeforeSheet: null));

    [Fact]
    public void ASecondSheetFinishDoesNotRewriteTheStage()
    {
        // 会话收尾可能被路过两次（提交与关窗各回调一次）。第二次的落点必须是"什么都不动"：
        // 若照快照改写，用户看到的就是"截图结束把画板又复位了一遍"，
        // 而更坏的是 BoardDrawing 里用户已经改了工具，被一次多余的收尾悄悄改回去。
        var afterFirst = AnnotationSessions.Move(AnnotationStage.Sheet, SessionEvent.EndSheet,
            AnnotationStage.BoardDrawing);
        Assert.Equal(AnnotationStage.BoardDrawing,
            AnnotationSessions.Move(afterFirst, SessionEvent.EndSheet, AnnotationStage.BoardPenetrating));
    }

    [Fact]
    public void NoStageOrEventCombinationIsLeftUndefined()
    {
        // 穷举臂：任何 (态, 事件) 都要给出一个<b>在场</b>的态，且不能原样吐回一个不存在的值。
        // 这条是"半年后加第五种工具态时忘了补臂"的兜底（加一个枚举值，这一条立刻红）。
        foreach (var stage in All)
            foreach (SessionEvent what in System.Enum.GetValues<SessionEvent>())
            {
                var to = AnnotationSessions.Move(stage, what, AnnotationStage.BoardPenetrating);
                Assert.Contains(to, All);
                if (stage == AnnotationStage.Idle
                    && what is SessionEvent.EndSheet or SessionEvent.GivePointerBack or SessionEvent.TakePointer)
                    Assert.Equal(AnnotationStage.Idle, to);      // 没有会话在场时，收尾与"交出/收回鼠标"都不该凭空开出一块板子
            }
    }

    // ────────── 逐态读法（三处自愈合成一张表）──────────

    [Theory]
    [InlineData(AnnotationStage.Idle, false)]
    [InlineData(AnnotationStage.BoardPenetrating, true)]
    [InlineData(AnnotationStage.BoardDrawing, true)]
    [InlineData(AnnotationStage.Sheet, false)]
    public void GlassVisibilityIsOneRowPerStage(AnnotationStage stage, bool expected)
    {
        Assert.Equal(expected, stage.GlassVisible());
        // 玻璃不可见时工具条也不该飘着：截图期间两条栏同时在场就是"哪一条管当前这件事"说不清
        Assert.Equal(expected, stage.BoardStripVisible());
    }

    [Theory]
    [InlineData(AnnotationStage.Idle, false)]
    [InlineData(AnnotationStage.BoardPenetrating, true)]     // 只有这一态收不到按下，才需要自己看按键状态
    [InlineData(AnnotationStage.BoardDrawing, false)]        // 绘制态本来收得到：再叠一套读态＝同一按两家用（C2）
    [InlineData(AnnotationStage.Sheet, false)]
    public void QuickDrawReadsOnlyInPenetrating(AnnotationStage stage, bool expected)
        => Assert.Equal(expected, stage.QuickDrawReads());

    [Theory]
    [InlineData(AnnotationStage.Idle, false)]
    [InlineData(AnnotationStage.BoardPenetrating, false)]
    [InlineData(AnnotationStage.BoardDrawing, true)]
    [InlineData(AnnotationStage.Sheet, false)]               // 那时玻璃整块不可见，谈不上吃不吃鼠标
    public void OnlyDrawingTakesThePointer(AnnotationStage stage, bool expected)
        => Assert.Equal(expected, stage.GlassTakesPointer());

    [Fact]
    public void SheetIsTheOnlyStageThatSuppressesCanvasHotkeys()
    {
        Assert.False(AnnotationStage.Idle.SuppressesCanvasHotkeys());
        Assert.False(AnnotationStage.BoardPenetrating.SuppressesCanvasHotkeys());
        Assert.False(AnnotationStage.BoardDrawing.SuppressesCanvasHotkeys());
        Assert.True(AnnotationStage.Sheet.SuppressesCanvasHotkeys());
    }

    [Fact]
    public void FrameAuditRunsOnlyWhileABoardIsOnScreen()
    {
        // Idle 时逐屏读样式位是白做的（那块玻璃根本不在了）；Sheet 时同理。
        Assert.False(AnnotationStage.Idle.NeedsFrameAudit());
        Assert.False(AnnotationStage.Sheet.NeedsFrameAudit());
        Assert.True(AnnotationStage.BoardPenetrating.NeedsFrameAudit());
        Assert.True(AnnotationStage.BoardDrawing.NeedsFrameAudit());
    }

    // ────────── Z 序与让位 ──────────

    [Fact]
    public void StripIsAlwaysAboveEverything()
        => Assert.All(All, stage => Assert.Equal(1, LayerRules.Rank(SurfaceRole.Strip, stage)));

    [Fact]
    public void RanksAreDistinctSoNobodyTunesOnACoin()
    {
        // 并列排名＝"谁在上"取决于谁最后被交互过一次，那正是 C1 拉扯的定义。
        var roles = new[] { SurfaceRole.Strip, SurfaceRole.Sheet, SurfaceRole.Pin, SurfaceRole.Board };
        foreach (var stage in All)
        {
            var ranks = roles.Select(role => LayerRules.Rank(role, stage)).ToList();
            Assert.Equal(ranks.Count, ranks.Distinct().Count());
            Assert.All(ranks, rank => Assert.True(rank < 9, $"{stage} 下 {roles[ranks.IndexOf(rank)]} 没有排名"));
        }
    }

    [Fact]
    public void EachStageFlipsOnlyTheGlassAgainstThePins()
    {
        // 这张表里只有"玻璃 vs 贴图"随会话态翻面，其余相对次序恒定——
        // 翻面的那一臂若写错，症状就是"贴图上圈不了注"或"贴图看得见点不到"，两种都各有真机先例。
        Assert.True(LayerRules.Rank(SurfaceRole.Board, AnnotationStage.Sheet)
            > LayerRules.Rank(SurfaceRole.Sheet, AnnotationStage.Sheet));
        Assert.Equal(LayerRules.Rank(SurfaceRole.Pin, AnnotationStage.Idle),
            LayerRules.Rank(SurfaceRole.Pin, AnnotationStage.Sheet));
        Assert.Equal(LayerRules.Rank(SurfaceRole.Pin, AnnotationStage.BoardPenetrating),
            LayerRules.Rank(SurfaceRole.Pin, AnnotationStage.Sheet));
    }

    [Fact]
    public void DrawingPlacesTheGlassAboveThePinsSoYouCanAnnotateThem()
    {
        // 用例 5：贴图在场、切到绘制态、在贴图上圈注——玻璃压在贴图之下时点下去动的是那张贴图。
        Assert.True(LayerRules.Rank(SurfaceRole.Board, AnnotationStage.BoardDrawing)
            < LayerRules.Rank(SurfaceRole.Pin, AnnotationStage.BoardDrawing));
    }

    [Fact]
    public void PenetratingPlacesTheGlassBeneathThePinsSoTheyStayClickable()
    {
        // C8：穿透态那块玻璃虽然不吃鼠标，但盖在贴图之上时贴图就是"看得见点不到"。
        Assert.True(LayerRules.Rank(SurfaceRole.Pin, AnnotationStage.BoardPenetrating)
            < LayerRules.Rank(SurfaceRole.Board, AnnotationStage.BoardPenetrating));
        Assert.True(LayerRules.Rank(SurfaceRole.Pin, AnnotationStage.Idle)
            < LayerRules.Rank(SurfaceRole.Board, AnnotationStage.Idle));
    }

    [Fact]
    public void TheFrozenSheetCoversEveryOtherOwnWindow()
    {
        // 用例 1/2：截图期间冻帧盖住一切自家他窗，也盖住贴图（"贴图在截图时不被误提到最上层"）。
        Assert.True(LayerRules.Rank(SurfaceRole.Sheet, AnnotationStage.Sheet)
            < LayerRules.Rank(SurfaceRole.Pin, AnnotationStage.Sheet));
        Assert.True(LayerRules.Rank(SurfaceRole.Sheet, AnnotationStage.Sheet)
            < LayerRules.Rank(SurfaceRole.Board, AnnotationStage.Sheet));
        Assert.True(LayerRules.Rank(SurfaceRole.Strip, AnnotationStage.Sheet)
            < LayerRules.Rank(SurfaceRole.Sheet, AnnotationStage.Sheet));
    }

    [Theory]
    [InlineData(AnnotationStage.BoardDrawing, false, true, false)]    // 别人占了那一层：交回鼠标，不重排
    [InlineData(AnnotationStage.BoardDrawing, true, false, true)]     // 自家窗压上来：不交出鼠标，只重排
    [InlineData(AnnotationStage.BoardPenetrating, false, false, false)]  // 穿透态"不是我们"是设计本意
    [InlineData(AnnotationStage.Sheet, false, false, false)
    ]
    public void YieldAndReorderAreDifferentQuestions(
        AnnotationStage stage, bool ours, bool expectYield, bool expectReorder)
    {
        Assert.Equal(expectYield, LayerRules.ShouldYieldPointer(stage, ours));
        Assert.Equal(expectReorder, LayerRules.ShouldReorderFor(stage, ours));
    }

    [Fact]
    public void OnlyATopmostWindowMayBeHandedToWin32AsInsertAfter()
    {
        // 递给一个非 topmost 的窗会把整块画布拽出带（Win32 明写；WD-2 半对造成 WD-7 回归的真因）
        Assert.True(LayerRules.IsSafeInsertAfter(insertAfterIsTopmost: true, insertAfterIsUsable: true));
        Assert.False(LayerRules.IsSafeInsertAfter(insertAfterIsTopmost: false, insertAfterIsUsable: true));
        Assert.False(LayerRules.IsSafeInsertAfter(insertAfterIsTopmost: true, insertAfterIsUsable: false));
    }

    // ────────── 按会话的热键投影 ──────────

    [Theory]
    [InlineData(true, AnnotationStage.BoardDrawing)]
    [InlineData(false, AnnotationStage.Sheet)]
    public void NonCanvasActionsAreNeverTouchedByThisGate(bool canvasEnabled, AnnotationStage stage)
    {
        Assert.True(HotkeyGate.ShouldRegister(HotkeyActions.ScreenCapture, canvasEnabled, stage));
        Assert.True(HotkeyGate.ShouldRegister(HotkeyActions.MainToggle, canvasEnabled, stage));
    }

    [Theory]
    [InlineData(true, AnnotationStage.Idle)]
    [InlineData(false, AnnotationStage.Idle)]
    [InlineData(true, AnnotationStage.Sheet)]
    [InlineData(false, AnnotationStage.Sheet)]
    public void TheToggleKeyStaysRegisteredInEveryCombination(bool canvasEnabled, AnnotationStage stage)
        // 留住它才有地方说一句"为什么不生效"；摘掉它就成了最坏的收尾——哑键。
        => Assert.True(HotkeyGate.ShouldRegister(HotkeyActions.CanvasToggle, canvasEnabled, stage));

    [Theory]
    [InlineData(true, AnnotationStage.BoardPenetrating, true)]
    [InlineData(true, AnnotationStage.BoardDrawing, true)]
    [InlineData(true, AnnotationStage.Sheet, false)]        // C4：截图进行中不注册
    [InlineData(false, AnnotationStage.BoardDrawing, false)] // 总开关关掉时整批不注册（既有先例）
    [InlineData(false, AnnotationStage.Sheet, false)]
    public void TheNineInnerActionsFollowBothSwitches(
        bool canvasEnabled, AnnotationStage stage, bool expected)
        => Assert.Equal(expected, HotkeyGate.ShouldRegister(HotkeyActions.CanvasPen, canvasEnabled, stage));

    [Fact]
    public void EveryCanvasActionButTheToggleIsSuppressedByASheetSession()
    {
        // 遍历而不是数数量：将来加第十种画布动作时，它自动要落进"截图期间不注册"这一批，
        // 或者在这里红一次——不该由"闸门里写死的数字"跟着改绿。
        foreach (var action in HotkeyActions.Canvas)
            Assert.Equal(action == HotkeyActions.CanvasToggle,
                HotkeyGate.ShouldRegister(action, canvasEnabled: true, AnnotationStage.Sheet));
        Assert.Equal(HotkeyActions.Canvas.Count - 1, HotkeyGate.SuppressedBySheet().Count);
        Assert.DoesNotContain(HotkeyActions.CanvasToggle, HotkeyGate.SuppressedBySheet());
    }

    [Fact]
    public void TheSheetReasonWinsOverTheDisabledSwitchReason()
    {
        // 截图进行中按画布开关：正确回答是"等截图结束"，而不是把用户支使去设置页开一个本来就开着的功能。
        Assert.Equal("截图进行中，这条快捷键要等截图结束才归画布",
            HotkeyGate.ReasonNotRunning(HotkeyActions.CanvasToggle, canvasEnabled: false, AnnotationStage.Sheet));
        // 反过来：会话期间这条键<b>继续注册着</b>，所以必须有原因可说。
        // 少这一臂的话，按下去仍然"什么都不知道地没反应"——那正是这条设计要消掉的东西。
        Assert.True(HotkeyGate.ShouldRegister(HotkeyActions.CanvasToggle, false, AnnotationStage.Sheet));
        Assert.NotNull(HotkeyGate.ReasonNotRunning(HotkeyActions.CanvasToggle, false, AnnotationStage.Sheet));
    }

    [Fact]
    public void ReasonsAreNullWhenTheActionIsLive()
    {
        Assert.Null(HotkeyGate.ReasonNotRunning(HotkeyActions.CanvasUndo, true, AnnotationStage.BoardDrawing));
        Assert.Null(HotkeyGate.ReasonNotRunning(HotkeyActions.ScreenCapture, false, AnnotationStage.Sheet));
        // 总开关关着（且不在截图）：说的是"去设置里打开"，不提快捷键（那会把人引向一条哑键）
        Assert.Equal("要先在 设置 → 拓展功能 里打开「屏幕画布」",
            HotkeyGate.ReasonNotRunning(HotkeyActions.CanvasPen, false, AnnotationStage.Idle));
    }

    // ────────── Esc 路由 ──────────

    [Theory]
    [InlineData(AnnotationStage.BoardPenetrating, true, EscapeStep.CloseWorkInProgress)]
    [InlineData(AnnotationStage.BoardDrawing, true, EscapeStep.CloseWorkInProgress)]
    [InlineData(AnnotationStage.BoardPenetrating, false, EscapeStep.ExitBoard)]
    [InlineData(AnnotationStage.BoardDrawing, false, EscapeStep.ExitBoard)]
    [InlineData(AnnotationStage.Sheet, true, EscapeStep.CloseWorkInProgress)]
    [InlineData(AnnotationStage.Sheet, false, EscapeStep.CancelSheet)]
    [InlineData(AnnotationStage.Idle, false, EscapeStep.Nothing)]
    public void EscapeClosesTheInnermostThingFirst(
        AnnotationStage stage, bool hasWork, EscapeStep expected)
        => Assert.Equal(expected, EscapeRouter.Resolve(stage, hasWork));

    [Fact]
    public void IdleEscapeDoesSomethingToNothing()
    {
        // C7 的另一半："Idle·Esc = 无事发生"。若这里顺手关一下画板或截图，
        // 用户按 Esc 关掉的就是自己正在用的那个应用的对话框之外的东西。
        Assert.Equal(EscapeStep.Nothing, EscapeRouter.Resolve(AnnotationStage.Idle, hasWorkInProgress: true));
        Assert.Equal(EscapeStep.Nothing, EscapeRouter.Resolve(AnnotationStage.Idle, hasWorkInProgress: false));
    }
}
