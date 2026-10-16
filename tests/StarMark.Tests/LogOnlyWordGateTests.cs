#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-71（✅P-188）的闸门：<b>UI 层剩下的那 69 行汉字⛔ 有一颗住在屏幕上，而且每一行都认得出消费点</b>。
/// <para>
/// 这一格从前只按"还剩几行汉字"记账，而汉字行 ≠ 上屏行：日志、启动刻度、异常句里的字换语言不需要翻（✅WI-67 片4
/// 那条"只进日志的句⛔ 顺手交表"的口径）。批次 WI-71 把这 70 行<b>逐行追到消费点</b>（只读尺
/// <c>bin/WI71/consume_71.py</c>，六臂反向台架 <c>bin/WI71/bench_71a.py</c>）：读数＝上屏 0／只进日志 52／
/// 启动刻度 15／异常句 2／符号 1。这里钉的是<b>形状与颗数</b>，⛔ 声称复刻了那颗数据流。
/// </para>
/// <para>
/// ⚠ 两把尺⛔ 同一构造（名册 277 那一族）：C# 这侧只认"锚行＋名册写法＋上屏出口形状"，
/// 它与 python 那侧的<b>消费点数据流</b>读数是<b>独立算出来的同一组数</b>（69＋1＝70）——
/// 两侧同时错到一处去的概率，就是这颗闸门值得存在的理由。
/// </para>
/// <para>⚠ 测试工程引用不到 <c>StarMark.UI</c>（坑表 #370 那条边界），所以这里全是源码形状判据；
/// "切到英文之后那一行还剩什么"仍归屏幕读数（欠项 #575 那一跑）。</para>
/// </summary>
public sealed class LogOnlyWordGateTests
{
    /// <summary>真会念出来的「话」＝汉字或假名。全角标点／符号（＋、？）不在这一档：它不需要表，换语言也不变。</summary>
    private static readonly Regex Word = new(@"[぀-ヿ㐀-䶿一-鿿]", RegexOptions.Compiled);

    /// <summary>上屏出口的形状（XAML 属性、C# 属性赋值、对象初始化器、消息框与提示卡）。</summary>
    private static readonly Regex ScreenOutlet = new(
        @"(?:[\s<""])(Content|Text|Header|Title|Description|PlaceholderText|ToolTip|Subtitle|Label|Body)\s*=\s*\$?[@""]"
        + @"|\.(Content|Text|Header|Title|Description|PlaceholderText|ToolTip|Subtitle|Label|Body)\s*=\s*\$?[@""]"
        + @"|MessageBox\.\w+\(\s*\$?""", RegexOptions.Compiled);

    /// <summary>P-188 收口时那 12 档宿主（其余档目录的汉字面字早在 WI-59～WI-68 交进表里了）。</summary>
    private static readonly string[] Hosts =
    {
        "src/StarMark.UI/ViewModels/FolderTreePageViewModel.cs",
        "src/StarMark.UI/ViewModels/QuickNoteWidgetViewModel.cs",
        "src/StarMark.UI/ViewModels/SettingsPageViewModel.Updates.cs",
        "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs",
        "src/StarMark.UI/ViewModels/TodoWidgetViewModel.cs",
        "src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs",
        "src/StarMark.UI/Views/NoticeCardWindow.cs",
        "src/StarMark.UI/Views/QuickLaunchWidget.xaml",
        "src/StarMark.UI/Views/QuickLaunchWidget.xaml.cs",
        "src/StarMark.UI/Views/WidgetWindow.Appearance.cs",
        "src/StarMark.UI/Views/WidgetWindow.QuickLaunchDrop.cs",
        "src/StarMark.UI/Views/WidgetWindow.xaml.cs",
    };

    /// <summary>
    /// 名册：一颗汉字行<b>只许</b>落在这七种写法之一，颗数逐档钉死（数字来自 WI-71 那两把尺的同一读数）。
    /// 新增一种"字面量只进某个非屏幕出口"的写法＝这里红，要么把它读清楚再加一档，要么把那句交进表。
    /// </summary>
    private static readonly (string Name, Regex Pat, int Count)[] Roster =
    {
        ("只进日志（拼接的续行）", new Regex(@"(StarLog|Debug|Trace)\.\w+\("), 4),
        ("启动刻度（StartupProfile）", new Regex(@"StartupProfile\.(Mark|Measure)\s*\("), 15),
        ("异常句（throw）", new Regex(@"throw new \w*Exception"), 2),
        ("设置回读兜底（Safe 的 what）", new Regex(@"\bSafe\s*\("), 35),
        ("组件写兜底（GuardAsync 的 what）", new Regex(@"\bGuardAsync\s*\("), 7),
        ("拖放格式串厂（DropFormats）", new Regex(@"\b\w+\.Add\(|return \w+\.Count"), 5),
        ("贴图窗设备名（DeviceName）", new Regex(@"^\s*DeviceName\s*="), 1),
    };

    private static string CodeOf(string rel)
    {
        var code = Code(ReadRepoFile(rel));
        // FormatScanner 只抹 C# 的注释；XAML 的 <!-- --> 得自己抹（注释里的汉字⛔ 算屏幕字）
        return rel.EndsWith(".xaml", StringComparison.Ordinal)
            ? Regex.Replace(code, "<!--.*?-->", " ", RegexOptions.Singleline)
            : code;
    }

    private static bool StartsContinuation(string line) => line.TrimStart() is { Length: > 0 } t && "+?:.".Contains(t[0]);

    private static bool EndsUnfinished(string line)
    {
        var t = line.TrimEnd();
        return t.EndsWith(',') || t.EndsWith('(') || t.EndsWith('+') || t.EndsWith("&&") || t.EndsWith("||")
            || t.EndsWith('?') || t.EndsWith(':') || t.EndsWith(')');
    }

    /// <summary>
    /// 把一行<b>并回它所属的那条语句</b>：一颗实参常常住在续行上（<c>IntervalIndexOf(⏎ Safe(… "护眼间隔"))</c>、
    /// <c>StarLog.Info(…⏎ + $"…秒后自动消失）"</c>），只看单行就会把"整条语句的出口"读成"没有出口"。
    /// </summary>
    private static string StatementOf(string[] lines, int at)
    {
        var k = at;
        while (k > 0 && (StartsContinuation(lines[k]) || EndsUnfinished(lines[k - 1]))) k--;
        return string.Join("\n", lines[k..(at + 1)]);
    }

    /// <summary>逐行取那 12 档宿主里的汉字行（跳过日志头那一行＝<see cref="SourceGate.ScreenWords"/> 同一界线）。</summary>
    private static List<(string Rel, int Line, string Text)> WordsOfHosts()
    {
        var rows = new List<(string, int, string)>();
        foreach (var rel in Hosts)
        {
            var lines = CodeOf(rel).Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var head = lines[i].Split('"')[0];
                if (LogHeads.Any(h => head.Contains(h, StringComparison.Ordinal))) continue;
                if (!Word.IsMatch(lines[i])) continue;                      // 只含全角符号的那一行归"符号"档，不进名册
                rows.Add((rel, i + 1, StatementOf(lines, i)));
            }
        }

        return rows;
    }

    /// <summary>
    /// 名册那一半（读数）：69 行汉字逐行归进七种写法，<b>每档颗数钉死</b>；出现"名册外"的那一行就红，
    /// 并直接印出原文（红测要能指出该打开哪一行，⛔ 只报一个数）。
    /// </summary>
    [Fact]
    public void TheWordsLeftInTheTwelveHostsAreOnlyEverTakenByNamedNonScreenConsumers()
    {
        var rows = WordsOfHosts();
        Assert.Equal(69, rows.Count);                                       // 与那颗只读尺同一读数（70 行 − 1 行只含全角符号）

        var counted = Roster.ToDictionary(r => r.Name, _ => 0);
        var homeless = new List<string>();
        foreach (var (rel, line, stmt) in rows)
        {
            var hit = Roster.FirstOrDefault(r => r.Pat.IsMatch(stmt));
            if (hit.Name is null)
            {
                homeless.Add($"{rel}:{line}｜{string.Join(' ', stmt.Split('\n').Select(s => s.Trim()))}");
                continue;
            }

            counted[hit.Name]++;
        }

        Assert.True(homeless.Count == 0, $"名册外 {homeless.Count} 行（新写法先读清消费点，再把那句交进表）：\n  "
            + string.Join("\n  ", homeless));
        foreach (var (name, _, expected) in Roster)
            Assert.Equal(expected, counted[name]);                          // 某一档悄悄涨／跌＝消费点变了，先说要紧
    }

    /// <summary>
    /// 禁项那一半（P-188 归零的那句话）：<b>整个 <c>src/StarMark.UI</c> 里，上屏出口⛔ 挂汉字字面量</b>。
    /// <para>⚠ 这条判据必须"认得上屏"才算数，所以同一条里放着正件：把 <c>Text = "加载失败"</c> 递进同一对正则，
    /// 它<b>必须</b>被认出来（名册 289 那一族：一把只会念"没问题"的尺⛔ 是尺）。那颗 <c>Content="＋"</c>
    /// 与 <c>Content="？"</c> 仍在屏幕上，但它是<b>符号</b>——三语同形、⛔ 需要表，所以按"话＝汉字或假名"排除，
    /// 排除的理由也在这里钉一次。</para>
    /// </summary>
    [Fact]
    public void NoWordSitsOnAScreenOutletAnywhereInTheUiLayer()
    {
        var offenders = new List<string>();
        foreach (var (rel, text) in TextFilesUnderUi())
        {
            var lines = CodeOf(rel).Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (ScreenOutlet.IsMatch(lines[i]) && Word.IsMatch(lines[i]))
                    offenders.Add($"{rel}:{i + 1}｜{lines[i].Trim()}");
        }

        Assert.True(offenders.Count == 0,
            $"上屏出口上又坐了汉字字面量 {offenders.Count} 行（该交表，走 LocText/LocMessage）：\n  " + string.Join("\n  ", offenders));

        // 正件一：这一对正则认得上屏的那句话（⛔ 让"0 行"退化成"看不见"）
        Assert.True(ScreenOutlet.IsMatch("        hint.Text = \"加载失败\";") && Word.IsMatch("hint.Text = \"加载失败\";"));
        Assert.True(ScreenOutlet.IsMatch("<Button Content=\"添加\"/>") && Word.IsMatch("<Button Content=\"添加\"/>"));
        // 正件二：符号那一颗⛔ 算话，而这道排除只排除字形，⛔ 排除出口
        Assert.DoesNotMatch(Word, "<Button Content=\"＋\"/>");
        Assert.Matches(ScreenOutlet, "<Button Content=\"＋\"/>");
    }

    private static List<(string Rel, string Text)>? _uiFilesCache;

    /// <summary><c>src/StarMark.UI</c> 下全部手写 <c>.cs</c> 与 <c>.xaml</c>（<see cref="SourceGate.ReadRepoUnder"/> 只 glob <c>.cs</c>，
    /// 而上屏出口多半在 XAML 那一侧，所以这一把得自己走两样扩展名）。扫不到东西就当场抛——⛔ 让闸门白过。</summary>
    private static IReadOnlyList<(string Rel, string Text)> TextFilesUnderUi()
    {
        if (_uiFilesCache is not null) return _uiFilesCache;
        var root = RepoRoot();
        var list = new List<(string, string)>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src/StarMark.UI"), "*.*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal)) continue;
            if (!rel.EndsWith(".cs", StringComparison.Ordinal) && !rel.EndsWith(".xaml", StringComparison.Ordinal)) continue;
            if (Path.GetFileName(rel).Contains(".g.", StringComparison.Ordinal)) continue;
            list.Add((rel, File.ReadAllText(path)));
        }

        Assert.True(list.Count > 200, $"src/StarMark.UI 只扫到 {list.Count} 档（普查路径错了，这颗尺⛔ 白过）");
        _uiFilesCache = list;
        return list;
    }

    /// <summary>
    /// 四颗"只进日志"的写法，各钉一次<b>消费点</b>（⛔ 凭函数名放行——记忆 [UN]：调用点在⛔ 等于实参对）：
    /// <c>Safe</c> 与 <c>GuardAsync</c> 的那颗 <c>string what</c> 形参，在它们自己的函数体里只出现在日志出口行上；
    /// <c>DropFormats</c> 与 <c>DeviceName</c> 产出的串，在<b>全部</b>点名它们的行上只进日志。
    /// </summary>
    [Fact]
    public void TheFourNamedProducersOnlyEverSpeakToTheLog()
    {
        var settings = CodeOf("src/StarMark.UI/ViewModels/SettingsPageViewModel.cs");
        var safe = MethodBody(settings, "static T Safe<T>");
        Assert.Equal(1, Count(safe, "StarLog.Error("));
        // 那颗形参在<b>签名行</b>上出现的是<b>声明</b>（这颗方法的签名还折成了两行），⛔ 算一次"读"：只查 `{` 之后
        var safeBody = string.Join("\n", LinesOf(safe).SkipWhile(l => !l.EndsWith("{")));
        Assert.True(safeBody.Length > 0, "切不出 Safe 的块体（写法变了，守门要先跟上）");
        Assert.All(safeBody.Split('\n').Select(l => l.Trim()).Where(l => Regex.IsMatch(l, @"(?<!\w)what\b")),
            l => Assert.Contains("StarLog", l));

        var todo = CodeOf("src/StarMark.UI/ViewModels/TodoWidgetViewModel.cs");
        var guard = MethodBody(todo, "Task GuardAsync(string what");
        Assert.Equal(1, Count(guard, "StarLog.Error(what, ex)"));
        Assert.Equal(7, Count(todo, "GuardAsync(\""));                    // 七颗实参全走这一条兜底出口

        var drop = CodeOf("src/StarMark.UI/Views/WidgetWindow.QuickLaunchDrop.cs");
        var dropLines = LinesOf(drop).Where(l => l.Contains("DropFormats", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, dropLines.Count);                                 // 1 处定义＋3 处调用点
        Assert.Equal(3, dropLines.Count(l => l.Contains("StarLog", StringComparison.Ordinal)));   // 三处调用点全在日志里
        Assert.Equal(1, dropLines.Count(l => Regex.IsMatch(l, @"^\s*private static string DropFormats")));

        var pin = CodeOf("src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs");
        var shot = CodeOf("src/StarMark.UI/Services/ScreenshotService.cs");
        var nameLines = LinesOf(pin).Where(l => Regex.IsMatch(l, @"(?<!\w)DeviceName\b")).ToList();
        Assert.Equal(4, nameLines.Count);                                 // 声明 1＋写入 2＋日志读数 1
        Assert.Equal(2, LinesOf(shot).Count(l => Regex.IsMatch(l, @"(?<!\w)DeviceName\b")
                                                && l.Contains("StarLog", StringComparison.Ordinal)));
    }

    /// <summary>
    /// 启动那 29 颗段名⛔ 再上屏幕（✅P-191 他改判「文字可删去」）：诊断面板那一行交的是<b>编号＋毫秒</b>，
    /// 所以那颗档⛔ 许再出现任何汉字面字；而 <c>StartupProfile</c> 的唯一出口仍是 <c>StarLog.Info</c>。
    /// </summary>
    [Fact]
    public void SegmentNamesStayOffTheDiagnosticPanel()
    {
        var diag = CodeOf("src/StarMark.Core/Diagnostics/DiagnosticsService.cs");
        Assert.Empty(Regex.Matches(diag, "\"[^\"]*[぀-ヿ㐀-䶿一-鿿]"));      // 面板那一档⛔ 自带一句汉字
        Assert.Contains("string.Join(\" · \", timings)", diag, StringComparison.Ordinal);   // 交的是读数，⛔ 段名

        var profile = CodeOf("src/StarMark.Abstractions/StartupProfile.cs");
        Assert.Equal(1, Count(profile, "internal static Action<string> Sink = StarLog.Info;"));
    }

    private static IEnumerable<string> LinesOf(string code)
        => code.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
}
