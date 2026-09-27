#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.Clipboard;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// ClipIMG-1d（设置层）的守门。<b>测试工程不引用 StarMark.UI</b>，所以这一层能机检的只有源码形状——
/// 而恰恰是这几条最容易在"顺手改改文案"时静默破掉：默认值、警示原话、上下限的两份出处、哑键。
/// <para>每条都写清了"破了会怎样"，因为这类闸门最坏的失效不是红，是<b>安静地白过</b>。</para>
/// </summary>
public sealed class ClipboardImageSettingsGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string Vm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string Store = "src/StarMark.UI/Helpers/SettingsStore.cs";
    private const string App = "src/StarMark.UI/App.xaml.cs";
    private const string Watcher = "src/StarMark.Integrations/Clipboard/ClipboardWatcher.cs";

    /// <summary>设置页里"数据"那一页（剪贴板那张卡住这里）。</summary>
    private static string DataTab() => Between(ReadRepoFile(Xaml), "<TabViewItem Header=\"数据\">", "</TabViewItem>");

    /// <summary>Store 里剪贴板图片那一段（从分节注释到下一个分节注释）。</summary>
    private static string StoreImageRegion()
        => Between(ReadRepoFile(Store), "────────── 剪贴板图片", "────────── 护眼");

    [Fact]
    public void WarningKeepsBothHalvesOfTheCost()
    {
        // §3-Q3 裁决要求"文案明说"。两半各挡一种误解：
        //   前半——图片扫不出敏感内容（不是我们偷懒，是这条路根本没有）；
        //   后半——开启之后屏幕上出现过的东西都可能被留下（用户最想知道的那句代价）。
        var xaml = DataTab();
        Assert.Contains("图片无法做敏感检查", xaml);
        Assert.Contains("开启即包含一切屏幕内容", xaml);
    }

    [Fact]
    public void ImageCaptureDefaultsOffInEveryLayer()
    {
        // 三处都得默认关，任何一处默认开都等于把"图片默认关"这条裁决偷掉：
        // ① 采集器的字段没有初始化器（volatile bool 默认 false）；
        var watcher = ReadRepoFile(Watcher);
        Assert.Contains("public volatile bool ImageCapture;", watcher);
        Assert.DoesNotContain("ImageCapture = true", watcher.Replace("public volatile bool ImageCapture;", "", StringComparison.Ordinal));
        // ② 设置读取：缺字段按"没开"（== true 而不是 ?? true）；
        var store = ReadRepoFile(Store);
        Assert.Contains("d.ClipboardImageEnabled == true", store);
        Assert.DoesNotContain("ClipboardImageEnabled ?? true", store);
        // ③ 设置页不许自己给个 true 初值。
        Assert.DoesNotContain("ClipboardImageEnabled = true", ReadRepoFile(Vm));
    }

    [Fact]
    public void CapsShareOneRoundingRuleWithTheCollector()
    {
        // 上限的"坏值怎么办"只许有一份实现（ClipboardPolicy.Clamp*）：设置页再写一遍 Math.Clamp，
        // 将来改区间只会改一处，症状是"设置页显示 2000，实际留 500"。
        var region = StoreImageRegion();
        Assert.Equal(2, Count(region, "ClampImageMaxEntries"));      // 读 + 写
        Assert.Equal(2, Count(region, "ClampTextMaxEntries"));
        Assert.DoesNotContain("Math.Clamp", region);
        // 两个上限是同一个决定的两半：一次整档写，不留"图片已改、文本未改"的中间态。
        Assert.Contains("public void SaveClipboardMaxEntries(int imageEntries, int textEntries)", region);
    }

    [Fact]
    public void TypedNumbersLandAsOneCommitNotOnePerKeystroke()
    {
        // NumberBox 每敲一位都变更。逐键生效的坏法不是"多写几次盘"那么轻：
        // 把 200 改成 2000 的途中先经过 2，那一瞬间图片上限被夹成 10 并推给采集器，
        // 下一次复制就按 10 条轮转——用户为了调高上限反而丢掉一批历史。
        var vm = ReadRepoFile(Vm);
        var handler = MethodBody(vm, "partial void OnClipboardImageMaxValueChanged(double value)");
        Assert.Contains("QueueClipboardLimitsSave()", handler);
        Assert.DoesNotContain("_settings.Save", handler);
        Assert.Equal(1, Count(vm, "SaveClipboardMaxEntries("));
        var queue = MethodBody(vm, "private void QueueClipboardLimitsSave()");
        Assert.Contains("IsRepeating = false", queue);               // 一次性窗口，反复重启才是 debounce
        Assert.Contains("ApplyClipboardLimitsSave();", queue);       // 拿不到 DispatcherQueue 时直接落，不吞改动
    }

    [Fact]
    public void NumberBoxBoundsMatchThePolicyCeilings()
    {
        // XAML 里的 Min/Max 是写死的字符串，与 Core 的常量是两份出处——这条测就是把它们对起来。
        // （不这么做的话，改 Core 区间会让设置页继续允许输入越界值，而读侧又静默夹回去：看起来像 bug。）
        var xaml = DataTab();
        var image = BoundsOf(xaml, "ViewModel.ClipboardImageMaxValue");
        var text = BoundsOf(xaml, "ViewModel.ClipboardTextMaxValue");

        Assert.Equal(ClipboardPolicy.MinEntries, image.min);
        Assert.Equal(ClipboardPolicy.ImageMaxEntriesCeil, image.max);
        Assert.Equal(ClipboardPolicy.MinEntries, text.min);
        Assert.Equal(ClipboardPolicy.TextMaxEntriesCeil, text.max);
    }

    /// <summary>取某个绑定所在 NumberBox 的 Minimum / Maximum（绑定写在控件标签内部，故往前找它的起点）。</summary>
    private static (int min, int max) BoundsOf(string xaml, string binding)
    {
        var at = xaml.IndexOf(binding, StringComparison.Ordinal);
        Assert.True(at > 0, $"找不到绑定：{binding}");
        var open = xaml.LastIndexOf("<NumberBox", at, StringComparison.Ordinal);
        Assert.True(open >= 0, $"{binding} 前面没有 NumberBox");
        var tag = xaml[open..at];                       // 从标签起点到绑定处，Min/Max 都写在前面
        return (int.Parse(NeedAttr(tag, "Minimum"), System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(NeedAttr(tag, "Maximum"), System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string NeedAttr(string tag, string name)
    {
        var m = Regex.Match(tag, name + "=\"([^\"]+)\"");
        Assert.True(m.Success, $"NumberBox 标签里没有 {name}——区间必须由界面写死一次并由本测对起来");
        return m.Groups[1].Value;
    }

    [Fact]
    public void SingleImageByteCeilingHasNoEditor()
    {
        // 洞1：单张 20MB 是"挡事故"的内部常数，给设置页一个能把它调大的框，护栏就成了装饰。
        // 所以三处都不许出现它：界面、VM、以及"为它开的写入口"。
        Assert.DoesNotContain("MaxImageBytes", DataTab());
        Assert.DoesNotContain("MaxImageBytes", ReadRepoFile(Vm));
        Assert.DoesNotContain("SaveClipboardImageMaxBytes", ReadRepoFile(Store));
        // 但用户要能知道有这条线——它写在图片开关的说明里（"20 MB"），是陈述不是控件。
        Assert.Contains("20 MB", ReadRepoFile(Vm));
    }

    [Fact]
    public void BackupImageSwitchStaysInvisibleUntilPhaseA()
    {
        // 决议把"备份包含剪贴板图片"列成默认开的一项，但 b 期导出格式里没有附件这一维：
        // 现在放上设置页，就是一颗点了什么都不改变的哑键（用户最不能容忍的一类）。
        // key 先落盘（a 期直接读，跨版本语义不变），界面上刻意不出现。
        var store = ReadRepoFile(Store);
        Assert.Contains("public bool? BackupClipboardImagesEnabled { get; set; }", store);
        Assert.Contains("LoadBackupClipboardImagesEnabled", store);
        Assert.DoesNotContain("BackupClipboardImages", ReadRepoFile(Vm));
        Assert.DoesNotContain("BackupClipboardImages", ReadRepoFile(Xaml));
    }

    [Fact]
    public void EveryChangeReachesTheCollectorWithoutRestart()
    {
        // 三项设置在采集器上的落点一个都不能少：漏掉任何一个，症状都是"设置页改了但没生效"。
        var apply = MethodBody(ReadRepoFile(App), "public static void RefreshClipboardLimits()");
        Assert.Contains("watcher.ImageCapture = s.LoadClipboardImageEnabled()", apply);
        Assert.Contains("watcher.ImageMaxEntries = s.LoadClipboardImageMaxEntries()", apply);
        Assert.Contains("watcher.TextMaxEntries = s.LoadClipboardTextMaxEntries()", apply);

        // 总开关那条路也要顺手推一次（放在 finally）：否则"先开总开关、图片设置还留着上次的值"。
        var toggle = MethodBody(ReadRepoFile(App), "public static bool ApplyClipboardHistory(bool enabled)");
        Assert.Contains("finally", toggle);
        Assert.True(toggle.IndexOf("finally", StringComparison.Ordinal)
                    < toggle.IndexOf("RefreshClipboardLimits()", StringComparison.Ordinal));

        // 两个改动入口各自都要到得了采集器：开关是直接推，两个数字框是合并窗口之后一次整档推。
        // （数字框只留一处调用是刻意的——它们共用一个决定，见 TypedNumbersLandAsOneCommitNotOnePerKeystroke。）
        Assert.Equal(2, Count(ReadRepoFile(Vm), "App.RefreshClipboardLimits()"));
    }

    [Fact]
    public void BackfillDoesNotWriteSettingsToDisk()
    {
        // 进设置页只是"回灌显示"，不许把默认值写回用户文件：那会让一个从没碰过这里的用户
        // 在某次打开设置页之后，悄悄拥有了"他其实没选过"的设置。
        var body = MethodBody(ReadRepoFile(Vm), "public void LoadFromStore()");
        var guard = body.IndexOf("_suppressClipboardImageApply = true", StringComparison.Ordinal);
        var assign = body.IndexOf("ClipboardImageEnabled = Safe(", StringComparison.Ordinal);
        var release = body.IndexOf("_suppressClipboardImageApply = false", StringComparison.Ordinal);
        Assert.True(guard >= 0 && assign > guard && release > assign,
            "回灌没有被抑制旗标包住：打开设置页就会改用户的设置文件");

        // 占用统计不在这条链上（它只读盘），所以释放之后才调用。
        Assert.True(release >= 0);
        Assert.DoesNotContain("SaveClipboardImage", body);
        Assert.DoesNotContain("SaveClipboardText", body);
    }

    [Fact]
    public void UsageLineNamesTheFolderItMeasured()
    {
        // "存储对用户直接可见"（§3-Q6）如果只兑现成"没加密"，用户还是找不到东西在哪。
        var vm = ReadRepoFile(Vm);
        Assert.Contains("ClipAssets.Folder", vm);
        Assert.Contains("DescribeUsage", vm);
        Assert.Contains("DescribeProjection", vm);
        // 统计落在池线程：几百张图的 stat 放在 UI 线程上，就是"一打开设置页卡半秒"。
        var body = MethodBody(vm, "private void ComputeClipboardUsage()");
        Assert.Contains("Task.Run", body);
        Assert.Contains("DispatcherQueue", body);
    }
}
