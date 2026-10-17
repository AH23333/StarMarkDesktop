#nullable enable
using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using StarMark.Core.Boards;

namespace StarMark.UI.Boards;

/// <summary>
/// 画板那一扇窗（Phase C 片19＝§21 v1 的最小回路）：WebView2 虚拟宿主映随包资产、
/// board.html 挂引擎、消息进出全部过 Core 的 <see cref="BridgeProtocol"/>。
/// <para><b>生命周期档位＝§九 裁决 2「关窗即散」</b>：<see cref="Closed"/> 里 Close 掉
/// WebView2 控件（最后一扇走时浏览器进程树跟着散），userDataFolder 钉在
/// <c>%LOCALAPPDATA%\StarMark\wbview2</c>（v1 §坑8 那条改道）。</para>
/// <para><b>默认关</b>：本窗没有常驻入口，只有显式 <see cref="OpenNewAsync"/>／<see cref="OpenSceneAsync"/>；
/// 托盘那两颗（续上一块／新起一块）由接线批那颗总开关收放（本档⛔ 自己挂上去——"找不到人接又自己冒出来"是第二主人）。</para>
/// <para><b>回路现状</b>：bridge.ready／open.external／export.bytes 三颗全通；
/// paste.query／pin.request／background.request 回 <c>not-wired</c>——判定归各自批次（⛔ 假 allow）。</para>
/// <para><b>落盘与载入（片21＝§21.3）</b>：页面侧停笔 <see cref="BoardPolicy.AutosaveIdleMs"/> 毫秒自己交一份
/// 场景 JSON（<c>export.bytes</c>＋<c>kind:"scene"</c>，⛔ 新名字），宿主失焦那一拍再索取一次（<c>export.pick</c>）；
/// 字节经 <see cref="BridgeProtocol.LandsInSceneArchive"/> 分流进板档目录，png／svg 仍进导出目录。
/// <b>片22＝§21.4 那颗脏位</b>：交回来的字节与盘上那份<b>逐颗相等</b>时⛔ 写、⛔ 滚（<see cref="BoardPolicy.DiskAlreadyHolds"/>）；
/// 同批把 <c>export.bytes</c> 的 <c>suggested</c> 接上链——页面递 <c>appState.name</c>，只有导出那一路读它（板档档名＝那颗 id，⛔ 跟着显示名走）。
/// ⚠ 四拍里现在通了<b>两</b>拍（停笔／失焦）：渲染进程挂了那一拍页面已经死了、索取不到任何东西，
/// 关窗那一拍在窗已拆之后再收 <c>postMessage</c> 也来不及——那两拍归后续批次，本档⛔ 假装接了。</para>
/// </summary>
public sealed class BoardWindow : Window
{
    /// <summary>虚拟宿主名（本类是它的唯一主人；闸门测试读这颗常量）。</summary>
    public const string VirtualHostName = "appassets.local";

    /// <summary>页面入口（相对随包根）。</summary>
    public const string BoardPageName = "board.html";

    /// <summary>userDataFolder 那条改道（v1 §坑8）：钉在 <c>%LOCALAPPDATA%</c> 之下。</summary>
    public const string UserDataFolderRelative = @"StarMark\wbview2";

    /// <summary>导出产物那颗落点（硬线①：落点由 C# 定，web 侧只交字节）。
    /// <para>它<b>住在板档目录之下</b>：那颗根只许有一位主人（<see cref="BoardSceneStore.Folder"/>），
    /// 这里只负责在它下面再切一颗 <c>exports\</c>——自己再拼一遍 <c>%APPDATA%\StarMark</c> 就是第二主人，
    /// 而那份抄本⛔ 认沙盒改道（片23 归一的就是这一族）。</para></summary>
    public static string ExportsDirectory => Path.Combine(BoardSceneStore.Folder, "exports");

    private readonly Microsoft.UI.Xaml.Controls.WebView2 _web;
    private string _boardId;
    private byte[]? _pendingScene;

    /// <summary>盘上主档那一刻是哪些字节（只有宿主自己写得动它，所以这一份状态有唯一主人）。
    /// null＝这扇窗还没写过、也没<b>从主档</b>载过（新板，或档是从 <c>.bak</c> 捡回来的）。</summary>
    private byte[]? _lastWritten;
    private bool _bridgeReady;

    public BoardWindow()
    {
        _boardId = BoardPolicy.NewSceneId(DateTimeOffset.Now, Random.Shared.Next(0, 0x10000));
        Title = "StarMark Boards";
        _web = new Microsoft.UI.Xaml.Controls.WebView2 { DefaultBackgroundColor = Microsoft.UI.Colors.White };
        Content = _web;
        Closed += (_, _) => OnClosed();
        // 失焦那一拍（§21.3 四拍其二）：向页面索取一份场景快照。⛔ 在这里判"要不要写"——
        // 写不写由 Core 的档位分流说了算，这一处只负责"该问一次了"。
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated) RequestSceneSnapshot();
        };
    }

    /// <summary>那颗板 id（<see cref="BoardPolicy.NewSceneId"/> 的形状；宿主是它的主人）。</summary>
    public string BoardId => _boardId;

    /// <summary>起一扇新板（没带任何档）。</summary>
    public System.Threading.Tasks.Task OpenNewAsync()
    {
        _lastWritten = null;   // 盘上还没有这颗板的档：第一拍照常规写，别把"还没写过"读成"已经一样"
        return OpenAsync(null);
    }

    /// <summary>
    /// 接着开那颗已有的板：先按 id 把档读进内存，等 <c>bridge.ready</c> 那一刻再递给页面。
    /// <para>顺序是反不得的：桥没通就发 <c>board.load</c>，页面侧那位 <c>API</c> 还是 null，
    /// 场景就永远进不去（而宿主这边一句错都⛔ 报）。档读不到＝退回新板，⛔ 弹"档不见了"那种废话。</para>
    /// <para>从 <c>.bak</c> 捡回来的那一档⛔ 点亮脏位：主档位置上是空的，所以"盘上已经有了"那句⛔ 成立，
    /// 得让第一拍把主档补回去（否则那颗板在 <see cref="BoardSceneStore.LatestSceneId"/> 那一问里始终是缺的）。</para>
    /// </summary>
    public System.Threading.Tasks.Task OpenSceneAsync(string sceneId)
    {
        if (!BoardSceneStore.TryLoad(sceneId, out var bytes, out var fromBackup) || bytes.Length == 0) return OpenNewAsync();
        _boardId = sceneId;
        _lastWritten = fromBackup ? null : bytes;
        return OpenAsync(bytes);
    }

    private async System.Threading.Tasks.Task OpenAsync(byte[]? sceneBytes)
    {
        _pendingScene = sceneBytes;
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UserDataFolderRelative);
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userData, new CoreWebView2EnvironmentOptions());
        await _web.EnsureCoreWebView2Async(env);
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "Boards");
        if (!Directory.Exists(assets))
            throw new InvalidOperationException("随包资产不在：" + assets);
        _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHostName, assets, CoreWebView2HostResourceAccessKind.Allow);
        _web.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _web.CoreWebView2.Navigate("https://" + VirtualHostName + "/" + BoardPageName);
    }

    /// <summary>桥是否已就绪（页面递过 <c>bridge.ready</c>）。</summary>
    public bool BridgeReady => _bridgeReady;

    /// <summary>宿主主动发那颗 C#→JS 事件（键集先过 <see cref="BridgeProtocol.Check"/> 的出向门）。</summary>
    public void PostOutbound(string name, string json)
    {
        if (_web.CoreWebView2 is null) return;
        var envelope = BoardEnvelope.Parse(json);
        var (allowed, _) = BridgeProtocol.Check(
            envelope.Version, name, envelope.ArgKeys, BridgeProtocol.Direction.Outbound);
        if (!allowed) return;
        _web.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var envelope = BoardEnvelope.Parse(e.WebMessageAsJson ?? string.Empty);
        var (allowed, fault) = BridgeProtocol.Check(
            envelope.Version, envelope.Cmd, envelope.ArgKeys, BridgeProtocol.Direction.Inbound);
        if (!allowed)
        {
            Reply(envelope.Cmd, envelope.Id, ok: false, err: "fault:" + fault);
            return;
        }

        switch (envelope.Cmd)
        {
            case "bridge.ready":
                _bridgeReady = true;
                Reply(envelope.Cmd, envelope.Id, ok: true);
                OnBridgeReady();
                break;
            case "open.external":
                if (!BridgeProtocol.IsSafeExternalUrl(envelope.Url))
                {
                    Reply(envelope.Cmd, envelope.Id, ok: false, err: "url-rejected");
                    break;
                }
                Reply(envelope.Cmd, envelope.Id, ok: true);
                _ = Helpers.LauncherEx.TryOpenAsync(envelope.Url);
                break;
            case "export.bytes":
                HandleExportBytes(envelope);
                break;
            default:
                // paste.query / pin.request / background.request：回路通了、判定归各自批次（⛔ 假 allow）
                Reply(envelope.Cmd, envelope.Id, ok: false, err: "not-wired");
                break;
        }
    }

    private void HandleExportBytes(BoardEnvelope envelope)
    {
        if (!BridgeProtocol.IsKnownExportKind(envelope.Kind))
        {
            Reply(envelope.Cmd, envelope.Id, ok: false, err: "fault:UnknownName");
            return;
        }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(envelope.Bytes ?? string.Empty); }
        catch (FormatException)
        {
            Reply(envelope.Cmd, envelope.Id, ok: false, err: "bridge-broken");
            return;
        }
        // 那颗档位决定落点（判据住 Core）：scene＝这颗板的本体→板档目录、档名＝那颗 id、滚 .bak；
        // png／svg＝一次性产物→导出目录、名字带时刻。两族⛔ 共用一份命名规则。
        if (BridgeProtocol.LandsInSceneArchive(envelope.Kind))
        {
            // §21.4 那颗脏位＝宿主这一侧的字节对差：一个字节都没变（失焦那一拍最常撞这一档）就⛔ 动盘——
            // 动一次盘就多滚一次 .bak，而滚的那一下把上一份「真正不同」的退路顶成了同件。
            if (BoardPolicy.DiskAlreadyHolds(_lastWritten, bytes))
            {
                Reply(envelope.Cmd, envelope.Id, ok: true);
                return;
            }
            if (BoardSceneStore.TrySave(_boardId, bytes, out var fault))
            {
                _lastWritten = bytes;   // 这一刻起盘上主档就是这份字节
                Reply(envelope.Cmd, envelope.Id, ok: true);
            }
            else
            {
                StarMark.Abstractions.StarLog.Warn("boards.scene.save.failed: " + fault);
                Reply(envelope.Cmd, envelope.Id, ok: false, err: "save-failed");
            }
            return;
        }
        var stem = _boardId + "-" + BoardPolicy.FormatStamp(DateTime.Now);
        var name = BridgeProtocol.SanitizeExportName(envelope.Suggested, stem, envelope.Kind);
        Directory.CreateDirectory(ExportsDirectory);
        File.WriteAllBytes(Path.Combine(ExportsDirectory, name), bytes);
        Reply(envelope.Cmd, envelope.Id, ok: true);
    }

    private void OnBridgeReady()
    {
        // 三颗初始值（§21.5）：宿主是它们的主人；占位档先发中性值，接设置面归后续批次。
        PostOutbound("theme.set", Envelope("theme.set", "h1", "\"theme\":\"light\""));
        PostOutbound("lang.set", Envelope("lang.set", "h2", "\"langCode\":\"zh-CN\""));
        // 接着开的板：这一刻才把档递进去（早一步页面那位 API 还没起来，晚一步没有读者）。
        if (_pendingScene is { Length: > 0 } bytes)
        {
            PostOutbound("board.load", Envelope("board.load", "h3",
                JsonString("bytes") + ":" + JsonString(Convert.ToBase64String(bytes)) + ","
                + JsonString("id") + ":" + JsonString(_boardId)));
        }
        _pendingScene = null;
    }

    /// <summary>向页面索取一份场景快照（失焦那一拍走这里；页面侧回的是 <c>export.bytes</c>＋<c>kind:"scene"</c>）。</summary>
    private void RequestSceneSnapshot()
    {
        if (!_bridgeReady) return;   // 桥没通就索取＝发进一颗没人听的口子（回音都⛔ 有）
        PostOutbound("export.pick", Envelope("export.pick", "h4", JsonString("kind") + ":" + JsonString("scene")));
    }

    private static string Envelope(string cmd, string id, string argsJson)
        => "{" + JsonString("v") + ":1," + JsonString("cmd") + ":" + JsonString(cmd)
           + "," + JsonString("id") + ":" + JsonString(id)
           + "," + JsonString("args") + ":{" + argsJson + "}}";

    private void Reply(string? cmd, string? id, bool ok, string? err = null)
    {
        if (_web.CoreWebView2 is null) return;
        var errPart = err is null ? string.Empty : "," + JsonString("err") + ":" + JsonString(err);
        var json = """{"v":1,""" + JsonString("cmd") + ":" + JsonString(cmd)
                   + "," + JsonString("id") + ":" + JsonString(id)
                   + "," + JsonString("ok") + ":" + (ok ? "true" : "false") + errPart + "}";
        _web.CoreWebView2.PostWebMessageAsJson(json);
    }

    private static string JsonString(string? s) => System.Text.Json.JsonSerializer.Serialize(s);

    private void OnClosed()
    {
        // 决策 2「关窗即散」：页面侧收尾在 web 半边；宿主半边这里拆干净（最后一扇走＝浏览器进程树散）。
        if (_web.CoreWebView2 is not null)
        {
            _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        _web.Close();
    }
}
