#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Core.Widgets;

namespace StarMark.UI.Views;

/// <summary>
/// "这份内容自己带闹钟"的口子（批次 RU）。
/// <para>
/// 为什么是接口而不是让宿主窗口去读写存档：闹钟的真值<b>住在这个组件实例里</b>（用户的裁决是"放在时钟组件里"），
/// 宿主如果绕过组件自己去改存档，就会出现"内存里一份、存档里一份"——而改完不重开组件时屏幕上显示的还是旧的那份，
/// 症状正是"改了没反应"。宿主只通过这张表问与改，落盘与刷新都由组件自己做，一条链上只有一份真值。
/// </para>
/// <para>与 <see cref="IWidgetTicker"/> 同一形状：没实现这个接口的内容，宿主那一句 <c>as</c> 出来就是 null，不需要按类型分支。</para>
/// </summary>
public interface IAlarmEditor
{
    /// <summary>
    /// 当前这套闹钟（菜单每次打开重新取，所以不存在"菜单里是旧的"）。
    /// <b>交出去的是正在用的那些对象本身，不是副本</b>：读它们随便，改必须走下面那几个方法——
    /// 直接改字段不会落盘，症状是"改了当时有效、重启又回去了"。
    /// </summary>
    IReadOnlyList<AlarmItem> Alarms { get; }

    /// <summary>现在挂着几条"弹过了还没确认"（菜单要把那颗「停止」点亮并写出条数）。</summary>
    int PendingCount { get; }

    /// <summary>是否已到条数上限（<see cref="AlarmPolicy.MaxItems"/>）。菜单据此先说话，而不是按下没反应。</summary>
    bool IsFull { get; }

    void Add(int minute, string label, AlarmDays days);
    void SetEnabled(long id, bool enabled);
    void SetDays(long id, AlarmDays days);
    void SetTime(long id, int minute, string label);
    void Remove(long id);
    void ConfirmPending();

    /// <summary>
    /// 立刻为这一条发一次提示卡（<b>不改到点记录、不改待确认</b>）。
    /// 没有这条出口，"到点那一发到底看不看得见"只能等一个真实的早晨才能验证——
    /// 而"等 15 分钟验证一个开关"等于没给验证路径（批次 WA 立下的口径）。
    /// </summary>
    void TestFire(long id);

    /// <summary>下一次该响的时刻（没有可响的条目时为 null）。菜单那一行要报"下一次 07:30"，判据仍走 Core。</summary>
    DateTimeOffset? NextDueAt { get; }
}
