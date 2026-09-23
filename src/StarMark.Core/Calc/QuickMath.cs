#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace StarMark.Core.Calc;

/// <summary>一条速算结果（标签 + 已格式化好的数字）。</summary>
public sealed record QuickLine(string Label, string Value);

/// <summary>
/// 一次速算的结果：<see cref="Error"/> 非空表示输入不成立，此时 <see cref="Lines"/> 不给出。
/// <b>不给"看着像数"的 0</b>——0 折和"没填折扣"、除以 0 的商和"确实是 0"在界面上必须长得不一样。
/// </summary>
public sealed record QuickResult(IReadOnlyList<QuickLine> Lines, string? Error)
{
    public bool Ok => Error is null;

    /// <summary>拼成一行（"到手 224.25　省 74.75　相当于 74.75%"）。给提示条、日志这类单行容器用。</summary>
    public string Text
    {
        get
        {
            if (!Ok) return Error!;
            var parts = new string[Lines.Count];
            for (var i = 0; i < Lines.Count; i++) parts[i] = Lines[i].Label + " " + Lines[i].Value;
            return string.Join("　", parts);
        }
    }

    /// <summary>每项一行，给组件里结果显示与复制到剪贴板用（多条速算结果挤在一行读不清）。</summary>
    public string MultiLine
    {
        get
        {
            if (!Ok) return Error!;
            var parts = new string[Lines.Count];
            for (var i = 0; i < Lines.Count; i++) parts[i] = Lines[i].Label + "  " + Lines[i].Value;
            return string.Join("\n", parts);
        }
    }
}

/// <summary>
/// 计算器「常用」页的算法：打折与满减、涨跌与回本、两种包装哪个划算、两个日期相差多久。
/// <para>
/// 换掉原来的 Unix 时间戳页是发起人 2026-09-24 的判断（"时间戳计算不知所云，很少有该计算需求"）。
/// 这四样都是不用查资料、当场会按的算式，且全部只依赖手边的数：不联网、不引依赖、不需要外部数据。
/// </para>
/// </summary>
public static class QuickMath
{
    /// <summary>金额显示：两位小数并带千分位（与求值器同一套分节号写法，复制回去还能继续算）。</summary>
    public static string Money(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("#,0.00", CultureInfo.InvariantCulture);

    /// <summary>普通数字：最多两位小数，整数不带小数点。</summary>
    public static string Number(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

    // ─────────────── 打折与满减 ───────────────

    /// <summary>
    /// 打折后能不能再吃"每满减"：原价 <paramref name="original"/>、折扣 <paramref name="discount"/>、
    /// 每满 <paramref name="everyFull"/> 减 <paramref name="reduceEach"/>。
    /// <para>
    /// 折扣允许两种习惯写法：<b>≤10 视为"几折"</b>（7.5 → 七五折 = 75%），
    /// <b>10–100 视为百分比</b>（85 → 按 85% 收）。这不是含糊：中文语境里"打个 8"没人是想要八折的十分之一。
    /// </para>
    /// <para>满减按<b>折后价</b>算次数（多数电商的规则），向下取整——"每满 300 减 50"，
    /// 折后 690 元是减 2 次共 100，不是按比例减 115。<b>不需要"减成负数"的兜底</b>：门槛校验已保证
    /// 每次减的钱 <c>reduceEach &lt; everyFull</c>，而次数按折后价向下取整，
    /// 于是"减掉的总额 &lt; 折后价"是恒成立的，到手价必然大于 0。</para>
    /// </summary>
    public static QuickResult Bargain(decimal original, decimal discount, decimal everyFull, decimal reduceEach)
    {
        if (original <= 0) return Fail("原价要大于 0");
        if (discount <= 0 || discount > 100) return Fail("折扣要在 0–100 之间（7.5＝七五折，85＝按 85% 收）");
        if (everyFull < 0 || reduceEach < 0) return Fail("满减金额不能是负数");
        if (everyFull > 0 && reduceEach <= 0) return Fail("填了「每满」就要填「减多少」");
        if (everyFull == 0 && reduceEach > 0) return Fail("填了「减多少」就要填「每满」多少");
        if (everyFull > 0 && reduceEach >= everyFull) return Fail("减的钱不能等于或超过「每满」的门槛");

        var rate = discount <= 10 ? discount / 10m : discount / 100m;
        var afterDiscount = original * rate;
        var times = everyFull > 0 ? Math.Floor(afterDiscount / everyFull) : 0m;
        var reduction = times * reduceEach;
        var final = afterDiscount - reduction;
        var saved = original - final;

        var lines = new List<QuickLine>
        {
            new("到手", Money(final)),
            new("省", Money(saved)),
            new("相当于", Number(final / original * 100m) + "%"),
        };
        if (discount > 10) lines.Insert(0, new("折后", Money(afterDiscount)));
        else lines.Insert(0, new("折后", Money(afterDiscount) + $"（{Number(discount)} 折）"));
        if (times > 0) lines.Add(new("满减", $"减 {times.ToString("0", CultureInfo.InvariantCulture)} 次共 " + Money(reduction)));
        return new QuickResult(lines, null);
    }

    // ─────────────── 涨跌与回本 ───────────────

    /// <summary>
    /// 从 <paramref name="from"/> 变到 <paramref name="to"/> 的涨跌幅，附"要回到原值还需涨/跌多少"。
    /// <para>
    /// 后面那半句是这一页存在的理由：跌 20% 需要涨 <b>25%</b> 才回本（不是 20%），
    /// 跌 50% 要涨 100%。这个不对称心算很容易错，而它正是"亏损后还要多久回本"的答案。
    /// </para>
    /// <para>两边都按<b>非负数</b>要求：负的价格算不出有意义的百分比（跌 20% 与涨 20% 在负数轴上会互换方向），
    /// 与其给一个看着像答案的错数，不如直接说要正数。</para>
    /// </summary>
    public static QuickResult Change(decimal from, decimal to)
    {
        if (from <= 0) return Fail("起始值要大于 0（涨跌百分比以它为基准）");
        if (to < 0) return Fail("当前值不能是负数（本计算按价格/市值这类非负数算百分比）");
        var delta = to - from;
        var percent = delta / from * 100m;
        var lines = new List<QuickLine>
        {
            new("差额", Signed(delta, Money(delta))),
            new("涨跌", Signed(percent, Number(percent) + "%")),
        };
        if (to == 0) lines.Add(new("回本", "值已归零，按比例回不来"));
        else
        {
            // 回本方向与涨跌相反，故这里取绝对值配文字：写"需跌 -16.67%"是否定之否定，读的人得再翻一次。
            var back = (from - to) / to * 100m;
            var label = "回到 " + Number(from);
            if (back == 0) lines.Add(new(label, "已经是这个值"));
            else lines.Add(new(label, (back > 0 ? "需涨 " : "需跌 ") + Number(Math.Abs(back)) + "%"));
        }
        return new QuickResult(lines, null);
    }

    /// <summary>正数才补 "+"：给 0 加正号会让人以为它是涨了一点。</summary>
    private static string Signed(decimal value, string formatted)
        => value > 0 ? "+" + formatted : formatted;

    // ─────────────── 单价比较：哪个划算 ───────────────

    /// <summary>
    /// 两种包装谁便宜：A 组（价 <paramref name="priceA"/> / 量 <paramref name="qtyA"/>）与 B 组。
    /// 单价按 <b>4 位小数</b>比较后展示（1 位会让 12.345 与 12.344 判成"一样"，而 200g 与 250g 的差常常就卡在那一档）。
    /// 比较与显示用<b>同一个取整后的值</b>：结论说"B 便宜 19.84%"时，读者手上能复核的就是界面上那两个单价。
    /// </summary>
    public static QuickResult UnitPriceCompare(decimal priceA, decimal qtyA, decimal priceB, decimal qtyB)
    {
        if (priceA <= 0 || priceB <= 0) return Fail("两个价格都要大于 0");
        if (qtyA <= 0 || qtyB <= 0) return Fail("两个数量都要大于 0（0 除不出单价）");

        var unitA = Math.Round(priceA / qtyA, 4, MidpointRounding.AwayFromZero);
        var unitB = Math.Round(priceB / qtyB, 4, MidpointRounding.AwayFromZero);
        var lines = new List<QuickLine>
        {
            new("A 单价", unitA.ToString("0.0000", CultureInfo.InvariantCulture)),
            new("B 单价", unitB.ToString("0.0000", CultureInfo.InvariantCulture)),
        };
        var diff = unitA - unitB;
        if (diff == 0) lines.Add(new("结论", "两者单价相同"));
        else
        {
            var cheaper = diff > 0 ? "B" : "A";
            var dearer = diff > 0 ? "A" : "B";
            var gap = Math.Abs(diff) / Math.Min(unitA, unitB) * 100m;
            lines.Add(new("结论", $"{cheaper} 更划算，比 {dearer} 便宜 {Number(gap)}%"));
        }
        return new QuickResult(lines, null);
    }

    // ─────────────── 两个日期相差多久 ───────────────

    /// <summary>
    /// <paramref name="from"/> 到 <paramref name="to"/> 相差多少天，附"折合几周几天"与工作日数。
    /// 方向可反：<paramref name="to"/> 早于 <paramref name="from"/> 时天数带负号（"距今 266 天前"就是 -266），
    /// 并在结果里说明是"结束日早于开始日"，不给出一个看起来像未来的正数。
    /// <para><b>工作日只按周一到周五算</b>，不含法定节假日与调休——这句话必须出现在结果里，
    /// 否则用户会拿它去算请假，而节假日安排每年都不一样。</para>
    /// </summary>
    public static QuickResult DateSpan(DateTime from, DateTime to)
    {
        from = from.Date;
        to = to.Date;
        var days = (to - from).Days;
        var abs = Math.Abs(days);
        var sign = days < 0 ? -1 : 1;

        var lines = new List<QuickLine>
        {
            new("相差", (sign < 0 ? "-" : "") + abs.ToString(CultureInfo.InvariantCulture) + " 天"),
            new("折合", abs / 7 + " 周 " + abs % 7 + " 天"),
            new("工作日(含首尾)", CountWorkdays(from, to).ToString(CultureInfo.InvariantCulture)),
        };
        if (abs == 0) lines.Add(new("提示", "同一天"));
        else lines.Add(new("说明", sign > 0 ? "已含结束日" : "结束日早于开始日"));
        lines.Add(new("注意", "工作日不含法定节假日与调休"));
        return new QuickResult(lines, null);
    }

    /// <summary>周一到周五的天数，区间<b>含首含尾</b>；两端先后顺序无关（内部会交换）。</summary>
    public static int CountWorkdays(DateTime from, DateTime to)
    {
        var start = from.Date;
        var end = to.Date;
        if (end < start) (start, end) = (end, start);
        var count = 0;
        for (var day = start; day <= end; day = day.AddDays(1))
            if (day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday) count++;
        return count;
    }

    /// <summary>
    /// 解析界面上的日期输入：<c>2026-12-31</c> / <c>2026/12/31</c> / <c>2026.12.31</c> / <c>2026年12月31日</c>，
    /// 以及只写月日的 <c>12-31</c>（按 <paramref name="today"/> 的年份补全——"距年底还有几天"不必打全年份）。
    /// 解析不了返回 null：界面上那句"日期没认出来"就是从这里来的，不让用户猜。
    /// </summary>
    public static DateTime? ParseDate(string? text, DateTime today)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0) return null;
        // 与倒计时同一份格式约定：先按 ISO，再退到本地常用写法；不引第三方解析库
        foreach (var format in new[] { "yyyy-M-d", "yyyy/M/d", "yyyy.M.d", "yyyy年M月d日" })
            if (DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                return exact.Date;
        if (text.Length <= 5 && (text.Contains('-') || text.Contains('/')))
        {
            var parts = text.Split('-', '/');
            if (parts.Length == 2 && int.TryParse(parts[0], out var month) && int.TryParse(parts[1], out var day))
            {
                try { return new DateTime(today.Year, month, day); }
                catch (ArgumentOutOfRangeException) { return null; }
            }
        }
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose)
            ? loose.Date
            : null;
    }

    private static QuickResult Fail(string reason) => new(Array.Empty<QuickLine>(), reason);
}
