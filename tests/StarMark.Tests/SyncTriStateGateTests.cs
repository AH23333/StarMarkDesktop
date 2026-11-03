#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Data;
using StarMark.Core.Sync;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// P-38 (A) 的证人与闸门：单源那一轮的<b>三态</b>（成功／跳过／失败）。
/// <para>账本那颗缺陷的形状：源不可用时记 <c>Success = true</c>，与「拉了且成功、零新增」同形——
/// 主窗那颗绿卡替一颗从没参与的源说"全部完成"，而用户没有任何一路读数能看出「这一轮其实没跑它」。
/// 修法走 (A)：<see cref="SourceSyncResult.Success"/> 语义一字未动（跳过仍记 true——你从未启用的源
/// 不该每轮刷红），第三态由 <see cref="SourceSyncResult.Skipped"/> 那颗旗＋<see cref="SourceSyncResult.Status"/>
/// 那颗现算读数交给展示层：没失败、但没全参与＝灰卡逐源说清谁没参与，既不报喜也不报忧。</para>
/// <para>P-58 起同一张卡多了<b>第二份名单</b>：被拉取预算掐断的源（<see cref="SourceSyncResult.Truncated"/>）
/// 照旧 Success＝true，但「没拉全」有自己的旗、自己的名单（<see cref="SyncSummary.TruncatedLines"/>）、
/// 自己那句汇总话——⛔ 再顶着「同步完成」。行为④⑤与形状⑤是它的证人与闸。</para>
/// <para>前五格是行为证人（真协调器＋临时库＋假源，口径同 <see cref="SyncCoordinatorTests"/>）；
/// 后五格钉形状（测试工程引用不到 UI，只能按源码形状钉——坑表 #1020）。断言全部落在键与实参的
/// <b>身份</b>上，⛔ Render 后对句子——换语言重念那一片是 <see cref="SyncReasonWordGateTests"/> ③④
/// 的射程（与本档共用的正是同一条手臂），这里不重复它，也因此⛔ 挂 <c>LocaleGlobal</c> 那颗收集。</para>
/// </summary>
public sealed class SyncTriStateGateTests : IDisposable
{
    private const string Coord = "src/StarMark.Core/Sync/SyncCoordinator.cs";
    private const string Status = "src/StarMark.UI/MainWindow.Status.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public SyncTriStateGateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_tri_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }

    private sealed class FakeSource : IItemSource
    {
        public required string SourceId { get; init; }
        public required string DisplayName { get; init; }
        public bool Available { get; init; } = true;
        public IReadOnlyList<Item>? Result { get; init; }
        public Exception? Throw { get; init; }
        public bool Truncated { get; init; }   // P-58：假信封上那颗「没拉全」旗，由证④⑤举起

        public bool IsAvailable => Available;
        public Task<FetchResult> FetchAsync(SyncContext ctx, CancellationToken ct)
        {
            if (Throw != null) throw Throw;
            return Task.FromResult(new FetchResult(Result ?? Array.Empty<Item>(), Truncated));
        }
        public Task CommitCheckpointAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Item>>(Result ?? Array.Empty<Item>());
    }

    private SyncCoordinator Coordinator(params FakeSource[] sources)
        => new(new ItemRepository(_factory), sources);

    /// <summary>行为①：不可用那一支——Success 维持 true（(A) 刻意没动的那颗语义），但「没参与」
    /// 从此有自己的旗、自己的读数、自己那几行；它⛔ 混进失败名单，也⛔ 再与「拉了且成功」同形。</summary>
    [Fact]
    public async Task TheUnavailableSourceStaysSuccessfulButIsMarkedSkipped()
    {
        var summary = await Coordinator(new FakeSource { SourceId = "off", DisplayName = "Off", Available = false })
            .SyncAllAsync(CancellationToken.None);

        Assert.Equal(0, summary.FailedCount);
        Assert.Equal(1, summary.SkippedCount);
        var row = Assert.Single(summary.Sources);
        Assert.True(row.Success);                            // 没参与的源⛔ 刷红——(A) 与 (B) 的分岔点
        Assert.True(row.Skipped);
        Assert.Equal(SourceSyncStatus.Skipped, row.Status);
        Assert.Null(summary.FailureLines());                 // 跳过⛔ 冒充失败

        var lines = summary.SkippedLines();
        Assert.NotNull(lines);                               // 也⛔ 静默：谁没参与、为什么，逐源一行
        Assert.True(lines is { Key: "sync.src.line" });      // 一颗跳过源＝一行
        Assert.True(lines.Args?[1] is LocMessage { Key: "sync.src.unavailable" });   // 「为什么」复用不可用那颗键
    }

    /// <summary>行为②：健康与出错的两支各自站对位置——三态把三件事分开，任何一颗都⛔ 顺带挤歪另一颗。</summary>
    [Fact]
    public async Task HealthyStaysSucceededAndFailingStaysFailed()
    {
        var summary = await Coordinator(
            new FakeSource { SourceId = "good", DisplayName = "Good", Result = new[] { ItemFile("good") } },
            new FakeSource { SourceId = "bad", DisplayName = "Bad", Throw = new InvalidOperationException("boom") })
            .SyncAllAsync(CancellationToken.None);

        Assert.Equal(0, summary.SkippedCount);
        Assert.Null(summary.SkippedLines());                 // 没有跳过源＝没有那半张卡
        Assert.Equal(1, summary.FailedCount);

        var good = summary.Sources.Single(s => s.SourceId == "good");
        Assert.True(good.Success);
        Assert.False(good.Skipped);
        Assert.Equal(SourceSyncStatus.Succeeded, good.Status);

        var bad = summary.Sources.Single(s => s.SourceId == "bad");
        Assert.False(bad.Success);
        Assert.False(bad.Skipped);
        Assert.Equal(SourceSyncStatus.Failed, bad.Status);
        Assert.NotNull(summary.FailureLines());
    }

    /// <summary>行为③：三态同场——off＋good＋bad 一轮里，三份读数各数各的，两份名单互不串门。</summary>
    [Fact]
    public async Task TheMixedRunKeepsTheThreeReadingsApart()
    {
        var summary = await Coordinator(
            new FakeSource { SourceId = "off", DisplayName = "Off", Available = false },
            new FakeSource { SourceId = "good", DisplayName = "Good", Result = new[] { ItemFile("good") } },
            new FakeSource { SourceId = "bad", DisplayName = "Bad", Throw = new InvalidOperationException("boom") })
            .SyncAllAsync(CancellationToken.None);

        Assert.Equal(1, summary.TotalPulled);
        Assert.Equal(1, summary.SkippedCount);
        Assert.Equal(1, summary.FailedCount);

        // 两份名单各挑各的行：跳过名单里只有 off，失败名单里只有 bad——串门＝灰卡替坏源道歉／红卡替闲源讨功。
        Assert.Equal(1, summary.SkippedLines()!.Render().Split('\n').Length);
        Assert.Equal(1, summary.FailureLines()!.Render().Split('\n').Length);
        Assert.Contains("boom", summary.FailureLines()!.Render(), StringComparison.Ordinal);
        Assert.DoesNotContain("boom", summary.SkippedLines()!.Render(), StringComparison.Ordinal);
    }

    /// <summary>行为④（P-58）：被预算掐断的那一轮——拉到的那批照常落库（Success 照旧、Status 照旧
    /// Succeeded），但「没拉全」从此有自己的旗、自己的名单、自己的那句汇总话，⛔ 再顶着「同步完成」。</summary>
    [Fact]
    public async Task TheTruncatedSourceStaysSucceededButIsCalledOut()
    {
        var summary = await Coordinator(
            new FakeSource { SourceId = "capped", DisplayName = "Capped",
                Result = new[] { ItemFile("capped") }, Truncated = true })
            .SyncAllAsync(CancellationToken.None);

        Assert.Equal(0, summary.FailedCount);
        Assert.Equal(1, summary.TruncatedCount);
        var row = Assert.Single(summary.Sources);
        Assert.True(row.Success);                            // 拉到的那批是真的——「没拉全」⛔ 冒充失败
        Assert.False(row.Skipped);
        Assert.Equal(SourceSyncStatus.Succeeded, row.Status);
        Assert.Null(summary.FailureLines());                 // 没失败却有失败名单＝替好结果道歉
        Assert.Null(summary.SkippedLines());                 // 没跳过却有跳过名单＝多报一颗没参与的源

        var lines = summary.TruncatedLines();
        Assert.NotNull(lines);                               // 也⛔ 静默：谁没拉全，逐源一行
        Assert.True(lines is { Key: "sync.src.line" });      // 一颗被掐的源＝一行，行形与另两份名单同源
        Assert.True(lines.Args?[1] is LocMessage { Key: "sync.src.truncated" });   // 「为什么」念表里那颗键
        Assert.True(summary.Message() is { Key: "top.syncDoneTruncated" });        // 汇总话说得清「有源没拉全」
    }

    /// <summary>行为⑤（P-58）：没被掐断的一轮照旧——新名单是加法，⛔ 改写旧读数：
    /// 名单空、汇总话照旧那句「同步完成」。</summary>
    [Fact]
    public async Task ThePlainRunKeepsSayingPlainDone()
    {
        var summary = await Coordinator(
            new FakeSource { SourceId = "good", DisplayName = "Good", Result = new[] { ItemFile("good") } })
            .SyncAllAsync(CancellationToken.None);

        Assert.Equal(0, summary.TruncatedCount);
        Assert.Null(summary.TruncatedLines());
        Assert.True(summary.Message() is { Key: "top.syncDone" });
    }

    /// <summary>形状①：三态读数是<b>现算</b>的（<see cref="SourceSyncResult.Status"/>），⛔ 存档——
    /// 存了就有两份真值，一颗说跳过、一颗说成功。三条臂由两颗布尔唯一决定。</summary>
    [Fact]
    public void TheStatusIsDerivedFromTheTwoFlagsNeverStored()
    {
        Assert.Equal(SourceSyncStatus.Succeeded,
            new SourceSyncResult { SourceId = "a", Success = true, PulledCount = 3 }.Status);
        Assert.Equal(SourceSyncStatus.Skipped,
            new SourceSyncResult { SourceId = "a", Success = true, Skipped = true }.Status);
        Assert.Equal(SourceSyncStatus.Failed,
            new SourceSyncResult { SourceId = "a", Success = false }.Status);

        var code = Code(ReadRepoFile(Coord));
        Assert.Equal(1, Count(code, "public SourceSyncStatus Status =>"));   // 现算那颗读数恰一处
        Assert.Equal(0, Count(code, "Status = "));                            // 初始化器里写 Status＝把它当存档
    }

    /// <summary>形状②：协调器里那颗旗只在不可用那一支举起——正常拉取、超时、GitHub、兜底四条 catch
    /// 都⛔ 顺手举它（举了就是「失败也说成跳过」，比 (B) 更安静地撒谎）。</summary>
    [Fact]
    public void TheSkipFlagIsRaisedOnlyByTheUnavailableBranch()
    {
        var code = Code(ReadRepoFile(Coord));
        Assert.Equal(1, Count(code, "Skipped = true,"));
        Assert.Equal(1, Count(code, "public bool Skipped { get; init; }"));
        Assert.Equal(1, Count(code, "public int SkippedCount => Sources.Count(s => s.Skipped)"));
        // 逐源挑行的手臂只有一条：三份名单共用它，键与接合子各只有一处主人（坑表 #161）。
        Assert.Equal(1, Count(code, "private LocMessage? LinesWhere("));
        Assert.Equal(3, Count(code, "=> LinesWhere("));
        Assert.Equal(1, Count(code, "public LocMessage? SkippedLines("));
    }

    /// <summary>形状②′（P-58）：那颗「没拉全」旗同样只在信封那一臂举起——四条 catch 与不可用那一支
    /// 都⛔ 顺手举它（「没拉全」是拉到之后的事实，失败/跳过的那一轮谈不上「拉全没有」）。</summary>
    [Fact]
    public void TheTruncatedFlagIsRaisedOnlyByTheEnvelope()
    {
        var code = Code(ReadRepoFile(Coord));
        Assert.Equal(1, Count(code, "public bool Truncated { get; init; }"));
        Assert.Equal(1, Count(code, "public int TruncatedCount => Sources.Count(s => s.Truncated)"));
        Assert.Equal(1, Count(code, "public LocMessage? TruncatedLines("));
        Assert.Equal(1, Count(code, "Truncated = fetch.Truncated,"));   // 唯一举起的那一臂：信封带来的那颗
    }

    /// <summary>形状③（P-58 起含第二份名单）：主窗那张卡的判据与正文——绿卡只在「两份名单都空」
    /// 那一格出现；行分隔那颗布局留在 Core 的默认实参里（规则 #100，口径同失败名单那半张）。</summary>
    [Fact]
    public void TheGreenCardIsWiredOnlyWhenBothRostersAreEmpty()
    {
        var code = Code(ReadRepoFile(Status));
        Assert.Equal(1, Count(code, "var skipped = summary.SkippedLines();"));
        Assert.Equal(1, Count(code, "var truncated = summary.TruncatedLines();"));
        Assert.Equal(1, Count(code, "skipped is null && truncated is null"));   // 绿卡的判据＝两份名单
        Assert.Equal(1, Count(code, "summary.SkippedOrTruncatedLines()"));      // 合订正文住 Core，主窗只递话
        Assert.Equal(0, Count(code, "sync.src.lineJoin"));   // 布局键只许 Core 一处取词（#161）——主窗再取就是第二主人
        Assert.Equal(0, Count(code, "summary.SkippedLines(\""));   // gap 是布局：一处默认实参就够，⛔ 进表
    }

    /// <summary>形状④：那张灰卡的标题三语互异、零槽——它不带读数（几颗没参与写在正文那几行里）。</summary>
    [Fact]
    public void TheSkippedTitleLivesInTheTableWithDistinctColumnsAndNoSlots()
    {
        Assert.Equal(1, Count(ReadRepoFile(Table), "new Entry(\"top.syncSkippedTitle\""));
        Assert.True(UiStrings.TryGet("top.syncSkippedTitle", out var row));
        Assert.True(row.Zh != row.En && row.En != row.Ja && row.Zh != row.Ja);
        Assert.Equal(0, RowSlots("top.syncSkippedTitle"));
    }

    /// <summary>形状⑤（P-58）：「没拉全」那张卡的标题三语互异、零槽——口径对齐④那颗（不带读数）。</summary>
    [Fact]
    public void TheTruncatedTitleLivesInTheTableWithDistinctColumnsAndNoSlots()
    {
        Assert.Equal(1, Count(ReadRepoFile(Table), "new Entry(\"top.syncTruncatedTitle\""));
        Assert.True(UiStrings.TryGet("top.syncTruncatedTitle", out var row));
        Assert.True(row.Zh != row.En && row.En != row.Ja && row.Zh != row.Ja);
        Assert.Equal(0, RowSlots("top.syncTruncatedTitle"));
    }

    private static Item ItemFile(string source)
        => new() { Type = ItemType.File, Source = source, SourceId = "a", Title = "T" };
}
