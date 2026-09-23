#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Core.Calc;
using StarMark.Core.Widgets;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 计算器组件 ViewModel：表达式求值 / 单位换算 / 时间戳换算三种输入的统一承载。
/// <para>
/// <b>这里不放任何判定</b>——求值、换算率、位数判精度全部在 <c>StarMark.Core.Calc</c>（可单测）。
/// 本类只做三件事：接住逐键输入的字符串、把 Core 的结论拼成一行显示文本、维护历史带的顺序与上限。
/// </para>
/// </summary>
public sealed partial class CalcWidgetViewModel : ObservableObject
{
    /// <summary>历史带上限。再多也翻不到，且会让 widgets.json 无界增长。</summary>
    public const int HistoryLimit = 20;

    [ObservableProperty] private string _expression = string.Empty;
    [ObservableProperty] private string _calcResult = "输入算式，回车记入历史";
    [ObservableProperty] private string _unitValueText = "1";
    [ObservableProperty] private string _unitResult = string.Empty;
    [ObservableProperty] private string _stampText = string.Empty;
    [ObservableProperty] private string _stampResult = string.Empty;
    [ObservableProperty] private string _clockText = string.Empty;
    [ObservableProperty] private string _clockResult = string.Empty;
    [ObservableProperty] private IReadOnlyList<CalcUnit> _units;
    [ObservableProperty] private int _fromIndex;
    [ObservableProperty] private int _toIndex = 1;
    [ObservableProperty] private int _categoryIndex;

    private CalcUnitCategory? _category;

    public CalcWidgetViewModel()
    {
        Categories = UnitTables.Categories;
        _category = Categories[0];
        _units = _category.Units;
    }

    public IReadOnlyList<CalcUnitCategory> Categories { get; }

    public ObservableCollection<CalcHistoryEntry> History { get; } = new();

    /// <summary>类别下拉的当前序号（由界面写入）；改动后起止单位归位到该类别的前两个，<see cref="Units"/> 换一批。</summary>
    partial void OnCategoryIndexChanged(int value)
    {
        if (value < 0 || value >= Categories.Count) return;
        _category = Categories[value];
        Units = _category.Units;
        // 换类别后旧序号可能越界 ⇒ 归位到"该类别的前两个单位"，不能让换算静默用错单位
        FromIndex = 0;
        ToIndex = Math.Min(1, _category.Units.Count - 1);
        RecalcUnit();
    }

    // ───────────────────────── 表达式 ─────────────────────────

    /// <summary>逐键求值：只更新显示，不入历史（入历史要用户明确按回车或点"记入"）。</summary>
    public void ApplyExpression(string? text)
    {
        Expression = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Expression))
        {
            CalcResult = "输入算式，回车记入历史";
            return;
        }
        CalcResult = ExpressionEvaluator.Evaluate(Expression).Display;
    }

    /// <summary>
    /// 把当前算式记入历史。<b>失败的结果不入</b>——历史带是"算对过的东西"，
    /// 掺进一串错误原因后既没用又会误导（用户会以为那是个值）。
    /// </summary>
    /// <returns>是否真的记入（空/失败/与首条重复时 false）。</returns>
    public bool CommitExpression()
    {
        var r = ExpressionEvaluator.Evaluate(Expression);
        if (!r.Ok) return false;
        var answer = ExpressionEvaluator.Format(r.Value);
        var text = Expression.Trim();
        if (History.Count > 0 && string.Equals(History[0].Expression, text, StringComparison.Ordinal)) return false;

        var dup = History.FirstOrDefault(h => string.Equals(h.Expression, text, StringComparison.Ordinal));
        if (dup is not null) History.Remove(dup);
        History.Insert(0, new CalcHistoryEntry { Expression = text, Answer = answer });
        while (History.Count > HistoryLimit) History.RemoveAt(History.Count - 1);
        return true;
    }

    public void ClearHistory() => History.Clear();

    /// <summary>从实例配置装载历史（越界的脏数据直接丢弃，不做"看起来还在"的假回填）。</summary>
    public void SeedHistory(IEnumerable<CalcHistoryItem>? items)
    {
        History.Clear();
        foreach (var i in items ?? Array.Empty<CalcHistoryItem>())
        {
            if (string.IsNullOrWhiteSpace(i.Expression)) continue;
            History.Add(new CalcHistoryEntry { Expression = i.Expression, Answer = i.Answer });
            if (History.Count >= HistoryLimit) break;
        }
    }

    public List<CalcHistoryItem> ToPersisted() =>
        History.Select(h => new CalcHistoryItem { Expression = h.Expression, Answer = h.Answer }).ToList();

    // ───────────────────────── 单位换算 ─────────────────────────

    public void RecalcUnit()
    {
        var cat = _category;
        if (cat is null) return;
        if (Units.Count == 0) { UnitResult = string.Empty; return; }
        var from = Units[Math.Clamp(FromIndex, 0, Units.Count - 1)];
        var to = Units[Math.Clamp(ToIndex, 0, Units.Count - 1)];
        if (!decimal.TryParse(UnitValueText?.Trim(), NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var value))
        {
            UnitResult = "填入一个数字";
            return;
        }
        if (!UnitTables.TryConvert(cat.Key, from.Key, to.Key, value, out var result, out var error))
        {
            UnitResult = error ?? "换算失败";
            return;
        }
        UnitResult = $"{UnitTables.Format(value)} {from.Label} = {UnitTables.Format(result)} {to.Label}";
    }

    /// <summary>交换起止单位；已是同一个单位时无需交换。</summary>
    public void SwapUnits()
    {
        var to = ToIndex;
        ToIndex = FromIndex;
        FromIndex = to;
        RecalcUnit();
    }

    // ───────────────────────── 时间戳 ─────────────────────────

    /// <summary>时间戳 → 日期。时区取本机（Core 侧只接受参数，不在内部读环境）。</summary>
    public void RecalcStamp()
    {
        var zone = TimeZoneInfo.Local;
        if (!TimestampConverter.TryParseStamp(StampText, zone, out var at, out var unit, out var error))
        {
            StampResult = error ?? "无法解析";
            return;
        }
        StampResult = $"{TimestampConverter.Format(at)}  {TimestampConverter.OffsetText(at.Offset)}"
                    + $"（按{TimestampConverter.UnitLabel(unit)}理解）"
                    + (at.Offset == TimeSpan.Zero ? string.Empty
                       : $"\nUTC：{TimestampConverter.Format(at.ToUniversalTime())}");
    }

    /// <summary>日期 → 三种精度的时间戳一次给全（用户多半正要的是其中一个）。</summary>
    public void RecalcClock()
    {
        if (!TimestampConverter.TryParseClock(ClockText, TimeZoneInfo.Local, out var at, out var error))
        {
            ClockResult = error ?? "无法解析";
            return;
        }
        ClockResult = $"秒 {TimestampConverter.ToUnix(at, UnixStampUnit.Seconds)}"
                    + $"　毫秒 {TimestampConverter.ToUnix(at, UnixStampUnit.Milliseconds)}"
                    + $"　微秒 {TimestampConverter.ToUnix(at, UnixStampUnit.Microseconds)}";
    }

    /// <summary>「用此刻」：把当前时间同时填进两侧，省掉手打（也是"我这台机器的时区到底差几小时"的最快问法）。</summary>
    public void UseNow()
    {
        var now = DateTimeOffset.Now;
        ClockText = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        StampText = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        RecalcClock();
        RecalcStamp();
    }
}

/// <summary>历史带的一行（表达式 + 当次答案）。</summary>
public sealed class CalcHistoryEntry
{
    public string Expression { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}
