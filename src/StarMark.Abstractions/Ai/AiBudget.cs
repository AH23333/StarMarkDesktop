#nullable enable
using System;

namespace StarMark.Abstractions.Ai;

/// <summary>
/// token 预算的裁决件（§20.2）。<b>整类是无依赖纯函数：熔断是"账 + 档位"的算术，
/// 不是散在界面各处的 if</b>——判额、警告、手动恢复三条规则只在这里各写一遍，
/// 设置页与功能闸门读同一个裁决，才不会长出一个"显示没超、却被拦下"或反向的分岔。
/// 窗口锚点也不在这里算（那是仓储层按表内最大 at 滚动的事，§20.5），这里只吃"窗口内已用多少"。
/// </summary>
public sealed record AiBudget(long MonthlyTokenBudget, bool PausedByBudget)
{
    /// <summary>出厂默认（§20.2：500K/月）。<b>预算不区分云端与本地</b>：本地 Ollama 不花钱，
    /// 但 §19.1 的账——150K token ≈ 1–2 小时推理——说明它的"账"由钱换成时间后同样要有人喊停；
    /// 两类通道烧的是同一个东西（token），闸门就只该有一道。</summary>
    public const int DefaultMonthlyTokens = 500_000;

    /// <summary>月度窗口的起点（Unix 秒）：<b>以账本里的最大 at 为"现在"往回推 30 天</b>（§20.5），
    /// 不读墙钟——墙钟可被用户改，账本改不动。</summary>
    public static long MonthlyFrom(long maxRecordedAtUnixSec) => maxRecordedAtUnixSec - 30L * 24 * 3600;

    /// <summary>警告线（80%）。到线不拦截，只把话说明白——预算的意义是"看得境"，不是"暗中止"。</summary>
    public const double WarnRatio = 0.8;

    /// <summary>最低可调到的预算。<b>不给"预算=0 变成一键禁 AI"</b>：禁 AI 的开关是总开关（AiEnabled），
    /// 预算那一格改了不该长出第二含义；真要"永不超"是关总开关，而不是把预算调到 1 token。</summary>
    public const int MinMonthlyTokens = 10_000;

    public static AiBudget Default { get; } = new(DefaultMonthlyTokens, false);

    /// <summary>换算一个合法档位：<b>负数/0 是坏值回默认，小正数是"想要的值够不着"才夹紧</b>——
    /// 手滑打了 -7 与故意打 100 是两类输入，前者"回到从没设过"才是可解释的。</summary>
    public AiBudget WithMonthlyTokens(long tokens)
        => tokens <= 0 ? this with { MonthlyTokenBudget = DefaultMonthlyTokens }
                       : this with { MonthlyTokenBudget = Math.Clamp(tokens, MinMonthlyTokens, 1_000_000_000) };

    /// <summary>一次裁决。<paramref name="windowUsedTokens"/>＝账本在<b>当前窗口</b>里的合计。</summary>
    public AiBudgetVerdict Judge(long windowUsedTokens)
    {
        var remaining = Math.Max(0, MonthlyTokenBudget - windowUsedTokens);
        var over = windowUsedTokens >= MonthlyTokenBudget;
        if (over && !PausedByBudget) return new AiBudgetVerdict(AiBudgetState.Tripping, windowUsedTokens, remaining);
        if (PausedByBudget) return new AiBudgetVerdict(AiBudgetState.Blocked, windowUsedTokens, remaining);
        if (windowUsedTokens >= (long)(MonthlyTokenBudget * WarnRatio))
            return new AiBudgetVerdict(AiBudgetState.Warning, windowUsedTokens, remaining);
        return new AiBudgetVerdict(AiBudgetState.Ok, windowUsedTokens, remaining);
    }
}

public enum AiBudgetState
{
    /// <summary>放行，且不需要提预算的事。</summary>
    Ok,
    /// <summary>放行，但已过 80%：界面上要说出来（"再用 N token 就熔断"）。</summary>
    Warning,
    /// <summary>这一次越线：<b>拦下 + 同时持久化熔断位</b>（Tripping 与 Blocked 分开，是要让"刚刚熔掉"
    /// 能给出一句"为什么突然不能用了"的原地解释，而不是下次才说明白）。</summary>
    Tripping,
    /// <summary>熔断位亮着：无论窗口现在有没有滚回限内，都等用户手动点「恢复」。</summary>
    Blocked,
}

/// <param name="RemainingTokens">预算内剩余。</param>
public readonly record struct AiBudgetVerdict(AiBudgetState State, long Used, long Remaining)
{
    public bool AllowsCall => State is AiBudgetState.Ok or AiBudgetState.Warning;
}
