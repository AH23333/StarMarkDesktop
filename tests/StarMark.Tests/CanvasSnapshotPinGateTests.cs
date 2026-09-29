#nullable enable
using StarMark.Core.Hotkeys;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 S4-⑤「Board 快照成贴」的<b>接线</b>闸门：贴图那一条落点裁到哪、钉在哪、三条落点在哪儿分岔。
/// <para>
/// 这一格的病形状是"编译得过、旧测试一条不红、真机上按一次贴图整块桌面就被一张静止的图盖住"：
/// 贴图在 Z 序名册里压在画布玻璃之上（<c>LayerRules</c>：穿透态 Pin=3、Board=4）又默认吃鼠标，
/// 所以<b>整屏一张</b>等于把用户的桌面封起来。像素对不对只能眼睛看，这里钉<b>结构</b>：
/// ① 贴图必须走 <c>CanvasSnapshotMath</c> 那三条纯函数（判据不许住在 UI 里再写一份）；
/// ② 墨的落位必须在<b>叠底之前</b>量（顺序反了整幅都算有墨，裁等于没裁，而单测照旧全绿）；
/// ③ 复制／存图<b>不</b>裁——这是三条落点唯一的有意分岔，必须钉住，否则下一轮会被"好心"统一掉；
/// ④ 板上没墨时给一句看得见的原因并点名另一条出口，不许退回"那就贴整屏"。
/// </para>
/// <para>算术本身在 <see cref="CanvasSnapshotMathTests"/>。</para>
/// </summary>
public sealed class CanvasSnapshotPinGateTests
{
    private const string Service = "src/StarMark.UI/Services/CanvasService.cs";
    private const string SnapshotMath = "src/StarMark.Core/Canvas/CanvasSnapshot.cs";
    private const string ToolbarXaml = "src/StarMark.UI/Views/CanvasToolbarWindow.xaml";
    private const string ActionsFile = "src/StarMark.Core/Hotkeys/HotkeyActions.cs";

    /// <summary>
    /// 贴图那一条必须<b>裁到墨那一块再交出去</b>，并把落点换成"这块屏的原点 + 裁块在画面里的偏移"——
    /// 两者缺一都会回到"整屏盖桌面"。反向写法（直接把整幅画面与整块屏交出去）当场钉死。
    /// </summary>
    [Fact]
    public void ThePinIsCroppedToTheInkRegionAndPinnedBackAtItsOwnPlace()
    {
        var pin = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Service), "public static void SnapshotToPin()");

        Assert.Contains("CanvasSnapshotMath.PaddingDip * shot.Scale", pin);   // 边是 DIP：要按这块屏的缩放换算
        Assert.Contains("CanvasSnapshotMath.FrameOf(shot.Ink, shot.Width, shot.Height, padding)", pin);
        Assert.Contains("CanvasSnapshotMath.CropBgra(shot.Pixels, shot.Width, shot.Height, region)", pin);
        Assert.Contains("shot.Screen.X + region.X, shot.Screen.Y + region.Y", pin);
        Assert.Contains("region.Width, region.Height", pin);                  // 尺寸跟着裁块走，不是整幅

        // 旧形状：整幅画面 + 整块屏＝那张盖住桌面的静止图
        Assert.DoesNotContain("PinPixels(shot.Pixels", pin);
    }

    /// <summary>
    /// 墨的落位在<b>叠幕布那层底之前</b>量。叠过之后每个像素的 alpha 都是 128（半透明底），
    /// 判据会返回"整幅"，于是"只裁笔迹那一块"当场失效——而 <c>InkRegionOf</c> 的单测测的是判据本身，
    /// 顺序错了它一条都不红。所以顺序只能由接线这一层钉。
    /// <para><b>两条锚点都写成带分号的调用式</b>：这一版刚写出来时被自己的注释骗过一次——
    /// 注释里那句"而 InkRegionOf 的单测照旧全绿"含有裸方法名，把 <c>IndexOf</c> 抢先带走了，
    /// 于是"把测量挪到叠底之后"这个变异让闸门照样绿。方法体内的注释也是正文，裸名字数不住。</para>
    /// </summary>
    [Fact]
    public void TheInkRegionIsMeasuredBeforeTheBackdropIsBlendedIn()
    {
        var compose = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Service), "private static Shot Compose()");
        const string measure = "var inkRegion = CanvasSnapshotMath.InkRegionOf(ink, boardWidth, boardHeight);";
        const string blend = "screen.Window.CompositeForSnapshot(ink);";

        Assert.Contains(measure, compose);
        Assert.Contains(blend, compose);
        Assert.True(compose.IndexOf(measure, System.StringComparison.Ordinal)
            < compose.IndexOf(blend, System.StringComparison.Ordinal),
            "先叠幕布再量墨＝整幅都算有墨＝裁了等于没裁（桌面又被盖住了）");
    }

    /// <summary>
    /// 三条落点读同一份像素（<c>Compose()</c> 只有一处），但<b>只有贴图裁</b>：
    /// 复制／存图交出去的是一整页（留档要看整块板子的上下文），这是本批唯一的有意分岔。
    /// 不钉住它，下一轮就会有人把三条"顺手统一"成裁的（或反过来说服用户整屏才对）。
    /// </summary>
    [Fact]
    public void OnlyThePinCrops_CopyAndSaveStillHandOverTheWholeFrame()
    {
        var service = SourceGate.ReadRepoPartials(Service);
        foreach (var anchor in new[] { "public static void SnapshotToClipboard()", "public static void SavePng()" })
        {
            var body = SourceGate.MethodBody(service, anchor);
            Assert.Equal(1, SourceGate.Count(body, "Compose();"));
            Assert.DoesNotContain("CropBgra", body);
            Assert.DoesNotContain("FrameOf", body);
            Assert.Contains("shot.Pixels, shot.Width, shot.Height", body);
        }
        // 合成的入口只有一处：三条落点各调一次，别处不许再起一份"怎么合成"
        Assert.Equal(3, SourceGate.Count(service, "Compose();"));
    }

    /// <summary>
    /// 板上一块墨都没有 ⇒ 没有"哪一块"可贴。这一条必须<b>说清并点名另一条出口</b>：
    /// 退回"那就贴整屏"是本批修掉的病，静默什么都不做在用户眼里等于软件坏了。
    /// <para>而那句出口必须报<b>用户当前实际绑着的那颗键</b>（<c>BindingText</c>），不许写死"F1"——
    /// 键位是他自己可改的，把人支使去按一颗可能没用的键，等于给一条说明配了一颗哑键。</para>
    /// </summary>
    [Fact]
    public void AnEmptyBoardGivesAReasonAndTheOtherExitInsteadOfAFullScreenPin()
    {
        var pin = SourceGate.MethodBody(SourceGate.ReadRepoPartials(Service), "public static void SnapshotToPin()");

        Assert.Contains("shot.Ink.IsEmpty", pin);
        Assert.Contains("板上还没有笔迹可贴", pin);
        Assert.Contains("BindingText(HotkeyActions.ScreenCapture)", pin);     // 报的是当前绑定的键，不是写死的 F1
        Assert.DoesNotContain("（F1）", pin);
        var report = pin.IndexOf("板上还没有笔迹可贴", System.StringComparison.Ordinal);
        var crop = pin.IndexOf("CropBgra", System.StringComparison.Ordinal);
        Assert.True(report < crop, "空板必须在裁之前拦：那时 region 是空的，CropBgra 只会交出一张零字节的贴图");
    }

    /// <summary>
    /// 那颗按钮的悬停说明与热键目录里那一行，说的必须是<b>同一件事</b>（贴的是笔迹那一块）。
    /// <para>文案分岔的形状："条上写着整屏、点出来是一小块"或反过来——用户按哪一句做事，只有他自己承担。</para>
    /// </summary>
    [Fact]
    public void TheButtonHintAndTheHotkeyDirectoryTellTheSameStory()
    {
        var xaml = SourceGate.ReadRepoFile(ToolbarXaml);
        var actions = SourceGate.ReadRepoFile(ActionsFile);
        var pinHint = SourceGate.Between(xaml, "Content=\"贴图\"", "/>");

        Assert.Contains("只裁笔迹那一块", pinHint);
        Assert.Contains("不挡桌面", pinHint);
        Assert.DoesNotContain("屏幕画面 + 笔迹合成一张钉在桌面", xaml);       // 旧那句＝整屏，已不是事实
        Assert.Contains("画布 · 贴到桌面（笔迹那一块", SourceGate.MethodBody(actions, "public static string DisplayName"));
    }

    /// <summary>
    /// 判据只许有一份：三条纯函数住在 Core，UI 只调用。
    /// UI 侧若再出现一份"扫 alpha 求包围盒"或"逐行拷子矩形"，两处迟早对不上（症状＝贴图少一圈／错位）。
    /// </summary>
    [Fact]
    public void TheJudgementsLiveInCoreAndAreNotReimplementedInTheUi()
    {
        var service = SourceGate.ReadRepoPartials(Service);
        var math = SourceGate.ReadRepoFile(SnapshotMath);

        Assert.Contains("public static IntRect InkRegionOf", math);
        Assert.Contains("public static IntRect FrameOf", math);
        Assert.Contains("public static byte[] CropBgra", math);
        Assert.Equal(1, SourceGate.Count(math, "Buffer.BlockCopy"));

        var compose = SourceGate.MethodBody(service, "private static Shot Compose()");
        // UI 侧不自己算 alpha 阈值，也不自己写循环拷像素
        Assert.DoesNotContain("BlankPixel", compose);
        Assert.DoesNotContain("BlockCopy", compose);
        Assert.DoesNotContain("Math.Clamp", SourceGate.MethodBody(service, "public static void SnapshotToPin()"));
    }

    /// <summary>
    /// 空板那句说明指向的出口是<b>截图链</b>，所以那条动作必须真的存在、目录里也说得出它是干什么的。
    /// 指一条没有的动作＝把人支使去按一颗哑键（这条纪律来自批次 WD-8：说明里不许出现没用的那颗键）。
    /// </summary>
    [Fact]
    public void TheExitNamedInTheReasonActuallyExists()
    {
        Assert.Contains(HotkeyActions.ScreenCapture, HotkeyActions.All());
        Assert.Contains("截图", HotkeyActions.DisplayName(HotkeyActions.ScreenCapture));
    }
}
