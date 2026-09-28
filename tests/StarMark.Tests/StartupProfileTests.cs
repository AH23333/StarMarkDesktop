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

    public StartupProfileTests()
    {
        // 单测绝不往用户真实的日志文件里写行
        _previousSink = StartupProfile.Sink;
        StartupProfile.Sink = _lines.Add;
    }

    public void Dispose() => StartupProfile.Sink = _previousSink;

    [Fact]
    public void MarkReportsSinceLastSegment_AndCumulativeSinceSessionStart()
    {
        StartupProfile.ResetForTests(1_000);
        StartupProfile.Mark("迁移", 1_040);
        StartupProfile.Mark("首帧", 1_090);

        Assert.Contains("[启动] 迁移：+40 ms（自会话开始累计 40 ms）", _lines);
        // 第二段自上一段起＝50，而不是自起点起的 90——把两者混了就等于每段都被高估
        Assert.Contains("[启动] 首帧：+50 ms（自会话开始累计 90 ms）", _lines);
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
