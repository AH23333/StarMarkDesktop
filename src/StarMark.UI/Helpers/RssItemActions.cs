#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Feed;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Helpers;

/// <summary>
/// RSS 候选行「收藏到文件夹」的<b>唯一</b>实现：页面上的按钮、卡片右键、右键菜单工厂三处都走这里
/// （与 <see cref="TrendingItemActions"/> 同一分工——同一个动作在多个入口必须语义一致）。
/// <para>
/// <b>只做"收进"，不做"移出"</b>（P-88 裁决）：这一栏的职责是把看中的收进来；收进之后它就是一条普通书签，
/// 要改要删去「文件夹」页对应那一行。在这里再放一个"取消收藏"等于给同一个东西两个入口，
/// 还会让人误以为这一栏能反写回订阅源。
/// </para>
/// <para>
/// 落库走 <see cref="IItemRepository.RecordItemAsync"/>（按 <c>(source, source_id)</c> 幂等合并 + 数据广播），
/// <b>不</b>走 <c>UpsertLocalItemAsync</c>：那条不碰归一化也不记活动流（批次 NF 已定口径）。
/// </para>
/// </summary>
public static class RssItemActions
{
    /// <summary>每一次收藏的结果（成功与失败都广播）。页面订阅一次即可——静默的收藏与静默的失败一样，
    /// 用户都只能靠"列表有没有变"来猜自己点到了什么。</summary>
    public static event Action<string>? NoticeRaised;

    /// <summary>
    /// 把这一行收进本机书签，并放进「RSS订阅 / 源名」。
    /// <para>重复点<b>不会</b>多出第二行（幂等 upsert），但也不该毫无回显，所以已收藏时直接说明去处，
    /// 而不是再写一遍库。</para>
    /// </summary>
    public static async Task CollectAsync(ItemCardViewModel vm)
    {
        var folder = vm.RssFolderPath;
        if (vm.IsCollected)
        {
            Report(vm, $"这一条已经在「{folder}」里了；要改动请到「文件夹」页对应那一行");
            return;
        }
        if (string.IsNullOrWhiteSpace(vm.Uri))
        {
            Report(vm, "这一行没有链接，收藏不了（订阅源没给出条目地址）");
            return;
        }

        var repo = App.Services.GetRequiredItemRepository();
        try
        {
            var item = RssRowDraft.ForCollect(vm.GetItem(), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var id = await repo.RecordItemAsync(item, CancellationToken.None);
            vm.SetCollected(true);
            Report(vm, id > 0
                ? $"已收进「{folder}」：{item.Title}"
                : $"已写进本机收藏，但仓储没有回 id（标题：{item.Title}）");
            if (id > 0)
                await repo.LogActivityAsync(ActivityKind.BookmarkAdd, $"{ItemSources.Local}:{item.SourceId}",
                    item.Title, item.Uri, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StarLog.Error($"RSS 收藏失败（{vm.Uri}）", ex);
            Report(vm, "收藏没能完成：" + ex.Message);
        }
    }

    private static void Report(ItemCardViewModel vm, string message)
    {
        NoticeRaised?.Invoke(message);
        StarLog.Info($"[RSS] {message}");
    }
}

/// <summary>
/// 「收藏」这一颗按钮/菜单项的<b>唯一</b>分流口：卡片按钮、卡片右键菜单、右键菜单工厂三处都只调这里。
/// <para>为什么要有这一层：按钮位是同一个（<c>ItemCardPolicy.ShowsCollect</c>），
/// 但热榜与 RSS 两类行的落点不同。分流写在卡片 XAML 的 Click 里，就会出现
/// "卡片上点的是 RSS 的收藏、右键菜单里点的是热榜的收藏"——两处各判一次，迟早只改一处。</para>
/// </summary>
public static class ItemCollectActions
{
    public static Task ToggleAsync(ItemCardViewModel vm)
        => vm.IsRssCandidate ? RssItemActions.CollectAsync(vm) : TrendingItemActions.ToggleCollectAsync(vm);
}
