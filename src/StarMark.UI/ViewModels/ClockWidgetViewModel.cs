#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 时钟组件 ViewModel：承载时间 / 日期两行文本，外加<b>另外两行状态</b>——
/// 护眼休息提醒那一行（批次 WC-3）与闹钟待确认那一行（批次 RU）。
/// 由 ClockWidget 的 DispatcherQueueTimer 每秒调用 <see cref="Update"/> 刷新。
/// <para>
/// 两条状态行的口径是同一条：<b>引擎只有一个，这里只读它的状态、不自己记节拍</b>
/// （护眼读 <c>EyeRestService</c>，闹钟的判据读 <see cref="AlarmPolicy"/>）。
/// 组件与菜单/设置页各攒一套时间迟早对不上，而对不上的那次是用户先看见。
/// </para>
/// <para>
/// <b>闹钟那一行只在"有得确认"时出现</b>（用户裁决：气泡＋常驻待确认，不出声、不盖屏）。
/// 把整个排期印在桌面上会让时钟变成第二块日程表，而这条需求要的是"别让我错过那一次"。
/// </para>
/// </summary>
public sealed partial class ClockWidgetViewModel : ObservableObject
{
    [ObservableProperty]
    private string _timeText = DateTime.Now.ToString("HH:mm:ss");

    [ObservableProperty]
    private string _dateText = DateTime.Now.ToString("yyyy-MM-dd dddd");

    /// <summary>护眼那一行的文本；<see cref="HasRestLine"/> 为假时整行不占位。</summary>
    [ObservableProperty]
    private string _restText = string.Empty;

    /// <summary>只有"提醒开着"时才有这一行——默认关着的人不该在时钟上多看见一行空位。</summary>
    [ObservableProperty]
    private bool _hasRestLine;

    /// <summary>闹钟那一行（<c>闹钟 07:30 待确认</c>）；没有待确认的轮时时整行不占位。</summary>
    [ObservableProperty]
    private string _alarmText = string.Empty;

    [ObservableProperty]
    private bool _hasAlarmLine;

    private readonly List<AlarmItem> _alarms = new();

    /// <summary>某条闹钟到点了（组件据此发托盘气泡——<b>提醒的出口在 UI 侧，判据在 Core</b>）。</summary>
    public event Action<AlarmItem>? AlarmReached;

    /// <summary>当前这套闹钟（供右键菜单列条目）。<b>菜单每次打开重新取</b>，所以不存在"菜单里是旧的"。</summary>
    public IReadOnlyList<AlarmItem> Alarms => _alarms;

    /// <summary>满 8 条：菜单要能说"先删一条"，而不是按下没反应。</summary>
    public bool IsFull => _alarms.Count >= AlarmPolicy.MaxItems;

    /// <summary>用当前时间刷新三处文本；返回<b>这一拍刚弹掉的闹钟条数</b>（&gt;0 时组件必须落盘，否则重启再弹一次）。</summary>
    public int Update()
    {
        var now = DateTime.Now;
        TimeText = now.ToString("HH:mm:ss");
        DateText = now.ToString("yyyy-MM-dd dddd");
        RefreshRest(now);
        // 时钟显示的是本机时间，闹钟的判定也按本机墙上时钟走：这里把 DateTime 换成带本机偏移的时刻，
        // 于是 Core 那一侧只看"绝对时刻 + 那天的第几分钟"，不需要知道机器在哪个时区。
        return RefreshAlarms(new DateTimeOffset(now));
    }

    private void RefreshRest(DateTime now)
    {
        // 「休息中」优先：暗幕已经盖下来了，这一行再说"下一次 15:35"就是两套事实。
        if (StarMark.UI.Services.EyeRestService.IsResting)
        {
            RestText = "休息中";
            HasRestLine = true;
            return;
        }
        if (StarMark.UI.Services.EyeRestService.NextDueAt is not { } due)
        {
            RestText = string.Empty;
            HasRestLine = false;
            return;
        }
        // 到点时刻是"大约"：节拍判据在 Core，界面上不承诺精确到分。
        var local = due.ToLocalTime();
        var sameDay = local.Date == now.Date;
        RestText = $"休息 {(sameDay ? local.ToString("HH:mm") : local.ToString("M-d HH:mm"))}";
        HasRestLine = true;
    }

    /// <summary>
    /// 到点判定 + 那一行的措辞。<b>先判后写</b>：刚弹掉的那一条立刻进待确认，
    /// 于是气泡与桌面上那行说的是同一次，不会出现"弹了但桌上没痕迹"或反过来。
    /// </summary>
    private int RefreshAlarms(DateTimeOffset now)
    {
        var notified = 0;
        foreach (var item in _alarms)
        {
            if (!AlarmPolicy.ShouldNotify(item, now)) continue;
            var at = AlarmPolicy.TodayOccurrence(item, now)!.Value;
            AlarmPolicy.MarkNotified(item, at);
            notified++;
            AlarmReached?.Invoke(item);
        }
        AlarmText = AlarmPolicy.PendingLine(_alarms, now);
        HasAlarmLine = AlarmText.Length > 0;
        return notified;
    }

    // ────────── 以下由右键菜单调用（每一个改动都要跟着落盘，见 ClockWidget）──────────

    /// <summary>从存档灌入（<b>逐条 Clone</b>：菜单改的是这一份，存档读回来的那份不该跟着一起变）。</summary>
    public void Load(IEnumerable<AlarmItem>? items)
    {
        _alarms.Clear();
        if (items is null) return;
        foreach (var item in items)
            if (item is not null) _alarms.Add(item.Clone());
    }

    /// <summary>交给持久化的副本（同样逐条 Clone，避免存出去的东西与在用的共享对象）。</summary>
    public IReadOnlyList<AlarmItem> ToPersisted() => _alarms.Select(item => item.Clone()).ToList();

    public void Add(int minute, string label, AlarmDays days)
    {
        if (IsFull) return;                                  // 上限由菜单先问，这里只兜住"绕过菜单"的路
        _alarms.Add(new AlarmItem
        {
            Id = WidgetStorage.NewId(),
            MinuteOfDay = minute,
            Label = label,
            Days = days,
            Enabled = true,
        });
        RefreshAlarms(DateTimeOffset.Now);
    }

    public void Remove(long id)
    {
        if (_alarms.RemoveAll(item => item.Id == id) == 0) return;   // 找不到＝菜单打开之后别处删掉了，不做补偿
        RefreshAlarms(DateTimeOffset.Now);
    }

    /// <summary>开／关这一条（关掉的闹钟不删：留着那条才谈得上"再开回来"）。</summary>
    public void SetEnabled(long id, bool enabled) => Mutate(id, item => item.Enabled = enabled);

    public void SetDays(long id, AlarmDays days) => Mutate(id, item => item.Days = days);

    /// <summary>改钟点与名字。改完把待确认清掉：<b>改了时间还挂着"07:30 待确认"是最容易读成两件事的形状</b>。</summary>
    public void SetTime(long id, int minute, string label)
        => Mutate(id, item =>
        {
            item.MinuteOfDay = minute;
            item.Label = label;
            item.PendingSince = null;
        });

    /// <summary>桌面那一行的「停止」：确认掉所有还在等的轮次（不改动开关）。</summary>
    public void ConfirmPendingAlarms()
    {
        foreach (var item in _alarms)
            AlarmPolicy.Confirm(item);
        RefreshAlarms(DateTimeOffset.Now);
    }

    /// <summary>
    /// 试响：<b>只发那一条的气泡</b>——不写待确认、不动开关、不落盘。
    /// 没有这条出口，"托盘到底弹不弹得出来"只能等一个真实的早晨才能验（批次 WA 立下的口径）。
    /// </summary>
    public void TestFire(long id)
    {
        var item = _alarms.FirstOrDefault(each => each.Id == id);
        if (item is null) return;
        AlarmReached?.Invoke(item);
    }

    private void Mutate(long id, Action<AlarmItem> edit)
    {
        var item = _alarms.FirstOrDefault(each => each.Id == id);
        if (item is null) return;
        edit(item);
        RefreshAlarms(DateTimeOffset.Now);
    }
}
