#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;

namespace StarMark.Integrations.Ai;

/// <summary>一次连接自检的回报。<b>Report 是要直接显示给用户看的那一句</b>，
/// 所以它必须说清"找过哪里、按哪种通道、结果如何"，而不是"成功/失败"。</summary>
public sealed record AiProbe(bool Ok, string Report, IReadOnlyList<string> Models)
{
    /// <summary>失败时也带上清单（只要服务答过话）：界面上"改用本机真有那个模型"的按钮要靠它。</summary>
    public static AiProbe Bad(string report, IReadOnlyList<string>? models = null)
        => new(false, report, models ?? Array.Empty<string>());
}

/// <summary>
/// AI 通道的唯一入口。<b>"能不能用"只在这里判一次，"用哪个实现"只在这里选一次</b>——
/// 扩展项目两处各写一遍 <c>apiKey 非空</c> 结果漏掉 Ollama 免 Key，出现了"本机明明能用，
/// 界面说没配置"的故障；这类闸门分两处写迟早再分叉一次。
/// </summary>
public sealed class AiGateway : IDisposable
{
    private readonly OllamaProvider _ollama = new();
    private readonly OpenAiCompatibleProvider _openAi = new();

    /// <summary>配置能不能用。null＝可用，否则是"照着改就行"的原因。</summary>
    public string? ConfigProblem(AiSettings settings) => settings.Problem();

    public IAiProvider Of(AiProviderKind kind) => kind switch
    {
        AiProviderKind.Ollama => _ollama,
        AiProviderKind.OpenAiCompatible => _openAi,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "没有这个通道"),
    };

    /// <summary>
    /// 发一次对话。<b>配置不成立时直接回原因，一个包都不发</b>：
    /// 未开启的通道还在偷偷连外部端点，就是"AI 默认关闭"这条边界说过的话不算数。
    /// </summary>
    public async Task<AiReply> CompleteAsync(AiSettings settings, AiRequest request, CancellationToken ct)
    {
        if (settings.Problem() is { } bad)
            return AiReply.Fail(AiFailureKind.NotConfigured, bad);
        try
        {
            return await Of(settings.Provider).CompleteAsync(settings, request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            StarLog.Error($"[AI] 调用失败（{settings.DescribeWhere()}）", ex);
            return AiReply.Fail(AiFailureKind.Unknown, ex.Message);
        }
    }

    /// <summary>
    /// 「测试连接」：问服务有哪些模型，并核对配置里那个模型名在不在。
    /// <para>这一步刻意<b>不</b>先过 <see cref="AiSettings.Problem"/>：用户点它的时候往往正是还没配好的时候，
    /// 拦住不让人试，等于把唯一的诊断入口锁在诊断对象后面。地址/Key 的缺失由下面逐条报出来。</para>
    /// </summary>
    public async Task<AiProbe> ProbeAsync(AiSettings settings, CancellationToken ct)
    {
        var url = settings.EffectiveBaseUrl;
        if (AiSettings.UrlProblem(url, settings.Provider) is { } bad) return AiProbe.Bad("服务地址不成形：" + bad);

        try
        {
            var inventory = await Of(settings.Provider).ListModelsAsync(settings, ct);
            var report = inventory.Explain(settings.DescribeWhere(), settings.Model);
            var matches = string.IsNullOrWhiteSpace(settings.Model)
                || AiWire.ListsModel(settings.Model, inventory.Models);
            // "通了但模型名不对"不算成功：那恰恰是最需要人来处理的一种状态。清单原样带出去
            return inventory.Ok && matches
                ? new AiProbe(true, report, inventory.Models)
                : AiProbe.Bad(report, inventory.Models);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            StarLog.Warn($"[AI] 连接自检异常（{settings.DescribeWhere()}）：{ex.Message}");
            return AiProbe.Bad(AiFailures.Reason(AiFailureKind.Unknown, ex.Message, settings.DescribeWhere()));
        }
    }

    public void Dispose()
    {
        _ollama.Dispose();
        _openAi.Dispose();
    }
}
