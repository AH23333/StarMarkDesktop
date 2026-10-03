#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions.Updates;
using StarMark.Core.Updates;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 「这一版的更新说明」（批次 VX 后半）：<b>把对方写的 markdown 收成屏幕上能读的那几行</b>，
/// 以及<b>那份正文只活在一次检查里</b>这条边界。
/// <para>
/// 两族断言各钉一件事：
/// ① <see cref="ReleaseNotes.Clean"/> 的每条规则都逐字钉——这一串是<b>外部服务器的输入</b>，
///    规则若只靠"看起来能读"来判，下一次有人改一条就没人知道少了哪一面；
///    同时把<b>不许剥的东西</b>也钉住（单个 <c>*</c>／裸写的地址／列表符号），
///    过度收敛等于改了人家的内容，那与改了本程序自己的措辞是同一种错。
/// ② 正文<b>不进设置档</b>：从档里重算的那一份必然没有说明（<see cref="UpdateService.LastReport"/>），
///    而"不是有新版那一格"也没有。这两格都只由 Core 判一次，界面不许再判。
/// </para>
/// </summary>
public sealed class ReleaseNotesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    // ===== ① 收敛规则：逐字 =====

    [Theory]
    // 标题记号剥掉，那一行自己就是分隔；行内前后空白不算内容
    [InlineData("## 修了两件事", "修了两件事")]
    [InlineData("   ### 缩进过的标题", "缩进过的标题")]
    [InlineData("**加粗**与__下划线__与`代码`", "加粗与下划线与代码")]
    // 链接只留下连接文字：括号里那个地址是对方想写哪儿就写哪儿的串（与 html_url 同一口径）
    [InlineData("[看这里](https://evil.example.test/a)", "看这里")]
    // 徽章写成 [![img](png)](link)：整行清完什么都不剩
    [InlineData("[![badge](https://img.example.test/b.png)](https://link.example.test)", null)]
    [InlineData("<!-- 发布模板里的内部备注，不该出现在屏幕上 -->", null)]
    [InlineData("第一行\r\n第二行   ", "第一行\n第二行")]
    [InlineData("<kbd>Ctrl</kbd>+<kbd>S</kbd>", "Ctrl+S")]
    // 连续空行折一档：整段留白会把这张卡挤成一堵墙
    [InlineData("- 第一条\n\n\n\n- 第二条", "- 第一条\n\n- 第二条")]
    public void CleaningTakesTheMarkersAndKeepsTheText(string markdown, string? expected)
        => Assert.Equal(expected, ReleaseNotes.Clean(markdown));

    /// <summary>
    /// <b>不许剥的东西</b>同样逐字钉：单个 <c>*</c> 与 <c>_</c> 可能是内容本身（<c>2*3*4</c>），
    /// 列表符号是说明的实际结构，裸写的地址本程序既不会去开也不冒充成链接。
    /// 少一条这种用例，下一次"顺手把规则放宽"就会把正文改掉而全线仍绿。
    /// </summary>
    [Theory]
    [InlineData("2*3*4 与 a_b_c")]
    [InlineData("- 这条列表符号要留着")]
    [InlineData("1. 有序也一样")]
    [InlineData("详见 https://github.com/a/b/releases 那一页")]
    [InlineData("####### 七颗井号不是标题")]
    public void NothingThatIsContentIsTreatedAsAMarker(string plain)
        => Assert.Equal(plain, ReleaseNotes.Clean(plain));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n \t \n")]
    public void AbsentOrEmptyNotesAreNothingRatherThanAnEmptyString(string? markdown)
        => Assert.Null(ReleaseNotes.Clean(markdown));

    [Fact]
    public void TheLongestBodyTheScreenWillHoldIsStatedOnce()
    {
        Assert.Equal(4000, ReleaseNotes.MaxDisplayChars);
        Assert.Equal(ReleaseNotes.MaxDisplayChars, ReleaseNotes.Clean(new string('字', ReleaseNotes.MaxDisplayChars))!.Length);
    }

    /// <summary>超长那一刀<b>必须走全仓唯一的切法</b>：自己切片会把一枚 emoji 切成半个，屏幕上就是个方块。</summary>
    [Fact]
    public void CuttingTooLongBodyNeverSplitsAnEmoji()
    {
        var longBody = new string('字', ReleaseNotes.MaxDisplayChars - 1) + "🐓🐓" + new string('字', 10);

        var cleaned = ReleaseNotes.Clean(longBody)!;

        Assert.True(cleaned.Length <= ReleaseNotes.MaxDisplayChars + 1, $"这一刀切出 {cleaned.Length} 字");
        Assert.EndsWith("…", cleaned, StringComparison.Ordinal);
        // 逐位核对未配对代理：切在代理对中间时这里必然为奇数
        Assert.Equal(0, cleaned.Count(char.IsHighSurrogate) - cleaned.Count(char.IsLowSurrogate));
        Assert.DoesNotContain("🐓", cleaned, StringComparison.Ordinal);   // 切在前面的那一枚只能整枚丢掉
    }

    /// <summary>
    /// 拿<b>真发出去的那一版正文</b>的头三段当输入（逐字取自 <c>docs/发布说明/v1.0.2.md</c> 的第 1–5 行，
    /// 也就是公开 Release 里那份正文）：规则一条条钉过，还要一条把它们<b>合起来</b>钉——
    /// 标题、粗体、带地址的链接、反引号同一段里并存时先后顺序会互相影响（先剥链接才剥得掉外面的粗体）。
    /// 这一格不测"好看"，测的是清完之后屏幕上剩下什么字。
    /// </summary>
    [Fact]
    public void TheBodyActuallyPublishedForV102ReadsAsProseAfterCleaning()
    {
        var published = "# StarMark v1.0.2\n\n"
            + "程序能力与 v1.0.1 相同。这一版改的是**自动更新本身**：升级没做成的时候，你眼前的程序不该消失。\n\n"
            + "**运行前请先装 [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)**。"
            + "解压 `StarMark-1.0.2-win-x64.zip` 到任意目录，运行 `StarMark.UI.exe`。\n";

        Assert.Equal(
            "StarMark v1.0.2\n\n"
            + "程序能力与 v1.0.1 相同。这一版改的是自动更新本身：升级没做成的时候，你眼前的程序不该消失。\n\n"
            + "运行前请先装 .NET 9 Desktop Runtime。解压 StarMark-1.0.2-win-x64.zip 到任意目录，运行 StarMark.UI.exe。",
            ReleaseNotes.Clean(published));
    }

    // ===== ② 谁拿到正文、什么时候没有 =====

    [Fact]
    public async Task TheAnswerThatFoundANewerVersionCarriesTheCleanedText()
    {
        var service = Service(new FakeSource(new ReleaseProbeResult(ReleaseProbeStatus.Found,
            new RemoteRelease("v1.1.0", null, false, null, "## 修了两件事\n- 那条不再丢窗口"))));

        var report = await service.CheckAsync(manual: true);

        Assert.Equal("修了两件事\n- 那条不再丢窗口", report.Notes);
        // 正文不许混进那句话：状态行与说明各是各的出口，混一次就没法只收起其中一格
        Assert.DoesNotContain("修了两件事", report.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// "已经最新"那一格<b>不给说明</b>。这不是省一次字符串处理：在"这一版你已经装着"的下一行
    /// 摊开它的改动清单，读的人会以为那些改动还没到自己机器上。判据与那颗下载按钮同一处，不在界面再判一次。
    /// </summary>
    [Fact]
    public async Task UpToDateSaysNothingAboutWhatChanged()
    {
        var service = Service(new FakeSource(new ReleaseProbeResult(ReleaseProbeStatus.Found,
            new RemoteRelease("v1.0.0", null, false, null, "## 这一版你已经装着了"))), local: "1.0.0");

        var report = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateVerdict.UpToDate, report.Verdict);
        Assert.Null(report.Notes);
    }

    [Fact]
    public async Task AReleaseThatWroteNoBodyGivesNoTextToShow()
    {
        var service = Service(new FakeSource(new ReleaseProbeResult(ReleaseProbeStatus.Found,
            new RemoteRelease("v1.1.0", null, false, null, "   \n  "))));

        var report = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateVerdict.NewerAvailable, report.Verdict);   // 有新版是真的，只是没写说明
        Assert.Null(report.Notes);
    }

    [Fact]
    public async Task AProbeThatNeverGotAnAnswerCarriesNoTextEither()
    {
        var service = Service(new FakeSource(new ReleaseProbeResult(ReleaseProbeStatus.RateLimited)));

        Assert.Null((await service.CheckAsync(manual: true)).Notes);
    }

    /// <summary>
    /// <b>正文不进设置档</b>这一条最硬的可机检形式：同一次检查之后，当场那一份有正文，
    /// 而从档里重算的那一份一定没有。哪天有人给它加一格存档，这一条先红——
    /// 那时要改的是那个决定（连同"备份会把外部文本一起带走"这条理由），不是这条断言。
    /// </summary>
    [Fact]
    public async Task TheTextDoesNotSurviveAReReadFromTheArchive()
    {
        var store = new FakeStore();
        var service = Service(new FakeSource(new ReleaseProbeResult(ReleaseProbeStatus.Found,
            new RemoteRelease("v1.1.0", null, false, null, "## 修了两件事"))), store: store);

        Assert.NotNull((await service.CheckAsync(manual: true)).Notes);

        var reopened = service.LastReport();
        Assert.Null(reopened!.Notes);                       // 重启后进这一屏：那句话还在，正文没有
        Assert.Equal("v1.1.0", reopened.RemoteTag);         // 而标签照旧留着（那一格是存的）
        Assert.Equal(UpdateVerdict.NewerAvailable, reopened.Verdict);
    }

    private static UpdateService Service(IReleaseSource source, string local = "1.0.0", FakeStore? store = null)
        => new(source, store ?? new FakeStore(), () => local, null, () => Noon);

    private sealed class FakeSource(ReleaseProbeResult result) : IReleaseSource
    {
        public Task<ReleaseProbeResult> ProbeAsync(string repository, CancellationToken ct = default)
            => Task.FromResult(result);
    }

    private sealed class FakeStore : IUpdateStateStore
    {
        private UpdateState _state = new(true);
        public UpdateState Read() => _state;
        public void Write(UpdateState state) => _state = state;
    }
}
