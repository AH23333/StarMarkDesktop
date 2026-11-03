#nullable enable

using StarMark.Abstractions.I18n;

namespace StarMark.Abstractions;

/// <summary>
/// 「按 id 的单行状态写落到 0 行」时该对用户说什么——<b>这句话在全仓只有一个出处</b>（P-40，批次 SM）。
/// <para>
/// 为什么要有这颗：<c>UPDATE items SET … WHERE id = @id</c> 返回 0 行时，SQLite 没有别的解释——
/// <b>那一行已经不在了</b>（值没变也报 1 行）。过去这几处把 0 行当成功：界面把旗标翻过去、活动流记下一笔"修改"、
/// 回执说一句"已…"，而库里什么都没有，用户下次进来又看到原样。那是<b>程序在说谎</b>，不是"没反应"。
/// </para>
/// <para>
/// <b>刻意不并进来的一句</b>：剪贴板历史那句"这条记录已经不在历史里了（可能刚被清空或被新内容挤掉）"
/// 讲的是<b>另一件事</b>（行被轮转挤掉，原因不同、且已有 <c>DeleteClipboardEntryAsync</c> 的 bool 一路带到界面）。
/// 两句话各钉各的事实，合并只会把原因说糊——与"并措辞不并宿主策略"同条口径。
/// </para>
/// </summary>
public static class StateWriteNotice
{
    /// <summary>弹窗标题：谁没生效就说谁。<b>骨架与动作词都走表</b>（批次 WI-2p；WI-61d·片2 起动作词交<b>身份</b>）。</summary>
    public static LocMessage Title(LocMessage action) => LocMessage.Of("state.title", action);

    /// <summary>
    /// 正文：一句到位，并给出<b>下一步</b>（刷新这一页），不留"请稍后重试"那种要用户猜的话。
    /// 骨架进表（<c>state.rowGone</c>），<paramref name="action"/> 交的是<b>没渲染的那份身份</b>——
    /// 批次 WI-61d·片2：这一句的宿主是右下角提示条，它要在<b>贴上屏幕那一刻</b>才取词，换语言时才重贴得动
    /// （⛔ 在这里先 <c>Locale.Fmt</c> 完再回一句成品字＝那句被钉死在今天这一门语言上）。
    /// </summary>
    public static LocMessage RowGone(LocMessage action) =>
        LocMessage.Of("state.rowGone", action);

    /// <summary>
    /// 正文：<b>写这一行时抛了异常</b>（SQLite 忙／盘上出错）——与 <see cref="RowGone"/> 是<b>两种下落</b>：
    /// 那颗是"库里已经没有这一行"，这一颗是"这一行还在原处，只是这一次没写进去"。
    /// <para>为什么值得单独一句、而⛔ 复用 <see cref="RowGone"/>：那句让用户"刷新这一页"（行确实没了），
    /// 这句要说的是"条目未变动，稍后再点一次"——把两者并成一句，等于在一种下落里给用户错的下一步。
    /// 同一条口径也解释了为什么这一族⛔ 在页面上再长出自定义样式：<c>HiddenPage</c> 与
    /// <c>ItemCardActions</c> 两处宿主都走同一颗 <c>ShowError</c>（P-64 要的就是"原因得当面说出来"，
    /// 而<b>不是</b>"再多一行状态位"——那两处今天已经有右下角提示条这一颗出口）。</para>
    /// </summary>
    /// <param name="reason">异常交回的<b>语言中性事实</b>（<c>ex.Message</c>），不是要翻译的句子。</param>
    public static LocMessage WriteFailed(LocMessage action, string reason) =>
        LocMessage.Of("state.writeFailed", action, reason);
}
