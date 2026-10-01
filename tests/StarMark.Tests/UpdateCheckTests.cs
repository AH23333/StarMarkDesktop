#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// "检测版本更新"的判据与编排（批次 UE）。
/// <para>
/// 要钉住的两类东西都不在界面上：
/// ① <b>版本号怎么比</b>——判错的后果不是崩，是"每天告诉你有新版而你已经装着最新"，
///    或反过来"新版来了它说已最新"；两种都会让人把这条功能关掉，而它唯一的作用就是别错过。
/// ② <b>查不到时说什么</b>——失败不可怕，可怕的是并成一句"检查失败"（P-54 判过的那种"把要做的事推回给人"）。
/// </para>
/// <para>
/// 断言里<b>一律用注入的版本号与注入的时刻</b>，不拿真产物版本、不拿真时钟（#212 那一族：
/// 钉住一个会变的数，得到的只是一条随时序飘红的"契约"）。真装配那一条只钉<b>形状</b>。
/// </para>
/// </summary>
public sealed class UpdateCheckTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    // ===== 版本号解析：认什么、不认什么 =====

    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]                // GitHub 标签的惯例
    [InlineData("V1.2.3", 1, 2, 3, null)]
    [InlineData("  v1.2.3  ", 1, 2, 3, null)]            // JSON 字段里带空白很常见
    [InlineData("1.2", 1, 2, 0, null)]                  // 缺的段当 0：写标签的人省一截
    [InlineData("1", 1, 0, 0, null)]
    [InlineData("1.2.3-beta.2", 1, 2, 3, "beta.2")]
    [InlineData("1.2.3+build.7", 1, 2, 3, null)]         // 构建元数据不参与"谁更新"
    [InlineData("1.2.3-beta.2+build.7", 1, 2, 3, "beta.2")]
    public void ParsesTheShapesAReleaseTagActuallyComesIn(
        string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(AppVersion.TryParse(text, out var v), $"这一串认不下来：{text}");
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
        Assert.Equal(pre, v.PreRelease);
    }

    /// <summary>
    /// 认不回来就必须说"认不回来"。兜一个 0.0.0 会把"远端标签写歪了"变成"你这份太旧"，
    /// 于是那条提示永远点不通，而它看起来完全合理（#224 那一族：看起来像量到了比空白更误导人）。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("1.2.x")]
    [InlineData("1..3")]
    [InlineData("ver1.2.3")]
    [InlineData("v")]
    [InlineData("1.2.3.4")]                              // 四段是装配版本号，不是发布标签
    [InlineData("99999999999.1.1")]                       // 装不进 int
    public void UnparsableVersionsAreRejectedRatherThanGuessed(string? text)
        => Assert.False(AppVersion.TryParse(text, out _), $"这一串不该被认成版本：{text}");

    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]                   // 尾段大＝更新
    [InlineData("1.2.3", "1.3.0", -1)]                  // 进位时低位清零不许被读成"更旧"
    [InlineData("1.9.9", "1.10.0", -1)]                 // 按数值比；按字面 "9" &gt; "1"，这里就会翻转
    [InlineData("2.0.0", "10.0.0", -1)]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2", "1.2.0", 0)]                     // 缺段当 0 ⇒ 同一个版本，不许报"有新版"
    [InlineData("1.2.3", "1.2.3+build.9", 0)]           // 构建元数据不参与
    [InlineData("1.2.4", "1.2.3", 1)]                   // 本机更新（预发布或自己编的）
    [InlineData("1.2.3-beta.2", "1.2.3", -1)]           // 同号的预发布排在正式版之前：本机是 beta 而正式版已发 ⇒ 有新版本
    [InlineData("1.2.3", "1.2.3-beta.2", 1)]            // 反方向：正式版不该被自己的 beta 说成"有新版"
    [InlineData("1.2.3-beta.9", "1.2.3-beta.10", -1)]   // 测试序号也按数值
    [InlineData("1.2.3-alpha", "1.2.3-beta", -1)]       // 字母段按字面
    [InlineData("1.2.3-beta.1", "1.2.3-beta.1.1", -1)]  // 前缀相同，标识符多的更大
    public void CompareDecidesNewerOlderOrSame(string local, string remote, int expected)
    {
        Assert.True(AppVersion.TryParse(local, out var a));
        Assert.True(AppVersion.TryParse(remote, out var b));
        Assert.Equal(expected, Math.Sign(AppVersion.Compare(a, b)));
    }

    /// <summary>比较必须反对称：方向判反一次，"有新版"与"已最新"就整个对调。</summary>
    [Fact]
    public void TheComparisonIsAntisymmetric()
    {
        Assert.True(AppVersion.TryParse("1.0.0", out var older));
        Assert.True(AppVersion.TryParse("2.0.0", out var newer));
        Assert.True(AppVersion.Compare(older, newer) < 0);
        Assert.True(AppVersion.Compare(newer, older) > 0);
        Assert.Equal(0, AppVersion.Compare(older, older));
    }

    [Fact]
    public void DescribedVersionsRoundTripBackToThemselves()
    {
        Assert.True(AppVersion.TryParse("1.2.3-beta.2", out var v));
        Assert.Equal("1.2.3-beta.2", AppVersion.Describe(v));
        Assert.True(AppVersion.TryParse(AppVersion.Describe(v), out var again));
        Assert.Equal(0, AppVersion.Compare(v, again));
    }

    /// <summary>
    /// 真装配属性那条路也要有一个证人（钉<b>形状</b>不钉数值）：版本号的出处只有一处，
    /// 那处哪天断了，界面会一直显示"未知"，而不会有任何一条断言为它红。
    /// </summary>
    [Fact]
    public void TheRealAssemblyVersionIsEitherReadableOrSaysUnknown()
    {
        var previous = AppVersion.InformationalVersionReader;
        try
        {
            // 装配上真写着东西 ⇒ 必须能被解析成版本（这条会随 <Version> 一起漂，但不随具体数字漂）
            AppVersion.InformationalVersionReader = () => "1.0.0+8a1c2d3";
            Assert.True(AppVersion.TryReadLocal(out var v));
            Assert.Equal("1.0.0", AppVersion.Describe(v));

            AppVersion.InformationalVersionReader = () => null;
            Assert.False(AppVersion.TryReadLocal(out _));
        }
        finally
        {
            AppVersion.InformationalVersionReader = previous;
        }
        // 真走一次默认那条路：不许抛，也不算出一个假版本
        Assert.True(AppVersion.TryReadLocal(out var real) | !AppVersion.TryReadLocal(out _));
        if (real.Major == 0 && real.Minor == 0 && real.Patch == 0) Assert.True(real.Equals(default));
    }

    // ===== 结局分类：12 格各是一件事，不许并成"失败" =====

    [Fact]
    public void ASignificantlyNewerRemoteTagIsTheOnlyVerdictThatSaysNewer()
    {
        Assert.Equal(UpdateVerdict.NewerAvailable, UpdatePolicy.Classify(Found("v1.1.0"), "1.0.0"));
        Assert.Equal(UpdateVerdict.UpToDate, UpdatePolicy.Classify(Found("1.0.0"), "1.0.0"));
        Assert.Equal(UpdateVerdict.LocalAhead, UpdatePolicy.Classify(Found("1.0.0"), "1.1.0"));
    }

    /// <summary>本机版本读不出来时<b>不许下"有新版"的结论</b>——那是猜，而猜错的提示会天天弹。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nightly-20261001")]
    public void AnUnknownLocalVersionRefusesToCompare(string? local)
        => Assert.Equal(UpdateVerdict.LocalVersionUnknown, UpdatePolicy.Classify(Found("v9.9.9"), local));

    [Fact]
    public void AnUnreadableRemoteTagIsReportedAsUnreadableNotAsOlder()
    {
        Assert.Equal(UpdateVerdict.UnreadableRemoteTag,
            UpdatePolicy.Classify(new ReleaseProbeResult(ReleaseProbeStatus.Unreadable, Detail: "HTTP 418"), "1.0.0"));
        // 问到了对象却没带 tag_name：也算读不懂，不许拿名字或链接凑一个版本出来
        Assert.Equal(UpdateVerdict.UnreadableRemoteTag,
            UpdatePolicy.Classify(new ReleaseProbeResult(ReleaseProbeStatus.Found), "1.0.0"));
    }

    [Theory]
    [InlineData(ReleaseProbeStatus.NothingPublished, UpdateVerdict.NothingPublished)]
    [InlineData(ReleaseProbeStatus.RepositoryNotVisible, UpdateVerdict.RepositoryNotVisible)]
    [InlineData(ReleaseProbeStatus.Unauthorized, UpdateVerdict.Unauthorized)]
    [InlineData(ReleaseProbeStatus.RateLimited, UpdateVerdict.RateLimited)]
    [InlineData(ReleaseProbeStatus.NotReachable, UpdateVerdict.NotReachable)]
    [InlineData(ReleaseProbeStatus.TimedOut, UpdateVerdict.TimedOut)]
    [InlineData(ReleaseProbeStatus.ServerError, UpdateVerdict.ServerError)]
    [InlineData(ReleaseProbeStatus.Unreadable, UpdateVerdict.UnreadableRemoteTag)]
    public void EveryFailureKindKeepsItsOwnVerdict(ReleaseProbeStatus status, UpdateVerdict expected)
        => Assert.Equal(expected, UpdatePolicy.Classify(new ReleaseProbeResult(status), "1.0.0"));

    // ===== 措辞：每格都要带下一步，而且不许出现"请你…"那类话 =====

    public static IEnumerable<object[]> EveryVerdict =>
        Enum.GetValues<UpdateVerdict>().Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(EveryVerdict))]
    public void EveryVerdictHasItsOwnSentenceThatNeverPushesWorkBack(UpdateVerdict verdict)
    {
        var text = UpdatePolicy.Describe(verdict, Release("v1.1.0"), "1.0.0");

        Assert.False(string.IsNullOrWhiteSpace(text), $"{verdict} 这一格没有话可说");
        // 十二格十二种说法：并成两句就等于告诉用户"失败"（P-54 的判据在这里同样成立）
        Assert.DoesNotContain("请", text, StringComparison.Ordinal);
        Assert.DoesNotContain("重启", text, StringComparison.Ordinal);
        Assert.DoesNotContain("失败", text, StringComparison.Ordinal);
        Assert.DoesNotContain("稍后", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNewerSentenceCarriesBothVersions()
    {
        var text = UpdatePolicy.Describe(UpdateVerdict.NewerAvailable, Release("v1.1.0"), "1.0.0");
        Assert.Contains("1.1.0", text, StringComparison.Ordinal);
        Assert.Contains("1.0.0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoTwoVerdictsShareTheSameSentence()
    {
        var all = Enum.GetValues<UpdateVerdict>();
        var texts = all.Select(v => UpdatePolicy.Describe(v, Release("v1.1.0"), "1.0.0")).ToList();
        Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(all.Length, texts.Count);        // 反空转：漏了一格时这里会先红
    }

    // ===== 节奏与"只提醒一次" =====

    [Theory]
    [InlineData(true, null, true)]         // 从没查过 ⇒ 该查
    [InlineData(true, 23, false)]         // 23 小时前 ⇒ 不查
    [InlineData(true, 24, true)]          // 满 24 小时 ⇒ 查
    [InlineData(true, 25, true)]
    [InlineData(false, null, false)]      // 开关关掉 ⇒ 永不
    [InlineData(false, 999, false)]
    public void TheDailyCadenceDecidesByElapsedAndSwitch(bool enabled, int? hoursAgo, bool expected)
        => Assert.Equal(expected, UpdatePolicy.ShouldAutoProbe(
            enabled, hoursAgo is null ? null : Noon.AddHours(-hoursAgo.Value), Noon));

    [Fact]
    public void TheCadenceIgnoresTheSignOfTheClockSkew()
    {
        // 上次时间戳在未来且已超过一个间隔（系统时钟被调快过、或目录由云盘从别的机器同步进来）：
        // 带符号比较恒为负 ⇒ 这条线从此停摆，而时钟异常最可能伴随重装/迁移，正是最需要备份的那几天。
        Assert.True(UpdatePolicy.ShouldAutoProbe(true, Noon.AddHours(26), Noon));
        // 未到间隔与"未来但没差到一个间隔"都不该重写：后者每次唤起都重问一次，等于把节奏改成"每次开机都问"。
        Assert.False(UpdatePolicy.ShouldAutoProbe(true, Noon.AddMinutes(-5), Noon));
        Assert.False(UpdatePolicy.ShouldAutoProbe(true, Noon.AddHours(2), Noon));
    }

    [Theory]
    [InlineData("v1.1.0", null, true)]            // 第一次发现这版 ⇒ 出声
    [InlineData("v1.1.0", "v1.1.0", false)]      // 这版已经说过 ⇒ 闭嘴（否则每天弹一次，第三天他就把功能关掉）
    [InlineData("v1.2.0", "v1.1.0", true)]       // 又新了一版 ⇒ 再说一次
    [InlineData("V1.1.0", "v1.1.0", false)]      // 大小写不算"另一版"
    [InlineData(null, null, false)]
    [InlineData("", "v1.1.0", false)]
    public void OnlyTheFirstSightOfAVersionMakesNoise(string? remoteTag, string? announced, bool expected)
        => Assert.Equal(expected, UpdatePolicy.ShouldAnnounce(UpdateVerdict.NewerAvailable, remoteTag, announced));

    [Theory]
    [MemberData(nameof(EveryVerdict))]
    public void NothingButANewVersionIsEverWorthInterruptingFor(UpdateVerdict verdict)
    {
        if (verdict == UpdateVerdict.NewerAvailable) return;
        Assert.False(UpdatePolicy.ShouldAnnounce(verdict, "v9.9.9", null));
    }

    [Theory]
    [InlineData(UpdateVerdict.NotReachable, false)]       // 离线那次失败不能记成"今天问过了"，否则要白等 24 小时
    [InlineData(UpdateVerdict.TimedOut, false)]
    [InlineData(UpdateVerdict.RateLimited, true)]          // 对方明确答了"现在不行"，算问过了
    [InlineData(UpdateVerdict.ServerError, true)]
    [InlineData(UpdateVerdict.NothingPublished, true)]
    [InlineData(UpdateVerdict.RepositoryNotVisible, true)]
    [InlineData(UpdateVerdict.Unauthorized, true)]
    [InlineData(UpdateVerdict.UnreadableRemoteTag, true)]
    [InlineData(UpdateVerdict.LocalVersionUnknown, true)]
    [InlineData(UpdateVerdict.NewerAvailable, true)]
    [InlineData(UpdateVerdict.UpToDate, true)]
    [InlineData(UpdateVerdict.LocalAhead, true)]
    public void OnlyAnswersFromTheOtherSideCountAsAsked(UpdateVerdict verdict, bool expected)
        => Assert.Equal(expected, UpdatePolicy.CountsAsProbed(verdict));

    // ===== 去问谁：那串会被拼进请求路径，所以形状必须收死 =====

    [Theory]
    [InlineData(null, UpdatePolicy.DefaultRepository)]
    [InlineData("", UpdatePolicy.DefaultRepository)]
    [InlineData("   ", UpdatePolicy.DefaultRepository)]
    [InlineData("other/things", "other/things")]
    [InlineData(" other/things ", "other/things")]
    [InlineData("a/b/c", UpdatePolicy.DefaultRepository)]           // 多一段：会跑到别的 API 路径上去
    [InlineData("a/", UpdatePolicy.DefaultRepository)]
    [InlineData("/b", UpdatePolicy.DefaultRepository)]
    [InlineData("evil.test/", UpdatePolicy.DefaultRepository)]
    [InlineData("https://x/a", UpdatePolicy.DefaultRepository)]     // 带协议头＝想换主机，不接受
    public void TheRepositoryIsEitherOwnerSlashNameOrTheDefault(string? environment, string expected)
        => Assert.Equal(expected, UpdatePolicy.RepositoryOf(environment));

    // ===== 编排：一次检查只写一次盘、离线不写、手动不吵、并发只发一发 =====

    [Fact]
    public async Task OneCheckWritesTheStateExactlyOnce()
    {
        var store = new FakeStore();
        var source = new FakeSource(Found("v1.1.0"));
        var service = new UpdateService(source, store,
            localVersion: () => "1.0.0", environment: () => null, now: () => Noon);

        var report = await service.CheckAsync(manual: false);

        Assert.Equal(UpdateVerdict.NewerAvailable, report.Verdict);
        Assert.Equal(1, store.Writes);                       // 五格一起写：分开写就是"改了 A 没改 B"
        Assert.Equal(1, source.Calls);                       // 问一次就是问一次（对象必须是 service 拿到的那一颗）
        Assert.Equal(Noon, store.Latest.LastProbeUtc);
        Assert.Equal(UpdateVerdict.NewerAvailable, store.Latest.LastVerdict);
        Assert.Equal("v1.1.0", store.Latest.LastRemoteTag);
        Assert.Equal("v1.1.0", store.Latest.AnnouncedTag);   // 出过声的版本要记住，否则天天弹
        Assert.True(report.Announced);
    }

    [Fact]
    public async Task AnOfflineCheckKeepsThePromiseToAskAgainWhenBackUp()
    {
        var store = new FakeStore(new UpdateState(true, Noon.AddHours(-30)));
        var service = new UpdateService(
            new FakeSource(new ReleaseProbeResult(ReleaseProbeStatus.NotReachable)), store,
            localVersion: () => "1.0.0", environment: () => null, now: () => Noon);

        var report = await service.CheckAsync(manual: false);

        Assert.Equal(UpdateVerdict.NotReachable, report.Verdict);
        Assert.False(report.Announced);                                  // 网络问题不该弹卡
        Assert.Equal(Noon.AddHours(-30), store.Latest.LastProbeUtc);      // 没记成"这次问过了" ⇒ 一联网就自己补问
        Assert.Null(store.Latest.LastVerdict);                            // 也不把"没问到"写成上次的结论
    }

    [Fact]
    public async Task AManualCheckAnswersInPlaceAndDoesNotStackASecondCard()
    {
        var store = new FakeStore();
        var service = new UpdateService(new FakeSource(Found("v1.1.0")), store,
            localVersion: () => "1.0.0", environment: () => null, now: () => Noon);

        var report = await service.CheckAsync(manual: true);

        Assert.False(report.Announced);                        // 他正盯着这一屏，再弹一张卡是叠噪
        Assert.Equal("v1.1.0", store.Latest.AnnouncedTag);     // 但这一版确实已经告诉他了
        Assert.Equal(Noon, store.Latest.LastProbeUtc);
    }

    [Fact]
    public async Task TheSameVersionIsOnlyAnnouncedOnceAcrossChecks()
    {
        var store = new FakeStore();
        IReleaseSource source = new FakeSource(Found("v1.1.0"));

        Assert.True((await new UpdateService(source, store, () => "1.0.0", null, () => Noon)
            .CheckAsync(manual: false)).Announced);
        Assert.False((await new UpdateService(source, store, () => "1.0.0", null, () => Noon.AddHours(25))
            .CheckAsync(manual: false)).Announced);

        // 又新了一版 ⇒ 还要能再说一次（"永远只提醒一次"等于功能坏了）
        source = new FakeSource(Found("v1.2.0"));
        Assert.True((await new UpdateService(source, store, () => "1.0.0", null, () => Noon.AddHours(50))
            .CheckAsync(manual: false)).Announced);
    }

    [Fact]
    public async Task ConcurrentChecksSendExactlyOneRequest()
    {
        var source = new SlowSource(Found("v1.1.0"));
        var service = new UpdateService(source, new FakeStore(),
            localVersion: () => "1.0.0", environment: () => null, now: () => Noon);

        var first = service.CheckAsync(manual: true);
        var second = service.CheckAsync(manual: true);
        Assert.Same(first, second);        // 复用未完成那一发：连点不该变成并发请求（匿名配额每小时 60 次）

        source.Release();
        await first;
        await second;
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task CancellationIsHandedBackUntouched()
    {
        var store = new FakeStore();
        var service = new UpdateService(new FakeSource(Found("v1.1.0")), store,
            localVersion: () => "1.0.0", environment: () => null, now: () => Noon);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CheckAsync(manual: true, cancel.Token));
        Assert.Equal(0, store.Writes);     // 掐掉的那次不写状态，也不许被兜底伪装成"检查过了"
    }

    [Fact]
    public async Task TheSwitchStopsEveryOutgoingRequestButNotAHandPressedButton()
    {
        var source = new FakeSource(Found("v1.1.0"));
        var service = new UpdateService(source, new FakeStore(new UpdateState(false)),
            localVersion: () => "1.0.0", environment: () => null, now: () => Noon);

        Assert.Null(await service.TryAutoProbeAsync());       // 关着＝一次网也不上（"看着关了其实还在问"是最难发现的不诚实）
        Assert.Equal(0, source.Calls);

        Assert.NotNull(await service.CheckAsync(manual: true));   // 他亲手按的那一下照样问（绕过开关也绕过节奏）
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task TheCardReadsTheStoredAnswerWithoutAskingAgain()
    {
        var source = new FakeSource(Found("v1.1.0"));
        var service = new UpdateService(source,
            new FakeStore(new UpdateState(true, Noon, UpdateVerdict.NewerAvailable, "v1.1.0", "v1.1.0")),
            localVersion: () => "1.0.0", environment: () => null, now: () => Noon);

        var last = service.LastReport();
        Assert.NotNull(last);
        Assert.Equal(UpdateVerdict.NewerAvailable, last!.Verdict);
        Assert.Contains("1.1.0", last.Text, StringComparison.Ordinal);
        Assert.False(last.Announced);                       // 回看不该被记成"又提醒了一次"
        Assert.Equal(0, source.Calls);                      // 打开设置页不发请求
        await Task.CompletedTask;
    }

    [Fact]
    public void AFreshInstallHasNothingToReportYet()
        => Assert.Null(new UpdateService(new FakeSource(Found("x")), new FakeStore(),
            localVersion: () => "1.0.0").LastReport());

    // ===== 分层与边界 =====

    /// <summary>版本解析只读装配属性一处，且不许掺进任何互操作（层次闸门同口径）。</summary>
    [Fact]
    public void TheVersionReaderHasExactlyOnePlaceToReadFrom()
    {
        var code = Code(ReadRepoFile("src/StarMark.Core/Updates/AppVersion.cs"));
        Assert.DoesNotContain("DllImport", code, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "AssemblyInformationalVersionAttribute"));
        Assert.Contains("internal static Func<string?> InformationalVersionReader", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 这一批的边界：只问、不装。下载与替换正在运行的程序目录是另一件事，
    /// 前置条件（发布管线＋资产哈希）都还没落地 ⇒ 这两份文件里出现写盘或起进程的形状就是越界。
    /// </summary>
    [Theory]
    [InlineData("src/StarMark.Core/Updates/UpdateService.cs")]
    [InlineData("src/StarMark.Core/Updates/UpdatePolicy.cs")]
    [InlineData("src/StarMark.Integrations/Updates/GitHubReleaseSource.cs")]
    public void TheUpdateSideNeverWritesOrReplacesAnything(string file)
    {
        var code = Code(ReadRepoFile(file));
        foreach (var forbidden in new[] { "File.Write", "File.Move", "File.Replace", "File.Delete",
                "DownloadFile", "Process.Start", "Environment.Exit", "Assembly.Load" })
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
    }

    // ===== 发布页那个地址：由本程序拼，不能被对方牵着走 =====

    /// <summary>
    /// 检查更新唯一一步"往屏幕外走"的动作就是把一个地址交给浏览器，所以那一串必须由
    /// <b>配置里的仓库</b>决定。这里把形状钉死：换仓库会跟着换，多余的段一概不进路径。
    /// </summary>
    [Theory]
    [InlineData(null, "v1.1.0", "https://github.com/AH23333/StarMarkDesktop/releases/tag/v1.1.0")]
    [InlineData("", "v1.1.0", "https://github.com/AH23333/StarMarkDesktop/releases/tag/v1.1.0")]
    [InlineData("other/things", "v1.1.0", "https://github.com/other/things/releases/tag/v1.1.0")]
    [InlineData("other/things", null, "https://github.com/other/things/releases")]
    [InlineData("other/things", "", "https://github.com/other/things/releases")]
    [InlineData("https://evil.test/a", "v1.1.0", "https://github.com/AH23333/StarMarkDesktop/releases/tag/v1.1.0")]
    public void TheReleasePageUrlComesFromTheConfiguredRepository(string? environment, string? tag, string expected)
        => Assert.Equal(expected, UpdatePolicy.ReleasePageUrl(UpdatePolicy.RepositoryOf(environment), tag));

    /// <summary>
    /// 标签是远端给的字符串，而这里要把它放进路径。<b>认不回来就退回那一列的总页</b>：
    /// "少了半屏信息"与"把路径写到别处去"之间只该付出前者。
    /// </summary>
    [Theory]
    [InlineData("v1/../2")]
    [InlineData("v1%2F..")]
    [InlineData("javascript:alert(1)")]
    [InlineData("v1.0?x=1")]
    [InlineData("v1.0#frag")]
    [InlineData("https://evil.test/a")]
    [InlineData("v1.0 ")]
    [InlineData("带中文的标签")]
    public void AnUnrecognisableTagFallsBackToTheReleaseListPage(string tag)
        => Assert.Equal($"https://github.com/{UpdatePolicy.DefaultRepository}/releases",
            UpdatePolicy.ReleasePageUrl(UpdatePolicy.DefaultRepository, tag));

    /// <summary>长度也要有一道：一个几十 KB 的"标签"进路径不是版本号，是把整条 URL 变成别人的东西。</summary>
    [Fact]
    public void AnAbsurdlyLongTagFallsBackToTheReleaseListPage()
        => Assert.Equal($"https://github.com/{UpdatePolicy.DefaultRepository}/releases",
            UpdatePolicy.ReleasePageUrl(UpdatePolicy.DefaultRepository, new string('x', 101)));

    [Fact]
    public void TheReleasePageAlwaysLandsOnGithubOverHttps()
    {
        foreach (var tag in new[] { "v1", "a.b_c-d", "1.2.3-beta.10", "带中文的标签" })
        {
            var uri = new Uri(UpdatePolicy.ReleasePageUrl(UpdatePolicy.DefaultRepository, tag));
            Assert.Equal("github.com", uri.Host);
            Assert.Equal("https", uri.Scheme);
        }
    }

    /// <summary>服务给界面的那串地址来自<b>它自己那一份仓库配置</b>，与探测结果里的任何字段无关。</summary>
    [Fact]
    public async Task TheServiceHandsThePageUrlBuiltFromItsOwnRepository()
    {
        var service = new UpdateService(
            new FakeSource(new ReleaseProbeResult(ReleaseProbeStatus.Found, new RemoteRelease("v1.1.0", null, false, null))),
            new FakeStore(), localVersion: () => "1.0.0", environment: () => "mine/repo", now: () => Noon);

        var report = await service.CheckAsync(manual: true);

        Assert.Equal("https://github.com/mine/repo/releases/tag/v1.1.0", report.PageUrl);
    }

    [Theory]
    [InlineData(UpdateVerdict.NewerAvailable, true)]
    [InlineData(UpdateVerdict.UpToDate, false)]
    [InlineData(UpdateVerdict.LocalAhead, false)]
    [InlineData(UpdateVerdict.NothingPublished, false)]
    [InlineData(UpdateVerdict.RepositoryNotVisible, false)]
    [InlineData(UpdateVerdict.Unauthorized, false)]
    [InlineData(UpdateVerdict.RateLimited, false)]
    [InlineData(UpdateVerdict.NotReachable, false)]
    [InlineData(UpdateVerdict.TimedOut, false)]
    [InlineData(UpdateVerdict.ServerError, false)]
    [InlineData(UpdateVerdict.UnreadableRemoteTag, false)]
    [InlineData(UpdateVerdict.LocalVersionUnknown, false)]
    public void OnlyANewerVersionHasSomethingToDownload(UpdateVerdict verdict, bool expected)
        => Assert.Equal(expected, UpdatePolicy.HasDownloadableRelease(verdict));

    /// <summary>界面那行"当前版本"读不到时必须是"未知"——写 0.0.0 会被看着像一个很旧的版本。</summary>
    [Fact]
    public void LocalVersionSaysUnknownRatherThanZeroWhenItCannotBeRead()
    {
        var previous = AppVersion.InformationalVersionReader;
        try
        {
            AppVersion.InformationalVersionReader = () => null;
            Assert.Equal(AppVersion.Unknown, AppVersion.LocalDisplay);
            AppVersion.InformationalVersionReader = () => "v2.5.1";
            Assert.Equal("2.5.1", AppVersion.LocalDisplay);
        }
        finally
        {
            AppVersion.InformationalVersionReader = previous;
        }
    }

    // ===== 分层：宿主在 UI，判据在 Core，界面不另写一句措辞 =====

    /// <summary>
    /// 落盘那五格的<b>键名只许各出现一次</b>（DTO 那格声明 + 读 + 写）：多一处就是多一个编辑入口，
    /// 而"同一个设置两处写"是本仓反复判过的缺陷（EachSettingHasExactlyOneEditor 同一条口径，
    /// 只是这一组的宿主是 Core 的那个 record，不是散着的 28 项）。
    /// </summary>
    [Theory]
    [InlineData("UpdateAutoCheckEnabled")]
    [InlineData("UpdateLastProbeUnix")]
    [InlineData("UpdateLastVerdict")]
    [InlineData("UpdateLastRemoteTag")]
    [InlineData("UpdateAnnouncedTag")]
    public void EachPersistedUpdateKeyHasExactlyOneFieldOneReaderOneWriter(string key)
    {
        var store = Code(ReadRepoPartials("src/StarMark.UI/Helpers/SettingsStore.cs"));
        Assert.Equal(3, Count(store, key));      // 字段声明 / 读出来 / 写回去，各一处（注释里再提一次不算出口）
    }

    /// <summary>宿主认的是结局的<b>名字</b>；数字一律当"没查过"，五格一次落盘。</summary>
    [Fact]
    public void TheStoreRecognisesVerdictNames_AndRefusesNumbers()
    {
        var store = Code(ReadRepoFile("src/StarMark.UI/Helpers/SettingsStore.Updates.cs"));
        Assert.Contains("char.IsAsciiDigit(name[0])", store, StringComparison.Ordinal);   // "3" 不许被当序号认成一个结局
        Assert.Contains("ignoreCase: false", store, StringComparison.Ordinal);
        Assert.Equal(1, Count(store, "Save(d);"));     // 五格一次落盘
    }

    private static ReleaseProbeResult Found(string tag) => new(ReleaseProbeStatus.Found, Release(tag));

    private static RemoteRelease Release(string tag) => new(tag, null, false, null);

    private sealed class FakeSource(ReleaseProbeResult result) : IReleaseSource
    {
        public int Calls;
        public Task<ReleaseProbeResult> ProbeAsync(string repository, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class SlowSource(ReleaseProbeResult result) : IReleaseSource
    {
        private readonly TaskCompletionSource<bool> _gate = new();
        public int Calls;
        public void Release() => _gate.TrySetResult(true);

        public async Task<ReleaseProbeResult> ProbeAsync(string repository, CancellationToken ct = default)
        {
            Calls++;
            await _gate.Task;
            return result;
        }
    }

    private sealed class FakeStore(UpdateState? initial = null) : IUpdateStateStore
    {
        private UpdateState _state = initial ?? new UpdateState(true);
        public int Writes { get; private set; }
        public UpdateState Latest => _state;

        public UpdateState Read() => _state;
        public void Write(UpdateState state) { _state = state; Writes++; }
    }
}
