#nullable enable
using System;
using System.IO;
using System.Linq;
using StarMark.Abstractions.Ai;
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
        // 锚点搬过家（批次 WI-2i：这一栏的字面量都进了表）——改锚不改判，钉的还是"通道下拉不许手写项"。
        var aiCard = xaml[xaml.IndexOf("i18n:LocText.Key=\"settings.ai.title\"", StringComparison.Ordinal)..];
        var upToNext = aiCard.IndexOf("x:Name=\"AiStatusText\"", StringComparison.Ordinal);
        Assert.True(upToNext > 0, "AI 那一栏的结尾锚点（状态那行）没找到，闸门本身要看一眼");
        Assert.DoesNotContain("<ComboBoxItem", aiCard[..upToNext]);

        Assert.Contains("Enum.GetValues<AiProviderKind>()", Read(Page));
    }

    /// <summary>「测试连接」在忙时必须变成取消入口：<b>一个不能中断的黑盒按钮，等于没有出口</b>
    /// （本机模型冷启动时这一下可能要十几秒）。</summary>
    [Fact]
    public void ConnectionTestHasAnExitWhileItRuns()
    {
        var page = Read(Page);
        Assert.Contains("LocText.SetKey(AiProbeButton, ProbeCancelKey)", page);
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

    /// <summary>片259·用户点名（P-297 实测反馈）：额度耗尽这类<b>致命型</b>失败必须在卡顶横幅大字直说——
    /// 从前只有状态行一句 0.72 透明度的小字，"额度用完了"埋在满屏说明里，用户找不到一句"现在到底怎么了"。
    /// 钉三处：<b>横幅存在且是警示色的框</b>（不是又一行小字）、<b>致命型分型从编排层一路透传到界面</b>
    /// （片259 之前 FirstError 只有那句话、没有分型，界面想判"该不该上横幅"也无从判起——这就是根因断点）、
    /// <b>改配置就地收起</b>（横幅念的是"对当时那套配置的一次观察"，换了配置旧观察就不再描述眼前这套）。</summary>
    [Fact]
    public void FatalChannelFailuresSurfaceInTheBannerNotOnlyTheSmallStatusLine()
    {
        var xaml = Read(Xaml);
        Assert.Contains("x:Name=\"AiChannelProblemBanner\"", xaml);
        Assert.Contains("SystemFillColorCautionBackgroundBrush", xaml);   // 警示底色：横幅≠又一行小字
        Assert.Contains("SystemFillColorCautionBrush", xaml);

        var page = Read(Page);
        Assert.Contains("ShowAiChannelProblem(outcome)", page);           // 整理撞出致命型 → 横幅
        Assert.Contains("probe.Ok) HideAiChannelProblem()", page);        // 自检通了 → 收起；败了照上（同一拍那次观察）
        Assert.Contains("HideAiChannelProblem();", page);                 // 改任何一栏配置 → 旧观察作废

        // 分型链路三节：编排层带出 → 整理结果透传 → 界面才判得出"致不致命"，断一节横幅就永远升不起来
        Assert.Contains("AiFailureKind FirstFailure = AiFailureKind.Unknown",
            Read("src/StarMark.Core/Ai/ClassifyRunner.cs"));
        Assert.Contains("report.FirstFailure)", Read("src/StarMark.UI/Services/AiClassifyService.cs"));
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

        var store = SourceGate.ReadRepoPartials("src/StarMark.UI/Helpers/SettingsStore.cs");
        Assert.Contains("public void SaveAiSettings(AiSettings settings)", store);
        Assert.Contains("public AiSettings LoadAiSettings()", store);
    }

    /// <summary>"能不能用"只有一处判据（<c>AiSettings.Problem</c>）。<b>各处零散判 apiKey 非空</b>
    /// 是扩展项目两次故障的同根因，而其中一次就是漏了 Ollama 免 Key。</summary>
    [Fact]
    public void UsabilityGateIsNotDuplicated()
    {
        // 合法使用者：闸门自己、写盘时的空值归一、请求头要不要带 Bearer（"怎么发"不是"能不能用"）
        var allowed = new[] { "AiSettings.cs", "SettingsStore.cs", "AiProviders.cs" };        var offenders = new System.Collections.Generic.List<string>();
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
                // "写盘时把空串归一成 null"的合法判点是<b>设置存储这个类</b>，不是某一个文件名：
                // 批次 S4-④ 之后那段住在 SettingsStore.Ai.cs，按类放行才不会被搬家绊倒。
                if (Path.GetFileName(file).StartsWith("SettingsStore.", StringComparison.Ordinal)
                    || allowed.Contains(Path.GetFileName(file))) continue;
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
        var store = SourceGate.ReadRepoPartials("src/StarMark.UI/Helpers/SettingsStore.cs");
        var aiProps = store.Split('\n')
            .Where(line => line.TrimStart().StartsWith("public ") && line.Contains("{ get; set; }")
                           && line.Contains(" Ai"))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[2])
            .ToList();

        // 钉**名册**而不是钉个数（#181）：加一格就得在这里点名它，红的时候也能说出是哪一格动了——
        // 只数个数会把"新加了一格该记的"与"某格被顺手改名/被别的域覆盖"报成同一条红。
        // 名册＝开关/通道/模型/Key/两个地址 + 待应用方案 + 预算/熔断位/预警线 + 规则覆盖 JSON + 分类模型 + 单批超时 + 即时分类开关
        // + 整批撤销记录（P-90：AiUndoRecordJson，片240 落地时漏记名册，全量首跑红出来才补）。
        Assert.Equal(
            new[]
            {
                "AiApiKey", "AiBaseUrl", "AiBatchTimeoutSeconds", "AiBudgetPaused", "AiClassifyModel",
                "AiClassifyRulesJson", "AiEnabled", "AiInstantClassify", "AiModel", "AiOllamaBaseUrl",
                "AiPendingPlanJson", "AiProvider", "AiTokenBudget", "AiUndoRecordJson", "AiWarnRatio",
            },
            aiProps.OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(aiProps, name => Assert.StartsWith("Ai", name)); // 同名覆盖那条事故的形状
        // 各自独立＝一格一 key：重名会让两次写互相冲掉（扩展侧那次事故的原形）
        Assert.Equal(aiProps.Count, aiProps.Distinct(StringComparer.Ordinal).Count());
    }

    private const string Gateway = "src/StarMark.Integrations/Ai/AiGateway.cs";
    private const string StoreAi = "src/StarMark.UI/Helpers/SettingsStore.Ai.cs";

    /// <summary>
    /// P-192 本层普查最后一颗的证人（片120）：<c>AiGateway.Of</c> 那句「没有这个通道」是<b>防御臂</b>——
    /// 它自己⛔ 上屏，但<b>到上屏出口那一途确实存在</b>（两颗调用点都裹在 <c>catch (Exception ex)</c> 里，
    /// 而 <c>ex.Message</c> 会经 <c>common.rawValue</c> 贴到设置页那一格／提示卡）。
    /// <para>所以"⛔ 上屏"这句⛔ 是读一句 throw 就能说的，它挂在三扇门上：名册只有两颗通道（枚举两值）、
    /// 读档那扇用 <c>Enum.IsDefined</c> 挡、界面那扇的实参只可能来自由枚举生成的选择项。
    /// 钉这三颗是因为<b>它们任一失效，屏幕上就会念一句冻死在旧语言里的话</b>（这一层的原判据）。</para>
    /// </summary>
    [Fact]
    public void TheOnlyUnknownProviderArmCannotBeReachedFromRealInputs()
    {
        Assert.Equal(2, Enum.GetValues<AiProviderKind>().Length);       // 名册现读：多一颗通道就要重新问这一问
        Assert.Equal(new[] { 0, 1 }, Enum.GetValues<AiProviderKind>().Select(k => (int)k).ToArray());

        // 门①：盘上那个数字认不出来就退回默认（⛔ 抛＝设置页还要开得起来）
        var store = Read(StoreAi);
        Assert.Contains("var kind = d?.AiProvider is int raw && Enum.IsDefined(typeof(AiProviderKind), raw)", store);
        Assert.Contains(": AiProviderKind.Ollama;", store);
        Assert.Contains("d.AiProvider = (int)settings.Provider;", store);   // 写侧只写序号＝与读侧同一颗轴

        // 门②：界面交回来的通道只可能来自由枚举生成的选择项，其余一律默认
        Assert.Contains("Provider: AiProviderBox.SelectedItem is LocOption { Value: AiProviderKind kind }", Read(Page));

        // 门③：全仓没有把 <c>AiSettings</c> 直接从 JSON 反序列化（那条路会把枚举序号绕过门①）
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            Assert.DoesNotContain("Deserialize<AiSettings>", File.ReadAllText(file));
        }
    }

    /// <summary>那颗臂<b>被迫开火时</b>交的是什么（这一格把"测不到的失败模式"换成"测得到的那一半"）：
    /// 只有 <c>ArgumentOutOfRangeException</c>，句子留在异常里，⛔ 就地念成表里的词——
    /// 上屏的那一句由调用点的 <c>catch</c> 交给 <c>common.rawValue</c>，即"机器事实照原样贴"（✅P-199 口径）。</summary>
    [Fact]
    public void ForcedUnknownProviderThrowsInsteadOfSpeakingATableWord()
    {
        using var gateway = new StarMark.Integrations.Ai.AiGateway();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => gateway.Of((AiProviderKind)7));
        Assert.Equal("kind", ex.ParamName);
        Assert.Contains("没有这个通道", ex.Message);

        // 那颗串只有一个出处，且就钉在 throw 那一行（⛔ 第二处写死同一句话）
        var gw = Read(Gateway);
        Assert.Equal(1, gw.Split('\n').Count(l => l.Contains("没有这个通道")));
        Assert.Contains("_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, \"没有这个通道\"),", gw);
        // 两颗调用点＝这一层唯一能把那句带上屏的两条路（读档那两格各自的话）
        Assert.Equal(2, gw.Split('\n').Count(l => l.Contains("Of(settings.Provider)")));
    }
}
