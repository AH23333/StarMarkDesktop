#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using StarMark.Abstractions.Trending;
using StarMark.Core.Hotkeys;
using StarMark.Core.Performance;
using StarMark.Integrations.Weather;
using StarMark.UI.Services;   // 热键注册投影要问"此刻的会话态"（架构方案 §6.1，投影只有这一处）

namespace StarMark.UI.Helpers;

/// <summary>
/// SettingsStore 的这一段——AI 助手那组：配置、额度计划、分类规则与即时助手。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsStore
{

    /// <summary>读出 AI 通道配置。<b>档位序号认不出来时退回默认而不是抛</b>：这一格是用户可手改的
    /// JSON 数字，写错一个数字不该让设置页打不开。</summary>
    public AiSettings LoadAiSettings()
    {
        var d = Load();
        var kind = d?.AiProvider is int raw && Enum.IsDefined(typeof(AiProviderKind), raw)
            ? (AiProviderKind)raw
            : AiProviderKind.Ollama;
        return new AiSettings(
            Enabled: d?.AiEnabled == true,
            Provider: kind,
            Model: d?.AiModel,
            ApiKey: d?.AiApiKey,
            OllamaBaseUrl: d?.AiOllamaBaseUrl,
            BaseUrl: d?.AiBaseUrl,
            ClassifyModel: d?.AiClassifyModel);
    }

    /// <summary>整组一次写入。<b>刻意不提供"只改一个字段"的写法</b>：这一组字段互相才有意义
    /// （通道换了，Key 与地址的必填性跟着变），分开写会出现"Ollama 却带着 https 校验"的中间态。
    /// <para>注意：全组语义意味着<b>调用方交回来的必须是自己读到的完整一份</b>——
    /// 「分类模型」还没有界面输入（3b 批补上）前，设置页保存时把它从上一次 Load 的值原样带上，
    /// 否则用户手改 settings.json 里这一格、设置页一按开关就清零（"改了没落盘"最难自查的一种）。</para></summary>
    public void SaveAiSettings(AiSettings settings)
    {
        var d = Load() ?? new SettingsData();
        d.AiEnabled = settings.Enabled;
        d.AiProvider = (int)settings.Provider;
        d.AiModel = string.IsNullOrWhiteSpace(settings.Model) ? null : settings.Model.Trim();
        d.AiApiKey = string.IsNullOrWhiteSpace(settings.ApiKey) ? null : settings.ApiKey.Trim();
        d.AiOllamaBaseUrl = string.IsNullOrWhiteSpace(settings.OllamaBaseUrl) ? null : settings.OllamaBaseUrl.Trim();
        d.AiBaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl.Trim();
        d.AiClassifyModel = string.IsNullOrWhiteSpace(settings.ClassifyModel) ? null : settings.ClassifyModel.Trim();
        Save(d);
    }

    /// <summary>读出还没应用的整理方案。读不出来按"没有方案"处理：<b>这一栏只是缓存性质，
    /// 坏了不该让设置页打不开</b>（真正的数据——条目与标签——都在库里，没写进来过）。</summary>
    public ClassifyPlan LoadAiPlan()
    {
        var json = Load()?.AiPendingPlanJson;
        if (string.IsNullOrWhiteSpace(json)) return ClassifyPlan.Empty;
        try
        {
            var plan = JsonSerializer.Deserialize<AiPlanRow>(json);
            if (plan is null) return ClassifyPlan.Empty;
            var proposals = new List<TagProposal>();
            foreach (var row in plan.Items ?? new List<AiProposalRow>())
            {
                if (row is not { Id: > 0 } || row.Tags is not { Count: > 0 } tags) continue;
                var clean = TagText.Sanitize(tags);          // 存档里的标签再过一次闸门：那是用户可以手改的文件
                if (clean.Count > 0) proposals.Add(new TagProposal(row.Id, clean));
            }
            return new ClassifyPlan(proposals, plan.At == default ? DateTimeOffset.UtcNow : plan.At);
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[AI] 待应用的整理方案读不出，按没有方案处理：{ex.Message}");
            return ClassifyPlan.Empty;
        }
    }

    public void SaveAiPlan(ClassifyPlan plan)
    {
        var d = Load() ?? new SettingsData();
        if (plan.IsEmpty) d.AiPendingPlanJson = null;
        else d.AiPendingPlanJson = JsonSerializer.Serialize(new AiPlanRow
        {
            At = plan.CreatedAt,
            Items = plan.Proposals.Select(proposal => new AiProposalRow
            {
                Id = proposal.Id,
                Tags = proposal.Tags.ToList(),
            }).ToList(),
        });
        Save(d);
    }

    public void ClearAiPlan()
    {
        var d = Load() ?? new SettingsData();
        d.AiPendingPlanJson = null;
        Save(d);
    }

    /// <summary>读预算档位。两格都容错（这文件用户可以手改）：预算数字认不出来回默认、
    /// 不抛；熔断位 null 按"没过"。<b>读出的值一律过一遍 <see cref="AiBudget.WithMonthlyTokens"/></b>——
    /// 手改成 1 token 的档位在语义上不该等于"关了 AI"。</summary>
    public AiBudget LoadAiBudget()
    {
        var d = Load();
        var budget = new AiBudget(AiBudget.DefaultMonthlyTokens, d?.AiBudgetPaused == true);
        return d?.AiTokenBudget is { } tokens ? budget.WithMonthlyTokens(tokens) : budget;
    }

    /// <summary>预算整组写入（额度 + 熔断位）。<b>不给"只改一位"的写法</b>：
    /// 把"改额度"与"消熔断"分开调，会造出"额度升到天上、熔断位还亮着"的鬼状态——
    /// 用户在面板上调了额、功能照旧被拒，且找不到按钮解释这件事。</summary>
    public void SaveAiBudget(AiBudget budget)
    {
        var d = Load() ?? new SettingsData();
        d.AiTokenBudget = budget.MonthlyTokenBudget;
        d.AiBudgetPaused = budget.PausedByBudget;
        Save(d);
    }

    /// <summary>整理规则的原文（解析与"坏 JSON 出声"归 <c>ClassifyRules.Merged</c> 与用量面板，
    /// store 不做二次校验——存档格式的认知不该漏到持久层去）。</summary>
    public string? LoadAiClassifyRulesJson() => Load()?.AiClassifyRulesJson;

    public void SaveAiClassifyRulesJson(string? json)
    {
        var d = Load() ?? new SettingsData();
        d.AiClassifyRulesJson = string.IsNullOrWhiteSpace(json) ? null : json;
        Save(d);
    }

    /// <summary>收藏即时分类开关（§19 O5）。默认 false；setter 留给 3b 的 UI（本轮先支持手改生效）。</summary>
    public bool LoadAiInstantEnabled() => Load()?.AiInstantClassify == true;

    public void SaveAiInstantEnabled(bool enabled)
    {
        var d = Load() ?? new SettingsData();
        d.AiInstantClassify = enabled;
        Save(d);
    }

    /// <summary>方案的落盘形状。<b>不直接序列化 <see cref="ClassifyPlan"/> 本身</b>：那会让记录类型的
    /// 内部结构变成存档格式，将来给方案加一个字段就会读到旧档里的 null 集合。</summary>
    private sealed class AiPlanRow
    {
        public DateTimeOffset At { get; set; }
        public List<AiProposalRow>? Items { get; set; }
    }

    private sealed class AiProposalRow
    {
        public long Id { get; set; }
        public List<string>? Tags { get; set; }
    }
}
