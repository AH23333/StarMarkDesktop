#nullable enable
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 快捷启动"一次加一批"的守门（批次 PA-6 起，S4 起扩到两条出口）。
/// <para>
/// 被测的几处都在 <c>StarMark.UI</c>，测试工程刻意不引用它（分层红线），所以这里<b>读源文件</b>比对结构。
/// 为什么值得这么守：<b>逐条 await 的形状一旦回来，功能一点都不会坏</b>——拖 20 个文件照样成功，
/// 只是每次多 20 趟整档读写；只有秒表能看见，而这里没有可信的秒表。
/// 另一条同样重要：<b>落库的配对只许有一处</b>（<c>AddPathsToLauncherAsync</c>），
/// 拖放与系统选择器两条出口都走它——两处各配一遍的话，"登记了但没加进组件"这种半套状态只会在一处出现。
/// </para>
/// <para>每条守门都带"锚点必须扫到"的反空转断言：扫不到结构就抛，免得一条永不执行的检查冒充绿灯。</para>
/// </summary>
public sealed class QuickLaunchDropBatchGateTests
{
    private const string ManagerRelativePath = "src/StarMark.UI/Services/WidgetManager.cs";
    private const string WindowRelativePath = "src/StarMark.UI/Views/WidgetWindow.xaml.cs";
    private const string WidgetXamlRelativePath = "src/StarMark.UI/Views/QuickLaunchWidget.xaml";
    private const string WidgetCodeRelativePath = "src/StarMark.UI/Views/QuickLaunchWidget.xaml.cs";

    // 结构比对工具（仓库根 / 挖方法体 / 数出现次数）收在 SourceGate，两个守门文件共用一份。
    private static string ReadRepoFile(string path) => SourceGate.ReadRepoFile(path);

    // WidgetWindow 已按访问面拆成多个 partial（S4-④）：钉在它里面的方法必须读"整个类"，
    // 否则方法搬到哪个分段文件，这条守门就扫不到锚点而红。
    private static string ReadRepoPartials(string path) => SourceGate.ReadRepoPartials(path);

    private static string MethodBody(string source, string fragment) => SourceGate.MethodBody(source, fragment);

    [Fact]
    public void AWholeBatchReadsAndWritesTheStoreExactlyOnce()
    {
        var body = MethodBody(ReadRepoFile(ManagerRelativePath), "public async Task<int> AddLinksAsync(");

        Assert.Contains("inst.Links.Add(new LinkItem", body);     // 反空转：确实扫到了写入口的那一段
        Assert.Equal(1, Count(body, "_storage.Load()"));
        Assert.Equal(1, Count(body, "_storage.Save("));
        Assert.Equal(1, Count(body, "LinksChanged?.Invoke("));
        Assert.Contains("LogActivitiesAsync(", body);

        // 去重循环里只许攒列表：在这里 await 一次就是整档读写一遍
        var loop = Between(body, "foreach (var (title, uri) in links)", "if (fresh.Count == 0)");
        Assert.Contains("taken.Add(target)", loop);
        Assert.Equal(0, Count(loop, "await "));
    }

    /// <summary>单个添加入口不许再留第二份判据：<b>它只是长度为 1 的批量调用</b>。
    /// 两处各写一遍去重的话，改一处就留下一条"看起来还在工作"的旧口径。</summary>
    [Fact]
    public void TheSingleLinkExitJustDelegatesToTheBatch()
    {
        var manager = ReadRepoFile(ManagerRelativePath);
        var body = MethodBody(manager, "public async Task<bool> AddLinkAsync(");

        Assert.Contains("=> await AddLinksAsync(", body);
        Assert.Equal(0, Count(body, "_storage."));
        Assert.Equal(1, Count(manager, "inst.Links.Add(new LinkItem"));
    }

    /// <summary>拖放处理器：整批交给那<b>唯一一份</b>落库出口；收集循环里不再逐项打库，也不许自己配对两个批量出口。</summary>
    [Fact]
    public void TheDropHandlerHandsTheWholeBatchToTheSingleBulkExit()
    {
        var body = MethodBody(ReadRepoPartials(WindowRelativePath), "private async void QuickLaunch_Drop(");
        var loop = Between(body, "foreach (var item in items)", "await _manager.AddPathsToLauncherAsync");

        Assert.Contains("paths.Add((item.Name, item.Path));", loop);   // 反空转：确实扫到收集语句
        Assert.Equal(0, Count(loop, "await "));
        Assert.Contains("_manager.AddPathsToLauncherAsync(_instanceId, paths)", body);
        // 配对只许发生在 WidgetManager 那一处：拖放侧再自己调一遍两个批量出口，
        // 就成了"两处各写一份落库"，半套状态（登记了但没加进组件）迟早只在一处出现。
        Assert.Equal(0, Count(body, "RecordPathsToLibraryAsync("));
        Assert.Equal(0, Count(body, "AddLinksAsync("));
    }

    /// <summary>
    /// 「选择文件／选择文件夹」这条出口必须存在、必须绑到本窗、必须与拖放共用同一份落库形状。
    /// <para>它是那条"用户自己以管理员身份运行"场景下的唯一可用路（那种会话里 Windows 的 UIPI 会<b>整条拦下</b>
    /// 从资源管理器拖进来的消息流——不报错、不提示，症状就是"拖了没反应"）。系统选择器跑在本进程的对话框里，
    /// 与权限等级无关，于是"加本地文件"永远有一条点得到的路（而不是把用户支使去改权限——那按既定口径算缺陷）。
    /// 注：程序自身已不再为了连 Everything 而提权（P-108 改判，见 <see cref="NothingElevatesTheProcessToTalkToTheSearchEngine"/>），
    /// 所以这条路从"每天都被迫用"退回到"少数人选了管理员运行才有用"——但它不能删：删了就等于没留出口。</para>
    /// </summary>
    [Fact]
    public void ThePickerExitExistsAndSharesTheSingleBulkExit()
    {
        var xaml = ReadRepoFile(WidgetXamlRelativePath);
        Assert.Contains("Click=\"PickFiles_Click\"", xaml);
        Assert.Contains("Click=\"PickFolder_Click\"", xaml);

        var code = ReadRepoFile(WidgetCodeRelativePath);
        var files = MethodBody(code, "private async void PickFiles_Click(");
        var folder = MethodBody(code, "private async void PickFolder_Click(");

        // 选择器不绑窗口就开不出来（WinUI 3 的硬性要求），两颗都要绑
        foreach (var picked in new[] { files, folder })
        {
            Assert.Contains("InitializeWithWindow.Initialize(picker, WindowInterop.GetHwnd(_host))", picked);
            Assert.Contains("FileTypeFilter.Add(\"*\")", picked);   // 限死扩展名＝挑不到，等于这个功能没做
            Assert.Contains("AddPickedAsync(", picked);             // 两条都汇到那一个出口
        }
        Assert.Contains("PickMultipleFilesAsync(", files);          // 一次挑多个：逐个挑是把负担推给用户
        Assert.Contains("PickSingleFolderAsync(", folder);

        var shared = MethodBody(code, "private async Task AddPickedAsync(");
        Assert.Contains("_manager.AddPathsToLauncherAsync(_instanceId,", shared);
        Assert.Equal(0, Count(shared, "RecordPathsToLibraryAsync("));   // 同上：配对只许在 Manager 那一处
        Assert.Equal(0, Count(shared, "AddLinksAsync("));
    }

    /// <summary>被取代的"逐条登记"出口要删净：留着就等于给后来人留一条能走回旧形状的路。</summary>
    [Fact]
    public void ThePerItemRecordExitIsGoneFromEveryCaller()
    {
        var src = Path.Combine(SourceGate.RepoRoot(), "src");
        var hits = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(file => File.ReadAllLines(file))
            .Where(line => line.Contains("RecordPathToLibraryAsync("))          // 单数那一个
            .Where(line => !line.Contains("RecordPathsToLibraryAsync("))        // 复数的是新出口
            .ToList();

        Assert.Empty(hits);
        Assert.Contains("RecordPathsToLibraryAsync(", File.ReadAllText(
            Path.Combine(src, "StarMark.UI", "Services", "WidgetManager.cs"))); // 反空转：新出口还在
    }

    /// <summary>
    /// 用户自己"以管理员身份运行"时拖放被 Windows（UIPI）静默拦掉 ⇒ 组件必须<b>把原因说实话</b>。
    /// <para>留一句"拖不动时…"会让人怀疑自己的手势；这句是那条"任何『请重启/降权限/确认外部程序』都算缺陷"
    /// 口径的正面写法：不改系统、不支使人，只把手边那两颗永远可用的入口指给他。</para>
    /// </summary>
    [Fact]
    public void TheElevatedSessionSaysWhyDragIsBlocked()
    {
        var code = ReadRepoFile(WidgetCodeRelativePath);
        Assert.Contains("if (Privilege.IsElevated())", code);
        Assert.Contains("以管理员身份运行", code);
        Assert.Contains("Hint.Text =", code);
        var xaml = ReadRepoFile(WidgetXamlRelativePath);
        Assert.Contains("x:Name=\"Hint\"", xaml);
        Assert.Contains("选择文件／选择文件夹", xaml);              // 常态那句仍然指向两条可用的路
    }

    /// <summary>
    /// 全仓只许剩<b>一处</b>提权重启，而且它是为了收掉"权限比我们高的残留进程"，不是为了连上搜索引擎。
    /// <para>自我提权曾长在两处（App 启动期 + 设置页开关回调），理由都写作"要与提权运行的 Everything 同权限"。
    /// 2026-09-29 实测把前提推翻了：我们自带并亲手拉起的 Everything 跑在普通 IL（<c>S-1-16-8192</c>），
    /// 同 IL 的 WM_COPYDATA 本来就不被 UIPI 拦——提权对"能不能搜到"零增益，代价却是
    /// <b>资源管理器拖进／拖出双向失灵</b>、系统文件对话框调不起来、每次启动弹一次 UAC。
    /// 这两处一旦被"好心"补回来，本机看不出来（普通权限一切正常），只在真实用户机器上静默废掉一个手势，
    /// 而且症状与"组件坏了"完全一致——正是本次要结案的那条。</para>
    /// </summary>
    [Fact]
    public void NothingElevatesTheProcessToTalkToTheSearchEngine()
    {
        var app = ReadRepoFile("src/StarMark.UI/App.xaml.cs");
        var vm = ReadRepoPartials("src/StarMark.UI/ViewModels/SettingsPageViewModel.cs");

        // 反空转：收残留那一条还在（别把它当"自我提权已删净"一起删掉——它是唯一合法的提权出口）
        Assert.Contains("Privilege.TryRelaunchSelfElevated($\"{ResolveGhostArg} {pid}\")", app);
        Assert.Equal(1, Count(app, "TryRelaunchSelfElevated"));
        Assert.Equal(0, Count(vm, "TryRelaunchSelfElevated"));

        // 防退化：那个只为"防死循环"造出来的参数名不许再出现（它一出现就说明重启又回来了）
        Assert.DoesNotContain("--elevate-retry", app);
        Assert.DoesNotContain("--elevate-retry", vm);
        Assert.Contains("按普通权限运行（不自我提权）", app);

        // 「重试」只剩一个含义：就地重跑准备流程，不再按会话权限分叉措辞（那一版会说"重试提权重启"，
        // 而重启这件事已经没有任何代码会去做了）。判据只钉定义那一行——本文件上方的注释里出现过那句旧文案，
        // 拿整文件 DoesNotContain 去比会被自己的注释假失败（同一条坑在批次 WA 记过）。
        var label = Between(vm, "public string LocalDiskSearchRetryLabel", "\n");
        Assert.Contains("=> \"重试准备 Everything\";", label);
        Assert.DoesNotContain("IsElevated", label);
    }

    /// <summary>
    /// 「拖了没反应」必须在日志里分成两种可区分的事实，而不是两边都留零行。
    /// <para>有 DragEnter 那一行＝消息真进来了，问题只可能在后面的落库；一行都没有＝手势根本没到本进程。
    /// 上一轮就是因为这两面看起来一样，把"我们自己提权造成的"读成了"组件没坏、是系统拦的"，
    /// 结案结错方向。日志只写格式种类与条数，不写用户的路径与文件名。</para>
    /// </summary>
    [Fact]
    public void TheDragPathLeavesEvidenceEitherWay()
    {
        var win = ReadRepoPartials(WindowRelativePath);

        // 挂上来的必须是那个会留证的方法，不是一句什么都不记的 lambda
        Assert.Contains("RootBorder.DragEnter += QuickLaunch_DragEnter;", win);
        var enter = MethodBody(win, "private void QuickLaunch_DragEnter(");
        Assert.Contains("StarLog.Info($\"[拖放] 快捷启动收到 DragEnter", enter);
        Assert.Contains("if (_dropEnterLogged) return;", enter);      // 一次拖拽只留一行：要事实，不要流量

        var drop = MethodBody(win, "private async void QuickLaunch_Drop(");
        Assert.Contains("净新增入口 {added} 条", drop);               // 落库成功但"净 0 条"也要说得出
        Assert.Contains("都没有可用路径，未落库", drop);               // 收到 Drop 却拿不到路径＝另一种失败，也得留痕

        // 选择器那条出口与拖放共用落库形状，也就共用同一份取证口径
        var picked = MethodBody(ReadRepoFile(WidgetCodeRelativePath), "private async Task AddPickedAsync(");
        Assert.Contains("净新增入口 {added} 条", picked);
    }

    private static int Count(string text, string needle) => SourceGate.Count(text, needle);

    private static string Between(string text, string from, string to) => SourceGate.Between(text, from, to);
}