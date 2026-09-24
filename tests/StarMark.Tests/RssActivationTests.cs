#nullable enable
using System;
using System.Collections.Generic;
using StarMark.Abstractions.Feed;
using StarMark.Core.Feed;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「RSS 这一栏算不算开着」的判据（批次 RB）。
/// <para>为什么值得单独钉：这一栏决定导航栏里有没有「RSS」项。写成 <c>stated ?? false</c> 的话，
/// 升级来的用户（手里已经配好了源、但那一版根本没有总开关这个键）打开应用什么也不会多出来——
/// 他会以为功能没做，而<b>这正是这一批要解决的投诉</b>。写成 <c>?? anyEnabled</c> 之后，
/// "明确关过"又必须仍然是关，否则他刚按下的那一下不算数。</para>
/// </summary>
public sealed class RssActivationTests
{
    private static RssSourceConfig Source(bool enabled = true, string url = "https://a.example.com/feed")
        => new(1, "A", url, enabled);

    private static IReadOnlyList<RssSourceConfig> Params(string label) => label switch
    {
        "empty" => Array.Empty<RssSourceConfig>(),
        "disabled" => new[] { Source(enabled: false) },
        "bad-url" => new[] { Source(url: "not-a-url") },
        _ => new[] { Source() },
    };

    [Theory]
    [InlineData("one", true)]        // 从没表过态 + 有可用源 ⇒ 开（升级来的用户不必多做一步）
    [InlineData("empty", false)]     // 一个源都没有 ⇒ 关（没有可抓的东西，出现一个空页更像故障）
    [InlineData("disabled", false)]  // 全是停用的 ⇒ 关
    [InlineData("bad-url", false)]   // 启用着但地址不合法 ⇒ 关：不为一个抓不动的源开着这一栏
    public void NeverStated_FallsBackToWhetherAnySourceIsUsable(string which, bool expected)
        => Assert.Equal(expected, RssActivation.IsOn(null, Params(which)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnceStated_TheStatementWins(bool stated)
    {
        // 明确关过：即使列表里还有启用的源，也不能替他打开
        Assert.Equal(stated, RssActivation.IsOn(stated, Params("one")));
        Assert.Equal(stated, RssActivation.IsOn(stated, Params("empty")));
    }

    [Fact]
    public void NullSourceList_IsNotTreatedAsOn()
        => Assert.False(RssActivation.IsOn(null, null));

    [Fact]
    public void StatedFalse_OverNullSourceList()
        => Assert.False(RssActivation.IsOn(false, null));

    [Fact]
    public void StatedTrue_OverNullSourceList()
        => Assert.True(RssActivation.IsOn(true, null));
}
