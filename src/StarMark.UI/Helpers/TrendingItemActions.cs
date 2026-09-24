#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using StarMark.Abstractions;
using StarMark.Abstractions.Trending;
using StarMark.Integrations.GitHub;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;

namespace StarMark.UI.Helpers;

/// <summary>
/// 热榜行两个动作（⭐Star / 🔖收进收藏）的<b>唯一</b>实现：主窗「热榜」页的图标按钮、卡片右键、
/// 组件里的右键菜单三处都走这里 ⇒ 同一个动作在三个入口语义一致（历史上"组件行与主窗行为不同"出过多次）。
/// <para>
/// 每个动作都必须留下一句结果（<see cref="NoticeRaised"/>）：
/// 静默的成功会让人怀疑没点上，静默的失败更是直接把问题推回给用户（P-54 口径）。
/// </para>
/// </summary>
public static class TrendingItemActions
{
    /// <summary>扫描本机 star / 书签时的上限（与 P-57/P-58 同源：截断必须显式，不能静默少算）。</summary>
    private const int ScanLimit = 5000;

    /// <summary>
    /// 任意一次动作的结果（成功与失败都会广播）。UI 侧订阅一次即可，不必每个入口各拉一条属性线。
    /// <para>早先这里还往卡片写一个 <c>LastTrendingNotice</c> 属性，但没有任何一处读它——
    /// 一个从不被显示的结果槽只会让后来人以为"卡片上会显示"，故连属性一起删掉（批次 RB）。</para>
    /// </summary>
    public static event Action<string>? NoticeRaised;

    /// <summary>结果落点：广播给页面/组件的状态行（提示必须长在用户看得见的那一页）。</summary>
    private static void Report(ItemCardViewModel vm, string message) => NoticeRaised?.Invoke(message);

    /// <summary>Star / 取消 Star（远端写操作）。成功后记入会话状态，避免"刷新一下又显示未 Star"。</summary>
    public static async Task ToggleStarAsync(ItemCardViewModel vm)
    {
        var fullName = RepoOf(vm);
        if (fullName is null)
        {
            Report(vm, "这一行不是有效的 GitHub 仓库（owner/repo），无法 Star");
            return;
        }

        var wantStarred = !vm.IsStarred;
        try
        {
            await App.Services.GetRequiredService<GitHubClient>()
                .SetStarredAsync(fullName, wantStarred, CancellationToken.None);

            // 只有远端确认成功才改本地状态：先改后改错的显示会让人以为已经生效
            App.Services.GetRequiredService<TrendingStarState>().Record(fullName, wantStarred);
            vm.SetTrendingState(wantStarred, vm.IsCollected, hasToken: true);
            Report(vm, (wantStarred ? "已 Star：" : "已取消 Star：") + fullName);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"热榜 Star 操作失败（{fullName} → {(wantStarred ? "加星" : "取消星")}）：{ex.Message}");
            Report(vm, "Star 失败：" + ex.Message);
        }
        vm.RaiseStarRequested();
    }

    /// <summary>
    /// 收进收藏 / 移出收藏（本机书签条目，与 Star 是两个不同落点）。
    /// <para>移除按 <c>(source=local, source_id)</c> 而不是按 URI：同一 URL 在库里可能有别的生产者的行
    /// （浏览器导入的书签、上次同步的 star），按 URI 删会连它们一起删掉（P-65 同一形状）。</para>
    /// </summary>
    public static async Task ToggleCollectAsync(ItemCardViewModel vm)
    {
        var fullName = RepoOf(vm);
        if (fullName is null)
        {
            Report(vm, "这一行不是有效的 GitHub 仓库（owner/repo），无法收藏");
            return;
        }

        var repo = App.Services.GetRequiredItemRepository();
        try
        {
            if (vm.IsCollected)
            {
                var removedId = TrendingItemDraft.BookmarkSourceId(fullName);
                await repo.DeleteBySourceIdAsync(ItemSources.Local, removedId);
                vm.SetTrendingState(vm.IsStarred, collected: false, hasToken: true);
                Report(vm, $"已从本机收藏移除：{fullName}（不影响 GitHub 的 Star 状态）");
                await repo.LogActivityAsync(ActivityKind.BookmarkRemove, $"{ItemSources.Local}:{removedId}",
                    fullName, vm.Uri, CancellationToken.None);
            }
            else
            {
                var item = TrendingItemDraft.ForBookmark(
                    new TrendingRepo(fullName, vm.Uri, vm.Description ?? string.Empty, null, vm.StarsCount ?? 0),
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                var id = await repo.RecordItemAsync(item, CancellationToken.None);
                vm.SetTrendingState(vm.IsStarred, collected: true, hasToken: true);
                Report(vm, $"已收进本机收藏：{fullName}（不影响 GitHub 的 Star 状态）");
                if (id > 0)
                    await repo.LogActivityAsync(ActivityKind.BookmarkAdd, $"{ItemSources.Local}:{item.SourceId}",
                        item.Title, item.Uri, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            StarLog.Error($"热榜收藏操作失败（{fullName}）", ex);
            Report(vm, (vm.IsCollected ? "移出收藏失败：" : "收进收藏失败：") + ex.Message);
        }
        vm.RaiseCollectRequested();
    }

    /// <summary>热榜行的仓库标识：优先 <c>owner/repo</c> 标题，其次从 URL 反解（两处的判据必须同源）。</summary>
    private static string? RepoOf(ItemCardViewModel vm)
        => GitHubRepoId.Normalize(vm.Title) ?? GitHubRepoId.TryFromUri(vm.Uri);

    /// <summary>
    /// 一次读库，同时得出「哪些已 Star」与「哪些已收进收藏」，并把结果回填到卡片上。
    /// 星状态取自本机已同步的 star 条目（不发 HEAD 探测），因此页面在同步后要再调一次本方法。
    /// </summary>
    public static async Task RefreshStatesAsync(System.Collections.Generic.IReadOnlyList<ItemCardViewModel> rows,
        CancellationToken ct = default)
    {
        var repo = App.Services.GetRequiredItemRepository();
        var stars = App.Services.GetRequiredService<TrendingStarState>();
        try
        {
            stars.ReloadFrom(await repo.GetBySourceAsync(ItemSources.GitHub, ItemType.GitHubStar,
                ScanLimit, ct));
        }
        catch (Exception ex) { StarLog.Warn($"读取本机 Star 列表失败，热榜按“未 Star”显示：{ex.Message}"); }

        System.Collections.Generic.HashSet<string> collected;
        try
        {
            collected = TrendingCollectIndex.FromItems(await repo.GetBySourceAsync(
                ItemSources.Local, ItemType.Bookmark, ScanLimit, ct));
        }
        catch (Exception ex)
        {
            StarLog.Warn($"读取本机书签失败，热榜按“未收藏”显示：{ex.Message}");
            collected = new HashSet<string>(GitHubRepoId.Comparer);
        }

        var hasToken = !string.IsNullOrWhiteSpace(App.Services.GetRequiredService<GitHubOptions>().Token);
        foreach (var vm in rows)
        {
            var fullName = RepoOf(vm);
            vm.SetTrendingState(stars.IsStarred(fullName), TrendingCollectIndex.IsCollected(collected, fullName), hasToken);
        }
    }

    /// <summary>是否有未落到远端的 Star 会话改动（页面离开/同步前的提示依据）。</summary>
    public static bool HasPendingStarChanges
        => App.Services.GetRequiredService<TrendingStarState>().HasPendingChanges;
}
