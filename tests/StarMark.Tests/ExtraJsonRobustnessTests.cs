#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Abstractions.Clipboard;
using StarMark.Core.Backup;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <c>items.extra_json</c> 里混进<b>读不出的值</b>时，用户该看到什么（P-17，批次 SK）。
/// <para>这一族以前的行为是"整页失败"：SQLite 的 <c>json_extract(坏串, …)</c> 抛的是<b>语句级</b>错误，
/// 一行坏数据就够让语言下拉、按语言筛选浏览、按"最近 Star"排序、剪贴板轮转全部报错——
/// 用户读作"加载失败"，而库里其余几千条好数据一条都没坏。登记时写的是"三处读谓词"，
/// 按后果重扫＝<b>九个 JSON 函数实参位</b>，其中一处早就手抄过守卫（本批把措辞收成一颗）。</para>
/// <para>所以这里的每一条都<b>碰真 SQLite</b>：把"坏值进库"当成前提，断言"查询照常出结果、
/// 好行一条不少、坏行按没有元数据处理"。写成"返回该跳过哪些行"再在内存里断言，
/// 就等于把最容易坏的那一段（SQL 文本本身）留在没测的地方——<see cref="TheGuardEmitsSqlQuotedEmptyObject"/>
/// 就是这一条的具体回报：守卫兜底必须是字符串字面量 <c>'{}'</c>，裸 <c>{}</c> 是语法错，
/// 而这个错只有碰真引擎才暴露（本批第一遍全量红三十多条，正是它）。</para>
/// </summary>
public sealed class ExtraJsonRobustnessTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_extrajson_{Guid.NewGuid():N}.db");
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public ExtraJsonRobustnessTests()
    {
        _factory = new DbConnectionFactory(_db);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { }
    }

    // ────────── 助手 ──────────

    /// <summary>好的一条 GitHub star（语言 C#、star 时刻 2000），走正常写路径以便进全文索引。</summary>
    private async Task<long> SeedGoodStarAsync(string sourceId, string title, long updatedAt)
    {
        var item = new Item
        {
            Type = ItemType.GitHubStar,
            Source = "github",
            SourceId = sourceId,
            Title = title,
            Uri = "https://github.com/acme/" + sourceId,
            SearchText = title,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
            ExtraJson = """{"Language":"C#","StarredAt":2000}""",
        };
        await _repo.UpsertAsync(new[] { item }, CancellationToken.None);
        return item.Id;
    }

    /// <summary>
    /// 造一行<b>坏元数据</b>：先按正常路径写进去，再把那一列改坏——
    /// 这模拟的是"库里已经有的历史坏行"（手改、坏备份导入、<c>EnsureLanguage</c> 回吐），
    /// 而不是"写入器今天还能不能产出坏值"（那一条见 <see cref="UnreadableExtraJsonFromABackupIsStoredAsNull"/>）。
    /// </summary>
    private async Task PoisonAsync(long id, string poison)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET extra_json = @bad WHERE id = @id;";
        cmd.Parameters.AddWithValue("@bad", poison);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>不经过仓储的裸插入（剪贴板行的桶由 <c>extra_json.clipFormat</c> 决定，这里要的是形状而不是写路径）。</summary>
    private long InsertClipboardRow(string extraJson)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO items(type, source, source_id, title, subtitle, uri, search_text,
                              description, created_at, updated_at, synced_at, extra_json, hidden, notes)
            VALUES('clipboard','clipboard',@sid,'clip','','',@sid,'',@ts,@ts,@ts,@extra,0,'');
            SELECT last_insert_rowid();";
        var sid = "clip:" + Guid.NewGuid().ToString("N");
        cmd.Parameters.AddWithValue("@sid", sid);
        cmd.Parameters.AddWithValue("@ts", 1000L);
        cmd.Parameters.AddWithValue("@extra", extraJson);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>会炸的两种形状（非法 JSON 与空串）＋合法但读不出键的两种（数组与 null 字面量）。
    /// 后两种在 SQLite 侧不抛、在 C# 侧同样取不出键，所以也要一起按"没有元数据"处理。</summary>
    public static TheoryData<string> UnreadableExtras() => new()
    {
        "{bad",
        string.Empty,
        "[1,2]",
        "null",
    };

    // ────────── 判据本体 ──────────

    [Fact]
    public void TheGuardEmitsSqlQuotedEmptyObject()
    {
        // 逐字钉住拼出来的那句：ELSE 后面必须是 SQL 字符串字面量。
        Assert.Equal(
            "(CASE WHEN json_valid(i.extra_json) THEN i.extra_json ELSE '{}' END)",
            ExtraJsonGuard.Safe("i.extra_json"));
        Assert.Equal(
            "(CASE WHEN json_valid(extra_json) THEN extra_json ELSE '{}' END)",
            ExtraJsonGuard.Safe("extra_json"));
    }

    [Theory]
    [InlineData(null, false)]              // null 不是"坏值"，是"没写"——由调用点自己判，这里一律不许留
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("{bad", false)]
    [InlineData("[1,2]", false)]
    [InlineData("null", false)]
    [InlineData("{}", true)]
    public void OnlyAReadableObjectMayBeStored(string? extra, bool storable)
    {
        Assert.Equal(storable, ExtraJsonGuard.IsStorableObject(extra));
        // Sanitize 的口径＝"留不下就归 null"，合法对象逐字透传（不许顺手改用户字节）。
        var expected = storable ? extra : null;
        Assert.Equal(expected, ExtraJsonGuard.SanitizeForStore(extra));
    }

    // ────────── 读侧：坏行不许让整页崩 ──────────

    [Theory]
    [MemberData(nameof(UnreadableExtras))]
    public async Task LanguageDropdownSurvivesOnePoisonedRow(string poison)
    {
        var good = await SeedGoodStarAsync("good", "repo good", 1000);
        await PoisonAsync(good, poison);                       // 唯一的行也坏
        await SeedGoodStarAsync("other", "repo other", 1500);  // 这行是好的

        var langs = await _repo.GetStarLanguagesAsync(CancellationToken.None);

        Assert.Equal(new[] { "C#" }, langs);   // 坏行按"读不出语言"跳过，好行照常列出
    }

    [Theory]
    [MemberData(nameof(UnreadableExtras))]
    public async Task BrowseByLanguageSurvivesOnePoisonedRow(string poison)
    {
        var doomed = await SeedGoodStarAsync("doomed", "repo doomed", 1000);
        await PoisonAsync(doomed, poison);
        await SeedGoodStarAsync("kept", "repo kept", 1500);

        var rows = await _repo.GetAllAsync(
            new BrowseFilter { TypeFilter = "githubstar", Language = "C#", Limit = 100 }, CancellationToken.None);

        Assert.Equal("repo kept", Assert.Single(rows).Title);   // 不抛，且坏行不冒充任何语言
    }

    [Theory]
    [MemberData(nameof(UnreadableExtras))]
    public async Task BrowseSortedByStarredFallsBackToUpdatedAtInsteadOfFailing(string poison)
    {
        // 坏行的回落必须是"按 updated_at 排"，不是"这行消失"——COALESCE 的那一半口径。
        var doomed = await SeedGoodStarAsync("poisoned", "repo poisoned", 3000);
        await PoisonAsync(doomed, poison);
        await SeedGoodStarAsync("good", "repo good", 1000);   // StarredAt=2000

        var rows = await _repo.GetAllAsync(
            new BrowseFilter { TypeFilter = "githubstar", Sort = "starred", Limit = 100 }, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Equal("repo poisoned", rows[0].Title);   // 取不出 StarredAt ⇒ 按 updated_at 3000 排在前
        Assert.Equal("repo good", rows[1].Title);       // StarredAt 2000
    }

    [Theory]
    [MemberData(nameof(UnreadableExtras))]
    public async Task SearchWithLanguageFilterSurvivesOnePoisonedRow(string poison)
    {
        var doomed = await SeedGoodStarAsync("s-doomed", "alpha doomed", 1000);
        await PoisonAsync(doomed, poison);
        await SeedGoodStarAsync("s-kept", "alpha kept", 1500);

        var result = await _repo.SearchAsync("alpha",
            new SearchFilter { Language = "C#", Sort = "recent", MaxResults = 50 }, CancellationToken.None);

        Assert.Equal("alpha kept", Assert.Single(result.Items).Title);
    }

    [Theory]
    [MemberData(nameof(UnreadableExtras))]
    public async Task SearchSortedByStarredSurvivesOnePoisonedRow(string poison)
    {
        var doomed = await SeedGoodStarAsync("t-doomed", "beta doomed", 1000);
        await PoisonAsync(doomed, poison);
        await SeedGoodStarAsync("t-kept", "beta kept", 1500);

        var result = await _repo.SearchAsync("beta",
            new SearchFilter { Sort = "starred", MaxResults = 50 }, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);                        // 两行都在：坏行不消失，只按 updated_at 回落
        Assert.Equal("beta kept", result.Items[0].Title);           // StarredAt 2000 仍在前
        Assert.Equal("beta doomed", result.Items[1].Title);         // 读不出 StarredAt ⇒ 按 updated_at 1000
    }

    [Theory]
    [MemberData(nameof(UnreadableExtras))]
    public async Task ClipboardBucketingSurvivesOnePoisonedRow(string poison)
    {
        // ClipIMG-1e 当年实测踩到的就是这一条：坏 JSON 那一行让整张"图片行"清单抛出来，
        // 于是对账一件都没做、日志里只有一句"加载失败"。
        InsertClipboardRow(poison);
        InsertClipboardRow("""{"clipFormat":"image","name":"a.png","thumb":"a_t.png"}""");

        var images = await _repo.GetClipboardImageAssetsAsync(CancellationToken.None);

        Assert.Single(images);   // 坏行按文本桶走（不进图片清单），而不是让查询整个失败
    }

    // ────────── 写侧：坏值不许进库 ──────────

    [Fact]
    public async Task UnreadableExtraJsonFromABackupIsStoredAsNull()
    {
        // 备份还原是 extra_json 唯一还能带进坏值的信任边界（在线生产者交出的都是现拼的合法对象）。
        var snapshotDir = Path.Combine(Path.GetTempPath(), $"starmark_extrajson_snap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(snapshotDir);
        BackupService.SnapshotDirectoryOverride = snapshotDir;
        try
        {
            var dst = Path.Combine(Path.GetTempPath(), $"starmark_extrajson_dst_{Guid.NewGuid():N}.db");
            var factory = new DbConnectionFactory(dst);
            new MigrationRunner(factory).EnsureSchema();
            var backup = new BackupService(new BackupRepository(factory));

            var env = new BackupEnvelope();
            env.Payload.Items.Add(new Item
            {
                Type = ItemType.GitHubStar, Source = "github", SourceId = "poison",
                Title = "poisoned", Uri = "https://github.com/acme/poison", SearchText = "poisoned",
                CreatedAt = 1000, ExtraJson = "{bad",
            });
            env.Payload.Items.Add(new Item
            {
                Type = ItemType.GitHubStar, Source = "github", SourceId = "blank",
                Title = "blanked", Uri = "https://github.com/acme/blank", SearchText = "blanked",
                CreatedAt = 1001, ExtraJson = string.Empty,
            });
            env.Payload.Items.Add(new Item
            {
                Type = ItemType.GitHubStar, Source = "github", SourceId = "fine",
                Title = "fine", Uri = "https://github.com/acme/fine", SearchText = "fine",
                CreatedAt = 1002, ExtraJson = """{"Language":"Go"}""",
            });

            var rr = await backup.RestoreAsync(env, RestoreMode.Merge, null, CancellationToken.None);

            Assert.True(rr.Success);                                  // 一份坏元数据不该让整份备份导不进来
            Assert.Equal(3, rr.ItemsRestored);
            Assert.Null(ReadRawExtraIn(dst, "poison"));               // 归 null＝"这条没有元数据"，而不是留着会炸的形状
            Assert.Null(ReadRawExtraIn(dst, "blank"));
            Assert.Equal("""{"Language":"Go"}""", ReadRawExtraIn(dst, "fine"));   // 合法对象逐字不动
        }
        finally
        {
            BackupService.SnapshotDirectoryOverride = null;
            try { Directory.Delete(snapshotDir, true); } catch { }
        }
    }

    private static string? ReadRawExtraIn(string dbPath, string sourceId)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT extra_json FROM items WHERE source_id = @sid;";
        cmd.Parameters.AddWithValue("@sid", sourceId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : (string)v;
    }

    [Fact]
    public async Task MarkingMissingFilesNeverWipesAPoisonedRowsMetadata()
    {
        // 这一条钉的是行过滤（AND json_valid(extra_json)），不是表达式守卫：坏 JSON 喂给 json_set
        // 得到的不是报错而是 NULL ⇒ "对账顺手改一个布尔"会把用户整列元数据抹掉。
        // 那比崩更难被发现（行还在、界面只是少了几项），所以宁可这一次不标，下一轮对账还会再来。
        var doomed = InsertClipboardRow("{bad");

        var changed = await _repo.SetClipboardMissingFlagsAsync(
            new[] { doomed }, Array.Empty<long>(), CancellationToken.None);

        Assert.Equal(0, changed);                       // 一行都没改
        Assert.Equal("{bad", ReadRawExtra(doomed));     // 原样留着，没被整列写成 NULL
    }

    private string? ReadRawExtra(long id)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT extra_json FROM items WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : (string)v;
    }

    [Fact]
    public async Task PoisonedRowDoesNotStopRecordingTheNextClipboardCopy()
    {
        // "记一条历史"是日常路径；一行坏数据不该让它从此失败（P-17 的用户可见后果就是这一条）。
        InsertClipboardRow("{bad");
        var draft = new Item
        {
            Type = ItemType.Clipboard,
            Source = ItemSources.Clipboard,
            SourceId = "clipboard:text:zz",
            Title = "新的一剪",
            SearchText = "新的一剪",
            CreatedAt = 2000,
            ExtraJson = """{"clipFormat":"text"}""",
        };

        var recorded = await _repo.RecordClipboardAsync(draft, CancellationToken.None);

        Assert.Equal("新的一剪", recorded.Title);
        Assert.NotEqual(0, recorded.Id);
    }
}
