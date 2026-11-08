#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 片248（坑 #1172）护栏：**播放器退出的那一拍是生命周期瞬态，不是读取故障**。
/// <para>现场：Music 组件轮询 SMTC，播放器关闭/切换瞬间会话对象还在手上、底层已没了，
/// <c>TryGetMediaPropertiesAsync</c> 抛 <c>COMException 0x80070015</c>（ERROR_NOT_READY）；
/// 会话拆除的 SMTC 事件风暴又置脏位立刻补跑一轮——每关一次播放器就**两连发 ERROR＋整段堆栈**
/// （实测全天 7 条，双发间隔 4~34ms）。</para>
/// <para>刀法：catch 里对 0x80070015 专项降级——节流 INFO（首条留痕、窗口内重试合并计数，
/// 节流契约由 <see cref="StarLogThrottleTests"/> 另测），快照保持上一次的；
/// 其余异常照旧 ERROR＋堆栈（那才是故障）。脏位补跑机制（双发的来源）<b>不许顺手拆</b>——
/// 它是「切歌不刷新」那课（丢弃事件导致旧曲目）的修复，砍日志只砍日志级别。</para>
/// </summary>
public sealed class MediaSessionNotReadyGateTests
{
    private const string MediaCs = "src/StarMark.UI/Services/MediaSessionService.cs";

    private static string SnapshotBody() => SourceGate.MethodBody(
        SourceGate.ReadRepoFile(MediaCs), "private async Task ReadSnapshotAsync()");

    /// <summary>护栏：0x80070015（ERROR_NOT_READY）必须按 HRESULT 精确匹配专项降级——不许把所有 COMException 一律吞掉。</summary>
    [Fact]
    public void NotReadyComException_IsMatchedExactly_ByHresult()
    {
        Assert.Contains("ex is COMException && ex.HResult == unchecked((int)0x80070015)", SnapshotBody());
    }

    /// <summary>护栏：瞬态降级走节流 INFO，key 必须是有界字面量（节流字典禁拿运行期值当 key）。</summary>
    [Fact]
    public void NotReadyIsDowngraded_ToThrottledInfo_WithBoundedKey()
    {
        Assert.Contains("StarLog.InfoThrottled(\"media.session.notready\"", SnapshotBody());
    }

    /// <summary>护栏：真正的读取故障照旧 ERROR＋异常——本刀只降级瞬态，不许把整个 catch 吞成静默。</summary>
    [Fact]
    public void RealReadErrors_KeepTheFullErrorLog()
    {
        Assert.Contains("StarLog.Error(\"读取媒体会话失败\", ex)", SnapshotBody());
    }

    /// <summary>护栏：瞬态分支必须在 ERROR 之前 return——0x80070015 不许再走到 ERROR 那行。</summary>
    [Fact]
    public void TransientBranch_ReturnsBefore_ReachingTheErrorLog()
    {
        var body = SnapshotBody();
        var transient = body.IndexOf("ex is COMException && ex.HResult == unchecked((int)0x80070015)", StringComparison.Ordinal);
        var error = body.IndexOf("StarLog.Error(\"读取媒体会话失败\"", StringComparison.Ordinal);
        Assert.True(transient >= 0 && error > transient, "瞬态判定与 ERROR 日志都必须在 catch 里");
        var branchReturn = body.IndexOf("return;", transient, StringComparison.Ordinal);
        Assert.True(branchReturn >= 0 && branchReturn < error, "瞬态分支命中后先 return，不许落到 ERROR");
    }

    /// <summary>
    /// 护栏：双发的来源——<c>RefreshCoreAsync</c> 的脏位补跑（do/while）必须原样保留：
    /// 它修的是「闸门期间事件被丢弃、切了歌组件还显示旧曲目」。砍的只是日志级别，不是补跑本身。
    /// </summary>
    [Fact]
    public void PendingRequeue_ThatCausedTheDoubleFire_StaysIntact()
    {
        var body = SourceGate.MethodBody(
            SourceGate.ReadRepoFile(MediaCs), "private async Task RefreshCoreAsync()");
        Assert.Contains("_refreshPending = true", body);
        Assert.Contains("while (_refreshPending && IsAvailable && !_disposed)", body);
    }
}
