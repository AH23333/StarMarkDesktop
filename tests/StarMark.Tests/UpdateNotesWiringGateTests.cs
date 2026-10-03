#nullable enable
using System;
using System.Linq;
using StarMark.Core.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「这一版的更新说明」在界面那一侧的接线闸门（批次 VX 后半）。
/// <para>
/// 测试工程不引用 <c>StarMark.UI</c>（#184 那条边界），所以本批那两条边界只能钉源码形状：
/// ① <b>markdown 的收敛只有一处，在 Core</b>——界面自己 <c>Replace</c> 一次就有第二副正文，
///    而"屏幕上读到的是哪一副"从此没人说得清（同一族：读数那句的两处写法，批次 VW）；
/// ② <b>那段外部文本不许住进设置档</b>——落盘就会被自动备份与同步一起带走，
///    还会在下一版发布后继续冒充"这一版的说明"。这一条最坏的实现方式恰好是"顺手加一格存档"，
///    所以钉的是档里那组键的形状，不是某句注释。
/// </para>
/// </summary>
public sealed class UpdateNotesWiringGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string View = "src/StarMark.UI/Views/SettingsPage.Updates.cs";
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Updates.cs";
    private const string Service = "src/StarMark.Core/Updates/UpdateService.cs";
    private const string Cleaner = "src/StarMark.Core/Updates/ReleaseNotes.cs";
    private const string Store = "src/StarMark.UI/Helpers/SettingsStore.Updates.cs";

    // ===== 那一格：存在、默认收着、由"这一次真带回了正文"决定出不出现 =====

    [Fact]
    public void TheNotesPanelLivesInTheUpdateCardAndStartsHidden()
    {
        var xaml = ReadRepoFile(Xaml);
        // 排在「关于与更新」这张卡里、按钮之后：长高的是一格补充阅读，不该把刚点过的那一排顶下去
        Between(xaml, "Text=\"关于与更新\"", "UpdateNotesExpander");
        Assert.Contains("UpdateNotesExpander\" Header=\"这一版的更新说明\" Visibility=\"Collapsed\"", xaml,
            StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind ViewModel.ReleaseNotesText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePanelIsPutAwayBeforeTheNextCheckAndOnlyThenBroughtBack()
    {
        var body = MethodBody(Code(ReadRepoFile(View)), "private async void UpdateCheck_Click");
        var hidden = body.IndexOf("UpdateNotesExpander.Visibility = Visibility.Collapsed", StringComparison.Ordinal);
        var asked = body.IndexOf("await ViewModel.CheckForUpdatesAsync()", StringComparison.Ordinal);
        var shown = body.IndexOf("if (ViewModel.HasReleaseNotes)", StringComparison.Ordinal);
        // 三条都必须在：先收上一发的正文（那几样出口都属于这一次的答案），问完了才按这一发摆回来
        Assert.True(hidden >= 0 && asked >= 0 && shown >= 0, "那一格的收与放都得在同一个动作里");
        Assert.True(hidden < asked, "点下去时先把上一发的正文收掉");
        Assert.True(shown > asked, "正文只能由这一次的答复带回来");
        Assert.Equal(1, Count(body, "UpdateNotesExpander.Visibility = Visibility.Visible"));
    }

    /// <summary>
    /// 界面<b>只读 Core 清好的那一份</b>：这一屏不许自己清 markdown。
    /// 禁项读的是去掉注释后的代码——注释里引用一句"清掉 XX 记号"是为了讲为什么，不是又开一个出口（#123）。
    /// </summary>
    [Fact]
    public void TheUiReadsTheCleanedTextAndNeverCleansIt()
    {
        var vm = Code(ReadRepoFile(Vm));
        Assert.Contains("?.Notes", MethodBody(vm, "public string ReleaseNotesText"), StringComparison.Ordinal);
        foreach (var banned in new[] { "Regex", "Replace(", "Trim(", "TextTrim", "ReleaseNotes.Clean", "Body" })
            Assert.DoesNotContain(banned, vm + Code(ReadRepoFile(View)), StringComparison.Ordinal);
        // 状态行与那一格正文是两个出口：把说明拼进句子，收起折叠区时句子还留着它
        Assert.DoesNotContain("Notes", MethodBody(vm, "private string BuildUpdateStatus()"), StringComparison.Ordinal);
    }

    /// <summary>清 markdown 这件事<b>全仓只有一个调用点</b>，且那一处住在 Core。</summary>
    [Fact]
    public void TheTextIsCleanedInExactlyOnePlaceAndItIsCore()
    {
        var callers = ReadRepoUnder("src")
            .Where(entry => Code(entry.Text).Contains("ReleaseNotes.Clean", StringComparison.Ordinal))
            .Select(entry => entry.RelativePath)
            .ToArray();
        Assert.Equal(["src/StarMark.Core/Updates/UpdateService.cs"], callers);
        Assert.Equal(1, Count(Code(ReadRepoFile(Service)), "ReleaseNotes.Clean"));
    }

    /// <summary>
    /// 长度那一刀走全仓唯一的切法（P-130）：自己 <code>Substring</code> 会切出半个 emoji，
    /// 而这一串的内容由对方说了算，正是要防的那种输入。
    /// </summary>
    [Fact]
    public void TheTruncationOfTheBorrowedTextUsesTheOneSharedCut()
    {
        var cleaner = Code(ReadRepoFile(Cleaner));
        Assert.Contains("TextTrim.Ellipsize", cleaner, StringComparison.Ordinal);
        Assert.DoesNotContain("Substring", cleaner, StringComparison.Ordinal);
    }

    // ===== 边界：正文绝不进设置档 =====

    /// <summary>
    /// 宿主那五格<b>就是五格</b>：读五行＋写五行＝十处，多一处就是给那段外部文本开了一格存档。
    /// 这条比"文件里有没有 Notes 这个词"硬：它钉的是形状，改名字躲不过去。
    /// </summary>
    [Fact]
    public void TheSettingsFileStillCarriesOnlyTheFiveVerdictKeys()
    {
        var store = Code(ReadRepoFile(Store));
        Assert.Equal(10, Count(store, "d.Update"));
        foreach (var banned in new[] { "Notes", "Body", "Markdown" })
            Assert.DoesNotContain(banned, store, StringComparison.Ordinal);
    }

    /// <summary>
    /// Core 那一侧同一条边界：<b>落盘的那个形状没有正文那一格</b>，而一次答复有。
    /// 钉记录形状而不是钉字段名——把 <c>Notes</c> 改叫 <c>ReleaseBody</c> 再塞进 <see cref="UpdateState"/>
    /// 是同一个错误，那种改法必须照样红。
    /// </summary>
    [Fact]
    public void TheArchivedShapeHasNoPlaceForTheTextWhileTheAnswerDoes()
    {
        var code = Code(ReadRepoFile(Service));
        var archived = Between(code, "public sealed record UpdateState(", ");");
        foreach (var banned in new[] { "Notes", "Body", "Markdown" })
            Assert.DoesNotContain(banned, archived, StringComparison.Ordinal);

        var answer = Between(code, "public sealed record UpdateReport(", ");");
        Assert.Contains("Notes", answer, StringComparison.Ordinal);
        // 从档里重算的那一份填不出正文：它压根没有来源，这正是"重启后那一格空着"的机械原因
        Assert.DoesNotContain("Notes", MethodBody(code, "public UpdateReport? LastReport()"), StringComparison.Ordinal);
    }
}
