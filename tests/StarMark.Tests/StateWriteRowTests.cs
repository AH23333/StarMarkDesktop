#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 单行状态写的"落到几行"契约（P-40，批次 SM）。
/// <para>从前这六颗基元把 <c>ExecuteNonQueryAsync</c> 的受影响行数丢掉，然后<b>无条件</b>
/// <c>DataChangeHub.Notify()</c>。于是"库里那一行已经不在了"（并发改删、<c>source_id</c> 归一漂移）
/// 与"写成功了"长得一模一样：界面把旗标翻过去、活动流记下一笔"修改"、回执说一句"已…"，
/// 而库里什么都没发生，下一次重载再把它弹回原样——<b>无异常、无日志，用户只看到"点了没反应"</b>。</para>
/// <para>现在的契约（每条都同时钉两件事，缺一不成立）：<b>①返回值说实话</b>、
/// <b>②没变化就不广播</b>。判读口径也钉住：<c>UPDATE</c> 命中同一行时<b>即使值没变也报 1 行</b>
/// （SQLite 的行为，见 <see cref="RewritingTheSameValueStillCountsAsLanded"/>），
/// 所以 0 行只有一个解释＝那一行不在了。</para>
/// </summary>
public sealed class StateWriteRowTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    /// <summary>广播计数器。<b>必须强持委托</b>：<see cref="DataChangeHub"/> 弱引用它，被回收就等于没订阅。</summary>
    private Action _tallyHandler = null!;
    private int _broadcasts;

    public StateWriteRowTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_staterows_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private async Task<long> NewItemAsync(string sourceId = "s1")
    {
        var item = new Item
        {
            Type = ItemType.Bookmark,
            Source = ItemSources.Local,
            SourceId = sourceId,
            Title = "标题甲",
            Uri = "https://example.com/" + sourceId,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        return await _repo.RecordItemAsync(item, CancellationToken.None);
    }

    /// <summary>开始数广播。<b>要在装数据之后调用</b>——装数据自己会广播一次。</summary>
    private void StartTally()
    {
        _broadcasts = 0;
        _tallyHandler = () => Interlocked.Increment(ref _broadcasts);
        DataChangeHub.Subscribe(_tallyHandler);
    }

    /// <summary>库里那一行还回得来吗（用来确认"写没落到行上"与"内容确实没变"是同一件事）。</summary>
    private long Scalar(string sql)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is long l ? l : Convert.ToInt64(v ?? 0);
    }

    // ────────── 命中那一行：true ＋ 广播一次 ──────────

    [Fact]
    public async Task SetPinnedOnAnExistingRowLandsAndBroadcasts()
    {
        var id = await NewItemAsync();
        StartTally();

        Assert.True(await _repo.SetPinnedAsync(id, true, CancellationToken.None));
        Assert.Equal(1, _broadcasts);
    }

    [Fact]
    public async Task RewritingTheSameValueStillCountsAsLanded()
    {
        // SQLite 对"值没变的 UPDATE"也报 1 行 ⇒ "false 只能解释为那一行不在了"这句判读才站得住。
        // 这条若红（变成 0 行），说明 0 行不再唯一对应"行没了"，整套契约的读法都要重写。
        var id = await NewItemAsync();
        Assert.True(await _repo.SetPinnedAsync(id, true, CancellationToken.None));
        StartTally();

        Assert.True(await _repo.SetPinnedAsync(id, true, CancellationToken.None));
        Assert.Equal(1, _broadcasts);          // 值没变也是"落到了那一行"：照常广播，别把幂等写成失败
    }

    [Fact]
    public async Task SetNoteOnAnExistingRowLandsAndKeepsTheText()
    {
        var id = await NewItemAsync();
        StartTally();

        Assert.True(await _repo.SetNoteAsync(id, "这条要留着", CancellationToken.None));
        Assert.Equal(1, _broadcasts);
        Assert.Equal("这条要留着", await _repo.GetNoteAsync(id, CancellationToken.None));
    }

    // ────────── 那一行不在了：false ＋ 一声不响地不广播 ──────────

    [Theory]
    [InlineData("pinned")]
    [InlineData("hidden")]
    [InlineData("note")]
    public async Task MissingRowWritesReportFalseAndStayQuiet(string which)
    {
        await NewItemAsync();                          // 让库不是空的：错的只是那个 id，不是整张表
        StartTally();
        const long missing = 987654321L;

        var landed = which switch
        {
            "pinned" => await _repo.SetPinnedAsync(missing, true, CancellationToken.None),
            "hidden" => await _repo.SetHiddenAsync(missing, true, CancellationToken.None),
            _ => await _repo.SetNoteAsync(missing, "写给一条已经不存在的条目", CancellationToken.None),
        };

        Assert.False(landed);                          // 不许把 0 行报成成功
        Assert.Equal(0, _broadcasts);                  // 更不许惊动所有常驻组件去重读一遍"没变"的数据
    }

    [Fact]
    public async Task MissingRowNoteLeavesTheStoredTextUntouched()
    {
        var id = await NewItemAsync();
        await _repo.SetNoteAsync(id, "原文", CancellationToken.None);

        Assert.False(await _repo.SetNoteAsync(987654321L, "不该落笔的内容", CancellationToken.None));
        Assert.Equal("原文", await _repo.GetNoteAsync(id, CancellationToken.None));
    }

    // ────────── 标签挂接：幂等，但"没变化"要说出来 ──────────

    [Fact]
    public async Task AddTagFirstTimeLandsAndSecondTimeSaysNothingChanged()
    {
        var id = await NewItemAsync();
        Assert.True(await _repo.AddTagAsync(id, "重点", CancellationToken.None));

        StartTally();
        Assert.False(await _repo.AddTagAsync(id, "重点", CancellationToken.None));   // 本来就挂着＝没变化
        Assert.Equal(0, _broadcasts);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM item_tags;"));                  // 也没多挂一次
    }

    [Fact]
    public async Task AddingATagToAMissingItemThrowsAndLeavesNoStrayTagName()
    {
        // 本批复验时纠正的一条旧说法：`INSERT OR IGNORE` 只咽 UNIQUE／主键／CHECK 那类冲突，
        // **外键违例照常抛**。所以 AddTag 的 false 只代表"这个挂接本来就存在"，
        // 而"目标条目不存在"是一条会响的路径（从前账本把它记成"被吞"，害我第一版注释跟着写错）。
        StartTally();

        var ex = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => _repo.AddTagAsync(987654321L, "再没人用的名字", CancellationToken.None));
        Assert.Contains("FOREIGN KEY", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _broadcasts);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM tags WHERE name = '再没人用的名字';"));   // 事务没提交，不留一颗空标签
    }

    [Fact]
    public async Task RemoveTagOnAnExistingLinkLandsAndOnAnAbsentLinkIsSilentlyFalse()
    {
        var id = await NewItemAsync();
        await _repo.AddTagAsync(id, "要摘掉", CancellationToken.None);

        StartTally();
        Assert.True(await _repo.RemoveTagAsync(id, "要摘掉", CancellationToken.None));
        Assert.Equal(1, _broadcasts);

        Assert.False(await _repo.RemoveTagAsync(id, "要摘掉", CancellationToken.None));   // 已经摘过了
        Assert.Equal(1, _broadcasts);                                                      // 第二次不该再广播
    }

    [Fact]
    public async Task RemoveTagForAnUnknownTagNameReportsFalseWithoutTouchingOtherLinks()
    {
        var id = await NewItemAsync();
        await _repo.AddTagAsync(id, "留着", CancellationToken.None);
        StartTally();

        Assert.False(await _repo.RemoveTagAsync(id, "库里没这个名字", CancellationToken.None));
        Assert.Equal(0, _broadcasts);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM item_tags;"));
    }

    // ────────── 按来源键删：0 行是合法的幂等，但也不能说"删掉了" ──────────

    [Fact]
    public async Task DeleteBySourceIdOnAnExistingRowLandsAndOnAnAbsentKeyIsFalse()
    {
        await NewItemAsync("dup-1");
        StartTally();

        Assert.True(await _repo.DeleteBySourceIdAsync(ItemSources.Local, "dup-1", CancellationToken.None));
        Assert.Equal(1, _broadcasts);

        Assert.False(await _repo.DeleteBySourceIdAsync(ItemSources.Local, "dup-1", CancellationToken.None));
        Assert.Equal(1, _broadcasts);              // 重复调同一个键：没删到，也就不再广播一次
    }

    [Fact]
    public async Task DeleteBySourceIdNeverReachesForAnotherSourcesKey()
    {
        // 与批次 EF 那条作用域闸门同源，但这里量的是**返回值**：作用域不匹配＝0 行＝false。
        await NewItemAsync("mine");
        StartTally();

        Assert.False(await _repo.DeleteBySourceIdAsync("someone-elses-source", "mine", CancellationToken.None));
        Assert.Equal(0, _broadcasts);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM items;"));
    }
}
