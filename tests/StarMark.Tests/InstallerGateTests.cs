#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 那颗 <c>setup.exe</c>（批次 VB；✅P-166：Inno Setup 造、只作首装入口、载荷维持 framework-dependent）的闸门。
/// <para><b>为什么这一族只能这么守</b>：安装包由 ISCC 在一台特定机器上编出来，测试跑不到它；
/// 而它错一次的表现都不是红：<b>无 BOM 的 UTF-8</b> 让向导里的中文变成乱码（构建 0 警告），
/// <b>把安装包塞进 <c>files</c></b> 让自更新去换一颗装不进目录的东西，
/// <b>过滤掉 <c>Updater\</c></b> 让装完的每一版永远更新不了（UH 那笔老账），
/// <b>名字两侧各算一份</b> 让发布页上少一颗而脚本说"四颗已就位"。全都属 #189/#193 那一族：一处事实两个出处。</para>
/// <para>所以这里钉的是<b>出处与形状</b>：那颗包只吃 publish-tree 那一棵树、版本只从 props 读、
/// 第三方工具必须验签＋钉哈希＋落在 D 盘，而它绝不出现在更新链的契约里。</para>
/// </summary>
public sealed class InstallerGateTests
{
    private const string Iss = "scripts/installer/StarMark.iss";
    private const string Maker = "scripts/make-installer.ps1";
    private const string PackageScript = "scripts/release-package.ps1";
    private const string PublishScript = "scripts/publish-release.ps1";

    /// <summary>那颗包的资产名口径：由版本算，与 zip 同一形状（不带 <c>v</c>）。</summary>
    private const string SetupLiteral = "\"StarMark-$version-setup.exe\"";

    private static string MakerCode() => PsCode(ReadRepoFile(Maker));
    private static string IssText() => ReadRepoFile(Iss);

    /// <summary>
    /// Inno 的注释有两种：<b>段注释以 <c>;</c> 开头，脚本注释用 <c>//</c></b>——禁项一律读抹掉两者后的文本。
    /// <para>与 <see cref="SourceGate.PsCode"/>、<see cref="SourceGate.Code"/> 同一条口径：
    /// 这颗 .iss 的解释性注释里写着"%LOCALAPPDATA% 刻意不列"和那棵<b>已经证伪的注册表键</b>的路径，
    /// 按原文数会把这两句解释读成违规（#123 同族：假失败教人把闸门改松而不是改对）。</para>
    /// </summary>
    private static string IssDirectives(string text)
        => string.Join("\n", text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0 && !l.TrimStart().StartsWith(';'))
            .Select(l =>
            {
                var cut = l.IndexOf("//", StringComparison.Ordinal);
                return cut >= 0 ? l[..cut] : l;
            }));

    // ===== 编码：这一族的失败只有用户看得见 =====

    /// <summary>
    /// <c>.iss</c> 与造包脚本必须 <b>UTF-8 带 BOM ＋ CRLF</b>。
    /// <para>ISCC 读无 BOM 的 UTF-8 时按 ANSI 码页解，中文提示会<b>被写成乱码编进 setup.exe</b>——
    /// 编译 0 警告、测试全绿，只有装的人看得见（#152 那一族）。PowerShell 那一半另有那颗 BOM 闸门，
    /// 但它只看 BOM 不看行尾，而 Inno 的续行与日志都按 CRLF 算，所以这一颗两个都钉。</para>
    /// </summary>
    [Fact]
    public void TheInstallerDefinitionIsUtf8WithBomAndWindowsLineEndings()
    {
        foreach (var relative in new[] { Iss, Maker })
        {
            var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                $"{relative} 没有 UTF-8 BOM——ISCC/PowerShell 会把中文的尾字节当双字节字吃掉");
            var text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            var loneLf = Regex.Matches(text, "(?<!\r)\n").Count;
            Assert.Equal(0, loneLf);   // 只许 CRLF：混进一条 LF 就等于两侧看到的行号不一样
        }
    }

    // ===== 名字与颗数：一个事实不许有两个出处 =====

    // ===== 失败必须被读成失败 =====

    /// <summary>
    /// 造安装包那一步失败<b>不许被读成"四颗已就位"</b>，而且读的必须是 <c>$?</c> 不是 <c>$LASTEXITCODE</c>。
    /// <para>被 <c>&amp;</c> 调用的是另一颗 <c>.ps1</c>：它内部的退出码归它自己管（那边对着 ISCC 查过了），
    /// 传回这一侧的只有"抛没抛"。按 <c>$LASTEXITCODE</c> 数会把上一颗<b>原生</b>程序的旧读数当成本步结果——
    /// 那正是"造包失败、脚本却绿着收尾"的形状（#258 同族：读到的数是谁写的，比它是不是零重要）。</para>
    /// </summary>
    [Fact]
    public void AnInstallerFailureIsNotReadAsAFourArtifactRelease()
    {
        var code = PsCode(ReadRepoFile(PackageScript));
        Assert.Single(Regex.Matches(code, @"scripts\\make-installer\.ps1"));   // 调它的那一处只许有一个
        var at = code.IndexOf("make-installer.ps1", StringComparison.Ordinal);
        Assert.True(at >= 0, "打包脚本不再造那颗安装包了");
        var after = code[(at + "make-installer.ps1".Length)..Math.Min(code.Length, at + 400)];
        var guard = after.IndexOf("-not $?", StringComparison.Ordinal);
        Assert.True(guard >= 0, $"造完安装包没立刻自证成没成（读到的是：{after[..40].Trim()}…）");
        Assert.DoesNotContain("LASTEXITCODE", after[..guard], StringComparison.Ordinal);
        Assert.Contains("throw", after[guard..(guard + 200)], StringComparison.Ordinal);   // 只查不拒＝那句检查是装饰
    }

    /// <summary>
    /// 那颗包的名字在<b>造它的、点名它的、传它的</b>三颗脚本里必须算出同一串字面。
    /// <para>这里刻意钉<b>字面</b>而不是变量名：谁改局部名字都是合法重构，不该逼出一条假失败（#123）；
    /// 但三处字面一漂，发布页上就会少一颗而脚本还说"四颗已就位"。</para>
    /// </summary>
    [Fact]
    public void TheSetupAssetNameIsComputedTheSameWayEverywhereItIsNamed()
    {
        Assert.Contains(SetupLiteral, MakerCode(), StringComparison.Ordinal);
        Assert.Contains(SetupLiteral, PsCode(ReadRepoFile(PackageScript)), StringComparison.Ordinal);
        Assert.Contains(SetupLiteral, PsCode(ReadRepoFile(PublishScript)), StringComparison.Ordinal);
    }

    /// <summary>
    /// 发布那一侧的资产表现在是<b>四颗</b>，而点名那一侧也是四颗。
    /// <para>颗数用计数钉：往表里多塞一颗（比如顺手把 <c>.iss</c> 或 <c>stage\</c> 传上去）同样该红。</para>
    /// </summary>
    [Fact]
    public void TheReleaseNowCarriesFourArtifacts()
    {
        var publish = PsCode(ReadRepoFile(PublishScript));
        Assert.Equal(4, Regex.Matches(publish, @"Mime\s*=\s*\x22").Count);
        var pkg = PsCode(ReadRepoFile(PackageScript));
        var rollCall = Between(pkg, "foreach ($f in @(", ") {");
        Assert.Equal(4, rollCall.Split(',').Length);
        Assert.Contains("四颗", pkg, StringComparison.Ordinal);
    }

    /// <summary>
    /// 那颗包<b>绝不在更新链的契约里</b>：Abstractions 那棵资产形状与签名工具都不许认识它。
    /// <para>这是 ✅P-166 第三条的机器形状：安装包只作首装入口，自更新仍是"验签 → 对载荷名 → 逐文件对字节 → 事务替换"。
    /// 一旦 <c>-setup.exe</c> 进了 <c>files</c>，审包那侧就会去对一颗根本不该由更新器摆放的字节。</para>
    /// </summary>
    [Fact]
    public void TheInstallerIsNeverPartOfTheAuditChain()
    {
        foreach (var (relative, text) in new[]
                 {
                     ("src/StarMark.Abstractions/Updates/UpdatePackage.cs", ReadRepoFile("src/StarMark.Abstractions/Updates/UpdatePackage.cs")),
                     ("tools/StarMark.UpdateSigner/Program.cs", ReadRepoFile("tools/StarMark.UpdateSigner/Program.cs")),
                 })
        {
            var code = Code(text);
            Assert.DoesNotContain("-setup.exe", code, StringComparison.Ordinal);
            Assert.DoesNotContain("setup.exe", code, StringComparison.OrdinalIgnoreCase);
        }
        // 资产名那一档只许有清单与签名两颗：多出一颗 InstallerAssetName 就意味着更新链开始认安装包了
        Assert.Equal(2, Regex.Matches(Code(ReadRepoFile("src/StarMark.Abstractions/Updates/UpdatePackage.cs")),
            @"public\s+const\s+string\s+\w+AssetName").Count);
    }

    // ===== 一棵树只有一个产树的人 =====

    /// <summary>
    /// 造包脚本<b>不许自己 publish</b>，只吃 publish-tree 产出的那棵树。
    /// <para>两处编译必漂移（#189/#193），而这里的漂移最阴：zip 与安装包都叫"这一版"，里面却不是同一批字节——
    /// 用户从哪条路装进来决定了他手上是哪一份。版本也从 props 读，与打包那颗脚本用同一颗正则。</para>
    /// </summary>
    [Fact]
    public void TheInstallerBuildsFromThePublishedTreeAndReadsTheVersionFromProps()
    {
        var code = MakerCode();
        Assert.DoesNotMatch(@"dotnet\s+publish", code);
        Assert.Contains("<Version>([^<]+)</Version>", code, StringComparison.Ordinal);
        // 版本号仍只许那一颗出处：脚本里不许出现第二串 x.y.z（Inno 自己的版本走 URL 与常量，不是产品版本）
        Assert.False(Regex.IsMatch(code.Replace("is-6_7_3", "").Replace("6.7.3", ""), @"\b\d+\.\d+\.\d+\b"),
            "造包脚本里出现了第二串产品版本号");
    }

    /// <summary>
    /// 残缺的树在<b>编译之前</b>就被拒绝，两半各一句，而且只查不拒不算。
    /// <para>少入口 = 装完双击没反应；少 <c>Updater\</c> = 这一版之后永远没法自动更新（UH 那笔老账）。</para>
    /// </summary>
    [Fact]
    public void ATreeMissingEitherHalfIsRefusedBeforeCompiling()
    {
        var code = MakerCode();
        foreach (var half in new[] { $"\"{UpdateAssets.EntryExeName}\"", $"\"{UpdaterPaths.UpdaterFolderName}\\{UpdaterPaths.UpdaterExeName}\"" })
        {
            // 钉"查了这颗、并且当场拒绝"，而不是"第几行有 Test-Path"：两半各查一颗，
            // 少一颗的症状都不红——没入口 = 装完双击没反应，少 Updater\ = 这一版之后永远没法自动更新（UH 那笔老账）。
            var at = code.IndexOf(half, StringComparison.Ordinal);
            Assert.True(at >= 0, $"这棵树的自证没再查 {half}");
            Assert.True(code[..at].Contains("Test-Path", StringComparison.Ordinal), $"查 {half} 的那一句不再是 Test-Path");
            Assert.Contains("throw", code[at..Math.Min(code.Length, at + 400)], StringComparison.Ordinal);
        }
    }

    // ===== 第三方工具：验两遍、落在 D 盘、装完还要认得它 =====

    /// <summary>
    /// 下载 Inno Setup 那一步<b>两道校验缺一不可</b>，而且安装位置必须是 D 盘（用户 2026-10-04 指定）。
    /// <para>签名只证"没被改过、来自该发布者"，钉哈希才证"我们认这一版"；缺任一道都拦不住"官方源上换了一颗我没打算用的"。
    /// 哈希那颗常量占位时<b>故意拒绝</b>——"这次先放过"会变成"以后都放过"（#258：没验过 ≠ 验过）。</para>
    /// </summary>
    [Fact]
    public void TheThirdPartyToolIsVerifiedTwiceAndStaysOnD()
    {
        var code = MakerCode();
        Assert.Contains("-ne \"Valid\"", code, StringComparison.Ordinal);                       // Authenticode 状态
        Assert.Contains("SignerCertificate.Subject", code, StringComparison.Ordinal);           // 发布者名，不看产品名猜
        Assert.Contains("Get-FileHash", code, StringComparison.Ordinal);
        Assert.Contains("UNPINNED", code, StringComparison.Ordinal);                            // 没钉住就拒绝那条路还在
        var hashGuard = code.Split('\n').FirstOrDefault(l => l.Contains("$PinnedSha256", StringComparison.Ordinal)
            && l.Contains("-ne", StringComparison.Ordinal));
        Assert.NotNull(hashGuard);
        Assert.Contains("throw", hashGuard!, StringComparison.Ordinal);                          // 只比不拒＝那颗常量是装饰
        var driveGuard = code.Split('\n').FirstOrDefault(l => l.Contains("D:", StringComparison.Ordinal)
            && l.Contains("throw", StringComparison.Ordinal));
        Assert.NotNull(driveGuard);                                                             // 不在 D 盘就当场拒绝
        // 装完了却找不到 ISCC 不许当成功：那一步之后直接编空气，报出来的是一条不相干的失败。
        // 钉"同一行既查了又拒了"，不钉"文件里有 if (-not $iscc)"——那颗块子的开头长得一模一样，
        // 按 FirstOrDefault 抓位置会把外层那个 if 读成拒绝（我这批就先红过一次）。
        Assert.True(code.Split('\n').Any(l => l.Contains("if (-not $iscc)", StringComparison.Ordinal)
            && l.Contains("throw", StringComparison.Ordinal)),
            "装完了找不到 ISCC 必须当场拒绝");
    }

    /// <summary>
    /// 那颗外部工具的<b>便携副本不许进版本库</b>。
    /// <para><c>*.exe</c>/<c>*.dll</c> 已被全局忽略，但 Inno 的树里还有 <c>.isl</c>／<c>.chm</c>／各种小文件，
    /// 一次 <c>git add</c> 就能把一百多颗第三方二进制推进仓库——而进了仓就再没人审它们。</para>
    /// </summary>
    [Fact]
    public void TheToolCopyIsIgnoredByGit()
    {
        var ignore = ReadRepoFile(".gitignore").Replace("\r\n", "\n");
        Assert.Contains("tools/InnoSetup6/", ignore, StringComparison.Ordinal);
    }

    // ===== .iss 里的三条行为纪律 =====

    /// <summary>
    /// <c>[Code]</c> 段里<b>不许出现以 <c>;</c> 开头的注释行</b>。
    /// <para>这不是风格：ISCC 只把 <c>//</c> 与 <c>{ }</c> 当脚本注释，<c>;</c> 在别的段是注释、在 <c>[Code]</c> 里
    /// 是空语句——回的是 <c>'BEGIN' expected</c>，编译直接断（2026-10-04 这颗闸门就是被它逼出来的）。
    /// 那一发如果只靠"发版时才发现"，代价是一整条流水线跑到最后一步炸。</para>
    /// <para>段起点按<b>行首形</b>认（整行恰好 <c>[Code]</c>），不能按"文中任意提到"认——
    /// 批次 WI-2x 自己的段说明注释里写了那句被加 [Code] 括号，第一次就骗过 IndexOf：
    /// 从那句注释起到文件尾全被当成脚本段，别处的正常 <c>;</c> 注释一起被误报。锚点跟形状走（#286）。</para>
    /// </summary>
    [Fact]
    public void TheCodeSectionNeverUsesSemicolonComments()
    {
        var iss = IssText();
        var at = -1;
        var offset = 0;
        foreach (var raw in iss.Split('\n'))
        {
            if (raw.TrimEnd('\r').Trim() == "[Code]") { at = offset; break; }
            offset += raw.Length + 1;
        }
        Assert.True(at >= 0, "这颗 .iss 里没有以行首形出现的 [Code] 段头（结构变了，闸门先跟上）");
        var bad = iss[at..].Split('\n').Select(l => l.TrimEnd('\r'))
            .Select((l, i) => (l, i)).Where(t => t.l.TrimStart().StartsWith(';')).ToList();
        Assert.True(bad.Count == 0,
            $"[Code] 段里有 {bad.Count} 行 ';' 注释——ISCC 会把它们当空语句，回 'BEGIN' expected（首现行内偏移 {bad.FirstOrDefault().i}）");
    }

    /// <summary>
    /// 运行时判定按<b>共享框架目录</b>，不许回到那棵注册表键。
    /// <para>本仓记过这条实测：这台装了 <c>9.0.12／9.0.19</c> 的机器上，
    /// <c>HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64</c> 底下只有 <c>sharedhost</c> 一颗，
    /// <c>sharedframework</c> 整棵不存在——按注册表查就会<b>对每一个有运行时的用户白问一次</b>，
    /// 而症状不是崩，是多余的一道框（P-54 的反面：能自己判的事不许问人）。</para>
    /// </summary>
    [Fact]
    public void TheRuntimeIsDetectedByTheSharedFrameworkFolderNotTheRegistry()
    {
        var directives = IssDirectives(IssText());   // 注释里写着那棵已被证伪的注册表路径，按原文数会把它读成违规
        Assert.Contains(@"dotnet\shared\Microsoft.WindowsDesktop.App", directives, StringComparison.Ordinal);
        Assert.DoesNotContain("InstalledVersions", directives, StringComparison.Ordinal);
        Assert.Contains("FindFirst", directives, StringComparison.Ordinal);      // 逐档看版本号，不猜某一颗固定版本
        Assert.Contains("FindClose", directives, StringComparison.Ordinal);      // 枚举句柄要关
        // 认的是"就是那一档主版本"，不是"不小于"：机器上只有更高的 10.x 时，宿主会不会跨大档回退不在这台的证据里，
        // 赌错的代价是用户双击没反应；保守判 9、其余走补装（side-by-side 不打架）。
        Assert.Contains("Major = RequiredMajor", directives, StringComparison.Ordinal);
        Assert.DoesNotContain("Major >= RequiredMajor", directives, StringComparison.Ordinal);
    }

    /// <summary>
    /// 运行时检测与自动补装那一刀必须落在 <c>PrepareToInstall</c>，<b>不许回到 <c>InitializeSetup</c></b>。
    /// <para>后者跑在<b>提权之前</b>：机器级装 .NET 桌面运行时那一次 MSI 必须要管理员，
    /// 放在那里的 winget 会稳定失败在权限上，然后每次都走到"问用户"那一步——症状是"明明能自动装却偏要问"。
    /// 而且"装好了"由<b>重新检测</b>说了算，不信 winget 自报的退出码。</para>
    /// </summary>
    [Fact]
    public void TheRuntimeStepRunsAfterElevationAndTrustsOnlyItsOwnRedetect()
    {
        var iss = IssText();
        Assert.Contains("function PrepareToInstall", iss, StringComparison.Ordinal);
        Assert.DoesNotContain("function InitializeSetup", iss, StringComparison.Ordinal);
        Assert.Contains("Result := DesktopRuntimeInstalled", iss, StringComparison.Ordinal);
        Assert.Contains("MaxRedetectTries", iss, StringComparison.Ordinal);   // 有界：坏注册表不许把向导钉成死循环
        Assert.Contains("ShellExec('open', RuntimePageUrl", iss, StringComparison.Ordinal);  // 给一条能点的出口，不写"请自己去装"
    }

    /// <summary>
    /// 安装包<b>默认落机器级目录、永不删用户数据</b>（✅P-161 同向：不许程序自己搬机身）。
    /// <para><c>[UninstallDelete]</c> 里出现 <c>LocalAppData</c> 就是"卸载一个程序"顺手删掉用户的库、日志与剪贴板图片——
    /// 那件事的不可逆性与"卸载"完全不成比例；清理要从程序内的数据维护入口做。</para>
    /// </summary>
    [Fact]
    public void TheInstallerDefaultsToThePerUserWritableFolderAndNeverDeletesUserData()
    {
        var iss = IssText();
        Assert.Contains(@"DefaultDirName={localappdata}\Programs\{#MyAppName}", iss, StringComparison.Ordinal);
        Assert.Contains("PrivilegesRequired=lowest", iss, StringComparison.Ordinal);
        Assert.Contains("PrivilegesRequiredOverridesAllowed=dialog", iss, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivilegesRequired=admin", iss, StringComparison.Ordinal);
        Assert.DoesNotContain(@"DefaultDirName={autopf}", iss, StringComparison.OrdinalIgnoreCase);
        // 禁项读**抹掉注释后**的那一段：注释里写"%LOCALAPPDATA%\StarMark 刻意不列"是为了讲清为什么，
        // 按原文数会把这句解释当成违规（#123 同一条口径，Inno 的注释行以 ; 开头）。
        var directives = IssDirectives(Between(iss, "[UninstallDelete]", "[Code]"));
        Assert.DoesNotContain("LocalAppData", directives, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StarMark.UI.exe", directives, StringComparison.Ordinal);   // 卸载只由 Inno 登记的清单决定
    }

    /// <summary>
    /// 缺运行时那一刀：<b>没有管理员权限时不许去跑那发机器级 winget</b>。
    /// <para>默认形状是每用户安装（不提升），而 winget 装 .NET 桌面运行时走的是 MSI 机器级那一档 ⇒
    /// 非提升时它<b>注定失败</b>。白跑一次的代价不是慢，是用户看见"自动安装没成功"并把它读成
    /// winget 坏了——那是替自己的权限模型编一个假原因（坑表 #261 同族）。</para>
    /// <para>⚠ 一条实测事实，别拿这条闸门当"这台机器已经是 per-user"的证据：同一 <c>AppId</c> 只要机器上
    /// 还留着"为所有用户装"的记录，Inno 就沿用那一档模式与那个目录去升级（VC 那跑的 setup log：
    /// <c>Administrative install mode: Yes</c>、<c>Directory for uninstall files: D:\StarMark</c>）。
    /// 所以"默认每用户"只对<b>新机器</b>成立；老机器要先卸干净才会落到 <c>%LOCALAPPDATA%\Programs</c>。</para>
    /// </summary>
    [Fact]
    public void TheRuntimeStepOnlyTriesWingetInAnElevatedInstall()
    {
        var iss = IssText();
        // Pascal 的函数体不是大括号配对，SourceGate.MethodBody（C# 那把刀）在这里切不出来 ⇒ 按段首切。
        var body = IssDirectives(Between(iss, "function PrepareToInstall", "procedure CurStepChanged"));
        Assert.Contains("if IsAdminInstallMode then", body, StringComparison.Ordinal);
        Assert.True(body.IndexOf("if IsAdminInstallMode then", StringComparison.Ordinal)
                    < body.IndexOf("TryInstallRuntimeWithWinget", StringComparison.Ordinal),
            "winget 那一发必须坐在 IsAdminInstallMode 的分支里——非提升时它注定失败");
        Assert.Equal(1, Count(body, "TryInstallRuntimeWithWinget"));
        Assert.Equal(1, Count(body, "IsAdminInstallMode"));
    }

    /// <summary>
    /// <c>[Files]</c> 整棵搬进去，<b>不许带 <c>Exclude:</c></b>。
    /// <para>过滤（比如"不装 Updater\ 省点字节"）会静默砍掉自更新能力，而那比"包里多一颗文件"严重得多；
    /// 名字也不许在这里再算一次——那颗叫法由 <c>/D</c> 传进来。</para>
    /// </summary>
    [Fact]
    public void TheInstallerCopiesTheWholeTreeWithoutFiltering()
    {
        var files = IssDirectives(Between(IssText(), "[Files]", "[Icons]"));
        Assert.Contains("recursesubdirs", files, StringComparison.Ordinal);
        Assert.DoesNotContain("Exclude:", files, StringComparison.Ordinal);
        Assert.Contains("{#SourceDir}", files, StringComparison.Ordinal);   // 树由脚本传，不在这里写死
    }
}
