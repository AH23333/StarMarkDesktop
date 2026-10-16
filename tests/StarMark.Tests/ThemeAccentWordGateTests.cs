#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    private const string ThemeXaml = "src/StarMark.UI/Themes/StarMarkTheme.xaml";

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    /// <summary>代码侧（抹注释）的提问者普查：⛔ 只钉一两个文件，那会因搬家而假绿（RV 那条口径）。</summary>
    private static int AsksInCode(string quoted)
        => ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Sum(f => Count(Code(f.Text), quoted));

    /// <summary>① 四颗新键：表里各一行、代码与标记合计各一个主人，三语各说各的。</summary>
    [Theory]
    [InlineData("settings.general.accentTitle")]
    [InlineData("settings.general.accentSystemAccent")]
    [InlineData("settings.general.accentCustom")]
    [InlineData("settings.general.accentScope")]
    public void EachNewWordHasOneOwnerAndSaysItDifferentlyInEachLanguage(string key)
    {
        var quoted = "\"" + key + "\"";
        Assert.Equal(1, AsksInCode(quoted) + Count(ReadRepoFile(Page), quoted));
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

        // 背衬取色只有两个读者：套背衬那一腿 + 兜底画笔那一腿
        Assert.Equal(2, Count(Code(ReadRepoFile(WidgetAppearanceCs)), "MaterialTint.BackdropColor("));
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
        Assert.Equal(1, Count(code, "tint?.Color"));                 // 那颗色只交给背衬一次
        Assert.Contains("backdropOwnsSurface", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑤ 那颗自定义色<b>只有一个写的人</b>（落盘那一刻），读者只有两处（背衬染料 + 强调画笔）。
    /// <para>⛔ 让读者每次去读 settings.json：材质套用是启动头号嫌疑，画笔解析里加一次整档反序列化
    /// 就是给首屏加钱（性能纪律）。</para>
    /// </summary>
    [Fact]
    public void TheCustomAccentHasOneWriterAndTwoReaders()
    {
        Assert.Equal(2, ReadRepoUnder("src").Sum(f => Count(Code(f.Text), "ThemeAccent.Apply(")));
        Assert.Contains("Helpers.ThemeAccent.Apply(fileSettings.LoadThemeAccent());", Code(ReadRepoFile(App)),
            StringComparison.Ordinal);                              // 首个窗口之前
        Assert.Contains("ThemeAccent.Apply(accentText);", Code(ReadRepoFile(ViewModel)), StringComparison.Ordinal);

        Assert.Equal(1, Count(Code(ReadRepoFile(WidgetAppearanceCs)), "AppPalette.ResolveAccent("));
        Assert.Equal(1, Count(Code(ReadRepoFile(ThemeBrushCs)), "ThemeAccent.Custom"));
        Assert.Contains("AppPalette.ParseArgb(hexFromSettings)", Code(ReadRepoFile(ThemeAccentCs)), StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑥ 强调色那一族画笔的<b>覆盖点只有一处</b>：XAML 里那两颗写死的蓝（浅／深各一行）仍是
    /// "随系统"那一档的出处，代码侧那颗覆盖只认那三颗键，且淡底的浓淡只换 alpha（那颗判据住在 Core）。
    /// </summary>
    [Fact]
    public void TheAccentBrushFamilyIsOverriddenFromOnePlace()
    {
        var brush = Code(ReadRepoFile(ThemeBrushCs));
        var block = Between(brush, "AccentKeys =", "}");
        foreach (var key in new[] { "AppAccentBrush", "AppAccentSoftBrush", "WidgetDropHintBrush" })
        {
            Assert.Contains("\"" + key + "\"", block, StringComparison.Ordinal);
            Assert.Equal(2, Count(ReadRepoFile(ThemeXaml), "x:Key=\"" + key + "\""));   // Light＋Dark
            // 覆盖满不满，问的是"还有谁绕开这颗门自己拿"：这三颗在 XAML 里只定义、没人按键取⇒ 只有代码这一条路。
            Assert.Equal(0, Count(ReadRepoFile(ThemeXaml), "{ThemeResource " + key + "}"));
        }
        Assert.Equal(1, Count(brush, "AccentKeys.Contains"));
        Assert.Contains("AppPalette.Soft(", brush, StringComparison.Ordinal);
        Assert.DoesNotContain("FromArgb(0x1A", brush);               // ⛔ 在画笔侧再写一份 alpha
        // 第四颗是这颗门的<b>射程边界</b>，不是它的成就：那颗画笔住在 WinUI 自己的字典里，
        // XAML 侧按 <c>{ThemeResource}</c> 直接取（实测三处＝ItemCard／StarMarkTheme／SettingsPage），
        // 那条路不经过这里⇒ 自定义色今天管不到它们，控件内部那颗蓝同理（记在 P-213）。
        // 这三处颗数钉在这里，是为了"半覆盖"这件事有一个会响的地方，⛔ 让注释把它说成已经全覆盖。
        Assert.Contains("\"AccentFillColorDefaultBrush\"", block, StringComparison.Ordinal);
        Assert.Equal(3, new[] { "src/StarMark.UI/Controls/ItemCard.xaml", ThemeXaml, "src/StarMark.UI/Views/SettingsPage.xaml" }
            .Sum(f => Count(ReadRepoFile(f), "{ThemeResource AccentFillColorDefaultBrush}")));
    }

    /// <summary>⑦ 设置页那一行：一个编辑入口、一颗读数，⛔ 两处编辑同一颗量。</summary>
    [Fact]
    public void TheAccentSettingHasExactlyOneEditorAndItsReadingIsReadOnly()
    {
        var xaml = ReadRepoFile(Page);
        Assert.Equal(1, Count(xaml, "ViewModel.AccentIndex, Mode=TwoWay"));
        Assert.Equal(1, Count(xaml, "ViewModel.ThemeAccentColor, Mode=TwoWay"));
        Assert.Equal(1, Count(xaml, "ViewModel.ThemeAccentBrush, Mode=OneWay"));
        Assert.Equal(0, Count(xaml, "ViewModel.ThemeAccentBrush, Mode=TwoWay"));        // 那颗是读数⛔ 是入口
        Assert.Equal(2, Count(xaml, "ViewModel.CustomAccentVisible, Mode=OneWay"));      // 取色格＋预览方块同进同退

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
}
