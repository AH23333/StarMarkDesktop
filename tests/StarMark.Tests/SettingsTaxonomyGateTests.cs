#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 设置页"一张卡住在哪一页"的守门（批次 WC-2 系列）。
/// <para>
/// 设置页被用户点名过两次同类缺陷：<b>功能过多且分类不合理</b>。修法只有"搬家"这一种动作，
/// 而搬家最容易留下的两种残骸恰恰是编译器与运行时都不报错的：
/// ① <b>同一个设置出现两份编辑入口</b>（搬走了旧的那份没删干净，或复制时漏删）——
///    两处绑定同一个属性时看着都"生效"，改一处另一处显示旧值，用户只能靠猜；
/// ② <b>相对方位的说法穿帮</b>（"与上方「组件材质」互不影响"被留在另一页里）——
///    文字仍是通顺的中文，只是指向了一个不存在的位置，等于假指引。
/// </para>
/// <para>这两类都只能扫 XAML 结构来钉，故有此文件（测试工程不引用 <c>StarMark.UI</c>）。</para>
/// </summary>
public sealed class SettingsTaxonomyGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>
    /// 页签清单。批次 VC-3 多出一栏「使用说明」——<b>那是用户当场点的第三条裁决</b>
    /// （"进入软件后没有教程／说明"→ 首启一次性引导＋每页「？」＋设置页说明），不是我自己加的。
    /// 位置排在「常规」之后：日常项仍在最前、默认落地的还是「常规」（改默认页会让一次更新悄悄换掉用户的入口），
    /// 可选能力与诊断照旧在后。
    /// <para><b>这里钉的是身份而不是标题</b>（批次 WI-2 改的）：标题从这一批起进了界面语言表，
    /// 中文档叫「健康与诊断」、英文档叫 "Health & diagnostics"。清单继续抄标题的话，
    /// 这张表就跟着语言走——切一次语言，八格分类判据全变成假失败，而真失败（跳转落在默认页）反而没人钉。</para>
    /// </summary>
    private static readonly string[] ExpectedTabs =
    {
        "general", "guide", "ai", "widgets", "extend", "hotkeys", "data", "health",
    };

    private const string TabIdsSource = "src/StarMark.UI/Views/SettingsTabIds.cs";
    private const string SettingsCtor = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string SettingsGuide = "src/StarMark.UI/Views/SettingsPage.Guide.cs";
    private const string WidgetMenu = "src/StarMark.UI/Views/WidgetWindow.Menu.cs";
    private const string MainGuide = "src/StarMark.UI/MainWindow.Guide.cs";

    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>页签清单与顺序都钉死。<b>顺序也是分类的一部分</b>：日常项在前、可选能力在后。
    /// <para>比的是两样东西：XAML 上那八颗 <c>Tag</c> 的顺序，与调用方真正读的那张身份表
    /// （<c>SettingsTabIds.InOrder</c>）。两边都比一次才堵得住"加了栏忘了进表"与"改了表忘了给 Tag"——
    /// 后一种在屏幕上完全不报错，只是跳转悄悄落在默认页。</para></summary>
    [Fact]
    public void TabsAreTheAgreedTaxonomyInOrder()
    {
        var found = Regex.Matches(ReadRepoFile(Xaml), @"<TabViewItem x:Name=""\w+"" Tag=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(ExpectedTabs, found);
        Assert.Equal(ExpectedTabs, DeclaredTabIds());
    }

    /// <summary>把 <c>SettingsTabIds</c> 的 <c>InOrder</c> 按它自己写的顺序还原成那组串。
    /// <c>Guide</c> 那颗是<b>转发</b> Core 的（<c>PageGuides.SettingsTabKey</c>），所以这里也按 Core 的值算。</summary>
    private static string[] DeclaredTabIds()
    {
        var src = Code(ReadRepoFile(TabIdsSource));
        var literals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(src, @"public const string (\w+) = ""([^""]*)"";"))
            literals[m.Groups[1].Value] = m.Groups[2].Value;
        literals["Guide"] = StarMark.Core.Guide.PageGuides.SettingsTabKey;

        var listed = Regex.Match(src, @"InOrder =\s*\{([^}]*)\}", RegexOptions.Singleline).Groups[1].Value;
        var names = listed.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
        Assert.Equal(ExpectedTabs.Length, names.Length);
        return names.Select(n =>
        {
            Assert.True(literals.ContainsKey(n), $"InOrder 列了 {n}，但那颗常量没有可读的字面串（第二份出处？）");
            return literals[n];
        }).ToArray();
    }

    /// <summary>每张卡的标题在全页唯一：搬家时漏删旧副本，第一现场就是这里。</summary>
    [Fact]
    public void NoCardTitleIsDuplicated()
    {
        var titles = Regex.Matches(ReadRepoFile(Xaml),
                "<TextBlock Text=\"([^\"]+)\" Style=\"\\{StaticResource SettingTitle\\}\"")
            .Select(m => m.Groups[1].Value).ToList();

        Assert.True(titles.Count > 10, $"只扫到 {titles.Count} 张卡——锚点本身失效了，守门不能白过");
        Assert.Equal(titles.Count, titles.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 每个可写设置<b>只有一个</b> <c>Mode=TwoWay</c> 编辑入口。
    /// 标题唯一仍挡不住"两张卡各绑一次同一属性"（卡片标题不同、绑的却是同一个量）。
    /// </summary>
    [Fact]
    public void EachSettingHasExactlyOneEditor()
    {
        var xaml = ReadRepoFile(Xaml);
        var writable = new[]
        {
            "BackdropIndex", "MainWindowBackdropIndex", "WidgetOpacity", "MainWindowOpacity",
            "LocalDiskSearchEnabled", "EyeRestEnabled", "EyeRestDeferOnFullscreen",
            // 护眼那两颗下拉以前漏登记过（批次 BK 补上，批次 RS 把"强制"开关换成提醒形式那一颗）：
            // 不登记的话"只有一个编辑入口"对它根本不成立。
            "EyeRestIntervalIndex", "EyeRestNoticeIndex",
            "TrendingEnabled", "ClipboardHistoryEnabled", "EnableTray", "MinimizeToTray",
            // GithubToken 从这张表里移出去了（批次 UI/P-146）：那一格改成 PasswordBox，而 PasswordBox 的
            // Password 不可绑定，所以"只有一个编辑入口"对它不能再用 `Mode=TwoWay` 计数——
            // 同一件事由 CredentialInputGateTests 按"代码里那一对同名读写"的形状钉住，两处都不许松。
            "CanvasEnabled", "CanvasInScreenshots",
            // 批次 RN：光标那块圆的半径（滑杆）。不登记的话"只有一个编辑入口"对它根本不成立。
            "CursorCircleRadiusDip",
            // 批次 RP：截屏功能总开关（同一族，同一页）。
            "CaptureEnabled",
            // ClipIMG-1d 新增的三格可写设置：注册进这份清单，否则"只有一个编辑入口"这条对它们根本不生效。
            "ClipboardImageEnabled", "ClipboardImageMaxValue", "ClipboardTextMaxValue",
            // ClipIMG-3b：导出带不带图片本体（开关住在备份卡里，见设置闸门那条摆位判据）。
            "BackupClipboardImagesEnabled",
            // 批次 BK：自动备份的开关与间隔档（两颗都住在「数据」页的备份卡里）。
            "AutoBackupEnabled", "AutoBackupIntervalIndex",
            // 批次 UE：自动检查更新那一颗（住在「常规」页的「关于与更新」卡里，挨着它要用的 Token）。
            "UpdateAutoCheckEnabled",
        };

        foreach (var p in writable)
            Assert.Equal(1, Count(xaml, $"ViewModel.{p}, Mode=TwoWay"));
    }

    /// <summary>分类本身：卡必须住在裁决给它的那一页（"搬了"这件事要有据可查，否则只是口头完成）。</summary>
    [Fact]
    public void CardsLiveOnThePagesTheUserRuledFor()
    {
        var xaml = ReadRepoFile(Xaml);

        var ai = Between(xaml, "<TabViewItem x:Name=\"TabAi\" Tag=\"ai\"", "</TabViewItem>");
        Assert.Contains("这三样分别要填什么", ai);
        var regular = Between(xaml, "<TabViewItem x:Name=\"TabGeneral\" Tag=\"general\"", "</TabViewItem>");
        // Token 那一格是"库"的日常配置，卡留在常规页。批次 UI/P-146 之后这里**不再数绑定**：
        // 那颗编辑格换成了 PasswordBox（Password 不可绑定），编辑入口的形状与"只有一处读写"
        // 由 CredentialInputGateTests 钉；本文件只钉"这张卡住在哪一页"这一件事。
        // 读抹掉注释之后那一份（#228：注释会替被改掉的标题答题）。
        // 批次 WI-2d 起这张卡的标题进了文案表 ⇒ 锚点从"屏幕上那几个字"搬到那颗键：
        // 判的仍是"这张卡住在常规页"，改的只是地址（同一批把页签身份解耦时定下的口径）。
        Assert.Contains("i18n:LocText.Key=\"settings.general.githubTitle\"", Markup(regular));
        Assert.DoesNotContain("这三样分别要填什么", regular);
        Assert.DoesNotContain("AI 助手", Markup(regular));    // 说明卡与表单一起走，不能一份表单搬家、一份说明书留下
        // 批次 UE：「关于与更新」留在常规页——它用的凭据就在同一页，而"版本号 + 有没有新版"是日常一件事，
        // 不属于「数据」（那一页管的是本机那些不可重建的东西）也不属于「健康与诊断」（那一页是取证）。
        // 读<b>抹掉注释之后</b>的那一份：UE24 那一格实测出来，卡片上方那行注释会把标题替它答上，
        // 于是"卡搬走了"这种结构改动在这一格里没有凭据（同一族：#171/#179 锚点要认代码不认注释）。
        Assert.Contains("i18n:LocText.Key=\"settings.general.aboutTitle\"", Markup(regular));
        Assert.Contains("ViewModel.UpdateAutoCheckEnabled", regular);

        var widgets = Between(xaml, "<TabViewItem x:Name=\"TabWidgets\" Tag=\"widgets\"", "</TabViewItem>");
        Assert.Contains("组件材质", widgets);
        Assert.Contains("ViewModel.BackdropIndex", widgets);
        Assert.DoesNotContain("ViewModel.MainWindowBackdropIndex", widgets);

        var extras = Between(xaml, "<TabViewItem x:Name=\"TabExtend\" Tag=\"extend\"", "</TabViewItem>");
        Assert.Contains("本地磁盘搜索", extras);
        Assert.Contains("ViewModel.LocalDiskSearchEnabled", extras);
        Assert.Contains("网址来源（RSS / Atom）", extras);
        Assert.DoesNotContain("剪贴板历史", Markup(extras));
        // 屏幕画布（批次 WD-5）：开关与那份只读键位一览都住在拓展功能页
        Assert.Contains("<TextBlock Text=\"屏幕画布\" Style=\"{StaticResource SettingTitle}\"", extras);
        Assert.Contains("ViewModel.CanvasEnabled", extras);
        Assert.Contains("ViewModel.CanvasHotkeyRows", extras);
        // 「截图带画布」（批次 WH）跟着画布走：它说的是同一块玻璃，只不过影响的是截图那一帧
        Assert.Contains("ViewModel.CanvasInScreenshots", extras);
        // 光标那块圆的半径（批次 RN）：同一块圆的尺寸设置，必须和那块圆所在的卡同页——
        // 搬到「常规」就会出现"画布的设置分散在两页"，而它坏的时候没人知道去哪根滑杆
        Assert.Contains("ViewModel.CursorCircleRadiusDip, Mode=TwoWay", extras);
        // 截屏总开关（批次 RP）：发起人点名"放入拓展功能设置页"。它与画布那条是同一族（都是屏幕上的覆盖层），
        // 散到「常规」就会出现"关掉截图要去常规页找开关、关掉画布在拓展页"这种找不着入口的改动
        Assert.Contains("<TextBlock Text=\"截屏（截图 / 贴图 / 识字）\" Style=\"{StaticResource SettingTitle}\"", extras);
        Assert.Contains("ViewModel.CaptureEnabled, Mode=TwoWay", extras);
        Assert.DoesNotContain("ViewModel.CaptureEnabled", regular);

        var health = Between(xaml, "<TabViewItem x:Name=\"TabHealth\" Tag=\"health\"", "</TabViewItem>");
        Assert.Contains("护眼 · 休息提醒", health);
        Assert.Contains("ViewModel.EyeRestEnabled", health);

        var data = Between(xaml, "<TabViewItem x:Name=\"TabData\" Tag=\"data\"", "</TabViewItem>");
        // ClipIMG-1d：图片开关、两个上限、那句隐私警示，全部与"剪贴板历史"总开关同卡同页。
        // 分开摆会造出"两个开关各在一页"的第三种状态——用户得先猜哪个管哪个。
        Assert.Contains("ViewModel.ClipboardImageEnabled, Mode=TwoWay", data);
        Assert.Contains("ViewModel.ClipboardImageMaxValue, Mode=TwoWay", data);
        Assert.Contains("ViewModel.ClipboardTextMaxValue, Mode=TwoWay", data);
        Assert.Contains("图片无法做敏感检查", data);
        // 批次 BK：自动备份那颗开关与间隔下拉管的是"程序自己多久落一份"，与手动导出同属这张危险区卡；
        // 搬到别页就会出现"在剪贴板页里改备份频率"这种找不到入口的改动。
        Assert.Contains("ViewModel.AutoBackupEnabled, Mode=TwoWay", data);
        Assert.Contains("ViewModel.AutoBackupIntervalIndex, Mode=TwoWay", data);
        Assert.Contains("ViewModel.AutoBackupStatus", data);
        Assert.DoesNotContain("ViewModel.ClipboardImageEnabled", Between(xaml, "<TabViewItem x:Name=\"TabExtend\" Tag=\"extend\"", "</TabViewItem>"));
        // 「数据」页仍会在提权说明里提到本地磁盘搜索（那是一句原因，不是第二处开关），
        // 所以钉的是"这张卡不在这页"，而不是这个词不在这页。
        Assert.DoesNotContain("<TextBlock Text=\"本地磁盘搜索\" Style=\"{StaticResource SettingTitle}\"", data);
        Assert.DoesNotContain("ViewModel.LocalDiskSearchEnabled", data);
    }

    /// <summary>
    /// 跨页的相对方位说法（"上方／下方／下一栏"）必须清干净：卡一搬家，这些词就指向了不存在的东西。
    /// 点名页签的说法（"在「常规」页"）不受限制。
    /// </summary>
    [Fact]
    public void NoRelativeDirectionSurvivesAMove()
    {
        var markup = Markup(ReadRepoFile(Xaml));

        foreach (var phrasing in new[] { "上方「", "下方「", "见下方", "在下方单独设置" })
            Assert.False(markup.Contains(phrasing, StringComparison.Ordinal), $"残留的相对方位说法：{phrasing}");
    }

    /// <summary>
    /// 每一栏要同时有<b>名字、身份、按键的标题</b>，三样缺一都对应一种坏法：
    /// 没 <c>Tag</c> ⇒ 跳过去落在默认页（不报错）；留着 <c>Header="..."</c> 字面 ⇒ 那份标题在切语言之后
    /// 变成没人读的副本（WI-A2 那一族，批次 WI-2 把它扩到页签）。
    /// </summary>
    [Fact]
    public void EveryTabHasNameIdentityAndKeyedTitle()
    {
        var xaml = ReadRepoFile(Xaml);
        Assert.Equal(ExpectedTabs.Length, Regex.Matches(xaml, @"<TabViewItem x:Name=""Tab\w+"" Tag=""[a-z]+""").Count);
        Assert.Empty(Regex.Matches(xaml, @"<TabViewItem[^>]*Header="""));
        foreach (var id in ExpectedTabs)
            Assert.Equal(1, Count(xaml, $@"Tag=""{id}"" i18n:LocText.Key=""settings.tab.{id}"""));
    }

    /// <summary>
    /// <b>跳转带的是身份，不是屏幕上那几个字</b>。
    /// <para>判据钉在读的那一侧而不是写的那一侧：<c>SelectTab</c> 必须比 <c>tab.Tag</c> 而不再比
    /// <c>tab.Header as string</c>（标题从这一批起跟着语言走，比它就是比一个每次换语言都会变的值）；
    /// 调用方一律传常量，不许把中文页签名抄进参数——抄进去的那一格会在英文档里安静地落回默认页。</para>
    /// </summary>
    [Fact]
    public void TabJumpsCarryIdentityNeverDisplayText()
    {
        var select = MethodBody(Code(ReadRepoFile(SettingsCtor)), "public void SelectTab");
        Assert.Contains("tab.Tag as string, tabId", select, StringComparison.Ordinal);
        Assert.DoesNotContain("tab.Header as string", select, StringComparison.Ordinal);
        Assert.Contains("StarLog.Warn(", select, StringComparison.Ordinal);   // 找不到那一栏要出声

        Assert.Contains("Present(true, SettingsTabIds.Health)", Code(ReadRepoFile(WidgetMenu)), StringComparison.Ordinal);
        Assert.Contains("SelectTab(PageGuides.SettingsTabKey)", Code(ReadRepoFile(SettingsGuide)), StringComparison.Ordinal);
        Assert.Contains("OpenSettingsPage(PageGuides.SettingsTabKey)", Code(ReadRepoFile(MainGuide)), StringComparison.Ordinal);

        // ⚠ 这一族必须按<b>句式</b>扫整个 UI 工程，不能一颗一颗点名。反向台架 W2-A3 第一次跑：
        // 把 `MainWindow.Guide.cs` 里那颗「看完整说明」的实参退回 `"使用说明"`，判据<b>照样是绿的</b>——
        // 因为它只钉了 `WidgetMenu` 与 `SettingsGuide` 两份文件，第三个入口根本没人管
        // （PD 那一族的老账：该红没红有两种因，这一种是"真正该管的事没人钉"⇒ 补证人，不是改预期）。
        // 现场读数（写这条之前先数的）：这三句式在 `src/StarMark.UI` 全树 0 处 ⇒ "零"才配当期望值。
        foreach (var (path, text) in SourceGate.ReadRepoUnder("src/StarMark.UI"))
        {
            if (!path.EndsWith(".cs", StringComparison.Ordinal)) continue;
            var code = SourceGate.Code(text);
            foreach (var call in new[] { "OpenSettingsPage(\"", "SelectTab(\"", "Present(true, \"" })
                Assert.False(code.Contains(call, StringComparison.Ordinal),
                    $"{path.Replace('\\', '/')} 拿屏幕上那几个字当页签地址：{call}");
        }
    }

    /// <summary>「使用说明」那一栏的身份只有一个出处（Core 那张说明表）：UI 侧转发它，不许再抄一份字面。</summary>
    [Fact]
    public void TheGuideTabIdentityHasExactlyOneSource()
        => Assert.Contains("Guide = PageGuides.SettingsTabKey;", Code(ReadRepoFile(TabIdsSource)), StringComparison.Ordinal);
}
