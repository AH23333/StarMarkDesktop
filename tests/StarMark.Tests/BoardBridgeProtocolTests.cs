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
    private static readonly string[] ExportArgs = ["bytes", "kind", "suggested"];

    // ────────── ① 两份名单自己先要闭合 ──────────

    /// <summary>
    /// 十颗进、八颗出，且<b>每颗在册的名字都有一份字段表、每份字段表都对应一颗在册的名字</b>。
    /// <para>双向对账的原因：只查"名单里有 6 颗"会放过"新增一颗命令却忘了给字段表"——
    /// 那种消息在 <see cref="BridgeProtocol.Check"/> 里会当场 <c>KeyNotFoundException</c>，
    /// 而抛出去到 UI 侧就是"点了没反应"（本仓定性过的坏）。</para>
    /// <para><b>片56 多的那颗 <c>bridge.reply</c></b>＝页面答宿主问的那一路（账本 P-244）：它早就在发而⛔ 在册，
    /// 于是每一句 nack 死在名字闸门，连 switch 都走不到。这颗钉子同时是<b>颗数的脸</b>——
    /// 名单从七颗变八颗必须由这一格说一次，⛔ 由下一批"顺手补上"。</para>
    /// <para><b>片206 多的那三颗</b>：进向 <c>export.request</c>／<c>scale.request</c>（P-285 重做：引擎主菜单
    /// 那一组的索取），出向 <c>menu.set</c>（那一组的字与亮态）。颗数的脸同一把尺：八颗变十颗、
    /// 七颗变八颗都由这一格说一次。</para>
    /// </summary>
    [Fact]
    public void BothListsAreClosedOverTheirOwnFieldTables()
    {
        Assert.Equal(
            ["bridge.ready", "board.loaded", "paste.query", Export, "export.request", "pin.request", "scale.request",
             "background.request", "open.external", "bridge.reply"],
            BridgeProtocol.InboundCommands);
        Assert.Equal(
            ["board.load", "theme.set", "lang.set", "feature.flags", "export.pick", "autosave.arm", "image.insert",
             "menu.set"],
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

    /// <summary>
    /// 那颗 <c>suggested</c> 在册（片21 的现读缺陷：名单里没它⇒页面一递就整条 <see cref="Fault.UnexpectedField"/>，
    /// 于是 <c>SanitizeExportName</c> 里"网页给的名字"那一支<b>在链路上永远走不到</b>——形参活着、读者永远撞不到），
    /// 而<b>⛔ 在必带名单里</b>：没起过名的板递空串是正常形状，旧页面⛔ 带那颗键也照旧通。
    /// </summary>
    [Fact]
    public void TheSuggestedNameIsAllowedButNeverRequired()
    {
        Assert.Contains("suggested", BridgeProtocol.InboundFields[Export]);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, Export, ["bytes", "kind"]).Allowed);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, Export, ExportArgs).Allowed);
        Assert.Equal(BridgeProtocol.Fault.MissingField,
            BridgeProtocol.Check(BridgeProtocol.Version, Export, ["kind", "suggested"]).Reason);
    }

    /// <summary>
    /// 那颗⛔ 有读者的 <c>sessionId</c> 已被摘掉（片22 欠项④／片23 落地）：它曾在名单里、页面三拍都递、
    /// 宿主解进 <c>BoardEnvelope</c> 之后一颗读者也没有。
    /// <para>为什么⛔ 补一颗读者了事：名单上的键＝<b>越权面</b>（硬线②），留着它等于养一颗"注释承诺了
    /// 一个不存在的消费者"——P-212 那一族在这颗包里的第二颗，只是这回它连上屏都⛔ 够不上。
    /// 真要"哪一拍写的"那颗读数，等它有了读者再带着读者一起回来。</para>
    /// </summary>
    [Fact]
    public void TheFieldNobodyReadsIsOffTheListAgain()
    {
        Assert.Equal(ExportArgs, BridgeProtocol.InboundFields[Export]);   // 名单＝这张实参表，一颗不多一颗不少
        Assert.DoesNotContain("sessionId", BridgeProtocol.InboundFields[Export]);
        var (allowed, fault) = BridgeProtocol.Check(
            BridgeProtocol.Version, Export, ExportArgs.Append("sessionId").ToArray());
        Assert.False(allowed);
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField, fault);
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
    [InlineData(Export, new string[] { "bytes", "suggested" })]   // 有字节没档
    [InlineData("export.request", new string[0])]                 // 片206：要哪一档得自己说（P-285 重做）
    [InlineData("paste.query", new string[0])]
    [InlineData("board.loaded", new string[0])]                   // 片26 第一版的形状：那句话本身⛔ 带数
    [InlineData("board.loaded", new string[] { "elements" })]     // 报了档里几颗，没报案布上几颗
    [InlineData("board.loaded", new string[] { "canvas" })]
    [InlineData("bridge.reply", new string[0])]                    // 片56：一句没内容的 ack（两种下落都⛔ 许替用户猜）
    public void MissingRequiredArgumentIsItsOwnFault(string name, string[] keys)
    {
        var (allowed, fault) = BridgeProtocol.Check(BridgeProtocol.Version, name, keys);
        Assert.False(allowed);
        Assert.Equal(BridgeProtocol.Fault.MissingField, fault);
    }

    /// <summary>
    /// 载档那句回话<b>必带两颗数</b>（片26＝P-223 的协议面）。
    /// <para>本批第一版把它做成了一颗<b>无参</b>的命令，真机跑出来才发现"页面回了话"与"画布上有那块板"
    /// 是两件事（那句无参的话在两种现场上形状相同）。协议里"缺一颗"与"多一颗"这两道门在这一颗上各钉一次，
    /// 名单本身也钉一次——多出来的键⛔ 是忽略而整条拒（硬线②：忽略＝给越权留缝）。</para>
    /// </summary>
    [Fact]
    public void TheLoadReportCarriesBothCounts()
    {
        Assert.Equal(new[] { "elements", "canvas" }, BridgeProtocol.InboundFields["board.loaded"]);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "board.loaded",
            new[] { "elements", "canvas" }).Allowed);
        Assert.Equal(BridgeProtocol.Fault.MissingField,
            BridgeProtocol.Check(BridgeProtocol.Version, "board.loaded", new[] { "canvas" }).Reason);
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField,
            BridgeProtocol.Check(BridgeProtocol.Version, "board.loaded",
                new[] { "elements", "canvas", "bytes" }).Reason);
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
        // 片64＝那两档交回用户（P-242 落地腿）。⚠ 这里加的⛔ 是"方便"，是<b>那道静默的门</b>：
        // 宿主自己拼的信封若带一颗没登记的键，<c>PostOutbound</c> 读到一个 <c>allowed=false</c> 就<b>一颗字节也不发</b>，
        // 屏幕上没有任何一句坏话（那正是片54 关引擎那颗框之后⛔ 许留下的形状）。
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "export.pick",
            new[] { "kind", "exportScale", "exportBackground" }, BridgeProtocol.Direction.Outbound).Allowed);
        // 禁项与正件同一跑：那两颗<b>逐字</b>在册，换个写法（多一颗 s／大小写不同）就拒
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField,
            BridgeProtocol.Check(BridgeProtocol.Version, "export.pick", new[] { "kind", "exportScales" },
                BridgeProtocol.Direction.Outbound).Reason);
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField,
            BridgeProtocol.Check(BridgeProtocol.Version, "export.pick", new[] { "kind", "ExportBackground" },
                BridgeProtocol.Direction.Outbound).Reason);
        // scene 那一发⛔ 用不上那两档（桥在 <c>kind==="scene"</c> 那一支提前 return），登记成可选而⛔ 必带：
        // 四拍里索取板档那两拍⛔ 因此被自己新加的字段挡住（MissingField 那一道只认 kind）
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "export.pick", new[] { "kind" },
            BridgeProtocol.Direction.Outbound).Allowed);
        // 三颗无必需项的事件：递上那颗值就通（这道门⛔ 顺手扩到它们＝第二份判据）
        foreach (var (name, key) in new[] { ("theme.set", "theme"), ("lang.set", "langCode"),
                                            ("feature.flags", "uiOptions") })
            Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, name, new[] { key },
                BridgeProtocol.Direction.Outbound).Allowed, $"{name} 被这道多出来的门挡住了");

        // autosave.arm（片26＝P-221 那颗"载档那一拍已过"）：这颗门故意⛔ 参——它报的是时刻，不是东西。
        // 正负件各一：空 args 必通（宿主每一拍都发得出），多一颗键必拒（⛔ 让它长成第二份判据）。
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "autosave.arm", Array.Empty<string>(),
            BridgeProtocol.Direction.Outbound).Allowed);
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField,
            BridgeProtocol.Check(BridgeProtocol.Version, "autosave.arm", new[] { "id" },
                BridgeProtocol.Direction.Outbound).Reason);

        // image.insert（片148＝图进画板那一发）：四颗键全是必带——少一颗（有图没尺寸）现场就是
        // "点了「放到画板上」、画布上什么都没多"；多一颗没列的键照旧整条拒（硬线②）。
        Assert.Equal(BridgeProtocol.Fault.MissingField,
            BridgeProtocol.Check(BridgeProtocol.Version, "image.insert",
                new[] { "dataUrl", "mimeType", "width" }, BridgeProtocol.Direction.Outbound).Reason);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "image.insert",
            new[] { "dataUrl", "mimeType", "width", "height" },
            BridgeProtocol.Direction.Outbound).Allowed);
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField,
            BridgeProtocol.Check(BridgeProtocol.Version, "image.insert",
                new[] { "dataUrl", "mimeType", "width", "height", "token" },
                BridgeProtocol.Direction.Outbound).Reason);
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

    // ────────── ④b 那句 nack 进得来（片56＝P-244 的第二处） ──────────

    /// <summary>
    /// 页面那句"那一办发不出去"到底通不通这道门。
    /// <para><b>这一格钉的是改前那件事</b>：那颗名字⛔ 在册 ⇒ <see cref="Check"/> 一律拒成
    /// <c>UnknownName</c>，nack 连 UI 那一格都走不到（把 <c>InboundCommands</c> 里那颗删掉，第一行当场红）。
    /// 四种下落各给一颗：带 <c>ok</c> ＝通；只带 <c>ok</c> 也通（那颗 <c>err</c> 是可选的——页面只在真有一句故障名时带）；
    /// ⛔ 带 <c>ok</c> ＝<c>MissingField</c>；多一颗没列的键＝<c>UnexpectedField</c>（硬线②）；
    /// 从<b>出向</b>递进来＝<c>UnknownName</c>（宿主没理由把一句 nack 递给页面，那颗名字只住进向那一侧）。</para>
    /// </summary>
    [Fact]
    public void TheNackPassesTheGateOnlyInItsOwnShape()
    {
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "bridge.reply", new[] { "ok", "err" }).Allowed);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "bridge.reply", new[] { "ok" }).Allowed);
        Assert.Equal(BridgeProtocol.Fault.MissingField,
            BridgeProtocol.Check(BridgeProtocol.Version, "bridge.reply", new[] { "err" }).Reason);
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField,
            BridgeProtocol.Check(BridgeProtocol.Version, "bridge.reply", new[] { "ok", "path" }).Reason);
        Assert.Equal(BridgeProtocol.Fault.UnknownName,
            BridgeProtocol.Check(BridgeProtocol.Version, "bridge.reply", new[] { "ok" },
                BridgeProtocol.Direction.Outbound).Reason);
    }

    // ────────── ④c 引擎主菜单那一组的协议面（片206＝P-285 重做） ──────────

    /// <summary>
    /// 四颗索取＋那一推（<c>menu.set</c>）各自的形状，与页面真正递的对得上。
    /// <para>四颗索取里只有 <c>export.request</c> 带那颗档位——其余三颗无参（<c>pin.request</c> 从前那颗
    /// <c>token</c> 随片206 摘掉：页面从没递过它，名单上的键＝越权面，养一颗没人发的键就是给越权留缝）。
    /// 那一推六颗全必带＝宿主每一推都是<b>整份替换</b>，半份推送会把菜单上那一组冻在旧话上
    /// （缺 busy＝那一闩永远合不上，三颗动作从此点不亮；换语言那一拍漏一颗＝那一项还念旧语言）。</para>
    /// </summary>
    [Fact]
    public void TheMenuRequestFamilyMatchesWhatThePageReallySends()
    {
        // export.request：档位必带（缺＝MissingField），多一颗没列的键＝UnexpectedField
        Assert.Equal(BridgeProtocol.Fault.MissingField,
            BridgeProtocol.Check(BridgeProtocol.Version, "export.request", Array.Empty<string>()).Reason);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "export.request", new[] { "kind" }).Allowed);
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField,
            BridgeProtocol.Check(BridgeProtocol.Version, "export.request", new[] { "kind", "token" }).Reason);
        // 三颗无参索取：空 args 必通，多一颗键必拒
        foreach (var name in new[] { "pin.request", "scale.request", "background.request" })
        {
            Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, name, Array.Empty<string>()).Allowed,
                name + " 空参没过门");
            Assert.Equal(BridgeProtocol.Fault.UnexpectedField,
                BridgeProtocol.Check(BridgeProtocol.Version, name, new[] { "token" }).Reason);
        }
        // 那颗从未被页面递过的 token：pin.request 的字段表从此一颗也不许剩
        Assert.Empty(BridgeProtocol.InboundFields["pin.request"]);
        // menu.set（出向）：六颗全必带＝整份替换，少一颗＝菜单冻在旧话上
        var six = new[] { "png", "svg", "pin", "scale", "background", "busy" };
        Assert.Equal(six, BridgeProtocol.OutboundFields["menu.set"]);
        Assert.True(BridgeProtocol.Check(BridgeProtocol.Version, "menu.set", six,
            BridgeProtocol.Direction.Outbound).Allowed);
        Assert.Equal(BridgeProtocol.Fault.MissingField,
            BridgeProtocol.Check(BridgeProtocol.Version, "menu.set",
                six.Where(k => k != "busy").ToArray(), BridgeProtocol.Direction.Outbound).Reason);
        Assert.Equal(BridgeProtocol.Fault.UnexpectedField,
            BridgeProtocol.Check(BridgeProtocol.Version, "menu.set", six.Append("token").ToArray(),
                BridgeProtocol.Direction.Outbound).Reason);
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

    // ────────── ⑦ 那一发导出的落点（片54＝P-240 的②） ──────────
    // 本批第一版在这里排过一臂 15 格的表，钉的是"从下载报的 mime／档名判出档位"（ExportKindForDownload）。
    // 那颗判据随产品代码一起删了：真机量到那一发压根⛔ 是下载（引擎那颗 saveAs 走 showSaveFilePicker），
    // 逐字读数＝正本 bin/WI93/readings_54_134804.txt＋bin/WI93/log54_forensics.txt（boards.download 零行，
    // 而同一把尺读得到 boards.nav／bridge.ready／arm.post＝尺⛔ 聋）。
    // 还留在链路上的那几件事（档位→后缀→档名、每档各有后缀、坏名字也交得出可用的一颗）
    // 由上面 §⑤ 那三格钉着，⛔ 在这里再列一遍同一串期望（两颗名单迟早只补一处）。

}
