/* 画板桥的页面侧（Phase C 片19＝最小回路；片21＝存储适配器那一半，深析 §21.3 逐条落码）。
   名单的唯一主人＝Core 的 BridgeProtocol；本档每颗名字逐字照抄，闸门测试逐颗对账。
   七颗进（postIn 逐颗一处）＋六颗出（case 逐颗一处）——多写的名字闸门会红。
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
  /* 停笔那一拍的闸门（P-221 的修法）。⚠ 这里原本是一颗"看事件对象里有没有 elements"的判据，
     而引擎交的是位置参数（现读 bundle：`this.props.onChange?.(C,this.state,this.files)`）——
     那颗判据恒假，于是停笔拍在真机上一根也没落过。现在⛔ 由页面自己判"这一笔算不算改"：
     改没改由宿主那颗字节对差（DiskAlreadyHolds）说了算。
     要挡的只有一样：载档那一拍（含挂载那一响的空场景）——片25 的 R7 与片26 的重载那一跑都量到过
     "画布里还是空的，盘上那块板已经被盖成 241 字节／0 元素"。开闸那句话由宿主在收到 board.loaded
     （带两颗数）并<b>对完数</b>之后递过来；新板没档可载，宿主当场就递。
     ⚠ 这颗闸门只包得住停笔这一路（失焦／关窗那两拍是宿主问、页面答，绕不过它），
     所以宿主自己那份写闸（BoardWindow._autosaveArmed）才是主闸，这里这颗＝第二道。 */
  var hostArmed = false;

  /* 那棵 React 根只许生一次（片26＝P-222 的真因，也是 P-223 那颗数据丢失的同一颗根）。
     原来 theme.set／lang.set／feature.flags 每一颗都"造一棵新根再挂一次"——同一个 DOM 容器上再挂一棵根，
     React 18 只让最后一棵赢下这块 DOM，而<b>每挂一次＝一块全新的空画板</b>：宿主刚递进来的那块板被拆掉。
     现场的脸（06:22 那一跑）＝board.loaded 那一刻画布上确实有那颗可写元素（canvas=1 是真数），
     1.5 s 后交回盘上的却是 0 元素／241 字节——中间隔的就是这一手拆树。
     ⚠ 这颗格（ThePageMountsExactlyOneReactRoot）按字面数造根那句，所以这段注释⛔ 能把它照抄一遍。*/
  var root = null;

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

  /* 那颗 ⛔ 只出现一次的 export.bytes 出口（宿主是唯一读者；档位不认识时宿主拒，⛔ 由本页猜扩展名）。
     suggested＝那块板的显示名（`appState.name`）：它只是"用户想叫它什么"，⛔ 是身份（身份＝宿主给的那颗 id），
     宿主那边只有导出那一路会用它（板档的档名⛔ 跟着显示名走，见 §21.3）。
     ⚠ 片23 摘掉了那颗 sessionId：三拍都递、名单在册、宿主解出来却一颗读者也没有——多一颗没人读的键
     就是越权面上多一颗（硬线②守的正是这类东西），而"形参活着、读者永远撞不到"＝片21 欠项⑤ 那一族。
     哪一拍该写宿主自己知道（export.pick 那一问本就是它发的），⛔ 由页面再提醒一遍。 */
  function deliver(bytesB64, kind, suggested) {
    postIn("export.bytes", {
      bytes: bytesB64,
      kind: kind,
      suggested: String(suggested == null ? "" : suggested)
    });
  }

  /* §21.3 那颗「存」：serializeAsJSON(元素, 状态, 文件, "local") 交一颗 string。
     ⛔ 用 exportToBlob＋exportEmbedScene 顶替它——那交回的是 PNG（图里嵌一份场景），
     而宿主的板档后缀按 §21.3 是 .excalidraw：内容与名字两件事就对不上了。 */
  function sceneBytesB64() {
    var json = L.serializeAsJSON(API.getSceneElements(), API.getAppState(), API.getFiles(), "local");
    return b64FromBytes(new TextEncoder().encode(json));
  }

  /* 显示名读 appState.name（§21.3 逐字：那颗字段是 string | null，只当显示名、⛔ 当身份）。
     ⚠ 引擎那颗只读出口在这里用不得——现读 bundle：`"getName",()=>this.state.name||this.props.name||
     未命名-<时刻>`，它永远⛔ 回空。递上去＝宿主那颗"id＋时刻"的兜底名在链路上永远走不到，
     正是片21 欠项⑤ 那一族（形参活着、读者永远撞不到）。 */
  function boardName() {
    try {
      var s = API && API.getAppState ? API.getAppState() : null;
      return s && s.name ? String(s.name) : "";
    } catch (e) { return ""; }
  }

  function flushSave() {
    saveTimer = null;
    if (!API || !HOST) return;
    try {
      deliver(sceneBytesB64(), "scene", boardName());
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
      var els = scene.elements || [];
      var writable = 0;
      for (var i = 0; i < els.length; i++) { if (!els[i].isDeleted) writable += 1; }
      API.updateScene({
        elements: els,
        appState: scene.appState || {},
        captureUpdate: L.CaptureUpdateAction.NEVER
      });
      var files = scene.files ? Object.keys(scene.files).map(function (k) { return scene.files[k]; }) : [];
      if (files.length) API.addFiles(files);
      /* 开闸那句话⛔ 许是空的（片26 的修法）：宿主拿到三颗数——它递进去几颗、这颗档里几颗（writable）、
         画布上当场几颗（onCanvas），三颗全等才开闸、才许落盘。
         ⚠ 三颗必得是同一样东西＝<b>会被写回盘上的那几颗</b>：画布那颗取 getSceneElements（现读 bundle＝
         `scene.getNonDeletedElements()`），正是下面 sceneBytesB64() 问的那一颗出口；档里那颗也跳掉 isDeleted。
         片26 第一版⛔ 这么办：画布取的是那颗"连删掉的也一起数"的出口、档按数组整颗长度数，崩后重载那一跑
         三颗"全等"（4/4/4）而盘上那块板照样被盖成 0 元素——比的是两颗⛔ 同口径的数，闸门挡了个⛔ 存在的形状。
         数不出来给 -1：那颗认不出的数在宿主的判据里＝⛔ 开闸（读不懂时唯一安全的动作是不写）。 */
      var onCanvas = -1;
      try { onCanvas = API.getSceneElements().length; } catch (e) { onCanvas = -1; }
      postIn("board.loaded", { elements: writable, canvas: onCanvas });
    }).catch(function () { /* 载入失败＝档留在宿主，重开窗再来（§坑10 那条由宿主侧管）；⛔ 开闸 */ });
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
      case "autosave.arm":
        hostArmed = true;
        break;
      default:
        break;
    }
  }

  function rerenderWith(extra) {
    var box = document.getElementById("root");
    var props = baseProps();
    Object.keys(extra || {}).forEach(function (k) { props[k] = extra[k]; });
    // 同一棵根⛔ 许换第二棵：见上面那颗 root 的注释（换＝把宿主刚递进来的那块板整棵拆掉）
    if (!root) root = L.createRoot(box);
    root.render(L.React.createElement(L.Excalidraw, props));
  }

  function baseProps() {
    return {
      excalidrawAPI: function (api) { API = api; },
      onChange: function () {
        /* 引擎交回的是位置参数（现读 bundle：`this.props.onChange?.(C,this.state,this.files)`），
           而这里以前按"事件对象"去读它的三颗键⇒ 那颗判据恒假、停笔拍一根也没落过（P-221）。
           现在⛔ 由页面猜"哪一笔算改"：改没改由宿主的字节对差说了算。⚠ 选区那类键根本进不了档
           （现读那颗 export 白名单只有 4 颗：gridModeEnabled／gridSize／gridStep／viewBackgroundColor），
           所以"每点一下画布都要重写一遍"那颗担心在字节上不成立。
           要挡的只有一样：挂载／载档那一拍的回调——那时用户还没动手，写下去就是拿空板盖掉盘上那块
           （片25 的 R7 现场量到过：4 元素→0 元素／241 B）。 */
        if (!hostArmed) return;
        scheduleSave();
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
        deliver(sceneBytesB64(), kind, boardName());
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
        deliver(b64FromBytes(new Uint8Array(buf)), kind, boardName());
      });
    }).catch(function () { send(replyId, false, "export-failed"); });
  }

  if (HOST) {
    window.chrome.webview.addEventListener("message", onOut);
  }

  // 第一次挂＝这颗根的出生；此后 theme／lang／feature 那三颗都在<b>同一棵</b>根上重挂（rerenderWith）。
  rerenderWith({});
  /* 引擎就绪后再喊宿主（bridge.ready ⛔ 参；宿主回 theme/lang/feature 三颗）。 */
  if (HOST) {
    var tries = 0;
    var iv = setInterval(function () {
      tries += 1;
      if (API || tries > 50) { clearInterval(iv); postIn("bridge.ready"); }
    }, 100);
  }
})();
