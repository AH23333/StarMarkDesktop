#nullable enable
using System;
using System.Linq;
using StarMark.Core.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 清单新鲜度那道算术（批次 VZ）。<b>全部用传进去的时刻，不读系统钟</b>——
/// 一个"每秒都在变"的输入会让每一格断言都变成"今天过明天不过"（UJ 那批清的就是这种）。
/// </summary>
public sealed class ManifestFreshnessTests
{
    private static readonly DateTimeOffset Published = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    // ===== Decide：只有"早于发布超过容差"这一种坏 =====

    [Fact]
    public void EitherSideMissingMeansTheJudgementIsSkippedRatherThanFailed()
    {
        Assert.Equal(ManifestFreshnessVerdict.Unknown, ManifestFreshness.Decide(null, null));
        Assert.Equal(ManifestFreshnessVerdict.Unknown, ManifestFreshness.Decide(Published, null));
        Assert.Equal(ManifestFreshnessVerdict.Unknown, ManifestFreshness.Decide(null, Published));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(13.5)]
    [InlineData(23.999)]
    public void AnythingUpToOneDayEarlyIsFine(double hoursBeforeRelease)
        => Assert.Equal(ManifestFreshnessVerdict.Fine,
            ManifestFreshness.Decide(Published.AddHours(-hoursBeforeRelease), Published));

    /// <summary>边界钉死在"正好 24 小时算正常"：容差是<b>闭区间</b>，多一Ticks 才算异常。</summary>
    [Fact]
    public void TheToleranceIsAClosedBoundaryOnTheFineSide()
    {
        Assert.Equal(ManifestFreshnessVerdict.Fine, ManifestFreshness.Decide(Published - ManifestFreshness.ReleaseTolerance, Published));
        Assert.Equal(ManifestFreshnessVerdict.PredatesRelease,
            ManifestFreshness.Decide(Published - ManifestFreshness.ReleaseTolerance - TimeSpan.FromTicks(1), Published));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(24 * 30)]
    public void LaterThanThatIsTheOneRealAnomaly(double hoursBeforeRelease)
        => Assert.Equal(ManifestFreshnessVerdict.PredatesRelease,
            ManifestFreshness.Decide(Published.AddHours(-hoursBeforeRelease), Published));

    /// <summary>清单<b>晚于</b>发布时刻是常态（发完之后换载荷重签），不许有任何一格把它判成坏。</summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(24 * 400)]
    public void ASigningDoneAfterTheReleaseIsNormal(double hoursAfterRelease)
        => Assert.Equal(ManifestFreshnessVerdict.Fine,
            ManifestFreshness.Decide(Published.AddHours(hoursAfterRelease), Published));

    /// <summary>两个时刻都带偏移时，比的是<b>同一瞬间</b>而不是各自的字面串。</summary>
    [Fact]
    public void OffsetsAreNormalisedBeforeTheSubtraction()
    {
        var sameInstantInTwoZones = new DateTimeOffset(2026, 10, 4, 20, 0, 0, TimeSpan.FromHours(8));
        Assert.Equal(ManifestFreshnessVerdict.Fine, ManifestFreshness.Decide(sameInstantInTwoZones, Published));
        Assert.Equal(ManifestFreshnessVerdict.PredatesRelease,
            ManifestFreshness.Decide(sameInstantInTwoZones.AddDays(-30), Published));
    }

    // ===== 年龄：只说，不拦 =====

    [Fact]
    public void AnAbsentTimestampHasNoAgeToReport()
        => Assert.Null(ManifestFreshness.DaysOld(null, Published));

    /// <summary>本机钟慢 ⇒ 差值是负的。那一刻按 0 说：<b>"签于 −3 天前"不是诚实</b>，
    /// 是把一个量不出来的东西硬摆成量出来了（与"说不稳的话就不上屏幕"同一条纪律）。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-400)]
    public void AClockBehindTheSigningTimeReportsZeroDays(double daysSkew)
        => Assert.Equal(0L, ManifestFreshness.DaysOld(Published.AddDays(-daysSkew), Published));

    [Theory]
    [InlineData(3, 3)]
    [InlineData(44, 44)]
    [InlineData(45, 45)]
    [InlineData(3650, 3650)]
    public void TheAgeIsJustTheDifferenceInDays(double days, long expected)
        => Assert.Equal(expected, ManifestFreshness.DaysOld(Published.AddDays(-days), Published));

    [Theory]
    [InlineData(null, false)]
    [InlineData(0L, false)]
    [InlineData(44L, false)]
    [InlineData(45L, true)]
    [InlineData(400L, true)]
    public void OnlyTheOldEndOfTheRangeIsWorthALogLine(long? days, bool expected)
        => Assert.Equal(expected, ManifestFreshness.WorthMentioning(days));

    [Fact]
    public void TheNoticeThresholdIsTheOnlyPlaceThatNumberAppears()
    {
        var code = Code(ReadRepoFile("src/StarMark.Core/Updates/ManifestFreshness.cs"));
        Assert.Equal(1, Count(code, "FromDays("));
        Assert.Equal(1, Count(code, "FromHours("));
        // 判据这颗类不许去碰系统时钟：一旦碰了，上面那批"传时刻进去"的用例就全成了摆设。
        Assert.DoesNotContain("DateTimeOffset.Now", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", code, StringComparison.Ordinal);
    }

    // ===== 措辞：三句话在 UpdatePolicy，别处不许再写一份 =====

    [Fact]
    public void TheThreeAgeSentencesAreDistinctAndCoverMissingSeparately()
    {
        Assert.Equal("清单没写生成时间（这一份不判新鲜度）", UpdatePolicy.DescribeManifestAge(null));
        Assert.Equal("清单是今天签的", UpdatePolicy.DescribeManifestAge(0));
        Assert.Equal("清单签于 3 天前", UpdatePolicy.DescribeManifestAge(3));
        var old = UpdatePolicy.DescribeManifestAge(45);
        Assert.Contains("45 天前", old, StringComparison.Ordinal);
        Assert.NotEqual(UpdatePolicy.DescribeManifestAge(44), old);
        Assert.NotEqual(UpdatePolicy.DescribeManifestAge(null), UpdatePolicy.DescribeManifestAge(0));
    }

    [Fact]
    public void TheUiAndTheStoreNeverPhraseThisThemselves()
    {
        foreach (var file in new[]
                 {
                     "src/StarMark.UI/Views/SettingsPage.Updates.cs",
                     "src/StarMark.UI/ViewModels/SettingsPageViewModel.Updates.cs",
                     "src/StarMark.UI/Helpers/SettingsStore.Updates.cs",
                 })
        {
            var code = Code(ReadRepoFile(file));
            Assert.DoesNotContain("天前", code, StringComparison.Ordinal);
            Assert.DoesNotContain("generatedAt", code, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 发布那一侧也要有证人：<b>签出来的那一刻必须写进被签的那串字节</b>。
    /// 界面上读不到这一欄有两种成因（对方真没写 / 我们自己没签进去），而第二种只有这一格能当场发现——
    /// 少了它，"全线都没有生成时间"会被读成"远端没给"，而那是发布链坏了。
    /// </summary>
    [Fact]
    public void TheSigningToolStampsTheManifestItSigns()
    {
        var code = Code(ReadRepoFile("tools/StarMark.UpdateSigner/Program.cs"));
        var ctor = Between(code, "var manifest = new ReleaseManifest(", ");");
        Assert.Contains("DateTimeOffset.UtcNow", ctor, StringComparison.Ordinal);
        Assert.DoesNotContain("null);", ctor, StringComparison.Ordinal);
    }

    /// <summary>
    /// 落盘那几格<b>共用同一个条件</b>：<c>这次到底拿到对方答复了没有</c>。
    /// 四格（问到的时刻／结论／标签／那一版的发布时刻）拆成两个写法，就会出现"标签是新的、时刻是旧的"
    /// 那种半套状态——而"新标签配旧时刻"恰好会喂给清单新鲜度一个假参照（#189/#193 那一族：一处事实两个写法）。
    /// 这里数的是 4 而不是 2：条件每多用一次都说明它在这些地方是同一条判断，不是在各自猜。
    /// </summary>
    [Fact]
    public void TheArchivedTagAndItsReleaseTimeAreWrittenUnderOneCondition()
    {
        var code = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdateService.cs"));
        var block = Between(code, "var updated = stored with", "};");
        Assert.Contains("LastRemoteTag = UpdatePolicy.CountsAsProbed(verdict) ? release?.Tag : stored.LastRemoteTag,",
            block, StringComparison.Ordinal);
        Assert.Contains("LastPublishedUtc = UpdatePolicy.CountsAsProbed(verdict) ? release?.PublishedUtc : stored.LastPublishedUtc,",
            block, StringComparison.Ordinal);
        Assert.Equal(4, Count(block, "UpdatePolicy.CountsAsProbed(verdict)"));
    }
}
