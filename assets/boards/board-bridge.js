/* 画板桥的页面侧（Phase C 片19·§21.1 v1 的最小回路）。
   名单的唯一主人＝Core 的 BridgeProtocol；本档每颗名字逐字照抄，闸门测试逐颗对账。
   六颗进（postIn 逐颗一处）＋五颗出（case 逐颗一处）——多写的名字闸门会红。 */
(function () {
  "use strict";
  var L = window.StarMarkBoards;
  if (!L) { document.body.textContent = "boards bundle missing"; return; }
  var HOST = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  var API = null;
  var seq = 0;

  function postIn(cmd, args) {
    if (!HOST) return null;
    seq += 1;
    var id = "b" + seq;
    HOST.postMessage({ v: 1, cmd: cmd, id: id, args: args || {} });
    return id;
  }
  function send(id, ok, err) {
    if (!HOST) return;
    HOST.postMessage({ v: 1, cmd: "bridge.reply", id: id, ok: ok, err: err || null });
  }

  function b64FromBytes(bytes) {
    var bin = "";
    var n = bytes.length, chunk = 0x8000;
    for (var i = 0; i < n; i += chunk) {
      bin += String.fromCharCode.apply(null, bytes.subarray(i, Math.min(i + chunk, n)));
    }
    return btoa(bin);
  }
  function bytesFromB64(b64) {
    var bin = atob(b64), out = new Uint8Array(bin.length);
    for (var i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
  }

  function applyBoardLoad(bytesB64) {
    var blob = new Blob([bytesFromB64(bytesB64)], { type: "application/json" });
    L.loadFromBlob(blob).then(function (scene) {
      API.updateScene({
        elements: scene.elements || [],
        appState: scene.appState || {},
        captureUpdate: L.CaptureUpdateAction.NEVER
      });
    }).catch(function () { /* 载入失败＝档留在宿主，重开窗再来（§坑10 那条由宿主侧管） */ });
  }
  function applyProp(rerender) {
    if (!API) return;
    rerender();
  }

  function onOut(e) {
    var m = e.data;
    if (!m || m.v !== 1) return;
    switch (m.cmd) {
      case "board.load":
        applyBoardLoad(m.args && m.args.bytes);
        break;
      case "theme.set":
        applyProp(function () { rerenderWith({ theme: m.args && m.args.theme }); });
        break;
      case "lang.set":
        applyProp(function () { rerenderWith({ langCode: m.args && m.args.langCode }); });
        break;
      case "feature.flags":
        applyProp(function () { rerenderWith({ UIOptions: m.args && m.args.uiOptions }); });
        break;
      case "export.pick":
        runExport(m.args && m.args.kind, m.args && m.args.skipInliningFonts, m.id);
        break;
      default:
        break;
    }
  }

  function rerenderWith(extra) {
    var box = document.getElementById("root");
    var props = baseProps();
    Object.keys(extra || {}).forEach(function (k) { props[k] = extra[k]; });
    L.createRoot(box).render(L.React.createElement(L.Excalidraw, props));
  }

  function baseProps() {
    return {
      excalidrawAPI: function (api) { API = api; },
      UIOptions: {
        canvasActions: {
          loadScene: false,
          saveToActiveFile: false,
          export: { saveFileToDisk: false },
          clearCanvas: false,
          changeViewBackgroundColor: false,
          toggleTheme: false
        },
        tools: { image: false }
      }
    };
  }

  function runExport(kind, skipInliningFonts, replyId) {
    var isSvg = kind === "svg";
    var fn = isSvg ? L.exportToSvg : L.exportToBlob;
    var opts = {
      elements: API.getSceneElements(),
      appState: API.getAppState(),
      files: API.getFiles(),
      exportEmbedScene: kind === "scene",
      skipInliningFonts: !!skipInliningFonts
    };
    fn(opts).then(function (blob) {
      blob.arrayBuffer().then(function (buf) {
        postIn("export.bytes", {
          bytes: b64FromBytes(new Uint8Array(buf)),
          kind: kind,
          sessionId: String(replyId || "")
        });
      });
    }).catch(function () { send(replyId, false, "export-failed"); });
  }

  if (HOST) {
    window.chrome.webview.addEventListener("message", onOut);
  }

  L.createRoot(document.getElementById("root")).render(L.React.createElement(L.Excalidraw, baseProps()));
  /* 引擎就绪后再喊宿主（bridge.ready ⛔ 参；宿主回 theme/lang/feature 三颗）。 */
  if (HOST) {
    var tries = 0;
    var iv = setInterval(function () {
      tries += 1;
      if (API || tries > 50) { clearInterval(iv); postIn("bridge.ready"); }
    }, 100);
  }
})();
