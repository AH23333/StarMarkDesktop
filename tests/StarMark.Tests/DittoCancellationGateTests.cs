#nullable enable
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;
using StarMark.Abstractions;
using StarMark.Integrations.Ditto;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// Ditto 那颗源的<b>取消</b>闸门（批次 P-32 站点②）。
/// <para>账本原话把这一格写成「假异步 ⇒ 逐按键阻塞 UI 线程」。读调用点后<b>只有前半还成立</b>：
/// 实时搜索那侧 <c>SearchService</c> 早就把每一颗源包进 <c>Task.Run(…, ct)</c>，同步体现在跑在池线程上，
/// UI 线程不等它。真剩下的是后半——<b>令牌一路走到这颗源就被扔掉</b>：调用方 <c>WaitAsync(ct)</c> 解了等，
/// 而那轮 Ditto 全表 <c>LIKE '%…%'</c> 扫描＋每颗 clip 一颗 <c>Data</c> 点查仍会在池上跑到完。
/// 实时搜索逐按键发一次、上一发会被取消 ⇒ 每敲一个字都留下一条在跑的整表扫描，而屏幕上什么都不说。</para>
/// <para>修法沿用仓里那一族已有形状（<c>SyncCoordinator</c>／<c>ItemRepository.Local</c>／<c>RssPageViewModel</c>
/// 都在循环边界 <c>ct.ThrowIfCancellationRequested()</c>，而 <c>catch (OperationCanceledException) { throw; }</c>
/// 本仓有二十来处）：两颗公共出口一副规则（都 <c>Task.Run(…, ct)</c>・令牌下传到两颗查询）・
/// 掐中<b>上抛</b>。那颗「查询出错就交空表」的兜错<b>必须放行取消</b>：否则被掐掉的一次读降级成空表，
/// 在调用方看来自洽，而 <c>SyncCoordinator</c> 会记成 <c>Success=true, PulledCount=0</c>——正是 P-28 点名的那一族。</para>
/// <para>两把尺各问一维：① 源码尺问"这一族现在长什么样"（两颗出口各带令牌・假异步那形⛔ 长回来）；
/// ② 运行时尺问"掐下去真的会停手、而⛔ 掐的时候照旧读得到"。少了②，①就只是一段会说谎的注释。</para>
/// <para>夹具自建一份小库，⛔ 往 <c>DittoSourceTests</c> 的共享夹具里加行（那条夹具的计数读数会被每一行
/// 新增牵动，#160 同一条口径）。</para>
/// </summary>
public sealed class DittoCancellationGateTests : IDisposable
{
    private const string SourceFile = "src/StarMark.Integrations/Ditto/DittoSource.cs";
    private const string ReaderFile = "src/StarMark.Integrations/Ditto/DittoDatabaseReader.cs";

    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"starmark-ditto-cancel-{Guid.NewGuid():N}.db");

    public DittoCancellationGateTests()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Main(lID INTEGER PRIMARY KEY, lDate INTEGER, mText TEXT, bIsGroup INTEGER);
            CREATE TABLE Data(lID INTEGER PRIMARY KEY AUTOINCREMENT,
                              lParentID INTEGER, strClipBoardFormat TEXT, ooData BLOB);
            """;
        cmd.ExecuteNonQuery();

        // 21/22 是非分组文本行，23 是分组行（SQL 那一侧本就该把它挡在门外）
        InsertMain(21, "STARMARK 正文第一颗");
        InsertMain(22, "另一颗正文");
        InsertGroup(23, "分组: 不该被读到");
        InsertUnicode(21, "STARMARK 正文第一颗");
        InsertUnicode(22, "另一颗正文");
    }

    public void Dispose()
    {
        for (var i = 0; i < 5; i++)
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); return; }
            catch { Thread.Sleep(100); }
        }
    }

    // ==================== ① 源码尺：两颗公共出口一副规则・令牌一路下传 ====================

    [Fact]
    public void BothPublicExitsLeaveTheCallingThreadAndHandTheTokenToTheQuery()
    {
        var src = Code(ReadRepoFile(SourceFile));

        Assert.Equal(2, Count(src, "Task.Run("));                    // 两颗公共出口各一次
        Assert.Equal(2, Count(src, "}, ct);"));                      // 而两处都把令牌交给 Task.Run：已取消的一轮⛔ 起
        Assert.Equal(1, Count(src, "ReadRecent(MaxSync, ct)"));
        Assert.Equal(1, Count(src, "r.ReadRecent(Math.Max(1, filter.MaxResults), ct)"));
        Assert.Equal(1, Count(src, "r.Search(query, Math.Max(1, filter.MaxResults), ct)"));
        Assert.Equal(1, Count(src, "ct.ThrowIfCancellationRequested();"));   // 门口那道：已取消就⛔ 付探测那笔税

        // 禁项：旧的那一形（同步体＋Task.FromResult）不许长回来
        Assert.DoesNotContain("Task.FromResult", src, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReaderCarriesTheTokenIntoBothLoopsAndLetsCancellationEscape()
    {
        var src = Code(ReadRepoFile(ReaderFile));

        Assert.Equal(2, Count(src, "CancellationToken ct = default)"));                 // 两颗查询各收一份
        Assert.Equal(4, Count(src, "ct.ThrowIfCancellationRequested();"));               // 每颗各两道：逐行取回＋逐颗补附件
        Assert.Equal(2, Count(src, "catch (OperationCanceledException) { throw; }"));    // 掐中上抛，走在兜错之前
        Assert.Equal(2, Count(src, "result.Clear();"));                                  // 兜错照旧只管"查询真失败"
    }

    // ==================== ② 运行时尺：掐下去真停手・没掐时照旧读得到 ====================

    [Theory]
    [InlineData(true)]      // 最近列表那条腿（ORDER BY lDate DESC LIMIT）
    [InlineData(false)]     // 关键词那条腿（LIKE 全表扫描＝真正会跑久的那条）
    public void Reader_HandsBackCancellation_InsteadOfAnEmptyTable(bool recentShape)
    {
        using var reader = new DittoDatabaseReader(_dbPath);
        Assert.True(reader.IsOpen);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // 抛的是 OperationCanceledException 本型——若它被那颗"查询失败就交空表"的兜错吞掉，
        // 这里读到的会是空列表（＝P-28 那一族的形状），而不是"这一发被取消了"。
        if (recentShape)
            Assert.Throws<OperationCanceledException>(() => reader.ReadRecent(50, cts.Token));
        else
            Assert.Throws<OperationCanceledException>(() => reader.Search("STARMARK", 50, cts.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reader_StillReads_WhenNobodyCancels(bool recentShape)
    {
        using var reader = new DittoDatabaseReader(_dbPath);
        Assert.True(reader.IsOpen);

        var rows = recentShape
            ? reader.ReadRecent(50, CancellationToken.None)
            : reader.Search("STARMARK", 50, CancellationToken.None);

        Assert.NotEmpty(rows);
        Assert.DoesNotContain(rows, c => c.IsGroup);      // 正件没被新守卫顺手打死；分组行仍由 SQL 挡住
    }

    [Fact]
    public async Task BothSourceExits_HandTheCancellationBack()
    {
        var source = new DittoSource(_dbPath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.FetchAsync(new SyncContext(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.SearchAsync("STARMARK", new SearchFilter { MaxResults = 10 }, cts.Token));
    }

    [Fact]
    public async Task BothSourceExits_StillReturnRows_WhenNothingCancels()
    {
        var source = new DittoSource(_dbPath);

        var fetched = (await source.FetchAsync(new SyncContext(), CancellationToken.None)).Items;
        Assert.Equal(2, fetched.Count);

        var hits = await source.SearchAsync("STARMARK", new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        Assert.Contains(hits, i => i.SourceId == "ditto:21");
    }

    // ==================== 夹具（只建 reader 实际用到的那几列） ====================

    private void InsertMain(long id, string text) => InsertRow(id, text, 0);

    private void InsertGroup(long id, string text) => InsertRow(id, text, 1);

    private void InsertRow(long id, string text, int isGroup)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Main (lID, lDate, mText, bIsGroup) VALUES ($id, $date, $text, $g)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - id);
        cmd.Parameters.AddWithValue("$text", text);
        cmd.Parameters.AddWithValue("$g", isGroup);
        cmd.ExecuteNonQuery();
    }

    private void InsertUnicode(long parentId, string text)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Data (lParentID, strClipBoardFormat, ooData) VALUES ($p, $f, $d)";
        cmd.Parameters.AddWithValue("$p", parentId);
        cmd.Parameters.AddWithValue("$f", "CF_UNICODETEXT");
        cmd.Parameters.AddWithValue("$d", Encoding.Unicode.GetBytes(text + "\0"));
        cmd.ExecuteNonQuery();
    }
}
