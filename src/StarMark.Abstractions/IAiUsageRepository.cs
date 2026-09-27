#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Ai;

namespace StarMark.Abstractions;

/// <summary>一条用量记录：谁花的、多少 token、数字是哪来的（原数还是折算）。</summary>
/// <param name="Model">配置里的模型名；没填则 null——<b>记"当时实际用的配置"而不是猜</b>，
/// 空着就空着，比编一个"未知模型"诚实。</param>
public sealed record AiUsageEntry(
    DateTimeOffset At,
    string Feature,
    string Provider,
    string? Model,
    AiUsage Usage);

/// <summary>一段窗口内的用量汇总（设置页面板与预算闸门的共同数据源——<b>判额和展示读同一份，
/// 两处各算一个数就是"显示没超但被熔断"的诞生方式</b>）。</summary>
public sealed record AiUsageTotals(int Calls, long InputTokens, long OutputTokens, int EstimatedCalls)
{
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>窗口内某一项（功能名或模型名）的分布。</summary>
public sealed record AiUsageSlice(string Name, int Calls, long TotalTokens, bool EstimatedOnly);

/// <summary>
/// AI 用量计量的读写面（§20.1）。<b>写只有一处</b>：每次真正发出并获得答复的调用结束后记一行；
/// "配置能连但还没干活"（测试连接）不记——那是探测不是消耗。
/// 月度窗口不以墙钟为基准而以 <see cref="MaxRecordedAtAsync"/> 为准（§20.5：改系统时间绕预算）。
/// </summary>
public interface IAiUsageRepository
{
    Task LogAsync(AiUsageEntry entry, CancellationToken ct = default);

    /// <summary>聚合 at ≥ 指定时刻的全部记录。<paramref name="fromUnixSec"/> 由调用方定窗口。</summary>
    Task<AiUsageTotals> TotalsSinceAsync(long fromUnixSec, CancellationToken ct = default);

    /// <summary>窗口内按功能分组（feature 列）。</summary>
    Task<IReadOnlyList<AiUsageSlice>> ByFeatureSinceAsync(long fromUnixSec, CancellationToken ct = default);

    /// <summary>窗口内按模型分组（model 列；没记模型的行归入 <paramref name="Name"/> 为 null 之外？——不，
    /// 归入空名切片，由界面显示成「未记模型」。<b>分组不能悄悄丢行</b>：丢了分布就对不上总数）。</summary>
    Task<IReadOnlyList<AiUsageSlice>> ByModelSinceAsync(long fromUnixSec, CancellationToken ct = default);

    /// <summary>表内最大 at（Unix 秒；空表返回 null）。月度窗口、熔断展示都以它为"现在"。</summary>
    Task<long?> MaxRecordedAtAsync(CancellationToken ct = default);
}
