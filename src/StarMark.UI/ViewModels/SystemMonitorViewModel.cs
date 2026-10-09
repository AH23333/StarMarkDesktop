#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using StarMark.Integrations.SystemMonitor;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 监控的一行。行对象常驻，刷新只改这几个属性（不重建集合 ⇒ 不会闪烁，与世界时钟同一做法）。
/// <para>
/// <b>这一行上会说话的那三格存的都是身份（键＋实参），不是上一次取好的词</b>（批次 WI-60／P-188）：
/// 行名那颗键来自 Core 的目录，<see cref="Detail"/> 与 <see cref="Error"/> 的键来自 VM 自己或那颗探针。
/// 取词只发生在 <see cref="Repaint"/> 里——它被两处调用：采样那一刻，以及换语言那一刻（宿主自己订阅）。
/// 从前这里存的是中文成品字，换语言后整列行名停在旧语言，而那台机器上唯一会改口的只有数字。
/// </para>
/// </summary>
public sealed partial class MonitorRow : ObservableObject
{
    public MonitorRow(MonitorMetric metric, string labelKey)
    {
        Metric = metric;
        LabelKey = labelKey;
        Repaint();
    }

    public MonitorMetric Metric { get; }

    /// <summary>行名那颗键（Core 的目录给的；⛔ 在这里取词＝语言被定死在重建集合那一刻）。</summary>
    public string LabelKey { get; }

    /// <summary>主数字（"37%"、"↓13.3 KB/s"）。"没有数据"时是 "--"。机器事实，⛔ 进表。</summary>
    [ObservableProperty] private string _value = "--";

    /// <summary>次要说明（"已用 6.5 GB / 15.7 GB"、"↑1.2 KB/s"）——这一刻的字。</summary>
    [ObservableProperty] private string _detail = string.Empty;

    /// <summary>进度条 0..1；没有"占用率"含义的行（网速）留 0 并把条子隐藏。</summary>
    [ObservableProperty] private double _ratio;

    /// <summary>非空＝这一项读不到，界面显示原因而不是留个 "--"（"没测到"与"测到 0"必须可分辨）。</summary>
    [ObservableProperty] private string? _error;

    private LocMessage? _detailMessage;
    private LocMessage? _errorMessage;

    /// <summary>该行是否画进度条（CPU/内存画，网速不画——网速没有"满不满"的稳定语义）。</summary>
    public required bool ShowsBar { get; init; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    /// <summary>次要说明：交键＋实参（"已用 {0} / {1}" 那两句的字节数就是实参），当场念一遍贴到属性上。</summary>
    internal void SetDetail(LocMessage? message)
    {
        _detailMessage = message;
        Detail = message?.Render() ?? string.Empty;
    }

    /// <summary>行内原因：同上，交的是身份。null＝没有原因（那一格收起）。</summary>
    internal void SetError(LocMessage? message)
    {
        _errorMessage = message;
        Error = message?.Render() ?? string.Empty;
    }

    /// <summary>
    /// 按<b>此刻</b>的语言把这三格重念一遍（数字不在这里——它是每次采样自己写的）。
    /// <para>⚠ 只读手上已有的身份：⛔ 在这条回调里采样、读设置档或重建集合（<c>Locale.Changed</c> 那八位主人
    /// 每一条都守的同一条界线＝坑表 #386 那一族）。</para>
    /// </summary>
    internal void Repaint()
    {
        Label = Locale.T(LabelKey);
        Detail = _detailMessage?.Render() ?? string.Empty;
        Error = _errorMessage?.Render() ?? string.Empty;
    }

    [ObservableProperty] private string _label = string.Empty;
}

/// <summary>
/// 系统监控 ViewModel：把探针的原始读数按 D7 的口径折算成几行可显示的文本。
/// <para>
/// 本类<b>不做任何算术判据</b>——百分比、速率、滑动平均、采样间隔全在
/// <see cref="SystemMonitorPolicy"/>（那边有单测，这边只搬运）。
/// </para>
/// <para>
/// 语言这一侧同理：这一层交出的是<b>键＋实参</b>（<see cref="_note"/> 与每行那两句），
/// 取词发生在 <see cref="RepaintWords"/>——它由采样与换语言两处调用，⛔ 在这里再打一次接口。
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

    /// <summary>那颗说明的<b>身份</b>（多条理由由 <c>widget.mon.note.join</c> 那颗骨架套起来，⛔ 在代码里拼成品字）。</summary>
    private LocMessage? _noteMessage;

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
        SetNote(null);   // 全取消由界面上的"未选择任何指标"那一行说，这里不重复一遍
        return SystemMonitorPolicy.ToWire(metrics);
    }

    private void RebuildRows(MonitorMetric metrics)
    {
        Rows.Clear();
        // 目录那一列给的是**键**（✅WI-55 那颗 `UnitTables` 同一口径）：行名跟着语言走，⛔ 定死在重建那一刻
        foreach (var (metric, labelKey, _) in SystemMonitorPolicy.RowsFor(metrics))
            Rows.Add(new MonitorRow(metric, labelKey)
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

    /// <summary>
    /// 换语言那一刻由宿主调用：把<b>已经拿到手的那几句</b>按新语言重念一遍。
    /// ⛔ 在这里采样（那会把"字改口"变成"这次读数失败了"），也⛔ 重建集合（会闪）。
    /// </summary>
    public void RepaintWords()
    {
        foreach (var row in Rows) row.Repaint();
        Note = _noteMessage?.Render() ?? string.Empty;
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
                row.SetError(null);
                row.SetDetail(LocMessage.Of("widget.mon.accruing"));
                return;
            }
            Fail(row, LocMessage.Of("widget.mon.cpu.counterUnusable"));
            return;
        }
        // D7：显示 2 秒窗内的平均，而不是这一拍的瞬时值
        var average = _cpuWindow.Sample(nowMs, percent);
        row.Value = SystemMonitorPolicy.FormatPercent(average);
        row.Ratio = SystemMonitorPolicy.ToRatio(average);
        row.SetError(null);
        row.SetDetail(average is null ? LocMessage.Of("widget.mon.accruing") : null);
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
        // 两个字节数交给槽：那一格念出来是"已用 6.5 GB / 15.7 GB"，而基数与舍入只有 FileSizeText 一份主人
        row.SetDetail(LocMessage.Of("widget.mon.mem.used",
            FileSizeText.Human(reading.TotalBytes - reading.AvailableBytes), FileSizeText.Human(reading.TotalBytes)));
        row.SetError(percent is null ? LocMessage.Of("widget.mon.mem.unavailable") : null);
    }

    private void UpdateNetwork(MonitorRow row, long nowMs)
    {
        var reading = _probe.ReadNetwork();
        if (!reading.Ok)
        {
            Fail(row, reading.Error);
            SetNote(null);
            return;
        }
        var totals = SystemMonitorPolicy.SumCountedAdapters(reading.Adapters);
        _countedAdapters = totals.CountedAdapters;
        _viaVirtualFallback = totals.CountedViaVirtualFallback;

        if (totals.CountedAdapters == 0)
        {
            row.Value = "--";
            row.SetDetail(null);
            row.SetError(null);
            SetNote(LocMessage.Of("widget.mon.net.noLink"));
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
        // 那两支箭头的去向⚠ 是两条通道不是一套机制：`Value` 是<b>读数</b>通道（"37%"、"--"，本来就⛔ 进表），
        //   所以"↓"留在这里；`Detail` 是<b>身份</b>通道（一切走键＋槽），所以"↑"住在 `widget.mon.upRate`。
        row.Value = $"↓{SystemMonitorPolicy.FormatRate(inAverage)}";
        row.SetDetail(LocMessage.Of("widget.mon.upRate", SystemMonitorPolicy.FormatRate(outAverage)));
        row.SetError(null);

        var notes = new List<LocMessage>();
        if (_viaVirtualFallback) notes.Add(LocMessage.Of("widget.mon.net.virtualFallback", _countedAdapters));
        if (!hadBaseline) notes.Add(LocMessage.Of("widget.mon.accruing"));
        else if (inbound is null) notes.Add(LocMessage.Of("widget.mon.net.counterReset"));
        // 连接符住在表里（与 ✅WI-58 那颗 `trending.notice.join` 同一口径）：在代码里 `string.Join("；", …)`
        //   那一刻，"两条理由并排"这句话的语言就被定死了，而每一半自己都是会改口的。
        SetNote(notes.Count switch
        {
            0 => null,
            1 => notes[0],
            _ => notes.Aggregate((head, next) => LocMessage.Of("widget.mon.note.join", head, next)),
        });
    }

    private static void Fail(MonitorRow row, LocMessage? reason)
    {
        row.Value = "--";
        row.Ratio = 0;
        row.SetDetail(null);
        // 探针没给原因时也要说一句：那一格留空会被读成"数值真是 0"（三条通道同一颗兜底键）
        row.SetError(reason ?? LocMessage.Of("widget.mon.unavailable"));
    }

    private void SetNote(LocMessage? message)
    {
        _noteMessage = message;
        Note = message?.Render() ?? string.Empty;
    }
}
