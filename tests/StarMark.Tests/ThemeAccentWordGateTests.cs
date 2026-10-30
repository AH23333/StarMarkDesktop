#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Appearance;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-81 的落地闸门（用户裁：①「主题色只能选择两种」⇒ 加一颗自定义，且⛔ 动材质；
/// ②「选了透明材质再改组件颜色」必须<b>保留透明</b>）。
/// <para>
/// 这一档管的是<b>形状</b>：那颗颜色只有一个写的人、取色算术只住在一处、新键各只有一个主人、
/// 那条"改色就变实色"的旧写法不许回来。纯算术那一半住在 <see cref="MaterialTintTests"/>
/// （测试工程引用不到 <c>StarMark.UI</c>，坑表 #184 ⇒ 判据先下沉到 Core 才能出行为契约测）。
/// </para>
/// </summary>
[Collection("LocaleGlobal")]        // 这几格会换语言（I18nWiringGateTests 的点名册）
public sealed class ThemeAccentWordGateTests : IDisposable
{
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string Page = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string Options = "src/StarMark.UI/Views/SettingsPage.Options.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string App = "src/StarMark.UI/App.xaml.cs";
    private const string Store = "src/StarMark.UI/Helpers/SettingsStore.cs";
    private const string StoreAppearance = "src/StarMark.UI/Helpers/SettingsStore.Appearance.cs";
    private const string ThemeAccentCs = "src/StarMark.UI/Helpers/ThemeAccent.cs";
    private const string ThemeBrushCs = "src/StarMark.UI/Helpers/ThemeBrush.cs";
    private const string WidgetAppearanceCs = "src/StarMark.UI/Helpers/WidgetAppearance.cs";
    private const string WidgetWindow = "src/StarMark.UI/Views/WidgetWindow.Appearance.cs";
    private const string MainWindowThemeCs = "src/StarMark.UI/MainWindow.Theme.cs";
    private const string Editor = "src/StarMark.UI/Views/WidgetAppearanceEditor.cs";
    private const string PageCs = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string ThemeXaml = "src/StarMark.UI/Themes/StarMarkTheme.xaml";

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    /// <summary>代码侧（抹注释）的提问者普查：⛔ 只钉一两个文件，那会因搬家而假绿（RV 那条口径）。</summary>
    private static int AsksInCode(string quoted)
        => ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Sum(f => Count(Code(f.Text), quoted));

    /// <summary>① 四颗新键：表里各一行、代码与标记合计各一个<b>可见</b>主人，三语各说各的。
    /// <para>⚠ 那颗数是<b>读者名单</b>，按名册 305 那一族的口径只管改数、⛔ 放宽判据：
    /// <c>accentTitle</c> 从 1 涨到 2 是多了一颗合法读者——那颗下拉的可访问名（批次 Phase C 片142 借名，
    /// 屏幕上仍只有一处念这句话，⛔ 第二份文案）。再加读者时同批改这颗数与这段分布。</para></summary>
    [Theory]
    [InlineData("settings.general.accentTitle", 2)]     // ①标题那一格 ②AccentSelect 的可访问名（借同一颗键）
    [InlineData("settings.general.accentSystemAccent", 1)]
    [InlineData("settings.general.accentCustom", 1)]
    [InlineData("settings.general.accentScope", 1)]
    public void EachNewWordHasOneOwnerAndSaysItDifferentlyInEachLanguage(string key, int readers)
    {
        var quoted = "\"" + key + "\"";
        Assert.Equal(readers, AsksInCode(quoted) + Count(ReadRepoFile(Page), quoted));
        Assert.Equal(1, Count(ReadRepoFile(Table), "new Entry(\"" + key + "\""));

        Assert.True(UiStrings.TryGet(key, out var row), "表里没有这颗键：" + key);
        Assert.Empty(Cjk.Matches(row!.En).Cast<Match>());          // "没翻"看着像翻了＝坑表 #526
        Assert.NotEqual(row.Zh, row.En);
        Assert.NotEqual(row.Zh, row.Ja);
        Assert.NotEqual(row.En, row.Ja);
        Assert.DoesNotContain("{0}", row.Zh, StringComparison.Ordinal);   // 标题与范围说明⛔ 带槽

        var said = new List<string>();
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            said.Add(LocMessage.Of(key).Render());
        }
        Assert.Equal(3, said.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// ② 主题色那一行的<b>两档清单</b>必须还是"数据项＋模板"那一族（⛔ 在标记里再列两行正文——
    /// 闭着的那格念的是选中那一刻的快照，坑表 #272／#275），清单只住在 Options 那一颗。
    /// </summary>
    [Fact]
    public void TheAccentRowIsDataTemplatedAndTheListHasOneHome()
    {
        var block = Between(Code(ReadRepoFile(Options)), "AccentOptions { get; } =", "];");
        Assert.Equal(2, Count(block, "LocOption.T("));
        Assert.Contains("\"settings.general.accentSystemAccent\"", block, StringComparison.Ordinal);
        Assert.Contains("\"settings.general.accentCustom\"", block, StringComparison.Ordinal);

        var card = Between(ReadRepoFile(Page), "x:Name=\"AccentSelect\"", "/>");
        Assert.Contains("ItemsSource=\"{x:Bind AccentOptions, Mode=OneTime}", card, StringComparison.Ordinal);
        Assert.Contains("ItemTemplate=\"{StaticResource LocOptionTemplate}\"", card, StringComparison.Ordinal);
        Assert.Equal(0, Count(ReadRepoFile(Page), "<ComboBoxItem"));             // ⛔ 这一族不许退回字面标记
    }

    /// <summary>
    /// ③ <b>取色算术只住一处</b>：UI 里那份旧的取色档已整颗搬进 Core，⛔ 有人再把它当类型用
    /// （注释里"从哪儿搬来"的历史提法是合法的，那颗⛔ 在这里数——数<b>带点的调用形</b>，⛔ 数裸名字）。
    /// <para>⚠ 那颗名字是<b>拼出来的</b>：把禁项字面原样写进同一档，这条闸门会先咬到自己
    /// （"同名字面让 DoesNotContain 反咬"那一族的<b>字符串字面版</b>——本批第一次真用就撞上了）。</para>
    /// </summary>
    [Fact]
    public void TheColourMathHasExactlyOneHome()
    {
        var moved = "WidgetMaterialVisual" + "Calculator";
        Assert.False(System.IO.File.Exists(System.IO.Path.Combine(RepoRoot(),
            "src", "StarMark.UI", "Helpers", moved + ".cs")));
        foreach (var (_, text) in ReadRepoUnder("src").Concat(ReadRepoUnder("tests")))
            Assert.Equal(0, Count(Code(text), moved + "."));

        // 背衬取色的读者：①套背衬那一腿 ②兜底画笔那一腿 ③批次 WI-82 那颗"点了底色之后字念黑还是白"
        // 也要问同一颗合成色（⛔ 自己再算一遍 alpha——那正是"换了颜色顺手换了材质"的入口）。
        // 这颗数从 2 → 3 是<b>多了一个合法读者</b>，⛔ 把判据放宽；再加读者时同批改这颗数与这段分布。
        Assert.Equal(3, Count(Code(ReadRepoFile(WidgetAppearanceCs)), "MaterialTint.BackdropColor("));
        // 那一档 alpha 的性格只住在 Core，UI 侧⛔ 再算一遍（那正是"换了颜色顺手换了材质"的入口）
        Assert.Equal(0, Count(Code(ReadRepoFile(WidgetAppearanceCs)), "BackdropAlpha("));
        Assert.Contains("public static byte BackdropAlpha(",
            Code(ReadRepoFile("src/StarMark.Core/Appearance/MaterialTint.cs")), StringComparison.Ordinal);
    }

    /// <summary>
    /// ④ <b>那条缺陷不许回来</b>：组件「外观」里点定的底色⛔ 再把那一档材质判成实色（None）。
    /// 旧写法是 <c>useCustomBg ? WidgetBackdropKind.None : …</c> 配一句"实色铺满会盖住霜化背景"。
    /// </summary>
    [Fact]
    public void AWidgetBackgroundColourNeverForcesTheOpaqueTier()
    {
        var code = Code(ReadRepoFile(WidgetWindow));
        Assert.Equal(0, Count(code, "useCustomBg"));
        Assert.Equal(0, Count(code, "WidgetBackdropKind.None : ("));
        Assert.Contains("var kind = ov?.Backdrop ?? globalKind;", code, StringComparison.Ordinal);
        // 那颗点定的色交给两处：①背衬那一腿 ②批次 WI-82 起的兜底内容表面也照同一颗（⛔ 一处吃了色、另一处仍铺主题色）
        Assert.Equal(2, Count(code, "tint?.Color"));
        Assert.Contains("backdropOwnsSurface", code, StringComparison.Ordinal);
    }

    /// <summary>自定义色在启动与保存时更新缓存，只有组件与主窗背景读取它，画笔解析不读设置。</summary>
    [Fact]
    public void TheCustomAccentHasTwoApplySitesAndOnlyBackgroundReaders()
    {
        Assert.Equal(2, ReadRepoUnder("src").Sum(f => Count(Code(f.Text), "ThemeAccent.Apply(")));
        Assert.Contains("Helpers.ThemeAccent.Apply(fileSettings.LoadThemeAccent());", Code(ReadRepoFile(App)),
            StringComparison.Ordinal);
        Assert.Contains("ThemeAccent.Apply(accentText);", Code(ReadRepoFile(ViewModel)), StringComparison.Ordinal);

        var appearance = Code(ReadRepoFile(WidgetAppearanceCs));
        Assert.Equal(1, Count(appearance, "AppPalette.ResolveAccent("));
        Assert.Contains("AppPalette.ResolveAccent(ThemeAccent.Custom, Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B))", appearance, StringComparison.Ordinal);
        Assert.Contains("ThemeAccent.Custom ?? MaterialTint.FallbackAccent", appearance, StringComparison.Ordinal);
        Assert.DoesNotContain("ThemeAccent.Custom", Code(ReadRepoFile(ThemeBrushCs)), StringComparison.Ordinal);
        Assert.Equal(1, Count(Code(ReadRepoFile(MainWindowThemeCs)), "ThemeAccent.Custom"));
        Assert.Equal(new[] { WidgetAppearanceCs, MainWindowThemeCs }, ReadRepoUnder("src")
            .Where(f => Code(f.Text).Contains("ThemeAccent.Custom", StringComparison.Ordinal))
            .Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.Contains("AppPalette.ParseArgb(hexFromSettings)", Code(ReadRepoFile(ThemeAccentCs)), StringComparison.Ordinal);
    }

    /// <summary>按钮与选项画笔只查主题字典，不再从自定义背景色派生或覆盖。</summary>
    [Fact]
    public void TheAccentBrushFamilyComesOnlyFromThemeDictionaries()
    {
        var brush = Code(ReadRepoFile(ThemeBrushCs));
        foreach (var forbidden in new[] { "ThemeAccent", "AccentKeys", "AccentDerived", "AppPalette.", "AccentResourceSync", "new SolidColorBrush", "LoadThemeAccent" })
            Assert.DoesNotContain(forbidden, brush, StringComparison.Ordinal);
        Assert.DoesNotContain("FromArgb(0x1A", brush, StringComparison.Ordinal);

        var forTheme = MethodBody(brush, "public static Brush? For(ElementTheme theme, string key)");
        Assert.Contains("theme == ElementTheme.Dark", forTheme, StringComparison.Ordinal);
        Assert.Contains("theme == ElementTheme.Default && ThemeManager.IsSystemDark()", forTheme, StringComparison.Ordinal);
        Assert.Contains("return Resolve(dark, key);", forTheme, StringComparison.Ordinal);
        Assert.Contains("LightKeys = { \"Light\" };", brush, StringComparison.Ordinal);
        Assert.Contains("DarkKeys = { \"Dark\", \"Default\" };", brush, StringComparison.Ordinal);

        var resolve = MethodBody(brush, "public static Brush? Resolve(bool dark, string key)");
        Assert.Contains("var themeKeys = dark ? DarkKeys : LightKeys;", resolve, StringComparison.Ordinal);
        Assert.Contains("var app = Application.Current.Resources;", resolve, StringComparison.Ordinal);
        Assert.Contains("foreach(varthemeKeyinthemeKeys){if(FindInDict(app,themeKey,key,outvarbrush))returnbrush;" +
            "foreach(varmdinapp.MergedDictionaries)if(FindInDict(md,themeKey,key,outbrush))returnbrush;}",
            Regex.Replace(resolve, @"\s+", ""), StringComparison.Ordinal);
        Assert.Contains("if (app.TryGetValue(key, out var fallback) && fallback is Brush fb) return fb;", resolve, StringComparison.Ordinal);
        var find = MethodBody(brush, "private static bool FindInDict(");
        Assert.Contains("dict.ThemeDictionaries.TryGetValue(themeKey, out var themeObj)", find, StringComparison.Ordinal);
        Assert.Contains("themeObj is ResourceDictionary themeDict", find, StringComparison.Ordinal);
        Assert.Contains("themeDict.TryGetValue(key, out var v)", find, StringComparison.Ordinal);
        Assert.Contains("v is Brush b", find, StringComparison.Ordinal);
        Assert.Contains("brush = b;", find, StringComparison.Ordinal);
        Assert.Contains("foreach (var md in dict.MergedDictionaries)", find, StringComparison.Ordinal);
        Assert.Contains("if (FindInDict(md, themeKey, key, out brush)) return true;", find, StringComparison.Ordinal);

        var xaml = ReadRepoFile(ThemeXaml);
        var light = Between(xaml, "<ResourceDictionary x:Key=\"Light\">", "</ResourceDictionary>");
        var dark = Between(xaml, "<ResourceDictionary x:Key=\"Dark\">", "</ResourceDictionary>");
        foreach (var (key, lightColor, darkColor) in new[]
        {
            ("AppAccentBrush", "#0067C0", "#4CC2FF"),
            ("AppAccentSoftBrush", "#1A0067C0", "#2E4CC2FF"),
            ("WidgetDropHintBrush", "#330067C0", "#334CC2FF"),
        })
        {
            Assert.Equal(2, Count(xaml, "x:Key=\"" + key + "\""));
            Assert.Contains($"<SolidColorBrush x:Key=\"{key}\" Color=\"{lightColor}\"/>", light, StringComparison.Ordinal);
            Assert.Contains($"<SolidColorBrush x:Key=\"{key}\" Color=\"{darkColor}\"/>", dark, StringComparison.Ordinal);
            Assert.DoesNotContain("\"" + key + "\"", brush, StringComparison.Ordinal);
            Assert.Equal(0, Count(xaml, "{ThemeResource " + key + "}"));
        }
        Assert.DoesNotContain("\"AccentFillColorDefaultBrush\"", brush, StringComparison.Ordinal);
        Assert.Equal(3, new[] { "src/StarMark.UI/Controls/ItemCard.xaml", ThemeXaml, Page }
            .Sum(f => Count(ReadRepoFile(f), "{ThemeResource AccentFillColorDefaultBrush}")));
    }

    /// <summary>⑦ 设置页那一行：一个编辑入口（片144 起＝代码侧那颗面板），读数与白衬已灭，⛔ 两处编辑同一颗量。</summary>
    [Fact]
    public void TheAccentSettingHasExactlyOneEditorAndItsReadingIsReadOnly()
    {
        var xaml = ReadRepoFile(Page);
        Assert.Equal(1, Count(xaml, "ViewModel.AccentIndex, Mode=TwoWay"));
        Assert.Equal(0, Count(xaml, "ViewModel.ThemeAccentColor, Mode=TwoWay"));         // 片144：TwoWay 旧入口随旧润色器拆除，编辑入口改走代码侧的 ColorPickerPane
        Assert.Equal(0, Count(xaml, "ViewModel.ThemeAccentBrush, Mode=OneWay"));         // 那颗预览方块已灭（AccentPaneGateTests ② 钉着）
        Assert.Equal(0, Count(xaml, "ViewModel.ThemeAccentBrush, Mode=TwoWay"));        // 那颗是读数⛔ 是入口
        Assert.Equal(1, Count(xaml, "ViewModel.CustomAccentVisible, Mode=OneWay"));      // 只剩宿主容器那一颗（白衬与那颗串已灭）

        var vm = Code(ReadRepoFile(ViewModel));
        Assert.Equal(1, Count(vm, "private int _accentIndex"));
        Assert.Equal(1, Count(vm, "private Windows.UI.Color _themeAccentColor"));
        Assert.Contains("AccentIndex == 1 ? AppPalette.ToHexText(ThemeAccentColor) : null", vm, StringComparison.Ordinal);
    }

    /// <summary>⑧ 设置档那一对读写成对：各一颗定义，一处写，两处读（启动 + 设置页回填）。</summary>
    [Fact]
    public void TheStoredHexHasOneWriterAndTwoReaders()
    {
        Assert.Equal(1, Count(Code(ReadRepoFile(Store)), "public string? ThemeAccent { get; set; }"));
        var pair = Code(ReadRepoFile(StoreAppearance));
        Assert.Equal(1, Count(pair, "public string? LoadThemeAccent()"));
        Assert.Equal(1, Count(pair, "public void SaveThemeAccent(string? hex)"));
        Assert.Equal(3, ReadRepoUnder("src").Sum(f => Count(Code(f.Text), "LoadThemeAccent")));   // 定义＋启动＋回填
        Assert.Equal(2, ReadRepoUnder("src").Sum(f => Count(Code(f.Text), "SaveThemeAccent(")));  // 定义＋保存
    }

    /// <summary>
    /// ⑨ 片144 起「主题色 · 自定义」那一格与组件「外观」编辑器<b>共用同一颗取色器面板</b>
    /// （<c>ColorPickerPane</c>）：旧那颗保方润色器（220x140 ⇒ 现读 140x140、左缘差 222 px 那颗形状）
    /// 随白衬预览与 hex 读数一起拆除，这一格从此钉「同一颗面板」本身而不是两颗各自的几何。
    /// <para>编辑器带「跟随全局」钮（覆盖值 null 那一档得有一颗按钮可点）；设置页不带——
    /// 「随系统」由上面那颗下拉管。宿主各自决定摆位，面板只交内容⛔ 交版式。</para>
    /// </summary>
    [Fact]
    public void BothHostsMountTheSamePickerPane()
    {
        Assert.Equal(1, Count(Code(ReadRepoFile(Editor)), "new ColorPickerPane(showFollowGlobal: true)"));
        Assert.Equal(1, Count(Code(ReadRepoFile(PageCs)), "new ColorPickerPane(showFollowGlobal: false)"));
    }

    /// <summary>
    /// ⑩ 片144：设了自定义主题色时<b>主窗口表面也吃同一颗染料</b>——从前只有组件吃 AccentColor，
    /// 主窗实色永远灰白/灰黑。背衬腿（ApplyBackdrop）、实色腿（None 分支）、兜底腿（MainWindowSurfaceBrush）
    /// 都带上它，与组件那一侧同一判据；None＋tint 走 SurfaceBrush 铺染料原色（alpha FF），与组件实色那一档逐位同。
    /// </summary>
    [Fact]
    public void TheMainWindowSurfaceAlsoTakesTheCustomTint()
    {
        var theme = MethodBody(Code(ReadRepoFile(MainWindowThemeCs)), "public void RefreshAppearance()");
        Assert.Equal(1, Count(theme, "var customTint = ThemeAccent.Custom;"));
        Assert.Contains("var kind = _settings.LoadMainWindowBackdrop();", theme, StringComparison.Ordinal);
        Assert.Contains("var opacity = _settings.LoadMainWindowOpacity();", theme, StringComparison.Ordinal);
        Assert.Equal(1, Count(theme, "customTint is { } tint"));
        Assert.Equal(1, Count(theme, "FromArgb(0xFF, tint.R, tint.G, tint.B)"));
        Assert.Equal(1, Count(theme, "0xF3, 0xF3, 0xF3"));
        Assert.Equal(1, Count(theme, "0x20, 0x20, 0x20"));
        Assert.Contains("WidgetAppearance.ApplyBackdrop(this, kind, opacity, baseTheme, customTint);", theme, StringComparison.Ordinal);
        Assert.Contains("WidgetAppearance.MainWindowSurfaceBrush(baseTheme, kind, translucent, opacity, customTint);", theme, StringComparison.Ordinal);
        var helper = Code(ReadRepoFile(WidgetAppearanceCs));
        Assert.Equal(1, Count(helper, "public static Brush MainWindowSurfaceBrush(ElementTheme theme, WidgetBackdropKind kind, bool translucent, double? opacityOverride = null, Windows.UI.Color? tintOverride = null)"));
        Assert.Equal(1, Count(helper, "tintOverride is { } tint"));
        Assert.Equal(1, Count(helper, "SurfaceBrush(theme, WidgetBackdropKind.None, 1.0, tint)"));
        Assert.Equal(1, Count(helper, "SurfaceBrush(theme, kind, opacityOverride, tintOverride)"));
    }

    [Fact]
    public void MainWindowRequestedThemeComesOnlyFromThePreferenceNotTheCustomTint()
    {
        var body = MethodBody(Code(ReadRepoFile(MainWindowThemeCs)), "public void RefreshAppearance()");
        var compact = Regex.Replace(body, @"\s+", "");
        Assert.Contains("var baseTheme = TargetTheme(_themePref);", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?:^|[;{}])\s*baseTheme\s*=(?!=)", body);
        Assert.Contains("if(RootGrid.RequestedTheme!=baseTheme)RootGrid.RequestedTheme=baseTheme;", compact, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(compact, @"RootGrid\.RequestedTheme=(?!=)"));
        Assert.DoesNotContain("AutoForeground", body, StringComparison.Ordinal);
        Assert.DoesNotContain("effectiveTheme", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RootGrid.ActualTheme", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveTheme", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\b_themePref\s*=(?!=)", body);
        Assert.Contains("CenteredDialog.ApplyThemeToOpenDialogs(_themePref);", body, StringComparison.Ordinal);
        Assert.Contains("if(_refreshingAppearance)return;_refreshingAppearance=true;try{", compact, StringComparison.Ordinal);
        Assert.Contains("finally{_refreshingAppearance=false;}", compact, StringComparison.Ordinal);
        Assert.Contains("ApplyTitleBarButtonColors();", body, StringComparison.Ordinal);
        Assert.Contains("SetSourceButtonsHighlight(_currentSource);", body, StringComparison.Ordinal);
        Assert.Contains("SetStatusDot(_statusKind);", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 控件区⛔ 再铺一层我发明的底板：主题管按钮与选项的深浅（控件自己的主题画笔本来就够），自定义色只管背景。
    /// <para>
    /// 片145 一度加过三处卡片色背板（主窗一整条 <c>ControlSurface</c> ＋ 设置页标题条 ＋ 标签条），
    /// 真机上它把自定义色挤成窗边一条缝，用户裁"越做越差"⇒ 撤干净。这一格钉的就是那三处⛔ 许回来。
    /// </para>
    /// </summary>
    [Fact]
    public void TheControlAreaKeepsNoInventedBackingSoTheCustomColorStaysTheBackground()
    {
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        const string cardBackground = "{ThemeResource CardBackgroundFillColorDefaultBrush}";

        var mainSource = ReadRepoFile("src/StarMark.UI/MainWindow.xaml");
        var main = XDocument.Parse(mainSource);
        var root = Assert.Single(main.Root!.Elements(ui + "Grid"),
            e => (string?)e.Attribute(x + "Name") == "RootGrid");
        var rows = Assert.Single(root.Elements(ui + "Grid.RowDefinitions"));
        Assert.Equal(6, rows.Elements(ui + "RowDefinition").Count());
        Assert.DoesNotContain("ControlSurface", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain(cardBackground, mainSource, StringComparison.Ordinal);
        // 顶栏那一层用的是工程原有的层色画笔，⛔ 换成品145 那颗卡片背板。
        var topBar = Assert.Single(root.Elements(ui + "Border"),
            e => (string?)e.Attribute(x + "Name") == "TopBar");
        Assert.Equal("{ThemeResource LayerFillColorDefaultBrush}", (string?)topBar.Attribute("Background"));
        var content = Assert.Single(root.Elements(ui + "Frame"),
            e => (string?)e.Attribute(x + "Name") == "ContentFrame");
        Assert.Equal("5", (string?)content.Attribute("Grid.Row"));

        var settingsSource = ReadRepoFile(Page);
        Assert.DoesNotContain(cardBackground, settingsSource, StringComparison.Ordinal);
        var settings = XDocument.Parse(settingsSource);
        var settingsRoot = Assert.Single(settings.Root!.Elements(ui + "Grid"));
        Assert.Equal("Transparent", (string?)settingsRoot.Attribute("Background"));
        var header = Assert.Single(settingsRoot.Elements(ui + "Grid"),
            e => (string?)e.Attribute("Grid.Row") == "0");
        Assert.Equal("24,12,24,0", (string?)header.Attribute("Margin"));
        Assert.Null(header.Attribute("Background"));
        var tabs = Assert.Single(settingsRoot.Elements(ui + "TabView"),
            e => (string?)e.Attribute(x + "Name") == "SettingsTabs");
        Assert.Equal("1", (string?)tabs.Attribute("Grid.Row"));
        Assert.Null(tabs.Attribute("Background"));
    }

    [Theory]
    [InlineData("private void ApplyThemePreference(ThemePreference pref)", true)]
    [InlineData("public void RefreshThemeIcon(ThemePreference pref)", false)]
    public void MainWindowThemeEntriesRefreshWithoutApplyingTheBaseThemeFirst(string signature, bool persists)
    {
        var body = MethodBody(Code(ReadRepoFile(MainWindowThemeCs)), signature);
        Assert.DoesNotContain("ThemeManager.Apply(", Regex.Replace(body, @"\s+", ""), StringComparison.Ordinal);
        Assert.Contains("_themePref = pref;", body, StringComparison.Ordinal);
        Assert.Contains("UpdateThemeIcon();", body, StringComparison.Ordinal);
        Assert.Contains("RefreshAppearance();", body, StringComparison.Ordinal);
        Assert.Contains("ApplyTitleBarButtonColors();", body, StringComparison.Ordinal);
        Assert.Contains("_widgetManager.ApplyThemeToAllAsync(pref);", body, StringComparison.Ordinal);
        if (persists)
        {
            Assert.Contains("_settings.SaveTheme(pref);", body, StringComparison.Ordinal);
            Assert.Contains("ThemePreferenceQuickSwitched?.Invoke(pref);", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SettingsSaveRefreshesBackgroundAndThemeWithoutAccentOverridesOrRetheming()
    {
        var body = MethodBody(Code(ReadRepoFile(ViewModel)), "private void Save()");
        var compact = Regex.Replace(body, @"\s+", "");
        Assert.DoesNotContain("ThemeManager.Apply(App.MainWindow,", compact, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "AccentResourceSync", "Retheme", "accentChanged", "SystemAccentColor", "RequestedTheme" })
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        Assert.Contains("_settings.SaveTheme(theme);", body, StringComparison.Ordinal);
        Assert.Contains("_settings.SaveThemeAccent(accentText);", body, StringComparison.Ordinal);
        Assert.Contains("ThemeAccent.Apply(accentText);", body, StringComparison.Ordinal);
        Assert.Contains("_settings.SaveWidgetBackdrop((WidgetBackdropKind)BackdropIndex);", body, StringComparison.Ordinal);
        Assert.Contains("_settings.SaveWidgetOpacity(WidgetOpacity);", body, StringComparison.Ordinal);
        Assert.Contains("_settings.SaveMainWindowBackdrop((WidgetBackdropKind)MainWindowBackdropIndex);", body, StringComparison.Ordinal);
        Assert.Contains("_settings.SaveMainWindowOpacity(MainWindowOpacity);", body, StringComparison.Ordinal);
        Assert.Contains("App.MainWindow?.DispatcherQueue.TryEnqueue(", body, StringComparison.Ordinal);
        Assert.Contains("App.MainWindow.RefreshThemeIcon(theme);", body, StringComparison.Ordinal);
        Assert.Contains("App.MainWindow?.RefreshAppearance();", body, StringComparison.Ordinal);
        Assert.Contains("mgr.RefreshAppearanceAsync();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeTitleButtonsFollowTheRequestedThemeInEveryForegroundState()
    {
        var body = MethodBody(Code(ReadRepoFile("src/StarMark.UI/MainWindow.Chrome.cs")), "private void ApplyTitleBarButtonColors()");
        Assert.Contains("var theme = RootGrid.RequestedTheme;", body, StringComparison.Ordinal);
        Assert.Contains("var isDark = theme == ElementTheme.Dark;", body, StringComparison.Ordinal);
        Assert.Contains("var foreground = isDark ? Colors.White : Colors.Black;", body, StringComparison.Ordinal);
        foreach (var state in new[] { "ButtonForegroundColor", "ButtonHoverForegroundColor", "ButtonPressedForegroundColor" })
            Assert.Contains($"titleBar.{state} = foreground;", body, StringComparison.Ordinal);
        Assert.Contains("titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0x99, foreground.R, foreground.G, foreground.B);", body, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedSourceTextContrastsWithItsActualAccentBrush()
    {
        var body = MethodBody(Code(ReadRepoFile("src/StarMark.UI/MainWindow.Status.cs")), "private void SetSourceButtonsHighlight(string source)");
        Assert.Contains("var accent = ThemeBrush.For(RootGrid.ActualTheme, \"AccentFillColorDefaultBrush\");", body, StringComparison.Ordinal);
        Assert.Contains("accent is SolidColorBrush accentBrush", body, StringComparison.Ordinal);
        Assert.Contains("SurfaceReadability.ReadableOn(accentBrush.Color)", body, StringComparison.Ordinal);
        Assert.Contains("btn.Background = selected ? accent :", body, StringComparison.Ordinal);
        Assert.Contains("btn.Foreground = selected ? onAccent : muted;", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Colors.White", body, StringComparison.Ordinal);
    }
}
