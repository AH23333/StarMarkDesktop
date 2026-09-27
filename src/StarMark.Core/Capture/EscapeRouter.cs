#nullable enable
namespace StarMark.Core.Capture;

/// <summary>
/// 会话层的 <b>Esc 该收哪一层</b>（方案 §5 的 C7）。
/// <para>
/// 当前是"Esc 只管截图窗，画布玻璃没人收"——那块玻璃是 NOACTIVATE 窗，根本收不到键盘，
/// 于是画布只剩工具条按钮一个出口（批次 WD 记过的那条"退出必须≥3 条路"）。统一之后
/// Esc 的路由是 Hub 输入路由的一部分，而路由表要能在一张表上读完。
/// </para>
/// </summary>
public enum EscapeStep
{
    /// <summary>无事发生（Idle·Esc＝本来就没有会话在场，不能"顺手关点别的"）。</summary>
    Nothing,

    /// <summary>先收手上那半件事（勾到一半的折线／还拿着的笔），会话继续开着。</summary>
    CloseWorkInProgress,

    /// <summary>退出画板回 Idle（笔迹随之丢弃，要留得先存图／贴出）。</summary>
    ExitBoard,

    /// <summary>取消整场截图：会话回"冻结前的子态快照"（由 <see cref="AnnotationSessions.Move"/> 给落点）。</summary>
    CancelSheet,
}

/// <summary>
/// Esc 的分级判定。<b>只回答全局这一层</b>：贴图自己的 Esc（先丢选中、再关这一张）与截图子态内部
/// "编辑文字 → 收折线 → 收笔"那三级的优先次序都留在各自那条链上（方案 §6.2 沿用截图已调好的本地键表），
/// 这里只定"这些子层都空了之后，Esc 该动到哪一件大事"。
/// </summary>
public static class EscapeRouter
{
    /// <param name="stage">当前会话态。</param>
    /// <param name="hasWorkInProgress">手上是否有半件事：折线未收口、笔还armed、文字还在输入。
    /// <b>这一臂排在最前</b>：勾到一半想停下时若把整块板子一起关掉，那条折线也没画成，
    /// 症状是"按 Esc 之后我的笔迹全没了"。</param>
    public static EscapeStep Resolve(AnnotationStage stage, bool hasWorkInProgress)
    {
        if (hasWorkInProgress && (stage.IsBoard() || stage.IsSheet())) return EscapeStep.CloseWorkInProgress;
        return stage switch
        {
            AnnotationStage.Sheet => EscapeStep.CancelSheet,
            AnnotationStage.BoardPenetrating => EscapeStep.ExitBoard,
            AnnotationStage.BoardDrawing => EscapeStep.ExitBoard,
            _ => EscapeStep.Nothing,
        };
    }
}
