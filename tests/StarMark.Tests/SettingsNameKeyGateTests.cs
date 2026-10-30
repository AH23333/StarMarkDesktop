#nullable enable
using System;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 设置页那一族<b>可访问名</b>（✅P-266 同族・片142）。
/// <para>形状与来路：立案的手⛔ 是"读码觉得该有名字"，而是<b>屏幕上数出来的</b>——片141 那把普查尺
/// （走桌面 RawView、按 rect 去重、ProcessId 与窗口名两把筛子各报颗数）逐页现读八张设置页，
/// 去掉顶栏那六颗（✅P-266 已收）之后点到 15 颗；本批按同一族形状在标记里逐颗问「谁该是它的名」，
/// 一共问出 <b>18 颗</b>：六颗有名字的材质／主题／语言／性能下拉、七根滑杆、三颗无名下拉、
/// 两颗 NumberBox（UIA 报的是它模板里那颗 <c>InputBox</c>，形状是 Edit，名字由外壳那颗带下来）。</para>
/// <para>这一格守的是<b>「名字从谁的嘴里来」</b>：每一颗都借<b>自己那一行标题已经在用的那颗键</b>，
/// 所以屏幕上不会出现第二份文案，表里也不会长出第二行。⛔ 在标记里写死一颗成品字（坑表 #276 那一族：
/// 换了语言屏幕上每颗都改口、只有名字停在上一门）。</para>
/// <para>⚠ 射程说清楚：这里钉的是<b>源码里挂了哪颗键</b>，钉不了"屏幕上念不念得出来"——那一维由真机
/// 那把新尺逐颗回读（片142：zh／en／ja 三跑各 292 行、这一族念不出的 0 颗；自定义预算那两根
/// 要把性能模式现场选到「自定义」才摆得出来，那一拍也算量到了，本批⛔ 留"现场摆不出"那一格）。</para>
/// <para>片144 又补<b>第十九颗</b>：「更新说明」那颗 <c>Expander</c>。它是本页唯一一颗 <c>Key</c> 与
/// <c>NameKey</c> 并挂的元素——<c>Key</c> 贴 <c>Header</c>（屏幕上那句抬头，本来就念得出），
/// <c>NameKey</c> 贴 <c>AutomationProperties.Name</c>（辅助技术念的那句）。两颗并挂的理由是一条真机读数：
/// 只靠 Header 得来的 UIA Name 会<b>停在第一次贴的那一门</b>（zh 起、切 en／ja 后抬头改口成
/// "What's in this release"／「この版の更新内容」，而那颗 peer 的 Name 仍是「这一版的更新说明」＝P-269 量到的），
/// 走 AutomationProperties.Name 那一族则三语各念各的（✅P-268 那 33 颗 Expander 就是这么验的）。</para>
/// </summary>
public sealed class SettingsNameKeyGateTests
{
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>
    /// 每一颗＝<b>(标记里的元素种类, 认出那一颗的锚, 该行标题已经在用的键)</b>，共十九颗。
    /// <para>锚⛔ 用 <c>x:Name</c> 之外的形状？能命名就命名：六颗下拉与那颗 Expander 有名字、三颗没有，
    /// 没有名字的那几颗只能按<b>它自己那一行绑的读数</b>认（<c>EyeRestIntervalOptions</c> 那一族），
    /// 这是现场唯一不重复的出处。锚写宽一点（不带 <c>Mode=TwoWay</c>）就会被注释或邻居答题，
    /// 所以滑杆那七颗一律带<b>绑定模式</b>整串。</para>
    /// </summary>
    private static readonly (string Tag, string Anchor, string Key)[] Censused =
    {
        ("ComboBox", "x:Name=\"ThemeSelect\"", "settings.general.themeTitle"),
        ("ComboBox", "x:Name=\"AccentSelect\"", "settings.general.accentTitle"),
        ("ComboBox", "x:Name=\"LanguageSelect\"", "settings.languageTitle"),
        ("ComboBox", "x:Name=\"MainBackdropSelect\"", "settings.general.mainBackdrop"),
        ("ComboBox", "x:Name=\"WidgetBackdropSelect\"", "settings.widgets.materialLabel"),
        ("ComboBox", "x:Name=\"PerformanceSelect\"", "settings.widgets.perfLabel"),
        ("Slider", "ViewModel.MainWindowOpacity, Mode=TwoWay", "settings.general.mainOpacity"),
        ("Slider", "ViewModel.WidgetOpacity, Mode=TwoWay", "settings.widgets.opacityLabel"),
        ("Slider", "ViewModel.SnapStrength, Mode=TwoWay", "settings.widgets.snapStrengthLabel"),
        ("Slider", "ViewModel.SnapSpacing, Mode=TwoWay", "settings.widgets.snapSpacingLabel"),
        ("Slider", "ViewModel.CacheBudgetMb, Mode=TwoWay", "settings.widgets.budgetLabel"),
        ("Slider", "ViewModel.MaxCacheCount, Mode=TwoWay", "settings.widgets.cacheLabel"),
        ("Slider", "ViewModel.CursorCircleRadiusDip, Mode=TwoWay", "settings.extend.glowTitle"),
        ("ComboBox", "settings.data.autoBackupIntervalTip", "settings.data.autoBackupInterval"),
        ("ComboBox", "EyeRestIntervalOptions", "settings.health.eyeRestInterval"),
        ("ComboBox", "EyeRestNoticeOptions", "settings.health.eyeRestNotice"),
        ("NumberBox", "ViewModel.ClipboardImageMaxValue", "settings.data.imageMax"),
        ("NumberBox", "ViewModel.ClipboardTextMaxValue", "settings.data.textMax"),
        // 这颗是片144 补的第十九颗，也是本页唯一一颗「Key 与 NameKey 同时挂着」的元素：
        // Key 贴的是 Header（屏幕上那句抬头），NameKey 贴的是 AutomationProperties.Name（辅助技术念的那句）。
        // 两颗并存是刻意的——真机量到只靠 Header 得来的 UIA Name 会停在第一次贴的那一门，
        // 而抬头本身换了语言（P-269 的读数）。
        ("Expander", "x:Name=\"UpdateNotesExpander\"", "settings.general.releaseNotes"),
    };

    /// <summary>抹掉 XML 注释、但把行数留在原地（#228 那一族：注释里写着一模一样的标记，闸门照样绿）。</summary>
    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", m => new string('\n', m.Value.Length - m.Value.Replace("\n", string.Empty).Length),
            RegexOptions.Singleline);

    private static string Page() => Markup(SourceGate.ReadRepoFile(SettingsXaml));

    /// <summary>
    /// ① 十九颗<b>一颗都不许漏、也不许挂错键</b>：只数颗数抓得到"漏一颗"，抓不到"十九颗挂同一颗键"这种错法；
    /// 只钉逐颗配对又抓不到"整颗元素被搬走、NameKey 留在别处"。两颗一起问，再补一颗颗数闸门。
    /// <para>配对的正面＝<b>那颗键在这一页确实还挂在某颗标题上</b>：借名而不是起名，是本批与
    /// ✅P-265 那颗工厂同一口径的落地形状；如果哪天有人把标题删了只留 NameKey，这一格会红着问一句
    /// "屏幕上还有谁念这句"。</para>
    /// </summary>
    [Fact]
    public void EachCensusedControlBorrowsItsOwnRowTitleKey()
    {
        var page = Page();
        foreach (var (tag, anchor, key) in Censused)
        {
            var matches = Regex.Matches(page, "<" + tag + "\\b[^>]*?" + Regex.Escape(anchor) + "[^>]*?>", RegexOptions.Singleline);
            Assert.Single(matches);                       // 锚认不真＝这一格先红，⛔ 让 NameKey 落在错的那一颗上
            Assert.True(matches[0].Value.Contains("i18n:LocText.NameKey=\"" + key + "\"", StringComparison.Ordinal),
                $"{tag}（{anchor}）没借到 {key}——屏幕普查里它又是一颗念不出名字的裸控件");
            Assert.Contains("i18n:LocText.Key=\"" + key + "\"", page, StringComparison.Ordinal);
        }
        Assert.Equal(Censused.Length, Regex.Matches(page, "i18n:LocText\\.NameKey=\"").Count);
    }

    /// <summary>
    /// ② 这一页⛔ 有人绕过重贴名单、把成品字直接写进那颗可访问名属性（现读＝这一页今天一次都没碰过那颗属性）。
    /// <para>名字一旦落成字面串，换语言时没有任何人会重贴它——正是 ✅P-265 起这条线要拦的那一维。
    /// 名字只许从 <c>LocText.NameKey</c> 进（落点由 <c>MainBarNameKeyGateTests</c> ① 钉着，这里⛔ 再数一遍机制）。</para>
    /// </summary>
    [Fact]
    public void TheSettingsPageNeverWritesAnAutomationNameAsALiteral()
        => Assert.Equal(0, SourceGate.Count(Page(), "AutomationProperties.Name"));
}
