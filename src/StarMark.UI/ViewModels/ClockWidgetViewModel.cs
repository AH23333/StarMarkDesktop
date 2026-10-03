#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions;
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
/// <b>闹钟那一行只在"有得确认"时出现</b>（用户裁决：提示卡＋常驻待确认，不出声、不盖屏）。
/// 把整个排期印在桌面上会让时钟变成第二块日程表，而这条需求要的是"别让我错过那一次"。
/// </para>
/// </summary>
public sealed partial class ClockWidgetViewModel : ObservableObject
{
    [ObservableProperty]
    private string _timeText = DateTimeText.Clock(DateTime.Now, withSeconds: true);

    [ObservableProperty]
    private string _dateText = DateTimeText.DayWithWeekday(DateTime.Now);

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

    /// <summary>某条闹钟到点了（组件据此发右下角提示卡——<b>提醒的出口在 UI 侧，判据在 Core</b>）。</summary>
    public event Action<AlarmItem>? AlarmReached;

    /// <summary>
    /// 时钟表面那一排闹钟的内容（<b>不是每秒重算的那份</b>：只有定义/开关/待确认真变了才换，见 <see cref="AlarmFaceChanged"/>）。
    /// </summary>
    public IReadOnlyList<AlarmPolicy.AlarmFaceRow> FaceRows { get; private set; } = Array.Empty<AlarmPolicy.AlarmFaceRow>();

    /// <summary>那一排的内容签名，住在 Core（<see cref="AlarmPolicy.FaceSignature"/>），这里只保存上一次那一份。</summary>
    public string FaceSignature { get; private set; } = string.Empty;

    /// <summary>
    /// 那一排<b>确实变了</b>（加了/删了/改了钟点或名字/开关翻了一下/挂上或收掉一次待确认）。
    /// <para>组件订阅它来重画，<b>不订阅每秒那趟</b>：时钟每秒都要刷时间文本，
    /// 若按那一趟重画就是每秒销毁重建一遍控件——把"改一行字"升级成"一次布局重排"，
    /// 而且正是他此刻盯着看的那个小窗。</para>
    /// </summary>
    public event Action? AlarmFaceChanged;


    /// <summary>当前这套闹钟（供右键菜单列条目）。<b>菜单每次打开重新取</b>，所以不存在"菜单里是旧的"。</summary>
    public IReadOnlyList<AlarmItem> Alarms => _alarms;

    /// <summary>满 8 条：菜单要能说"先删一条"，而不是按下没反应。</summary>
    public bool IsFull => _alarms.Count >= AlarmPolicy.MaxItems;

    /// <summary>用当前时间刷新三处文本；返回<b>这一拍刚弹掉的闹钟条数</b>（&gt;0 时组件必须落盘，否则重启再弹一次）。</summary>
    public int Update()
    {
        var now = DateTime.Now;
        TimeText = DateTimeText.Clock(now, withSeconds: true);
        DateText = DateTimeText.DayWithWeekday(now);
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
        RestText = $"休息 {(sameDay ? DateTimeText.Clock(local) : DateTimeText.MonthDayClock(local))}";
        HasRestLine = true;
    }

    /// <summary>
    /// 到点判定 + 那一行的措辞。<b>先判后写</b>：刚弹掉的那一条立刻进待确认，
    /// 于是提示卡与桌面上那行说的是同一次，不会出现"弹了但桌上没痕迹"或反过来。
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
        RebuildFace(now);
        return notified;
    }

    /// <summary>
    /// 算出时钟表面那一排，并且<b>只在它真的变了</b>才通知重画。
    /// <para>"变没变"的比较交给 Core 的签名（<see cref="AlarmPolicy.FaceSignature"/>），这里不判内容——
    /// 每秒那趟 <see cref="Update"/> 也要走这个方法的邻路，比较写在这儿就会顺手把钟点也掺进签名，
    /// 于是每秒重画一次控件。</para>
    /// </summary>
    private void RebuildFace(DateTimeOffset now)
    {
        var rows = AlarmPolicy.FaceRows(_alarms, now);
        var signature = AlarmPolicy.FaceSignature(rows);
        if (signature == FaceSignature) return;
        FaceRows = rows;
        FaceSignature = signature;
        AlarmFaceChanged?.Invoke();
    }

    // ────────── 以下由两个入口调用：时钟表面那一排 + 宿主右键菜单（每个改动都要落盘，见 ClockWidget）──────────

    /// <summary>从存档灌入（<b>逐条 Clone</b>：菜单改的是这一份，存档读回来的那份不该跟着一起变）。</summary>
    public void Load(IEnumerable<AlarmItem>? items)
    {
        _alarms.Clear();
        if (items is not null)
            foreach (var item in items)
                if (item is not null) _alarms.Add(item.Clone());
        RebuildFace(DateTimeOffset.Now);      // 不判到点：到点那一发由每秒那趟与宿主启动校准负责，重画排本身不用等
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

    /// <summary>
    /// 翻一下这一条。<b>表面那颗点走这条，而不是把界面上看到的 bool 传回来</b>：
    /// 界面上那份是建行那一刻的快照，而同一条目有两个入口能改它（表面＋右键菜单里那颗
    /// <c>ToggleMenuFlyoutItem</c>）——拿快照回写的症状是"我按下去没反应，再按一下才关"。
    /// 当前状态只从条目本身读。
    /// </summary>
    public void ToggleEnabled(long id) => Mutate(id, item => item.Enabled = !item.Enabled);

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
    /// 试响：<b>只发那一条提示卡</b>——不写待确认、不动开关、不落盘。
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
