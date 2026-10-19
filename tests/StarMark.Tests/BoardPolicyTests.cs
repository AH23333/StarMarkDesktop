#nullable enable
using System;
using System.Linq;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 画板那侧的身份、落盘节拍与撤销立场（批次 Phase C 片12＝v1 §21.3／§21.4）。
/// <para>这颗证人里最值得记住的一条是 ⑦：<b>载档与桥回写必须⛔ 进撤销栈</b>。那颗包里 <c>captureUpdate</c>
/// 的默认逐字是 <c>EVENTUALLY</c>——也就是"少传一个实参"不会报错，只会让用户一次 Ctrl+Z 把整块板退成空白。
/// 那种坏在屏幕上长得像"程序自己把我的图删了"。</para>
/// </summary>
public sealed class BoardPolicyTests
{
    private const string Id = "261017-071500-abcd";

    // ────────── ① 稳定 id 的形状 ──────────

    [Theory]
    [InlineData(0x0000, "-0000")]
    [InlineData(0xabcd, "-abcd")]
    [InlineData(0xFFFF, "-ffff")]
    public void SceneIdIsEighteenCharsWithFourLowerHex(int rand, string tail)
    {
        var id = BoardPolicy.NewSceneId(new DateTimeOffset(2026, 10, 17, 7, 15, 0, TimeSpan.Zero), rand);

        Assert.Equal(18, id.Length);
        Assert.EndsWith(tail, id, StringComparison.Ordinal);       // 小写、补零到四位
        Assert.True(BoardPolicy.IsSceneId(id), id);
    }

    /// <summary>
    /// 那颗 id 的年月日时分秒取自<b>本地时刻</b>（档名给用户看）。
    /// <para>期望按<b>颗</b>拼（月用 <c>MM</c>、分钟用 <c>mm</c>），⛔ 复用判据里那颗格式串——
    /// 否则格式串写错（把分钟抄进月位）会被同一颗错读成"对"。本仓那条"三侧⛔ 同一构造函数"的旧课，
    /// 这一批以另一种形状撞回来一次。</para>
    /// </summary>
    [Fact]
    public void StampFollowsTheLocalClockAndEachSegmentIsItself()
    {
        var utcNoon = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var id = BoardPolicy.NewSceneId(utcNoon, 0x0001);
        var local = utcNoon.ToLocalTime();

        Assert.True(BoardPolicy.IsSceneId(id), id);
        Assert.Equal(local.ToString("yy"), id[..2]);                       // 年
        Assert.Equal(local.ToString("MM"), id[2..4]);                      // 月（⛔ 是分钟）
        Assert.Equal(local.ToString("dd"), id[4..6]);                      // 日
        Assert.Equal('-', id[6]);
        Assert.Equal(local.ToString("HH"), id[7..9]);                      // 时
        Assert.Equal(local.ToString("mm"), id[9..11]);                     // 分
        Assert.Equal(local.ToString("ss"), id[11..13]);                    // 秒
        Assert.Equal('-', id[13]);
        Assert.Equal("0001", id[14..]);                                    // 随机段：小写四位
    }

    /// <summary>随机段越界＝编程错，直接抛；⛔ 取模（取模会让那四位不是均匀分布，id 更容易撞）。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0x10000)]
    [InlineData(int.MaxValue)]
    public void OutOfRangeRandomSegmentThrowsInsteadOfSilentlyWrapping(int rand)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            BoardPolicy.NewSceneId(DateTimeOffset.UnixEpoch, rand));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("261017-07150-abcd")]          // 少一位秒
    [InlineData("261017-0715000abcd")]        // 多了位
    [InlineData("261017_071500_abcd")]         // 分隔符换掉
    [InlineData("261017-071500-gggg")]         // 随机段⛔ 是十六进制
    [InlineData("26101-071500-abcd")]          // 年段少一位
    [InlineData("261017-071500--123")]         // 随机段是"负号＋三位"：TryParse(HexNumber) 认符号
    [InlineData("261017-071500- ab")]         // 随机段是"空格＋两位"：同一颗 TryParse 认前后空白
    public void IdShapeRejectsEveryBrokenFormWeListed(string? id)
        => Assert.False(BoardPolicy.IsSceneId(id));

    // ────────── ②③ 一颗档一板，上一份另算 ──────────

    [Fact]
    public void SceneAndBackupNamesAreDerivedFromTheSameId()
    {
        Assert.Equal(Id + ".excalidraw", BoardPolicy.SceneFileName(Id));
        Assert.Equal(Id + ".excalidraw.bak", BoardPolicy.BackupFileName(Id));
        Assert.True(BoardPolicy.IsSceneFileName(BoardPolicy.SceneFileName(Id)));
        Assert.True(BoardPolicy.IsBackupFileName(BoardPolicy.BackupFileName(Id)));
        // 那颗 .bak ⛔ 许同时被认成板档：对账三分类会把一颗备份数成一颗板，"档在而板缺"就永远对不平
        Assert.False(BoardPolicy.IsSceneFileName(BoardPolicy.BackupFileName(Id)));
        Assert.False(BoardPolicy.IsBackupFileName(BoardPolicy.SceneFileName(Id)));
    }

    /// <summary>扩展名大小写⛔ 敏感（NTFS 上 <c>.EXCALIDRAW</c> 是真会出现的档名）；坏 id 直接抛。</summary>
    [Fact]
    public void ExtensionCaseTolerantWhileBadIdIsRefused()
    {
        // ⛔ 拿 `Id.ToUpperInvariant()` 当"大写档名"：那一颗根本没有后缀，判它 false 是判"没扩展名"，
        // 这一格会一直绿而永远测不到扩展名那一半（本批第一跑就是这么假绿了一次，红的是别处）。
        Assert.True(BoardPolicy.IsSceneFileName(BoardPolicy.SceneFileName(Id).ToUpperInvariant()));
        Assert.True(BoardPolicy.IsBackupFileName(BoardPolicy.BackupFileName(Id).ToUpperInvariant()));
        Assert.False(BoardPolicy.IsSceneFileName("notes.txt"));
        Assert.False(BoardPolicy.IsSceneFileName(Id));                 // 没后缀＝⛔ 是板档
        Assert.False(BoardPolicy.IsSceneFileName(null));
        Assert.Throws<ArgumentException>(() => BoardPolicy.SceneFileName("随手起的名"));
    }

    // ────────── ④ 四拍 autosave ──────────

    /// <summary>
    /// 落盘的四拍逐颗点名（v1 §21.3）。<b>那颗 <c>RenderProcessFailed</c> 是这一批新增的</b>：
    /// 崩溃之后重载若回到空板，用户看到的是"程序把我的图弄丢了"，所以那一拍必须与另外三拍同住判据、
    /// ⛔ 由 UI 侧在事件里临时决定。
    /// </summary>
    [Fact]
    public void ThereAreExactlyFourSaveBeatsAndTheyAreNamed()
    {
        Assert.Equal(
            ["Idle", "LosingFocus", "RenderProcessFailed", "ClosingWindow"],
            Enum.GetNames<BoardPolicy.SaveBeat>());
        Assert.Equal(1500, BoardPolicy.AutosaveIdleMs);
    }

    /// <summary>
    /// 四拍里<b>问得到</b>页面的只有两拍（片24＝§21.3）。<c>RenderProcessFailed</c> 那一拍问不到东西：
    /// 那一刻页面已经死了，发出去的 <c>export.pick</c> 没人接（§21.5 给它派的活是<b>恢复＋把见过那份递回去</b>）；
    /// <c>Idle</c> 本来就是页面自己发起的那一拍，宿主"问"它等于把话回头递给自己。
    /// <para>三颗节拍常数同住这一格＝本仓那条"量程收成一处"：改数字只许改在这里，UI 侧再写一遍数字＝第二主人。</para>
    /// </summary>
    [Fact]
    public void OnlyTheBeatsWithALivingPageCanAskForASnapshot()
    {
        Assert.False(BoardPolicy.BeatCanAskForSnapshot(BoardPolicy.SaveBeat.Idle));
        Assert.False(BoardPolicy.BeatCanAskForSnapshot(BoardPolicy.SaveBeat.RenderProcessFailed));
        Assert.True(BoardPolicy.BeatCanAskForSnapshot(BoardPolicy.SaveBeat.LosingFocus));
        Assert.True(BoardPolicy.BeatCanAskForSnapshot(BoardPolicy.SaveBeat.ClosingWindow));
        Assert.Equal(2, Enum.GetValues<BoardPolicy.SaveBeat>().Count(BoardPolicy.BeatCanAskForSnapshot));

        Assert.Equal(1500, BoardPolicy.AutosaveIdleMs);
        Assert.Equal(2000, BoardPolicy.CloseFlushBudgetMs);   // 来历＝§18.6 那颗往返表（1 MB 31 ms／8 MB 177 ms）
        Assert.Equal(3, BoardPolicy.MaxRecreatePerWindow);     // 自愈要有个数，⛔ 留一条不需要谁帮忙就会一直跑的循环
    }

    // ────────── ④b 那颗进程失效表（片24＝§21.5） ──────────

    /// <summary>
    /// 十颗档位<b>全点名</b>，名单是现读的：那颗 <c>Microsoft.Web.WebView2.Core.xml</c>（钉住的 1.0.3719.77）里
    /// <c>CoreWebView2ProcessFailedKind</c> 的 <c>F:</c> 成员就是这十颗——⛔ 靠记忆抄官方表（名册 299 那一族）。
    /// <para><b>这一格最值钱的那一行是 <c>FrameRenderProcessExited</c></b>：官方逐字写它"⛔ 影响顶层文档，
    /// 只有部分子帧换成错误页"，而重载＝把用户<b>整块板</b>退成错误页。"看见进程没了就重载"听起来稳妥，恰好是最不稳妥的那一颗。</para>
    /// </summary>
    private static readonly (string Kind, BoardPolicy.FailureRecovery Tier)[] SdkFailureTable =
    [
        ("BrowserProcessExited", BoardPolicy.FailureRecovery.RecreateWeb),
        ("RenderProcessExited", BoardPolicy.FailureRecovery.ReloadPage),
        ("RenderProcessUnresponsive", BoardPolicy.FailureRecovery.ReportOnly),
        ("FrameRenderProcessExited", BoardPolicy.FailureRecovery.ReportOnly),
        ("UtilityProcessExited", BoardPolicy.FailureRecovery.ReportOnly),
        ("SandboxHelperProcessExited", BoardPolicy.FailureRecovery.ReportOnly),
        ("GpuProcessExited", BoardPolicy.FailureRecovery.ReportOnly),
        ("PpapiPluginProcessExited", BoardPolicy.FailureRecovery.ReportOnly),
        ("PpapiBrokerProcessExited", BoardPolicy.FailureRecovery.ReportOnly),
        ("UnknownProcessExited", BoardPolicy.FailureRecovery.ReportOnly),
    ];

    public static System.Collections.Generic.IEnumerable<object[]> SdkFailureRows()
        => SdkFailureTable.Select(r => new object[] { r.Kind, r.Tier });

    [Theory]
    [MemberData(nameof(SdkFailureRows))]
    public void EachProcessFailureKindPaysTheTierTheOfficialTableSays(
        string kind, BoardPolicy.FailureRecovery expected)
    {
        Assert.Equal(expected, BoardPolicy.RecoveryForFailure(kind));
    }

    [Fact]
    public void OnlyTwoKindsInThePinnedTableAreAllowedToDoAnything()
    {
        // 会动手的档位只许两颗：官方表里只有那两档写了"要重载／要重建"，其余八档要么自己起得来、要么动不得。
        // ⚠ 这一格数的是<b>那颗判据当场交回来的东西</b>，⛔ 我自己那份名单里写的档位——台架 r1 撞出来的：
        // 数自己的字面量＝那颗函数怎么改这一格都⛔ 响（一颗恒绿断言；本仓"自己数自己"那一族的又一副脸）。
        Assert.Equal(10, SdkFailureTable.Length);
        var tiers = SdkFailureTable.Select(r => BoardPolicy.RecoveryForFailure(r.Kind)).ToList();
        Assert.Equal(2, tiers.Count(t => t != BoardPolicy.FailureRecovery.ReportOnly));
        Assert.Equal(8, tiers.Count(t => t == BoardPolicy.FailureRecovery.ReportOnly));
    }

    /// <summary>
    /// 名单<b>外</b>的那颗名字退到"只上报"。这一条是这颗 <c>switch</c> 的兜底分支的方向：
    /// 那颗表将来多出一档时，默认动作必须是"什么也⛔ 毁"，而⛔ 是"先重载再说"——
    /// 猜错的代价落在用户画的那块板上，⛔ 落在日志里。
    /// </summary>
    [Fact]
    public void AnUnlistedProcessFailureIsNeverTreatedAsReloadable()
    {
        Assert.Equal(BoardPolicy.FailureRecovery.ReportOnly, BoardPolicy.RecoveryForFailure("WebView2ProcessExited"));
        Assert.Equal(BoardPolicy.FailureRecovery.ReportOnly, BoardPolicy.RecoveryForFailure("browserprocessexited"));
        Assert.Equal(BoardPolicy.FailureRecovery.ReportOnly, BoardPolicy.RecoveryForFailure(""));
    }

    // ────────── ⑤⑥⑦ 撤销立场 ──────────

    /// <summary>
    /// 载档与桥回写＝<c>NEVER</c>，用户动作＝<c>IMMEDIATELY</c>，且<b>三档里⛔ 一颗回成包默认那颗 <c>EVENTUALLY</c></b>。
    /// <para>那颗 <c>switch</c> 的兜底分支故意也回 <c>NEVER</c>：来源名字来路不明时，
    /// 宁可少进一次撤销栈，也⛔ 把用户的图退成空白。</para>
    /// </summary>
    [Fact]
    public void LoadsAndWriteBacksNeverEnterTheUndoStack()
    {
        Assert.Equal("IMMEDIATELY", BoardPolicy.CaptureFor(BoardPolicy.SceneOrigin.UserEdit));
        Assert.Equal("NEVER", BoardPolicy.CaptureFor(BoardPolicy.SceneOrigin.SceneLoad));
        Assert.Equal("NEVER", BoardPolicy.CaptureFor(BoardPolicy.SceneOrigin.BridgeWriteBack));
        Assert.Equal("NEVER", BoardPolicy.CaptureFor((BoardPolicy.SceneOrigin)99));

        // 那颗包自己的默认仍逐字在册——⛔ 靠它就是那颗缺陷的形状
        Assert.Equal("EVENTUALLY", BoardPolicy.Capture.Eventualy);
        var outcomes = Enum.GetValues<BoardPolicy.SceneOrigin>().Select(BoardPolicy.CaptureFor).ToArray();
        Assert.Equal(3, outcomes.Length);
        Assert.DoesNotContain(BoardPolicy.Capture.Eventualy, outcomes);
    }

    // ────────── ⑧ 两颗引擎读数⛔ 是我们的上限 ──────────

    [Fact]
    public void EngineNumbersAreRecordedVerbatimAndFlaggedAsNotOurs()
    {
        Assert.Equal(1440, BoardPolicy.EngineReportedLongEdgePx);
        Assert.Equal(4L * 1024 * 1024, BoardPolicy.EngineReportedFileCeilingBytes);
        Assert.False(BoardPolicy.ImageCeilingsMeasured);        // 那条曲线量到之前，这颗旗⛔ 许翻
    }

    // ────────── ⑨ 那颗脏位＝字节对差（片22＝§21.4） ──────────

    /// <summary>
    /// 「盘上已经有了」只由<b>字节逐颗相等</b>说了算：⛔ 计数器、⛔ 时间戳、⛔ 让 JS 递一颗 bool（§21.4 那三句禁令）。
    /// <para>这颗判据的值不在"少写一次盘"（那是性能），在<b>退路</b>：每写一次就滚一次 <c>.bak</c>，
    /// 而一次没改动的失焦把上一份<b>真正不同</b>的 <c>.bak</c> 顶成同件——用户按 Alt+Tab 出去倒杯水的代价。</para>
    /// <para>正件要落在我要测的那一维（名册 349）：同内容的<b>两颗不同数组</b>才算"字节对差"，
    /// 拿同一颗数组自比只能证 <c>==</c>，证不到 <c>SequenceEqual</c>。</para>
    /// </summary>
    [Fact]
    public void TheDirtyBitIsAByteDiffAndAbsenceMeansWrite()
    {
        var scene = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"excalidraw\",\"version\":2}");
        var sameBytesDifferentArray = (byte[])scene.Clone();

        Assert.True(BoardPolicy.DiskAlreadyHolds(scene, sameBytesDifferentArray));
        Assert.False(BoardPolicy.DiskAlreadyHolds(null, scene));            // 还没写过＝必写

        var oneTailByteDiffers = (byte[])scene.Clone();
        oneTailByteDiffers[^1] = (byte)'!';
        Assert.False(BoardPolicy.DiskAlreadyHolds(scene, oneTailByteDiffers));

        Assert.False(BoardPolicy.DiskAlreadyHolds(scene, scene.Append((byte)' ').ToArray()));  // 长度不同
        Assert.False(BoardPolicy.DiskAlreadyHolds(scene, scene[..^1]));                         // 少一颗也算不同
    }

    // ────────── ⑧ 载档那一拍那三颗数（片26＝P-223 的开闸判据） ──────────

    /// <summary>
    /// "这份档里有几颗<b>会被写回盘上的</b>元素"<b>认不出就交 null</b>，⛔ 折成一颗 0——0 读起来是"这块板是空的"，
    /// 而它真正的意思是"我读不懂"，把后者当后者用＝拿一次解析失败去判"用户把板画没了"。
    /// <para><b>这颗数的口径＝本格的全部意义</b>：它⛔ 是"档里数组的长度"，而是"页面那颗序列化器交回的那一堆"——
    /// 现读 bundle：<c>getSceneElements</c> 走 <c>scene.getNonDeletedElements()</c>（<c>isDeleted</c> 那颗被滤掉），
    /// 所以这里也跳掉 <c>isDeleted:true</c>。片26 第一版按"数组整颗长度"数、页面那颗画布数取的是
    /// <c>getSceneElementsIncludingDeleted</c>，崩后重载那一跑三颗读数报出来"全等"（4／4／4）而盘上那块板
    /// 照样被盖成 0 元素＝<b>两颗⛔ 同口径的数比 equality，闸门看着在挡、其实⛔ 挡任何东西</b>（坑表）。</para>
    /// <para>正件按名册 349 落在我要测的那一维：真形状那份（两颗 freedraw、一颗标着删）必须数回 <b>1</b>，
    /// 缺 <c>isDeleted</c> 那颗⛔ 算删。</para>
    /// </summary>
    [Fact]
    public void SceneElementCountsWhatWouldBeWrittenBackAndGivesUpWhenItCannotRead()
    {
        var twoFreedrawsOneDeleted = Utf8("{\"type\":\"excalidraw\",\"version\":2,\"source\":\"https://starmark.local\","
            + "\"elements\":["
            + "{\"id\":\"A1\",\"type\":\"freedraw\",\"x\":12,\"y\":34,\"points\":[[0,0],[3,5],[9,11]],\"isDeleted\":false},"
            + "{\"id\":\"B2\",\"type\":\"freedraw\",\"x\":88,\"y\":99,\"points\":[[0,0]],\"isDeleted\":true}"
            + "],\"appState\":{\"gridModeEnabled\":false},\"files\":{}}");
        var noneDeleted = Utf8("{\"elements\":[{\"id\":\"A1\",\"type\":\"freedraw\",\"isDeleted\":false},"
            + "{\"id\":\"B2\",\"type\":\"freedraw\"}]}");

        Assert.Equal(1, BoardPolicy.CountWritableElements(twoFreedrawsOneDeleted));  // 那颗标了删的进不了档
        Assert.Equal(2, BoardPolicy.CountWritableElements(noneDeleted));              // ⛔ isDeleted 那颗⛔ 算删
        Assert.Equal(1, BoardPolicy.CountWritableElements(nestedOne()));             // 嵌套⛔ 数进去，数的是外层
        Assert.Equal(0, BoardPolicy.CountWritableElements(Utf8("{\"elements\":[]}")));  // 空板＝一颗，而⛔ "读不懂"

        Assert.Null(BoardPolicy.CountWritableElements(Utf8("{\"appState\":{}}")));        // ⛔ 这颗键
        Assert.Null(BoardPolicy.CountWritableElements(Utf8("{\"elements\":{\"a\":1}}"))); // 有这颗键而⛔ 是数组
        Assert.Null(BoardPolicy.CountWritableElements(Utf8("[1,2,3]")));                  // 根⛔ 是对象
        Assert.Null(BoardPolicy.CountWritableElements(Utf8("{\"elements\":[")));          // 半截档（写坏的那一颗形状）
        Assert.Null(BoardPolicy.CountWritableElements(Array.Empty<byte>()));
        Assert.Null(BoardPolicy.CountWritableElements(null));
    }

    /// <summary>
    /// 开闸要<b>三颗数全等</b>：⛔ "对不上就先信页面"——猜错的代价是拿一块空板盖掉盘上那块好档
    /// （片26 真机量到：崩后重载那一趟页面照实回了话，而盘上那块 4 元素／4936 字节的板被盖成 0 元素／241 字节）。
    /// <para>任一颗认不出（null）也⛔ 开闸：那是"读不懂"，而读不懂时唯一安全的动作是不写。</para>
    /// </summary>
    [Fact]
    public void TheArmWordNeedsAllThreeCountsToAgree()
    {
        Assert.True(BoardPolicy.ShouldArmAfterLoad(2, 2, 2));
        Assert.True(BoardPolicy.ShouldArmAfterLoad(0, 0, 0));      // 空板也是"回到了那块板"，⛔ 因此⛔ 写

        Assert.False(BoardPolicy.ShouldArmAfterLoad(4, 4, 0));     // 片26 那一跑的脸：档里 4 颗可写，画布交不出可写的
        Assert.False(BoardPolicy.ShouldArmAfterLoad(2, 0, 0));     // 档自己也没读到（loadFromBlob 交回空）
        Assert.False(BoardPolicy.ShouldArmAfterLoad(2, 1, 2));     // 中间那颗少一颗也⛔ 开
        Assert.False(BoardPolicy.ShouldArmAfterLoad(2, 2, -1));    // 页面数不出来＝认不出
        Assert.False(BoardPolicy.ShouldArmAfterLoad(null, 2, 2));  // 宿主读不懂自己那份字节
        Assert.False(BoardPolicy.ShouldArmAfterLoad(2, null, 2));
        Assert.False(BoardPolicy.ShouldArmAfterLoad(2, 2, null));
        // ⚠ 这一颗是台架逼出来的（A3「null 当成 0 也开闸」第一版没把它撞红）：另两颗都数到 0 时，
        // "宿主读不懂自己那份字节"与"递进去的是块空板"读起来一模一样——把 null 折成 0 就分不出这两件事，
        // 而后者才许写。认不出＝⛔ 开闸这一条必须<b>在另两颗全等时也照样成立</b>。
        Assert.False(BoardPolicy.ShouldArmAfterLoad(null, 0, 0));
    }

    /// <summary>
    /// 拒过一次的档还许不许<b>再递一次</b>（片27＝P-224 那半句自愈的额度）。
    /// <para><b>额度为什么是 1 而⛔ 更多</b>：片26 现场里"再递一次就好"的形状＝一次性时序故障，
    /// 递两次与递一次在这一档上等价；而 2、3、4 圈是那条<b>不需要谁帮忙就会一直跑</b>的循环
    /// （同一块空画布反复刷给用户看，每一圈还往盘上问一次）。同一颗口径住在 <see cref="MaxRecreatePerWindow"/> 那一族：
    /// 给一次改错的机会，再多就没有一颗自愈能修的东西了。</para>
    /// <para><b>认不出的那颗数（负）＝⛔ 再递</b>：读不懂时唯一安全的动作是停下来开口，而不是继续试。
    /// 这一档与 <see cref="ShouldRetryArm"/> 的"越界也绿"是两种坏：把 -1 当 0 会多递一次，
    /// 把 5 当"还没到 1"会让那一圈永远跑下去。</para>
    /// </summary>
    [Fact]
    public void OneRetryOnlyAndAnUnrecognisedCountDoesNotBuyAnotherTrip()
    {
        Assert.Equal(1, BoardPolicy.ArmRetryBudget);
        Assert.True(BoardPolicy.ShouldRetryArm(0));            // 第一次拒：还有额度，自己再递一次
        Assert.False(BoardPolicy.ShouldRetryArm(1));           // 额度用尽：这一趟⛔ 再递，该开口了
        Assert.False(BoardPolicy.ShouldRetryArm(2));           // 越界⛔ 绕回真
        Assert.False(BoardPolicy.ShouldRetryArm(int.MaxValue));
        Assert.False(BoardPolicy.ShouldRetryArm(-1));          // 认不出＝⛔ 再递（与"读不懂就不写"同一颗口径）
    }

    /// <summary>片33 两臂成对量到的那一格钉成判据：那颗「另存一份到别处」到底交哪一份。
    /// <para>真读数：闸开那一臂同一双腿把主档推成 3 颗可写元素（2809 字节）；闸关那一臂里那一笔只走到
    /// <c>boards.scene.write.refused notArmed bytes=2810</c> 就被退回，而那颗按钮当时交出去的是 2 颗／858 字节
    /// ＝载入那一份，屏幕上却念「已另存到 &lt;路径&gt;」。少的那一格正是用户刚画上去的那一笔。</para>
    /// <para>所以那一颗可疑字节只有一种资格：<b>数得出颗数、那颗数⛔ 是零、而且⛔ 比手里那份少</b>。
    /// 空画布（P-223 那颗形状：2608 字节／2 元素→241 字节／0 元素）与读不懂的形状一律输——
    /// 把白屏当救命档递出去，比少一笔更坏。同数也交屏幕上那一份：那是用户看得见的那一块。</para></summary>
    [Theory]
    [InlineData(0, 2, false)]     // 空画布 vs 手里两颗：交⛔ 那块
    [InlineData(1, 0, true)]      // 手里那份读不出颗数，而屏幕有一颗：交屏幕上那块
    [InlineData(2, 2, true)]      // 同数＝屏幕上那块
    [InlineData(1, 2, false)]     // 屏幕上少了一颗（多半⛔ 回到那块板）：交⛔ 那块
    [InlineData(3, 2, true)]      // 面板起来之后画的那一笔：这一颗才配救
    public void TheRescueCopyTakesTheScreenSceneOnlyWhenItIsNotShorterThanWhatWeHold(int screen, int loaded, bool want)
        => Assert.Equal(want, BoardPolicy.ScreenSceneIsWorthSaving(Scene(loaded), Scene(screen)));

    /// <summary>读不懂／空的那一颗一律⛔ 配当救生档：交出去那一份必须数得出颗数（⛔ 把 null 折成 0 再去赢）。</summary>
    [Fact]
    public void TheRescueCopyRefusesASceneItCannotCount()
    {
        Assert.False(BoardPolicy.ScreenSceneIsWorthSaving(Scene(2), null));
        Assert.False(BoardPolicy.ScreenSceneIsWorthSaving(Scene(2), Utf8("")));
        Assert.False(BoardPolicy.ScreenSceneIsWorthSaving(Scene(2), Utf8("not json")));
        Assert.False(BoardPolicy.ScreenSceneIsWorthSaving(Scene(2), Utf8("{\"elements\":null}")));
        Assert.True(BoardPolicy.ScreenSceneIsWorthSaving(null, Scene(1)));       // 手里⛔ 有档＝屏幕上那颗是唯一候选
    }

    /// <summary>那颗场景的夹具：n 颗<b>没被标删</b>的可写元素（与 <c>CountWritableElements</c> 同口径）。</summary>
    private static byte[] Scene(int n)
    {
        var items = "";
        for (var i = 0; i < n; i++) items += (i == 0 ? "" : ",") + "{\"isDeleted\":false}";
        return Utf8("{\"elements\":[" + items + "]}");
    }

    private static byte[] Utf8(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    /// <summary>
    /// 那颗"另存一份到别处"问完系统之后的三档（片28 的真机读数逼出来的口径）。
    /// <para>坏的形状是<b>两种 null 走同一条兜底路</b>：用户在那颗框里点【取消】，交回的是 <c>null</c>，
    /// 宿主那颗框起不来时交回的也是 <c>null</c>（或干脆抛）——旧代码对这两样一律退到导出目录，
    /// 于是"取消"也写出一份 858 字节的档，屏幕上还念一句"已另存到 …"。
    /// 这一侧唯一分得开的信号是<b>它有没有抛</b>，所以那颗判据住这里而⛔ 写在 UI 的 if 里。</para>
    /// </summary>
    [Fact]
    public void ADeclinedSaveDialogIsNotTreatedAsAnUnavailableOne()
    {
        Assert.Equal(BoardPolicy.CopyPick.Chosen, BoardPolicy.ClassifyCopyPick(false, "D:\\a.excalidraw"));
        Assert.Equal(BoardPolicy.CopyPick.Declined, BoardPolicy.ClassifyCopyPick(false, null));
        Assert.Equal(BoardPolicy.CopyPick.Unavailable, BoardPolicy.ClassifyCopyPick(true, null));
        // 两颗信号同时到（⛔ 真会撞见）：抛了就说那颗框没活成，⛔ 因为"手里还有一颗路径"就把它读成用户挑的
        Assert.Equal(BoardPolicy.CopyPick.Unavailable, BoardPolicy.ClassifyCopyPick(true, "D:\\a.excalidraw"));
        Assert.False(BoardPolicy.ShouldWriteCopy(BoardPolicy.CopyPick.Declined));   // 正件：取消＝⛔ 写
        Assert.True(BoardPolicy.ShouldWriteCopy(BoardPolicy.CopyPick.Chosen));
        Assert.True(BoardPolicy.ShouldWriteCopy(BoardPolicy.CopyPick.Unavailable));  // 框起不来仍要有出口（P-54）
    }

    /// <summary>外层一颗、里面套三颗——这颗形状用来钉"数的是外层"。</summary>
    private static byte[] nestedOne() => Utf8("{\"elements\":[{\"elements\":[1,2,3]}]}");
}
