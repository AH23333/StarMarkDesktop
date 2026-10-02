#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using StarMark.Abstractions;
using StarMark.Abstractions.Updates;

namespace StarMark.Core.Updates;

/// <summary>
/// 把更新器<b>请到安装目录之外</b>再跑起来（批次 UG-2 的启动侧）。
/// <para>顺序是有意义的：先认这条指令 → 再找到那一带头 → 再整带复制到安装目录之外 → 最后才起进程。
/// <b>绝不在安装目录里直接跑那颗</b>：它一进去就要把自已脚下的地挪走，那种坏不是"失败得难看"，
/// 而是"做成一半"（<see cref="UpdaterEngine"/> 里另有一条同样的自检，两侧各守一道）。</para>
/// <para>为什么是"复制一整带"而不是复制一颗 exe：一颗 exe 跑不起来——它要自己的 <c>.dll</c>、
/// <c>runtimeconfig.json</c> 和它引用的那几个程序集。列清单就会漏（漏的那一次只在用户机器上出现，
/// 而表现是"更新永远不动"），整带复制不需要知道有几颗（#189/#193 那一族：能不算的账就别两处算）。</para>
/// </summary>
public static class UpdaterLauncher
{
    /// <summary>唯一一条与外界打交道的边（起那个进程）；测试从这条边注入，其余走真文件系统。</summary>
    public sealed class Options
    {
        public Action<string, IReadOnlyList<string>> StartRunner { get; init; } = StartProcess;

        /// <summary>那一带头的来源目录。默认是<b>安装目录里的 <c>Updater\</c></b>（随版本一起发布）；
        /// 测试给一个现造的小目录。</summary>
        public string? RunnerSourceOverride { get; init; }

        /// <summary>副本落在哪儿的根。默认是 <see cref="UpdaterPaths.UpdaterHome"/>（本机那块可变数据），
        /// 测试给一个临时目录——<b>不许为了测去动真那一块</b>：清理旧副本是按"这个根下面的子目录全删"做的。</summary>
        public string? RunnerHomeOverride { get; init; }

        public int Attempts { get; init; } = UpdaterContract.MoveAttempts;
        public TimeSpan RetryDelay { get; init; } = UpdaterContract.MoveRetryDelay;
    }

    public static LaunchResult PrepareAndStart(UpdaterRequest request, Options? options = null)
    {
        var o = options ?? new Options();
        if (!UpdaterContract.Validate(request, out var why)) return new(LaunchStatus.InvalidRequest, why);

        var source = o.RunnerSourceOverride ?? UpdaterPaths.InInstallDir(request.InstallDir);
        if (!File.Exists(Path.Combine(source, UpdaterPaths.UpdaterExeName)))
            return new(LaunchStatus.NotStarted, $"这台机器上的这一版没带更新器（{source}）");

        var homeRoot = o.RunnerHomeOverride ?? UpdaterPaths.UpdaterHome;
        var home = Path.Combine(homeRoot,
            request.ParentPid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Inside(home, request.InstallDir))
            return new(LaunchStatus.NotStarted, "更新器要跑的那个位置还在安装目录里面，不能动手");

        try
        {
            Directory.CreateDirectory(homeRoot);
            SweepStaleHomes(homeRoot, home);
            if (!TryCopyTree(source, home, o))
                return new(LaunchStatus.NotStarted, $"更新器没能复制到安装目录之外（{home}）");
            var exe = Path.Combine(home, UpdaterPaths.UpdaterExeName);
            o.StartRunner(exe, UpdaterContract.Encode(request));
            StarLog.Info($"[更新] 替换已交给更新器：{exe}（等进程 {request.ParentPid} 退出）");
            return new(LaunchStatus.Started, null, exe);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(LaunchStatus.NotStarted, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 清掉以前几轮留下的副本目录。<b>正在跑的那一颗删不掉，删不掉就留着</b>——
    /// 它属于另一条还没走完的替换流程，硬等它反而把"两个人同时点更新"做成互相卡住。
    /// </summary>
    private static void SweepStaleHomes(string root, string keep)
    {
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception) { /* 还在用：留着，下一轮再试 */ }
        }
    }

    private static bool TryCopyTree(string source, string target, Options o)
    {
        for (var attempt = 1; attempt <= Math.Max(1, o.Attempts); attempt++)
        {
            try
            {
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                CopyRecursive(source, target);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 上一轮那个更新器还在跑会撞在这一格里：先自己试几次，仍不成才说"没换成"
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

    private static void CopyRecursive(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyRecursive(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static bool Inside(string candidate, string parent)
        => Path.TrimEndingDirectorySeparator(candidate).StartsWith(
            Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 起那个进程。<b>参数走 <c>ArgumentList</c> 而不是拼一条长串</b>：安装路径里带空格是常态
    /// （<c>C:\Program Files\...</c>、还有用户名里的中文空格），拼串那层的引号规则一旦与解析侧不一致，
    /// 坏法子是"路径被切成两半"这种查不出来源的形状。
    /// </summary>
    private static void StartProcess(string runnerPath, IReadOnlyList<string> argv)
    {
        var start = new ProcessStartInfo
        {
            FileName = runnerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(runnerPath),
        };
        foreach (var arg in argv) start.ArgumentList.Add(arg);
        Process.Start(start);
    }
}
