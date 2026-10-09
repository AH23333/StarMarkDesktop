#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using StarMark.Abstractions.I18n;

namespace StarMark.Core.Calc;

/// <summary>一个输入槽的取值方式：金额/数字，还是日期。</summary>
public enum QuickFieldKind
{
    Money,
    Date,
}

/// <summary>
/// 速算页的一个输入槽。<see cref="Required"/>=false 表示留空有意义：
/// 数字留空按 0（"没有满减"），日期留空按今天（"距年底还有几天"只填一头）——
/// "可空"那两个字由界面按这颗 bool 现拼一句（<c>calc.quick.optionalField</c>），⛔ 写进每格的名字里。
/// <para>念法交 <see cref="LabelKey"/>（批次 WI-55b）：这三句被托盘上的标签、错误句里的引用同时念，
/// 存成品字就换语言后各念一份（✅WI-46c 那颗组名同一条界线）。</para>
/// <para>占位符两种之一，<b>恰好一种</b>（<see cref="HasOnePlaceholderShape"/>）：
/// <see cref="PlaceholderKey"/> 是"真有一句话要说"（如「留空＝今天」），
/// <see cref="Placeholder"/> 是给照着打的<b>例子</b>（199、7.5、2026-12-31：数字与 ISO 日期三语一样，
/// ⛔ 为它们各造一行＝表里多出一批三列同形的空转行）。</para>
/// </summary>
public sealed record QuickField(string LabelKey, string? PlaceholderKey, string? Placeholder,
                                QuickFieldKind Kind, bool Required = true)
{
    public bool HasOnePlaceholderShape => (PlaceholderKey is null) != (Placeholder is null);
}

/// <summary>一种速算：标题那颗键、一句话说明那颗键、需要哪几个输入槽。</summary>
public sealed record QuickMode(string TitleKey, string HintKey, IReadOnlyList<QuickField> Fields);

/// <summary>
/// 计算器「常用」页的目录：有哪些速算、各要哪些输入、把原始文本解析成什么再交给 <see cref="QuickMath"/>。
/// <para>
/// 模式表与算法放在一起是刻意的：新增一种速算必须同时给出输入槽和落到哪个算法，
/// 否则它会在页面上变成一个"有标题没有输入"的空壳；<see cref="Modes"/> 逐项配齐由单测钉住。
/// </para>
/// <para>
/// 解析一律<b>按槽位下标</b>取值而不是先分拣再压缩（那样一旦某个模式混排数字与日期，
/// 参数就会静默错位），失败也一律点出<b>是哪个槽</b>——四个输入并排时，
/// "数字没认出来"这种没有主语的原因等于没说。那句主语交的是<b>还没渲染的</b>那颗标签键（坑表 #393）。
/// </para>
/// </summary>
public static class QuickCalculator
{
    /// <summary>界面按这个数量预放输入槽（多退少不填：没有哪个模式用到第 5 个）。</summary>
    public const int MaxFields = 4;

    /// <summary>
    /// 那十二颗<b>输入槽身份</b>（屏幕上那一行的键名）。住进嵌套类而不是摊成外层 <c>public const</c>：
    /// 外层常量会被那颗"扫键族"的反射证人当成自己人算进名单（✅WI-40-2 坑表 #359 同一件事）。
    /// <para>算法的拒绝句要点名"是哪一格"时，引用的就是这里的<b>那颗键</b>，⛔ 抄屏幕上那句话。</para>
    /// </summary>
    public static class Field
    {
        public const string Original = "calc.quick.field.original";
        public const string Discount = "calc.quick.field.discount";
        public const string EveryFull = "calc.quick.field.everyFull";
        public const string Reduce = "calc.quick.field.reduce";
        public const string Start = "calc.quick.field.start";
        public const string Current = "calc.quick.field.current";
        public const string PriceA = "calc.quick.field.priceA";
        public const string QtyA = "calc.quick.field.qtyA";
        public const string PriceB = "calc.quick.field.priceB";
        public const string QtyB = "calc.quick.field.qtyB";
        public const string StartDate = "calc.quick.field.startDate";
        public const string EndDate = "calc.quick.field.endDate";
    }

    public static IReadOnlyList<QuickMode> Modes { get; } = new List<QuickMode>
    {
        new("calc.quick.mode.bargain", "calc.quick.hint.bargain",
            new List<QuickField>
            {
                new(Field.Original, null, "199", QuickFieldKind.Money),
                new(Field.Discount, null, "7.5", QuickFieldKind.Money),
                new(Field.EveryFull, null, "300", QuickFieldKind.Money, Required: false),
                new(Field.Reduce, null, "50", QuickFieldKind.Money, Required: false),
            }),
        new("calc.quick.mode.change", "calc.quick.hint.change",
            new List<QuickField>
            {
                new(Field.Start, null, "100", QuickFieldKind.Money),
                new(Field.Current, null, "80", QuickFieldKind.Money),
            }),
        new("calc.quick.mode.unit", "calc.quick.hint.unit",
            new List<QuickField>
            {
                new(Field.PriceA, null, "29.9", QuickFieldKind.Money),
                new(Field.QtyA, null, "500", QuickFieldKind.Money),
                new(Field.PriceB, null, "39.9", QuickFieldKind.Money),
                new(Field.QtyB, null, "800", QuickFieldKind.Money),
            }),
        new("calc.quick.mode.days", "calc.quick.hint.days",
            new List<QuickField>
            {
                // 这一格的占位符是一句话（"空着就是今天"），不是例子 ⇒ 走表
                new(Field.StartDate, "calc.quick.placeholder.emptyToday", null, QuickFieldKind.Date, Required: false),
                new(Field.EndDate, null, "2026-12-31", QuickFieldKind.Date),
            }),
    };

    /// <summary>
    /// 按模式序号算一次。<paramref name="texts"/> 是界面上各输入槽的原始文本（可含 null 项、可以比槽数短）。
    /// </summary>
    public static QuickResult Evaluate(int modeIndex, IReadOnlyList<string?> texts, DateTime today)
    {
        if (modeIndex < 0 || modeIndex >= Modes.Count)
            return new QuickResult(Array.Empty<QuickLine>(), LocMessage.Of("calc.quick.noMode", modeIndex));

        var mode = Modes[modeIndex];
        // 两组槽按同一份下标填充：数字槽的 Date 为 null、日期槽的 Money 为 0，取值时永不错位。
        var money = new decimal[MaxFields];
        var date = new DateTime?[MaxFields];
        for (var i = 0; i < mode.Fields.Count; i++)
        {
            var field = mode.Fields[i];
            var text = (texts.Count > i ? texts[i] : null)?.Trim() ?? string.Empty;
            if (field.Kind == QuickFieldKind.Date)
            {
                if (text.Length == 0)
                {
                    if (field.Required) return Missing(field);
                    date[i] = today.Date;
                    continue;
                }
                var parsed = QuickMath.ParseDate(text, today);
                if (parsed is null)
                    return Fail(LocMessage.Of("calc.quick.badDate", LocMessage.Of(field.LabelKey)));
                date[i] = parsed;
                continue;
            }
            if (text.Length == 0)
            {
                if (field.Required) return Missing(field);
                money[i] = 0m;
                continue;
            }
            if (!decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out var value))
                return Fail(LocMessage.Of("calc.quick.badNumber", LocMessage.Of(field.LabelKey), text));
            money[i] = value;
        }

        return modeIndex switch
        {
            0 => QuickMath.Bargain(money[0], money[1], money[2], money[3]),
            1 => QuickMath.Change(money[0], money[1]),
            2 => QuickMath.UnitPriceCompare(money[0], money[1], money[2], money[3]),
            _ => QuickMath.DateSpan(date[0] ?? today.Date, date[1] ?? today.Date),
        };
    }

    /// <summary>
    /// 必填那一格没填：两句各自把"日期／数字"写在句子里，⛔ 由代码在句子后面拼那两个字
    /// （✅坑表 #371 那一族：分隔词留在片段里，屏幕上就是一句半截话）。
    /// </summary>
    private static QuickResult Missing(QuickField field)
        => Fail(LocMessage.Of(field.Kind == QuickFieldKind.Date
            ? "calc.quick.missingDate"
            : "calc.quick.missingNumber", LocMessage.Of(field.LabelKey)));

    private static QuickResult Fail(LocMessage reason) => new(Array.Empty<QuickLine>(), reason);
}
