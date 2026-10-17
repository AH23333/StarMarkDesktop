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
/// <para><b>默认关</b>：本窗没有常驻入口，只有显式 <see cref="OpenNewAsync"/>；主窗接线归后续批次
/// （接线那批要给入口上屏字交 <c>LocMessage</c>，本档⛔ 成品字）。</para>
/// <para><b>回路现状</b>：bridge.ready／open.external／export.bytes 三颗全通；
/// paste.query／pin.request／background.request 回 <c>not-wired</c>——判定归各自批次（⛔ 假 allow）。</para>
/// </summary>
public sealed class BoardWindow : Window
{
    /// <summary>虚拟宿主名（本类是它的唯一主人；闸门测试读这颗常量）。</summary>
    public const string VirtualHostName = "appassets.local";

    /// <summary>页面入口（相对随包根）。</summary>
    public const string BoardPageName = "board.html";

    /// <summary>userDataFolder 那条改道（v1 §坑8）：钉在 <c>%LOCALAPPDATA%</c> 之下。</summary>
    public const string UserDataFolderRelative = @"StarMark\wbview2";

    /// <summary>导出产物那颗落点（硬线①：落点由 C# 定，web 侧只交字节）。</summary>
    public static string ExportsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StarMark", "boards", "exports");

    private readonly Microsoft.UI.Xaml.Controls.WebView2 _web;
    private readonly string _boardId;
    private bool _bridgeReady;

    public BoardWindow()
    {
        _boardId = BoardPolicy.NewSceneId(DateTimeOffset.Now, Random.Shared.Next(0, 0x10000));
        Title = "StarMark Boards";
        _web = new Microsoft.UI.Xaml.Controls.WebView2 { DefaultBackgroundColor = Microsoft.UI.Colors.White };
        Content = _web;
        Closed += (_, _) => OnClosed();
    }

    /// <summary>那颗板 id（<see cref="BoardPolicy.NewSceneId"/> 的形状；宿主是它的主人）。</summary>
    public string BoardId => _boardId;

    /// <summary>起窗＋导航；桥就绪以收到 <c>bridge.ready</c> 为准（<see cref="BridgeReady"/>）。</summary>
    public async System.Threading.Tasks.Task OpenNewAsync()
    {
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
        var stem = _boardId + "-" + BoardPolicy.FormatStamp(DateTime.Now);
        var name = BridgeProtocol.SanitizeExportName(envelope.Suggested, stem, envelope.Kind);
        Directory.CreateDirectory(ExportsDirectory);
        File.WriteAllBytes(Path.Combine(ExportsDirectory, name), bytes);
        Reply(envelope.Cmd, envelope.Id, ok: true);
    }

    private void OnBridgeReady()
    {
        // 三颗初始值（§21.5）：宿主是它们的主人；占位档先发中性值，接设置面归接线批。
        PostOutbound("theme.set", Envelope("theme.set", "h1", "\"theme\":\"light\""));
        PostOutbound("lang.set", Envelope("lang.set", "h2", "\"langCode\":\"zh-CN\""));
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
