#nullable enable
using Microsoft.UI.Xaml.Controls;
using StarMark.Core.Widgets;

namespace StarMark.UI.Views;

/// <summary>
/// WidgetWindow 的这一段——时钟组件右键菜单里的「闹钟」一节（批次 RU）。
/// <para>按访问面拆出来的 partial：这里<b>不持有任何状态</b>。闹钟的真值住在那个时钟组件实例里
/// （<see cref="IAlarmEditor"/>），本文件只做"把当前值摆出来 + 把用户的意思转成一次调用"；
/// 哪一天响、什么时候算到点、待确认挂多久，一律问 <see cref="AlarmPolicy"/>。</para>
/// <para>菜单每次打开都重建（<see cref="OnContextMenuOpening"/>），所以勾与"下一次几点"因此总是当前值。</para>
/// <para>
/// 批次 VO 之后，<b>一条闹钟的动作表搬到了 <see cref="AlarmMenu"/></b>：时钟表面上那一排与这里必须共用同一份，
/// 否则第一次加动作忘了改另一处，就成了"从表面上删不掉、从右键里却删得掉"。
/// 本节因此只剩<b>这一节自己的</b>三件事：状态行、待确认的出口、加一条。
/// </para>
/// </summary>
public sealed partial class WidgetWindow
{
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
                    ? $"下一次大约 {StarMark.Abstractions.DateTimeText.Clock(due)}（本程序运行时才提醒）"
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
        add.Click += (_, _) => _ = AlarmMenu.AddAsync(alarms, this);
        section.Items.Add(add);

        foreach (var item in alarms.Alarms)
            section.Items.Add(BuildAlarmRow(alarms, item));

        menu.Items.Add(section);
    }

    /// <summary>
    /// 一条闹钟一行：标题就是它的全部设定（<c>07:30 起床 · 每天</c>），点开才是动作。
    /// 动作与<b>时钟表面上那一排完全同一份</b>（<see cref="AlarmMenu.RowItems"/>），这里只负责把它们装进子菜单。
    /// </summary>
    private MenuFlyoutSubItem BuildAlarmRow(IAlarmEditor alarms, AlarmItem item)
    {
        var row = new MenuFlyoutSubItem { Text = AlarmMenu.TitleOf(item) };
        foreach (var each in AlarmMenu.RowItems(alarms, item, this)) row.Items.Add(each);
        return row;
    }
}
