#nullable enable
using System;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// ClipIMG-3a（「清理孤儿」入口）的守门。<b>测试工程不引用 StarMark.UI</b>，所以这一层只能钉源码形状——
/// 而清理这件事的三条"必坏线"恰好全在形状上：
/// <para>① 顺序：<b>没列出名单之前一件都不许删</b>（决议 §3-Q6"不静默删用户目录"在代码里就长这个形状）；</para>
/// <para>② 事实来源：删除那一步必须<b>自己重扫</b>，界面上那份旧名单不能当删除依据；</para>
/// <para>③ 坏消息：扫不出差集时既不能把按钮点亮、也不能写成"没有孤儿"。</para>
/// </summary>
public sealed class ClipboardOrphanCleanupGateTests
{
    private const string Page = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string Watcher = "src/StarMark.Integrations/Clipboard/ClipboardWatcher.cs";
    private const string Assets = "src/StarMark.Abstractions/Clipboard/ClipAssets.cs";
    private const string CommandAnchor = "private async Task CleanClipboardAssetsAsync()";

    [Fact]
    public void TheEntryExistsAndIsGreyedWithAReason()
    {
        var xaml = ReadRepoFile(Page);
        Assert.Contains("清理孤儿", xaml);
        Assert.Equal(1, Count(xaml, "ViewModel.CleanClipboardAssetsCommand"));
        Assert.Equal(1, Count(xaml, "ViewModel.CanCleanClipboardAssets, Mode=OneWay"));
        Assert.Equal(1, Count(xaml, "ViewModel.ClipboardCleanupStatus, Mode=OneWay"));
        // 灰着的时候必须看得见为什么：那句"另有 N 个文件不在历史里"就在同一张卡片里，由占用那行念出来。
        Assert.Contains("ClipboardUsage", xaml);
        // 但剪贴板页不许再长一颗（同一个动作两个入口＝两处确认框文案会分岔）。
        Assert.DoesNotContain("清理孤儿", ReadRepoFile("src/StarMark.UI/Views/ClipboardPage.xaml"));
    }

    [Fact]
    public void NothingIsDeletedBeforeTheUserSeesTheList()
    {
        var body = MethodBody(ReadRepoPartials(Vm), CommandAnchor);
        var scan = body.IndexOf("ScanAsync", StringComparison.Ordinal);
        var dialog = body.IndexOf("ShowContentAsync", StringComparison.Ordinal);
        var clean = body.IndexOf("CleanAsync", StringComparison.Ordinal);
        Assert.True(scan >= 0 && dialog >= 0 && clean >= 0, "顺序应为：扫一次 → 列名单 → 才删");
        Assert.True(scan < dialog, "没扫出名单就弹框＝框里那句“这些文件…”是编的");
        Assert.True(dialog < clean, "确认框之前一件都不许删（§3-Q6：不静默删用户目录）");
        // 取消那一支必须在 CleanAsync 之前就返回，否则"先不删"变成"照删"。
        var cancel = body.IndexOf("HostedDialogResult.Committed", StringComparison.Ordinal);
        Assert.True(cancel >= 0 && cancel < clean, "判确认结果的那一句必须早于 CleanAsync");
        Assert.Contains("先不删", body);
        // 空名单不许弹一个空框。
        Assert.Contains("现在没有需要清理的文件", body);
        Assert.True(body.IndexOf("现在没有需要清理的文件", StringComparison.Ordinal) < dialog);
    }

    [Fact]
    public void TheDeleteStepRescansInsteadOfTrustingTheDisplayedList()
    {
        var watcher = ReadRepoFile(Watcher);
        // 签名里只有仓储与取消令牌：名单进不来，"按界面上那份删"就写不出来。
        var sig = "public static async Task<CleanupOutcome?> CleanAsync(IItemRepository repo, CancellationToken ct = default)";
        Assert.Contains(sig, watcher);
        Assert.DoesNotContain("IReadOnlyList<string>", sig);          // 名单不许成为入参
        var body = MethodBody(watcher, sig);
        Assert.Contains("ScanAsync(repo", body);                      // 自己重扫一次
        Assert.Contains("DeleteAll(", body);
        Assert.DoesNotContain("File.Delete(", body);                   // 不许绕过名册自己删

        // 真正动手那一处：每个名字都过"必须在 clip 目录里"那道名册，所以这条路构造上删不到目录之外。
        var del = MethodBody(watcher,
            "private static (int Deleted, long Bytes, int Failed) DeleteAll(IReadOnlyList<string> names, CancellationToken ct)");
        Assert.Contains("foreach (var name in names)", del);
        Assert.Contains("ClipboardImageStore.TryDelete(name)", del);
        Assert.DoesNotContain("File.Delete(", del);
    }

    [Fact]
    public void TheResultSentenceKeepsTheThreeOutcomesApart()
    {
        var body = MethodBody(ReadRepoPartials(Vm), CommandAnchor);
        Assert.Contains("腾出约", body);                       // 全删成：删了几件 + 多少体积
        Assert.Contains("还在名单里", body);                     // 部分/全部删不掉：不许报"清理完成"
        Assert.Contains("一件都没删掉", body);
        Assert.Contains("没删任何东西", body);                   // 用户点了"先不删"也要有一句话落回界面上
        // 删完必须就地重算占用：数字不动，用户会以为那一步白点。
        Assert.Contains("ComputeClipboardUsage()", body);
    }

    [Fact]
    public void AnUnavailableScanIsNeitherGreenLightNorFalseAllClear()
    {
        var vm = ReadRepoPartials(Vm);
        var body = MethodBody(vm, "private void ComputeClipboardUsage()");
        Assert.Contains("清理按钮暂时不可用", body);
        Assert.DoesNotContain("没有孤儿", body);               // 扫不出差集时不许冒充"一切正常"
        Assert.Contains("CanCleanClipboardAssets = canClean", body);
        // 回灌只发生在 UI 那一侧：池线程不碰绑定（否则症状是"数字偶尔不更新"）。
        Assert.True(body.IndexOf("DispatcherQueue", StringComparison.Ordinal)
                    < body.IndexOf("CanCleanClipboardAssets = canClean", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewLimitAndOrphanRuleHaveOneHomeEach()
    {
        var assets = ReadRepoFile(Assets);
        Assert.Equal(1, Count(assets, "public const int CleanupPreviewLimit"));
        Assert.Equal(1, Count(assets, "public static string DescribeOrphans("));
        var vm = ReadRepoPartials(Vm);
        // UI 不许手抄那个上限，也不许自己算"什么算孤儿"（判据只有一份＝ClipAssets.Reconcile）。
        Assert.DoesNotContain("CleanupPreviewLimit = 8", vm);
        Assert.DoesNotContain("Reconcile(", vm);
        Assert.Equal(1, Count(ReadRepoFile(Watcher), "ClipAssets.Reconcile(rows"));
    }
}
