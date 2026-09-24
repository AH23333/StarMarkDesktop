#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Core.Ai;
using StarMark.UI.Helpers;

namespace StarMark.UI.Services;

/// <summary>一轮整理的结果汇总（界面上那一行就靠它）。</summary>
/// <param name="Plan">整理出来、还没应用的方案（<b>已经过"不成类标签"这一刀</b>）。</param>
/// <param name="MissingItems">模型答了但漏掉的条目数——<b>"整理完了"这句话要把缺的报出来</b>。</param>
/// <param name="DroppedTags">被砍掉的"整个方案里只挂了一条条目"的标签数。<b>砍了多少必须报</b>：
/// 建议条数变少了，用户有权知道是程序替他砍的，而不是模型少答了。</param>
/// <param name="DroppedItems">因为这些标签被砍而一个标签都不剩、于是整条不提的条目数。</param>
/// <param name="FirstError">第一个坏批的原因。<b>只报第一个</b>：六个批各自超时，用户要处理的是同一件事。</param>
public sealed record OrganiseOutcome(
    ClassifyPlan Plan,
    int AskedItems,
    int Batches,
    int FailedBatches,
    int StoppedBatches,
    int MissingItems,
    int UnknownOrdinals,
    int DroppedTags,
    int DroppedItems,
    string? FirstError)
{
    public bool NothingOrganised => Plan.IsEmpty && FailedBatches > 0;
}

/// <summary>
/// AI 批量整理标签的落地服务：选候选 → 交给 <see cref="ClassifyRunner"/> → 方案落盘 → 按组应用。
/// <para>
/// <b>剪贴板条目不参与整理</b>：它有自己的历史生命周期（会轮转、会被一键清空），
/// 给它打上的标签既进不了日常检索，又会跟着清空一起消失——那是纯写入没有回报。
/// </para>
/// </summary>
public sealed class AiClassifyService
{
    /// <summary>一次最多问多少条。本地小模型 500 条要十几批、耗时以分钟计，再多就该分批做而不是硬扛。</summary>
    public const int MaxCandidates = 500;
    public const int DefaultCandidates = 100;

    private static readonly ItemType[] InScope =
    {
        ItemType.Bookmark, ItemType.GitHubStar, ItemType.File, ItemType.Todo, ItemType.Note,
    };

    private readonly IItemRepository _repo;
    private readonly SettingsStore _store;
    private readonly StarMark.Integrations.Ai.AiGateway _gateway;

    public AiClassifyService(IItemRepository repo, SettingsStore store, StarMark.Integrations.Ai.AiGateway gateway)
    {
        _repo = repo;
        _store = store;
        _gateway = gateway;
    }

    /// <summary>
    /// 候选：库里<b>还没有任何标签</b>的条目，按最近优先取前 limit 条。
    /// <para>筛选整个交给 <see cref="IItemRepository.GetUntaggedAsync"/> 在 SQL 里做。
    /// 先前这里写法是"取最近 2000 条再在内存里挑没标签的"，那有一个窗口：
    /// 库里最近两千条都打了标签时，早期真正待整理的一条都进不来，
    /// 而界面会老老实实报告"没有待整理的条目"——<b>少报比报错难发现得多</b>。</para>
    /// </summary>
    public async Task<IReadOnlyList<ClassifyItem>> LoadCandidatesAsync(int limit, CancellationToken ct)
    {
        var wanted = Math.Clamp(limit, 1, MaxCandidates);
        var rows = await _repo.GetUntaggedAsync(InScope, wanted, ct);
        return rows
            .Select(item => new ClassifyItem(item.Id, item.Title,
                string.IsNullOrWhiteSpace(item.Subtitle) ? null : item.Subtitle,
                string.IsNullOrWhiteSpace(item.Description) ? null : item.Description,
                NameOf(item), item.Tags ?? new List<string>()))
            .ToList();
    }

    private static string NameOf(Item item) => item.Type switch
    {
        ItemType.File => "本地文件",
        ItemType.Bookmark => "书签",
        ItemType.GitHubStar => "GitHub",
        ItemType.Todo => "待办",
        ItemType.Note => "随记",
        _ => item.Source,
    };

    /// <summary>参考类别：库里用得最多的那些标签。<b>锚定它是为了避免同一件事出现三个近义词</b>——
    /// 而那正是用户要整理的起因。</summary>
    public async Task<IReadOnlyList<string>> ReferenceTagsAsync(CancellationToken ct)
    {
        var tags = await _repo.GetAllTagsAsync(ct);
        return tags.Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .OrderByDescending(row => row.Count)
            .Select(row => row.Name)
            .Take(ClassifyPrompt.MaxReferenceTags)
            .ToList();
    }

    /// <summary>
    /// 跑一轮整理。<b>不写库</b>：整理出来的东西要用户在预览里点头才算数。
    /// <para>每批边界把"目前为止的方案"落一次盘——用户中途关窗口、进程被杀，
    /// 已经整理出来的部分都还在，回来还能看见「继续应用上次的整理结果」。</para>
    /// </summary>
    public async Task<OrganiseOutcome> OrganiseAsync(
        int limit, Action<string> onStatus, Action<int, int> onProgress, CancellationToken ct)
    {
        var settings = _store.LoadAiSettings();
        if (settings.Problem() is { } bad)
            return new OrganiseOutcome(ClassifyPlan.Empty, 0, 0, 0, 0, 0, 0, 0, 0, "AI 通道还不能用：" + bad);

        onStatus("正在挑出还没有标签的条目…");
        var candidates = await LoadCandidatesAsync(limit, ct);
        if (candidates.Count == 0)
            return new OrganiseOutcome(ClassifyPlan.Empty, 0, 0, 0, 0, 0, 0, 0, 0,
                "没有待整理的条目（要么都有标签了，要么只剩剪贴板条目）");

        var catalog = await ReferenceTagsAsync(ct);
        var total = ClassifyPrompt.Batches(candidates).Count;
        onStatus($"共 {candidates.Count} 条待整理，分 {total} 批问模型…");

        // 从"上次整理好但还没应用"的方案接着写：<b>不种子就会让这一轮把旧结果整档覆盖掉</b>——
        // 用户上一次花掉的那次整理会在毫无提示的情况下消失（存档是整档写的，没有"追加"这回事）。
        // 同一条目若这轮又问了一遍，Merge 以新的一份为准，不会把两次的标签并起来。
        var proposals = LoadPending().Proposals.ToList();
        var report = await ClassifyRunner.RunAsync(
            candidates, catalog,
            request => _gateway.CompleteAsync(settings, request, ct),
            done =>
            {
                Merge(proposals, done.Proposals);
                SavePending(new ClassifyPlan(proposals.ToList(), DateTimeOffset.UtcNow));
                onProgress(done.Index + 1, total);
                return Task.CompletedTask;
            },
            ct);

        Merge(proposals, report.Proposals);                 // 编排层给的汇总为准（含回调没覆盖到的情况）
        // 最后再砍一刀：整个方案里只挂上一条条目的标签不叫分类（用户要的正是"别再一条一个词"）。
        // 计数按<b>整个方案</b>而不是按批：同一个词在两批里各出现一次，合起来是两条，它够格留下。
        var pruned = new ClassifyPlan(proposals, DateTimeOffset.UtcNow).WithoutSingletonTags();
        var plan = pruned.Plan;
        if (plan.IsEmpty) ClearPending(); else SavePending(plan);

        return new OrganiseOutcome(
            plan,
            candidates.Count,
            report.Batches.Count,
            report.FailedBatches,
            report.StoppedBatches,
            report.Batches.Sum(batch => batch.MissingCount),
            report.Batches.Sum(batch => batch.UnknownCount),
            pruned.DroppedTags.Count,
            pruned.DroppedItems,
            report.Batches.FirstOrDefault(batch => !batch.Ok)?.Error);
    }

    /// <summary>把一批的结果并进"目前为止"。<b>同一条目再出现时以新的那份为准而不是合并标签</b>：
    /// 分批只按候选顺序切，重复说明上层传错了列表——取后者比"两批标签并一起"更可预期。</summary>
    private static void Merge(List<TagProposal> into, IReadOnlyList<TagProposal> incoming)
    {
        foreach (var proposal in incoming)
        {
            var at = into.FindIndex(existing => existing.Id == proposal.Id);
            if (at >= 0) into[at] = proposal;
            else into.Add(proposal);
        }
    }

    // ────────── 未应用方案的落盘 ──────────

    /// <summary>读"还没应用的方案"。<b>读侧也要砍一次不成类的标签</b>：每批边界的检查点存的是
    /// 汇总前的原始结果（那样中途被杀不丢信息），而这一刀必须在交给界面之前落——
    /// 这条规则之前整理出来的旧档里那些"一条一个词"的标签，否则又会原样摆回预览。</summary>
    public ClassifyPlan LoadPending() => _store.LoadAiPlan().WithoutSingletonTags().Plan;
    public void SavePending(ClassifyPlan plan) => _store.SaveAiPlan(plan);
    public void ClearPending() => _store.ClearAiPlan();

    /// <summary>
    /// 应用方案（整份或某一组）。返回真正加上标签的条目数。
    /// <para>写库全部交给 <see cref="IItemRepository.TagItemsAsync"/>：一次事务、每条目一次索引重建、
    /// 整批一次通知。<b>不要在界面上自己转圈逐条 AddTagAsync</b>——那正是这个方法要避免的形状。</para>
    /// </summary>
    public async Task<int> ApplyAsync(ClassifyPlan plan, CancellationToken ct)
    {
        if (plan.IsEmpty) return 0;
        return await _repo.TagItemsAsync(
            plan.Proposals.Select(proposal => new ItemTagAssignment(proposal.Id, proposal.Tags)).ToList(), ct);
    }
}
