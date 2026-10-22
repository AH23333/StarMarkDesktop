#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 那颗导出档名的<b>同名不盖</b>判据（批次 Phase C 片55＝账本 P-243 的落地腿）。
/// <para>钉的是这一件具体的事：同一块板连着导出两次，第二次⛔ 许把第一次那颗<b>悄悄抹掉</b>——
/// 屏幕上那句"已存到 &lt;路径&gt;"两次念的是同一颗路，谁都不会知道少了一颗档。
/// 判据住 Core 的原因（片43 同一条路）：测试工程⛔ 引用 <c>StarMark.UI</c>，
/// 而"盘上有没有同一颗"那一问只有做成纯函数才摆得出<b>整族都被占满</b>那一档。</para>
/// </summary>
public sealed class BoardExportNameTests
{
    /// <summary>把"盘上已有这几颗档"摆成一把问句（⛔ 碰真文件系统＝那颗上限那一档就测不到）。</summary>
    private static Func<string, bool> OnDisk(params string[] names)
        => new HashSet<string>(names, StringComparer.OrdinalIgnoreCase).Contains;

    private const string Board = "板子.png";
    private const string Fallback = "261019-143021-a1b2.png";

    // ────────── ① 空目录与第一次撞名 ──────────

    [Fact]
    public void AFreeDirectoryKeepsTheNameUntouched()
    {
        Assert.Equal(Board, BridgeProtocol.UniqueExportName(Board, OnDisk(), Fallback));
    }

    [Fact]
    public void TheSecondSameNameGetsASuffixInsteadOfOverwriting()
    {
        var picked = BridgeProtocol.UniqueExportName(Board, OnDisk(Board), Fallback);

        Assert.Equal("板子-2.png", picked);
        // 那颗坏法的形状：兜底⛔ 是"同一颗名再写一遍"（盘上那颗还在＝File.WriteAllBytes 会盖掉它）
        Assert.NotEqual(Board, picked);
    }

    [Fact]
    public void TheSentenceNamesTheFileThatActuallyLanded()
    {
        // 成对判据：添完序号之后，那颗名字在盘上必须⛔ 存在——"这句『已存到』指的那颗档"才是它的资格
        var disk = new List<string> { Board, "板子-2.png", "板子-3.png" };
        var picked = BridgeProtocol.UniqueExportName(Board, OnDisk(disk.ToArray()), Fallback);

        Assert.Equal("板子-4.png", picked);
        Assert.DoesNotContain(picked, disk);
    }

    // ────────── ② 人自己写过的 -2 也⛔ 许被踩 ──────────

    [Fact]
    public void AnExistingDashedNameIsNotSteppedOn()
    {
        // 盘上同时有 `板子.png` 与一颗<b>人自己起名</b>的 `板子-2.png`：从 板子.png 添出来的下一颗正好撞它
        Assert.Equal("板子-3.png", BridgeProtocol.UniqueExportName(Board, OnDisk(Board, "板子-2.png"), Fallback));
    }

    [Fact]
    public void TheFallbackRootIsNumberedToo()
    {
        // 原名的整族（1＋UniqueTrials 颗）都被占满＝换带时刻的兜底名，而不是硬写
        var taken = new List<string> { Board };
        for (var n = 2; n <= BridgeProtocol.UniqueTrials + 1; n++) taken.Add("板子-" + n + ".png");

        Assert.Equal(Fallback, BridgeProtocol.UniqueExportName(Board, OnDisk(taken.ToArray()), Fallback));
        Assert.Equal("261019-143021-a1b2-2.png",
            BridgeProtocol.UniqueExportName(Board, OnDisk(taken.Append(Fallback).ToArray()), Fallback));
    }

    [Fact]
    public void WhenEvenTheFallbackFamilyIsFullNothingIsWritten()
    {
        var taken = new List<string>();
        foreach (var root in new[] { "板子", "261019-143021-a1b2" })
        {
            taken.Add(root + ".png");
            for (var n = 2; n <= BridgeProtocol.UniqueTrials + 1; n++) taken.Add(root + "-" + n + ".png");
        }

        // ⛔ 落＝交回 null；宿主那一头据此走"没能存成"那一句（宁可说不出去，也⛔ 许盖掉别人的东西）
        Assert.Null(BridgeProtocol.UniqueExportName(Board, OnDisk(taken.ToArray()), Fallback));
    }

    // ────────── ③ 上限、点与空格、大小写 ──────────

    [Fact]
    public void TheSuffixNeverPushesTheStemPastTheCap()
    {
        var longStem = new string('名', BridgeProtocol.MaxStemLength);
        var name = longStem + ".png";

        var picked = BridgeProtocol.UniqueExportName(name, OnDisk(name), Fallback)!;
        var stem = System.IO.Path.GetFileNameWithoutExtension(picked);

        Assert.True(stem.Length <= BridgeProtocol.MaxStemLength,
            $"添完序号把主干顶到 {stem.Length} 颗＝超了上限 {BridgeProtocol.MaxStemLength}");
        Assert.EndsWith("-2.png", picked, StringComparison.Ordinal);   // 扩展名⛔ 被挪动位置
    }

    [Fact]
    public void ATrimmedStemNeverEndsInADotOrASpace()
    {
        // Windows 不许档名以 `.` 或空格收尾；主干正好卡在上限、截完之后剩一串点＝那颗名⛔ 落
        var dots = new string('.', BridgeProtocol.MaxStemLength);
        var picked = BridgeProtocol.UniqueExportName(dots + ".png", OnDisk(dots + ".png"), Fallback);

        Assert.Equal(Fallback, picked);   // 添不出合法的一颗就换根，而⛔ 交回一颗开不了的档名
    }

    [Fact]
    public void TwoRootsDifferingOnlyInCaseAreOneName()
    {
        // 页面递 "Board.png" 而兜底拼出来是 "Board.PNG"＝NTFS 上同一颗名。
        // ⚠ "盘上有没有"那一问⛔ 住在调用方（宿主递的是 <c>File.Exists</c>，NTFS 天生认⛔ 大小写＝那颗尺归 DL 那一格钉），
        //   这颗格钉的是候选那一串自己⛔ 许把同一颗名报两遍。
        var both = BridgeProtocol.ExportNameCandidates("Board.png", "Board.PNG").ToList();

        Assert.Equal(1 + BridgeProtocol.UniqueTrials, both.Count);
        Assert.Equal("Board.png", both[0]);
    }

    [Fact]
    public void SvgKeepsItsOwnExtensionAcrossTheFamily()
    {
        var picked = BridgeProtocol.UniqueExportName("板子.svg", OnDisk("板子.svg"), "261019-143021-a1b2.svg");

        Assert.Equal("板子-2.svg", picked);
    }

    [Fact]
    public void ANameWithoutExtensionStillNumbers()
    {
        Assert.Equal("板子-2", BridgeProtocol.UniqueExportName("板子", OnDisk("板子"), Fallback));
    }

    // ────────── ④ 候选那一串自己的形状 ──────────

    [Fact]
    public void TheCandidateListNeverHandsOutTheSameNameTwice()
    {
        // 两颗根恰好同名（页面没给名字时就会这样）＝⛔ 报两遍；重复的一颗会让"整族被占满"那颗判据虚报颗数
        var same = BridgeProtocol.ExportNameCandidates(Board, Board).ToList();
        Assert.Equal(same.Count, same.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var both = BridgeProtocol.ExportNameCandidates(Board, Fallback).ToList();
        Assert.Equal(both.Count, both.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Board, both[0]);
        Assert.Equal(Fallback, both[1 + BridgeProtocol.UniqueTrials]);
    }

    [Fact]
    public void AnEmptyNameStartsStraightAtTheFallbackRoot()
    {
        var both = BridgeProtocol.ExportNameCandidates("", Fallback).ToList();

        Assert.Equal(Fallback, both[0]);                  // ⛔ 交回一颗空名＝宿主那一路会写到目录本身上去
        Assert.Equal(1 + BridgeProtocol.UniqueTrials, both.Count);
    }

    // ────────── ⑤ 收名那一手：先成裸名，再谈同名 ──────────

    /// <summary>
    /// <see cref="BridgeProtocol.SanitizeExportName"/> 交回的裸名⛔ 带目录、⛔ 空主干、⛔ 是保留名——
    /// 它是这颗唯一主人的<b>上游</b>：先收成裸名，再谈同名（扩展名只从档位取）。
    /// </summary>
    [Theory]
    [InlineData(@"..\..\evil.png", "evil.png")]
    [InlineData("D:/a/b/evil2.png", "evil2.png")]
    [InlineData("CON.png", "FB-STEM.png")]
    [InlineData("   .png", "FB-STEM.png")]
    public void SanitizingComesBeforeNumbering(string suggested, string expected)
    {
        var name = BridgeProtocol.SanitizeExportName(suggested, "FB-STEM", "png");

        Assert.Equal(expected, name);
        Assert.Equal(".png", System.IO.Path.GetExtension(name));
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("\\", name);
    }

    /// <summary>
    /// 深析 §35.6 那颗待查在这一格收口：页面递的 <c>suggested</c> 是<b>串</b>，形状⛔ 由我们定
    /// （<c>appState.name</c> 可以空、可以全是点、可以带设备名），所以这颗主人<b>每一档形状都要交回一颗能写的裸名</b>——
    /// 空名会让宿主那一手写到目录本身上去，保留名会让 <c>File.WriteAllBytes</c> 当场炸，带目录的那颗会写到落点之外。
    /// <para><b>那颗以 <c>-</c> 开头的是有意留下的</b>：Windows 里 <c>-</c> ⛔ 是非法字符，而我们<b>整条落盘那一路⛔ 把档名递给任何命令行</b>
    /// （<c>Path.Combine(dir, name)</c> ＋ <c>File.WriteAllBytes</c>），所以⛔ 需要一颗「不许以 - 开头」的门——
    /// 片74 那颗 <c>-rx3-2.png</c> 就是<b>仪器自己</b>把桥改成 <c>boardName() + "-rx" + scale</c> 造出来的形状（产品那一路递的是 <c>appState.name</c>）。</para>
    /// </summary>
    [Theory]
    [InlineData(null, "FB-STEM.png")]                                  // null＝⛔ 递名字（旧版桥／那颗⛔ 带的档）
    [InlineData("", "FB-STEM.png")]                                    // 空串
    [InlineData("   ", "FB-STEM.png")]                                 // 只有空白（Trim 之后空）
    [InlineData("/", "FB-STEM.png")]                                   // 整颗名都是目录分隔
    [InlineData("CON", "FB-STEM.png")]                                 // 设备名・⛔ 带扩展名
    [InlineData("nul.txt", "FB-STEM.png")]                             // 设备名・带扩展名（主干才是那颗名）
    [InlineData("a:b.png", "b.png")]                                   // ⚠ 那颗冒号：.NET 先按「驱动器相对路径」解析（<c>a:</c>＝一颗盘符），
                                                                        // 于是"丢目录"那一支先赢过"非法字符"那一支＝交回 <c>b.png</c>。
                                                                        // 两颗都落在同一句口径上（裸名・可写・⛔ 出落点），所以本批⛔ 改行为，只把这个<b>先后</b>钉成读数。
    [InlineData("板子:副本.png", "FB-STEM.png")]                        // 同一颗冒号，前面⛔ 是单颗字母＝⛔ 盘符 ⇒ 整颗名留下冒号→撞非法字符那一支→换兜底
    [InlineData(@"a""b.png", "FB-STEM.png")]                           // 非法字符（引号＝那颗名⛔ 能写成文件名的形状）
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.png",
             "FB-STEM.png")]                                            // 超 MaxStemLength＝换兜底，⛔ 截一半留颗认不出的
    [InlineData("-rx3", "-rx3.png")]                                   // ⚠ 以 - 开头＝<b>原样留</b>（见那段口径）
    [InlineData("...png", "...png")]                                   // 整颗名都是点：主干交回 ".."＋档位那颗扩展名
    [InlineData("..\\..\\evil.png", "evil.png")]                       // 往上跳两级
    [InlineData("D:/a/b/evil2.png", "evil2.png")]                      // 绝对路径
    [InlineData("我的 板 v2.final.png", "我的 板 v2.final.png")]            // 空格与多点：扩展名只从档位取，主干原样
    [InlineData("evil.PNG", "evil.png")]                               // 档位赢过递上来的扩展名
    [InlineData("%2e%2e%2f", "%2e%2e%2f.png")]                         // ⛔ 解码＝⛔ 把没递过的字符当成递了的
    public void EveryShapeOfSuggestedComesBackAsAWriteablePlainName(string? suggested, string expected)
    {
        var name = BridgeProtocol.SanitizeExportName(suggested, "FB-STEM", "png");

        Assert.Equal(expected, name);
        Assert.False(string.IsNullOrEmpty(name));
        Assert.Equal(".png", System.IO.Path.GetExtension(name));
        Assert.Empty(name.Intersect(System.IO.Path.GetInvalidFileNameChars()));
        Assert.Equal(System.IO.Path.GetFileName(name), name);           // ⛔ 带目录＝⛔ 落在落点之外
        Assert.True(System.IO.Path.GetFileNameWithoutExtension(name).Length <= BridgeProtocol.MaxStemLength,
            "主干现读 " + System.IO.Path.GetFileNameWithoutExtension(name).Length + " 颗，超了那颗上限");
        Assert.False(StarMark.Core.Files.DeviceNamePolicy.IsReservedStem(System.IO.Path.GetFileNameWithoutExtension(name)),
            "交回的主干⛔ 是设备名（现读 " + System.IO.Path.GetFileNameWithoutExtension(name) + "）");
    }
}
