#nullable enable
using System;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 今日速览的收官口径（批次 WI-31 立的 P-172 最后一行，批次 WI-56／P-186 改了其中"整区折叠"那一半）：
/// ① 月题一键三列且<b>两个消费点同一颗键</b>（主行与月历头曾是各写一份的第二个主人）；
/// ② <b>农历日序那一行</b>仍只中文档出现（成品词、⛔ 另一种语言的说法）；
/// ③ <b>节日与倒计时那一行三语都有</b>——各念自己那张清单（用户 2026-10-09 裁：
/// 「只放公历，或者按照美国/日本的日历和传统节日编排」，取第二条）。
/// 于是"折叠区的句子不进表"那条旧口径在这一族作废：那两句话现在是<b>会露面的句子</b>，必须住表里。
/// </summary>
[Collection("LocaleGlobal")]
public sealed class GlanceI18nTests : IDisposable
{
    private readonly AppLanguage _before = Locale.Current;

    public void Dispose() => Locale.TrySet(_before);

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

    /// <summary>农历<b>日序</b>只中文档出现；三档都有节日清单（那颗判据在 GlanceCalendar 那一侧，这里钉界面读的那一颗）。</summary>
    [Fact]
    public void LunarDayLineAppearsOnlyInChinese()
    {
        Assert.True(GlanceCalendar.ShowsLunarDayText(AppLanguage.Chinese));
        Assert.False(GlanceCalendar.ShowsLunarDayText(AppLanguage.English));
        Assert.False(GlanceCalendar.ShowsLunarDayText(AppLanguage.Japanese));
    }

    /// <summary>
    /// 源码正向钉：两个消费点都问同一颗 <c>widget.glance.ym</c>；日期区那三句都走表；
    /// 旧的插值骨架（「农历{lunar} · {festival}」「距「…」还有 N 天（M月D日）」）以任何形式回来都红——
    /// 那是"句子住在代码里"的第二个出处（✅WI-46b 那颗分隔符、坑表 #371 同一条界线）。
    /// </summary>
    [Fact]
    public void TheWidgetAsksTheSharedKeyAndNeverRebuildsTheOldShapes()
    {
        var src = fs_text();
        // ⚠ 禁项读<b>抹掉注释</b>那一份（台架规则 #131）：注释里引用一句旧话是为了讲清为什么，
        //   按原文数会把那种引用判成违规，而假失败教人删注释换绿灯。
        var code = SourceGate.Code(src);
        Assert.Equal(2, occurrences(src, "Locale.Fmt(\"widget.glance.ym\""));
        Assert.DoesNotContain("{today.Year}年{today.Month}月", code, StringComparison.Ordinal);
        Assert.DoesNotContain("{_calendarYear}年{_calendarMonth}月", code, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"农历{lunar}", code, StringComparison.Ordinal);
        Assert.DoesNotContain("距「{next.Name}」还有", code, StringComparison.Ordinal);
        Assert.Contains("GlanceCalendar.ShowsLunarDayText(Locale.Current)", src, StringComparison.Ordinal);
        Assert.Contains("Locale.Fmt(\"widget.glance.lunarDay\", lunar)", src, StringComparison.Ordinal);
        Assert.Contains("Locale.Fmt(\"widget.glance.lunarDayWithFestival\", lunar, festival)", src, StringComparison.Ordinal);
        Assert.Contains("Locale.Fmt(\"widget.glance.nextFestival\", next.Name,", src, StringComparison.Ordinal);
        Assert.Contains("LunarBlock.Visibility = string.IsNullOrEmpty(LunarBlock.Text) ? Visibility.Collapsed : Visibility.Visible",
            src, StringComparison.Ordinal);
    }

    /// <summary>
    /// 这两句是<b>当场算出来的读数</b>（倒计时每天在变）⇒ 取词必须发生在算的那一刻，而换语言那一刻
    /// 必须由<b>本组件自己订阅</b> <c>Locale.Changed</c> 重贴（坑表 #361：<c>LocText.SetKey</c> 连实参一起冻住；
    /// #386：长命宿主自己订阅并与摘掉成对）。⛔ 给这一族挂 LocText——那是"名单里多一条贴不出去的空壳"。
    /// </summary>
    [Fact]
    public void TheReadoutLinesRefetchAtTheMomentTheyAreShownAndTheWidgetUnsubscribes()
    {
        var src = fs_text();
        Assert.Contains("Locale.Changed += RebuildWords", src, StringComparison.Ordinal);
        Assert.Contains("Locale.Changed -= RebuildWords", src, StringComparison.Ordinal);
        Assert.DoesNotContain("LocText.SetKey(LunarBlock", src, StringComparison.Ordinal);
        Assert.DoesNotContain("LocText.SetKey(CountdownBlock", src, StringComparison.Ordinal);
        // 摘掉那颗必须挂在 Unloaded 里（挂在 Loaded 里就是"越挂越少直到没有"）
        var unloaded = src.Substring(src.IndexOf("Unloaded +=", StringComparison.Ordinal),
            src.IndexOf("Loaded +=", StringComparison.Ordinal) - src.IndexOf("Unloaded +=", StringComparison.Ordinal));
        Assert.Contains("Locale.Changed -= RebuildWords", unloaded, StringComparison.Ordinal);
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

    /// <summary>
    /// 月历上的圆点问的是<b>当前那一档</b>的清单（批次 WI-56）：⛔ 在这里再排一份"哪些天算节日"，
    /// 那是第二个出处——判据是这颗调用必须走 <c>GlanceCalendar</c>，⛔ 自己数日期。
    /// </summary>
    [Fact]
    public void TheCalendarDotsAskTheSameListTheCountdownLineUses()
    {
        var src = System.IO.File.ReadAllText(RepoRelative("src/StarMark.Core/Widgets/MonthGrid.cs"));
        Assert.Contains("GlanceCalendar.FestivalMessage(date) is not null", src, StringComparison.Ordinal);
        Assert.DoesNotContain("GlanceCalendar.Festival(", src, StringComparison.Ordinal);   // 交词的那颗旧出口⛔ 回来
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
