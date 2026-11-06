#nullable enable
using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 备份/恢复「可取消」的接线闸门（片212·P-36）。测试工程不引用 StarMark.UI，
/// 钉的是源码形状：大库的导出/恢复是几秒到几十秒的长跑，<b>跑起来必须真的能喊停</b>——
/// 而喊停之后的账要算清：导出清半截（只在写确实开始过之后）、恢复按「走到哪一步」报中间态
/// （未动数据／停在半路＋快照路径），⛔ 套「失败」口吻把中间态说没，也⛔ 假装原子。
/// </summary>
public sealed class BackupCancelGateTests
{
    private const string Repo = "src/StarMark.Data/BackupRepository.cs";
    private const string Migrations = "src/StarMark.Data/MigrationRunner.cs";
    private const string Service = "src/StarMark.Core/Backup/BackupService.cs";
    private const string Envelope = "src/StarMark.Core/Backup/BackupEnvelope.cs";
    private const string Page = "src/StarMark.UI/Views/SettingsPage.Backup.cs";
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    /// <summary>
    /// 仓储那七处长循环（导出读×4＋导入 foreach×3）＋ ImportItems 的 200 条批循环，
    /// 每处都要听得见取消；Reindex 收尾把令牌递给迁移的同名重载（取消前已落库的行仍要进索引）。
    /// </summary>
    [Fact]
    public void TheRepositoryHearsTheTokenInsideEveryLongLoop()
    {
        var repo = SourceGate.ReadRepoFile(Repo);
        Assert.True(Count(repo, "ct.ThrowIfCancellationRequested()") >= 8,
            $"BackupRepository 长循环里的取消检查少于 8 处（现在 {Count(repo, "ct.ThrowIfCancellationRequested()")}）");
        Assert.Contains("RebuildSearchTextAndIndex(conn, ct)", repo, StringComparison.Ordinal);

        var migrations = SourceGate.ReadRepoFile(Migrations);
        // (conn, ct) 重载是真的重载（迁移三处走旧签名转 None），⛔ 只有一颗签名改参数。
        Assert.Contains("RebuildSearchTextAndIndex(conn, CancellationToken.None)", migrations, StringComparison.Ordinal);
        Assert.Contains("RebuildSearchTextAndIndex(Microsoft.Data.Sqlite.SqliteConnection conn, CancellationToken ct)",
            migrations, StringComparison.Ordinal);
        Assert.True(Count(migrations, "ct.ThrowIfCancellationRequested()") >= 2,
            "Rebuild 两段循环（读行／回写）都要能听见取消");
    }

    /// <summary>
    /// Core 侧的账：直接写 target 的两条路有「清半截」，且都在写之前先查一次取消
    /// （写前取消＝旧档完好，⛔ 拿它的名字冒风险）；zip 臂刻意不清——WriteAsync 是
    /// temp+Move 的原子形，取消时 target 完好无损，删它＝删掉完好的旧档。
    /// </summary>
    [Fact]
    public void TheServiceCleansPartialsOnlyWhereTheWriteActuallyStarted()
    {
        var svc = SourceGate.ReadRepoFile(Service);
        Assert.Equal(2, Count(svc, "TryDeletePartial(target);"));
        Assert.Equal(2, Count(svc, "ct.ThrowIfCancellationRequested();"));   // 两条直写路各一道「先查再写」闸
        Assert.Contains("private static void TryDeletePartial(string path)", svc, StringComparison.Ordinal);
        // 恢复的两段取消臂按「走到哪一步」分档交键。
        Assert.Contains("settings.data.backup.cancelledBefore", svc, StringComparison.Ordinal);
        Assert.Contains("settings.data.backup.cancelledMid", svc, StringComparison.Ordinal);
        Assert.Contains("DataChangeHub.Notify(DataChangeKind.Items)", svc, StringComparison.Ordinal);   // 停在半路也要刷新（✅P-44：条目族）：屏幕⛔ 停在取消前的旧数据上

        var envelope = SourceGate.ReadRepoFile(Envelope);
        Assert.Contains("public bool Cancelled { get; init; }", envelope, StringComparison.Ordinal);
    }

    /// <summary>
    /// UI 侧的接线：四处长跑一律传真令牌（⛔ 留 CancellationToken.None 装样子），
    /// 范围由 Begin/End 配对圈定，取消按钮的可见性跟着范围走，取消回执各按各的那句话说。
    /// </summary>
    [Fact]
    public void TheUiWiresARealTokenAndACancellableScope()
    {
        var page = SourceGate.ReadRepoFile(Page);
        Assert.DoesNotContain("ExportWithClipImagesAsync(target, CancellationToken.None)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportToFileAsync(target, CancellationToken.None)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("BackupService.ReadAsync(source, CancellationToken.None)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("null, CancellationToken.None, source", page, StringComparison.Ordinal);
        Assert.Contains("BeginBackupCancelScope()", page, StringComparison.Ordinal);
        Assert.Contains("EndBackupCancelScope()", page, StringComparison.Ordinal);
        Assert.Contains("settings.data.backup.cancelledExport", page, StringComparison.Ordinal);
        Assert.Contains("settings.data.backup.cancelledBefore", page, StringComparison.Ordinal);
        Assert.Contains("rr.Cancelled", page, StringComparison.Ordinal);   // 取消单独走 Core 交回的那句，⛔ 套「导入失败」

        var vm = SourceGate.ReadRepoFile(Vm);
        Assert.Contains("public CancellationToken BeginBackupCancelScope()", vm, StringComparison.Ordinal);
        Assert.Contains("public void EndBackupCancelScope()", vm, StringComparison.Ordinal);
        Assert.Contains("_backupCts?.Cancel()", vm, StringComparison.Ordinal);
        Assert.Contains("[RelayCommand]", vm, StringComparison.Ordinal);

        var xaml = SourceGate.ReadRepoFile(Xaml);
        Assert.Contains("ViewModel.CancelBackupCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsBackupCancellable", xaml, StringComparison.Ordinal);
    }
}
