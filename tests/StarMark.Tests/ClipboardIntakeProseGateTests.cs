#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「图片采集」状态行只许有一个主人（批次 SE，P-123 清单 #7）。
/// <para>判据住在 <c>Abstractions</c> ⇒ 那句话的内容由 <see cref="ClipboardImageIntakeTextTests"/> 真测；
/// 这里只钉<b>"界面不许再自己写一遍"</b>与<b>"体积读数不许再绕过梯子自己算"</b>两件事（#184：测试工程不引用 UI）。</para>
/// <para><b>按坑表 #195 的口径选判据形状</b>：这批没有钉"160px／16px"这种数字串——<c>16px</c> 在别处是进度条高度、
/// 在天气组件是内边距，钉数字就是给人添误伤。钉的是<b>句子签名</b>（"px 缩略图""或短边小于"这种只可能属于这一句的串）
/// 与<b>算法形状</b>（内插孔里的整数除法算 MB）。</para>
/// </summary>
public sealed class ClipboardIntakeProseGateTests
{
    private const string JudgeFile = "src/StarMark.Abstractions/Clipboard/ClipboardPolicy.cs";
    private const string ViewModelFile = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Clipboard.cs";

    /// <summary>四格状态句里各取一段"只可能属于这句话"的签名。
    /// <para>✅WI-64 起这些句子住在<b>文案表</b>里（三语各一列），所以"只有一个主人"这一问的档案从判据换成了表：
    /// 判据里那些字出现 0 次才是对的——它只该点名键。</para></summary>
    private static readonly string[] SentenceSignatures =
        ["只记录文本与文件列表", "一条图片都不会记录", "px 缩略图", "或短边小于"];

    /// <summary>判据点名的那三颗键（三臂各一颗，颗数由这一份名单报）。</summary>
    private static readonly string[] IntakeKeys = ["clip.intake.off", "clip.intake.paused", "clip.intake.on"];

    // ────────── ① 句子只有一个主人 ──────────

    [Fact]
    public void EachStateSentenceLivesExactlyOnce_andOnlyInTheJudge()
    {
        var judge = CodeOf(JudgeFile);
        var table = SourceGate.ReadRepoFile("src/StarMark.Abstractions/I18n/UiStrings.cs");
        foreach (var signature in SentenceSignatures)
        {
            // ✅WI-64：中文列里恰好一次，而<b>主人是表</b>——判据与 UI 工程都该零次（那里只该点名键）。
            Assert.Equal(1, Count(table, signature));
            Assert.Equal(0, Count(judge, signature));
            var inUi = FormatScanner.SourcesUnder("src/StarMark.UI")
                .Where(f => Count(Code(f.Code), signature) > 0)
                .Select(f => f.Path)
                .ToList();
            Assert.True(inUi.Count == 0, $"设置页又自己写了一遍这句（该问表要键）：{signature} ⇒\n" + string.Join("\n", inUi));
        }
        // 三臂各一颗键，逐颗点名（计数型判据抓不到"换名"＝#164）
        foreach (var key in IntakeKeys) Assert.Equal(1, Count(judge, "LocMessage.Of(\"" + key + "\""));
    }

    // ────────── ② VM 只做转发 ──────────

    [Fact]
    public void ViewModelOnlyForwardsToTheJudge()
    {
        var vm = CodeOf(ViewModelFile);
        Assert.Equal(1, Count(vm, "ClipboardPolicy.DescribeImageIntake(collecting, imageOn)"));
        // 反空转：这颗判据确实被四处状态刷新调用（否则①"UI 里没有那句话"会因为没人显示而假绿）
        Assert.True(Count(vm, "ClipboardImageStatusText(") >= 3,
            "状态行只剩一个调用点 ⇒ 上面那条'UI 不许写第二遍'会因为没人读而变成空闸门");
    }

    // ────────── ③ 体积读数不许再绕过"字节→人话"的梯子自己算 ──────────

    /// <summary>
    /// 禁的形状：<b>内插孔里做 <c>/ (1024*1024)</c></b>（空格写法都算）。整数除法把 20.5 MiB 报成 "20 MB"，
    /// 于是拒收原因写成"图片 20 MB 超过 20 MB 上限"——同句话里两个数互相矛盾（批次 SE 修掉的正是这条）。
    /// </summary>
    [Fact]
    public void NoHandRolledMegabyteDivisionLandsInProse()
    {
        var offenders = FormatScanner.SourcesUnder("src")
            .SelectMany(f => Regex.Matches(Code(f.Code), @"\{[^{}]*\(1024\s*\*\s*1024\)[^{}]*\}")
                .Select(m => $"{f.Path}: {m.Value}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            "又有人自己在句子里算 MB（该走 FileSizeText.Human / ClipAssets.DescribeBytes）：\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// 正面凭据（#161）：③是禁项，零命中本身不说明梯子还活着——它必须真有读者。
    /// 八处是今天各模块（图片占用、备份、诊断、缓存…）用它的下限，删掉一大片就会红。
    /// </summary>
    [Fact]
    public void TheLadderHasReaders()
    {
        var readers = FormatScanner.SourcesUnder("src")
            .Sum(f => Count(Code(f.Code), "FileSizeText.Human("));
        Assert.True(readers >= 8, $"字节梯子只剩 {readers} 个调用点（含定义处）——③那条禁项快成空闸门了");
    }

    // ────────── ④ 判据不空壳 ──────────

    [Fact]
    public void TheJudgeStillNamesAllThreeMeasuredValues()
    {
        // 必须钉在**那颗句子的方法体**里：整文件数会被另一处合法用法（拒收原因里也算过一次体积）顶住，
        // 于是"状态句把上限写死成 20 MB"这种接线断裂扫不出来（台架 SE5 第一遍就是这么绿的）。
        var body = MethodBody(ReadRepoFile(JudgeFile), "public static LocMessage DescribeImageIntake(");
        // ⚠ 那颗 needle 连着<b>回型</b>一起比（上面已同批改口）：这颗方法今天交的是身份，
        // 回型一改就是命中 0，读起来像"闸门有洞"，其实是别处的证人要跟上新形状（坑表 #465 同族）。
        Assert.Contains("ClipAssets.ThumbnailMaxEdge", body, StringComparison.Ordinal);
        Assert.Contains("ClipAssets.DescribeBytes(MaxImageBytes)", body, StringComparison.Ordinal);
        Assert.Contains("MinImageEdge", body, StringComparison.Ordinal);
        Assert.Equal(1, Count(CodeOf(JudgeFile), "public static LocMessage DescribeImageIntake("));
    }

    // ────────── ⑤ 签名清单不许悄悄删短 ──────────

    /// <summary>
    /// 上面①是靠"签名清单"守的，而清单本身被人删一项就静默少守一格 ⇒ 两半一起钉：
    /// <b>判据里至少有三格</b>（函数结构没被拆散）＋<b>清单恰好四条</b>（"已开启"那一格带两条，因为它说了三件事）。
    /// 加一格必须同时加签名——就像 SA 钉判据常数条数那样：<b>改这里的数应当是一次有意识的动作</b>。
    /// </summary>
    [Fact]
    public void TheSignatureListCoversEveryStateCell()
    {
        var body = MethodBody(ReadRepoFile(JudgeFile), "public static LocMessage DescribeImageIntake(");
        // 旧口径"数三元的出口"（`? "` / `: "` 后面紧跟引号）随 ✅WI-64 失效：出口现在都是 `LocMessage.Of("…")`，
        // 引号前多了一段方法调用 ⇒ 那颗正则恒 0，看着像"函数被拆了"、其实是尺⛔ 跟上形状。改成数点名的键。
        var cells = Regex.Matches(body, "LocMessage\\.Of\\(").Count;
        Assert.True(cells >= 3, $"只从判据里认出 {cells} 格状态句——函数结构变了，①那条守不住任何东西");
        Assert.Equal(4, SentenceSignatures.Length);
    }

    /// <summary>注释里的旧写法不参与判据（#190：按行截注释会把同行后半段真违规一起抹掉，这里用扫描器）。</summary>
    [Fact]
    public void CommentMentionsOfTheOldShapeDoNotCount()
    {
        const string commented = "// 旧写法是 bytes / (1024 * 1024)（整数除法）\nvar a = 1;\n";
        Assert.Empty(Regex.Matches(Code(commented), @"\{[^{}]*\(1024\s*\*\s*1024\)[^{}]*\}"));
        const string realCode = "var s = $\"图片 {size / (1024*1024)} MB\";";
        Assert.NotEmpty(Regex.Matches(Code(realCode), @"\{[^{}]*\(1024\s*\*\s*1024\)[^{}]*\}"));
    }

    private static string CodeOf(string relativePath) => Code(ReadRepoFile(relativePath));

    /// <summary>抹掉注释后的代码（注释要跳过，但不能靠截行）。</summary>
    private static string Code(string text) => FormatScanner.Scan(text).Code;
}
