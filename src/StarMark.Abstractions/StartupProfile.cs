#nullable enable
using System;
using System.Collections.Generic;

namespace StarMark.Abstractions;

/// <summary>
/// 启动分段计时：把"启动慢"从体感变成日志里能前后对比的数。
/// <para>
/// 为什么要有它：性能清单上每条收益都写着"约 -50~150 ms"这种猜出来的数，而真机日志里已经出现过
/// "首帧阻塞 1516 ms"——没有分段数字，改完也不知道到底有没有变快，下一次还是一样靠猜。
/// 只做观测，不改任何行为；一行日志的成本换掉一整轮推测。
/// </para>
/// </summary>
public static class StartupProfile
{
    private static readonly object _gate = new();
    private static long _start = Environment.TickCount64;
    private static long _last = _start;
    private static readonly List<string> _segments = new();

    /// <summary>
    /// 输出去处。<b>测试里必须换掉</b>：单测不该往用户真实的日志文件里写行。
    /// </summary>
    internal static Action<string> Sink = StarLog.Info;

    /// <summary>记一段（自上一段起的耗时 + 自会话开始的累计）。</summary>
    public static void Mark(string segment) => Mark(segment, Environment.TickCount64);

    /// <summary>带时刻的重载只为可测：真实时钟下"增量该是几毫秒"没法断言。</summary>
    internal static void Mark(string segment, long now)
    {
        string line;
        lock (_gate)
        {
            var sinceLast = now - _last;
            _last = now;
            _segments.Add($"{segment} +{sinceLast} ms");
            line = $"[启动] {segment}：+{sinceLast} ms（自会话开始累计 {now - _start} ms）";
        }
        Sink(line);
    }

    /// <summary>本次启动已记下的分段表（诊断面板用，按记录顺序）。</summary>
    public static IReadOnlyList<string> Segments
    {
        get { lock (_gate) return _segments.ToArray(); }
    }

    /// <summary>重新对齐基线并清空分段表。测试用（真实启动路径每次都从进程起点开始，不需要重置）。</summary>
    internal static void ResetForTests(long start)
    {
        lock (_gate)
        {
            _start = start;
            _last = start;
            _segments.Clear();
        }
    }

    /// <summary>量一段真实工作并按阈值写日志，返回它的返回值。</summary>
    public static T Measure<T>(string label, Func<T> work, long logWhenMs = 0)
    {
        var from = Environment.TickCount64;
        var result = work();
        var ms = Environment.TickCount64 - from;
        if (ms >= logWhenMs) Sink($"[耗时] {label}：{ms} ms");
        return result;
    }

    /// <summary>量一段没有返回值的工作并按阈值写日志。</summary>
    public static void Measure(string label, Action work, long logWhenMs = 0)
    {
        var from = Environment.TickCount64;
        work();
        var ms = Environment.TickCount64 - from;
        if (ms >= logWhenMs) Sink($"[耗时] {label}：{ms} ms");
    }
}
