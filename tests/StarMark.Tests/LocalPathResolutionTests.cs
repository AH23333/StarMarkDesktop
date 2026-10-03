#nullable enable
using System;
using System.Collections.Generic;
using Xunit;
using StarMark.Abstractions;

namespace StarMark.Tests;

/// <summary>
/// <b>一个 <c>file://</c> URI 到底该还原成哪条磁盘路径</b>的契约（批次 SH，账本 P-131 清单 #8）。
/// <para>
/// 这条规则以前<b>在 <c>LauncherEx</c> 与 <c>PreviewHost</c> 各写一遍</b>（连"两类生产者互补"那句因果注释也抄了两份），
/// 而 <c>ItemCardActions</c>（复制路径／打开所在位置）与 <c>ItemDragHelper</c>（拖出）<b>只写了一半</b>——
/// 只试原始形态。后果不是"迟早分岔"而是今天就有两种读数：
/// 剪贴板图片行的 <c>Uri</c> 出自 <c>new Uri(path).AbsoluteUri</c>（percent 编码，见 <c>ClipboardEntry.UriOf</c>），
/// 只试原始形态 ⇒ 复制出来的是带 <c>%20</c> 的假路径、拖出判不出"存在"于是静默退成一行文本。
/// ⇒ 现在还原规则只有 <c>LocalFileIdentity.TryExistingPath</c> 一颗主人，宿主只交出"什么算存在"的判定。
/// </para>
/// <para>
/// <b>这批顺手补的是一整类反面断言</b>：全仓此前<b>没有任何一条</b>用例钉过"另一条还原法什么时候会给出错路径"——
/// 两斜杠被当主机名、裸 <c>#</c> 被当片段，都只活在注释里。注释坏了不会红，用例坏了会红（#179/#192）。
/// </para>
/// </summary>
public sealed class LocalPathResolutionTests
{
    /// <summary>
    /// 假磁盘：只认名单里的路径，并记下判据<b>问过的每一格与提问顺序</b>。
    /// 记顺序是为了反空转（#188/#194）——"两个候选都试"如果其实只试了一个，这里就会露出来。
    /// </summary>
    private sealed class FakeDisk
    {
        private readonly HashSet<string> _existing;
        public List<string> Asked { get; } = [];
        public FakeDisk(params string[] existing) => _existing = new HashSet<string>(existing, StringComparer.Ordinal);
        public bool Exists(string path)
        {
            Asked.Add(path);
            return _existing.Contains(path);
        }
    }

    // ────────── 先钉住"两个候选各自是什么"（纯文本，不碰磁盘） ──────────

    /// <summary>生产者 ①（<see cref="LocalFileIdentity.UriForPath"/>／Everything／Ditto）：原始那格就是原路径。</summary>
    [Theory]
    [InlineData(@"C:\Data\demo\readme.md")]
    [InlineData(@"D:\工作 报告\x.pdf")]              // 空格原样
    [InlineData(@"C:\docs\C#入门.docx")]            // 裸 '#' 必须留在路径里
    [InlineData(@"C:\a\100%.txt")]                   // '%' 不是转义就不能被解掉
    public void RawCandidateRoundTripsProducerOne(string path)
    {
        var uri = LocalFileIdentity.UriForPath(path);
        var (raw, _) = LocalFileIdentity.PathCandidates(uri);
        Assert.Equal(path, raw);
    }

    /// <summary>生产者 ②（<c>new Uri(path).AbsoluteUri</c>，剪贴板图片行与快捷启动的选择器）：解码那格才是原路径。</summary>
    [Fact]
    public void DecodedCandidateRoundTripsProducerTwo()
    {
        const string path = @"C:\My Doc\工作.png";
        var encoded = new Uri(path).AbsoluteUri;                       // file:///C:/My%20Doc/%E5%B7%A5%E4%BD%9C.png
        var (raw, decoded) = LocalFileIdentity.PathCandidates(encoded);
        Assert.Equal(path, decoded);
        Assert.Equal(@"C:\My%20Doc\%E5%B7%A5%E4%BD%9C.png", raw);      // 原始那格不解码——这是与 UriForPath 的往返契约
    }

    /// <summary>
    /// <b>反面断言①（实测，net9-windows）</b>：文件名里<b>真的</b>带 <c>%XX</c> 时，解码那格会把它解掉 ⇒
    /// 指到另一个名字上去（<c>100%20.txt</c> 变成 <c>"100 .txt"</c>）。这才是"原始那格必须优先"的真正理由——
    /// <b>老注释给的理由（"两斜杠形态会把盘符当主机名"）实测复现不出来，已在批次 SH 就地更正</b>（#192：注释里的因果句也是快照）。
    /// </summary>
    [Fact]
    public void DecodedCandidateManglesALiteralPercentInTheName()
    {
        const string uri = "file://C:/b/100%20.txt";                   // 真名字就叫 100%20.txt（不编码的生产者）
        var (raw, decoded) = LocalFileIdentity.PathCandidates(uri);
        Assert.Equal(@"C:\b\100%20.txt", raw);
        Assert.Equal(@"C:\b\100 .txt", decoded);                       // 解错了：这是另一个（不存在的）名字
    }

    /// <summary>
    /// <b>钉住被更正的那条事实</b>：两斜杠形态在 net9 上交给 <c>LocalPath</c> 并不会把盘符当主机名——
    /// 两格读数相同。写下这条不是为了用它，而是为了<b>下一次谁再引用那条错理由时会红在这里</b>。
    /// </summary>
    [Fact]
    public void TwoSlashFormIsParsedFineByNet9SoTheOrderingIsAboutPercentsNotHosts()
    {
        const string uri = "file://C:/My Doc/x.png";                   // 不编码＋空格＋两斜杠
        var (raw, decoded) = LocalFileIdentity.PathCandidates(uri);
        Assert.Equal(@"C:\My Doc\x.png", raw);
        Assert.Equal(raw, decoded);
    }

    /// <summary><b>反面断言</b>：裸 <c>#</c> 交给 <c>LocalPath</c> 会被当片段截断，所以解码格不等于原始格。</summary>
    [Fact]
    public void DecodedCandidateTruncatesBareHash()
    {
        const string uri = "file://C:/docs/C#入门.docx";
        var (raw, decoded) = LocalFileIdentity.PathCandidates(uri);
        Assert.Equal(@"C:\docs\C#入门.docx", raw);
        Assert.DoesNotContain("#", decoded, StringComparison.Ordinal);  // 截断后 '#' 之后整段没了
    }

    [Theory]
    [InlineData("https://example.com/x")]      // 非 file:// ⇒ 两格都不给（不许去猜别的协议）
    [InlineData("starmark://item/7")]
    [InlineData("")]
    [InlineData(null)]
    public void NonFileSchemeGivesNoCandidates(string? uri)
        => Assert.Equal((string.Empty, string.Empty), LocalFileIdentity.PathCandidates(uri));

    // ────────── 再钉"怎么从两个候选里选" ──────────

    /// <summary>原始形态在磁盘上 ⇒ 就用它（<see cref="LocalFileIdentity.UriForPath"/> 的往返优先，与改道前一致）。</summary>
    [Fact]
    public void RawWinsWhenItIsOnDisk()
    {
        const string uri = "file://C:/Data/demo/readme.md";
        var disk = new FakeDisk(@"C:\Data\demo\readme.md");
        Assert.True(LocalFileIdentity.TryExistingPath(uri, disk.Exists, out var path));
        Assert.Equal(@"C:\Data\demo\readme.md", path);
        Assert.Equal(@"C:\Data\demo\readme.md", Assert.Single(disk.Asked));   // 命中就不必再问第二格
    }

    /// <summary>
    /// <b>这批真正修的那条</b>：只有解码形态在磁盘上时，必须选解码那一格。
    /// 改道前 <c>ItemCardActions.CopyUri</c>／<c>ItemDragHelper</c> 只试原始形态，
    /// 于是剪贴板图片行"复制路径"给出带 <c>%20</c> 的假路径、拖出判不出存在而静默退成文本。
    /// </summary>
    [Fact]
    public void DecodedWinsWhenOnlyItIsOnDisk()
    {
        const string path = @"C:\Users\工作\AppData\Local\StarMark\clipboard\clip image.png";
        var uri = new Uri(path).AbsoluteUri;
        var disk = new FakeDisk(path);                                 // 磁盘上只有解码后的真路径
        Assert.True(LocalFileIdentity.TryExistingPath(uri, disk.Exists, out var resolved));
        Assert.Equal(path, resolved);
        Assert.Equal(2, disk.Asked.Count);                             // 先问原始、再问解码
    }

    /// <summary>
    /// 两格<b>真的不同</b>且<b>都在盘上</b>时必须取原始那一格：真名叫 <c>100%20.txt</c> 的文件与它的解码影子
    /// <c>"100 .txt"</c> 可以同名并存，定序反了就会指到另一个人的文件上去。
    /// <para>⚠ 这一格原先用的是 <c>UriForPath(@"C:\My Doc\x.png")</c>——那条 uri 里没有 <c>%XX</c>，
    /// 两格算出来是<b>同一个串</b>，所以"反了也照样绿"（批次 VR 台架 B2 把定序调反，只有问次数那格替它认了罪）。
    /// 夹具换成两格确实分岔的形态，这颗才真的在钉定序。</para>
    /// </summary>
    [Fact]
    public void RawStillWinsWhenBothAreOnDisk()
    {
        const string uri = "file://C:/b/100%20.txt";
        var (raw, decoded) = LocalFileIdentity.PathCandidates(uri);
        Assert.NotEqual(raw, decoded);                                 // 夹具自证：两格确实不是一个串
        var disk = new FakeDisk(raw, decoded);                         // 盘上真的同时放着两个名字
        Assert.True(LocalFileIdentity.TryExistingPath(uri, disk.Exists, out var resolved));
        Assert.Equal(raw, resolved);                                   // 定序不许反：反了就会解掉真名叫 %XX 的文件
        Assert.Equal(raw, Assert.Single(disk.Asked));                   // 且命中就不必再问第二格
    }

    /// <summary>两个候选都不在磁盘上 ⇒ false，且<b>不吐一条造出来的路径</b>；兜法（报原因／照抄原串／退成文本）归宿主。</summary>
    [Fact]
    public void NeitherOnDiskReturnsFalseAndInventsNothing()
    {
        var uri = new Uri(@"C:\Gone\folder\missing.png").AbsoluteUri;
        var disk = new FakeDisk();
        Assert.False(LocalFileIdentity.TryExistingPath(uri, disk.Exists, out var path));
        Assert.Equal(string.Empty, path);
    }

    [Fact]
    public void NonFileSchemeNeverAsksTheDisk()
    {
        var disk = new FakeDisk("whatever");
        Assert.False(LocalFileIdentity.TryExistingPath("https://example.com/x", disk.Exists, out _));
        Assert.Empty(disk.Asked);
    }

    /// <summary>
    /// <b>能力下限（台架 SH4 钉这条）</b>：缺盘符／UNC 没有原始那一格，但解码那一格还在——
    /// <c>LauncherEx</c> 今天能打开 <c>file://server/share</c>，收成一颗时不许顺手把它收窄掉。
    /// </summary>
    [Fact]
    public void UncHasNoRawCandidateButStillResolvesThroughDecoded()
    {
        const string uri = "file://server/share";
        var (raw, decoded) = LocalFileIdentity.PathCandidates(uri);
        Assert.Equal(string.Empty, raw);
        Assert.NotEmpty(decoded);
        Assert.True(LocalFileIdentity.TryExistingPath(uri, p => p == decoded, out var path));
        Assert.Equal(decoded, path);
    }

    // ────────── 不需要磁盘判断的那两个消费者（快捷启动的默认标题） ──────────

    /// <summary>
    /// 标题那颗的取法（批次 SH 顺手修的一条可见缺陷）：<b>先问磁盘</b>，两格都不在时才退回原始那格。
    /// 改道前它只认原始那格，而手输／文本拖入的地址会先被 <c>new Uri()</c> 规范化成 percent 编码
    /// ⇒ 新条目的默认名会印成 <c>%E5%B7%A5%E4%BD%9C%20%E6%8A%A5%E5%91%8A.docx</c> 这种乱码。
    /// </summary>
    [Fact]
    public void PreferredPathAsksTheDiskFirstAndFallsBackToRaw()
    {
        const string existing = @"C:\工作 报告.docx";
        var encoded = new Uri(existing).AbsoluteUri;
        // ① 东西在盘上 ⇒ 拿真名字（不再印 %XX）
        Assert.Equal(existing, LocalFileIdentity.PreferredPathFromUri(encoded, p => p == existing));
        // ② 不在盘上 ⇒ 退回原始那格：至少保得住 '#'，也不会把名字里的 %20 解掉
        Assert.Equal(@"C:\docs\C#入门.docx",
            LocalFileIdentity.PreferredPathFromUri("file://C:/docs/C#入门.docx", _ => false));
        Assert.Equal(@"C:\b\100%20.txt",
            LocalFileIdentity.PreferredPathFromUri("file://C:/b/100%20.txt", _ => false));
        // ③ 非 file:// ⇒ 空串（这两处调用点都在 parsed.IsFile 分支里，走不到；留着是为了不许别人拿它当通用解析器）
        Assert.Equal(string.Empty, LocalFileIdentity.PreferredPathFromUri("https://example.com/x", _ => true));
    }

    /// <summary>
    /// 判据不许沾磁盘（<c>Abstractions</c> 里出现 <c>File</c>／<c>Directory</c> 就是把它变成不可测的东西）：
    /// 存在与否一律由调用方注入。这条断言钉的是"两颗职责的边界"，不是实现细节。
    /// </summary>
    [Fact]
    public void TheJudgeNeverTouchesTheDiskItself()
    {
        var code = FormatScanner.Scan(SourceGate.ReadRepoFile("src/StarMark.Abstractions/LocalFileIdentity.cs")).Code;
        var body = SourceGate.MethodBody(code, "public static bool TryExistingPath(");
        Assert.DoesNotContain("File.Exists", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Exists", body, StringComparison.Ordinal);
        Assert.Contains("existsOnDisk", body, StringComparison.Ordinal);
    }

    // ────────── 批次 VR：给人看的那一格（显示侧）──────────

    /// <summary>
    /// <b>没有任何 %XX 可解时不去问磁盘</b>：两格本来就是同一条（裸 <c>#</c> 我们要的也正是原始那格）。
    /// <para>这条不是微优化而是行为不变式：文件夹树重建对<b>每一行</b>文件条目都走这里，
    /// 几千行 ⇒ 每行一次 stat 是启动刻度上看得见的耗时；而结果与问了磁盘完全一致。</para>
    /// </summary>
    [Fact]
    public void PreferredPathSkipsTheDiskWhenThereIsNothingToDecode()
    {
        var disk = new FakeDisk();                       // 空磁盘：任何一格都不在
        const string uri = "file://C:/docs/C#入门.docx";
        Assert.Equal(@"C:\docs\C#入门.docx", LocalFileIdentity.PreferredPathFromUri(uri, disk.Exists));
        Assert.Empty(disk.Asked);
        // 空格／中文都不算 %XX，一样不必问
        Assert.Equal(@"D:\工作 报告\x.pdf",
            LocalFileIdentity.PreferredPathFromUri(LocalFileIdentity.UriForPath(@"D:\工作 报告\x.pdf"), disk.Exists));
        Assert.Empty(disk.Asked);
    }

    /// <summary>带 %XX 时仍按"先问磁盘"的老口径：原始那格在就用原始那格，真名叫 <c>100%20.txt</c> 的不许被解码展示。</summary>
    [Fact]
    public void DisplayPathStillPrefersTheLiteralPercentNameWhenItIsTheOneOnDisk()
    {
        var disk = new FakeDisk(@"C:\b\100%20.txt");
        Assert.Equal(@"C:\b\100%20.txt", LocalFileIdentity.DisplayPath("file://C:/b/100%20.txt", disk.Exists));
    }

    /// <summary>编码那格才是盘上真名时，显示侧交回人话（VR 修的那一条：树上不许印 <c>Visual%20Studio%20Code</c>）。</summary>
    [Fact]
    public void DisplayPathGivesTheHumanNameWhenTheEncodedFormIsTheOneOnDisk()
    {
        const string real = @"D:\Visual Studio Code\Something";
        var disk = new FakeDisk(real);
        var shown = LocalFileIdentity.DisplayPath(new Uri(real).AbsoluteUri, disk.Exists);
        Assert.Equal(real, shown);
        Assert.DoesNotContain("%", shown, StringComparison.Ordinal);
    }

    /// <summary>两格都不在盘上时<b>不许凭空造一个名字</b>：仍给库里那一串（与 VR 之前逐字一致）。</summary>
    [Fact]
    public void DisplayPathKeepsTheStoredFormWhenNeitherCandidateExists()
        => Assert.Equal(@"Q:\Never%20Here\x.md",
            LocalFileIdentity.DisplayPath("file:///Q:/Never%20Here/x.md", _ => false));

    /// <summary>
    /// <b>非 <c>file://</c> 那一臂是这颗与 <see cref="LocalFileIdentity.PreferredPathFromUri"/> 唯一的分工</b>：
    /// 显示侧要原样给出串本身，因为它的调用点是卡片 tooltip——回空串等于把书签行的地址从界面上抹掉。
    /// </summary>
    [Theory]
    [InlineData("https://example.com/x")]
    [InlineData("starmark://item/7")]
    [InlineData("")]
    [InlineData(null)]
    public void DisplayPathNeverReturnsBlankForSomethingTheCardHasToPrint(string? uri)
    {
        var shown = LocalFileIdentity.DisplayPath(uri, _ => false);
        if (string.IsNullOrEmpty(uri)) Assert.Equal(string.Empty, shown);
        else Assert.Equal(uri, shown);
    }

    /// <summary>缺盘符／UNC 没有原始那一格，解码那一格照样能还原成人话（与 <c>LauncherEx</c> 的既有能力同一口径）。</summary>
    [Fact]
    public void DisplayPathResolvesUncThroughTheDecodedCandidate()
    {
        const string unc = @"\\server\share";
        Assert.Equal(unc, LocalFileIdentity.DisplayPath("file://server/share", p => p == unc));
    }
}
