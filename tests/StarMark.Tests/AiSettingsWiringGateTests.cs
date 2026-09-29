#nullable enable
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 R4（AI 设置页三格）的接线守门。<b>测试工程不引用 StarMark.UI</b>，所以这一层只能钉源码形状——
/// 而恰恰是"格子画了却没接上存储"这类失效最容易在改文案时静默回来：XAML 里名字还在、看着像有功能，
/// 实际保存路径上根本没有它（<c>_aiClassifyModelHeld</c> 那一段就是这枚闸门前身要挡的事故）。
/// <para>每条都写清"破了会怎样"；每条钉的都是<b>调用式</b>，不是出现次数（#181：钉计数的闸门会在
/// 下一次搬家或多一个合法读者时先红给你看，而不是先绿着漏掉真问题）。</para>
/// </summary>
public sealed class AiSettingsWiringGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string PageAi = "src/StarMark.UI/Views/SettingsPage.Ai.cs";
    private const string PageUsage = "src/StarMark.UI/Views/SettingsPage.AiUsage.cs";
    private const string StoreAi = "src/StarMark.UI/Helpers/SettingsStore.Ai.cs";
    private const string Service = "src/StarMark.UI/Services/AiClassifyService.cs";
    private const string Budget = "src/StarMark.Abstractions/Ai/AiBudget.cs";
    private const string Runner = "src/StarMark.Core/Ai/ClassifyRunner.cs";

    /// <summary>设置页「AI 增强」整页。</summary>
    private static string AiTab()
        => Between(ReadRepoFile(Xaml), "<TabViewItem Header=\"AI 增强\">", "</TabViewItem>");

    // ───────── 1）三格都在页上，且各自有名字（画了格子） ─────────

    [Theory]
    [InlineData("AiClassifyModelBox")]
    [InlineData("AiTimeoutBox")]
    [InlineData("AiWarnRatioBox")]
    public void EachNewBoxIsDeclared(string name)
    {
        // 破了会怎样：代码里读这一个控件会当场编译不过——除非 XAML 与 .cs 同时改错，
        // 那就正好是这条闸门要拦的"看起来接上了"。
        Assert.Contains($"x:Name=\"{name}\"", AiTab());
    }

    // ───────── 2）每一格都要既有装载又有保存（缺一半＝哑键） ─────────

    [Fact]
    public void ClassifyModelBoxIsBothLoadedAndSaved()
    {
        // 装载缺了：开页时这格永远显示空，用户以为没设过；保存缺了：填了字、按了别处，值就没了。
        // 后者正是当年"分类模型只能手改 JSON"的成因（当时靠一个 _aiClassifyModelHeld 透传兜住）。
        var page = ReadRepoPartials(PageAi);
        Assert.Contains("AiClassifyModelBox.Text = stored.ClassifyModel", page);
        Assert.Contains("ClassifyModel: AiClassifyModelBox.Text", page);
        // 透传那位"临时持有人"必须整条消失：留着它就会有人以为界面还没接上，再在保存路径上补第二份事实源。
        Assert.DoesNotContain("_aiClassifyModelHeld", page);
    }

    [Fact]
    public void TimeoutBoxGoesThroughTheStore()
    {
        var page = ReadRepoPartials(PageAi);
        Assert.Contains("store.SaveAiBatchTimeout(asked)", page);
        Assert.Contains("LoadAiBatchTimeout()", page);
        // 装载那一段必须压住写盘闸门，否则"打开设置页"这件事本身就是一次整档读写（P-43 那一类）。
        Assert.Contains("if (_aiLoading) return;", MethodBody(page, "private void AiTimeoutBox_TextChanged"));
        Assert.Contains("_aiLoading = true;", MethodBody(page, "private void InitAiOrganiseSection"));
    }

    [Fact]
    public void WarnRatioBoxGoesThroughTheBudget()
    {
        var usage = ReadRepoPartials(PageUsage);
        Assert.Contains("store.LoadAiBudget().WithWarnRatio(percent)", usage);
        Assert.Contains("RefreshAiWarnRatioNote();", MethodBody(usage, "private void InitAiUsagePanel"));
        Assert.Contains("if (_aiLoading) return;", MethodBody(usage, "private void AiWarnRatioBox_TextChanged"));
        // 界面只调 WithWarnRatio：自己写一份"这个比例合不合法"就是第二真值，两边迟早分岔（R8 第一梯队那个毛病）。
        Assert.DoesNotContain("WarnRatioOverride = ", usage);
    }

    [Fact]
    public void BothNewSettingsHaveAStoreReaderAndWriter()
    {
        var store = ReadRepoPartials(StoreAi);
        // 超时：读写都过 ClassifyRunner 那一道判据，界面与仓储都不许自己夹边
        Assert.Contains("ClassifyRunner.NormalizeTimeoutSeconds(stored)", MethodBody(store, "public (int Seconds, string? Reason) LoadAiBatchTimeout"));
        Assert.Contains("ClassifyRunner.NormalizeTimeoutSeconds(seconds) == seconds", MethodBody(store, "public void SaveAiBatchTimeout"));
        // 预警比例：消毒放在读取而不是展示——判额与展示读的是同一个实例，只有一边消毒就会出现"面板写 80%、裁决按 5 倍预算"
        Assert.Contains("budget.WithWarnRatio(ratio)", MethodBody(store, "public AiBudget LoadAiBudget"));
        Assert.Contains("d.AiWarnRatio = budget.WarnRatioOverride;", MethodBody(store, "public void SaveAiBudget"));
    }

    [Fact]
    public void BatchTimeoutHasReadersAtBothClassifyCallSites()
    {
        // 破了会怎样：格子能填、能存、能显示，而发请求时仍用出厂 180 秒——"改了没反应"最难自查的一种。
        // 两条路（批量整理 / 收藏即时分类）都是"一批"，所以两处都要传；只钉一处会漏掉另一条。
        var service = ReadRepoFile(Service);
        const string call = "timeoutSeconds: _store.LoadAiBatchTimeout().Seconds";
        Assert.Contains(call, MethodBody(service, "public async Task<OrganiseOutcome> OrganiseAsync"));
        Assert.Contains(call, MethodBody(service, "public async Task TryInstantClassifyAsync"));
    }

    // ───────── 3）三格"只读展示"的那三个数：数字必须来自常数，不许在界面里再写一遍 ─────────

    [Fact]
    public void FixedBatchLimitsAreShownFromTheirConstants()
    {
        var page = ReadRepoPartials(PageAi);
        Assert.Contains("AiBatchLimitsText.Text = DescribeFixedBatchLimits();", MethodBody(page, "private void InitAiOrganiseSection"));
        var note = MethodBody(page, "private static string DescribeFixedBatchLimits");
        Assert.Contains("ClassifyPrompt.MaxItemsPerBatch", note);
        Assert.Contains("ClassifyPrompt.UserBudgetChars", note);
        Assert.Contains("ClassifyPrompt.MaxReferenceTags", note);
        // 真正会挡住的那道闸门也要报出同一个数：只报"18K 预算"而不报 24K 输入闸门，用户以为还能再调大
        Assert.Contains("AiRequest.MaxPromptChars", note);
    }

    [Theory]
    [InlineData(StoreAi)]
    [InlineData(PageAi)]
    [InlineData(PageUsage)]
    public void BandConstantsAreNotReauthoredOutsideTheirHome(string file)
    {
        // 这三颗上下界只在 ClassifyRunner.cs 赋值一次；谁在别的文件再赋一次，就是长出第二真值的现场。
        var text = ReadRepoPartials(file);
        Assert.DoesNotContain("MaxTimeoutSeconds = ", text);
        Assert.DoesNotContain("MinTimeoutSeconds = ", text);
        Assert.DoesNotContain("DefaultWarnRatio = ", text);
        Assert.DoesNotContain("WarnRatioOverride = ", text);
    }

    [Fact]
    public void JudgeStillReadsTheSingleWarnRatio()
    {
        // 熔断与预警各读一处才算一套口径：Judge 改成读 WarnRatioOverride 就变成"设过才预警"，
        // 没设过的用户会在 80% 那句提示消失之后才发现自己被熔断了。
        var judge = MethodBody(ReadRepoFile(Budget), "public AiBudgetVerdict Judge");
        Assert.Contains("MonthlyTokenBudget * WarnRatio", judge);
        Assert.DoesNotContain("WarnRatioOverride", judge);
    }
}
