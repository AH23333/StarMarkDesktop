#nullable enable
using System;
using System.Globalization;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 今日速览的收官口径（批次 WI-31，P-172 最后一行）：
/// ① 月题一键三列且**两个消费点同一颗键**（主行与月历头曾是各写一份的第二个主人）；
/// ② 农历区是"整区可见性"裁决——中文档出现、en/ja 折叠：判据是纯函数，可测；
///    折叠区的成品句**不进表**——把永不露面的句子登记进表＝假翻译，登记"没做"比假装"做了"诚实。
/// </summary>
public sealed class GlanceI18nTests : IDisposable
{
    private readonly AppLanguage _before = Locale.Current;

    public void Dispose()
    {
        Locale.TrySet(_before);
    }

    [Fact]
    public void MonthTitleHasThreeLanguageShapes()
    {
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal("2026年10月", Locale.Fmt("widget.glance.ym", 2026, 10));
        Locale.TrySet(AppLanguage.Japanese);
        Assert.Equal("2026年10月", Locale.Fmt("widget.glance.ym", 2026, 10));   // 同形是语言事实，名单在案
        Locale.TrySet(AppLanguage.English);
        Assert.Equal("2026/10", Locale.Fmt("widget.glance.ym", 2026, 10));      // 不加月名表也不补零：组件位读得出就够
        Assert.Equal("2026/3", Locale.Fmt("widget.glance.ym", 2026, 3));
    }

    [Fact]
    public void LunarZoneAppearsOnlyInChinese()
    {
        Assert.True(GlanceCalendar.ShowsLunarZone(AppLanguage.Chinese));
        Assert.False(GlanceCalendar.ShowsLunarZone(AppLanguage.English));
        Assert.False(GlanceCalendar.ShowsLunarZone(AppLanguage.Japanese));
    }

    /// <summary>源码正/反向钉（组件不跑在单测里，取词时机由源码形状把关）：
    /// 两个消费点必须都问同一颗键，旧的插值骨架不许以任何形式回来；农历区必须整个走可见性判据。</summary>
    [Fact]
    public void TheWidgetAsksTheSharedKeyAndNeverRebuildsTheOldShapes()
    {
        var src = fs_text();
        Assert.Equal(2, occurrences(src, "Locale.Fmt(\"widget.glance.ym\""));
        Assert.DoesNotContain("{today.Year}年{today.Month}月", src, StringComparison.Ordinal);
        Assert.DoesNotContain("{_calendarYear}年{_calendarMonth}月", src, StringComparison.Ordinal);
        Assert.Contains("GlanceCalendar.ShowsLunarZone(Locale.Current)", src, StringComparison.Ordinal);
        Assert.Contains("LunarBlock.Visibility = Visibility.Collapsed", src, StringComparison.Ordinal);
        Assert.Contains("CountdownBlock.Visibility = Visibility.Collapsed", src, StringComparison.Ordinal);
        // 农历/倒计时成品句没被误登记进表：折叠区的句子不进表（登记＝假装翻过）。
        // 判据按"键名族不存在"来，不按句子字面扫——"还有"这类词在别的合法句子里有权出现（普查教训）。
        Assert.DoesNotContain(UiStrings.All, e =>
            e.Key.StartsWith("widget.glance.", StringComparison.Ordinal)
            && (e.Key.Contains("lunar", StringComparison.Ordinal) || e.Key.Contains("countdown", StringComparison.Ordinal)));
    }

    /// <summary>表头逐取：曾经 static readonly 数组＝语言冻在首次触碰那一刻（WI-31 现行案）。</summary>
    [Fact]
    public void CalendarWeekHeadersAreTakenPerCallNotFrozenOnce()
    {
        var src = System.IO.File.ReadAllText(RepoRelative("src/StarMark.Core/Widgets/MonthGrid.cs"));
        Assert.DoesNotContain("static readonly string[] WeekHeaders", src, StringComparison.Ordinal);
        Assert.Contains("public static string[] WeekHeaders()", src, StringComparison.Ordinal);
        var widget = fs_text();
        Assert.Contains("MonthGrid.WeekHeaders()[c]", widget, StringComparison.Ordinal);
    }

    private static string fs_text() => System.IO.File.ReadAllText(RepoRelative("src/StarMark.UI/Views/GlanceWidget.xaml.cs"));

    private static string RepoRelative(string rel)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return System.IO.Path.Combine(dir!.FullName, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }

    private static int occurrences(string hay, string needle)
    {
        var n = 0;
        for (var i = hay.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = hay.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }
}
