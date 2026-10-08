#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-52（P-183）的闸门：<b>「组件外观」那扇编辑器窗与公共对话框工厂自己的屏幕字，只许有表里那一份出处</b>。
/// <para>
/// ⚠ 这一族的证人全是<b>源码形状</b>：<c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>（坑表 #370 那条边界），
/// 所以"切到英文之后那扇编辑器说什么"只能由真机读数补（已排在 #575 那一跑），这里⛔ 假称读过。
/// </para>
/// <para>尺＝抹注释后逐行取连续汉字片段（坑表 #375／#378：调用形状那把尺看不见私有包装的实参，
/// 引号配对那把看不见插值串里的三元）；日志行按 #368 那条界线<b>另列且不进表</b>。</para>
/// <para><b>这一批新暴露的一面</b>（坑表 #387）：交进<b>公共工厂</b>的那几颗字（<c>CenteredDialog</c> 的默认按钮、
/// 各宿主递进去的标题与正文）既不调取词出口、也不是宿主档自己的屏幕属性形状——四把旧尺全都看不见。
/// 这里⛔ 修那 59 处里的本批三处（编辑器那颗确认框＋工厂自己的默认值），其余登记成新开放项（P-185）。</para>
/// </summary>
public sealed class WidgetAppearanceWordGateTests
{
    private const string Editor = "src/StarMark.UI/Views/WidgetAppearanceEditor.cs";
    private const string Dialog = "src/StarMark.UI/Helpers/CenteredDialog.cs";
    private const string Options = "src/StarMark.UI/Views/SettingsPage.Options.cs";
    private const string SnapshotPage = "src/StarMark.UI/Views/SnapshotPage.xaml.cs";

    private static readonly Regex Cjk = new(@"[一-鿿]+", RegexOptions.Compiled);

    private static string CodeOf(string rel) => Code(ReadRepoFile(rel));

    /// <summary>带着汉字且⛔ 日志的行（逐行原文，红的时候直接印出来，省得下一手再猜是哪颗）。</summary>
    private static string[] CjkLines(string code)
        => code.Replace("\r\n", "\n").Split('\n')
            .Where(line => Cjk.IsMatch(line) && !line.Contains("StarLog", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    /// <summary>
    /// 编辑器那一扇⛔ 再留一颗自己的汉字面字（它登记过 28 行、0 处取词，就是这一格要归零的那个数）。
    /// <para>这条是<b>一般式</b>（台架规则 #131）：按"抹注释后逐行找汉字"扫，⛔ 按上次看到的那几种写法
    /// （<c>Content="</c>／<c>Text =</c>）点名——换个形状（实参、元组、内插里的三元）就溜过去了。</para>
    /// </summary>
    [Fact]
    public void TheEditorCarriesNoWordsOfItsOwn()
    {
        var left = CjkLines(CodeOf(Editor));
        Assert.True(left.Length == 0, $"编辑器里还有 {left.Length} 行屏幕字没交表：\n  " + string.Join("\n  ", left));
    }

    /// <summary>
    /// 公共对话框工厂<b>自己那几颗默认字</b>也交了表（「确定／取消」各两份 ＋「知道了」）。
    /// <para>为什么单独立一格：工厂里的字面量<b>不在任何宿主档</b>，屏幕普查按宿主数的时候永远读不到它，
    /// 而它一次改动管着全仓所有弹窗（"这处坏＝N 处坏"的那一类，规则 #107）。
    /// 判据成对：一条语言读数（那五句只许从表里取）＋一条形状禁项（⛔ 默认值再写成中文字面量）。</para>
    /// </summary>
    [Fact]
    public void TheDialogFactorysOwnVerbsComeFromTheTable()
    {
        var code = CodeOf(Dialog);
        var left = CjkLines(code);
        Assert.True(left.Length == 0, $"工厂里还有 {left.Length} 行屏幕字：\n  " + string.Join("\n  ", left));

        // 读数那一半：五处动词只许现场问表（Prompt 两颗＋Confirm 两颗＋Message 那颗「知道了」）。
        Assert.Equal(5, Count(code, @"Locale.T(""dialog."));
        // 禁项那一半（带引号点名⛔ 裸词，裸词会撞上日志措辞——#367 那一族）：默认值不许再写字面量。
        Assert.Empty(Regex.Matches(code, @"""(确定|取消|知道了)"""));
    }

    /// <summary>
    /// <b>常驻</b>那一族（建一次就挂在屏幕上、而编辑器可以开着不动换语言）只许交键走 <c>LocText</c> 重贴：
    /// 分区标题、三颗对象按钮、底部三颗按钮、窗口标题。当场取词（<c>Locale.T</c>）写进它们的<b>本体</b>
    /// ＝把那一句钉死在创建那一刻的语言上（WI-44／WI-45 那条判据，这里落到这一族）。
    /// </summary>
    [Fact]
    public void TheElementsThatOutliveTheirFirstPaintGetAKeyNotAWord()
    {
        foreach (var signature in new[] { "private static TextBlock MakeLabel", "private static Button MakeButton" })
        {
            var body = MethodBody(CodeOf(Editor), signature);
            Assert.Contains("LocText.SetKey(", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Locale.T(", body, StringComparison.Ordinal);
        }

        var objects = MethodBody(CodeOf(Editor), "Button MakeObjectButton");
        Assert.Contains("LocText.SetKey(ob, titleKey)", objects, StringComparison.Ordinal);

        // 窗口标题只许走那张第二名单（Window 不是 DependencyObject，坑表 #338）
        Assert.Equal(1, Count(CodeOf(Editor), "LocText.SetWindowTitle(win, \"widget.appearance.windowTitle\")"));
    }

    /// <summary>
    /// 那一排<b>随状态重建</b>的字（预设颜色按钮、当前颜色那一行、"默认"那颗读数、一次性确认框那四句）
    /// 反过来只许<b>当场取词</b>：⛔ 挂 <c>LocText</c>——它们开完就被下一次状态覆盖，挂上去只是名单里
    /// 几条永远贴不出去的空壳（WI-45 同条判据在这一族的另一面）。
    /// <para>⚠ 这类宿主自己的节拍里<b>没有语言这件事</b> ⇒ 必须自己订阅 <c>Locale.Changed</c>，
    /// 且与 <c>-=</c> 成对（坑表 #386，✅WI-51 那条口径在这一族第一次落到"编辑器"这种长命宿主上）。</para>
    /// </summary>
    [Fact]
    public void TheWordsRebuiltOnEachStateChangeAreTakenAtThatMomentAndTheHostFollowsLanguage()
    {
        var code = CodeOf(Editor);
        Assert.Equal(4, Count(code, @"Locale.T(""widget.appearance.cancelConfirm"));
        Assert.Equal(1, Count(code, @"Locale.T(""widget.appearance.scaleDefault"""));
        Assert.Equal(2, Count(code, @"Locale.T(""widget.appearance.followGlobal"""));
        Assert.Equal(1, Count(code, @"Locale.T(nameKey)"));
        // 一次性对话框⛔ 是 LocText 的宿主：那一族只在 LocText.Set 那一臂出现，这里按整档数一颗都不许多。
        Assert.Equal(3, Count(code, "LocText.SetKey("));

        Assert.Equal(1, Count(code, "Locale.Changed += RebuildWords"));
        Assert.Equal(1, Count(code, "Locale.Changed -= RebuildWords"));
        Assert.Contains("wordRefreshers", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 材质那一格＝<b>数据项＋模板</b>（WI-2c 那条路），而且六档名字⛔ 在这档另立一份：
    /// 清单的主人住在 <see cref="Options"/> 的 <c>WidgetMaterialWords.SixTiers</c>（三颗下拉共用）。
    /// <para>合取的两半都要钉：只挂 <c>ItemsSource</c> 不挂 <c>ItemTemplate</c>，WinUI 会去 <c>ToString()</c>
    /// 那颗数据项——屏幕上是一整排 <c>LocOption { Key = settings…, … }</c>（坑表 #180 同族）。
    /// 那颗旧的 <c>ComboItem</c> 是这条路的反面教材：文字是<b>建那一刻抄走的快照</b>（坑表 #272／#275）。</para>
    /// </summary>
    [Fact]
    public void TheMaterialComboIsDataPlusTemplateAndSharesTheOneWordList()
    {
        var code = CodeOf(Editor);
        Assert.Equal(0, Count(code, "ComboItem"));
        Assert.Equal(0, Count(code, "settings.general.material."));          // ⛔ 在这档抄一遍材质键
        Assert.Equal(0, Count(code, "IndexOfBackdrop"));                     // ⛔ 在别处再写一份 kind↔第几项
        Assert.Contains("WidgetMaterialWords.SixTiers", code, StringComparison.Ordinal);
        Assert.Contains("LocOption.T(\"widget.appearance.followGlobal\")", code, StringComparison.Ordinal);
        Assert.Contains("Application.Current.Resources[\"LocOptionTemplate\"]", code, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "backdropCombo.ItemTemplate"));          // 挂了数据源就必须挂模板
        // 材质"是哪一档"也只许问那份清单一次（⛔ 在宿主里再写一份 kind↔第几项：加一档时那份必漏，且会静默指错一档）
        Assert.Equal(2, Count(code, "WidgetMaterialWords.IndexOf("));
    }

    /// <summary>
    /// 工厂那颗默认值改成"没有默认"之后，<b>原本靠默认值的那一颗调用方必须自己点名</b>（快照那一格详情窗）。
    /// <para>它省掉两颗按钮也能编译、也能跑，屏幕上只是再没有「确定／取消」——这一族坏法没有读数，
    /// 只有源码形状拦得住：那颗调用⛔ 再回到"什么都不传"。</para>
    /// </summary>
    [Fact]
    public void TheCallerThatUsedToLeanOnTheFactoryDefaultNowNamesItsWords()
    {
        var code = CodeOf(SnapshotPage);
        Assert.Equal(1, Count(code, @"primaryText: Locale.T(""dialog.ok"")"));
        Assert.Equal(1, Count(code, @"cancelText: Locale.T(""dialog.cancel"")"));
    }

    /// <summary>
    /// 预设调色板：名字交<b>键</b>、身份仍是那颗 hex（⛔ 拿名字当身份＝✅WI-46c 那颗组名的同一缺陷，
    /// 换语言后"选中的那颗颜色"会认不出来）。六颗共有名收 ✅P-181 那套词厂，⛔ 再抄一份中文。
    /// </summary>
    [Fact]
    public void TheSwatchStripKeepsHexAsIdentityAndTheTableAsName()
    {
        var block = Between(CodeOf(Editor), "(string NameKey, string Hex)[] Palette", "};");
        Assert.Equal(11, Regex.Matches(block, @"anno\.color\.\w+").Count);
        Assert.Equal(11, Regex.Matches(block, @"""#FF[0-9A-F]{6}""").Count);
        Assert.Empty(Cjk.Matches(block));
    }

    /// <summary>
    /// 本批新立的 26 行<b>每一颗都得有人问</b>（通用闸门 <c>TheTableHasNoRowNothingAsksFor</c> 已钉过全表，
    /// 这里按本批名单点一次名：⛔ 只靠那条计数判据——它抓不到"问错了键"，规则 #134 那一族）。
    /// </summary>
    [Fact]
    public void EveryRowThisBatchShippedIsActuallyAskedFor()
    {
        var asked = CodeOf(Editor) + CodeOf(Dialog);
        var keys = new[]
        {
            "dialog.ok", "dialog.gotIt",
            "anno.color.darkGray", "anno.color.lightGray", "anno.color.cyan", "anno.color.purple", "anno.color.pink",
            "widget.appearance.windowTitle", "widget.appearance.followGlobal",
            "widget.appearance.section.material", "widget.appearance.section.target",
            "widget.appearance.target.bg", "widget.appearance.target.fg", "widget.appearance.target.border",
            "widget.appearance.section.swatches", "widget.appearance.section.gradient", "widget.appearance.section.current",
            "widget.appearance.slider.borderThickness", "widget.appearance.slider.cornerRadius", "widget.appearance.slider.textScale",
            "widget.appearance.scaleDefault", "widget.appearance.resetGlobal",
            "widget.appearance.cancelConfirmTitle", "widget.appearance.cancelConfirmBody",
            "widget.appearance.cancelConfirmPrimary", "widget.appearance.cancelConfirmSecondary",
        };
        foreach (var key in keys)
        {
            Assert.True(UiStrings.TryGet(key, out _), $"本批登记的键不在表里：{key}");
            Assert.True(asked.Contains('"' + key + '"', StringComparison.Ordinal), $"本批这颗键没人问：{key}");
        }
        Assert.Equal(26, keys.Length);
    }

    /// <summary>
    /// 表里那三行<b>带读数形状</b>的口径（#100：一颗格子里不许有换行——换行是布局，不是话）：
    /// 那颗"有未保存更改"的确认正文原本分两行显示，交表时并成一句，⛔ 把 <c>\n</c> 抄进表。
    /// </summary>
    [Fact]
    public void NoRowOfThisBatchCarriesAControlCharacter()
        => Assert.All(UiStrings.All.Where(e => e.Key.StartsWith("widget.appearance.", StringComparison.Ordinal)),
            e => Assert.True(!e.Zh.Contains('\n') && !e.Zh.Contains('\r'), $"{e.Key} 的中文列里躺着一颗控制字符"));
}
