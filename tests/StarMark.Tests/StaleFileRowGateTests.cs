#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「库里那一行 ↔ 盘上那个东西」这条链要说真话（批次 VQ，用户真机反馈）。
/// <para>
/// 现场四条并成一个缺陷族：文件夹树里留着早已删掉的本机文件路径，
/// ① 点了毫无反应 ② 没人告诉他"这行已经指不到东西" ③ 也不许他自己删掉 ④ 系统不清理。
/// 读码结果比"缺功能"更具体：<b>那句话说得出来，只是被丢在 <c>await</c> 的返回值里</b>——
/// <c>LauncherEx.TryOpenAsync</c> 一直会返回"本机上的这个路径已经不在了"，而
/// <c>ItemCardActions.Open</c> 写的是 <c>await LauncherEx.OpenAsync(...)</c>，返回值没人接（坑表 #221 那一族）。
/// </para>
/// <para>
/// 于是这一族钉四件事：<b>坏消息必须被接住</b>、<b>删得掉的判据只有一份</b>、
/// <b>「索引进库」那台生产者不许被接回来</b>（它是"删了又冒出来"与"非法/提权目录被批量入库"的共同根源），
/// 以及<b>空库不再被灌进假条目</b>。前三条都是"改坏了会响"，第四条是"别再往回走"。
/// </para>
/// </summary>
public sealed class StaleFileRowGateTests
{
    private const string Actions = "src/StarMark.UI/Helpers/ItemCardActions.cs";
    private const string Launcher = "src/StarMark.UI/Helpers/LauncherEx.cs";
    private const string CardMenu = "src/StarMark.UI/Helpers/ItemContextMenu.cs";
    private const string CardXaml = "src/StarMark.UI/Controls/ItemCard.xaml";
    private const string CardCode = "src/StarMark.UI/Controls/ItemCard.xaml.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";
    private const string Policy = "src/StarMark.Abstractions/ItemCardPolicy.cs";
    private const string Repo = "src/StarMark.Data/ItemRepository.Local.cs";
    private const string Everything = "src/StarMark.Integrations/Everything/EverythingQueryQueue.cs";
    private const string AppCode = "src/StarMark.UI/App.xaml.cs";
    private const string SettingsXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string LocalDiskVm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.LocalDisk.cs";

    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>全仓（<c>src/</c> 下每一颗手写 .cs）抹掉注释后合成的一份文本——"某句话只许有一个出处"那类普查用它。</summary>
    private static string AllSrcCode()
        => string.Join("\n", ReadRepoUnder("src").Select(f => Code(f.Text)));

    // ────────── ① 坏消息必须被接住（② 症状里"没有告知"那一半）──────────

    /// <summary>
    /// 「打开」那两条入口（按 Id / 按视图模型）都必须把 <c>LauncherEx</c> 的回报<b>递出去</b>。
    /// <para>钉法：<see cref="ItemCardActions.Open(XamlRoot, long)"/> 与 <c>Open(XamlRoot, ItemCardViewModel)</c>
    /// 两个方法体里，<b>每一处</b> <c>TryOpen*</c> 调用都必须被赋值或用掉，而不是 <c>_ = ...</c>／裸 await。
    /// 反自证在下面那格：把回报改回扔掉，这一格必须红。</para>
    /// </summary>
    [Fact]
    public void BothOpenEntriesReportWhatTheyWereTold()
    {
        foreach (var signature in new[]
                 {
                     "public static async void Open(XamlRoot xamlRoot, long itemId)",
                     "public static async void Open(XamlRoot xamlRoot, ItemCardViewModel? vm)",
                 })
        {
            var body = MethodBody(Code(ReadRepoFile(Actions)), signature);
            Assert.Contains("TryOpenDetailedAsync", body, StringComparison.Ordinal);
            // 裸 await 或 `_ =` 都等于把话扔了——那正是"点了没反应"的形状
            Assert.DoesNotContain("_ = LauncherEx", body, StringComparison.Ordinal);
            Assert.DoesNotContain("await LauncherEx.OpenAsync", body, StringComparison.Ordinal);
        }
        // 组件行右键那条入口也必须汇进同一个收口处（两处各写一遍，坏消息就会只从一边冒出来）
        Assert.Contains("ItemCardActions.Open(root, vm)", Code(ReadRepoFile(CardMenu)), StringComparison.Ordinal);
        Assert.DoesNotContain("LauncherEx.OpenAsync", Code(ReadRepoFile(CardMenu)), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>只有"盘上确实没有这东西"那一类</b>才递出删行按钮。
    /// <para>其余四类（没地址／协议被挡／认不出地址／系统里没有能开它的程序）给同一颗按钮是错的：
    /// 那条不是坏数据，用户要的下一步是"装个浏览器/换个关联程序"，把行删掉反而把他想开的东西弄丢。</para>
    /// </summary>
    [Fact]
    public void OnlyTheMissingOnDiskArmOffersToDeleteTheRow()
    {
        var body = MethodBody(Code(ReadRepoFile(Actions)),
            "private static void ReportOpenFailure(ItemCardViewModel vm, (OpenFailure Kind, string? Message) outcome)");
        Assert.Equal(1, Count(body, "ShowErrorWithAction"));
        Assert.Contains("OpenFailure.MissingOnDisk", body, StringComparison.Ordinal);
        Assert.Contains("vm.Id <= 0", body, StringComparison.Ordinal);   // 没入库的行库里没有可删的东西
        // 那句话本身不许在这里另写一份（同一件事只许一个出处，批次 SI 立的口径）
        Assert.DoesNotContain("已经不在了", body, StringComparison.Ordinal);
    }

    /// <summary>话与种类都出自 <c>LauncherEx</c> 同一处代码，且那句话归 Abstractions 那颗唯一出处。</summary>
    [Fact]
    public void TheMissingRowSentenceHasExactlyOneOwner()
    {
        Assert.Contains("ItemCardPolicy.MissingRowMessage", Code(ReadRepoFile(Launcher)), StringComparison.Ordinal);
        Assert.Contains("public static string MissingRowMessage(string uri)", Code(ReadRepoFile(Policy)), StringComparison.Ordinal);
        // 那句话的"事实"半截仍归 ClipboardPolicy 那颗唯一出处；宿主侧不许另写一份由 G_ARM 那格钉
        Assert.Contains("MissingFileClause", Code(ReadRepoFile(Policy)), StringComparison.Ordinal);
    }

    // ────────── ② 删得掉的判据只有一份 ──────────

    /// <summary>
    /// 文件行「删除这一行」的<b>可见性判据</b>必须住在 Abstractions，且两个菜单入口都只<b>引用</b>它。
    /// <para>入口各判一次，就会出现"主窗卡片能删、组件行删不掉"（与闹钟动作表同一族，坑表 #189）。</para>
    /// </summary>
    [Fact]
    public void TheDeletableJudgementLivesOnceAndBothEntriesAskForIt()
    {
        var policy = Code(ReadRepoFile(Policy));
        Assert.Contains("public static bool CanDeletePermanently(string? source, ItemType type, bool isLauncherMode, bool isBuiltinClipboard)", policy, StringComparison.Ordinal);
        Assert.Contains("public static bool IsLocalFileRow(string? source, ItemType type)", policy, StringComparison.Ordinal);
        // 剪贴板那一族仍归 ClipboardPolicy 判：删除判据自己的方法体里不许出现 ItemType.Clipboard
        // （整档扫会连"📋 剪贴板"那颗图标映射一起冤枉进去——那是另一件事的出处）
        Assert.DoesNotContain("ItemType.Clipboard",
            MethodBody(policy, "public static bool CanDeletePermanently"), StringComparison.Ordinal);

        // VM 只回答"这行是什么"，不自己判能不能删
        Assert.Contains("ItemCardPolicy.CanDeletePermanently", Code(ReadRepoFile(ViewModel)), StringComparison.Ordinal);
        Assert.DoesNotContain("=> !IsLauncherMode && ClipboardPolicy.IsBuiltinEntry", Code(ReadRepoFile(ViewModel)), StringComparison.Ordinal);

        // 两个入口都读同一个属性、都用同一句文案属性
        Assert.Contains("ViewModel.CanDeletePermanently", ReadRepoFile(CardXaml), StringComparison.Ordinal);
        Assert.Contains("ViewModel.DeleteMenuText", ReadRepoFile(CardXaml), StringComparison.Ordinal);
        Assert.Contains("vm.CanDeletePermanently", Code(ReadRepoFile(CardMenu)), StringComparison.Ordinal);
        Assert.Contains("vm.DeleteMenuText", Code(ReadRepoFile(CardMenu)), StringComparison.Ordinal);
    }

    /// <summary>
    /// 一颗菜单项、<b>两种落点</b>：剪贴板行删行并顺带删我们目录里的附件；本机文件行<b>只删行、绝不碰磁盘</b>。
    /// 两个入口都得有这个分支——少一处，就是"从组件里删会把用户的文件删掉"。
    /// </summary>
    [Fact]
    public void DeletingAFileRowNeverTouchesTheDiskAndBothEntriesRouteItRight()
    {
        Assert.Contains("ItemCardActions.DeleteFileRow", Code(ReadRepoFile(CardCode)), StringComparison.Ordinal);
        Assert.Contains("ItemCardActions.DeleteFileRow(vm.Id)", Code(ReadRepoFile(CardMenu)), StringComparison.Ordinal);
        var repo = Code(ReadRepoFile(Repo));
        var body = MethodBody(repo, "public async Task<int> DeleteFileEntriesAsync");
        // 这条 SQL 必须自己就是安全的：Id 与 (type, source) 三样一起限定
        Assert.Contains("id = @id", body, StringComparison.Ordinal);
        Assert.Contains("type = @type", body, StringComparison.Ordinal);
        Assert.Contains("source IN (@fs, @local)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TryDeleteClipFiles", body, StringComparison.Ordinal);   // 不碰附件文件的唯一途径就是不调那颗
        Assert.DoesNotContain("File.Delete", body, StringComparison.Ordinal);
    }

    // ────────── ③ 那台批量入库的生产者不许回来 ──────────

    /// <summary>
    /// 「索引进库」整条拆掉后剩下的形状：Everything <b>仍然参与实时搜索</b>，但<b>不再全量入库</b>，
    /// 而设置页也不再有任何"目录／上限／保存"三件套。
    /// <para>为什么值得钉住：<c>ItemCardPolicy.CanDeletePermanently</c> 放开文件行的前提是
    /// "删了不会被同步拉回来"。这台生产者一回来，那个判据就变成"看着无效的动作"（而且比原来更糟：
    /// 用户以为删掉了，下次同步又多出一批）。所以这一格与 ② 是绑在一起的。</para>
    /// </summary>
    [Fact]
    public void TheBulkIndexingProducerIsGoneWhileLiveSearchStays()
    {
        var everything = Code(ReadRepoFile(Everything));
        // 钉的是"这一臂不再扫任何东西"，不钉它用哪种写法交回空集合（台架 SP2 钉这一条）
        var body = MethodBody(everything, "public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)");
        Assert.Equal(0, Count(body, "QueryAsync"));                       // 不发 IPC 查询
        Assert.DoesNotContain("foreach", body, StringComparison.Ordinal);   // 不逐目录遍历
        Assert.DoesNotContain("Roots", body, StringComparison.Ordinal);
        Assert.Contains("Task<IReadOnlyList<Item>> SearchAsync", everything, StringComparison.Ordinal);   // 实时搜索那一半必须还在
        Assert.DoesNotContain("public IReadOnlyList<string> Roots", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("public int MaxCount", everything, StringComparison.Ordinal);

        // 设置页那张卡：目录框、上限框、保存按钮、"索引进库"这句说明都不许回来
        var xaml = Markup(ReadRepoFile("src/StarMark.UI/Views/SettingsPage.xaml"));
        Assert.DoesNotContain("索引进库", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FileIndexRootsText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxFileIndexCountText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveRoots_Click", xaml, StringComparison.Ordinal);
        Assert.Equal(0, Count(AllSrcCode(), "FileIndexRoots"));
    }

    /// <summary>「打开时给按钮」与「同步完成那句话」之间不能再有第三种说法：这条链只有一处递按钮。</summary>
    [Fact]
    public void TheInPlaceExitIsTheOnlyPlaceThatOffersIt()
    {
        var src = AllSrcCode();
        Assert.Equal(1, Count(src, "ShowErrorWithAction(") - Count(src, "public void ShowErrorWithAction("));
    }

    // ────────── ④ 空库不再被灌进假条目 ──────────

    /// <summary>
    /// 那颗 seeder 会把三条假 star/书签 ＋ <b>一条指向不存在文件的假路径</b>写进用户真库——
    /// 用户报的"树里留着早已删除的测试用例路径"第一颗就是它（它自己的注释都写着"接入同步后可移除"）。
    /// <para>钉"文件不存在"而不是"内容改了"：把 seeder 改小、留个空壳也算违规——那一行只要还在，
    /// 就会有用户对着一条打不开的死路径问"为什么删不掉"。</para>
    /// </summary>
    [Fact]
    public void TheSeederIsGoneSoAnEmptyLibraryStaysEmpty()
    {
        Assert.False(System.IO.File.Exists(System.IO.Path.Combine(RepoRoot(), "src", "StarMark.UI", "SeedData.cs")));
        var app = Code(ReadRepoFile(AppCode));
        Assert.DoesNotContain("SeedIfEmptyAsync", app, StringComparison.Ordinal);
        Assert.DoesNotContain("SeedData", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「扫过了、没有要清理的行」与「扫描失败」这两种也都必须说得出：那句话的可见性只能绑<b>有没有话要说</b>，
    /// 不能绑"有没有可删的行"——绑后者就等于把失败与"都还在"一起藏起来，
    /// 于是那颗按钮点下去照旧"没反应"（与批次 IJ 那颗 <c>HasItems</c> 未复位导致失败提示看不见同族）。
    /// <para>数<b>引用次数</b>而不是钉整行 XAML：两个名字差一个后缀，按行找会被排版改动冤枉。</para>
    /// </summary>
    [Fact]
    public void TheScanSentenceSpeaksEvenWhenThereIsNothingToDelete()
    {
        var xaml = Markup(ReadRepoFile(SettingsXaml));
        Assert.Equal(1, Count(xaml, "ViewModel.HasMissingFileRowsNotice"));   // 那句话自己的可见性
        Assert.Equal(1, Count(xaml, "ViewModel.HasMissingFileRows,"));        // 带逗号的这颗只许是「清掉这些行」按钮

        var vm = Code(ReadRepoFile(LocalDiskVm));
        Assert.Contains("!string.IsNullOrEmpty(MissingFileRowsStatus)", vm, StringComparison.Ordinal);
        var body = MethodBody(vm, "private async Task RescanMissingFilesAsync()");
        Assert.DoesNotContain("MissingFileRowsStatus = string.Empty", body, StringComparison.Ordinal);
    }

    /// <summary>守门自己要有证人：喂一段"退回旧写法"的样串必须命中，否则上面几格是装饰（#228/#236 那一族）。</summary>
    [Fact]
    public void TheScannersActuallyBitOnSamples()
    {
        // 把回报扔掉的老写法：必须被 ① 那格的两道禁项各抓一次
        var old = "public static void Open(XamlRoot xamlRoot, ItemCardViewModel? vm)\n{\n    _ = LauncherEx.OpenAsync(vm.Uri);\n}";
        Assert.Contains("_ = LauncherEx", old, StringComparison.Ordinal);
        // 删行 SQL 丢掉种类条件：必须能被认出（少一个 @type/@fs 就是那条 WHERE 不再自证安全）
        Assert.DoesNotContain("source IN (@fs, @local)",
            "DELETE FROM items WHERE id = @id;", StringComparison.Ordinal);
    }

    // ────────── ⑤ 判据本身的真值表（钉行为，不是钉文本）──────────

    /// <summary>
    /// 哪些行给"永久删除"：<b>内置剪贴板历史 ∪ 本机文件条目</b>；书签 / Star / Ditto 派 / 热榜候选都不给。
    /// <para>Ditto 那一行是这张表最要紧的一格：它的 <c>Type</c> 也是 <c>Clipboard</c>，
    /// 只看类型就会多出一个"点了只会报记录已经不在"的死项（删得掉与能删必须同一个判据）。</para>
    /// </summary>
    [Theory]
    [InlineData(ItemSources.Clipboard, ItemType.Clipboard, false, true, true)]     // 内置历史：给（旧裁决不变）
    [InlineData(ItemSources.FileSystem, ItemType.File, false, false, true)]         // 索引遗留的文件行：给（本批新加）
    [InlineData(ItemSources.Local, ItemType.File, false, false, true)]              // 「记录到本地」/拖入登记的：给
    [InlineData("ditto", ItemType.Clipboard, false, false, false)]                  // 外部程序的库：不给（越界且删不掉）
    [InlineData(ItemSources.Chrome, ItemType.Bookmark, false, false, false)]        // 会被下次同步拉回来：不给
    [InlineData(ItemSources.GitHub, ItemType.GitHubStar, false, false, false)]       // 同上，且连着不可重建的用户态
    [InlineData(ItemSources.FileSystem, ItemType.File, true, false, false)]          // 启动器行：只走它自己的"移除"，不写主库
    public void CanDeletePermanently_TruthTable(string source, ItemType type, bool launcher, bool builtin, bool expected)
        => Assert.Equal(expected, ItemCardPolicy.CanDeletePermanently(source, type, launcher, builtin));

    /// <summary>
    /// "是不是本机文件行"只认 (type, source)：<b>不看盘</b>、也<b>不看 Uri 长什么样</b>。
    /// <para>盘上有没有是另一件事（决定"打开时要不要递出删行按钮"），把两件事混进一颗判据，
    /// 就会出现"移动盘没插 ⇒ 那一行突然不许删"这种讲不通的行为。</para>
    /// </summary>
    [Theory]
    [InlineData(ItemSources.FileSystem, ItemType.File, true)]
    [InlineData(ItemSources.Local, ItemType.File, true)]
    [InlineData(ItemSources.FileSystem, ItemType.Bookmark, false)]
    [InlineData(ItemSources.Local, ItemType.Todo, false)]
    [InlineData(ItemSources.GitHub, ItemType.GitHubStar, false)]
    [InlineData(ItemSources.Clipboard, ItemType.Clipboard, false)]
    public void IsLocalFileRow_TruthTable(string source, ItemType type, bool expected)
        => Assert.Equal(expected, ItemCardPolicy.IsLocalFileRow(source, type));
}
