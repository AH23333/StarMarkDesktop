#nullable enable
using System;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using Xunit;
using StarMark.Abstractions;
using StarMark.Core.Sync;
using StarMark.Data;
using StarMark.Integrations.Bookmarks;
using StarMark.Integrations.Ditto;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「源这一轮没读出来」闸门（片213·P-28B′＋P-26A 被 B′ 吞并后的落点）。
/// <para>P-28 账本的原话只点了取消那一条（<see cref="DittoCancellationGateTests"/> 守）；
/// 侦察时撞见的同族更大——<b>真读取失败</b>（Ditto 库被独占、书签文件正被浏览器写）从前被
/// <c>catch { result.Clear(); }</c> 吞成空表：在调用方看来自洽，<c>SyncCoordinator</c> 记成
/// <c>Success=true, PulledCount=0</c>，用户看到「同步完成」而账号上明明有书签。</para>
/// <para>修法分三段，本档各有一段尺：<b>①源读法</b>——真失败抛 <see cref="SourceReadException"/>
/// （InnerException 留底），未配置/不存在仍宽松交空（装机差异⛔ 读成故障）；
/// <b>②协调器</b>——认得这颗异常，把这一源记成失败＋<see cref="SourceSyncErrorKind.Read"/>，
/// 那句「为什么」走表（<c>sync.src.read</c>）；<b>③形状</b>——busy_timeout 在场、兜错空表那形⛔ 长回来、
/// catch 的次序不许把 SourceReadException 错吞进兜底 Exception。</para>
/// <para>⑤钉的是 ParseSafe 的既有决定：垃圾 JSON 解析失败＝交空列表（浏览器写到一半的那半个 JSON
/// 是「还没写完」不是「读失败」），⚡ 这条与「读失败要抛」是两件事，谁也别吞并谁。</para>
/// </summary>
public sealed class SourceReadFailureGateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark-srcread-{Guid.NewGuid():N}");

    public SourceReadFailureGateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        for (var i = 0; i < 5; i++)
        {
            try { Directory.Delete(_dir, true); return; }
            catch { Thread.Sleep(100); }
        }
    }

    // ==================== ① Ditto：真读取失败抛 SourceReadException（InnerException 留底） ====================

    [Fact]
    public void DittoReader_ThrowsSourceRead_WhenTheSchemaIsWrong()
    {
        var dbPath = Path.Combine(_dir, "bad-schema.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            // 建库但 Main 的列对不上读取器的 SELECT（lDate 等列缺失）：文件在、库合法，读才是真的会失败。
            cmd.CommandText = "CREATE TABLE Main(lID INTEGER PRIMARY KEY, wrong INTEGER)";
            cmd.ExecuteNonQuery();
        }

        using var reader = new DittoDatabaseReader(dbPath);
        Assert.True(reader.IsOpen, "库在且合法，读取器应当开着（这一站不背 schema 的锅）");

        var ex = Assert.Throws<SourceReadException>(() => reader.ReadRecent(50, CancellationToken.None));
        Assert.IsType<SqliteException>(ex.InnerException);     // 底层那颗真异常必须留底，⛔ 包丢了

        var ex2 = Assert.Throws<SourceReadException>(() => reader.Search("anything", 50, CancellationToken.None));
        Assert.IsType<SqliteException>(ex2.InnerException);
    }

    [Fact]
    public void DittoReader_MissingDatabase_StaysLenient()
    {
        // 装机差异（没装 Ditto）⛔ 读成同步故障：这一站保持宽松——不开门就交空，一声不响。
        using var reader = new DittoDatabaseReader(Path.Combine(_dir, "not-there.db"));
        Assert.False(reader.IsOpen);
        Assert.Empty(reader.ReadRecent(50, CancellationToken.None));
        Assert.Empty(reader.Search("q", 50, CancellationToken.None));
    }

    // ==================== ② 书签：在场文件全数读败才抛；未配置/垃圾 JSON 宽松 ====================

    private static string WriteBookmarks(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"starmark-bm-{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Bookmarks_LockedFile_ThrowsSourceRead()
    {
        // 真实失败模式：Chrome 正写 Bookmarks 那一瞬，读侧 File.ReadAllText 撞 IOException。
        var path = WriteBookmarks("locked.json", "{\"roots\": {}}");
        try
        {
            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var source = new EdgeBookmarksSource(path);
            Assert.True(source.IsAvailable);

            var ex = await Assert.ThrowsAsync<SourceReadException>(
                () => source.FetchAsync(new SyncContext(), CancellationToken.None));
            Assert.NotNull(ex.InnerException);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task Bookmarks_NotConfigured_ReturnsEmptyWithoutThrowing()
    {
        var source = new EdgeBookmarksSource(Path.Combine(_dir, "no-such-bookmarks.json"));
        Assert.False(source.IsAvailable);

        var fetch = await source.FetchAsync(new SyncContext(), CancellationToken.None);
        Assert.Empty(fetch.Items);      // 「本机没有它的数据」＝装机差异：交空，⛔ 抛
    }

    [Fact]
    public async Task Bookmarks_GarbageJson_IsSwallowedIntoEmpty()
    {
        // ⚡ 钉 ParseSafe 的既有决定：写到一半的半个 JSON 是「还没写完」不是「读失败」——
        // 解析层吞掉、交空列表。这条与「IO 失败要抛」是两件事，谁也别吞并谁。
        var path = WriteBookmarks("garbage.json", "{ this is not json");
        try
        {
            var source = new EdgeBookmarksSource(path);
            Assert.True(source.IsAvailable);

            var fetch = await source.FetchAsync(new SyncContext(), CancellationToken.None);
            Assert.Empty(fetch.Items);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    // ==================== ③ 协调器：认得这颗异常，记「读取失败」而不是「内部故障」 ====================

    private sealed class ThrowingSource : IItemSource
    {
        public string SourceId => "throwing";
        public bool IsAvailable => true;
        public Task<FetchResult> FetchAsync(SyncContext ctx, CancellationToken ct)
            => throw new SourceReadException("读不出来", new IOException("被独占了"));
        public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
    }

    [Fact]
    public async Task Coordinator_RecordsReadFailure_SeparatelyFromInternalFaults()
    {
        var dbPath = Path.Combine(_dir, "coord.db");
        var factory = new DbConnectionFactory(dbPath);
        new MigrationRunner(factory).EnsureSchema();
        var coordinator = new SyncCoordinator(new ItemRepository(factory), new[] { new ThrowingSource() });

        var summary = await coordinator.SyncAllAsync(CancellationToken.None);

        Assert.Equal(1, summary.FailedCount);
        var row = Assert.Single(summary.Sources);
        Assert.False(row.Success);
        Assert.Equal(SourceSyncErrorKind.Read, row.ErrorKind);          // ⛔ Unknown：那颗是「软件的错」
        Assert.NotNull(row.Error);
        Assert.Equal("sync.src.read", row.Error!.Key);                  // 那句「为什么」走表，⛔ 拼机器话
    }

    // ==================== ④ 形状闸：busy_timeout 在场・兜错空表那形⛔ 长回来・catch 次序 ====================

    [Fact]
    public void TheReaderAsksForTheBusyTimeoutAndNeverSwallowsIntoAnEmptyTable()
    {
        var reader = Code(ReadRepoFile("src/StarMark.Integrations/Ditto/DittoDatabaseReader.cs"));

        // 与主库 DbConnectionFactory 同口径：撞锁先等 5 秒，一瞬撞锁不再被读成「这一轮读失败」。
        Assert.Equal(1, Count(reader, "PRAGMA busy_timeout=5000;"));
        // P-28 点名的那一族形状⛔ 长回来：读失败降级空表＝调用方记 Success=true, PulledCount=0。
        Assert.Equal(0, Count(reader, "result.Clear();"));
        Assert.Equal(2, Count(reader, "throw new SourceReadException("));   // 两颗读法各一条：失败改抛
    }

    [Fact]
    public void TheCoordinatorCatchesSourceReadBeforeTheBlanket()
    {
        var coord = Code(ReadRepoFile("src/StarMark.Core/Sync/SyncCoordinator.cs"));

        // 次序：GitHubApiException → SourceReadException → Exception。SourceReadException 若排在
        // 兜底 Exception 之后＝永远接不到（编译器不吭声），那句「读取失败」就永远念不出来。
        var githubAt = coord.IndexOf("catch (GitHubApiException gex)", StringComparison.Ordinal);
        var readAt = coord.IndexOf("catch (SourceReadException srex)", StringComparison.Ordinal);
        var blanketAt = coord.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(githubAt >= 0 && readAt > githubAt && blanketAt > readAt,
            $"catch 次序不对（github={githubAt}, read={readAt}, blanket={blanketAt}）");
        Assert.Equal(1, Count(coord, "SourceSyncErrorKind.Read"));
    }
}
