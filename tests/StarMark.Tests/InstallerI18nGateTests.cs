#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 安装器那一族的语言闸（P-169 ⑥）。向导标准字跟着 .isl 走，<b>[Code] 现场弹的那几句不跟</b>——
/// 这一族坏过两次形状：只加语言格不补弹句＝"日文/英文向导配中文弹窗"（#286 的半中半英）；
/// 以及编码本身（iss/ps1 无 BOM 的 UTF-8 被按 ANSI 读＝乱码编进产物、构建全绿只有用户看得见，#262 族）。
/// </summary>
public sealed class InstallerI18nGateTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarMark.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static readonly Encoding Utf8Bom = new UTF8Encoding(true);

    private static string Iss() => File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "installer", "StarMark.iss"), Utf8Bom);
    private static string Ps() => File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "make-installer.ps1"), Utf8Bom);

    // 键名不许带点（ISCC 实测：`runtime.lead=` 会按"语言前缀 runtime ＋ 键 lead"解析并报
    // Unknown language name——真编这一发抓到，文本闸门放行。2026-10-06 编译教训进坑表。）
    private static readonly string[] RuntimeKeys =
        ["runtimeLead", "runtimeWhyAdmin", "runtimeWhyLow", "runtimeChoices", "runtimeCancelled"];

    /// <summary>Pascal 注释按行剥掉再扫（口径同 P-169 普查的"先去注释再数"）——
    /// 函数体里那句讲 #261 教训的注释**合法地引用**了旧弹句（"用户看见'自动安装没成功'会误读成 winget 坏了"），
    /// 不剥就会把"教训讲解"误报成"字面回归"；而"注释在解释代码、代码不该在重复它"正是这里要钉的形状。</summary>
    private static string WithoutPascalComments(string body)
        => string.Join("\n", body.Replace("\r\n", "\n").Split('\n')
            .Select(l => { var i = l.IndexOf("//", StringComparison.Ordinal); return i >= 0 ? l[..i] : l; }));

    [Fact]
    public void RunTimePopupComesFromTableNotHardCodedInCode()
    {
        var iss = Iss();
        var from = iss.IndexOf("function PrepareToInstall", StringComparison.Ordinal);
        var to = iss.IndexOf("procedure CurStepChanged", StringComparison.Ordinal);
        Assert.True(from > 0 && to > from, "PrepareToInstall 函数体圈不出来");
        var body = WithoutPascalComments(iss[from..to]);
        // 旧的五处中文字面（剥注释后）一颗都不许留在代码里（Log 不过这把尺子，但 Log 句里本来也没有这些）
        Assert.DoesNotContain("这台机器上没检测到", body);
        Assert.DoesNotContain("自动安装没成功", body);
        Assert.DoesNotContain("这一份是「只给我装」", body);
        Assert.DoesNotContain("而你选择了取消", body);
        // 正向半边：五颗键各有一处 CustomMessage 取用（只钉"没了旧的"不钉"新的接上了"＝半边证人，#293 教训）
        Assert.Equal(RuntimeKeys.Length,
            RuntimeKeys.Count(k => body.Contains($"CustomMessage('{k}')")));
    }

    [Fact]
    public void EachRunTimeKeyHasDefaultEnglishAndJapaneseColumns()
    {
        var lines = Iss().Replace("\r\n", "\n").Split('\n');
        foreach (var key in RuntimeKeys)
        {
            Assert.Contains(lines, l => l.StartsWith(key + "="));                    // 默认列＝中文
            Assert.Contains(lines, l => l.StartsWith("english." + key + "="));      // 英文屏今天是同一洞，同补
            Assert.Contains(lines, l => l.StartsWith("japanese." + key + "="));
        }
    }

    [Fact]
    public void JapaneseEntryIsConditionalLikeChinese()
    {
        var iss = Iss();
        // 无条件引用 = 换台没有官方语言全集的机器直接构建失败；两个语言包同一个"有则收、无则降级要出声"的形状
        Assert.Contains("#define HasJapanese FileExists", iss);
        Assert.Contains("#if HasJapanese", iss);
        Assert.Contains("Name: \"japanese\"", iss);
        // 回落次序：日文格排在 english 之后（缺日文包时"第一格"仍是中英，别把日文摆到兜底位）
        Assert.True(iss.IndexOf("Name: \"japanese\"", StringComparison.Ordinal)
                    > iss.IndexOf("Name: \"english\"", StringComparison.Ordinal));
    }

    [Fact]
    public void MakeInstallerSaysOutLoudAboutBothOptionalPacks()
    {
        var ps = Ps();
        Assert.Contains("中文语言包在", ps);           // 既有那颗不许被这批改丢
        Assert.Contains("日文语言包在", ps);
        Assert.Contains("没有日文语言包", ps);
        Assert.Contains("别让它无声", ps);
    }

    [Fact]
    public void InstallerFilesStayBomAndCrlf()
    {
        foreach (var rel in new[]
                 { Path.Combine("scripts", "installer", "StarMark.iss"), Path.Combine("scripts", "make-installer.ps1") })
        {
            var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), rel));
            Assert.True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                rel + " 丢了 UTF-8 BOM（#262 族：Inno 与 PS5.1 对无 BOM 按 ANSI 读，乱码编进产物、构建全绿）");
            for (var i = 1; i < bytes.Length; i++)
                if (bytes[i] == 0x0A && i > 0)
                    Assert.True(bytes[i - 1] == 0x0D, rel + " 出现裸 LF（第 " + i + " 字节）——这两个文件必须整文件 CRLF");
        }
    }

    [Fact]
    public void MessagesSectionCarriesTheWhyThisTableExists()
    {
        // [CustomMessages] 段头必须自己说清"为什么这几句要单独进表"——只留键名，下一个加语言包的人会以为可删
        var iss = Iss();
        var at = iss.IndexOf("[CustomMessages]", StringComparison.Ordinal);
        Assert.True(at > 0);
        Assert.Contains("半中半英", iss[at..(at + 600)]);
    }
}
