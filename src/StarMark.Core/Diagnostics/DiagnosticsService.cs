#nullable enable
using System.Globalization;
using System.IO;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Data;

namespace StarMark.Core.Diagnostics;

/// <summary>
/// 诊断面板中的一行（标签 + 值）。对应扩展设置页的「诊断」区块（对比方案 P2-8）。
/// <para><b>两个字段都是 <see cref="LocMessage"/>，不是成品字</b>（批次 WI-57，P-187）：这一行直接上屏幕，
/// 而屏幕上那句话的语言由<b>贴上去的那一刻</b>决定。旧写法在这里就把「数据库体积」「从未」拼成中文，
/// 换语言之后没人能回去重算——服务它的 <c>CollectAsync</c> 是"进这一页才跑一次"的采集，
/// 里面还有 <c>COUNT(*)</c> 全表扫描，⛔ 拿换语言当重采的由头。</para>
/// <para>所以<b>事实与念法分两步</b>：这一颗记录存的是键＋实参，宿主在显示那一刻渲染一次，
/// 并自己订阅 <c>Locale.Changed</c> 重贴（<c>SettingsPageViewModel.PaintDiagnostics</c>）。</para>
/// </summary>
public sealed record DiagnosticEntry(LocMessage Label, LocMessage Value)
{
    /// <summary>
    /// 把<b>机器事实</b>（路径、版本号、ETag、启动刻度行）包成那一格要求的形状。
    /// <para>它走的是表里 <c>diag.fact</c> 那颗直通行（三语同形＝"{0}"）：<b>这一格交的是事实，不是一句话</b>。
    /// 为什么不直接收 <c>string</c>：那样这颗记录就成了"一半已取词、一半没取词"的混合体，
    /// 而这一族的缺陷恰恰是"忘了翻的那一句"被当成事实混在里面——形状统一，闸门才只有一条要读。</para>
    /// </summary>
    public static LocMessage Fact(string machineFact) => LocMessage.Of("diag.fact", machineFact);
}

/// <summary>
/// 只读诊断信息服务：聚合数据库、索引、各源与同步状态的关键事实。
/// 这是排查用户报障时最省沟通成本的一屏（对比方案 P2-8）。
/// </summary>
public sealed class DiagnosticsService
{
    private readonly DbConnectionFactory _factory;
    private readonly IItemRepository _repository;
    private readonly IEnumerable<IItemSource> _sources;

    public DiagnosticsService(DbConnectionFactory factory, IItemRepository repository, IEnumerable<IItemSource> sources)
    {
        _factory = factory;
        _repository = repository;
        _sources = sources;
    }

    public async Task<IReadOnlyList<DiagnosticEntry>> CollectAsync(CancellationToken ct = default)
    {
        var entries = new List<DiagnosticEntry>();

        // ── 这一份是哪次构建 ──
        // 放在最前面：它的用途是回答"你刚才跑的那次，到底含不含最新改动"。
        // 这个问题以前只能靠人记，记错一次的代价是把一轮已修好的判据当成无效又重新猜一遍。
        entries.Add(new DiagnosticEntry(LocMessage.Of("diag.label.build"), DiagnosticEntry.Fact(StarMark.Abstractions.BuildInfo.Display)));

        // ── 这一次启动的各段花了多久 ──
        // 与上一行同一个用途：报"启动慢"时不必来回猜，面板上就有分段数字（也才判断得出优化有没有真的变快）。
        // 交的是<b>编号＋毫秒</b>，⛔ 段名（批次 WI-67·P-191 他改判「文字可删去」）：那 29 颗名字是中文写死在
        // Mark 调用点的，而日志那一路按既定口径⛔ 国际化 ⇒ 屏幕与日志之间从"逐字对上文字"改成"按编号对上"，
        // #N ＝ 日志里第 N 条 [启动]。外层那句"段名见日志"是表里的话，三种语言各有各的写法（⛔ 拼在 Core 里）。
        if (StarMark.Abstractions.StartupProfile.SegmentTimings is { Count: > 0 } timings)
            entries.Add(new DiagnosticEntry(LocMessage.Of("diag.label.startupSegments"),
                LocMessage.Of("diag.value.startupSegments", string.Join(" · ", timings))));

        // ── 数据库 ──
        entries.Add(new DiagnosticEntry(LocMessage.Of("diag.label.dbPath"), DiagnosticEntry.Fact(_factory.DbPath)));
        entries.Add(new DiagnosticEntry(LocMessage.Of("diag.label.dbSize"), DiagnosticEntry.Fact(FormatBytes(GetDbSizeBytes()))));
        // 展示库内实际 schema_version，而非代码常量 MigrationRunner.CurrentVersion——
        // 迁移失败/半途中断时二者会背离，常量会让面板永远显示"最新"，掩盖问题。
        entries.Add(new DiagnosticEntry(LocMessage.Of("diag.label.schema"), GetSchemaVersionMessage()));

        // ── 条目与索引 ──
        var counts = await _repository.GetCountsByTypeAsync(ct);
        long total = 0;
        foreach (var c in counts.Values) total += c;
        string TypeCount(ItemType t) => counts.TryGetValue(t, out var v) ? v.ToString() : "0";
        entries.Add(new DiagnosticEntry(LocMessage.Of("diag.label.itemTotal"),
            LocMessage.Of("diag.value.itemTotal", total, TypeCount(ItemType.GitHubStar),
                TypeCount(ItemType.Bookmark), TypeCount(ItemType.File), TypeCount(ItemType.Clipboard))));

        using (var conn = _factory.Open())
        {
            entries.Add(new DiagnosticEntry(LocMessage.Of("diag.label.ftsRows"), Scalar(conn, "SELECT COUNT(*) FROM items_fts;")));
            entries.Add(new DiagnosticEntry(LocMessage.Of("diag.label.activityRows"), Scalar(conn, "SELECT COUNT(*) FROM activity;")));
        }

        // ── 各源可用性 ──
        // 不能只写"不可用"：那会把"这台机器压根没装"（用 Edge 的人看见 Chrome 不可用）与
        // "装了但读不到"、"没配 Token" 压成同一句话，用户只能猜。成因由来源自己给（它才知道找过什么）。
        // 名字与成因都交键：这一行是同一颗宿主上<b>同时</b>显示的两格，只翻外层等于半中半英。
        foreach (var source in _sources)
        {
            entries.Add(new DiagnosticEntry(
                LocMessage.Of("diag.label.source", SourceNames.NameOf(source.SourceId)),
                SourceAvailabilityText.Of(source.IsAvailable, source.AvailabilityHint)));
        }

        // ── GitHub 同步检查点（P1-4 落库的 sync_state）──
        var lastSynced = await _repository.GetSyncStateAsync("github:last_synced_at", ct);
        entries.Add(new DiagnosticEntry(LocMessage.Of("diag.label.lastSync"), FormatUnixSeconds(lastSynced)));
        var etag = await _repository.GetSyncStateAsync("github:etag", ct);
        entries.Add(new DiagnosticEntry(LocMessage.Of("diag.label.etag"),
            string.IsNullOrEmpty(etag) ? LocMessage.Of("diag.value.etagNone") : DiagnosticEntry.Fact(ShortenEtag(etag))));

        return entries;
    }

    /// <summary>读取库内实际 schema_version 并与代码目标版本对比展示；不一致时高亮警示。</summary>
    private LocMessage GetSchemaVersionMessage()
    {
        try
        {
            using var conn = _factory.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM sync_state WHERE key = 'schema_version';";
            var raw = cmd.ExecuteScalar()?.ToString();
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var actual))
                return LocMessage.Of("diag.value.schemaUnrecorded", MigrationRunner.CurrentVersion);
            return actual == MigrationRunner.CurrentVersion
                ? DiagnosticEntry.Fact(actual.ToString(CultureInfo.InvariantCulture))
                : LocMessage.Of("diag.value.schemaMismatch", actual, MigrationRunner.CurrentVersion);
        }
        catch (Exception ex)
        {
            return QueryFailed(ex);
        }
    }

    /// <summary>查不到时的同一句话：两个宿主（schema 那一格与两条计数）⛔ 各写一份原因形状。</summary>
    private static LocMessage QueryFailed(Exception ex) => LocMessage.Of("diag.value.queryFailed", ex.Message);

    private long GetDbSizeBytes()
    {
        try
        {
            long size = 0;
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var p = _factory.DbPath + suffix;
                if (File.Exists(p)) size += new FileInfo(p).Length;
            }
            return size;
        }
        catch { return 0; }
    }

    private static LocMessage Scalar(Microsoft.Data.Sqlite.SqliteConnection conn, string sql)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return DiagnosticEntry.Fact(cmd.ExecuteScalar()?.ToString() ?? "0");
        }
        catch (Exception ex)
        {
            return QueryFailed(ex);
        }
    }

    private static LocMessage FormatUnixSeconds(string? seconds)
    {
        if (string.IsNullOrEmpty(seconds) || !long.TryParse(seconds, CultureInfo.InvariantCulture, out var sec) || sec <= 0)
            return LocMessage.Of("diag.value.never");
        // InvariantCulture：诊断时间戳必须恒为公历数字；否则 th-TH 等区域会把年份显示成佛历（2569），
        // 这是本仓多次踩过的"佛历漂移"坑（见 RelativeTimeHelper / ActivityItemViewModel 的同款修）。
        return DiagnosticEntry.Fact(
            DateTimeOffset.FromUnixTimeSeconds(sec).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
    }

    /// <summary>ETag 这类"中段没信息、两头才认得出"的串用<b>中间省略</b>：头 12 ＋ … ＋ 尾 6；不超过 24 枚单元的原样过。</summary>
    private const int EtagUnchangedUpTo = 24, EtagHeadChars = 12, EtagTailChars = 6;

    /// <summary>
    /// 旧签名是 <c>Shorten(string value, int max = 24)</c>——<b>那个 <code>max</code> 只管闸门、不管输出</b>
    /// （切多长是写死的 12/6），传 40 也只会拿到 19 枚单元，属于"看起来在管、其实没管"的开关（P-130）。
    /// 只有一个调用点，所以把参数换成三颗有名字的常数：读数逐字不变，撒谎没有了。
    /// 本处<b>有意不走 <see cref="TextTrim"/></b>：那是"尾部截断＋省略号"的切法，与中间省略不是一种语义；
    /// 而 ETag 是十六进制串，代理对不可达。
    /// </summary>
    private static string ShortenEtag(string value)
        => value.Length <= EtagUnchangedUpTo
            ? value
            : value[..EtagHeadChars] + "…" + value[^EtagTailChars..];

    private static string FormatBytes(long bytes) => FileSizeText.Human(bytes);
}
