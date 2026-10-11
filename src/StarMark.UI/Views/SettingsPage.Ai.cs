#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.I18n;
using StarMark.Core.Ai;
using StarMark.Integrations.Ai;
using StarMark.UI.Helpers;
using StarMark.UI.Services;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页的「AI 助手」这一栏（批次 A）。<b>这一批只做到"通道能不能用"</b>：
/// 批量整理标签的界面与编排在批次 B，那里会从这里读同一份 <see cref="AiSettings"/> 与同一个
/// <see cref="AiGateway"/>，而不是再建一套配置。
/// <para>
/// 默认关闭且不发任何请求（§15.5）：这一栏在用户亲手打开开关之前，程序不会把库里的标题摘要发给任何进程。
/// </para>
/// </summary>
public sealed partial class SettingsPage
{
    private bool _aiLoading;
    private bool _aiBusy;
    private CancellationTokenSource? _aiProbeCts;
    private IReadOnlyList<string> _aiModels = Array.Empty<string>();

    private void InitAiSection()
    {
        // 通道下拉由枚举生成：加了新通道就自动多一项，不会出现"枚举里有、界面没有"
        _aiLoading = true;
        try
        {
            AiProviderBox.Items.Clear();
            foreach (var kind in Enum.GetValues<AiProviderKind>())
                AiProviderBox.Items.Add(new ComboBoxItem { Content = AiSettings.NameOf(kind), Tag = kind });

            var stored = App.Services.GetRequiredService<SettingsStore>().LoadAiSettings();
            AiEnabledToggle.IsOn = stored.Enabled;
            AiProviderBox.SelectedIndex = Array.IndexOf(
                Enum.GetValues<AiProviderKind>(), stored.Provider);
            AiModelBox.Text = stored.Model ?? string.Empty;
            AiClassifyModelBox.Text = stored.ClassifyModel ?? string.Empty;
            AiOllamaUrlBox.Text = stored.OllamaBaseUrl ?? string.Empty;
            AiBaseUrlBox.Text = stored.BaseUrl ?? string.Empty;
            AiKeyBox.Password = stored.ApiKey ?? string.Empty;
        }
        finally
        {
            _aiLoading = false;
        }
        ShowAiProblem();
        InitAiOrganiseSection();
        InitAiUsagePanel();     // §20.1 用量面板：读一次账本，失败只留一行解释不拦设置页
    }

    /// <summary>把界面上这一组控件读成一份配置。<b>只有这一个地方读控件</b>：
    /// 分散着读就会出现"某一格改了但没进保存"的漏。</summary>
    private AiSettings ReadAiSettings() => new(
        Enabled: AiEnabledToggle.IsOn,
        Provider: AiProviderBox.SelectedItem is ComboBoxItem { Tag: AiProviderKind kind }
            ? kind : AiProviderKind.Ollama,
        Model: AiModelBox.Text,
        ApiKey: AiKeyBox.Password,
        OllamaBaseUrl: AiOllamaUrlBox.Text,
        BaseUrl: AiBaseUrlBox.Text,
        ClassifyModel: AiClassifyModelBox.Text);

    private void AiEnabled_Toggled(object sender, RoutedEventArgs e) => PersistAiAndShow();

    private void AiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e) => PersistAiAndShow();

    private void AiField_TextChanged(object sender, RoutedEventArgs e) => PersistAiAndShow();

    private void PersistAiAndShow()
    {
        if (_aiLoading) return;           // 装载时控件逐个置位，不能每一步都写盘一次
        var settings = ReadAiSettings();
        App.Services.GetRequiredService<SettingsStore>().SaveAiSettings(settings);
        ShowAiProblem(settings);
    }

    /// <summary>未测试过就把"能不能用"的判据显示出来：<b>配置不成立时不该让用户去点「测试连接」才发现</b>。</summary>
    private void ShowAiProblem(AiSettings? settings = null)
    {
        settings ??= ReadAiSettings();
        // 那一格里混着两家的话：骨架（"配置还不能用／可以点…"）进表，`Problem()`／`DescribeWhere()`
        // 那两句成品字由 Abstractions 念完再交过来 ⇒ 走 `common.rawValue` 那道门（⛔ 半搬，归 P-193）。
        LocText.SetMessage(AiStatusText, settings.Problem() is { } bad
            ? LocMessage.Of("settings.ai.status.configBlocked", LocMessage.Of("common.rawValue", bad))
            : LocMessage.Of("settings.ai.status.whereHint",
                LocMessage.Of("common.rawValue", settings.DescribeWhere()),
                LocMessage.Of("settings.ai.testConnection")));
    }

    /// <summary>这两句字<b>走键不走字</b>（坑表 #276）：直接写 <c>Content = "…"</c> 的话，
    /// 换语言那一刻 <c>LocText</c> 会按标记里那颗"空闲"键把话重贴回去——界面替一件还在跑的事改了口。</summary>
    private const string ProbeIdleKey = "settings.ai.testConnection";
    private const string ProbeCancelKey = "dialog.cancel";

    /// <summary>
    /// 「测试连接」：问服务有哪些模型，并核对模型名在不在。
    /// <para>抓取期间同一个按钮变成「取消」：本机模型冷启动时这一下可能要十几秒，
    /// 只把按钮置灰让人干等，等于把唯一入口变成一个不能中断的黑盒。</para>
    /// </summary>
    private async void AiProbe_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy)
        {
            _aiProbeCts?.Cancel();
            LocText.SetMessage(AiStatusText, LocMessage.Of("settings.ai.status.cancellingProbe"));
            return;
        }

        var settings = ReadAiSettings();
        _aiBusy = true;
        LocText.SetKey(AiProbeButton, ProbeCancelKey);
        AiFillModelButton.Visibility = Visibility.Collapsed;
        LocText.SetMessage(AiStatusText, LocMessage.Of("settings.ai.status.probing"));
        var cts = new CancellationTokenSource();
        _aiProbeCts = cts;
        try
        {
            var probe = await App.Services.GetRequiredService<AiGateway>().ProbeAsync(settings, cts.Token);
            LocText.SetMessage(AiStatusText, LocMessage.Of("common.rawValue", probe.Report));

            // "通了但模型名不对"要能一步修好：把服务侧的名字直接递过去，而不是让用户自己抄一遍
            _aiModels = probe.Models;
            if (!probe.Ok && _aiModels.Count > 0)
            {
                LocText.SetMessage(AiFillModelButton, LocMessage.Of("settings.ai.fillModel", _aiModels[0]));
                AiFillModelButton.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            LocText.SetMessage(AiStatusText, LocMessage.Of("settings.ai.status.probeCancelled"));
        }
        catch (Exception ex)
        {
            StarLog.Error("[AI] 连接自检失败", ex);
            LocText.SetMessage(AiStatusText,
                LocMessage.Of("settings.ai.status.probeFailed", LocMessage.Of("common.rawValue", ex.Message)));
        }
        finally
        {
            _aiBusy = false;
            _aiProbeCts = null;
            LocText.SetKey(AiProbeButton, ProbeIdleKey);
            cts.Dispose();
        }
    }

    private void AiFillModel_Click(object sender, RoutedEventArgs e)
    {
        if (_aiModels.Count == 0) return;
        AiModelBox.Text = _aiModels[0];       // TextChanged 会顺手保存并刷新状态
    }

    // ───────────────────── 批量整理（批次 B）─────────────────────

    private readonly ObservableCollection<AiGroupRow> _aiGroups = new();
    private ClassifyPlan _aiPlan = ClassifyPlan.Empty;
    private bool _aiOrganising;
    private CancellationTokenSource? _aiOrganiseCts;

    /// <summary>装载时把"上次整理好但还没应用"的方案递出来。<b>它必须看得见</b>：
    /// 用户中途关了设置页（甚至重启了程序），已经花掉的那次整理不该凭空消失。</summary>
    private void InitAiOrganiseSection()
    {
        AiGroupList.ItemsSource = _aiGroups;
        ShowPlan(PendingPlan());
        if (_aiPlan.IsEmpty) AiResumeButton.Visibility = Visibility.Collapsed;
        LocText.SetMessage(AiBatchLimitsText, DescribeFixedBatchLimits());
        // 置位这一格要压住 TextChanged：否则"打开设置页"这件事本身就是一次写盘（整档读写一遍）。
        _aiLoading = true;
        try
        {
            var seconds = App.Services.GetRequiredService<SettingsStore>().LoadAiBatchTimeout().Seconds;
            // 存档里显式写着默认值与"留空"是同一件事，界面上就同一个样子——不许出现两处看着不同、行为相同。
            AiTimeoutBox.Text = seconds == ClassifyRunner.DefaultTimeoutSeconds
                ? string.Empty
                : seconds.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _aiLoading = false;
        }
        RefreshAiTimeoutNote();
    }

    /// <summary>三格只读展示。<b>数字全部取自代码里的常数</b>：写死在界面里就会和常数分岔（同一份预算两处各说一次）。
    /// 不开放填写的理由也同批写出来——只留一句"这格不做"，用户无法判断这条决定对不对。</summary>
    private static LocMessage DescribeFixedBatchLimits()
        => LocMessage.Of("settings.ai.batchLimits", ClassifyPrompt.MaxItemsPerBatch,
            ClassifyPrompt.UserBudgetChars, ClassifyPrompt.MaxReferenceTags, AiRequest.MaxPromptChars);

    /// <summary>超时那一格的可设范围。<b>坏值必须当场说原因</b>：这一格用户可以手改存档，
    /// 静默回默认等于"改了没落盘"里最难自查的一种。当前值⛔ 在这句里再念一遍——框里就显示着它
    /// （等于默认时框是空的，那时由 <c>timeoutBlank</c> 那句接手）。</summary>
    private void RefreshAiTimeoutNote()
    {
        var (_, reason) = App.Services.GetRequiredService<SettingsStore>().LoadAiBatchTimeout();
        LocText.SetMessage(AiTimeoutNoteText, reason
            ?? LocMessage.Of("settings.ai.timeoutNote", ClassifyRunner.MinTimeoutSeconds,
                ClassifyRunner.MaxTimeoutSeconds, ClassifyRunner.DefaultTimeoutSeconds));
    }

    private void AiTimeoutBox_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_aiLoading) return;               // 同 PersistAiAndShow：装载时的置位不写盘
        var text = AiTimeoutBox.Text.Trim();
        var defaultSeconds = ClassifyRunner.DefaultTimeoutSeconds;
        // 只认不分组、不带文化的整数写法：「1,000」这类字符串按当前区域可能 parses，那不是用户想说的数。
        int? asked = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
        var store = App.Services.GetRequiredService<SettingsStore>();
        store.SaveAiBatchTimeout(asked);
        if (text.Length == 0)
            LocText.SetMessage(AiTimeoutNoteText, LocMessage.Of("settings.ai.timeoutBlank", defaultSeconds,
                ClassifyRunner.MinTimeoutSeconds, ClassifyRunner.MaxTimeoutSeconds));
        else if (asked is null)
            LocText.SetMessage(AiTimeoutNoteText,
                LocMessage.Of("settings.ai.timeoutNotInt", text, defaultSeconds));
        else if (ClassifyRunner.NormalizeTimeoutSeconds(asked) != asked)
            LocText.SetMessage(AiTimeoutNoteText, LocMessage.Of("settings.ai.timeoutOutOfRange", asked,
                ClassifyRunner.MinTimeoutSeconds, ClassifyRunner.MaxTimeoutSeconds, defaultSeconds));
        else
            RefreshAiTimeoutNote();   // 与装载那一拍同一颗键⛔ 这一格两处各写一遍（✅WI-61b2 那颗分岔）
    }


    /// <summary>"还没应用的方案"只从一个入口读（那条"砍掉不成类标签"的规则住在服务里）。
    /// 界面上再开一条直读存档的口子就是第二个事实源：旧档里的专有词会绕过那一刀又摆回预览。</summary>
    private ClassifyPlan PendingPlan() => App.Services.GetRequiredService<AiClassifyService>().LoadPending();

    private async void AiOrganise_Click(object sender, RoutedEventArgs e)
    {
        if (_aiOrganising) return;              // 忙的时候"开始"是灰的；这一道只防重入（连点 / 快捷键）

        var settings = ReadAiSettings();
        if (settings.Problem() is { } bad)
        {
            // 就地给原因，不让人对着一个灰按钮猜：通道没通是"点了没反应"最常见的原因
            LocText.SetMessage(AiStatusText,
                LocMessage.Of("settings.ai.status.organiseBlocked", LocMessage.Of("common.rawValue", bad)));
            return;
        }

        _aiOrganising = true;
        AiOrganiseButton.IsEnabled = false;      // 忙的时候"开始"让位给「停止」：一次点击只管一件事
        AiStopButton.Visibility = Visibility.Visible;
        AiApplyAllButton.IsEnabled = false;
        var cts = new CancellationTokenSource();
        _aiOrganiseCts = cts;
        try
        {
            var classifier = App.Services.GetRequiredService<AiClassifyService>();
            var limit = (int)Math.Round(AiScopeBox.Value);

            // §20.3 预估前置：发第一发之前把"N 批 ≈ X token"算给用户看，确认了才动手。
            // 口径与 service 内部一致（LoadCandidates + ClassifyRules.Split + Estimate）——
            // "弹窗说 15K 实际花 40K"比不报更伤信任，宁可多读一次候选也不许两处各算各的。
            LocText.SetMessage(AiStatusText, LocMessage.Of("settings.ai.status.estimating"));
            var candidates = await classifier.LoadCandidatesAsync(limit, CancellationToken.None);
            if (candidates.Count > 0)
            {
                var rulesJson = App.Services.GetRequiredService<SettingsStore>().LoadAiClassifyRulesJson();
                var (forAi, ruled) = ClassifyRules.Split(candidates, ClassifyRules.Merged(rulesJson));
                var catalog = await classifier.ReferenceTagsAsync(CancellationToken.None);
                var (batchCount, approxTokens) = ClassifyPrompt.Estimate(forAi, catalog);
                if (batchCount > 0 && !await ConfirmOrganiseAsync(candidates.Count, ruled.Count, batchCount, approxTokens))
                    return;    // finally 把忙碌态收回去，什么都没发
            }

            LocText.SetMessage(AiStatusText, LocMessage.Of("settings.ai.status.preparing"));
            var outcome = await classifier
                .OrganiseAsync(limit, status => LocText.SetMessage(AiStatusText, status),
                    (done, total) => LocText.SetMessage(AiStatusText,
                        LocMessage.Of("settings.ai.status.batchDone", done, total)), cts.Token);

            ShowPlan(outcome.Plan);
            LocText.SetMessage(AiStatusText, Describe(outcome));
        }
        catch (OperationCanceledException)
        {
            LocText.SetMessage(AiStatusText, LocMessage.Of("settings.ai.status.stoppedWithLeft"));
            ShowPlan(PendingPlan());
        }
        catch (Exception ex)
        {
            StarLog.Error("[AI] 批量整理失败", ex);
            LocText.SetMessage(AiStatusText,
                LocMessage.Of("settings.ai.status.organiseFailed", LocMessage.Of("common.rawValue", ex.Message)));
        }
        finally
        {
            _aiOrganising = false;
            _aiOrganiseCts = null;
            AiOrganiseButton.IsEnabled = true;
            AiStopButton.IsEnabled = true;
            AiStopButton.Visibility = Visibility.Collapsed;
            AiApplyAllButton.IsEnabled = !_aiPlan.IsEmpty;
            cts.Dispose();
            _ = RefreshAiUsageAsync();          // 这一轮烧了多少，面板当场跟上
        }
    }

    /// <summary>确认那一下（§20.3 预估前置）。<b>默认按钮是"取消"</b>：一个会花用户 token 的动作，
    /// 顺手回车等于没确认。</summary>
    private async Task<bool> ConfirmOrganiseAsync(int total, int ruled, int batches, long approxTokens)
    {
        var detail = ruled > 0
            ? LocMessage.Of("settings.ai.confirm.detailRuled", total, ruled, batches,
                NumberText.Grouped(approxTokens))
            : LocMessage.Of("settings.ai.confirm.detailPlain", total, batches, NumberText.Grouped(approxTokens));
        // 改道 ✅WI-61d 片1 那颗收身份的工厂：默认焦点在「取消」那一侧这件事它已经做了，⛔ 在这里再摆一次。
        // ⚠ 改口：原先那句指着「token 预算与用量」这颗抬头，而那五个字只活在两份注释里——屏幕上从来没有它。
        return await CenteredDialog.ConfirmAsync(
            LocMessage.Of("settings.ai.confirm.title"),
            LocMessage.Of("settings.ai.confirm.body", detail),
            LocMessage.Of("settings.ai.confirm.go"), LocMessage.Of("dialog.cancel"),
            owner: App.MainWindow, dedupeKey: "aiorganiseconfirm");
    }

    /// <summary>
    /// 停下这一轮。<b>停止不是"取消整场"</b>：按下去之后断的是正在飞的那一次请求（不再等它把
    /// 剩下的 token 吐完），后面的批次一个都不问，而停之前整理出来的那些照旧留在下面可以挑几组应用。
    /// </summary>
    private void AiStop_Click(object sender, RoutedEventArgs e)
    {
        if (!_aiOrganising) return;
        AiStopButton.IsEnabled = false;         // 按过一次就够了：正在停下这件事不需要第二次指令
        _aiOrganiseCts?.Cancel();
        LocText.SetMessage(AiStatusText, LocMessage.Of("settings.ai.status.stopping"));
    }

    /// <summary>一句话交代这一轮：<b>整理出多少、缺多少、坏了几批、以及"还没写进库"</b>。
    /// 只报"完成"会漏掉两种真话：模型答漏了条目、某些批次根本没成功。</summary>
    private static LocMessage Describe(OrganiseOutcome outcome) => LocMessage.Of("settings.ai.result.full",
        outcome.Plan.IsEmpty ? LocMessage.Of("settings.ai.result.none")
            : LocMessage.Of("settings.ai.result.count", outcome.Plan.ItemCount, outcome.AskedItems),
        outcome.MissingItems > 0 ? LocMessage.Of("settings.ai.result.missing", outcome.MissingItems) : null,
        outcome.UnknownOrdinals > 0 ? LocMessage.Of("settings.ai.result.unknown", outcome.UnknownOrdinals) : null,
        outcome.DroppedTags > 0
            ? outcome.DroppedItems > 0
                ? LocMessage.Of("settings.ai.result.droppedWith", outcome.DroppedTags, outcome.DroppedItems)
                : LocMessage.Of("settings.ai.result.dropped", outcome.DroppedTags)
            : null,
        outcome.StoppedBatches > 0 ? LocMessage.Of("settings.ai.result.stopped", outcome.StoppedBatches) : null,
        outcome.FailedBatches > 0
            ? outcome.FirstError is { } err
                ? LocMessage.Of("settings.ai.result.failedWith", outcome.FailedBatches, err)
                : LocMessage.Of("settings.ai.result.failed", outcome.FailedBatches)
            : null,
        // 那句尾巴里点名的那颗按钮⛔ 把名字抄进句子：改了它的说法，这句还在教人点一个不存在的名字。
        LocMessage.Of("settings.ai.result.tail", LocMessage.Of("settings.ai.applyGroup")));

    /// <summary>把一串<b>用户数据</b>（标签名／账本里的功能名）连成一句：连接这件事走键，
    /// ⛔ 在 C# 里 `string.Join("、", …)`——那是把这一族的标点钉死在中文那一侧（✅WI-60b 同一口径）。</summary>
    private static LocMessage? Joined(IEnumerable<LocMessage> parts, string joinKey)
    {
        LocMessage? acc = null;
        foreach (var part in parts)
            acc = acc is null ? part : LocMessage.Of(joinKey, acc, part);
        return acc;
    }

    private void ShowPlan(ClassifyPlan plan)
    {
        _aiPlan = plan;
        _aiGroups.Clear();
        foreach (var group in plan.Groups())
        {
            if (group.Ids.Count == 0) continue;
            var ids = new HashSet<long>(group.Ids);
            _aiGroups.Add(new AiGroupRow(group, plan.Proposals.Where(proposal => ids.Contains(proposal.Id)).ToList()));
        }
        AiApplyAllButton.IsEnabled = !plan.IsEmpty;
        AiResumeButton.Visibility = plan.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        LocText.SetMessage(AiResumeButton, plan.IsEmpty ? LocMessage.Of("settings.ai.resumeIdle")
            : LocMessage.Of("settings.ai.resumeCount", plan.ItemCount));
    }

    private void AiResume_Click(object sender, RoutedEventArgs e)
        => ShowPlan(PendingPlan());

    private async void AiApplyAll_Click(object sender, RoutedEventArgs e)
        => await ApplyAsync(_aiPlan, LocMessage.Of("settings.ai.applyScopeAll"));

    private async void AiApplyGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AiGroupRow row }) return;
        // 只切这一组的条目出去应用；"这一组"是用户点头的单位，不能顺手把别的组也写了
        await ApplyAsync(new ClassifyPlan(row.Proposals, _aiPlan.CreatedAt),
            LocMessage.Of("settings.ai.applyScopeGroup",
                Joined(row.Group.Tags.Select(tag => LocMessage.Of("common.rawValue", tag)),
                    "settings.ai.tagJoin")!));
    }

    /// <summary>只应用这一组：把组内条目从总方案里切出来交给服务，应用完再从预览里摘掉。</summary>
    private async Task ApplyAsync(ClassifyPlan plan, LocMessage label)
    {
        if (plan.IsEmpty)
        {
            LocText.SetMessage(AiStatusText, LocMessage.Of("settings.ai.applyNothing", label));
            return;
        }
        AiApplyAllButton.IsEnabled = false;
        try
        {
            var applied = await App.Services.GetRequiredService<AiClassifyService>()
                .ApplyAsync(plan, CancellationToken.None);
            // 应用过的从待处理方案里剔除，剩下的（用户还没点的那些）继续留在预览与存档里
            var appliedIds = plan.Proposals.Select(proposal => proposal.Id).ToHashSet();
            ShowPlan(new ClassifyPlan(
                _aiPlan.Proposals.Where(proposal => !appliedIds.Contains(proposal.Id)).ToList(), _aiPlan.CreatedAt));
            App.Services.GetRequiredService<AiClassifyService>().SavePending(_aiPlan);
            LocText.SetMessage(AiStatusText, applied > 0
                ? LocMessage.Of("settings.ai.applyDone", label, applied)
                : LocMessage.Of("settings.ai.applyNoop", label));
        }
        catch (Exception ex)
        {
            StarLog.Error("[AI] 应用整理结果失败", ex);
            LocText.SetMessage(AiStatusText,
                LocMessage.Of("settings.ai.status.writeFailed", LocMessage.Of("common.rawValue", ex.Message)));
        }
        finally
        {
            AiApplyAllButton.IsEnabled = !_aiPlan.IsEmpty;
        }
    }

    private void AiIgnoreGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AiGroupRow row }) return;
        ShowPlan(_aiPlan.WithoutGroup(row.Group));
        App.Services.GetRequiredService<AiClassifyService>().SavePending(_aiPlan);
        LocText.SetMessage(AiStatusText, _aiPlan.IsEmpty
            ? LocMessage.Of("settings.ai.rejectedAll")
            : LocMessage.Of("settings.ai.rejectedOne", row.Group.Ids.Count, _aiPlan.ItemCount));
    }
}

/// <summary>预览里"一组标签一样的条目"那一行。<b>标签集合与条目 id 都用字符串显示出来</b>：
/// 用户在应用之前要能看见"到底给哪几条打哪几个标签"，看不见就等于让人盲签。</summary>
public sealed class AiGroupRow
{
    public AiGroupRow(TagGroup group, IReadOnlyList<TagProposal> proposals)
    {
        Group = group;
        Proposals = proposals;
        TagLine = string.Join("、", group.Tags) + "（" + group.Ids.Count + " 条）";
        ItemLine = "条目编号 " + string.Join("、", group.Ids.Take(6))
            + (group.Ids.Count > 6 ? $"…（共 {group.Ids.Count} 条）" : string.Empty);
    }

    public TagGroup Group { get; }
    public string TagLine { get; }
    public string ItemLine { get; }
    public IReadOnlyList<TagProposal> Proposals { get; }
}
