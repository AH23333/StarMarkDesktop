#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-53（P-182a 第一刀·天气那一族）的闸门：<b>天气码到那句话只许表里一份出处，而且 Core 交出的是键</b>。
/// <para>
/// 射程按<b>屏幕上那一处宿主</b>划（台架规则 #115）＝天气组件那一扇：Core 那把 <c>WeatherCode</c> 码表、
/// 它的 <c>.xaml</c>、它的 code-behind，再加上<b>递进公共对话框工厂的那三句</b>（坑表 #387 说清了四把旧尺为什么看不见它们）。
/// 倒计时那一族是同一条 P 的另一颗宿主，另批做（账本里 P-182 自己就写了⛔ 把三族并成一刀）。
/// </para>
/// <para>⚠ 证人分两类：<b>源码形状</b>（<c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>，坑表 #370 那条边界）
/// 与<b>能真跑的 Core 读数</b>（<c>WeatherCode.DescribeMessage</c> 在测试工程里点得着 ⇒ 那一格⛔ 只读形状）。
/// 屏幕上到底是哪种语言，仍欠真机读数（#575 那一跑，坑表 #389 之外的第⑧格）。</para>
/// <para>尺＝抹注释后逐行取连续汉字片段（坑表 #375／#378：按调用形状数会漏掉标记里的 <c>Content=""</c> 与私有包装的实参）；
/// 日志行按 #368 那条界线另列且⛔ 进表。</para>
/// </summary>
public sealed class WeatherWordGateTests
{
    private const string WeatherCodeCs = "src/StarMark.Core/Widgets/WeatherCode.cs";
    private const string WeatherWidgetCs = "src/StarMark.UI/Views/WeatherWidget.xaml.cs";
    private const string WeatherWidgetXaml = "src/StarMark.UI/Views/WeatherWidget.xaml";

    private static readonly Regex Cjk = new(@"[一-鿿]+", RegexOptions.Compiled);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>本批落表的 44 颗键（26 颗天气现象名＋18 颗这一扇宿主自己的话）——逐颗点名，⛔ "大概都问过了"。</summary>
    private static readonly string[] Shipped =
    {
        "weather.desc.clear", "weather.desc.mainlyClear", "weather.desc.cloudy", "weather.desc.overcast",
        "weather.desc.fog", "weather.desc.freezingFog", "weather.desc.drizzle", "weather.desc.lightRain",
        "weather.desc.moderateRain", "weather.desc.heavyRain", "weather.desc.freezingDrizzle",
        "weather.desc.freezingRain", "weather.desc.heavyFreezingRain", "weather.desc.lightSnow",
        "weather.desc.moderateSnow", "weather.desc.heavySnow", "weather.desc.snowGrains", "weather.desc.showers",
        "weather.desc.heavyShowers", "weather.desc.violentRain", "weather.desc.snowShowers",
        "weather.desc.heavySnowShowers", "weather.desc.thunderstorm", "weather.desc.thunderstormHail",
        "weather.desc.thunderstormHeavyHail", "weather.desc.unknown",
        "widget.weather.dailyView", "widget.weather.hourlyView", "widget.weather.updated",
        "widget.weather.updateFailed", "widget.weather.noData", "widget.weather.city.title",
        "widget.weather.city.body", "widget.weather.city.sample", "widget.weather.city.offline",
        "widget.weather.city.notFound", "widget.weather.today", "widget.weather.metric.precip",
        "widget.weather.metric.uv", "widget.weather.metric.pressure", "widget.weather.metric.sunrise",
        "widget.weather.metric.sunset", "widget.weather.feelsWind", "widget.weather.feelsOnly",
    };

    /// <summary>带着汉字、又⛔ 日志的行（日志那句不上屏幕，豁免的口径与形状见 CaptureBarWordGateTests 同颗证人）。</summary>
    private static string[] CjkLines(string code)
        => code.Replace("\r\n", "\n").Split('\n')
            .Where(line => Cjk.IsMatch(line) && !line.Contains("StarLog", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    /// <summary>
    /// 这一族的两档宿主⛔ 留一颗自己的汉字面字（日志除外）。
    /// <para>⚠ <b>一般式</b>：钉的是"这一档里有汉字的行"，⛔ 钉上次看到的那几种写法（规则 #131）——
    /// 下一手换成 <c>TipContent=</c>、内插串里的三元、或递进工厂的实参，都照样落在这一格里。</para>
    /// </summary>
    [Fact]
    public void TheWeatherHostAndItsCodeTableCarryNoWordsOfTheirOwn()
    {
        foreach (var rel in new[] { WeatherCodeCs, WeatherWidgetCs })
        {
            var left = CjkLines(CodeOf(rel));
            Assert.True(left.Length == 0,
                $"{rel} 还剩 {left.Length} 行自己的汉字面字：\n  " + string.Join("\n  ", left));
        }
    }

    /// <summary>
    /// <b>Core 交出的是那颗键，⛔ 是已经取好词的那句</b>（✅WI-37-C 那颗闹钟同一条口径）。
    /// <para>为什么这不是洁癖：这里每次刷新都会走一遍，而语言是用户随时改的——交字符串就等于把语言
    /// 冻在算出它的那一瞬间（同一形状在 ✅WI-45 的提示卡、✅WI-49 的画笔名上各结过一次案）。</para>
    /// </summary>
    [Fact]
    public void TheCodeTableHandsOutKeysAndNotRenderedWords()
    {
        var code = CodeOf(WeatherCodeCs);
        Assert.DoesNotContain("public static string Describe(", code, StringComparison.Ordinal);   // 那颗交词的出口不许回来
        Assert.Contains("public static LocMessage DescribeMessage(int code)", code, StringComparison.Ordinal);
        // 29 个臂（28 颗 WMO 码＋兜底）＝29 次交键；⛔ 一条 `Locale.T` 混进来（那就在这儿把语言定死了）
        Assert.Equal(29, SourceGate.Count(code, "LocMessage.Of(\"weather.desc."));
        Assert.Equal(0, SourceGate.Count(code, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(code, "Locale.Fmt("));

        // 能真跑的那一半：逐臂问一次，键必须⛔ 是表里没登记的东西，且三语都取不出汉字串之外的形状
        foreach (var code2 in new[] { 0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99, 9999 })
        {
            var key = WeatherCode.DescribeMessage(code2).Key;
            Assert.True(UiStrings.TryGet(key, out _), $"天气码 {code2} 交出的键没登记在表里：{key}");
        }
    }

    /// <summary>
    /// <b>键按词开，⛔ 按码开</b>：29 个臂只许 26 颗不同的键。
    /// <para>53/61 都说"小雨"、55/63 都说"中雨"、57/66 都说"冻雨"——为码各开一颗的后果不是行数好看，
    /// 是<b>同一句话有四个主人</b>：改一次措辞要回来改四行，漏一行就是屏幕上两样说法并存（✅P-179 那批的同形口径）。</para>
    /// </summary>
    [Fact]
    public void CodesThatShareAWordShareAKey()
    {
        var groups = new[]
        {
            new[] { 53, 61 }, new[] { 55, 63 }, new[] { 57, 66 },
        };
        foreach (var group in groups)
            Assert.True(group.Select(code => WeatherCode.DescribeMessage(code).Key).Distinct().Count() == 1,
                $"码 {string.Join('/', group)} 该共用一颗键（同一句话⛔ 两个主人）");

        var all = new[] { 0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99 }
            .Select(code => WeatherCode.DescribeMessage(code).Key).Distinct().Count();
        Assert.Equal(25, all);   // 加兜底那颗"未知"＝26（与表里那 26 行对齐；数⛔ 抄，这里按码表现场算）
    }

    /// <summary>
    /// 本批每一颗键都得<b>真的有人问</b>（正向尺读字面量，看不见 <c>Make(…)</c> 那种私有包装的实参 ⇒ 这里逐颗点名）。
    /// </summary>
    [Fact]
    public void EveryRowThisBatchShippedIsActuallyAskedFor()
    {
        var asked = string.Join("\n",
            CodeOf(WeatherCodeCs), CodeOf(WeatherWidgetCs), SourceGate.ReadRepoFile(WeatherWidgetXaml));
        var unasked = Shipped.Where(key => !asked.Contains("\"" + key + "\"", StringComparison.Ordinal)).ToList();
        Assert.True(unasked.Count == 0, "这些行进表却没人问（要么没接线，要么表里留了颗死行）：\n  " + string.Join("\n  ", unasked));
        Assert.Equal(44, Shipped.Length);
        Assert.Equal(Shipped.Length, Shipped.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// <b>"更新"那一行只许一个主人</b>：成功念时刻、失败念那句实话，两颗念法收在 <c>PaintUpdatedLine</c> 一颗方法里。
    /// <para>两处各写一遍的坏法不是重复，是<b>下一手只改其中一处</b>：换语言重画走 <c>Render</c>，
    /// 那条路径若忘了带上失败态，屏幕上就会在明明没连上的时候写着"18:42 更新"＝一句当场可见的假话。</para>
    /// </summary>
    [Fact]
    public void TheStateLineHasOnePainterAndTheFailureStateSurvivesAReRender()
    {
        var code = CodeOf(WeatherWidgetCs);
        Assert.Equal(1, SourceGate.Count(code, @"Locale.T(""widget.weather.updateFailed"")"));
        Assert.Equal(1, SourceGate.Count(code, @"Locale.Fmt(""widget.weather.updated"""));
        Assert.Equal(3, SourceGate.Count(code, "PaintUpdatedLine("));            // 一颗声明＋Render＋ShowError
        Assert.Contains("private void PaintUpdatedLine(WeatherReport report)", code, StringComparison.Ordinal);
        // 那颗状态位三处赋值各有一次理由：失败立起来、成功与空态各自清掉（⛔ 第五种"顺手写一下"）
        Assert.Equal(3, Regex.Matches(code, @"_failed = ").Count);
        Assert.Contains("_failed = false;", SourceGate.MethodBody(code, "private void Render(WeatherReport report)"), StringComparison.Ordinal);
        Assert.Contains("_failed = true;", SourceGate.MethodBody(code, "private void ShowError()"), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>XAML</c> 那 4 句面字⛔ 留成"第二份出处"：那几颗元素的值由 code-behind 当场重写，
    /// 挂着静态键＝换语言把带值那句盖掉（坑表 #276／#295 那一族，✅WI-2n 同一颗判据）。
    /// </summary>
    [Fact]
    public void TheWeatherMarkupCarriesNoChineseAttributeValues()
    {
        var markup = SourceGate.ReadRepoFile(WeatherWidgetXaml).Replace("\r\n", "\n");
        var offenders = Regex.Matches(markup, @"(Text|Content|Header|ToolTipService\.ToolTip|PlaceholderText)=""([^""]*)""")
            .Cast<Match>()
            .Where(m => Cjk.IsMatch(m.Groups[2].Value))
            .Select(m => m.Value)
            .ToArray();
        Assert.True(offenders.Length == 0,
            "标记里又留下了面字（那一处的主人应当唯一）：\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>表里⛔ 有控制字符（换行是布局，不是文案；规则 #100）。</summary>
    [Fact]
    public void NoRowOfThisBatchCarriesAControlCharacter()
    {
        foreach (var key in Shipped)
        {
            Assert.True(UiStrings.TryGet(key, out var entry), key + " 不在表里");
            foreach (var column in new[] { entry.Zh, entry.En, entry.Ja })
                Assert.False(column.Any(char.IsControl), $"{key} 那一列带了控制字符：{column}");
        }
    }

    /// <summary>
    /// 那一句"今天"⛔ 与速览那颗按钮合用一颗键：同形而不同事（那是"回到本月"的动作，这是日期身份），
    /// 合成一颗的后果是改一处带动另一处。这里钉住"两份都在、各问各的"。
    /// </summary>
    [Fact]
    public void TheTwoTodaysStaySeparateOwners()
    {
        Assert.True(UiStrings.TryGet("widget.gl.today", out var glance) && glance.Zh == "今天");
        Assert.True(UiStrings.TryGet("widget.weather.today", out var weather) && weather.Zh == "今天");
        Assert.Contains("Locale.T(\"widget.weather.today\")", CodeOf(WeatherWidgetCs), StringComparison.Ordinal);
        Assert.DoesNotContain("widget.gl.today", CodeOf(WeatherWidgetCs), StringComparison.Ordinal);
    }
}
