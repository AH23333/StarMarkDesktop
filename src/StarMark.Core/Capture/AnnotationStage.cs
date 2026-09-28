#nullable enable
using StarMark.Core.Canvas;

namespace StarMark.Core.Capture;

/// <summary>
/// 屏幕标注系统唯一的会话态（架构方案 §5）。<b>整台机器上"此刻谁在吃鼠标"只有这一个答案</b>，
/// 它由 <c>AnnotationHub</c> 持有；画布玻璃、截图遮罩、贴图、工具条都不再各自记一份模式布尔位。
/// <para>
/// 为什么要收成一份：当前"穿透/绘制"在 <c>CanvasService</c>、"在不在截图"在 <c>ScreenshotService</c>、
/// "有没有框选"在遮罩窗自己，三处各说各话，症状就是重影、串键、Z 序拉扯与"写着绘制中点下去却画不上"
/// （方案 §1 的 R1/R3）。分岔的根源不是漏了某条互斥检查，而是<b>同一件事有三个书写点</b>。
/// </para>
/// <para>
/// <b>贴图（Pinned）不在这里</b>：它是独立实例的宿主，不占用全局会话——"画板开着 + 若干张贴图"与
/// "截图进行中 + 贴图在场"都是合法组合（方案 §5 末条），贴图只通过 <see cref="SurfaceRole"/> 参与 Z 序排法。
/// </para>
/// </summary>
public enum AnnotationStage
{
    /// <summary>没有任何标注界面在场。</summary>
    Idle,

    /// <summary>画板在场，但把鼠标让给下层应用（边讲边翻 PPT 那一态，也是画布的默认态）。</summary>
    BoardPenetrating,

    /// <summary>画板在场并吃掉屏上的每一按——只有这一态能留持久笔迹。</summary>
    BoardDrawing,

    /// <summary>截图会话在场：屏幕冻住，框选 / 就地标注 / 编辑文字 / 勾折线。</summary>
    Sheet,
}

/// <summary>
/// 促使会话挪动的那<b>一件事</b>（方案 §3.1：任何公开动作都表达为事件，Hub 统一做转移判定）。
/// <para>工具条按钮、全局热键、托盘菜单、截图收尾都折成这几个事件之一。写成事件而不是"直接调状态 setter"
/// 是为了让"谁改得动会话"可机检：接线层只能 <c>Raise(事件)</c>，不许出现第四处改 <c>_clickThrough</c> 的路。</para>
/// </summary>
public enum SessionEvent
{
    /// <summary>开 / 关画板（canvas.toggle、托盘那一项）。</summary>
    ToggleBoard,

    // 2026-09-27 用户改判：原先这里有 PickPersistentPen／PickSpotlightPen——"选一支笔"顺手把穿透态翻了。
    // 他要的是<b>换工具与能不能画解耦</b>：穿透态就是穿透态，要画一律先用那颗按钮或 canvas.through 关掉。
    // 保留那两个事件等于让"点一下荧光笔"偷偷改掉鼠标归属（也正是"点工具条那一下被画布吃掉"的源头之一），
    // 所以事件本身删掉：换工具不再产生迁移，见 CanvasService.SelectTool。

    /// <summary>把鼠标交还给下层应用（点「穿透」／画布上右键／F5 那类）。</summary>
    GivePointerBack,

    /// <summary>反过来：把鼠标收回给画布（那颗「交出/收回鼠标」按钮的另一个方向，或 F5 来回按）。</summary>
    TakePointer,

    /// <summary>冻帧进截图（F1 给条／F3 直贴／识字）。</summary>
    BeginSheet,

    /// <summary>截图会话结束（提交、贴出、存图、Esc 取消都算，一个会话只许回调一次）。</summary>
    EndSheet,
}

/// <summary>
/// 会话态的<b>转移函数与读法</b>（架构方案 §4、§5）。
/// <para>
/// 每条判据都是纯函数：玻璃该不该显、读态该不该跑、鼠标该不该吃——这些方向只在真机上看得出来，
/// 写成布尔表达式留在编排里就是"编译得过、过得了看着像在测它的闸门、真机却坏着"的老路子
/// （批次 WF-1 定下的固定口径：<b>方向判据下沉 Core ＋ 三臂单测，接线层只准调用它</b>）。
/// </para>
/// </summary>
public static class AnnotationSessions
{
    public static bool IsBoard(this AnnotationStage stage)
        => stage is AnnotationStage.BoardPenetrating or AnnotationStage.BoardDrawing;

    public static bool IsSheet(this AnnotationStage stage) => stage == AnnotationStage.Sheet;

    /// <summary>
    /// 转移。<paramref name="boardBeforeSheet"/> 是"进入截图前画板所在的子态"，画板本来没开就传 null——
    /// 离开截图时照着它恢复，这是"截完图回不来"那一类错误的结构性解法（方案 §5）。
    /// <para>不合法的组合一律<b>返回原态</b>而不是抛：Sheet 的收尾回调可能被路过两次（提交与关窗各一次），
    /// 抛出去等于把用户摁在一层吃满屏幕的顶层窗里；但返回值必须仍是"那一次的真结果"，
    /// 所以每个臂都单独测（重复 EndSheet 不许把 Board 吞成 Idle）。</para>
    /// </summary>
    public static AnnotationStage Move(AnnotationStage from, SessionEvent what, AnnotationStage? boardBeforeSheet)
        => what switch
        {
            // 画板开着（或 Sheet 里根本不谈画板）时按开关＝关掉；Sheet 期间这一按不改变全局态，
            // 由 Hub 给一句"截图进行中"的回执——哑键是最坏的收尾。
            SessionEvent.ToggleBoard => from switch
            {
                AnnotationStage.Idle => AnnotationStage.BoardPenetrating,
                AnnotationStage.Sheet => AnnotationStage.Sheet,
                _ => AnnotationStage.Idle,
            },
            SessionEvent.GivePointerBack => from.IsBoard()
                ? AnnotationStage.BoardPenetrating
                : from,
            // 反过来那半下：只在板子在场时才把鼠标收回给画布（Idle 里按它不该凭空开出一块板子）
            SessionEvent.TakePointer => from.IsBoard()
                ? AnnotationStage.BoardDrawing
                : from,
            SessionEvent.BeginSheet => from.IsSheet() ? from : AnnotationStage.Sheet,
            // 只有真在 Sheet 里才收得回来：路过第二次的收尾不能把 Board 子态改写成快照里的那个值
            SessionEvent.EndSheet => from.IsSheet()
                ? boardBeforeSheet ?? AnnotationStage.Idle
                : from,
            _ => from,
        };

    /// <summary>
    /// 画板玻璃此刻<b>该不该在屏幕上</b>。
    /// <para><b>Sheet 返回 false 是消重影的那一刀</b>：抓屏抓的是已经合成完的屏幕，遮罩窗显示的是那一瞬的
    /// 冻帧；玻璃若还亮着，用户就在"冻帧上的旧笔迹"之上又看见"活笔迹"——同一份东西的两份，
    /// 而谁盖住谁取决于两家谁最后被提过层。截图带不带笔迹只由<b>冻帧那一下收没收起玻璃</b>决定，
    /// 与会话进行中玻璃亮不亮无关。</para>
    /// </summary>
    public static bool GlassVisible(this AnnotationStage stage) => stage.IsBoard();

    /// <summary>
    /// 画板那条工具条该不该在。<b>Sheet 期间收起</b>：验收用例 1 要"截图期间看不到任何悬浮幽灵层"，
    /// 而截图自己带一条按钮栏，两条同时在场就是"哪一条管当前这件事"说不清（S3 合并成一条 Strip 后自然消失）。
    /// </summary>
    public static bool BoardStripVisible(this AnnotationStage stage) => stage.IsBoard();

    /// <summary>
    /// "按住即画"的读态该不该跑（旧代码叫 <c>PollPress</c>）。
    /// <para><b>只在 Board·穿透态</b>：那一态收不到鼠标消息，才需要自己去看按键状态。绘制态本来就收得到按下，
    /// 再叠一套读态就是同一按两家用（方案 §4 的 C2）；Sheet 态下这块玻璃连显示都不被允许，
    /// 读态再抢一次就把截图交互打断在别的程序手里。</para>
    /// </summary>
    public static bool QuickDrawReads(this AnnotationStage stage) => stage == AnnotationStage.BoardPenetrating;

    /// <summary>画板玻璃这一态吃不吃鼠标（穿透＝不吃，绘制＝吃）。Sheet 不谈这一位：那时玻璃不可见。</summary>
    public static bool GlassTakesPointer(this AnnotationStage stage) => stage == AnnotationStage.BoardDrawing;

    /// <summary>
    /// 会话要不要<b>拦住"退出键"之外的全局动作</b>——Sheet 期间画布那批快捷键都不该触发画布
    /// （方案 §6.1）。注册投影与回执文案在 <c>HotkeyGate</c>，这里只回答"该不该收权"。
    /// </summary>
    public static bool SuppressesCanvasHotkeys(this AnnotationStage stage) => stage.IsSheet();

    /// <summary>这一态要不要做每帧对账（Idle 什么都没有，省掉一次逐屏读样式位）。</summary>
    public static bool NeedsFrameAudit(this AnnotationStage stage) => stage.IsBoard();
}
