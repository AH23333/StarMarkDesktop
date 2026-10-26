#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.Capture;
using StarMark.Abstractions.I18n;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 截图标注那一族「为什么没画上去」的接线守门（批次 片118，P-192 第五颗）。
/// <para>要守的只有一件事：抛出的一句话<b>走上屏那条出口时必须还是键</b>。
/// 屏幕宿主从前把 <c>ex.Message</c> 塞进表里那颗「{0}」槽，等于把我们自己写的中文
/// 钉在英文与日文界面上（同一片坏法在识字那一路与剪贴板图片那一路各被抓到一次）。</para>
/// <para>这颗也反面证掉一件事：异常消息并非一律该交字符串——日志要字符串，屏幕要键，
/// 所以 <see cref="CaptureFailureException"/> 两头都给，取词时机留在宿主那一拍。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class CaptureReasonGateTests : IDisposable
{
    private const string Drawer = "src/StarMark.Integrations/Capture/GdiTextDrawer.cs";
    private const string Transform = "src/StarMark.Integrations/Capture/BitmapTransform.cs";
    private const string Painter = "src/StarMark.Core/Capture/AnnotationPainter.cs";
    private const string Actions = "src/StarMark.UI/Views/CaptureOverlayWindow.Actions.cs";
    private const string Selection = "src/StarMark.UI/Views/CaptureOverlayWindow.Selection.cs";
    private const string Pin = "src/StarMark.UI/Views/CaptureOverlayWindow.Pin.cs";
    private const string Annotation = "src/StarMark.Core/Capture/Annotation.cs";
    private const string Strings = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    /// <summary>那颗收尾的双引号由这颗常量给：⛔ 在各条判据里手写转义——同一种写法本批被搬运脚本切掉过一次。</summary>
    private const string Quote = @"""";

    /// <summary>本批进表的 12 颗（逐颗由下面那颗格问一遍"表里真有一行吗"）。</summary>
    private static readonly string[] ShippedKeys =
    [
        "capture.pixel.bufferShort", "capture.canvas.badSize", "capture.geom.bufferMismatch", "capture.geom.cropEmpty",
        "capture.gdi.fontHeightMin", "capture.gdi.dcFailed", "capture.gdi.dibFailed", "capture.gdi.fontFailed",
        "capture.gdi.lineHeightFailed", "capture.gdi.measureFailed", "capture.gdi.measureAllEmpty",
        "capture.gdi.textOutFailed",
    ];

    private readonly AppLanguage _before = Locale.Current;

    public CaptureReasonGateTests()
    {
        // TrySet 交回的是「这颗换没换过来」而⛔「这一档在不在」（规则 #112）：默认档本来就是中文，
        // 拿返回值断言会让每一格都炸在构造期，屏幕上只剩一句 Assert.True() Failure。
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal(AppLanguage.Chinese, Locale.Current);
    }

    public void Dispose() => Locale.TrySet(_before);

    private static string Code(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>① 三档各自抛出的颗数、当场取键的颗数各按现读钉住，并且⛔ 许有一颗把成品串直接交出去。</summary>
    [Theory]
    [InlineData(Drawer, 17, 17)]
    [InlineData(Transform, 5, 5)]
    [InlineData(Painter, 3, 2)]
    public void TheThrowSitesHandOutKeys(string file, int throws, int inlineKeys)
    {
        var code = Code(file);
        Assert.Equal(throws, SourceGate.Count(code, "throw new CaptureFailureException("));
        Assert.Equal(inlineKeys, SourceGate.Count(code, "LocMessage.Of(" + Quote));
        Assert.Equal(0, SourceGate.Count(code, "throw new CaptureFailureException(" + Quote));
    }

    /// <summary>
    /// ①·补 Painter 三颗里少的那一颗⛔ 是漏网：它把 <c>mark.Problem()</c> 那颗键原样递上去，
    /// 而那颗键的主人声明的返回类型本就是 LocMessage——"句子在半路落地成字符串"这件事由这一格钉住。
    /// </summary>
    [Fact]
    public void TheForwardedProblemIsAlreadyAKey()
    {
        Assert.Equal(1, SourceGate.Count(Code(Annotation), "LocMessage? Problem()"));
        Assert.Equal(1, SourceGate.Count(Code(Painter),
            "if (mark.Problem() is { } problem) throw new CaptureFailureException(problem);"));
    }

    /// <summary>② 旧形状不许回来：那三档里⛔ 再有任何带中文字面量的 throw。</summary>
    [Theory]
    [InlineData(Drawer)]
    [InlineData(Transform)]
    [InlineData(Painter)]
    public void TheBareSentencesMayNotComeBack(string file)
    {
        var code = Code(file);
        Assert.Equal(0, SourceGate.Count(code, "throw new InvalidOperationException(" + Quote));
        Assert.Equal(0, SourceGate.Count(code, "throw new ArgumentException(" + Quote));
        Assert.Equal(0, SourceGate.Count(code, "problem.Render()"));
        foreach (var dead in new[] {
                "创建内存画布失败", "系统没能创建标注用的字体", "量不出这段文字要占多大",
                "像素缓冲与声明尺寸不符", "裁不出画面", "文字没能写出去", "画面尺寸不合法",
                "像素缓冲比声明的尺寸短" })
            Assert.Equal(0, SourceGate.Count(code, dead));
    }

    /// <summary>③ 四颗屏幕宿主都问那颗主人；那一句为何⛔ 算漏网由下面的注释说。</summary>
    [Fact]
    public void TheHostsAskTheOneOwner()
    {
        // 三档⛔ 两档：宿主名单以前按"这一刀改过的档"列，漏了 Pin 那一档里那颗打码预览的框。
        var hosts = Code(Actions) + Code(Selection) + Code(Pin);
        Assert.Equal(4, SourceGate.Count(hosts, "CaptureFailureException.ReasonOf(ex)"));
        // 旧形状的锚点必须把键串那颗收尾双引号一起咬住：⛔ 写成「键名, ex.Message」——那种 needle
        // 两边都数不到（既数不到旧形也数不到新形），红与绿长得一模一样，正是这一族要防的"尺聋"。
        foreach (var retired in new[] { "composeFailed", "cropFailed", "resizeFailed", "drawFailed" })
            Assert.Equal(0, SourceGate.Count(hosts, "capture.strip." + retired + Quote + ", ex.Message"));
        // 打码预览那一格留 ex.Message 是有理由的（那颗框在 Pin 那一档的 FlushMosaicScratch 里）：
        // 那个 try 里只有 WinRT 的像素流写，它抛的是系统异常（机器文本），
        // 确实没有句子可交表——数据门正是为这一类留的。
        Assert.Equal(1, SourceGate.Count(hosts, "capture.strip.mosaicFailed" + Quote + ", ex.Message"));
        // 总账：宿主圈里"把 ex.Message 直接塞进槽"这一形状只剩上面那一种，一颗都不许多。
        Assert.Equal(1, SourceGate.Count(hosts, Quote + ", ex.Message"));
    }

    /// <summary>④ 判类型这件事只许住在主人那一颗里（⛔ 每个 catch 各抄一份三元表达式）。</summary>
    [Fact]
    public void OnlyTheOwnerTestsForThatType()
    {
        var files = SourceGate.ReadRepoUnder("src").ToList();
        Assert.True(files.Count >= 200, $"只扫到 {files.Count} 个源文件＝扫描路径错了，闸门在空转");
        var hits = files
            .Select(f => (f.RelativePath, SourceGate.Count(SourceGate.Code(f.Text), "is CaptureFailureException")))
            .Where(f => f.Item2 > 0)
            .ToList();
        Assert.Equal(new[] { ("src/StarMark.Abstractions/Capture/CaptureFailureException.cs", 1) },
            hits.Select(h => (h.RelativePath, h.Item2)).ToArray());
    }

    /// <summary>⑤ 表里每颗新键恰有一行。</summary>
    [Theory]
    [InlineData("capture.pixel.bufferShort")]
    [InlineData("capture.canvas.badSize")]
    [InlineData("capture.geom.bufferMismatch")]
    [InlineData("capture.geom.cropEmpty")]
    [InlineData("capture.gdi.fontHeightMin")]
    [InlineData("capture.gdi.dcFailed")]
    [InlineData("capture.gdi.dibFailed")]
    [InlineData("capture.gdi.fontFailed")]
    [InlineData("capture.gdi.lineHeightFailed")]
    [InlineData("capture.gdi.measureFailed")]
    [InlineData("capture.gdi.measureAllEmpty")]
    [InlineData("capture.gdi.textOutFailed")]
    public void EachKeyHasExactlyOneRow(string key)
    {
        var table = SourceGate.ReadRepoFile(Strings);
        Assert.Equal(1, SourceGate.Count(table, "new Entry(" + Quote + key));
    }

    /// <summary>⑥ 行为：槽里那颗键在贴上去那一刻才取词——英文档整句⛔ 一个汉字。</summary>
    [Fact]
    public void TheNestedSentenceIsTakenAtPasteTime()
    {
        Locale.TrySet(AppLanguage.English);
        Assert.Equal(AppLanguage.English, Locale.Current);   // ⛔ 拿 TrySet 的返回值当「换没换到」（规则 #112）
        var line = LocMessage.Of("capture.strip.drawFailed", LocMessage.Of("capture.gdi.dcFailed")).Render();
        Assert.Equal("The annotation couldn't be drawn: The memory canvas couldn't be created (out of GDI handles)", line);

        Locale.TrySet(AppLanguage.Japanese);
        Assert.Equal(AppLanguage.Japanese, Locale.Current);   // ⛔ 拿 TrySet 的返回值当「换没换到」（规则 #112）
        var ja = LocMessage.Of("capture.strip.drawFailed", LocMessage.Of("capture.gdi.dcFailed")).Render();
        Assert.Contains("GDI", ja, StringComparison.Ordinal);
        Assert.DoesNotContain("创建内存画布失败", ja, StringComparison.Ordinal);

        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal(AppLanguage.Chinese, Locale.Current);   // ⛔ 拿 TrySet 的返回值当「换没换到」（规则 #112）
        Assert.Contains("GDI 句柄用尽",
            LocMessage.Of("capture.strip.drawFailed", LocMessage.Of("capture.gdi.dcFailed")).Render(),
            StringComparison.Ordinal);
    }

    /// <summary>⑦ 那颗异常两头都给：屏幕要键、日志要字符串，参数名不许在这一刀里丢掉。</summary>
    [Fact]
    public void TheExceptionCarriesBothHalves()
    {
        var reason = LocMessage.Of("capture.gdi.textOutFailed", 5);
        var ex = new CaptureFailureException(reason, "bgra");
        Assert.Same(reason, ex.Reason);
        Assert.Equal("bgra", ex.ParamName);
        Assert.Equal(reason.Render(), ex.Message);            // 日志与崩溃转储那条出口照旧是字符串
        Assert.Same(reason, CaptureFailureException.ReasonOf(ex));

        var foreign = new InvalidOperationException("boom");
        var fallback = CaptureFailureException.ReasonOf(foreign);
        Assert.Equal("common.rawValue", fallback.Key);
        Assert.Equal(new object?[] { "boom" }, fallback.Args);
    }
}
