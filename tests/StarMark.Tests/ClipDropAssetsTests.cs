#nullable enable
using System;
using System.IO;
using System.Text;
using StarMark.Abstractions.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <see cref="ClipDropAssets"/> 纯函数逐值断言（✅P-307 第二刀・乙）：命名、洗名、名册、改道。
/// <para>
/// 这里只吃字符串与字节——IO 那一半（物化、幂等、尽力删）在 <c>ClipboardDropFileStoreTests</c>，
/// 删除接线（轮转/清空/删单条收走认领件）在 <c>ClipboardAssetDeletionTests</c>。
/// 三处必须分开：纯函数错＝名字错，IO 错＝件在不在，接线错＝孤儿——三种症状，三份测试。
/// </para>
/// </summary>
public sealed class ClipDropAssetsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"starmark_dropassets_{Guid.NewGuid():N}");

    public ClipDropAssetsTests()
    {
        Directory.CreateDirectory(_dir);
        // 改道与 ClipAssets.FolderOverride 同一课：不改道＝FullPathOf 指向用户真实 clip-drops。
        ClipDropAssets.FolderOverride = _dir;
    }

    public void Dispose()
    {
        ClipDropAssets.FolderOverride = null;
        try { Directory.Delete(_dir, true); } catch { /* 临时目录留给系统清 */ }
    }

    // ────────── 哈希段：确定性、形状 ──────────

    [Fact]
    public void HashOf_IsDeterministic_TwelveLowercaseHexChars()
    {
        var content = Encoding.UTF8.GetBytes("拖进来的一个虚拟文件");

        var h1 = ClipDropAssets.HashOf(content);
        var h2 = ClipDropAssets.HashOf((byte[])content.Clone());

        Assert.Equal(ClipDropAssets.HashPrefixChars, h1.Length);
        Assert.Equal(h1, h2);                                    // 同内容永远同名 ⇒ 合并回放，不攒孤儿
        foreach (var c in h1)
            Assert.True(c is >= '0' and <= '9' or >= 'a' and <= 'f', $"哈希段里出了非小写十六进制字符：'{c}'");
    }

    [Fact]
    public void HashOf_DifferentContentGetsDifferentHash()
    {
        // 哈希的意义只在"同内容同名"：不同内容若同名，第二次拖放会顶掉第一次的件。
        var a = ClipDropAssets.HashOf(Encoding.UTF8.GetBytes("内容甲"));
        var b = ClipDropAssets.HashOf(Encoding.UTF8.GetBytes("内容乙"));
        Assert.NotEqual(a, b);
    }

    // ────────── 洗名：拖源给什么名字都不许出界 ──────────

    [Theory]
    [InlineData(null, "dropped")]                                  // 拖源可以不给名字
    [InlineData("", "dropped")]
    [InlineData("   ", "dropped")]
    [InlineData("..", "dropped")]                                  // 连点全折掉就剩兜底
    [InlineData("C:\\Users\\me\\报告.pdf", "报告.pdf")]           // 路径段不进名字
    [InlineData("/mnt/data/notes.txt", "notes.txt")]
    [InlineData("a<b>c.txt", "a_b_c.txt")]                         // Windows 不许的字符换 _，不丢段
    [InlineData("半角:冒号.docx", "半角_冒号.docx")]
    [InlineData("a..b.txt", "a.b.txt")]                            // 连点折叠：不折的话自家名册拒 ".."
    [InlineData("trailing dots... ", "trailing dots")]             // 结尾点/空格折掉（Windows 会静默剥）
    [InlineData("说 明.docx", "说 明.docx")]                       // 中间的空格是用户认件用的，不许动
    [InlineData("\u0001a.txt", "_a.txt")]                          // 控制符也换 _
    public void SanitizeOriginalName_TamesWhateverTheSourceGave(string? raw, string expected)
        => Assert.Equal(expected, ClipDropAssets.SanitizeOriginalName(raw));

    [Fact]
    public void SanitizeOriginalName_CutsAtEightyChars()
    {
        // 名字是给人看的，不是给拖源的：拖源给的名字可以长到离谱，超出的部分不带遗憾地截。
        var longName = new string('名', ClipDropAssets.MaxOriginalNameChars + 10);

        var sanitized = ClipDropAssets.SanitizeOriginalName(longName);

        Assert.Equal(ClipDropAssets.MaxOriginalNameChars, sanitized.Length);
        Assert.Equal(new string('名', ClipDropAssets.MaxOriginalNameChars), sanitized);
    }

    // ────────── 组名与名册：我们写的形状只有一种 ──────────

    [Fact]
    public void FileNameOf_ProducesNamesTheRosterAccepts()
    {
        // 自家写的名字必须过自家名册——两处各写一套"安全"就会有一天写出去的名字自己删不掉。
        foreach (var original in new[] { "报告 v1.pdf", null, "", "a<b>:c.txt" })
        {
            var name = ClipDropAssets.FileNameOf(ClipDropAssets.HashOf(new byte[] { 9 }), original);
            Assert.True(ClipDropAssets.IsSafeFileName(name), $"名册拒了自己写的名字：{name}");
            Assert.NotNull(ClipDropAssets.FullPathOf(name));
        }
    }

    [Theory]
    [InlineData("0123456789ab_报告.pdf", true)]                    // 我们写的形状
    [InlineData("0123456789ab_dropped", true)]                     // 兜底名也是我们写的形状
    [InlineData("0123456789ab_报告.pdf.tmp", true)]                // 半写残的临时名也是自己的
    [InlineData("0123456789ab", false)]                            // 连下划线都没有
    [InlineData("0123456789a_报告.pdf", false)]                    // 哈希段短一位＝界碑挪了位
    [InlineData("0123456789abX报告.pdf", false)]                   // 下划线长成了别的字符
    [InlineData("0123456789AB_报告.pdf", false)]                   // 大写＝不是我们写的（宁可少删）
    [InlineData("zzzzzzzzzzzz_报告.pdf", false)]                   // 哈希段不是十六进制
    [InlineData("0123456789ab_../evil.pdf", false)]                // ".."
    [InlineData("0123456789ab_a/b.pdf", false)]
    [InlineData("0123456789ab_a\\b.pdf", false)]
    [InlineData("../evil.pdf", false)]                             // 从手改备份里回来的越界名
    [InlineData("C:\\windows\\evil.pdf", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsSafeFileName_AcceptsOnlyOurOwnShape(string? name, bool expected)
        => Assert.Equal(expected, ClipDropAssets.IsSafeFileName(name));

    [Fact]
    public void SafeFileNamesFitWithin120Chars_LongerOnesAreRefused()
    {
        var prefix = "0123456789ab_";
        var atLimit = prefix + new string('名', ClipDropAssets.MaxFileNameChars - prefix.Length);

        Assert.True(ClipDropAssets.IsSafeFileName(atLimit));       // 恰好 120＝我们的件
        Assert.False(ClipDropAssets.IsSafeFileName(atLimit + "余"));   // 121＝不是我们的件
    }

    [Fact]
    public void TempNamesAreRecognizedAsOurOwnHalves()
    {
        var final = ClipDropAssets.FileNameOf(ClipDropAssets.HashOf(new byte[] { 4 }), "半成品.txt");
        Assert.True(ClipDropAssets.IsSafeFileName(ClipDropAssets.TempNameOf(final)));
    }

    // ────────── 拼路径与改道：写删两侧问同一个字段 ──────────

    [Fact]
    public void FullPathOf_LandsInsideTheOverriddenFolder_UnsafeGetsNull()
    {
        var safe = ClipDropAssets.FileNameOf(ClipDropAssets.HashOf(new byte[] { 1, 2, 3 }), "材料.pdf");

        Assert.Equal(Path.Combine(_dir, safe), ClipDropAssets.FullPathOf(safe));   // 改道生效
        Assert.Null(ClipDropAssets.FullPathOf("../evil.txt"));
        Assert.Null(ClipDropAssets.FullPathOf("0123456789ab_../evil.pdf"));
    }

    [Fact]
    public void DirectoryFor_HangsTheFolderUnderTheGivenRoot()
        => Assert.Equal(
            Path.Combine("C:\\AnyRoot", ClipDropAssets.FolderName),
            ClipDropAssets.DirectoryFor("C:\\AnyRoot"));
}
