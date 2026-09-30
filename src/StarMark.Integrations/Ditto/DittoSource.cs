#nullable enable
using StarMark.Abstractions;

namespace StarMark.Integrations.Ditto;

/// <summary>
/// Ditto 剪贴板源：直读 %APPDATA%\Ditto\DB\DittoDB.db（只读）。
/// 文本剪贴板 → Clipboard 条目；文件剪贴板（CF_HDROP）→ 取第一个文件做副标题与 Uri。
/// </summary>
public sealed class DittoSource : IItemSource
{
    private const int MaxSync = 500;

    private readonly string? _dbPath;

    /// <summary>探测过的位置清单——提示里要说清"找过哪里"，否则用户只知道"没有"却无从判断是自己没装、还是装在别处。</summary>
    private readonly IReadOnlyList<string> _probed;

    public string SourceId => ItemSources.Ditto;

    public string DisplayName => "Ditto 剪贴板";

    public bool IsAvailable => _dbPath != null && File.Exists(_dbPath);

    /// <summary>Ditto 是外部程序：没装就没有数据库，这一项本来就该是空的（与内置剪贴板历史是两回事）。</summary>
    public string? AvailabilityHint
        => $"没在本机找到 Ditto 的数据库（找过：{string.Join("；", _probed)}）。"
         + "没用过 Ditto 属正常，StarMark 自带的「剪贴板历史」不依赖它——在设置里开启后即可用";

    /// <param name="dbPath">显式指定库路径（测试/自定义安装位置）；为 null 时自动探测 <see cref="CandidateDbPaths"/>。</param>
    public DittoSource(string? dbPath = null)
    {
        if (dbPath is not null)
        {
            _dbPath = dbPath;
            _probed = new[] { dbPath };
            return;
        }
        _probed = CandidateDbPaths();
        _dbPath = FindDbPath(_probed);
    }

    /// <summary>
    /// Ditto 数据库的候选位置。<b>Ditto 没有单一约定路径</b>：安装版默认在
    /// <c>%APPDATA%\Ditto\DB\DittoDB.db</c>，但便携版把库放在 <c>Ditto.exe</c> 旁边（<c>DB\</c> 或同目录），
    /// 也有人把数据目录指到 <c>%LOCALAPPDATA%</c>。只探一条等于"在我这台机器上能跑"，
    /// 所以这里把已知形态都列出来，由 <see cref="FindDbPath"/> 取第一个真实存在的。
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
            list.Add(Path.Combine(root, "Ditto", "DB", "DittoDB.db"));
            list.Add(Path.Combine(root, "Ditto", "DittoDB.db"));
        }
        list.AddRange(PortableDbPaths());
        return list.Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>便携版：正在运行的 Ditto.exe 所在目录下的 <c>DB\DittoDB.db</c> 或 <c>DittoDB.db</c>。</summary>
    private static IEnumerable<string> PortableDbPaths()
    {
        foreach (var dir in RunningDittoDirectories())
        {
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
        // 且可能被 Ditto 自身持写锁）——留在调用线程上就是一次点击冻一次。
        => Task.Run(() =>
        {
            var clip = ReadAsync(r => r.ReadRecent(MaxSync), ct);
            if (clip.Count == 0) return (IReadOnlyList<Item>)Array.Empty<Item>();
            return clip.Select(Map).ToList();
        });

    public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
    {
        var matched = ReadAsync(r =>
            string.IsNullOrWhiteSpace(query)
                ? r.ReadRecent(Math.Max(1, filter.MaxResults))
                : r.Search(query, Math.Max(1, filter.MaxResults)), ct);
        return Task.FromResult<IReadOnlyList<Item>>(matched.Select(Map).ToList());
    }

    private List<DittoClip> ReadAsync(Func<DittoDatabaseReader, List<DittoClip>> query, CancellationToken ct)
    {
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
            Subtitle = isFiles ? clip.Files[0] : "Ditto 剪贴板",
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