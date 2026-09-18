#nullable enable
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarMark.Core.Widgets;

/// <summary>桌面组件类型（对标 DeskBox：快捷启动/待办/随记/时钟/搜索 + 差异化条目格）。</summary>
public enum WidgetKind
{
    QuickLaunch = 0, // 快捷启动格（收藏入口 + 置顶条目）
    Todo = 1,        // 待办
    QuickNote = 2,   // 随记
    Clock = 3,       // 时钟/日期
    Search = 4,      // 快捷搜索（唤起主窗口并搜索）
    TagGrid = 5,     // 标签格：某标签条目常驻桌面（差异化护城河）
    SearchResults = 6, // 搜索结果格：钉一条查询常驻（差异化护城河）
    Activity = 7,    // 最近活动格：按 updated_at 展示最近条目
    Pinned = 8,      // 置顶条目格：pinned=1 的条目
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

/// <summary>单个组件实例配置（v3 起按实例管理：同一类型可重复添加多个）。</summary>
public sealed class WidgetInstanceConfig
{
    /// <summary>实例唯一 ID（区分同类型的多个组件）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public WidgetKind Kind { get; set; }

    public double X { get; set; } = 120;
    public double Y { get; set; } = 120;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 360;

    /// <summary>窗口是否常驻最顶层（WS_EX_TOPMOST / HWND_TOPMOST）。</summary>
    public bool Topmost { get; set; }

    /// <summary>
    /// 外壳呈现模式：标准 / 收起为胶囊 / 隐藏外壳（Phase B 胶囊模式）。
    /// 旧实例缺此字段反序列化为 Standard，不影响其它类型。
    /// </summary>
    public WidgetChromeMode ChromeMode { get; set; } = WidgetChromeMode.Standard;

    // ── 每显示器拓扑布局（Phase B）：位置以 DIP 存为「所在显示器工作区左上角」的偏移，
    //    配合 MonitorDevice 在恢复时按该显示器当前 DPI 重新换算物理像素；
    //    MonitorDevice 为空（旧实例/降级路径）时回退物理像素 X/Y/Width/Height 并做越界回收。
    //    全部有默认值，旧 instances.json 反序列化缺字段零影响。 ──

    /// <summary>组件所在显示器的稳定设备名（如 \\.\DISPLAY1，来自 Win32 MONITORINFOEX.szDevice）。空串表示未记录。</summary>
    public string MonitorDevice { get; set; } = string.Empty;

    /// <summary>组件左上角相对所在显示器工作区左上角的 DIP 偏移（X）。</summary>
    public double MonitorLeft { get; set; }

    /// <summary>组件左上角相对所在显示器工作区左上角的 DIP 偏移（Y）。</summary>
    public double MonitorTop { get; set; }

    /// <summary>组件宽度（DIP）。</summary>
    public double MonitorWidth { get; set; }

    /// <summary>组件高度（DIP）。</summary>
    public double MonitorHeight { get; set; }

    /// <summary>该实例自己的待办内容（多实例互不干扰）。</summary>
    public List<TodoItem> Todos { get; set; } = new();

    /// <summary>该实例自己的随记内容。</summary>
    public List<QuickNoteItem> Notes { get; set; } = new();

    /// <summary>该实例自己的快捷入口（置顶条目来自数据库，仍共享）。</summary>
    public List<LinkItem> Links { get; set; } = new();

    // ── 差异化条目格（TagGrid / SearchResults）的每实例查询配置 ──
    // 均为可空、向后兼容：旧实例缺这些字段时反序列化为 null，不影响其它类型。

    /// <summary>标签格所钉的标签名（TagGrid 用）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GridTag { get; set; }

    /// <summary>搜索结果格所钉的关键词（SearchResults 用）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GridQuery { get; set; }

    /// <summary>搜索结果格所钉的标签过滤（AND 语义，SearchResults 用）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? GridTags { get; set; }

    /// <summary>每实例外观覆盖（材质/颜色/边框/圆角/文本缩放）。为 null 时本实例沿用全局外观设置。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetAppearanceOverride? Appearance { get; set; }
}

/// <summary>每实例外观覆盖（任一字段为 null 即回退到全局设置）。</summary>
public sealed class WidgetAppearanceOverride
{
    /// <summary>材质覆盖（亚克力/云母/不透明）。null = 用全局。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StarMark.Abstractions.WidgetBackdropKind? Backdrop { get; set; }

    /// <summary>背景色（#RRGGBB 或 #AARRGGBB）。null = 用全局材质表面（透明/实色随主题）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackgroundColor { get; set; }

    /// <summary>前景（文本）色（#RRGGBB）。null = 用主题默认。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ForegroundColor { get; set; }

    /// <summary>边框色（#RRGGBB）。null = 用主题默认（1px 分隔色）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BorderColor { get; set; }

    /// <summary>边框粗细（物理 px）。null = 用全局（1）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BorderThickness { get; set; }

    /// <summary>圆角半径（物理 px）。null = 用全局（8）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CornerRadius { get; set; }

    /// <summary>文本缩放（1.0 = 100%）。null = 用全局（1.0）。范围建议 0.7–1.5。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TextScale { get; set; }
}

/// <summary>组件存储根。</summary>
public sealed class WidgetStoreData
{
    public int Version { get; set; } = 3;

    /// <summary>全部组件实例（v3 起唯一真源；同一类型可多个）。</summary>
    public List<WidgetInstanceConfig> Instances { get; set; } = new();

    /// <summary>用户保存的组件布局方案（同一时刻只套用一套；切换即隐藏不属于该布局的实例）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WidgetLayout>? Layouts { get; set; }

    /// <summary>
    /// 默认布局（上次「选中/应用」的布局方案 Id）。
    /// 用户每次应用某套布局即把它记为此字段；启动恢复时若此字段存在则自动套用该布局，
    /// 使「最后一次选择的布局」成为组件默认状态。为 null 时回退为逐个显示全部实例。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultLayoutId { get; set; }

    /// <summary>本地条目（待办/随记）是否已迁移进统一 items 表。为 true 时迁移跳过（幂等）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LocalItemsMigrated { get; set; }

    // ── v2（每类型一个实例）遗留字段，仅用于迁移，迁移后清空不再落盘 ──

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WidgetKind>? Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, WidgetConfig>? WindowConfigs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TodoItem>? Todos { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<QuickNoteItem>? Notes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LinkItem>? Links { get; set; }

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
        return Path.Combine(appData, StarMark.Abstractions.AppConstants.AppName, "widgets.json");
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
        if (data.WindowConfigs != null && data.WindowConfigs.TryGetValue(kind.ToString(), out var c) && c != null)
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
            if (showOnStartup)
            {
                data.Enabled ??= new List<WidgetKind>();
                if (data.Enabled.Count == 0)
                {
                    data.Enabled.AddRange(AllKinds);
                    if (data.LegacyConfig is { } legacy)
                    {
                        legacy.LegacyShowOnStartupInConfig = null;
                        data.WindowConfigs ??= new Dictionary<string, WidgetConfig>();
                        data.WindowConfigs[WidgetKind.QuickLaunch.ToString()] = legacy;
                    }
                }
            }
            data.LegacyConfig = null;
            data.LegacyShowOnStartup = null;
        }

        // v2 → v3 迁移：每类型一个实例 → 多实例（按实例管理，同一类型可重复添加）。
        // 把 v2 的 Enabled + WindowConfigs + 全局 Todos/Notes/Links 归并为若干实例；
        // 全局内容归并到该类型的首个实例，其余新实例各自为空（多组件内容互不覆盖）。
        if (data.Version < 3)
        {
            data.Instances ??= new List<WidgetInstanceConfig>();
            if (data.Enabled is { Count: > 0 })
            {
                foreach (var kind in data.Enabled)
                {
                    var cfg = (data.WindowConfigs != null
                               && data.WindowConfigs.TryGetValue(kind.ToString(), out var w)
                               && w is not null)
                        ? w
                        : new WidgetConfig();
                    var inst = new WidgetInstanceConfig
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Kind = kind,
                        X = cfg.X, Y = cfg.Y, Width = cfg.Width, Height = cfg.Height,
                        Topmost = cfg.Topmost,
                    };
                    if (kind == WidgetKind.Todo && data.Todos is not null) inst.Todos = data.Todos;
                    else if (kind == WidgetKind.QuickNote && data.Notes is not null) inst.Notes = data.Notes;
                    else if (kind == WidgetKind.QuickLaunch && data.Links is not null) inst.Links = data.Links;
                    data.Instances.Add(inst);
                }
            }
            data.Enabled = null;
            data.WindowConfigs = null;
            data.Todos = null;
            data.Notes = null;
            data.Links = null;
        }

        data.Version = 3;
        data.Instances ??= new List<WidgetInstanceConfig>();
        data.Layouts = WidgetLayoutCollection.Normalize(data.Layouts);

        // 每个实例的内容分别规范化（排序/限量），避免多实例数据相互覆盖。
        foreach (var inst in data.Instances)
        {
            inst.Todos = inst.Todos ?? new List<TodoItem>();
            inst.Notes = inst.Notes ?? new List<QuickNoteItem>();
            inst.Links = inst.Links ?? new List<LinkItem>();

            inst.Todos = inst.Todos
                .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Text))
                .OrderBy(t => t.Done)                    // 未完成(false)在前，已完成在后
                .ThenByDescending(t => t.CreatedAt)
                .Take(200)
                .ToList();
            inst.Notes = inst.Notes
                .Where(n => n is not null && !string.IsNullOrWhiteSpace(n.Text))
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .ToList();
            inst.Links = inst.Links
                .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Uri))
                .OrderByDescending(l => l.CreatedAt)
                .Take(100)
                .ToList();
        }
        return data;
    }

    public static long NewId() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000 + Random.Shared.Next(0, 999);

    /// <summary>取某实例配置（按 Id）。</summary>
    public WidgetInstanceConfig? FindInstance(string id)
    {
        lock (_gate)
        {
            var data = Load();
            return data.Instances.FirstOrDefault(i => i.Id == id);
        }
    }

    /// <summary>在已加载的数据中按 Id 取或新建一个实例（供视图模型就地改集合后回写）。</summary>
    public static WidgetInstanceConfig GetOrAddInstance(WidgetStoreData data, string id, WidgetKind kind)
    {
        var inst = data.Instances.FirstOrDefault(i => i.Id == id);
        if (inst is null)
        {
            inst = new WidgetInstanceConfig { Id = id, Kind = kind };
            data.Instances.Add(inst);
        }
        return inst;
    }

    // ───────────────────────── 布局方案 ─────────────────────────

    /// <summary>全部已保存布局（按名称排序，名称保证唯一）。</summary>
    public IReadOnlyList<WidgetLayout> GetLayouts()
    {
        lock (_gate) return Load().Layouts ?? new List<WidgetLayout>();
    }

    /// <summary>按 Id 查找布局。</summary>
    public WidgetLayout? FindLayout(string id)
    {
        lock (_gate) return Load().Layouts?.FirstOrDefault(l => l.Id == id);
    }

    /// <summary>新增或覆盖同名布局（按 Id 判定）；返回去重后的最终名称。</summary>
    public string SaveLayout(WidgetLayout layout)
    {
        lock (_gate)
        {
            var data = Load();
            var layouts = data.Layouts ??= new List<WidgetLayout>();
            var existing = layouts.FirstOrDefault(l => l.Id == layout.Id);
            if (existing is not null) layouts.Remove(existing);
            layouts.Add(layout);
            this.Save(data);
            return layout.Name;
        }
    }

    /// <summary>删除布局；返回是否删除成功。</summary>
    public bool DeleteLayout(string id)
    {
        lock (_gate)
        {
            var data = Load();
            if (data.Layouts is not { Count: > 0 } layouts) return false;
            var target = layouts.FirstOrDefault(l => l.Id == id);
            if (target is null) return false;
            layouts.Remove(target);
            this.Save(data);
            return true;
        }
    }
}
