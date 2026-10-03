#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 闹钟的<b>接线</b>闸门（批次 RU 起，扫源码不跑界面；批次 VO 加了"表面那一排"与"动作表只有一份"）。
/// <para>
/// 判据本身由 <see cref="AlarmPolicyTests"/> 用注入时刻钉死了；这里守的是只有真机才看得见、
/// 但一写错就变成"改了没反应 / 重启又回去 / 在错误的日子响 / 每秒重画一遍"的那几条链路约束：
/// <b>真值只有一份</b>（住在时钟组件实例里，菜单与表面都只是入口）、<b>改动必须落盘</b>、
/// <b>位序与时刻换算只许在 Core 一处</b>、<b>"待确认"必须有出口</b>、
/// <b>一条闹钟的动作表只许有一份</b>（批次 VO：表面与右键共用 <see cref="AlarmMenu"/>）。
/// </para>
/// </summary>
public sealed class AlarmWiringGateTests
{
    private const string Core = "src/StarMark.Core/Widgets/AlarmPolicy.cs";
    private const string Storage = "src/StarMark.Core/Widgets/WidgetStorage.cs";
    private const string Manager = "src/StarMark.UI/Services/WidgetManager.PerWidget.cs";
    private const string Factory = "src/StarMark.UI/Services/WidgetContentFactory.cs";
    private const string Vm = "src/StarMark.UI/ViewModels/ClockWidgetViewModel.cs";
    private const string Widget = "src/StarMark.UI/Views/ClockWidget.xaml.cs";
    private const string WidgetXaml = "src/StarMark.UI/Views/ClockWidget.xaml";
    private const string Table = "src/StarMark.UI/Views/AlarmMenu.cs";
    private const string Menu = "src/StarMark.UI/Views/WidgetWindow.Alarms.cs";
    private const string Host = "src/StarMark.UI/Views/WidgetWindow.xaml.cs";
    private const string HostMenu = "src/StarMark.UI/Views/WidgetWindow.Menu.cs";

    /// <summary>XAML 里的注释与代码注释一样会冒充锚点（"把那一行注释掉"是最便宜的假装修，规则 14）。</summary>
    private static string Markup(string relativePath)
        => System.Text.RegularExpressions.Regex.Replace(
            SourceGate.ReadRepoFile(relativePath), "<!--.*?-->", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>
    /// 一整套闹钟只认一个主人：工厂把配置与管理器交给时钟组件，宿主只 <c>as</c> 一次接口，
    /// 两个入口都只通过那张表问与改。<b>入口不许绕过组件直接写存档</b>——那样屏幕上显示的还是组件手里那份，
    /// 症状正是"改了没反应"。
    /// </summary>
    [Fact]
    public void AlarmsHaveExactlyOneOwner_AndBothEntriesAreOnlySecondEntries()
    {
        var factory = SourceGate.ReadRepoFile(Factory);
        Assert.Contains("factory.Register(WidgetKind.Clock, w => new ClockWidget(w.Config, w.Manager));", factory);
        Assert.DoesNotContain("new ClockWidget()", factory);          // 无参构造＝拿不到自己那份闹钟，改动就没处落盘

        var host = SourceGate.ReadRepoFile(Host);
        Assert.Equal(1, SourceGate.Count(host, "as IAlarmEditor"));   // 只有"内容重建"那一处认这个接口
        var call = SourceGate.MethodBody(SourceGate.ReadRepoPartials(HostMenu), "private void PopulateMenu(MenuFlyout menu)");
        Assert.Contains("if (_alarms is { } alarms) BuildAlarmSection(menu, alarms);", call);

        // 两个<b>入口</b>都不许绕过组件直接写存档（落盘只有一条路，在组件那一步）
        foreach (var entry in new[] { Menu, Table })
        {
            var text = SourceGate.ReadRepoFile(entry);
            Assert.DoesNotContain("SaveAlarmsAsync", text);
            Assert.DoesNotContain("inst.Alarms", text);
            Assert.DoesNotContain("GetRequiredService", text);         // 不另开一份数据源
        }

        // 每颗开关都钉<b>整条调用式（含实参）</b>：只钉"调用了 SetEnabled"的话，把极性写反照样全绿，
        // 而真机上的症状是"我明明把它关了，第二天照旧响"（坑表 #177 那一族）。
        var table = SourceGate.ReadRepoFile(Table);
        Assert.Contains("alarms.SetEnabled(item.Id, onOff.IsChecked);", table);
        Assert.Contains("check.IsChecked ? AlarmPolicy.TurnOn(item.Days, day) : AlarmPolicy.TurnOff(item.Days, day)", table);
        Assert.Contains("alarms.Remove(item.Id);", table);
        Assert.Contains("alarms.TestFire(item.Id);", table);
        Assert.Contains("alarms.Add(minute, label, AlarmDays.Daily);", table);
        Assert.DoesNotContain("AlarmDays.None);", table);              // 新加的不能默认"单次"：那等于大部分人要每天响、却每次都只响一回
        Assert.Contains("alarms.SetTime(item.Id, minute, label);", table);
        Assert.Contains("alarms.ConfirmPending();", SourceGate.ReadRepoFile(Menu));
    }

    /// <summary>
    /// 一条闹钟的动作表<b>只许有一份</b>（批次 VO 立的口径）：表面上那一排与右键菜单里那一子排必须点得出同样的动作。
    /// 两处各摆一份的话，第一次加动作忘了改另一处就成了"从表面上删不掉、从右键里却删得掉"，
    /// 而这类分岔只有真机看得见（记忆 ⑧／坑表 #189 那一族）。
    /// </summary>
    [Fact]
    public void TheRowActionTableLivesOnce_AndBothEntriesAskForIt()
    {
        var table = SourceGate.ReadRepoFile(Table);
        var menu = SourceGate.ReadRepoFile(Menu);
        var widget = SourceGate.Code(SourceGate.ReadRepoFile(Widget));

        // 建菜单项那几句只出现在动作表里（开关那颗＋按星期勾那一排＝两颗 Toggle，四个快捷档＝七颗 Radio 里的一档一份）。
        // 数"实例化"这一句而不是数名字：入口引用 AlarmMenu 时也会出现同一个词。
        Assert.Equal(2, SourceGate.Count(table, "new ToggleMenuFlyoutItem"));
        Assert.Equal(1, SourceGate.Count(table, "new RadioMenuFlyoutItem"));
        Assert.Equal(1, SourceGate.Count(table, "new MenuFlyoutSubItem { Text = $\"重复"));
        foreach (var entry in new[] { menu, widget })
            foreach (var built in new[] { "new ToggleMenuFlyoutItem", "new RadioMenuFlyoutItem" })
                Assert.DoesNotContain(built, entry);

        // 两个入口都问同一张表
        Assert.Contains("foreach (var each in AlarmMenu.RowItems(alarms, item, this)) row.Items.Add(each);", menu);
        Assert.Contains("AlarmMenu.TitleOf(item)", menu);
        Assert.Contains("AlarmMenu.ShowRow(this, item, (FrameworkElement)sender, _manager.WindowOf(_instanceId));", widget);
        // 加那一条的输入框也只有一份：两个入口都调 AlarmMenu.AddAsync（各写一份解析迟早对不上）
        Assert.Contains("_ = AlarmMenu.AddAsync(alarms, this);", menu);
        Assert.Contains("=> _ = AlarmMenu.AddAsync(this, _manager.WindowOf(_instanceId));", widget);
        Assert.Equal(2, SourceGate.Count(menu + widget + table, "AlarmMenu.AddAsync"));
    }

    /// <summary>
    /// 时钟表面那一排：内容来自 Core 的行，重画只由"签名变了"触发。
    /// <b>每秒那趟不许碰重画</b>——时钟每秒刷整张表，按那一趟重建控件就是把"改一行字"升级成"一次布局重排"，
    /// 代价正好由常驻桌面的小窗付（判据住在 Core，<see cref="AlarmPolicyTests"/> 钉"时间走一秒不换签名"）。
    /// </summary>
    [Fact]
    public void TheFaceShowsTheAlarms_AndRedrawsOnlyWhenTheContentChanged()
    {
        var widget = SourceGate.ReadRepoFile(Widget);
        var vm = SourceGate.Code(SourceGate.ReadRepoFile(Vm));

        Assert.Contains("ViewModel.AlarmFaceChanged += RebuildAlarmFace;", widget);
        var rebuild = SourceGate.MethodBody(widget, "private void RebuildAlarmFace()");
        Assert.Contains("foreach (var row in ViewModel.FaceRows) AlarmList.Children.Add(BuildAlarmRow(row, size));", rebuild);
        Assert.Contains("AlarmPolicy.MaxItems", rebuild);              // 上限要说出来并让那颗加号按下无效，不能悄悄加不上

        // 每秒那趟只判到点，不重画
        var tick = SourceGate.MethodBody(widget, "private async void Timer_Tick(DispatcherQueueTimer sender, object args)");
        Assert.DoesNotContain("RebuildAlarmFace", tick);
        Assert.DoesNotContain("RebuildAlarmFace", SourceGate.MethodBody(widget, "private async System.Threading.Tasks.Task PersistIfNotifiedAsync()"));

        // ViewModel 这一侧：签名比较只认 Core，变了才发事件
        var face = SourceGate.MethodBody(vm, "private void RebuildFace(DateTimeOffset now)");
        Assert.Contains("var signature = AlarmPolicy.FaceSignature(rows);", face);
        Assert.Contains("if (signature == FaceSignature) return;", face);
        Assert.Contains("AlarmFaceChanged?.Invoke();", face);
        Assert.Contains("RebuildFace(now);", SourceGate.MethodBody(vm, "private int RefreshAlarms(DateTimeOffset now)"));
        // 从存档灌入那一趟也要重画：只 Add/Remove 才刷新的话，重启后表面是空的
        Assert.Contains("RebuildFace(DateTimeOffset.Now);", SourceGate.MethodBody(vm, "public void Load(IEnumerable<AlarmItem>? items)"));
    }

    /// <summary>
    /// 表面那一排在<b>代码里</b>建：行 id 直接进被点那个元素的 <c>Tag</c>，认不出 id 不许静默。
    /// 批次 UN 刚在待办那颗颜色点上查清同一形状——XAML 声明式 flyout 套进模板元素后 <c>x:Bind</c> 带不出 id，
    /// 反查天天静默 return，全绿而功能坏。这里连"反查位置"那条路一起钉掉。
    /// </summary>
    [Fact]
    public void TheFaceTakesTheIdFromTheClickedElement_NotFromWhereItSits()
    {
        var widget = SourceGate.Code(SourceGate.ReadRepoFile(Widget));
        Assert.Equal(2, SourceGate.Count(widget, "Tag = row.Id,"));     // 那颗点与那一行字各自带上 id
        Assert.Contains("if (TryAlarmId(sender, out var id)) ToggleEnabled(id);", widget);
        Assert.Contains("if (sender is FrameworkElement { Tag: long v }) { id = v; return true; }", widget);
        // Warn 要钉在<b>认 id 那一个方法体里</b>：整档"出现过 StarLog.Warn"拦不住（台架 V9：摘掉这一句仍旧全绿，
        // 因为旁边那条链上也有一句 Warn＝坑表 #247 那一族"return 太早不在射程内"）。
        Assert.Contains("StarLog.Warn",
                        SourceGate.MethodBody(widget, "private static bool TryAlarmId(object sender, out long id)"));
        Assert.DoesNotContain(".Items.IndexOf(", widget);
        // 那颗点只交 id：<b>不许把界面上的快照回写成布尔</b>——同一条目有两个入口能改它，
        // 拿快照回写的症状是"按下去没反应，再按一下才关"。
        Assert.Contains("IsChecked = row.Enabled,", widget);
        Assert.DoesNotContain("SetEnabled", SourceGate.MethodBody(widget, "private void AlarmToggle_Click(object sender, RoutedEventArgs e)"));
        Assert.Contains("public void ToggleEnabled(long id) => Apply(() => ViewModel.ToggleEnabled(id));", widget);

        // 表面用的是 XAML 里那一个容器，且<b>没有声明式 flyout</b>（UN 那一族的正解：菜单在代码里现建）
        var xaml = Markup(WidgetXaml);
        Assert.Contains("<StackPanel x:Name=\"AlarmList\"", xaml);
        Assert.DoesNotContain("Flyout", xaml);
    }

    /// <summary>位序（哪天算周几）与时刻换算只许住在 Core：UI 里出现一次移位，就是第二份真值。</summary>
    [Theory]
    [InlineData(Menu, "1 << ")]
    [InlineData(Menu, "AddMinutes")]
    [InlineData(Menu, "TimeSpan.From")]
    [InlineData(Menu, "HasFlag")]
    [InlineData(Table, "1 << ")]
    [InlineData(Table, "AddMinutes")]
    [InlineData(Table, "TimeSpan.From")]
    [InlineData(Table, "HasFlag")]
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
        // 批次 VO：出口要在桌面上就近给一个（P-54），措辞与菜单那颗说同一件事
        Assert.Contains("BuildTextButton($\"停止待确认（{PendingCount}）\", size, ConfirmPending,", widget);
    }

    /// <summary>
    /// 「试响一次」不许动到点记录：它存在的唯一理由是"不用等一个真实的早晨也能确认那一发真的看得见"。
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
        Assert.Contains("alarms.TestFire(item.Id);", SourceGate.ReadRepoFile(Table));
    }

    /// <summary>
    /// 用户裁决"本程序运行时才提醒"要<b>就地写明</b>在入口上：不写，这一节读起来就像系统闹钟
    /// （"我关了电脑它还会响吗"只有一个答案：不会，那就必须让他第一眼看出来）。
    /// <para>批次 VO 之后桌面上那一排也成了入口，这句话就<b>两处都要有</b>：只在菜单里写，
    /// 从表面上加的那一条读起来仍是"没写清楚的闹钟"。</para>
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

        var face = SourceGate.ReadRepoFile(Widget);
        Assert.Equal(2, SourceGate.Count(face, "本程序运行时才提醒"));   // 停止待确认那颗 ＋ 加一条那颗
        Assert.Contains("确认掉所有还在等的轮次（本程序运行时才提醒）", face);
        Assert.Contains("加一条，例如「7:30 起床」（本程序运行时才提醒）", face);
        // 提示卡也要把人指到最近的那一个出口，而不是支去右键菜单
        Assert.Contains("点时钟上那一排的「停止待确认」就收", SourceGate.ReadRepoFile("src/StarMark.UI/Views/ClockWidget.xaml.cs"));
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
