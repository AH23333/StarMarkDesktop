#nullable enable
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 世界时钟纯逻辑测试：显示名裁剪、跨日标注、时区解析兜底、去重与上限、默认点位可用。
/// </summary>
public sealed class WorldClockPolicyTests
{
    [Theory]
    [InlineData("(UTC+08:00) 北京", "北京")]
    [InlineData("(UTC) 都柏林、爱丁堡", "都柏林、爱丁堡")]
    [InlineData("(UTC-05:00) Eastern Time (US & Canada)", "Eastern Time (US & Canada)")]
    [InlineData("  北京  ", "北京")]
    public void ShortName_StripsTheOffsetPrefix(string display, string expected)
        => Assert.Equal(expected, WorldClockPolicy.ShortName(display));

    [Fact]
    public void ShortName_NeverReturnsEmpty()
    {
        // 显示名只剩一个括号段时，裁完为空 ⇒ 回退整串，界面上不能出现无名行
        Assert.Equal("(UTC+08:00)", WorldClockPolicy.ShortName("(UTC+08:00)"));
        Assert.Equal("未命名时区", WorldClockPolicy.ShortName("   "));
        Assert.Equal("未命名时区", WorldClockPolicy.ShortName(null!));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "明天")]
    [InlineData(-1, "昨天")]
    [InlineData(2, "+2 天")]
    [InlineData(-3, "-3 天")]
    public void DayLabel_NamesEveryNonToday(int diff, string expected)
        => Assert.Equal(expected, WorldClockPolicy.DayLabel(diff));

    [Theory]
    [InlineData(8, "UTC+08:00")]
    [InlineData(-5, "UTC-05:00")]
    [InlineData(0, "UTC+00:00")]
    [InlineData(5, "UTC+05:30")]
    public void OffsetLabel_IsPaddedAndSigned(int hours, string expected)
        => Assert.Equal(expected, WorldClockPolicy.OffsetLabel(
            new TimeSpan(hours, hours == 5 ? 30 : 0, 0)));

    [Fact]
    public void DefaultCities_AllResolveOnThisMachine()
    {
        // 默认点位如果落在一个本机没有的时区 Id 上，新装用户第一屏就是四行错误
        Assert.NotEmpty(WorldClockPolicy.DefaultCities);
        foreach (var city in WorldClockPolicy.DefaultCities)
        {
            Assert.NotNull(WorldClockPolicy.TryResolve(city.ZoneId));
            Assert.False(string.IsNullOrWhiteSpace(city.Name));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NoSuch/Zone Id")]
    public void TryResolve_ReturnsNullInsteadOfThrowing(string? zoneId)
        => Assert.Null(WorldClockPolicy.TryResolve(zoneId));

    [Fact]
    public void CanAdd_RejectsDuplicateCaseInsensitivelyAndRespectsCap()
    {
        var one = new WorldClockCity("W. Europe Standard Time", "柏林");
        Assert.False(WorldClockPolicy.CanAdd(new[] { one }, one.ZoneId));
        Assert.False(WorldClockPolicy.CanAdd(new[] { one }, "w. europe standard time"));  // 时区 Id 大小写不敏感
        Assert.True(WorldClockPolicy.CanAdd(new[] { one }, "Tokyo Standard Time"));

        var atCap = Enumerable.Range(0, WorldClockPolicy.MaxCities)
            .Select(i => new WorldClockCity($"Zone-{i}", $"城{i}")).ToList();
        Assert.False(WorldClockPolicy.CanAdd(atCap, "Tokyo Standard Time"));
    }

    [Fact]
    public void MaxCities_LeavesRoomForEveryCollaborationBand()
        => Assert.InRange(WorldClockPolicy.MaxCities, 4, 12);
}
