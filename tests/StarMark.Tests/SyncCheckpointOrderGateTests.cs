#nullable enable
using System;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 同步检查点的<b>提交次序</b>闸门（P-19，批次 SN）。
/// <para>
/// 行为测（<see cref="SyncCoordinatorTests"/> 那三条 + <c>GitHubCancelSafetyTests</c> 那四条）量的是真库里的先后；
/// 这里量的是<b>形状</b>：谁把"写检查点"这一步搬回拉取里、谁把协调器里那句提交挪到 upsert 之前、
/// 谁在第三个文件里另开一处写检查点，都会在崩一次之后表现为"永久少一批数据"，而且现场什么都不留
/// ——所以它必须被文本钉住。
/// </para>
/// </summary>
public sealed class SyncCheckpointOrderGateTests
{
    private const string Source = "src/StarMark.Integrations/GitHub/GitHubSource.cs";
    private const string Coordinator = "src/StarMark.Core/Sync/SyncCoordinator.cs";
    private const string Contract = "src/StarMark.Abstractions/IItemSource.cs";
    private const string SrcRoot = "src";

    [Fact]
    public void FetchMustNotWriteCheckpoints_CommitMust()
    {
        var file = SourceGate.ReadRepoFile(Source);
        var fetch = SourceGate.Code(SourceGate.MethodBody(file, "public async Task<IReadOnlyList<Item>> FetchAsync"));
        var commit = SourceGate.Code(SourceGate.MethodBody(file, "public async Task CommitCheckpointAsync"));

        // 拉取里一次都不许写 sync_state：写了就是"载荷还没进库，检查点先进了"。
        Assert.DoesNotContain("SetSyncStateAsync", fetch, StringComparison.Ordinal);
        Assert.Contains("_pendingCheckpoint = null", fetch, StringComparison.Ordinal);   // 上一轮没提交成的不带进这一轮
        Assert.Equal(2, SourceGate.Count(commit, "SetSyncStateAsync"));                  // etag + last_synced_at，两条都在提交里
        Assert.Contains("if (pending is null) return;", commit, StringComparison.Ordinal); // 没跑完的一轮什么都不写
        // 写进库的必须是**这一轮暂存的那一只** ETag。台架 SN8 量出来的射程边界：把这里改成读客户端当前值
        // （`_client.CachedETag`）时，上面四条全部照旧绿，而"提交的内容来自哪一轮"这件事就没人守了——
        // 那正是 P-19 的另一半（检查点必须与载荷同轮）。协调器今天是串行的（拉完立刻提交），那条改法
        // 还没可达路径，所以只能由文本钉住，等它变成可达时不至于静悄悄。
        Assert.Contains("pending.Value.Etag", commit, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorCommitsTheCheckpointAfterTheUpsert()
    {
        var body = SourceGate.Code(SourceGate.MethodBody(
            SourceGate.ReadRepoFile(Coordinator), "public async Task<SyncSummary> SyncAllAsync"));

        Assert.Contains("await source.FetchAsync(new SyncContext(), ct)", body, StringComparison.Ordinal);
        Assert.Contains("await _repository.UpsertAsync(syncable, ct)", body, StringComparison.Ordinal);
        Assert.Contains("await source.CommitCheckpointAsync(ct)", body, StringComparison.Ordinal);
        Assert.True(body.IndexOf("UpsertAsync(syncable, ct)", StringComparison.Ordinal)
                    < body.IndexOf("CommitCheckpointAsync(ct)", StringComparison.Ordinal),
            "提交检查点又跑到落库前面了＝崩溃窗口回来了");
    }

    [Fact]
    public void TheContractStepIsOptInSoOtherSourcesNeedNoChange()
    {
        // 这道门钉的是本批选定的**改法**：加一个带默认实现的成员，而不是改 FetchAsync 的签名。
        // 后者会牵动每一条源与每个测试桩（P-28 那格当年的顾虑就在这里），而默认实现让它成为可选项。
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Contract));

        Assert.Contains("Task CommitCheckpointAsync(CancellationToken ct) => Task.CompletedTask;", code, StringComparison.Ordinal);
        Assert.Equal(1, SourceGate.Count(code, "Task CommitCheckpointAsync("));   // 一处声明（默认实现），不是又开一个重载
    }

    [Fact]
    public void TheCheckpointKeysHaveExactlyOneWriterInSrc()
    {
        // 上一条只盯 GitHubSource 自己的两个方法体，管不到<b>旁路</b>：谁在别处再写一次这两个键
        // （诊断页顺手回写、另一个源自己提前落检查点……），P-19 的崩溃窗口就从那条缝重新打开，
        // 而 GitHubSource 的形状依然完全正确。所以整棵 src 只许有一个写点。
        var files = SourceGate.ReadRepoUnder(SrcRoot);
        Assert.True(files.Count >= 200, $"只扫到 {files.Count} 个源文件＝普查路径错了，这条闸门在空转");

        var writers = files
            .Select(f => (
                Path: f.RelativePath,
                EtagWrites: SourceGate.Count(SourceGate.Code(f.Text), "SetSyncStateAsync(\"github:etag\""),
                TimeWrites: SourceGate.Count(SourceGate.Code(f.Text), "SetSyncStateAsync(\"github:last_synced_at\"")))
            .ToList();

        var writeSites = writers.Where(w => w.EtagWrites + w.TimeWrites > 0).ToList();
        Assert.Equal(new[] { "src/StarMark.Integrations/GitHub/GitHubSource.cs" }, writeSites.Select(w => w.Path));
        Assert.Equal(1, writeSites.Sum(w => w.EtagWrites));   // 各只有一处，位置由 FetchMustNotWriteCheckpoints_CommitMust 钉在提交里
        Assert.Equal(1, writeSites.Sum(w => w.TimeWrites));

        // 读侧刻意**不**钉数（拉取前取上次 ETag、诊断页展示，两个都是正当读者，将来多一个读数不是违规）。
        // 台架 SN10 也顺手量了这条闸门的射程：旁路改用常量别名写同一个键时，按字面扫的普查看不见，
        // 红的是诊断页那两条读数行为测。不追别名（追就得钉住每个常量），只把边界记在这儿。
    }
}
