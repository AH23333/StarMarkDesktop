#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 发布那条流水线（批次 UH）的闸门：产出那一棵树的脚本、打包并签名的脚本、以及.gitignore 里那条"产物不许入库"。
/// <para><b>为什么这一族只能这么守</b>：`dotnet publish`、`Compress-Archive` 与私钥都在测试跑不到的地方，
/// 而它们错一次的表现都不是构建红：<b>少带更新器</b> ⇒ 用户点「立即更新」只得到一句"没能起来"，
/// 而且这一版之后再也更新不了（要再发一版才能补救）；<b>版本号写成第二份</b> ⇒ 签出来的清单与产物对不上，
/// 界面说的是"版本对不上"而代码全绿。这两格都在 #189/#193 那一族的形状里：一处事实有两个出处就一定会漂。</para>
/// <para>所以这里钉的是<b>出处</b>：产物树只许一处产出、名字只许取自 Abstractions 那两颗常量、
/// 版本只许从 `Directory.Build.props` 读、拒签与残缺树必须当场 throw。</para>
/// </summary>
public sealed class ReleasePackagingGateTests
{
    private const string TreeScript = "scripts/publish-tree.ps1";
    private const string PackageScript = "scripts/release-package.ps1";
    private const string PublishScript = "scripts/publish-release.ps1";
    private const string LocalPublish = "publish.ps1";

    /// <summary>仓库里所有 .ps1（跳过产物目录）。扫不到任何一颗就抛——守门失效比红测危险。</summary>
    private static IReadOnlyList<(string Relative, string Text)> EveryPowerShellScript()
    {
        var root = RepoRoot();
        var list = new List<(string, string)>();
        foreach (var file in Directory.GetFiles(root, "*.ps1", SearchOption.AllDirectories))
        {
            var normalized = file.Replace(Path.DirectorySeparatorChar, '/');
            if (normalized.Contains("/bin/") || normalized.Contains("/obj/")
                || normalized.Contains("/publish/") || normalized.Contains("/dist/")) continue;
            list.Add((normalized[(root.Replace(Path.DirectorySeparatorChar, '/').TrimEnd('/').Length + 1)..],
                File.ReadAllText(file)));
        }
        Assert.True(list.Count >= 3, $"只扫到 {list.Count} 颗 .ps1，普查路径错了");
        return list;
    }

    /// <summary>
    /// PowerShell 版的"抹掉注释"。<b>禁项一律读这个</b>（与 <see cref="SourceGate.Code"/> 同一个道理：
    /// 注释里写一句"私钥在 %USERPROFILE%…"是为了讲清为什么，不是又开一条出口）。
    /// <para>口径：一颗脚本里<b>不许把 `#` 写进字符串字面量</b>——这条在这里成立，改脚本时请保持。</para>
    /// </summary>
    private static string PsCode(string text)
    {
        var kept = text.Split('\n').Select(line =>
        {
            var cut = line.IndexOf('#');
            return (cut >= 0 ? line[..cut] : line).TrimEnd('\r');
        });
        return string.Join("\n", kept.Where(l => !string.IsNullOrWhiteSpace(l)));
    }

    // ===== 一棵树：只许有一处产出，而且两半都得当场自证 =====

    [Fact]
    public void TheMainProgramIsPublishedInExactlyOneScript()
    {
        var scripts = EveryPowerShellScript();
        // "dotnet publish …StarMark.UI" 这颗调用全仓只许有一处（在 publish-tree.ps1 里）。
        // publish.ps1 与 release-package.ps1 各写一份，本机那一棵与发出去那一棵就会长得不一样，
        // 而差别只在用户机器上露头（发出去的那份没带 Updater\ 是这一族最坏的一种）。
        var owners = scripts
            .Where(s => Regex.IsMatch(PsCode(s.Text), @"dotnet\s+publish[^\n]*StarMark\.UI"))
            .Select(s => s.Relative)
            .ToList();
        Assert.Equal(new[] { TreeScript }, owners);
    }

    [Fact]
    public void TheUpdaterShipsAsAPublishedFolderNotACopiedExe()
    {
        var code = PsCode(ReadRepoFile(TreeScript));
        // 目录名与 exe 名都从那颗契约常量取：C# 侧改了名而脚本没跟上，这里就红（跨语言钉同一件事）。
        // 这里刻意只钉**字面**、不钉脚本里的变量名：改个局部名字是合法重构，不该逼出一条假失败（#123）。
        Assert.Contains($"\"{UpdaterPaths.UpdaterFolderName}\"", code, StringComparison.Ordinal);
        Assert.Contains($"\"{UpdaterPaths.UpdaterFolderName}\\{UpdaterPaths.UpdaterExeName}\"", code, StringComparison.Ordinal);
        Assert.Matches(@"dotnet\s+publish[^\n]*StarMark\.Updater", code);
        // 不许退化成"只搬一颗 exe"：它要自己的 .dll 与 runtimeconfig，按清单逐颗复制就会漏（#189/#193）
        Assert.DoesNotContain("Copy-Item", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ATreeMissingEitherHalfIsRefusedWhereItIsBuilt()
    {
        var code = PsCode(ReadRepoFile(TreeScript));
        // 残缺的一棵树一旦被 zip 打进发布产物，代价是"发一版没人能启动"或"这一版之后再也更新不了"，
        // 两种都只能靠下一版补救——所以"两半各有一句当场拒绝"必须留在**产这棵树的那颗脚本**里。
        Assert.Contains($"\"{UpdateAssets.EntryExeName}\"", code, StringComparison.Ordinal);
        var guards = code.Split('\n')
            .Where(line => line.Contains("Test-Path", StringComparison.Ordinal))
            .ToList();
        // 主程序、更新器、编译后的 XAML、资源索引——四样各有一句当场拒绝（少于四句就是有一样不再被自证）
        Assert.True(guards.Count >= 4, $"这棵树的自证只剩 {guards.Count} 句（该有四句）");
        foreach (var guard in guards)
            Assert.Contains("throw", guard, StringComparison.Ordinal);   // 只查不拒＝那句检查是装饰
    }

    [Fact]
    public void TheTreeCarriesTheCompiledXamlTheRuntimeNeeds()
    {
        var code = PsCode(ReadRepoFile(TreeScript));
        // 非打包的 WinUI 3 运行时要 *.xbf（每个页面编译后的 XAML）与 <程序名>.pri（ms-appx:/// 的索引）。
        // dotnet publish 天生不把它们复制出去，所以搬运那一刀在 Directory.Build.targets 里；
        // 而"少搬了"的表现不是构建红，是**发布产物双击抛 XamlParseException**（坑表 #237：真发过一次的话，
        // 用户装完新版连程序都打不开）。这里钉的是"产树那一侧不许不查这两样"。
        var appPri = Path.GetFileNameWithoutExtension(UpdateAssets.EntryExeName) + ".pri";
        Assert.Contains("\"App.xbf\"", code, StringComparison.Ordinal);
        Assert.Contains($"\"{appPri}\"", code, StringComparison.Ordinal);
        // 搬运那一刀在 Directory.Build.targets 里（另一颗闸门守它），这里只钉"产树那一侧不许不查这两样"。
    }

    [Fact]
    public void TheLocalInstallKeepsUsingTheSameTreeScript()
    {
        var code = PsCode(ReadRepoFile(LocalPublish));
        Assert.Contains("scripts\\publish-tree.ps1", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"dotnet\s+publish", code);      // 它不再自己产树，只加快捷方式
        Assert.Contains("StarMark.lnk", code, StringComparison.Ordinal);   // 本机那条路照旧能用
    }

    // ===== 版本与名字：只许有一个出处，而且必须能被应用自己的解析器认回来 =====

    [Fact]
    public void TheVersionIsReadFromDirectoryBuildPropsAndIsNowhereWrittenTwice()
    {
        var code = PsCode(ReadRepoFile(PackageScript));
        Assert.Contains("<Version>([^<]+)</Version>", code, StringComparison.Ordinal);
        // 脚本里不许出现第二串版本号（连 "$version" 拼出来的那种都不许写死）：
        // 写死的那一次的表现是"检查到新版、点下去说版本对不上"，而构建与测试全绿。
        Assert.False(Regex.IsMatch(code, @"\b\d+\.\d+\.\d+\b"), "打包脚本里有硬编码的版本号");
    }

    [Fact]
    public void TheVersionInPropsIsCanonicalUnderTheAppsOwnParser()
    {
        var props = ReadRepoFile("Directory.Build.props");
        var matched = Regex.Match(props, "<Version>([^<]+)</Version>");
        Assert.True(matched.Success, "Directory.Build.props 里没有 <Version>");
        var declared = matched.Groups[1].Value.Trim();

        // 三件事一次核完，用的全是应用那侧的真解析器（不是脚本里的字符串玩法）：
        // ① 那一串能被认回来；② 它已经是规范化形状（1.0 而不是 1.0.0 ⇒ 脚本算出的 zip 名与签名工具算出的那个不相等，
        //    签名当场拒，而这本可以在这里就红）；③ 标签口径"v + 那一串"也被解析成同一版。
        Assert.True(AppVersion.TryParse(declared, out var parsed), $"props 里那一串版本号解析不了：{declared}");
        Assert.Equal(declared, AppVersion.Describe(parsed));
        Assert.True(AppVersion.TryParse("v" + declared, out var tagged));
        Assert.Equal(0, AppVersion.Compare(parsed, tagged));
    }

    [Fact]
    public void EveryAssetNameComesFromTheContractRatherThanTheScript()
    {
        var code = PsCode(ReadRepoFile(PackageScript));
        // zip 的名字由版本算（应用那边也这么算，见 UpdateAssets.PackageNameFor 的注释）：
        // 这里把"$version"当成那颗函数的入参比一次字面，两侧一漂就红。
        var expectedPackage = "\"" + UpdateAssets.PackageNameFor("$version") + "\"";
        Assert.Contains(expectedPackage, code, StringComparison.Ordinal);
        Assert.Contains($"\"v$version\"", code, StringComparison.Ordinal);
        Assert.Contains(UpdateAssets.ManifestAssetName, code, StringComparison.Ordinal);
        Assert.Contains(UpdateAssets.SignatureAssetName, code, StringComparison.Ordinal);
    }

    // ===== 失败必须被读成失败 =====

    [Fact]
    public void ASigningRefusalIsNotReadAsAPublishedVersion()
    {
        var code = PsCode(ReadRepoFile(PackageScript));
        Assert.Matches(@"dotnet\s+run\s+--project[^\n]*StarMark\.UpdateSigner", code);
        // 签名工具"宁可发不出版"（私钥与内嵌公钥不是一对、包里没有主程序）时给的是非零退出码。
        // 不接这一句，一次拒签会被读成"三颗已经就位"，而发出去的那一版没人能装。
        Assert.True(Between(code, "StarMark.UpdateSigner", "$LASTEXITCODE").Length < 400,
            "签完之后必须立刻看退出码");
        Assert.Contains("throw", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecursiveCleanIsFencedToItsOwnScratchDirectory()
    {
        var code = PsCode(ReadRepoFile(PackageScript));
        var at = code.IndexOf("Remove-Item -Recurse -Force", StringComparison.Ordinal);
        Assert.True(at >= 0, "每一版都该从空目录产（残渣文件会被签进清单，然后跟着 zip 一直活下去）");
        // 递归删除之前必须先证明"要删的东西在 dist\stage 之下"：少了这道篱，一个传错的 -OutDir 就是一次删库。
        var fence = code[..at].Split('\n')
            .LastOrDefault(line => line.Contains("\"stage\"", StringComparison.Ordinal));
        Assert.NotNull(fence);
        Assert.Contains("throw", fence!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSigningKeyNeverTravelsIntoTheRepository()
    {
        // 私钥的落点只在那颗签名工具的缺省路径里（C# 一侧）；发布脚本不许碰它、更不许把它复制进仓库。
        // 这条不是洁癖：签名私钥泄露的后果是"攻击者能给所有在线安装签一份更新"。
        foreach (var (relative, text) in EveryPowerShellScript())
        {
            var code = PsCode(text);
            Assert.False(code.Contains(".pk8.pem", StringComparison.Ordinal),
                $"{relative} 里出现了私钥文件的名字（脚本不该经手它）");
            Assert.False(code.Contains("SecureString", StringComparison.Ordinal)
                || code.Contains("ConvertTo-SecureString", StringComparison.Ordinal),
                $"{relative} 不该自己经手凭据");
        }
    }

    [Fact]
    public void ReleaseArtifactsCannotBeCommitted()
    {
        // 一百多兆的产物与"这台机器这一版的全部字节"都不许被一次顺手 git add 推进仓库。
        // ⚠ 这里读的是**规则行**而不是子串：.gitignore 里的注释也写着 "dist/"，
        //   按子串断言会被自己那段说明喂饱（台架 V10 第一趟就是这么空转的——绿而没牙）。
        var rules = ReadRepoFile(".gitignore").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .ToList();
        Assert.Contains("dist/", rules);
        Assert.Contains("publish/", rules);
    }

    // ===== 建 Release 那一颗：凭据只进 header，公开只在账对完之后 =====

    [Fact]
    public void ThePublisherTargetsTheSameRepositoryTheAppProbes()
    {
        var code = PsCode(ReadRepoFile(PublishScript));
        // 桌面版问的是 UpdatePolicy.DefaultRepository，发布发到别处 ⇒ 那一版永远不被任何人看见，
        // 而两边的输出各自都"成功"。默认值必须与那颗常量同一串（C# 侧改了名而这里没跟上就红）。
        Assert.Contains($"\"{UpdatePolicy.DefaultRepository}\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCredentialThePublisherReadsIsNeverPrintedNorEmbedded()
    {
        var code = PsCode(ReadRepoFile(PublishScript));
        // 它读的是应用自己存的那把 Token（P-144 的裁决），所以这条不是形式：
        // 一次把 $token 打进输出台日志，就等于把那把"能给所有在线安装签更新"的钥匙抄进一堆没人管的文件里。
        Assert.Contains(".Token", code, StringComparison.Ordinal);          // 从应用的凭据文件读，不写死
        Assert.DoesNotMatch(@"Write-Host[^\n]*\$token", code);              // 不许被打出来
        Assert.DoesNotMatch(@"\$token[^\n]*(Out-File|Add-Content|Tee-Object)", code);   // 不许被写进文件
        Assert.DoesNotContain("github_pat_", code, StringComparison.Ordinal);   // 也不许被贴进脚本本身
        Assert.DoesNotContain("ghp_", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GoingLiveIsGatedOnReadingTheAssetsBackFromTheApi()
    {
        var code = PsCode(ReadRepoFile(PublishScript));
        var live = code.IndexOf("-Method Patch", StringComparison.Ordinal);
        Assert.True(live >= 0, "转正式那一步得是显式的 API 调用");
        // 对账那一次读回＝离公开最近的那次 GET。脚本里还会有"先看那个标签上挂着什么"的 GET，
        // 所以钉的是"公开之前最后一次读回"，而不是"第一次 GET 在第几次 POST 之后"这种位置巧合。
        var read = code.LastIndexOf("-Method Get", live, StringComparison.Ordinal);
        Assert.True(read >= 0, "公开之前必须从 API 读回来一次");
        // 读回来之后还要真比一次数，否则那句"读回来"只是又发一个请求（形状对、内容空的闸门最危险）
        Assert.Contains("-ne", code[read..live], StringComparison.Ordinal);
        // 三颗必须先挂上去再读回来对账（比对空气容易得多，也绿得多）
        var upload = code.IndexOf("upload_url", StringComparison.Ordinal);
        Assert.True(upload >= 0 && upload < read, "先把三颗挂上去，再读回来对账");
        // 公开这一步自己也不许被读成成功：PATCH 之后要再读一次，看它是不是真不再是草稿
        // （否则"发了"而装着的程序永远看不见那一版，而脚本的输出已经说了"已发布"）
        var after = code[live..];
        Assert.True(after.IndexOf("-Method Get", StringComparison.Ordinal) >= 0, "转正式之后要再读回来一次");
        Assert.Contains(".draft", after, StringComparison.Ordinal);
        Assert.True(after.Split('\n').Any(line => line.Contains(".draft", StringComparison.Ordinal)
            && line.Contains("throw", StringComparison.Ordinal)), "读到还是草稿就得拒绝收工");
    }

    [Fact]
    public void ReplacingALiveReleaseIsAllThreeOrNothingAndNeedsAnExplicitYes()
    {
        var code = PsCode(ReadRepoFile(PublishScript));
        var sw = Regex.Match(code, @"\[switch\]\s*\$(\w+)");
        Assert.True(sw.Success, "换已经公开的一版得由人当面点头：脚本要有个开关参数");
        var del = code.IndexOf("-Method Delete", StringComparison.Ordinal);
        var up = code.IndexOf("upload_url", StringComparison.Ordinal);
        Assert.True(del >= 0 && up > del, "先撤后传：同名资产不撤就传不干净");
        // 撤与传各只有一处，且都由同一个逐颗循环驱动——按颗点名（"只撤 zip"）留下的是"新包配旧清单"，
        // 那一版在用户那边永远审不过，而 Release 看起来三颗齐全。
        Assert.Single(Regex.Matches(code, "-Method Delete"));
        Assert.Single(Regex.Matches(code, "-InFile"));
        // 点头之前就得拒绝，而且拒绝要真拒绝（同一行 throw）；这里钉的是"那道拒绝用到了那个开关"，不钉开关叫什么
        var guard = code[..del].Split('\n').LastOrDefault(line => line.Contains(".draft", StringComparison.Ordinal));
        Assert.NotNull(guard);
        Assert.Contains("throw", guard!, StringComparison.Ordinal);
        Assert.Contains("$" + sw.Groups[1].Value, guard!, StringComparison.Ordinal);
        // 本地长度只许在传完之后读：拿它当"这颗要不要传"的判据会把旧签名留下——
        // 重新签一次名出来的串和原来一样长，长度证得了"传丢了没有"，证不了"内容变了没有"。
        Assert.True(code.IndexOf("(Get-Item", StringComparison.Ordinal) > up, "本地长度只能在传之后读，不能用来跳过上传");
    }

    [Fact]
    public void TheReconciliationUsesTheRemoteDigestWhenThereIsOne()
    {
        var code = PsCode(ReadRepoFile(PublishScript));
        // 摘要比字节数强：重新签一次名出来的串**和原来一样长**，只比长度的对账会把旧内容读成"没变"
        // （GitHub 在资产的 digest 字段里直接给 sha256，本地 Get-FileHash 就能对上——v1.0.0 那次实测逐字相同）。
        Assert.Contains("Get-FileHash", code, StringComparison.Ordinal);
        var digest = code.Split('\n')
            .FirstOrDefault(line => line.Contains(".digest", StringComparison.Ordinal)
                && line.Contains("-ne", StringComparison.Ordinal));
        Assert.NotNull(digest);                                   // 真比过一次摘要，不是又发一个请求
        Assert.Contains("throw", digest!, StringComparison.Ordinal);   // 只查不拒＝那句比较是装饰
        // 摘要不是保证有的（还没算出来、或那颗不是走 API 传的）：那种时候退回比长度，并把"退到了哪一档"说出来
        var fallback = code.Split('\n')
            .FirstOrDefault(line => line.Contains(".size", StringComparison.Ordinal)
                && line.Contains("-ne", StringComparison.Ordinal));
        Assert.NotNull(fallback);
        Assert.Contains("Write-Warning", code, StringComparison.Ordinal);
    }
}
