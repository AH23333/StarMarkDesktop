#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Calc;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-55（P-182b＝计算器那一族）的闸门：<b>整扇窗上每句话只许表里一份出处，而且 Core 交出的是键</b>。
/// <para>
/// 射程按<b>屏幕上那一处宿主</b>划（台架规则 #115）＝计算器那一扇窗的三个页签（计算／常用／换算）。
/// 切页签不重建元素 ⇒ 只交一档就是"族内半中半英"，所以三档一起进这一档的名单里读。
/// ⚠ 与 ✅WI-51 同一颗教训：旧尺只扫 code-behind，<c>.xaml</c> 那 23 行从没进过名单 ⇒ 这里把标记一起摆进射程。
/// </para>
/// <para>⚠ 两类证人分着钉（<c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>，坑表 #370）：
/// <b>源码形状</b>与<b>能真跑的 Core 读数</b>。本批那一族真跑得到的话是"换算结果句＋三句拒绝句"，
/// 而结果句里嵌着两颗单位名 ⇒ 它同时是✅WI-2k 那颗"嵌套读数按当下语言渲染"的第二次兑现。</para>
/// <para>尺＝抹注释后逐行取汉字／假名片段（坑表 #375／#378：按调用形状数会漏掉内插串与标记里的面字）。
/// ⚠ 全角运算符（＋－（【）那一族）⛔ 算"词"——它们是输入格式，分词器认的是字符不是话。</para>
/// </summary>
public sealed class CalcWordGateTests : IDisposable
{
    private const string UnitTablesCs = "src/StarMark.Core/Calc/UnitTables.cs";
    private const string ViewModelCs = "src/StarMark.UI/ViewModels/CalcWidgetViewModel.cs";
    private const string WidgetCs = "src/StarMark.UI/Views/CalcWidget.xaml.cs";
    private const string WidgetXaml = "src/StarMark.UI/Views/CalcWidget.xaml";
    private const string QuickMathCs = "src/StarMark.Core/Calc/QuickMath.cs";
    private const string QuickCalculatorCs = "src/StarMark.Core/Calc/QuickCalculator.cs";

    /// <summary>汉字／假名那几段码位：全角运算符（＋－（【）不在里面——它们是输入形状，不是话。</summary>
    private static readonly Regex Cjk = new(@"[㐀-䶿一-鿿぀-ヿ]", RegexOptions.Compiled);

    /// <summary>位置式占位符（与通用闸门同一形状：去重后按数字序，见 <c>I18nContractTests</c> 那颗同名的）。</summary>
    private static readonly Regex Slots = new(@"\{(\d+)\}", RegexOptions.Compiled);

    /// <summary>换算页那六句（三句拒绝＋一句骨架＋一句"没填对"＋一句兜底）。其余 66 颗是名字，由运行时表自己数出来。</summary>
    private static readonly string[] Sentences =
    {
        "calc.convert.result", "calc.convert.noCategory", "calc.convert.badUnit",
        "calc.convert.zeroRate", "calc.convert.needNumber", "calc.convert.failed",
    };

    /// <summary>
    /// 这一格走不到的三颗，各有各的原因（⛔ 把它们算成"测过了"）：
    /// <c>zeroRate</c> 要表里配一颗 <c>ToBase==0</c> 的单位才进得了；<c>failed</c> 与 <c>needNumber</c> 住在 VM，
    /// 而 <c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>（坑表 #370）。
    /// 三颗仍由 <see cref="EveryRowThisBatchShippedIsActuallyAskedFor"/> 拦"有没有人在问"，
    /// 由 <see cref="NoRowOfThisBatchCarriesAControlCharacter"/> 拦那一行的形状。
    /// </summary>
    private static readonly string[] NotReachableHere =
    [
        "calc.convert.zeroRate", "calc.convert.failed", "calc.convert.needNumber",
    ];

    private readonly AppLanguage _before = Locale.Current;

    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string MarkupOf(string rel) => Markup(SourceGate.ReadRepoFile(rel));

    private static IReadOnlyList<string> NameKeys()
        => UnitTables.Categories.Select(c => c.LabelKey)
            .Concat(UnitTables.Categories.SelectMany(c => c.Units.Select(u => u.LabelKey)))
            .ToArray();

    /// <summary>
    /// VM 递实参的那一颗骨架，照<b>调用点的形状</b>搭出来：两个读数＋<b>还没渲染的</b>两颗单位名。
    /// <para>⚠ 实参交出去的那一刻若已 <c>Locale.T(...)</c> 取完词，语言就被定死在算出它的那一瞬间，
    /// 换语言只重贴外层＝屏幕上半中半英（坑表 #328／#361）。这里搭的就是 <c>RecalcUnit</c> 那一行。</para>
    /// </summary>
    private static LocMessage ResultSentence()
    {
        var from = UnitTables.Category(UnitTables.TemperatureKey)!.Find("c")!;
        var to = UnitTables.Category(UnitTables.TemperatureKey)!.Find("f")!;
        UnitTables.TryConvert(UnitTables.TemperatureKey, from.Key, to.Key, 100m, out var result, out _);
        return LocMessage.Of("calc.convert.result",
            UnitTables.Format(100m), LocMessage.Of(from.LabelKey),
            UnitTables.Format(result), LocMessage.Of(to.LabelKey));
    }

    /// <summary>
    /// <b>每一颗交出去的键都得按代码实际给的实参渲染得出来</b>：槽数与实参数对不上，<c>string.Format</c> 当场抛
    /// <c>FormatException</c>，界面上是那一行<b>整格空白</b>（坑表 #393，本批前一刀就是这么红的）。
    /// <para>拒绝句走<b>真调用</b>取（⛔ 在测试里再写一份 <c>LocMessage.Of("...")</c>，那样测的是我自己）。</para>
    /// </summary>
    [Fact]
    public void EveryRowThisBatchShippedRendersWithTheArgsTheCodeActuallyHands()
    {
        var handed = new List<LocMessage> { ResultSentence() };
        Assert.False(UnitTables.TryConvert("nope", "m", "km", 1m, out _, out var noCategory));
        handed.Add(noCategory!);
        Assert.False(UnitTables.TryConvert(UnitTables.LengthKey, "zz", "km", 1m, out _, out var badUnit));
        handed.Add(badUnit!);
        Assert.True(noCategory!.Args is null or { Length: 0 });
        Assert.True(badUnit!.Args is null or { Length: 0 });

        foreach (var key in NameKeys()) handed.Add(LocMessage.Of(key));

        var reachable = Sentences.Where(k => !NotReachableHere.Contains(k, StringComparer.Ordinal))
            .Concat(NameKeys()).ToArray();
        var unasked = reachable.Where(key => !handed.Any(m => m.Key == key)).ToList();
        Assert.True(unasked.Count == 0, "这些键本批交了表⛔ 真跑里没有一格走到它：\n  " + string.Join("\n  ", unasked));

        foreach (var message in handed)
        {
            Assert.True(UiStrings.TryGet(message.Key, out var row), $"交出的键没登记在表里：{message.Key}");
            var slots = Slots.Matches(row.Zh).Cast<Match>()
                .Select(m => m.Groups[1].Value).Distinct().Count();
            var args = message.Args?.Length ?? 0;
            Assert.True(slots == args,
                $"{message.Key} 中文列有 {slots} 个槽，代码递了 {args} 颗实参（对不上⇒屏幕上那一格空白，坑表 #393）");
            var rendered = message.Render();
            Assert.False(string.IsNullOrWhiteSpace(rendered), $"{message.Key} 渲染出来是空的");
            Assert.DoesNotContain("{", rendered, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <b>嵌在结果句里的那两颗单位名跟着语言当场改口</b>（✅WI-2k 那颗机制在换算页的兑现）。
    /// <para>期望那一侧<b>现取同一颗键</b>（规则 #130）：⛔ 把"摄氏度 ℃"抄进断言——那会让"只改措辞"变成一次红。</para>
    /// </summary>
    [Fact]
    public void TheNestedUnitNamesAreRenderedInTheLanguageOfTheMoment()
    {
        Locale.TrySet(AppLanguage.Chinese);
        var chineseWord = Locale.T("calc.unit.celsius");
        var chineseRender = ResultSentence().Render();

        Locale.TrySet(AppLanguage.English);
        Assert.NotEqual(chineseRender, ResultSentence().Render());          // 同一颗对象，换个语言必须换个念法
        Assert.Contains(Locale.T("calc.unit.celsius"), ResultSentence().Render(), StringComparison.Ordinal);
        Assert.DoesNotContain(chineseWord, ResultSentence().Render(), StringComparison.Ordinal);

        Locale.TrySet(AppLanguage.Japanese);
        var japaneseRender = ResultSentence().Render();
        Assert.NotEqual(chineseRender, japaneseRender);
        Assert.Contains(Locale.T("calc.unit.celsius"), japaneseRender, StringComparison.Ordinal);
        Assert.Contains(Locale.T("calc.unit.fahrenheit"), japaneseRender, StringComparison.Ordinal);
    }

    /// <summary>走「数据项＋模板」的那些行⛔ 带槽（✅WI-43 那颗教训：模板只交一颗键，没人给那句填空）。</summary>
    [Fact]
    public void TheDropdownNamesCarryNoSlotsAndAreAllInTheTable()
    {
        var keys = NameKeys();
        Assert.True(keys.Count >= 60, $"运行时表只数出 {keys.Count} 颗名字（现场是 66＝8 类别＋58 单位）");
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());   // 一颗名字配两行＝两个主人
        foreach (var key in keys)
        {
            Assert.True(UiStrings.TryGet(key, out var row), $"名字那颗键没进表：{key}");
            Assert.DoesNotMatch(@"\{\d+\}", row.Zh);
            Assert.DoesNotMatch(@"\{\d+\}", row.En);
            Assert.DoesNotMatch(@"\{\d+\}", row.Ja);
        }
    }

    /// <summary>
    /// <b>表体⛔ 一句成品话</b>：抹掉注释后这一档一行汉字／假名都不许剩。
    /// <para>⚠ 一般式（规则 #131）：钉的是"这一档里有汉字／假名的行"，⛔ 钉上次看到的那几种写法。</para>
    /// </summary>
    [Fact]
    public void TheUnitTableCarriesNoWordsOfItsOwn()
    {
        var left = WordLines(UnitTablesCs);
        Assert.True(left.Length == 0, $"{UnitTablesCs} 还剩 {left.Length} 行自己的汉字／假名面字：\n  "
                                      + string.Join("\n  ", left));
    }

    /// <summary>
    /// <b>每一颗名字交的都是那颗键</b>：新增一行换个写法（把成品字塞回 <c>Label</c>、或把键写成别的族）也会被这一格咬住。
    /// <para>行数按<b>运行时表</b>数，⛔ 钉一个我抄下来的常数（规则 #108：颗数相等型判据得让"多加一颗合规单位"仍然绿）。</para>
    /// </summary>
    [Fact]
    public void EveryNameIsAKeyOfItsOwnFamily()
    {
        var code = CodeOf(UnitTablesCs);

        var unitRows = Regex.Matches(code, @"new CalcUnit\(\s*(?:""[^""]*""|[A-Za-z]+),\s*""([^""]*)""")
            .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(UnitTables.Categories.Sum(c => c.Units.Count), unitRows.Length);
        Assert.All(unitRows, name => Assert.StartsWith("calc.unit.", name, StringComparison.Ordinal));
        Assert.Equal(unitRows.Length, unitRows.Distinct(StringComparer.Ordinal).Count());   // 两个单位共用一行＝下拉里两格念同一个名字

        var categoryRows = Regex.Matches(code, @"new CalcUnitCategory\(\s*[A-Za-z]+,\s*""([^""]*)""")
            .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(UnitTables.Categories.Count, categoryRows.Length);
        Assert.All(categoryRows, name => Assert.StartsWith("calc.cat.", name, StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>本批交的 72 行每一行都有人问，而表里⛔ 留没人问的死行</b>（双向对账，规则 #111）。
    /// </summary>
    [Fact]
    public void EveryRowThisBatchShippedIsActuallyAskedFor()
    {
        var asked = CodeOf(UnitTablesCs) + "\n" + CodeOf(ViewModelCs);
        var shipped = NameKeys().Concat(Sentences).ToArray();
        Assert.Equal(72, shipped.Length);

        var orphan = shipped.Where(key => !asked.Contains('"' + key + '"', StringComparison.Ordinal)).ToList();
        Assert.True(orphan.Count == 0, $"这些键交了表却没有一处源码在问：\n  " + string.Join("\n  ", orphan));

        // 反方向：表里带 calc.unit.／calc.cat. 前缀的行必须都在运行时表里（有人手抄一行而没接进表 ⇒ 死行）
        var live = new HashSet<string>(NameKeys(), StringComparer.Ordinal);
        var dead = UiStrings.All
            .Where(e => (e.Key.StartsWith("calc.unit.", StringComparison.Ordinal)
                         || e.Key.StartsWith("calc.cat.", StringComparison.Ordinal))
                        && !live.Contains(e.Key))
            .Select(e => e.Key).ToArray();
        Assert.True(dead.Length == 0, "表里有没人问的名字行（⛔ 留一颗死行）：\n  " + string.Join("\n  ", dead));
    }

    /// <summary>
    /// 四颗下拉是<b>数据项＋模板</b>：⛔ <c>DisplayMemberPath="Label"</c>——那条路把表里的成品字直接搬到屏幕上，
    /// 换语言那一刻闭着的那一格念的还是旧语言（✅WI-2c／✅WI-43 同一颗判据，本批把最后一族下拉交出来）。
    /// <para>那颗模板是 14 处下拉共用的主人（主窗 2／设置页 7／热榜 1／计算器 4），射程记在 <c>StarMarkTheme.xaml</c> 的注释里。</para>
    /// </summary>
    [Fact]
    public void TheFourDropdownsAreDataItemsNotAMemberPath()
    {
        var markup = MarkupOf(WidgetXaml);
        Assert.Equal(0, SourceGate.Count(markup, "DisplayMemberPath"));
        Assert.Equal(4, SourceGate.Count(markup, @"ItemTemplate=""{StaticResource LocOptionTemplate}"""));
        // 三颗下拉的数据源交的是"项"而不是单位对象本身（身份在 LocOption.Value 里，⛔ 拿显示文字当身份）
        Assert.Equal(1, SourceGate.Count(markup, "ViewModel.CategoryOptions"));
        Assert.Equal(2, SourceGate.Count(markup, "ViewModel.UnitOptions"));
        Assert.Equal(1, SourceGate.Count(markup, "ViewModel.QuickModeOptions"));

        var total = Directory.GetFiles(Path.Combine(SourceGate.RepoRoot(), "src"), "*.xaml", SearchOption.AllDirectories)
            .Select(f => f.Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal))
            .Sum(f => SourceGate.Count(Markup(File.ReadAllText(f)), "StaticResource LocOptionTemplate"));
        Assert.Equal(14, total);
    }

    /// <summary>
    /// <b>本批修掉的那颗真缺陷</b>：换算页的输入框只调 <c>RecalcUnit()</c> 而从不把文本交给 VM，
    /// 于是 VM 永远拿"1"去算——改数字之后屏幕上那个结果一动不动。读调用点才看得见（两把尺：形状尺数得出调用，数不出实参）。
    /// </summary>
    [Fact]
    public void TheUnitValueBoxHandsItsTextToTheViewModel()
    {
        var body = SourceGate.MethodBody(CodeOf(WidgetCs), "private void UnitValueBox_TextChanged");
        Assert.Contains("ViewModel.UnitValueText = UnitValueBox.Text;", body, StringComparison.Ordinal);
        Assert.Contains("ViewModel.RecalcUnit()", body, StringComparison.Ordinal);
        // 算的那一头读的必须就是交接过来的那一颗（⛔ 改回"看别处"，那又一辈子是 1）
        var recalc = SourceGate.MethodBody(CodeOf(ViewModelCs), "public void RecalcUnit");
        Assert.Contains("UnitValueText", recalc, StringComparison.Ordinal);
    }

    /// <summary>
    /// VM 只在<b>读数那一刻</b>取词，而且结果句里那两颗名字交的是<b>还没渲染的</b> <see cref="LocMessage"/>。
    /// <para>方法体切出来再读：⛔ 整档计数（下一刀别的页签也要取词，整档计数会逼后人改尺）。</para>
    /// </summary>
    [Fact]
    public void TheVmFetchesWordsAtTheMomentItReadsAndNeverPreRendersAName()
    {
        var recalc = SourceGate.MethodBody(CodeOf(ViewModelCs), "public void RecalcUnit");
        Assert.Contains("LocMessage.Of(from.LabelKey)", recalc, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(to.LabelKey)", recalc, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.T(from.LabelKey", recalc, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.T(to.LabelKey", recalc, StringComparison.Ordinal);
        Assert.Contains(@"Locale.Fmt(""calc.convert.result""", recalc, StringComparison.Ordinal);

        // 那颗"数据项"⛔ 把词存进 Text：存了就是第二份出处，而且换语言不会再算一遍
        var options = SourceGate.MethodBody(CodeOf(ViewModelCs), "private static IReadOnlyList<LocOption> UnitOptionsOf");
        Assert.Contains("string.Empty", options, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.T(", options, StringComparison.Ordinal);
    }

    /// <summary>本批那 72 行的三列都不许混进控制字符（表不过 XAML 解析器，换行是布局不是文案，规则 #100）。</summary>
    [Fact]
    public void NoRowOfThisBatchCarriesAControlCharacter()
    {
        var keys = NameKeys().Concat(Sentences).ToArray();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.True(UiStrings.TryGet(key, out var row), key + " 不在表里");
            foreach (var (text, column) in new[] { (row.Zh, "中文"), (row.En, "英文"), (row.Ja, "日文") })
                Assert.True(text.All(c => c >= ' '), $"{key} 的{column}列含控制字符（那一行必须写成 verbatim 或换掉写法）");
        }
    }

    // ───────────────────────── 批次 WI-55b：常用速算那一族 ─────────────────────────

    /// <summary>本批 55b 交表的行：一切 <c>calc.quick.*</c> 加上那两颗纯骨架（由表本身数出来，⛔ 抄一份名单）。</summary>
    private static string[] ShippedQuick()
        => UiStrings.All
            .Where(e => e.Key.StartsWith("calc.quick.", StringComparison.Ordinal)
                        || e.Key.StartsWith("calc.num", StringComparison.Ordinal))
            .Select(e => e.Key).ToArray();

    private static readonly DateTime Day = new DateTime(2026, 5, 5);

    /// <summary>
    /// 住在 code-behind、<c>StarMark.Tests</c> 点不着的一颗（坑表 #370）：<c>calc.quick.optionalField</c> 由
    /// <c>RepaintQuickSlots</c> 现场 <c>Locale.Fmt(…, LocMessage.Of(field.LabelKey))</c> 递出——一颗实参、一个槽。
    /// ⛔ 因此把这一行摘出射程：<see cref="EveryQuickRowIsAskedForAndNoAskedKeyIsMissingFromTheTable"/> 仍在代码里数它有没有人被问，
    /// <see cref="TheOptionalSlotLabelIsASentenceThatTakesTheFieldNameAsAnArg"/> 钉那颗调用与那一行的槽数（名单⛔ 长大：新加一颗就得写理由）。
    /// </summary>
    private static readonly string[] UiSideQuick = ["calc.quick.optionalField"];

    /// <summary>把一组结果行的<b>标签与读数都</b>摆进名单：只取读数那一半会把"只有归零／持平才出现的那颗标签"漏成死行。</summary>
    private static void AddLines(ICollection<LocMessage> into, QuickResult r)
    {
        Assert.True(r.Ok, r.Error?.Key);
        foreach (var line in r.Lines)
        {
            into.Add(line.Label);
            into.Add(line.Value);
        }
    }

    private static LocMessage ErrorOf(QuickResult r)
    {
        Assert.False(r.Ok, "这一格要的正是失败那一句，结果算出来了");
        Assert.NotNull(r.Error);
        return r.Error!;
    }

    /// <summary>
    /// 把速算这一族<b>真跑得到的每一颗键</b>各配一个真的会走到的场景：四模式的样例输入（标签／说明／槽名／占位／
    /// 每一行读数）＋每一条拒绝句＋涨跌与单价与日期那三个"只有换个输入才走得到"的臂。
    /// </summary>
    private static LocMessage[] RenderedByTheQuickFamily()
    {
        var samples = new[]
        {
            new[] { "800", "9", "300", "50" },        // 打折＋满减（折后带"几折"、满减那一行）
            new[] { "100", "80" },                    // 跌 20% ⇒ 需涨 25%
            new[] { "29.9", "500", "39.9", "800" },   // 单价比较：结论带两个组名
            new[] { "2026-10-01", "2026-10-11" },     // 间隔天数：折合／工作日／说明／注意
        };
        var messages = new List<LocMessage>();
        for (var i = 0; i < samples.Length; i++)
        {
            var mode = QuickCalculator.Modes[i];
            messages.Add(LocMessage.Of(mode.TitleKey));
            messages.Add(LocMessage.Of(mode.HintKey));
            foreach (var field in mode.Fields)
            {
                messages.Add(LocMessage.Of(field.LabelKey));
                if (field.PlaceholderKey is { } placeholder) messages.Add(LocMessage.Of(placeholder));
            }
            var r = QuickCalculator.Evaluate(i, samples[i], Day);
            Assert.True(r.Ok, $"模式 {i} 的样例输入应当算得出来，实际那颗键：{r.Error?.Key}");
            AddLines(messages, r);
        }

        // 只有换一组输入才走得到的臂（⛔ 拿"sample 里没出现"当"这行永远念不出来"的证据，规则 #111）
        messages.Add(LocMessage.Of("calc.num", QuickMath.Money(1m)));                       // 按百分比收的那一档：折后只有钱
        AddLines(messages, QuickMath.Bargain(100m, 85m, 0m, 0m));                           // 不参加了满减：那一行整行不出现
        AddLines(messages, QuickMath.Change(100m, 120m));                                   // 需跌那一臂
        AddLines(messages, QuickMath.Change(50m, 50m));                                     // 已经是这个值
        AddLines(messages, QuickMath.Change(100m, 0m));                                     // 值已归零（那颗标签只有这一臂给得出）
        AddLines(messages, QuickMath.UnitPriceCompare(10m, 2m, 15m, 3m));                   // 两者单价相同
        AddLines(messages, QuickMath.DateSpan(Day, Day));                                   // 同一天那一臂
        AddLines(messages, QuickMath.DateSpan(new DateTime(2026, 6, 1), Day));               // 结束日早于开始日
        AddLines(messages, QuickCalculator.Evaluate(0, new[] { "100", "7.5", "", null }, Day));   // 可空槽留空＝那一行整行不出现

        // 拒绝句：一句一句按调用点真的递一次（嵌进去的那颗字段名是<b>还没渲染的</b> LocMessage）
        messages.Add(ErrorOf(QuickMath.Bargain(0m, 7.5m, 0m, 0m)));
        messages.Add(ErrorOf(QuickMath.Bargain(100m, 700m, 0m, 0m)));
        messages.Add(ErrorOf(QuickMath.Bargain(100m, 7.5m, -1m, 0m)));
        messages.Add(ErrorOf(QuickMath.Bargain(100m, 7.5m, 300m, 0m)));
        messages.Add(ErrorOf(QuickMath.Bargain(100m, 7.5m, 0m, 50m)));
        messages.Add(ErrorOf(QuickMath.Bargain(100m, 7.5m, 50m, 50m)));
        messages.Add(ErrorOf(QuickMath.Change(0m, 5m)));
        messages.Add(ErrorOf(QuickMath.Change(100m, -1m)));
        messages.Add(ErrorOf(QuickMath.UnitPriceCompare(0m, 1m, 2m, 1m)));
        messages.Add(ErrorOf(QuickMath.UnitPriceCompare(2m, 0m, 3m, 1m)));
        messages.Add(ErrorOf(QuickCalculator.Evaluate(9, new[] { "1", "2" }, Day)));
        messages.Add(ErrorOf(QuickCalculator.Evaluate(0, new[] { "", "7.5" }, Day)));
        messages.Add(ErrorOf(QuickCalculator.Evaluate(3, new[] { "2026-01-01", "" }, Day)));
        messages.Add(ErrorOf(QuickCalculator.Evaluate(3, new[] { "下周三", "2026-12-31" }, Day)));
        messages.Add(ErrorOf(QuickCalculator.Evaluate(1, new[] { "abc", "80" }, Day)));
        return messages.ToArray();
    }

    /// <summary>
    /// 与 55a 那颗同一把尺（坑表 #393）：<b>每一颗交出去的键都得按代码实际给的实参渲染得出来</b>，
    /// 槽数与实参数对不上＝屏幕上那一格整空白，而通用表闸门一条都不红。
    /// </summary>
    [Fact]
    public void EveryQuickRowRendersWithTheArgsTheCodeActuallyHands()
    {
        var handed = RenderedByTheQuickFamily();
        var shipped = ShippedQuick();
        Assert.True(shipped.Length >= 60, $"表里只数出 {shipped.Length} 行速算的键（现场是 68）——前缀筛错了就不是空转，是假绿");

        var unreachable = shipped
            .Where(key => !handed.Any(m => m.Key == key) && !UiSideQuick.Contains(key, StringComparer.Ordinal))
            .ToList();
        Assert.True(unreachable.Count == 0,
            "这些行交了表⛔ 真跑里没有一格走到它（要么场景少了＝证人空转，要么那一行永远念不出来）：\n  "
            + string.Join("\n  ", unreachable));

        foreach (var message in handed)
        {
            Assert.True(UiStrings.TryGet(message.Key, out var row), $"交出的键没登记在表里：{message.Key}");
            var slots = Slots.Matches(row.Zh).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().Count();
            var args = message.Args?.Length ?? 0;
            Assert.True(slots == args,
                $"{message.Key} 中文列有 {slots} 个槽，代码递了 {args} 颗实参（对不上⇒那一格屏幕上空白；"
                + "⛔ 把分隔词或字段名预先拼进实参凑成一颗——那正是这个形状，坑表 #371／#393）");
            var rendered = message.Render();
            Assert.False(string.IsNullOrWhiteSpace(rendered), $"{message.Key} 渲染出来是空的");
            Assert.DoesNotContain("{", rendered, StringComparison.Ordinal);
            // 实参只许是这几类：读数（string／int／decimal）或还没取词的 LocMessage。
            // ⛔ 在这里加"能自带措辞的类型"（DateTime／枚举／自定义 record 的 ToString）——那一类会把语言定死在算出它的那一刻。
            // 至于"字段名被预先拼成中文再递进来"，拦它的是 <see cref="TheCalculatorWindowCarriesNoWordsOfItsOwn"/>：代码里根本不许有汉字面字。
            foreach (var arg in message.Args ?? Array.Empty<object?>())
                Assert.True(arg is null or string or LocMessage or int or decimal,
                    $"{message.Key} 递了一颗没预料到的实参类型：" + arg?.GetType().Name);
        }
    }

    /// <summary>双向对账：表里 68 行速算的键每行都有人问，代码里⛔ 问一颗表里没有的（⛔ 死行，⛔ 悬空键）。</summary>
    [Fact]
    public void EveryQuickRowIsAskedForAndNoAskedKeyIsMissingFromTheTable()
    {
        var asked = CodeOf(QuickMathCs) + "\n" + CodeOf(QuickCalculatorCs) + "\n"
                  + CodeOf(ViewModelCs) + "\n" + CodeOf(WidgetCs);
        var shipped = ShippedQuick();
        var orphan = shipped.Where(key => !asked.Contains('"' + key + '"', StringComparison.Ordinal)).ToList();
        Assert.True(orphan.Count == 0, "这些行交了表却没有一处源码在问：\n  " + string.Join("\n  ", orphan));

        var dangling = Regex.Matches(asked, @"""(calc\.quick\.[A-Za-z0-9.]+|calc\.num[A-Za-z0-9.]*)""").Cast<Match>()
            .Select(m => m.Groups[1].Value).Distinct()
            .Where(key => !UiStrings.TryGet(key, out _)).ToList();
        Assert.True(dangling.Count == 0, "代码在问表里没有的键（屏幕上会显示成 ⟨key⟩）：\n  " + string.Join("\n  ", dangling));
    }

    /// <summary>抹注释后逐行取那一档里会<b>上屏幕</b>的汉字／假名面字（日志行按坑表 #368 那条界线豁免）。</summary>
    private static string[] WordLines(string rel)
        => CodeOf(rel).Replace("\r\n", "\n").Split('\n')
            .Where(line => Cjk.IsMatch(line) && !line.Contains("StarLog", StringComparison.Ordinal))
            .Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();

    /// <summary>
    /// "可空"那两个字是一句<b>收实参的话</b>（<c>calc.quick.optionalField</c>）：
    /// ⛔ 在标签后面拼 <c>+ "（可空）"</c>，也⛔ 抄进每一格占位文本里（那样五个槽就有五个主人）。
    /// </summary>
    [Fact]
    public void TheOptionalSlotLabelIsASentenceThatTakesTheFieldNameAsAnArg()
    {
        var widget = CodeOf(WidgetCs);
        Assert.DoesNotContain("(" + "可空)", widget, StringComparison.Ordinal);
        Assert.Contains(@"Locale.Fmt(""calc.quick.optionalField"", LocMessage.Of(field.LabelKey))", widget, StringComparison.Ordinal);
        Assert.True(UiStrings.TryGet("calc.quick.optionalField", out var row));
        Assert.Equal(new[] { "0" }, Slots.Matches(row.Zh).Cast<Match>().Select(m => m.Groups[1].Value).ToArray());
        // 占位文本里⛔ 再留一份"可空"：那是同一件事的第二处（✅WI-46c 那颗组名同一条界线）
        var calculator = CodeOf(QuickCalculatorCs);
        Assert.DoesNotContain("可空", calculator, StringComparison.Ordinal);
    }

    /// <summary>
    /// 换语言那一刻重铺四个槽，⛔ 走那颗会清空输入的 <c>ApplyQuickMode</c>：
    /// 清空四格是"换速算模式"的活（<c>CalcWidgetViewModel.SelectQuickMode</c> 一起作废上一组文本），
    /// 挂到换语言上就是吃掉用户正在打的字（与 55a 那颗方法体禁项同一族，这里钉的是"铺字只有一颗主人"）。
    /// </summary>
    [Fact]
    public void TheQuickSlotsAreRepaintedByOneOwnerThatNeverClearsInput()
    {
        var widget = CodeOf(WidgetCs);
        var repaint = SourceGate.MethodBody(widget, "private void RepaintQuickSlots");
        Assert.Contains("_quickLabels[i].Text =", repaint, StringComparison.Ordinal);
        Assert.Contains("_quickBoxes[i].PlaceholderText =", repaint, StringComparison.Ordinal);
        Assert.DoesNotContain(".Text = string.Empty", repaint, StringComparison.Ordinal);   // 铺字的那一颗⛔ 顺手清空
        var rebuild = SourceGate.MethodBody(widget, "private void RebuildWords");
        Assert.Contains("RepaintQuickSlots(", rebuild, StringComparison.Ordinal);
        Assert.DoesNotContain("box.Text = string.Empty", rebuild, StringComparison.Ordinal);   // 换语言⛔ 顺手吃掉用户正在打的字
        // 清空四格只有两颗主人，各管一件事：换模式（上一模式的旧值不能留着）与「清空」按钮。
        // ⛔ 把它们并成一颗公共 helper 再让换语言也去调它——那正是这一族坏掉的样子（铺字的与清空的分成两半，谁都能顺手调错的那一半）。
        Assert.Equal(2, SourceGate.Count(widget, "foreach (var box in _quickBoxes) box.Text = string.Empty;"));
        Assert.Contains("box.Text = string.Empty;",
            SourceGate.MethodBody(widget, "private void ApplyQuickMode"), StringComparison.Ordinal);
        Assert.Contains("box.Text = string.Empty;",
            SourceGate.MethodBody(widget, "private void ClearQuick_Click"), StringComparison.Ordinal);
        Assert.Contains("ApplyQuickMode(QuickModeBox.SelectedIndex)", widget, StringComparison.Ordinal);
    }

    /// <summary>速算下拉那一颗与换算那三颗同一条路：交 <c>LocOption</c>，⛔ 把标题存成成品字（VM 建表那一刻取词＝语言定死）。</summary>
    [Fact]
    public void TheQuickModeDropdownCarriesKeysNotFinishedTitles()
    {
        var options = SourceGate.MethodBody(CodeOf(ViewModelCs), "public CalcWidgetViewModel");
        Assert.Contains("LocOption(m.TitleKey, string.Empty, index)", options, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.T(m.TitleKey", options, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickModeTitles", CodeOf(ViewModelCs) + CodeOf(WidgetCs), StringComparison.Ordinal);
    }

    // ───────────────────────── 批次 WI-55c：计算那一档（求值器 19 句＋八句瞬时提示） ─────────────────────────

    private const string EvaluatorCs = "src/StarMark.Core/Calc/ExpressionEvaluator.cs";

    /// <summary>本刀交表的行：一切 <c>calc.expr.*</c> 与 <c>calc.hint.*</c>（由表本身数出来，⛔ 抄一份名单）。</summary>
    private static string[] ShippedExpr()
        => UiStrings.All
            .Where(e => e.Key.StartsWith("calc.expr.", StringComparison.Ordinal)
                        || e.Key.StartsWith("calc.hint.", StringComparison.Ordinal))
            .Select(e => e.Key).ToArray();

    /// <summary>
    /// 这一格走不到的那些，各有各<b>可核对</b>的理由（⛔ 把"我没构造出输入"写成"这行永远念不出来"，⛔ 反过来把走不到的摘进名单换绿灯）：
    /// <list type="bullet">
    /// <item><c>expr.unspecified</c>／<c>expr.lexCrash</c>／<c>expr.evalCrash</c>／<c>expr.numberRange</c>＝四道兜底
    /// （两颗 catch、一句"失败却没给原因"、比 <c>double.TryParse</c> 更宽的数字自检）。兜底就是走不到才对：
    /// 真要走到它，得先把"任何输入都不抛"这条硬契约改坏。存在的证据在 <see cref="TheDefensiveArmsAreWiredToTheirOwnKeys"/>。</item>
    /// <item><c>expr.undefined</c>：这套语法里 NaN 只能来自 ÷0 与 0 的负数次幂，两者都被<b>先一步</b>判成"除数为 0"
    /// （那句顺序是刻意的，见 <c>Evaluate</c> 里的注释）——证据是同一颗方法体里那两行的先后，也钉在同一格。</item>
    /// <item><c>expr.idle</c> 与七颗 <c>calc.hint.*</c>：住在 UI，而 <c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>（坑表 #370）；
    /// 它们的取词时机与槽数由 <see cref="TheUiSideSentencesAreFetchedAtTheMomentTheyAreShown"/> 按源码形状钉。</item>
    /// </list>
    /// </summary>
    private static readonly string[] NotReachableInExprFamily =
    [
        "calc.expr.unspecified", "calc.expr.lexCrash", "calc.expr.evalCrash", "calc.expr.numberRange",
        "calc.expr.undefined", "calc.expr.idle",
        "calc.hint.notCommitted", "calc.hint.historyCleared", "calc.hint.noCopyNoAnswer",
        "calc.hint.noCopyIncomplete", "calc.hint.copied", "calc.hint.copiedQuick", "calc.hint.clearedQuick",
    ];

    /// <summary>真跑得得到的那一族：<b>每一颗都用能走到它的输入去取</b>（⛔ 在测试里再 <c>LocMessage.Of</c> 一份，那样测的是我自己）。</summary>
    private static LocMessage[] RenderedByTheExprFamily()
    {
        var inputs = new[]
        {
            "",                                                     // empty
            new string('1', ExpressionEvaluator.MaxLength + 1),     // tooLong
            "2e",                                                   // trailing（e 是常量，"2" 之后多出的一颗记号）
            "1/0",                                                  // divZero
            "1e308*10",                                             // overflow
            "1,23",                                                 // badNumber
            "x+1",                                                  // unknownName（两颗实参：名字＋那份名单）
            ",5",                                                   // unsupportedChar
            "2+",                                                   // incomplete
            "(1+2",                                                 // missingRParen
            ")",                                                    // extraRParen（⚠ "1+2)" 走的是 trailing：那颗多余的记号是"读完后还剩东西"，不是"开头就是右括号"）
            "%",                                                    // percentNoNumber
            "*3",                                                   // needNumber
            string.Concat(Enumerable.Repeat("(", ExpressionEvaluator.MaxDepth + 5)) + "1"
                + string.Concat(Enumerable.Repeat(")", ExpressionEvaluator.MaxDepth + 5)),   // deepNest
        };
        var handed = new List<LocMessage>();
        foreach (var input in inputs)
        {
            var r = ExpressionEvaluator.Evaluate(input);
            Assert.False(r.Ok, $"这一格要的正是失败那一句，结果却算出来了：{input}");
            handed.Add(r.Error!);
        }
        return handed.ToArray();
    }

    [Fact]
    public void EveryExprRowRendersWithTheArgsTheCodeActuallyHands()
    {
        var handed = RenderedByTheExprFamily();
        var shipped = ShippedExpr();
        Assert.True(shipped.Length >= 27, $"表里只数出 {shipped.Length} 行（这一刀现场是 27）——前缀筛错了不是空转，是假绿");
        Assert.Equal(14, handed.Length);   // 守门测试不许空转：真跑确实取到 14 句

        var unreachable = shipped
            .Where(key => !handed.Any(m => m.Key == key) && !NotReachableInExprFamily.Contains(key, StringComparer.Ordinal))
            .ToList();
        Assert.True(unreachable.Count == 0,
            "这些行交了表⛔ 真跑里没有一格走到它，也没在名单里写理由：\n  " + string.Join("\n  ", unreachable));

        // 名单⛔ 藏死行：登记成「走不到」的每一颗，代码里必须真的有人在问它
        var asked = CodeOf(EvaluatorCs) + CodeOf(ViewModelCs) + CodeOf(WidgetCs);
        foreach (var key in NotReachableInExprFamily)
            Assert.True(asked.Contains('"' + key + '"', StringComparison.Ordinal),
                $"{key} 被登记成「走不到」，可源码里也没有一处问它＝表里一颗死行");

        foreach (var message in handed)
        {
            Assert.True(UiStrings.TryGet(message.Key, out var row), $"交出的键没登记在表里：{message.Key}");
            var slots = Slots.Matches(row.Zh).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().Count();
            var args = message.Args?.Length ?? 0;
            Assert.True(slots == args,
                $"{message.Key} 中文列有 {slots} 个槽，代码递了 {args} 颗实参（对不上⇒屏幕上那一格空白，坑表 #393）");
            var rendered = message.Render();
            Assert.False(string.IsNullOrWhiteSpace(rendered), $"{message.Key} 渲染出来是空的");
            Assert.DoesNotContain("{", rendered, StringComparison.Ordinal);
            foreach (var arg in message.Args ?? Array.Empty<object?>())
                Assert.True(arg is null or string or int or LocMessage,
                    $"{message.Key} 递了一颗没预料到的实参类型：" + arg?.GetType().Name);
        }
    }

    /// <summary>双向对账（与 55b 那颗同名者一条路）：交表的行每行都有人在问；代码⛔ 问一颗表里没有的。</summary>
    [Fact]
    public void EveryExprRowIsAskedForAndNoAskedKeyIsMissingFromTheTable()
    {
        var asked = CodeOf(EvaluatorCs) + CodeOf(ViewModelCs) + CodeOf(WidgetCs);
        var orphan = ShippedExpr().Where(key => !asked.Contains('"' + key + '"', StringComparison.Ordinal)).ToList();
        Assert.True(orphan.Count == 0, "这些行交了表却没有一处源码在问：\n  " + string.Join("\n  ", orphan));

        var dangling = Regex.Matches(asked, @"""(calc\.(expr|hint)\.[A-Za-z0-9]+)""").Cast<Match>()
            .Select(m => m.Groups[1].Value).Distinct()
            .Where(key => !UiStrings.TryGet(key, out _)).ToList();
        Assert.True(dangling.Count == 0, "代码在问表里没有的键（屏幕上会显示成 ⟨key⟩）：\n  " + string.Join("\n  ", dangling));
    }

    /// <summary>
    /// UI 那一侧八句的<b>形状</b>（工程引用不到，只能按源码钉，坑表 #370）：
    /// ① 每句都在<b>被展示的那一刻</b>取词——<c>Locale.T/Fmt</c> 直接坐在 <c>ShowHint</c>／<c>ShowQuickHint</c> 的实参位上；
    /// ② ⛔ 挂 <c>LocText</c>（✅WI-45 那条裁决：瞬时行挂上去只会得到"名单里多条贴不出去的空壳"，
    /// 而 <c>SetKey</c> 连实参一起冻住＝贴回来的是上一次那句，坑表 #361）；
    /// ③ 空闲那句<b>一颗主人</b>：字段初值与"清空后"都走 <c>IdleText()</c>，⛔ 两处各抄一遍那六个字。
    /// </summary>
    [Fact]
    public void TheUiSideSentencesAreFetchedAtTheMomentTheyAreShown()
    {
        var widget = CodeOf(WidgetCs);
        foreach (var key in new[]
        {
            "calc.hint.notCommitted", "calc.hint.historyCleared", "calc.hint.noCopyNoAnswer",
            "calc.hint.noCopyIncomplete", "calc.hint.copiedQuick", "calc.hint.clearedQuick",
        })
            Assert.Contains($"Locale.T(\"{key}\")", widget, StringComparison.Ordinal);

        Assert.Contains(@"Locale.Fmt(""calc.hint.copied"", ExpressionEvaluator.Format(r.Value))", widget, StringComparison.Ordinal);
        Assert.Equal(2, SourceGate.Count(widget, @"Locale.Fmt(""clip.status.copyFail"", why)"));
        Assert.DoesNotContain("LocText.SetKey", widget, StringComparison.Ordinal);
        Assert.DoesNotContain("LocText.SetToolTipKey", widget, StringComparison.Ordinal);

        var vm = CodeOf(ViewModelCs);
        // 那颗主人自己（声明）＋两处调用＝三处；两处调用必须以同一种形状出现（⛔ 一处抄字面量）
        Assert.Contains(@"private static string IdleText() => Locale.T(""calc.expr.idle"");", vm, StringComparison.Ordinal);
        Assert.Equal(2, SourceGate.Count(vm, "= IdleText();"));
        Assert.Contains(@"_calcResult = IdleText();", vm, StringComparison.Ordinal);
        Assert.Contains("CalcResult = IdleText();", vm, StringComparison.Ordinal);

        // 三列同槽：引用不到的那两颗带实参的，形状在这里钉住（中文列与英文列的编号集合必须相等）
        foreach (var key in new[] { "calc.hint.copied", "calc.hint.copiedQuick", "clip.status.copyFail" })
        {
            Assert.True(UiStrings.TryGet(key, out var row), key + " 不在表里");
            var zh = Slots.Matches(row.Zh).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().Count();
            var en = Slots.Matches(row.En).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().Count();
            Assert.True(zh == en, $"{key} 中英两列的槽数不同（{zh} vs {en}）：换语言那一刻就是填错的句子");
        }
    }

    /// <summary>
    /// "复制失败：{0}"这句<b>整张表只许一份</b>：计算器与剪贴板页报的是同一件机器事实（剪贴板写入失败并带原因）。
    /// <para>⛔ 为计算器另开一颗同义行（✅WI-46b 那颗"识字另立复制失败"同一条界线）；两处宿主共问一颗＝一句话一份出处，
    /// 改那句会同时动两处，那是对的。表里 <c>capture.card.copyFailedTitle</c> 那句是<b>卡片抬头</b>（"复制失败"四个字、不带原因、
    /// 另一个表面），不在这一格的判据里。</para>
    /// </summary>
    [Fact]
    public void TheClipboardFailureSentenceHasExactlyOneOwnerInTheTable()
        => Assert.Equal(1, UiStrings.All.Count(e => e.Zh == "复制失败：{0}"));

    /// <summary>四道兜底<b>为什么</b>走不到，钉的是可核对的形状（⛔ 拿"我构造不出输入"当证据，规则 #111）。</summary>
    [Fact]
    public void TheDefensiveArmsAreWiredToTheirOwnKeys()
    {
        var evl = CodeOf(EvaluatorCs);
        Assert.Equal(2, SourceGate.Count(evl, "catch (Exception ex)"));
        Assert.Contains(@"CalcResult.Fail(LocMessage.Of(""calc.expr.lexCrash"", ex.GetType().Name))", evl, StringComparison.Ordinal);
        Assert.Contains(@"CalcResult.Fail(LocMessage.Of(""calc.expr.evalCrash"", ex.GetType().Name))", evl, StringComparison.Ordinal);
        Assert.Contains(@"new LexOutcome(false, LocMessage.Of(""calc.expr.numberRange"", raw), null)", evl, StringComparison.Ordinal);
        Assert.Contains(@"(Error ?? LocMessage.Of(""calc.expr.unspecified""))", evl, StringComparison.Ordinal);

        // ÷0 先于 NaN／∞ 判：那句顺序是刻意的 ⇒ "结果无定义"在正常输入下走不到（顺序反了就是把除号报成溢出）
        Assert.True(
            evl.IndexOf("DivisionByZero) return", StringComparison.Ordinal)
            < evl.IndexOf("IsNaN(value)) return", StringComparison.Ordinal),
            "「除数为 0」必须判在 NaN 之前，否则 1/0 会被报成「结果超出可表示范围」");

        // "失败却没给原因"只能由绕过两颗工厂的直接构造造出来，而全文件⛔ 那样写（这一格是那四行名单的地基）
        Assert.DoesNotContain("new CalcResult(", evl, StringComparison.Ordinal);
        Assert.Equal(1, SourceGate.Count(evl, "public static CalcResult Success("));
        Assert.Equal(1, SourceGate.Count(evl, "public static CalcResult Fail("));
    }

    /// <summary>
    /// 那句"只支持 pi / π / e"里的<b>名单接出处</b>：交的是 <c>SupportedNames</c> 那颗常量，⛔ 抄进表里。
    /// <para>抄进表里＝加一颗常量时界面上那句话还在说三颗（✅WI-2g 那颗"画布内的九条快捷键"就是这句话的墓志铭）。</para>
    /// </summary>
    [Fact]
    public void TheSupportedConstantNamesAreTakenFromOnePlace()
    {
        var evl = CodeOf(EvaluatorCs);
        Assert.Contains(@"public const string SupportedNames = ""pi / π / e"";", evl, StringComparison.Ordinal);
        Assert.Contains(@"LocMessage.Of(""calc.expr.unknownName"", name, SupportedNames)", evl, StringComparison.Ordinal);
        Assert.True(UiStrings.TryGet("calc.expr.unknownName", out var row));
        Assert.DoesNotContain("pi / π / e", row.Zh, StringComparison.Ordinal);   // 名单不在句子里，它是递进去的
        Assert.Equal(2, Slots.Matches(row.Zh).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().Count());
    }

    /// <summary>
    /// 「计算」那一档交完＝<b>整扇窗五颗代码宿主＋标记</b>一起归零（批次 WI-55／P-182b 到此收口）。
    /// <para>⚠ 名单⛔ 只扫 code-behind：✅WI-51 那次正是旧尺漏了 <c>.xaml</c> 里那 23 行（坑表 #384），
    /// 所以这一格把标记一起摆进射程。55b 那批留的临时名单（<c>PendingCalcTabWords</c>）在这一刀删掉——
    /// 过期的名单比没有名单更坏。</para>
    /// <para>⚠ 唯一点名豁免的一行：<c>ParseDate</c> 那份格式表里的 <c>yyyy年M月d日</c> 是<b>输入</b>的形状，⛔ 上屏幕
    /// （豁免写在这里而不是把尺改松，规则 #110／#121）。全角运算符（＋－（【）那一族）本来就不在这把尺的码位段里。</para>
    /// </summary>
    [Fact]
    public void TheCalculatorWindowCarriesNoWordsOfItsOwn()
    {
        foreach (var rel in new[] { EvaluatorCs, QuickMathCs, QuickCalculatorCs, ViewModelCs, WidgetCs })
        {
            var left = WordLines(rel);
            if (rel == QuickMathCs)
            {
                Assert.Single(left);
                Assert.Contains("yyyy", left[0], StringComparison.Ordinal);   // 就是那份输入格式表
                continue;
            }
            Assert.True(left.Length == 0, $"{rel} 还剩 {left.Length} 行自己的汉字／假名面字：\n  " + string.Join("\n  ", left));
        }

        var markup = WordLinesOfMarkup(WidgetXaml);
        Assert.True(markup.Length == 0, $"{WidgetXaml} 的标记里还剩 {markup.Length} 处面字：\n  " + string.Join("\n  ", markup));
    }

    /// <summary>标记那一档：抹掉标记注释之后逐行数汉字／假名（属性里的面字与表头文字都在这把尺里）。</summary>
    private static string[] WordLinesOfMarkup(string rel)
        => MarkupOf(rel).Replace("\r\n", "\n").Split('\n')
            .Where(line => Cjk.IsMatch(line))
            .Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();

    /// <summary>
    /// <b>Core 那四颗文件只交键，一次词都不取</b>：<c>Locale.T(</c>／<c>Locale.Fmt(</c> 在它们里⛔ 出现。
    /// <para>为什么单独立一颗而不是靠"代码里不许有汉字面字"那把尺：把 <c>Locale.T(field.LabelKey)</c> 递进
    /// <c>LocMessage.Of("calc.quick.rej.pair", …)</c> 的实参位，<b>两把旧尺一条都不红</b>——源码里没有汉字（词是从表里取的），
    /// 槽数与实参数也照样对得上（渲染后的字符串仍是一颗实参）。屏幕上却是半中半英：外层那句跟着语言改口，
    /// 嵌进去的那颗停在算出它的那一刻（坑表 #361；✅WI-46c 那颗组名同一条界线）。
    /// 唯一的合法取词出口是 <c>LocMessage.Render()</c>，而它只许住在"被读那一刻"（<c>QuickResult.MultiLine</c>）。</para>
    /// </summary>
    [Fact]
    public void TheCoreCalcFilesHandKeysAndNeverFetchWords()
    {
        foreach (var rel in new[] { EvaluatorCs, UnitTablesCs, QuickMathCs, QuickCalculatorCs })
        {
            var code = CodeOf(rel);
            Assert.DoesNotContain("Locale.T(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Locale.Fmt(", code, StringComparison.Ordinal);
        }
        // 而界面那一侧正是相反的形状：读数在被算的那一刻取词，Core 交出来的键由宿主现贴
        Assert.Contains("Locale.T(", CodeOf(ViewModelCs), StringComparison.Ordinal);
        Assert.Contains("Locale.T(", CodeOf(WidgetCs), StringComparison.Ordinal);
    }
}
