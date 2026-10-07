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
        // 批次 WI-37-C：那句"重复：X"进了表（代码只剩键名），所以这里钉的是"这一颗子菜单的标题问的是 alarm.repeat.title"。
        Assert.Equal(1, SourceGate.Count(table, "new MenuFlyoutSubItem { Text = Locale.Fmt(\"alarm.repeat.title\""));
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
        // 标题自己说"有几条在等"（批次 WI-37-C 起这句话在表里，代码只问键；带条数那颗键是 alarm.section.pending）
        Assert.Contains("Locale.Fmt(\"alarm.section.pending\", pending)", menu);

        var widget = SourceGate.ReadRepoFile(Widget);
        Assert.Contains("AlarmPolicy.IsPending(item, DateTimeOffset.Now)", widget);
        Assert.DoesNotContain("FromHours", widget);                    // 别在 UI 里再写一个时限
        // 批次 VO：出口要在桌面上就近给一个（P-54），措辞与菜单那颗说同一件事——
        //  ⇒ 两扇宿主交的是同一颗键（alarm.stop.button 包着 alarm.stop.label），⛔ 各写一遍字。
        Assert.Contains("BuildTextButton(\"alarm.stop.button\", size, ConfirmPending, \"alarm.face.stopTip\",", widget);
        Assert.Contains("LocMessage.Of(\"alarm.stop.label\"), PendingCount)", widget);
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
    /// <summary>
    /// 用户裁决"本程序运行时才提醒"要<b>就地写明</b>在入口上：不写，这一节读起来就像系统闹钟
    /// （"我关了电脑它还会响吗"只有一个答案：不会，那就必须让他第一眼看出来）。
    /// <para>批次 VO 之后桌面上那一排也成了入口，这句话就<b>两处都要有</b>：只在菜单里写，
    /// 从表面上加的那一条读起来仍是"没写清楚的闹钟"。</para>
    /// <para>批次 WI-37-C 起这五句话住在<b>表</b>里（代码只剩键名）⇒ 尺子跟着搬到主人那一侧，
    /// 并且⛔ 不许这串字再回到代码里（下面那两条 <c>DoesNotContain</c> 就是防"表挂了、代码还留一份"的双写）。
    /// <b>三列各自钉一句同义话</b>：只钉中文列的话，换语言那一刻这句话就消失了——那正是本条要防的事。</para>
    /// </summary>
    [Fact]
    public void TheEntrySaysOutLoudThatItOnlyRingsWhileTheAppRuns()
    {
        var mustCarry = new[]
        {
            "alarm.status.pending", "alarm.status.due", "alarm.status.empty",   // 三条状态（待确认／有下一次／还没有）
            "alarm.face.addTip", "alarm.face.stopTip",                          // 桌面上那两颗的提示
        };
        foreach (var key in mustCarry)
        {
            Assert.Contains("（本程序运行时才提醒）", Column(key, e => e.Zh), StringComparison.Ordinal);
            Assert.Contains("only rings while it runs", Column(key, e => e.En), StringComparison.Ordinal);
            Assert.Contains("本アプリ起動中のみ", Column(key, e => e.Ja), StringComparison.Ordinal);
        }

        // 接线：五颗键都真被问到了（挂了键却没接线＝屏幕上是一串 ⟨键⟩，与"没写清楚"同一种后果）
        var menu = SourceGate.ReadRepoFile(Menu);
        foreach (var key in new[] { "alarm.status.pending", "alarm.status.due", "alarm.status.empty" })
            Assert.Contains("\"" + key + "\"", menu);
        var widget = SourceGate.ReadRepoFile(Widget);
        Assert.Contains("\"alarm.face.addTip\"", widget);
        Assert.Contains("\"alarm.face.stopTip\"", widget);

        // 代码那两侧⛔ 再留一份字面量（双写的那一份在换语言后就是没人读的副本）
        Assert.DoesNotContain("本程序运行时才提醒", SourceGate.Code(menu));
        Assert.DoesNotContain("本程序运行时才提醒", SourceGate.Code(widget));

        // 提示卡要把人指到最近的那一个出口，而且那个名字与按钮那颗是同一颗键（⛔ 两处各写一遍）
        var card = SourceGate.ReadRepoFile(Widget);
        Assert.Contains("Locale.Fmt(\"alarm.notify.body\", LocMessage.Of(\"alarm.stop.label\")", card);
    }

    private static string Column(string key, Func<StarMark.Abstractions.I18n.UiStrings.Entry, string> side)
        => side(StarMark.Abstractions.I18n.UiStrings.All.First(e => string.Equals(e.Key, key, StringComparison.Ordinal)));

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

    // ────────── 批次 WI-37-C：闹钟整族交键，代码里⛔ 再留一句字 ──────────

    /// <summary>
    /// 一句会贴到屏幕上的中文字面量的形状。只喂<b>抹掉注释后的代码</b>（<see cref="SourceGate.Code"/>）：
    /// 这一族最想在注释里解释"旧写法是 <c>"每天"</c>"，读原文就会把自己的说明弄红（#195）。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex CjkLiteral = new(
        "\"[^\"\\n]*[\\u4e00-\\u9fff][^\"\\n]*\"",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 把<b>日志那一类出口</b>从禁项的射程里摘掉：全工程的 <c>StarLog.*</c> 一律写中文，
    /// 那是给排障的人读的、<b>不上屏幕</b>（与 <c>.Render()</c> 只许出现在日志/通知那一路是同一条口径，批次 WI-37-C）。
    /// <para>⚠ 改窄必须给读数：上面那颗 <see cref="TheLiteralScannerSeesTheBadShape_andIgnoresComments"/>
    /// 钉"尺子本身认得出违规"，下面 <see cref="LogsAreNotTheScreen"/> 钉"摘掉的只有日志这一族"——
    /// 少了这两条，"我只摘日志"就会变成"什么都能摘"。</para>
    /// </summary>
    private static string OffScreen(string code)
        => string.Join("\n", code.Split('\n').Where(l => !l.Contains("StarLog.", StringComparison.Ordinal)));

    /// <summary>摘掉的射程只到 <c>StarLog.*</c>：一颗贴着屏幕的属性名不许被这条顺手放过。</summary>
    [Fact]
    public void LogsAreNotTheScreen()
    {
        const string sample = "StarLog.Error(\"加闹钟失败\", ex);\nText = \"加闹钟失败\";\n";
        var kept = OffScreen(SourceGate.Code(sample));
        Assert.Empty(CjkLiteral.Matches(OffScreen(SourceGate.Code("StarLog.Error(\"加闹钟失败\", ex);\n"))));
        Assert.Single(CjkLiteral.Matches(kept));
        Assert.Equal("\"加闹钟失败\"", CjkLiteral.Match(kept).Value, System.StringComparer.Ordinal);
    }

    /// <summary>
    /// 正面凭据（#161）：上面那颗禁项今天能红，靠的是这把尺子<b>认得出违规</b>——
    /// 先证明"代码里的一句"算违规、"注释里的一句"不算，否则零命中只是尺子失效。
    /// </summary>
    [Fact]
    public void TheLiteralScannerSeesTheBadShape_andIgnoresComments()
    {
        const string sample = "// 旧写法是把档位写成 \"每天\"，注释里出现不算违规\n"
            + "var preset = \"每天\";\n";
        var code = SourceGate.Code(sample);
        Assert.Equal(2, CjkLiteral.Matches(sample).Count);      // 读原文会连注释一起数（没人敢解释旧写法）
        Assert.Single(CjkLiteral.Matches(code));
        Assert.Equal("\"每天\"", CjkLiteral.Match(code).Value, System.StringComparer.Ordinal);
    }

    /// <summary>
    /// 两个只装闹钟的文件里<b>零句中文字面量</b>（用户真机报的第一条："所有组件名称未国际化，组件内容和右键菜单未国际化"）。
    /// <para>⛔ 整棵 <c>src/StarMark.UI</c> 一起数：那会把还没轮到那一刀的文件（快照页、AI 那一栏＝账上 WI-40/41）
    /// 也算进来，红成噪音，然后被人整颗关掉（P-169 的按页推进口径）。加一颗文件＝那一族的字必须同批进表。</para>
    /// </summary>
    [Theory]
    [InlineData(Table)]
    [InlineData(Menu)]
    public void TheAlarmHostsHoldNoChineseLiterals(string relativePath)
    {
        var code = OffScreen(SourceGate.Code(SourceGate.ReadRepoFile(relativePath)));
        var left = CjkLiteral.Matches(code).Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Value).ToArray();
        Assert.True(left.Length == 0,
            $"{relativePath} 的代码里还剩 {left.Length} 句中文字面量（换语言时它们停在中文，屏幕上就是半中半英）：\n"
            + string.Join("\n", left));
    }

    /// <summary>
    /// 时钟组件那一档是<b>混住</b>的（护眼、秒表、休息都在同一份文件），所以那一侧只收闹钟那几颗方法体：
    /// ⛔ 整档扫会把护眼那一族（WI-37-B 已交键、但同一档里还有别的中文）误算成这一刀的欠账。
    /// <para>"读不到方法体"单独钉一条：<see cref="SourceGate.MethodBody"/> 对表达式体只会切到第一个分号，
    /// 切出空串时禁项天然零命中＝假绿（#161 那一族的反向）。</para>
    /// </summary>
    [Theory]
    [InlineData("private void RebuildAlarmFace()")]
    [InlineData("private FrameworkElement BuildAlarmRow(")]
    [InlineData("private Button BuildTextButton(")]
    [InlineData("private static void OnAlarmReached(")]
    public void TheAlarmBodiesOnTheFaceHoldNoChineseLiterals(string signature)
    {
        var body = OffScreen(SourceGate.MethodBody(SourceGate.Code(SourceGate.ReadRepoFile(Widget)), signature));
        Assert.False(string.IsNullOrWhiteSpace(body), $"{signature}：读不到方法体，这一格是空判（读不到不等于通过）");
        var left = CjkLiteral.Matches(body).Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Value).ToArray();
        Assert.True(left.Length == 0, $"{signature} 里还剩 {left.Length} 句中文字面量：\n{string.Join("\n", left)}");
    }

    /// <summary>
    /// <c>RepeatPresets</c> 是 <c>static readonly</c>：中文字一旦进去，就在<b>类首次被碰到的那一刻</b>把语言冻住
    /// （WI-39 那颗 <c>ByTag</c> 化石同一形状，而屏幕上看不出来）。所以这一排四颗只许是键。
    /// <para>四这一颗数不写死在断言里而是从数组自己读——它哪天变成五档，这一格不许继续绿。</para>
    /// </summary>
    [Fact]
    public void TheRepeatPresetTableHandsFourKeysNotWords()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Table));
        var region = SourceGate.Between(code, "RepeatPresets =", "];");
        var keys = System.Text.RegularExpressions.Regex.Matches(region, @"""(alarm\.[a-zA-Z.]+)""")
            .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(4, keys.Length);                                   // 单次／每天／工作日／周末
        Assert.Empty(CjkLiteral.Matches(region));                       // 那颗数组里一颗字都没有
        Assert.Contains("\"alarm.preset.once\"", region, System.StringComparison.Ordinal);   // 唯一带解释的那一档
        foreach (var key in keys)
            Assert.Contains("new Entry(\"" + key + "\"",
                SourceGate.ReadRepoFile("src/StarMark.Abstractions/I18n/UiStrings.cs"), System.StringComparison.Ordinal);
    }
}
