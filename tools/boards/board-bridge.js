/* 画板桥的页面侧（Phase C 片19＝最小回路；片21＝存储适配器那一半，深析 §21.3 逐条落码）。
   名单的唯一主人＝Core 的 BridgeProtocol；本档每颗名字逐字照抄，闸门测试逐颗对账。
   六颗进（postIn 逐颗一处）＋五颗出（case 逐颗一处）——多写的名字闸门会红。
   ⚠ 那颗 AUTOSAVE_MS 与 Core 的 BoardPolicy.AutosaveIdleMs 是同一个数的两颗持有者（跨语言）：
   等值由 BoardSceneStoreGateTests 现读对账，改一颗不动另一颗＝那一格红。 */
(function () {
  "use strict";
  var L = window.StarMarkBoards;
  if (!L) { document.body.textContent = "boards bundle missing"; return; }
  var HOST = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  var API = null;
  var seq = 0;
  var AUTOSAVE_MS = 1500;
  var saveTimer = null;

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

  /* 那颗 ⛔ 只出现一次的 export.bytes 出口（宿主是唯一读者；档位不认识时宿主拒，⛔ 由本页猜扩展名）。 */
  function deliver(bytesB64, kind, sessionId) {
    postIn("export.bytes", {
      bytes: bytesB64,
      kind: kind,
      sessionId: String(sessionId == null ? "" : sessionId)
    });
  }

  /* §21.3 那颗「存」：serializeAsJSON(元素, 状态, 文件, "local") 交一颗 string。
     ⛔ 用 exportToBlob＋exportEmbedScene 顶替它——那交回的是 PNG（图里嵌一份场景），
     而宿主的板档后缀按 §21.3 是 .excalidraw：内容与名字两件事就对不上了。 */
  function sceneBytesB64() {
    var json = L.serializeAsJSON(API.getSceneElements(), API.getAppState(), API.getFiles(), "local");
    return b64FromBytes(new TextEncoder().encode(json));
  }

  function flushSave() {
    saveTimer = null;
    if (!API || !HOST) return;
    try {
      deliver(sceneBytesB64(), "scene", "idle");
    } catch (e) { /* 序列化失败＝这一拍不写；档留在上一份（⛔ 拿坏 JSON 覆盖上一份） */ }
  }
  /* 停笔那一拍：去抖住在页面侧（宿主看不到"什么时候算停笔"，而 §21.1 的名单里⛔ dirty 那颗）。 */
  function scheduleSave() {
    if (!HOST) return;
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(flushSave, AUTOSAVE_MS);
  }

  /* §21.3 那颗「载」天生是三步：字节 → loadFromBlob → updateScene(captureUpdate: NEVER) → addFiles。
     第三步⛔ 省：SceneData 逐字⛔ files 字段，图不补回去＝一张白板配一堆"缺图"的方框。 */
  function applyBoardLoad(bytesB64) {
    var blob = new Blob([bytesFromB64(bytesB64)], { type: "application/json" });
    L.loadFromBlob(blob).then(function (scene) {
      API.updateScene({
        elements: scene.elements || [],
        appState: scene.appState || {},
        captureUpdate: L.CaptureUpdateAction.NEVER
      });
      var files = scene.files ? Object.keys(scene.files).map(function (k) { return scene.files[k]; }) : [];
      if (files.length) API.addFiles(files);
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
      onChange: function (event) {
        /* 只有真的动了元素／图／档名才去抖一次；纯选区（appState）不算"改了这一笔"——
           否则每点一下画布都要重写一遍那颗档。 */
        if (event && (event.elements || event.files || event.name)) scheduleSave();
      },
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
    if (kind === "scene") {
      try {
        deliver(sceneBytesB64(), kind, replyId);
      } catch (err) {
        send(replyId, false, "export-failed");
      }
      return;
    }
    var isSvg = kind === "svg";
    var fn = isSvg ? L.exportToSvg : L.exportToBlob;
    var opts = {
      elements: API.getSceneElements(),
      appState: API.getAppState(),
      files: API.getFiles(),
      skipInliningFonts: !!skipInliningFonts
    };
    fn(opts).then(function (blob) {
      blob.arrayBuffer().then(function (buf) {
        deliver(b64FromBytes(new Uint8Array(buf)), kind, replyId);
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
