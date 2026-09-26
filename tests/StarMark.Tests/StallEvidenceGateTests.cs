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
}
