#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 截图那一路送到屏幕上的话（批次 WI-46a／46b／46c，P-177 三刀）：右下角提示卡的标题与正文、
/// 遮罩窗跟随鼠标那颗错误角标、以及 Core 里"回原因"的那几句判据。
/// <para>三刀的分工：46a＝截图本身，46b＝识字那一路，46c＝贴图与贴图组（这一族的抬头那颗「贴图」与 46a 共用一颗键）。</para>
/// <para>
/// <b>为什么这一族的取词可以发生在调用点</b>：提示卡是建好即弃的瞬时表面（批次 RV-1），
/// 错误角标也只在出事那一刻写一次（写完就被下一次成功覆盖）——两者都不靠"换语言时重贴"活着。
/// 所以 <c>Locale.T/Fmt</c> 当场取词是合法的，要点是<b>字⛔ 住在代码里</b>。
/// ⛔ 把它们挂进 <c>LocText</c> 重贴名单：那条卡下一刻就不在了，名单里只会多几条再也贴不出去的空壳
/// （口径与 <see cref="CanvasWiringGateTests.CanvasNoticeSentencesAreAllTakenFromTheTable"/> 一致）。
/// </para>
/// <para>
/// <b>射程＝屏幕上那一处同时显示哪几句</b>（坑表 #357／#365）：卡片的抬头那颗"谁在说话"与标题、正文
/// 一起出现，所以 <c>category</c> 也在射程内——它从前是一条带默认值的参数，"忘了传"也会替程序说话，
/// 于是贴图那一路可能顶着一张"截图"的卡。这一批把默认值去掉：谁开口谁报名字，编译器替这条把关。
/// </para>
/// <para>
/// ⚠ 卡片<b>底部那一行</b>（"点一下关闭 · N 秒后自动消失"）⛔ 在这一批改：它是每一张卡共用的句子，
/// 提前交出去＝还没交完的那几族每张卡从此半英半中。它归这一族的最后一刀（见 P-177 的补记）。
/// </para>
/// </summary>
public sealed class CaptureNoticeWiringGateTests
{
    private const string Screenshot = "src/StarMark.UI/Services/ScreenshotService.cs";
    private const string Overlay = "src/StarMark.UI/Views/CaptureOverlayWindow.xaml.cs";
    private const string Geometry = "src/StarMark.Core/Capture/CaptureGeometry.cs";
    private const string HotkeyGateFile = "src/StarMark.Core/Hotkeys/HotkeyGate.cs";
    private const string CaptureGateFile = "src/StarMark.Core/Hotkeys/CaptureGate.cs";
    private const string PinRoster = "src/StarMark.UI/Services/PinManager.cs";
    private const string Grouping = "src/StarMark.Core/Capture/PinGrouping.cs";
    private const string Ocr = "src/StarMark.UI/Services/OcrService.cs";
    private const string OcrReadFile = "src/StarMark.Core/Ocr/OcrRead.cs";
    private const string OcrTextFile = "src/StarMark.Core/Ocr/OcrText.cs";
    private const string OcrPipelineFile = "src/StarMark.Core/Ocr/OcrPipeline.cs";
    private const string SnapshotPartial = "src/StarMark.UI/Services/CanvasService.Snapshot.cs";
    private const string HostPartial = "src/StarMark.UI/Services/CanvasService.Host.cs";
    private const string Gdi = "src/StarMark.Integrations/Capture/GdiScreenCapture.cs";

    /// <summary>
    /// 每一张失败脸都有人问：少一条＝那条失败从此静默（用户只会以为"这个键坏了"）。
    /// 计数按<b>语义单位</b>钉（一句面话一次取词），⛔ 按标点相邻钉（坑表 #363 那一族假红）。
    /// </summary>
    [Fact]
    public void CaptureCardSentencesAreAllTakenFromTheTable()
    {
        var svc = SourceGate.Code(SourceGate.ReadRepoFile(Screenshot));

        // ① 五张"截图失败"的失败脸共用同一颗标题键：抓帧空／没显示器／建窗抛／裁不动／取像素抛
        Assert.Equal(5, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.failedTitle\")"));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.disabledTitle\")"));
        Assert.Equal(2, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.copyFailedTitle\")"));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.encodeFailed\")"));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.copiedTitle\")"));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.copiedBody\""));
        Assert.Equal(2, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.saveFailedTitle\")"));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.savedTitle\")"));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.saveWriteFailed\")"));
        // ② 两条关于机器的事实：截图这边一处，另一处在画布那一路（同一颗键，⛔ 两个出处）
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"capture.grab.noFrame\")"));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"capture.grab.noMonitors\")"));
        // ③ 抬头那颗"谁在说话"：两条选区出口 + 那颗工厂 = 3 处，且⛔ 再带默认值
        Assert.Equal(3, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.category\")"));
        Assert.DoesNotContain("string category =", svc);
        // ④ "被关掉了"那句的正文＝闸门那颗判据自己交的话，⛔ 在这包一层 common.rawValue（那道门只装念完的成品字；
        //    ✅WI-65 片3 第二刀撤的门，由这一行钉着它⛔ 回来）
        Assert.Equal(1, SourceGate.Count(svc,
            "Report(LocMessage.Of(\"capture.card.disabledTitle\"), CaptureGate.ReasonFor(CaptureBlock.Disabled)!);"));
        // ④ 旧句子一概不许回到代码里（现在由表里说）。逐颗点名，⛔ 用"汉字总数下降"那种整体口径。
        //    ⚠ 短到与日志撞车的那一颗要**带引号点名**：`StarLog.Error($"[{category}] 存图失败（{path}）")` 里的
        //    "存图失败"是日志措辞，⛔ 上屏（射程按"会不会出现在这一处屏幕上"划，坑表 #365；日志⛔ 进表是既有口径）。
        foreach (var dead in new[]
        {
            "截屏功能已关闭", "截图失败", "系统没有返回画面", "系统没有报告任何显示器",
            "把画面编成图片时失败", "的画面已进剪贴板", "已复制", "复制失败",
            "已存图", "\"存图失败\"", "写入图片文件时出错", "\"截图\"",
        })
            Assert.DoesNotContain(dead, svc);
    }

    /// <summary>
    /// 遮罩窗上那颗错误角标与"谁在说话"那两条：贴图态与截图态共用同一颗三元表达式，
    /// 所以这一批把<b>两半一起</b>交出去——只交一半＝贴出去的那张卡半英半中。
    /// </summary>
    [Fact]
    public void OverlayErrorChipAndCategoryAreTakenFromTheTable()
    {
        var all = SourceGate.Code(SourceGate.ReadRepoPartials(Overlay));
        foreach (var key in new[]
        {
            "capture.strip.composeFailed", "capture.strip.cropFailed", "capture.strip.mosaicFailed",
            "capture.strip.resizeFailed", "capture.strip.drawFailed", "capture.strip.textNoFocus",
            "capture.grab.frameNotShown",
        })
            Assert.Equal(1, SourceGate.Count(all, key));
        Assert.Equal(1, SourceGate.Count(all, "LocMessage.Of(\"capture.card.category\")"));
        // 贴图那一路自己的句子归下一刀，但抬头那颗名字⛔ 再抄一份：它已经立在这一行的表里
        Assert.Equal(1, SourceGate.Count(all, "LocMessage.Of(\"pin.card.category\")"));
        foreach (var dead in new[]
        {
            "标注没能合成", "选区那块画面没能裁出来", "打码预览没能刷到屏幕上", "这一改画不出来",
            "标注没能画上去", "这一行字还没拿到键盘焦点", "这一帧没能铺到屏幕上",
            "? \"贴图\" : \"截图\"",                              // 那一整颗三元表达式必须两半都走表
        })
            Assert.DoesNotContain(dead, all);
        // ⚠⛔ 把这一档里剩下的汉字串一并列为禁项：`DeviceName = "贴图"` 只进日志（三处读者全是 StarLog），
        //   而工具条与贴图右键菜单那一族（`Pickers`／`Pin` 那 26 处）是**另一处屏幕宿主**，归新立的 P-179。
        //   射程按"这一处屏幕同时显示哪几句"划，⛔ 按文件划（坑表 #365／#367）。
    }

    /// <summary>
    /// "回原因的判据"住在 Core，但那句话是直接念给人看的（<c>ShowError(reason)</c>／卡的正文），
    /// 所以词同样从表里取——先例是 <see cref="StarMark.Core.Hotkeys.CaptureGate"/>（批次 WI-2t）。
    /// <para>✅WI-65 片3 第二刀把这一格的射程从"⛔ 写死汉字"加宽到"<b>也不许当场念出来</b>"（P-194 那一族）：
    /// 判据回的是「键＋实参」，取词发生在<b>那句话上屏的那一拍</b>。前两档（<c>CaptureGeometry</c> 与两道闸门）
    /// 的读者都是一直开着的表面（贴图条／常驻工具条），念早一次就停在旧语言上。</para>
    /// </summary>
    [Fact]
    public void CoreReasonSentencesAreTakenFromTheTable()
    {
        var geo = SourceGate.Code(SourceGate.ReadRepoFile(Geometry));
        Assert.Equal(1, SourceGate.Count(geo, "LocMessage.Of(\"capture.select.offscreen\")"));
        Assert.Equal(1, SourceGate.Count(geo, "LocMessage.Of(\"capture.select.tooSmall\""));
        Assert.Equal(1, SourceGate.Count(geo, "LocMessage.Of(\"capture.grab.noDisplay\")"));
        Assert.Equal(1, SourceGate.Count(geo, "LocMessage.Of(\"capture.grab.outsideFrame\")"));
        foreach (var dead in new[] { "选区落在所有显示器之外", "选区太小", "本机没有可用的显示器画面", "选区不在已捕获的画面范围内" })
            Assert.DoesNotContain(dead, geo);
        // ② 两道闸门（截图总开关／画布热键）：✅片3 第二刀起各档两颗键交「键＋实参」，整档一颗当场取词都不留
        var hotkeyGate = SourceGate.Code(SourceGate.ReadRepoFile(HotkeyGateFile));
        var captureGate = SourceGate.Code(SourceGate.ReadRepoFile(CaptureGateFile));
        foreach (var code in new[] { hotkeyGate, captureGate })
        {
            Assert.Equal(0, SourceGate.Count(code, "Locale.T("));
            Assert.Equal(0, SourceGate.Count(code, "Locale.Fmt("));
        }
        Assert.Equal(2, SourceGate.Count(hotkeyGate, "LocMessage.Of(\"hotkey.gate."));
        Assert.Equal(2, SourceGate.Count(captureGate, "LocMessage.Of(\"hotkey.gate."));
        Assert.Contains("public static LocMessage? ReasonNotRunning(", hotkeyGate);
        Assert.Contains("public static LocMessage? ReasonFor(", captureGate);
        // ⚠ 同档里⛔ 上屏的那一句仍然留在代码里：`throw new ArgumentOutOfRangeException("DPI 缩放因子必须为正")`
        // 是给日志与开发者的参数校验（它一旦说出来就是"程序自己坏了"，⛔ 进表＝让表替 bug 说话）。
        // 同档的贴图上限那两句本批已交表（`pin.card.*`，见 <see cref="PinAndGroupCardsAreTakenFromTheTable"/>）。
    }

    /// <summary>
    /// 贴图与贴图组那一路（批次 WI-46c，P-177 第三刀）：卡上的抬头、标题、正文与"回原因"的两句全部交表。
    /// <para><b>抬头那颗「贴图」⛔ 再立一行</b>：它和 WI-46a 为 `_pinned ? 贴图 : 截图` 立的是同一颗
    /// <c>pin.card.category</c>（一个词两个出处＝改了其中一处，同一族卡的抬头从此各说各话）。</para>
    /// <para><b>这一族坏过的形状是"念法被当成身份存下来"</b>：从前 <c>PinGrouping</c> 有一颗
    /// <c>const string NamePrefix</c>，而 <c>PinGroup.Name</c> 在建组那一刻把结果存进字段 ⇒ 换语言后托盘与条上那几行
    /// 停在旧语言。现在 const 没了、名字每次现算，所以这两种"回来的形状"各列一颗禁项。</para>
    /// <para>⚠ 「组 N（M 张）」那个计数形状只许 <c>pin.groupLabel</c> 一个主人（托盘那一行读的也是它），
    /// 「关闭这组」那颗菜单行因此交<b>纯短语</b>；上限那句里引用的两个名字（托盘那一行、关闭那一条）也交键。</para>
    /// </summary>
    [Fact]
    public void PinAndGroupCardsAreTakenFromTheTable()
    {
        var pin = SourceGate.Code(SourceGate.ReadRepoFile(PinRoster));
        var geo = SourceGate.Code(SourceGate.ReadRepoFile(Geometry));
        var grouping = SourceGate.Code(SourceGate.ReadRepoFile(Grouping));

        // ① 抬头两颗（贴图／贴图组）＋那一排的范围名：按主人点名，⛔ 用"汉字总数下降"那种整体口径
        Assert.Equal(8, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.category\")"));
        Assert.Equal(8, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.groupCategory\")"));   // 7 处 Report + 那句正文里的引用
        Assert.Equal(2, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.scopeAll\")"));   // ✅片5b：交键，⛔ 当场念完
        // ② 每张卡的标题各有几张脸（少一条＝那条从此静默；"没有可操作的贴图"三处共用一颗）
        //    ✅WI-65 片3 第二刀：那四处把判据自己交的话<b>原样递进卡里</b>（第三个实参就是那颗 <c>none</c>／<c>full</c>），
        //    ⛔ 在这里包一层 <c>common.rawValue</c>——那道门只装念完的成品字，包上去就等于替用户把语言钉在判据那一刻。
        Assert.Equal(3, SourceGate.Count(pin,
            "TrayReporter.Report(LocMessage.Of(\"pin.card.category\"), LocMessage.Of(\"pin.card.noneTitle\"), none);"));
        Assert.Equal(1, SourceGate.Count(pin,
            "TrayReporter.Report(LocMessage.Of(\"pin.card.category\"), LocMessage.Of(\"pin.card.limitTitle\"), full);"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.limitTitle\")"));
        Assert.Equal(3, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.noneTitle\")"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.pinnedTitle\")"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.pinFailedTitle\")"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.throughMissTitle\")"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.throughPartialTitle\")"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.groupFullTitle\")"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.groupedTitle\")"));
        Assert.Equal(2, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.groupGoneTitle\")"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.joinedTitle\")"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.leftTitle\")"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.groupClosedTitle\")"));
        // ③ 带插值的正文：一颗都不许漏（漏掉的那条只会以中文出现在英文界面上）
        Assert.Equal(1, SourceGate.Count(pin, "Locale.Fmt(\"pin.card.pinnedBody\""));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.throughMissBody\")"));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.groupedBody\""));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.goneBeforeBody\""));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.goneOnClickBody\""));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.leftBody\""));
        Assert.Equal(1, SourceGate.Count(pin, "LocMessage.Of(\"pin.card.throughPartialBody\""));
        // ④ 回原因那两句住 Core，但话是说给人看的 ⇒ 词同样进表（先例＝WI-2t／WI-46a）。
        //    ✅WI-65 片3 第二刀起那两句交的是「键＋实参」⛔ 在 Core 里先念成字——念出来就把语言冻在了判据那一刻。
        Assert.Equal(1, SourceGate.Count(geo, "LocMessage.Of(\"pin.card.limitBody\""));
        Assert.Equal(1, SourceGate.Count(geo, "LocMessage.Of(\"pin.card.noneBody\")"));
        // ⑤ 念法与形状各只一个主人：组名＝`pin.group.name`，"名字（张数）"＝`pin.groupLabel`
        //    ✅WI-65 片5b 起那五颗念法全部交「键＋实参」⇒ 那一档自己⛔ 取词，两把尺都得读成 0。
        Assert.Equal(1, SourceGate.Count(grouping, "LocMessage.Of(\"pin.group.name\""));
        Assert.Equal(1, SourceGate.Count(grouping, "LocMessage.Of(\"pin.groupLabel\""));   // 只有 GroupLabel 自己；CloseLabel 借它
        Assert.Equal(0, SourceGate.Count(grouping, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(grouping, "Locale.Fmt("));
        Assert.Equal(1, SourceGate.Count(grouping, "LocMessage.Of(\"pin.group.limitBody\""));
        Assert.Equal(0, SourceGate.Count(grouping, "Locale.Fmt(\"pin.group.limitBody\""));   // ✅片4：那句⛔ 在 Core 里念完
        Assert.Equal(3, SourceGate.Count(pin, "PinGrouping.GroupLabel("));                    // 172/190/248：那里⛔ 再拼一份「（N 张）」
        Assert.Equal(0, SourceGate.Count(pin, "Locale.Fmt(\"pin.groupLabel\""));
        Assert.Equal(2, SourceGate.Count(pin, "PinGrouping.NameOf(serial)"));
        // ✅WI-65 片5b 逐字钉「递进槽的是那颗键」：内层名字一旦被当场念完，提示卡就冻在贴出来那一刻（#393／P-196 那一族）
        Assert.Contains("LocMessage.Of(\"pin.card.groupedBody\", PinGrouping.GroupLabel(group.Name, 1), LocMessage.Of(\"pin.card.groupCategory\"))", pin);
        Assert.Contains("PinGrouping.GroupLabel(target.Name, target.Members.Count))", pin);
        // ⑥ 旧句子一概不许回来（短词带引号点名：`[Pin] 贴图未能钉住` 那一族是日志措辞，⛔ 上屏）
        foreach (var dead in new[]
        {
            "\"贴图\"", "\"贴图组\"", "\"全部贴图\"", "已达上限", "穿透未跟上新贴图", "系统拒绝了这一张的扩展样式",
            "已钉住", "张，上限", "钉住失败", "没有可操作的贴图", "不能再分组", "已分组", "那一组已经不在了",
            "在本次操作前被关掉", "在本次点击前被关掉", "已并入", "还剩", "已关闭这一组", "穿透未能全部生效",
        })
        {
            Assert.DoesNotContain(dead, pin);
            Assert.DoesNotContain(dead, geo);
            Assert.DoesNotContain(dead, grouping);
        }
        // ⑦ 两种坏掉的形状：那颗 const 回来了＝念法又被当成身份；名字被存进字段＝换语言后那一行不再动
        Assert.DoesNotContain("public const string NamePrefix", grouping);
        Assert.DoesNotContain("Name { get; } = PinGrouping.NameOf", pin);
        Assert.DoesNotContain("关闭这组（", grouping);          // 「N 张」⛔ 由 pin.groupClose 自己带
    }

    /// <summary>
    /// 同一件关于机器的事实只许一个出处：画布那一路从前自己立过两颗同名行，本批并进来。
    /// 这一格钉的是"画布那边问的还是那两颗键"，⛔ 让它退回一颗只有画布念得到的名字。
    /// <para><b>✅WI-65 片4 加宽</b>：那两颗"键"里有一颗的<b>正文</b>根本⛔ 在 C# 的键上——画布那三张卡的
    /// 失败正文有三分之二来自 <c>GdiScreenCapture</c> 里写死的八句中文（P-194 那把尺只数 <c>Locale.T(</c>／
    /// <c>Locale.Fmt(</c>，那八颗一颗也看不见＝账本 P-192 那句"原生互调的 reason 串"）。这一格因此把那颗
    /// 盲区一起钉住：回型升 <c>LocMessage?</c>、八颗 Fail 各交一颗键、那一档⛔ 再留一句写死的中文。</para>
    /// </summary>
    [Fact]
    public void TheCanvasChainBorrowsTheSameTwoMachineFacts()
    {
        Assert.Equal(1, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(SnapshotPartial)),
            "LocMessage.Of(\"capture.grab.noFrame\")"));
        Assert.Equal(1, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(HostPartial)),
            "LocMessage.Of(\"capture.grab.noMonitors\")"));
        Assert.False(StarMark.Abstractions.I18n.UiStrings.TryGet("canvas.shot.noFrame", out _));
        Assert.False(StarMark.Abstractions.I18n.UiStrings.TryGet("canvas.notice.noMonitors", out _));

        // ② 抓屏那八句：一句一颗键，Win32 码／尺寸／异常正文走实参（先例＝✅WI-60b 系统监控那三型读数）。
        var gdi = SourceGate.Code(SourceGate.ReadRepoFile(Gdi));
        Assert.Contains("public sealed record CaptureResult(bool Ok, ScreenFrame? Frame, LocMessage? Error)", gdi);
        Assert.Equal(8, SourceGate.Count(gdi, "CaptureResult.Fail("));
        Assert.Equal(8, SourceGate.Count(gdi, "LocMessage.Of(\"capture.grab."));
        foreach (var key in new[] { "zeroSize", "tooLarge", "noScreenDc", "noMemoryDc", "noBitmap",
                                   "blitFailed", "readPixelsFailed", "failed" })
            Assert.Equal(1, SourceGate.Count(gdi, "LocMessage.Of(\"capture.grab." + key + "\""));
        // ③ 那一档自己⛔ 取词（交「键＋实参」＝这一族的界线，✅WI-53／54／58 同一条），也⛔ 留一句写死的中文。
        Assert.Equal(0, SourceGate.Count(gdi, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(gdi, "Locale.Fmt("));
        foreach (var dead in new[] { "系统报告的桌面尺寸为 0", "超出可截取上限", "取不到屏幕设备上下文",
                                    "创建内存设备上下文失败", "创建位图失败", "拷贝屏幕失败",
                                    "读出像素失败", "截屏异常" })
            Assert.DoesNotContain(dead, gdi);
        // ④ 撤门的形状：那两档消费点现在直接把判据递进提示卡，⛔ 再包一道 common.rawValue。
        Assert.Equal(1, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(SnapshotPartial)),
            "NoShot(captured.Error ?? LocMessage.Of(\"capture.grab.noFrame\"))"));
        Assert.Equal(0, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(Screenshot)),
            "LocMessage.Of(\"common.rawValue\", grabError"));
    }

    /// <summary>
    /// 识字那一路（批次 WI-46b，P-177 第二刀）：卡上的标题、正文与那行"怎么认的"全部交表。
    /// <para>⚠ `ocr.card.noReason` 只有<b>一处</b>被问（Core 的 `OcrPipeline`）：✅WI-65 片3 第二刀起 UI 那侧
    /// 不再替它兜底——同一颗键⛔ 两个出处，而"引擎连原因都没给"这件事只由管线那一处回答。</para>
    /// <para>⚠ `ocr.how.*` 这一族的存在理由：从前片段各自开头带一颗全角逗号，"语言没报＋放大了"那一格
    /// 屏幕上会露出「，放大 2×」——分隔符现在由拼接处放一次（`OcrReadTests` 里有那一格的读数）。</para>
    /// </summary>
    [Fact]
    public void OcrCardSentencesAndTheHowNoteAreTakenFromTheTable()
    {
        var svc = SourceGate.Code(SourceGate.ReadRepoFile(Ocr));
        var read = SourceGate.Code(SourceGate.ReadRepoFile(OcrReadFile));
        var text = SourceGate.Code(SourceGate.ReadRepoFile(OcrTextFile));
        var pipe = SourceGate.Code(SourceGate.ReadRepoFile(OcrPipelineFile));
        var overlay = SourceGate.Code(SourceGate.ReadRepoPartials(Overlay));

        // ① 三张失败脸各有话说（引擎坏了／认出个空／写不进剪贴板），成功那一张也要报数
        Assert.Equal(2, SourceGate.Count(svc, "LocMessage.Of(\"ocr.card.failedTitle\")"));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"ocr.card.emptyTitle\")"));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"ocr.card.copiedTitle\")"));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"ocr.card.copyFailedBody\""));
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"ocr.card.copiedBody\""));
        // ② 抬头那颗必传（⛔ 默认值），两态各报自己的名字；同一颗"复制失败"与截图那一路共用一个出处
        Assert.Equal(2, SourceGate.Count(svc, "LocMessage.Of(\"ocr.card.category\")"));
        Assert.DoesNotContain("string category =", svc);
        Assert.Equal(1, SourceGate.Count(svc, "LocMessage.Of(\"capture.card.copyFailedTitle\")"));
        Assert.Equal(1, SourceGate.Count(overlay, "LocMessage.Of(\"capture.card.categoryOcr\")"));
        Assert.Equal(1, SourceGate.Count(overlay, "LocMessage.Of(\"pin.card.categoryOcr\")"));
        // ③ "怎么认的"那三片＋分隔符＋括号：Core 一份，UI⛔ 再写第二份括号。
        //    ✅WI-65 片3 第二刀起那一串交的是 <c>LocJoin</c>＝连连接符本身也进表（`ocr.how.join`），
        //    括号那颗交的是「键＋那一句」⇒ 这一档从此⛔ 出现当场取词（念法冻在建对象那一刻＝坑表 #362 那一族）。
        Assert.Equal(1, SourceGate.Count(read, "new LocJoin("));
        Assert.Equal(1, SourceGate.Count(read, "LocMessage.Of(\"ocr.how.byLanguage\""));
        Assert.Equal(1, SourceGate.Count(read, "LocMessage.Of(\"ocr.how.upscale\""));
        Assert.Equal(1, SourceGate.Count(read, "LocMessage.Of(\"ocr.how.swapped\")"));
        Assert.Equal(1, SourceGate.Count(read, "LocMessage.Of(\"ocr.how.paren\""));
        Assert.Equal(1, SourceGate.Count(read, "\"ocr.how.join\""));
        Assert.Equal(0, SourceGate.Count(read, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(read, "Locale.Fmt("));
        Assert.DoesNotContain("private static string Paren(", svc);
        // ④ 三张卡的正文各由一颗胶水位把两段连起来：⛔ 在 C# 里拼 `+`——那是把语序定死在中文那一侧（规则 #100）
        Assert.Equal(3, SourceGate.Count(svc, "LocMessage.Of(\"ocr.body.join\""));
        Assert.Equal(0, SourceGate.Count(svc, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(svc, "Locale.Fmt("));
        // ⑤ 引擎自己那句话是机器文本 ⇒ 数据门立在它<b>进来的那一处</b>（Core 的管线），⛔ 留在 UI 的 `??` 里；
        //    "连原因都没给"这件事从此只由管线那一处回答（同一颗键⛔ 两个出处）
        Assert.Equal(1, SourceGate.Count(pipe, "LocMessage.Of(\"common.rawValue\", engine)"));
        Assert.Equal(1, SourceGate.Count(pipe, "LocMessage.Of(\"ocr.card.noReason\")"));
        Assert.Equal(0, SourceGate.Count(svc, "ocr.card.noReason"));
        Assert.Equal(1, SourceGate.Count(text, "LocMessage.Of(\"ocr.text.noText\")"));
        // ⑥ 旧句子一概不许回来（带引号的短词同 WI-46a 那条口径：裸词会撞上日志措辞）
        foreach (var dead in new[]
        {
            "没能识别", "没认出文字", "已复制文字", "识别引擎没有给出原因", "但没能写进剪贴板",
            "字但", "\"识字\"", "这块画面里没有认出文字", "按 {Language}", "已换语言",
        })
        {
            Assert.DoesNotContain(dead, svc);
            Assert.DoesNotContain(dead, text);
            Assert.DoesNotContain(dead, pipe);
        }
        Assert.DoesNotContain("\"识字\"", read);
    }
}
