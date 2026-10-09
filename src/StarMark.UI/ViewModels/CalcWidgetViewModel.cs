#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StarMark.Abstractions.I18n;
using StarMark.Core.Calc;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 计算器组件 ViewModel：表达式求值 / 常用速算 / 单位换算三种输入的统一承载。
/// <para>
/// <b>这里不放任何判定</b>——求值、速算目录与算法、换算率全部在 <c>StarMark.Core.Calc</c>（可单测）。
/// 本类只做三件事：接住逐键输入的字符串、把 Core 的结论拼成显示文本、维护历史带的顺序与上限。
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
    [ObservableProperty] private string _quickResult = string.Empty;
    [ObservableProperty] private IReadOnlyList<LocOption> _unitOptions;
    [ObservableProperty] private int _fromIndex;
    [ObservableProperty] private int _toIndex = 1;
    [ObservableProperty] private int _categoryIndex;

    private CalcUnitCategory? _category;

    /// <summary>各输入槽的原始文本；按槽位下标存，切模式时整组作废。</summary>
    private readonly string?[] _quickTexts = new string?[QuickCalculator.MaxFields];
    private int _quickMode = -1;
    private QuickResult _quick = new(Array.Empty<QuickLine>(), null);

    public CalcWidgetViewModel()
    {
        Categories = UnitTables.Categories;
        _category = Categories[0];
        _unitOptions = UnitOptionsOf(_category);
        QuickModeOptions = QuickCalculator.Modes
            .Select((m, index) => new LocOption(m.TitleKey, string.Empty, index)).ToList();
    }

    public IReadOnlyList<CalcUnitCategory> Categories { get; }

    /// <summary>类别下拉的那一批<b>数据项</b>：文字走那颗键，<see cref="LocOption.Value"/> 装 ASCII 身份。</summary>
    public IReadOnlyList<LocOption> CategoryOptions =>
        Categories.Select(c => new LocOption(c.LabelKey, string.Empty, c.Key)).ToList();

    /// <summary>速算下拉的那一批<b>数据项</b>（与 <see cref="QuickCalculator.Modes"/> 同序）：
    /// 文字走那颗键，<see cref="LocOption.Value"/> 装模式序号（⛔ 拿标题当身份，✅WI-55a 那三颗下拉同一条路）。</summary>
    public IReadOnlyList<LocOption> QuickModeOptions { get; }

    public ObservableCollection<CalcHistoryEntry> History { get; } = new();

    /// <summary>类别下拉的当前序号（由界面写入）；改动后起止单位归位到该类别的前两个，<see cref="UnitOptions"/> 换一批。</summary>
    partial void OnCategoryIndexChanged(int value)
    {
        if (value < 0 || value >= Categories.Count) return;
        _category = Categories[value];
        UnitOptions = UnitOptionsOf(_category);
        // 换类别后旧序号可能越界 ⇒ 归位到"该类别的前两个单位"，不能让换算静默用错单位
        FromIndex = 0;
        ToIndex = Math.Min(1, _category.Units.Count - 1);
        RecalcUnit();
    }

    /// <summary>
    /// 某一类量的单位选项（<b>念法走那颗键</b>，批次 WI-55a）：
    /// ⛔ 在这里把 <c>Locale.T(...)</c> 的结果存成 <c>LocOption.Text</c>——建表那一刻取词就等于把语言定死，
    /// 而模板那颗 <c>TextBlock</c> 自己带着键，换语言由 <c>LocText</c> 那一条唯一的重贴路改口。
    /// </summary>
    private static IReadOnlyList<LocOption> UnitOptionsOf(CalcUnitCategory category) =>
        category.Units.Select(u => new LocOption(u.LabelKey, string.Empty, u.Key)).ToList();

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
        var units = cat.Units;
        if (units.Count == 0) { UnitResult = string.Empty; return; }
        var from = units[Math.Clamp(FromIndex, 0, units.Count - 1)];
        var to = units[Math.Clamp(ToIndex, 0, units.Count - 1)];
        if (!decimal.TryParse(UnitValueText?.Trim(), NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var value))
        {
            UnitResult = Locale.T("calc.convert.needNumber");
            return;
        }
        if (!UnitTables.TryConvert(cat.Key, from.Key, to.Key, value, out var result, out var error))
        {
            UnitResult = (error ?? LocMessage.Of("calc.convert.failed")).Render();
            return;
        }
        // 那两颗单位名交出的是<b>还没渲染的</b> LocMessage：string.Format 在填空那一刻调 ToString()→Render()，
        // 于是外层骨架与嵌进去的两个名字在同一次取词里改口（坑表 #328／#361：实参先渲染＝语言定死在算出它的那一刻）。
        UnitResult = Locale.Fmt("calc.convert.result",
            UnitTables.Format(value), LocMessage.Of(from.LabelKey),
            UnitTables.Format(result), LocMessage.Of(to.LabelKey));
    }

    /// <summary>交换起止单位；已是同一个单位时无需交换。</summary>
    public void SwapUnits()
    {
        var to = ToIndex;
        ToIndex = FromIndex;
        FromIndex = to;
        RecalcUnit();
    }

    /// <summary>
    /// 换语言那一刻由宿主（<c>CalcWidget</c>）调进来的重贴：三页的读数各按<b>新语言</b>再念一遍。
    /// <para>这里⛔ 取词时机以外的任何事：不落盘、不重建控件、不清输入框——
    /// 那三件都属于"用户做了某个动作"，而不属于"语言变了"（✅WI-52／✅WI-53／✅WI-54 同一条口径）。</para>
    /// <para>之所以要这一颗而不是靠 <c>LocText</c>：这三句都是<b>当场算出来的读数</b>，
    /// 注册进那张名单只会得到"名单里多条贴不出去的空壳"（✅WI-45），而 <c>SetKey</c> 连实参一起冻住
    /// 更会把上一次那句贴回来（坑表 #361）。</para>
    /// </summary>
    public void RebuildWords()
    {
        ApplyExpression(Expression);
        RecalcUnit();
        RecalcQuick();
    }

    // ───────────────────────── 常用速算 ─────────────────────────

    /// <summary>当前速算模式（<see cref="SelectQuickMode"/> 之后有效；未初始化时是"打折 / 满减"）。</summary>
    public QuickMode CurrentQuickMode => QuickCalculator.Modes[Math.Clamp(_quickMode, 0, QuickCalculator.Modes.Count - 1)];

    /// <summary>
    /// 切到某种速算，返回它的输入槽（界面据此改标题/占位/显隐）。
    /// <b>换模式必定作废上一组的输入文本</b>：把"原价 800"接着当成"起始值 800"用，
    /// 是给用户一个看起来算得飞快、其实答非所问的结果。
    /// </summary>
    public IReadOnlyList<QuickField> SelectQuickMode(int index)
    {
        if (index < 0 || index >= QuickCalculator.Modes.Count) index = 0;
        if (_quickMode != index)
        {
            Array.Clear(_quickTexts, 0, _quickTexts.Length);
            _quickMode = index;
        }
        RecalcQuick();
        return CurrentQuickMode.Fields;
    }

    /// <summary>某个输入槽逐键变化（越界的槽号直接忽略：界面预放了 <see cref="QuickCalculator.MaxFields"/> 个，用不满是常态）。</summary>
    public void SetQuickText(int slot, string? text)
    {
        if (slot < 0 || slot >= _quickTexts.Length) return;
        _quickTexts[slot] = text;
        RecalcQuick();
    }

    public void RecalcQuick()
    {
        if (_quickMode < 0) _quickMode = 0;
        _quick = QuickCalculator.Evaluate(_quickMode, _quickTexts, DateTime.Now);
        // 什么都没填时这里就是"「原价」还没填数字"：结果区一片空白会被当成功能没做（组件里的老教训）
        QuickResult = _quick.MultiLine;
    }

    /// <summary>可复制的速算结果。算错/没填时给 null，让界面明说"现在没东西可复制"而不是把错误原因塞进剪贴板。</summary>
    public string? QuickCopyText => _quick.Ok ? _quick.MultiLine : null;
}

/// <summary>历史带的一行（表达式 + 当次答案）。</summary>
public sealed class CalcHistoryEntry
{
    public string Expression { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}
