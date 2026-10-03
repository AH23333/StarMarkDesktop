#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 替换那一层的闸门（批次 UG-2）：<b>真临时目录、真改名、真进程</b>。
/// <para>这层只有两件事值得信：① 换完之后的盘上是什么，② 摔了之后盘上还剩什么。
/// 两件都必须当场看得见，不能靠注释承诺（UF/UG-0 那格欠证的教训，#230/#232）——
/// 所以除了"等进程退出"和"起一个 exe"这两条与外界打交道的边，其余一律走文件系统真的那一套。</para>
/// </summary>
public sealed class UpdaterEngineTests : IDisposable
{
    private const string Entry = UpdateAssets.EntryExeName;      // "StarMark.UI.exe"
    private const string OldMarker = "old-version";
    private const string NewMarker = "new-version";

    private readonly string _root;
    private readonly string _install;
    private readonly string _staged;
    private readonly List<string> _starts = new();
    private readonly List<FileStream> _holds = new();

    public UpdaterEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "StarMarkUpdaterTests", Guid.NewGuid().ToString("N"));
        _install = Path.Combine(_root, "App", "StarMark");
        _staged = Path.Combine(_root, ".StarMarkUpdate", "1.0.1" + UpdateAssets.NewTreeSuffix);
        Directory.CreateDirectory(_install);
        Directory.CreateDirectory(_staged);
        File.WriteAllText(Path.Combine(_install, Entry), OldMarker);
        File.WriteAllText(Path.Combine(_install, "data.txt"), OldMarker);
        File.WriteAllText(Path.Combine(_staged, Entry), NewMarker);
        File.WriteAllText(Path.Combine(_staged, "data.txt"), NewMarker);
    }

    public void Dispose()
    {
        foreach (var hold in _holds) { try { hold.Dispose(); } catch (IOException) { } }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* 测试尾巴，不因为它把结论改掉 */ }
    }

    private UpdaterRequest Req() => new(12345, _staged, _install);

    /// <summary>
    /// 默认的替身边：<b>等进程说"退了"，起 exe 只记账</b>；重试节奏压到最短，
    /// 免得一次单元测试因为"自己等自己"跑上一秒。
    /// </summary>
    private UpdaterEngine.Options Opts(bool parentGone = true) => new()
    {
        WaitForExit = (_, _) => parentGone,
        StartExe = _starts.Add,
        RunningExePath = Path.Combine(_root, "runner", "StarMark.Updater.exe"),
        Attempts = 3,
        RetryDelay = TimeSpan.FromMilliseconds(5),
        Ceiling = TimeSpan.FromMilliseconds(50),
    };

    /// <summary>占住一颗文件（<c>FileShare.None</c>）——Windows 上这就是"这颗文件此刻不许动"的真形状。</summary>
    private void Hold(string path)
    {
        File.WriteAllText(path, "held");
        _holds.Add(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
    }

    private string ReadInstalledEntry() => File.ReadAllText(Path.Combine(_install, Entry));

    // ===== 成功那一格：换进去的必须是新树，而且走的是改名不是复制 =====

    [Fact]
    public void TheSwapPutsTheNewTreesFilesWhereTheOldOnesWere()
    {
        var result = UpdaterEngine.Run(Req(), Opts());

        Assert.Equal(UpdaterOutcome.Success, result.Status);
        Assert.Equal(NewMarker, ReadInstalledEntry());
        Assert.Equal(NewMarker, File.ReadAllText(Path.Combine(_install, "data.txt")));
    }

    [Fact]
    public void TheStagedTreeIsRenamedIntoPlaceRatherThanCopied()
    {
        UpdaterEngine.Run(Req(), Opts());

        // 复制出来的东西不会有"原名不见了"这一条；这一句把"只改名"钉住（跨盘那条路因此根本不许走）
        Assert.False(Directory.Exists(_staged));
    }

    [Fact]
    public void TheOldVersionIsNotLeftAnywhereUnderTheStagingRoot()
    {
        var result = UpdaterEngine.Run(Req(), Opts());

        Assert.Equal(UpdaterOutcome.Success, result.Status);
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, ".StarMarkUpdate")));
    }

    [Fact]
    public void TheRelaunchedExeIsTheEntryExeUnderTheInstallDirectory()
    {
        UpdaterEngine.Run(Req(), Opts());

        Assert.Equal(new[] { Path.Combine(_install, Entry) }, _starts);
    }

    [Fact]
    public void SubdirectoriesInsideBothTreesRideAlong()
    {
        Directory.CreateDirectory(Path.Combine(_staged, "assets"));
        File.WriteAllText(Path.Combine(_staged, "assets", "icon.png"), NewMarker);
        Directory.CreateDirectory(Path.Combine(_install, "Resources"));
        File.WriteAllText(Path.Combine(_install, "Resources", "old.res"), OldMarker);

        UpdaterEngine.Run(Req(), Opts());

        Assert.Equal(NewMarker, File.ReadAllText(Path.Combine(_install, "assets", "icon.png")));
        Assert.False(File.Exists(Path.Combine(_install, "Resources", "old.res")));
    }

    [Fact]
    public void ALeftoverOldSlotFromAnEarlierRunDoesNotBlockTheSwap()
    {
        Directory.CreateDirectory(_install + UpdateAssets.OldTreeSuffix);          // 上一轮的旧账占着位置
        File.WriteAllText(Path.Combine(_install + UpdateAssets.OldTreeSuffix, "junk.txt"), "x");

        var result = UpdaterEngine.Run(Req(), Opts());

        Assert.Equal(UpdaterOutcome.Success, result.Status);                        // 换得成，不是"请你去删目录"
        Assert.Equal(NewMarker, ReadInstalledEntry());
    }

    [Fact]
    public void EveryOldTreeIsSweptAfterASuccessfulSwap()
    {
        Directory.CreateDirectory(_install + UpdateAssets.OldTreeSuffix + ".9");   // 更早几轮留下的
        File.WriteAllText(Path.Combine(_install + UpdateAssets.OldTreeSuffix + ".9", "junk.txt"), "x");

        UpdaterEngine.Run(Req(), Opts());

        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(_install)!,
            Path.GetFileName(_install) + UpdateAssets.OldTreeSuffix + "*"));
    }

    // ===== 动手之前先拒：一条都不许先碰盘 =====

    [Fact]
    public void ATreeWithoutTheEntryExeIsRefusedAndNothingMoves()
    {
        File.Delete(Path.Combine(_staged, Entry));

        var result = UpdaterEngine.Run(Req(), Opts());

        Assert.Equal(UpdaterOutcome.InvalidRequest, result.Status);
        Assert.Equal(OldMarker, ReadInstalledEntry());       // 没换
        Assert.True(Directory.Exists(_staged));              // 也没挪走
        Assert.Empty(_starts);                               // 更没起任何进程
    }

    [Fact]
    public void AStagedTreeThatIsNoLongerOnDiskIsRefused()
    {
        Directory.Delete(_staged, recursive: true);

        var result = UpdaterEngine.Run(Req(), Opts());

        Assert.Equal(UpdaterOutcome.InvalidRequest, result.Status);
        Assert.Equal(OldMarker, ReadInstalledEntry());
    }

    [Fact]
    public void TheUpdaterRefusesToRunFromInsideTheTreeItWouldMove()
    {
        var o = Opts();
        var result = UpdaterEngine.Run(Req(), new UpdaterEngine.Options
        {
            WaitForExit = o.WaitForExit, StartExe = o.StartExe, Ceiling = o.Ceiling,
            Attempts = o.Attempts, RetryDelay = o.RetryDelay,
            RunningExePath = Path.Combine(_install, "Updater", "StarMark.Updater.exe"),   // 就在自己脚下
        });

        Assert.Equal(UpdaterOutcome.InvalidRequest, result.Status);
        Assert.Equal(OldMarker, ReadInstalledEntry());
        Assert.Empty(_starts);
    }

    [Fact]
    public void NothingMovesWhenTheParentProcessWillNotExit()
    {
        var result = UpdaterEngine.Run(Req(), Opts(parentGone: false));

        Assert.Equal(UpdaterOutcome.ParentStillRunning, result.Status);
        Assert.Equal(OldMarker, ReadInstalledEntry());
        Assert.True(Directory.Exists(_staged));
        Assert.Contains("什么都没动", result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"D:\Other\1.0.1.new", @"C:\App\StarMark")]         // 跨盘：只能复制，而复制不原子
    [InlineData(@"C:\App\StarMark\.StarMarkUpdate\1.0.1.new", @"C:\App\StarMark")]  // 新树在旧树里面
    [InlineData(@"C:\App\StarMark\1.0.1.new", @"C:\App\StarMark\1.0.1.new")]         // 同一颗
    [InlineData(@"C:\App\StarMark\..\StarMark", @"C:\App\StarMark")]                 // 字符串不是它自己
    [InlineData("Relative\\1.0.1.new", @"C:\App\StarMark")]                           // 不是绝对路径
    public void ARequestThatWouldMoveSomethingIntoItselfIsRefused(string staged, string install)
        => Assert.False(UpdaterContract.Validate(new UpdaterRequest(1, staged, install), out _));

    [Fact]
    public void AStagedTreeWithoutTheDotNewSuffixIsRefused()
        => Assert.False(UpdaterContract.Validate(
            new UpdaterRequest(1, @"C:\App\.StarMarkUpdate\1.0.1", @"C:\App\StarMark"), out _));

    // ===== 参数这颗：编解码必须是一对，且不许有第二个入口 =====

    [Fact]
    public void TheRequestSurvivesTheCommandLineRoundTrip()
    {
        var request = Req();

        Assert.True(UpdaterContract.TryDecode(UpdaterContract.Encode(request), out var back, out var error));
        Assert.Null(error);
        Assert.Equal(request, back);
    }

    public static TheoryData<string[]> BadCommandLineShapes() => new()
    {
        Array.Empty<string>(),
        new[] { "--pid", "1" },                                                                     // 缺两对
        new[] { "--pid", "1", "--new", @"C:\a.new", "--target", @"C:\b", "--extra", "x" },         // 多一对
        new[] { "--pid", "1", "--new", @"C:\a.new", "--new", @"C:\z.new", "--target", @"C:\b" },   // 重名
        new[] { "--pid", "abc", "--new", @"C:\a.new", "--target", @"C:\b" },                       // 进程号不是号
        new[] { "positional", "--pid", "1", "--new", @"C:\a.new", "--target", @"C:\b" },           // 裸串
    };

    [Theory]
    [MemberData(nameof(BadCommandLineShapes))]
    public void AnyOtherCommandLineShapeIsRefused(string[] argv)
    {
        Assert.False(UpdaterContract.TryDecode(argv, out var request, out var error));
        Assert.Null(request);                       // 拒了就不许留下一条"半解出来"的指令
        Assert.NotNull(error);                      // 每一句拒绝都说得出为什么
    }

    [Fact]
    public void TheCommandLineCarriesNoIdeaOfWhatExeToStart()
    {
        var argv = UpdaterContract.Encode(Req());

        Assert.Equal(6, argv.Count);                                   // 三对：pid / new / target
        Assert.Equal(new[] { UpdaterContract.PidFlag, UpdaterContract.NewFlag, UpdaterContract.TargetFlag },
            new[] { argv[0], argv[2], argv[4] });                      // 没有第四颗"起哪个 exe"
        Assert.DoesNotContain(UpdateAssets.EntryExeName, string.Join(" ", argv), StringComparison.Ordinal);
    }

    // ===== 摔了之后：盘上必须说得出是哪一种状态 =====

    [Fact]
    public void AnInstallDirectoryThatWontMoveSaysSoAndLeavesEverythingInPlace()
    {
        Hold(Path.Combine(_install, "data.txt"));

        var result = UpdaterEngine.Run(Req(), Opts());

        Assert.Equal(UpdaterOutcome.OldTreeNotMoved, result.Status);
        Assert.Equal(OldMarker, ReadInstalledEntry());
        Assert.True(Directory.Exists(_staged));
        // 交棒那一刻界面对用户说的是"程序马上会重开"，而父进程此刻已经退出、屏上没有一个窗口。
        // 这一条出口不把他原来那一版重新起来，那句真话就成了一句谎，而用户手上只剩一行日志（P-54）。
        Assert.Equal(new[] { Path.Combine(_install, Entry) }, _starts);
        Assert.Contains("重新起来", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenTheOldVersionCantBeStartedEitherThatGetsItsOwnOutcome()
    {
        // "盘上没改动"与"程序还开着"是两件事。折成一格就会拿后者担保前者（#234 那一族），
        // 而这一格的盘上比 RolledBackButNotRunning 那一格更干净——它连一次改名都没发生过。
        Hold(Path.Combine(_install, "data.txt"));

        var result = UpdaterEngine.Run(Req(), new UpdaterEngine.Options
        {
            WaitForExit = (_, _) => true,
            StartExe = _ => throw new InvalidOperationException("这台机器不肯起新进程"),
            RunningExePath = Path.Combine(_root, "runner", "StarMark.Updater.exe"),
            Attempts = 3,
            RetryDelay = TimeSpan.FromMilliseconds(5),
            Ceiling = TimeSpan.FromMilliseconds(50),
        });

        Assert.Equal(UpdaterOutcome.UntouchedButNotRunning, result.Status);
        Assert.Equal(OldMarker, ReadInstalledEntry());
        Assert.True(Directory.Exists(_staged));
        Assert.Contains("没能起来", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AFilledUpOldTreeSlotAlsoGivesTheOldVersionBack()
    {
        // 这一格走的也是"改名之前就得停"：旧树原位完好无损，所以它同样不许把人留在空屏幕上。
        var first = _install + UpdateAssets.OldTreeSuffix;
        Directory.CreateDirectory(first);
        for (var n = 1; n <= 9; n++) Directory.CreateDirectory($"{first}.{n}");

        var result = UpdaterEngine.Run(Req(), Opts());

        Assert.Equal(UpdaterOutcome.InvalidRequest, result.Status);
        Assert.Equal(OldMarker, ReadInstalledEntry());
        Assert.Equal(new[] { Path.Combine(_install, Entry) }, _starts);
    }

    [Fact]
    public void InUseFilesAreRetriedRatherThanHandedBackToTheUser()
    {
        // 复杂环境里的常态：杀软／索引器／另一个进程只是"此刻"占着这颗文件。
        // 这一格证明的是"过一会儿就好"这一族我们自己扛，不写进任何一句界面话里（P-54）。
        var live = new FileStream(Path.Combine(_install, "data.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        // 松手用专用线程而不是 Task.Run：引擎的重试预算只有 40×25 ms，而 Task.Run 的续体排在**线程池注入速率**
        // 后面——全量并行跑时池是饱和的，"120 ms 后松手"可能变成"一秒之后才松手"，红的就是这一格（批次 UJ，与 ✅P-148 同族）。
        var releaser = new Thread(() => { Thread.Sleep(120); live.Dispose(); }) { IsBackground = true };
        releaser.Start();

        var result = UpdaterEngine.Run(Req(), new UpdaterEngine.Options
        {
            WaitForExit = (_, _) => true, StartExe = _starts.Add,
            RunningExePath = Path.Combine(_root, "runner", "StarMark.Updater.exe"),
            Attempts = 40, RetryDelay = TimeSpan.FromMilliseconds(25), Ceiling = TimeSpan.FromMilliseconds(50),
        });
        releaser.Join();                       // 别在还占着句柄的那颗线程没跑完时就把目录交出去

        Assert.Equal(UpdaterOutcome.Success, result.Status);
        Assert.Equal(NewMarker, ReadInstalledEntry());
    }

    [Fact]
    public void AMoveInThatFailsRollsTheOldTreeBackAndRestartsTheOldVersion()
    {
        Hold(Path.Combine(_staged, "data.txt"));           // 新树挪不进来

        var result = UpdaterEngine.Run(Req(), Opts());

        Assert.Equal(UpdaterOutcome.RolledBack, result.Status);
        Assert.Equal(OldMarker, ReadInstalledEntry());                   // 旧的那一版回到原位
        Assert.Equal(new[] { Path.Combine(_install, Entry) }, _starts);   // 而且重新起来了
        Assert.Contains("新树没能换进安装目录", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ARelaunchThatFailsPutsTheOldVersionBackAndKeepsTheNewTreeForLookin()
    {
        var result = UpdaterEngine.Run(Req(), OptsThat(throwOnStartCalls: new[] { 1 }));

        Assert.Equal(UpdaterOutcome.RolledBack, result.Status);
        Assert.Equal(OldMarker, ReadInstalledEntry());                                     // 旧的那一版回到原位
        Assert.Equal(NewMarker, File.ReadAllText(Path.Combine(_staged + ".failed", Entry))); // 新的那棵留着，没删
        Assert.Equal(new[] { Path.Combine(_install, Entry) }, _starts);                     // 记下来的只有"重新起旧版"那一次
        Assert.Equal(2, _startAttempts);                                                    // 试了两次：新版一次、旧版一次
    }

    [Fact]
    public void ANewTreeThatCannotBePulledOutOfTheInstallDirectorySaysItIsIncomplete()
    {
        var o = Opts();
        var failing = new UpdaterEngine.Options
        {
            WaitForExit = o.WaitForExit, Ceiling = o.Ceiling, Attempts = o.Attempts, RetryDelay = o.RetryDelay,
            RunningExePath = o.RunningExePath,
            StartExe = _ =>
            {
                Hold(Path.Combine(_install, Entry));            // 新树此刻被自己占住 ⇒ 拔不出来
                throw new InvalidOperationException("新版本起不来");
            },
        };

        var result = UpdaterEngine.Run(Req(), failing);

        Assert.Equal(UpdaterOutcome.RollbackFailed, result.Status);
        Assert.Contains(_install, result.Detail, StringComparison.Ordinal);
        Assert.Contains(UpdateAssets.OldTreeSuffix, result.Detail, StringComparison.Ordinal);  // 旧树在哪儿也说清了
    }

    [Fact]
    public void ARollbackThatCannotRestartTheOldVersionIsSaidSeparatelyFromARollbackThatDid()
    {
        // 两格都试：新版起不来（第 1 次）、退回去之后旧版也起不来（第 2 次）
        var result = UpdaterEngine.Run(Req(), OptsThat(throwOnStartCalls: new[] { 1, 2 }));

        Assert.Equal(UpdaterOutcome.RolledBackButNotRunning, result.Status);
        Assert.Equal(OldMarker, ReadInstalledEntry());   // 盘是完好的，只是程序没开着
        Assert.Empty(_starts);
        Assert.Equal(2, _startAttempts);
    }

    private int _startAttempts;

    private UpdaterEngine.Options OptsThat(int[] throwOnStartCalls)
    {
        var o = Opts();
        return new UpdaterEngine.Options
        {
            WaitForExit = o.WaitForExit, Ceiling = o.Ceiling, Attempts = o.Attempts, RetryDelay = o.RetryDelay,
            RunningExePath = o.RunningExePath,
            StartExe = path =>
            {
                if (throwOnStartCalls.Contains(++_startAttempts)) throw new InvalidOperationException("起不来");
                _starts.Add(path);
            },
        };
    }

    // ===== 默认那两条边：真进程 =====

    [Fact]
    public void TheDefaultWaiterLetsTheSwapGoOnceARealProcessHasExited()
    {
        var process = StartNapping(1);
        try
        {
            var result = UpdaterEngine.Run(new UpdaterRequest(process.Id, _staged, _install),
                new UpdaterEngine.Options { StartExe = _starts.Add, RunningExePath = Path.Combine(_root, "r.exe") });

            Assert.Equal(UpdaterOutcome.Success, result.Status);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); process.Dispose(); }
    }

    [Fact]
    public void TheDefaultWaiterDoesNotPretendALiveProcessIsGone()
    {
        var process = StartNapping(30);        // 真还在跑：这条是唯一能证明"等着"不是恒真的证人
        try
        {
            var result = UpdaterEngine.Run(new UpdaterRequest(process.Id, _staged, _install),
                new UpdaterEngine.Options
                {
                    StartExe = _starts.Add, RunningExePath = Path.Combine(_root, "r.exe"),
                    Ceiling = TimeSpan.FromMilliseconds(400),
                });

            Assert.Equal(UpdaterOutcome.ParentStillRunning, result.Status);
            Assert.Equal(OldMarker, ReadInstalledEntry());
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); process.Dispose(); }
    }

    private static Process StartNapping(int seconds)
        => Process.Start(new ProcessStartInfo("ping.exe", $"-n {seconds} 127.0.0.1")
        { UseShellExecute = false, CreateNoWindow = true })!;

    // ===== 措辞与射程（这一族只有读源码才看得见）=====

    [Fact]
    public void TheReplacementSentencesAreAsManyDistinctOnesAsThereAreOutcomes()
    {
        var values = Enum.GetValues<UpdaterOutcome>();
        Assert.Equal(values.Length, values.Select(UpdatePolicy.Describe).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryUpdaterOutcomeHasItsOwnArmInUpdatePolicy()
    {
        // 只比句数挡不住"新增一格忘了配话"：兜底臂会给它一句仍然不同的话（同一坑的现场见 #233）
        var policy = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdatePolicy.cs"));
        foreach (UpdaterOutcome value in Enum.GetValues<UpdaterOutcome>())
            Assert.Contains($"UpdaterOutcome.{value} =>", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryUpdaterOutcomeHasItsOwnExitCodeArm()
    {
        var contract = Code(ReadRepoFile("src/StarMark.Abstractions/Updates/UpdaterContract.cs"));
        foreach (UpdaterOutcome value in Enum.GetValues<UpdaterOutcome>())
            Assert.Contains($"UpdaterOutcome.{value} =>", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessIsTheOnlyZeroExitCodeAndNoneOfThemCollapsesIntoTheFallbackNine()
    {
        foreach (UpdaterOutcome value in Enum.GetValues<UpdaterOutcome>())
        {
            var code = UpdaterContract.ExitCodeFor(value);
            Assert.Equal(value == UpdaterOutcome.Success, code == 0);
            Assert.NotEqual(9, code);       // 9 是"有人新增了一格而这里没配"，不能被任何真的格子占用
        }
        Assert.Equal(Enum.GetValues<UpdaterOutcome>().Length,
            Enum.GetValues<UpdaterOutcome>().Select(UpdaterContract.ExitCodeFor).Distinct().Count());
    }

    [Fact]
    public void NoReplacementSentenceHandsWorkToTheUserExceptTheOneThatGenuinelyMust()
    {
        foreach (UpdaterOutcome value in Enum.GetValues<UpdaterOutcome>())
        {
            var text = UpdatePolicy.Describe(value);
            if (value == UpdaterOutcome.RollbackFailed)
            {
                Assert.Contains("重新安装", text, StringComparison.Ordinal);   // 唯一一条我们自己走不出来的路
                continue;
            }
            Assert.DoesNotContain("重新安装", text, StringComparison.Ordinal);
            Assert.DoesNotContain("请", text, StringComparison.Ordinal);
            Assert.DoesNotContain("关闭", text, StringComparison.Ordinal);
            Assert.DoesNotContain("稍后", text, StringComparison.Ordinal);
            if (value != UpdaterOutcome.Success) Assert.DoesNotContain("已经换好", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheSentenceAboutRestartingIsBackedByTheExitThatActuallyRestarts()
    {
        // 那句话现在写着"程序也已经重新起来"。它一旦能从一条没起过程序的出口返回，就成了第二句谎——
        // 而这一次谎的方向是把"屏幕空了"说成"一切正常"（#234：措辞与实现各走各的）。
        var engine = ReadRepoFile("src/StarMark.Core/Updates/UpdaterEngine.cs");
        var body = MethodBody(engine, "private static UpdaterResult NothingSwapped");
        Assert.Contains("TryStart(entryExe", body, StringComparison.Ordinal);
        Assert.Contains("UpdaterOutcome.UntouchedButNotRunning", body, StringComparison.Ordinal);

        var code = Code(engine);
        Assert.Equal(0, Count(code, "new(UpdaterOutcome.OldTreeNotMoved"));
        Assert.Equal(1, Count(code, "NothingSwapped(UpdaterOutcome.OldTreeNotMoved"));
        Assert.Equal(1, Count(code, "NothingSwapped(UpdaterOutcome.InvalidRequest"));

        // 反方向也要钉：这一族的两句话分别承诺了"程序已经重开"与"不猜成因"。
        // 话被抹平或把猜写回事实，都不会让上面任何一格红——只有这两行会。
        var notMoved = UpdatePolicy.Describe(UpdaterOutcome.OldTreeNotMoved);
        Assert.Contains("重新起来", notMoved, StringComparison.Ordinal);
        Assert.DoesNotContain("有别的东西", notMoved, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLaunchSentencesDoNotClaimTheUpdateIsFinished()
    {
        var values = Enum.GetValues<LaunchStatus>();
        Assert.Equal(values.Length, values.Select(UpdatePolicy.Describe).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("已经换好", UpdatePolicy.Describe(LaunchStatus.Started), StringComparison.Ordinal);
        Assert.DoesNotContain("重新安装", UpdatePolicy.Describe(LaunchStatus.NotStarted), StringComparison.Ordinal);
        foreach (LaunchStatus value in values)
            Assert.Contains($"LaunchStatus.{value} =>",
                Code(ReadRepoFile("src/StarMark.Core/Updates/UpdatePolicy.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheUpdaterExeStaysAThinShellAroundTheEngine()
    {
        // 壳里一旦长出"挪目录／等进程／自己重试"，那些判据就从今天起没有证人（测试工程不引用可执行工程）
        var shell = Code(ReadRepoFile("src/StarMark.Updater/Program.cs"));
        Assert.Contains("UpdaterEngine.Run(", shell, StringComparison.Ordinal);
        Assert.Contains("UpdaterContract.ExitCodeFor(", shell, StringComparison.Ordinal);
        foreach (var banned in new[]
                 { "Directory.Move", "Directory.Delete", "File.Copy", "File.Move", "File.Delete",
                   "Process.Start", "Process.GetProcessById", "Thread.Sleep" })
            Assert.DoesNotContain(banned, shell, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheEngineMovesTreesAndTheLauncherOnlyCopiesOne()
    {
        var engine = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdaterEngine.cs"));
        var launcher = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdaterLauncher.cs"));
        Assert.Contains("Directory.Move(", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Move(", launcher, StringComparison.Ordinal);   // 它只负责"请出去跑"
        Assert.DoesNotContain("Environment.Exit", engine, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTreeSuffixesAreDefinedOnceAndUsedByBothSides()
    {
        var definitions = ReadRepoUnder("src")
            .Where(f => f.Text.Contains("NewTreeSuffix =") || f.Text.Contains("OldTreeSuffix =")).ToList();
        Assert.Single(definitions);
        Assert.Equal("src/StarMark.Abstractions/Updates/UpdatePackage.cs", definitions[0].RelativePath);

        var staging = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdateStaging.cs"));
        var engine = Code(ReadRepoFile("src/StarMark.Core/Updates/UpdaterEngine.cs"));
        Assert.Contains("UpdateAssets.NewTreeSuffix", staging, StringComparison.Ordinal);
        Assert.DoesNotContain("\".new\"", staging, StringComparison.Ordinal);     // 不许两侧各写一份字面串
        Assert.Contains("UpdateAssets.OldTreeSuffix", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("\"_old\"", engine, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRunnersHomeIsNotDerivedFromTheDirectoryItReplaces()
    {
        var paths = Code(ReadRepoFile("src/StarMark.Abstractions/Updates/UpdaterContract.cs"));
        Assert.Contains("SpecialFolder.LocalApplicationData", paths, StringComparison.Ordinal);
        Assert.DoesNotContain("AppContext.BaseDirectory", paths, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.ProcessPath", paths, StringComparison.Ordinal);
    }
}
