#nullable enable
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.Feed;

namespace StarMark.Core.Feed;

/// <summary>
/// 「RSS 这一栏到底开没开」的判据（只在这里说一次）。
/// <para>
/// 存档里刻意区分<b>两种"没开"</b>：<c>null</c> ＝ 从没表过态，<c>false</c> ＝ 明确关过。
/// 这一版之前只有源列表、没有总开关，所以老用户手里已经配好了地址，而那一栏里根本没有这个键——
/// 把"从没表过态"当成"关过"，升级后导航栏仍是空的，等于要他先去设置页翻一次开关才能看到本该自动出现的东西。
/// 反过来，明确关过之后<b>列表里还剩几个启用的源都不算数</b>：他刚按下的那一下必须生效，
/// 否则"关掉"就变成了"关掉，但如果你的源列表非空那就还开着"。</para>
/// <para>判据放在 Core 而不是 SettingsStore：后者在 UI 层，测试引用不到，写错了没有任何东西会红。</para>
/// </summary>
public static class RssActivation
{
    /// <summary>
    /// <paramref name="stated"/> 为 null（从没表过态）时按"列表里有没有启用的源"决定；
    /// 表过态就一律以它为准。源列表为 null（读档失败）时按未开启处理，不猜。
    /// </summary>
    public static bool IsOn(bool? stated, IReadOnlyList<RssSourceConfig>? sources)
        => stated ?? HasEnabledSource(sources);

    /// <summary>有没有至少一个"启用且还能用"的源（地址都不合法就不算，否则开关会为一个抓不动的源而开着）。</summary>
    public static bool HasEnabledSource(IReadOnlyList<RssSourceConfig>? sources)
        => sources is { } list
           && list.Any(s => s.Enabled && RssSourceConfig.UrlProblem(s.Url) is null);
}
