using Microsoft.UI.Xaml.Controls;

namespace StarMark.UI.Helpers;

/// <summary>
/// 代码建的 <see cref="Expander"/> 唯一一处挂可访问名的地方（批次 Phase C 片143，✅P-267 同族）。
/// <para>为什么要有这一颗：UIA 把 Expander 的抬头报成一颗可点的 Button，而两页的 Header 都是
/// 两块 TextBlock 拼的（分类名／类型名 ＋ 那颗计数）——WinUI 替这种合成体念不出名字，
/// 辅助技术只剩一句「按钮」（真机那把尺现读 33 颗：「快捷键」页 16 颗＋「桌面工具（组件）」页 17 颗）。</para>
/// <para>名字借的是<b>抬头那句话自己已经在用的那颗键</b>，走 <see cref="LocText.NameKeyProperty"/> 进重贴名单
/// ⇒ 换语言那一刻跟着改口；⛔ 新字串、⛔ 第二份文案。做成扩展方法而不是在两处各写一行语句，
/// 是因为账本里指向那两颗建造档的行号指针还活着：行数一个字都不动，引用才不会静默过期。</para>
/// </summary>
public static class ExpanderTitleNames
{
    /// <summary>贴上抬头那颗键并原物交回，让建造那一处仍然是「一句说完」。</summary>
    public static Expander TitleNamed(this Expander expander, string titleKey)
    {
        LocText.SetNameKey(expander, titleKey);
        return expander;
    }
}
