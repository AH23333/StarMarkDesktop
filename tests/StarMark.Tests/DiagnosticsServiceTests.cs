#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Diagnostics;
using StarMark.Data;

namespace StarMark.Tests;

/// <summary>P2-8 诊断信息服务：只读聚合的正确性。</summary>
public sealed class DiagnosticsServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public DiagnosticsServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_diag_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    /// <summary>
    /// "跑的是哪份构建"必须是程序自己报的，而且报的就是<em>正在运行那个文件</em>的写入时间。
    /// <para>真机反馈里出现过一整轮误判：树里已经修好，跑的人是旧产物（VS 源码未变时 F5 不重编，
    /// 光看产物时间戳分不出来），于是"问题依旧"被当成"修复无效"，又往判据上叠了一层猜测。
    /// 这条断言钉的是取值来源：换成任何"编译期常量/版本号"都会在这里红——那些都不能证明是这次编出来的。</para>
    /// </summary>
    [Fact]
    public void TheBuildStampIsReadFromTheRunningExecutableItself()
    {
        var path = Environment.ProcessPath;
        Assert.NotNull(path);
        Assert.Equal(new FileInfo(path!).LastWriteTime, BuildInfo.LocalTime);
        Assert.Equal(BuildInfo.LocalTime!.Value.ToString("yyyy-MM-dd HH:mm"), BuildInfo.Display);
    }

    [Fact]
    public async Task DiagnosticsOpensWithTheBuildStampSoTheAnswerIsTheFirstLine()
    {
        var svc = new DiagnosticsService(_factory, _repo, Array.Empty<IItemSource>());
        var entries = await svc.CollectAsync(CancellationToken.None);

        // 面板第一行就是它：不用滚动、不用解释去哪儿看（这一条存在的意义是"一眼能答"）。
        // ⚠ 这里钉的是**那颗键**，⛔ 屏幕上那几个字（批次 WI-57／P-187 交表之后，钉中文＝把语言钉进判据，
        // 另两列一改就红，而红的是一句假话——规则 #130；✅WI-53 的 `WeatherTests` 同一改法）。
        Assert.Equal("diag.label.build", entries[0].Label.Key);
        Assert.Equal(BuildInfo.Display, entries[0].Value.Render());   // 值那一格是机器事实：直通行的读数逐字过
    }

    [Fact]
    public async Task CollectAsync_ReportsDbCountsSourcesAndSyncState()
    {
        await _repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.GitHubStar, Source = "github", SourceId = "repo-1",
                       Title = "tool", Subtitle = "", Uri = "https://github.com/a/tool", SearchText = "tool" },
            new Item { Type = ItemType.Bookmark, Source = "test", SourceId = "b-1",
                       Title = "note", Subtitle = "", Uri = "https://example.com", SearchText = "note" },
        }, CancellationToken.None);
        await _repo.SetSyncStateAsync("github:last_synced_at", "1700000000", CancellationToken.None);
        await _repo.SetSyncStateAsync("github:etag", "W/\"abc123\"", CancellationToken.None);

        var svc = new DiagnosticsService(_factory, _repo, Array.Empty<IItemSource>());
        var entries = await svc.CollectAsync(CancellationToken.None);

        var byLabel = entries.ToDictionary(e => e.Label.Key, e => e.Value);

        // 数据库事实（路径／体积／库内版本号都按当前语言念出来——它们是事实，念法就是原样）
        Assert.Equal(_dbPath, byLabel["diag.label.dbPath"].Render());
        Assert.Contains("B", byLabel["diag.label.dbSize"].Render());
        Assert.Equal(MigrationRunner.CurrentVersion.ToString(), byLabel["diag.label.schema"].Render());

        // 条目计数：那句是**带读数的一句话**，所以钉"哪颗键、每颗槽收到的是哪个数"（坑表 #393 的判据形状）。
        var itemTotal = byLabel["diag.label.itemTotal"];
        Assert.Equal("diag.value.itemTotal", itemTotal.Key);
        Assert.Equal(new object?[] { 2L, "1", "1", "0", "0" }, itemTotal.Args);   // 总数 / Star / 书签 / 文件 / 剪贴板

        // 索引
        Assert.Equal("2", byLabel["diag.label.ftsRows"].Render());

        // 同步检查点（P1-4 落库的 sync_state 被读取展示；按本机时区动态计算期望值）
        var expected = DateTimeOffset.FromUnixTimeSeconds(1700000000)
            .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        Assert.Equal(expected, byLabel["diag.label.lastSync"].Render());
        Assert.Equal("W/\"abc123\"", byLabel["diag.label.etag"].Render());

        // 无源时不出"源："那一行
        Assert.DoesNotContain("diag.label.source", byLabel.Keys);
    }

    [Fact]
    public async Task CollectAsync_WithoutSyncState_ShowsNever()
    {
        var svc = new DiagnosticsService(_factory, _repo, Array.Empty<IItemSource>());
        var entries = await svc.CollectAsync(CancellationToken.None);

        Assert.Contains(entries, e => e.Label.Key == "diag.label.lastSync" && e.Value.Key == "diag.value.never");
        Assert.Contains(entries, e => e.Label.Key == "diag.label.etag" && e.Value.Key == "diag.value.etagNone");
    }

    /// <summary>
    /// 源的每一行：<b>名字与"现在怎么样"是同一行上同时显示的两格</b> ⇒ 两格都交键，
    /// 而名字那颗由 <see cref="SourceNames"/> 按 <c>SourceId</c> 映射（⛔ 在 Core 里抄一份中文名字）。
    /// </summary>
    [Fact]
    public async Task CollectAsync_RendersEachSourceRowFromTwoKeys()
    {
        var stub = new StubSource();
        var entries = await new DiagnosticsService(_factory, _repo, new IItemSource[] { stub })
            .CollectAsync(CancellationToken.None);

        var row = entries.Single(e => e.Label.Key == "diag.label.source");
        Assert.Equal("source.name.chrome", Assert.IsType<LocMessage>(row.Label.Args![0]).Key);
        Assert.Equal("source.avail.available", row.Value.Key);

        stub.Unavailable();
        var failed = (await new DiagnosticsService(_factory, _repo, new IItemSource[] { stub })
            .CollectAsync(CancellationToken.None)).Single(e => e.Label.Key == "diag.label.source");
        Assert.Equal("source.avail.missing", failed.Value.Key);
        Assert.Equal("source.hint.ditto", Assert.IsType<LocMessage>(failed.Value.Args![0]).Key);
    }

    /// <summary>一颗只用来被列进诊断的源：它不查库，只把"我叫什么、我能不能用"说清楚。</summary>
    private sealed class StubSource : IItemSource
    {
        private bool _available = true;
        public string SourceId => ItemSources.Chrome;
        public bool IsAvailable => _available;
        public LocMessage? AvailabilityHint => _available ? null : LocMessage.Of("source.hint.ditto", "某条路径");
        public void Unavailable() => _available = false;
        public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
        public Task<IReadOnlyList<Item>> SearchAsync(string q, SearchFilter f, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
    }

    [Fact]
    public async Task SchemaVersion_ReflectsActualDbValue_NotCodeConst()
    {
        // R8#6：诊断必须显示库内**实际** schema_version，而非硬编码的 MigrationRunner.CurrentVersion。
        // 迁移半途中断/失败时二者背离，常量会永远显示"最新"而掩盖问题。
        // 手动把库内值改成 2（低于代码目标 4），模拟"迁移未成功"。
        using (var conn = _factory.Open())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE sync_state SET value='2' WHERE key='schema_version';";
            cmd.ExecuteNonQuery();
        }

        var svc = new DiagnosticsService(_factory, _repo, Array.Empty<IItemSource>());
        var entries = await svc.CollectAsync(CancellationToken.None);
        var schema = entries.Single(e => e.Label.Key == "diag.label.schema").Value;

        Assert.Equal("diag.value.schemaMismatch", schema.Key);
        Assert.Equal("2", schema.Args![0]!.ToString());                              // 读到的真是库里那个 2
        Assert.Equal(MigrationRunner.CurrentVersion.ToString(), schema.Args![1]!.ToString());  // 目标那颗槽给的才是代码常量
    }
}
