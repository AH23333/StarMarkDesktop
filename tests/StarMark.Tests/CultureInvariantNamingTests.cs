#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using StarMark.Abstractions;
using StarMark.Integrations.GitHub;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 文件名里的日期段必须锁定公历（InvariantCulture）。th-TH（佛历）/ar-SA（希吉来历）等区域下，
/// 不加锁定会让 yyyyMMdd 采用 CurrentCulture 的默认历法，得到 2569/1447 这类年份——与文档承诺及
/// 行内 ISO 时间戳不一致，并破坏回滚点按文件名排序的时间序。
/// </summary>
public sealed class CultureInvariantNamingTests
{
    private static void WithCulture(string name, Action probe)
    {
        var saved = CultureInfo.CurrentCulture;
        var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            var culture = new CultureInfo(name);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            probe();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }

    [Theory]
    [InlineData("th-TH")]   // 佛历：公历 2026 会渲染成 2569
    [InlineData("ar-SA")]   // 希吉来历：年份差异更大
    public void StarLog_CurrentLogFile_AlwaysGregorianYear(string culture)
        => WithCulture(culture, () =>
        {
            var file = Path.GetFileName(StarLog.CurrentLogFile);
            var match = Regex.Match(file, @"^starmark-(\d{4})(\d{4})\.log$");
            Assert.True(match.Success, $"日志文件名格式异常: {file}");
            // DateTime.Now.Year 恒为公历内部表示；未锁 InvariantCulture 时这里会得到佛历/希吉拉年份
            Assert.Equal(DateTime.Now.Year.ToString(CultureInfo.InvariantCulture), match.Groups[1].Value);
        });

    /// <summary>
    /// 解析侧的文化免疫。<b>实测四文化对照</b>（th-TH / ar-SA / my-MM / zh-CN）：GitHub 现网回的
    /// <c>2024-05-01T12:00:00Z</c> 在四者下都得 1714564800 ⇒ "整批 Star 时间戳偏 540 年"这一说法
    /// 对现网载荷<b>不成立</b>（登记为证伪）。但日期一旦退化成 <c>2024-05-01</c>：th-TH 得
    /// -15420960000（把 2024 当佛历年＝公元 1481）、ar-SA 干脆解析失败、my-MM/zh-CN 得本地零点
    /// ⇒ 锁 InvariantCulture 是把日期解析从"机器文化 + 机器时区"手里拿走，属纵深防御。
    /// </summary>
    [Theory]
    [InlineData("th-TH")]   // 佛历
    [InlineData("ar-SA")]   // 希吉来历（Umm al-Qura）
    public void GitHubSource_ParseUnixTime_NotBentByMachineCalendar(string culture)
    {
        const string dateOnly = "2024-05-01";
        var expected = GitHubSource.ParseUnixTime(dateOnly);   // 本方法已锁文化 ⇒ 可在默认文化下取基准

        WithCulture(culture, () =>
        {
            // 前提自证：不锁文化时该日期拿不到正确瞬间（th-TH 偏 ~543 年、ar-SA 干脆解析失败），
            // 否则这条断言没有鉴别力。
            var parsedLoose = DateTimeOffset.TryParse(dateOnly, out var loose);
            Assert.False(parsedLoose && loose.ToUnixTimeSeconds() == expected);

            Assert.Equal(expected, GitHubSource.ParseUnixTime(dateOnly));
            Assert.Equal(1_714_564_800, GitHubSource.ParseUnixTime("2024-05-01T12:00:00Z"));
        });
    }

    [Fact]
    public void GitHubSource_ParseUnixTime_HonoursExplicitOffset_AndRejectsUnparseable()
    {
        // 显式 +08:00 与 Z 必须落到同一瞬间：锁定 InvariantCulture 不得顺手改掉偏移语义。
        Assert.Equal(1_714_564_800, GitHubSource.ParseUnixTime("2024-05-01T20:00:00+08:00"));
        Assert.Null(GitHubSource.ParseUnixTime(null));
        Assert.Null(GitHubSource.ParseUnixTime(""));
        Assert.Null(GitHubSource.ParseUnixTime("昨天"));
    }
}
