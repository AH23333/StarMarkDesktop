#nullable enable
using System;
using System.Collections.Generic;
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
}
