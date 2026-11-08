#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
using StarMark.Core.Sync;
using StarMark.Data;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// P-31 (A)+(C) 的证人与闸门：写事务一律 IMMEDIATE 占写锁；同步重入＝排队。
/// <para>
/// 账本那颗缺陷的形状：(A) 全部 18 处写事务走 provider 的 DEFERRED <c>BeginTransaction</c>——
/// 「先读后写」的事务在升级写锁那一刻撞上另一连接的活跃写事务，得到的是 BUSY_SNAPSHOT(517)：
/// <b>永不可重试成功</b>的错误（快照已定，等再久也升不上去，busy_timeout 对这条路径不构成上界）。
/// 修法＝裸 BEGIN IMMEDIATE（<see cref="ImmediateTxn"/>）：BEGIN 那一刻就占住写锁，
/// 真竞争退化为有意义的 BUSY(5)（对方一提交就让路，busy_timeout 排得住队）。
/// (C) 同步协调器没有重入闸：连点两次＝两轮并发读改写同一批行。修法＝排队——每个请求都被兑现。
/// </para>
/// <para>前四格是行为证人（真库真锁／真协调器＋可卡住的假源，口径同 <see cref="SyncTriStateGateTests"/>）；
/// 后两格钉形状（测试工程引用不到 UI 层，结构只能按源码钉——坑表 #1020）。本档不 Render 任何句子，
/// 故不挂 <c>LocaleGlobal</c> 那颗收集。</para>
/// </summary>
public sealed class SyncWriteTxnGateTests : IDisposable
{
    private const string Coord = "src/StarMark.Core/Sync/SyncCoordinator.cs";
    private const string ProbeKey = "p31_probe";

    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public SyncWriteTxnGateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_wtxn_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ===== 行为证人：(A) BEGIN IMMEDIATE 真占写锁 =====

    /// <summary>行为①：BEGIN 那一刻写锁就在手上（另一连接 busy_timeout=0 立刻 BUSY(5)），COMMIT 才放行。
    /// 先做对照（没开事务时同一连接随手写得进）——证明后面的 BUSY 是锁、不是连接串的巧合。</summary>
    [Fact]
    public void BeginImmediateHoldsTheWriteLockUntilCommit()
    {
        using (var plain = BareConnection())
            Exec(plain, $"INSERT INTO sync_state(key, value) VALUES('{ProbeKey}0', 'x')");
        Assert.Equal(1L, ProbeRows());

        using var conn = _factory.Open();
        using (var tx = conn.BeginImmediate())
        {
            // 事务内命令不设 Transaction（与既有 UpsertOne 同形状）：连接级事务之下照样入伍——提交后可见。
            Exec(conn, $"INSERT INTO sync_state(key, value) VALUES('{ProbeKey}1', 'x')");

            using var other = BareConnection();
            var ex = Assert.Throws<SqliteException>(() =>
                Exec(other, $"INSERT INTO sync_state(key, value) VALUES('{ProbeKey}2', 'x')"));
            Assert.Equal(5, ex.SqliteErrorCode);            // SQLITE_BUSY：写锁在 BEGIN 那一刻就被占住
            tx.Commit();
        }
        Assert.Equal(2L, ProbeRows());                      // 事务里那笔提交可见；对照行也还在

        using (var after = BareConnection())
            Exec(after, $"INSERT INTO sync_state(key, value) VALUES('{ProbeKey}3', 'x')");
        Assert.Equal(3L, ProbeRows());                      // COMMIT 放行写锁：第三笔立刻进得去
    }

    /// <summary>行为②：未提交就 Dispose＝整笔回滚。半截事务要是留在库里，调用方按「已失败」收场，
    /// 数据却写着半成品——比白写一次坏一个量级。</summary>
    [Fact]
    public void DisposingAnImmediateTxnWithoutCommitRollsItBack()
    {
        using var conn = _factory.Open();
        using (var tx = conn.BeginImmediate())
        {
            Exec(conn, $"INSERT INTO sync_state(key, value) VALUES('{ProbeKey}1', 'x')");
            // 不 Commit 就 Dispose：白写的那一笔要跟着事务一起消失
        }
        Assert.Equal(0L, ProbeRows());
    }

    // ===== 行为证人：(C) 重入＝排队 =====

    /// <summary>行为③：第二轮排队等第一轮收尾，然后自己跑——每个请求都被兑现（丢弃会让
    /// 「点了同步没反应」成为偶发且无从解释），全程最多一轮在跑。</summary>
    [Fact]
    public async Task ASecondRunQueuesBehindTheFirstAndBothAreServed()
    {
        var src = new GatedSource();
        var coord = new SyncCoordinator(new ItemRepository(_factory), new[] { src });

        var first = coord.SyncAllAsync(CancellationToken.None);
        await src.Entered.Task;                        // 第一轮已进 fetch：闸在它手里（确定性锚点）

        var second = coord.SyncAllAsync(CancellationToken.None);
        await Task.Delay(200);                         // 给「闸门失效」留出作案时间：失效的话第二轮早进 fetch 了
        Assert.Equal(1, src.FetchEntries);             // 排队：第二轮还没轮上
        Assert.False(second.IsCompleted);

        src.Unblock.TrySetResult();                    // 放行第一轮 → 闸移交 → 第二轮自己跑
        var s1 = await first;
        var s2 = await second;

        Assert.Equal(2, src.FetchEntries);             // 排队不是丢弃：两个请求各跑各的一轮
        Assert.Equal(1, src.MaxConcurrent);            // 全程最多一轮在跑
        Assert.Equal(0, s1.FailedCount);
        Assert.Equal(0, s2.FailedCount);
    }

    /// <summary>行为④：排队等闸的那一轮也吃取消——令牌一取消就撤队且从头到尾没跑；
    /// 闸也没被破坏：第一轮照常收尾、照常放闸。</summary>
    [Fact]
    public async Task ACancelledQueuedRunLeavesTheQueueAndNeverRuns()
    {
        var src = new GatedSource();
        var coord = new SyncCoordinator(new ItemRepository(_factory), new[] { src });

        var first = coord.SyncAllAsync(CancellationToken.None);
        await src.Entered.Task;

        using var cts = new CancellationTokenSource();
        var second = coord.SyncAllAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);   // 撤队：等闸也挂着令牌

        src.Unblock.TrySetResult();
        var s1 = await first;                          // 取消没有破坏闸：第一轮照常收尾
        Assert.Equal(0, s1.FailedCount);
        Assert.Equal(1, src.FetchEntries);             // 被取消的那轮从头到尾没跑
    }

    // ===== 形状闸门：接线钉在源码上 =====

    /// <summary>形状①：全 src 的写事务只有一种形态——18 处 <c>BeginImmediate</c>、裸 BEGIN 唯一出处、
    /// provider 事务类型与逐条挂 Transaction 的旧簿记全数退役。改漏一处＝同库两种锁语义并存，
    /// 而 BUSY_SNAPSHOT 恰恰会从漏掉的那一处回来。</summary>
    [Fact]
    public void EveryWriteTransactionIsImmediateAndNothingEnlistsPerCommand()
    {
        var deferred = 0; var immediate = 0; var literal = 0; var providerTxn = 0; var enlisted = 0;
        foreach (var (_, text) in ReadRepoUnder("src"))
        {
            var code = Code(text);
            deferred += Count(code, "BeginTransaction(");
            immediate += Count(code, "conn.BeginImmediate()");
            literal += Count(code, "\"BEGIN IMMEDIATE\"");
            providerTxn += Count(code, "SqliteTransaction");
            enlisted += Count(code, ".Transaction = ");
        }
        Assert.Equal(0, deferred);                     // DEFERRED 写事务全数退役
        // 片215·P-5B 起＝20：18 ＋ ReconcileStaleAsync 与 DeleteStaleAsync 各一处
        // （对账的置/清位与一键清都是写事务，同一把 IMMEDIATE 锁语义）；
        // 片240 起＝21：AI 整批撤销的差集留档（ItemRepository.Tags.cs）也是写事务——它落刀时没回写这颗
        // 计数（坑表 #1174），射域网第一次跑到这里才把红捞出来。
        Assert.Equal(21, immediate);                   // 21 处写事务全部走 IMMEDIATE
        Assert.Equal(1, literal);                      // 裸 BEGIN 只有一个出处
        Assert.Equal(0, providerTxn);                  // provider 事务类型不再出现
        Assert.Equal(0, enlisted);                     // 连接级事务：命令自动入伍，逐条挂 Transaction 的旧簿记全删
    }

    /// <summary>形状②：协调器的排队闸——闸只有一颗、进闸挂令牌、放闸在 finally（任何出口都不许卡死，
    /// 卡死等于从此再也同步不了）、同步主体整体住在 Core 那一层（闸门层不夹私货）。</summary>
    [Fact]
    public void TheCoordinatorQueuesRunsBehindItsGateAndAlwaysReleasesIt()
    {
        var raw = ReadRepoFile(Coord);
        var code = Code(raw);
        Assert.Equal(1, Count(code, "SemaphoreSlim"));
        Assert.Equal(1, Count(code, "_syncGate.WaitAsync(ct)"));
        Assert.Equal(1, Count(code, "_syncGate.Release()"));
        Assert.Equal(0, Count(code, "_syncGate.Wait(0)"));     // Wait(0) 会真占闸：随后的 WaitAsync 成了自己等自己
        Assert.Equal(1, Count(code, "private async Task<SyncSummary> SyncAllCoreAsync(CancellationToken ct)"));

        var gate = MethodBody(raw, "public async Task<SyncSummary> SyncAllAsync(CancellationToken ct)");
        Assert.Contains("WaitAsync", gate);
        Assert.Contains("SyncAllCoreAsync", gate);
        Assert.Contains("finally", gate);                       // 异常出口也放闸
        Assert.DoesNotContain("UpsertAsync", gate);             // 主体在 Core：闸门层不夹同步私货
    }

    // ===== 工具 =====

    /// <summary>可卡住的假源：FetchAsync 入口即报到（<see cref="GatedSource.Entered"/>），卡到
    /// <see cref="GatedSource.Unblock"/> 才放——测试由此拿到「第一轮正在跑」的确定性锚点，不靠秒表猜。</summary>
    private sealed class GatedSource : IItemSource
    {
        public string SourceId { get; } = "gated";
        public string DisplayName { get; } = "Gated";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Unblock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly object _lk = new();
        private int _concurrent;
        public int FetchEntries { get; private set; }
        public int MaxConcurrent { get; private set; }

        public bool IsAvailable => true;

        public async Task<FetchResult> FetchAsync(SyncContext ctx, CancellationToken ct)
        {
            lock (_lk)
            {
                _concurrent++;
                FetchEntries++;
                MaxConcurrent = Math.Max(MaxConcurrent, _concurrent);
            }
            try
            {
                Entered.TrySetResult();
                await Unblock.Task.WaitAsync(ct);
            }
            finally
            {
                lock (_lk) _concurrent--;
            }
            return new FetchResult(Array.Empty<Item>(), false);
        }

        public Task CommitCheckpointAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
    }

    /// <summary>第二条连接：busy_timeout=0——写锁被占时立刻 BUSY(5)，测试不等满默认 30 秒装作没发生。</summary>
    private SqliteConnection BareConnection()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ConnectionString);
        conn.Open();
        Exec(conn, "PRAGMA busy_timeout=0;");
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>探针行数（key 以探针前缀开头）：事务里那笔白写要么跟着回滚整批消失、要么跟着提交整批可见。</summary>
    private long ProbeRows()
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sync_state WHERE key LIKE '{ProbeKey}%';";
        return (long)cmd.ExecuteScalar()!;
    }
}
