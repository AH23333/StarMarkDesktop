#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-82 片2 的落地闸门：<b>「上一次同步、哪一源、为什么」也摆到设置页那张 GitHub 卡上</b>（P-212 走 (B) 的后半）。
/// <para>他裁 (B) 那一头时说的是"把原因摆上屏"，WI-81 落在了主窗那张提示卡上；而提示卡 9 秒自动收、
/// 还随时能点关闭，用户读到「凭据无效」时通常已经跳到设置页来换 Token 了——那里恰好是唯一没说"哪一源"的地方。</para>
/// <para>这批要防的形状很具体：<b>第二份汇总、第二份措辞</b>。所以禁项钉的是"设置页⛔ 自己再数一遍失败名单、
/// ⛔ 自己拼一句为什么"，正件钉的是"它问的是 Core 那一颗 <c>LastSummary</c>"。
/// 行为那一半（同一发同步＝同一颗对象、取消那一发不覆盖）住在 <see cref="SyncCoordinatorTests"/> 真测；
/// 这里只能钉写法（#184：测试工程⛔ 引用 StarMark.UI）。</para>
/// </summary>
public sealed class GithubSyncStatusGateTests
{
    private const string SyncPartial = "src/StarMark.UI/Views/SettingsPage.Sync.cs";
    private const string Shell = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string XamlFile = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    /// <summary>①那一行只许问那颗主人，⛔ 在界面上再算一份（"第二份真值"就是 P-212 的本体）。</summary>
    [Fact]
    public void TheSettingsRowAsksTheCoordinatorInsteadOfRecountingTheFailures()
    {
        var code = Code(ReadRepoFile(SyncPartial));
        Assert.Equal(2, Count(code, "LastSummary"));              // 进页读一次 + 换语言读一次
        Assert.Equal(1, Count(code, "summary.Message()"));        // 汇总那句：Core 交键，这里⛔ 取词
        Assert.Equal(1, Count(code, "summary.FailureLines()"));   // 逐源那几行：同上
        // 禁项必须配正面计数（#161），也必须是"下一批真会这么写"的形状：
        Assert.Equal(0, Count(code, "FailedCount"));              // 自己数失败几颗＝WI-80 判出来的那颗缺陷
        Assert.Equal(0, Count(code, ".Sources"));                 // 自己遍历源
        Assert.Equal(0, Count(code, "LocMessage.Of(\"sync."));    // 自己挑一句"为什么"塞进槽里
    }

    /// <summary>②那一行是宿主重贴的元素，⛔ 绑成品串；从没跑完过一次时整行收着（⛔ 编一句"已完成"）。</summary>
    [Fact]
    public void TheRowIsAnElementTheHostRepastes_NotAStringBinding()
    {
        var xaml = ReadRepoFile(XamlFile);
        Assert.Equal(1, Count(xaml, "x:Name=\"GithubSyncStatusText\""));
        var element = Regex.Match(xaml, @"<TextBlock x:Name=""GithubSyncStatusText""[^>]*?>", RegexOptions.Singleline).Value;
        Assert.True(element.Length > 0, "抽取器没抓到那一行——下面那三句禁项会因此永远绿（#161）");
        Assert.DoesNotContain("Text=\"{x:Bind", element, StringComparison.Ordinal);
        Assert.DoesNotContain("i18n:LocText.Key", element, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", element, StringComparison.Ordinal);
    }

    /// <summary>③订阅必须成对、跨线程必须自己 marshal（这颗服务是 <b>单例</b>：挂着就是每进一次设置页多一份）。</summary>
    [Fact]
    public void TheSubscriptionIsPairedAndMarshalled()
    {
        var code = Code(ReadRepoFile(SyncPartial));
        Assert.Equal(2, Count(code, "SyncCompleted -="));     // 先退再订那颗 + Unloaded 那颗
        Assert.Equal(1, Count(code, "SyncCompleted +="));
        Assert.Equal(1, Count(code, "DispatcherQueue.TryEnqueue"));
        Assert.Equal(0, Count(code, "Unloaded +="));          // 摘手臂只许走本页那一对 Loaded/Unloaded

        var shell = Code(ReadRepoFile(Shell));
        // 两颗 -=、一颗 +=：本页"先退再订"（可能被反复导航回来），再加 Unloaded 那一次摘＝两退一进（同页那四组手臂同一形状）。
        Assert.Equal(2, Count(shell, "Locale.Changed -= RepasteSyncStatus"));
        Assert.Equal(1, Count(shell, "Locale.Changed += RepasteSyncStatus"));
        Assert.Equal(1, Count(shell, "SubscribeSyncStatus()"));
        Assert.Equal(1, Count(shell, "UnsubscribeSyncStatus()"));
    }

    /// <summary>④两颗新词在表里、列数对，且<b>接合子那行的形状</b>就是它承诺的那件事。</summary>
    [Fact]
    public void TheTwoNewWordsAreInTheTableWithTheColumnsTheyClaim()
    {
        Assert.Equal(1, Count(ReadRepoFile(Table), "new Entry(\"settings.general.github.lastSync\""));
        Assert.Equal(1, Count(ReadRepoFile(Table), "new Entry(\"settings.general.github.lastSyncJoin\""));

        var head = Columns("settings.general.github.lastSync");
        Assert.Equal(3, head.Distinct(StringComparer.Ordinal).Count());   // zh/en/ja 互异（⛔ 把中文抄进日文列）
        var join = Columns("settings.general.github.lastSyncJoin");
        Assert.Single(join.Distinct(StringComparer.Ordinal));             // 接合子三列逐字相同（同 sync.src.lineJoin）
        Assert.Equal("{0}{2}{1}", join[0]);                               // 槽位＝两句话 + 一根分隔符，顺序不许反
    }

    /// <summary>读一颗键的三列正文（表里每行是 <c>new Entry(键, zh, en, ja)</c>）。</summary>
    private static string[] Columns(string key)
    {
        var m = Regex.Match(ReadRepoFile(Table),
            $"new Entry\\(\"{Regex.Escape(key)}\",\\s*\"([^\"]*)\",\\s*\"([^\"]*)\",\\s*\"([^\"]*)\"\\)",
            RegexOptions.Singleline);
        Assert.True(m.Success, $"表里读不到「{key}」那一行的三列（抽取器该红，⛔ 让禁项空转）");
        return [m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value];
    }
}
