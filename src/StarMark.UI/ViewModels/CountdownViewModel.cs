#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
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
    [ObservableProperty] private string _newTitle = string.Empty;
    [ObservableProperty] private string _newDate = string.Empty;
    [ObservableProperty] private string _newTime = string.Empty;
    [ObservableProperty] private bool _newYearly;
    [ObservableProperty] private bool _isAdding;

    // 那两句（空态提示／没存下去的原因）是**状态读数**：每次问都按当前语言现取，⛔ 存成"当时取好的那句"
    // （存成品字＝换语言之后屏幕上留着旧语言那一句，而这一扇窗没有自己的节拍替它重算）。
    private LocMessage? _addError;

    /// <summary>空态那句提示。那颗「新建」按钮的名字<b>交实参</b>（那颗行只有 <c>widget.cd.add</c> 一个主人，✅WI-46c 同口径）。</summary>
    public string EmptyHint => Rows.Count == 0
        ? LocMessage.Of("widget.cd.empty", Locale.T("widget.cd.add")).Render()
        : string.Empty;

    /// <summary>最近一次没存下去的原因：每次读都现取词（<c>_addError</c> 存的是那颗键）。</summary>
    public string AddError => _addError is null ? string.Empty : _addError.Render();

    private void SetError(LocMessage? message)
    {
        _addError = message;
        OnPropertyChanged(nameof(AddError));
    }

    public ObservableCollection<CountdownRow> Rows { get; } = new();

    public bool IsFull => Rows.Count >= CountdownPolicy.MaxItems;

    /// <summary>某一轮刚到点：组件借此发右下角提示卡（进程不在跑时发不出，界面上的到点高亮是兜底）。</summary>
    public event Action<CountdownItem>? OccurrenceReached;

    public void Load(IEnumerable<CountdownItem>? items)
    {
        Rows.Clear();
        foreach (var item in items ?? Array.Empty<CountdownItem>())
        {
            // 坏行的判据＝"连日期都没有"（Title 空现在<b>是合法的</b>：那是"用派生名"，见 CountdownPolicy.TitleStored）
            if (item.At == default) continue;   // 半截数据（手改/坏备份）不占行
            Rows.Add(new CountdownRow(item));
        }
        OnPropertyChanged(nameof(EmptyHint));
    }

    /// <summary>展开新建区时给一个好起点：日期填今天，时刻填 09:00（工作日早晨最常被设成截止）。</summary>
    public void BeginAdd()
    {
        if (IsFull)
        {
            SetError(LocMessage.Of("widget.cd.limit", CountdownPolicy.MaxItems));
            return;
        }
        NewTitle = string.Empty;
        NewDate = DateTimeText.Day(DateTime.Today);
        NewTime = "09:00";
        NewYearly = false;
        SetError(null);
        IsAdding = true;
    }

    public void CancelAdd()
    {
        IsAdding = false;
        SetError(null);
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
            SetError(error ?? LocMessage.Of("widget.cd.errUnknown"));
            return false;
        }
        if (Rows.Count >= CountdownPolicy.MaxItems)
        {
            // 上限句只留一颗键：旧写法在这里又写一份不带"先删掉不用的"的那句＝同一件事两个主人
            SetError(LocMessage.Of("widget.cd.limit", CountdownPolicy.MaxItems));
            return false;
        }
        var title = CountdownPolicy.TitleStored(NewTitle);   // ⛔ 把生成的默认名连同当时语言一起存进档（那是身份问题，不是措辞问题）
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
        SetError(null);
        OnPropertyChanged(nameof(EmptyHint));
        Tick(now);
        return true;
    }

    public CountdownRow? Remove(CountdownRow? row)
    {
        if (row is null) return null;
        Rows.Remove(row);
        OnPropertyChanged(nameof(EmptyHint));
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
            // Core 交的是那颗键；词在**这一秒**取（这一排每秒重算 ⇒ 节拍就是它的重贴，⛔ 挂 LocText＝#361 那一族）
            row.Headline = view.Headline.Render();
            row.Subline = view.Subline.Render();
            row.IsDue = view.IsDueNow;
            row.Title = CountdownPolicy.TitleText(row.Item);   // 用户起的名字原样念；没起的按<b>当前</b>语言现算
            if (!CountdownPolicy.ShouldNotify(row.Item, view.Occurrence, now)) continue;
            CountdownPolicy.MarkNotified(row.Item, view.Occurrence);
            notified++;
            OccurrenceReached?.Invoke(row.Item);
        }
        return notified;
    }

    public List<CountdownItem> ToPersisted() => Rows.Select(r => r.Item).ToList();

    /// <summary>
    /// 换语言那一拍（批次 WI-54）：空态提示、错误句、每行的主行／次行／名字都是<b>当场算的读数</b>，
    /// 而这一扇窗自己的节拍（每秒 <c>Tick</c>）⛔ 含"语言换了"这件事 ⇒ 由宿主订阅 <c>Locale.Changed</c> 喊这一颗。
    /// <para>⛔ 在这里读盘、⛔ 在这里改条目：只做"把已经在算的那几行再算一遍"。</para>
    /// </summary>
    public void RebuildWords(DateTimeOffset now)
    {
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(AddError));
        Tick(now);
    }
}
