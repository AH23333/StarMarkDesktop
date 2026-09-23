#nullable enable
using System;

namespace StarMark.Abstractions;

/// <summary>
/// 条目卡片"哪个动作该出现"的单一判据（纯函数，口径同 <c>ClipboardPolicy</c>）。
/// <para>
/// 为什么收在这里而不是各写各的：卡片与右键菜单一度是<b>逐处</b>写 <c>IsLauncherMode … Invert</c>，
/// 每加一类"外部来的、不该写主库的行"就要在十来处各补一个条件——漏掉一处就是一个点了会误写库的按钮。
/// 现在宿主只需要回答"这行是什么"，显示与否由这里的真值表决定（可单测，改坏了会红）。
/// </para>
/// </summary>
public static class ItemCardPolicy
{
    /// <summary>这一行是不是 GitHub 热榜候选（外部数据、默认不入库）。</summary>
    public static bool IsTrendingRepo(string? source)
        => string.Equals(source, ItemSources.Trending, StringComparison.Ordinal);

    /// <summary>
    /// 是否显示"库管理"类动作（置顶 / 隐藏 / 编辑笔记 / 编辑标签 / 发送到桌面 / 删除）。
    /// <para>两种宿主合成的行都要关掉：快捷启动入口（<paramref name="isLauncherMode"/>）与热榜候选
    /// （<paramref name="isTrendingRepo"/>）——前者没有对应库行，后者<b>有</b>会被写进库：
    /// 对 Id=0 的候选执行置顶会经"按需登记"把它塞进 items，直接违背"热榜默认不入库"。</para>
    /// </summary>
    public static bool ShowsLibraryActions(bool isLauncherMode, bool isTrendingRepo)
        => !isLauncherMode && !isTrendingRepo;

    /// <summary>是否显示热榜专属动作（⭐Star / 🔖收进收藏）。反过来也成立：只有热榜候选才有这两个按钮。</summary>
    public static bool ShowsTrendingActions(bool isTrendingRepo) => isTrendingRepo;

    /// <summary>
    /// 是否提供「发送到桌面 · 快捷启动」。它写的是<b>组件配置</b>而不是主库，所以 <see cref="ShowsLibraryActions"/>
    /// 没管它，但两类合成行同样不该出现：启动器行本来就在快捷启动里（发过去＝自己给自己再加一条），
    /// 热榜候选则会把"顺手一发"变成一个长期存在的入口——用户只是想看看这个仓库。
    /// </summary>
    public static bool CanSendToLauncher(bool isLauncherMode, bool isTrendingRepo, bool hasUri)
        => hasUri && ShowsLibraryActions(isLauncherMode, isTrendingRepo);

    /// <summary>Star 按钮的图标与文字：已 Star 必须一眼可辨（它是"再点会取消"的信号）。</summary>
    public static string StarGlyph(bool starred) => starred ? "★" : "☆";

    public static string StarLabel(bool starred) => starred ? "已 Star" : "Star";

    /// <summary>
    /// Star 的 tooltip：把"点下去会发生什么"说全，包括"已 Star 是本地已同步列表的判定，
    /// 刚在网页上 star 的要等下次同步才认得"——这句不写，用户会把界面当成不准确的实时状态。
    /// </summary>
    public static string StarTip(bool starred, bool hasToken)
        => (starred ? "已在你的 Star 列表中，点击取消 Star" : "Star 这个仓库（需 GitHub Token）")
           + (hasToken ? string.Empty : "；当前未配置 Token，点击会提示失败")
           + "。判定取自本机已同步的 Star 列表，网页上刚 Star 的要等下次同步才显示。";

    public static string CollectLabel(bool collected) => collected ? "移出收藏" : "收进收藏";

    public static string CollectTip(bool collected)
        => collected
            ? "已在本机收藏（书签）里，点击移除；不会动 GitHub 的 Star 状态"
            : "把它作为书签存进本机收藏；不会动 GitHub 的 Star 状态";
}
