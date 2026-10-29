using System;
using System.Text.RegularExpressions;
using StarMark.Abstractions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 主窗顶栏与筛选那一行的<b>可访问名</b>（✅P-265 同族・片141）。
/// <para>形状与来路：这一批⛔ 是"读码觉得该有名字"起的手，而是<b>屏幕上数出来的</b>——
/// 一把普查尺（走桌面 RawView、按 rect 去重、ProcessId 与窗口名两把筛子各报颗数）现读主窗有 6 颗
/// 可交互元素在 UIA 里 Name 为空（<c>SyncButton</c>／<c>ThemeButton</c>／<c>WidgetsButton</c>／
/// <c>FileKindButton</c>／<c>SortCombo</c>／<c>LanguageCombo</c>），其中 <c>LanguageCombo</c> 正是本线自己那颗控件。</para>
/// <para>这里守的是<b>机制</b>那一半：名字走 <c>LocText.NameKey</c> 进重贴名单，
/// ⛔ 在 XAML 里写死一颗成品字——那是坑表 #276 那一族（换了语言屏幕上每颗都改口、只有名字停在上一门）。</para>
/// </summary>
public class MainBarNameKeyGateTests
{
    private const string LocText = "src/StarMark.UI/Helpers/LocText.cs";
    private const string MainXaml = "src/StarMark.UI/MainWindow.xaml";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    private static readonly string[] Six =
    {
        "SyncButton=top.sync",
        "ThemeButton=top.themeLabel",
        "WidgetsButton=top.widgetsTip",
        "FileKindButton=top.kindFilterLabel",
        "SortCombo=top.sortLabel",
        "LanguageCombo=top.searchLangLabel",
    };

    /// <summary>
    /// <c>NameKey</c> 的落点必须是 <c>AutomationProperties.NameProperty</c>，且<b>摘掉那颗键要把名字一起抹掉</b>：
    /// 只清屏幕上的字而留着注册项，下一次换语言会把上一版名字贴回来（与 <c>ApplyKey</c> 同一条口径）。
    /// 取词也⛔ 在这里另实现一份——重贴吃的是 <c>Paste</c> 那一处。
    /// </summary>
    [Fact]
    public void NameKeyPastesOntoTheAutomationNameAndDropsCleanly()
    {
        var cs = SourceGate.Code(SourceGate.ReadRepoFile(LocText));
        Assert.Contains("\"NameKey\", typeof(string), typeof(LocText), new PropertyMetadata(null, OnNameKeyChanged)", cs);
        var handler = SourceGate.MethodBody(cs, "private static void OnNameKeyChanged");
        Assert.Contains("Paste(d, AutomationProperties.NameProperty, key, null);", handler);
        Assert.Contains("Drop(d, AutomationProperties.NameProperty);", handler);
        Assert.DoesNotContain("Locale.T(", handler);
        Assert.DoesNotContain("Locale.Fmt(", handler);
        Assert.Contains("using Microsoft.UI.Xaml.Automation;", cs);
    }

    /// <summary>
    /// 普查点到的那六颗<b>一颗都不许漏、也不许挂错键</b>：只数颗数抓得到"漏一颗"，
    /// 抓不到"六颗挂同一颗键"这种错法 ⇒ 颗数与逐颗字面量都要钉。
    /// </summary>
    [Fact]
    public void TheSixCensusedControlsEachCarryTheirOwnNameKey()
    {
        var xaml = SourceGate.ReadRepoFile(MainXaml);
        foreach (var pair in Six)
        {
            var parts = pair.Split('=');
            var (name, key) = (parts[0], parts[1]);
            var at = xaml.IndexOf("x:Name=\"" + name + "\"", StringComparison.Ordinal);
            Assert.True(at >= 0, name + " 这颗元素不见了");
            var block = xaml.Substring(at, Math.Min(600, xaml.Length - at));
            Assert.True(block.Contains("i18n:LocText.NameKey=\"" + key + "\""),
                $"{name} 没挂上 {key}——屏幕普查里它又是一颗念不出名字的裸控件");
        }
        Assert.Equal(6, Regex.Matches(xaml, "i18n:LocText\\.NameKey=\"").Count);
    }

    /// <summary>
    /// 本批新添的四颗名字键必须<b>三语齐</b>（表形状闸门管"四参必填"，这一颗管"这一族⛔ 只写中文"）：
    /// 辅助技术读的是<b>当下语言</b>那一列，缺一门就是那一门语言的用户念出空白。
    /// </summary>
    [Fact]
    public void TheNewNameKeysAreTrilingual()
    {
        var table = SourceGate.ReadRepoFile(Table);
        foreach (var key in new[] { "top.themeLabel", "top.sortLabel", "top.kindFilterLabel", "top.searchLangLabel" })
        {
            var at = table.IndexOf("new Entry(\"" + key + "\",", StringComparison.Ordinal);
            Assert.True(at >= 0, "表里找不到 " + key);
            var end = table.IndexOf("),", at, StringComparison.Ordinal);
            var row = table.Substring(at, end - at + 1);
            Assert.Equal(4, Regex.Matches(row, "\"(?:[^\"\\\\]|\\\\.)*\"").Count);
        }
    }
}
