#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using StarMark.Abstractions;
using StarMark.Abstractions.Updates;

namespace StarMark.Core.Updates;

/// <summary>
/// 把一棵<b>逐颗对过账</b>的新树换进安装目录，摔了就当场退回原样（批次 UG-2）。
/// <para>
/// 这一层的形状与 <c>UpdateStaging</c> 相反：那一层只写一棵新树、什么都不敢；这一层要动的就是
/// 正在运行的程序脚下那块地，所以它<b>只能被一个不在那块地里的进程调用</b>
/// （<c>StarMark.Updater</c> 跑的是 <c>%LOCALAPPDATA%\StarMark\Updater\</c> 里那份副本）。
/// </para>
/// <para>四件判据：
/// ① <b>只改名，不复制</b>——同盘改名是"要么整个要么没有"，复制有一段"半棵树正被当成安装目录"的窗口，
///    而那一段没有任何办法守住。<see cref="UpdaterContract.Validate"/> 因此先拒跨盘。
/// ② <b>动过之后不许留半截</b>——把旧树挪开之后，后面每一步的失败都必须落进"回滚"或"如实说清旧树在哪儿"，
///    所以回滚写在一条公共出口上（不是各 catch 一份，#230/#232 那条纪律在这一层同样成立）。
/// ③ <b>不喊人帮忙</b>——文件此刻被占用就自己重试（<see cref="UpdaterContract.MoveAttempts"/> 次），
///    等不到那个进程退出就什么都不动；不会出现"请先关闭程序再试"那一类话（P-54）。</para>
/// <para>④ <b>交出去了就得有人接着</b>——父进程一退，唯一还知道安装目录在哪儿的就是这一个进程。
///    所以"还没碰盘就不得不停"那一族出口要把原来那一版重新起来（<see cref="NothingSwapped"/>）：
///    屏幕上什么都不剩不叫"本机没改动"，那叫把人的程序弄丢了。</para>
/// </summary>
public static class UpdaterEngine
{
    /// <summary>两条与外界打交道的边（等进程、起程序）＋重试的节奏。测试从这两条边注入，其余走真文件系统。</summary>
    public sealed class Options
    {
        public Func<int, TimeSpan, bool> WaitForExit { get; init; } = WaitPidGone;
        public Action<string> StartExe { get; init; } = StartDetached;

        /// <summary>此刻这个进程自己在哪儿。<b>站在要被换掉的那棵树里就别动手</b>——
        /// 那不是"运气好会失败"，那是把一件本来能成的事做成一半。</summary>
        public string? RunningExePath { get; init; } = Environment.ProcessPath;

        public TimeSpan Ceiling { get; init; } = UpdaterContract.ParentExitCeiling;
        public int Attempts { get; init; } = UpdaterContract.MoveAttempts;
        public TimeSpan RetryDelay { get; init; } = UpdaterContract.MoveRetryDelay;
    }

    public static UpdaterResult Run(UpdaterRequest request, Options? options = null)
    {
        var o = options ?? new Options();
        if (!UpdaterContract.Validate(request, out var why)) return Invalid(why);

        var staged = Path.TrimEndingDirectorySeparator(request.StagedTree);
        var install = Path.TrimEndingDirectorySeparator(request.InstallDir);
        if (!Directory.Exists(staged)) return Invalid($"摊好的那棵树不在盘上了：{staged}");
        if (!File.Exists(Path.Combine(staged, UpdateAssets.EntryExeName)))
            return Invalid($"摊好的那棵树里没有 {UpdateAssets.EntryExeName}——换过去会得到一个启动不了的目录");
        if (!Directory.Exists(install)) return Invalid($"安装目录不在盘上：{install}");
        if (o.RunningExePath is { Length: > 0 } me && Inside(me, install))
            return Invalid("更新器正站在要被换掉的那棵树里跑——先把自己复制到别处再来");

        if (!o.WaitForExit(request.ParentPid, o.Ceiling))
            return new(UpdaterOutcome.ParentStillRunning,
                $"等了 {o.Ceiling.TotalSeconds:0} 秒，进程 {request.ParentPid} 还在；什么都没动");

        var oldPath = ChooseOldPath(install);
        if (oldPath is null)
            return NothingSwapped(UpdaterOutcome.InvalidRequest, "旧树的位置排满了（上一次替换留下的 _old 没清掉）", install, o);

        var (movedOld, oldError) = TryMove(install, oldPath, o);
        if (!movedOld)
            return NothingSwapped(UpdaterOutcome.OldTreeNotMoved, $"旧目录挪不动：{oldError}", install, o);

        // —— 从这里起，盘上有一棵不在安装位置的旧树：任何一条出口都要么换好，要么退回 ——
        var (movedNew, newError) = TryMove(staged, install, o);
        if (!movedNew) return BackToOld(request, install, oldPath, o, $"新树没能换进安装目录：{newError}");

        var entryExe = Path.Combine(install, UpdateAssets.EntryExeName);
        if (!TryStart(entryExe, o, out var startError))
        {
            // 新树已经在地里了：得先把它整棵拔到旁边，旧树才有位置回原位。
            // 少这一步就会把"换好了但起不来"报成"回滚失败、里面有我们没见过的东西"——那是句谎，
            // 而谎的方向是把我们的成果说成别人的地盘。
            var aside = staged + ".failed";
            var (pulled, pullError) = TryMove(install, aside, o);
            if (!pulled)
                return new(UpdaterOutcome.RollbackFailed,
                    $"换好了但新版本起不来：{startError}；而新的那一棵没能从安装目录里拔出来：{pullError}"
                    + $"——旧树还在 {oldPath}，这两处就是现在盘上的样子");
            return BackToOld(request, install, oldPath, o,
                $"换好了，但新版本起不来：{startError}（新的那一棵留在 {aside}，没有删）");
        }

        StarLog.Info($"[更新] 已换到新版本，旧树挪去 {oldPath}");
        var leftover = SweepOldTrees(install, o);
        return new(UpdaterOutcome.Success,
            leftover is null ? "旧树已清掉" : $"新版本已经起来了；旧树没清干净（{leftover}），不影响这次更新");
    }

    /// <summary>
    /// 等到了那个进程退出、却在还没碰安装目录之前就不得不停：<b>把原来那一版重新起来再走</b>。
    /// <para>这一条不是修饰话。交棒那一刻界面对用户说的是"程序马上会重开"
    /// （<see cref="LaunchStatus.Started"/>），而父进程此刻已经退出、屏上一个窗口都没有；
    /// 留它在地上，那句话就成了谎，而用户手上只剩一行日志（P-54：不许把人支到"你自己再开一次"上）。</para>
    /// <para>盘上一个字节都没改，所以起来的就是他原来那一版。<b>起不来时如实换一格</b>
    /// （<see cref="UpdaterOutcome.UntouchedButNotRunning"/>）——"没改动"与"程序还开着"是两件事，
    /// 折成一格就会拿后者担保前者（#234 那一族）。</para>
    /// </summary>
    private static UpdaterResult NothingSwapped(UpdaterOutcome cause, string why, string install, Options o)
    {
        var entryExe = Path.Combine(install, UpdateAssets.EntryExeName);
        if (TryStart(entryExe, o, out var startError))
            return new(cause, $"{why}；什么都没动，原来那一版已经重新起来");
        return new(UpdaterOutcome.UntouchedButNotRunning,
            $"{why}；什么都没动，而原来那一版也没能起来：{startError}");
    }

    /// <summary>
    /// 退回原样：把旧树挪回安装目录，再把它起来。<b>这一条是"动过之后"所有失败的公共出口</b>
    /// （摊在两个 catch 上就会漏掉没想到的那一种，#230）。
    /// </summary>
    private static UpdaterResult BackToOld(
        UpdaterRequest request, string install, string oldPath, Options o, string cause)
    {
        // 挪失败那一步可能留下一个空的安装目录占着位置；只清"确实是空的"那一格，
        // 里面有东西就说明我们不知道那是谁的东西——那要如实报成回滚失败，不许猜。
        if (Directory.Exists(install))
        {
            if (IsEmpty(install)) TryDelete(install, o);
            else return new(UpdaterOutcome.RollbackFailed,
                $"{cause}；而安装目录里此刻有我们没见过的东西，旧树没敢挪回去——旧树在 {oldPath}");
        }
        var (movedBack, backError) = TryMove(oldPath, install, o);
        if (!movedBack)
            return new(UpdaterOutcome.RollbackFailed,
                $"{cause}；旧树也没能挪回原位：{backError}——旧树在 {oldPath}");

        var entryExe = Path.Combine(install, UpdateAssets.EntryExeName);
        if (!TryStart(entryExe, o, out var startError))
            return new(UpdaterOutcome.RolledBackButNotRunning,
                $"{cause}；已退回原来那一版，但它也没能起来：{startError}");

        return new(UpdaterOutcome.RolledBack, $"{cause}；已退回原来那一版并重新起来了");
    }

    /// <summary>
    /// 改名，带着"这颗此刻不许动"的重试。<b>只认 IOException 与 UnauthorizedAccessException 两种</b>：
    /// 后者在 Windows 上也常常是"文件被占用"（共享模式给得比拒绝还严），而这两种都可能是过一会儿就好的，
    /// 所以先自己试几次；试到最后仍不成，说的是<b>为什么不动</b>，不是"请稍后再试"。
    /// </summary>
    private static (bool Ok, string? Error) TryMove(string from, string to, Options o)
    {
        string? error = null;
        for (var attempt = 1; attempt <= Math.Max(1, o.Attempts); attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return (true, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                if (attempt < o.Attempts) Thread.Sleep(o.RetryDelay);
            }
            catch (Exception ex)
            {
                return (false, $"{ex.GetType().Name}: {ex.Message}");     // 路径本身不对：再试一百次也不会对
            }
        }
        return (false, error is null ? "没挪成但没给出原因" : $"{o.Attempts} 次之后仍然挪不动：{error}");
    }

    private static bool TryStart(string exe, Options o, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(exe)) { error = $"{Path.GetFileName(exe)} 不在那里"; return false; }
            o.StartExe(exe);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>换成了之后清旧账：把这次与以往留下的 <c>_old</c> 树都删掉（尽力，删不掉只多占 disk）。</summary>
    private static string? SweepOldTrees(string install, Options o)
    {
        string? error = null;
        var parent = Path.GetDirectoryName(install) ?? install;
        var stem = Path.GetFileName(install);
        foreach (var dir in Directory.EnumerateDirectories(parent, stem + UpdateAssets.OldTreeSuffix + "*",
                     SearchOption.TopDirectoryOnly))
        {
            if (!TryDelete(dir, o)) error = Path.GetFileName(dir);
        }
        return error;
    }

    private static bool TryDelete(string dir, Options o)
    {
        for (var attempt = 1; attempt <= Math.Max(1, o.Attempts); attempt++)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 杀软/索引器刚碰过这颗文件也会给这两种，过一会儿就好——所以两种都值得自己再试几次
                if (attempt >= o.Attempts) return false;
                Thread.Sleep(o.RetryDelay);
            }
            catch (Exception)
            {
                return false;
            }
        }
        return false;
    }

    private static bool IsEmpty(string dir)
    {
        try { return !Directory.EnumerateFileSystemEntries(dir).Any(); }
        catch (Exception) { return false; }        // 读不动就当它不空：宁可报回滚失败，也不要移走别人的东西
    }

    private static string? ChooseOldPath(string install)
    {
        var first = install + UpdateAssets.OldTreeSuffix;
        if (!Directory.Exists(first)) return first;
        for (var n = 1; n <= 9; n++)
        {
            var candidate = $"{first}.{n}";
            if (!Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool Inside(string candidate, string parent)
        => Path.TrimEndingDirectorySeparator(candidate).StartsWith(
            Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static UpdaterResult Invalid(string? why)
        => new(UpdaterOutcome.InvalidRequest, $"{why ?? "这条指令不成立"}；什么都没动");

    // ===== 真做的那两条边（测试从 Options 换掉它们）=====

    /// <summary>等那个进程真的没了。<b>进程号已经不在了算"没了"</b>——那是最常见的顺利路径，不是错误。</summary>
    private static bool WaitPidGone(int pid, TimeSpan ceiling)
    {
        var deadline = DateTime.UtcNow + ceiling;
        while (DateTime.UtcNow < deadline)
        {
            Process? process;
            try { process = Process.GetProcessById(pid); }
            catch (Exception) { return true; }
            using (process)
            {
                try
                {
                    if (process.WaitForExit((int)UpdaterContract.ParentExitPoll.TotalMilliseconds)) return true;
                }
                catch (Exception) { return true; }      // 句柄都读不到：再等下去也只是把"什么都没动"拖成"很久什么都没动"
            }
        }
        return false;
    }

    private static void StartDetached(string exe)
        => Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe),
        });
}
