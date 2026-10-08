#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页那几颗下拉的<b>数据项清单</b>（批次 WI-2c）：一项＝一颗键（＋一份代码要读的身份），
/// 文字一律由 <see cref="LocText"/> 按当前语言贴，⛔ 不再写在标记里。
/// <para>为什么非要从标记搬出来：那颗闭着的格子显示的是<b>选中那一刻从 Content 抄走的快照</b>，
/// 换语言时 <see cref="LocText"/> 只重贴"还活在可视树里、自己带着键"的元素——于是展开的列表当场翻，
/// 闭着的那一格要等用户重新选一次才改口（坑表 #272／#275 登记的那处不彻底）。换成"数据项＋模板"之后
/// 格子里那颗元素自己带键，重贴才落到它身上。</para>
/// <para>⚠ 同一族选项<b>只许一份出处</b>：主窗材质与组件材质那两颗下拉要共用同一份材质清单，
/// ⛔ 不许为另一颗再造一份同义键（坑表 #290）。</para>
/// </summary>
public sealed partial class SettingsPage
{
    /// <summary>主题三档。第 0 项那句"跟随系统"与界面语言那一栏用的是同一颗键（同一句字，同一个出处）。</summary>
    public IReadOnlyList<LocOption> ThemeOptions { get; } =
    [
        LocOption.T(FollowSystemKey),
        LocOption.T("settings.general.themeLight"),
        LocOption.T("settings.general.themeDark"),
    ];

    /// <summary>
    /// 材质六档：主窗口那一格与组件那一格<b>共用这一份</b>（两颗下拉只是各自记住选到第几项）。
    /// <para><b>顺序＝<c>WidgetBackdropKind</c> 的序号</b>——设置档里存的就是那颗序号，
    /// 插一项会把所有人已存的材质静默平移一格（同 <c>P-142</c> 那族"存位置"的坏法）。
    /// 所以清单只许有一份、且新增只能追加在末尾。</para>
    /// <para>批次 WI-52（P-183）：第三颗下拉＝「组件外观」编辑器里那一格，也收这一份
    /// （<see cref="WidgetMaterialWords"/>）。它当年把六档另写成"亚克力／云母／不透明…"六个短名，
    /// 于是同一件事在屏幕上有了两套念法——现在编辑器⛔ 不自写。</para>
    /// </summary>
    public IReadOnlyList<LocOption> MaterialOptions => WidgetMaterialWords.SixTiers;

    /// <summary>性能三档，顺序＝<c>PerformanceMode</c> 的序号（同上：存的是序号，不是名字）。</summary>
    public IReadOnlyList<LocOption> PerformanceOptions { get; } =
    [
        LocOption.T("settings.widgets.perfBalanced"),
        LocOption.T("settings.widgets.perfSavings"),
        LocOption.T("settings.widgets.perfCustom"),
    ];
}

/// <summary>
/// 材质六档的<b>唯一清单</b>（批次 WI-52·P-183）：一颗＝（那档在表里的键, 那档的 <c>WidgetBackdropKind</c> 身份）。
/// <para>为什么从 <see cref="SettingsPage"/> 的实例属性搬出来：第三颗下拉——「组件外观」编辑器那一格——
/// 建在 <c>WidgetAppearanceEditor</c> 里，够不到设置页的实例，于是它当年自己抄了六个短名
/// （亚克力／云母／不透明…），屏幕上同一件事就有了两套念法。清单住在这里，三颗下拉都收同一份。</para>
/// <para>⚠ <b>顺序＝<c>WidgetBackdropKind</c> 的序号</b>（设置页那两颗下拉把选中的第几项存进了设置档），
/// 只许追加在末尾、⛔ 换序（同 <c>P-142</c> 那族"存位置"的坏法）。</para>
/// </summary>
public static class WidgetMaterialWords
{
    public static readonly LocOption[] SixTiers =
    [
        LocOption.T("settings.general.material.acrylicThin", WidgetBackdropKind.Acrylic),
        LocOption.T("settings.general.material.mica", WidgetBackdropKind.Mica),
        LocOption.T("settings.general.material.opaque", WidgetBackdropKind.None),
        LocOption.T("settings.general.material.micaAlt", WidgetBackdropKind.MicaAlt),
        LocOption.T("settings.general.material.acrylicThick", WidgetBackdropKind.AcrylicBase),
        LocOption.T("settings.general.material.solid", WidgetBackdropKind.Solid),
    ];

    /// <summary>那一档材质在这份清单里的下标；<c>null</c>（＝跟随全局）或认不出的档给 <c>-1</c>。
    /// ⛔ 在别处再写一份 <c>kind ↔ 第几项</c> 的 switch：加一档材质时那份必漏，而它会<b>静默指错一档</b>。</summary>
    public static int IndexOf(WidgetBackdropKind? kind)
        => kind is { } k ? Array.FindIndex(SixTiers, o => o.Value is WidgetBackdropKind v && v == k) : -1;
}
