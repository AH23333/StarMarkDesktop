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
/// 批次 WI-59（P-188 第一刀＋P-193 第一颗）的闸门：<b>番茄钟那一扇屏幕上那几句话只许住在表里，
/// 而 Core 与 VM 交出的是还没取词的那一句</b>。
/// <para>
/// 射程按<b>屏幕上那一处宿主</b>划（台架规则 #115）＝组件那一扇的正文行＋倒计时＋那颗主按钮＋那颗提示，
/// 外加那颗输入框的占位句。两颗档：<c>FocusTimer.cs</c>（Core 状态机）与 <c>FocusTimerViewModel.cs</c>（VM）；
/// 那颗宿主自己（<c>FocusTimerWidget.xaml</c>／<c>.xaml.cs</c>）在本批复算里<b>非日志屏幕字＝0</b>，
/// 它的四颗 <c>{x:Bind}</c> 与那颗一秒拍⛔ 是形状判据（读不到"贴上去的是哪一版的字"），
/// 所以那一侧交的是<b>节拍判据</b>（见 <see cref="TheHintKeepsItsIdentityAndIsRepaintedByTheBeat"/>）。
/// </para>
/// <para><b>这颗缺陷的形状与历次"交表"那一批不同</b>：话本来就在表里吗？不是——
/// <c>FocusTimer.StatusLine</c> 直接返回 <c>"专注中 · 剩 25:00"</c> 这种成品字，换语言后那一行停在旧语言；
/// 而 VM 里那句占位提示「这一轮在做什么？可留空」是表里 <c>widget.pomo.taskHint</c> 的<b>第二种措辞</b>
/// （第二个主人＝P-171 那一族）。两条都在本批一起断掉。</para>
/// <para>⚠ 证人分两类：源码形状（<c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>，坑表 #370 那条边界）
/// 与能真跑的 Core 读数（<c>FocusTimer.StatusMessage</c> 点得着 ⇒ 那一格⛔ 只读形状）。
/// 屏幕上到底是哪一版念哪一句，仍欠真机读数（#575 那一跑）。</para>
/// <para>尺＝抹注释后逐行取连续 CJK 片段，<b>字符类含全角那一族</b>（坑表 #403／#407：
/// 只写汉字与假名的话，那颗写死在代码里的「（）」与「、」量得到却判不出）。出现次数口径，⛔ 去重。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class FocusWordGateTests : IDisposable
{
    private const string CoreCs = "src/StarMark.Core/Widgets/FocusTimer.cs";
    private const string VmCs = "src/StarMark.UI/ViewModels/FocusTimerViewModel.cs";
    private const string WidgetCs = "src/StarMark.UI/Views/FocusTimerWidget.xaml.cs";
    private const string TableCs = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    private static readonly Regex Cjk = new(@"[぀-ヿ㐀-䶿一-鿿　-〿＀-￯]+", RegexOptions.Compiled);
    private static readonly DateTimeOffset T0 = new(2026, 9, 23, 9, 0, 0, TimeSpan.FromHours(8));

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>屏幕字计数口径：抹注释后逐行取 CJK（含全角那一族），排除日志头那一行。</summary>
    private static List<string> ScreenWords(string code)
        => code.Split('\n').Select(l => l.Trim())
               .Where(l => l.Length > 0 && !l.StartsWith("StarLog.", StringComparison.Ordinal)
                                                && !l.StartsWith("Debug.", StringComparison.Ordinal)
                                                && !l.StartsWith("Trace.", StringComparison.Ordinal))
               .Where(l => Cjk.IsMatch(l))
               .ToList();

    /// <summary>
    /// <b>供话者⛔ 自己取词</b>（形状禁项，与 <see cref="TheSameStatusLineAnswersInEveryLanguage"/> 那颗语言读数成对＝规则 #124）：
    /// Core 与 VM 只许交出"键＋实参"，取词只发生在 VM 那四颗绑定属性被重算的那一刻。
    /// <para>颗数⛔ 凭记忆（规则 #159）：名单由现场 grep 出来（键名类⛔ 用 <c>[a-z.]*</c>——那把尺会把 camelCase 整颗读没），
    /// 逐颗抄在下面，期望钉 <c>==</c> 而⛔ <c>&gt;=</c>：下界抓不到"少一颗"，而多一颗键时这一格连着注释一起说出是哪一颗。</para>
    /// </summary>
    [Fact]
    public void TheCoreAndTheViewModelNeverTakeWordsThemselves()
    {
        foreach (var rel in new[] { CoreCs, VmCs })
        {
            var code = CodeOf(rel);
            Assert.Equal(0, SourceGate.Count(code, "Locale.T("));
            Assert.Equal(0, SourceGate.Count(code, "Locale.Fmt("));
        }

        // Core 那 5 颗（状态行三颗＋"进行中"两颗）：
        //   widget.pomo.status.{idle,paused,running} ＋ widget.pomo.doing.{focusing,resting}
        Assert.Equal(5, SourceGate.KeyedCallSites(CodeOf(CoreCs)).Select(c => c.Key).Distinct().Count());
        // VM 那 11 颗（按钮四颗＋提示六颗＋那颗占位句）：
        //   widget.pomo.btn.{start,resume,skipRest,pause} ＋ widget.pomo.hint.{stopped,runningElseStop,
        //   startedFromTodo,cannotStart,clamped,nextRound} ＋ widget.pomo.taskHint
        Assert.Equal(11, SourceGate.KeyedCallSites(CodeOf(VmCs)).Select(c => c.Key).Distinct().Count());

        // 取词只许发生在 VM 那四颗绑定属性上（Core⛔ 一次；VM 恰好四颗，逐颗点名）：
        //   StatusMessage(now).Render() ／ PrimaryMessage(Phase).Render()
        //   LocMessage.Of("widget.pomo.taskHint").Render() ／ _hint?.Render()
        Assert.Equal(0, SourceGate.Count(CodeOf(CoreCs), ".Render()"));
        Assert.Equal(4, SourceGate.Count(CodeOf(VmCs), ".Render()"));
    }

    /// <summary>
    /// <b>这一路一颗屏幕字都不许留</b>（登记剩余量必须把尺子写进句子＝规则 #126）。
    /// 从前 Core 那档读回 7 行（状态行、阶段名、四颗按钮字里的两句都在），VM 那档 13 行。
    /// </summary>
    [Fact]
    public void NoScreenWordSurvivesOnTheFocusPath()
    {
        foreach (var rel in new[] { CoreCs, VmCs, WidgetCs })
        {
            var left = ScreenWords(CodeOf(rel));
            Assert.True(left.Count == 0, $"{rel} 还剩 {left.Count} 行带字的代码：{string.Join(" ⧸ ", left.Take(4))}");
        }
    }

    /// <summary>
    /// <b>每一处调用点按代码实给的实参数比槽数</b>（坑表 #393 那颗"屏幕上那一格整空白"的通用版）：
    /// 这一族的尺⛔ 用正则切跨行调用点，按括号深度走到与 <c>Of(</c> 配对那颗 <c>)</c>（公共那份＝规则 #86）。
    /// </summary>
    [Fact]
    public void EveryKeyedCallHandsExactlyTheSlotsItsRowDeclares()
    {
        var watched = 0;
        foreach (var rel in new[] { CoreCs, VmCs })
            foreach (var (key, args, line) in SourceGate.KeyedCallSites(CodeOf(rel)))
            {
                watched++;
                Assert.True(args == SourceGate.RowSlots(key),
                            $"{rel}:{line} 给 {key} 递了 {args} 颗实参，而表里那颗行有 {SourceGate.RowSlots(key)} 颗槽");
            }
        Assert.Equal(16, watched);                 // 5（Core）＋11（VM），与上一颗判据同一份名单
    }

    /// <summary>
    /// <b>那颗提示存的是身份，不是上一次取好的词</b>——这颗宿主没有 <c>LocText</c> 那条重贴路，
    /// 靠的是自己那一拍：可见时每秒 <c>Tick → Refresh</c> 把四颗绑定属性按<b>此刻</b>的语言重算一遍
    /// （✅WI-44 那颗时钟同一口径＝<b>节拍就是它的重贴</b>）。
    /// <para>所以这里要钉三件事：① 存的字段是 <c>LocMessage?</c>；② 交到属性上的是 <c>Render()</c> 的结果
    /// 而不是某句中文；③ 那一拍真的每秒跑。⚠ ③ 读的是那颗宿主的<b>形状</b>：
    /// 把间隔改成 30 秒，屏幕上那句就会停在旧语言半分钟——只有源码这一层看得见。</para>
    /// </summary>
    [Fact]
    public void TheHintKeepsItsIdentityAndIsRepaintedByTheBeat()
    {
        var vm = CodeOf(VmCs);
        Assert.Contains("private LocMessage? _hint;", vm, StringComparison.Ordinal);
        Assert.Contains("HintText = _hint?.Render() ?? string.Empty;", vm, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"HintText = """, vm);                              // ⛔ 再往属性上贴成品字
        Assert.DoesNotMatch(@"(StatusText|PrimaryText|TaskText) = """, vm);     // 同一条禁项管另外三颗
        Assert.DoesNotContain("Locale.Changed", vm, StringComparison.Ordinal);  // ⛔ 另开订阅者（主人名单仍是 8 位）

        var widget = CodeOf(WidgetCs);
        Assert.Contains("_timer.Interval = TimeSpan.FromSeconds(1);", widget, StringComparison.Ordinal);
        Assert.Contains("private void Tick(DispatcherQueueTimer sender, object args) => ViewModel.Tick();",
                        widget, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.Changed", widget, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>那颗主按钮的四句话有四颗键、没有第五份措辞</b>：阶段↔按钮字这张表只许 VM 里那一处 switch 有。
    /// 四颗都是 0 槽行（⛔ 有人把分钟数插进按钮字里＝两颗主人管同一格）。
    /// </summary>
    [Fact]
    public void TheFourButtonsAreFourKeysAndNotFourSentences()
    {
        var body = SourceGate.MethodBody(CodeOf(VmCs), "private static LocMessage PrimaryMessage(FocusPhase phase)");
        var keys = new[] { "widget.pomo.btn.start", "widget.pomo.btn.resume", "widget.pomo.btn.skipRest", "widget.pomo.btn.pause" };
        foreach (var key in keys)
        {
            Assert.Contains($"LocMessage.Of(\"{key}\")", body, StringComparison.Ordinal);
            Assert.Equal(0, SourceGate.RowSlots(key));
        }
        Assert.Equal(4, Regex.Matches(body, "LocMessage\\.Of\\(\"widget\\.pomo\\.btn\\.").Count);
        Assert.DoesNotMatch(@"=> """, body);                                     // ⛔ 第五份措辞：那一格里只许交键
    }

    /// <summary>
    /// <b>那颗占位句只有一个主人</b>（本批顺带修掉的真缺陷）：VM 从前硬写着「这一轮在做什么？可留空」，
    /// 而表里 <c>widget.pomo.taskHint</c> 写的是「这一轮在做什么？（可留空）」——同一格屏幕字两种写法，
    /// 而通用闸门看不见（被问的键没变，变的是<b>实参</b>那一族＝P-171）。
    /// </summary>
    [Fact]
    public void ThePlaceholderSentenceHasOneOwner()
    {
        Assert.Contains("LocMessage.Of(\"widget.pomo.taskHint\")", CodeOf(VmCs), StringComparison.Ordinal);

        var table = SourceGate.ReadRepoFile(TableCs);
        // ⚠ 读 `Code()`⛔ 读原文：本节上面那句"两种措辞"的说明里也带着这几个字，按原文数会把注释算成第二份（规则 #131）。
        Assert.Single(Regex.Matches(SourceGate.Code(table), "这一轮在做什么"));     // 这颗占位句在表里只有一颗行
        Assert.Equal(2, Regex.Matches(SourceGate.Code(table), "可留空").Count);    // 另一颗是快捷启动那颗名字提示（⛔ 混用）
        foreach (var (rel, text) in SourceGate.ReadRepoUnder("src"))
        {
            if (rel.EndsWith("UiStrings.cs", StringComparison.Ordinal)) continue;  // 那颗行自己就是它的主人
            Assert.True(0 == Regex.Matches(SourceGate.Code(text), "可留空").Count,
                        $"{rel} 里还硬写着那颗占位句的第二种措辞");
        }
    }

    /// <summary>
    /// <b>同一颗键按语言各念一句</b>（真跑那一侧，期望⛔ 抄表里的措辞＝规则 #130）：
    /// 进行中的那一句里，「专注中」是<b>嵌套的键</b> ⇒ 它跟着最外层那一刻的语言走，
    /// 而 25:00 那颗读数在三语里都原样在场（机器事实）。
    /// </summary>
    [Fact]
    public void TheSameStatusLineAnswersInEveryLanguage()
    {
        Locale.TrySet(AppLanguage.Chinese);
        var doingZh = Locale.T("widget.pomo.doing.focusing");                     // 期望那一侧现取同一颗键

        var t = new FocusTimer(new FocusTimerConfig { FocusMinutes = 25, RestMinutes = 5 });
        t.Start(T0);
        var running = t.StatusMessage(T0);
        t.Pause(T0.AddMinutes(5));
        var paused = t.StatusMessage(T0.AddMinutes(5));

        Locale.TrySet(AppLanguage.Chinese); var zhRun = running.Render(); var zhPause = paused.Render();
        Locale.TrySet(AppLanguage.English); var enRun = running.Render(); var enPause = paused.Render();
        Locale.TrySet(AppLanguage.Japanese); var jaRun = running.Render(); var jaPause = paused.Render();

        // 那颗"暂停"句：槽里递进去的必须是<b>那一刻的剩余</b>，而且⛔ 把词取死在半路。
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal(Locale.Fmt("widget.pomo.status.paused", "20:00"), zhPause);   // 同一个键＋同一个实参，⛔ 抄那句字
        Assert.DoesNotContain("25:00", zhPause, StringComparison.Ordinal);          // 暂停后剩余必须真的少 5 分钟
        Assert.Contains("（", Locale.T("widget.pomo.status.paused"), StringComparison.Ordinal);   // 那一档的括号本来就全角

        Assert.Contains(doingZh, zhRun, StringComparison.Ordinal);                  // 嵌套那颗跟着最外层走
        Assert.DoesNotContain(doingZh, enRun, StringComparison.Ordinal);            // 换语言后半句中⛔ 留在原地
        Assert.DoesNotContain(doingZh, jaRun, StringComparison.Ordinal);
        Assert.NotEqual(zhPause, enPause);
        Assert.NotEqual(enPause, jaPause);
        Assert.DoesNotContain("（", enPause, StringComparison.Ordinal);              // 拉丁那一档⛔ 混进全角括号
        Assert.Contains("（", jaPause, StringComparison.Ordinal);                  // 日文那一档<b>应当</b>有全角括号

        // 那颗<b>机器事实</b>在三语里都原样在场（⛔ 被哪一档的语言规则改掉）：
        foreach (var text in new[] { zhRun, enRun, jaRun })
            Assert.Contains("25:00", text, StringComparison.Ordinal);
        foreach (var text in new[] { zhPause, enPause, jaPause })
            Assert.Contains("20:00", text, StringComparison.Ordinal);
        foreach (var text in new[] { zhRun, enRun, jaRun, zhPause, enPause, jaPause })
            Assert.DoesNotContain("{", text, StringComparison.Ordinal);             // 少递实参会在这里露出来（#393）
        Assert.Empty(Cjk.Matches(enRun).Cast<Match>().Select(m => m.Value));        // 拉丁那一档⛔ CJK 与全角
        Assert.NotEmpty(Cjk.Matches(jaRun).Cast<Match>());                          // 日文那一档应有本族文字
    }

    /// <summary>
    /// <b>"这处坏＝N 处坏"的 N 要当场 grep</b>（规则 #107）＋<b>⛔ 立没人问的行</b>（规则 #111 的反向）：
    /// 从前那颗 <c>PhaseLabel</c> 只被状态行读，所以本批把它整个删掉，也⛔ 为"空闲／已暂停"两颗名词立行——
    /// 立了没人问的行是死行（通用闸门 <c>TheTableHasNoRowNothingAsksFor</c> 会红在这里）。
    /// </summary>
    [Fact]
    public void ThePhaseNounMethodIsGoneAndNoRowAwaitsIt()
    {
        Assert.DoesNotContain("PhaseLabel", CodeOf(CoreCs), StringComparison.Ordinal);
        Assert.DoesNotContain("StatusLine", CodeOf(CoreCs), StringComparison.Ordinal);   // ⛔ 留一份旧名字当第二入口
        Assert.Empty(Regex.Matches(SourceGate.ReadRepoFile(TableCs), "\"widget\\.pomo\\.phase\\."));
        // 只有"进行中"那一族真有第二份念法（英文要写成 Focusing／On a break），所以那两颗⛔ 拼、要立行：
        Assert.Equal(0, SourceGate.RowSlots("widget.pomo.doing.focusing"));
        Assert.Equal(0, SourceGate.RowSlots("widget.pomo.doing.resting"));
    }
}
