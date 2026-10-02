#nullable enable
using System;
using System.IO;
using StarMark.Abstractions;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;

namespace StarMark.Updater;

/// <summary>
/// 更新器这个进程的<b>壳</b>：解参数 → 交给 <see cref="UpdaterEngine"/> → 把结局写进日志 → 按结局给退出码。
/// <para>这里刻意不长出任何判断：判断在 Core 里才有单元测试可跑（测试工程不引用可执行工程）。
/// 一条 <c>TheUpdaterStaysAThinShell</c> 的闸门钉着这件事——壳里一旦出现"挪目录、等进程、决定回滚"，
/// 那些判据就从今天起没有人证了（#226/#231 那一族：没人测过的判据会安静地烂掉）。</para>
/// </summary>
public static class Program
{
    public static int Main(string[] argv)
    {
        var status = UpdaterOutcome.Unknown;
        string? detail = null;
        try
        {
            if (UpdaterContract.TryDecode(argv, out var request, out var why) && request is not null)
            {
                var result = UpdaterEngine.Run(request);
                status = result.Status;
                detail = result.Detail;
            }
            else
            {
                status = UpdaterOutcome.InvalidRequest;
                detail = why ?? "参数解不出";
            }
        }
        catch (Exception ex)
        {
            // 引擎没机会说话就摔了：不猜它走到了哪一步，只补一条看得见的事实——旧树在不在
            detail = $"{ex.GetType().Name}: {ex.Message}；旧树那一头{(OldTreePresent(argv) ? "还在" : "没留下东西")}";
        }

        var line = $"[更新] 更新器结局：{status} · {UpdatePolicy.Describe(status)}"
            + (detail is null ? "" : $" · {detail}");
        if (status == UpdaterOutcome.Success) StarLog.Info(line);
        else StarLog.Warn(line);
        return UpdaterContract.ExitCodeFor(status);
    }

    /// <summary>从参数里认出安装目录，再看它旁边有没有 <c>_old</c>。<b>这一句只做一件事：给人一条能查的线索</b>，
    /// 它不参与任何决定（决定了就该在引擎里，那里才有测）。</summary>
    private static bool OldTreePresent(string[] argv)
    {
        try
        {
            var at = Array.IndexOf(argv, UpdaterContract.TargetFlag);
            if (at < 0 || at + 1 >= argv.Length) return false;
            var install = Path.TrimEndingDirectorySeparator(argv[at + 1]);
            return Directory.Exists(install + UpdateAssets.OldTreeSuffix);
        }
        catch (Exception)
        {
            return false;       // 连这条线索都取不到也不该把日志这一步弄崩——尺子不许弄坏被量的东西
        }
    }
}
