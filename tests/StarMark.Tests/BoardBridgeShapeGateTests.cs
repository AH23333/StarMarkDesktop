#nullable enable
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Core.Boards;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 画板那一族的<b>写法</b>闸门（批次 Phase C 片12）：钉的是"这件事只有这一颗主人"、"我们还没给自己发明数字"、
/// 以及"每颗故障名都有生产者"。
/// <para>
/// 行为由 <see cref="BoardBridgeProtocolTests"/> 与 <see cref="BoardPolicyTests"/> 真测；这里四格都是形状：
/// ① 保留名表⛔ 许被抄第二份（抄了就会有两套"合法档名"互相认不出）；
/// ② 桥的名字只许住在一颗文件里（接线批若把命令名抄进 UI，越权面就有两处可改）；
/// ③ 那颗"我们自定的图片字节／长边上限"<b>还没量</b>，所以判据里⛔ 许出现它——出现即红（防把猜测当判据）；
/// ④ <c>Fault</c> 的每颗成员都得真被 <c>Check</c> 返回过（本批删掉的就是这颗形状：一句"外链被拒"的故障名
/// 从无生产者，而 UI 那边读不到它＝P-212 那一族在同一批里又长了一颗）。
/// </para>
/// </summary>
public sealed class BoardBridgeShapeGateTests
{
    private const string JudgeFile = "src/StarMark.Core/Boards/BoardPolicy.cs";
    private const string BridgeFile = "src/StarMark.Core/Boards/BridgeProtocol.cs";
    private const string DeviceJudge = "src/StarMark.Core/Files/DeviceNamePolicy.cs";
    private const string BackupJudge = "src/StarMark.Core/Backup/BackupPathPolicy.cs";
    private const string ManifestJudge = "src/StarMark.Core/Updates/UpdateManifestCodec.cs";

    // ────────── ① 设备名判据一颗主人，三处读者 ──────────

    /// <summary>
    /// 那颗"Windows 设备名⛔ 许当文件名"的判断<b>只许有一份成员表</b>；三处读者（备份输入框、更新清单、
    /// 画板导出名）都读它。
    /// <para><b>本批并掉的是一颗既有二主</b>：备份那一侧原先自带一张 22 行的表且<b>⛔ 认 <c>CLOCK$</c></b>，
    /// 而更新清单那一侧按算法认——同一件事两套"合法"，前者的形状正是它自己注释写的那句
    /// （"能通过检查、却在真正写盘时以奇怪方式失败"）。成员一颗字面都不许漏到读者文件里。</para>
    /// <para>清单那一档读<b>两次</b>是刻意的：包名与解包路径是两颗入口，而它们此前口径不同——
    /// 包名那一格把整颗 <c>xxx.zip</c> 交给判据（永远比不中＝那一格从没判过），路径那一格按主干判。</para>
    /// </summary>
    [Fact]
    public void TheDeviceNameJudgeHasOneOwnerAndThreeReaders()
    {
        var probe = "\"CLOCK$\"";
        var holders = ReadRepoUnder("src")
            .Where(f => f.Text.Contains(probe, System.StringComparison.Ordinal))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToList();
        Assert.Equal([DeviceJudge], holders);

        // 三处读者各读它（读不到＝那颗表又被人抄回去／或者这条闸门圈错了文件）
        Assert.Equal(1, Count(Code(BackupJudge), "DeviceNamePolicy.IsReservedStem"));
        Assert.Equal(2, Count(Code(ManifestJudge), "DeviceNamePolicy.IsReservedStem"));
        Assert.Equal(1, Count(Code(BridgeFile), "DeviceNamePolicy.IsReservedStem"));
        // 读者⛔ 许再带成员表
        foreach (var reader in new[] { BackupJudge, ManifestJudge, BridgeFile })
            Assert.DoesNotContain(probe, Code(reader), System.StringComparison.Ordinal);
    }

    // ────────── ② 桥的名字一颗主人 ──────────

    /// <summary>
    /// 六颗进向命令的名字全仓只许两处：<c>BridgeProtocol.cs</c>（名单本体）＋ <c>BoardWindow.cs</c>
    /// （已接线的分发 switch——C# 的 case 标签要常量字面串，接线批片19 起它就是第二颗合法持有者）。
    /// <para>第三处出现＝有人又抄了一份名单，⛔ 容（UI 侧⛔ 第二份名单另由
    /// <c>BoardWindowBridgeGateTests</c> 钉：switch 里⛔ 出现成颗的名字数组）。</para>
    /// </summary>
    [Fact]
    public void BridgeNamesLiveInExactlyOneSourceFile()
    {
        var probe = "bridge.ready";
        var holders = ReadRepoUnder("src")
            .Where(f => f.Text.Contains(probe, System.StringComparison.Ordinal))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToList();

        // 两颗合法持有者：名单本体（Core）＋已接线的分发 switch（UI BoardWindow，片19 起）。
        Assert.Equal([BridgeFile, "src/StarMark.UI/Boards/BoardWindow.cs"], holders);
        Assert.Equal(1, Count(Code(BridgeFile), "public static readonly string[] InboundCommands"));
        Assert.Equal(1, Count(Code(BridgeFile), "public static readonly string[] OutboundEvents"));
    }

    // ────────── ③ 还没量的东西⛔ 许先发明 ──────────

    /// <summary>
    /// <c>BoardPolicy.cs</c> 里<b>⛔ 一颗"我们自己给的图片上限"</b>。
    /// <para>那颗包里现读的两颗（1440 长边／4 MB 字节）名字都以 <c>EngineReported</c> 开头——它们是
    /// <b>引擎那侧的读数</b>，写在判据里只为排障对账（§19.3 那句"too big 其实可能是解码失败"就靠它对上号）。
    /// 而"宿主自己缩到多大、内嵌 base64 之后一颗板档允许多大"这条曲线<b>还没量</b>（v1 §21.3／§21.7）：
    /// 先写一颗常数＝把猜测当判据，也是本仓定性过两次的那类坏（<c>TextScale</c> 那句"范围建议 0.7–1.5"⛔ 存在过）。</para>
    /// <para>翻这条红法⛔ 是删掉 <c>ImageCeilingsMeasured</c> 那颗旗（下面同时钉它还在）：
    /// 唯一出路是把 §19.5 第②臂那条曲线量出来，再带着数字来改这条。</para>
    /// </summary>
    [Fact]
    public void NoSelfInventedImageCeilingExistsYet()
    {
        var code = Code(JudgeFile);
        var offenders = Regex.Matches(code,
                @"public const (int|long) (?<n>(?!(?:EngineReported))\w*(Ceiling|Limit|Budget|Max|Min|Floor|Cap)\w*)\s*=")
            .Select(m => m.Groups["n"].Value).ToList();

        Assert.True(offenders.Count == 0,
            "那颗⛔ 还没量的上限被写成了常数（先量再写）：\n" + string.Join("\n", offenders));

        // 两颗引擎读数照旧在，且名字带来源（⛔ 改名成我们的样子）
        Assert.Equal(2, Regex.Matches(code, @"public const (int|long) EngineReported\w+").Count);
        Assert.Contains("ImageCeilingsMeasured = false", code, System.StringComparison.Ordinal);
    }

    // ────────── ④ 每颗故障名都有生产者 ──────────

    /// <summary>
    /// <c>Fault</c> 的每颗成员都必须在判据里被<b>返回过</b>（⛔ 只是声明）。
    /// <para>为什么单独钉：那五颗名字是 UI 侧挑词的唯一索引——名字在册却⛔ 有生产者，就是"注释承诺了一个
    /// 不存在的消费者"那一族（批次 WI-80 立 P-212、本批在同一颗 enum 上又撞出一颗，只是规模小得多）。
    /// 读屏上永远不会出现那句话，而人以为那条拒有出口。</para>
    /// <para>反空转（#161）：⛔ 只数"名字数对"，这里逐颗找读者；<c>Check</c> 里若整段 return 被删光，
    /// 每颗名字都会变成孤儿而这一格当场红。</para>
    /// </summary>
    [Fact]
    public void EveryFaultNameHasAProducerInTheJudge()
    {
        var code = Code(BridgeFile);
        var names = System.Enum.GetNames<BridgeProtocol.Fault>();
        var orphans = names.Where(n => !code.Contains("Fault." + n, System.StringComparison.Ordinal)).ToList();

        Assert.True(orphans.Count == 0,
            "这些故障名⛔ 被 Check 返回过（要么补生产者，要么把名字删掉）：\n" + string.Join("\n", orphans));
        // 正面计数：在册的五颗各有恰好一处 return（多一处＝同一颗故障有两条出口，UI 挑词会分岔）
        Assert.Equal(5, names.Length);
        Assert.Equal(5, names.Count(n => Count(code, "Fault." + n + ")") == 1));
    }

    private static string Code(string relativePath)
        => FormatScanner.Scan(ReadRepoFile(relativePath)).Code;      // 注释里的举例⛔ 算违规（#190 那一族）
}
