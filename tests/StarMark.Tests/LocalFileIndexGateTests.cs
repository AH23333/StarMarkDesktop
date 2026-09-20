#nullable enable
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 本地磁盘搜索生命周期门（纯逻辑）：默认关→一切不动；未授权不提示扫描；仅就绪才查询。
/// 这是 UI 与后台提权服务共同遵循的契约，先于服务落地锁定语义。
/// </summary>
public sealed class LocalFileIndexGateTests
{
    [Fact]
    public void Disabled_IgnoresAllOtherFacts_NoQueryNoPromptNoElevation()
    {
        // 关键护栏：即便服务在跑、索引就绪，只要用户没开启，一切门控都必须为关（默认零内存零打扰）。
        var phase = LocalFileIndexGate.Evaluate(
            new LocalFileIndexFacts(Enabled: false, ServiceInstalled: true, ServiceRunning: true, IndexReady: true));

        Assert.Equal(LocalFileIndexPhase.Disabled, phase);
        Assert.False(LocalFileIndexGate.ShouldQuery(phase));
        Assert.False(LocalFileIndexGate.ShouldShowScanProgress(phase));
        Assert.False(LocalFileIndexGate.ShouldRequestElevation(phase));
    }

    [Fact]
    public void Enabled_ButServiceNotInstalled_IsNeedsSetup_PromptsElevation_NotScanProgress()
    {
        // 未授权（服务未装）阶段：应提示走 UAC，但绝不可显示"首次扫描进行中"——用户还没授权。
        var phase = LocalFileIndexGate.Evaluate(
            new LocalFileIndexFacts(Enabled: true, ServiceInstalled: false, ServiceRunning: false, IndexReady: false));

        Assert.Equal(LocalFileIndexPhase.NeedsSetup, phase);
        Assert.True(LocalFileIndexGate.ShouldRequestElevation(phase));
        Assert.False(LocalFileIndexGate.ShouldShowScanProgress(phase));
        Assert.False(LocalFileIndexGate.ShouldQuery(phase));
    }

    [Theory]
    [InlineData(true, false, false, LocalFileIndexPhase.Starting)]
    [InlineData(true, true, false, LocalFileIndexPhase.Indexing)]
    [InlineData(true, true, true, LocalFileIndexPhase.Ready)]
    public void Enabled_Progression(bool installed, bool running, bool indexReady, LocalFileIndexPhase expected)
    {
        var phase = LocalFileIndexGate.Evaluate(
            new LocalFileIndexFacts(Enabled: true, ServiceInstalled: installed, ServiceRunning: running, IndexReady: indexReady));
        Assert.Equal(expected, phase);
    }

    [Fact]
    public void Only_Indexing_ShowsScanProgress_AndOnly_Ready_Queries()
    {
        Assert.True(LocalFileIndexGate.ShouldShowScanProgress(LocalFileIndexPhase.Indexing));
        Assert.False(LocalFileIndexGate.ShouldShowScanProgress(LocalFileIndexPhase.Starting));
        Assert.False(LocalFileIndexGate.ShouldShowScanProgress(LocalFileIndexPhase.Ready));

        Assert.True(LocalFileIndexGate.ShouldQuery(LocalFileIndexPhase.Ready));
        Assert.False(LocalFileIndexGate.ShouldQuery(LocalFileIndexPhase.Indexing));
    }
}
