#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Core.Boards;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 桥那张白名单表的<b>行为</b>证人（批次 Phase C 片12＝v1 §21.1 的落码）。
/// <para>这里钉的是"<b>名单外的东西进不来</b>"这件具体的事：一条越权消息被读成放行，屏幕上什么都不会发生，
/// 只会留下"我明明让它存了它没存"——所以每一颗拒都要有名字、每一颗拒都要被真测到。</para>
/// </summary>
public sealed class BoardBridgeProtocolTests
{
    private const string Export = "export.bytes";
    private static readonly string[] ExportArgs = ["bytes", "kind", "sessionId"];

    // ────────── ① 两份名单自己先要闭合 ──────────

    /// <summary>
    /// 六颗进、五颗出，且<b>每颗在册的名字都有一份字段表、每份字段表都对应一颗在册的名字</b>。
    /// <para>双向对账的原因：只查"名单里有 6 颗"会放过"新增一颗命令却忘了给字段表"——
    /// 那种消息在 <see cref="BridgeProtocol.Check"/> 里会当场 <c>KeyNotFoundException</c>，
    /// 而抛出去到 UI 侧就是"点了没反应"（本仓定性过的坏）。</para>
    /// </summary>
    [Fact]
    public void BothListsAreClosedOverTheirOwnFieldTables()
    {
        Assert.Equal(
            ["bridge.ready", "paste.query", Export, "pin.request", "background.request", "open.external"],
            BridgeProtocol.InboundCommands);
        Assert.Equal(
            ["board.load", "theme.set", "lang.set", "feature.flags", "export.pick"],
            BridgeProtocol.OutboundEvents);

        Assert.Equal(BridgeProtocol.InboundCommands.Length, BridgeProtocol.InboundFields.Count);
        foreach (var name in BridgeProtocol.InboundCommands)
            Assert.True(BridgeProtocol.InboundFields.ContainsKey(name), $"进向名单里的 {name} 没有字段表");
        foreach (var name in BridgeProtocol.InboundFields.Keys)
            Assert.Contains(name, BridgeProtocol.InboundCommands);

        Assert.Equal(BridgeProtocol.OutboundEvents.Length, BridgeProtocol.OutboundFields.Count);
        foreach (var name in BridgeProtocol.OutboundEvents)
            Assert.True(BridgeProtocol.OutboundFields.ContainsKey(name), $"出向名单里的 {name} 没有字段表");
        foreach (var name in BridgeProtocol.OutboundFields.Keys)
            Assert.Contains(name, BridgeProtocol.OutboundEvents);
    }

    /// <summary>
    /// v0 那两颗被摘掉的命令⛔ 许回来（这颗钉子是为本批那次更正本身存在的：
    /// <c>scene.save</c>／<c>scene.dirty</c> 一旦被"顺手补上"，等于回到"JS 决定何时存"那一版设计）。
    /// </summary>
    [Theory]
    [InlineData("scene.save")]
    [InlineData("scene.dirty")]
    [InlineData("scene/load")]                      // 名字像也不放行
    [InlineData("EXPORT.BYTES")]                   // 大小写不同＝另一颗名字
    [InlineData("export.bytes ")]                  // 尾空格
    [InlineData("")]
    public void OffTheListIsRefusedByName(string? name)
    {
        var (allowed, fault) = BridgeProtocol.Check(BridgeProtocol.Version, name, ExportArgs);
        Assert.False(allowed);
        Assert.Equal(BridgeProtocol.Fault.UnknownName, fault);
    }

    [Fact]
    public void NullNameIsRefusedAndNeverThrows()
    {
        var (allowed, fault) = BridgeProtocol.Check(BridgeProtocol.Version, null, null);
        Assert.False(allowed);
        Assert.Equal(BridgeProtocol.Fault.UnknownName, fault);
        Assert.False(BridgeProtocol.IsKnownName(null, BridgeProtocol.Direction.Inbound));
    }

    // ────────── ② 字段：多一颗就拒，⛔ 忽略 ──────────

    /// <summary>
    /// 在册命令带了未列的键＝整条拒（含那颗 <c>path</c>）。
    /// <para>"忽略未知键"是这条协议最坏的一种松：web 侧递 <c>path</c> 而宿主照旧干活，
    /// §十 那句"保存任意路径→全拒"就成了空话。</para>
    /// </summary>
    [Theory]
    [InlineData("path")]
    [InlineData("Path")]
    [InlineData("absolutePath")]
    [InlineData("dir")]
    [InlineData("fileName")]
    [InlineData("")]
    public void ASingleExtraFieldRejectsTheWholeMessage(string extra)
    {
        var keys = ExportArgs.Append(extra).ToArray();
        var (allowed, fault) = BridgeProtocol.Check(BridgeProtocol.Version, Export, keys);
        Assert.False(allowed);
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField, fault);
    }

    [Fact]
    public void TheDocumentedArgumentSetPasses()
    {
        var (allowed, fault) = BridgeProtocol.Check(BridgeProtocol.Version, Export, ExportArgs);
        Assert.True(allowed);
        Assert.Equal(BridgeProtocol.Fault.None, fault);
    }

    /// <summary>⛔ 参的那颗命令被塞了键也要拒（<c>bridge.ready</c> 的字段表是空的）。</summary>
    [Fact]
    public void NoArgCommandRejectsAnyArg()
    {
        var (allowed, fault) = BridgeProtocol.Check(BridgeProtocol.Version, "bridge.ready", new[] { "v" });
        Assert.False(allowed);
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField, fault);

        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "bridge.ready", Array.Empty<string>()).Allowed);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "background.request", null).Allowed);
    }

    // ────────── ③ 缺实参与版本 ──────────

    [Theory]
    [InlineData("open.external", new string[0])]
    [InlineData(Export, new string[] { "kind" })]                 // 有档没字节
    [InlineData(Export, new string[] { "bytes", "sessionId" })]   // 有字节没档
    [InlineData("pin.request", new string[0])]
    [InlineData("paste.query", new string[0])]
    public void MissingRequiredArgumentIsItsOwnFault(string name, string[] keys)
    {
        var (allowed, fault) = BridgeProtocol.Check(BridgeProtocol.Version, name, keys);
        Assert.False(allowed);
        Assert.Equal(BridgeProtocol.Fault.MissingField, fault);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void AForeignEnvelopeVersionIsRefusedBeforeAnythingElse(int v)
    {
        Assert.Equal(1, BridgeProtocol.Version);
        var (allowed, fault) = BridgeProtocol.Check(v, Export, ExportArgs);
        Assert.False(allowed);
        Assert.Equal(BridgeProtocol.Fault.WrongVersion, fault);
    }

    /// <summary>
    /// 出向（C#→JS）那两颗也过同一道缺参门：宿主自己拼的信封，少一颗键现场就是"点了没反应"。
    /// <para>v1 §21.1 那张 args 列早就写着 <c>board.load</c> 带 <c>bytes</c>＋<c>id</c>、<c>export.pick</c> 带
    /// <c>kind</c>——这一格钉的是"写在表上的事有一道门守着"，同时钉住⛔ 把门扩到那三颗本就无参的事件上
    /// （<c>theme.set</c>／<c>lang.set</c>／<c>feature.flags</c> 各一颗值，缺一颗就是缺那颗，⛔ 另设一道门）。</para>
    /// </summary>
    [Fact]
    public void TheHostsOwnEnvelopesAreCheckedToo()
    {
        Assert.Equal(BridgeProtocol.Fault.MissingField,
            BridgeProtocol.Check(BridgeProtocol.Version, "board.load", new[] { "bytes" },
                BridgeProtocol.Direction.Outbound).Reason);
        Assert.Equal(BridgeProtocol.Fault.MissingField,
            BridgeProtocol.Check(BridgeProtocol.Version, "export.pick", Array.Empty<string>(),
                BridgeProtocol.Direction.Outbound).Reason);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "board.load", new[] { "bytes", "id" },
            BridgeProtocol.Direction.Outbound).Allowed);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "export.pick",
            new[] { "kind", "skipInliningFonts" }, BridgeProtocol.Direction.Outbound).Allowed);
        // 三颗无必需项的事件：递上那颗值就通（这道门⛔ 顺手扩到它们＝第二份判据）
        foreach (var (name, key) in new[] { ("theme.set", "theme"), ("lang.set", "langCode"),
                                            ("feature.flags", "uiOptions") })
            Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, name, new[] { key },
                BridgeProtocol.Direction.Outbound).Allowed, $"{name} 被这道多出来的门挡住了");
    }

    // ────────── ④ 方向决定名单（同一颗名字⛔ 能两头用） ──────────

    [Fact]
    public void EachDirectionHasItsOwnList()
    {
        // 出向的名字从进向递进来＝拒
        Assert.Equal(BridgeProtocol.Fault.UnknownName,
            BridgeProtocol.Check(BridgeProtocol.Version, "board.load", new[] { "bytes", "id" }).Reason);
        // 进向的名字从出向递出去＝拒（宿主自己把 pin.request 发给页面，就是那条越权路径）
        Assert.Equal(BridgeProtocol.Fault.UnknownName,
            BridgeProtocol.Check(BridgeProtocol.Version, "pin.request", new[] { "token" },
                BridgeProtocol.Direction.Outbound).Reason);
        // 各自方向上都通
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "board.load", new[] { "bytes", "id" },
            BridgeProtocol.Direction.Outbound).Allowed);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "theme.set", new[] { "theme" },
            BridgeProtocol.Direction.Outbound).Allowed);
    }

    // ────────── ⑤ 档名：路径永远进不来 ──────────

    /// <summary>
    /// §18.4 那一格真机递过的三颗＋两颗新形状：收成裸文件名之后⛔ 带目录、⛔ 是空、⛔ 是保留名。
    /// <para>这颗证人钉的是"落点归 C#"那条硬线的<b>可执行形式</b>——读起来像常识，
    /// 而真机上那颗 <c>a.download</c> 会被 WebView2 丢成 <c>blob:</c> 的 UUID，所以"永远有个名"也是它的一部分。</para>
    /// </summary>
    [Theory]
    [InlineData(@"..\..\evil.png")]
    [InlineData("D:/Visual Studio Code/evil2.png")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nul")]                                   // Windows 保留名
    [InlineData("COM1.png")]
    [InlineData("con.json")]
    [InlineData("a/b/c/d/e/f/board.svg")]
    [InlineData(@"\\server\share\x.png")]
    public void ExportNameIsAlwaysABareSafeFileName(string? suggested)
    {
        var name = BridgeProtocol.SanitizeExportName(suggested, "board-20261017-071500", "png");

        Assert.EndsWith(".png", name, StringComparison.Ordinal);
        Assert.DoesNotContain("/", name, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", name, StringComparison.Ordinal);
        Assert.DoesNotContain(":", name, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.DoesNotContain("nul", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COM1", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("con.", name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>好名字照原样活下来（⛔ 为了消毒把用户起的名一律换成兜底名＝另一种坏）。</summary>
    [Fact]
    public void APlainSuggestedNameSurvives()
    {
        Assert.Equal("我的板.png", BridgeProtocol.SanitizeExportName("我的板.png", "fallback", "png"));
        Assert.Equal("arch-2.png", BridgeProtocol.SanitizeExportName("  arch-2.PNG  ", "fallback", "png"));
        // web 侧那颗扩展名⛔ 说话：档位是 scene，落盘就是板档，哪怕名字里写着 .png
        Assert.Equal("我的板.excalidraw", BridgeProtocol.SanitizeExportName("我的板.png", "fallback", "scene"));
    }

    /// <summary>超长主干换兜底名（截一半会留一颗认不出的名，比换名更坏）。</summary>
    [Fact]
    public void OverlongStemFallsBackInsteadOfBeingTruncated()
    {
        var name = BridgeProtocol.SanitizeExportName(new string('x', BridgeProtocol.MaxStemLength + 1) + ".png",
            "fallback", "png");
        Assert.Equal("fallback.png", name);
    }

    /// <summary>
    /// 扩展名<b>只</b>由档位枚举给：不认识的名字给 <c>null</c>，判据⛔ 替它编一颗。
    /// <para>这一格是本批撞出来的真缺陷：那颗判据先写成"点＋字母数字"的形状检查，
    /// 于是 <c>.exe</c> 一路合规。形状合不合规不是问题，<b>谁有资格说这颗文件是什么</b>才是——
    /// web 侧递的串⛔ 是主人（§18.4 现读它递上来的本来就是 <c>blob:</c> 的 UUID）。</para>
    /// </summary>
    [Theory]
    [InlineData("png", ".png")]
    [InlineData("svg", ".svg")]
    [InlineData("scene", ".excalidraw")]               // 与 BoardPolicy.SceneExtension 同一颗（⛔ 两处各写）
    [InlineData("exe", null)]
    [InlineData(".png", null)]                         // 传扩展名而不是档位＝⛔ 认（那颗参数已经换成档位）
    [InlineData("PNG", null)]                          // 档位名在册是 Ordinal（与 Check 那颗口径一致）
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtensionComesFromTheKindTableNotFromWeb(string? kind, string? expected)
        => Assert.Equal(expected, BridgeProtocol.ExtensionForKind(kind));

    /// <summary>
    /// 档位名单与那颗 switch <b>一颗不许漏</b>：加一档而忘了给后缀，现场就是"导出的板子全叫 .png"。
    /// <para>反空转（#161）：上面那格是"逐颗给读数"，这一格是"颗数对得上"——缺其一，
    /// 新增档位时要么无声无息，要么全靠人记得去补 InlineData。</para>
    /// </summary>
    [Fact]
    public void EveryExportKindHasExactlyOneExtension()
    {
        Assert.Equal(BridgeProtocol.ExportKinds.Length,
            BridgeProtocol.ExportKinds.Count(k => BridgeProtocol.ExtensionForKind(k) is not null));
        Assert.All(BridgeProtocol.ExportKinds, k =>
            Assert.True(BridgeProtocol.ExtensionForKind(k)!.StartsWith('.'), $"{k} 的后缀没带点"));
    }

    /// <summary>
    /// 那颗兜底链本身：null 输入 + 保留名 + 档位不认识三样叠在一起，也必得回一颗可用档名
    /// （"每次导出都弹错"就是这种叠法造出来的）。
    /// </summary>
    [Fact]
    public void EvenAStackedBadNameYieldsAUsableFileName()
    {
        var name = BridgeProtocol.SanitizeExportName("nul", "board-261017-071500-abcd", "exe");
        Assert.Equal("board-261017-071500-abcd.png", name);
    }

    // ────────── ⑥ 外链闸门 ──────────

    [Theory]
    [InlineData("https://docs.excalidraw.com/docs/x", true)]
    [InlineData("http://example.com", true)]
    [InlineData("https://excalidraw.com#help", true)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,x", false)]
    [InlineData("mailto:someone@example.com", false)]
    [InlineData("ftp://host/pub", false)]
    [InlineData("https://user:token@github.com/x", false)]     // 凭据会跟着跳出去
    [InlineData("https:///nohost", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("not a url", false)]
    public void OnlyPlainHttpHttpsLeavesTheSandbox(string? url, bool expected)
        => Assert.Equal(expected, BridgeProtocol.IsSafeExternalUrl(url));

    /// <summary>档位与粘贴形状那两张小表也是"在册⛔ 等于放行"的两颗名字表。</summary>
    [Fact]
    public void KindAndPasteShapeTablesAreEnumeratedOnce()
    {
        Assert.Equal(["png", "svg", "scene"], BridgeProtocol.ExportKinds);
        Assert.Equal(["elements", "spreadsheet", "text", "errorMessage"], BridgeProtocol.PasteShapes);
        Assert.True(BridgeProtocol.IsKnownExportKind("svg"));
        Assert.False(BridgeProtocol.IsKnownExportKind("SVG"));
        Assert.False(BridgeProtocol.IsKnownExportKind(null));
        Assert.True(BridgeProtocol.IsKnownPasteShape("spreadsheet"));
        Assert.False(BridgeProtocol.IsKnownPasteShape("image"));   // 图那一档⛔ 在这四档里（§20.3 现读那四档）
    }
}
