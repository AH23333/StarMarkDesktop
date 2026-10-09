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
/// 批次 WI-60（P-188 第二刀＋P-192 第一颗＋P-193 那颗行名）的闸门：
/// <b>系统监控那一扇屏幕上会说话的那几格只许住在表里，而 Core、Integrations、VM 三层交出的是还没取词的那一句</b>。
/// <para>
/// 射程＝同一张组件窗的同一屏：<c>SystemMonitorViewModel.cs</c>（P-188）、<c>SystemMonitorPolicy.cs</c>（P-193
/// 的行名目录）、<c>SystemMetricsProbe.cs</c> 与 <c>SystemMonitorNative.cs</c>（P-192 的失败原因），
/// 加那颗宿主 <c>SystemMonitorWidget.xaml{.cs}</c>。这一扇是 WI-59a 名单里<b>唯一"三本账同一颗宿主"</b>的一扇
/// ——拆刀就是屏幕上半中半英，所以三层要同批立在这里。
/// </para>
/// <para><b>从前那三格各是什么形状</b>：行名是 Core 目录里写死的「内存」「网速」，
/// 次要说明与行内原因是 VM／探针里拼好的中文成品字（「已用 6.5 GB / 15.7 GB」「读取系统时间失败（Win32 5）」），
/// 三条通道一起贴到同一屏上；换语言那一刻只有数字在改口。而那颗宿主的行名当年还钉着
/// <c>Mode=OneTime</c>——就算交的是键，第一遍贴上去之后也没人回去重念。</para>
/// <para><b>⚠ 与 ✅WI-59 那一句分岔的地方</b>：番茄钟那一扇靠"每秒一拍把四颗绑定属性重算一遍"当重贴，
/// 所以它⛔ 另开订阅者；这一扇那一拍是<b>采样</b>拍（1 秒／省资源 3 秒），换语言并不必然等来下一拍，
/// 而行名与那句原因<b>不在采样里重算</b>——所以这一扇<b>必须</b>订阅，且订阅与摘掉成对（坑表 #386）。
/// 主人名单因此从八位变成九位；那份名单与"成对订阅"由 <c>I18nWiringGateTests</c> 钉着，
/// 这里只补它看不见的几件事：重贴那一路⛔ 顺手采样、⛔ 重建行集合。</para>
/// <para>⚠ 目录那颗键的<b>存在性／颗数／覆盖</b>已由 <c>SystemMonitorPolicyTests.Catalog_CoversEverySelectableMetricExactlyOnce</c>
/// 逐颗点名（那颗是本批改的），这里⛔ 再把那三颗键抄一遍——只钉那颗跨层的事：同一格行名与勾选标签<b>是不是一句词</b>。</para>
/// <para>证人分两类：源码形状（<c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>，坑表 #370 那条边界）
/// 与能真跑的 Core／Abstractions 读数（目录那颗键与每一颗句子的三语念法点得着 ⇒ 那几格⛔ 只读形状）。
/// 屏幕上到底是哪一版念哪一句、三条通道各自失败时那一行长什么样，仍欠真机读数（#575 那一跑）；
/// <c>widget.mon.err.rowSize</c>／<c>fieldOffset</c> 那两句在本机<b>走不到</b>（布局自检通过）——
/// 这里只验到"键＋槽＋三语念法"，那条臂本身登记为<b>未验证</b>。</para>
/// <para>尺＝抹注释后逐行取连续 CJK 片段，<b>字符类含全角那一族</b>（坑表 #403／#407）。出现次数口径，⛔ 去重。
/// 颗数一律来自 <c>bin/WI60/census_60b.txt</c>（2026-10-10 现场复算），⛔ 凭记忆（规则 #159）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class MonitorWordGateTests : IDisposable
{
    private const string VmCs = "src/StarMark.UI/ViewModels/SystemMonitorViewModel.cs";
    private const string CoreCs = "src/StarMark.Core/Widgets/SystemMonitorPolicy.cs";
    private const string ProbeCs = "src/StarMark.Integrations/SystemMonitor/SystemMetricsProbe.cs";
    private const string NativeCs = "src/StarMark.Integrations/SystemMonitor/SystemMonitorNative.cs";
    private const string HostCs = "src/StarMark.UI/Views/SystemMonitorWidget.xaml.cs";
    private const string HostXaml = "src/StarMark.UI/Views/SystemMonitorWidget.xaml";
    private const string TableCs = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    /// <summary>三层各自"供话"的那两档＋Core 目录：它们⛔ 取词（取词只发生在 VM 重念的那一刻）。</summary>
    private static readonly string[] Suppliers = { CoreCs, ProbeCs, NativeCs };

    private const string CjkPattern = @"[぀-ヿ㐀-䶿一-鿿　-〿＀-￯]+";
    private static readonly Regex Cjk = new(CjkPattern, RegexOptions.Compiled);

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>屏幕字计数口径：抹注释后逐行取 CJK（含全角那一族），排除日志抬头那一行。</summary>
    private static List<string> ScreenWords(string code)
        => code.Split('\n').Select(l => l.Trim())
               .Where(l => l.Length > 0 && !l.StartsWith("StarLog.", StringComparison.Ordinal)
                                                && !l.StartsWith("Debug.", StringComparison.Ordinal)
                                                && !l.StartsWith("Trace.", StringComparison.Ordinal))
               .Where(l => Cjk.IsMatch(l))
               .ToList();

    private static string LabelOf(MonitorMetric metric)
        => SystemMonitorPolicy.Catalog.Single(e => e.Metric == metric).LabelKey;

    /// <summary>
    /// <b>供话者⛔ 自己取词</b>（形状禁项，与 <see cref="TheSentencesAnswerInEveryLanguageAndCarryTheMachineFact"/>
    /// 那颗语言读数成对＝规则 #124）。
    /// <para>本批之前的形状是三型读数各带一颗 <c>string? Error</c>（中文成品字），探针里连
    /// <c>$"读取系统时间失败（Win32 {…}）"</c> 都是当场拼好的——所以这里既禁"取词"也禁"存成品字"：
    /// <c>LocMessage? Error</c> 必须恰好三颗（三条通道各一颗），<c>string? Error</c> 一颗都不许留。
    /// VM 那一样：三格存的是身份（<c>private LocMessage?</c> 三颗＝行内说明／行内原因／底部说明），
    /// 多条理由的连接符⛔ 在代码里拼（<c>string.Join(</c> 必须为 0——那句话住在 <c>widget.mon.note.join</c>）。</para>
    /// <para>取词只发生在"重念已经拿到手的那几句"这一刻：VM 里 <c>.Render()</c> 恰好六颗
    /// （SetDetail／SetError／SetNote 各一颗＝采样那一刻贴上去，Repaint 两颗＝Detail＋Error，RepaintWords 一颗＝Note），
    /// <c>Locale.T(</c> 恰好一颗＝行名。期望⛔ 用"⩾"：下界抓不到"少一颗"，而多一颗时这一格连着注释说出是哪一颗。</para>
    /// </summary>
    [Fact]
    public void TheThreeLayersHandIdentitiesAndNotSentences()
    {
        foreach (var rel in Suppliers)
        {
            var code = CodeOf(rel);
            Assert.Equal(0, SourceGate.Count(code, "Locale.T("));
            Assert.Equal(0, SourceGate.Count(code, "Locale.Fmt("));
            Assert.Equal(0, SourceGate.Count(code, ".Render()"));
        }

        var probe = CodeOf(ProbeCs);
        Assert.Equal(3, SourceGate.Count(probe, "LocMessage? Error"));   // Cpu／Memory／Network 三型读数
        Assert.Equal(0, SourceGate.Count(probe, "string? Error"));       // ⛔ 留一份旧的字符串出口

        var vm = CodeOf(VmCs);
        Assert.Equal(3, SourceGate.Count(vm, "private LocMessage?"));    // 行内说明／行内原因／底部说明
        Assert.Equal(0, SourceGate.Count(vm, "string.Join("));
        Assert.Equal(6, SourceGate.Count(vm, ".Render()"));
        Assert.Equal(1, SourceGate.Count(vm, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(vm, "Locale.Fmt("));
        Assert.DoesNotMatch(@"(Detail|Error|Note) = """, vm);            // ⛔ 再往那三格上贴成品字

        Assert.Equal(0, SourceGate.Count(CodeOf(HostCs), ".Render()"));  // 宿主只转发，自己⛔ 取词
    }

    /// <summary>
    /// <b>这一路一颗非日志屏幕字都不许留</b>（登记剩余量必须把尺子写进句子＝规则 #126）。
    /// 落地前现场：VM 11 行、Core 目录 2 行、探针 11 行、声明 2 行，宿主 <c>.xaml.cs</c> 本来就是 0 行
    /// （<c>bin/WI60/roster_60a.txt</c>）。XAML 那一侧另有两把尺：写死的 <c>Text=</c> 必须 0 处，
    /// 而 <c>LocText.Key</c> 必须恰好勾选面板那五颗（none／pick／cpu／mem／net）。
    /// </summary>
    [Fact]
    public void NoScreenWordSurvivesOnTheMonitorPath()
    {
        foreach (var rel in new[] { VmCs, CoreCs, ProbeCs, NativeCs, HostCs })
        {
            var left = ScreenWords(CodeOf(rel));
            Assert.True(left.Count == 0, $"{rel} 还剩 {left.Count} 行带字的代码：{string.Join(" ⧸ ", left.Take(4))}");
        }

        var xaml = Regex.Replace(SourceGate.ReadRepoFile(HostXaml), @"<!--.*?-->", " ", RegexOptions.Singleline);
        var hard = Regex.Matches(xaml, "Text=\"([^\"]*)\"").Cast<Match>()
                       .Select(m => m.Groups[1].Value).Where(s => Cjk.IsMatch(s)).ToList();
        Assert.True(hard.Count == 0, "XAML 里还写着成品字：" + string.Join(" ⧸ ", hard));
        Assert.Equal(5, SourceGate.Count(xaml, @"LocText.Key=""widget.mon."));
    }

    /// <summary>
    /// <b>每一处调用点按代码实给的实参数比槽数</b>（坑表 #393 那颗"屏幕上那一格整空白"的通用版；
    /// 尺⛔ 用正则切跨行调用点——按括号深度走到与 <c>Of(</c> 配对的 <c>)</c>＝公共那颗
    /// <see cref="SourceGate.KeyedCallSites"/>，规则 #86/#155）。
    /// <para>名单来自现场复算：<b>25 处</b>＝VM 12 ＋ 探针 11 ＋ 声明 2。少一颗（某句话又退回硬写字符串）
    /// 或多一颗（新增了句子却没比槽）都会在这里红。</para>
    /// </summary>
    [Fact]
    public void EveryKeyedCallHandsExactlyTheSlotsItsRowDeclares()
    {
        var watched = 0;
        foreach (var rel in new[] { VmCs, ProbeCs, NativeCs })
            foreach (var (key, args, line) in SourceGate.KeyedCallSites(CodeOf(rel)))
            {
                watched++;
                Assert.True(args == SourceGate.RowSlots(key),
                            $"{rel}:{line} 给 {key} 递了 {args} 颗实参，而表里那颗行有 {SourceGate.RowSlots(key)} 颗槽");
            }
        Assert.Equal(25, watched);
    }

    /// <summary>
    /// <b>颗数按主人点名</b>（规则 #98/#111：只钉总数抓不到"换了一颗键名"）。
    /// 探针那 11 颗、声明那 2 颗各自<b>恰好一处</b>调用点——同一句原因⛔ 有两个主人；
    /// VM 那 12 处里 <c>widget.mon.accruing</c> 恰好三处（CPU 第一拍、窗平均还没成形、网速没有基线＝三个臂同一句话），
    /// 其余九颗各一处。
    /// </summary>
    [Fact]
    public void EachReasonHasOneOwnerAndTheNamedRosterStillAddsUp()
    {
        var probe = SourceGate.KeyedCallSites(CodeOf(ProbeCs));
        Assert.Equal(11, probe.Count);
        Assert.All(probe.GroupBy(c => c.Key).ToList(), g => Assert.Single(g));
        Assert.Equal(new[]
        {
            "widget.mon.err.ifRowCount", "widget.mon.err.ifTableException", "widget.mon.err.ifTableHelper",
            "widget.mon.err.ifTableMismatch", "widget.mon.err.ipHelperTooOld", "widget.mon.err.memStateException",
            "widget.mon.err.memStateWin32", "widget.mon.err.memTotalZero", "widget.mon.err.noIpHelper",
            "widget.mon.err.sysTimeException", "widget.mon.err.sysTimeWin32",
        }, probe.Select(c => c.Key).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray());

        var native = SourceGate.KeyedCallSites(CodeOf(NativeCs));
        Assert.Equal(new[] { "widget.mon.err.fieldOffset", "widget.mon.err.rowSize" },
                     native.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray());

        var vm = SourceGate.KeyedCallSites(CodeOf(VmCs));
        Assert.Equal(12, vm.Count);
        Assert.Equal(10, vm.Select(c => c.Key).Distinct().Count());
        Assert.Equal(3, vm.Count(c => c.Key == "widget.mon.accruing"));
        Assert.All(vm.Where(c => c.Key != "widget.mon.accruing").Select(c => c.Key).Distinct().ToList(),
                   key => Assert.Equal(1, vm.Count(c => c.Key == key)));
    }

    /// <summary>
    /// <b>行名那颗键与勾选标签那颗键：一颗词只许一个主人</b>（这颗只在跨层那一点上有意思，
    /// 目录自己的覆盖与颗数在 <c>SystemMonitorPolicyTests</c> 那边）：
    /// 「网速」两侧<b>共用</b> <c>widget.mon.net</c>（勾选行与行名中文列一模一样，另立一行就是第二个主人——
    /// 所以这里禁 <c>widget.mon.row.net</c> 进表）；而 CPU／内存两颗<b>必须</b>另立：
    /// 勾选行的中文列多着「占用」二字，复用等于把屏幕上那两个词换掉，而换措辞是产品决定（发起人那句
    /// 「精简说明文字」指的是说明行，不是行名）。两两不同那一格⛔ 抄表里的措辞（规则 #130）：现取那颗键比。
    /// </summary>
    [Fact]
    public void TheRowLabelAndTheCheckboxLabelAreOneWordOrTwoOnPurpose()
    {
        var core = CodeOf(CoreCs);
        Assert.Contains("(MonitorMetric Metric, string LabelKey, string Glyph)> Catalog { get; }", core, StringComparison.Ordinal);
        Assert.Equal(0, SourceGate.Count(core, "string Label,"));        // ⛔ 退回那个能放成品字的字段名
        Assert.DoesNotContain(@"""widget.mon.row.net""", SourceGate.ReadRepoFile(TableCs), StringComparison.Ordinal);

        Assert.Equal("widget.mon.net", LabelOf(MonitorMetric.Network));

        foreach (var (metric, checkboxKey) in new[]
                 { (MonitorMetric.Cpu, "widget.mon.cpu"), (MonitorMetric.Memory, "widget.mon.mem") })
        {
            Assert.NotEqual(checkboxKey, LabelOf(metric));
            foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
            {
                Locale.TrySet(language);
                Assert.NotEqual(Locale.T(checkboxKey), Locale.T(LabelOf(metric)));   // 两颗词真不同（不是同名换字）
            }
        }

        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal(Locale.T("widget.mon.net"), Locale.T(LabelOf(MonitorMetric.Network)));   // 同一颗键，不是两份措辞
    }

    /// <summary>
    /// <b>同一颗键按语言各念一句，而机器事实逐字在场</b>（真跑那一侧，期望⛔ 抄表里的措辞＝规则 #130）。三件事分开钉：
    /// ① 行名那颗键换语言真的改口（本批修掉的正是"Core 交的是「内存」两个字"）；
    /// ② 带实参的那几句里，Win32 码／异常正文／字节数／字段名<b>在三语中都逐字在场</b>——机器事实进的是槽不是句子；
    /// ③ 三语念法两两不同（一列抄了中文、或一颗没人改口的键会红在这里）。
    /// <para>⚠ 两颗<b>刻意同形</b>的键单列在下一格：<c>widget.mon.row.cpu</c>（CPU 在三种写法里都那么念）与
    /// <c>widget.mon.upRate</c>（整颗就是一支箭头加读数，没有词）。这一格钉的是"同形是决定"而不是"漏翻"——
    /// 哪天要把那颗日文列换成片假名，这里该红一次。</para>
    /// </summary>
    [Fact]
    public void TheSentencesAnswerInEveryLanguageAndCarryTheMachineFact()
    {
        Locale.TrySet(AppLanguage.Chinese); var memZh = Locale.T(LabelOf(MonitorMetric.Memory));
        Locale.TrySet(AppLanguage.English); var memEn = Locale.T(LabelOf(MonitorMetric.Memory));
        Locale.TrySet(AppLanguage.Japanese); var memJa = Locale.T(LabelOf(MonitorMetric.Memory));
        Assert.True(Cjk.IsMatch(memZh) && Cjk.IsMatch(memJa));       // 中文／日文那一档是本族文字
        Assert.Empty(Cjk.Matches(memEn).Cast<Match>().Select(m => m.Value));   // 拉丁那一档⛔ 留着上一次取好的词
        Assert.NotEqual(memZh, memEn);
        Assert.NotEqual(memEn, memJa);
        Locale.TrySet(AppLanguage.Chinese); var netZh = Locale.T(LabelOf(MonitorMetric.Network));
        Locale.TrySet(AppLanguage.English); Assert.NotEqual(netZh, Locale.T(LabelOf(MonitorMetric.Network)));

        var facts = new (string Key, object?[] Args)[]
        {
            ("widget.mon.err.sysTimeWin32", new object?[] { 5 }),
            ("widget.mon.err.memStateWin32", new object?[] { 87 }),
            ("widget.mon.err.ifTableHelper", new object?[] { 1223 }),
            ("widget.mon.err.ifRowCount", new object?[] { -1 }),
            ("widget.mon.err.rowSize", new object?[] { 8, 176 }),
            ("widget.mon.err.fieldOffset", new object?[] { "OperStatus", 12, 68 }),
            ("widget.mon.err.sysTimeException", new object?[] { "boom" }),
            ("widget.mon.err.memStateException", new object?[] { "boom" }),
            ("widget.mon.err.ifTableException", new object?[] { "boom" }),
            ("widget.mon.mem.used", new object?[] { "6.5 GB", "15.7 GB" }),
            ("widget.mon.net.virtualFallback", new object?[] { 3 }),
        };
        foreach (var (key, args) in facts)
        {
            var message = LocMessage.Of(key, args);
            Locale.TrySet(AppLanguage.Chinese); var zh = message.Render();
            Locale.TrySet(AppLanguage.English); var en = message.Render();
            Locale.TrySet(AppLanguage.Japanese); var ja = message.Render();
            foreach (var fact in args)
            {
                var asText = fact?.ToString();
                Assert.False(string.IsNullOrEmpty(asText), $"{key} 那颗实参念出来是空的");
                foreach (var text in new[] { zh, en, ja })
                    Assert.Contains(asText!, text, StringComparison.Ordinal);
            }
            Assert.NotEqual(zh, en);
            Assert.NotEqual(en, ja);
            foreach (var text in new[] { zh, en, ja })
            {
                Assert.DoesNotContain("{", text, StringComparison.Ordinal);   // 少递实参会在这里露出来（#393）
                Assert.DoesNotContain(key, text, StringComparison.Ordinal);   // ⛔ 键名漏到屏幕上
            }
        }

        foreach (var key in new[] { "widget.mon.accruing", "widget.mon.cpu.counterUnusable",
                                    "widget.mon.mem.unavailable", "widget.mon.net.noLink",
                                    "widget.mon.net.counterReset", "widget.mon.unavailable",
                                    "widget.mon.err.memTotalZero", "widget.mon.err.ifTableMismatch",
                                    "widget.mon.err.noIpHelper", "widget.mon.err.ipHelperTooOld" })
        {
            Locale.TrySet(AppLanguage.Chinese); var zh = Locale.T(key);
            Locale.TrySet(AppLanguage.English); var en = Locale.T(key);
            Locale.TrySet(AppLanguage.Japanese); var ja = Locale.T(key);
            Assert.NotEqual(zh, en);
            Assert.NotEqual(en, ja);
            Assert.DoesNotContain("{", en, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <b>两颗刻意同形的键：同形要三列一起同形，且⛔ 混进任何一版的中文字</b>。
    /// 这一格是上一格那句"⚠ 两颗刻意同形"的落地——它们走的不是"漏翻"那条路，所以得有人钉住"三列确实一样"：
    /// 一旦有人只改其中一列（比如把日文列换成片假名），这里红，而通用闸门只查"日文列没抄中文列"会放过去。
    /// </summary>
    [Fact]
    public void TheTwoDeliberatelyUntranslatedRowsStayUntranslatedTogether()
    {
        foreach (var key in new[] { "widget.mon.row.cpu", "widget.mon.upRate" })
        {
            Assert.True(UiStrings.TryGet(key, out var row), $"表里没有这颗键：{key}");
            Assert.Equal(row.Zh, row.En);
            Assert.Equal(row.En, row.Ja);
            Assert.Empty(Cjk.Matches(row.Zh).Cast<Match>().Select(m => m.Value));

            var rendered = new List<string>();
            foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
            {
                Locale.TrySet(language);
                rendered.Add(LocMessage.Of(key).Render());
            }
            Assert.All(rendered, text => Assert.Equal(rendered[0], text));    // 三语念出来同一串（⛔ 只有一档改了）
        }

        // 那颗箭头行带一颗槽：读数在三语里逐字在场，而句子本身没有词可翻；那颗行名一颗槽都不许有
        Assert.Equal(1, SourceGate.RowSlots("widget.mon.upRate"));
        Assert.Equal(0, SourceGate.RowSlots("widget.mon.row.cpu"));
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            var up = LocMessage.Of("widget.mon.upRate", "1.2 KB/s").Render();
            Assert.Contains("1.2 KB/s", up, StringComparison.Ordinal);
            Assert.DoesNotContain("{", up, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <b>多条理由并排时，那句话的语言由<b>贴上去那一刻</b>决定</b>——这一格钉套娃那颗骨架
    /// <c>widget.mon.note.join</c>：外层那句的槽里递进去的是<b>另外两颗 LocMessage</b>，
    /// 半句在 <c>string.Format</c> 调 <c>ToString</c> 的那一刻才取词。
    /// <para>⛔ 写成 <c>string.Join("；", …)</c> 那种成品字：那样每一半自己是会改口的，
    /// 而"两条理由并排"这件事的语言被定死在拼接那一刻（与 ✅WI-58 那颗 <c>trending.notice.join</c> 同一口径）。
    /// 读的是<b>连接符跟着最外层走</b>（中文／日文那一档是全角分号、拉丁那一档不是）与"整句⛔ 剩半中半英"。</para>
    /// </summary>
    [Fact]
    public void TwoReasonsJoinedAreRenderedByTheMomentTheyArePasted()
    {
        var joined = LocMessage.Of("widget.mon.note.join",
            LocMessage.Of("widget.mon.accruing"), LocMessage.Of("widget.mon.net.counterReset"));

        Locale.TrySet(AppLanguage.Chinese);
        var zh = joined.Render();
        Assert.Contains(Locale.T("widget.mon.accruing"), zh, StringComparison.Ordinal);
        Assert.Contains(Locale.T("widget.mon.net.counterReset"), zh, StringComparison.Ordinal);
        Assert.Contains("；", zh, StringComparison.Ordinal);

        Locale.TrySet(AppLanguage.English);
        var en = joined.Render();
        Assert.Contains(Locale.T("widget.mon.accruing"), en, StringComparison.Ordinal);
        Assert.Contains(Locale.T("widget.mon.net.counterReset"), en, StringComparison.Ordinal);
        Assert.DoesNotContain("；", en, StringComparison.Ordinal);              // 连接符⛔ 留在中文那一档
        Assert.Empty(Cjk.Matches(en).Cast<Match>().Select(m => m.Value));      // 半句⛔ 停在旧语言＝屏幕上那格半中半英
        Assert.DoesNotContain("{", en, StringComparison.Ordinal);

        Assert.NotEqual(zh, en);
        Assert.Equal(2, SourceGate.RowSlots("widget.mon.note.join"));
        Assert.Contains(@"LocMessage.Of(""widget.mon.note.join"", head, next)", CodeOf(VmCs), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>重贴那一路只许念已经拿到手的身份</b>（那颗订阅者的界线，坑表 #386 那一族；
    /// 名单与"订阅／摘掉成对"由 <c>I18nWiringGateTests</c> 钉，这里补它看不见的四件事）：
    /// ① VM 自己⛔ 是那颗订阅者（宿主才是，否则两路都会重念一遍）；
    /// ② <c>RepaintWords</c> 体内⛔ 采样、⛔ 重建行集合——那会把"字改口"变成"这次读数失败了"或一次闪烁；
    /// ③ 行对象<b>建出来就带着名字</b>（构造里调 <c>Repaint()</c>），否则第一拍之前那一格是空的；
    /// ④ 探针没说原因时那一格⛔ 留空（"没测到"会被读成"数值真是 0"），兜底那句也走键。
    /// <para>⚠ 还有一条只在源码这层看得见：XAML 那颗行名当年钉的是 <c>Mode=OneTime</c>——
    /// 就算交的是键，第一遍贴上去之后也没人回去重念，所以连着钉它改成了 <c>OneWay</c>。</para>
    /// </summary>
    [Fact]
    public void RepaintingWordsOnlyReReadsTheIdentitiesAlreadyHeld()
    {
        Assert.Equal(0, SourceGate.Count(CodeOf(VmCs), "Locale.Changed"));

        var repaint = SourceGate.MethodBody(CodeOf(VmCs), "public void RepaintWords()");
        Assert.Contains("row.Repaint()", repaint, StringComparison.Ordinal);
        Assert.DoesNotContain("_probe.", repaint, StringComparison.Ordinal);
        Assert.DoesNotContain("RebuildRows", repaint, StringComparison.Ordinal);
        Assert.DoesNotContain("Tick(", repaint, StringComparison.Ordinal);

        var rowRepaint = SourceGate.MethodBody(CodeOf(VmCs), "internal void Repaint()");
        Assert.Contains("Label = Locale.T(LabelKey);", rowRepaint, StringComparison.Ordinal);
        Assert.DoesNotContain("_probe.", rowRepaint, StringComparison.Ordinal);

        var ctor = SourceGate.MethodBody(CodeOf(VmCs), "public MonitorRow(MonitorMetric metric, string labelKey)");
        Assert.Contains("Repaint();", ctor, StringComparison.Ordinal);

        var fail = SourceGate.MethodBody(CodeOf(VmCs), "private static void Fail(MonitorRow row, LocMessage? reason)");
        Assert.Contains(@"row.SetError(reason ?? LocMessage.Of(""widget.mon.unavailable""))", fail, StringComparison.Ordinal);

        var xaml = SourceGate.ReadRepoFile(HostXaml);
        Assert.Contains(@"Text=""{x:Bind Label, Mode=OneWay}""", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Bind Label, Mode=OneTime", xaml, StringComparison.Ordinal);
    }
}
