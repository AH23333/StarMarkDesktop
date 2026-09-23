#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>添加下拉里的一项候选时区。</summary>
public sealed record TimeZoneOption(string ZoneId, string Name);

/// <summary>世界时钟的一行。行对象常驻，秒级刷新只改这几个属性（不重建集合 ⇒ 不会闪烁）。</summary>
public sealed partial class WorldClockRow : ObservableObject
{
    public WorldClockRow(string zoneId, string name)
    {
        ZoneId = zoneId;
        Name = name;
    }

    public string ZoneId { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _timeText = "--:--:--";
    [ObservableProperty] private string _offsetText = string.Empty;
    [ObservableProperty] private string _dayTag = string.Empty;

    /// <summary>非空表示这个时区在本机解析不出来（精简系统/手改配置）；界面直接显示原因而不是留空。</summary>
    [ObservableProperty] private string? _error;

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Error 是 HasError 的唯一来源，变了必须连带通知，否则错误行与时间行会同时显示。</summary>
    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
}

/// <summary>
/// 世界时钟 ViewModel：点位集合与每秒刷新。
/// 时间计算全部委托 <see cref="TimeZoneInfo"/>，跨日/夏令时由框架按"该 UTC 时刻"取偏移；
/// 本类只负责集合的增删去重与显示文本。
/// </summary>
public sealed partial class WorldClockViewModel : ObservableObject
{
    [ObservableProperty] private string _emptyHint = string.Empty;

    public ObservableCollection<WorldClockRow> Rows { get; } = new();

    /// <summary>候选时区（系统全量，去掉已添加的）。每次增删后重算——列表只在打开下拉时被看到，代价无关紧要。</summary>
    public List<TimeZoneOption> AddableZones { get; private set; } = new();

    public bool IsFull => Rows.Count >= WorldClockPolicy.MaxCities;

    public WorldClockViewModel() => RefreshCandidates();

    /// <summary>
    /// 装载点位。<paramref name="persisted"/> 为 <b>null</b> 表示"这台机器上从没配置过"⇒ 给默认四城；
    /// 为空集合表示"用户自己删光了"⇒ 保持空（判据是 null/非 null，不是 Count——这是快捷启动那轮踩过的）。
    /// </summary>
    public void Load(IReadOnlyList<WorldClockCity>? persisted)
    {
        Rows.Clear();
        var source = persisted ?? WorldClockPolicy.DefaultCities;
        foreach (var city in source) AddCore(city.ZoneId, city.Name);
        RefreshCandidates();
        UpdateEmptyHint();
        Tick();
    }

    /// <summary>添加一个点位；重复或超上限时返回 false（界面据此起提示，不静默忽略）。</summary>
    public bool Add(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) return false;
        if (!WorldClockPolicy.CanAdd(
                Rows.Select(r => new WorldClockCity(r.ZoneId, r.Name)).ToList(), zoneId)) return false;
        var zone = WorldClockPolicy.TryResolve(zoneId);
        AddCore(zoneId, zone is null ? zoneId : WorldClockPolicy.ShortName(zone.DisplayName));
        RefreshCandidates();
        UpdateEmptyHint();
        Tick();
        return true;
    }

    private WorldClockRow? AddCore(string zoneId, string name)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) return null;
        if (Rows.Any(r => string.Equals(r.ZoneId, zoneId, StringComparison.OrdinalIgnoreCase))) return null;
        var row = new WorldClockRow(zoneId, name);
        Rows.Add(row);
        return row;
    }

    public void Remove(WorldClockRow? row)
    {
        if (row is null) return;
        Rows.Remove(row);
        RefreshCandidates();
        UpdateEmptyHint();
    }

    public IReadOnlyList<WorldClockCity> ToPersisted() =>
        Rows.Select(r => new WorldClockCity(r.ZoneId, r.Name)).ToList();

    /// <summary>每秒一次：按各自时区取当前时刻，并算出与本机相差几日。</summary>
    public void Tick()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc.UtcDateTime, TimeZoneInfo.Local));
        foreach (var row in Rows)
        {
            var zone = WorldClockPolicy.TryResolve(row.ZoneId);
            if (zone is null)
            {
                row.Error = $"本机没有这个时区：{row.ZoneId}";
                row.TimeText = "--:--:--";
                row.OffsetText = string.Empty;
                row.DayTag = string.Empty;
                continue;
            }
            var at = nowUtc.ToOffset(zone.GetUtcOffset(nowUtc));
            row.Error = null;
            row.TimeText = at.ToString("HH:mm:ss");
            row.OffsetText = WorldClockPolicy.OffsetLabel(at.Offset);
            row.DayTag = WorldClockPolicy.DayLabel(
                DateOnly.FromDateTime(at.DateTime).DayNumber - localToday.DayNumber);
        }
    }

    private void RefreshCandidates()
    {
        var taken = Rows.Select(r => r.ZoneId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<TimeZoneOption>();
        foreach (var z in TimeZoneInfo.GetSystemTimeZones())
        {
            if (taken.Contains(z.Id)) continue;
            list.Add(new TimeZoneOption(z.Id, WorldClockPolicy.ShortName(z.DisplayName)));
        }
        AddableZones = list;
        OnPropertyChanged(nameof(AddableZones));
    }

    private void UpdateEmptyHint() => EmptyHint = Rows.Count == 0
        ? "还没有点位：用上方下拉添加时区。"
        : string.Empty;
}
