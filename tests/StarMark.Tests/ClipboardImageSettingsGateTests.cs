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
    private const string StoreClipboard = "src/StarMark.UI/Helpers/SettingsStore.Clipboard.cs";
    private const string App = "src/StarMark.UI/App.xaml.cs";
    private const string Watcher = "src/StarMark.Integrations/Clipboard/ClipboardWatcher.cs";

    /// <summary>设置页里"数据"那一页（剪贴板那张卡住这里）。</summary>
    private static string DataTab() => Between(ReadRepoFile(Xaml), "<TabViewItem Header=\"数据\">", "</TabViewItem>");

    /// <summary>
    /// Store 里剪贴板图片那一段。批次 S4-④ 之后<b>这一段自己就是一枚 partial 文件</b>，
    /// 所以这里读那一份而不是读整套：这条判据数的是"夹只在 Core 做一次"的<b>段内</b>计数
    /// （读整套会把别的段一起数进来），也要求"段内不许出现 Math.Clamp"（读整套会被别处的 Clamp 误伤）。
    /// </summary>
    private static string StoreImageRegion() => ReadRepoFile(StoreClipboard);

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
        var store = ReadRepoPartials(Store);
        Assert.Contains("d.ClipboardImageEnabled == true", store);
        Assert.DoesNotContain("ClipboardImageEnabled ?? true", store);
        // ③ 设置页不许自己给个 true 初值。
        Assert.DoesNotContain("ClipboardImageEnabled = true", ReadRepoPartials(Vm));
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
        var vm = ReadRepoPartials(Vm);
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
        Assert.DoesNotContain("MaxImageBytes", ReadRepoPartials(Vm));
        Assert.DoesNotContain("SaveClipboardImageMaxBytes", ReadRepoPartials(Store));
        // 但用户要能知道有这条线——它写在图片开关的说明里（"20 MB"），是陈述不是控件。
        Assert.Contains("20 MB", ReadRepoPartials(Vm));
    }

    [Fact]
    public void BackupImageSwitchIsWiredAndSitsRightAboveTheExportButton()
    {
        // 1d 时这颗开关刻意不上界面：那时导出格式里还没有附件这一维，放上就是一颗点了什么都不改变的哑键。
        // a 期（批次 3b）把附件打包做出来了，闸门随之改判：**现在它必须出现，而且必须接线接全**。
        // 四种"看着在、其实没用"的失效各钉一条：
        var store = ReadRepoPartials(Store);
        // ① 有 key、有读、有写——只落 key 不写 = 界面上拨完就忘；
        Assert.Contains("public bool? BackupClipboardImagesEnabled { get; set; }", store);
        Assert.Contains("public void SaveBackupClipboardImagesEnabled(bool enabled)", store);
        // ② 默认开（决议 §4）：写成 == true 就成了"默认关"，而文案里没有一句会说这件事；
        Assert.Contains("BackupClipboardImagesEnabled ?? true", store);
        Assert.DoesNotContain("BackupClipboardImagesEnabled == true", store);

        var vm = ReadRepoPartials(Vm);
        // ③ 回灌初值时不许写盘（否则每次进设置页都把用户的值按默认覆盖一次），且初值取自 store 而不是硬编 true；
        var apply = MethodBody(vm, "partial void OnBackupClipboardImagesEnabledChanged(bool value)");
        Assert.Contains("_settings.SaveBackupClipboardImagesEnabled(value)", apply);
        Assert.Contains("if (_suppressClipboardImageApply) return;", apply);
        Assert.Contains("BackupClipboardImagesEnabled = Safe(_settings.LoadBackupClipboardImagesEnabled, true,", vm);
        Assert.DoesNotContain("BackupClipboardImagesEnabled = true", vm);

        // ④ 摆位：这颗开关管的是"下一次导出会长成什么文件"，所以必须在<b>导出按钮之前</b>。
        //    放进剪贴板那张卡（同页、离按钮很远）等于让用户点导出时看不见自己刚决定了什么。
        var card = Between(DataTab(), "<TextBlock Text=\"数据备份与恢复\"", "Click=\"ExportBackup_Click\"");
        Assert.Contains("ViewModel.BackupClipboardImagesEnabled, Mode=TwoWay", card);
        // 关掉它的代价（图片条目要靠本机还留着文件才打得开）写在卡上，而不是只写在 tooltip 里。
        Assert.Contains("关掉则只带条目与文件名", card);
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
        Assert.Equal(2, Count(ReadRepoPartials(Vm), "App.RefreshClipboardLimits()"));
    }

    [Fact]
    public void BackfillDoesNotWriteSettingsToDisk()
    {
        // 进设置页只是"回灌显示"，不许把默认值写回用户文件：那会让一个从没碰过这里的用户
        // 在某次打开设置页之后，悄悄拥有了"他其实没选过"的设置。
        var body = MethodBody(ReadRepoPartials(Vm), "public void LoadFromStore()");
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
        var vm = ReadRepoPartials(Vm);
        Assert.Contains("ClipAssets.Folder", vm);
        Assert.Contains("DescribeUsage", vm);
        Assert.Contains("DescribeProjection", vm);
        // 统计落在池线程：几百张图的 stat 放在 UI 线程上，就是"一打开设置页卡半秒"。
        var body = MethodBody(vm, "private void ComputeClipboardUsage()");
        Assert.Contains("Task.Run", body);
        Assert.Contains("DispatcherQueue", body);
    }

    /// <summary>
    /// <b>给"守门本身"作的保</b>（承 <c>CaptureOverlayGateTests</c> 同名那条）：批次 S4-④ 把设置页 VM
    /// 按访问面拆成 <c>SettingsPageViewModel.*.cs</c> 若干份，读法一旦退回单个文件，
    /// 那些"锚点必须命中 1 处"与"某个字面不许出现"的守门会<b>静默扫不到</b>而红或直接空转。
    /// 这里钉住每个分段各一个代表方法在"读整个类"时仍看得见。
    /// </summary>
    [Fact]
    public void TheViewModelGatesReadEveryPartialFile()
    {
        var all = ReadRepoPartials(Vm);

        Assert.Contains("public void LoadFromStore()", all);                          // 主文件
        Assert.Contains("public async Task LoadHealthAsync()", all);                   // Health
        Assert.Contains("private void ComputeClipboardUsage()", all);                  // Clipboard
        Assert.Contains("private string BuildEyeRestStatus()", all);                   // EyeRest
        Assert.Contains("public void RefreshRssEnabled()", all);                       // Feeds
        Assert.Contains("private IEnumerable<StarMark.UI.Views.CanvasHotkeyRow> BuildCanvasHotkeyRows()", all);   // Canvas
        Assert.Contains("private string BuildCaptureStatus()", all);                        // Capture
        Assert.Contains("private void PrepareLocalDiskSearch()", all);                 // LocalDisk
    }
}
