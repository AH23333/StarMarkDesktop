#nullable enable
using System;
using System.Globalization;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;

namespace StarMark.Core.Calc;

/// <summary>
/// 一条速算结果。<b>两头都交键</b>（批次 WI-55b／P-182b）：标签是"这一行说什么"，值常常是一整句
/// （"需涨 25%"、"减 2 次共 100.00"），把它们存成成品字＝语言被定死在算出它的那一刻，
/// 换语言那一刻没人回去重算（✅WI-46c 那颗组名／✅WI-54 那个默认名同一条界线）。
/// <para>纯数字那一半走一颗骨架 <c>calc.num</c>（那一格没有词可翻，⛔ 为此再造十个三列同形的行）。</para>
/// </summary>
public sealed record QuickLine(LocMessage Label, LocMessage Value);

/// <summary>
/// 一次速算的结果：<see cref="Error"/> 非空表示输入不成立，此时 <see cref="Lines"/> 不给出。
/// <b>不给"看着像数"的 0</b>——0 折和"没填折扣"、除以 0 的商和"确实是 0"在界面上必须长得不一样。
/// </summary>
public sealed record QuickResult(IReadOnlyList<QuickLine> Lines, LocMessage? Error)
{
    public bool Ok => Error is null;

    /// <summary>
    /// 每项一行，给组件里的结果显示与复制到剪贴板用（多条速算结果挤在一行读不清）。
    /// <para>念法在<b>被读的那一刻</b>现算：这一颗是屏幕上那一行的唯一拼装处，宿主换语言时重跑它一次
    /// （见 <c>CalcWidgetViewModel.RebuildWords</c>）。早先还有一颗单行版的 <c>Text</c>，全仓没有消费者，
    /// 连同它那套"　"分隔一起删掉——两颗 join 各自漂移过不了多久就会念出两种话。</para>
    /// </summary>
    public string MultiLine
    {
        get
        {
            if (!Ok) return Error!.Render();
            var parts = new string[Lines.Count];
            for (var i = 0; i < Lines.Count; i++) parts[i] = Lines[i].Label.Render() + "  " + Lines[i].Value.Render();
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
/// <para>
/// 本档⛔ 取词，也⛔ 出现一句界面用词：每一行交的是 <see cref="LocMessage"/>（键＋实参），
/// 拒绝句里引用的字段名交的是<b>还没渲染的</b>那颗键（实参错位＝屏幕上那一格空白，坑表 #393）。
/// </para>
/// </summary>
public static class QuickMath
{
    /// <summary>金额显示：两位小数并带千分位（与求值器同一套分节号写法，复制回去还能继续算）。</summary>
    public static string Money(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("#,0.00", CultureInfo.InvariantCulture);

    /// <summary>普通数字：最多两位小数，整数不带小数点。</summary>
    public static string Number(decimal value)
        => NumberText.UpTo2(Math.Round(value, 2, MidpointRounding.AwayFromZero));

    /// <summary>纯读数那一半的骨架：那一格只有数字，没有词可翻（批次 WI-55b）。</summary>
    private static LocMessage Num(string value) => LocMessage.Of("calc.num", value);

    /// <summary>带百分号的读数：号留在句子那一侧（⛔ 在代码里拼 <c>+ "%"</c>，那是✅坑表 #371 那一族）。</summary>
    private static LocMessage Percent(string value) => LocMessage.Of("calc.num.percent", value);

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
        if (original <= 0) return Fail(LocMessage.Of("calc.quick.rej.original"));
        if (discount <= 0 || discount > 100) return Fail(LocMessage.Of("calc.quick.rej.discount"));
        if (everyFull < 0 || reduceEach < 0) return Fail(LocMessage.Of("calc.quick.rej.negative"));
        // 那两句"填了这颗就要填那颗"引用的是屏幕上那两行标签 ⇒ 名字交那颗键（同一个主人），⛔ 抄进句子（改了字段说法这句就是假话）
        if (everyFull > 0 && reduceEach <= 0) return Fail(Pair(QuickCalculator.Field.EveryFull, QuickCalculator.Field.Reduce));
        if (everyFull == 0 && reduceEach > 0) return Fail(Pair(QuickCalculator.Field.Reduce, QuickCalculator.Field.EveryFull));
        if (everyFull > 0 && reduceEach >= everyFull)
            return Fail(LocMessage.Of("calc.quick.rej.reduceTooBig", LocMessage.Of(QuickCalculator.Field.EveryFull)));

        var rate = discount <= 10 ? discount / 10m : discount / 100m;
        var afterDiscount = original * rate;
        var times = everyFull > 0 ? Math.Floor(afterDiscount / everyFull) : 0m;
        var reduction = times * reduceEach;
        var final = afterDiscount - reduction;
        var saved = original - final;

        var lines = new List<QuickLine>
        {
            new(LocMessage.Of("calc.quick.line.final"), Num(Money(final))),
            new(LocMessage.Of("calc.quick.line.saved"), Num(Money(saved))),
            new(LocMessage.Of("calc.quick.line.ratio"), Percent(Number(final / original * 100m))),
        };
        // "几折"那一档要把折数一起念出来（7.5 折），按百分比填的那一档只念钱：两句话不是同一件事实，各开一行
        lines.Insert(0, discount > 10
            ? new QuickLine(LocMessage.Of("calc.quick.line.afterDiscount"), Num(Money(afterDiscount)))
            : new QuickLine(LocMessage.Of("calc.quick.line.afterDiscount"),
                LocMessage.Of("calc.quick.value.discountTenths", Money(afterDiscount), Number(discount))));
        if (times > 0) lines.Add(new QuickLine(LocMessage.Of("calc.quick.line.spendSave"),
            LocMessage.Of("calc.quick.value.times", times.ToString("0", CultureInfo.InvariantCulture), Money(reduction))));
        return new QuickResult(lines, null);
    }

    /// <summary>「填了 A 就要填 B」那句：两颗名字按调用点给的顺序嵌进去（⛔ 在这里拼「」）。</summary>
    private static LocMessage Pair(string firstKey, string secondKey)
        => LocMessage.Of("calc.quick.rej.pair", LocMessage.Of(firstKey), LocMessage.Of(secondKey));

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
        if (from <= 0) return Fail(LocMessage.Of("calc.quick.rej.startBase"));
        if (to < 0) return Fail(LocMessage.Of("calc.quick.rej.currentNegative"));
        var delta = to - from;
        var percent = delta / from * 100m;
        var lines = new List<QuickLine>
        {
            new(LocMessage.Of("calc.quick.line.delta"), Num(Signed(delta, Money(delta)))),
            new(LocMessage.Of("calc.quick.line.change"), Percent(Signed(percent, Number(percent)))),
        };
        if (to == 0) lines.Add(new QuickLine(LocMessage.Of("calc.quick.line.backEven"), LocMessage.Of("calc.quick.value.zeroed")));
        else
        {
            // 回本方向与涨跌相反，故这里取绝对值、方向交给句子：写"需跌 -16.67%"是否定之否定，读的人得再翻一次。
            var back = (from - to) / to * 100m;
            var label = LocMessage.Of("calc.quick.line.backTo", Number(from));
            lines.Add(back == 0
                ? new QuickLine(label, LocMessage.Of("calc.quick.value.already"))
                : new QuickLine(label, LocMessage.Of(back > 0 ? "calc.quick.value.needUp" : "calc.quick.value.needDown",
                    Number(Math.Abs(back)))));
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
        if (priceA <= 0 || priceB <= 0) return Fail(LocMessage.Of("calc.quick.rej.prices"));
        if (qtyA <= 0 || qtyB <= 0) return Fail(LocMessage.Of("calc.quick.rej.quantities"));

        var unitA = Math.Round(priceA / qtyA, 4, MidpointRounding.AwayFromZero);
        var unitB = Math.Round(priceB / qtyB, 4, MidpointRounding.AwayFromZero);
        var lines = new List<QuickLine>
        {
            // 组名 A／B 是数据（与输入槽那一排同一个字母），⛔ 为它开一行：那一格三语都写 A
            new(LocMessage.Of("calc.quick.line.unitPrice", "A"), Num(unitA.ToString("0.0000", CultureInfo.InvariantCulture))),
            new(LocMessage.Of("calc.quick.line.unitPrice", "B"), Num(unitB.ToString("0.0000", CultureInfo.InvariantCulture))),
        };
        var diff = unitA - unitB;
        if (diff == 0) lines.Add(new QuickLine(LocMessage.Of("calc.quick.line.verdict"), LocMessage.Of("calc.quick.value.sameUnit")));
        else
        {
            var cheaper = diff > 0 ? "B" : "A";
            var dearer = diff > 0 ? "A" : "B";
            var gap = Math.Abs(diff) / Math.Min(unitA, unitB) * 100m;
            lines.Add(new QuickLine(LocMessage.Of("calc.quick.line.verdict"),
                LocMessage.Of("calc.quick.value.cheaper", cheaper, dearer, Number(gap))));
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
            // "N 天"这颗与 ✅WI-54 的倒计时、RSS 节奏同一行（time.unit.days），⛔ 为速算再开一颗同义行
            new(LocMessage.Of("calc.quick.line.daysBetween"), LocMessage.Of("time.unit.days", days)),
            new(LocMessage.Of("calc.quick.line.weeks"), LocMessage.Of("calc.quick.value.weeksDays", abs / 7, abs % 7)),
            new(LocMessage.Of("calc.quick.line.workdays"), Num(CountWorkdays(from, to).ToString(CultureInfo.InvariantCulture))),
        };
        lines.Add(abs == 0
            ? new QuickLine(LocMessage.Of("calc.quick.line.hint"), LocMessage.Of("calc.quick.value.sameDay"))
            : new QuickLine(LocMessage.Of("calc.quick.line.explain"),
                LocMessage.Of(sign > 0 ? "calc.quick.value.inclusiveEnd" : "calc.quick.value.endBeforeStart")));
        lines.Add(new QuickLine(LocMessage.Of("calc.quick.line.caution"), LocMessage.Of("calc.quick.value.workdayNote")));
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
    /// <para>⚠ 那份格式表是<b>输入</b>的形状，⛔ 上屏幕（唯一一处带汉字的行因此留在本档，由
    /// <c>CalcWordGateTests</c> 点名豁免而不是把尺改松）。</para>
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

    private static QuickResult Fail(LocMessage reason) => new(Array.Empty<QuickLine>(), reason);
}
