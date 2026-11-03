#nullable enable
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;

namespace StarMark.Integrations.Ditto;

/// <summary>
/// Ditto 剪贴板源：直读 Ditto 的剪贴板库（只读；装机版 3.25 实测落 %APPDATA%\Ditto\Ditto.db・旧形与便携形见候选表）。
/// 文本剪贴板 → Clipboard 条目；文件剪贴板（CF_HDROP）→ 取第一个文件做副标题与 Uri。
/// </summary>
public sealed class DittoSource : IItemSource
{
    private const int MaxSync = 500;

    private string? _dbPath;

    /// <summary>探测过的位置清单——提示里要说清"找过哪里"，否则用户只知道"没有"却无从判断是自己没装、还是装在别处。</summary>
    private IReadOnlyList<string> _probed = Array.Empty<string>();

    private bool _probeDone;

    /// <summary>这台机器上真探过几次（诊断/测试用）。<b>构造期必须是 0</b>——那是"没装 Ditto 就不该付这笔税"的证据。</summary>
    internal int ProbeCount;

    public string SourceId => ItemSources.Ditto;

    /// <summary>问一次就够：<c>IsAvailable</c> 在一次搜索里会被问两遍（Core 侧筛源＋队列查询前），探两遍是白花。</summary>
    public bool IsAvailable
    {
        get
        {
            EnsureProbed();
            return _dbPath != null && File.Exists(_dbPath);
        }
    }

    /// <summary>
    /// Ditto 是外部程序：没装就没有数据库，这一项本来就该是空的（与内置剪贴板历史是两回事）。
    /// <para><b>交回键＋实参</b>（批次 WI-57）：那句话住在表里，这里只递"探过哪几条路径"。
    /// 路径清单的分隔符取<b>语言中性的「 · 」</b>——它切在事实与事实之间，⛔ 为它再立一颗"分隔符"表行
    /// （全仓已有 <c>health.join.list</c>／<c>trending.notice.join</c> 两颗按句子主人各写一份，
    /// 第三颗只会让"清单怎么连"多出一个人；这一族要不要收成一颗另见账本）。</para>
    /// </summary>
    public LocMessage? AvailabilityHint
    {
        get
        {
            EnsureProbed();
            var where = TextTrim.Ellipsize(string.Join(" · ", _probed), SourceAvailabilityText.MaxFactLength);
            return LocMessage.Of("source.hint.ditto", where);
        }
    }

    /// <param name="dbPath">显式指定库路径（测试/自定义安装位置）；为 null 时<b>推迟到第一次真要答案时</b>才探测。</param>
    public DittoSource(string? dbPath = null)
    {
        if (dbPath is null) return;
        _dbPath = dbPath;
        _probed = new[] { dbPath };
        _probeDone = true;
    }

    /// <summary>
    /// 探测只做一次，而且<b>不在构造里做</b>。为什么这条值得单独立：候选路径含"枚举进程 + 读别的进程的 MainModule"，
    /// 而 DI 在主窗构造那一刻就把这五颗源全建出来（P-135）——本机没装 Ditto 时（今天就是：库不存在、进程也没有），
    /// 这笔钱<b>每次启动都付、且永远为空</b>。推到这里付的第一个代价是"第一次问可用性时多等几毫秒"，
    /// 而那一次本来就要开库读数据，不差这点。
    /// </summary>
    private void EnsureProbed()
    {
        if (_probeDone) return;
        _probeDone = true;
        ProbeCount++;
        _probed = CandidateDbPaths();
        _dbPath = FindDbPath(_probed);
    }

    /// <summary>
    /// Ditto 数据库的候选位置。<b>Ditto 没有单一约定路径</b>：装机版（3.25.113.0・2026-10-31 授权装机实测）
    /// 把库落在 <c>%APPDATA%\Ditto\Ditto.db</c>（无 <c>DB\</c> 子目录・名里没有 <c>DB</c>），老文档形是
    /// <c>%APPDATA%\Ditto\DB\DittoDB.db</c>，便携版把库放在 <c>Ditto.exe</c> 旁边，也有人把数据目录指到
    /// <c>%LOCALAPPDATA%</c>。只探一条等于"在我这台机器上能跑"——旧表整张只认 <c>DittoDB.db</c> 那一族，
    /// 结果装着 Ditto 也永远"不可用"（P-192 结案入口①授权装机量出来的）。所以这里把已知形态都列出来，
    /// 由 <see cref="FindDbPath"/> 取第一个真实存在的。
    /// </summary>
    public static IReadOnlyList<string> CandidateDbPaths()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };
        var list = new List<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            list.Add(Path.Combine(root, "Ditto", "Ditto.db"));
            list.Add(Path.Combine(root, "Ditto", "DB", "DittoDB.db"));
            list.Add(Path.Combine(root, "Ditto", "DittoDB.db"));
        }
        list.AddRange(PortableDbPaths());
        return list.Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>便携版：正在运行的 Ditto.exe 所在目录下的 <c>Ditto.db</c>、<c>DB\DittoDB.db</c> 或 <c>DittoDB.db</c>。</summary>
    private static IEnumerable<string> PortableDbPaths()
    {
        foreach (var dir in RunningDittoDirectories())
        {
            yield return Path.Combine(dir, "Ditto.db");
            yield return Path.Combine(dir, "DB", "DittoDB.db");
            yield return Path.Combine(dir, "DittoDB.db");
        }
    }

    /// <summary>
    /// 取运行中 Ditto 的模块目录。<b>整体兜错</b>：读别的进程的 MainModule 在无权限/进程刚退出时会抛，
    /// 而这只是"多试一个候选路径"，不该让源探测本身失败。（先收集再返回：C# 不允许在带 catch 的
    /// try 块里 yield return。）
    /// </summary>
    private static IReadOnlyList<string> RunningDittoDirectories()
    {
        var dirs = new List<string>();
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("Ditto"))
        {
            try
            {
                var dir = Path.GetDirectoryName(p.MainModule?.FileName ?? string.Empty);
                if (!string.IsNullOrEmpty(dir)) dirs.Add(dir!);
            }
            catch { }
            finally { p.Dispose(); }
        }
        return dirs;
    }

    /// <summary>第一个真实存在的候选路径；都没有则返回首个默认位置（让提示能说清"找过哪里"）。</summary>
    public static string? FindDbPath(IReadOnlyList<string>? candidates = null)
    {
        candidates ??= CandidateDbPaths();
        foreach (var c in candidates)
        {
            try { if (File.Exists(c)) return c; } catch { }
        }
        return candidates.Count > 0 ? candidates[0] : null;
    }

    public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
        // 「立即同步」由 UI 线程一路 await 到这里，而下面读的是 Ditto 的 SQLite 文件（同步实现，
        // 且可能被 Ditto 自身持写锁）——留在调用线程上就是一次点击冻一次。令牌一并交给 <c>Task.Run</c>：
        // <b>已取消的一轮从此⛔ 起</b>，⛔ 只把底下那颗同步读搬到池上、由调用方独自等。
        => Task.Run(() =>
        {
            var clip = ReadAsync(r => r.ReadRecent(MaxSync, ct), ct);
            if (clip.Count == 0) return (IReadOnlyList<Item>)Array.Empty<Item>();
            return clip.Select(Map).ToList();
        }, ct);

    public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
        // 两颗公共出口一副规则（旧写法在这里是「Task 形・同步体」：它把同步读留在<b>调用线程</b>上，
        // 今天没冻到 UI 只因为 <c>SearchService</c> 那侧替它包了 <c>Task.Run</c>——⛔ 让一颗公共形状
        // 的成败挂在别人的调用点上）。令牌交到底层那颗 LIKE 扫描的循环边界上，掐中就停手。
        => Task.Run(() =>
        {
            var matched = ReadAsync(r => string.IsNullOrWhiteSpace(query)
                ? r.ReadRecent(Math.Max(1, filter.MaxResults), ct)
                : r.Search(query, Math.Max(1, filter.MaxResults), ct), ct);
            return (IReadOnlyList<Item>)matched.Select(Map).ToList();
        }, ct);

    private List<DittoClip> ReadAsync(Func<DittoDatabaseReader, List<DittoClip>> query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();   // 门口先回绝：下面那颗 IsAvailable 是"枚举进程＋读别的进程模块"的探测税
        if (!IsAvailable) return new List<DittoClip>();
        using var reader = new DittoDatabaseReader(_dbPath!);
        return reader.IsOpen ? query(reader) : new List<DittoClip>();
    }

    private static Item Map(DittoClip clip)
    {
        var text = clip.Text ?? string.Empty;
        var firstLine = FirstLine(text, 140);
        var isFiles = clip.Files.Count > 0;

        var item = new Item
        {
            Type = ItemType.Clipboard,
            Source = ItemSources.Ditto,
            SourceId = $"ditto:{clip.Id}",
            Title = isFiles ? string.Join(", ", clip.Files.Take(1)) : firstLine,
            // 文本行的副标题一颗字都不存：那句「Ditto 剪贴板」的主人住在表里（<c>source.name.ditto</c>・
            // P-187 删掉各源自写的中文常量就是为了这颗），这里存一份就是第二主人，而且存进去的那一刻就冻了语言。
            // 文件行照存路径——那是中性事实，⛔ 语言内容（口径同 ✅P-199）。
            Subtitle = isFiles ? clip.Files[0] : string.Empty,
            Uri = isFiles ? ToFileUri(clip.Files[0]) : string.Empty,
            SearchText = isFiles ? string.Join("\n", clip.Files) + "\n" + text : text,
            CreatedAt = clip.Date,
            UpdatedAt = clip.Date,
            // 正文必须落在 Description：ItemRepository.UpsertOne 落库时用 title+description+notes+tags
            // 重算 search_text（并 CJK 展开），**无条件忽略**来源自带的 SearchText。旧实现把整段正文只塞进
            // SearchText、文本 clip 的 Description 留 null → 首行(≤140字符)之外的正文永远进不了 FTS 索引，
            // 表现为「关键词写在第二行，SQLite 侧搜不到；但 Ditto 实时 LIKE 能搜到」两路径结果不一致，
            // 且 Ditto 未运行/DB 被占用时彻底搜不到。文件 clip 本就把 text 放 Description，故仅文本分支受害。
            Description = text,
        };
        return item;
    }

    private static string FirstLine(string text, int maxLen)
    {
        var idx = text.IndexOfAny(new[] { '\r', '\n' });
        var line = idx >= 0 ? text[..idx] : text;
        line = line.Trim();
        return TextTrim.Ellipsize(line, maxLen);
    }

    private static string ToFileUri(string path)
    {
        try
        {
            var normalized = path.Replace('\\', '/');
            return normalized.StartsWith("/") ? $"file://{normalized}" : $"file:///{normalized}";
        }
        catch
        {
            return string.Empty;
        }
    }
}