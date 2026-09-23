#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>倒计时的一行（行对象常驻，秒级刷新只改文本，不重建集合）。</summary>
public sealed partial class CountdownRow : ObservableObject
{
    public CountdownRow(CountdownItem item)
    {
        Item = item;
        Title = item.Title;
    }

    public CountdownItem Item { get; }

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _headline = "--";
    [ObservableProperty] private string _subline = string.Empty;

    /// <summary>已到点：整行换强调色，让用户在桌面上一眼扫到。</summary>
    [ObservableProperty] private bool _isDue;
}

/// <summary>
/// 倒计时 / 纪念日 ViewModel。
/// 所有时刻算术与提醒判定都在 <see cref="CountdownPolicy"/>（可单测）；本类只做集合维护与显示。
/// </summary>
public sealed partial class CountdownViewModel : ObservableObject
{
    [ObservableProperty] private string _emptyHint = string.Empty;
    [ObservableProperty] private string _newTitle = string.Empty;
    [ObservableProperty] private string _newDate = string.Empty;
    [ObservableProperty] private string _newTime = string.Empty;
    [ObservableProperty] private bool _newYearly;
    [ObservableProperty] private string _addError = string.Empty;
    [ObservableProperty] private bool _isAdding;

    public ObservableCollection<CountdownRow> Rows { get; } = new();

    public bool IsFull => Rows.Count >= CountdownPolicy.MaxItems;

    /// <summary>某一轮刚到点：组件借此发托盘气泡（进程不在跑时发不出，界面上的到点高亮是兜底）。</summary>
    public event Action<CountdownItem>? OccurrenceReached;

    public void Load(IEnumerable<CountdownItem>? items)
    {
        Rows.Clear();
        foreach (var item in items ?? Array.Empty<CountdownItem>())
        {
            if (string.IsNullOrWhiteSpace(item.Title)) continue;   // 半截数据（手改/坏备份）不占行
            Rows.Add(new CountdownRow(item));
        }
        UpdateEmptyHint();
    }

    /// <summary>展开新建区时给一个好起点：日期填今天，时刻填 09:00（工作日早晨最常被设成截止）。</summary>
    public void BeginAdd()
    {
        if (IsFull)
        {
            AddError = $"最多 {CountdownPolicy.MaxItems} 条，先删掉不用的";
            return;
        }
        NewTitle = string.Empty;
        NewDate = DateTime.Today.ToString("yyyy-MM-dd");
        NewTime = "09:00";
        NewYearly = false;
        AddError = string.Empty;
        IsAdding = true;
    }

    public void CancelAdd()
    {
        IsAdding = false;
        AddError = string.Empty;
    }

    /// <summary>
    /// 校验并落一条新项目。<b>返回是否发生变化</b>（false 时 <see cref="AddError"/> 必带原因），
    /// 调用方仅在 true 时才写盘——避免"输入没写完就整档保存"。
    /// </summary>
    public bool CommitAdd(string zoneId, DateTimeOffset now)
    {
        if (!CountdownPolicy.TryParseInput(NewDate, NewYearly ? null : NewTime, requireTime: !NewYearly,
                out var wall, out var error))
        {
            AddError = error ?? "日期或时刻写错了";
            return false;
        }
        if (Rows.Count >= CountdownPolicy.MaxItems)
        {
            AddError = $"最多 {CountdownPolicy.MaxItems} 条";
            return false;
        }
        var title = CountdownPolicy.TitleOf(NewTitle, wall, NewYearly);
        var item = new CountdownItem
        {
            Id = WidgetStorage.NewId(),
            Title = title,
            At = wall,
            ZoneId = zoneId,
            Yearly = NewYearly,
            CreatedAt = now.ToUnixTimeSeconds(),
        };
        // 设一条早就过去的纪念日不该立刻弹泡：把它那一轮标成"已提醒"，明年那一轮自然生效
        var first = CountdownPolicy.NextOccurrence(item, now);
        if (now >= first) CountdownPolicy.MarkNotified(item, first);

        Rows.Add(new CountdownRow(item));
        IsAdding = false;
        AddError = string.Empty;
        UpdateEmptyHint();
        Tick(now);
        return true;
    }

    public CountdownRow? Remove(CountdownRow? row)
    {
        if (row is null) return null;
        Rows.Remove(row);
        UpdateEmptyHint();
        return row;
    }

    /// <summary>
    /// 每秒一次：重算各行，并为"刚跨过本轮时刻"的项目发一次提醒。
    /// 返回提醒到的条数（>0 时调用方要落盘，因为提醒键已写进条目）。
    /// </summary>
    public int Tick(DateTimeOffset now)
    {
        var notified = 0;
        foreach (var row in Rows)
        {
            var view = CountdownPolicy.Describe(row.Item, now);
            row.Headline = view.Headline;
            row.Subline = view.Subline;
            row.IsDue = view.IsDueNow;
            row.Title = row.Item.Title;
            if (!CountdownPolicy.ShouldNotify(row.Item, view.Occurrence, now)) continue;
            CountdownPolicy.MarkNotified(row.Item, view.Occurrence);
            notified++;
            OccurrenceReached?.Invoke(row.Item);
        }
        return notified;
    }

    public List<CountdownItem> ToPersisted() => Rows.Select(r => r.Item).ToList();

    private void UpdateEmptyHint() => EmptyHint = Rows.Count == 0
        ? "还没有项目：点下方「新建」添加截止日或纪念日。"
        : string.Empty;
}
