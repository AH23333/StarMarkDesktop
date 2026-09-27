#nullable enable
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 A 里<b>只有界面知道</b>的那几条规则。<b>测试工程按分层规定引不到 StarMark.UI</b>，
/// 所以只能扫源码——这与截图工具条那两条闸门同一做法：
/// 规则本身没别的办法守，那就让它写歪的下一刻就红，而不是等下一次改界面的人撞上去。
/// </summary>
public sealed class AiLayerGateTests
{
    private static readonly string RepoRoot = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（没有 StarMark.sln）");
    }

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(RepoRoot, relative));

    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string Page = "src/StarMark.UI/Views/SettingsPage.Ai.cs";

    /// <summary>通道下拉的项必须由枚举生成。<b>手写 ComboBoxItem 就是第二份"有哪些通道"的事实</b>：
    /// 将来加第三个通道时，枚举改了而这里没改，界面上永远选不到它，且不会有任何报错。</summary>
    [Fact]
    public void ProviderPickerIsGeneratedNotHandWritten()
    {
        var xaml = Read(Xaml);
        var aiCard = xaml[xaml.IndexOf("AI 助手", StringComparison.Ordinal)..];
        var upToNext = aiCard.IndexOf("还没测过", StringComparison.Ordinal);
        Assert.True(upToNext > 0, "AI 那一栏的结尾锚点没找到，闸门本身要看一眼");
        Assert.DoesNotContain("<ComboBoxItem", aiCard[..upToNext]);

        Assert.Contains("Enum.GetValues<AiProviderKind>()", Read(Page));
    }

    /// <summary>「测试连接」在忙时必须变成取消入口：<b>一个不能中断的黑盒按钮，等于没有出口</b>
    /// （本机模型冷启动时这一下可能要十几秒）。</summary>
    [Fact]
    public void ConnectionTestHasAnExitWhileItRuns()
    {
        var page = Read(Page);
        Assert.Contains("AiProbeButton.Content = \"取消\"", page);
        Assert.Contains("_aiProbeCts?.Cancel()", page);
    }

    /// <summary>这一栏的每一项改动都要就地落盘——<b>不能有"改了但要点保存才生效"的中间态</b>。
    /// 三格（开关 / 下拉 / 文本）各自的处理器都必须直接拐到同一个持久化出口。</summary>
    /// <summary>
    /// 「停止」必须是单独一颗、只在忙的时候现身的按钮，而"开始"那一段忙碌期间要退场。
    /// <para>把"开始"就地改成"暂停"（一次点击两件事）在扩展项目里返工过好几次：用户在忙的那一刻
    /// 不确定自己按下的到底是哪一个，而且没有第二次出口。这里钉的是形状：
    /// 有 <c>AiStopButton</c>、它接自己的处理器、忙时"开始"被禁用而不是改字。</para>
    /// </summary>
    [Fact]
    public void OrganisingHasItsOwnStopButton()
    {
        var xaml = Read(Xaml);
        Assert.Contains("x:Name=\"AiStopButton\"", xaml);
        Assert.Contains("Click=\"AiStop_Click\"", xaml);

        var page = Read(Page);
        Assert.Contains("AiStopButton.IsEnabled = false", page);              // 正在停下时不许再按一次
        Assert.Contains("AiOrganiseButton.IsEnabled = false", page);
        Assert.Contains("AiStopButton.Visibility = Visibility.Visible", page);
        Assert.Contains("AiStopButton.Visibility = Visibility.Collapsed", page);
        Assert.DoesNotContain("AiOrganiseButton.Content = ", page);           // 改字＝一键两义回来了

        // 一轮整理只有一个写存档的人：界面上再开一处 OrganiseAsync 就会出现两份"当前方案"
        Assert.Equal(1, page.Split('\n').Count(line => line.Contains(".OrganiseAsync(")));
    }

    /// <summary>重跑一轮不许把"上次整理好但还没应用"的方案整档盖掉——存档是整档写的，
    /// 没有"追加"这回事 ⇒ 这一轮必须<b>从旧方案接着写</b>（同一条以新的为准）。
    /// 用户上一次花掉的那次整理凭空消失，是这里最贵的一类错。</summary>
    [Fact]
    public void ReRunningSeedsFromTheUnappliedPlan()
    {
        var service = Read("src/StarMark.UI/Services/AiClassifyService.cs");
        Assert.Contains("LoadPending().Proposals.ToList()", service);
        Assert.Contains("public ClassifyPlan LoadPending()", service);        // 种子与读档必须走同一个口子
    }

    [Fact]
    public void EveryAiFieldPersistsImmediately()
    {
        var page = Read(Page);
        var lines = page.Split('\n');
        foreach (var handler in new[] { "AiEnabled_Toggled", "AiProvider_SelectionChanged", "AiField_TextChanged" })
        {
            // 中间只要多一步判断或改回"按保存按钮才写"，就会出现某一格改了却没落盘
            var line = lines.Single(l => l.Contains("void " + handler));
            Assert.EndsWith("=> PersistAiAndShow();", line.TrimEnd());
        }

        // 写盘只有一个出口：出现第二处 SaveAiSettings 调用就等于有了两套"什么算改完"
        Assert.Equal(1, lines.Count(l => l.Contains(".SaveAiSettings(")));

        var store = Read("src/StarMark.UI/Helpers/SettingsStore.cs");
        Assert.Contains("public void SaveAiSettings(AiSettings settings)", store);
        Assert.Contains("public AiSettings LoadAiSettings()", store);
    }

    /// <summary>"能不能用"只有一处判据（<c>AiSettings.Problem</c>）。<b>各处零散判 apiKey 非空</b>
    /// 是扩展项目两次故障的同根因，而其中一次就是漏了 Ollama 免 Key。</summary>
    [Fact]
    public void UsabilityGateIsNotDuplicated()
    {
        // 合法使用者：闸门自己、写盘时的空值归一、请求头要不要带 Bearer（"怎么发"不是"能不能用"）
        var allowed = new[] { "AiSettings.cs", "SettingsStore.cs", "AiProviders.cs" };
        var offenders = new System.Collections.Generic.List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            var text = File.ReadAllText(file);
            // 除了 AiSettings.Problem 自己，谁都不许拿 ApiKey 空不空来当"能不能用"的判据
            foreach (var line in text.Split('\n'))
            {
                if (!line.Contains("ApiKey")) continue;
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///")) continue;
                if (allowed.Contains(Path.GetFileName(file))) continue;
                if (trimmed.Contains("IsNullOrWhiteSpace") || trimmed.Contains("IsNullOrEmpty"))
                    offenders.Add(Path.GetFileName(file) + " → " + trimmed);
            }
        }

        Assert.True(offenders.Count == 0, string.Join("；", offenders));
    }

    /// <summary>AI 那一组的六个设置项必须全部带 <c>Ai</c> 前缀。<b>扩展项目出过一次四个 key 同名互相覆盖</b>
    /// 的事故（第一批分类结果落盘就把 AI 设置冲掉），而代码里完全看不出来。</summary>
    /// <summary>
    /// "还没应用的方案"只能经服务那一道出口读（<c>AiClassifyService.LoadPending</c>）。
    /// <para>批次 QA-1 把"整个方案里只挂一条的标签"砍掉，那一刀住在服务里：界面上谁再直接
    /// <c>SettingsStore.LoadAiPlan()</c>，旧存档里几百个一条一词的标签就会绕过规则原样摆回预览——
    /// 而这正是用户这次点名要消灭的东西。</para>
    /// </summary>
    [Fact]
    public void PendingPlanIsReadThroughTheOnePruningDoor()
    {
        var page = Read(Page);
        Assert.Contains("AiClassifyService>().LoadPending()", page);   // 锚点必须扫到：不然这条闸门是在空转
        Assert.DoesNotContain(".LoadAiPlan()", page);
        Assert.DoesNotContain(".SaveAiPlan(", page);
        Assert.DoesNotContain("WithoutSingletonTags", page);           // 那一刀不许在界面上再砍一次（两处口径迟早分岔）

        var service = Read("src/StarMark.UI/Services/AiClassifyService.cs");
        Assert.Equal(2, service.Split('\n').Count(l => l.Contains("WithoutSingletonTags")));  // 出轮 + 读档，各一次
    }

    [Fact]
    public void AiStorageKeysAreNamespaced()
    {
        var store = Read("src/StarMark.UI/Helpers/SettingsStore.cs");
        var aiProps = store.Split('\n')
            .Where(line => line.TrimStart().StartsWith("public ") && line.Contains("{ get; set; }")
                           && line.Contains(" Ai"))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[2])
            .ToList();

        Assert.Equal(11, aiProps.Count);          // 开关/通道/模型/Key/两个地址 + 待应用方案 + 预算/熔断位 + 规则覆盖 JSON + 分类模型
        Assert.All(aiProps, name => Assert.StartsWith("Ai", name)); // 同名覆盖那条事故的形状
    }
}
