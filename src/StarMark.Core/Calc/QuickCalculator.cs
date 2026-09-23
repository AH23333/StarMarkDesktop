#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace StarMark.Core.Calc;

/// <summary>一个输入槽的取值方式：金额/数字，还是日期。</summary>
public enum QuickFieldKind
{
    Money,
    Date,
}

/// <summary>
/// 速算页的一个输入槽。<see cref="Required"/>=false 表示留空有意义：
/// 数字留空按 0（"没有满减"），日期留空按今天（"距年底还有几天"只填一头）。
/// </summary>
public sealed record QuickField(string Label, string Placeholder, QuickFieldKind Kind, bool Required = true);

/// <summary>一种速算：标题、一句话说明、需要哪几个输入槽。</summary>
public sealed record QuickMode(string Title, string Hint, IReadOnlyList<QuickField> Fields);

/// <summary>
/// 计算器「常用」页的目录：有哪些速算、各要哪些输入、把原始文本解析成什么再交给 <see cref="QuickMath"/>。
/// <para>
/// 模式表与算法放在一起是刻意的：新增一种速算必须同时给出输入槽和落到哪个算法，
/// 否则它会在页面上变成一个"有标题没有输入"的空壳；<see cref="Modes"/> 逐项配齐由单测钉住。
/// </para>
/// <para>
/// 解析一律<b>按槽位下标</b>取值而不是先分拣再压缩（那样一旦某个模式混排数字与日期，
/// 参数就会静默错位），失败也一律点出<b>是哪个槽</b>——四个输入并排时，
/// "数字没认出来"这种没有主语的原因等于没说。
/// </para>
/// </summary>
public static class QuickCalculator
{
    /// <summary>界面按这个数量预放输入槽（多退少不填：没有哪个模式用到第 5 个）。</summary>
    public const int MaxFields = 4;

    public static IReadOnlyList<QuickMode> Modes { get; } = new List<QuickMode>
    {
        new("打折 / 满减",
            "折扣可写 7.5（七五折）或 85（按 85% 收）；满减两栏没有就留空。",
            new List<QuickField>
            {
                new("原价", "199", QuickFieldKind.Money),
                new("折扣", "7.5", QuickFieldKind.Money),
                new("每满", "300（可空）", QuickFieldKind.Money, Required: false),
                new("减", "50（可空）", QuickFieldKind.Money, Required: false),
            }),
        new("涨跌 / 回本",
            "起始值填买入价、当前值填现价。跌 20% 要涨 25% 才回本，跌 50% 要涨 100%。",
            new List<QuickField>
            {
                new("起始值", "100", QuickFieldKind.Money),
                new("当前值", "80", QuickFieldKind.Money),
            }),
        new("哪个划算",
            "两种包装的单价对比，如 29.9 元/500g 与 39.9 元/800g。",
            new List<QuickField>
            {
                new("价格 A", "29.9", QuickFieldKind.Money),
                new("数量 A", "500", QuickFieldKind.Money),
                new("价格 B", "39.9", QuickFieldKind.Money),
                new("数量 B", "800", QuickFieldKind.Money),
            }),
        new("间隔天数",
            "日期可写 2026-12-31 或 12-31（按今年）；开始留空＝今天。",
            new List<QuickField>
            {
                new("开始日期", "留空＝今天", QuickFieldKind.Date, Required: false),
                new("结束日期", "2026-12-31", QuickFieldKind.Date),
            }),
    };

    /// <summary>
    /// 按模式序号算一次。<paramref name="texts"/> 是界面上各输入槽的原始文本（可含 null 项、可以比槽数短）。
    /// </summary>
    public static QuickResult Evaluate(int modeIndex, IReadOnlyList<string?> texts, DateTime today)
    {
        if (modeIndex < 0 || modeIndex >= Modes.Count)
            return new QuickResult(Array.Empty<QuickLine>(), $"没有序号为 {modeIndex} 的速算");

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
                    return new QuickResult(Array.Empty<QuickLine>(),
                        $"「{field.Label}」没认出日期（写成 2026-12-31，或 12-31 按今年）");
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
                return new QuickResult(Array.Empty<QuickLine>(), $"「{field.Label}」没认出数字：{text}");
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

    private static QuickResult Missing(QuickField field)
        => new QuickResult(Array.Empty<QuickLine>(),
            $"「{field.Label}」还没填" + (field.Kind == QuickFieldKind.Date ? "日期" : "数字"));
}
