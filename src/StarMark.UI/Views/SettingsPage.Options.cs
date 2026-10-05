#nullable enable
using System.Collections.Generic;
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
    /// </summary>
    public IReadOnlyList<LocOption> MaterialOptions { get; } =
    [
        LocOption.T("settings.general.material.acrylicThin"),
        LocOption.T("settings.general.material.mica"),
        LocOption.T("settings.general.material.opaque"),
        LocOption.T("settings.general.material.micaAlt"),
        LocOption.T("settings.general.material.acrylicThick"),
        LocOption.T("settings.general.material.solid"),
    ];

    /// <summary>性能三档，顺序＝<c>PerformanceMode</c> 的序号（同上：存的是序号，不是名字）。</summary>
    public IReadOnlyList<LocOption> PerformanceOptions { get; } =
    [
        LocOption.T("settings.widgets.perfBalanced"),
        LocOption.T("settings.widgets.perfSavings"),
        LocOption.T("settings.widgets.perfCustom"),
    ];
}
