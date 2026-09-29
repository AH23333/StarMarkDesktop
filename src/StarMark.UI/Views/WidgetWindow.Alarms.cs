#nullable enable
using System;
using System.Globalization;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// WidgetWindow 的这一段——时钟组件右键菜单里的「闹钟」一节（批次 RU）。
/// <para>按访问面拆出来的 partial：这里<b>不持有任何状态</b>。闹钟的真值住在那个时钟组件实例里
/// （<see cref="IAlarmEditor"/>），本文件只做"把当前值摆出来 + 把用户的意思转成一次调用"；
/// 哪一天响、什么时候算到点、待确认挂多久，一律问 <see cref="AlarmPolicy"/>。</para>
/// <para>菜单每次打开都重建（<see cref="OnContextMenuOpening"/>），所以勾与"下一次几点"因此总是当前值。</para>
/// </summary>
public sealed partial class WidgetWindow
{
    /// <summary>重复的四个快捷档（"勾星期"那一排给的是同一份旗标的另一种设法，不是第二套判据）。</summary>
    private static readonly (string Name, AlarmDays Days)[] RepeatPresets =
    [
        ("单次（叫一次就关）", AlarmDays.None),
        ("每天", AlarmDays.Daily),
        ("工作日（周一到周五）", AlarmDays.Weekdays),
        ("周末（周六周日）", AlarmDays.Weekends),
    ];

    /// <summary>星期那一排的阅读顺序（周一在前）。<b>位权仍按 <see cref="DayOfWeek"/> 算</b>——这里只是列出来的次序。</summary>
    private static readonly (string Name, DayOfWeek Day)[] DayOrder =
    [
        ("周一", DayOfWeek.Monday), ("周二", DayOfWeek.Tuesday), ("周三", DayOfWeek.Wednesday),
        ("周四", DayOfWeek.Thursday), ("周五", DayOfWeek.Friday), ("周六", DayOfWeek.Saturday),
        ("周日", DayOfWeek.Sunday),
    ];

    /// <summary>
    /// 「闹钟」一节：状态一行（下一次几点／待确认几条）＋ 停止待确认 ＋ 加一条 ＋ 每条一子排。
    /// <para>为什么把"待确认"做成一颗按钮而不是等它自己消失：用户裁决的是"提示卡＋常驻，不出声"，
    /// 那么<b>桌面上那一行必须有一个明确的关掉的地方</b>——否则"挂在那儿的红字"就成了第二个问题。</para>
    /// </summary>
    private void BuildAlarmSection(MenuFlyout menu, IAlarmEditor alarms)
    {
        menu.Items.Add(new MenuFlyoutSeparator());
        var pending = alarms.PendingCount;
        var section = new MenuFlyoutSubItem
        {
            Text = pending > 0 ? $"闹钟 · {pending} 条待确认" : "闹钟",
            Icon = new FontIcon { Glyph = "\uE823", FontSize = 14 },   // Clock
        };

        // 状态常驻一行：按完任何一项都要立刻看得见"下一次是几点"，否则"按了没反应"与"生效了但没到点"分不开。
        // "只有本程序活着的时候才响"这句话写死在下面<b>三条状态</b>里：不让人以为它是系统闹钟
        // （用户裁决：就地写明，不做系统级排程）。只在其中一条上写＝另外两条读起来仍是系统闹钟。
        section.Items.Add(new MenuFlyoutItem
        {
            Text = pending > 0
                ? $"到点了：按下面「停止待确认」就收（本程序运行时才提醒）"
                : alarms.NextDueAt is { } due
                    ? $"下一次大约 {due.ToString("HH:mm", CultureInfo.InvariantCulture)}（本程序运行时才提醒）"
                    : "还没有闹钟：加一条，例如「7:30 起床」（本程序运行时才提醒）",
            IsEnabled = false,
        });

        if (pending > 0)
        {
            var stop = new MenuFlyoutItem
            {
                Text = $"停止待确认（{pending}）",
                Icon = new FontIcon { Glyph = "\uE73E", FontSize = 14 },   // CheckMark：确认掉这一轮
            };
            stop.Click += (_, _) => alarms.ConfirmPending();
            section.Items.Add(stop);
        }

        var add = new MenuFlyoutItem
        {
            Text = alarms.IsFull ? $"加一条（最多 {AlarmPolicy.MaxItems} 条，先删一条）" : "加一条…",
            Icon = new FontIcon { Glyph = "\uE710", FontSize = 14 },       // Add
            IsEnabled = !alarms.IsFull,
        };
        add.Click += (_, _) => _ = AddAlarmAsync(alarms);
        section.Items.Add(add);

        foreach (var item in alarms.Alarms)
            section.Items.Add(BuildAlarmRow(alarms, item));

        menu.Items.Add(section);
    }

    /// <summary>一条闹钟一行：标题就是它的全部设定（<c>07:30 起床 · 每天</c>），点开才是动作。</summary>
    private MenuFlyoutSubItem BuildAlarmRow(IAlarmEditor alarms, AlarmItem item)
    {
        var row = new MenuFlyoutSubItem
        {
            Text = (item.Enabled ? string.Empty : "（已关）") + AlarmPolicy.LineOf(item),
        };

        var onOff = new ToggleMenuFlyoutItem { Text = "开着", IsChecked = item.Enabled };
        onOff.Click += (_, _) => alarms.SetEnabled(item.Id, onOff.IsChecked);
        row.Items.Add(onOff);

        var repeat = new MenuFlyoutSubItem { Text = $"重复：{AlarmPolicy.DaysLabel(item.Days)}" };
        foreach (var (name, days) in RepeatPresets)
        {
            var preset = new RadioMenuFlyoutItem { Text = name, IsChecked = days == item.Days };
            var chosen = days;
            preset.Click += (_, _) => alarms.SetDays(item.Id, chosen);
            repeat.Items.Add(preset);
        }
        // 星期逐天勾：四个快捷档覆盖不到的组合（"只有周三和周六"）用这一排。勾完之后标题那一行立刻跟着变。
        var pick = new MenuFlyoutSubItem { Text = "按星期勾" };
        foreach (var (name, day) in DayOrder)
        {
            var check = new ToggleMenuFlyoutItem { Text = name, IsChecked = AlarmPolicy.Matches(item.Days, day) };
            // 加一天/减一天的算法全在 AlarmPolicy（TurnOn／TurnOff／Matches）：这一层不许自己摆位权，
            // 位权在 UI 里再算一遍就是第二份真值（错的时候是"勾了周三、响在周四"）。
            check.Click += (_, _) => alarms.SetDays(item.Id,
                check.IsChecked ? AlarmPolicy.TurnOn(item.Days, day) : AlarmPolicy.TurnOff(item.Days, day));
            pick.Items.Add(check);
        }
        repeat.Items.Add(pick);
        row.Items.Add(repeat);

        var edit = new MenuFlyoutItem { Text = "改时间或名字…" };
        edit.Click += (_, _) => _ = EditAlarmAsync(alarms, item);
        row.Items.Add(edit);

        var test = new MenuFlyoutItem { Text = "试响一次（不动到点记录）" };
        test.Click += (_, _) => alarms.TestFire(item.Id);
        row.Items.Add(test);

        var remove = new MenuFlyoutItem
        {
            Text = "删掉这一条",
            Icon = new FontIcon { Glyph = "\uE711", FontSize = 14 },   // Cancel
        };
        remove.Click += (_, _) => alarms.Remove(item.Id);
        row.Items.Add(remove);

        return row;
    }

    /// <summary>
    /// 加一条：一个输入框同时收钟点和名字（<c>7:30 起床</c>）。
    /// <b>解析失败必须原样把原因说出来</b>——"我明明写了时间"却只得到一句"格式不对"是最招烦的一种红，
    /// 而这里连"改完没加上"都没有别的地方可以核对（输入框一关就没了）。
    /// </summary>
    private async System.Threading.Tasks.Task AddAlarmAsync(IAlarmEditor alarms)
    {
        try
        {
            var input = await CenteredDialog.PromptAsync(
                title: "加一条闹钟",
                message: "写钟点，后面可以跟名字。例如「7:30 起床」。",
                placeholder: "7:30 起床",
                owner: this);
            if (input is null) return;                                  // 取消：什么都不改
            if (!AlarmPolicy.TryParseInput(input, out var minute, out var label, out var error))
            {
                await CenteredDialog.MessageAsync("没加上", error ?? "看不懂这个时间。", this);
                return;
            }
            alarms.Add(minute, label, AlarmDays.Daily);                 // 新条目默认"每天"：只有"单次"需要额外说明，其余都能就地改
        }
        catch (Exception ex)
        {
            StarLog.Error("加闹钟失败", ex);
        }
    }

    /// <summary>改一条：把当前那行原样填回输入框，改完再解析。</summary>
    private async System.Threading.Tasks.Task EditAlarmAsync(IAlarmEditor alarms, AlarmItem item)
    {
        try
        {
            var current = $"{AlarmPolicy.FormatMinute(item.MinuteOfDay)} {item.Label}".Trim();
            var input = await CenteredDialog.PromptAsync(
                title: "改这条闹钟",
                message: "写钟点，后面可以跟名字。留空或取消都不改动。",
                placeholder: current,
                defaultText: current,
                owner: this);
            if (input is null) return;
            if (!AlarmPolicy.TryParseInput(input, out var minute, out var label, out var error))
            {
                await CenteredDialog.MessageAsync("没改", error ?? "看不懂这个时间。", this);
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
