#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// 一条闹钟的动作表，与"加／改"那两个输入框：<b>时钟表面上那一排与宿主右键菜单共用这一份</b>（批次 VO）。
/// <para>
/// 为什么要抽出来：RU 那时只有右键一个入口，动作表就写死在 <c>WidgetWindow.Alarms.cs</c> 里。
/// 现在表面上也能直接点（开关就地翻、点一行就改），若两处各摆一份菜单，第一次加动作忘了改另一处，
/// 症状就是"从表面上删不掉、从右键里却删得掉"——这类只有真机看得见的分岔，本仓已栽过多次
/// （同一件判据两处各写一份 ⇒ 记忆 ⑧／坑表 #189）。
/// </para>
/// <para>
/// 这里<b>不持有任何状态</b>：所有改动都交给 <see cref="IAlarmEditor"/>，落盘由时钟组件做
/// （同 RU 立下的"真值只有一份，入口是第二个不是第二个引擎"）。
/// </para>
/// </summary>
public static class AlarmMenu
{
    /// <summary>
    /// 重复的四个快捷档（"按星期勾"那一排是同一份旗标的另一种设法，不是第二套判据）。
    /// <para>这里存的是<b>键</b>不是字（批次 WI-37-C）：这颗数组是 <c>static readonly</c>，
    /// 一旦把中文写进去，它就在<b>类首次被碰到的那一刻</b>把语言冻住——菜单每次重建都读到那句旧字，
    /// 屏幕上"档位停在中文"没有任何一处代码能看出来（WI-39 那颗 <c>ByTag</c> 化石 bug 同一个形状）。</para>
    /// <para>只有"单次"这一档带解释：响过一次之后它自己关掉，那是<b>看不见副作用</b>的那类事，
    /// 值得多占一行（他这一轮的裁决口径）；"工作日（周一到周五）"那种括号里的话是同名复述 ⇒ 直接复用星期档那颗键。</para>
    /// </summary>
    private static readonly (string Key, AlarmDays Days)[] RepeatPresets =
    [
        ("alarm.preset.once", AlarmDays.None),
        ("alarm.days.daily", AlarmDays.Daily),
        ("alarm.days.weekdays", AlarmDays.Weekdays),
        ("alarm.days.weekends", AlarmDays.Weekends),
    ];

    /// <summary>
    /// 星期那一排的<b>阅读顺序</b>（周一在前）。位权仍按 <see cref="DayOfWeek"/> 算、并只由
    /// <see cref="AlarmPolicy"/> 换算；这里只是列出来的次序，中文名归 <see cref="DateTimeText.WeekdayShort"/>。
    /// </summary>
    private static readonly DayOfWeek[] DayOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    /// <summary>
    /// 一行闹钟的标题（两个入口同一句措辞）：<c>（已关）07:30 起床 · 每天</c>。
    /// <para>交的是<b>还没取词的那句</b>：右键菜单每次重建、当场渲染一次就对了，而"（已关）"这一前缀
    /// 若在这里就拼成字符串，将来任何常驻宿主拿到的都是定死的语言（坑表 #344）。</para>
    /// </summary>
    public static LocMessage TitleMessage(AlarmItem item)
        => item.Enabled
            ? AlarmPolicy.LineMessage(item)
            : LocMessage.Of("alarm.row.offTitle", AlarmPolicy.LineMessage(item));

    /// <summary>那一行的<b>成品串</b>：只给每次重建的菜单标题用（<see cref="TitleMessage"/> 的同一条路，取词时刻不同）。</summary>
    public static string TitleOf(AlarmItem item) => TitleMessage(item).Render();

    /// <summary>
    /// 这一条的全部动作，按顺序。调用方把它们放进自己的容器：宿主菜单放进 <see cref="MenuFlyoutSubItem"/>、
    /// 表面上那一排放进 <see cref="ShowRow"/> 现建的 <see cref="MenuFlyout"/>。
    /// <para><b>这一路每次点开都重建</b>，所以这里的 <c>Locale.T/Fmt</c> 是正当的取词时刻（批次 WI-37-C 的口径：
    /// 重建那一路当场取词，常驻那一路交键）。⛔ 把这些字改成静态字段＝把语言冻在建表那一刻。</para>
    /// </summary>
    /// <param name="owner">弹输入框时以哪扇窗所在屏幕为基准（组件传自己那扇宿主窗；null 落回主显示器）。</param>
    public static IEnumerable<MenuFlyoutItemBase> RowItems(IAlarmEditor alarms, AlarmItem item, Window? owner)
    {
        var onOff = new ToggleMenuFlyoutItem { Text = Locale.T("alarm.toggle.on"), IsChecked = item.Enabled };
        onOff.Click += (_, _) => alarms.SetEnabled(item.Id, onOff.IsChecked);
        yield return onOff;

        // 实参交出的是 LocMessage：Fmt 里 string.Format 对它调 ToString ⇒ 在这一刻（重建时）才取词，嵌套那句不会停在旧语言。
        var repeat = new MenuFlyoutSubItem { Text = Locale.Fmt("alarm.repeat.title", AlarmPolicy.DaysMessage(item.Days)) };
        foreach (var (key, days) in RepeatPresets)
        {
            var preset = new RadioMenuFlyoutItem { Text = Locale.T(key), IsChecked = days == item.Days };
            var chosen = days;                       // 捕获：档位不靠菜单项位置反推
            preset.Click += (_, _) => alarms.SetDays(item.Id, chosen);
            repeat.Items.Add(preset);
        }
        // 逐天勾：四个快捷档覆盖不到的组合（"只有周三和周六"）用这一排；勾完标题那一行立刻跟着变。
        var pick = new MenuFlyoutSubItem { Text = Locale.T("alarm.repeat.byDay") };
        foreach (var day in DayOrder)
        {
            var check = new ToggleMenuFlyoutItem
            {
                Text = DateTimeText.WeekdayShort(day),
                IsChecked = AlarmPolicy.Matches(item.Days, day),
            };
            // 加一天/减一天的算法全在 AlarmPolicy（TurnOn／TurnOff／Matches）：这一层不许自己摆位权。
            check.Click += (_, _) => alarms.SetDays(item.Id,
                check.IsChecked ? AlarmPolicy.TurnOn(item.Days, day) : AlarmPolicy.TurnOff(item.Days, day));
            pick.Items.Add(check);
        }
        repeat.Items.Add(pick);
        yield return repeat;

        var edit = new MenuFlyoutItem { Text = Locale.T("alarm.edit") };
        edit.Click += (_, _) => _ = EditAsync(alarms, item, owner);
        yield return edit;

        var test = new MenuFlyoutItem { Text = Locale.T("alarm.testFire") };
        test.Click += (_, _) => alarms.TestFire(item.Id);
        yield return test;

        var remove = new MenuFlyoutItem
        {
            Text = Locale.T("alarm.remove"),
            Icon = new FontIcon { Glyph = "\uE711", FontSize = 14 },   // Cancel
        };
        remove.Click += (_, _) => alarms.Remove(item.Id);
        yield return remove;
    }

    /// <summary>
    /// 表面上那一行被点开时的浮出菜单。<b>菜单在代码里现建、id 与条目都从参数进来</b>——
    /// 不走"XAML 声明 flyout ＋ 从菜单项反查 Parent"那条路（批次 UN 刚在待办那颗颜色点上查清：
    /// 模板元素套进 flyout 之后 <c>x:Bind</c> 带不出 id，反查天天静默 return）。
    /// </summary>
    public static void ShowRow(IAlarmEditor alarms, AlarmItem item, FrameworkElement anchor, Window? owner)
    {
        var menu = new MenuFlyout();
        foreach (var each in RowItems(alarms, item, owner)) menu.Items.Add(each);
        menu.ShowAt(anchor);
    }

    /// <summary>
    /// 加一条：一个输入框同时收钟点与名字（<c>7:30 起床</c>）。
    /// <b>解析失败必须原样把原因说出来</b>——"我明明写了时间"却只得到一句"格式不对"是最招烦的一种红，
    /// 而这里连"到底加上没有"都能在刚弹完的框后面看不见。
    /// </summary>
    public static async System.Threading.Tasks.Task AddAsync(IAlarmEditor alarms, Window? owner)
    {
        try
        {
            var input = await CenteredDialog.PromptAsync(
                title: LocMessage.Of("alarm.add.title"),
                message: LocMessage.Of("alarm.add.message"),
                placeholder: LocMessage.Of("alarm.add.placeholder"),
                owner: owner);
            if (input is null) return;                                  // 取消：什么都不改
            if (!AlarmPolicy.TryParseInput(input, out var minute, out var label, out var error))
            {
                // 弹窗是一次性的消费者：这一刻渲染当前语言那句，⛔ 不该把成品串提前存进任何常驻元素。
                await CenteredDialog.MessageAsync(LocMessage.Of("alarm.add.failed"), error ?? LocMessage.Of("alarm.unreadable"), owner);
                return;
            }
            alarms.Add(minute, label, AlarmDays.Daily);                 // 新条目默认"每天"：只有"单次"需要额外说明，其余都能就地改
        }
        catch (Exception ex)
        {
            StarLog.Error("加闹钟失败", ex);
        }
    }

    /// <summary>改一条：把当前那行原样填回输入框，改完再解析（留空或取消都不改动）。</summary>
    public static async System.Threading.Tasks.Task EditAsync(IAlarmEditor alarms, AlarmItem item, Window? owner)
    {
        try
        {
            var current = AlarmPolicy.FormatMinute(item.MinuteOfDay) + " " + item.Label;
            var input = await CenteredDialog.PromptAsync(
                title: LocMessage.Of("alarm.edit.title"),
                message: LocMessage.Of("alarm.edit.message"),
                // placeholder 那一颗⛔ 交"当前名字"：那是用户会改的值，而它随后就坐在 defaultText 里
                // （框没清空时提示根本不显示）——同一件值交两次，换语言那一刻只会留下两份出处（批次 WI-61d）。
                defaultText: current.Trim(),
                owner: owner);
            if (input is null) return;
            if (!AlarmPolicy.TryParseInput(input, out var minute, out var label, out var error))
            {
                await CenteredDialog.MessageAsync(LocMessage.Of("alarm.edit.kept"), error ?? LocMessage.Of("alarm.unreadable"), owner);
                return;
            }
            alarms.SetTime(item.Id, minute, label);
        }
        catch (Exception ex)
        {
            StarLog.Error("改闹钟失败", ex);
        }
    }
}
