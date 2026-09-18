#nullable enable
using System.Linq;

namespace StarMark.Core.Widgets;

/// <summary>组件内容的成熟阶段。</summary>
public enum WidgetContentStage
{
    Implemented,
    Placeholder
}

/// <summary>组件对用户的可见性。</summary>
public enum WidgetContentAvailability
{
    Available,
    Planned
}

/// <summary>组件外壳（标题栏/胶囊）的呈现模式，移植自 DeskBox <c>WidgetChromeMode</c>。</summary>
public enum WidgetChromeMode
{
    /// <summary>标准标题栏 + 内容。</summary>
    Standard,
    /// <summary>收起为胶囊（Phase B 胶囊模式用）。</summary>
    Compact,
    /// <summary>完全隐藏外壳，仅留内容（透明浮层）。</summary>
    Hidden
}

/// <summary>
/// 组件类型的元数据描述。
/// 移植自 DeskBox <c>WidgetContentDescriptor</c>：把标题、图标、默认尺寸、可否缩放、
/// 成熟度、是否特性组件、外壳模式等"按类型分支"的信息集中到一个不可变记录里，
/// 使新增组件只需在 <see cref="WidgetRegistry"/> 的清单中加一行，
/// 而不必到各处 switch 里补分支。
/// </summary>
public sealed record WidgetDescriptor(
    WidgetKind Kind,
    string Title,
    string Glyph,
    int DefaultWidth,
    int DefaultHeight,
    bool IsResizable = true,
    WidgetContentStage Stage = WidgetContentStage.Implemented,
    WidgetContentAvailability Availability = WidgetContentAvailability.Available,
    bool CanCreateWindow = true,
    bool ShowInCreateEntry = true,
    // ── 对标 DeskBox WidgetContentDescriptor 的扩展维度（A-1 补齐）──
    /// <summary>特性组件：默认隐藏，需用户在设置页显式开启才可见（false 时默认可见）。</summary>
    bool IsFeatureWidget = false,
    /// <summary>是否有独立设置页。</summary>
    bool HasSettingsPage = false,
    /// <summary>默认外壳模式。</summary>
    WidgetChromeMode DefaultChromeMode = WidgetChromeMode.Standard,
    /// <summary>是否允许收起为胶囊（Phase B 胶囊模式前提）。</summary>
    bool CanHideChrome = true,
    /// <summary>是否允许使用叠加（overlay）外壳。</summary>
    bool CanUseOverlayChrome = true)
{
    /// <summary>带图标的展示名，用于托盘菜单 / 设置页。</summary>
    public string DisplayTitle => $"{Glyph} {Title}";

    public bool HasImplementedContent => Stage == WidgetContentStage.Implemented;
    public bool IsPlaceholderOnly => Stage == WidgetContentStage.Placeholder;
    public bool IsAvailable => Availability == WidgetContentAvailability.Available;
    public bool IsPlanned => Availability == WidgetContentAvailability.Planned;
}

/// <summary>
/// 组件类型注册表：整个应用对"有哪些组件"的唯一事实来源。
/// 移植自 DeskBox <c>Services/WidgetRegistry.cs</c>。
/// 已规划但未实现的类型可以先注册进来（<c>CanCreateWindow: false</c>），
/// 使其配置可持久化，但不会被创建成窗口。
/// </summary>
public sealed class WidgetRegistry
{
    public static WidgetRegistry Default { get; } = new(CreateDefaults());

    private readonly IReadOnlyDictionary<WidgetKind, WidgetDescriptor> _map;
    private readonly IReadOnlyList<WidgetDescriptor> _list;

    public WidgetRegistry(IEnumerable<WidgetDescriptor> descriptors)
    {
        _list = descriptors.ToArray();
        _map = _list.ToDictionary(descriptor => descriptor.Kind);
    }

    /// <summary>全部已注册描述符，声明顺序有意义（设置页与托盘菜单按此顺序展示）。</summary>
    public IReadOnlyList<WidgetDescriptor> All => _list;

    public bool IsKnown(WidgetKind kind) => _map.ContainsKey(kind);

    /// <summary>取描述符；未注册的类型直接抛异常，避免静默走到默认分支。</summary>
    public WidgetDescriptor Get(WidgetKind kind) =>
        _map.TryGetValue(kind, out var descriptor)
            ? descriptor
            : throw new NotSupportedException($"组件类型 '{kind}' 未注册描述符。");

    public bool TryGet(WidgetKind kind, out WidgetDescriptor descriptor)
    {
        if (_map.TryGetValue(kind, out var found))
        {
            descriptor = found;
            return true;
        }

        descriptor = null!;
        return false;
    }

    public bool CanCreateWindow(WidgetKind kind) => TryGet(kind, out var d) && d.CanCreateWindow;

    /// <summary>可在"新建组件"入口中列出的类型。</summary>
    public IReadOnlyList<WidgetDescriptor> GetCreateEntryDescriptors() =>
        _list.Where(d => d.ShowInCreateEntry).ToArray();

    /// <summary>可真正创建窗口的类型（已实现且可用）。</summary>
    public IReadOnlyList<WidgetDescriptor> GetWindowDescriptors() =>
        _list.Where(d => d.CanCreateWindow && d.HasImplementedContent && d.IsAvailable).ToArray();

    // 单一事实来源：新增组件只在这里加一行。
    private static IEnumerable<WidgetDescriptor> CreateDefaults()
    {
        yield return new WidgetDescriptor(
            WidgetKind.QuickLaunch, "快捷启动", "★", 320, 460);

        yield return new WidgetDescriptor(
            WidgetKind.Todo, "待办", "✅", 300, 380);

        yield return new WidgetDescriptor(
            WidgetKind.QuickNote, "随记", "📝", 300, 340);

        yield return new WidgetDescriptor(
            // 时钟同样可以缩放（字号随窗口自适应，见 ClockWidget.ApplyAdaptiveFontSize）
            WidgetKind.Clock, "时钟", "🕒", 240, 170, IsResizable: true);

        yield return new WidgetDescriptor(
            WidgetKind.Search, "快捷搜索", "🔍", 300, 130);

        // ── 差异化条目格（StarMark 护城河：全部基于统一 items 表，DeskBox 结构上做不到）──
        yield return new WidgetDescriptor(
            WidgetKind.TagGrid, "标签格", "🏷️", 300, 400, IsFeatureWidget: true);

        yield return new WidgetDescriptor(
            WidgetKind.SearchResults, "搜索结果格", "📌", 320, 420, IsFeatureWidget: true);

        yield return new WidgetDescriptor(
            WidgetKind.Activity, "最近活动格", "🕘", 300, 400, IsFeatureWidget: true);

        yield return new WidgetDescriptor(
            WidgetKind.Pinned, "置顶条目格", "⏫", 300, 380, IsFeatureWidget: true);

        // ── Phase C：今日速览（Glance）──
        // 节日 / 农历算法照搬 DeskBox 的 GlanceFestivalService（基于 .NET 内置 ChineseLunisolarCalendar）。
        yield return new WidgetDescriptor(
            WidgetKind.Glance, "今日速览", "📅", 300, 360, IsFeatureWidget: true);

        // ── Phase C：天气 ──
        // 数据源 Open-Meteo（免费、无需 API Key）；WMO 码映射照搬 DeskBox 的 WeatherCodeMapper。
        yield return new WidgetDescriptor(
            WidgetKind.Weather, "天气", "🌤️", 300, 320, IsFeatureWidget: true);

        // ── Phase C：音乐 ──
        // 走 Windows SMTC，读取系统当前播放会话并控制播放，不需要对接任何第三方播放器。
        yield return new WidgetDescriptor(
            WidgetKind.Music, "音乐", "🎵", 300, 240, IsFeatureWidget: true);
    }
}
