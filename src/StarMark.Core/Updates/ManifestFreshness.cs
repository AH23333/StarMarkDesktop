#nullable enable
using System;

namespace StarMark.Core.Updates;

/// <summary>
/// 一份清单的"新鲜度"有哪几种下落（批次 VZ）。
/// <para><b><see cref="Unknown"/> 是一等公民，不是失败</b>：这一欄是可选的（老清单没有它、
/// 对方没回发布时刻也没有它），而"缺信息就拒"会把现在装着的机器全部挡在门外——
/// 那是拿一条新加的判据去惩罚所有没犯错的机器。</para>
/// </summary>
public enum ManifestFreshnessVerdict
{
    /// <summary>判不了：清单没写生成时间，或这一次没拿到该版的发布时刻。<b>照常放行</b>，但要在日志里说出"没判"。</summary>
    Unknown,

    /// <summary>生成时间与这一版的发布时刻对得上（差在容差之内，或清单更晚——换载荷重签就是更晚）。</summary>
    Fine,

    /// <summary>清单说自己<b>比这一版发布还早得多</b>：它多半不是为这一版签的（发错纸、混了另一次构建的产物）。</summary>
    PredatesRelease,
}

/// <summary>
/// 清单生成时间与这一版发布时刻之间的<b>那道算术，只在这里做一次</b>（批次 VZ，P-165）。
/// <para>为什么判据是"清单 vs 发布时刻"而不是"清单 vs 本机现在"：后者要拿这台机器的钟去裁一条签名，
/// 而任何有限的年龄硬界都会把<b>我们自己的发布节奏变成用户的更新闸门</b>——
/// 一版挂了半年没发新版，那半年里"最新的那一版"就把自己全网拒绝了。
/// 那种坏没有任何用户侧出口（除了作者重签一次），所以它不算安全收益，算自伤。
/// 与本机时钟有关的那一档因此只做一件事：<b>说出来</b>（<see cref="DaysOld"/>），不拦。</para>
/// <para>为什么这一道算术不需要本机时钟：两个数都是对方服务器说的瞬间，差值是同一参照系里的减法。
/// 这也是为什么它可以硬拒而年龄那一档不行——它不依赖"这台机器的表准不准"。</para>
/// </summary>
public static class ManifestFreshness
{
    /// <summary>
    /// 清单可以比该版发布<b>早</b>多少。24 小时是照着我们自己的发布流程给的：
    /// 产树 → 打包 → 签名 → 建草稿 → 传三颗 → 公开，全程几十分钟；隔一整天才算异常，
    /// 是为了不把"提前一天签好、第二天才有空公开"这种正常操作判成事故。
    /// </summary>
    public static readonly TimeSpan ReleaseTolerance = TimeSpan.FromHours(24);

    /// <summary>清单生成时间距今超过这么久，就在日志里把天数说出来（<b>只说，不拦</b>）。</summary>
    public static readonly TimeSpan NoticeAfter = TimeSpan.FromDays(45);

    /// <summary>判一眼。<b>两个时刻任一缺席 ⇒ <see cref="Unknown"/></b>，不猜。</summary>
    public static ManifestFreshnessVerdict Decide(DateTimeOffset? generatedAtUtc, DateTimeOffset? publishedUtc)
    {
        if (generatedAtUtc is not { } generated || publishedUtc is not { } published)
            return ManifestFreshnessVerdict.Unknown;
        var gap = published.ToUniversalTime() - generated.ToUniversalTime();
        return gap > ReleaseTolerance ? ManifestFreshnessVerdict.PredatesRelease : ManifestFreshnessVerdict.Fine;
    }

    /// <summary>
    /// 这份清单距今多少天（日志用）。清单没写时间 ⇒ null。
    /// <para>本机钟走得比真实时间慢时差值会是负的——那一刻按 <b>0</b> 说。
    /// "签于 −3 天前"不是诚实，是把一个量不出来的东西硬摆成量出来了。</para>
    /// </summary>
    public static long? DaysOld(DateTimeOffset? generatedAtUtc, DateTimeOffset nowUtc)
    {
        if (generatedAtUtc is not { } generated) return null;
        var age = nowUtc.ToUniversalTime() - generated.ToUniversalTime();
        return age.Ticks <= 0 ? 0L : (long)age.TotalDays;
    }

    /// <summary>值不值得为"这份清单有点年头了"写一行日志。</summary>
    public static bool WorthMentioning(long? days) => days is { } d && d >= (long)NoticeAfter.TotalDays;
}
