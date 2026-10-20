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
/// ③ 我们自己给的任何尺寸上限都得<b>带一句真存在的出处</b>（片58 之前那颗曲线还没量，那颗闸门问的是"⛔ 许有"）；
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
    /// <para><b>片24 改的是问法而⛔ 放宽</b>：这一列原来读<b>原文</b>，于是档里一句解释（"崩那一拍等
    /// <c>bridge.ready</c> 再来"）也被数成一颗持有者——那把尺报的"谁抄了名单"其实由语法形状说了算，⛔ 由
    /// "谁有资格持有"说了算（名册 347）。现在读 <c>SourceGate.Code</c>（抹注释、留字面量），并<b>自带正件</b>：
    /// 同一批里确有颗档在注释里写了那颗名字（<c>BoardPolicy.cs</c>），原文读得到而代码行读不到——
    /// 少了这一对，这一格就分不清"尺准了"与"尺瞎了"（名册 409）。</para>
    /// </summary>
    [Fact]
    public void BridgeNamesLiveInExactlyOneSourceFile()
    {
        var probe = "bridge.ready";
        var everyFile = ReadRepoUnder("src").ToList();
        var codeHolders = everyFile
            .Where(f => SourceGate.Code(f.Text).Contains(probe, System.StringComparison.Ordinal))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToList();
        var rawHolders = everyFile
            .Where(f => f.Text.Contains(probe, System.StringComparison.Ordinal))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToList();

        // 两颗合法持有者：名单本体（Core）＋已接线的分发 switch（UI BoardWindow，片19 起）。
        Assert.Equal([BridgeFile, "src/StarMark.UI/Boards/BoardWindow.cs"], codeHolders);
        Assert.Equal(1, Count(Code(BridgeFile), "public static readonly string[] InboundCommands"));
        Assert.Equal(1, Count(Code(BridgeFile), "public static readonly string[] OutboundEvents"));
        // 正件：注释里那颗名字的写法在语料里真存在（片24＝BoardPolicy 那句），而它⛔ 算持有者
        Assert.Contains("src/StarMark.Core/Boards/BoardPolicy.cs", rawHolders);
        Assert.DoesNotContain("src/StarMark.Core/Boards/BoardPolicy.cs", codeHolders);
    }

    // ────────── ③ 自给的上限必须带着出处（片58 之前：⛔ 许有；之后：⛔ 许⛔ 带读数） ──────────

    /// <summary>
    /// <c>BoardPolicy.cs</c> 里每一颗<b>我们自己给</b>的尺寸上限，都必须带一句「读数出自哪一份正本」，
    /// 而那颗正本<b>真在盘上</b>。
    /// <para>片12 立这一格时它问的是「还没量的东西⛔ 许先发明」（名单里一颗也⛔ 许有）；片58 把那条曲线量出来了，
    /// 于是问法换成它<b>一直想问的那一句</b>：数字得有出处。两颗引擎自报的读数（1440 长边／4 MB 字节）照旧
    /// 以 <c>EngineReported</c> 命名并被排除在外——它们是<b>对方</b>的读数，写在判据里只为排障对账。</para>
    /// <para>⚠ 这一格⛔ 再钉 <c>ImageCeilingsMeasured == false</c>：那颗旗今天翻成 true，而它仍是这颗常数的
    /// 资格闸门（<c>BoardPolicyTests</c> 那一格现在钉 <c>True</c>，⛔ 允许谁偷偷翻回去）。
    /// 删掉那颗旗⛔ 是出路——出路是给那颗常数配一句真存在的出处（名册 425：禁项判据必须自带正件）。</para>
    /// </summary>
    [Fact]
    public void EverySelfGivenCeilingCitesAReadingThatExists()
    {
        var raw = ReadRepoFile(JudgeFile);      // 读<b>原文</b>：那颗出处写在常数的注释里，抹掉注释就把它一起删了
        var code = Code(JudgeFile);             // 抹掉注释的那一份只用来抓<b>声明</b>，出处要到原文里找
        var offenders = CeilingNames(code);
        // 反空转（名册 444／451）：名单空＝这把尺今天量不到任何东西，得响，而⛔ 是"绿灯"
        Assert.True(offenders.Count >= 1,
            "本档一颗自给上限也没有＝这颗尺瞎了（片58 起 SceneBytesCeilingBytes 应在名单里）");
        foreach (var name in offenders)
        {
            // 出处只许写在<b>那一颗常数自己那块注释</b>里：取声明之上连着的 <c>///</c> 那一整块，
            // ⛔ 用"往上数 N 颗字符"那种窗口（上一颗常数的出处会替它说话＝射程宽过那一枚，名册 424／461）
            var lines = raw.Replace("\r\n", "\n").Split('\n');
            var at = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\s*public const (int|long) " + name + @"\s*=")) continue;
                at = i;
                break;
            }
            Assert.True(at >= 0, $"找不到那颗上限的声明：{name}");
            var head = new List<string>();
            for (var j = at - 1; j >= 0 && lines[j].TrimStart().StartsWith("///", System.StringComparison.Ordinal); j--)
                head.Insert(0, lines[j]);
            Assert.True(head.Count > 0, $"{name} 连一块注释也没有＝那颗数压根没出处");
            var cited = Regex.Matches(string.Join("\n", head), @"bin/WI\d+/[\w.\-]*\*?[\w.\-]*\.txt")
                .Select(m => m.Value).ToList();
            Assert.True(cited.Count > 0, $"{name} 那颗数没写出处（正本档名）＝又回到「把猜测当判据」那一族");
            Assert.True(cited.Any(ReadingExists),
                $"{name} 写的出处在盘上找不到：{string.Join("／", cited)}");
        }
        // 两颗引擎读数照旧在，且名字带来源（⛔ 改名成我们的样子＝把它们冒充成我们量过的）
        Assert.Equal(2, Regex.Matches(code, @"public const (int|long) EngineReported\w+").Count);
        Assert.Contains("ImageCeilingsMeasured = true", code, System.StringComparison.Ordinal);
    }

    /// <summary>那颗正本（档名里可带 <c>*</c>）在盘上有没有——「出处」⛔ 是一句写着的假话。</summary>
    private static bool ReadingExists(string relative)
    {
        var path = relative.Replace('/', System.IO.Path.DirectorySeparatorChar);
        var root = RepoRoot();
        if (!path.Contains('*')) return System.IO.File.Exists(System.IO.Path.Combine(root, path));
        var dir = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        var pattern = System.IO.Path.GetFileName(path);
        var full = System.IO.Path.Combine(root, dir);
        return System.IO.Directory.Exists(full) && System.IO.Directory.GetFiles(full, pattern).Length > 0;
    }

    /// <summary>
    /// 这颗尺问的是「<b>那颗⛔ 还没量的尺寸</b>被写成常数了没」，⛔ 是「名字里带 Budget/Max 的常数一律越权」。
    /// <para>片24 撞出来的那处误伤：那两颗新常数（<c>CloseFlushBudgetMs</c>＝由 §18.6 那颗实测往返表换算的等待上限、
    /// <c>MaxRecreatePerWindow</c>＝重试要有个数）都⛔ 是"一颗档 / 一张图多大"那一族的东西，却按名字形状被抓。
    /// 用语法形状代替"谁有资格说"＝名册 347 那颗坑，而把它照抄放行⛔ 是答案——要改的是<b>问法</b>。</para>
    /// <para>新问法要同时带一颗<b>尺寸</b>词（Image／File／Bytes／Size／Edge／Px／Board／Scene）与一颗<b>档位</b>词。
    /// 下面那对正件／负件就是这一格没瞎的证据：两种写法的图片上限必须都被抓到（名册 409），
    /// 而片24 那两颗必须⛔ 被抓（否则这一格只是把误伤换了个方向）。</para>
    /// </summary>
    [Fact]
    public void TheCeilingRulerAsksAboutSizesAndProvesItSeesThem()
    {
        // 正件：那颗"我们自己的图片上限"的两种写法（尺寸在前／档位在前）各造一颗，必须都响
        Assert.Equal(
            ["ImageBytesCeiling", "MaxSceneFileBytes"],
            CeilingNames("public const int ImageBytesCeiling = 4;\npublic const long MaxSceneFileBytes = 5;"));
        // 负件：片24 那两颗⛔ 属于"还没量的尺寸"那一族，它们各有自己的实测／理由出处
        Assert.Empty(CeilingNames(
            "public const int CloseFlushBudgetMs = 2000;\npublic const int MaxRecreatePerWindow = 3;"));
    }

    private static List<string> CeilingNames(string code)
        => Regex.Matches(code,
                @"public const (int|long) (?<n>(?!(?:EngineReported))(?=\w*(?:Ceiling|Limit|Budget|Max|Min|Floor|Cap))(?=\w*(?:Image|File|Bytes|Size|Edge|Px|Board|Scene))\w+)\s*=")
            .Select(m => m.Groups["n"].Value).ToList();

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
