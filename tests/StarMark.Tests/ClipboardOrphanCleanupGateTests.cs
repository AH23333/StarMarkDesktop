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
        // 批次 WI-2f 起这颗按钮的字面量住在字符串表里 ⇒ 锚点搬按键（改锚不改判）。
        // 下面这几句钉的是"这一个动作在这页有出口、且给得出灰的理由"，与那颗键叫什么无关。
        Assert.Contains("i18n:LocText.Key=\"settings.data.cleanOrphans\"", xaml);
        Assert.Equal(1, Count(xaml, "ViewModel.CleanClipboardAssetsCommand"));
        Assert.Equal(1, Count(xaml, "ViewModel.CanCleanClipboardAssets, Mode=OneWay"));
        // 批次 WI-61c 起那行结果句改由宿主贴（VM 交回的是身份）⇒ 锚点搬到那颗元素名，改锚不改判：
        // "这一个动作在这页有出口、且看得见结果"仍由同一格钉着；句子本身归 SettingsDataWordGateTests。
        Assert.Equal(0, Count(xaml, "ViewModel.ClipboardCleanupStatus, Mode=OneWay"));
        Assert.Contains("x:Name=" + '"' + "ClipboardCleanupStatusText" + '"', xaml);
        // 灰着的时候必须看得见为什么：那句"另有 N 个文件不在历史里"就在同一张卡片里，由占用那行念出来。
        Assert.Contains("ClipboardUsage", xaml);
        // 但剪贴板页不许再长一颗（同一个动作两个入口＝两处确认框文案会分岔）。
        // 钉的是<b>命令</b>与那颗键而不是"清理孤儿"那四个字：换了语言或改了措辞，第二个入口仍然是同一个命令。
        var clipboardPage = ReadRepoFile("src/StarMark.UI/Views/ClipboardPage.xaml");
        Assert.DoesNotContain("CleanClipboardAssetsCommand", clipboardPage);
        Assert.DoesNotContain("settings.data.cleanOrphans", clipboardPage);
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
        // 批次 WI-61c→61d：那颗取消按钮的字也交进了表里 ⇒ 这里⛔ 再钉"先不删"那三个字（钉字＝钉住一种语言），
        // 改钉<b>那颗键在 CleanAsync 之前就被点名</b>——顺序判据一个字没放宽。
        Assert.Contains("LocMessage.Of(" + '"' + "clip.cleanCancel" + '"' + ")", body);
        Assert.True(body.IndexOf("LocMessage.Of(" + '"' + "clip.cleanCancel" + '"' + ")", StringComparison.Ordinal) < clean,
            "取消那颗键要在 CleanAsync 之前点名（否则弹框还没定下来就先删了）");
        // 空名单不许弹一个空框。
        // 批次 WI-61c：那句"没东西可清"交进了表里 ⇒ 这里改钉"那颗键在弹框之前就已定下来"。
        Assert.Contains("LocMessage.Of(" + '"' + "settings.data.cleanNothing" + '"' + ")", body);
        Assert.True(body.IndexOf("LocMessage.Of(" + '"' + "settings.data.cleanNothing" + '"' + ")", StringComparison.Ordinal) < dialog);
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
        // 批次 WI-61c 把这几种下落各交了一颗键 ⇒ 这里逐颗点名"有几种下落就有几颗键"，
        // 句子本身的事实（件数／体积／占用数／那批文件仍在名单里）由 SettingsDataWordGateTests 读表去管。
        // needle 连着键名后面那颗收尾引号一起比、⛔ 再带上收尾括号：cleanDone 是 cleanDoneWithTemp 的前缀，
        // 少那颗引号就"只贴了临时件那一颗"也能蒙过；带上括号则把<b>带实参</b>的那几句全判成没贴（现场如此）。
        foreach (var key in new[] { "cleanDone", "cleanDoneWithTemp", "cleanPartlyBlocked", "cleanAllBlocked", "cleanCancelled" })
            Assert.Contains("LocMessage.Of(" + '"' + "settings.data." + key + '"', body);
        // 体积读数仍由那一颗共享的字节梯子供：界面自己再排一份梯子就回到 ✅P-122 那个缺陷。
        Assert.Contains("ClipAssets.DescribeBytes(", body);
        // 删完必须就地重算占用：数字不动，用户会以为那一步白点。
        Assert.Contains("ComputeClipboardUsage()", body);
    }

    [Fact]
    public void AnUnavailableScanIsNeitherGreenLightNorFalseAllClear()
    {
        var vm = ReadRepoPartials(Vm);
        var body = MethodBody(vm, "private void ComputeClipboardUsage()");
        // ✅WI-64：那句"暂时不可用"进了表（屏幕上念出来的是哪一句归表与 `ClipboardAssetsWordGateTests`），
        // 这一格守的还是同一件事——<b>扫不出差集那一支必须给得出灰的理由</b>，⛔ 冒充"一切正常"。
        Assert.Contains("LocMessage.Of(\"clip.usage.recountBlocked\")", body);
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
        // 回型连着一起数（✅WI-64 把它升成可空的 <c>LocMessage</c>：0 颗孤儿交 null 而⛔ 空串）
        Assert.Equal(1, Count(assets, "public static LocMessage? DescribeOrphans("));
        var vm = ReadRepoPartials(Vm);
        // UI 不许手抄那个上限，也不许自己算"什么算孤儿"（判据只有一份＝ClipAssets.Reconcile）。
        Assert.DoesNotContain("CleanupPreviewLimit = 8", vm);
        Assert.DoesNotContain("Reconcile(", vm);
        Assert.Equal(1, Count(ReadRepoFile(Watcher), "ClipAssets.Reconcile(rows"));
    }
}
