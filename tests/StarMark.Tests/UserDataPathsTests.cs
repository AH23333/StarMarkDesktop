#nullable enable
using System;
using System.IO;
using System.Linq;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Core.Feed;
using StarMark.Core.Widgets;
using StarMark.Integrations.GitHub;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 用户数据档的落点只许有一条约定（批次 SS）。
/// <para>
/// 起因是内存基线的第一跑：采样进程明明把 <c>STARMARK_DB_PATH</c> 指进了沙盒，
/// 他真实目录里那份 <c>widgets.json</c> 却仍在每一次 <c>dotnet test</c> 后被整档重写
/// （连带 <c>.bak</c>）。查下去是<b>五份档各抄一遍优先规则，其中 <c>GitHubOptions</c> 那一份漏掉了环境变量</b>
/// ——而"备份导出读默认路径、还原写默认路径"这条链一旦落在真目录，测试就在动他的桌面组件配置。
/// 漏掉的那一份不会报错，它只是<b>永远写到真目录去</b>：改道、沙盒、迁移，对它都无效，
/// 而那正是这套约定存在的唯一理由。
/// </para>
/// <para>
/// 所以这里钉三件事：五份档必须同住一个目录（形状）、测试进程里那份真目录一个都不许被指向（后果）、
/// 两个环境变量各自的射程（<c>STARMARK_SETTINGS_PATH</c> 给整条路径且不顶掉主库；
/// <c>STARMARK_DB_PATH</c> 一处带走整包）。
/// </para>
/// </summary>
[CollectionDefinition("UserDataPaths")]
public class UserDataPathsCollection { }

[Collection("UserDataPaths")]
public sealed class UserDataPathsTests
{
    private static (string? Settings, string? Db) Capture() => (
        Environment.GetEnvironmentVariable(UserDataPaths.SettingsVariable),
        Environment.GetEnvironmentVariable(UserDataPaths.DatabaseVariable));

    private static void Restore((string? Settings, string? Db) saved)
    {
        Environment.SetEnvironmentVariable(UserDataPaths.SettingsVariable, saved.Settings);
        Environment.SetEnvironmentVariable(UserDataPaths.DatabaseVariable, saved.Db);
    }

    /// <summary>五份档的名字与所在目录一起钉：少一份、多一份、或某一份跑出这个目录，都是红。</summary>
    [Fact]
    public void AllFiveUserDataFilesSitInTheSameDirectory()
    {
        var files = new (string Name, string Path)[]
        {
            ("starmark.db", UserDataPaths.Database()),
            ("settings.json", UserDataPaths.Settings()),
            ("widgets.json", WidgetStorage.DefaultPath()),
            ("rss-cache.json", RssCacheStore.DefaultPath()),
            ("github.json", GitHubOptions.DefaultConfigPath),
        };

        foreach (var (name, path) in files)
        {
            Assert.Equal(name, Path.GetFileName(path));
            Assert.Equal(TestSandboxRedirect.Root, Path.GetDirectoryName(path));
        }
    }

    /// <summary>
    /// 沙盒生效的<b>后果式</b>证人：断言"没有一份默认落点指回真实用户目录"。
    /// <para>上一条钉的是形状，这一条钉的才是这次真正伤到人的那件事——它会在有人删掉
    /// <see cref="TestSandboxRedirect"/> 里那一行环境变量时转红，哪怕形状仍然自洽。</para>
    /// </summary>
    [Fact]
    public void NoDefaultPointsBackAtTheRealUserProfile()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.AppName);
        var paths = new[]
        {
            UserDataPaths.Database(), UserDataPaths.Settings(), WidgetStorage.DefaultPath(),
            RssCacheStore.DefaultPath(), GitHubOptions.DefaultConfigPath,
        };
        foreach (var path in paths)
            Assert.False(path.StartsWith(real, StringComparison.OrdinalIgnoreCase),
                $"这一份默认落点指回了真实用户目录：{path}（跑一次测试就动一次他的档）");
    }

    [Fact]
    public void TheDatabaseVariableTakesTheWholeSetTogether()
    {
        var saved = Capture();
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), $"udp_db_{Guid.NewGuid():N}");
            Environment.SetEnvironmentVariable(UserDataPaths.SettingsVariable, null);
            Environment.SetEnvironmentVariable(UserDataPaths.DatabaseVariable, Path.Combine(dir, "starmark.db"));

            Assert.Equal(Path.Combine(dir, "starmark.db"), UserDataPaths.Database());
            // 这一条就是沙盒取证的全部前提：指一处，五份档一起走（少了哪一份，那一份就在真目录里被读写）
            Assert.Equal(Path.Combine(dir, "settings.json"), UserDataPaths.Settings());
            Assert.Equal(Path.Combine(dir, "widgets.json"), WidgetStorage.DefaultPath());
            Assert.Equal(Path.Combine(dir, "rss-cache.json"), RssCacheStore.DefaultPath());
            Assert.Equal(Path.Combine(dir, "github.json"), GitHubOptions.DefaultConfigPath);
        }
        finally { Restore(saved); }
    }

    /// <summary>
    /// <c>STARMARK_SETTINGS_PATH</c> 的两条边界：它给的是 settings.json 的<b>整条路径</b>（原样用，不取目录），
    /// 而它<b>不许</b>顶掉主库位置——否则一份别的库上的设置档会把用户的主库搬走。
    /// </summary>
    [Fact]
    public void TheSettingsVariableTakesItsWholePathButNeverTheDatabase()
    {
        var saved = Capture();
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), $"udp_settings_{Guid.NewGuid():N}");
            Environment.SetEnvironmentVariable(UserDataPaths.SettingsVariable, Path.Combine(dir, "settings.json"));
            Environment.SetEnvironmentVariable(UserDataPaths.DatabaseVariable, null);

            Assert.Equal(Path.Combine(dir, "settings.json"), UserDataPaths.Settings());
            Assert.Equal(Path.Combine(dir, "widgets.json"), WidgetStorage.DefaultPath());
            Assert.Equal(Path.Combine(dir, "github.json"), GitHubOptions.DefaultConfigPath);
            Assert.Equal(Path.Combine(dir, "rss-cache.json"), RssCacheStore.DefaultPath());

            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.AppName);
            Assert.Equal(Path.Combine(appData, "starmark.db"), UserDataPaths.Database());
        }
        finally { Restore(saved); }
    }

    /// <summary>
    /// 约定只剩一处：两个变量名在 <c>src</c> 的代码行里只许出现在 <see cref="UserDataPaths"/>。
    /// <para>只看代码行——"为什么用这两个变量"的解释本来就该写在注释里，连注释一起数会变成守门
    /// 被自己的说明打红（这条坑记过不止一次）。缺这一条，下一份抄本可以带着自己新读的一个变量名
    /// 悄悄长在第三个文件里，而上面四条行为测全都照样绿。</para>
    /// </summary>
    [Fact]
    public void OnlyOneFileReadsTheTwoEnvironmentVariables()
    {
        var offenders = SourceGate.ReadRepoUnder("src")
            .Where(file => file.Text.Split('\n').Any(line =>
            {
                var text = line.Trim();
                return !text.StartsWith("//")
                    && (text.Contains(UserDataPaths.DatabaseVariable, StringComparison.Ordinal)
                        || text.Contains(UserDataPaths.SettingsVariable, StringComparison.Ordinal));
            }))
            .Select(file => file.RelativePath.Replace('\\', '/'))
            .ToList();

        Assert.Equal(new[] { "src/StarMark.Abstractions/UserDataPaths.cs" }, offenders);
    }

    /// <summary>尺子不许把"五份档同住"写成一句口号：<c>Sibling</c> 与另两个出口必须真的同源。</summary>
    [Fact]
    public void SiblingAndTheTwoNamedFilesComeFromOneRule()
    {
        Assert.Equal(UserDataPaths.Sibling("settings.json"), UserDataPaths.Settings());
        Assert.Equal(Path.GetDirectoryName(UserDataPaths.Database()),
            Path.GetDirectoryName(UserDataPaths.Sibling("anything.json")));
    }

    /// <summary>
    /// 一颗<b>子目录</b>也必须跟着那份优先规则走（片23：画板档那一族从此问这里要根）。
    /// <para>为什么单独一格：上面那格只管"档"，而画板那一族住的是目录。目录一旦自己拼 <c>%APPDATA%\StarMark</c>，
    /// 它就落在整套改道之外——沙盒与单测对它⛔ 生效（那份抄本不会报错，它只是永远写进真目录，正是本档开头那颗坑）。
    /// 这里钉的是<b>同一条规则的两个出口</b>：档与目录必须同住一颗根，⛔ 一个跟着走、一个留在原地。</para>
    /// </summary>
    [Fact]
    public void ASubdirectoryTravelsWithTheSameRedirect()
    {
        var saved = Capture();
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), $"udp_dir_{Guid.NewGuid():N}");
            Environment.SetEnvironmentVariable(UserDataPaths.SettingsVariable, null);
            Environment.SetEnvironmentVariable(UserDataPaths.DatabaseVariable, Path.Combine(dir, "starmark.db"));

            Assert.Equal(Path.Combine(dir, "boards"), UserDataPaths.Subdirectory("boards"));
            // 目录与档同源：db 变量带走档的那一格，必须同时带走目录那一格
            Assert.Equal(Path.GetDirectoryName(UserDataPaths.Sibling("anything.json")),
                Path.GetDirectoryName(UserDataPaths.Subdirectory("boards")));
        }
        finally { Restore(saved); }
    }

    /// <summary>
    /// <b>一颗目录</b>跟着改道走还不够：那一族里还住着一份<b>会被删</b>的档（片122）。
    /// <para>起因是 P-192 欠的那格屏幕读数——「历史图片行读不出 PNG ⇒ 提示卡上那句为什么」。
    /// 要造那颗现场，得往<b>沙盒自己那份库</b>插一行、再往它认得的图片目录种一颗非 PNG 字节的档。
    /// 而 <c>ClipAssets</c> 那棵根当时硬拼 <c>%LOCALAPPDATA%\StarMark</c>，⛔ 认任何一颗环境变量
    /// ⇒「递进沙盒自己那份库」这句根本走不到：种下去的档与轮转/删除/对账都落在他<b>真图目录</b>里
    /// ——正是本档开头那颗坑（一份⛔ 认改道的抄本不会报错，它只是永远读写真目录）换了个宿主。</para>
    /// <para>两臂各钉一件事：<b>没改道时⛔ 搬走</b>（图片目录一旦从 <c>%LOCALAPPDATA%</c> 挪窝，
    /// 他库里每一行历史图片当场变「已经不在这台机器上」，那是拿修取证伤用户）；
    /// <b>改道时必须跟着走</b>（这一格才是"取证跑⛔ 再删他真图"的依据——写侧、删侧、对账侧共用同一颗 <see cref="ClipAssets.Folder"/>，
    /// 所以只要这一颗跟着走，整族一起进来）。</para>
    /// </summary>
    [Fact]
    public void TheClipImageDirectoryTravelsWithTheRedirectButNeverMovesByDefault()
    {
        var saved = Capture();
        var savedOverride = ClipAssets.FolderOverride;
        try
        {
            ClipAssets.FolderOverride = null;
            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.AppName);

            Environment.SetEnvironmentVariable(UserDataPaths.SettingsVariable, null);
            Environment.SetEnvironmentVariable(UserDataPaths.DatabaseVariable, null);
            Assert.Equal(Path.Combine(local, "clip"), ClipAssets.Folder);

            var dir = Path.Combine(Path.GetTempPath(), $"udp_clip_{Guid.NewGuid():N}");
            Environment.SetEnvironmentVariable(UserDataPaths.DatabaseVariable, Path.Combine(dir, "starmark.db"));
            Assert.Equal(Path.Combine(dir, "clip"), ClipAssets.Folder);
            Assert.False(ClipAssets.Folder.StartsWith(local, StringComparison.OrdinalIgnoreCase),
                $"改道之后那颗图片目录仍指回真目录：{ClipAssets.Folder}（一次沙盒跑就能轮转／删掉他的真图）");
        }
        finally
        {
            ClipAssets.FolderOverride = savedOverride;
            Restore(saved);
        }
    }

    /// <summary>
    /// 那颗「有没有被改道」的信号（片39 为产物那一族新开的出口）：没改道交 <b>null</b>，⛔ 把真目录当改道结果递出去。
    /// <para>为什么值得单独钉：那一棵 <c>%APPDATA%\StarMark</c> 是<b>程序内部档</b>的根，而画板那颗产物目录平时
    /// 住的⛔ 是它。把"默认落哪一棵"混进这颗信号，UI 那边就分不出"该跟着改道"与"该进图片目录"这两件事。</para>
    /// </summary>
    [Fact]
    public void TheRedirectSignalSaysNothingWhenNothingWasRedirected()
    {
        var saved = Capture();
        try
        {
            Environment.SetEnvironmentVariable(UserDataPaths.SettingsVariable, null);
            Environment.SetEnvironmentVariable(UserDataPaths.DatabaseVariable, null);

            Assert.Null(UserDataPaths.RedirectedDataRoot());
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.AppName);
            Assert.Equal(Path.Combine(appData, "boards"), UserDataPaths.Subdirectory("boards"));
        }
        finally { Restore(saved); }
    }

    /// <summary>
    /// 那颗信号与 <see cref="UserDataPaths.Subdirectory"/> 同住一颗优先规则（settings ＞ db ＞ 真目录）。
    /// <para>这一族最坏的坏法不是它报错，而是它<b>安静地写到真目录去</b>：那份抄本⛔ 认改道（批次 SS 的原件就是
    /// <c>GitHubOptions</c> 漏了 db 那一档）。这里连"settings 赢过 db"那一臂一起钉——两条各测一件事，
    /// 而把优先规则抄第二份时，两臂里至少有一颗会红。</para>
    /// </summary>
    [Fact]
    public void TheRedirectSignalAndTheSubdirectoryShareOnePrecedenceRule()
    {
        var saved = Capture();
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), $"udp_signal_{Guid.NewGuid():N}");
            Environment.SetEnvironmentVariable(UserDataPaths.SettingsVariable, null);
            Environment.SetEnvironmentVariable(UserDataPaths.DatabaseVariable, Path.Combine(dir, "starmark.db"));

            Assert.Equal(dir, UserDataPaths.RedirectedDataRoot());
            Assert.Equal(Path.Combine(dir, "boards"), UserDataPaths.Subdirectory("boards"));

            Environment.SetEnvironmentVariable(UserDataPaths.SettingsVariable, Path.Combine(dir, "settings.json"));
            Environment.SetEnvironmentVariable(UserDataPaths.DatabaseVariable, Path.Combine(dir, "other", "starmark.db"));
            Assert.Equal(dir, UserDataPaths.RedirectedDataRoot());
        }
        finally { Restore(saved); }
    }

    /// <summary>那颗优先规则只许有一颗主人：把"settings 给整条路径、db 给目录"那段<b>读法</b>抄第二份，
    /// 比"变量名出现在第二个文件里"更常发生也⛔ 被上一格扫到（上一格只认文件，而抄本就在同一档里）。
    /// <para>⚠ 这里⛔ 数 <c>GetEnvironmentVariable(...)</c> 那一串：那颗变量本来就有正当的第二读者
    /// （<c>Database()</c> 与 <c>Settings()</c> 各读自己那一颗），按它数会把合法读法当抄本——本批第一跑就是这么红的，
    /// 于是改成钉<b>那段解析读法的那一行</b>只许出现一次＋那棵根＝「信号 ?? 真目录」一条路。</para></summary>
    [Fact]
    public void ThePrecedenceRuleIsReadExactlyOnceInsideTheOwner()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.Abstractions/UserDataPaths.cs"));
        Assert.Equal(1, SourceGate.Count(code, "Path.GetDirectoryName(settingsOverride)"));
        // ⛔ 数 <c>GetEnvironmentVariable(DatabaseVariable)</c> 那一串：主库档自己认那颗变量，
        // 那是<b>另一件事</b>（一处读法≠一份优先规则），按它数会把合法读者算成抄本（本批第一跑红的就是这一格）。
        // 那棵根＝「改道信号 ?? 真目录」，⛔ 第三条路（把那段 if 抄回 RootDirectory 就是第二条路）
        Assert.Contains("private static string RootDirectory() => RedirectedDataRoot() ?? AppDataDirectory();",
            code, StringComparison.Ordinal);
    }
}
