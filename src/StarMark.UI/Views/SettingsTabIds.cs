#nullable enable
using StarMark.Core.Guide;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页那八栏的<b>身份</b>：外部说"带我去哪一栏"只认这里的串，不认屏幕上那几个字（批次 WI-2）。
/// <para><b>为什么要把身份从标题里拆出来</b>：标题从这一批起进界面语言表，中文档写着「健康与诊断」、
/// 英文档写着 "Health &amp; diagnostics"，而调用方（组件右键「完整设置」、顶栏「？」、重看引导）
/// 是编译期写死的。两者绑在一起的最坏结果不是报错，是<b>安静地落在默认页</b>——
/// 用户到了门口还得自己找房间，而这正是当初给它做"跳到那一栏"的理由。</para>
/// <para>XAML 里那八颗 <c>Tag</c> 与本表由 <c>SettingsTaxonomyGateTests</c> 两侧对账：
/// 少一颗、多一颗、顺序换了都会红。</para>
/// </summary>
public static class SettingsTabIds
{
    public const string General = "general";

    /// <summary>「使用说明」那一栏：身份住在 Core（那边的说明表也认它），这里只转发，不留第二份字面。</summary>
    public const string Guide = PageGuides.SettingsTabKey;

    public const string Ai = "ai";
    public const string Widgets = "widgets";
    public const string Extend = "extend";
    public const string Hotkeys = "hotkeys";
    public const string Data = "data";
    public const string Health = "health";

    /// <summary>从左到右的顺序。<b>顺序也是分类的一部分</b>：日常项在前、可选能力与取证在后（批次 WC-2 定下）。</summary>
    public static readonly string[] InOrder =
    {
        General, Guide, Ai, Widgets, Extend, Hotkeys, Data, Health,
    };
}
