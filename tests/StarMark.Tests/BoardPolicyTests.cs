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
}
