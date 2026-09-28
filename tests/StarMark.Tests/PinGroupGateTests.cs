using System;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 贴图组（批次 S4-③）的守门。被测的四处都在 <c>StarMark.UI</c> / <c>StarMark.Integrations</c> 之外不可引用的一侧，
/// 所以按本仓惯例读源文件比结构。
/// <para>为什么值得钉：这一批最容易坏成两种"全绿而看不见"的形状——
/// ① 组里存了一份状态（于是"这组已隐藏"与"那张其实还在屏幕上"能同时成立）；
/// ② 托盘的发号规则被新增的子菜单打乱（于是点「收起这组」执行成「关闭这组」——不可逆的那一个）。</para>
/// <para>每条都带"锚点必须扫到"的反空转断言，扫不到结构就抛，免得一条永不执行的检查冒充绿灯。</para>
/// </summary>
public sealed class PinGroupGateTests
{
    private const string Roster = "src/StarMark.UI/Services/PinManager.cs";
    private const string Policy = "src/StarMark.Core/Capture/PinGrouping.cs";
    private const string Host = "src/StarMark.Integrations/SystemTray/TrayHost.cs";
    private const string Main = "src/StarMark.UI/MainWindow.xaml.cs";
    private const string Bar = "src/StarMark.UI/Views/CaptureOverlayWindow.ToolBar.cs";
    private const string Pickers = "src/StarMark.UI/Views/CaptureOverlayWindow.Pickers.cs";
    private const string PinWindow = "src/StarMark.UI/Views/CaptureOverlayWindow.Pin.cs";

    /// <summary>
    /// 组<b>不存状态</b>：成员表是唯一事实，"这组隐没隐 / 穿不穿"一律现算。
    /// 一旦有人加一个 <c>bool Hidden</c> 字段，托盘与窗体就变成两份账——那份错位只有用户看得出来。
    /// </summary>
    [Fact]
    public void AGroupStoresMembershipAndNeverASecondCopyOfState()
    {
        var pins = SourceGate.ReadRepoFile(Roster);
        var group = SourceGate.Between(pins, "public sealed class PinGroup", "\n    public static int Count");

        Assert.Contains("public List<CaptureOverlayWindow> Members { get; } = [first];", group);   // 反空转：确实扫到成员表
        Assert.Contains("public bool AllHidden => PinGrouping.AllIn(", group);                     // 派生（=> 而不是存储）
        Assert.Contains("public bool AllThrough => PinGrouping.AllIn(", group);
        Assert.DoesNotContain("private bool", group);
        Assert.DoesNotContain("{ get; set; }", group);
        Assert.DoesNotContain("{ get; private set; }", group);
    }

    /// <summary>
    /// 关掉一张必须同时把它从每一组里摘掉并修掉空组。留着幽灵成员的后果不是崩溃而是<b>数字骗人</b>：
    /// 托盘写着「组 1（3 张）」，点「关闭这组」只关掉剩下的两张。
    /// </summary>
    [Fact]
    public void AClosedPinIsTornOutOfEveryGroupSoCountsNeverLie()
    {
        var pins = SourceGate.ReadRepoFile(Roster);
        var unregister = SourceGate.Between(pins, "internal static void Unregister(CaptureOverlayWindow pin)",
            "public static bool UndoRedoAtFocusedPin");

        Assert.Contains("Pins.Remove(pin)", unregister);           // 反空转
        Assert.Contains("Detach(pin);", unregister);
        Assert.Contains("Prune();", unregister);
        // 摘成员与删空组各只有一处实现，否则"哪条路要记得 prune"又会变成口头约定
        Assert.Equal(1, SourceGate.Count(pins, "private static void Detach("));
        Assert.Equal(1, SourceGate.Count(pins, "private static void Prune()"));
        // 整批关掉时也一样：名册清了，组账必须一起清
        var closeAll = SourceGate.Between(pins, "public static void CloseAll()", "internal static void Unregister");
        Assert.Contains("GroupRoster.Clear();", closeAll);
    }

    /// <summary>
    /// 命令号的编解码只有 Core 一份，UI 两侧各自调用它：
    /// 名册不自己算号（否则托盘与名册对同一串数字各有解释），主窗不自己认号。
    /// </summary>
    [Fact]
    public void OnlyCoreDecodesTheGroupCommandNumbers()
    {
        var pins = SourceGate.ReadRepoFile(Roster);
        var main = SourceGate.ReadRepoFile(Main);
        var policy = SourceGate.ReadRepoFile(Policy);

        Assert.Contains("public static int TagOf(int serial, Action action)", policy);
        Assert.Contains("public static bool IsGroupTag(int tag)", policy);
        // 主窗：只调用，不自己算
        Assert.Contains("PinGrouping.TagOf(group.Serial, PinGrouping.Action.Show)", main);
        Assert.Contains("PinGrouping.TagOf(group.Serial, PinGrouping.Action.Through)", main);
        Assert.Contains("PinGrouping.TagOf(group.Serial, PinGrouping.Action.Close)", main);
        Assert.Contains("case int groupTag when PinGrouping.IsGroupTag(groupTag):", main);
        // 名册：拿到号直接交回 Core 解码，绝不出现第二处算术
        Assert.DoesNotContain("TagBase +", pins);
        var run = SourceGate.Between(pins, "public static void RunGroupCommand(int tag)", "private static void CloseGroup(");
        Assert.Contains("PinGrouping.IsGroupTag(tag)", run);
        Assert.Contains("PinGrouping.SerialOf(tag)", run);
        Assert.Contains("PinGrouping.ActionOf(tag)", run);
    }

    /// <summary>组命令只许动这一组的成员。<c>ApplyHiddenTo(Pins…)</c> 混进来就是"点一组、动了全体"。</summary>
    [Fact]
    public void AGroupCommandNeverTouchesPinsOutsideThatGroup()
    {
        var pins = SourceGate.ReadRepoFile(Roster);
        var run = SourceGate.Between(pins, "public static void RunGroupCommand(int tag)", "private static void CloseGroup(");

        Assert.Contains("ApplyHiddenTo(group.Members,", run);       // 反空转：动作确实落在成员上
        Assert.Contains("ApplyThroughTo(group.Members,", run);
        Assert.Contains("CloseGroup(group);", run);
        Assert.DoesNotContain("ApplyHiddenTo(Pins", run);
        Assert.DoesNotContain("ApplyThroughTo(Pins", run);
        Assert.DoesNotContain("CloseAll()", run);

        // 而"全部"那三条不许反过来被组命令改写：范围只由调用点决定
        var toggleHidden = SourceGate.Between(pins, "public static void ToggleHidden()", "public static void ToggleClickThrough()");
        Assert.Contains("ApplyHiddenTo(Pins, PinGrouping.NextHidden(AreHidden), \"全部贴图\")", toggleHidden);
    }

    /// <summary>
    /// 托盘发号是"渲染顺序 + 起始号"。加了子菜单之后<b>父行也必须占号位</b>：
    /// 跳过父行会让它后面每一行整体挪一格——点「收起这组」执行成「关闭这组」，而关闭是不可逆的那一个。
    /// </summary>
    [Fact]
    public void ASubmenuRowStillConsumesAnIdSoNothingShiftsByOne()
    {
        var host = SourceGate.ReadRepoFile(Host);
        var append = SourceGate.Between(host, "private void AppendCommands(IntPtr parent", "public void Dispose()");

        Assert.Contains("_hostCommands.Add(item);", append);       // 反空转：确实是那份登记
        // 占号必须在分流之前（先判 Children 再 Add 就会把父行漏掉）
        Assert.True(append.IndexOf("_hostCommands.Add(item);", StringComparison.Ordinal)
                    < append.IndexOf("item.Children", StringComparison.Ordinal),
                    "父行必须与叶子行一样先占号位，否则子菜单之后每一行的命令号整体错位一格");
        // 发号只许有一处：宿主那段循环若被复制回 ShowContextMenu，两处就会各自数各自的顺序
        Assert.Equal(1, SourceGate.Count(host, "HostCommandBase + index"));
        Assert.Contains("AppendMenuW(parent, MF_POPUP, sub, item.Label);", append);
    }

    /// <summary>
    /// 归组入口长在贴图自己的条子上（那里"哪一张"没有歧义），整排按名册生成：
    /// 组号是文字（画成图形就没人认得出是第几组），但不许出现写死的组名。
    /// </summary>
    [Fact]
    public void TheGroupEntryIsBuiltFromTheRosterAndOnlyAppearsOnPins()
    {
        var bar = SourceGate.ReadRepoFile(Bar);
        var pickers = SourceGate.ReadRepoFile(Pickers);
        var insidePinned = SourceGate.Between(bar, "_groupButton = IconButton(GroupIcon()", "var cancel = IconButton");

        Assert.Contains("_groupButton.Click += (_, _) => ToggleGroupPicker(_groupButton);", insidePinned);
        Assert.Contains("GroupButtonText()", bar);                  // 那颗的说明按"在哪一组、组里几张"现写
        Assert.Contains("ToolTipService.SetToolTip(_groupButton, GroupButtonText());", bar);   // 并组后立刻跟着变
        Assert.Contains("foreach (var group in PinManager.Groups)", pickers);
        Assert.Contains("PinManager.NewGroup(this)", pickers);
        Assert.Contains("PinManager.JoinGroup(this, target.Serial)", pickers);
        Assert.Contains("PinManager.LeaveGroup(this)", pickers);
        Assert.DoesNotContain("\"组 1\"", pickers);                 // 写死一颗＝加一组就少一颗
        // 点开与收起都必须叫上重摆（批次 WN：那扇条子窗不会自己跟着长高）
        Assert.Contains("ShowPicker(anchor, row);", SourceGate.MethodBody(pickers, "private void ShowGroupPicker("));
    }

    /// <summary>
    /// 一张最多属一组：并组前必须先摘。漏掉这一步，"整组隐藏"会对同一张窗重复执行，
    /// 而组数量与张数都不再等于屏幕上的事实。
    /// </summary>
    [Fact]
    public void JoiningAGroupAlwaysDetachesFirst()
    {
        var pins = SourceGate.ReadRepoFile(Roster);

        var create = SourceGate.Between(pins, "public static void NewGroup(CaptureOverlayWindow pin)", "public static void JoinGroup(");
        var join = SourceGate.Between(pins, "public static void JoinGroup(", "public static void LeaveGroup(");
        Assert.Contains("Detach(pin);", create);
        Assert.Contains("Detach(pin);", join);
        Assert.Contains("target.Members.Add(pin);", join);
        // 那一组已经不在（右键之后被关掉）也要回执，不能静默：静默＝用户以为并进去了
        Assert.Contains("TrayReporter.Report(\"贴图组\", \"那一组已经不在了\"", join);
    }

    /// <summary>名册里的"隐藏"只有两个写点：窗自己收（HidePin）与窗自己回（Present）。</summary>
    [Fact]
    public void OnlyTheWindowItselfRecordsHidden()
    {
        var pin = SourceGate.ReadRepoFile(PinWindow);
        Assert.Equal(1, SourceGate.Count(pin, "IsHidden = true;"));
        Assert.Equal(1, SourceGate.Count(pin, "IsHidden = false;"));
        var name = SourceGate.ReadRepoFile(Roster);
        Assert.DoesNotContain("IsHidden =", name);                  // 名册只读不写
        var policy = SourceGate.ReadRepoFile(Policy);
        Assert.Contains("flags.Count > 0", policy);                 // 空集那一臂是判据的一部分，不是修饰
    }
}
