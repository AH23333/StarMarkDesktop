#nullable enable
using System;
using System.Linq;
using StarMark.Core.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「立即更新」在界面那一侧的接线闸门（批次 UG-3）。
/// <para><c>StarMark.Tests</c> 不引用 <c>StarMark.UI</c>（#184 那条边界），所以这一族的断言只能读源码形状：
/// 判据与措辞都在 Core（能被行为测逐字钉住），界面只剩"按钮、那一行字、让锁、退出"四件没有逻辑的事。
/// 这四件恰恰是<b>行为测永远看不见</b>的那一层——少让一次锁、多抄一句措辞，全线测试照样绿。</para>
/// </summary>
public sealed class UpdateApplyWiringGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string View = "src/StarMark.UI/Views/SettingsPage.Updates.cs";
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Updates.cs";
    private const string AppFile = "src/StarMark.UI/App.xaml.cs";
    private const string MainFile = "src/StarMark.UI/MainWindow.xaml.cs";

    // ===== 那颗按钮：存在、默认收着、由 Core 的判据决定它出不出现 =====

    [Fact]
    public void TheApplyButtonExistsAndStartsHidden()
    {
        var xaml = ReadRepoFile(Xaml);
        Assert.Contains("x:Name=\"UpdateApplyButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"UpdateApply_Click\"", xaml, StringComparison.Ordinal);
        // 默认收起：在"还没有发布过"那一格摆一颗「立即更新」，等于承诺了一个不存在的产物
        Assert.Contains("UpdateApplyButton\" Content=\"立即更新\"", xaml, StringComparison.Ordinal);
        Assert.True(Between(xaml, "x:Name=\"UpdateApplyButton\"", "/>").Contains("Visibility=\"Collapsed\"", StringComparison.Ordinal),
            "那颗按钮的初始可见性必须是 Collapsed");
    }

    [Fact]
    public void TheApplyButtonAppearsOnlyOnTheCoreVerdictAndNotOnItsOwnReading()
    {
        var view = Code(ReadRepoFile(View));
        Assert.Contains("ViewModel.HasDownloadableRelease", view, StringComparison.Ordinal);
        // 界面不许自己再判一次"有没有新版"（判据有两处出处就会漂，#189/#193）
        foreach (var banned in new[] { "Verdict ==", "RemoteTag >", "AppVersion.TryParse" })
            Assert.DoesNotContain(banned, view, StringComparison.Ordinal);
    }

    // ===== 交棒之后：先让锁，再退出 =====

    [Fact]
    public void TheHandoffExitReleasesTheSingleInstanceLockBeforeExiting()
    {
        var body = MethodBody(ReadRepoFile(MainFile), "public void ExitForUpdateHandoff()");
        Assert.Contains("App.ReleaseSingleInstanceForHandoff()", body, StringComparison.Ordinal);
        Assert.Contains("ExitApp()", body, StringComparison.Ordinal);
        // 顺序也是判据：先退出再让锁＝新一版去唤起这个马上要消失的旧窗口，两个进程都没了
        Assert.True(body.IndexOf("ReleaseSingleInstanceForHandoff", StringComparison.Ordinal)
                    < body.IndexOf("ExitApp()", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyARealHandoffMakesTheWindowQuit()
    {
        var view = Code(ReadRepoFile(View));
        var body = MethodBody(view, "private async void UpdateApply_Click");
        Assert.Contains("if (result.IsHandedOff) App.MainWindow?.ExitForUpdateHandoff();", body, StringComparison.Ordinal);
        // 除了那一格，任何失败都不许让程序退（"没换成还把用户的应用关掉"是最难复现的一种坏）
        Assert.Equal(1, Count(body, "ExitForUpdateHandoff"));
    }

    // ===== 取消：那一发必须真能掐掉（P-55 / QA-2 同一条口径） =====

    [Fact]
    public void TheSameButtonReallyCancelsTheRunningFlow()
    {
        var body = MethodBody(Code(ReadRepoFile(View)), "private async void UpdateApply_Click");
        // 钉的是"这一发被掐了"这个动作与它的三条出口，不钉那颗字段叫什么（改名的合法重构不该把门撞红，#33 的对价）
        Assert.Contains("?.Cancel();", body, StringComparison.Ordinal);
        Assert.Contains("UpdateApplyButton.Content = \"取消更新\"", body, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", body, StringComparison.Ordinal);
        Assert.Contains(".Dispose();", body, StringComparison.Ordinal);   // 掐掉了也要把那颗 CTS 还回去
    }

    /// <summary>
    /// 两条进度出口<b>都必须回到 UI 线程再写</b>（批次 VW 把字节读数加成第二条）。
    /// 只钉 <c>ShowApplyPhase</c> 的那一格不够：读数那一条是每下一格就叫一次的，
    /// 跨线程写绑定属性的表现是随机崩，而它偏偏只在真下载时才有量。
    /// </summary>
    [Theory]
    [InlineData("private void ShowApplyPhase(ApplyPhase phase)")]
    [InlineData("private void ShowDownloadProgress(DownloadProgress progress)")]
    public void TheProgressLineComesBackOnTheUIThread(string method)
    {
        var body = MethodBody(Code(ReadRepoFile(View)), method);
        Assert.Contains("DispatcherQueue.TryEnqueue", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 读数那句的<b>措辞出处只有一处</b>：界面拿 <see cref="StarMark.Core.Updates.UpdatePolicy"/> 的那一句，
    /// 不自己拼"已下多少 MB"。数字写法与字节梯子也都不许在界面里另抄一份（P-122 那一族：两处写法迟早漂成两种读数）。
    /// </summary>
    [Fact]
    public void TheReadoutSentenceComesFromTheSamePlaceAsThePhaseSentence()
    {
        var vm = MethodBody(Code(ReadRepoFile(Vm)), "public void ShowDownloadProgress(DownloadProgress progress)");
        Assert.Contains("UpdatePolicy.Describe(progress)", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("MB", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("FileSizeText", vm, StringComparison.Ordinal);
    }

    // ===== 措辞唯一出处：界面一个字都不抄 =====

    [Fact]
    public void TheUiCopiesNoUpdateSentenceOutOfUpdatePolicy()
    {
        var ui = Code(ReadRepoFile(View)) + Code(ReadRepoFile(Vm));
        foreach (var banned in new[]
                 {
                     "正在下载", "正在核对", "正在把新版本摊开", "正在把替换交给更新器",
                     "停在半路", "交给更新器", "没通过校验", "没带更新器",
                 })
            Assert.DoesNotContain(banned, ui, StringComparison.Ordinal);
        Assert.Contains("UpdatePolicy.Describe", ui, StringComparison.Ordinal);
        Assert.Contains("UpdatePolicy.UpdateCancelledSentence", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProbeSentenceIsScopedAndNoLongerPromisesTheProgramNeverReplacesItself()
    {
        var xaml = ReadRepoFile(Xaml);
        // 旧那句"本程序不做下载与安装"已经成谎：它现在能做，只有点下去那一发才做
        Assert.DoesNotContain("下载与安装是另一件事，本程序不做", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"只问一句", xaml, StringComparison.Ordinal);   // 没限定主语的那句全局承诺
        Assert.Contains("自动那一发只问", xaml, StringComparison.Ordinal);            // "不下载不替换"只属于自动那一发
        Assert.Contains("只有点「立即更新」才真的下载并替换", xaml, StringComparison.Ordinal);
    }

    // ===== 容器与射程：路径与落点不经界面之手 =====

    [Fact]
    public void TheApplierAndThePackageSourceAreEachRegisteredOnce()
    {
        var app = Code(ReadRepoFile(AppFile));
        Assert.Equal(1, Count(app, "new StarMark.Core.Updates.UpdateApplier("));
        Assert.Equal(1, Count(app, "new StarMark.Integrations.Updates.GitHubUpdatePackageSource("));
        // 载荷是匿名可下的：把凭据递进"往盘上写字节"那条链路是白给的攻击面
        var block = Between(app, "IUpdatePackageSource>", "UpdateApplier(");
        Assert.DoesNotContain("Token", block, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUiHandsOverATagAndNothingElse()
    {
        var vm = Code(ReadRepoFile(Vm));
        var body = MethodBody(vm, "public Task<UpdateApplier.ApplyResult> ApplyUpdateAsync");
        Assert.Contains("LastReport()?.RemoteTag", body, StringComparison.Ordinal);
        // 路径／落点／更新器本体都不许从界面递过去（递过去就等于能被改成别的落点）
        foreach (var banned in new[] { "UpdaterPaths", "installDir", "UpdaterRequest", "UpdateApplier.Options", "Staging" })
            Assert.DoesNotContain(banned, vm, StringComparison.Ordinal);
    }
}
