#nullable enable
using System;
using System.Linq;
using Xunit;
using StarMark.Abstractions.I18n;
using StarMark.Core.Boards;

namespace StarMark.Tests;

/// <summary>
/// 画板那两颗"设置同步"的 outbound（Phase C 片147＝集成矩阵「设置同步（主题／语言）」那一格的后半）。
/// <para>这一格坏的方式很安静：改前宿主发的是两颗硬编码占位值（浅色＋<c>zh-CN</c>），
/// 于是深色主题／英文／日文用户打开画板看到的仍是浅色中文，而那扇窗⛔ 报错、⛔ 变样——
/// 屏幕上"一切正常"，只有语言与主题不对。所以这里钉的不是"发没发"（那一半早就绿），
/// 而是<b>发出去的那两颗值⛔ 是写死的</b>、以及<b>该叫到它的三拍都真叫到了</b>。</para>
/// </summary>
public sealed class BoardAppearanceGateTests
{
    private const string WindowPath = "src/StarMark.UI/Boards/BoardWindow.cs";
    private const string RosterPath = "src/StarMark.UI/Boards/BoardsWindows.cs";
    private const string ThemePath = "src/StarMark.UI/MainWindow.Theme.cs";
    private const string PolicyPath = "src/StarMark.Core/Boards/BoardPolicy.cs";

    // ─────────────── ① 两颗值的判据本身 ───────────────

    [Fact]
    public void EngineThemeName_AnswersOnlyTheTwoLiteralsTheEngineKnows()
    {
        Assert.Equal("dark", BoardPolicy.EngineThemeName(true));
        Assert.Equal("light", BoardPolicy.EngineThemeName(false));
    }

    /// <summary>
    /// 每一颗可选语言都要有引擎那一侧的键，且中文那颗⛔ 等于我们自己的代码。
    /// <para>那颗引擎要的是它自己语言表里的键（中文与日文都带地区），而 <c>AppLanguages.CodeOf</c> 交的是
    /// <c>zh</c>／<c>en</c>／<c>ja</c>——照抄那一颗的结果是<b>静默退回默认语言</b>：没有红、没有痕，
    /// 只有"看着像没翻译"。这一格不是推理：片147 真机先递 <c>ja</c>，画布上引擎自己那句提示<b>仍是英文</b>，
    /// 换成 <c>ja-JP</c> 才看到日文（同一颗 bundle 的语言表里 <c>zh-CN</c>／<c>ja-JP</c> 成对）。</para>
    /// </summary>
    [Fact]
    public void EngineLangCode_CoversEveryPickableLanguage_WithTheEnginesOwnKeys()
    {
        Assert.Equal("zh-CN", BoardPolicy.EngineLangCode(AppLanguage.Chinese));
        Assert.Equal("en", BoardPolicy.EngineLangCode(AppLanguage.English));
        Assert.Equal("ja-JP", BoardPolicy.EngineLangCode(AppLanguage.Japanese));
        // 遍历 Pickable：新增一格而那颗 switch 没配＝当场抛（⛔ 静默落到某一档）
        var mapped = AppLanguages.Pickable.Select(BoardPolicy.EngineLangCode).ToList();
        Assert.Equal(AppLanguages.Pickable.Count, mapped.Count);
        Assert.Equal(mapped.Count, mapped.Distinct(StringComparer.Ordinal).Count());
        // 中文那一颗是"照抄 CodeOf 就会坏"的那一格（我们 "zh"，引擎 "zh-CN"）；
        // 英／日两颗恰好同前缀，⛔ 拿"三颗都⛔ 相等"当判据（第一版就是这么红的，⛔ 改判据而⛔ 改事实）。
        Assert.NotEqual(AppLanguages.CodeOf(AppLanguage.Chinese), BoardPolicy.EngineLangCode(AppLanguage.Chinese));
        Assert.Equal("zh", AppLanguages.CodeOf(AppLanguage.Chinese));
    }

    // ─────────────── ② 占位值⛔ 许回来 ───────────────

    [Fact]
    public void TheHardCodedPlaceholderValuesAreGoneFromTheHost()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(WindowPath));
        Assert.DoesNotContain("\"theme\\\":\\\"light\\\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("\"langCode\\\":\\\"zh-CN\\\"", code, StringComparison.Ordinal);
        // 两颗值都由 Core 那两颗判据算出来（"这里只把两边接起来"那句的可检形式）
        Assert.Contains("BoardPolicy.EngineThemeName(dark)", code, StringComparison.Ordinal);
        Assert.Contains("BoardPolicy.EngineLangCode(StarMark.Abstractions.I18n.Locale.Current)", code, StringComparison.Ordinal);
        // "浅还是深"那一问⛔ 在这一格重算一遍：主人是 ThemeManager.ResolveEffectiveTheme
        Assert.Contains("ThemeManager.ResolveEffectiveTheme(pref)", code, StringComparison.Ordinal);
        Assert.Equal(0, SourceGate.Count(code, "IsSystemDark"));   // 抄第二份推导＝第二主人
    }

    /// <summary>两颗 outbound 各自只许有一位发送人（多一颗＝换主题与换语言两路各发各的、谁覆盖谁没人说得出）。</summary>
    [Fact]
    public void TheOnlySenderOfEachAppearanceEventIsThatOneMethod()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(WindowPath));
        Assert.Equal(1, SourceGate.Count(code, "PostOutbound(\"theme.set\""));
        Assert.Equal(1, SourceGate.Count(code, "PostOutbound(\"lang.set\""));
    }

    // ─────────────── ③ 该叫到它的三拍 ───────────────

    [Fact]
    public void TheBridgeReadyBeatAndTheLanguageBeatBothPushTheRealValues()
    {
        var src = SourceGate.ReadRepoFile(WindowPath);
        var ready = SourceGate.MethodBody(src, "private void OnBridgeReady()");
        var locale = SourceGate.MethodBody(src, "private void OnLocaleChanged()");
        Assert.Contains("ReapplyAppearance();", ready, StringComparison.Ordinal);
        Assert.Contains("ReapplyAppearance();", locale, StringComparison.Ordinal);
        // 那颗闸门先关着：桥没通就发＝发进一颗没人听的口子（与那颗 RequestSceneSnapshot 同一道门）
        var push = SourceGate.MethodBody(src, "public void ReapplyAppearance()");
        Assert.Contains("if (!_bridgeReady || _tearingDown) return;", push, StringComparison.Ordinal);
        Assert.True(push.IndexOf("_bridgeReady", StringComparison.Ordinal)
                    < push.IndexOf("PostOutbound(\"theme.set\"", StringComparison.Ordinal),
            "那道闸门如果排在发送之后，它今天就⛔ 拦任何东西（存在⛔ 等于生效）");
    }

    [Fact]
    public void TheThemeBeatReachesEveryOpenWindow_AndOneStuckWindowCannotTakeTheRestDown()
    {
        var theme = SourceGate.Code(SourceGate.ReadRepoFile(ThemePath));
        // 与那颗 ApplyThemeToOpenDialogs 同一拍、同一副理由（独立窗⛔ 继承主窗主题）
        Assert.Contains("CenteredDialog.ApplyThemeToOpenDialogs(_themePref);", theme, StringComparison.Ordinal);
        Assert.Contains("BoardsWindows.ReapplyAppearance();", theme, StringComparison.Ordinal);
        Assert.True(theme.IndexOf("CenteredDialog.ApplyThemeToOpenDialogs", StringComparison.Ordinal)
                    < theme.IndexOf("BoardsWindows.ReapplyAppearance", StringComparison.Ordinal));

        var roster = SourceGate.Code(SourceGate.ReadRepoFile(RosterPath));
        var push = SourceGate.MethodBody(roster, "public static void ReapplyAppearance()");
        Assert.Contains("lock (_open) windows = _open.ToArray();", push, StringComparison.Ordinal);
        Assert.Contains("w.ReapplyAppearance();", push, StringComparison.Ordinal);
        Assert.Contains("catch", push, StringComparison.Ordinal);   // 逐扇兜异常：一扇桥没通⛔ 配带走其余几扇
    }

    /// <summary>那颗名册的读法⛔ 许漏掉"画板"这一族——它现在管三件事：退出、起窗、重贴外观。</summary>
    [Fact]
    public void TheRosterStillOwnsExactlyTheThreeThingsItWasMadeFor()
    {
        var roster = SourceGate.Code(SourceGate.ReadRepoFile(RosterPath));
        Assert.Contains("public static void Add(BoardWindow window)", roster, StringComparison.Ordinal);
        Assert.Contains("public static void Remove(BoardWindow window)", roster, StringComparison.Ordinal);
        Assert.Contains("public static async Task OpenSceneAsync(string? sceneId)", roster, StringComparison.Ordinal);
        Assert.Contains("public static async Task<int> FlushAllForExitAsync()", roster, StringComparison.Ordinal);
        Assert.Contains("public static void ReapplyAppearance()", roster, StringComparison.Ordinal);
        // 起窗⛔ 许有两处各写一遍（托盘那两颗入口与收藏系统那一行共用这一颗）
        var offenders = SourceGate.ReadRepoUnder("src/StarMark.UI")
            .Where(f => SourceGate.Code(f.Text).Contains("new BoardWindow()", StringComparison.Ordinal)
                     && !f.RelativePath.EndsWith("BoardsWindows.cs", StringComparison.Ordinal))
            .Select(f => f.RelativePath).ToList();
        Assert.Empty(offenders);
    }

    /// <summary>那颗判据住 Core 而⛔ 住 UI（同"引擎认哪串字面"那一族：它是引擎的事实，⛔ 是我们的措辞）。</summary>
    [Fact]
    public void TheEngineLiteralOwnersLiveInCore()
    {
        var policy = SourceGate.Code(SourceGate.ReadRepoFile(PolicyPath));
        Assert.Contains("public static string EngineThemeName(bool isDark)", policy, StringComparison.Ordinal);
        Assert.Contains("public static string EngineLangCode(StarMark.Abstractions.I18n.AppLanguage language)",
            policy, StringComparison.Ordinal);
        // ⛔ 层次倒挂：Core 那颗判据⛔ 认框架那颗 enum（档头②那条口径）
        Assert.Equal(0, SourceGate.Count(policy, "ElementTheme"));
    }
}
