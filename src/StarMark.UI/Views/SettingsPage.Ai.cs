#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
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
    /// <summary>分类专用模型（§19 O6）暂无输入格，保存整组时透传这一格，防止被"看不见=清掉"。</summary>
    private string? _aiClassifyModelHeld;

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
            // 分类模型暂无输入格（用量面板一并补齐）：先持有读到的值，保存时原样带回——
            // 手改过 settings.json 的用户不该因为"按了一下总开关"被清零这一格。
            _aiClassifyModelHeld = stored.ClassifyModel;
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
        // 分类模型目前没有控件：读"上次 Load 到的那份"——保存永远写回完整组，谁都不被顺手清零。
        ClassifyModel: _aiClassifyModelHeld);

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
        AiStatusText.Text = settings.Problem() is { } bad
            ? "配置还不能用：" + bad
            : settings.DescribeWhere() + " —— 可以点「测试连接」确认服务真的答话。";
    }

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
            AiStatusText.Text = "正在取消…";
            return;
        }

        var settings = ReadAiSettings();
        _aiBusy = true;
        AiProbeButton.Content = "取消";
        AiFillModelButton.Visibility = Visibility.Collapsed;
        AiStatusText.Text = "正在问服务有哪些模型…";
        var cts = new CancellationTokenSource();
        _aiProbeCts = cts;
        try
        {
            var probe = await App.Services.GetRequiredService<AiGateway>().ProbeAsync(settings, cts.Token);
            AiStatusText.Text = probe.Report;

            // "通了但模型名不对"要能一步修好：把服务侧的名字直接递过去，而不是让用户自己抄一遍
            _aiModels = probe.Models;
            if (!probe.Ok && _aiModels.Count > 0)
            {
                AiFillModelButton.Content = "模型名改用「" + _aiModels[0] + "」";
                AiFillModelButton.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            AiStatusText.Text = "已经取消，没有等到答复。";
        }
        catch (Exception ex)
        {
            StarLog.Error("[AI] 连接自检失败", ex);
            AiStatusText.Text = "自检没能完成：" + ex.Message;
        }
        finally
        {
            _aiBusy = false;
            _aiProbeCts = null;
            AiProbeButton.Content = "测试连接";
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
            AiStatusText.Text = "还不能开始整理：" + bad;
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
            AiStatusText.Text = "正在算这一轮要花多少…";
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

            AiStatusText.Text = "正在准备…";
            var outcome = await classifier
                .OrganiseAsync(limit, status => AiStatusText.Text = status,
                    (done, total) => AiStatusText.Text = $"第 {done} / {total} 批完成，继续中…", cts.Token);

            ShowPlan(outcome.Plan);
            AiStatusText.Text = Describe(outcome);
        }
        catch (OperationCanceledException)
        {
            AiStatusText.Text = "已经停下。下面这些是停之前整理出来的，可以先挑几组应用。";
            ShowPlan(PendingPlan());
        }
        catch (Exception ex)
        {
            StarLog.Error("[AI] 批量整理失败", ex);
            AiStatusText.Text = "整理没能完成：" + ex.Message;
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
            ? $"共 {total} 条：{ruled} 条按高置信规则先定（不进模型）；其余分 {batches} 批问模型，约 {approxTokens:N0} token。"
            : $"共 {total} 条，分 {batches} 批问模型，约 {approxTokens:N0} token。";
        var dialog = new ContentDialog
        {
            Title = "开始 AI 整理？",
            Content = detail + "\n预算与历史用量见下方「token 预算与用量」。",
            PrimaryButtonText = "开始",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,   // Page 自身就是 FrameworkElement，取自己挂靠的 XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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
        AiStatusText.Text = "正在停下…（已经整理出来的会留在下面）";
    }

    /// <summary>一句话交代这一轮：<b>整理出多少、缺多少、坏了几批、以及"还没写进库"</b>。
    /// 只报"完成"会漏掉两种真话：模型答漏了条目、某些批次根本没成功。</summary>
    private static string Describe(OrganiseOutcome outcome)
    {
        var text = outcome.Plan.IsEmpty
            ? "没有整理出可用的建议"
            : $"整理出 {outcome.Plan.ItemCount} 条建议（问的是 {outcome.AskedItems} 条）";
        if (outcome.MissingItems > 0) text += $"，{outcome.MissingItems} 条模型没答";
        if (outcome.UnknownOrdinals > 0) text += $"，丢掉 {outcome.UnknownOrdinals} 个对不上号的编号";
        if (outcome.DroppedTags > 0)
            text += $"，砍掉 {outcome.DroppedTags} 个只挂在 1 条上的标签"
                + (outcome.DroppedItems > 0 ? $"（{outcome.DroppedItems} 条因此没有建议）" : string.Empty);
        if (outcome.StoppedBatches > 0) text += $"，{outcome.StoppedBatches} 批没问（已停止）";
        if (outcome.FailedBatches > 0)
            text += $"，{outcome.FailedBatches} 批没成功" + (outcome.FirstError is { } err ? $"（{err}）" : string.Empty);
        return text + "。点「应用这组」才会写进库。";
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
        AiResumeButton.Content = plan.IsEmpty ? "继续处理上次的整理结果"
            : $"继续处理上次整理好的 {plan.ItemCount} 条";
    }

    private void AiResume_Click(object sender, RoutedEventArgs e)
        => ShowPlan(PendingPlan());

    private async void AiApplyAll_Click(object sender, RoutedEventArgs e) => await ApplyAsync(_aiPlan, "全部");

    private async void AiApplyGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AiGroupRow row }) return;
        // 只切这一组的条目出去应用；"这一组"是用户点头的单位，不能顺手把别的组也写了
        await ApplyAsync(new ClassifyPlan(row.Proposals, _aiPlan.CreatedAt),
            $"「{string.Join("、", row.Group.Tags)}」这一组");
    }

    /// <summary>只应用这一组：把组内条目从总方案里切出来交给服务，应用完再从预览里摘掉。</summary>
    private async Task ApplyAsync(ClassifyPlan plan, string label)
    {
        if (plan.IsEmpty)
        {
            AiStatusText.Text = label + "没有可应用的内容。";
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
            AiStatusText.Text = applied > 0
                ? $"已把{label}的标签写进库：{applied} 条。"
                : $"{label}的标签其实都已经有了，没有需要新加的。";
        }
        catch (Exception ex)
        {
            StarLog.Error("[AI] 应用整理结果失败", ex);
            AiStatusText.Text = "写入没能完成：" + ex.Message;
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
        AiStatusText.Text = _aiPlan.IsEmpty
            ? "这一组不要了。剩下的预览已清空。"
            : $"这一组不要了（{row.Group.Ids.Count} 条），还剩 {_aiPlan.ItemCount} 条。";
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
