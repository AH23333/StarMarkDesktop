#nullable enable
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// StarLog 后台写盘的行为测必须独占跑：DirectoryOverride 是全局静态，并行的其他集合
/// （UserDataPathsTests 一族）翻它会把入队的行引去别的目录——内容断言会读到假红（首轮 143/200 的另一半原因）。
/// </summary>
[CollectionDefinition("starlog-flush", DisableParallelization = true)]
public sealed class StarLogFlushCollection
{
}

/// <summary>
/// StarLog 后台写盘队列闸门（片243，前三格扫源码、后两格跑真行为）。
/// <para>
/// 现场（starmark-20261108.log 全天取证）：每条日志＝调用者线程一次同步 open-append-close＋全局锁，
/// 打开设置页一轮 60+ 条全落在 UI 线程上打文件 I/O——全天微卡顿的固定成分。这里守五半，缺一算没做：
/// ① 调用线程（Write）只入队，⛔ 直接文件 I/O（File.AppendAllText / Directory.CreateDirectory）；
/// ② 专职刷盘线程（StarLog-Flush）＋无界队列＋批量上限，攒批一次开-写-关；
/// ③ 进程退出保尾（ProcessExit→FlushNow）：IsBackground 线程不拦进程，滞留队列必须有人倒干净；
/// ④ FlushNow 行为：入队的行同步落盘可读；
/// ⑤ 风暴行为：200 条全数到盘（队列不丢、批量收口不挂死）。
/// </para>
/// <para>
/// 行为测读数口径：⛔ 边写边 <see cref="File.ReadAllText"/>——读方不许碰内容句柄，
/// 否则刷盘线程的 AppendAllText 撞共享锁失败、整批被自愈吞掉（首轮 143/200 的教训）；
/// 先等文件长度连三轮不动（只查元数据），确认队列已清空再读。
/// </para>
/// </summary>
[Collection("starlog-flush")]
public sealed class LogFlushQueueGateTests
{
    private static string Code() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Abstractions/StarLog.cs"));

    /// <summary>调用线程只入队：Write 里一件文件 I/O 都不许有，落盘责任整体移交队列。</summary>
    [Fact]
    public void CallerThreadOnlyEnqueues()
    {
        var write = SourceGate.MethodBody(Code(), "private static void Write(string level");
        Assert.DoesNotContain("File.AppendAllText", write);
        Assert.DoesNotContain("Directory.CreateDirectory", write);
        Assert.Contains("EnqueueOrAppend(line)", write);   // 落盘责任移交队列
    }

    /// <summary>专职刷盘线程＋无界队列＋批量上限：攒批一次开-写-关，风暴只涨内存不卡调用者。</summary>
    [Fact]
    public void DedicatedFlushThreadDrainsInBatches()
    {
        var code = Code();
        Assert.Contains("BlockingCollection<string>", code);
        Assert.Contains("StarLog-Flush", code);            // 专职刷盘线程（IsBackground，不拦进程退出）
        Assert.Contains("FlushBatchCap", code);            // 批量上限：一次开-写-关写完整批
        var loop = SourceGate.MethodBody(code, "private static void FlushLoop()");
        Assert.Contains("TryTake", loop);
        Assert.Contains("AppendLines(batch)", loop);
    }

    /// <summary>进程退出保尾：ProcessExit 挂钩＋FlushNow 倒干队列（零超时取：空闲时不许挂死）。</summary>
    [Fact]
    public void ProcessExitFlushesTheTail()
    {
        var code = Code();
        Assert.Contains("ProcessExit", code);
        var flush = SourceGate.MethodBody(code, "internal static void FlushNow()");
        Assert.Contains("TryTake(out var line, 0)", flush);   // 零超时：空闲时 FlushNow 必须立即返回
        Assert.Contains("AppendCore(rest)", flush);
    }

    /// <summary>缓存的当日档只在同一目录内有效：LogDirectory 一变（测试改道/数据根变化）必须作废重走，
    /// 否则整批写向已不存在的旧路径、被自愈吞掉——第二轮组合跑 199/200 的教训：前一颗测试删了自己的改道目录，
    /// 后一颗的首批仍按旧缓存路径写，一条不落全丢。</summary>
    [Fact]
    public void DayFileCacheInvalidatesWhenDirectoryChanges()
    {
        var append = SourceGate.MethodBody(Code(), "private static void AppendCore(");
        Assert.Contains("Path.GetDirectoryName(_dayFile) != dir", append);   // 换目录即作废
        Assert.Contains("_dirEnsured = false;", append);                     // 建目录也重走
    }

    /// <summary>行为：入队的行经 FlushNow 同步到盘、可读回。</summary>
    [Fact]
    public void FlushNowPersistsEnqueuedLines()
    {
        var dir = Path.Combine(Path.GetTempPath(), "StarLogGateTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var saved = StarMark.Abstractions.StarLog.DirectoryOverride;
        StarMark.Abstractions.StarLog.DirectoryOverride = dir;
        try
        {
            var marker = $"FlushNow 取证行 {Guid.NewGuid():N}";
            StarMark.Abstractions.StarLog.Info(marker);
            StarMark.Abstractions.StarLog.FlushNow();
            var file = StarMark.Abstractions.StarLog.CurrentLogFile;
            WaitForQuiescentFile(file);
            Assert.Contains(marker, ReadAllTextSafe(file));
        }
        finally
        {
            StarMark.Abstractions.StarLog.DirectoryOverride = saved;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>行为：200 条风暴全数到盘——队列不丢、批量收口不挂死（唯一 token 断言到条）。</summary>
    [Fact]
    public void BurstEnqueueDrainsCompletely()
    {
        var dir = Path.Combine(Path.GetTempPath(), "StarLogGateTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var saved = StarMark.Abstractions.StarLog.DirectoryOverride;
        StarMark.Abstractions.StarLog.DirectoryOverride = dir;
        try
        {
            var token = Guid.NewGuid().ToString("N");
            for (var i = 0; i < 200; i++)
                StarMark.Abstractions.StarLog.Info($"风暴行 {token} #{i}");
            var file = StarMark.Abstractions.StarLog.CurrentLogFile;
            // 等队列清空（长度连三轮不动＝刷盘线程已把 200 条写完、回到阻塞等待），
            // 再 FlushNow 收可能的理论尾批，最后才允许读内容。
            WaitForQuiescentFile(file);
            StarMark.Abstractions.StarLog.FlushNow();
            WaitForQuiescentFile(file, stableRounds: 2);
            Assert.Equal(200, SourceGate.Count(ReadAllTextSafe(file), token));
        }
        finally
        {
            StarMark.Abstractions.StarLog.DirectoryOverride = saved;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ───────────────────────── 行为测读数帮手 ─────────────────────────

    /// <summary>等文件长度连 <paramref name="stableRounds"/> 轮不动。只查元数据（FileInfo.Length），
    /// ⛔ ReadAllText——读方碰内容句柄会让刷盘线程的 AppendAllText 撞共享锁、整批被自愈吞掉。</summary>
    private static void WaitForQuiescentFile(string file, int stableRounds = 3, int roundMs = 100)
    {
        long prev = -1;
        var stable = 0;
        var deadline = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < deadline && stable < stableRounds)
        {
            var len = File.Exists(file) ? new FileInfo(file).Length : 0;
            stable = len == prev ? stable + 1 : 0;
            prev = len;
            Thread.Sleep(roundMs);
        }
    }

    private static string ReadAllTextSafe(string file)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return File.Exists(file) ? File.ReadAllText(file) : ""; }
            catch (IOException) when (attempt < 50) { Thread.Sleep(20); }
        }
    }
}
