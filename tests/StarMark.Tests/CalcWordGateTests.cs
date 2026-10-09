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
            var slots = Regex.Matches(row.Zh, @"\{(\d+)\}").Cast<Match>()
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
        var left = CodeOf(UnitTablesCs).Replace("\r\n", "\n").Split('\n')
            .Where(line => Regex.IsMatch(line, @"[㐀-䶿一-鿿぀-ヿ]"))
            .Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
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
    /// 三颗下拉是<b>数据项＋模板</b>：⛔ <c>DisplayMemberPath="Label"</c>——那条路把表里的成品字直接搬到屏幕上，
    /// 换语言那一刻闭着的那一格念的还是旧语言（✅WI-2c／✅WI-43 同一颗判据，本批把最后一族下拉交出来）。
    /// <para>那颗模板是 13 处下拉共用的主人（主窗 2／设置页 7／热榜 1／计算器 3），射程记在 <c>StarMarkTheme.xaml</c> 的注释里。</para>
    /// </summary>
    [Fact]
    public void TheThreeDropdownsAreDataItemsNotAMemberPath()
    {
        var markup = MarkupOf(WidgetXaml);
        Assert.Equal(0, SourceGate.Count(markup, "DisplayMemberPath"));
        Assert.Equal(3, SourceGate.Count(markup, @"ItemTemplate=""{StaticResource LocOptionTemplate}"""));
        // 三颗下拉的数据源交的是"项"而不是单位对象本身（身份在 LocOption.Value 里，⛔ 拿显示文字当身份）
        Assert.Equal(1, SourceGate.Count(markup, "ViewModel.CategoryOptions"));
        Assert.Equal(2, SourceGate.Count(markup, "ViewModel.UnitOptions"));

        var total = Directory.GetFiles(Path.Combine(SourceGate.RepoRoot(), "src"), "*.xaml", SearchOption.AllDirectories)
            .Select(f => f.Replace(Path.DirectorySeparatorChar, '/'))
            .Where(f => !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal))
            .Sum(f => SourceGate.Count(Markup(File.ReadAllText(f)), "StaticResource LocOptionTemplate"));
        Assert.Equal(13, total);
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
}
