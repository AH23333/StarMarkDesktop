#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 点亮路径那两把新刻度（批次 SZ；P-134 选项 (A)）。
/// <para>
/// UI 层出不了契约测（#184：测试工程不引用 <c>StarMark.UI</c>），所以这里守的是接线形状。
/// 值得守的点很具体：<b>这两把刻度的全部意义就是"恢复返回之后那 1.5 秒不再是空白"</b>——
/// 一旦它们被挂进创建循环（改动点亮顺序，就把 P-133 要量的东西同时改掉了）、
/// 被合并成一条（静止不出帧时两条一起消失＝看着有度量其实一条都没有，#160）、
/// 或者第一帧那个订阅忘了退（每帧写两行日志＝把量具变成噪声源，正是本条拒掉选项 (B) 的理由），
/// 刻度还在，读数却再也读不出东西。
/// </para>
/// </summary>
public sealed class WidgetLightupCheckpointGateTests
{
    private const string Main = "src/StarMark.UI/MainWindow.xaml.cs";

    private static string Code(string file) => SourceGate.Code(SourceGate.ReadRepoFile(file));

    private const string HandbackLabel = "组件点亮后 UI 让出（Low 档）";
    private const string FrameLabel = "组件首个组合帧提交";

    /// <summary>两把刻度各自恰好一条：少一把就退回"空白还在"，多一把就是有人把它挪进了循环里逐颗打。</summary>
    [Fact]
    public void EachNewCheckpointHasExactlyOneSite()
    {
        Assert.Equal(1, SourceGate.Count(Code(Main), HandbackLabel));
        Assert.Equal(1, SourceGate.Count(Code(Main), FrameLabel));
    }

    /// <summary>
    /// 两把都必须挂在"组件恢复任务返回"那把<b>之后</b>（同一段续体里）。
    /// 挂在创建循环之前＝量的不是那 1.5 秒；插进循环＝同时改掉 P-133 要量的顺序（两批不许混）。
    /// </summary>
    [Fact]
    public void BothHangAfterTheRestoreReturnedCheckpoint()
    {
        var afterRestore = SourceGate.Between(Code(Main),
            "StartupProfile.Mark(\"组件恢复任务返回（UI 队列）\")",
            "STARMARK_START_QUERY");

        Assert.Contains("MarkUiHandback()", afterRestore, StringComparison.Ordinal);
        Assert.Contains("MarkFirstComposedFrame()", afterRestore, StringComparison.Ordinal);
    }

    /// <summary>
    /// 反向钉：<b>逐窗的创建/点亮代码里不许出现这两把刻度</b>。
    /// 它们一旦进到 <c>WidgetManager</c>／<c>WidgetWindow</c>，量的就不再是"恢复之后"，
    /// 而是每颗一次——12 颗就是 24 行成对日志，把被量的那段淹没。
    /// </summary>
    [Fact]
    public void ThePerWindowLightupPathStaysFreeOfThem()
    {
        foreach (var file in new[] { "src/StarMark.UI/Services/WidgetManager.Lifecycle.cs",
                                     "src/StarMark.UI/Views/WidgetWindow.xaml.cs" })
        {
            var code = Code(file);
            Assert.DoesNotContain(HandbackLabel, code, StringComparison.Ordinal);
            Assert.DoesNotContain(FrameLabel, code, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 第一帧那把必须<b>自己退订</b>，而且退订要打在刻度之前——
    /// 留着就是每帧两条日志（[启动] + [内存] 成对发，见 <c>StartupProfile.Mark</c>）。
    /// </summary>
    [Fact]
    public void TheFrameCheckpointUnsubscribesBeforeItMarks()
    {
        var body = SourceGate.MethodBody(Code(Main), "void MarkFirstComposedFrame");

        Assert.Contains("CompositionTarget.Rendering +=", body, StringComparison.Ordinal);
        int off = body.IndexOf("CompositionTarget.Rendering -=", StringComparison.Ordinal);
        int mark = body.IndexOf(FrameLabel, StringComparison.Ordinal);
        Assert.True(off >= 0, "回调里必须退订（一次性）");
        Assert.True(mark >= 0 && off < mark, "必须先退订再打刻度");
    }

    /// <summary>
    /// 合成器订阅全仓只许一进一出（多一次进＝多一条每帧日志；只进不出＝永久挂着）。
    /// </summary>
    [Fact]
    public void TheRenderingSubscriptionIsArmedOnceAndNoMore()
    {
        var code = Code(Main);
        Assert.Equal(1, SourceGate.Count(code, "CompositionTarget.Rendering +="));
        Assert.Equal(1, SourceGate.Count(code, "CompositionTarget.Rendering -="));
    }

    /// <summary>
    /// UI 让出那把只能排<b>一次</b>、且用 <c>Low</c> 档：
    /// 本仓的 <c>DispatcherQueuePriority</c> 只有 Low/Normal/High，<b>没有 Idle</b>——
    /// 谁把它改成"每帧重投"或"Normal 档插队"，这两把刻度报的就不是同一件事了。
    /// </summary>
    [Fact]
    public void TheHandbackCheckpointIsEnqueuedOnceAtLowPriority()
    {
        var body = SourceGate.MethodBody(Code(Main), "void MarkUiHandback");

        Assert.Contains("DispatcherQueuePriority.Low", body, StringComparison.Ordinal);
        Assert.Equal(1, SourceGate.Count(body, "TryEnqueue"));
        Assert.DoesNotContain("DispatcherQueuePriority.Idle", Code(Main), StringComparison.Ordinal);
    }

    /// <summary>
    /// 两把必须是<b>两条独立的 Mark</b>（各有各的标签与调用点）：合并成一条，
    /// 静止界面不出帧时就会两条一起没有——那正是 #160 说的"看起来有度量比没有更误导人"。
    /// </summary>
    [Fact]
    public void TheTwoCheckpointsAreNotCollapsedIntoOne()
    {
        Assert.Contains(HandbackLabel, SourceGate.MethodBody(Code(Main), "void MarkUiHandback"), StringComparison.Ordinal);
        Assert.Contains(FrameLabel, SourceGate.MethodBody(Code(Main), "void MarkFirstComposedFrame"), StringComparison.Ordinal);
    }
}
