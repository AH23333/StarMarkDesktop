#nullable enable
using System;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// <b>卡顿取证那根柱子自己的守门</b>（批次 WJ）。
/// <para>
/// "启动后 1.5–3 秒 UI 冻结"能不能定方向，全看恢复行里那个 <c>CPU ms</c>：
/// CPU≈墙钟＝忙在自己手上（该拆分/挪线程），CPU≪墙钟＝在等锁或 IPC（优化 CPU 是白改）。
/// 而它写出来一直是"未知"——真因是 <c>OpenThread</c> 要的权利位写错（0x0400 是
/// <c>THREAD_DIRECT_IMPERSONATION</c>；<c>GetThreadTimes</c> 要 <c>THREAD_QUERY_INFORMATION</c>＝0x0040，
/// 而 <c>THREAD_QUERY_LIMITED_INFORMATION</c> 又是 0x0800，三个数长得几乎一样）。
/// </para>
/// <para>
/// <b>这类故障的可怕之处是它不报错</b>：句柄 0 ⇒ 取不到 ⇒ 日志少一个数 ⇒ 看起来像"系统没给"。
/// 所以这里既钉那个数，也钉"取不到必须当场说一句"。
/// </para>
/// </summary>
public sealed class StallEvidenceGateTests
{
    private const string Watchdog = "src/StarMark.UI/Helpers/UIStallWatchdog.cs";

    [Fact]
    public void ThreadAccessMaskIsTheOneGetThreadTimesRequires()
    {
        var code = ReadRepoFile(Watchdog);
        Assert.Contains("private const uint ThreadQueryInformation = 0x0040;", code);
        // 反钉那两个"看着都对"的数：换回去就是 CPU 列再次静默变"未知"
        Assert.DoesNotContain("= 0x0400", code);
        Assert.DoesNotContain("= 0x0800", code);
        Assert.Contains("OpenThread(ThreadQueryInformation, false, GetCurrentThreadId())", code);
        Assert.Contains("DllImport(\"kernel32.dll\", SetLastError = true)", code);
    }

    [Fact]
    public void MissingCpuSampleIsLoud_NotSilent()
    {
        var start = MethodBody(ReadRepoFile(Watchdog), "public static void Start(");
        Assert.Contains("if (_uiThread == IntPtr.Zero)", start);
        Assert.Contains("StarLog.Warn", start);
        Assert.Contains("GetLastWin32Error()", start);
    }

    /// <summary>
    /// 卡顿的两条日志都要带上"卡住之前最后一个完成的刻度"（批次 WE-2）。
    /// <para>
    /// 只有时长的卡顿行是一段悬空数字：真机日志里那条"阻塞约 1500 ms"前后分别是"首帧提交"和
    /// "桌面组件恢复"，而这两笔分段<b>一个在冻结前、一个在冻结后</b>，读的人无法把 1.5 s 归到任何一件具体的事上。
    /// 把起点前的刻度一起报出来，冻结才被夹在两个刻度之间。
    /// </para>
    /// <para>
    /// 钉"两条都有"而不是只钉一条：长冻结期间每隔一倍时长报一次"仍在进行"，
    /// 缺了这条就等于中间那几行全是在报数、不报位置。
    /// </para>
    /// </summary>
    [Fact]
    public void StallLinesNameTheLastCompletedCheckpoint()
    {
        var code = ReadRepoFile(Watchdog);
        var probe = MethodBody(code, "private static void Probe(");
        Assert.Equal(2, Count(probe, "卡顿前最后一个完成的刻度"));
        // 起点快照只在"判定为卡顿的那一刻"做一次：事后（恢复行）再读，读到的是卡顿期间才补上的刻度，
        // 那会把"卡在哪之前"说成"卡在哪之后"——方向正好反过来。
        Between(probe, "if (Interlocked.CompareExchange", "_stallCheckpoint = CheckpointAtStallStart();");
        // 恢复行必须用"清空之前先取出的那份局部快照"：直接读字段的话，字段在上面几行已被清成 null，
        // 于是最长、也最该说清位置的那一条永远报"无"。
        Assert.Contains("{checkpointAtStart ?? \"无\"}", probe);
        Between(probe, "var checkpointAtStart = _stallCheckpoint;", "_stallCheckpoint = null;");

        // 读数只许有一个出口，且那个出口必须把"这把刻度放了多久"一起带上：
        // 刻度表在启动之外几乎不动，只报标签会把十分钟后的冻结指回启动那条链。
        Assert.Equal(1, Count(code, "StartupProfile.LastCheckpoint;"));
        Assert.Contains("StartupProfile.LastCheckpointAgeMs",
            MethodBody(code, "private static string? CheckpointAtStallStart("));
    }
}
