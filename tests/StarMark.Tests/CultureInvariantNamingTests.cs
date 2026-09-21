#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using StarMark.Abstractions;
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
}
