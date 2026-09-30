#nullable enable

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
    /// <summary>弹窗标题：谁没生效就说谁。</summary>
    public static string Title(string action) => $"{action}没生效";

    /// <summary>
    /// 正文：一句到位，并给出<b>下一步</b>（刷新这一页），不留"请稍后重试"那种要用户猜的话。
    /// </summary>
    public static string RowGone(string action) =>
        $"{action}没落上去：这条已经不在这台机器的库里了（可能在别处被删掉）。刷新这一页就能看到它的现状。";
}
