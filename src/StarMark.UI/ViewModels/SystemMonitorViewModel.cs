#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.Integrations.SystemMonitor;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 监控的一行。行对象常驻，刷新只改这几个属性（不重建集合 ⇒ 不会闪烁，与世界时钟同一做法）。
/// </summary>
public sealed partial class MonitorRow : ObservableObject
{
    public MonitorRow(MonitorMetric metric, string label)
    {
        Metric = metric;
        Label = label;
    }

    public MonitorMetric Metric { get; }

    public string Label { get; }

    /// <summary>主数字（"37%"、"↓13.3 KB/s"）。"没有数据"时是 "--"。</summary>
    [ObservableProperty] private string _value = "--";

    /// <summary>次要说明（"已用 6.5 GB / 15.7 GB"、"↑1.2 KB/s"）。</summary>
    [ObservableProperty] private string _detail = string.Empty;

    /// <summary>进度条 0..1；没有"占用率"含义的行（网速）留 0 并把条子隐藏。</summary>
    [ObservableProperty] private double _ratio;

    /// <summary>该行是否画进度条（CPU/内存画，网速不画——网速没有"满不满"的稳定语义）。</summary>
    public required bool ShowsBar { get; init; }

    /// <summary>非空＝这一项读不到，界面显示原因而不是留个 "--"（"没测到"与"测到 0"必须可分辨）。</summary>
    [ObservableProperty] private string? _error;

    public bool HasError => !string.IsNullOrEmpty(Error);

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
}

/// <summary>
/// 系统监控 ViewModel：把探针的原始读数按 D7 的口径折算成几行可显示的文本。
/// <para>
/// 本类<b>不做任何算术判据</b>——百分比、速率、滑动平均、采样间隔全在
/// <see cref="SystemMonitorPolicy"/>（那边有单测，这边只搬运）。
/// </para>
/// </summary>
public sealed partial class SystemMonitorViewModel : ObservableObject
{
    private readonly SystemMetricsProbe _probe = new();

    private readonly AverageWindow _cpuWindow;
    private readonly AverageWindow _inWindow;
    private readonly AverageWindow _outWindow;

    private CpuCounters? _previousCpu;
    private bool _hasCpuBaseline;
    private long _previousIn, _previousOut, _previousAtMs;
    private bool _hasNetworkBaseline;
    private int _countedAdapters;
    private bool _viaVirtualFallback;

    public ObservableCollection<MonitorRow> Rows { get; } = new();

    /// <summary>底部说明：走了虚拟接口退路、或第一次读数还在累计时，理由写在这里而不是塞进行里。</summary>
    [ObservableProperty] private string _note = string.Empty;

    /// <summary>当前勾选的指标（勾选面板据此回显）。</summary>
    [ObservableProperty] private bool _pickCpu = true;

    [ObservableProperty] private bool _pickMemory = true;
    [ObservableProperty] private bool _pickNetwork = true;

    public SystemMonitorViewModel(int windowMs)
    {
        _cpuWindow = new AverageWindow(windowMs);
        _inWindow = new AverageWindow(windowMs);
        _outWindow = new AverageWindow(windowMs);
    }

    /// <summary>装载勾选（null＝从没配过 ⇒ 三项全开）。</summary>
    public void Load(int? persistedWire)
    {
        var metrics = SystemMonitorPolicy.ResolveMetrics(persistedWire);
        PickCpu = metrics.HasFlag(MonitorMetric.Cpu);
        PickMemory = metrics.HasFlag(MonitorMetric.Memory);
        PickNetwork = metrics.HasFlag(MonitorMetric.Network);
        RebuildRows(metrics);
    }

    /// <summary>
    /// 勾选面板任一项变化后调用：重建行并给出要落盘的整数。
    /// "全取消"要存成 0 而不是回到默认——那是用户自己的决定，与"这台机器上从没配过"是两种形态。
    /// </summary>
    public int ApplyPicks()
    {
        var metrics = (PickCpu ? MonitorMetric.Cpu : MonitorMetric.None)
                    | (PickMemory ? MonitorMetric.Memory : MonitorMetric.None)
                    | (PickNetwork ? MonitorMetric.Network : MonitorMetric.None);
        RebuildRows(metrics);
        Note = string.Empty;   // 全取消由界面上的"未选择任何指标"那一行说，这里不重复一遍
        return SystemMonitorPolicy.ToWire(metrics);
    }

    private void RebuildRows(MonitorMetric metrics)
    {
        Rows.Clear();
        foreach (var (metric, label, _) in SystemMonitorPolicy.RowsFor(metrics))
            Rows.Add(new MonitorRow(metric, label)
            {
                ShowsBar = metric is MonitorMetric.Cpu or MonitorMetric.Memory,
            });
        OnPropertyChanged(nameof(IsEmpty));
    }

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// 一拍采样：读三条通道 → 折算 → 写回各行。
    /// <para>
    /// 第一拍没有"上一次读数"可减，CPU 与网速会先显示 "--"，下一拍起才有数——这是真实状态，
    /// 不给它编一个 0%。中间某一拍读数失败也不复位基线：累计计数在那段时间里照常增长，
    /// 跨过去的差值仍是那段时间的平均速率，把它清掉只会让界面多点一次 "--"。
    /// </para>
    /// </summary>
    public void Tick()
    {
        var nowMs = Environment.TickCount64;
        foreach (var row in Rows)
        {
            switch (row.Metric)
            {
                case MonitorMetric.Cpu: UpdateCpu(row, nowMs); break;
                case MonitorMetric.Memory: UpdateMemory(row); break;
                case MonitorMetric.Network: UpdateNetwork(row, nowMs); break;
            }
        }
    }

    private void UpdateCpu(MonitorRow row, long nowMs)
    {
        var reading = _probe.ReadCpuTicks();
        if (!reading.Ok)
        {
            Fail(row, reading.Error);
            return;
        }
        var current = new CpuCounters(reading.Idle, reading.Kernel, reading.User);
        var hadBaseline = _hasCpuBaseline;
        var percent = hadBaseline && _previousCpu is { } previous
            ? SystemMonitorPolicy.CpuPercent(previous, current)
            : null;
        _previousCpu = current;
        _hasCpuBaseline = true;
        if (percent is null)
        {
            // 第一拍（还没有可减的基线）不是故障：只说"正在累计"，别把"--"报成"读不到"
            if (!hadBaseline)
            {
                row.Value = "--";
                row.Ratio = 0;
                row.Error = string.Empty;
                row.Detail = "正在累计读数";
                return;
            }
            Fail(row, "内核时间计数这一拍不可用（计数器倒退或两次采样落在同一瞬间）");
            return;
        }
        // D7：显示 2 秒窗内的平均，而不是这一拍的瞬时值
        var average = _cpuWindow.Sample(nowMs, percent);
        row.Value = SystemMonitorPolicy.FormatPercent(average);
        row.Ratio = SystemMonitorPolicy.ToRatio(average);
        row.Error = string.Empty;
        row.Detail = average is null ? "正在累计读数" : string.Empty;
    }

    private void UpdateMemory(MonitorRow row)
    {
        var reading = _probe.ReadMemory();
        if (!reading.Ok)
        {
            Fail(row, reading.Error);
            return;
        }
        var percent = SystemMonitorPolicy.MemoryPercent(reading.TotalBytes, reading.AvailableBytes);
        row.Value = SystemMonitorPolicy.FormatPercent(percent);
        row.Ratio = SystemMonitorPolicy.ToRatio(percent);
        row.Detail = $"已用 {FileSizeText.Human(reading.TotalBytes - reading.AvailableBytes)} / {FileSizeText.Human(reading.TotalBytes)}";
        row.Error = percent is null ? "系统报告的内存数值不可用" : string.Empty;
    }

    private void UpdateNetwork(MonitorRow row, long nowMs)
    {
        var reading = _probe.ReadNetwork();
        if (!reading.Ok)
        {
            Fail(row, reading.Error);
            Note = string.Empty;
            return;
        }
        var totals = SystemMonitorPolicy.SumCountedAdapters(reading.Adapters);
        _countedAdapters = totals.CountedAdapters;
        _viaVirtualFallback = totals.CountedViaVirtualFallback;

        if (totals.CountedAdapters == 0)
        {
            row.Value = "--";
            row.Detail = string.Empty;
            row.Error = string.Empty;
            Note = "没有已连接的网卡，网速暂无数据";
            return;
        }

        var hadBaseline = _hasNetworkBaseline && nowMs > _previousAtMs;
        double? inbound = null, outbound = null;
        if (hadBaseline)
        {
            var elapsedSeconds = (nowMs - _previousAtMs) / 1000.0;
            inbound = SystemMonitorPolicy.RatePerSecond(_previousIn, totals.InBytes, elapsedSeconds);
            outbound = SystemMonitorPolicy.RatePerSecond(_previousOut, totals.OutBytes, elapsedSeconds);
        }
        _previousIn = totals.InBytes;
        _previousOut = totals.OutBytes;
        _previousAtMs = nowMs;
        _hasNetworkBaseline = true;

        var inAverage = _inWindow.Sample(nowMs, inbound);
        var outAverage = _outWindow.Sample(nowMs, outbound);
        row.Value = $"↓{SystemMonitorPolicy.FormatRate(inAverage)}";
        row.Detail = $"↑{SystemMonitorPolicy.FormatRate(outAverage)}";
        row.Error = string.Empty;

        var notes = new List<string>();
        if (_viaVirtualFallback) notes.Add($"没有真硬件网卡在线，按 {_countedAdapters} 块虚拟接口统计");
        if (!hadBaseline) notes.Add("正在累计读数");
        else if (inbound is null) notes.Add("网卡计数发生重置，这一拍没有可信速率");
        Note = string.Join("；", notes);
    }

    private static void Fail(MonitorRow row, string? reason)
    {
        row.Value = "--";
        row.Ratio = 0;
        row.Detail = string.Empty;
        row.Error = string.IsNullOrWhiteSpace(reason) ? "读数不可用" : reason;
    }
}
