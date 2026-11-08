#nullable enable
using StarMark.Abstractions;
using StarMark.Integrations.Bookmarks;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 片247（坑 #1171）护栏：**浏览器未安装是装机差异，不是枚举故障**。
/// <para>现场：本机没装 Chrome（<c>…\Google\Chrome\User Data</c> 不存在），书签同步每轮都从
/// <c>DiscoverProfiles</c> 抛 <c>DirectoryNotFoundException</c>，被 catch 接住后 WARN 同一句话
/// ——今天一天 8 条，全是同一颗「装机差异被当成故障」的噪声。真机的可用性判断
/// （<c>IsAvailable</c>＝File.Exists）与可用性提示（只递路径事实）都不需要这条 WARN。</para>
/// <para>刀法：枚举前先 <c>Directory.Exists</c>——User Data 不在场就只交 Default 那条、不枚举、不记日志；
/// 在场时照旧枚举，真正的枚举失败（权限/网络盘）仍走 catch＋WARN（那才是故障）。</para>
/// </summary>
public sealed class BrowserProfileAbsenceGateTests : IDisposable
{
    private const string BrowserSource = "src/StarMark.Integrations/Bookmarks/BrowserBookmarksSource.cs";

    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { Directory.Delete(d, recursive: true); } catch { /* 临时目录清理失败不影响断言 */ }
    }

    private const string ValidJson =
        @"{""roots"":{""bookmark_bar"":{""type"":""folder"",""name"":""Bar"",""children"":[" +
        @"{""type"":""url"",""name"":""GitHub"",""url"":""https://github.com/"",""date_added"":""13300000000000000""}," +
        @"{""type"":""folder"",""name"":""Dev"",""children"":[" +
        @"{""type"":""url"",""name"":""Work"",""url"":""https://work.example/intranet""}]}]}}}";

    private const string SecondProfileJson =
        @"{""roots"":{""bookmark_bar"":{""type"":""folder"",""name"":""Bar"",""children"":[" +
        @"{""type"":""url"",""name"":""Work2"",""url"":""https://work2.example/intranet""}]}}}";

    /// <summary>造一个 Chromium 风格的 …\&lt;root&gt;\User Data\&lt;profileDir&gt;\Bookmarks，返回该文件路径。</summary>
    private string WriteProfileBookmarks(string profileDir, string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "starmark-absence-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "User Data", profileDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Bookmarks");
        File.WriteAllText(path, json);
        _tempDirs.Add(root);
        return path;
    }

    /// <summary>从日志档的 <paramref name="fromByte"/> 偏移读到末尾，找那句话——只断言**本测试窗口内新增**的段。</summary>
    private static bool LogAppendedContains(string logFile, long fromByte, string phrase)
    {
        using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(fromByte, SeekOrigin.Begin);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd().Contains(phrase, StringComparison.Ordinal);
    }

    /// <summary>
    /// 主断言（行为）：Chrome／Edge 没装（User Data 整个不在场）时——可用性照旧交 false、
    /// 同步照旧交空，但**不许再往日志里 WARN「枚举失败」**。沙盒日志由
    /// <c>TestSandboxRedirect</c> 改道，这里按偏移只看本窗口新增段；断言的短语带上
    /// 「edge」这颗源名，免得并行的其他集合偶发日志行混进窗口被误算。
    /// </summary>
    [Fact]
    public async Task AbsentBrowserInstall_IsMachineDifference_NotWarnNoise()
    {
        var root = Path.Combine(Path.GetTempPath(), "starmark-absence-" + Guid.NewGuid().ToString("N"));
        _tempDirs.Add(root);
        var bookmarksPath = Path.Combine(root, "User Data", "Default", "Bookmarks");

        var logFile = StarLog.CurrentLogFile;
        long before = File.Exists(logFile) ? new FileInfo(logFile).Length : 0;

        var source = new EdgeBookmarksSource(bookmarksPath);
        Assert.False(source.IsAvailable);
        Assert.Empty((await source.FetchAsync(new SyncContext(), CancellationToken.None)).Items);

        StarLog.FlushNow();
        if (File.Exists(logFile))
            Assert.False(LogAppendedContains(logFile, before, "枚举 edge 的浏览器 profile 目录失败"),
                "浏览器未安装是装机差异：不许每轮同步都 WARN 一遍「枚举失败」");
    }

    /// <summary>
    /// 护栏（行为）：缺席前置检查不许把「在场」的那一半也吞了——User Data 在场时
    /// Default 与 <c>Profile 1</c> 照旧全数读到（这是多账号机器书签能进来的主路径）。
    /// </summary>
    [Fact]
    public async Task PresentUserData_StillDiscoversDefaultAndProfileSiblings()
    {
        var defaultPath = WriteProfileBookmarks("Default", ValidJson);
        var userData = Path.GetDirectoryName(Path.GetDirectoryName(defaultPath))!;
        Directory.CreateDirectory(Path.Combine(userData, "Profile 1"));
        File.WriteAllText(Path.Combine(userData, "Profile 1", "Bookmarks"), SecondProfileJson);

        var source = new EdgeBookmarksSource(defaultPath);
        var items = (await source.FetchAsync(new SyncContext(), CancellationToken.None)).Items;

        Assert.Equal(3, items.Count);   // Default 2 条 + Profile 1 的 1 条
        Assert.Contains(items, i => i.Uri == "https://work2.example/intranet");
    }

    /// <summary>
    /// 护栏（源码）：真正的枚举失败（User Data 在场但权限/网络盘出问题）仍要有 catch＋WARN
    /// ——「枚举失败只读 Default」那条退路不许因为本刀被顺手拆掉。
    /// </summary>
    [Fact]
    public void RealEnumerationFailures_KeepTheCatchAndWarn()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(BrowserSource),
            "private IReadOnlyList<string> DiscoverProfiles()");
        Assert.Contains("catch (Exception ex)", body);
        Assert.Contains("StarLog.Warn($\"枚举 {SourceId} 的浏览器 profile 目录失败", body);
    }

    /// <summary>护栏（源码）：缺席判定必须在枚举动作之前——先判 <c>Directory.Exists</c> 再 <c>EnumerateDirectories</c>。</summary>
    [Fact]
    public void AbsenceIsPrechecked_BeforeTheEnumeration()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(BrowserSource),
            "private IReadOnlyList<string> DiscoverProfiles()");
        var precheck = body.IndexOf("if (!Directory.Exists(userData)) return list;", StringComparison.Ordinal);
        var enumeration = body.IndexOf("Directory.EnumerateDirectories(userData)", StringComparison.Ordinal);
        Assert.True(precheck >= 0, "缺席前置检查（Directory.Exists）必须在场");
        Assert.True(enumeration > precheck, "先判在场再枚举：前置检查必须排在 EnumerateDirectories 之前");
    }

    /// <summary>护栏（源码）：缺席时不许顺手把「同步幂等」的既有承诺改了——profile 枚举仍要排序稳定。</summary>
    [Fact]
    public void ProfileOrdering_StaysStable_ForSyncIdempotence()
    {
        var body = SourceGate.MethodBody(SourceGate.ReadRepoFile(BrowserSource),
            "private IReadOnlyList<string> DiscoverProfiles()");
        Assert.Contains(".OrderBy(d => d, StringComparer.Ordinal)", body);
    }
}
