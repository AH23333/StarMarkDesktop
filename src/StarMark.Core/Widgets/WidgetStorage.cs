#nullable enable
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarMark.Core.Widgets;

/// <summary>桌面组件类型（对标 DeskBox：快捷启动/待办/随记/时钟/搜索）。</summary>
public enum WidgetKind
{
    QuickLaunch = 0, // 快捷启动格（收藏入口 + 置顶条目）
    Todo = 1,        // 待办
    QuickNote = 2,   // 随记
    Clock = 3,       // 时钟/日期
    Search = 4,      // 快捷搜索（唤起主窗口并搜索）
}

/// <summary>待办条目。</summary>
public sealed class TodoItem
{
    public long Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool Done { get; set; }
    public long CreatedAt { get; set; }
}

/// <summary>随记条目。</summary>
public sealed class QuickNoteItem
{
    public long Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
}

/// <summary>快捷入口条目（用户在快捷启动格内手动添加 / 拖入 / 从卡片发送）。</summary>
public sealed class LinkItem
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Uri { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
}

/// <summary>单个组件窗口配置（位置/尺寸为物理像素，置顶为窗口状态）。</summary>
public sealed class WidgetConfig
{
    public double X { get; set; } = 120;
    public double Y { get; set; } = 120;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 360;

    /// <summary>窗口是否常驻最顶层（WS_EX_TOPMOST / HWND_TOPMOST）。</summary>
    public bool Topmost { get; set; }

    /// <summary>仅 v1 迁移用：旧版开关嵌在 Config 内。</summary>
    [JsonPropertyName("ShowOnStartup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyShowOnStartupInConfig { get; set; }
}

/// <summary>组件存储根。</summary>
public sealed class WidgetStoreData
{
    public int Version { get; set; } = 2;

    /// <summary>启用的组件类型（设置页 / 托盘自由增减）。全新安装默认空——由用户主动添加（DeskBox 语义）。</summary>
    public List<WidgetKind> Enabled { get; set; } = new();

    /// <summary>每组件独立窗口配置（key = WidgetKind 名）。</summary>
    public Dictionary<string, WidgetConfig> WindowConfigs { get; set; } = new();

    public List<TodoItem> Todos { get; set; } = new();
    public List<QuickNoteItem> Notes { get; set; } = new();

    /// <summary>快捷启动格内的用户自定义条目。</summary>
    public List<LinkItem> Links { get; set; } = new();

    // ── v1（单面板 WidgetHostWindow 时代）遗留字段，仅用于迁移 ──

    [JsonPropertyName("Config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetConfig? LegacyConfig { get; set; }

    [JsonPropertyName("ShowOnStartup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyShowOnStartup { get; set; }
}

/// <summary>
/// 桌面组件持久化（对标 DeskBox 的 ResilientJsonStore/TodoWidgetStore 模式）：
/// 单个 JSON 文件，损坏时静默回退默认值，写入先落临时文件再原子替换。
/// 纯逻辑、无 UI 依赖，便于单元测试与 SmokeTest 无头验证。
/// </summary>
public sealed class WidgetStorage
{
    private readonly string _path;
    private readonly object _gate = new();

    public WidgetStorage(string? path = null)
    {
        _path = path ?? DefaultPath();
    }

    public string StorePath => _path;

    public static string DefaultPath()
    {
        // 与 settings.json 同目录（复刻 SettingsStore.ResolveSettingsPath 的路径约定：
        // STARMARK_SETTINGS_PATH > STARMARK_DB_PATH 同目录 > %APPDATA%\StarMark）
        var overridePath = Environment.GetEnvironmentVariable("STARMARK_SETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.Combine(Path.GetDirectoryName(overridePath) ?? ".", "widgets.json");

        var dbPath = Environment.GetEnvironmentVariable("STARMARK_DB_PATH");
        if (!string.IsNullOrWhiteSpace(dbPath))
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir)) return Path.Combine(dir, "widgets.json");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "StarMark", "widgets.json");
    }

    /// <summary>全部组件类型（设置页 / 托盘菜单遍历用），来源为 <see cref="WidgetRegistry"/>。</summary>
    public static IReadOnlyList<WidgetKind> AllKinds { get; } =
        WidgetRegistry.Default.GetWindowDescriptors().Select(d => d.Kind).ToArray();

    /// <summary>组件展示名（含图标）；未知类型回退为类型名。</summary>
    public static string KindTitle(WidgetKind kind) =>
        WidgetRegistry.Default.TryGet(kind, out var d) ? d.DisplayTitle : kind.ToString();

    /// <summary>取某组件的窗口配置（无记录时按类型给默认尺寸并级联摆放）。</summary>
    public WidgetConfig GetConfig(WidgetStoreData data, WidgetKind kind, int index)
    {
        if (data.WindowConfigs.TryGetValue(kind.ToString(), out var c) && c != null)
            return c;
        return new WidgetConfig
        {
            X = 160 + index * 40,
            Y = 140 + index * 40,
            Width = DefaultWidth(kind),
            Height = DefaultHeight(kind),
        };
    }

    /// <summary>默认尺寸（DIP 逻辑像素，窗口首次创建时按显示器 DPI 换算为物理像素）。</summary>
    public static int DefaultWidth(WidgetKind kind) =>
        WidgetRegistry.Default.TryGet(kind, out var d) ? d.DefaultWidth : 320;

    public static int DefaultHeight(WidgetKind kind) =>
        WidgetRegistry.Default.TryGet(kind, out var d) ? d.DefaultHeight : 400;

    public static bool IsResizable(WidgetKind kind) =>
        !WidgetRegistry.Default.TryGet(kind, out var d) || d.IsResizable;

    public WidgetStoreData Load()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path))
                {
                    var data = JsonSerializer.Deserialize<WidgetStoreData>(File.ReadAllText(_path));
                    return Normalize(data);
                }
            }
            catch
            {
                // 损坏文件 → 回退默认（与 DeskBox ResilientJsonStore 同策略）
            }
            return Normalize(null);
        }
    }

    public void Save(WidgetStoreData data)
    {
        lock (_gate)
        {
            var normalized = Normalize(data);
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    /// <summary>规范数据：去 null/空文本、排序（待办未完成在前按时间倒序，随记/入口按时间倒序）、限量、v1 迁移。</summary>
    public static WidgetStoreData Normalize(WidgetStoreData? data)
    {
        data ??= new WidgetStoreData();

        // v1 → v2 迁移：旧版是单面板（Config + ShowOnStartup，开关可能在根上也可能嵌在 Config 内）。
        // 旧用户若勾选了“启动时显示”，迁移为启用全部五种组件；旧面板位置交给快捷启动格。
        if (data.Version < 2)
        {
            var showOnStartup = data.LegacyShowOnStartup == true
                || data.LegacyConfig?.LegacyShowOnStartupInConfig == true;
            if (showOnStartup && data.Enabled.Count == 0)
            {
                data.Enabled.AddRange(AllKinds);
                if (data.LegacyConfig is { } legacy)
                {
                    legacy.LegacyShowOnStartupInConfig = null;
                    data.WindowConfigs[WidgetKind.QuickLaunch.ToString()] = legacy;
                }
            }
            data.LegacyConfig = null;
            data.LegacyShowOnStartup = null;
        }

        data.Version = 2;
        data.Enabled ??= new List<WidgetKind>();
        // 去重并保持枚举顺序稳定
        data.Enabled = data.Enabled.Distinct().OrderBy(k => (int)k).ToList();
        data.WindowConfigs ??= new Dictionary<string, WidgetConfig>();
        data.Todos ??= new List<TodoItem>();
        data.Notes ??= new List<QuickNoteItem>();
        data.Links ??= new List<LinkItem>();

        data.Todos = data.Todos
            .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Text))
            .OrderBy(t => t.Done)                    // 未完成(false)在前，已完成在后
            .ThenByDescending(t => t.CreatedAt)
            .Take(200)
            .ToList();
        data.Notes = data.Notes
            .Where(n => n is not null && !string.IsNullOrWhiteSpace(n.Text))
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToList();
        data.Links = data.Links
            .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Uri))
            .OrderByDescending(l => l.CreatedAt)
            .Take(100)
            .ToList();
        return data;
    }

    public static long NewId() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000 + Random.Shared.Next(0, 999);
}
