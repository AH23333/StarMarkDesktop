#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 启动侧的闸门（批次 UG-2）：真临时目录、真复制，只有"起进程"那一条边是假的。
/// <para>这一层的存在理由只有一句：<b>更新器不许站在它自己要挪走的那棵树里</b>。
/// 所以它的判据全都是"落哪儿、带了几颗、什么时候不许起"，而不是"换得对不对"（那是引擎的事）。</para>
/// </summary>
public sealed class UpdaterLauncherTests : IDisposable
{
    private const string Exe = UpdaterPaths.UpdaterExeName;
    private const string Pid = "12345";

    private readonly string _root;
    private readonly string _install;
    private readonly string _staged;
    private readonly string _home;
    private readonly List<(string Exe, IReadOnlyList<string> Argv)> _started = new();

    public UpdaterLauncherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "StarMarkUpdaterLauncherTests", Guid.NewGuid().ToString("N"));
        _install = Path.Combine(_root, "App", "StarMark");
        _staged = Path.Combine(_root, ".StarMarkUpdate", "1.0.1" + UpdateAssets.NewTreeSuffix);
        _home = Path.Combine(_root, "localdata", "Updater");
        Directory.CreateDirectory(_install);
        Directory.CreateDirectory(_staged);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* 测试尾巴，不因为它把结论改掉 */ }
    }

    /// <summary>在安装目录里造一个"随版本发布的更新器"那一带头（可执行文件 + 它要的两颗依赖）。</summary>
    private void ShipUpdaterInsideInstallDir()
    {
        var folder = UpdaterPaths.InInstallDir(_install);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Exe), "exe");
        File.WriteAllText(Path.Combine(folder, "StarMark.Updater.dll"), "dll");
        File.WriteAllText(Path.Combine(folder, "StarMark.Updater.runtimeconfig.json"), "{}");
    }

    private UpdaterRequest Req(string staged = "", string install = "")
        => new(int.Parse(Pid), string.IsNullOrEmpty(staged) ? _staged : staged, string.IsNullOrEmpty(install) ? _install : install);

    private UpdaterLauncher.Options Opts() => new()
    {
        StartRunner = (exe, argv) => _started.Add((exe, argv)),
        RunnerHomeOverride = _home,
        Attempts = 2,
        RetryDelay = TimeSpan.FromMilliseconds(5),
    };

    // ===== 那条存在理由 =====

    [Fact]
    public void TheRunnerIsCopiedOutOfTheInstallDirectoryBeforeItIsStarted()
    {
        ShipUpdaterInsideInstallDir();

        var result = UpdaterLauncher.PrepareAndStart(Req(), Opts());

        Assert.Equal(LaunchStatus.Started, result.Status);
        var started = Assert.Single(_started);
        Assert.StartsWith(_home, started.Exe, StringComparison.Ordinal);
        Assert.False(started.Exe.StartsWith(_install, StringComparison.OrdinalIgnoreCase));   // 不在自己脚下跑
        Assert.Equal(Path.Combine(_home, Pid, Exe), started.Exe);
    }

    [Fact]
    public void TheWholeUpdaterFolderRidesAlongRatherThanJustTheExe()
    {
        ShipUpdaterInsideInstallDir();

        UpdaterLauncher.PrepareAndStart(Req(), Opts());

        // 只带一颗 exe 的写法在这儿就红了：它会"起不来"，而那次失败只在用户机器上出现
        var copied = Directory.GetFiles(Path.Combine(_home, Pid)).Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "StarMark.Updater.dll", Exe, "StarMark.Updater.runtimeconfig.json" }, copied);
    }

    [Fact]
    public void TheCopyThatIsAlreadyRunningIsNeverOverwrittenByTheNextRound()
    {
        ShipUpdaterInsideInstallDir();
        var first = UpdaterLauncher.PrepareAndStart(new UpdaterRequest(111111, _staged, _install), Opts());
        var second = UpdaterLauncher.PrepareAndStart(new UpdaterRequest(222222, _staged, _install), Opts());

        Assert.Equal(LaunchStatus.Started, first.Status);
        Assert.Equal(LaunchStatus.Started, second.Status);
        Assert.NotEqual(first.RunnerPath, second.RunnerPath);   // 一人一间，而不是"覆盖那颗正在跑的"
    }

    [Fact]
    public void AnEarlierRoundsLeftoverCopyIsSwept()
    {
        ShipUpdaterInsideInstallDir();
        var stale = Path.Combine(_home, "999999");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, Exe), "旧的");

        UpdaterLauncher.PrepareAndStart(Req(), Opts());

        Assert.False(Directory.Exists(stale));
    }

    [Fact]
    public void AStaleCopyThatIsStillBusyDoesNotBlockThisRound()
    {
        ShipUpdaterInsideInstallDir();
        var stale = Path.Combine(_home, "999999");
        Directory.CreateDirectory(stale);
        using (new FileStream(Path.Combine(stale, Exe), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var result = UpdaterLauncher.PrepareAndStart(Req(), Opts());

            Assert.Equal(LaunchStatus.Started, result.Status);   // 删不掉就留着：那是另一条还在走的替换流程
        }
    }

    // ===== 什么时候不许起 =====

    [Fact]
    public void AVersionThatShipsNoUpdaterSaysSoAndStartsNothing()
    {
        var result = UpdaterLauncher.PrepareAndStart(Req(), Opts());   // 安装目录里没有 Updater\ 那一带头

        Assert.Equal(LaunchStatus.NotStarted, result.Status);
        Assert.Empty(_started);
        Assert.Contains(UpdaterPaths.InInstallDir(_install), result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidRequestNeverReachesTheRunner()
    {
        ShipUpdaterInsideInstallDir();
        var bogus = new UpdaterRequest(0, _staged, _install);      // 没有要等的那个进程：这条指令不成立

        var result = UpdaterLauncher.PrepareAndStart(bogus, Opts());

        Assert.Equal(LaunchStatus.InvalidRequest, result.Status);
        Assert.Empty(_started);
    }

    [Fact]
    public void AHomeThatWouldLandInsideTheInstallDirectoryIsRefused()
    {
        ShipUpdaterInsideInstallDir();
        var o = Opts();
        var result = UpdaterLauncher.PrepareAndStart(Req(), new UpdaterLauncher.Options
        {
            StartRunner = o.StartRunner, RunnerSourceOverride = o.RunnerSourceOverride,
            Attempts = o.Attempts, RetryDelay = o.RetryDelay,
            RunnerHomeOverride = Path.Combine(_install, "UpdaterRun"),     // 例如 LOCALAPPDATA 被人挪进安装目录
        });

        Assert.Equal(LaunchStatus.NotStarted, result.Status);
        Assert.Empty(_started);
        Assert.Contains("安装目录里面", result.Detail, StringComparison.Ordinal);
    }

    // ===== 跨进程边界那一趟：参数不许漂 =====

    [Fact]
    public void TheRunnerIsHandedExactlyTheEncodedRequest()
    {
        ShipUpdaterInsideInstallDir();
        var request = Req();

        UpdaterLauncher.PrepareAndStart(request, Opts());

        var started = Assert.Single(_started);
        Assert.Equal(UpdaterContract.Encode(request), started.Argv);
        Assert.True(UpdaterContract.TryDecode(started.Argv.ToArray(), out var decoded, out _));   // 解得回来才算过了这道边界
        Assert.Equal(request, decoded);
    }

    [Fact]
    public void TheStagingRootSharesItsVolumeWithTheInstallDirectoryItFeeds()
    {
        var staging = UpdaterPaths.StagingRootFor(_install);

        Assert.Equal(Path.GetPathRoot(_install), Path.GetPathRoot(staging), ignoreCase: true);
        Assert.Equal(Path.GetDirectoryName(_install), Path.GetDirectoryName(staging), ignoreCase: true);
    }
}
