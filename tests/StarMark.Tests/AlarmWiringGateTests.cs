#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 闹钟的<b>接线</b>闸门（批次 RU，扫源码不跑界面）。
/// <para>
/// 判据本身由 <see cref="AlarmPolicyTests"/> 用注入时刻钉死了；这里守的是只有真机才看得见、
/// 但一写错就变成"改了没反应 / 重启又回去 / 在错误的日子响"的那几条链路约束：
/// <b>真值只有一份</b>（住在时钟组件实例里，菜单只是第二个入口）、<b>改动必须落盘</b>、
/// <b>位序与时刻换算只许在 Core 一处</b>、<b>"待确认"必须有出口</b>。
/// </para>
/// </summary>
public sealed class AlarmWiringGateTests
{
    private const string Core = "src/StarMark.Core/Widgets/AlarmPolicy.cs";
    private const string Storage = "src/StarMark.Core/Widgets/WidgetStorage.cs";
    private const string Manager = "src/StarMark.UI/Services/WidgetManager.PerWidget.cs";
    private const string Factory = "src/StarMark.UI/Services/WidgetContentFactory.cs";
    private const string Widget = "src/StarMark.UI/Views/ClockWidget.xaml.cs";
    private const string Menu = "src/StarMark.UI/Views/WidgetWindow.Alarms.cs";
    private const string Host = "src/StarMark.UI/Views/WidgetWindow.xaml.cs";
    private const string HostMenu = "src/StarMark.UI/Views/WidgetWindow.Menu.cs";

    /// <summary>
    /// 一整套闹钟只认一个主人：工厂把配置与管理器交给时钟组件，宿主只 <c>as</c> 一次接口，
    /// 菜单只通过那张表问与改。<b>菜单不许绕过组件直接写存档</b>——那样屏幕上显示的还是组件手里那份，
    /// 症状正是"改了没反应"。
    /// </summary>
    [Fact]
    public void AlarmsHaveExactlyOneOwner_AndTheMenuIsOnlyASecondEntry()
    {
        var factory = SourceGate.ReadRepoFile(Factory);
        Assert.Contains("factory.Register(WidgetKind.Clock, w => new ClockWidget(w.Config, w.Manager));", factory);
        Assert.DoesNotContain("new ClockWidget()", factory);          // 无参构造＝拿不到自己那份闹钟，改动就没处落盘

        var host = SourceGate.ReadRepoFile(Host);
        Assert.Equal(1, SourceGate.Count(host, "as IAlarmEditor"));   // 只有"内容重建"那一处认这个接口
        var call = SourceGate.MethodBody(SourceGate.ReadRepoPartials(HostMenu), "private void PopulateMenu(MenuFlyout menu)");
        Assert.Contains("if (_alarms is { } alarms) BuildAlarmSection(menu, alarms);", call);

        var menu = SourceGate.ReadRepoFile(Menu);
        Assert.DoesNotContain("SaveAlarmsAsync", menu);                // 落盘只有一条路（在组件那一步）
        Assert.DoesNotContain("inst.Alarms", menu);
        Assert.DoesNotContain("GetRequiredService", menu);             // 不另开一份数据源
        // 每颗开关都钉<b>整条调用式（含实参）</b>：只钉"调用了 SetEnabled"的话，把极性写反照样全绿，
        // 而真机上的症状是"我明明把它关了，第二天照旧响"（坑表 #177 那一族）。
        Assert.Contains("alarms.SetEnabled(item.Id, onOff.IsChecked);", menu);
        Assert.Contains("check.IsChecked ? AlarmPolicy.TurnOn(item.Days, day) : AlarmPolicy.TurnOff(item.Days, day)", menu);
        Assert.Contains("alarms.Remove(item.Id);", menu);
        Assert.Contains("alarms.TestFire(item.Id);", menu);
        Assert.Contains("alarms.ConfirmPending();", menu);
        Assert.Contains("alarms.Add(minute, label, AlarmDays.Daily);", menu);
        Assert.DoesNotContain("AlarmDays.None);", menu);               // 新加的不能默认"单次"：那等于大部分人要每天响、却每次都只响一回
    }

    /// <summary>位序（哪天算周几）与时刻换算只许住在 Core：UI 里出现一次移位，就是第二份真值。</summary>
    [Theory]
    [InlineData(Menu, "1 << ")]
    [InlineData(Menu, "AddMinutes")]
    [InlineData(Menu, "TimeSpan.From")]
    [InlineData(Menu, "HasFlag")]
    [InlineData(Widget, "1 << ")]
    [InlineData(Widget, "DayOfWeek")]
    [InlineData(Widget, "AddMinutes")]
    public void TheBitOrderAndTheClockMathStayInCore(string file, string forbidden)
        => Assert.DoesNotContain(forbidden, SourceGate.ReadRepoFile(file));

    /// <summary>
    /// Core 自己也不许"把坏值夹到最近的一端"：越界的分钟数只能得到"这条永远不响 + 界面上写着时间无效"，
    /// 不能悄悄变成 00:00 在半夜响一次（同 <c>EyeRestPolicy.ClampNotice</c> 那条判据）。
    /// </summary>
    [Fact]
    public void BrokenStoredMinute_IsNotClampedToTheNearestEdge()
    {
        var core = SourceGate.ReadRepoFile(Core);
        Assert.DoesNotContain("Math.Clamp", core);
        Assert.Contains("public static bool HasValidMinute(int minuteOfDay) => minuteOfDay is >= 0 and < 24 * 60;", core);
        // 每一次算时刻之前都要先问这一句（漏一处的话，坏值就会算出一个"看起来合法"的钟点）
        Assert.Equal(2, SourceGate.Count(core, "if (!HasValidMinute(item.MinuteOfDay)) return null;"));
    }

    /// <summary>
    /// 到点标记必须落盘，而且要<b>在UI 线程判、判完才写盘</b>：
    /// 不落盘＝重启后同一轮再弹一次；把判定丢到后台线程＝在碰界面属性（那是必炸的）。
    /// </summary>
    [Fact]
    public void NotifiedRoundsArePersisted_OnTheUiThread()
    {
        var widget = SourceGate.ReadRepoFile(Widget);
        var persist = SourceGate.MethodBody(widget, "private async System.Threading.Tasks.Task PersistIfNotifiedAsync()");
        Assert.Contains("if (ViewModel.Update() > 0) await PersistAsync();", persist);
        Assert.DoesNotContain("Task.Run", widget);                    // 判定要写界面属性，不能挪到后台
        Assert.Contains("_manager.SaveAlarmsAsync(_instanceId, ViewModel.ToPersisted());", widget);
        // 启动校准那一趟也要走同一条路：只刷新不落盘＝"开机正好弹了一发，重启还会再弹"
        var running = SourceGate.MethodBody(widget, "public async void UpdateRunning(bool windowVisible)");
        Assert.Contains("await PersistIfNotifiedAsync();", running);
    }

    /// <summary>
    /// "待确认"必须有出口，而且这条链上<b>只有 Core 那一个过期时限</b>：
    /// UI 自己再判一次"多久算过期"迟早与 Core 对不上（一边收了另一边还挂着）。
    /// </summary>
    [Fact]
    public void ThePendingRoundHasAnExit_AndItsExpiryIsAskedFromCore()
    {
        var menu = SourceGate.ReadRepoFile(Menu);
        Assert.Contains("alarms.ConfirmPending();", menu);
        Assert.Contains("pending > 0 ? $\"闹钟 · {pending} 条待确认\"", menu);   // 标题自己说"有几条在等"

        var widget = SourceGate.ReadRepoFile(Widget);
        Assert.Contains("AlarmPolicy.IsPending(item, DateTimeOffset.Now)", widget);
        Assert.DoesNotContain("FromHours", widget);                    // 别在 UI 里再写一个时限
    }

    /// <summary>
    /// 「试响一次」不许动到点记录：它存在的唯一理由是"不用等一个真实的早晨也能确认气泡弹得出来"。
    /// 顺手把待确认写脏，就会变成"我试了一下，结果今天的闹钟没响"。
    /// </summary>
    [Fact]
    public void TestFire_DoesNotTouchTheRound()
    {
        var vm = SourceGate.ReadRepoFile("src/StarMark.UI/ViewModels/ClockWidgetViewModel.cs");
        var fire = SourceGate.MethodBody(vm, "public void TestFire(long id)");
        Assert.Contains("AlarmReached?.Invoke(item);", fire);
        foreach (var forbidden in new[] { "MarkNotified", "PendingSince", "Confirm", "Enabled =" })
            Assert.DoesNotContain(forbidden, fire);
        Assert.Contains("alarms.TestFire(item.Id);", SourceGate.ReadRepoFile(Menu));
    }

    /// <summary>
    /// 用户裁决"本程序运行时才提醒"要<b>就地写明</b>在入口上：不写，这一节读起来就像系统闹钟
    /// （"我关了电脑它还会响吗"只有一个答案：不会，那就必须让他第一眼看出来）。
    /// </summary>
    [Fact]
    public void TheEntrySaysOutLoudThatItOnlyRingsWhileTheAppRuns()
    {
        var menu = SourceGate.ReadRepoFile(Menu);
        Assert.Equal(3, SourceGate.Count(menu, "本程序运行时才提醒"));
        // 三条状态（待确认 / 有下一次 / 还没有闹钟）都得带上这句：只在其中一条上写＝另外两条读起来仍是"系统闹钟"
        Assert.Contains("按下面「停止待确认」就收（本程序运行时才提醒）", menu);
        Assert.Contains("（本程序运行时才提醒）\"", menu);
        Assert.Contains("加一条，例如「7:30 起床」（本程序运行时才提醒）", menu);
    }

    /// <summary>存档字段本身：按实例存、整表替换（与倒计时同一条形状，"两个时钟各一套"要成立）。</summary>
    [Fact]
    public void AlarmsArePersistedPerInstance_LikeCountdowns()
    {
        var storage = SourceGate.ReadRepoPartials(Storage);
        Assert.Contains("public List<AlarmItem>? Alarms { get; set; }", storage);   // 可空＝"从没配过"与"配了又删光"分得开

        // 这一批的落盘出口是<b>表达式体 + 多行 lambda</b>（=> OnUiAsync(() => { ... })），
        // MethodBody 对这种形状只会切到"第一条以分号结尾的行"就收，切出来的体里没有 inst.Alarms——
        // 所以这里数<b>整档</b>的出现次数（这些字面在本文件里各只有一处，数整档不会数错，反而比假体可靠）。
        var manager = SourceGate.ReadRepoFile(Manager);
        Assert.Contains("public Task SaveAlarmsAsync(string instanceId, IReadOnlyList<AlarmItem> alarms)", manager);
        Assert.Equal(1, SourceGate.Count(manager, "inst.Alarms = alarms.ToList();"));   // 整表替换，不 append（否则删不掉）
        Assert.Equal(1, SourceGate.Count(manager, "inst.Alarms"));
    }
}
