#nullable enable
using System.IO;
using Microsoft.Data.Sqlite;

namespace StarMark.Data;

/// <summary>
/// SQLite 连接工厂。统一管理连接字符串与 PRAGMA 设置。
/// 数据库文件位于 %APPDATA%\StarMark\starmark.db。
/// </summary>
public sealed class DbConnectionFactory
{
    private readonly string _connectionString;

    public string DbPath { get; }

    public DbConnectionFactory(string? dbPath = null)
    {
        // 开发期允许通过 STARMARK_DB_PATH 环境变量覆盖默认路径
        // （避免沙盒限制 %APPDATA% 写入；生产环境走 %APPDATA%\StarMark\starmark.db）。
        // 两个变量的名字与优先顺序只写在 StarMark.Abstractions.UserDataPaths 那一处。
        DbPath = dbPath ?? StarMark.Abstractions.UserDataPaths.Database();
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        // SQLite 数据库文件位于 %APPDATA%\StarMark\starmark.db。
        // 用 builder 而非裸拼接：路径里的 ';' / '=' 是连接串保留字符（Windows 文件名合法，如用户名含 ';'），
        // 裸拼会被解析成关键字分隔符而抛 ArgumentException，导致应用在自己的库上都打不开。builder 会自动加引号转义。
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DbPath }.ConnectionString;
    }

    /// <summary>
    /// 可选的<b>连接往返</b>计数器。<b>按实例挂载、不是全局开关</b>：全局计数会被同时段别的代码写库带偏，
    /// 那种数字只是看起来像证据。生产环境为 null，代价是一次判断。
    /// <para>为什么只量连接而不量语句：性能闸门要的是"确定、不 flaky、且能回归"，
    /// 秒表在开发机上测不出任何结论（磁盘/杀软/后台同步都在动），
    /// 而"给一批条目打标签要开几次库连接"是一个直接对应往返成本的整数——
    /// 每次 <c>Open()</c> 都带一遍 PRAGMA（WAL/foreign_keys/busy_timeout），
    /// 所以连接数就是这类"逐条调用"写法的主要开销。</para>
    /// </summary>
    public DbActivityCounter? Counter { get; set; }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        var counter = Counter;
        if (counter is not null)
        {
            counter.RecordConnectionOpened();

        }
        conn.Open();
        // 每次连接时设置 PRAGMA（SQLite 每连接独立）
        using (var cmd = conn.CreateCommand())
        {
            // busy_timeout：WAL 下写写仍互斥，应用有约 30 处各自 Open 的短连接 + 后台同步/组件写入并发。
            // 不设时默认 0ms，任一次瞬时锁争用即抛 SQLITE_BUSY（表现为同步/迁移/组件保存偶发失败）。
            // 给 5s 让 SQLite 自行重试排队，消除这类伪失败。
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
        return conn;
    }

    public static string DefaultDbPath() => StarMark.Abstractions.UserDataPaths.Database();
}

/// <summary>
/// 一个连接工厂上的库连接计数。<b>存在的唯一目的是让"往返次数"变成可断言的量</b>：
/// 秒表测不出结论（磁盘、杀软、后台同步都在动），而"这一批活开了几次连接"是一个确定的整数，
/// 能写进回归用例——性能改动最容易烂掉的方式正是"当初快了多少"没人能复现。
/// </summary>
public sealed class DbActivityCounter
{
    private long _connections;

    public void RecordConnectionOpened() => Interlocked.Increment(ref _connections);

    /// <summary>到这里为止开了多少次库连接（每次都带一遍 PRAGMA）。</summary>
    public int Connections => (int)Interlocked.Read(ref _connections);

    /// <summary>清零并返回自己：<c>factory.Counter = counter.Measure(); …… counter.Connections</c>。</summary>
    public DbActivityCounter Reset()
    {
        Interlocked.Exchange(ref _connections, 0);
        return this;
    }
}

/// <summary>
/// 裸 <c>BEGIN IMMEDIATE</c> 的写事务包装（P-31 (A)）。
/// <para>
/// 为什么不用 <c>SqliteConnection.BeginTransaction</c>：它发的 <c>BEGIN</c>（DEFERRED）把写锁推迟到
/// <b>第一条写语句</b>才去占。「先读后写」的事务（同步 upsert／打标签那一族）在升级写锁那一刻若撞上
/// 另一连接的活跃写事务，得到的是 <c>BUSY_SNAPSHOT(517)</c>——那是<b>永不可重试成功</b>的错误：
/// 快照已定，等再久也升不上去，busy_timeout 对这条路径不构成上界。IMMEDIATE 在 BEGIN 那一刻就占住
/// 写锁：真有竞争时退化为有意义的 <c>BUSY(5)</c>（对方一提交就让路，busy_timeout 排得住队），
/// 517 从根上不再出现。
/// </para>
/// <para>
/// 为什么是裸 SQL 而不是 provider 事务：Microsoft.Data.Sqlite 的 <c>BeginTransaction</c> 没有
/// immediate 重载。SQLite 的事务本就是<b>连接级</b>的——裸 BEGIN 之后这条连接上的命令自动在事务里
/// （provider 对 <c>cmd.Transaction</c> 的要求只是它自己的簿记），既有「事务内命令不设 Transaction」
/// 的路径（<c>UpsertAsync</c>→<c>UpsertOne</c>）就是这一事实的活证人。
/// </para>
/// </summary>
public sealed class ImmediateTxn : IDisposable
{
    private readonly SqliteConnection _conn;
    private bool _finished;

    internal ImmediateTxn(SqliteConnection conn)
    {
        _conn = conn;
        Exec("BEGIN IMMEDIATE");
    }

    /// <summary>提交。SQLite 是本地执行，没有真正的异步——异步包装只为与既有调用点同形。</summary>
    public void Commit()
    {
        Exec("COMMIT");
        _finished = true;
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        Commit();
        return Task.CompletedTask;
    }

    public void Rollback()
    {
        Exec("ROLLBACK");
        _finished = true;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        Rollback();
        return Task.CompletedTask;
    }

    /// <summary>未提交就走到 Dispose（异常上抛／早退）＝整笔回滚：半截事务要是提交出去，比「白写一次」
    /// 坏一个量级——调用方按「已失败」收场，库里却留着半成品。</summary>
    public void Dispose()
    {
        if (_finished) return;
        try { Rollback(); }
        catch (SqliteException) { }   // 回滚本身失败（连接已坏）时不再叠一层异常盖掉原始错误
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

public static class ImmediateTxnExtensions
{
    /// <summary>在这条连接上开一个 IMMEDIATE 写事务（P-31 (A)）：BEGIN 那一刻就占住写锁，
    /// 「先读后写」的事务从此不会升级成 BUSY_SNAPSHOT(517)。</summary>
    public static ImmediateTxn BeginImmediate(this SqliteConnection conn) => new(conn);
}
