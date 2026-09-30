#nullable enable
using System;
using System.IO;
using System.Linq;
using StarMark.Abstractions;
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
}
