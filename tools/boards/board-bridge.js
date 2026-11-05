/* 画板桥的页面侧（Phase C 片19＝最小回路；片21＝存储适配器那一半，深析 §21.3 逐条落码）。
   名单的唯一主人＝Core 的 BridgeProtocol；本档每颗名字逐字照抄，闸门测试逐颗对账。
   名单在册十一颗，页面今天真喊九颗（postIn 逐颗一处＝bridge.ready／board.loaded／export.bytes／
   open.external／export.request／pin.request／scale.request／background.request／folder.request——
   后五颗＝片206 引擎主菜单那一组的索取，末颗＝片207 白条退役后那颗「打开所在文件夹」的出口）
   ＋send 那颗 bridge.reply；paste.query 名字过了门而回路还没接＝证人钉 0。
   八颗出（case 逐颗一处）——多写的名字闸门会红，少写的也会。
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
    HOST.postMessage({ v: 1, cmd: "bridge.reply", id: id, args: { ok: ok, err: err || null } });
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
    /* 片206＝深色用户开旧板仍见一屏浅色（账本侧查注定的根因）：loadFromBlob 那颗第二参是
       "缺键时拿哪一份 appState 补"的兜底，只递一颗时引擎按它自家那份恒 light 的默认补 ⇒
       主题那颗键每一趟载档都被兜底顶回浅色。递引擎此刻活着的那一份＝主题跟着现状走；
       档里自己的键（网格那族＋画布底色）照旧由场景档赢。 */
    var live = API && API.getAppState ? API.getAppState() : null;
    L.loadFromBlob(blob, live).then(function (scene) {
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
  /* 宿主放上那块板的一张图（深析 §四 那三格共用的唯一注入出口＝image.insert）。
     ⚠ 那颗 files 的<b>值里必须带 id</b>：引擎那句 addMissingFiles 逐条按 s.id 记账，而这里递的是"只取值"那一族，
     值里没 id 就记到 undefined 那颗键下，元素按 fileId 永远查不到＝屏幕上只剩一块灰色占位框，
     而且宿主那一趟 autosave 写回时那颗 file 因为"没人引用"被整颗跳掉（图连本体一起没了）。
     2026-10-30 两发真机读数把这两副脸分开量清了，正本 bin/WI99/p148_image_route_readings.txt，账本 P-274。 */
  var insertSeq = 0;
  var INDEX_DIGITS = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

  /* 引擎那套位序是"同前缀比尾数"的字符串序，所以往<b>最大那颗</b>后面接一颗有两条路：
     尾数没到顶＝把最后一颗进一；尾数已到顶＝添一颗最小的（前缀关系本身就严格更大）。
     认不出来的字符⛔ 猜——交回空串，调用方那一发就不做（做了＝一颗排不进队的元素）。 */
  function nextIndexAfter(highest) {
    if (!highest) return "a0";
    var pos = INDEX_DIGITS.indexOf(highest.charAt(highest.length - 1));
    if (pos < 0) return "";
    if (pos < INDEX_DIGITS.length - 1)
      return highest.slice(0, highest.length - 1) + INDEX_DIGITS.charAt(pos + 1);
    return highest + INDEX_DIGITS.charAt(0);
  }

  function applyImageInsert(a) {
    if (!API || !a) return;
    var mime = String(a.mimeType || "");
    var data = String(a.dataUrl || "");
    var w = Math.round(Number(a.width));
    var h = Math.round(Number(a.height));
    if (!data || !(w > 0) || !(h > 0)) return;      // 形状不对＝这一发⛔ 做（宿主那侧已经按名单拒过一遍了）
    var els = API.getSceneElements();
    var highest = "";
    for (var i = 0; i < els.length; i++) if (els[i].index > highest) highest = els[i].index;
    var index = nextIndexAfter(highest);
    if (!index) return;
    var now = Date.now();
    insertSeq += 1;
    var fid = "host-" + now.toString(36) + "-" + insertSeq.toString(36);
    API.addFiles([{ id: fid, mimeType: mime, dataURL: data, created: now, lastAccessed: now }]);
    var st = API.getAppState();
    var zoom = (st.zoom && st.zoom.value) || 1;
    // 摆在这一眼的正中：屏幕坐标＝(场景坐标＋scrollX)×zoom，反解就是下面那两行。
    var x = window.innerWidth / 2 / zoom - (st.scrollX || 0) - w / 2;
    var y = window.innerHeight / 2 / zoom - (st.scrollY || 0) - h / 2;
    API.updateScene({
      elements: els.concat([{
        id: fid + "-el", type: "image", x: x, y: y, width: w, height: h, angle: 0,
        strokeColor: "transparent", backgroundColor: "transparent", fillStyle: "solid",
        strokeWidth: 2, strokeStyle: "solid", roughness: 0, opacity: 100,
        groupIds: [], frameId: null, index: index, roundness: null,
        seed: Math.floor(Math.random() * 2147483647), version: 1,
        versionNonce: Math.floor(Math.random() * 2147483647), isDeleted: false,
        boundElements: null, updated: now, link: null, locked: false,
        status: "saved", fileId: fid, scale: [1, 1], crop: null
      }]),
      // 这一发是<b>用户点出来的一次编辑</b>，要进撤销栈（⛔ ALWAYS 换成 NEVER：那张图撤不掉）。
      captureUpdate: L.CaptureUpdateAction.ALWAYS
    });
    scheduleSave();
  }

  /* 那三颗外观 prop（theme／langCode／UIOptions）各自记住最后一次递进来的值（片205＝深浅色那副真机脸的真因）：
     从前每一发都只带自己那一颗键重挂一遍，而重挂那一挂的 props 每次都从 baseProps() 重新拼——
     后到的那发把先到那发刚挂上去的兄弟键抹掉了（宿主 bridge.ready 那一拍先发 theme.set 再发 lang.set，
     末了那一挂上只剩 langCode 一颗：深色用户的画布恒浅色；中文用户的语言码碰巧同默认值，连「没跟」都看不出来）。
     记住的这一份每次整份并进去：谁后到都抹不掉谁先到。 */
  var stickyProps = {};

  function applyProp(extra) {
    if (!API) return;
    Object.keys(extra).forEach(function (k) { stickyProps[k] = extra[k]; });
    rerenderWith(stickyProps);
  }

  /* ───────── 引擎主菜单里那一组（片206＝P-285 重做・片207＝P-285 残余） ─────────
     从前右上角那一排动作按钮整条压在引擎自己的工具条上（片204 真机量到：遮住、关不掉、挪不开），
     收进「⋯」只是把遮挡折起来，工具条上仍多一块宿主表面。片206 把五个动作搬进引擎那颗主菜单；
     片207 把右上角那一行白条整块退役——那颗「打开所在文件夹」出口与两行读数（那句落点／
     「未显示的外部内容」）也搬进同一组：出口是第六颗动作项，两行读数做成只读行（div 而⛔ button，
     免掉悬停那张脸）。深浅色由引擎自己的模板给，宿主侧那一排钉死三色的配方整排退役。
     机制＝MutationObserver 钉 document.body（引擎那颗菜单按需挂进应用 DOM 而⛔ 走 body portal），
     挂进来那一刻往里注入一组。幂等＝那颗标记属性查重；注入项是我们自己的节点，React 的 onSelect
     听不见 ⇒ 点进去由本页 postIn 对应那一颗索取，再替用户把扳手钮合上（外来节点关不掉宿主菜单）。 */
  var menuState = {
    png: "", svg: "", pin: "", scale: "", background: "", folder: "",
    busy: false, folderEnabled: false, status: "", hidden: "",
  };
  var MENU_GROUP_MARK = "data-starmark-menu";
  var MENU_ITEM_ORDER = ["png", "pin", "svg", "scale", "background", "folder"];
  // 两行读数（片207）：⛔ 是动作——一颗都点不进去，只在有话要说时在场（空话＝整行收起）。
  var MENU_INFO_ORDER = ["status", "hidden"];

  function closeHostMenu() {
    var trigger = document.querySelector('[data-testid="main-menu-trigger"]');
    if (trigger) trigger.click();
  }

  function menuAction(key) {
    if (key === "png" || key === "svg") postIn("export.request", { kind: key });
    else if (key === "pin") postIn("pin.request", {});
    else if (key === "scale") postIn("scale.request", {});
    else if (key === "background") postIn("background.request", {});
    else if (key === "folder") postIn("folder.request", {});
  }

  // 引擎自己的菜单项长的是两颗类＋三段内芯（__icon／__text／__shortcut）——只挂 dropdown-menu-item
  // 而缺 -base 那一颗＝字色／字号／弹性全丢（真机量到的那副黑字小号的脸）。宿主注入项照同一副长法：
  // 双类都挂，内芯给 __text 那一段。
  function buildMenuItem(key) {
    var item = document.createElement("button");
    item.type = "button";
    item.className = "dropdown-menu-item dropdown-menu-item-base";
    item.setAttribute("data-starmark-item", key);
    var text = document.createElement("div");
    text.className = "dropdown-menu-item__text";
    text.textContent = menuState[key];
    item.appendChild(text);
    // busy 那一闩只压三颗动作（两颗导出＋贴桌面：连按＝两发抢同一刻的落点）；
    // 两颗旋钮照旧点得动——挑档是宿主自己的事，⛔ 跟着"这一发正在跑"一起关。
    // 那颗出口只在"上一次真写成的那一份"不在盘上时点不亮（folderEnabled 由宿主递）。
    item.disabled = (menuState.busy && (key === "png" || key === "svg" || key === "pin"))
      || (key === "folder" && !menuState.folderEnabled);
    item.addEventListener("click", function () {
      menuAction(key);
      closeHostMenu();
    });
    return item;
  }

  function buildInfoRow(key) {
    var row = document.createElement("div");
    row.className = "dropdown-menu-item-base";
    row.setAttribute("data-starmark-info", key);
    var text = document.createElement("div");
    text.className = "dropdown-menu-item__text";
    text.textContent = menuState[key];
    row.appendChild(text);
    if (!menuState[key]) row.style.display = "none";   // 空话＝整行收起，⛔ 念一颗 0 等于替用户造一颗他没问过的消息
    return row;
  }

  function injectMenuGroup() {
    var menu = document.querySelector('[data-testid="dropdown-menu"]');
    if (!menu) return;
    var host = menu.querySelector(".dropdown-menu-container") || menu;
    if (host.querySelector("[" + MENU_GROUP_MARK + "]")) return;    // 幂等：这一组已经在了
    var group = document.createElement("div");
    group.className = "dropdown-menu-group";
    group.setAttribute(MENU_GROUP_MARK, "1");
    var title = document.createElement("div");
    title.className = "dropdown-menu-group-title";
    title.textContent = "StarMark";
    group.appendChild(title);
    for (var i = 0; i < MENU_ITEM_ORDER.length; i++) group.appendChild(buildMenuItem(MENU_ITEM_ORDER[i]));
    for (var j = 0; j < MENU_INFO_ORDER.length; j++) group.appendChild(buildInfoRow(MENU_INFO_ORDER[j]));
    host.appendChild(group);
  }

  function refreshMenuGroup() {
    var menu = document.querySelector('[data-testid="dropdown-menu"]');
    if (!menu) return;
    var items = menu.querySelectorAll("[data-starmark-item]");
    for (var i = 0; i < items.length; i++) {
      var key = items[i].getAttribute("data-starmark-item");
      var text = items[i].querySelector(".dropdown-menu-item__text");
      if (text) text.textContent = menuState[key];
      items[i].disabled = (menuState.busy && (key === "png" || key === "svg" || key === "pin"))
        || (key === "folder" && !menuState.folderEnabled);
    }
    var infos = menu.querySelectorAll("[data-starmark-info]");
    for (var j = 0; j < infos.length; j++) {
      var infoKey = infos[j].getAttribute("data-starmark-info");
      var infoText = infos[j].querySelector(".dropdown-menu-item__text");
      if (infoText) infoText.textContent = menuState[infoKey];
      infos[j].style.display = menuState[infoKey] ? "" : "none";
    }
  }

  function applyMenuSet(a) {
    if (!a) return;
    menuState.png = String(a.png || "");
    menuState.svg = String(a.svg || "");
    menuState.pin = String(a.pin || "");
    menuState.scale = String(a.scale || "");
    menuState.background = String(a.background || "");
    menuState.folder = String(a.folder || "");
    menuState.busy = !!a.busy;
    menuState.folderEnabled = !!a.folderEnabled;
    menuState.status = String(a.status || "");
    menuState.hidden = String(a.hidden || "");
    refreshMenuGroup();      // 菜单正开着就地在位刷新；关着＝状态存好，下一开注入时用
  }

  function onOut(e) {
    var m = e.data;
    if (!m || m.v !== 1) return;
    switch (m.cmd) {
      case "board.load":
        applyBoardLoad(m.args && m.args.bytes);
        break;
      case "theme.set":
        applyProp({ theme: m.args && m.args.theme });
        break;
      case "lang.set":
        applyProp({ langCode: m.args && m.args.langCode });
        break;
      case "feature.flags":
        applyProp({ UIOptions: m.args && m.args.uiOptions });
        break;
      case "export.pick":
        runExport(m.args && m.args.kind, m.args && m.args.skipInliningFonts, m.id,
                  m.args && m.args.exportScale, m.args && m.args.exportBackground);
        break;
      case "autosave.arm":
        hostArmed = true;
        break;
      case "image.insert":
        applyImageInsert(m.args);
        break;
      case "menu.set":
        applyMenuSet(m.args);
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
          // 导出落点由宿主接管；export 关闭 JSON 框，saveAsImage 关闭图片框，两者不能互相替代。
          export: false,
          saveAsImage: false,
          clearCanvas: false,
          changeViewBackgroundColor: false,
          toggleTheme: false
        },
        tools: { image: false }
      },
      // 仅拒绝 URL 型 embeddable；生成 HTML 的 iframe 不受此 prop 控制，出站限制仍由 CSP 与宿主承担。
      validateEmbeddable: false,
      /* 账本 P-249＝§28.5 那句「<c>open.external</c> 反过来：宿主有 case 而页面一颗也不发」的落地腿。
         现读随包 bundle：那颗链接只留<b>一处</b> <c>window.open(</c>，逐字形状＝
         <c>this.props.onLinkOpen&&(D=ip("excalidraw-link",E.nativeEvent),this.props.onLinkOpen({...this.hitLinkElement,link:d},D)),
         !D?.defaultPrevented&&{…window.open(void 0,h)}</c>；而那颗气泡（选中一颗带链接的元素就弹出来那颗）里的
         <c>&lt;a&gt;</c> 问的是同一样东西（<c>h.defaultPrevented&&D.preventDefault()</c>）。
         ⇒ 交回 undefined 时<b>两处都走引擎默认</b>，而默认那两支我们都付不起：
         ① <c>_blank</c> 那一支要一颗新窗，本窗⛔ 订 <c>NewWindowRequested</c>（现读 0 处），
            那一手交回什么我们⛔ 读过＝⛔ 能替它下结论（名册 584：常数有主人⛔ 等于那条路走得到，反过来也一样）；
         ② <c>kF</c> 认「含本站 origin 或以 / 开头」，那一支交的是 <c>_self</c>＝<b>拿 about:blank 换掉这一页</b>，
            画布连同没存的那笔一起没了（片26 那颗根的量级）。
         那颗递给 onLinkOpen 的东西是<b>一颗没上过树的 CustomEvent</b>（工厂逐字 <c>cancelable:!0</c>），
         所以 <c>preventDefault()</c> 只翻 <c>defaultPrevented</c> 那一维＝引擎正好问那一维。
         拦下来之后⛔ 由本页猜起哪个程序：判据与打开都归宿主（硬线③＝<c>BridgeProtocol.IsSafeExternalUrl</c>
         只认 http(s)、要有机名、⛔ 带 userinfo，再由 <c>LauncherEx</c> 收绝对 URI 形态那一颗）。
         ⚠ 这颗与上面那颗同族＝<b>顶层</b> prop（名册 622：名字在档里⛔ 等于在引擎读的那一层）。 */
      onLinkOpen: function (element, event) {
        var url = element && element.link;
        if (!url) return;                    // 那颗元素本来⛔ 带链接＝这一支与本页无关，别替它拦
        event.preventDefault();
        postIn("open.external", { url: String(url) });
      }
    };
  }

  /* 那一发交回<b>什么形状</b>，由档位分岔（片56＝P-244 的第一处）。
     ⚠ 现读那颗类型（随包的 dist/types/utils/export.d.ts 逐字）＝`exportToSvg(...) => Promise<SVGSVGElement>`——
     它交回的是<b>一颗 DOM 节点</b>，而⛔ 是一颗 Blob，所以⛔ 能跟 png 那一路共用 `blob.arrayBuffer()`：
     那是向一颗节点从没有的方法要东西，抛出来的错既进⛔ deliver 也进⛔ send＝屏幕上从此再没这一发的下落。
     换成字节这一步<b>逐字照抄引擎自己那一路</b>（现读随包 bundle 的导出分支：svg 那一支拿的是
     `new Blob([那颗节点的 outerHTML], {type: MIME_TYPES.svg})`，而 MIME_TYPES.svg＝"image/svg+xml"）：
     取 outerHTML、按那颗 MIME 包一颗 Blob，剩下的字节转换仍走 png 那一趟<b>同一个</b>出口（⛔ 两份编码）。
     ⚠ 链上每一环都得<b>把 promise 交回去</b>：从前那颗 arrayBuffer 写在 then 里而没 return，
     它一旦抛就永远走不到 .catch——而 .catch 那句 send 正是宿主唯一的"这一发办不成"的回音。
     那颗 send 现只在<b>办不成</b>时出声（成功那一路交的是 export.bytes），宿主那头的对应主人见 BoardWindow。 */
  function runExport(kind, skipInliningFonts, replyId, scale, background) {
    // 那两档（缩放／背景）由宿主右上角那一行给，名单的主人是那颗下拉（1／2／3）。这里⛔ 认就沿引擎那颗默认，
    // 并且<b>只并进这一发的 opts</b>：⛔ 写回引擎活着的那份 appState（那会改用户屏上引擎自己的导出面板，
    // 也会把"这一发用了什么"混进面板状态里）。引擎自家那颗导出对话框合并的正是同一手。
    var prefs = {};
    if (scale === 1 || scale === 2 || scale === 3) prefs.exportScale = scale;
    if (background === true || background === false) prefs.exportBackground = background;
    try {
      if (kind === "scene") {
        deliver(sceneBytesB64(), kind, boardName());
        return;
      }
      var opts = {
        elements: API.getSceneElements(),
        appState: Object.assign({}, API.getAppState(), prefs),
        files: API.getFiles(),
        skipInliningFonts: !!skipInliningFonts,
        // ⚠ PNG 那一路的缩放<b>只从这颗来得</b>（现读随包 bundle 里那颗 exportToCanvas 的形参表：它⛔ 收
        // <c>exportScale</c>，只有连 <c>maxWidthOrHeight</c> 一起给的那一支才回头乘 appState 里那颗；
        // 没给时尺寸取 <c>getDimensions()</c> 的返回值、系数取其中那颗 <c>scale ?? 1</c>）。
        // 真机两处同向：三档产物逐颗同宽（bin/WI98/cap64_console_r2.txt），而页面<b>确实收到了档位</b>
        // （档名念回 rx3，bin/WI98/cap74_console_r2.txt）；在 opts 顶层再放一颗 exportScale 也⛔ 响（同一份臂 C）。
        // 那颗 SVG 走的是另一颗函数，它读 appState 里那一颗，所以这里⛔ 动它（<c>undefined</c>＝沿引擎默认那颗尺寸）。
        getDimensions: prefs.exportScale ? function (w, h) {
          return { width: w * prefs.exportScale, height: h * prefs.exportScale, scale: prefs.exportScale };
        } : undefined
      };
      var toBlob = kind === "svg"
        ? L.exportToSvg(opts).then(function (node) { return new Blob([node.outerHTML], { type: "image/svg+xml" }); })
        : L.exportToBlob(opts);
      toBlob.then(function (blob) { return blob.arrayBuffer(); })
        .then(function (buf) { deliver(b64FromBytes(new Uint8Array(buf)), kind, boardName()); })
        .catch(function () { send(replyId, false, "export-failed"); });
    } catch (e) {
      send(replyId, false, "export-failed");
    }
  }

  if (HOST) {
    window.chrome.webview.addEventListener("message", onOut);
  }

  // 引擎那颗主菜单按需挂进应用 DOM：挂进来那一拍注入那一组（菜单每一次重挂都会重新注入＝
  // 幂等查重挡住重复；组被引擎拆掉也会随下一挂回来）。
  new MutationObserver(function (muts) {
    for (var i = 0; i < muts.length; i++) {
      var added = muts[i].addedNodes;
      for (var j = 0; j < added.length; j++) {
        var node = added[j];
        if (node.nodeType !== 1) continue;
        if ((node.getAttribute && node.getAttribute("data-testid") === "dropdown-menu")
            || (node.querySelector && node.querySelector('[data-testid="dropdown-menu"]'))) {
          injectMenuGroup();
          return;
        }
      }
    }
  }).observe(document.body, { childList: true, subtree: true });

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
