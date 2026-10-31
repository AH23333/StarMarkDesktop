#nullable enable
using System;
using System.Linq;
using Xunit;
using StarMark.Core.Boards;

namespace StarMark.Tests;

/// <summary>
/// 深析 §四 第2行那一格：画板右上角那颗「贴到桌面」——同一发 PNG 的<b>第二种下落</b>（片151）。
/// <para>这一格坏的方式有两种，都不是"点了没反应"那种一眼看得出的：
/// ① 那颗落点位（<c>_exportPins</c>）如果只在"字节真回来"那一支清掉，下一发按「导出 PNG」就会
///    <b>悄悄贴到桌面上</b>而屏幕上说的是"已存到 &lt;路径&gt;"——那颗按钮上的字与它真做的事分岔，
///    比崩难归因一个量级（S1 那条口径：会改状态的收尾，最后一步要重读，⛔ 复用入口快照）；
/// ② 那一发如果走了下面写档那一路再顺手贴一次，用户就同时得到一张档与一张贴图，
///    而「打开所在文件夹」那颗资格会指向一张<b>用户没要过</b>的档（P-219 那句"界面上要给出口"的反面）。</para>
/// <para>片150 曾按 P-212 口径把这两颗键当孤儿摘掉（那时只有句子没有码）；本档同时是"句子重新有读者"那颗证人。</para>
/// </summary>
public sealed class BoardPinExitGateTests
{
    private const string WindowPath = "src/StarMark.UI/Boards/BoardWindow.cs";
    private const string CardActionsPath = "src/StarMark.UI/Helpers/ItemCardActions.cs";
    private const string StringsPath = "src/StarMark.Abstractions/I18n/UiStrings.cs";

    private static string Code => SourceGate.Code(SourceGate.ReadRepoFile(WindowPath));

    // ─────────────── ① 那颗入口本身在不在、通不通 ───────────────

    /// <summary>
    /// 第五颗出口三处都要在：造那一行、按下去问那一发、以及右上角那句落点的话。
    /// <para>摆位钉在「导出 PNG」与「导出 SVG」<b>之间</b>：它与前者是同一发的两种下落，
    /// 中间插进那颗 SVG 会让人以为这一颗也产一张档（两处菜单各排各的＝ClipIMG 那一族的老脸）。</para>
    /// </summary>
    [Fact]
    public void TheFifthExitIsWiredAtAllThreePlaces()
    {
        var code = Code;
        Assert.Contains(
            "_barPinButton.Click += (_, _) => ExportImageButton(\"png\", pinsInsteadOfSave: true);",
            code, StringComparison.Ordinal);
        Assert.Contains("_barPinButton.Content = Word(\"boards.pinAction\");", code, StringComparison.Ordinal);
        Assert.Contains("_barPinButton.IsEnabled = live;", code, StringComparison.Ordinal);
        Assert.Contains("_barPinButton is null", code, StringComparison.Ordinal);   // 重贴那一拍的闸门也认得它

        var pngAt = code.IndexOf("barRow.Children.Add(_barPngButton);", StringComparison.Ordinal);
        var pinAt = code.IndexOf("barRow.Children.Add(_barPinButton);", StringComparison.Ordinal);
        var svgAt = code.IndexOf("barRow.Children.Add(_barSvgButton);", StringComparison.Ordinal);
        Assert.True(pngAt > 0 && pinAt > pngAt && svgAt > pinAt,
            "那一行上的顺序必须是 PNG → 贴到桌面 → SVG（同一发的两种下落要挨着）");
    }

    /// <summary>那两颗键重新有读者了（片150 摘孤儿键时说的就是"等有了读者再一起回来"）。</summary>
    [Fact]
    public void TheTwoSentencesHaveTheirReaderAgain()
    {
        var table = SourceGate.ReadRepoFile(StringsPath);
        Assert.Contains("new Entry(\"boards.pinAction\"", table, StringComparison.Ordinal);
        Assert.Contains("new Entry(\"boards.pinnedBody\"", table, StringComparison.Ordinal);
        var code = Code;
        Assert.Contains("Word(\"boards.pinAction\")", code, StringComparison.Ordinal);
        Assert.Contains("ReportExport(\"boards.pinnedBody\", null);", code, StringComparison.Ordinal);
        Assert.Contains("\"boards.pinnedBody\" => Word(\"boards.pinnedBody\"),", code, StringComparison.Ordinal);
    }

    // ─────────────── ② 那颗落点位：存在、且只在一处收口 ───────────────

    /// <summary>
    /// <c>_exportPins</c> 整档只许两处赋值：<b>一处立</b>（开口之前）、<b>一处清</b>（<see cref="ReportExport"/> 那一闩的
    /// 唯一收口处）。清那一发⛔ 许只长在"字节真回来"那一支——三条下落（真回来／页面说办不成／预算到点）
    /// 都走那一颗，少一条就把"这一发是贴桌面"活到下一发。
    /// </summary>
    [Fact]
    public void TheDestinationFlagIsSetOnceAndClearedAtTheSingleExit()
    {
        var code = Code;
        Assert.Equal(2, SourceGate.Count(code, "_exportPins = "));
        var setter = SourceGate.MethodBody(code, "void ExportImageButton");
        var clearer = SourceGate.MethodBody(code, "void ReportExport");
        Assert.Contains("_exportPins = pinsInsteadOfSave && kind == \"png\";", setter, StringComparison.Ordinal);
        Assert.Contains("_exportPins = false;", clearer, StringComparison.Ordinal);
        // 分流那一支当然<b>读</b>这颗位（读⛔ 写）——禁的是在这里清它：清只长在"字节真回来"那一支，
        // nack 与预算到点那两条路就漏了，而那颗位活到下一发＝下一发「导出 PNG」悄悄贴到桌面上。
        Assert.DoesNotContain("_exportPins =", SourceGate.MethodBody(code, "void HandleExportBytes"),
            StringComparison.Ordinal);
    }

    /// <summary>三条下落都真走那颗收口处（否则上面那颗"只在一处清"是空的）。</summary>
    [Fact]
    public void AllThreeOutcomesPassThroughThatSingleExit()
    {
        foreach (var anchor in new[] { "void HandleBridgeReply", "Task WatchExportAnswerAsync" })
            Assert.Contains("ReportExport(", SourceGate.MethodBody(Code, anchor), StringComparison.Ordinal);
        // 字节真回来那一支：写档那一路与贴桌面那一路各自结一次
        var landed = SourceGate.MethodBody(Code, "void HandleExportBytes");
        Assert.Contains("ReportExport(\"boards.exportSavedBody\", target);", landed, StringComparison.Ordinal);
        Assert.Contains("PinExportedPng(bytes);", landed, StringComparison.Ordinal);
    }

    // ─────────────── ③ 那一发⛔ 顺手写一张档 ───────────────

    /// <summary>
    /// 落点是桌面那一支必须<b>排在起档名之前</b>直接返回：它既不产文件，也⛔ 参与
    /// 「打开所在文件夹」那颗资格（那颗资格的唯一写处是 <c>landed is not null</c>）。
    /// </summary>
    [Fact]
    public void ThePinnedExportNeverNamesOrWritesAFile()
    {
        var landed = SourceGate.MethodBody(Code, "void HandleExportBytes");
        var branch = landed.IndexOf("if (_exportPins", StringComparison.Ordinal);
        var naming = landed.IndexOf("BridgeProtocol.UniqueExportName(", StringComparison.Ordinal);
        Assert.True(branch > 0, "落点分流那一支不在了");
        Assert.True(branch < naming, "贴桌面那一支排在起档名之后＝先替用户造一张他没要过的档");
        Assert.Contains("return;\n        }\n        var name", landed, StringComparison.Ordinal);

        var pinned = SourceGate.MethodBody(Code, "void PinExportedPng");
        Assert.Contains("ReportExport(\"boards.pinnedBody\", null);", pinned, StringComparison.Ordinal);
        Assert.DoesNotContain("_exportPath", pinned, StringComparison.Ordinal);   // 那颗资格⛔ 由这一路碰
    }

    /// <summary>
    /// 解码吃仓里那一颗<b>纯托管</b> PNG 解码器，摆位吃剪贴板那条贴图路同一颗判据——
    /// 两问各配一份新实现＝这一格最容易"顺手长出来"的第二真值（⛔ 在这里再写一份 PNG 解码／再算一遍居中）。
    /// </summary>
    [Fact]
    public void TheDecodeAndThePlacementReuseTheExistingOwners()
    {
        var pinned = SourceGate.MethodBody(Code, "void PinExportedPng");
        Assert.Contains("ClipboardPayload.TryDecodePng(", pinned, StringComparison.Ordinal);
        Assert.Contains("ItemCardPolicy.CenteredPlacement(", pinned, StringComparison.Ordinal);
        Assert.Contains("ScreenshotService.PinPixels(", pinned, StringComparison.Ordinal);
        Assert.Equal(0, SourceGate.Count(pinned, "BitmapDecoder"));
        Assert.Equal(0, SourceGate.Count(pinned, "new IntRect"));

        // 成对的那一半：剪贴板那条路还在用同一颗摆放判据（两路分岔＝两句话）
        var card = SourceGate.MethodBody(
            SourceGate.Code(SourceGate.ReadRepoFile(CardActionsPath)), "void PinImageToDesktop");
        Assert.Contains("ItemCardPolicy.CenteredPlacement(", card, StringComparison.Ordinal);
    }

    /// <summary>解不开那一句要说得出原因：那颗 <c>why</c> 进日志，屏幕上走"没能弄成"那一句。</summary>
    [Fact]
    public void AnUndecodableExportSaysSoInsteadOfStayingSilent()
    {
        var pinned = SourceGate.MethodBody(Code, "void PinExportedPng");
        Assert.Contains("boards.pin.decode.failed", pinned, StringComparison.Ordinal);
        Assert.Contains("why=\" + whyNot", pinned, StringComparison.Ordinal);
        Assert.Contains("ReportExport(\"boards.exportFailedBody\", null);", pinned, StringComparison.Ordinal);
    }

    // ─────────────── ④ 档位名单⛔ 因为这一格而变 ───────────────

    /// <summary>
    /// 「贴到桌面」是<b>那颗 PNG 的另一种下落</b>，⛔ 是第三种档位：档位名单还是三颗，
    /// 而名单外那一档（svg）即使带着 <c>pinsInsteadOfSave</c> 也立不起那颗位。
    /// </summary>
    [Fact]
    public void PinningIsADestinationAndNotAThirdExportKind()
    {
        Assert.Equal(new[] { "png", "svg", "scene" }, BridgeProtocol.ExportKinds);
        Assert.Contains(
            "_exportPins = pinsInsteadOfSave && kind == \"png\";", Code, StringComparison.Ordinal);
        // 那一发的问句只有一颗形状：⛔ 为"贴"另拼一发 export.pick（第二颗发送处＝第二个主人）
        Assert.Equal(1, SourceGate.Count(Code, "PostOutbound(\"export.pick\""));
        Assert.Equal(1, SourceGate.Count(Code, "Envelope(\"export.pick\""));
    }
}
