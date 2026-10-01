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

    private static readonly string[] ExpectedTabs =
    {
        "常规", "AI 增强", "桌面工具（组件）", "拓展功能", "快捷键", "数据", "健康与诊断",
    };

    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>页签清单与顺序都钉死。<b>顺序也是分类的一部分</b>：日常项在前、可选能力在后。</summary>
    [Fact]
    public void TabsAreTheAgreedTaxonomyInOrder()
    {
        var found = Regex.Matches(ReadRepoFile(Xaml), "<TabViewItem Header=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(ExpectedTabs, found);
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
            "TrendingEnabled", "ClipboardHistoryEnabled", "EnableTray", "MinimizeToTray", "GithubToken",
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

        var ai = Between(xaml, "<TabViewItem Header=\"AI 增强\">", "</TabViewItem>");
        Assert.Contains("这三样分别要填什么", ai);
        var regular = Between(xaml, "<TabViewItem Header=\"常规\">", "</TabViewItem>");
        Assert.Contains("ViewModel.GithubToken", regular);   // Token 是"库"的日常配置，留在常规页
        Assert.DoesNotContain("这三样分别要填什么", regular);
        Assert.DoesNotContain("AI 助手", Markup(regular));    // 说明卡与表单一起走，不能一份表单搬家、一份说明书留下
        // 批次 UE：「关于与更新」留在常规页——它用的凭据就在同一页，而"版本号 + 有没有新版"是日常一件事，
        // 不属于「数据」（那一页管的是本机那些不可重建的东西）也不属于「健康与诊断」（那一页是取证）。
        // 读<b>抹掉注释之后</b>的那一份：UE24 那一格实测出来，卡片上方那行注释会把标题替它答上，
        // 于是"卡搬走了"这种结构改动在这一格里没有凭据（同一族：#171/#179 锚点要认代码不认注释）。
        Assert.Contains("关于与更新", Markup(regular));
        Assert.Contains("ViewModel.UpdateAutoCheckEnabled", regular);

        var widgets = Between(xaml, "<TabViewItem Header=\"桌面工具（组件）\">", "</TabViewItem>");
        Assert.Contains("组件材质", widgets);
        Assert.Contains("ViewModel.BackdropIndex", widgets);
        Assert.DoesNotContain("ViewModel.MainWindowBackdropIndex", widgets);

        var extras = Between(xaml, "<TabViewItem Header=\"拓展功能\">", "</TabViewItem>");
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

        var health = Between(xaml, "<TabViewItem Header=\"健康与诊断\">", "</TabViewItem>");
        Assert.Contains("护眼 · 休息提醒", health);
        Assert.Contains("ViewModel.EyeRestEnabled", health);

        var data = Between(xaml, "<TabViewItem Header=\"数据\">", "</TabViewItem>");
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
        Assert.DoesNotContain("ViewModel.ClipboardImageEnabled", Between(xaml, "<TabViewItem Header=\"拓展功能\">", "</TabViewItem>"));
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
}
