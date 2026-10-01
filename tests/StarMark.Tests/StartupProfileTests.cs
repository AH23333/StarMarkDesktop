#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 启动分段计时的契约（批次 PO-1）。
/// <para>
/// 这批不做优化，先把"到底慢在哪一段"变成日志里的数字——清单上那些"约 -50~150 ms"的收益
/// 全是没有量过的猜测，而真机日志里已经有一条 <c>[卡顿] …阻塞约 1516 ms</c>。
/// 所以这里要钉住的是<b>读法正确</b>：增量是自上一段起的、累计是自会话开始起的、
/// 顺序与标签一一对应、慢阈值真的会筛掉常态噪声、计时壳绝不吞返回值与异常。
/// 这些错了，下一个读日志的人就会被数字骗到——比没有度量更糟。
/// </para>
/// </summary>
public sealed class StartupProfileTests : IDisposable
{
    private readonly List<string> _lines = new();
    private readonly Action<string> _previousSink;
    private readonly Func<string> _previousMemoryReader;
    private readonly Func<long?>? _previousCpuReader;

    public StartupProfileTests()
    {
        // 单测绝不往用户真实的日志文件里写行
        _previousSink = StartupProfile.Sink;
        StartupProfile.Sink = _lines.Add;
        // 也不许把真系统的读数带进断言：那会造出一条只会在某些时刻为真的"契约"（同 #212 那一族）
        _previousMemoryReader = StartupProfile.MemoryReader;
        StartupProfile.MemoryReader = () => "读数占位";
        _previousCpuReader = StartupProfile.ThreadCpuMsReader;
        StartupProfile.ThreadCpuMsReader = () => 7;
    }

    public void Dispose()
    {
        StartupProfile.Sink = _previousSink;
        StartupProfile.MemoryReader = _previousMemoryReader;
        StartupProfile.ThreadCpuMsReader = _previousCpuReader;
    }

    // ===== 内存读数（批次 SS：先装尺子，再谈懒加载） =====

    [Fact]
    public void EveryMarkIsFollowedByItsOwnMemoryLine()
    {
        StartupProfile.ResetForTests(1_000);
        StartupProfile.Mark("迁移", 1_040);

        Assert.Equal(2, _lines.Count);
        Assert.StartsWith("[启动] 迁移：", _lines[0], StringComparison.Ordinal);
        // 成对且同名：两条分开或标签错位，读表的人就会把上一段的内存涨幅安到这一段头上
        Assert.Equal("[内存] 迁移：读数占位", _lines[1]);
    }

    [Fact]
    public void TheSecondMarkPairsWithItsOwnLabelNotTheFirst()
    {
        StartupProfile.ResetForTests(1_000);
        StartupProfile.Mark("第一段", 1_010);
        StartupProfile.Mark("第二段", 1_020);

        Assert.Equal("[内存] 第一段：读数占位", _lines[1]);
        Assert.Equal("[内存] 第二段：读数占位", _lines[3]);
    }

    /// <summary>高频计时壳（逐组件、逐源）不配内存行：每段都读一次系统，开销就跑进被量的那段里。</summary>
    [Fact]
    public void MeasureStaysSilentAboutMemory()
    {
        StartupProfile.Measure("逐组件的细段", () => { });
        Assert.Single(_lines);
        Assert.StartsWith("[耗时] 逐组件的细段", _lines[0], StringComparison.Ordinal);
    }

    // ===== 本线程 CPU（批次 TC：让"首帧→让出"这种不到 1 秒的段也能算出"在算还是在等"） =====
    //
    // 起因（批次 TB）：卡顿恢复行那句"其间用了 843 ms CPU"量的不是"其间"（CPU 基线取在判定为卡顿那一刻，
    // 比墙钟起点晚 1~1.5 秒）。修好之后还剩一个够不着的地方——看门狗那一列只在**连续阻塞 ≥1 s** 时才出现，
    // 于是 6 颗（首帧→让出 761 ms）与 1 颗（141 ms）两格根本没有 CPU 读数。这两把刻度之间要能直接相减，
    // 就得出 CPU 的第三格，而且必须与那两个时长**取自同一瞬间**。

    [Fact]
    public void TheCpuReadingIsSampledAtTheMarkNotOncePerSession()
    {
        var readings = new Queue<long?>(new long?[] { 7, 41 });
        StartupProfile.ThreadCpuMsReader = () => readings.Count > 0 ? readings.Dequeue() : 99;
        StartupProfile.ResetForTests(1_000);
        StartupProfile.Mark("第一段", 1_010);
        StartupProfile.Mark("第二段", 1_080);

        // 每把刻度各取各的：整条链共用一个读数，相邻两行相减永远是 0，"这段烧了多少"就又量不出来了
        Assert.Contains("CPU 累计 7 ms）", _lines[0]);
        Assert.Contains("CPU 累计 41 ms）", _lines[2]);
    }

    /// <summary>取不到就写"未知"，<b>不许退化成 0</b>——0 会伪装成"这段几乎没烧 CPU"，那正是把批次 TB 带错方向的那个结论。</summary>
    [Fact]
    public void AnUnmeasurableThreadCpuStaysUnknownNotZero()
    {
        StartupProfile.ThreadCpuMsReader = () => null;
        StartupProfile.ResetForTests(1_000);
        StartupProfile.Mark("迁移", 1_040);

        Assert.Contains("CPU 累计 未知）", _lines[0]);
        Assert.DoesNotContain("CPU 累计 0", _lines[0]);
    }

    /// <summary>
    /// 读数必须<b>点明它是哪根线程的</b>：启动里的刻度大多在 UI 线程上打，但不是全部（恢复任务、后台服务启动点都有）。
    /// 没有这个号，两把分别打在不同线程上的刻度就能被当成同一段相减——批次 TB 那个错只是换了个载体重来一遍。
    /// </summary>
    [Fact]
    public void TheCpuFieldNamesTheThreadItWasSampledOn()
        => Assert.Contains($"线程#{Environment.CurrentManagedThreadId} CPU 累计", LineOfFirstMark());

    private string LineOfFirstMark()
    {
        StartupProfile.ResetForTests(1_000);
        StartupProfile.Mark("第一段", 1_010);
        return _lines[0];
    }

    /// <summary>高频计时壳同样不配 CPU 行：它逐组件、逐源地打，每段都读一次就把量具放进了被量的那段。</summary>
    [Fact]
    public void MeasureStaysSilentAboutThreadCpu()
    {
        StartupProfile.Measure("逐组件的细段", () => { });
        Assert.DoesNotContain("CPU 累计", Assert.Single(_lines));
    }

    /// <summary>没接线（宿主没把读数交进来）也写"未知"，不许写成 0——0 会被读成"这段几乎没烧 CPU"。</summary>
    [Fact]
    public void AnUnwiredRulerSaysUnknownInsteadOfLookingLikeAMeasurement()
    {
        StartupProfile.ThreadCpuMsReader = null;
        StartupProfile.ResetForTests(1_000);
        StartupProfile.Mark("第一段", 1_010);

        Assert.Contains("CPU 累计 未知）", _lines[0]);
        Assert.DoesNotContain("CPU 累计 0", _lines[0]);
    }

    // ---- 互操作放在哪一层、什么时候接线（层次闸门把我从 Abstractions 里赶出来之后补的三条钉） ----

    /// <summary>刻度这一层只留一个可注入的读数口子，不许出现 Win32 互操作（SP 立的层次闸门守着这件事）。</summary>
    [Fact]
    public void TheRulerLayerStaysFreeOfWin32()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Abstractions/StartupProfile.cs"));
        Assert.DoesNotContain("DllImport", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetThreadTimes", code, StringComparison.Ordinal);
        Assert.Contains("public static Func<long?>? ThreadCpuMsReader", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 自读与跨线程读<b>同一个主人</b>：都住在卡顿看门狗那份文件里、共用那一个 <c>GetThreadTimes</c> 声明；
    /// 读自己走当前线程<b>伪句柄</b>，那条路上没有权利位可写错（批次 WJ 栽的就是权利位）。
    /// </summary>
    [Fact]
    public void TheSelfReadLivesWithTheOtherThreadTimesInterop()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Helpers/UIStallWatchdog.cs"));
        Assert.Contains("GetThreadTimes(CurrentThreadHandle", code, StringComparison.Ordinal);
        Assert.Contains("CurrentThreadHandle = new(-2)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenThread",
            SourceGate.MethodBody(code, "long? SelfThreadCpuMs"), StringComparison.Ordinal);
        // 换算口径只许有一份 per 用途：两处（跨线程／自读）都写 (kernel + user) / 10_000，改了其中一处就要分岔
        Assert.Equal(2, SourceGate.Count(code, "(kernel + user) / 10_000"));
    }

    /// <summary>接线必须早于第一把刻度：晚一步，启动最前面那几段就永远"未知"，而那几段正是最想看的。</summary>
    [Fact]
    public void TheRulerIsWiredBeforeTheFirstMark()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/App.xaml.cs"));
        var wired = code.IndexOf("InstallSelfCpuReader()", StringComparison.Ordinal);
        var firstMark = code.IndexOf("StartupProfile.Mark(", StringComparison.Ordinal);
        Assert.True(wired >= 0, "App 里找不到接线那一行 ⇒ 刻度行的 CPU 列会一路空白");
        Assert.True(firstMark >= 0, "App 里已经没有刻度了？那这条闸门要去看真正的调用点");
        Assert.True(wired < firstMark,
            $"接线（偏移 {wired}）晚于第一把刻度（偏移 {firstMark}）⇒ 启动最前面那几段量不到 CPU");
    }

    /// <summary>
    /// "这一列拿不到"必须**当场说一句、且只说一次**：它不崩、不报错、LastError 也不动，
    /// 唯一的症状是那根柱子从此空白——而它是"这段到底是算还是等"唯一能定方向的数据（批次 WJ／TB）。
    /// </summary>
    [Fact]
    public void TheUnavailabilityIsReportedOnce()
    {
        var body = SourceGate.MethodBody(
            SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Abstractions/StartupProfile.cs")),
            "string CpuUnknown");
        Assert.Contains("StarLog.Warn", body, StringComparison.Ordinal);
        Assert.Contains("_cpuUnavailableReported", body, StringComparison.Ordinal);   // 只说一次，别把量具变成噪声源
    }

    /// <summary>那句 CPU 只许有一个出处：写在两处，两处的口径迟早分岔（P-122/P-123 那条线上量过不止一次）。</summary>
    [Fact]
    public void TheCpuFieldIsFormattedInExactlyOnePlace()
        => Assert.Equal(1, SourceGate.Count(
            SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Abstractions/StartupProfile.cs")), "CPU 累计"));

    /// <summary>尺子自己也得有一条真调用的证人：断言<b>形状</b>而不是数值（数值随机器与时序变）。</summary>
    [Fact]
    public void TheRealMemoryLineCarriesEveryField()
    {
        var line = StartupProfile.SystemMemoryLine();
        var match = System.Text.RegularExpressions.Regex.Match(line,
            @"工作集 (\d+) MB · 私有 (\d+) MB · 托管堆 (\d+) MB · 句柄 (\d+) · 程序集 (\d+)");

        Assert.True(match.Success, $"真实读数形状不对，前后没法对比：{line}");
        Assert.True(int.Parse(match.Groups[5].Value) > 0, "程序集数为 0 ⇒ 这一格是空的，量不到加载");
        Assert.True(int.Parse(match.Groups[4].Value) > 0, "句柄数为 0 ⇒ 窗口/句柄泄漏那条证据是空的");
    }

    /// <summary>
    /// 每一行读数都必须真的重问系统（批次 SS）。
    /// <para>起因是 SS 的第一跑：读<b>自己</b>这个进程时，<c>Process</c> 对象会把首次读到的值缓存下来，
    /// 于是整条启动链上那五格内存读数一字不差（工作集 109 MB、句柄 561 从头到尾不动），
    /// 而同一时刻外部采样同一个 PID 明明是 292 MB／2044 句柄。
    /// 一条"看着有、其实恒定"的读数比空白更误导人——读表的人会得出"这一段没涨"的结论。</para>
    /// <para>断言只借<b>我自己</b>新开的那批内核句柄：200 个的量，别的用例再怎么开合也抵不掉，
    /// 所以这条不会随时序飘红；而它恰好就是"删掉 <c>Refresh()</c>"那个变异的证人。</para>
    /// </summary>
    [Fact]
    public void TheRealMemoryLineReReadsTheSystemEveryTime()
    {
        var before = HandleCountOf(StartupProfile.SystemMemoryLine());
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"StarMarkRuler_{Guid.NewGuid():N}.tmp");
        System.IO.File.WriteAllText(path, "probe");
        var held = new List<IDisposable>();
        try
        {
            for (var i = 0; i < 200; i++)
                held.Add(System.IO.File.Open(path, System.IO.FileMode.Open,
                    System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite));

            var after = HandleCountOf(StartupProfile.SystemMemoryLine());
            Assert.True(after - before >= 100,
                $"自己开了 200 个句柄，读数却只动了 {after - before}（{before} → {after}）⇒ 尺子把首次读数缓存住了");
        }
        finally
        {
            foreach (var h in held) h.Dispose();
            System.IO.File.Delete(path);
        }
    }

    private static int HandleCountOf(string line)
    {
        var match = System.Text.RegularExpressions.Regex.Match(line, "句柄 (\\d+)");
        Assert.True(match.Success, $"读数里没有句柄那一格，前后没法对比：{line}");
        return int.Parse(match.Groups[1].Value);
    }

    [Fact]
    public void MarkReportsSinceLastSegment_AndCumulativeSinceSessionStart()
    {
        StartupProfile.ResetForTests(1_000);
        StartupProfile.Mark("迁移", 1_040);
        StartupProfile.Mark("首帧", 1_090);

        // 批次 TC 起，这一行还带着"这根线程到这里为止烧了多少 CPU"（断言里用注入值，不许带真读数）
        var tail = $"｜线程#{Environment.CurrentManagedThreadId} CPU 累计 7 ms）";
        Assert.Contains("[启动] 迁移：+40 ms（自会话开始累计 40 ms" + tail, _lines);
        // 第二段自上一段起＝50，而不是自起点起的 90——把两者混了就等于每段都被高估
        Assert.Contains("[启动] 首帧：+50 ms（自会话开始累计 90 ms" + tail, _lines);
    }

    [Fact]
    public void SegmentTableKeepsLabelAndDeltaTogether()
    {
        StartupProfile.ResetForTests(2_000);
        StartupProfile.Mark("第一段", 2_030);
        StartupProfile.Mark("第二段", 2_100);

        var segments = StartupProfile.Segments;
        Assert.Equal(2, segments.Count);
        Assert.Equal("第一段 +30 ms", segments[0]);
        Assert.Equal("第二段 +70 ms", segments[1]);      // 错位一格就会把慢的段安到别的标签上
        // Mark 同时推进"最后一个刻度"，且用的是表里同一份文字——两处不同源，看门狗报的就是另一个动作
        Assert.Equal(segments[^1], StartupProfile.LastCheckpoint);
    }

    [Fact]
    public void MeasureOnlyWritesWhenTheThresholdIsReached()
    {
        var value = StartupProfile.Measure("常态段", () => 7, logWhenMs: 60_000);
        Assert.Equal(7, value);                            // 计时壳必须原样交回结果
        Assert.Empty(_lines);                              // 没到阈值就不该产噪声
    }

    [Fact]
    public void MeasureNeverSwallowsWhatItWraps()
    {
        // 度量代码最容易犯的错：为了"不影响被测量那段"把异常吞掉——那会让故障静默成"没日志"
        Assert.Throws<InvalidOperationException>(() =>
            StartupProfile.Measure("炸的段", () => throw new InvalidOperationException("boom"), logWhenMs: 60_000));
        Assert.Throws<InvalidOperationException>(() =>
            StartupProfile.Measure("炸的段", () => throw new InvalidOperationException("boom")));
    }

    /// <summary>
    /// 尺子本身必须比一个系统计时器节拍细（批次 WE-2）。
    /// <para>起因是真机日志里那一串整整齐齐的"+15 ms / +16 ms / +31 ms"：那是
    /// <c>Environment.TickCount64</c> 的 15.6 ms 量化台阶，不是实测值——2 ms 的段与 15 ms 的段
    /// 在日志里长得一样，于是"首屏之后那 1.5 s 是谁占着"永远读不出来。
    /// 比没有度量更糟的是<b>看起来有度量</b>。</para>
    /// </summary>
    [Fact]
    public void AShortSegmentIsNotRoundedToATimerTick()
    {
        Assert.True(StartupProfile.ClockTicksPerSecond >= 1_000_000,
            $"计时源只有 {StartupProfile.ClockTicksPerSecond} 刻度/秒，量不出毫秒级的段");

        StartupProfile.Measure("约 6 毫秒的细段", SpinSixMs, logWhenMs: 0);
        var reported = System.Text.RegularExpressions.Regex.Match(_lines[^1], @"：(\d+) ms");
        Assert.True(reported.Success, $"这一行没有可解析的毫秒数：{_lines[^1]}");
        var ms = int.Parse(reported.Groups[1].Value);
        // 节拍量化只会给出 0 或 15/16；两者都不是"这段到底多长"的答案
        Assert.InRange(ms, 2, 60);
    }

    /// <summary>整份计时只许有一个时钟，且那个时钟不许是 TickCount64（换回去就悄悄退回量化台阶）。</summary>
    [Fact]
    public void TheRulerHasExactlyOneClockAndItIsNotTickCount()
    {
        var code = SourceGate.ReadRepoFile("src/StarMark.Abstractions/StartupProfile.cs");
        // 只看代码行：那段"为什么不用系统计时器"的注释里必须写出它的名字，
        // 连注释一起数会变成守门自己被自己的解释打红（这条坑记过不止一次）。
        var offenders = code.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("Environment.TickCount64") && !line.StartsWith("//"))
            .ToList();
        Assert.Empty(offenders);
        Assert.Equal(1, SourceGate.Count(code, "Stopwatch.StartNew()"));
    }

    /// <summary>
    /// 没到写日志阈值的段，<b>照样要把"最后一个刻度"往前推</b>（批次 WE-2）。
    /// <para>起因：启动里最重的那块（建 19 个组件窗）只在结束时 Mark 一次，中间全是按阈值过滤的
    /// <c>Measure</c>。卡在那块工作正中间时，"分段表末尾"还是上一笔"首帧提交"——等于没报，
    /// 而那次冻结恰恰是本轮唯一要查的对象。刻度必须由每一段推进，日志才由阈值决定。</para>
    /// </summary>
    [Fact]
    public void AQuietMeasureStillMovesTheCheckpoint()
    {
        StartupProfile.ResetForTests(0);
        StartupProfile.Measure("远低于阈值的静默段", () => { }, logWhenMs: 60_000);
        Assert.Contains("远低于阈值的静默段", StartupProfile.LastCheckpoint);
        // 另一个壳（有返回值的那个）也得推进：接线处两种都在用，只改一个臂＝另一半照旧报"无"
        StartupProfile.Measure("有返回值的静默段", () => 7, logWhenMs: 60_000);
        Assert.Contains("有返回值的静默段", StartupProfile.LastCheckpoint);
        Assert.Empty(_lines);                                        // 阈值照旧筛掉噪声
    }

    /// <summary>抛出去的那段不算"完成"：看门狗要看到的是"最后一个做完了的动作"，卡住的那个由时长自己说。</summary>
    [Fact]
    public void AFailingMeasureLeavesTheCheckpointOnTheLastThingThatFinished()
    {
        StartupProfile.ResetForTests(0);
        StartupProfile.Mark("做完了的段", 5);
        Assert.Throws<InvalidOperationException>(() =>
            StartupProfile.Measure("炸的无返回值段", () => throw new InvalidOperationException("boom")));
        Assert.StartsWith("做完了的段", StartupProfile.LastCheckpoint);
        Assert.Throws<InvalidOperationException>(() =>
            StartupProfile.Measure<int>("炸的有返回值段", () => throw new InvalidOperationException("boom")));
        Assert.StartsWith("做完了的段", StartupProfile.LastCheckpoint);   // 两个壳都不许把没做完的记成刻度
    }

    /// <summary>
    /// 刻度必须连年龄一起给（批次 WE-2）。
    /// <para>刻度表基本只在启动阶段前进，之后长期不动。看门狗只报标签的话，一次发生在十分钟之后的
    /// 冻结会被写成"卡在『组件恢复任务返回』之前"——读的人照着启动那条链找，什么都找不到。
    /// 这里钉两件事：没人打新刻度时年龄要跟着长，打了新刻度要归零。</para>
    /// </summary>
    [Fact]
    public void TheCheckpointAgeFollowsTheNewestCheckpoint()
    {
        StartupProfile.ResetForTests(0);
        Assert.Null(StartupProfile.LastCheckpointAgeMs);          // 一条刻度都没有时报"未知"，不报 0
        StartupProfile.Measure("第一段", () => { });
        SpinSixMs();
        var aged = StartupProfile.LastCheckpointAgeMs;
        Assert.NotNull(aged);
        Assert.True(aged > 0, $"空转了 6 ms，年龄却还是 {aged} ms ⇒ 年龄没跟着真实时钟走");
        StartupProfile.Measure("第二段", () => { });
        Assert.True(StartupProfile.LastCheckpointAgeMs < aged,
            "打了新刻度之后年龄还更大 ⇒ 标签换了、年龄没换，日志会说谎");
    }

    private static void SpinSixMs()
    {
        var until = System.Diagnostics.Stopwatch.StartNew();
        while (until.ElapsedMilliseconds < 6) { }
    }
}
