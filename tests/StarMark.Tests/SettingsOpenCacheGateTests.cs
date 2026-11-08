#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 设置页导航缓存闸门（片245，扫源码不跑界面）。
/// <para>
/// 现场读数（starmark-20261108.log・pid 46592・19:42:10 段）：点「设置」→ UI 线程阻塞约 1500ms
/// （其间 UI 线程自耗 828ms CPU・前台页 tree→settings），且 19:43:01 再进又是一条全新 ctor
/// （「页面已加载=False」取证）——131KB 的 SettingsPage.xaml 每次进页重解析；
/// ctor 与 OnNavigatedTo 在首开还把 LoadHealthAsync／三族行构建各跑两遍。
/// 这里守五半，缺一算没做：
/// ① NavigationCacheMode="Required"——2nd+ 进页零 ctor 零 XAML 解析（页早已写成缓存就绪形状：
/// OnNavigatedTo=每次进页刷新、OnNavigatedFrom=退订/热键冲账/兜底保存）；
/// ② ctor 不再重复 OnNavigatedTo 必做的活（LoadHealthAsync＋三族行构建）；
/// ③ 进页刷新不许被顺手删——删了缓存页就停在旧值（LoadFromStore＋行构建每次进页必到）；
/// ④ 离开页的行为（热键冲账＋兜底保存＋停录制）原样在册——缓存后每次导航都走 From，丢一件就丢一辈子；
/// ⑤ 首建/进页分步取证在场：慢步留名（≥100ms）、总账补行（≥300ms），快路径零日志。
/// </para>
/// </summary>
public sealed class SettingsOpenCacheGateTests
{
    private static string PageXaml() => SourceGate.ReadRepoFile("src/StarMark.UI/Views/SettingsPage.xaml");
    private static string PageCode() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Views/SettingsPage.xaml.cs"));
    private static string NavCode() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Views/SettingsPage.Nav.cs"));
    private static string TraceCode() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Helpers/SettingsOpenTrace.cs"));

    /// <summary>缓存刀本体：Frame 缓存这颗页（Required＝不受 Frame.CacheSize 摆布），2nd+ 打开不再付 ctor。</summary>
    [Fact]
    public void ThePageIsCachedByTheFrame()
        => Assert.Contains("NavigationCacheMode=\"Required\"", PageXaml());

    /// <summary>ctor 不再重复 OnNavigatedTo 必做的活（首开原本双跑）；LoadFromStoreSilently 刻意留下——
    /// BuildAccentPane／Apply* 首建要直读直贴 VM 值，不押「值变了才有通知」那层间接。</summary>
    [Fact]
    public void TheCtorDoesNotDuplicateWhatOnNavigatedToAlreadyDoes()
    {
        var ctor = SourceGate.MethodBody(PageCode(), "public SettingsPage()");
        Assert.DoesNotContain("LoadHealthAsync", ctor);
        Assert.DoesNotContain("BuildWidgetRows", ctor);
        Assert.DoesNotContain("BuildHotkeyRows", ctor);
        Assert.DoesNotContain("BuildLayoutRows", ctor);
        Assert.Contains("LoadFromStoreSilently", ctor);   // 首建直贴的那份在（BuildAccentPane 前面）
        Assert.True(ctor.IndexOf("LoadFromStoreSilently", System.StringComparison.Ordinal)
                    < ctor.IndexOf("BuildAccentPane", System.StringComparison.Ordinal),
                    "首建装载必须排在取色器面板构建之前（直读直贴，不押变更通知）");
    }

    /// <summary>进页刷新不许被顺手删：缓存页的新鲜度全靠它——LoadFromStore＋健康诊断＋三族行构建每次进页必到。</summary>
    [Fact]
    public void PerEntryRefreshStaysInOnNavigatedTo()
    {
        var to = SourceGate.MethodBody(NavCode(), "protected override void OnNavigatedTo(NavigationEventArgs e)");
        Assert.Contains("LoadFromStoreSilently", to);
        Assert.Contains("LoadHealthAsync", to);
        Assert.Contains("LoadDiagnosticsAsync", to);
        Assert.Contains("BuildWidgetRows", to);
        Assert.Contains("BuildHotkeyRows", to);
        Assert.Contains("BuildLayoutRows", to);
    }

    /// <summary>离开页的行为原样在册：缓存后每次导航都走 From——热键冲账、停录制、兜底保存丢一件就丢一辈子。</summary>
    [Fact]
    public void LeavingStillFlushesHotkeysAndSaves()
    {
        var from = SourceGate.MethodBody(NavCode(), "protected override void OnNavigatedFrom(NavigationEventArgs e)");
        Assert.Contains("StopRecording", from);
        Assert.Contains("_hotkeysDirty", from);
        Assert.Contains("ApplyHotkeyBindings", from);
        Assert.Contains("SaveCommand.Execute", from);
    }

    /// <summary>分步取证在场且只在慢现场说话：单步 ≥100ms 记行、总账 ≥300ms 补行——快路径一条日志都不发；
    /// 首建（ctor）与进页（OnNavigatedTo）两只表各接各的（缓存刀的效果＝「首建全程」一次、「进页刷新全程」多次）。</summary>
    [Fact]
    public void SlowStepsAreTracedAndFastPathStaysSilent()
    {
        var trace = TraceCode();
        Assert.Contains("internal const int StepLogMs = 100", trace);
        Assert.Contains("internal const int TotalLogMs = 300", trace);
        var step = SourceGate.MethodBody(trace, "internal static long Step(");
        Assert.True(step.IndexOf("now - fromMs >= StepLogMs", System.StringComparison.Ordinal)
                    < step.IndexOf("StarLog.Info", System.StringComparison.Ordinal),
                    "单步日志必须锁在阈值之内（快路径零日志）");
        var total = SourceGate.MethodBody(trace, "internal static void Total(");
        Assert.True(total.IndexOf("sw.ElapsedMilliseconds >= TotalLogMs", System.StringComparison.Ordinal)
                    < total.IndexOf("StarLog.Info", System.StringComparison.Ordinal),
                    "总账日志必须锁在阈值之内（快路径零日志）");
        var ctor = SourceGate.MethodBody(PageCode(), "public SettingsPage()");
        Assert.Contains("SettingsOpenTrace.Step(buildSw", ctor);
        Assert.Contains("SettingsOpenTrace.Total(buildSw", ctor);
        var to = SourceGate.MethodBody(NavCode(), "protected override void OnNavigatedTo(NavigationEventArgs e)");
        Assert.Contains("SettingsOpenTrace.Step(entrySw", to);
        Assert.Contains("SettingsOpenTrace.Total(entrySw", to);
    }
}
