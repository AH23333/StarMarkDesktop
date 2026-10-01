#nullable enable
using Xunit;
using StarMark.Core.Widgets;

namespace StarMark.Tests;

/// <summary>
/// 批次 SV：「开机自动加载组件」这颗开关（用户裁决：这笔性能取舍由他自己选）。
/// <para>
/// 判据与数字住在 <see cref="WidgetStartupPolicy"/>（可单测），宿主那边只剩接线——
/// UI 层出不了契约测（#184：StarMark.Tests 不引用 StarMark.UI），所以接线的部分是形状闸门。
/// 报告 §二百一十二。
/// </para>
/// </summary>
public sealed class WidgetStartupPolicyTests
{
    private const string Lifecycle = "src/StarMark.UI/Services/WidgetManager.Lifecycle.cs";
    private const string MainWindow = "src/StarMark.UI/MainWindow.xaml.cs";
    private const string Store = "src/StarMark.UI/Helpers/SettingsStore.cs";
    private const string StorePerf = "src/StarMark.UI/Helpers/SettingsStore.Performance.cs";
    private const string VmWidgets = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Widgets.cs";
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    private static string Body(string file, string signature)
        => SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(file), signature));

    /// <summary>
    /// 切出「表达式体里套一个多语句 lambda」（<c>public Task X() =&gt; OnUiAsync(() =&gt; { …一句; …一句; });</c>）的方法。
    /// <para>
    /// <see cref="SourceGate.MethodBody"/> 的表达式体分支"取到第一个分号行"，而这类体内<b>每一句都以分号结尾</b>，
    /// 于是它只交出前三行——闸门看不见真正的判据，SV 第一跑的两条红就是这么来的（不是代码错，是切片错）。
    /// 这里改按「本方法签名 → 下一个成员签名」切：整段都在，红也只可能红在判据上。
    /// </para>
    /// </summary>
    private static string LambdaBody(string file, string signature, string nextMember)
        => SourceGate.Code(SourceGate.Between(SourceGate.ReadRepoFile(file), signature, nextMember));

    private static string Code(string file) => SourceGate.Code(SourceGate.ReadRepoFile(file));

    /// <summary>
    /// 「数字＋MB」这个形状。<b>闸门禁的是形状，不是某一个具体的数</b>——见 <see cref="TheUserFacingFilesDoNotHardCodeTheNumber"/>。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex MbLiteral
        = new(@"\d+\s*MB", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // ───────── 判据与数字 ─────────

    /// <summary>
    /// 每颗的私有成本是<b>量出来的那个数</b>。7 来自本批那一对"只差这颗开关"的 A/B（12 颗：省 82.2 MB ⇒ 6.85/颗），
    /// SU-0 那条"删空档 vs 12 颗"的曲线给的是 95 MB ⇒ 7.9/颗；两跑同一格差 ~14 MB＝这把尺子的跨跑噪声，
    /// 所以取<b>靠下那一格</b>并在界面写"约"。改这个数必须是"又量了一次"（报告 §二百一十二）。
    /// </summary>
    [Fact]
    public void ThePerWindowCostIsTheMeasuredOne()
        => Assert.Equal(7, WidgetStartupPolicy.PerWindowPrivateMb);

    /// <summary>线性外推：12 颗＝84 MB（本批 A/B 实测 82.2，SU-0 那跑 95），6 颗＝42，1 颗＝7。</summary>
    [Fact]
    public void TheEstimateScalesWithInstances()
    {
        Assert.Equal(84, WidgetStartupPolicy.EstimatedPrivateMb(12));
        Assert.Equal(42, WidgetStartupPolicy.EstimatedPrivateMb(6));
        Assert.Equal(7, WidgetStartupPolicy.EstimatedPrivateMb(1));
    }

    /// <summary>0 颗与脏数据（负数）都说"没得省"，不许报出一个负 MB——那是"关了开关反而更占内存"的错觉来源。</summary>
    [Fact]
    public void NothingToSaveWhenThereIsNothingToSkip()
    {
        Assert.Equal(0, WidgetStartupPolicy.EstimatedPrivateMb(0));
        Assert.Equal(0, WidgetStartupPolicy.EstimatedPrivateMb(-5));
    }

    /// <summary>关掉时日志要说清三件事：关了什么、省了多少、怎么再点亮。缺第三条就会有人来报"我的组件没了"。</summary>
    [Fact]
    public void TheSkipLineCarriesCountMoneyAndTheWayBack()
    {
        var line = WidgetStartupPolicy.DescribeSkipped(12);

        Assert.Contains("开机自动加载已关", line, StringComparison.Ordinal);
        Assert.Contains("12 颗", line, StringComparison.Ordinal);
        // 钱数本身由上面两条钉死；这里只验"那句里确实带了钱数"——再抄一遍数字就成了第二份出处
        Assert.Contains($"{WidgetStartupPolicy.EstimatedPrivateMb(12)} MB", line, StringComparison.Ordinal);
        Assert.Contains("设置", line, StringComparison.Ordinal);
        Assert.Contains("托盘", line, StringComparison.Ordinal);
        Assert.Contains("重建", line, StringComparison.Ordinal);
    }

    /// <summary>颗数为 0 时那句不许说"0 颗一颗都不建"这种半截话——它得读起来像人话。</summary>
    [Fact]
    public void TheSkipLineStillReadsAtZero()
    {
        var line = WidgetStartupPolicy.DescribeSkipped(0);

        Assert.Contains("0 颗", line, StringComparison.Ordinal);
        Assert.Contains("0 MB", line, StringComparison.Ordinal);
    }

    /// <summary>界面那句话要写明<b>不用重启</b>——"请重启"在本项目里按缺陷算，措辞也得站在这一边。</summary>
    [Fact]
    public void TheTradeoffNotePromisesNoRestart()
    {
        var note = WidgetStartupPolicy.TradeoffNote;

        Assert.Contains("不用重启", note, StringComparison.Ordinal);
        Assert.Contains("默认", note, StringComparison.Ordinal);
        Assert.Contains($"{WidgetStartupPolicy.PerWindowPrivateMb} MB", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>"每颗几 MB"全仓只许有一个出处</b>（P-123/P-130 那条线）。
    /// <para>禁的是<b>"数字＋MB"这个形状</b>，不是某一个具体的数：本批就把 8 重量成了 7——
    /// 闸门钉住具体数字的话，下一次重量得跟着改闸门，而界面/日志里那份写死的读数却不会跟着动，
    /// 留下的正是"半新半旧两份数"。插值（<c>{政策} MB</c>）里没有数字，天然不撞这一条。</para>
    /// </summary>
    [Fact]
    public void TheUserFacingFilesDoNotHardCodeTheNumber()
    {
        foreach (var file in new[] { VmWidgets, SettingsXaml, Lifecycle })
        {
            var hit = MbLiteral.Match(Code(file));
            Assert.False(hit.Success,
                $"{file} 里写死了读数「{hit.Value}」——钱数只许从 WidgetStartupPolicy 取");
        }
    }

    // ───────── 接线（形状闸门）─────────

    /// <summary>启动路径必须<b>在建任何窗之前</b>判这颗开关——先建再判就等于没省（§二百零九：关掉退不回来）。</summary>
    [Fact]
    public void TheStartupGateRunsBeforeAnyWindowIsBuilt()
    {
        var body = LambdaBody(Lifecycle, "RestoreOnStartupAsync(bool loadOnStartup = true)",
                              "public Task ShutdownAllAsync");
        var gate = body.IndexOf("if (!loadOnStartup)", StringComparison.Ordinal);

        Assert.True(gate >= 0, "启动路径里找不到\"开关关掉就直接返回\"这一支");
        Assert.True(body.IndexOf("ApplyLayoutCore", StringComparison.Ordinal) > gate,
            "不建那一支必须排在套布局／显示之前，否则窗口已经建完了");
        // 只钉"有这一支"是不够的：把 return 摘掉，代码照样记日志、照样建窗——那才是这一条要拦的事。
        Assert.Contains("return;", SourceGate.Between(body, "if (!loadOnStartup)", "ApplyLayoutCore"),
            StringComparison.Ordinal);
        Assert.Contains("DescribeSkipped(", body, StringComparison.Ordinal);
        Assert.Contains("Mark(", body, StringComparison.Ordinal);   // 省没省都要有刻度可对
    }

    /// <summary>关掉开关走的是<b>关闭</b>出口，不是"隐藏"改名——隐藏只 SW_HIDE，那笔内存一分不退。</summary>
    [Fact]
    public void CollapsingClosesRatherThanHiding()
    {
        var body = LambdaBody(Lifecycle, "public Task CollapseAllAsync()", "private void ArmHiddenWindowReclaim");

        // 只钉"走整批关闭出口 + 把几何留在盘上"，不钉局部变量名：把 ids 改成 openIds 是合法重构，
        // 一条会因为改名而红的闸门教出来的不是"别绕过关闭出口"，而是"别改我的变量名"。
        var closeCall = SourceGate.Between(body, "CloseAll(", ");");
        Assert.Contains("persist: true", closeCall, StringComparison.Ordinal);
        Assert.DoesNotContain("HideTemporary(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("HideTemporaryAll(", body, StringComparison.Ordinal);
    }

    /// <summary>主窗把设置里那颗开关<b>原样递给</b>恢复入口：不许在这儿再判一次"要不要建"（两个判据＝将来分岔）。</summary>
    [Fact]
    public void TheHostPassesTheSwitchThroughAndDoesNotJudgeIt()
    {
        var code = Code(MainWindow);

        Assert.Contains("RestoreOnStartupAsync(_settings.LoadWidgetsLoadOnStartup())", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_settings.LoadWidgetsLoadOnStartup())", code, StringComparison.Ordinal);
    }

    /// <summary>读设置的判据是<b>"只有明确写着 false 才关"</b>：旧档没这个字段时必须等于今天的行为。</summary>
    [Fact]
    public void TheSettingDefaultsToTodaysBehaviour()
        => Assert.Contains("is not false", Body(StorePerf, "public bool LoadWidgetsLoadOnStartup()"), StringComparison.Ordinal);

    /// <summary>开关的 setter 只做三件事：落盘、递给自己、让宿主去建/收。<b>不许自己 new 窗口</b>（第二份"怎么建窗"的知识）。</summary>
    [Fact]
    public void TheViewModelDelegatesInsteadOfBuildingWindows()
    {
        var code = Code(VmWidgets);

        Assert.Contains("SaveWidgetsLoadOnStartup(value)", code, StringComparison.Ordinal);
        Assert.Contains("RestoreOnStartupAsync(loadOnStartup: true)", code, StringComparison.Ordinal);
        Assert.Contains("CollapseAllAsync()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new WidgetWindow(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowInternal", code, StringComparison.Ordinal);
    }

    /// <summary>设置键在 DTO 里只有一份（写坏的存档、复制粘贴的字段名，都从这条红里露出来）。</summary>
    [Fact]
    public void TheSettingKeyHasOneDeclaration()
        => Assert.Equal(1, SourceGate.Count(Code(Store), "WidgetsLoadOnStartup"));
}
