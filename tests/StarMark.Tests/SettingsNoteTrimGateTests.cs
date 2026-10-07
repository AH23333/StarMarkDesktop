#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Abstractions.I18n;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 设置页那些"小字说明"这一族（批次 WI-42，按用户裁决精简）。
/// <para><b>这里的判据不是"写得漂亮"，而是"值不值得占那一行"</b>：一条说明只许说一件
/// <b>控件自己显示不出来的事</b>——副作用范围、不可见的代价、隐私与安全边界、两种相似档位的真实差别。
/// 换颜色、换语言、字号、导航可见性这类一般功能<b>干脆不要说明</b>（他原话），
/// 因为标题与选项自己就是那句话，多出来的一行只把整页推成一屏小字。</para>
/// <para>⚠ 这三颗闸门都是<b>数量与长度</b>型的：它们拦的不是"这句写错了"，而是"下一次又慢慢长回去"——
/// 说明族的退化方式永远是加一行、多加半句，而不是有人主动重写坏。</para>
/// </summary>
public sealed class SettingsNoteTrimGateTests
{
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>删掉的说明：⛔ 不许回来（表行、XAML 两处都不许），也不许只删一半留一颗死键。</summary>
    private static readonly string[] DeletedNotes =
    {
        "settings.general.themeNote", "settings.languageNote", "settings.general.autostartNote",
        "settings.general.trayResidentNote", "settings.general.minimizeToTrayNote",
        "settings.general.mainBackdropNote", "settings.general.currentVersionNote",
        "settings.widgets.materialNote", "settings.widgets.cacheNote", "settings.widgets.snapOnNote",
        "settings.extend.trendingPeriodNote", "settings.hotkeys.mapNote", "settings.health.eyeRestNoticeNote",
        "settings.general.mainOpacityNote", "settings.widgets.opacityNote",   // 合成一颗主人键
        "settings.ai.note",
    };

    /// <summary>一颗说明的中文上限：一件控件自己显示不出来的事，一句话。</summary>
    private const int ZhCapPerNote = 26;

    /// <summary>整族中文预算。现场量过＝批次 WI-42 精简之后＝658 字（38 行）；
    /// 留出的是改措辞的余量，⛔ 不是"再加几条"的余量——加一条得先说清哪一条该被挤掉。</summary>
    private const int TotalZhBudget = 700;

    /// <summary>点名豁免的两颗：它们的<b>两半各挡一种误解</b>，砍掉任一半那句话听起来仍像说全了
    /// （<see cref="ZhCapPerNote"/> 装不下两件事，所以多出来的字数是点名给的，⛔ 别的行不许引用这一格）。
    /// 两半本身由 <c>UpdateApplyWiringGateTests</c> 与 <c>ClipboardImageSettingsGateTests</c> 逐字钉着，这里只管长度。</summary>
    private static readonly (string Key, int Cap, string Why)[] BothHalvesAreInvisible =
    {
        ("settings.general.autoCheckNote", 34, "只问版本／只有点下去才替换"),
        ("settings.data.includeImagesNote", 40, "开着会变大／关着缺的那半读作「文件缺失」"),
    };

    /// <summary>抹掉 XML 注释后的设置页标记：注释里出现的键名是给人读的叙述，不算一处挂键。</summary>
    private static string Xaml()
        => System.Text.RegularExpressions.Regex.Replace(
            ReadRepoFile(SettingsXaml), "<!--.*?-->", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>说明那一族：表里所有 <c>settings.*</c> 且尾名以 <c>Note</c> 收口的行，加合成出来的那颗不透明度。
    /// <para>⚠ 尺子的射程是<b>命名约定</b>而不是样式：<c>SettingRowText</c> 那一枚同时被行标题与状态读数穿着
    /// （现场 82 颗），按它数会把 26 颗标题算成说明。所以这一族按尾名点名——
    /// 换个尾名（<c>…Hint</c>）写一条长说明会绕过这一格，那条线只能靠裁决与点验拦，这里如实记下。</para></summary>
    private static List<(string Key, string Zh, string En, string Ja)> NoteFamily()
        => UiStrings.All.Where(e => e.Key.StartsWith("settings.", StringComparison.Ordinal)
                && (e.Key[(e.Key.LastIndexOf('.') + 1)..].EndsWith("Note", StringComparison.Ordinal)
                    || e.Key == "settings.shared.opacityScope"))
            .Select(e => (e.Key, e.Zh, e.En, e.Ja))
            .ToList();

    /// <summary>
    /// 一颗说明只许说一件事（中文 ≤ <see cref="ZhCapPerNote"/>，两颗点名的除外），
    /// 而整族的中文总量不许越过 <see cref="TotalZhBudget"/>。
    /// <para>⚠ 这里⛔ 用"颗数上限"当尺子：他这一轮的原话是"每一项说明仍然过多"，
    /// 那是<b>总字数</b>的事——把三条 20 字换成一条 60 字，颗数不涨而屏幕更挤；
    /// 反过来颗数能守住"又慢慢加回去"，却量不到"每一条都变长"。所以两半都量：逐条长度 + 整族预算。</para>
    /// </summary>
    [Fact]
    public void ASurvivingNoteSaysExactlyOneShortThing()
    {
        var family = NoteFamily();
        Assert.True(family.Count >= 30, $"表里只认出 {family.Count} 行说明（尾名约定变了，这一格先跟上）");

        var allowed = BothHalvesAreInvisible.ToDictionary(x => x.Key, x => x.Cap);
        foreach (var (key, zh, en, ja) in family)
        {
            var cap = allowed.TryGetValue(key, out var own) ? own : ZhCapPerNote;
            Assert.True(zh.Length <= cap,
                $"{key} 的中文列 {zh.Length} 字，这一颗的上限是 {cap}"
                + (allowed.ContainsKey(key) ? "（点名豁免给的也就这么多）" : "：一句说明装不下两件事")
                + $"——「{zh}」");
            Assert.True(en.Length <= 120 && ja.Length <= 120,
                $"{key} 的英／日文列过长（{en.Length}／{ja.Length}）");
        }

        var total = family.Sum(f => f.Zh.Length);
        Assert.True(total <= TotalZhBudget,
            $"设置页那一族说明一共 {total} 个中文字，预算是 {TotalZhBudget}——要加回去得先说清哪一条该被挤掉（他这一轮的意见就是这一屏小字太多）");
    }

    /// <summary>
    /// 删掉的 16 颗⛔ 不许回来；同时它们⛔ 不许只剩一半（表行删了、XAML 还挂着＝屏幕上是一颗空元素；
    /// 反过来就是"没人问的一行"，那一半由反向对账闸门负责）。
    /// </summary>
    [Fact]
    public void TheDeletedNotesAreGoneFromBothSides()
    {
        var xaml = Xaml();
        foreach (var key in DeletedNotes)
        {
            Assert.False(xaml.Contains("\"" + key + "\"", StringComparison.Ordinal), $"{key} 又挂回设置页了");
            Assert.False(UiStrings.TryGet(key, out _), $"{key} 的表行又回来了（或只剩表行＝死键）");
        }
    }

    /// <summary>
    /// 两颗不透明度说明合成<b>一颗主人键</b>：同一句话在常规页与组件页各挂一次，⛔ 两个键名各存一份。
    /// <para>这也是"为什么删掉 <c>mainOpacityNote</c> 与 <c>opacityNote</c> 两颗键却没少一处文字"的凭据。</para>
    /// </summary>
    [Fact]
    public void TheOpacitySentenceHasOneKeyAndTwoHosts()
    {
        var xaml = Xaml();
        Assert.Equal(2, Count(xaml, "i18n:LocText.Key=\"settings.shared.opacityScope\""));
        Assert.True(UiStrings.TryGet("settings.shared.opacityScope", out var entry));
        Assert.Contains("纯色", entry!.Zh, StringComparison.Ordinal);
    }

    /// <summary>
    /// 长解释搬进「使用说明」＝<b>同一批键换个宿主</b>：⛔ 为它起第二个键名。
    /// <para>这一格钉两件事：① 那六颗键确实挂在说明表上（不是被删掉——那是把信息扔掉，不是精简）；
    /// ② 它们确实不再出现在设置页那一栏里（否则两处各一份，正是这一族最不该有的第二个出处）。</para>
    /// </summary>
    [Fact]
    public void TheLongAiExplanationsMovedIntoTheGuideWithoutANewKey()
    {
        var moved = new[] { "settings.ai.fieldGuide", "settings.ai.deepseekGuide", "settings.ai.kimiGuide",
            "settings.ai.zhipuGuide", "settings.ai.openaiGuide", "settings.ai.localGuide" };
        var guide = Code(ReadRepoFile("src/StarMark.Core/Guide/PageGuides.cs"));
        var xaml = Xaml();
        foreach (var key in moved)
        {
            Assert.True(UiStrings.TryGet(key, out _), $"{key} 整条被删了——那是丢信息，不是搬走");
            Assert.Contains("\"" + key + "\"", guide, StringComparison.Ordinal);
            Assert.False(xaml.Contains("\"" + key + "\"", StringComparison.Ordinal), $"{key} 还留在设置页上");
        }
    }
}
