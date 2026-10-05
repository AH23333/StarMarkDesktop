#nullable enable
namespace StarMark.UI.Helpers;

/// <summary>
/// 下拉框的一颗<b>数据项</b>：文字走表（<see cref="Key"/>）或就是一句不翻的自名（<see cref="Text"/>）。
/// <para>为什么不让 <c>ComboBoxItem</c> 直接躺在标记里（批次 WI-2c）：WinUI 那颗闭着的格子显示的是
/// <b>选中那一刻从 Content 抄走的一份快照</b>，换语言时 <c>LocText</c> 只重贴"还活在可视树里"的那些元素——
/// 展开的列表当场翻，闭着的那一格要等用户重新选一次才跟着改口（坑表 #272／#275 登记过的那处不彻底）。
/// 换成"数据项＋<see cref="LocText.KeyProperty"/> 模板"之后，格子里那颗 <c>TextBlock</c> 自己带着键、
/// 自己就是被注册的元素，重贴也就落到它身上。</para>
/// <para><see cref="Value"/> 是给代码读的那颗<b>身份</b>（排序键、语言代码、枚举值），⛔ 不许拿显示文字当身份：
/// 换语言之后显示文字变了，而选中的还是同一项。</para>
/// <para><see cref="TipKey"/> 是这一项自己的悬停提示（可以没有）。它与 <see cref="Key"/> <b>必须是两颗</b>：
/// 原来那些 <c>&lt;ComboBoxItem&gt;</c> 同时挂着正文键与提示键，一颗键贴不出两句话——
/// 只贴正文的那一版会把提示永远留在旧语言。</para>
/// </summary>
public sealed record LocOption(string? Key, string Text, object? Value, string? TipKey = null)
{
    /// <summary>一句要走表的话：文字先留空，<c>LocText</c> 在贴的时候按当前语言填。</summary>
    public static LocOption T(string key, object? value = null, string? tipKey = null) => new(key, string.Empty, value, tipKey);

    /// <summary>一句<b>本来就不该翻</b>的字（语言自名这类：简体中文在英文界面上仍写作「简体中文」）。</summary>
    public static LocOption SelfName(string text, object? value = null, string? tipKey = null) => new(null, text, value, tipKey);
}
