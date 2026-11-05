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
    private const string BridgeJsPath = "tools/boards/board-bridge.js";
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

    /// <summary>那颗名册的读法⛔ 许漏掉"画板"这一族——它管四件事：退出、起窗、重贴外观、收窗（片204 起）。
    /// <para>⚠ 这一格原名念的是三件事：收窗那一颗（<c>CloseAll</c>＝开关 OFF 那一拍）进来之后名与实分了家，
    /// 本批连名一起改（措辞与实现对齐那笔账）；全仓无别处引用旧名，改名零牵连。</para></summary>
    [Fact]
    public void TheRosterOwnsExactlyItsFourJobs()
    {
        var roster = SourceGate.Code(SourceGate.ReadRepoFile(RosterPath));
        Assert.Contains("public static void Add(BoardWindow window)", roster, StringComparison.Ordinal);
        Assert.Contains("public static void Remove(BoardWindow window)", roster, StringComparison.Ordinal);
        Assert.Contains("public static async Task OpenSceneAsync(string? sceneId)", roster, StringComparison.Ordinal);
        Assert.Contains("public static async Task<int> FlushAllForExitAsync()", roster, StringComparison.Ordinal);
        Assert.Contains("public static void ReapplyAppearance()", roster, StringComparison.Ordinal);
        Assert.Contains("public static void CloseAll()", roster, StringComparison.Ordinal);
        // 起窗⛔ 许有两处各写一遍（托盘那两颗入口与收藏系统那一行共用这一颗）
        var offenders = SourceGate.ReadRepoUnder("src/StarMark.UI")
            .Where(f => SourceGate.Code(f.Text).Contains("new BoardWindow()", StringComparison.Ordinal)
                     && !f.RelativePath.EndsWith("BoardsWindows.cs", StringComparison.Ordinal))
            .Select(f => f.RelativePath).ToList();
        Assert.Empty(offenders);
    }

    /// <summary>片204＝P-287 的发侧读数位：设置页换语言→宿主订阅→桥同根重渲→引擎按 langCode 惰量加载内嵌 locale，
    /// 六环静态全通；而用户真机量到的「画布上的字没跟着换」断点只能真机定界。所以 <c>ReapplyAppearance</c> 每一发
    /// 都把<b>实际递出去的那串语言码</b>留在日志里：复现那一趟对着它读——日志对着而画布没换＝断点在桥/引擎侧；
    /// 日志里缺这一行＝断点在宿主侧。⛔ 在复现之前猜着改（这一格钉的是「读数在场」，⛔ 是答案）。</summary>
    [Fact]
    public void EveryAppearancePushLeavesTheLanguageCodeInTheLog()
    {
        var push = SourceGate.MethodBody(SourceGate.Code(SourceGate.ReadRepoFile(WindowPath)),
            "public void ReapplyAppearance()");
        Assert.Equal(1, SourceGate.Count(push, "boards.lang.push code="));
        Assert.Contains("StarLog.Info(\"boards.lang.push code=\"", push, StringComparison.Ordinal);
        // 读数念的必须是真递给引擎的那颗判据（发出去的与写进日志的＝同一颗值），而⛔ 自己再拼一串
        Assert.Contains("BoardPolicy.EngineLangCode(StarMark.Abstractions.I18n.Locale.Current)",
            push, StringComparison.Ordinal);
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

    // ─────────────── ⑤ 片205：深浅色真因（页侧重挂抹兄弟键）＋换语言链逐户隔离＋同板去重 ───────────────

    /// <summary>深浅色那副真机脸的真因住在页侧（片205 定界）：宿主 bridge.ready 那一拍先发 theme.set
    /// 再发 lang.set，而页侧从前每一发都只带自己那一颗键重挂一遍、重挂的 props 每次从 baseProps() 重拼——
    /// 后到的那发把先到那发刚挂上去的兄弟键抹掉（末了那一挂上只剩 langCode：深色用户恒浅色，
    /// 中文用户的语言码碰巧同默认值连「没跟」都看不出）。这一格钉修法：三颗外观 prop 记进一份粘性档，
    /// 每一发整份并进去重挂——谁后到都抹不掉谁先到。</summary>
    [Fact]
    public void ThePageKeepsEveryAppearancePropAcrossRerenders()
    {
        var js = SourceGate.ReadRepoFile(BridgeJsPath);
        Assert.Contains("var stickyProps = {};", js, StringComparison.Ordinal);
        Assert.Equal(1, SourceGate.Count(js, "rerenderWith(stickyProps)"));
        // 旧形状⛔ 许回来：单键重挂＝每一发把兄弟键抹掉
        Assert.DoesNotContain("rerenderWith({ theme:", js, StringComparison.Ordinal);
        Assert.DoesNotContain("rerenderWith({ langCode:", js, StringComparison.Ordinal);
    }

    /// <summary>换语言那一拍：入口留读数＋三颗重贴逐颗兜（P-287 真机定界后的修法）——
    /// 链上任何一颗抛了⛔ 配把 lang.set 那一发也带走（真机脸：主窗换了、画板窗一动不动，
    /// 日志里连 boards.lang.push 那一行都没有）。入口那行把「订阅活着、这一拍真的进来了」留在日志里，
    /// 三颗 catch 把抛点收窄到户；三颗重贴的字面调用语句照旧在场（闸门连它们的计数一起钉）。</summary>
    [Fact]
    public void TheLanguageBeatIsTappedLoggedAndNeverTakenDownByOneStuckPaste()
    {
        var raw = SourceGate.ReadRepoFile(WindowPath);
        var locale = SourceGate.MethodBody(raw, "private void OnLocaleChanged()");
        Assert.Equal(1, SourceGate.Count(locale, "boards.locale.tap id="));
        Assert.Equal(3, SourceGate.Count(locale, "catch (System.Exception"));
        Assert.Equal(3, SourceGate.Count(locale, "boards.locale.paste.threw part="));
        // 三颗重贴都照贴，而 lang.set 那一发排在三颗之后（一颗被兜住了⛔ 配把那一发也带走）
        Assert.True(locale.IndexOf("PasteNotice();", StringComparison.Ordinal)
                    < locale.IndexOf("ReapplyAppearance();", StringComparison.Ordinal),
            "重贴排在 lang.set 之后＝重贴那一户的坏把该发的也带走了");
        Assert.True(locale.IndexOf("PasteHiddenEmbedNotice();", StringComparison.Ordinal)
                    < locale.IndexOf("ReapplyAppearance();", StringComparison.Ordinal),
            "第三颗重贴排在 lang.set 之后＝同一颗坏");
    }

    /// <summary>同一颗板只开一扇（片205：真机量到同板连点两下＝两颗引擎各自冷启的卡顿＋两扇各往
    /// 同一颗档里写）：名册里已有那颗 id 的窗就带到眼前，⛔ 再起第二扇。去重排在起窗之前；
    /// 新起那一档（sceneId 交 null）⛔ 参加去重——「新起一块」每一拍都该是一块新板。</summary>
    [Fact]
    public void OpeningTheSameSceneTwiceBringsItsOneWindowToFrontInsteadOfASecondEngine()
    {
        var raw = SourceGate.ReadRepoFile(RosterPath);
        var open = SourceGate.MethodBody(raw, "public static async Task OpenSceneAsync(string? sceneId)");
        Assert.Contains("lock (_open)", open, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault(w => w.BoardId == id)", open, StringComparison.Ordinal);
        Assert.Contains("boards.scene.dedupe id=", open, StringComparison.Ordinal);
        Assert.Contains("already.Activate();", open, StringComparison.Ordinal);
        // 先查再起（去重排在造窗之前），而新起那一臂照旧走到造窗那一句
        Assert.True(open.IndexOf("FirstOrDefault", StringComparison.Ordinal)
                    < open.IndexOf("new BoardWindow()", StringComparison.Ordinal),
            "去重排在造窗之后＝第二扇已经起来了才想起去查");
        Assert.Contains("if (sceneId is null) await win.OpenNewAsync();", open, StringComparison.Ordinal);
    }

    // ─────────────── ⑥ 片209：P-291 装载路补钉（片206 修了行为、当批未立闸） ───────────────

    /// <summary>装载那一拍的兜底递的是<b>引擎此刻活着的那一份 appState</b>（P-291・片206 修）：
    /// <c>loadFromBlob</c> 的第二参是「档里缺的键拿哪一份补」——只递一颗参时引擎按它自家那份
    /// 恒 light 的默认补，深色用户开旧板每一趟载档都被兜底顶回浅色。片206 修了行为、当批未立闸
    /// （账本侧明写「归下一批画板闸补钉」＝本格）；这一格钉的正是那颗第二参——
    /// 单参形状回来＝深色用户的板开始悄悄变白，而其余每一格都照绿。</summary>
    [Fact]
    public void TheLoadFallsBackToTheLiveAppStateForKeysTheSceneFileDoesNotCarry()
    {
        var js = SourceGate.ReadRepoFile(BridgeJsPath);
        // 兜底取的是引擎现读那一份（API 在就问它，⛔ 宿主自己另拼一份假活态）
        Assert.Equal(1, SourceGate.Count(js, "var live = API && API.getAppState ? API.getAppState() : null;"));
        // 那一颗第二参真递进去了（档里自己的键照旧由场景档赢＝引擎侧行为，闸外）
        Assert.Equal(1, SourceGate.Count(js, "L.loadFromBlob(blob, live)"));
        // 单参形状⛔ 许回来：回来＝缺键全按引擎默认补（P-291 的根因原样复活）
        Assert.DoesNotContain("L.loadFromBlob(blob)", js, StringComparison.Ordinal);
    }
}
