# excalidraw 整合 · 功能全量可行性深析

> 日期：2026-09-29｜接续：《excalidraw 嵌入可行性分析（对话版）》的路线结论（嵌入 WebView2、不复刻）。
> **本文性质**：功能点级全量盘点 + 逐点可行性 + 离线破坏面 + 避坑清单。用户要求"切记不可乐观"——
> 所有数字实测自本地仓库（excalidraw monorepo @ 0.18.0），所有坑标证据出处，**没有一条"应该没问题"**。
> 状态：**决议前分析**，未动任何代码。三件必须人拍板的事列在 §九，未拍板前任何批不许开工。

---

## 〇、一页结论

1. **"一比一"的正确定义**：`@excalidraw/excalidraw@0.18.0` **编辑器组件功能 1:1**（44 个注册 action + 全部工具 + 场景引擎，实测清单在 §二）；**不是** excalidraw.com 产品 1:1——官方壳（实时协作/云库/AI 生成/PWA 登录）**全部是服务器功能，桌面版按 §三 D 表逐项排除**。这个措辞治理写进后续每份验收，防止被翻案成"说好的 1:1 呢"。
2. **技术栈问题在嵌入路线下消解**：React/canvas 引擎原样跑在 WebView2 里；原生↔web 之间**只交换文件与图像，零像素流量**——这条界线是历史裁决（待决策:2057 否决 Fabric/Konva）的兼容条件，破了它，当年否决的理由全部复活。
3. **本项目实测无 WebView2**（csproj 零引用，`IViewer.HtmlContent→WebView` 是未点火的抽象预留）——这是**新增运行时**，不是复用既有件；对应审计全部前置在 §五/§九。
4. 最大工程风险按杀伤力排序：**①下载/导出拦截缺失**（导出=点了没反应，§坑13）、**②字体/子集化远程回退**（离线观感塌，§坑4/5）、**③WebView2 进程树驻留**（顶铁律①，§坑8）、**④构建链 Node 引入**（桌面项目第一例外，§九决策1）、**⑤全局键位冲突**（画布批已演示过一次，§坑9）。
5. 总体判定：**可行，但它是"新子系统"不是"接一根线"**——编辑器引擎白拿，宿主生命周期、存储接管、离线完整面、桥安全四件事全要自己做且各有硬坑。

---

## 一、证据面（本轮实测，非记忆）

| 事实 | 数值 | 出处 |
|---|---|---|
| monorepo 规模 | 618 文件 / 205,529 行 ts/tsx | 本地普查 |
| editor 包本体 | 466 文件 / 135,918 行 | packages/excalidraw |
| element 包 | 84 文件 / 59,433 行 | packages/element |
| 注册 action 数 | 44（§二 A 表全列） | packages/excalidraw/actions/ |
| 随包字体 | 11 族 / 合计 ~12.97MB，**Xiaolai(中文手写) 单族 12.4MB** | fonts/ 目录实测 |
| editor 内网络出口 | 16 处命中（help 外链×4 / PublishLibrary fetch / TTD fetch / blob 图 fetch / library 导入 fetch / **字体 esm.sh 回退** / **woff2 子集 wasm fetch** / docs 链） | 普查 grep |
| app 壳服务器件 | firebase.ts(collab) / sentry.ts / AI.tsx / ExcalidrawPlusPromoBanner / share/ / TTDStorage | excalidraw-app |
| 许可 | 代码 MIT（根 LICENSE 实证）；字体授权逐族核验为**待办**（§坑6） | LICENSE |
| 宿主先例 | examples/with-script-in-browser（官方纯 HTML 嵌入示例） | examples/ |
| 桌面 WebView2 | **零引用**（csproj 全扫描）；HtmlContent→WebView 仅注释预留 | StarMarkDesktop |

---

## 二、功能全量清单与可行性

### A 表 · 编辑功能（44 actions + 工具族）——嵌入路线下**引擎自带，全部可行**

工具栏：选择/手绘笔/直线/箭头/双线/虚线/矩形/菱形/椭圆/图片/便签(sticky)/框架(frame)/**mermaid 转换(本地计算部分)**/橡皮/矩形 erase/文字/激光笔/收藏(手型)/放大镜(zoom-tool)。
action 注册表实测 44 个（前两轮普查逐条在列），按族归并：

| 族 | 代表 action | 桌面可行性 | 备注 |
|---|---|---|---|
| 剪贴板 | copy/cut/paste/paste-as-newer/prose | ✔ 引擎自带 | 但 web clipboard 图片粘贴需宿主授权（坑12） |
| 历史 | undo/redo | ✔ | 与全局键位冲突走桥策略（坑9） |
| 变换 | flip/group/align/distribute/zindex/lock/finalize/resize | ✔ | 无服务器依赖 |
| 文本 | bound-text/autoresize/link | ✔ | IME 由 WebView 原生管线（比自研遮罩的 RD-1 战场省心——白赚） |
| 样式 | styles copy/change*（stroke/fill/width/opacity/roundness/…） | ✔ | |
| 视图 | zoom/grid/snap(对象+中点)/zen/view/search-menu/stats/show-hints | ✔ | search-menu=属性搜索，纯本地 |
| 框架 | frame 全家（selectAllInFrame 等） | ✔ | 0.18 frame 成熟 |
| 图片 | 图片插入/裁剪(cropEditor)/减重(image-blob-reduce+pica) | ✔/⚠ | **pica 的 wasm 随包核实**（§坑5 同类：不随包=大图优化静默失效） |
| 导出 | export png/svg/后端导出 | ✔**本地渲染路径**（renderer.js 在 element 包） | ⚠ 大坑在"导出产物怎么递出 WebView"——见坑13，**这是嵌入方案里最容易做成死按钮的一件** |
| 库 | addToLibrary(本地)/publishLibrary(**服务器**) | 本地 ✔ 发布 ✘ | 发布按钮必须摘（D表） |
| 线性编辑 | linearEditor(节点拖拽) | ✔ | |
| 其它 | changeProjectName(文件内改名)/changeViewBackground/emitter/convertToBinary(mermaid 双向) | ✔ | |

> A 表没有自研工作量，但有**宿主验证工作量**：44 项每项走一遍"在这个嵌法里真的可用"（点验清单在 spike §十）。"引擎自带"≠"壳里自带"。

### B 表 · 引擎级能力（非 action，但用户当"功能"要）——同样自带，各带一条注意

| 能力 | 可行性 | 注意 |
|---|---|---|
| .excalidraw 场景保存/恢复 + **版本迁移** | ✔ migrate 引擎自带 | 存储层是 app 壳的（FileManager/IndexedDB）——桌面**接管存储**即无；迁移版本链由引擎处理，宿主别抄一份 |
| 素材库导入导出 .excalidrawlib | ✔ 本地 | 内置库随包？——需 spike 验证 assets；库 JSON 本地文件加载走"打开文件"桥（坑13） |
| i18n（简体中文等） | ✔ | locales 体积取舍：只打 zh-CN+en 还是全量 58 个语言（dist 几百 KB 级，倾向全量，打包一次的事） |
| 深色主题/容器主题联动 | ✔ 可配置注入 | 主题三规则（本仓主题纪律）怎么跨 web 面同步——**app 的 theme 是唯一出处，桥过去注入**，别两边各存（分岔预定） |
| 手绘观感（roughjs/perfect-freehand/Xiaolai） | ✔ | 字体离线坑 4/5（下述） |
| 撤销栈含多页？(0.18 单页场景) | 以引擎现状为准，宿主别加戏 |

---

## 三、D 表 · 服务器/产品壳功能 ——"1:1"里的减法，逐项处置

| 功能 | 本体 | 桌面处置 | 处置手段（不是嘴上关，要可验证） |
|---|---|---|---|
| 实时协作 collab（firebase） | app 层 + editor encode/加密件 | **不带** | 嵌入 bundle 不引入 firebase 依赖；editor 里 `onPointerUpdate`/collab 广播件不接线即死码，不碍事；**验收：包内无 firebase 域名字符串** |
| TTD 文本生图（AI，含 Chat 面板 16 文件） | **editor 内置组件**；需后端/用户 Key | **默认关闭** | feature flag（common DEFAULT_FEATURES_STATE，具体 flag 名 spike 实证）；关不掉的版本=菜单摘除（坑1 验证项：AI 入口若还能点，点开必须给出**为什么不能用**而不是 fetch 卡死） |
| PublishLibrary 发布到社区库 | editor 内 fetch(VITE_APP_LIBRARY_BACKEND) | **摘除入口** | 构建 env 置空 + 按钮开关；同上，空 env 下 fetch 行为 spike 实证（有的版本静默 fail 有的抛未捕获） |
| embeddable（网页嵌入元素） | element+components，iframe | **默认隐藏工具 + 导入场景带 entry 时降级为占位** | 离线无意义；**注意 scene 向前兼容**：别人分享的 .excalidraw 里若有 embeddable，打开会注入远程 iframe——**桥层 CSP/iframe src 白名单必须在**（安全硬线，不是观感问题） |
| excalidraw+ 促销/导入到 plus | PromoBanner/ExportToExcalidrawPlus（app 层）+ HelpDialog 内 docs/blog 外链（**editor 层实测有**） | 不打包/外链**接管** | HelpDialog 外链：宿主 `NewWindowRequested` 统一转 `LauncherEx`（交给系统浏览器开，弹窗内不导航） |
| sentry / analytics / CustomStats | app 层 | 不引入 | 嵌入构建不接；**验收：无遥测域名** |
| 云字体 CDN | fonts esm.sh 回退 URL + woff2 子集 wasm fetch | **全部本地化** | §坑4/5，spike 一票否决项（首屏字体请求必须可断网复现全对） |
| share/link 加密分享、PWA、登录 | app 层/服务端 | **不带** | — |
| 图片 URL 导入（blob.ts fetch(imageUrl)） | editor | 禁用或经宿主代理（**默认禁用**，用户显式"从地址插入"再议） | 自动抓取=出机流量+坏图静默；桥层禁此命令 |

---

## 四、与 StarMark 既有能力的集成矩阵（这部分才是桌面的增值，也是写进产品文档"为什么不直接开网页版"的唯一答案）

| 集成点 | 方向 | 现成地基 | 新建件 |
|---|---|---|---|
| 截图冻帧 → 画板背景 | 截图链出 | Pin 像素管线现成 | "去画板"入口 + 桥命令（背景图注入，excalidraw 原生 backgroundImage） |
| 画板导出 PNG → 贴桌面 | excalidraw 出 | **ClipIMG-P3 的 PinPixels 接合现成** | 导出产物经桥→BGRA→PinPixels（只传文件/图，符合零像素流量界线） |
| Board(画布)快照 → 图元素 | 快画成果落图 | CanvasService 快照/试 | 同源桥命令 |
| ClipIMG 历史图 → 素材/背景 | 剪贴板图片入画板 | ClipAssets 目录/管线 | "置为背景"入口 |
| 场景落盘 → items 注册 → FTS | 沉淀生命周期⑥ | ClipIMG 三分类对账/可读命名惯例 | boards\ 目录 + 场景 JSON 的文本元素抽字入 search_text（**图第一次可被收藏系统检索**——差异化核心） |
| 备份链带场景 | 备份 a 期 | zip(store)+开关 惯例现成 | 附件清单加 boards | 
| 全局键位共存 | 桥策略 | S1 HotkeyGate/Hub 状态机 | "画板前台"入注册表投影（坑9） |
| 设置同步 | 主题/语言 | theme token | 桥单向 native→web 注（B 表） |

---

## 五、避坑清单（编号引用位；每条=真会坏的形状 + 出处，不是假设）

### 致命坑（不处理=上线即死或功能大面积不可用）

**坑1 · "1:1"口径无治理** —— 验收必翻案。措辞钉死为"编辑器功能 1:1、产品外壳按 D 表"，写进任务书本档和验收，三处一致。
**坑2 · WebView2 = 新运行时但文档叙事当旧件** —— 本项目实测零引用；"RSS 有 WebView2 先例"是浏览器扩展的事实混入（本分析自己差点顺手写成"复用既有"——已在本会话内实抓自纠，**教训**：集成叙事先实查两仓）。落地批的 csproj diff 会第一次引入该包，review 时按"新运行时引入"重量级过。
**坑3 · TTD/PublishLibrary 的 fetch 在关不掉的版本上静默死亡** —— 关法以 spike 实测为准 + 按钮摘除双保险；"点了没反应 30 秒"是本仓定性过的坏。
**坑4 · 字体 esm.sh 远程回退（实测硬编码，fonts/ExcalidrawFontFace.ts:24）** —— `createUrls` 本地 URL 在前回退在后，但**打包后 import.meta.env.PKG_* 缺失时回退变 latest 远程**。处置：构建时注入 PKG_NAME/VERSION（或 patch 掉回退）；spike 必须做**断网全字体验证**（中/英/emoji/手写体逐族，含 Xiaolai 中文断网）。〔片9 更正 那颗基座的因果与第一颗出路〕先读官方全局 <c>window.EXCALIDRAW_ASSET_PATH</c>（字符串／数组），再把 <c>ASSETS_FALLBACK_URL</c>＝<c>https://esm.sh/@excalidraw/excalidraw@0.18.0/dist/prod/</c> <b>永远追加成最后一颗候选</b>（<c>createUrls</c> 函数体逐字见 §19.1；随包 dist 在 <c>dev/chunk-3KPV5WBD.js:4926～4951</c>，我自己那颗 min 同段在 <c>@2272572</c>）。⛔ 人配基座时名单里<b>只有那颗 CDN</b>——这才是默认坏法（r3／r5 引擎原话 <c>Failed to load font "16px Virgil, Segoe UI Emoji" from urls "https://esm.sh/…"</c>，urls 后面就一颗）。那句「<c>import.meta.env.PKG_*</c> 缺失时回退变 latest 远程」在这份包里兑现⛔：我钉的这颗 dist 现读带版本 <c>@0.18.0</c>。处置第一颗＝宿主页面一行 <c>window.EXCALIDRAW_ASSET_PATH="/"</c>（那颗包 <c>README.md:24</c> 逐字给的就是这一行）＋<c>dist/prod/fonts</c> 随包；构建期注入 PKG_NAME/VERSION 或 patch 回退降为第二颗。
**坑5 · woff2 子集化 wasm 也是 fetch 加载（subset/woff2/woff2-bindings.ts 实测）** —— 子集化管线断网失效=字体回退+首屏字形突变。处置同坑4：wasm 随包 + 加载路径本地化核验（该 wasm 是 Emscripten 产物，URL 可配置性 spike 实证）。〔片9 更正 那句在我钉的这颗 <c>0.18.0</c> 随包 dist 里兑现⛔〕整棵 <c>dist</c> 走一遍 <c>.wasm</c> 档 <b>0</b> 颗；两颗 wasm 以 base64 活在 <c>prod/chunk-EIO257PC.js</c>（1 824 966 字节）里，解码后 <b>727 188</b>／<b>590 664</b> 字节，magic 四颗字节都是 <c>00 61 73 6d</c>；子集化那颗的点火点吃的是内联字节（min <c>@2073652</c>：<c>WebAssembly.instantiate(那颗base64解出的buffer)</c>，出口形状 <c>{subset:…}</c>）⇒「wasm 随包」这一格由「它本来就在 JS 里」兑现。r5 里 <c>WebAssembly.instantiate</c> 0 次＝它<b>没被叫到</b>（它排在「字体字节取到」之后，§19.2），⛔ 等于「断网时它坏」。读数正本＝<c>bin/WI83/spike2c_probe_83bc.txt</c> ②／③ 段。
**坑6 · 字体授权未核完** —— 11 族里 Virgil/Excalifont/Lilita/Cascadia/Xiaolai/Assistant/Nunito…许可证不一（Xiaolai 是 OFL、Cascadia MIT 可再查）；**代码 MIT 不含资产授权**；随包分发前逐族写 NOTICE（桌面仓库有 NOTICE 先例=WordList.Dic 那一族惯例）；Emoji 目录实测 0KB（可能是索引样式声明）——spike 核实它到底是什么，别默认中文 emoji 能画。
**坑7 · Xiaolai 单族 12.4MB 的体积决策** —— 不打=中文手绘观感降级为系统字体（用户拿它就是因为中文好看）；打=dist 从 ~2MB 级跳到 ~15MB 级（未实测 bundle 总量，**数字要实测后才许写**）。倾向打但列为 §九决策 4。
**坑8 · 内存红线（铁律①）** —— WebView2 进程树驻留是常态。硬约束：画板窗**按需建窗、关窗即 Dispose environment/Process.Kill 兜底实测进程数归零**；默认功能关=零资源。这条要进验收（任务书既有横切第 8 类"功能默认关零资源"的直接适用）；"关窗后 msedgewebview2 还活着"的坑在别的项目是常客。userDataFolder 强制改道 %LOCALAPPDATA%\StarMark\wbview2（默认跟随 exe 目录=开发态可写性雷——本仓 NuGet/安装目录两类事故的镜像）。
**坑9 · 全局键位互杀** —— 画板前台时 Ctrl+S/滚轮缩放/Z/Ctrl+Z 会被全局键层截走或反向。处置：画板进窗口状态机（Hub 会话位或并列的"画板前台"事实），注册表 GetRegisterableHotkeyBindings 按会话摘除（**该机制现成，但画板没被登记为会话态时要接**）；web 内键盘事件不回冒到 WPF/UIA 全局钩子的核验在 spike。
**坑10 · 崩溃边界** —— ProcessFailed（渲染进程崩）现形="画板白了"。处置：ProcessFailed→事件桥报+当前场景自动落盘（场景快照防用户丢图）+重载（非静默）。宿主纪律"崩溃不能带走主程序"（本项目托盘/采集全按这条办）。
**坑11 · 存储接管=职责表不是"顺手换个写盘"** —— IndexedDB/tabSync/LocalStorage（实测 EditorLocalStorage/appState 等 3 处）属 app 壳；桌面接管后：autosave 时机、同文件多窗冲突（**tabSync 的跨标签同步在桌面多窗下不自动成立，多板窗=数据竞争面**）、文件名↔场景 id 映射、"未保存离开"拦截。**此表进设计批，先写后码**。 〔片10 源码现读换掉那三处的猜测〕<b>随包那颗引擎</b>自己⛔ 往任何持久化层写：整棵 <c>dist/dev</c>（65 档 5 827 756 字节）＋我自己那颗 <c>spike1.min.js</c> 两套各数一遍——<c>indexedDB</c> <b>0</b>、<c>openDatabase</c> <b>0</b>、<c>BroadcastChannel</c> <b>0</b>、<c>sessionStorage</c> <b>0</b>、<c>document.cookie</c> <b>0</b>、<c>serviceWorker</c> <b>0</b>；<c>localStorage</c> 只有 <b>8</b> 处命中，全在 <c>EditorLocalStorage</c> 那颗 wrapper 里（<c>has/get/set/delete</c>，每颗都 <c>try/catch</c> ＋ <c>console.warn</c>），而它的<b>调用点只有 5 颗、实名键只有 2 颗</b>：<c>publish-library</c>（发布预览）与 <c>mermaid-to-excalidraw</c>（那颗自带 300 ms 防抖）。⇒ 这张职责表第一行改口：<b>⛔ 需要「关掉引擎那一层」，只需要「宿主自己定策略」</b>；autosave／跨窗同步／appState 持久化那三样住在 excalidraw.<b>com</b> 那颗网页壳里，⛔ 在包里（§二十）。
**坑12 · 剪贴板与文件拖放的 web 侧权限** —— WebView2 默认权限策略对 image paste 可能弹询问或直接拒（不同版本行为有记录级差异）。处置：spike 测"从 Word 复制图/文件拖入"是否免打扰；权限声明按需 Allow。
**坑13 · 导出/下载出口（最大的"看着好"陷阱）** —— 浏览器里 export 走 download（blob URL）；**WebView2 默认弹下载对话框或根本没处理=用户点导出没反应**。处置：`Downloading` 事件或桥接导出字节到 C#（落"图片\StarMark 截图"同目录族惯例），且**每个导出档位（PNG/SVG/.excalidraw/剪贴板位图）逐一真点验收**；SVG 导出是否内嵌字体（fonts 外链=拿到别处看掉字体——待 spike，不许先承诺"1:1"）。
**坑14 · 桥安全=命令白名单** —— C# 听 JS 消息 =攻击面（恶意 .excalidrawlib/scene 触发 JS 行为→桥指令）。桥协议：命令白名单（save/export/pin/requestBackground/openFile）、**无路径注入**（文件落点由 C# 定死目录，JS 只递字节）、消息 schema 校验。这条写死在协议设计，不写=形同虚设（对照 askConfirm 教训：确认机制不设形同虚设）。

### 一般坑（会痛不致命）

**坑15 · 构建链 Node 引入** —— 两模式（预编译 dist 随仓 / CI 装 Node）都要拍（§九决策2）；选 A 要配套：LICENSE/NOTICE 入包、上游版本号钉进文件名/清单、升级流程文档化（重打 dist=有 Node 时的手动动作，产物提交带校验和）。不拍=第一行代码前卡住。
**16 · 上游版本漂移** —— 0.18.0 快照钉死；升级=独立批（spike 全点验重跑+**场景回归：旧板新打开**）；升级不许搭车。
**17 · 语言包全量化** —— 见 B 表：全打，spike 验中文不依赖网络（坑4 联动）。
**18 · 触屏/触控板** —— 手绘笔 pressure/tilt 与触控手势；WebView2 指针支持存在但**笔压可用性要验**（无则文案不承诺。
**19 · 高 DPI/多屏** —— zoom factor 跟随屏；本仓 WO 批次(DPI)同类事故已有坑表；spike 副屏验证。
**20 · 打印** —— excalidraw 无 app 壳打印；桌面不承诺。
**21 · HelpDialog docs 链接在 editor 层** —— 不摘（外链接管后无害），但"文档"按钮点开=系统浏览器；验收写明，别当 bug 修。
**22 · 与截图遮罩的抢前台** —— 画板窗激活时全局热键截屏 F1 等共存（现有 HotkeyGate 语义）；遮罩打开时画板窗必须摘热+暂停 autosave（否则遮罩背景抓到画板自己的板——**这是 C 类旧冲突的画板版，先登记**）。
**23 · 场景即数据→隐私面** —— 板内可含贴图/截图/剪贴板粘图；备份"含图片"开关既有先例（ClipIMG：备份开关/隐私声明）；boards 附件同样纳入那颗（**复用开关，别加新键**——同参数表唯一出处纪律）。

---

## 六、架构分层（定稿提案，待拍板细化）

```
桌面壳 (WinUI)                          web bundle (离线) ────────────
┌─────────────────┐    PostWebMessage     ┌──────────────────────┐
│ BoardWorkspaceWin│◄──(白名单桥协议)─────►│ @excalidraw/excalidraw │
│ 生命周期/Dispose │                        │ (engine + fonts 本地  │
│ 存储适配器/对账   │                        │  +T T D 关/publish 摘) │
│ PinPixels 接合   │──导出回调───────────► │                        │
│ 全局键位闸门投影 │                        └──────────────────────┘
│ theme/i18n 注入 │
└─────────────────┘
```
关键：`BridgeProtocol` 单一 schema 出处；`StorageAdapter` 归 C#（web 内无 IndexedDB 写——`LocalData` 层禁用，引擎 onChange 上抛，防坑11 双主）；`export` 落点策略归 C#；"桥无路径"（JS 不决定文件写哪；用户开文件=WebView 发起→C# 弹框→选后回字节或只读流）。  〔片11a v1 定稿三处：①那句「<c>LocalData</c> 层禁用」⛔ 再写（那颗层在随包 dist 里⛔ 存在，§20.4）；②「桥无路径」从此有<b>正件</b>（§18.4 越权那格三颗 evil 全被 <c>Path.GetFileName</c> 收进目录）；③那两份名单逐颗换成真名——命令 <b>7 颗→6 颗</b>（摘 <c>scene.save</c>／<c>scene.dirty</c>，加 <c>paste.query</c>），见 §21.1。〕

---

## 七、非目标（写死防蔓延）

协作房间、excalidraw+、AI 生图（TTD）、云库发布、embeddable 远程、登录/PWA/离线包自管（**桌面版=我们离线化**）、图片 URL 抓取、官方 share 链接、Mermaid→（mermaid **转成编辑对象** 那半本地可用，AI 生 mermaid 码不走 mermaid 生图那半**——engine 本地算，可用；写清，防误传）。

## 八、建议批形（拍板后才排，非承诺）

0. **Spike 批（裁决前唯一允许的动作）**：①runtime 探测+环境释放实测；②字体/wasm 断网全验；③导出/下载出口 3 法各试（Downloading 事件/桥回调/剪贴板中转）；④TTD/Publish 关按钮有效性；⑤桥消息往返时延实测。产物=裁决数字表，产品码不进。
1. 架构批（桥协议+存储适配器设计文档先行，过评审再码）  〔片11b 交付＝<b>§二十一 v1</b>：那份「待评审稿」现在每颗名字都钉在包里读到的真名上（§21.0 那八行逐颗点名，其中第 5 行是<b>我自己写反的形7</b>）；代码批仍⛔ 开工，等 v1 过评审。〕
2. 宿主窗+引擎 bundle（先跑通空板，不含集成）
3. 存储接管批（boards/对账/autosave/冲突表逐项对 §坑11） 〔片10〕那一批现在有的零件：引擎交出的<b>读面只有 4 颗 getter</b>（<c>getSceneElements</c>／<c>getAppState</c>／<c>getFiles</c>／<c>getName</c>）＋写面 <c>updateScene</c>，⛔ 任何自带落盘⇒时机、粒度、冲突策略<b>全归 C#</b>（§20.4／§20.7）。
4. 集成批×3（截图→画板/画板→贴图/Board→快照；ClipIMG→背景）
5. 检索沉淀批（items 注册+search_text 场景抽字）
6. 入口/设置批（主题键位共存/备份开关/隐私声明/快捷键摘挂）
每批：任务书逐条对（#182）、真机点验进 §十、坑表续排。

## 九、三（四）个人拍板项（不定=后续全停）

1. **构建交付模式**：A 预编译 dist 随仓（推荐：守零构建链；成本=仓体积+版本人工钉）；B 构建期 Node（第二工具链进 CI；升级顺滑但新风险面）。
2. **生命周期档位**：关窗即散（内存派，默认推荐）vs 空闲 N 分钟（体验派）。
   - **〔裁决 2026-10-17·代裁〕＝「关窗即散」**。理由：坑8 铁律①（功能默认关＝零资源）与 §18.7 现读（userDataFolder 隔离后关窗进程树 0 颗）站在同一边；「空闲 N 分钟」意味着常驻 WebView2 进程树＝常驻内存，买的是「二次开窗快几百 ms」，而首帧现读 ≈960 ms（§21.5）本身可接受。<b>体验派那半若他睡醒要翻，翻这条即可，其余腿不动。</b>
3. **Xiaolai 随包**（12.4MB 换中文手绘观感）：倾向"是"，但数字见坑7。
   - **〔裁决 2026-10-17·代裁〕＝中档「主区 100 片」≈5.43 MB**（§24.3 三档：全随 12.08／主区 5.43／出现过 135 KB）。理由：随 0＝中文手写观感降级为系统字面（§坑7 照旧成立）；全随多花 6.6 MB 买的是生僻字那 105 片；「只随出现过那档」的清单随用户内容漂移＝<b>⛔ 可复算、冻结清单没法钉</b>（每新增一张板都可能要重算随包名单）。生僻字回落系统字面＝已知且说出口的降级。另：片17 那 4 颗死常量（0 处 <c>uri:</c> 引用）<b>⛔ 进随包清单</b>——分母先按注册表 205 筛、再按主区筛 100。
4. **D 表全拒认吗**：collab/云/AI/发布全不带（本分析默认建议），若你要 AI 生图（TTD 自带 BYOK 接用户 Key 的路径），它是**另一个议题另立批**——不许顺手进 embed（默认 Key 不配时它=死按钮）。
   - **〔裁决 2026-10-17·代裁〕＝全拒认照旧；mermaid 那颗插件摘除**（打包腿 stub alias 照旧——片13 量到入口离线可用，但宿主自排 MainMenu 时根本⛔ 排它的入口，留着只是 bundle 白胖）；TTD／AI 生图另立批，⛔ 顺手进。

## 十、真机点验单（spike/每批交付后跑，先于任何"完成"宣告）

- 断网：冷启画板 → 全字体渲染（中/英/数字/emoji/手写）无请求失败痕迹（DevTools 关网）→ **字体族缺失=坑4/5 未修好**
- 导出 PNG/SVG/.excalidraw/复制为图 四路各一遍 → 文件真落盘/粘贴真出图 → 路径/命名由 **C# 定**（桥无路径核验）
- 图元素插入（拖 20MB 图）→ 减重管线 wasm 生效（断网下）
- 关窗 60 秒后：进程树进程数=0，localStorage userData 落点目录=改道后目录
- F1/全局键共存验证（截图键位不被吃）+ 画板前台时全局键位摘挂（截图热键 F1 不吃）
- 桥越权测试：消息伪造（保存任意路径/命令/iframe url）→ 全拒
- 场景回环：保存→重开→undo 栈清空→引擎迁移静默无 warn
- 旧 embeddable 场景打开：iframe 不加载远程（占位+警告文案）
- 多板窗同文件：冲突表行为可复现
- TTD/AI/Publish 按钮在包内不存在或摘除；Help 外部走系统浏览器



---

## 十一、Spike-0 复称与数字表（批次 Phase C 片1，2026-10-17·只读探测，⛔ 产品码⛔ 起窗⛔ 装东西）

> 尺与正本＝<c>StarMarkDesktop/bin/WI83/spike0_83a.py</c> → <c>bin/WI83/spike0_83a.txt</c>。
> 本节做两件事：① 把 §一 那张"证据面"表里<b>本轮能复称的</b>那几颗重新读一遍（三处与本文不符，逐颗点名）；
> ② 把 §八 Spike 批里<b>不起窗就能答</b>的那几格交掉，其余各格按名册 <b>289</b> 留「未跑」并写明为什么跑不到。

### ① 运行时与 SDK：这台机器上⛔ 需要联网装东西（覆盖 §〇-3 与 §坑2 的叙事）

| 现读 | 数 |
|---|---|
| WebView2 <b>Runtime</b>（注册表 <c>EdgeUpdate\Clients</c> 里那颗 WebView2 GUID 的 <c>pv</c>） | <b>152.0.4191.66</b> |
| Runtime 已装版本目录（<c>Program Files (x86)\Microsoft\EdgeWebView\Application</c>） | <b>6 版</b>：151.0.4129.107, 151.0.4129.72, 151.0.4129.78, 152.0.4191.53, 152.0.4191.62, 152.0.4191.66 |
| NuGet 缓存里的 <c>Microsoft.Web.WebView2</c> <b>SDK</b> | <b>3 版</b>：1.0.2651.64, 1.0.2903.40, 1.0.3719.77 |
| 本仓 <c>*.csproj</c> 含 "WebView2" 的档数 | <b>0 / 38</b>（＝§一 那颗"零引用"复称一致） |

⇒ <b>§一 那句"本项目实测无 WebView2"仍然成立，但它说的是"仓里没引用"，⛔ 是"这台机器上没有"。</b>
Runtime 与 SDK 两样本机都在，所以"新增运行时"的<b>开发态</b>成本比 §〇-3 读起来的样子低（⛔ 联网、⛔ 装 SDK）；
<b>用户机器上有没有 Runtime 仍是另一件事</b>——那要 §五/§九 的"运行时缺失怎么办"单独答，本节⛔ 判。

### ② §坑16「上游版本漂移」：本地那棵树⛔ 是 0.18.0 那一发

<c>packages/excalidraw/package.json</c> 的 <c>version</c> ＝ <b>0.18.0</b>，
而 <c>git describe</c> ＝ <b>ed10ac7d / v0.18.0-429-ged10ac7d</b> ⇒ 本地树在 <b>v0.18.0</b> 之后 <b>429 颗提交</b>上（HEAD <b>ed10ac7d</b>）。
本文 §一 写的是"实测自 monorepo @ 0.18.0"——那颗数是从 <c>package.json</c> 读的，而<b>钉版本要钉 commit</b>：
走 §九 决策 1 的 A 路线时，清单里必须写 <b>ed10ac7d</b>（或改拿 tag 那一发重新 checkout），
⛔ 只写"0.18.0"——那就是名册 <b>290</b>（"以版本命名的产物证不了内容"）在依赖树上的同一形状。

### ③ §九 决策 1（构建交付模式）的前置事实：A 路线<b>目前无 dist 可随</b>

- <c>packages/excalidraw/dist</c> ＝ <b>⛔ 不存在（§九 决策 1 走 A 就得先有这一步）</b>；<c>node_modules</c> ＝ <b>⛔ 不在</b>。
- ⇒ 选 A（预编译 dist 随仓）⛔ 是"把现成目录拷进来"：要么在<b>别处</b>（有 Node＋联网的机器）先打一次再把产物随仓，
  要么本机装 Node 走 B。<b>两颗都不是"零成本"</b>，而 §九 那句"守零构建链"只在<b>产物已存在</b>时成立。
- 这条数字表交到这里为止：⛔ 装 Node、⛔ 跑 npm install、⛔ 改任何 csproj（§八 明写 Spike 批⛔ 产品码）。

### ④ §一 与 §坑7 那两颗字体数：复称不符（就地更正）

| 本文原句 | 现读 |
|---|---|
| §一「随包字体 <b>11 族</b>／合计 <b>~12.97MB</b>」 | <b>9 族 / 12.50 MB / 234 个文件</b> |
| §一／§坑7「Xiaolai(中文手写) <b>单族 12.4MB</b>」 | Xiaolai ＝ <b>209 个 woff2 分片 / 12.08 MB</b> |
| §B 表「locales 全量 <b>58</b> 个语言」 | <b>58 支</b>（一致） |

⇒ 新信息⛔ 是"少了几 MB"，而是<b>形状</b>：那颗中文手写体是 <b>209 个文件</b>，⛔ 一颗。
随包时它要进<b>逐文件清单</b>（发布那侧的冻结清单／sha256 对账、以及 ClipIMG 那套"孤儿只计数不静默删"的对账）——
§坑7 只算了体积没算<b>文件颗数</b>，而 §九 决策 3 拍"是"的那一刻，代价是"209 个文件＋12.08 MB"。

### ⑤ §坑4／§坑5 那两处远程回退：行号漂了，但都还在（且 woff2 那一半有了新答案）

- <c>esm.sh</c> 那颗在 <c>fonts/ExcalidrawFontFace.ts</c> 的<b>第 11 行</b>（<c>ASSETS_FALLBACK_URL</c>），
  本文写的是 <c>:24</c> ⇒ 行号漂（同一颗教训第一次落到<b>另一个仓</b>：名册 <b>315</b>——行号只当导航，身份在那句正文）。
- <c>woff2</c> 子集那颗：现读 <b>8 处</b>命中在 <c>subset/woff2/woff2-bindings.ts</c>，
  其中 <c>:340 if (Module.wasmBinary)</c> ／ <c>:341 wasmBinary = Module.wasmBinary</c> 说明<b>那一侧支持"直接喂字节"</b>
  ⇒ §坑5 那句"URL 可配置性 spike 实证"的答案是<b>半个好消息</b>：宿主可以把 wasm 读成字节递给 Emscripten 的
  <c>wasmBinary</c>，⛔ 必须给它一个可 fetch 的 URL（真断网验证仍在那一格的「未跑」里）。

### ⑥ §坑13 那颗事件名⛔ 对（三版 SDK 文档一致）

现读缓存里那三版 SDK 的 XML 文档：<c>Downloading</c> ＝ <b>0 处</b>，
<c>DownloadStarting</c> ＝ <b>12 处</b>，<c>DownloadOperation</c> ＝ <b>75 处</b>
⇒ §坑13 与 §十 点验单里那句"`Downloading` 事件"要改成 <b><c>DownloadStarting</c></b>（老名 <c>Downloading</c> 在这三版里根本⛔ 存在）。
桥那两件：`PostWebMessageAsJson`＝<b>39 处</b>／`WebMessageReceived`＝<b>74 处</b>；
§坑10 那颗崩溃事件 `ProcessFailed`＝<b>33 处</b>——名字都在，<b>真机行为仍待起窗</b>。

### ⑦ 未跑（⛔ 从表里消失，也⛔ 拿"应该没问题"填上）

③导出出口三法的真机行为（要点真窗才知道）／④TTD 与 PublishLibrary 关按钮有效性／⑤桥消息往返时延／②断网全字体验证／字体授权逐族 NOTICE（§坑6）
⇒ 这五格的共同前提＝<b>起一颗真窗</b>（或人查授权），而那一步归 §九 那四颗拍板之后；
本节⛔ 动产品码，所以 §十 那张点验单<b>一格都还没打勾</b>。

### ⑧ 对 §九 那四颗拍板的影响（技术裁决归我，口径仍等他）

1. <b>决策 1（构建交付）</b>：本节的 ③ 把 A 路线的"零成本"读掉了——但 A 仍是<b>唯一守得住"零构建链进 CI"</b>的那条：
   我的裁决＝<b>A，且把"产物从哪个 commit 打的"写进清单</b>（②那颗 <b>ed10ac7d</b>），⛔ B。
2. <b>决策 2（生命周期）</b>：本机读数⛔ 涉及进程树，仍按 §坑8 默认「关窗即散＋Dispose 后实测进程数归零」。
3. <b>决策 3（Xiaolai 随包）</b>：④ 那颗形状（209 个文件）是他点验时要看的代价之一，<b>口径题仍归他</b>。
4. <b>决策 4（D 表全不带）</b>：本节⛔ 新信息，维持默认（全不带；AI 生图另立批）。


---

## 十二、落地设计提案 v0（批次 Phase C 片2，2026-10-17·设计先行，⛔ 产品码⛔ 建工程⛔ 装工具链）

> 这一节是 §八 那颗<b>第 1 批</b>（§八 原文逐字："桥协议+存储适配器设计文档先行，过评审再码"）的<b>待评审稿</b>。
> 只写"怎么接"，不写"什么时候接"——§九 那四颗拍板里还有两颗归他（决策 3 Xiaolai 随包、决策 4 AI 生图另立批），
> 且 Spike-1 要装 Node＋`npm install`（<b>那是要他点头的外部动作</b>，见本节末）。
> 文中点名的每颗本仓成员都⛔ 是想象：落笔前逐颗在源码里读到过那一行。

### 12.1 桥协议（`BridgeProtocol`＝唯一 schema 出处）

一颗窗、一条通道、<b>两份名单</b>：JS→C# 的<b>命令白名单</b>（名单外一律拒并记日志），C#→JS 的<b>事件白名单</b>。  〔片11c：那两份名单逐颗换成真名，见 §21.1；本表里 <c>scene.save</c>／<c>scene.dirty</c> 两行<b>作废</b>（宿主按 §20.2 的读面 getter 自己取，⛔ 等 JS 喊存），另补一颗 <c>paste.query</c>（唯一否决口是 <c>onPaste</c>，§20.3）。〕
形状＝`{ "v": 1, "cmd": "<名字>", "id": "<回执配对用>", "args": { … } }`，走 <c>PostWebMessageAsJson</c> ↔
<c>WebMessageReceived</c>（§十一 ⑥ 现读那两名字在缓存的三版 SDK 文档里各有 39／74 处）。

| JS→C# 命令 | args（⛔ 任何路径／文件名／URL） | C# 侧动作 | 拒的形状 |
|---|---|---|---|
| `scene.save` | 场景 JSON（<b>字节</b>） | 由 C# 定落点（12.2）后写盘，回 `id`＋那颗稳定 id | args 里出现路径字段⇒整条拒 |
| `scene.dirty` | 一颗 bool | 只改那颗"有未保存"的位，⛔ 写盘 | — |
| `export.bytes` | 导出产物字节＋档位枚举（png／svg／scene） | 落「图片\StarMark 截图」同目录族，命名由 C# 定 | 档位不在枚举⇒拒 |
| `pin.request` | 一颗已有产物的 <b>token</b>（C# 发过给 JS 的那颗） | 走 `ClipIMG-P3` 那条接合贴到桌面 | token 认不出⇒拒 |
| `background.request` | 无参（要一张背景，C# 自己知道是哪张） | 冻帧／ClipIMG 历史图选一张，回字节 | — |
| `open.external` | 一条 <b>http(s) URL</b>（HelpDialog 那类） | 交 `LauncherEx.TryOpenAsync`（系统浏览器），弹窗内⛔ 导航 | 非 http(s)／带凭据⇒拒 |
| `bridge.ready` | 无参 | 回 12.5 那三颗注入值（主题／语言／功能位） | — |

<b>硬线三条</b>（§坑14 的落地形状）：① <b>JS 永远⛔ 决定文件写在哪</b>——所有落点目录与命名由 C# 定；
② 每条命令都要过 <c>cmd</c> 白名单＋args 的<b>字段白名单</b>（多一个键就拒，⛔ 忽略）；
③ 场景里带 <c>embeddable</c>／远程 <c>iframe</c>／图片 URL 的那几颗，在<b>导入那一刻</b>降级为占位（§三 D 表），
配套 CSP 由宿主注入（⛔ 依赖 bundle 自带的策略）。
验收格＝§十「桥越权测试：消息伪造（保存任意路径/命令/iframe url）→ 全拒」。

### 12.2 存储接管（`StorageAdapter` 全在 C# 侧；引擎的 LocalData 层⛔ 点火）

| 事项 | 提案 | 为什么这一形 |
|---|---|---|
| 落点 | <c>%APPDATA%\StarMark\boards</c> 目录下一颗文件一板（<c>&lt;稳定 id&gt;.excalidraw</c>） | 与 <c>settings.json</c>／<c>widgets.json</c> 同目录族，备份链与"读档⛔ 截零"那套惯例直接复用 |
| 稳定 id | 建板那一刻由 C# 生成（<c>yymmdd-HHMMss-4hex</c>），<b>文件名即 id</b> | 与 ClipIMG 的"稳定 id＋显示侧当场念"同一口径；⛔ 拿标题当身份（标题会随语言与用户改字） |
| autosave | 停笔 1.5 s 或窗失焦即存；<b>另存一份 <c>.bak</c></b>（只留最近一颗） | 引擎自带的 autosave 住在 <c>FileManager</c>／IndexedDB（§坑11），桌面接管后<b>时机归 C#</b>  〔片10 更正那颗归因〕那句「引擎自带的 autosave 住在 <c>FileManager</c>／<c>IndexedDB</c>」讲的是 excalidraw.<b>com</b> 那颗壳；随包那颗⛔ autosave（整颗包 localStorage 写点只有两颗键，§20.4）⇒ 那一行的「时机归 C#」从「改道」升为<b>「本来就⛔ 主人」</b>。 |
| 同文件多窗 | 打开时登记 <c>(id → pid 窗实例)</c>；第二扇要开同一颗 ⇒ <b>聚焦已有那扇</b>，⛔ 起第二份 | 引擎的 <c>tabSync</c> 只在<b>同源同浏览器</b>的标签间成立，跨两扇 WinUI 窗⛔ 自动同步＝数据竞争面  〔片10 换那颗因果〕数据竞争的<b>来源</b>更正：⛔ 是「引擎的 tabSync 失配」，而是<b>引擎根本没有跨窗同步这一层</b>（<c>BroadcastChannel</c> 两套语料各 0 颗）⇒ 桌面两扇板窗开同一颗档，谁都不让谁，<b>合与不合全由宿主定</b>；现成零件＝包里导出的 <c>reconcileElements</c>／<c>getSceneVersion</c>／<c>hashElementsVersion</c> ＋那颗 <c>CaptureUpdateAction</c>（现读 157 处）（§20.7）。 |
| 未保存离开 | 拦截那一拍给<b>就地三颗按钮</b>（存／丢／留下决定），⛔ 把"请先保存"写成用户作业 | P-54 的口径（程序能做的动作⛔ 写成用户作业） |
| 迁移 | 场景版本链<b>全交引擎</b>（<c>migrate</c>），宿主⛔ 抄一份 | §B 表那句"宿主别抄一份"；抄了就出现第二个主人 |
| 对账 | 启动三分类（板在／档在而板缺／板在而注册缺）＝只<b>计数＋给按钮</b>，⛔ 静默删 | ClipIMG-1e 那一族直接复用 |

### 12.3 生命周期与进程树（§坑8 的硬约束）

- <b>按需建、关窗即散</b>：`CoreWebView2Environment` 随那扇窗生，窗 `Closed` 里 <c>Dispose</c>；
  兜底＝按<b>自己那颗 pid 的子树</b>清 <c>msedgewebview2</c>（⛔ 杀不属于自己的进程，取证线那条纪律同样适用于自己写的码）。
- <c>userDataFolder</c> 强制 <c>%LOCALAPPDATA%\StarMark\wbview2</c>（默认跟随 exe 目录＝开发态可写性雷）。
- <b>功能总开关关掉 ⇒ 一扇都不建、一个子进程都不起</b>（横切第 8 类"默认关零资源"的直接适用；
  内存那条线早已量过：只有"不建"省得下来）。
- 验收格＝§十「关窗 60 秒后：进程树进程数=0，localStorage userData 落点目录=改道后目录」。

### 12.4 崩溃与出机流量

- <c>ProcessFailed</c>（§十一 ⑥ 现读：那一名字在缓存的三版 SDK 文档里 33 处）⇒ 事件桥报＋<b>当前场景自动落盘</b>＋重载，
  ⛔ 静默白屏（§坑10；"点了没反应"是本仓定性过的坏）。  〔片11d 再加一格：⛔ 白屏的下一颗风险是「重载之后回到空板」。那份自动落盘的草稿由<b>宿主持有</b>（§21.3 那四拍之一），重载时用 <c>board.load</c> 按<b>三步</b>递回去（<c>loadFromBlob</c>→<c>updateScene</c>→<c>addFiles</c>，真签名在 §21.3）；递之前把 §21.1 硬线⑥那五颗退订句柄摘一次。〕
- 下载出口＝接 <c>DownloadStarting</c>（<b>⛔ 叫 <c>Downloading</c></b>——§十一 ⑥ 现读三版 SDK 里那颗老名 0 处、
  新名 12 处；§坑13 与 §十 里那句老名已在那一格改口）。四个导出档位（PNG／SVG／<c>.excalidraw</c>／剪贴板位图）<b>逐一真点验收</b>。
- 引擎自带的 16 处网络出口按 §三 D 表逐项处置；<b>字体那颗远程回退</b>（§坑4）与 <b>woff2 子集的 wasm</b>（§坑5）
  是<b>一票否决项</b>：§十一 ⑤ 现读 <c>woff2-bindings.ts</c> 认 <c>Module.wasmBinary</c> ⇒ 宿主可直接喂字节，
  这条把"URL 可配置性"那一半的不确定消掉了，<b>断网全字体验证仍欠</b>。

### 12.5 §四 那张矩阵的落地形状（八条全数——"为什么不直接开网页版"那顶帽子在 §四，这里只答"走哪颗现成的、新建哪一颗"）

| 接缝 | 走哪条现成的 | 新建 |
|---|---|---|
| 截图冻帧 → 画板背景 | 截图链的冻帧管线（S1 已穿 Hub） | `background.request` 一条命令 |
| 画板导出 → 贴桌面 | <b>ClipIMG-P3 的 PinPixels 接合</b> | token 发放与回收 |
| 剪贴板历史图 → 背景／素材 | <c>ClipAssets</c> 目录与命名 | "置为背景"那一颗入口 |
| 画布（Board 快照）→ 图元素 | 现有那条画布快照成贴（S4-⑤） | 与 `export.bytes` 同源的一条命令（⛔ 另写一份导出） |
| 场景 → 可检索 | items 注册 + <c>search_text</c> | <b>把场景 JSON 里的文字元素抽出来入库</b>（图第一次能被收藏系统检索） |
| 场景 → 备份链 | 备份 a 期 zip(store)＋那颗开关 | boards 进附件清单，<b>复用"含图片"那颗开关</b>（§坑23：别加新键） |
| 主题／语言注入 | `Locale`＋那颗主题资源同步 | C#→JS 三颗值（⛔ 两边各存一份） |
| 全局键位共存 | `AnnotationHub.Raise(SessionEvent)` ＋ `ReapplyHotkeys()` 那一拍按 `GetRegisterableHotkeyBindings()` 摘挂 | <b>画板窗登记成 Hub 的一颗会话态</b>（`SessionEvent` 加一格）——现成机制只⛔ 认它 |

### 12.6 评审时要一起定的三件事（⛔ 我能替定的都写了；这三颗要他）

1. **Spike-1 的外部动作**：装 Node＋`npm install`（拉几百颗包）才能产出第一份 dist——§十一 ③ 现读
   <c>⛔ 不存在（§九 决策 1 走 A 就得先有这一步）</c>、<c>node_modules</c> ⛔ 在。要不要装、装到 D 盘哪一处、还是改由他那边打一份 dist 给我，<b>等他点头</b>
   （我不在他机器上静默装工具链）。钉版本要钉 <b>commit</b>：现读 <c>ed10ac7d / v0.18.0-429-ged10ac7d</c>。  〔片11e 更正那颗「等他点头」：那一档到 §十六／§十七 <b>已不需要外部动作</b>——本机运行时本来就活（§16.1），打包落在那棵<b>⛔ 随仓</b>的 <c>bin/WI83/npm0e/</c>（整棵 <c>bin/</c> 在 <c>.gitignore</c> 内），⛔ 全局装⛔ 改 PATH⛔ 动那棵树 ⇒ 这颗从「欠他」移到<b>「已办」</b>；本节末剩下两颗（决策 3、4）照旧归他。〕
2. **§九 决策 3**：Xiaolai 随包＝209 个分片 / 12.08 MB（形状：分片⛔ 一颗文件，代价要进逐文件清单）。
3. **§九 决策 4**：AI 生图（TTD）要不要走"自带 Key"那条——本提案默认<b>不带</b>，要带就另立批。

> 本节状态＝<b>待评审</b>。过评审前，代码批（§八 的第 2～6 批）一颗都⛔ 开工。


---

## 十三、Spike-0b：字体授权面⛔ 一手材料取证（批次 Phase C 片3，2026-10-17·只读探测，⛔ 产品码⛔ 起窗⛔ 装东西⛔ 联网）

> 尺与正本＝<c>StarMarkDesktop/bin/WI83/spike0b_83f.py</c> → <c>bin/WI83/spike0b_83f.txt</c>（80 行）。
> 这一格在 §八 那颗「未跑」名册里写的是「<b>字体授权逐族 NOTICE（§坑6）</b>」——它⛔ 需要 Node、⛔ 需要起窗，
> 纯读档就能答，所以先交掉；交完反而把 §九 决策 3 与打包批的验收口径改硬了。

### ① 族数那三档口径（§十一 ④ 那颗「9 族」⛔ 是写错，但它数的是另一档）

| 口径 | 现读 |
|---|---|
| <c>packages/excalidraw/fonts</c> 下的目录颗数 | <b>11 个</b> |
| 其中<b>带 woff2 文件</b>的族 | <b>9 个</b>＝§十一 ④ 那颗「9 族」数的就是这一档 |
| 其中<b>⛔ 一个字体文件都⛔ 带</b>、只声明本地字体的族 | <b>2 个</b>＝<c>Emoji</c>／<c>Helvetica</c> |
| woff2 复算（当场走盘，⛔ 抄 §十一） | <b>234 颗 / 12.50 MB</b> |
| <b>各族子目录</b>合计（含 10 颗 <c>index.ts</c> 与那颗 <c>.sfd</c>） | <b>245 颗 / 13.27 MB</b> |
| <b>全树</b>再加 <c>fonts</c> 根目录那 4 颗（<c>fonts.css</c>／<c>Fonts.ts</c>／<c>ExcalidrawFontFace.ts</c>／<c>index.ts</c>） | <b>249 颗 / 13.29 MB</b> |

⇒ §十一 ④ 那句「9 族／12.50 MB／234 个文件」与 §一 原句「11 族／~12.97 MB」其实<b>各说了一档</b>：
一颗数<b>带文件的族</b>、一颗数<b>目录</b>。随包清单要的是<b>第三档</b>＝<b>249 颗文件／13.29 MB</b>，
而 §九 决策 3 的代价要按那颗「分片⛔ 是一颗文件」的形状算（Xiaolai＝210 颗 / 12.20 MB（其中 209 颗是 woff2））。
⇒ 另现算一颗形状：<b>⛔ 带 <c>index.ts</c> 的族＝Assistant</b>（它那一族的描述符只出现在 <c>fonts.css</c> 那一侧，见 ⑤）。

### ② 一手授权档＝<b>0 颗</b>，11 族<b>全数</b>

现读每一族目录里名字带 <c>license</c>／<c>Licence</c>／<c>notice</c>／<c>copying</c>／<c>copyright</c>／<c>authors</c>／<c>OFL</c> 的档：
<c>Assistant、Cascadia、ComicShanns、Emoji、Excalifont、Helvetica、Liberation、Lilita、Nunito、Virgil、Xiaolai</c> 十一族<b>各 0 颗</b>。整个上游 <c>packages</c> 全树里那一族档名只命中 <b>1</b> 颗＝<c>packages/laser-pointer/LICENSE</c>，
而那颗是<b>代码</b>的 MIT；仓根 <c>LICENSE</c> 首行现读＝<c>MIT License</c>。
⇒ <b>§坑6 那句「随包分发前逐族写 NOTICE」的代价被量化了</b>：上游树里<b>⛔ 任何一份随字体带的授权文本</b>，
所以 NOTICE ⛔ 是「拷一下现成的」，而是<b>逐族去上游把一手材料取回来</b>（每族一颗出处），11 族＝11 次取证。
⇒ <b>§坑6 那几颗按族点名的许可证（"Xiaolai 是 OFL、Cascadia MIT 可再查"）在本地这棵树上⛔ 有一手材料能证</b>——
它们目前的状态是<b>未核实</b>（本仓那位读者的跨库证据闸门要求带出处，那颗列法当时⛔ 带）。
这一格⛔ 我能替他判：判的是<b>「本地这棵树里 0 份」</b>，⛔ 是「那些字体⛔ 是 OFL／MIT」。

### ③ 那两颗「⛔ 带文件的族」到底声明了什么（§坑6 欠的那一句答掉）

<c>Emoji</c> 那一族目录里只有 <b>1</b> 颗档（现读只有 <c>index.ts</c> 那一颗），而那颗 <c>index.ts</c> 导出的 <c>EmojiFontFaces</c> 只有一颗描述符，其 <c>uri</c> ＝常量 <c>LOCAL_FONT_PROTOCOL</c>，
而那颗常量现读 <c>local:</c>（<c>packages/common/src/font-metadata.ts</c>）。<c>Helvetica</c> 同一形。
⇒ <b>答案</b>：那两颗⛔ 字体字节、⛔ 网络出口，它们把 emoji／Helvetica 交给<b>用户机器上的那颗本地字体</b>。
⇒ 所以「中文 emoji 能不能画」这一格<b>⛔ 能从读码判</b>，判的是<b>装机上有没有那颗本地字体</b>——
本机⛔ 验（起窗＋字体枚举才算量到），它留在 §十 点验单里，⛔ 因为「上游是 <c>local:</c>」就写成「没问题」。

### ④ 那颗混在字体目录里的<b>非产物</b>：ComicShanns 的 FontForge 源

<c>fonts/ComicShanns/ComicShanns-Regular.sfd</c> 现读 <b>669377 字节（0.64 MB）</b>，就躺在要拷进 dist 的那颗目录里。
⇒ 打包批那句「拷 fonts」⛔ 等于「拷要用的东西」；清单要按<b>扩展名</b>圈（woff2 那 234 颗），
⛔ 整目录 rsync，否则那颗源码跟着随包多带 0.64 MB 且它⛔ 是运行时产物。

### ⑤ <c>fonts.css</c> ⛔ 是九族的总表（这一条会直接影响打包批的第一刀）

现读 <c>fonts/fonts.css</c>＝<b>4</b> 颗 <c>@font-face</c>，其 <c>src</c> 里 <c>local(</c> 命中 <b>0</b> 颗、
<c>url(</c> 命中 <b>4</b> 颗，出现的族名只有 <b>Assistant</b> 一颗。
⇒ 声明描述符的路径有<b>两条并存</b>：那颗 <c>fonts.css</c> 只写了 Assistant 一族，其余各族各自写在 <c>index.ts</c> 里（<b>而 Assistant 那一族目录⛔ 带 <c>index.ts</c></b>＝现算 Assistant）。
<b>「把那颗 css 拷过去就算带上全部字体」这一形⛔ 成立</b>——打包批那一刀的验收要按 <c>index.ts</c> 那侧的清单对颗数。

### ⑥ 就地更正：§坑6 那句「桌面仓库有 NOTICE 先例＝WordList.Dic 那一族惯例」<b>本仓读不到</b>

| 问法 | 现读 |
|---|---|
| 本仓（排除 <c>bin</c>／<c>obj</c>／<c>tools</c>）里名字带 <c>WordList</c> 的档 | <b>0 颗</b> |
| <c>src</c> 里源码提到 <c>WordList</c> 的档 | <b>0 颗</b> |
| 本仓随包的字体／词库类资产（<c>.ttf</c>／<c>.otf</c>／<c>.woff(2)</c>／<c>.dic</c>） | <b>0 颗</b> |
| 本仓 LICENSE／NOTICE 那一族档名的档 | <b>6 颗</b>：仓根 <c>LICENSE</c>（我们<b>自己代码</b>的 MIT）＋<c>StateWriteNotice.cs</c>／<c>NoticeCard.cs</c>／<c>NoticeCardWindow.cs</c>（那是<b>界面提示卡</b>＝名字撞车、语义无关）＋两档测试名（<c>CaptureNoticeWiringGateTests</c>／<c>NoticeCardGateTests</c>，同一撞车）<b>——⛔ 一颗是「随包第三方资产授权档」那一族的东西</b> |

⇒ <b>那颗「先例」⛔ 存在</b>，而 §坑6 拿它当「打包批可以直接复用惯例」的依据。形状与 <b>✅P-212 那半颗</b>
（注释承诺了一颗⛔ 存在的 UI 消费者）同一颗，只是这一颗长在<b>文档</b>那一侧；也与 <b>§坑2</b> 自己写过的那条教训
（「RSS 有 WebView2 先例」是浏览器扩展的事实混入，教训＝集成叙事先实查两仓）同族——<b>这一条我自己又踩了一遍</b>。
⇒ 现行结论：excalidraw 那颗画布的<b>逐族授权档是「新立」⛔ 是「复用」</b>；§坑6 那句先例要么改口、要么补上一颗能打开的档名＋那一行。

### ⑦ §八「未跑」名册这一格的复称

| 那一格 | 状态 |
|---|---|
| 字体授权逐族 NOTICE（§坑6） | <b>已由本腿交掉</b>＝一手材料现读 <b>0 份</b>／11 族，出处要去上游取（本腿⛔ 联网，⛔ 声称核过许可证类型） |
| ③导出出口三法的真机行为 | 仍「未跑」＝要起窗 |
| ④TTD 与 PublishLibrary 关按钮有效性 | 仍「未跑」＝要构建期 env＋起窗 |
| ⑤桥消息往返时延 | 仍「未跑」＝要起窗 |
| ②断网全字体验证 | 仍「未跑」＝要 dist（⇒ 还是挂在 Node 那颗后面） |
| Emoji 那颗目录到底是什么 | <b>已由本腿交掉</b>＝<c>local:</c>，⛔ 随包字节 |

### ⑧ 对 §九 决策 3 的影响（口径仍等他，材料先给全）

Xiaolai 随包那一颗的代价现在有三段，⛔ 只第一段是体积：
<b>①</b>210 颗 / 12.20 MB（其中 209 颗是 woff2）（形状：分片⛔ 一颗文件⇒逐文件清单与 sha256 对账都要按 209 行走）；
<b>②</b>本腿现读：那一族的授权档<b>⛔ 一手材料</b>（同其余 11 族）⇒ 随包前要多做一次「去上游取 LICENSE＋保留字体名那颗要求」的取证动作；
<b>③</b>不打＝中文手绘观感降级为系统字体（§坑7 那句照旧成立）。


---

## 十四、Spike-0c：出机流量<b>逐颗落点</b>清单（批次 Phase C 片4，2026-10-17·只读探测，⛔ 产品码⛔ 起窗⛔ 装东西⛔ 联网）

> 尺与正本＝<c>bin/WI83/netsites_83j.py</c> → <c>bin/WI83/netsites_83j.txt</c>（339 行明细）。
> §一 那颗「editor 内网络出口 <b>16 处命中</b>」与 §三 D 表都只给了<b>族</b>；§12.4 与打包批要的是<b>每一颗在哪一行、由哪颗开关管</b>。
> 这一格⛔ 需要 Node、⛔ 需要起窗⇒ 先交掉。

### ① 这把尺的射程（名册 304：发表颗数之前先说清它覆盖哪几种形状）

- 扫过 <b>415</b> 个源文件（packages/excalidraw 392 档／packages/common 23 档），扩展名 <c>.ts/.tsx/.js/.jsx/.mjs/.css</c>，⛔ 测试目录、⛔ <c>dist</c>／<c>node_modules</c>。
- 圈的是 <b>12 种形状</b>：<c>fetch(</c>／<c>XMLHttpRequest</c>／<c>EventSource</c>／<c>new WebSocket</c>／<c>navigator.sendBeacon</c>／
  <c>axios</c>／<c>window.open(</c>／<c>iframe</c>／<c>createElement("script"|"link")</c>／<c>import(</c>／<c>new Worker(</c>／任意 <c>http(s)://</c> 字面量。
- ⇒ <b>⛔ 全集</b>：那颗 <c>navigator.clipboard.read</c>、Service Worker 注册、<c>&lt;img src&gt;</c> 模板里的远程、
  <c>srcset</c>／<c>prefetch</c> 那一族都<b>在形状表之外</b>（要补就先补尺，⛔ 拿这颗数当"出机总量"）。
- 四颗<b>正件</b>（本文其它段落已实测存在过的落点）逐颗在自己那颗档里命中：
  <c>fonts/ExcalidrawFontFace.ts</c>＝esm.sh 回退、<c>subset/woff2/woff2-bindings.ts</c>＝wasm、<c>components/PublishLibrary.tsx</c>＝库后端 fetch、docs 外链那一类现读落在<c>components/App.tsx</c> 与 <c>components/HelpDialog.tsx</c>。

### ② 颗数分两档（⛔ 混着说）

| 那一档 | 现读 |
|---|---|
| 全形状命中 | <b>339 行</b>（XMLHttpRequest 3／fetch( 7／iframe 139／new Worker( 1／window.open 1／动态 import( 13／外链字符串 175） |
| 其中<b>真调用点</b>（会发请求或起子资源的） | <b>25 颗</b>＝fetch( 7／XMLHttpRequest 3／动态 import( 13／new Worker( 1／window.open 1 |
| 其中<b>真会出机</b>（<c>fetch</c>＋<c>XMLHttpRequest</c>） | <b>10 颗</b> |
| 外链字面量里出现最多的<b>前缀</b>（top 8；这把尺<b>⛔ 砍路径</b>⇒那一列是「主机＋头一段路径」，⛔ 是纯主机名） | https://github.com 35、https://api.example.com 21、https://excalidraw.com 13、http://www.w3.org 10、https://example.com 8、https://example.com">click 8、https://example.com">{el}< 8、https://developer.mozilla.org 7 |

⇒ 与 §一 那颗「16 处命中」<b>⛔ 能直接对</b>：那颗没写它的口径，而这一把写了<b>三档</b>（命中／真调用点／真出机）——
同一族的教训刚记过（坑表 #624：两颗数⛔ 相等⛔ 等于其中一颗写错，<b>缺的是口径那一列</b>）。
⇒ 一个新形状：外链字面量里带 <c>platform.openai</c> 的有 <b>4</b> 行命中（现读，按那一串出现在明细里数；这把尺⛔ 分注释与运行时代码，也⛔ 分主机与路径）；
  <b>逐颗落在注释还是运行时代码⛔ 判</b>（明细在正本里，⛔ 因为主机名像 AI 就当成已定性的出机出口）；处置仍归 §三 D 表那颗「AI 生图另立批」。

### ③ 逐颗落点（打包批与 §12.4 的验收底稿；⛔ 我能定的都标了）

| 那一族 | 逐颗落点（档:行＝形状） |
|---|---|
| 字体那族（§坑4） | `fonts/ExcalidrawFontFace.ts:92`＝fetch( |
| woff2 子集那族（§坑5） | `subset/woff2/woff2-bindings.ts:178`＝XMLHttpRequest；`subset/woff2/woff2-bindings.ts:185`＝XMLHttpRequest；`subset/woff2/woff2-bindings.ts:193`＝XMLHttpRequest；`subset/woff2/woff2-bindings.ts:930`＝fetch(；`subset/woff2/woff2-bindings.ts:989`＝fetch( |
| 库导入那族 | `data/library.ts:733`＝fetch( |
| PublishLibrary 那族 | `components/PublishLibrary.tsx:301`＝fetch( |
| TTD 那族 | `components/TTDDialog/Chat/ChatMessage.tsx:86`＝window.open；`components/TTDDialog/TTDDialog.tsx:73`＝动态 import(；`components/TTDDialog/TTDDialogInput.tsx:56`＝动态 import(；`components/TTDDialog/utils/TTDStreamFetch.ts:101`＝fetch( |
| 图片 URL 那族 | `data/blob.ts:37`＝动态 import(；`data/blob.ts:385`＝动态 import(；`data/blob.ts:387`＝动态 import(；`data/blob.ts:428`＝fetch( |
| 本地分块与 worker 那族 | `components/App.clipboard.ts:208`＝动态 import(；`data/blob.ts:37`＝动态 import(；`data/blob.ts:385`＝动态 import(；`data/blob.ts:387`＝动态 import(；`data/blob.ts:428`＝fetch(；`data/index.ts:178`＝动态 import(；`i18n.ts:101`＝动态 import(；`index.tsx:181`＝动态 import(；`subset/subset-main.ts:75`＝动态 import(；`subset/subset-main.ts:76`＝动态 import(；`subset/subset-main.ts:80`＝动态 import(；`subset/subset-main.ts:89`＝动态 import(；`workers.ts:9`＝new Worker( |

### ④ 这一把读出来的三颗<b>新事实</b>（改的是设计⛔ 是文案）

1. <b>woff2 那颗有两条路并存</b>：<c>Module.wasmBinary</c>（§十一 ⑤ 现读 :340/:341）之外，<b>另有</b>
   <c>fetch(wasmBinaryFile, {credentials:"same-origin"})</c> 两颗（:930/:989）与三颗 <c>XMLHttpRequest</c>（:178/:185/:193）——
   ⇒ §12.4 那句"宿主直接喂字节消掉一半不确定"要加一颗<b>硬验收</b>：喂了 <c>wasmBinary</c> 之后那<b>五颗真的⛔ 走</b>
   （不验＝断网下仍然报红，而我们以为修好了）。
2. <b>库导入那一族自己已经带了一颗 hostname 白名单</b>：<c>data/library.ts:510</c> 造 <c>allowedUrl</c>、<c>:514</c> 取
   <c>{hostname, pathname}</c> 比对 ⇒ §12.1 那句「args 走字段白名单」在他仓<b>已有同形状的现成机制</b>，
   桥层与它<b>共一份⛔ 各写一份</b>（一个主人那条纪律）。这颗⛔ 我替他定，但技术裁决＝复用。
3. <b>那颗 worker 的 URL 是形参递进来的</b>（<c>workers.ts:8/9</c>，调用方给）⇒ 本腿只判到「由调用方决定」，
   <b>⛔ 能写成"随包本地"</b>：留「未判定」，与 §七 那颗"默认关零资源"一起验。

### ⑤ 这一格交完之后剩下什么

§八「未跑」名册里那一行现在只剩<b>四格</b>（导出出口真机行为／TTD 与 PublishLibrary 关按钮有效性／桥消息往返时延／断网全字体验证），
共同前提＝<b>先有 dist、再起真窗</b>⇒ 全部挂在 §九 决策 1 与那颗「装 Node」的外部动作后面（12.6 第 ① 颗）。
本节⛔ 动产品码、⛔ 起窗、⛔ 装东西、⛔ 联网。


---

## 十五、Spike-0d：WebView2 那一侧「要不要装东西」的现读（Phase C 片5，2026-10-17·⛔ 产品码⛔ 建工程⛔ 装工具链⛔ 联网）

> 这一腿查的是<b>我自己没问过的一句前提</b>。§十一 ③ 现读「本仓 csproj 含 WebView2 的档数＝0 」，
> 我顺着把它写成了「整合要新引一颗 NuGet 包＋一次联网还原」，还把那件事跟「装 Node」一起挂到 §12.6 那颗要他点头的格上。
> 现读结论：<b>那一半⛔ 成立</b>——那颗包早就在还原图里（那棵 <c>MicrosoftWindowsAppSDKPackageDir=在</c>
> 的开关就是原因），而<b>项目今天的求值结果就带着那颗显式引用</b>。
> 尺与正本＝<c>bin/WI83/probe_83n.py</c>（更正 <c>fix_83n.py</c>）＋<c>probe_83v.py</c>＋<c>probe_83u.py</c>
> → <c>bin/WI83/spike0d_83n.txt</c>（47 行）＋<c>bin/WI83/arm_83u.txt</c>。

### 15.1 依赖那一侧：⛔ 欠新包、⛔ 欠网、⛔ 欠 csproj 改动

| 事项 | 现读 | 这一条为什么算钉死 |
|---|---|---|
| 那颗包在还原图里的位置 | <b>⛔ 是（transitive）</b>（由 <c>Microsoft.WindowsAppSDK 2.4.0</c> 带进来） | 问的是 <c>project.assets.json</c> 里 <c>project.frameworks.libraries</c> 那一族，⛔ 看包名出现在哪几档里 |
| 项目求值出的显式 <c>Reference</c> | <b>1 条</b>＝Microsoft.Web.WebView2.Core.Projection.dll（在 net8.0-windows10.0.17763.0 那一档） | <c>dotnet msbuild -getItem:Reference</c>——问的是<b>这颗项目当下真拿去编译的引用清单</b> |
| 包内那三颗分支开关 | <c>EnableCsWinRTProjection=true</c>｜<c>NeverCopyLoaderDllToOutputDirectory=true</c>｜<c>LoaderPreference=Dynamic</c> | 取的是<b>求值后的实际值</b>；那段 <c>build/Common.targets</c> 的 XML 我只是读过，⛔ 拿它当读数 |
| 发布树与暂存树那一族 | publish＝3：Microsoft.Web.WebView2.Core.Projection.dll(1470464 B), Microsoft.Web.WebView2.Core.dll(793704 B), WebView2Loader.dll(160368 B)；stage/1.0.4＝3：Microsoft.Web.WebView2.Core.Projection.dll(1470464 B), Microsoft.Web.WebView2.Core.dll(793704 B), WebView2Loader.dll(160368 B) | <b>成对判据</b>：两棵树各自现数；另 <c>deps.json</c> 两份也各自解析⇒相等（2 条） |
| 本机原生资产 | native=WebView2Loader.dll; build=Microsoft.Web.WebView2.targets | RID 图里那一条就是那颗 Loader 的来路（另：求值三档条目现读＝0：(空)；1：Microsoft.Web.WebView2.Core.dll；0：(空)） |
| 三颗文件与包内那颗<b>内容同源</b>？ | <b>2 颗同源、1 颗⛔</b>（不等的那颗：Microsoft.Web.WebView2.Core.Projection.dll）｜逐颗：Microsoft.Web.WebView2.Core.Projection.dll＝sha256 前 16 位 不等：包=c0036e5306aa9758 发布=813f20228d59fb3c；Microsoft.Web.WebView2.Core.dll＝sha256 前 16 位 相等 17ccd9141c486d7a；WebView2Loader.dll＝sha256 前 16 位 相等 ae69309c5e356350 | 逐颗 sha256 对拍。⚠ <b>那颗投影档发布树里≠包内</b>⇒它是构建期那一条链的产物：<b>编译引用的是包内那份、跟着安装包走的是另一份字节</b>；哪条 target 生成它⛔ 钉，留 15.4 那一格 |
| 打包侧要不要动 | <b>⛔ 要</b>：<c>.iss</c> 的 <c>[Files]</c> 只有 1 条，那一行是<b>整棵递归不过滤</b>（是）；<c>update-manifest</c> 里那一族已列 3：Microsoft.Web.WebView2.Core.Projection.dll, Microsoft.Web.WebView2.Core.dll, WebView2Loader.dll | 逐颗过滤＝当年那颗「不装 <c>Updater</c> 目录」的雷；它整棵搬⇒新依赖自动进包、自动进清单 |
| 今天有没有人在用它 | csproj 0  颗／分母 38 颗；src 的 <c>.cs</c>＋<c>.xaml</c> 里 <c>WebView2</c> 命中 <b>0</b> | ⛔ 包在≠已在用：第一颗代码批仍是从零接线，只是<b>⛔ 欠装东西</b> |

### 15.2 编译那一半：Roslyn⛔ 止于「有没有」，还判出「挂在哪一层、叫什么名」

§十一 ⑦ 那几处计数来自缓存包里的 <c>lib/net462/Microsoft.Web.WebView2.Core.xml</c>（现读：PostWebMessageAsJson=3 / WebMessageReceived=2 / DownloadStarting=1 / Downloading=0 / ProcessFailed=1 / CoreWebView2Environment=1 / AreDevToolsEnabled=1）。
而我们<b>真编进去的那颗是投影档</b>——那一档里只有 1：Microsoft.Web.WebView2.Core.Projection.dll，<c>.xml</c> 文档 <b>0</b> 颗
（<c>lib_manual</c> 四档合计 4 颗，那是<b>另外那几档</b>的包装器文档）。
<b>「文档里有那句话」⛔ 等于「我们的码调得到那颗成员」</b>，所以这一腿补的是真编译：六臂各问一颗事，
每臂往真项目落一颗⛔ 提交的临时档、编完当场删，<b>六臂全跑完再逐臂判</b>（判据＝臂名↔该红的颗名）。

| 臂 | 那一臂问的 | 现读 |
|---|---|---|
| <b>A 正件</b> | §12.1 那四颗（<c>PostWebMessageAsJson</c>／<c>WebMessageReceived</c>／<c>DownloadStarting</c>／<c>ProcessFailed</c>）＋<c>NavigateToString</c> <b>只绑与只调</b> | exit=0｜succeeded=是｜红名=(无)｜错误码=(无) |
| <b>B1 正件</b> | 两颗功能位走 <c>cw.Settings.*</c>（<c>IsStatusBarEnabled</c>／<c>AreDevToolsEnabled</c>） | exit=0｜succeeded=是｜红名=(无)｜错误码=(无) |
| <b>B2 普查</b> | 候选层级一起编、按点名逐颗判（含一颗⛔ 存在的对照名） | exit=1｜succeeded=⛔｜红名=AreStatusBarEnabled,DefaultBackgroundColor,Z8NoSuchMemberZ8｜错误码=CS1061 |
| <b>C 对照</b> | 同一颗功能位<b>直接挂</b> <c>cw.*</c>（我原本会顺手写的那一形） | exit=1｜succeeded=⛔｜红名=IsStatusBarEnabled｜错误码=CS1061 |
| <b>D 正件</b> | 事件参数用<b>投影档的叫法</b>（<c>e.WebMessageAsJson</c>／<c>e.ProcessFailedKind</c>） | exit=0｜succeeded=是｜红名=(无)｜错误码=(无) |
| <b>E 对照</b> | 同一颗参数用<b>包装器的叫法</b>（<c>e.Kind</c>）＝第一版把我撞红的那一形 | exit=1｜succeeded=⛔｜红名=Kind｜错误码=CS1061 |

第一次那一臂把八颗一起编时，逐颗判定＝PostWebMessageAsJson=在 / WebMessageReceived=在 / DownloadStarting=在 / ProcessFailed=在 / IsStatusBarEnabled=⛔ 在 / AreDevToolsEnabled=⛔ 在 / DefaultBackgroundColor=⛔ 在 / NavigateToString=在；型名那一半＝0 ＝CoreWebView2 那颗型认得。
六臂跑完的残留核对：csproj 分母 38｜src 里 WebView2 命中 0｜git status 干净｜臂数 6/6。

<b>由此定下三条写码口径</b>（技术裁决，归我）：
① <b>功能位一律走 <c>cw.Settings.*</c></b>——§12.5 那行「C#→JS 三颗值（主题／语言／功能位）」里那颗「功能位」
   以后落到 <c>Settings</c>，⛔ 落 <c>CoreWebView2</c> 本体（C 臂现读编不过）；§12.1 的桥那四颗<b>挂本体</b>成立（A 臂）。
② <b>那颗默认背景色⛔ 在 <c>Settings</c> 上，在 <c>CoreWebView2Controller</c> 上</b>——B2 普查现读：同一颗名在
   <c>Settings</c> 那一形被点名、在 <c>Controller</c> 那一形没被点名（同臂里 <c>IsVisible</c>／<c>Bounds</c> 也⛔ 被点名）。
   ⚠ 顺带纠正我自己一处手滑：那一族里<b>只有一颗</b>叫 <c>IsStatusBarEnabled</c>，<c>AreStatusBarEnabled</c> ⛔ 存在
   （B2 点名了它）⇒⛔ 按「Are／Is 前缀随手挑」。
③ <b>事件参数的名字按投影档念</b>——崩溃分档那一颗叫 <c>ProcessFailedKind</c>，⛔ <c>Kind</c>（E 臂现读编不过）。
   ⛔ 照 <c>lib/net462</c> 那份包装器文档抄那一族的名字；§12.4 落码时逐颗按 D 臂那一形写。

### 15.3 三处更正（都改我自己写下的那句）

1. <b>§八 与 §12.6 那颗「外部动作」的射程缩小</b>：要他点头的只剩「装 Node＋<c>npm install</c>」<b>这一件</b>；
   「新引 NuGet 包／联网还原／改产树与安装器」那三件按现读<b>⛔ 成立</b>⇒第一颗代码批的前置少三件事。
2. <b>§十一 ⑦ 那几处计数的归属改口</b>：那是 <c>net462</c> 包装器的文档计数，⛔ 是我们编进去那颗投影档的计数
   （投影档⛔ 带文档）；正件由 15.2 的真编译补上，<b>⛔ 删 §十一 那一行</b>（它是当时的如实读数，本节是它的补）。
3. <b>§12.5 那颗「功能位」的挂点与 §12.4 那颗「崩溃分档」的叫法</b>：现读要求走 <c>Settings</c>／<c>Controller</c>
   与 <c>ProcessFailedKind</c>（15.2 末那三条）——这两条在 §十二 的原文里没写出来；落地那一批按本节口径写，
   <b>⛔ 回头改 §十二 的表</b>（那节状态仍是<b>待评审</b>，改它要先过评审；本节只登记口径）。

### 15.4 仍欠（⛔ 这一腿能判的）

- <b>那颗投影档为什么与包内⛔ 同源</b>（哪条 target 生成、生成的那颗与引用那颗的版本怎么对账）——只读那三档条目读不到，
  要 <c>-getItem</c> 扩到 <c>CsWinRT</c> 那一族或读构建日志全文；<b>⛔ 影响「要不要动打包」那一半结论</b>
  （文件已在树里、已在清单里），但<b>影响版本核对</b>⇒留一格未跑。
- <b>真起窗那一半照旧欠</b>：<c>CoreWebView2Environment.CreateAsync</c>／<c>EnsureCoreWebView2Async</c> 的<b>实际形状</b>与时延、
  §十 那颗「导出出口三法」、断网全字体验证——都要有 <c>dist</c>＋真窗，挂在 §九 决策 1 与那颗「装 Node」后面。
- <b>⛔ 把「编得过」念成「行为对」</b>：15.2 只判<b>可编</b>；投影档与 <c>net462</c> 包装器在同一颗宿主上的<b>行为差异</b>
  （事件时序、<c>WebMessageAsJson</c> 的空值形状）⛔ 照片能定，也⛔ 这段码能定。
- <b>版本对齐</b>：现读那颗 <c>1.0.3719.77</c> 是 <c>WindowsAppSDK 2.4.0</c> 钉下来的；想要更新的 SDK 特性就<b>要联网</b>
  （缓存里只那一颗与还原图对齐）——这是<b>将来的</b>动作，⛔ 本批的前置。
- <b>一颗⛔ 动的前提</b>：本节「⛔ 改打包」的结论建立在 <c>WindowsAppSDKSelfContained=true</c> 与
  「安装器整棵递归不过滤」这两颗现读上；哪天改成非自包含打包，那颗 Loader 与 Runtime 的配对要<b>重新量</b>。

> 这一腿写进仓里的只有 <c>docs</c> 这一节与 <c>bin/WI83</c> 那几份正本（临时臂档当场删，见 15.2 末那一行）。
> <b>src／UiStrings 表／测试／账本一字未动</b>；§十二 那颗<b>待评审</b>状态⛔ 变，过评审前代码批仍一颗⛔ 开工。

## 十六、Spike-0e：第一份 dist 到底“要不要装东西”——本机运行时＋那颗发布档的现读（批次 Phase C 片6，2026-10-17·只读探测＋一次 <c>npm pack</c>，⛔ 产品码⛔ 建工程⛔ 装工具链⛔ 起窗）

> 尺与正本＝<c>bin/WI83/probe_83ab.py</c> → <c>bin/WI83/spike0e_83ab.txt</c>（41 行／每行三列）。
> 这一腿撞掉的是<b>我自己前两节写下的那颗前提</b>：§十一 ③ 那句「<c>packages/excalidraw/dist</c> ⛔ 不存在⇒选 A 就得先在<b>别处</b>打一次，或者本机装 Node 走 B」，与 §12.6 ① 那句「装 Node＋<c>npm install</c>（拉几百颗包）才能产出第一份 dist……<b>等他点头</b>」。

### 16.1 本机那一侧：那颗“要装 Node”的前提⛔ 成立

| 现读 | 数 |
|---|---|
| PATH 里 <c>node</c>（在不在／版本） | 在（那颗 exe 在 D 盘）／<b>v24.18.0</b> |
| PATH 里 <c>npm</c>（在不在／版本） | 在（那颗 exe 在 D 盘）／<b>11.7.0</b> |
| PATH 里 <c>npx</c>／<c>corepack</c>／<c>yarn</c> | 在（那颗 exe 在 D 盘）／在（那颗 exe 在 D 盘）／⛔ 在 |
| 那颗 npm 指的 registry 主机 | <b>registry.npmmirror.com</b>（⇒本机 npm 早已配成国内镜像，⛔ 我的假设） |

⇒ §12.6 ① 那颗「要不要装、装到 D 盘哪一处」是<b>我问错的问题</b>：机器上<b>早就有</b>，而它<b>本来就在 D 盘</b>（他给的那颗硬约束已经被满足了）。
⇒ 真正欠的从来⛔ 是「Node」这一颗，而是「一次 <c>npm pack</c>／一次打包」这件<b>会联网、会落盘</b>的动作。
  本腿只做了前者（产物＝一颗 tgz，落在⛔ 随仓的取证目录里，<c>bin/</c> 整棵在 <c>.gitignore</c> 内⇒<b>仓⛔ 涨</b>）；打包那一颗<b>留给下一腿</b>并先写明代价（§16.4）。

### 16.2 那颗发布档<b>自带已编译的 dist</b>（§十一 ③ 就地更正）

| 现读 | 数 |
|---|---|
| <c>npm pack</c> 那颗 tgz 的字节／sha256 前缀 | <b>31234118</b>／0f2851674434336f |
| registry 报的 <c>dist.integrity</c> 前缀 | sha512-QkIiS+5qdy8lmDWTK |
| 解出来的整棵树 | <b>1029 颗／46802737 字节</b> |
| <c>dist/prod</c>／<c>dist/dev</c>／<c>dist/types</c> 各多少 | prod 300 颗／17692829 字节｜dev 366 颗／27964931 字节｜types 361 颗／1139919 字节 |
| <c>dist/prod</c> 根上的文件（10 颗） | chunk-6U3AYISY.js chunk-EIO257PC.js chunk-FX7ZIABN.js chunk-SQ5PDB2P.js chunk-SRAX5OIU.js chunk-Z3N5DIM6.js index.css index.js subset-shared.chunk.js subset-worker.chunk.js |
| 其中按 <c>chunk-</c>／<c>subset-</c> 名型圈的颗数 | <b>8</b> 颗 |
| <c>dist/prod/fonts</c>／<c>locales</c>／<c>data</c> 各多少 | fonts 234 颗／13107068 字节｜locales 55 颗／1648766 字节｜data 1 颗／189 字节 |
| <b>成对判据</b>：树字节合计 ↔ registry 的 <c>unpackedSize</c> | <b>46802737 ↔ 46802737｜相等</b> |

⇒ §十一 ③ 那颗「<c>dist</c> ⛔ 不存在」<b>数是对的，结论是错的</b>：仓外那棵 <c>excalidraw</c> 树确实没打过 dist，但<b>发布到 npm 的那一颗带着 dist</b>。走 A 的前置动作从「装工具链＋几百颗包」缩成「<b>拉一颗 tgz</b>」。
⇒ 那颗成对判据是本节的骨头：<b>两把独立尺</b>（我自己 walk 出来的字节合计 vs registry 报的 <c>unpackedSize</c>）对同一颗包读数相等，才许发「这颗包我读全了」这句话（名册 <b>327</b> 的方向：⛔ 只在自己那一侧数得动就当全集）。

### 16.3 包 ↔ 树同源：两把尺一起量（⛔ 拿“颗数相等”当证据）

- 仓外那棵树的 <c>fonts</c>＝<b>249 颗／13935163 字节</b>｜包里 <c>dist/prod/fonts</c>＝<b>234 颗／13107068 字节</b>；
- 第一把（名字）：交集 <b>234 颗</b>｜只在包里 <b>0：∅</b>｜只在树里 <b>15 颗</b>；
- 换种问法再数一遍：那 15 颗按扩展名分档＝<b>.ts=13 .sfd=1 .css=1</b>⇒多出来的<b>全是源件</b>（各家族那颗 <c>index.ts</c>＋那颗 <c>.sfd</c> 源件＋<c>fonts.css</c>），<b>⛔ 一颗产物</b>（这颗接住 §十三 ④ 那颗混在字体目录里的 FontForge 源）；
- 第二把（内容）：交集里逐颗 <c>sha256</c> 相等＝<b>234／234 颗</b>。

⇒ 发布的那一颗<b>就是这棵树编出来的</b>（字体面 234 颗逐颗同字节），§坑16 那颗“上游版本漂移”在<b>字体</b>这一档上⛔ 成立；钉版本仍要<b>两颗一起钉</b>：commit <c>ed10ac7d</c>＋那颗 <c>0.18.0</c> 的 <c>dist.integrity</c>（颗数相等⛔ 算同源＝名册 326 要防的那类）。

### 16.4 但 <c>dist/prod/index.js</c> ⛔ 是“浏览器直接能吃的东西”——这一节才是本腿的结论

| 现读 | 数 |
|---|---|
| <c>dist/prod/index.js</c> 的字节 | <b>502071</b> |
| 那颗档里<b>裸引用</b>（bare specifier）的颗数 | <b>16</b> |
| 那 16 颗各是什么 | @excalidraw/laser-pointer @radix-ui/react-popover @radix-ui/react-tabs clsx fuzzy jotai jotai-scope lodash.debounce lodash.throttle nanoid open-color react react-dom react/jsx-runtime roughjs/bin/rough tunnel-rat |
| <c>peerDependencies</c> | react=^17.0.2 || ^18.2.0 || ^19.0.0 react-dom=^17.0.2 || ^18.2.0 || ^19.0.0（⇒react／react-dom <b>⛔ 打在那颗 dist 里</b>） |
| <c>index.css</c> 里 <c>url(</c> 命中／其中带 scheme 的 | <b>11</b>／<b>0</b> |
| <c>dist/prod</c> 那些 js 里含 <c>http(s)</c> 的行数 | 13（按 §十四 口径：静态命中<b>⛔ 等于</b>出机） |

⇒ 三条要紧的：
① 走 A 还差一步，而那一步<b>⛔ 是“打 dist”</b>（dist 已在），是「<b>把 16 颗裸引用变成浏览器能加载的东西</b>」——§十一 ③ 与 §12.6 ① 都把这一步混在“装 Node”里说了，措辞把两件不同的事说成一件事；
② 那颗 css 里 <b>带 scheme 的 url＝0</b> ⇒ 包内那一份样式表的字体引用<b>全是相对路径</b>。这颗比 §十三 的取证更强一档：那是<b>产物</b>侧的直接证据，而 §坑4/5 要治的是<b>源码里那两条远程回退</b>——两回事，别混（§十四 ④ 已经点过一次这个口径）。
③ 两条出路：<b>(i)</b> 一颗打包器（<c>esbuild</c> 那一类）跑<b>一次</b>⇒产出一颗自足 bundle＋随它拷 <c>fonts</c>/<c>locales</c>；<b>(ii)</b> 手写 import map＋逐颗 ESM 化（⛔ 装东西，但那 16 颗里 CJS-only 的各自要垫片，且⛔ 可复算）。
  <b>技术裁决＝(i)</b>（理由：产物只一颗文件、可逐颗 sha256 冻结、在<b>⛔ 装 Node 的用户机</b>上照跑；代价＝一次性打包腿＋冻结清单要多记两颗：那颗打包器的版本与 sha256）。按“产品要按复杂环境设计”那条纪律，<b>那颗打包器⛔ 进仓、⛔ 进产品构建链</b>（它只在取证目录里跑一次，产物进仓＝§九 决策 1 的 A 那一半照旧成立）。

### 16.5 本腿⛔ 判到的（把射程写出来，也⛔ 让缺格从表里消失）

- **那 16 颗裸引用各自是不是浏览器能直接吃的 ESM**＝未判定（本腿只读那一侧，⛔ 逐颗拉包）
- **打完一颗 bundle 之后的字节**＝未跑（本腿⛔ 装打包器、⛔ 跑 bundle）
- **空板起窗后的四格（导出 3 法／关按钮有效性／桥时延／断网全字体）**＝未跑（欠那颗 bundle＋宿主）
- §十五 那颗「投影档为什么与包内⛔ 同源」还挂着（与本腿无关，但同一张欠账表）。

### 16.6 本腿自己两处瞎（收口腿进坑表）

- <b>那颗 <c>tar tvzf</c> 的列位在 bash 里读对、在 python 的 <c>subprocess</c> 里整列读成 0</b>⇒我一度把“字节合计＝0”读成“那颗包里⛔ 带字节”，
  而<b>同一腿的 fonts 行已经报了 234 颗／13107068 字节</b>——一句话自己打自己（名册 <b>323</b> 那一类）却没被读回圈喊住：那颗尺只查“列数＝3”，⛔ 查“读数自洽”。
  ⇒ 换尺＝<b>走解压后的树</b>（<c>os.walk</c>＋逐颗 <c>getsize</c>），并补一颗<b>成对判据</b>：自己合计 ↔ registry 的 <c>unpackedSize</c>（现读 相等）；
- <b><c>npm view --json dist.integrity</c> 回的是<b>点号形式的键</b></b>（字符串 <c>"dist.integrity"</c>），我按 <c>meta["dist"]["integrity"]</c> 取⇒
  读出<b>空串</b>，而<b>同一腿的 <c>unpackedSize</c> 也一起读成 <c>None</c></b>——那颗<b>⛔ 报错，只是静默空</b>。
  ⇒ 新尺的正件＝“读出来必须与另一颗独立来源<b>相等</b>”，⛔“没抛异常就算对”（名册 <b>289／311</b> 那一族第五次撞）。

### 16.7 §八 批形与 §九 决策 1 的现在位置

- §八 0（Spike 批）里「⛔ 起窗就能答」的那几格又交掉一颗：<b>“第一份 dist 从哪来”现在有了带出处的答案</b>（拉一颗 tgz，⛔ 打一棵 monorepo）。
- §九 决策 1 由我<b>技术裁决＝A′</b>＝「<b>产物随仓＋一次性打包腿，构建期⛔ Node</b>」；那颗打包器与它落的目录都<b>⛔ 进仓</b>。
- 还剩他点头的只有两件观感／口径题：<b>§九 决策 3</b>（Xiaolai 随包＝现读 209 个分片／12.08 MB，那颗数来自 §12.6 ②）与 <b>§九 决策 4</b>（AI 生图要不要走自带 Key）。
- 下一腿＝<b>Spike-1a 打包腿</b>：跑一次打包器产出 bundle＋逐文件冻结清单（版本／sha256／来源 commit），并当场判掉 §16.5 那颗“那 16 颗各自是不是 ESM”。

## 十七、Spike-1a：那一次打包真跑了——三臂的字节、65 颗导出面、和「摘哪一颗才真的摘掉」（批次 Phase C 片7，2026-10-17·一次性打包＋只读取数，⛔ 产品码⛔ 建工程⛔ 起窗⛔ 改系统 PATH⛔ 表⛔ 账本）

> 尺与正本＝<c>bin/WI83/probe_83ae.py</c> → <c>bin/WI83/spike1a_83ae.txt</c>（59 行／每行三列）。
> §十六 那颗技术裁决（<b>A′＝一次性打包腿＋产物随仓、构建期⛔ Node</b>）在这一腿<b>第一次真跑了一遍</b>；这一节交的是「跑出来是什么样」的数字，⛔ 是「能跑」这句断言。

### 17.1 「跑一遍」的形状（写成可复算的三行，⛔ 系统级动作）

| 现读 | 数 |
|---|---|
| 钉住的版本（四颗） | <b>@excalidraw/excalidraw=0.18.0 esbuild=0.25.12 react=18.3.1 react-dom=18.3.1</b> |
| 那颗 <c>node_modules</c> 顶层档数 | <b>171</b> 档 |
| 那颗打包器自报的版本 | 0.25.12 |

⇒ 三行命令形状＝① <c>npm install</c> 那四颗钉住的版本 ② <c>esbuild entry.js --bundle --minify --format=iife --define:process.env.NODE_ENV</c> ③（可选那一臂）<c>--alias:@excalidraw/mermaid-to-excalidraw=./stub.js</c>。
⇒ 那颗 <c>--define</c> ⛔ 是装饰：react 读那颗环境变量，⛔ 给就编出一颗开发态的、给了才对（这一条要进未来那颗构建脚本，⛔ 记成「顺手加的」）。
⇒ 落盘全在<b>⛔ 随仓</b>的取证目录（<c>bin/</c> 整棵在 <c>.gitignore</c> 内⇒<b>仓⛔ 涨</b>）；⛔ 全局装、⛔ 改 PATH、⛔ 动那棵 <c>excalidraw</c> 树本身。

### 17.2 三臂的字节，与「还有没有没打进去的引用」——两把尺⛔ 一致

| 臂 | 产物字节 | metafile 的 inputs 档数 | metafile 的 <c>outputs.imports</c> | 我这把正则念出的「裸引用」 |
|---|---|---|---|---|
| iife | 14395067 | 1815 | <b>0</b> | 4 |
| iife＋minify（这一颗是要留的那一臂） | <b>8376525</b> | 1815 | <b>0</b> | 3 |
| iife＋minify＋摘 mermaid | <b>4878693</b> | 233 | <b>0</b> | 2 |

⇒ 权威那颗＝<b>打包器自己报的 <c>outputs.imports</c>＝0 颗</b>（三臂都是 0）；我这把正则那颗（3 颗）是把<b>字符串里的词</b>念成了语句（pica 那句 debug、katex 那句注释）⇒ 这一列只当<b>对照件</b>，⛔ 当结论（名册 296 那一族：bundle 既然自足，我这把尺也该报 0 才对；它现读报 3 颗，正是「我在量错的东西」那颗信号）。
⇒ §十六 那句「把 16 颗裸引用变成浏览器能加载的东西」<b>在这一腿做到了</b>：那颗 bundle 自足。

### 17.3 摘掉 mermaid 那一臂：省 41.8%，但「⛔ import 就⛔ 带」这一句只有一半成立

- 字节 8376525 ↔ 4878693（差 <b>3 497 832</b> 颗字节＝<b>41.8%</b>）；解析集 inputs <b>1815</b> 档 ↔ <b>233</b> 档；
- 那颗产物里几颗串现读：<c>DiagramToCodePlugin</c>=<b>0</b> 颗／<c>TTDDialog</c>=<b>9</b> 颗／<c>mermaid</c>=<b>230</b> 颗／<c>WelcomeScreen</c>=<b>28</b> 颗。

⇒ 三件事：<b>①</b> tree-shaking 真会带走<b>⛔ 被我 import 的具名导出</b>（那颗 <c>DiagramToCodePlugin</c> 现读 0 颗＝正件）；<b>②</b> 但 <c>TTDDialog</c>／<c>mermaid</c>／<c>WelcomeScreen</c> 坐在 <c>Excalidraw</c> 组件<b>自己的图</b>里⇒「不接就行」这句⛔ 成立，要摘就得<b>alias/stub 那一臂</b>（现读省 3 497 832 颗字节）或运行时关；<b>③</b> 那颗 stub 臂的<b>观感代价</b>（mermaid→画板还能不能进）本腿⛔ 判（见 §17.7）。
⇒ 这颗直接改写 §九 决策 4 的形状：那颗「AI 生图（TTD）要不要带」<b>⛔ 是一个「接不接线」的决定，而是「省不省 41.8% 的包」＋「默认 Key 不配时那颗按钮是不是死按钮」两颗一起定。

### 17.4 出机面：bundle 里那些 URL 的点数（口径先说清，⛔ 让「0」替我说话）

| 现读（minify 那一臂） | 数 |
|---|---|
| <c>http(s)</c> 串的<b>颗数</b> | <b>154</b> 颗（⛔ 按行计：那颗档一行≈半档） |
| 主机名去重后的颗数 | 36 颗 |
| 其中 <c>www.w3.org</c> 那一族 | <b>89</b> 颗＝SVG/MathML 的 <c>xmlns</c> 串⇒<b>⛔ 出机</b> |
| 带真取数形状的那几颗分档 | embeddable-srcdoc=5 xmlns-in-data=3 其它:.src==1 |
| <c>srcdoc</c> 那一族点到的主机 | embed.reddit.com gist.github.com platform.twitter.com reddit.com twitter.com |
| 模板里那颗 <c>script async src</c> | <b>4</b> 颗 |
⇒ <b>这一族⛔ 被打包摘掉</b>：同一颗产物里 <c>twitter.com</c> 现读 <b>4</b> 颗、<c>libraries.excalidraw.com</c> <b>2</b> 颗还在。
⇒ §十 点验单那颗「旧 embeddable 场景打开：iframe ⛔ 加载远程（占位＋警告文案）」从此有了一颗<b>具体拦点</b>：只能拦在<b>宿主那一侧</b>（<c>NewWindowRequested</c>／<c>WebResourceRequested</c>，或在桥里当场拒那颗元素类型）——<b>⛔ 能靠「我们这份包⛔ 有远程 URL」这句话过关</b>，那句话现读⛔ 成立（§十四 那句「静态命中⛔ 等于出机」在这一臂再次兑现）。

### 17.5 那颗入口的导出面＝桥协议与导出批的底稿

- 具名导出 <b>65</b> 颗、<b>⛔ 有 default</b>（现读：读不到 default）；
- 那颗<b>对照臂</b>（<c>import Excalidraw from …</c> ＋ <c>convertToBlob</c>）<b>红成预期（rc=1，报错点了那颗缺的导出名）</b>⇒一处把「default 那颗写法」与「那颗导出的真名」两件事同时钉住：0.18 这一发读不到 <c>convertToBlob</c>，它叫 <b><c>exportToBlob</c></b>。<b>⚠ 它以前叫什么、哪一发改的名，本腿⛔ 判</b>（⛔ 把「我按记忆写错了」写成「上游漂移」）。
- 出口那一族现读名字＝<c>bumpVersion exportToBlob exportToCanvas exportToClipboard exportToSvg loadFromBlob serializeAsJSON serializeLibraryAsJSON</c> ⇒ §八 0 ③「导出／下载出口 3 法各试」那一格的名字从此⛔ 是猜的；
- 按钮／插件那一族现读＝<c>DiagramToCodePlugin Footer MainMenu Sidebar TTDDialog TTDDialogTrigger WelcomeScreen</c> ⇒ §12.5 那张「摘哪几颗」的名单从此有出处（那颗 Sidebar 与 WelcomeScreen 都在这颗清单里）。

### 17.6 随包那一档的代价与冻结那三颗（P-209 那条教训在这里落地）

- 打包只管那颗 js；那颗 <c>index.css</c> 按<b>相对路径</b>点字体⇒要一起拷 <c>fonts</c>（现读 <b>234</b> 颗／<b>13 107 068</b> 字节）与 <c>index.css</c>（<b>144 689</b> 字节）；
- 现读三档当场相加（⛔ 手打）：<b>21 628 282</b> 字节（≈20.6 MB）＝带 mermaid 那一臂；摘掉那颗＝<b>18 130 450</b> 字节（≈17.3 MB）；
- 冻结三颗：产物 <c>spike1.min.js</c> 的 sha256 <b>e361972f8487c6f0e33077147c43fc39b16014e43c0f208612f1ac37501f674a</b>；来源 <c>@excalidraw/excalidraw 0.18.0</c> 的 <c>dist.integrity</c> 前缀 <b>sha512-QkIiS+5qdy8lmDWTK</b>；工具 esbuild <b>0.25.12</b>。
  再加一颗身份＝仓外那棵树的 commit <c>ed10ac7d</c>（§十一 ② 现读）。⇒ ⛔ 拿「以版本命名的目录」当内容证据，<b>逐颗 sha256 才算</b>。
⇒ 与 §九 决策 3 并排看：那颗 Xiaolai 随包的代价是 209 个分片／12.08 MB（§十二 ② 现读），而这一节<b>整套字体</b>才是那颗大头（现读 13 107 068 字节）⇒「只带／不带 Xiaolai」的比价口径要从这里改口（⛔ 我把「字体⛔ 全带」当成了「⛔ 多几 MB」）。

### 17.7 本腿⛔ 判到的（照抄正本 ⑥ 那三行）＋下一腿

- 未跑（欠那颗宿主窗）
- 未判定（本腿只量字节，⛔ 问功能）
- 未判定（本腿⛔ 试 --loader:.css=text 那一类改法）
- 下一腿＝<b>Spike-1b 起窗腿</b>：⛔ 产品码，先在取证目录里起一颗<b>一次性</b>宿主窗（<c>WebView2</c> 由 §十五 那颗 transitive 依赖带着，⛔ 新引包），量 §八 0 的 ②③④⑤ 那四格＋§17.4 那颗拦截点的可行性。
- 本腿自己一处瞎（收口腿进坑表）：第一版把「http 那颗数」按<b>行</b>计，而那颗产物的形状现读 <b>254990 行／最长 969606 字符</b>、minify 那一臂 <b>3091 行／最长 1090864 字符</b>⇒同一份内容的<b>颗数 ↔ 行数</b>＝163 颗 ↔ 151 行｜差 12 与 154 颗 ↔ 52 行｜差 102，两臂之间根本⛔ 可比；⇒ 列名跟着尺走（名册 322 那一类），口径写进那颗行的名字里（这一版已改成<b>颗数</b>＋一句「⛔ 按行计」）。


## 十八、Spike-1b：那颗宿主窗真起过五回——字体那颗 CDN、4 MB 那堵墙、和「⛔ 焦点时剪贴板会吊住」（批次 Phase C 片8，2026-10-17·一次性起窗＋只读取数，⛔ 产品码⛔ 建工程⛔ 改系统 PATH⛔ 拔网⛔ 装工具链⛔ 表⛔ 账本）

> 尺与正本＝<c>bin/WI83/spike1b/</c>（<c>host.csproj</c>＋<c>Program.cs</c>＋<c>www/battery*.js</c>）→
> <c>bin/WI83/spike1b/out_r3/</c>、<c>out_r4/</c>、<c>out_r5/</c>（<c>host_log.jsonl</c>／<c>page_report.json</c>／<c>shots/*.png</c>）；
> 铺站与那颗冻结 sha 的对账＝<c>bin/WI83/spike1b/prep_83ap.py</c> → <c>bin/WI83/spike1b/spike1b_prep_83ap.txt</c>；
> 进程树那把尺＝<c>bin/WI83/spike1b/tree_83aq.py</c>（同一把尺在跑前／跑中／关窗后各读一次）。
> ⚠ 这一腿⛔ 起的是产品窗：那颗 exe 只活在 <c>bin/</c>（整棵在 <c>.gitignore</c> 内⇒<b>仓⛔ 涨</b>），跑完自己关，⛔ 留改坏的源码。

### 18.0 形状：五回各撞的哪一颗墙

| 跑次 | 结果 | 为什么（都是⛔ 被测对象，是我那把尺） |
|---|---|---|
| r1 | 挂载超时 | 那颗 <c>ref</c> 我按「对象 ref」递，<c>Excalidraw</c> 现读是 <c>Symbol(react.memo)</c> 包一颗函数⇒ref 从没挂上 |
| r2 | 同上＋诊断 | 诊断给出：<c>.excalidraw</c> 在、2 颗 canvas、9 颗 button、root 内 17 622 字符⇒<b>画板其实上了屏</b>，缺的只是「我拿不到那颗 API」 |
| r3 | 交全表（2 分 39 秒） | 改走 <c>initialData</c>＋<c>onChange</c>（两颗都是包里的 prop）⇒ 不再需要那颗 API |
| r4 | 460 秒超时 | 页面吊在 <c>exportToClipboard</c>（见 18.4）；那一跑仍交回那颗<b>桥时延数字表</b> |
| r5 | 交全表（≈85 秒） | 给那颗调用套了 6 秒守卫⇒「吊住」从此是一格<b>读数</b>，⛔ 是一场事故 |

⇒ 四处我自己的瞎（那颗 ref 的递法／截图旧帧／pong 的读法／<c>Local Storage</c> 落点找错层）全写在 <b>18.8</b>，改完才有的 r4／r5。

### 18.1 「断网」是怎么断的：先给那颗对照件

| 现读 | 数 |
|---|---|
| 这台机器此刻联网吗（⛔ 网页侧，python 一把独立尺） | <b>通</b>：esm.sh／bing／github TCP 各通，<c>https://www.bing.com/robots.txt</c> 200／2 676 字节 |
| 同一时刻宿主那一侧 | <c>blocked=17</c>（r3）／<c>blocked=3</c>（r4、r5），主机名只有 <b>esm.sh</b>（r4 另加一颗 <c>www.bing.com</c>＝我故意递的对照） |
| 那颗拦截机制 | <c>AddWebResourceRequestedFilter("http(s)://*", All)</c> ＋ <c>WebResourceRequested</c> 里给一颗 451 |

⇒ <b>「断网」是拦出来的，⛔ 是这台机器本来没网</b>——那颗 bing 探针在 python 侧 200、在网页侧被拦，就是这一句的正件。
⇒ 顺带一颗<b>机制事实</b>（§17.4 那颗拦截点从此有答案）：<c>blocked</c> 里出现过 <c>ctx=Document</c> 那颗 <c>https://www.youtube.com/embed/…?enablejsapi=1</c>⇒<b>那颗 filter 连 iframe 的文档请求都盖得住</b>。

### 18.2 ② 字体面：那堵「远程回退」现读是 <c>esm.sh</c>，而且⛔ 止于画板

<b>§坑4／§坑5 那句「字体有远程回退」在这一腿量到了那颗主机名、那几族、和它漏到哪儿。</b>

| 现读 | 数 |
|---|---|
| <c>index.css</c> 里那颗 <c>@font-face</c> 的 src | 4 颗，<b>remote 0 颗</b>（全相对路径⇒<b>打包侧⛔ 改</b>，§17.6 那句复称一致） |
| 但运行时真去要的 | <c>https://esm.sh/@excalidraw/excalidraw@0.18.0/dist/prod/fonts/&lt;族&gt;/&lt;档&gt;.woff2</c> |
| 被拦的族（r3 那 17 颗点名） | Virgil／Cascadia／Excalifont／<b>Xiaolai×3 分片</b>／Nunito／Liberation／Lilita／ComicShanns，另 7 颗走 <c>XmlHttpRequest</c> 重试 |
| 断网下真落地的族 | <b>只有 Assistant</b>（3 颗本地 woff2＝<c>status 200</c>）；<c>document.fonts</c> 其余全 <c>unloaded</c>，逐族 <c>document.fonts.load()</c> 的 <c>matched=0</c> |
| 白底照片那一格 | <c>out_r3/shots/r3_a_scene.png</c>：矩形与箭头是手绘味（roughjs 在 bundle 里），<b>六行「永字八法 Abc 123」全是系统兜底字</b>⇒ 手写体这一格断网即灭 |
| ⚠ 漏到出口外面 | 断网下 <c>exportToSvg</c> 交出的那段 SVG 里仍写着 <c>https://esm.sh/…Virgil-Regular.woff2</c>（r5 现读 <c>remoteCount=2</c>／<c>dataUri=0</c>／<c>fontFace=1</c>）⇒<b>对方打开那张 SVG 会自己去请 esm.sh</b>   〔片9 更正那颗因果〕它⛔ 是「导出总会带一条远程引用」：链＝<c>toCSS</c>（<c>chunk:4856</c>）→<c>getContent</c> 逐颗候选取（<c>:4872～4888</c>，<c>fetchFont</c> 带 <c>cache:"force-cache"</c> 与 <c>Accept: font/woff2</c>）→<b>全部取不到才</b> <c>return this.urls[this.urls.length-1].toString()</c>（<c>:4893</c>）＝把最后那颗兜底 CDN 当正文写出去。本地基座接通⇒那颗函数交 base64 子集⇒<c>dataUri</c> ⛔ 再是 0（源码判，§19.5 第①臂才兑现）。 |

⇒ <b>形1（本腿新撞，进 §五 续排的候选）</b>：那颗包里<b>⛔ 带</b> <c>esm.sh</c> 那颗基座，也<b>⛔ 是</b> <c>index.css</c> 写的——它在 JS 里（<c>fonts.css</c> 之外的一颗运行时回退）。
后果两档：<b>①</b> 用户机器断网／⛔ 联网⇒画板手写体与中文手写体全灭（＝§十 第一格现读不通过）；<b>②</b> 导出的 SVG 带着那条远程引用出机（＝§十四 那句「静态命中⛔ 等于出机」在这一臂又兑现一次，而且这次是<b>会替我们出机</b>的那一类）。
⇒ 出路（<b>片9 按那颗包自己的源码重排</b>，逐字行号在 §十九）：<b>①官方那颗全局</b> <c>window.EXCALIDRAW_ASSET_PATH</c>——那颗包 <c>README.md</c>「Self-hosting fonts」一节（<c>:19／:21／:24</c>）逐字给的就是它，形状＝一行 <c>&lt;script&gt;window.EXCALIDRAW_ASSET_PATH = "/";&lt;/script&gt;</c>；语义现读 <c>createUrls</c>＝配的那颗基座排<b>前</b>、<c>esm.sh</c> 永远追加成<b>最后</b>一颗 ⇒ <b>①⛔ 改包、⛔ 预载、⛔ 451</b>，代价＝宿主 HTML 多一行＋那颗 <c>fonts/</c> 目录必须随包（两颗<b>必须一起</b>：全局⛔ 等于断网，本地缺件时那颗兜底照样被请＝一次真出机）。<b>②</b>整族预载进 <c>document.fonts</c>、<b>③</b>打包期换那颗运行时常量、<b>④</b>宿主侧把 <c>esm.sh</c> 永久 451 并接受「断网即兜底字」——三颗全降为①失效时的备选。<b>§九 决策 3（Xiaolai 随包⛔ 随包）在这一格上仍⛔ 够，但改口：随包＋那颗全局＝两颗一起才兑现；上一版那句「随了包也还要那颗运行时基座改口」⛔ 成立——那颗常量本来就是兜底，配了全局就轮不到它。</b>

### 18.3 ② wasm 那半格，撞出来的是<b>一堵 4 MB 的墙</b>

| 现读 | 数 |
|---|---|
| 那颗 3 600×3 600 噪声 PNG | <b>20 298 916</b> 字节（≈19.4 MiB；我那句 <c>over20MB</c> 按 20 MiB 判⇒报 False，口径以颗字节为准） |
| 拖进去之后屏幕上出现什么 | 一颗居中 <b>Error</b> 卡：<c>File is too big. Maximum allowed size is 4MB.</c>（<c>out_r5/shots/r5_after_drop.png</c>）＋背景压暗 |
| 减重管线跑到没 | <b>跑到</b>〔片9 就地更正：这一格上一版把「⛔ 到」与「compile 2 次」写进了同一行的两个半句，我读前半⛔ 读后半〕那颗闸门在减重<b>之后</b>：<c>dev/index.js:28411</c> 先 <c>resizeImageFile(imageFile,{maxWidthOrHeight:1440})</c>，<c>:28420</c> 才 <c>if(imageFile.size&gt;4*1024*1024) throw</c>（两颗常数原名 <c>DEFAULT_MAX_IMAGE_WIDTH_OR_HEIGHT</c>／<c>MAX_ALLOWED_FILE_BYTES</c>，定义在 <c>dev/chunk-3KPV5WBD.js:322／:323</c>）。而我<b>自己在 r5 就录了它跑过</b>：<c>attempts[2]/[3]=wasm.compile</c>（t=16 104／16 105 ms），同一把全局尺在更早那格 <c>export-svg-detail</c> 读到 <c>wasmNow=0</c> ⇒ 那两次落在那两格之间＝那颗拖放路径里；min 里 <c>WebAssembly.compile</c> 只有两种调用形状（<c>@2093525／@2128643</c>），同属一颗内部标签写着 <c>mathlib</c>、由 <c>onmessage</c>＋<c>OffscreenCanvas</c> 驱动的模块＝pica 那颗，base64 内联⇒<b>断网可用、⛔ 远程 .wasm 请求</b> |
| 那颗常量在包里长什么样 | <c>Ine=4*1024*1024</c>（<c>dist/spike1.min.js</c> 现读 1 次）；<b>⛔ 任何 prop／env 出口</b> |
| 那句正文进没进语言表 | <c>fileTooBig</c> 在 bundle 里出现 <b>55</b> 次＝55 份语言表各一颗（引擎自己按 <c>langCode</c> 念，⛔ 走我的表） |

⇒ <b>形2</b>：StarMark 那条「截图／贴图进画板」的集成路线，在默认包下会被这堵墙挡在门外——<b>4K 全屏一张 PNG 常&gt;4 MB</b>，而那颗闸门⛔ 出口、⛔ 配置〔片9 更正：上一版这颗「也⛔ 走减重」是错的——它就排在减重之后，见本节那颗表第一行与 §19.3〕。 〔片9 同段补一颗边界〕那颗 resize 对 <b>SVG 原样返回</b>（<c>dev/chunk-3KPV5WBD.js:24819～24821</c>），且输出 <c>type</c> 沿用原档（<c>:24846</c>）⇒ 拖 PNG 进去不会自动转 JPEG 减重。
⇒ 出口（<b>片9 按源码重排，代价从低到高</b>）：<b>①</b>罩着那颗闸门的整块 <c>if</c> 有现成出口——<c>dev/index.js:28409</c> <c>if(!this.files[fileId]?.dataURL)</c>：宿主<b>自己缩好、自己编成 <c>dataURL</c> 交进 <c>files</c></b> ⇒ 减重与闸门<b>一起跳过</b>（那颗 <c>fileId</c> 可由 prop <c>generateIdForFile</c> 影响，<c>:28399</c>）；源码判，§19.5 那一臂才兑现，代价＝跳过引擎那颗 1440 之后<b>尺寸上限得我自己给</b>，否则原图整颗进场景。<b>②</b>⛔ 动任何东西、直接拖——引擎那颗 1440 已把多数照片味压进闸内（待量）。<b>③</b>打包期换那颗常数（一次改一颗，代价＝与上游漂移，§坑16 那一族）。<b>④</b>那条路⛔ 走引擎文件入口，改走「画板背景／贴图」那颗⛔ 经闸的通道。<b>两颗排障形状（本腿新撞）</b>：那颗 resize 抛错只 <c>console.error("Error trying to resizing image file on insertion",…)</c>（<c>:28415</c>）⇒ 原图裸着去撞闸门，屏幕上那句「too big」有可能是「解码失败」；<c>resizeImageFile</c> 对 SVG 原样返回 ⇒ 那颗也直接撞闸门。
⇒ §十 点验单那句「拖 20 MB 图→减重管线 wasm 生效」——〔片9 就地更正〕<b>那颗管线有被叫到</b>（r5 同一格里 <c>wasm.compile</c> 2 次，而 r3 那颗 <c>image-paste</c> 格交回 <c>wasmCalls=[]</c>、<c>filesAfter=0</c>＝那一条压根没进插入链），它只是<b>压不动</b>那颗噪声图；那句「生效」要靠一颗<b>可压缩</b>的图才量得出（§19.5 第②臂），⛔ 是这一颗。上一版那句「根本没被叫到」是错的。

### 18.4 ③ 出口三法：两成一吊，而且「档名」⛔ 归网页

| 出口 | 现读 |
|---|---|
| (a) 下载事件 | <c>DownloadStarting</c> 触发（这颗 SDK 里它叫 <b>DownloadStarting</b>，⛔ 叫 Downloading）；<c>mime=image/png</c>、<c>total=28 759</c>、<c>state=Completed</c>、真落盘 28 759 字节。<b>但</b> <c>a.download="spike1b_download.png"</c> 被无视⇒<c>suggested</c> 是那颗 <c>blob:</c> 的 UUID |
| (b) 桥回调 | 38 348 字符 base64 → 宿主解出 <b>28 759</b> 字节、<c>magic=PNG</c>、sha 前缀 <c>223db0fa</c>⇒<b>与那颗 blob 字节数成对相等</b>（成对判据，⛔「&gt;0」） |
| (c) 剪贴板·网页侧 | <c>NotAllowedError: Document is not focused</c>（r3）；r4 同一条调用<b>直接吊住</b>（6 秒守卫现读 <c>pngMs=6004</c>、<c>focused=false</c>），且吊住之后剪贴板格式表还是<b>空</b> |
| (c′) 剪贴板·宿主侧 | 三行对照：清空后 <c>formats=[]</c> → 宿主 <c>Clipboard.SetImage</c> 后 <c>formats=[Bitmap]</c>、读回 <c>350x250</c> 真档⇒<b>这颗出口只有宿主那一半成立</b> |
| 越权那一格 | 递 <c>..\..\evil.png</c>／<c>D:/…/evil2.png</c>／空名三颗⇒宿主一律 <c>Path.GetFileName</c> 收进沙盒 <c>bridge/</c>（<c>r3_evil.png</c> 3 字节，落点⛔ 出目录）；那颗下载名同样被改写 |

⇒ <b>形3</b>：桥协议那颗「复制为图」⛔ 能交给网页做（无焦点即吊／拒），必须<b>字节走桥、宿主上板</b>；而那颗 <c>exportToClipboard</c> 在宿主侧要<b>自带超时</b>（r4 就是被它吊掉一跑）。
⇒ §十 那句「路径/命名由 C# 定」从此有读数：网页给的档名<b>会被丢</b>，⛔ 是「建议」。

### 18.5 ④ 界面 chrome：两颗菜单各自的行，和「canvasActions 管⛔ 到哪些」

| 现读（1440×879 一颗窗） | 数 |
|---|---|
| 闭合态带 title/aria 的可点件（r4） | <b>35</b> 颗，全在屏上 |
| 同一颗数在「七项动作全 false」那一臂 | <b>33</b> 颗⇒<b>少 2 颗</b>（那两颗归 <c>canvasActions</c> 管） |
| 左上角那颗 hamburger（按几何找到） | class=<c>dropdown-menu-button main-menu-trigger</c> @16,16 |
| 它打开后新增的行 | <b>16</b> 颗：<c>Open</c>／<c>Save to...</c>／<c>Export image...</c>／<c>Find on canvas</c>／<c>Help</c>／<b><c>GitHub</c></b>／<b><c>X</c></b>／<b><c>Discord</c></b>／<c>Dark mode</c>／6 颗背景色板／<c>Show background color picker</c> |
| 那颗 title 现读「More tools」的按钮 | 打开后新增 4 行：<c>Frame tool</c>／<b><c>Web Embed</c></b>／<b><c>Laser pointer</c></b>／<b><c>Mermaid to Excalidraw</c></b>；<b>它的 class 本腿没读到</b>⇒⛔ 就此认它是包里那颗 <c>TTDDialogTrigger</c>（§17.5 那颗导出名），那一格留「未判定」 |
| <c>aiEnabled:false</c> 那一臂 | 与默认臂<b>逐颗相同</b>（delta=0）⇒那颗 prop 管⛔ 到上面这些行 |
| <c>canvasActions</c> 七项全 false＋<c>tools.image:false</c> | 「More tools」里那四行<b>一颗⛔ 少</b>⇒那颗开关只管画布动作，⛔ 管工具与插件行 |
| <c>langCode:"zh-CN"</c> | 那 9 颗按钮里 <b>6</b> 颗念出中文（更多工具／缩小／重置缩放／放大／帮助／退出禅模式）⇒引擎自带语言表真在生效 |

⇒ §12.5 那张「摘哪几颗」的名单从此有出处，且<b>摘法分三档</b>：①画布动作走 <c>canvasActions</c>；②<b>GitHub／X／Discord／Web Embed／Laser／Mermaid 那几颗走⛔ 任何 prop</b>⇒要么 CSS 摘、要么运行时摘 DOM、要么打包期把那颗 <c>TTDDialogTrigger</c>／<c>OverrideMenu</c> 换掉（§17.3 那颗 alias/stub 臂是同一族手段）；③那句「默认 Key ⛔ 配时它是颗死按钮」在这一腿没测（那颗按钮在包内，但本腿没点它）。
⇒ ⚠ 观感那一格留给他：<b>照片里那颗菜单是收起的</b>——我这把截图尺为了拿新鲜帧会把窗顶到前面，而<b>顶窗这一下正好把弹层关掉</b>（见 18.7）。菜单内容以那一列 DOM 读数为准。

### 18.6 ⑤ 桥那颗往返：数字表（r4，n=150）

| 载荷 | 单颗往返 |
|---|---|
| 空载荷 ×150 | min <b>0.2</b> ms／p50 <b>0.4</b> ms／p95 <b>0.7</b> ms／max 11.6 ms／超时 0 |
| 64 KB | 4 ms（回声 65 536 字符，逐颗对过） |
| 1 MB | 31 ms（1 048 576） |
| 8 MB | 177 ms（8 388 608） |

⇒ §12.1 那颗桥协议从此有预算：<b>控制消息按亚毫秒算</b>，⛔ 拿它当第二条数据通道；图／导出这类载荷走「字节进桥＋宿主落盘」那一臂（18.4(b) 已证成对）。
⇒ 32 MB 那一档本腿<b>⛔ 跑</b>（我把它从臂里摘了：宿主那颗 <c>pad</c> 是字符串，撞上 OOM 会<b>整跑丢</b>，⛔ 拿一跑换一颗数）。

### 18.7 ① 生命周期与那颗 userData：改道成功，且尺带正件

| 现读 | 数 |
|---|---|
| Runtime（环境自报） | <c>152.0.4191.66</c>；UA 尾 <c>Chrome/152.0.0.0 … Edg/152.0.0.0</c>；dpr 1 |
| 跑中进程树（按那颗沙盒 <c>--user-data-dir</c> 归因） | <b>7</b> 颗（全局 12→19）；<c>GetProcessInfos()</c> 自报 5～8 颗：Browser／Gpu／Utility×N／Renderer×N |
| <b>关窗后</b>（同一把尺，再读） | <b>0</b> 颗归因于我；全局回到 12⇒§坑8 那句「关窗即散」在这一腿<b>可复现</b>（正件＝跑中那 7 颗） |
| userData 落点 | <c>&lt;沙盒&gt;\wv2data\EBWebView\Default\Local Storage</c>（5 档／452 字节，含我写那颗 <c>spike1b.marker</c>）；exe 旁 <c>*.WebView2</c> 残留 <b>0</b>；<c>%LOCALAPPDATA%\spike1b</c> <b>⛔ 存在</b> |
| 引擎自己往 localStorage 写了没 | <b>没</b>：整跑 <c>Object.keys(localStorage)</c> 只剩我那颗 marker，<c>engineKeys=[]</c>；<c>IndexedDB</c> 目录 <b>0</b> 颗⇒<b>§12.2「引擎的 LocalData 层⛔ 点火」这一格现读成立</b>（正件＝我自己那颗写得进也读得回） |
| 首帧 | 挂载到 <c>.excalidraw</c> 出现 ≈<b>960 ms</b>（r3／r5 两跑同档）；<c>onChange</c> 首跑 2 次 |

### 18.8 本腿自己四处瞎（收口腿进坑表；都是<b>我</b>的，⛔ 被测对象）

1. <b>那颗 <c>ref</c></b>：按「对象 ref」递 <c>&lt;Excalidraw ref={{current:null}}&gt;</c>⇒生产包里<b>静默⛔ 挂</b>（memo 包函数组件），我把「画板没上屏」读了两回。改法＝走 <c>initialData</c>＋<c>onChange</c>，并把「ref 给不给」本身留成一格读数。
2. <b>那颗截图</b>：<c>CapturePreviewAsync</c> 在窗被遮挡时交<b>旧帧</b>（r3 四张<b>同一颗 sha</b>）。改法＝<c>--disable-features=CalculateNativeWinOcclusion</c>＋拍前顶窗，并<b>当场算 sha 报「第几颗不同」</b>；副作用＝顶窗那一下会关掉正在拍的弹层（18.5 那颗 ⚠）。
3. <b>那颗 pong</b>：宿主用 <c>PostWebMessageAsJson</c>，页面收到的是<b>对象</b>，我 <c>JSON.parse(ev.data)</c> 恒抛⇒r3 那颗「桥时延」全部读成超时（诚实留空才对，它留了 <c>timedOutAt=0</c>）。改法＝两侧都按形状分档。
4. <b>那颗 <c>Local Storage</c> 落点</b>：我按 <c>&lt;udf&gt;\Default\…</c> 找⇒读成 0 颗档；真形状多一层 <c>EBWebView</c>。同一类瞎在 18.7 那颗 <c>onChange</c> 实参位上又现一次（files 在第 3 还是第 4 颗，本腿<b>未判定</b>⇒那颗「图数据多大」留白）。

### 18.9 §八 Spike 批与 §十 点验单：这一腿交掉哪几格、还欠哪几格

<b>交掉</b>：0① 运行时探测＋环境释放（18.7，含正件）；0② 字体/wasm 断网全验（18.2／18.3，<b>结论是「⛔ 通过」两颗缺陷形状</b>）；0③ 出口 3 法各试（18.4，两成一吊）；0④ TTD／Publish 关按钮有效性（18.5，<c>aiEnabled</c>／<c>canvasActions</c> 都管⛔ 到那几行）；0⑤ 桥往返时延（18.6）；§17.4 那颗拦截点（18.1，含 iframe Document）；§十 的「userData 改道」「关窗进程树归零」「旧 embeddable ⛔ 加载远程（iframe 被拦后是颗空框，<b> 警告文案要我们自己写</b>）」三格。 〔片9：18.3 那一半的因果已就地更正（那颗闸门在减重<b>之后</b>、那颗管线在 r5 真跑过 2 次），形2 收窄与四颗出口见 §十九；那颗 <c>woff2.wasm</c> 的「断网坏没坏」也在 §19.4 换成两颗更准的问题〕

<b>还欠（⛔ 从表里消失）</b>：
- <b>观感／口径</b>：菜单展开态那张照片（我这把截图尺⛔ 拍得到弹层，见 18.7 第 2 条的副作用）；手写体断网那两档的<b>肉眼定论</b>；「默认 Key ⛔ 配时那颗按钮是不是死按钮」。
- <b>机制</b>：那颗 4 MB 墙的三颗出路选哪颗（18.3）；<c>esm.sh</c> 基座改口那三颗（18.2）；<c>woff2.wasm</c> 那颗子集化开关<b>本腿没找到点它的那颗按钮</b>（⇒「断网时它坏没坏」未判定）；<c>onChange</c> 实参位与那颗图数据多大（18.8 第 4 条）。 〔片9 换了一茬：前两颗由 §十九 换成「一颗有官方出处的全局＋四颗出口并排」；那颗 <c>woff2.wasm</c> 拆成两问——<b>断网编不编得好＝编</b>（包内 <c>.wasm</c> 0 颗、base64 内联，§19.4），<b>出没出过字节＝还没被叫到</b>（排在字体字节取到之后，与 §19.5 第①臂同跑判掉）；剩下 <c>onChange</c> 那颗仍欠〕 〔片10 又交一格，⛔ 等那颗窗〕<c>onChange</c> 的实参位次<b>源码判完</b>＝<c>(elements, this.state, this.files)</c> 三颗、<c>files</c> 在第 3 位（<c>dev/index.js:1025104</c>）；那颗 <c>ref</c> 的事另有官方出口 <c>excalidrawAPI</c> prop（§20.1）。
- <b>多窗</b>：两块板窗开同一颗档的冲突表（§12.2）——那是<b>存储接管</b>那一格，⛔ 属本腿。

### 18.10 对 §八 批形与 §九 决策的位置

- §八 那颗「Spike 批」<b>到这一腿交完</b>（0①～0⑤＋§17.4）⇒按 §八，下一批该是 <b>1 架构批</b>（桥协议＋存储适配器设计文档），而<b>⛔ 是继续起窗</b>。
- 但 18.2／18.3 那两颗形状<b>排在架构批之前</b>：一颗决定「字体到底带什么、基座改到哪儿」，一颗决定「图进画板那条路走⛔ 走得通」——两颗都是<b>架构输入</b>，⛔ 是文案。⇒ 下一腿＝<b>Spike-2 裁决腿</b>：把「4 MB 三出路」与「esm.sh 三出路」各做一颗<b>最小可测臂</b>（仍⛔ 产品码），拿到数字再进架构批。
- §九 决策 3（Xiaolai 随⛔ 随包）现读<b>加了一条前提</b>：随包⛔ 等于能用，那颗运行时基座还得改口；决策 4（AI／TTD 带⛔ 带）加了一条：<b>屏上确有一颗能开出那四行的按钮</b>（title 现读「More tools」），而摘它⛔ 归 <c>aiEnabled</c> 管（现读 delta=0）。
- 本腿⛔ 动过：产品源码、那颗 <c>excalidraw</c> 树、系统 PATH、网络配置、那颗 <c>待决策事项.md</c> 账本。三颗形状（形1／形2／形3）<b>登记在本节</b>，进不进 §五 坑表与账本，等架构批一起排。

## 十九、Spike-2 裁决腿（源码面）：那颗包里早就写好的开关，和三处我自己写反的句（批次 Phase C 片9，2026-10-17·⛔ 起窗的只读源码腿，⛔ 产品码⛔ 建工程⛔ 改系统 PATH⛔ 拔网⛔ 装工具链⛔ 表⛔ 账本）

> 尺与正本＝<c>bin/WI83/spike2_83az.py</c> → <c>spike2_probe_83az.txt</c>（资产基座那面：那颗全局的 4 处命中、回退那颗常量、<c>createUrls</c> 函数体逐字、那颗包里的 <c>./fonts/</c> 资产串）；
> <c>bin/WI83/spike2b_83ba.py</c> → <c>spike2_probe2_83ba.txt</c>（4 MB 那堵闸门的<b>括号嵌套</b>＋<c>fileTooBig</c> 那颗正文在 min 里的 55 处＝55 份语言表）；
> <c>bin/WI83/spike2c_83bc.py</c> → <c>spike2c_probe_83bc.txt</c>（那颗 wasm 的<b>归因</b>＝r3／r5 同一把全局尺的两次读数；整棵 <c>dist</c> 的 <c>.wasm</c> 普查；min 里 20 处 <c>WebAssembly.*</c> 调用点逐个打上下文）。
> 被查的两颗档＝<b>上游那颗包</b>（<c>@excalidraw/excalidraw@0.18.0</c> 的 <c>dist/dev/</c> 未混淆那一面，加那颗包自己的 <c>README.md</c>）与<b>我自己那颗 Spike bundle</b>（<c>bin/WI83/npm0e/build/dist/spike1.min.js</c>，8 376 283 字节；那颗 sha 与 §17.6 冻结一致，片8 <c>spike1b_prep_83ap.txt</c> 复称过）。两颗互证的用处：未混淆面给我的是<b>原名</b>（<c>MAX_ALLOWED_FILE_BYTES</c>／<c>DEFAULT_MAX_IMAGE_WIDTH_OR_HEIGHT</c>／<c>LOCAL_FONT_PROTOCOL</c>／<c>normalizeBaseUrl</c>），我那颗 min 只给我偏移——同一形状在两处都读到才算数。
> ⚠ 这一腿<b>没起窗</b>：动手前最后两把 <c>GetLastInputInfo</c> 都是 <b>0.0 秒</b>（他在用电脑）⇒ 需要那颗窗的两格（19.5）登记成「待跑」，⛔ 拿「源码说会这样」冒充「量到它这样」。

### 19.0 形状：只问两颗问题，三处答案全打在我自己 §十八／§五 那几句上

| 我写的 | 那颗包现读的 | 落在哪儿 |
|---|---|---|
| 18.2 出路①「把那颗基座改成本地（换 <c>fonts.css</c> 与那颗运行时常量）」 | 包里躺着一颗<b>官方全局</b> <c>window.EXCALIDRAW_ASSET_PATH</c>，<c>README.md</c>「Self-hosting fonts」一节逐字给了那一行 ⇒ ⛔ 换文件、⛔ 改常量 | §18.2 与 §五 坑4（就地更正） |
| 18.3 那格「减重管线跑到没＝⛔ 到（闸门在减重<b>之前</b>）」＋末行「那颗管线在这种尺寸下根本没被叫到」 | 顺序<b>相反</b>：<c>dev/index.js:28411</c> 先压到 1440，<c>:28420</c> 才量 4 MB；而我<b>自己在 r5 就录了它跑过</b> | §18.3（就地更正）＋§十 那一格 |
| §五 坑5「woff2 子集化 wasm 也是 fetch 加载＝断网即失效」 | 整棵 <c>dist</c> 里 <c>.wasm</c> 档 <b>0</b> 颗，两颗 wasm 以 base64 活在 JS 里 | §五 坑5（就地更正）＋19.4 |

⇒ 三处都⛔ 是被测对象，都是<b>我</b>：①「读到关键字就当成读到顺序」——那颗包名与那颗顺序我各记过一遍，却⛔ 把它们放进同一句里比；②<b>同一行里自己打自己</b>——「⛔ 到」与「compile 2 次」写进同一格的两个半句，我读前半⛔ 读后半；③<b>把上游早先的形状当现读</b>——坑5 那句来自读 <c>woff2-bindings.ts</c> 的印象，这一腿才真去数那颗包里有几颗 <c>.wasm</c>。三条进 <c>docs/踩坑记录.md</c>（收口腿）。（片10 又添一处：我把「关掉引擎的 <c>LocalData</c> 层」写进了 §十二 那条关键设计，而那一片在这颗包里⛔ 存在——先数再设计，见 §20.4。）

### 19.1 那颗资产基座：<c>createUrls</c> 的四档出口与那颗全局的语义、时机、射程

出处：<c>dist/dev/chunk-3KPV5WBD.js:4926～4951</c>（未混淆那颗名 <c>ExcalidrawFontFace.createUrls</c>）；我那颗 min 同段在 <c>@2272572</c>，体长 466 字节、括号配平闭于 <c>@2273050</c>，两处<b>分支序列一致</b>。

函数体（<b>分支与顺序逐字</b>；行内有缩写、注释是我加的；完整原文＝正本 ③ 段）：

```js
static createUrls(uri) {
  if (uri.startsWith("data"))  return [uri];                       // ① 自己就是正文
  if (uri.startsWith(LOCAL_FONT_PROTOCOL)) return [];               // ② "local:"（:4619）＝⛔ 请任何人
  if (uri.startsWith("http"))  return [new URL(uri)];               // ③ 绝对 URL
  const assetUrl = uri.replace(/^\/+/, ""); const urls = [];          // ④ 其余＝随包那几族全走这档
  if (typeof window.EXCALIDRAW_ASSET_PATH === "string") { urls.push(new URL(assetUrl, normalizeBaseUrl(…))) }
  else if (Array.isArray(window.EXCALIDRAW_ASSET_PATH)) { forEach(path => urls.push(…)) }
  urls.push(new URL(assetUrl, _ExcalidrawFontFace.ASSETS_FALLBACK_URL));   // :4949 兜底永远最后
  return urls;
}
// :4978  ASSETS_FALLBACK_URL = `https://esm.sh/${PKG_NAME?`${PKG_NAME}@${PKG_VERSION}`:"@excalidraw/excalidraw"}/dist/prod/`
```

| 现读 | 数 |
|---|---|
| 那颗全局在 min 里的命中 | <b>4</b> 处，全在同一颗 <c>createUrls</c> 里（<c>@2272742／2272810／2272880／2272911</c>）⇒ 它是<b>唯一</b>消费点，⛔ 第二处偷偷读 |
| <b>读取时机</b> | <c>new ExcalidrawFontFace(family, uri, descriptors)</c> 的构造函数里（<c>:4842</c>）＝<b>建那颗对象时</b>才读，⛔ 模块顶层一次性 ⇒ 宿主在 <c>&lt;script type=module&gt;</c> 之前那句全局就够（README 给的形状正是那颗一行） |
| <b>那颗全局可以是相对</b> | <c>normalizeBaseUrl</c>（<c>:4966～4976</c>）：对 <c>/^\.?\//</c> 的串按 <c>window.location.origin</c> 解析，再补一颗尾斜杠 ⇒ 在 <c>SetVirtualHostNameToFolderMapping</c> 那颗虚拟站上写 <c>"/"</c> 就正指我那份 <c>www/</c> 根 |
| <b>可以是数组</b> | 逐颗 push 进候选（多基座＝多颗按序尝试，<c>getContent</c> 的注释原文「from first to last, treated as fallbacks」<c>:4868</c>） |
| 那 17 颗被拦的 URL 出自哪一档 | 全出自第④档（<c>./fonts/<族>/<档>.woff2</c> ⇒ 拼到兜底基座上），与 §18.2 那颗表一致 |
| 走第②档的字族 | <c>local: true</c> 那两颗：<c>Helvetica</c>（<c>:4570</c>）与 <c>Segoe UI Emoji</c>（<c>:4608</c>）⇒ 它们<b>⛔ 候选</b>，全靠系统里那颗；引擎那句原话里的字体栈 <c>"16px Virgil, Segoe UI Emoji"</c> 正是这一档的读者 |
| <b>射程</b> | ⛔ 管 <c>index.css</c> 里那 4 颗 <c>@font-face</c>（全相对、走 CSS 那条）⇒ §18.2 表第一行与「断网下真落地的族只有 Assistant（3 颗本地 woff2＝200）」那格<b>照旧成立</b>，那颗全局动⛔ 了它 |

⇒ <b>那颗包里早就写好的开关</b>：<c>README.md:17～25</c>「#### Self-hosting fonts」三行逐字——「By default, Excalidraw will try to download all the used fonts from the [CDN](https://esm.run/@excalidraw/excalidraw/dist/prod)」／「you'll have to copy the content of the folder <c>…/dist/prod/fonts</c> to the path where your assets should be served from … you should also set <c>window.EXCALIDRAW_ASSET_PATH</c> to the very same path」／「<c>&lt;script&gt;window.EXCALIDRAW_ASSET_PATH = "/";&lt;/script&gt;</c>」。<b>两颗一起</b>（拷 <c>fonts/</c> ＋ 设全局）是那颗包<b>自己要求的</b>装法，⛔ 我发明的组合。⚠ 那颗 README 写的主机名是 <c>esm.run</c>，代码那颗常量拼的是 <c>esm.sh</c>（我 r3／r5 被拦的 URL 现读 <c>esm.sh</c>）⇒ 以代码为准，那颗文档句只当出处。

### 19.2 那条「SVG 里写着 esm.sh」的真实因果（比 §18.2 那一版硬一档）

链：<c>toCSS(chars)</c>（<c>:4856</c>，那颗 <c>unicodeRange</c> 命中才继续，否则 <c>undefined</c>）→ <c>getContent(codepoints)</c>（<c>:4872</c>）→ 逐颗候选 <c>fetchFont</c>（<c>:4895</c>：<c>fetch(url,{cache:"force-cache",headers:{Accept:"font/woff2"}})</c>，非 2xx 就 throw）→ 取到才 <c>subsetWoff2GlyphsByCodepoints</c> 交 base64（<c>:4879</c>）→ <b>循环走完还没取到</b>才 <c>console.error("Failed to fetch font family …",JSON.stringify(errorMessages))</c> ＋ <c>return urls[last].toString()</c>（<c>:4889～4893</c>）。

⇒ r5 那格 <c>remoteCount=2／dataUri=0／fontFace=1</c> 的形状因此有了准确读法：<b>「所有候选都落空，于是把最后一颗候选（＝那颗兜底 CDN）当正文写进 SVG」</b>，⛔ 是「导出总会挂一条远程引用」。
⇒ 于是 19.1 那颗开关<b>一颗顶两档后果</b>：本地基座取得到 ⇒ ①画板手写体与中文手写体活（§十 第一格），②那颗导出交 base64 子集 ⇒ 那张 SVG ⛔ 再带远程引用（§十四 那条「静态命中⛔ 等于出机」在这一臂由同颗开关堵住，而且是以<b>⛔ 出机</b>的方式堵）。
⇒ <b>⚠ 但两颗必须一起，且那颗兜底⛔ 消失</b>：<c>esm.sh</c> 永远排在名单最后，本地缺一颗档／虚拟站根指错／那颗 <c>fonts/</c> 没进包 ⇒ 那一次请求<b>真的替我们出机</b>。⇒ 这条进 §五 续排（形1′），宿主侧那颗 451 仍是最后一道，⛔ 因为「配了全局」就摘掉。

### 19.3 那堵 4 MB 的墙：罩着它的那颗 <c>if</c> 比墙本身更该记住

逐字（<c>dist/dev/index.js:28378</c> 起的那颗 <c>initializeImage</c>；我那颗 min 同段在 <c>@8263778</c>，括号嵌套逐字一致）：

```js
const fileId = await (this.props.generateIdForFile?.(imageFile) || generateIdFromFile(imageFile));   // :28399
const existingFileData = this.files[fileId];                        // :28408
if (!existingFileData?.dataURL) {                                   // :28409  ← 整块由这颗 if 罩住
  try { imageFile = await resizeImageFile(imageFile, { maxWidthOrHeight: DEFAULT_MAX_IMAGE_WIDTH_OR_HEIGHT }); }  // :28411  减重在前（1440）
  catch (error) { console.error("Error trying to resizing image file on insertion", error); }                     // :28415  只进 console
  if (imageFile.size > MAX_ALLOWED_FILE_BYTES) {                    // :28420  ← 闸门在后（4*1024*1024）
    throw new Error(t("errors.fileTooBig", { maxSize: `${Math.trunc(MAX_ALLOWED_FILE_BYTES/1024/1024)}MB` })); }
}                                                                   // :28427
const dataURL = this.files[fileId]?.dataURL || await getDataURL(imageFile);  // :28433  已有就⛔ 再编码
```

| # | 由这颗顺序得出的判断 | 状态 |
|---|---|---|
| 1 | 那颗闸门量的是<b>减重之后</b>的字节 ⇒ §18.3 上一版「闸门在减重之前」错 | 已就地更正 |
| 2 | 那颗管线在 r5 <b>真跑过</b>：同一格 <c>wasm.compile</c> 2 次（t=16 104／16 105 ms），更早那格 <c>wasmNow=0</c>；r3 那颗 <c>image-paste</c> 格 <c>wasmCalls=[]</c> ＋ <c>filesAfter=0</c> ⇒ paste 那条没进插入链，drop 那条进了 | 已就地更正（归因见表下） |
| 3 | <c>dataURL</c> 那条出口：只要有，减重<b>与</b>闸门一起跳过 ⇒ 宿主自缩自编是包里现成形状（那颗 <c>fileId</c> 还归 prop 管） | <b>源码判，待 §19.5 第②臂</b> |
| 4 | resize 抛错只 <c>console.error</c> ⇒ 原图裸着撞闸门：屏幕上那句「too big」可能是「解码失败」 | 排障形状，进 §五 候选（形4） |
| 5 | <c>resizeImageFile</c> 对 SVG <b>原样返回</b>（<c>dev/chunk-3KPV5WBD.js:24819～24821</c>）；输出 <c>type</c> 沿用原档（<c>:24846</c>）⇒ PNG 拖进去<b>⛔ 转 JPEG</b>，压不动就是压不动 | 同上 |

第 2 行的<b>归因</b>（⛔ 停在「compile 了两次」）：min 里 <c>WebAssembly.compile</c> 只有两种调用形状（<c>@2093525</c> 与 <c>@2128643</c>），两处都属于一颗内部标签写着 <c>mathlib</c>、由 <c>onmessage</c>＋<c>OffscreenCanvas</c> 驱动的模块（<c>@2090663／@2091316</c> 一带），且我读到它的构造参数原文是 <c>pica({features:["js","wasm"]})</c>（<c>@2598589</c>）⇒ 那两次是 <b>pica 那颗减重的 wasm 在编译</b>，与「resize 跑到」同向；两处形状一样＝主线程与 worker 各一份，这份证据⛔ 判「跑在哪个线程上」，那一格本腿⛔ 判。

### 19.4 那颗 wasm 的资产面（顺手把 §五 坑5 那句判掉一半）

| 现读 | 数 |
|---|---|
| 整棵 <c>…/excalidraw/dist</c> 里的 <c>.wasm</c> 档 | <b>0</b> 颗（<c>os.walk</c> 全树计数，正本 ② 段） |
| <c>prod/chunk-EIO257PC.js</c>（1 824 966 字节）里的 base64 大块 | 2 颗：解码后 <b>727 188</b>／<b>590 664</b> 字节，magic 四颗字节 <c>00 61 73 6d</c> |
| 我那颗 min 的同两处 | <c>@251353</c>（969 587→727 188）与 <c>@1286017</c>（787 554→590 664）＝<b>同两颗模块</b>，尺寸逐颗对上 |
| 子集化那颗的点火点 | <c>WebAssembly.instantiate(D0i)</c>（min <c>@2073652</c>，紧跟它 base64 尾部；出口形状 <c>{subset:(a,s)=>b0i.subset(i,n,a,s)}</c>，同段还有 <c>{Subset:"SUBSET"}</c> 那颗命令枚举）⇒ <b>吃内联字节，⛔ 远程请求</b> |
| r5 里 <c>wasm.instantiate</c> 的颗数 | <b>0</b>（<c>attemptsSummary.byVia={"fetch":1,"fetch:ERR":1,"wasm.compile":2}</c>）⇒ 子集化那颗<b>从没被叫到</b>：它排在「字体字节取到」之后（19.2），而那次一颗字体都没取到 |

⇒ <b>§五 坑5 那句「woff2 子集化 wasm 也是 fetch 加载」在我钉的这颗 <c>0.18.0</c> 随包 dist 里兑现⛔</b>：它⛔ 靠一颗远程 <c>.wasm</c> 活着。
⇒ <b>§18.9 那颗「woff2.wasm 断网时坏没坏」的未判定</b>由这一腿换成两颗更准的问题：<b>(a) 断网编不编得好＝编</b>（base64 内联，同形状的 pica 那颗在断网下真编译了 2 次）；<b>(b) 出没出过字节＝还没被叫到</b>，这一格与 §19.5 第①臂<b>同一次跑</b>就判掉（⛔ 另起一跑）。
⇒ ⚠ 一颗我⛔ 判的：那两颗 727 188／590 664 字节里<b>哪一颗是子集化、另一颗归谁</b>——我只证到「紧跟第二颗 base64 尾部的是那颗 <c>{subset:…}</c> 入口」，第一颗的归属本腿⛔ 判，登记成候选而⛔ 写成结论。

### 19.5 待跑那一臂（两格；源码⛔ 等于兑现，欠的是那颗窗）

| 臂 | 形状（全在片8 那套现成仪器里，⛔ 新工具⛔ 改包⛔ 产品码） | 判什么 | 现读 |
|---|---|---|---|
| ① | 同一份页两跑：<c>window.EXCALIDRAW_ASSET_PATH="/"</c> 设／⛔ 设，其余全同（那颗 451 拦截照旧、字体逐族点名照旧） | 断网下手写体与中文手写体上不上屏（§十 第一格）；<c>dataUri</c> 从 0 变不变；引擎原话里 <c>from urls […]</c> 的名单从 1 颗变 2 颗；<c>WebAssembly.instantiate</c> 起没起＝19.4(b) | ⛔ 跑 |
| ② | 三跑各拖一颗：<b>StarMark 自己截的一张 4K 照片味 PNG</b>／我那颗 3 600×3 600 <b>噪声</b> PNG／第三跑＝宿主先把图缩到 ≤1440 并编成 <c>dataURL</c> 交进 <c>files</c> | 1440 之后照片味那颗进不进 4 MB（＝形2 到底挡谁）；<c>dataURL</c> 那一臂是不是真把 <c>:28409</c> 那整块跳过；顺带把「尺寸上限归谁」量成数字 | ⛔ 跑 |
| 闸门 | 起窗前一把 <c>GetLastInputInfo</c>（本腿最后两把＝<b>0.0 秒</b>⇒⛔ 起窗）；那颗 exe 只活在 <c>bin/</c>（整棵在 <c>.gitignore</c> 内⇒仓⛔ 涨），跑完自己关 | — | — |

⇒ 这两臂<b>属 Spike-2 的后半，⛔ 属落地批</b>：第①臂＝<c>www/index3.html</c> 里加那一行全局；第②臂＝递两颗不同熵的 PNG 进那颗现成页。<b>§十九 全部结论到这两臂跑完才许升「已兑现」</b>；在此之前 §19.1～19.4 是<b>源码面</b>，18.2／18.3 是<b>真机面</b>，两面对着看⛔ 混成一格。

### 19.6 这一腿交掉／新开／改口的账

- <b>交掉</b>：§18.9 那句「<c>esm.sh</c> 基座改口那三颗」换成一颗有官方出处的开关＋一颗必须配套的随包动作（19.1／19.2）；「那颗 4 MB 墙的三颗出路选哪颗」换成四颗出口并排＋一颗源码指出的现成出口（19.3）；§五 坑4／坑5 各就地更正一句；§十 那句「拖 20 MB 图→减重管线 wasm 生效」的因果改口。
- <b>新开（进 §五 续排，等架构批一起点名）</b>：<b>形1′</b>＝那颗全局⛔ 等于断网（兜底 CDN 永远排最后，本地缺件即出机）；<b>形4</b>＝那颗 resize 的 <c>catch</c> 把「解码失败」报成「文件太大」；<b>形5</b>＝<c>dataURL</c> 那条出口同时跳过减重⇒宿主自缩时<b>尺寸上限得自己给</b>，否则原图整颗进场景（体积／内存／导出体积三头都吃）。
- <b>§九 决策 3 改口</b>：那颗「Xiaolai 随⛔ 随包」不再欠「还要改那颗运行时常量」——随包＋那颗全局＝两颗一起就兑现。决策 4 ⛔ 动（仍欠「屏上那颗按钮」那一格）。
- <b>§八／§十二 的位置</b>：那一行全局进<b>落地批清单</b>（宿主 HTML 那一颗，与 §12 那颗虚拟站同名同根）；「两颗必须一起」进<b>架构批</b>那张资产／存储表；三颗形状（形1′／形4／形5）进 §五 续排。下一腿按 §八 该是<b>架构批</b>，而 §19.5 那两臂＝<b>架构批之前最后一次起窗</b>（同片8 那颗一次性宿主，⛔ 新仪器）。

### 19.7 本腿⛔ 动过／⛔ 跑过

⛔ 动过：产品源码、那颗 <c>excalidraw</c> 树、语言表、<c>待决策事项.md</c>、系统 PATH、网络配置。⛔ 起窗（⇒ 19.5 两格登记「待跑」而⛔ 「已通过」）。⛔ 装东西、⛔ 新引包（三把尺都是仓内 python，读的是片6／片7 已落的那两颗档）。⛔ 留改坏的源码：本腿唯一改过的文件是 <c>docs/excalidraw整合可行性深析.md</c> 自己，且改动＝就地更正＋新增一节，⛔ 删别行。

## 二十、Spike-2 后半（桥面与存储面·源码普查）：那颗包把哪 25 颗旋钮交给宿主，以及它其实⛔ 写盘（批次 Phase C 片10，2026-10-17·⛔ 起窗的只读源码腿，⛔ 产品码⛔ 建工程⛔ 改系统 PATH⛔ 拔网⛔ 装工具链⛔ 表⛔ 账本）

> 尺与正本＝<c>bin/WI83/spike3_83bl.py</c>→<c>spike3_probe_83bl.txt</c>（<c>dist/types</c> 361 份 <c>.d.ts</c> 普查＋props 逐字＋文件形状）；<c>spike3b_83bm.py</c>→<c>spike3b_probe_83bm.txt</c>（持久化层颗数：dev 65 档 vs 我那颗 min，两套各数一遍）；<c>spike3c_83bn.py</c>→<c>spike3c_probe_83bn.txt</c>（<c>onPaste</c> 极性／<c>excalidrawAPI</c> 调用点／那颗键表／<c>UIOptions</c>／<c>handleKeyboardGlobally</c>）；<c>spike3d_83bo.py</c>→<c>spike3d_probe_83bo.txt</c>（运行时那颗 <c>api</c> 字面量交出的 25 颗键，逐字）。
> 语料＝<c>@excalidraw/excalidraw@0.18.0/dist</c>（<c>dev/</c> 未混淆 65 档 5 827 756 字节 ＋ <c>types/</c> 361 档 1 139 919 字节）与<b>我那颗</b> <c>bin/WI83/npm0e/build/dist/spike1.min.js</c>（8 376 283 字节，sha 与 §17.6 那颗冻结一致）。
> ⚠ 这一腿<b>⛔ 起窗</b>：起窗第 0 道核对现读 <c>GetLastInputInfo</c> 闲置 <b>3.3 秒</b>（他在用电脑）⇒ §19.5 那两臂继续挂「待跑」，本腿⛔ 动那颗宿主窗。

### 20.0 形状：只问两颗问题

| 问 | 一句话答 |
|---|---|
| 宿主能挂哪几颗钩（⛔ 靠 <c>ref</c>、⛔ 靠猜） | 官方一颗 <c>excalidrawAPI(api)</c> 把 <b>25 颗旋钮</b>整包交出；另有 15 颗 <c>props</c> 钩（含那颗能<b>否决粘贴</b>的 <c>onPaste</c>）与 <c>UIOptions</c> 那张按钮表（§20.1～20.3、§20.5） |
| 引擎自己会不会往盘上写（「接管存储」有没有第二个主人） | <b>⛔</b>：<c>indexedDB</c>／<c>openDatabase</c>／<c>BroadcastChannel</c>／<c>sessionStorage</c>／<c>cookie</c>／<c>serviceWorker</c> 在<b>两套语料里各 0 颗</b>；<c>localStorage</c> 只有 <c>EditorLocalStorage</c> 一颗 wrapper、8 处命中、<b>2 颗实名键、5 个调用点</b>（§20.4） |

### 20.1 那颗包里另有官方 API 出口：<c>excalidrawAPI</c>（r1／r2 那颗 <c>ref</c> 之争的正解）

| 现读 | 形状 |
|---|---|
| 声明（<c>dist/types/excalidraw/types.d.ts</c> 的 <c>ExcalidrawProps</c> 逐字） | <c>excalidrawAPI?: (api: ExcalidrawImperativeAPI) =&gt; void</c> ⇒ <b>⛔ 需要 ref</b>；同档还逐字给着 <c>onChange?: (elements: readonly OrderedExcalidrawElement[], appState: AppState, files: BinaryFiles) =&gt; void</c> |
| 运行时那颗调用点 | <c>dev/index.js:982392</c> 起：先建一颗 <c>const api = {…}</c>（<c>@981130</c>），再 <c>if (typeof excalidrawAPI === function) { excalidrawAPI(api) } else { console.error(…) }</c> ⇒ <b>传非函数只 console.error</b>，屏幕上⛔ 任何提示（§20.6 那条排障形状） |
| 我片8 的读法 | r1／r2 撞的是「我把 <c>ref</c> 按对象 ref 递，而 <c>Excalidraw</c> 是 <c>Symbol(react.memo)</c> 包一颗函数⇒<c>ref</c> 从没挂上」（名册 332）。当时我改走 <c>initialData</c>＋<c>onChange</c>（⛔ 需要 API），<b>绕对了但⛔ 绕在官方出口上</b>：这颗 prop 一直在 <c>types</c> 里躺着。⇒ 落地批的桥<b>第一选择＝这颗 prop</b>，⛔ 是 <c>ref</c>、⛔ 是 <c>window</c> 上挂野对象。 |

### 20.2 那张 25 颗旋钮的名单（桥协议的白名单⛔ 用发明的，用现成的）

运行时 <c>api</c> 字面量交出的键（<c>dev/index.js:981130..982392</c> 逐字，共 <b>25</b> 颗）：

| 档 | 键 |
|---|---|
| 写（宿主→画板） | <c>updateScene</c>、<c>updateLibrary</c>、<c>addFiles</c>、<c>resetScene</c>、<c>scrollToContent</c>、<c>setActiveTool</c>、<c>setCursor</c>、<c>resetCursor</c>、<c>updateFrameRendering</c>、<c>toggleSidebar</c>、<c>setToast</c>、<c>refresh</c>、<c>registerAction</c>、<c>history.clear</c> |
| 读（宿主问画板要现状） | <c>getSceneElements</c>、<c>getSceneElementsIncludingDeleted</c>、<c>getAppState</c>、<c>getFiles</c>、<c>getName</c>、<c>id</c> |
| 订阅（画板→宿主） | <c>onChange</c>、<c>onPointerDown</c>、<c>onPointerUp</c>、<c>onScrollChange</c>、<c>onUserFollow</c>（五颗都是 <c>emitter.on</c>，⛔ 一处 unsubscribe 句柄⇒桥要自己摘）  〔片11f <b>更正：这一格我写反了</b>——那颗 <c>on</c> <b>交回退订句柄</b>：<c>types/excalidraw/emitter.d.ts</c> 逐字 <c>on(...): UnsubscribeCallback</c> 且注释原文写着 <i>@returns unsubscribe function</i>，<c>UnsubscribeCallback = () =&gt; void</c>；而那颗 api 是<b>表达式体箭头</b>（<c>onChange: (cb) =&gt; this.onChangeEmitter.on(cb)</c>，<c>dev/index.js:982087</c> 一带）⇒ 句柄一路交给宿主；包<b>自己的</b>那份消费者就是这么用的（<c>hooks/useEmitter.ts</c>：<c>const unsubscribe = emitter.on(...); return () =&gt; { unsubscribe(); };</c>）⇒ 桥<b>⛔ 再造一套摘除</b>，改成「存进会话表、<c>Closed</c> 里逐颗调」（§21.1 硬线⑥）。〕 |

⇒ <b>三颗由此定死的裁决材料</b>：①「保存」⛔ 需要 JS 侧序列化—宿主拿 <c>getSceneElements()</c>＋<c>getAppState()</c>＋<c>getFiles()</c> 自己 <c>serializeAsJSON</c>（那颗函数在 <c>index.d.ts</c> 导出名单里，与 <c>loadFromBlob</c>／<c>restore</c>／<c>reconcileElements</c> 同列）；②「贴图进画板」有官方写面 <c>addFiles</c>（配 §19.3 那颗 <c>dataURL</c> 出口＝两颗都⛔ 走 <c>input</c>）；③<b>订阅那五颗⛔ 带退订句柄</b>⇒关窗时桥若还活着，回调会往已 Disposed 的宿主上打（§十 点验单加一格：关窗后 5 秒内不得再有桥消息）。

### 20.3 那颗 <c>onChange</c> 是通知，⛔ 是拦截；拦截点是 <c>onPaste</c>

- <c>onChange</c> 的调用点：<c>this.props.onChange?.(elements, this.state, this.files)</c>（<c>dev/index.js:1025104</c>），且同一行之后立刻 <c>this.onChangeEmitter.trigger(…)</c> ⇒ <b>prop 与 api 订阅两条路同时喂</b>，实参同一颗对象；返回值⛔ 看（类型就是 <c>=&gt; void</c>）⇒ <b>想改内容⛔ 靠 <c>onChange</c> 否决</b>，只能回写（<c>updateScene</c>）。
- <c>onPaste</c> 才是否决口（<c>dev/index.js:832002</c> 逐字）：<c>if (this.props.onPaste) { try { if (await this.props.onPaste(data, event) === false) { return; } } catch (error) { console.error(error) } }</c>。⇒ <b>只有<b>严格等于</b> <c>false</c> 才拦得下</b>（返 <c>true</c>／<c>undefined</c>／什么都不返＝引擎照走），而<b>宿主那颗钩一抛就被 <c>catch</c> 吞掉、默认行为照走（fail-open）</b>。⇒ <b>形状登记（形6）</b>：若「敏感图不进画板」这类策略挂在 <c>onPaste</c>，钩子本体⛔ 许抛、且⛔ 忘记 <c>return false</c>——两颗都是静默放行，屏幕上只会看到「它怎么还是进去了」。
- 那颗 <c>data</c> 之后的分支（同段逐字）给了策略的粒度：<c>data.errorMessage</c> → 报错卡；<c>data.spreadsheet && !isPlainPaste</c> → 表格粘贴框；<c>data.elements</c> → 场景片段。⇒ 宿主想「只拦图、⛔ 拦字」要自己辨 <c>data</c> 的形状（这颗⛔ 在 §18 出现过，桥协议里给它留一档）。

### 20.4 存储面普查：那颗引擎⛔ 写盘（把 §坑11 从猜测换成颗数）

| 持久化层 | dev 65 档 | 我那颗 min |
|---|---|---|
| <c>indexedDB</c> / <c>openDatabase</c> | 0 / 0 | 0 / 0 |
| <c>BroadcastChannel</c>（跨标签同步唯一的机制） | <b>0</b> | <b>0</b> |
| <c>sessionStorage</c> / <c>document.cookie</c> / <c>serviceWorker</c> | 0 / 0 / 0 | 0 / 0 / 0 |
| <c>caches.</c>（CacheStorage 形状） | 1（一颗同名局部变量，⛔ 网络缓存） | 1 |
| <c>localStorage</c> | <b>8</b> 处，全在 <c>EditorLocalStorage</c>（<c>index.js:323486</c> 起，四颗静态方法各包一颗 <c>try/catch</c>） | 8 |
| <c>navigator.clipboard</c> | 15 处（读写都有＝片8 那颗「无焦点即拒」的那一片） | 15 |

- 引擎<b>实际会写的实名键只有 2 颗</b>：<c>EDITOR_LS_KEYS = { OAI_API_KEY: excalidraw-oai-api-key, MERMAID_TO_EXCALIDRAW: mermaid-to-excalidraw, PUBLISH_LIBRARY: publish-library… }</c>（<c>chunk-3KPV5WBD.js:11500</c>），但<b>调用点只碰后两颗</b>（<c>index.js:329409／331567／333097／593750／599215</c> 共 5 处）；那颗 <c>OAI_API_KEY</c> 在整包里<b>只出现在常量表两处、⛔ 读者⛔ 写者</b>＝一颗死键。
- 那颗唯一自带防抖的写盘：<c>debounce(saveMermaidDataToStorage, 300)</c>；另有 <c>SEARCH_DEBOUNCE = 350</c> 与 <c>debounce(setSearchTerm, 20)</c>（⛔ 落盘）。⇒ <b>场景 autosave 在这颗包里⛔ 存在</b>。
- 与片8 真机互证：r3 那跑「<c>Object.keys(localStorage)</c> 只剩我那颗 marker、<c>engineKeys=[]</c>、<c>IndexedDB</c> 目录 0 颗」（§18.7）——<b>一颗是运行时数、一颗是源码数，两颗对上了</b>。
⇒ <b>这一格把 §十二 那条设计改了口径</b>：「web 内⛔ IndexedDB 写——<c>LocalData</c> 层禁用」⛔ 需要禁，它⛔ 在；真正的活是「<b>宿主自己写，且只有宿主一个主人</b>」（少了双主，也少了要并轨的第三套键名）。

### 20.5 那三颗官方开关（把 §坑9／坑13 从「要 hack」改成「有 prop」）

| 现读 | 用途 |
|---|---|
| <c>handleKeyboardGlobally?: boolean</c> 的真消费点两处：容器 <c>onKeyDown: this.props.handleKeyboardGlobally ? undefined : this.onKeyDown</c>（<c>index.js:997685</c>）＋ <c>addEventListeners()</c> 里那颗 <c>if (this.props.handleKeyboardGlobally)</c> 门（<c>:1017261</c>） | <b>§坑9（全局键位互杀）有官方出口</b>：关掉它＝画板⛔ 抢 window 级键盘，宿主的全局热键层照常活；默认值现读 <c>false</c>（<c>:1142292</c> 解构处）＝默认就⛔ 抢，那一格比 §坑9 担心的要好 |
| <c>detectScroll</c> 门在 <c>index.js:1020338</c>（默认 <c>true</c>，<c>:1142267</c>） | 滚轮归属；与那颗 <c>onScrollChange</c> 订阅配成一对（宿主做「画板内滚动⛔ 动外层」那一格） |
| <c>DEFAULT_UI_OPTIONS</c>（<c>chunk-3KPV5WBD.js:8781</c> 起逐字）＝<c>canvasActions:{changeViewBackgroundColor,clearCanvas,export:{saveFileToDisk},loadScene,saveToActiveFile,toggleTheme:null,saveAsImage}</c> ＋ <c>tools:{image:true}</c> | 摘按钮的正规出口（片8 §18.5 已现读：<c>aiEnabled</c>／<c>canvasActions</c> 管⛔ 到左上那 16 行与「More tools」那 4 行⇒摘除要两颗一起） |

### 20.6 一处⛔ 对齐：那颗 <c>showOpenFilePicker</c>（dev 0 处 vs min 3 处）

现读：dev 语料里只有<b>那颗依赖的名字</b>（<c>chunk-3KPV5WBD.js:719954</c>：<c>import { fileOpen as _fileOpen, fileSave as _fileSave, supported as nativeFileSystemSupported } from browser-fs-access</c>），而 <c>showOpenFilePicker</c> 这串字面量只出现在<b>我那颗 min</b>（3 处，<c>@244657／249405／249439</c>，都在依赖正文里）。
⇒ 颗数差的解释＝<b>打包形状</b>：esbuild 把 <c>browser-fs-access</c> 整颗打进我那颗 bundle，而未混淆的 dev 分块把那棵依赖留给<b>各自的打包器</b>；这⛔ 是「版本不同」也⛔ 是「上游有两套代码」。
⇒ 一条纪律（进坑表）：<b>我那颗 min 与随包 <c>dist</c> 在依赖内联这一维上⛔ 同形</b>⇒凡「字符串在⛔ 在」类判据，两套都要数；而<b>结论只按随包那一套下</b>（落地批走的是那颗 <c>dist</c>），我这颗 bundle 只当交叉证据。
⚠ 顺带一格待量（⛔ 属本腿）：那条 <c>nativeFileSystemSupported</c> 分支在 WebView2 里到底走⛔ 走得通（走不通时引擎退回 <c>a.href + click()</c> 那颗下载口＝片8 §18.4 现读「档名⛔ 归网页」那一条）。

### 20.7 那颗「多窗冲突表」现在长什么样（引擎⛔ 立场，宿主全权）

| 格 | 现读零件 | 谁定 |
|---|---|---|
| 两份现状怎么合 | 包里<b>导出</b>了 <c>reconcileElements</c>（<c>index.d.ts</c> 逐字：与 <c>restore</c>／<c>serializeAsJSON</c>／<c>loadFromBlob</c> 同列） | 宿主调⛔ 调、什么顺序调 |
| 谁新 | <c>getSceneVersion</c>／<c>hashElementsVersion</c>（现读各 5 处）；那颗 <c>sceneVersion</c> 只在 <c>chunk</c> 里 1 处 | 宿主取哪颗当版本戳 |
| 撤销栈 | <c>CaptureUpdateAction</c>（<c>IMMEDIATELY</c>／<c>EVENTUALLY</c>，现读 157 处）＋<c>api.history.clear</c> | 「载档／桥回写算⛔ 算一次用户动作」＝宿主策略，默认要<b>清</b>栈（否则 Ctrl+Z 把用户的图退回空白板＝本仓定性过的坏） |
| 同档两扇窗 | 引擎<b>⛔ 任何跨窗机制</b>（20.4 那颗 0） | 「第二扇要开同一颗档」＝聚焦已有那扇（§十二 那条不变，⛔ 再指望引擎兜底） |

### 20.8 这一腿交掉／新开／还欠

- <b>交掉</b>：§18.9 那颗「<c>onChange</c> 实参位与那颗图数据多大」（三颗、<c>files</c> 第 3 位；图数据以 <c>BinaryFileData.dataURL</c> 活在 <c>files</c> 里⇒<b>整颗图以 base64 串进场景对象</b>）。那颗形状逐字（<c>types/excalidraw/types.d.ts</c>）：<c>BinaryFileData = { mimeType: ValueOf&lt;typeof IMAGE_MIME_TYPES&gt; | typeof MIME_TYPES.binary; id: FileId; dataURL: DataURL; created: number; lastRetrieved: number }</c>，其中 <c>DataURL</c> 是<b>带 <c>_brand:DataURL</c> 的牌号</b>（⛔ 随手塞一颗字符串），而 <c>lastRetrieved</c> 的注释原文写着「<i>我们用这颗 flag 决定要不要把没用到的文件从存储里删掉</i>」⇒ 引擎<b>留了</b>一套 GC 判据却⛔ 自带存储（20.4），那颗删除动作住在壳里（在不在包里本腿⛔ 判，登记候选）；§坑11 那三处猜测换成颗数；§十二 那条「禁用 <c>LocalData</c> 层」改口。
- <b>新开</b>：<b>形6</b>＝<c>onPaste</c> 只在<b>严格 <c>false</c></b> 时否决、抛错被吞（fail-open）；<b>形7</b>＝桥订阅那五颗⛔ 退订句柄⇒关窗后的回调要宿主自己掐；<b>形8</b>＝<c>excalidrawAPI</c> 传错只 <c>console.error</c>，屏幕上⛔ 读数（宿主接不上 API 时会表现成「按钮没反应」＝本仓定性过的坏）。  〔片11g 更正<b>形7</b>：那颗句柄<b>有</b>（<c>emitter.d.ts</c> 逐字 <c>on(...): UnsubscribeCallback</c>；api 那颗是表达式体箭头⇒原样交回；包自己的 <c>useEmitter</c> 就是这么摘的）⇒ 形状从「宿主自己掐」改成「宿主<b>必须用</b>那颗现成的退订句柄」。我那句「⛔ 一处 unsubscribe」错在<b>只认 <c>return</c> 关键字、⛔ 认表达式体箭头</b>（那颗 <c>on</c> 的实现体压根⛔ 写在 <c>dev/index.js</c> 里，是 <c>emitter.d.ts</c>＋那颗 hook 交的答案）。〕
- <b>⛔ 判</b>：那颗 <c>dataURL</c> 到底多大才把档撑爆（图进画板后 <c>.excalidraw</c> 的体积↔张数关系）；那颗 <c>nativeFileSystemSupported</c> 在 WebView2 的分支；§19.5 两臂照旧欠。
- <b>下一腿</b>：按 §八 该是<b>架构批</b>（桥协议＋存储适配器设计文档）——三颗输入现在齐了：钩子名单（§20.2）、存储面无主（§20.4）、官方开关（§20.5）；那张协议白名单要<b>照着 §20.2 写而⛔ 我自己发明命令名</b>。

### 20.9 本腿⛔ 动过

⛔ 起窗（闲置 3.3 秒）；⛔ 动产品源码、那颗 <c>excalidraw</c> 树、语言表、<c>待决策事项.md</c>、系统 PATH、网络配置；⛔ 装东西⛔ 新引包（四把尺都是仓内 python，读片6／片7 已落的那两颗档）；唯一改过的文件是 <c>docs/excalidraw整合可行性深析.md</c> 自己（新增一节＋就地补句，⛔ 删邻行）。

## 二十一、落地设计提案 v1（架构批·§八 那颗「第 1 批」的待评审稿收口）（批次 Phase C 片11，2026-10-17·⛔ 起窗的源码＋设计腿，⛔ 产品码⛔ 建工程⛔ 表⛔ 账本⛔ 装东西⛔ 改 PATH）

> 〔状态改口 2026-10-17〕v1 由「<b>待评审</b>」改「<b>已代裁开建</b>」：用户逐字令「电脑完全由你接管……为画布功能整合excalidraw」＋既有的决策委托（技术待决策项归实现侧，安全＞稳定＞性能、不扰正常使用）⇒ §九 那三颗拍板项由实现侧代裁（裁决块见 §九，各带理由与推翻入口）。他睡醒后推翻任何一颗＝改 §九 那条、对应腿重跑；其余腿⛔ 连坐。

> 这一腿新读的四把尺与正本＝<c>bin/WI83/spike4_83bw.py</c>→<c>spike4_probe_83bw.txt</c>（types 里那几颗签名的逐字）；<c>spike4b_83bx.py</c>→<c>spike4b_probe_83bx.txt</c>（props 那几颗钩＋导出／载入的形参）；<c>spike4c_83by.py</c>→<c>spike4c_probe_83by.txt</c>（<c>UnsubscribeCallback</c>＋那颗 api 类型体整颗＋运行时 25 颗键）；<c>spike4d_83bz.py</c>→<c>spike4d_probe_83bz.txt</c>（<c>emitter.d.ts</c> 整档＋包自己那份 <c>useEmitter</c> 的正件）。
> 语料＝同一颗 <c>@excalidraw/excalidraw@0.18.0/dist</c>：<c>types/</c> 361 档 <b>1 139 905 字符</b>（字符⛔ 字节，名册 328 那一族），其中 <c>types/excalidraw/types.d.ts</c> 一颗 26 215 字符；<c>dev/index.js</c> 那颗运行时同片10 那颗。
> ⚠ 这一腿<b>⛔ 起窗</b>：起窗第 0 道核对现读 <c>GetLastInputInfo</c> 闲置 <b>0.2 秒</b>（他在用电脑）⇒ §19.5 那两臂照旧挂「待跑」，本节所有「待量」都照实留着。〔片66 就地更正：那句话在本节写成时是真的，今天第①臂已由片13 量到（§二十二.1），第②臂的<b>前提</b>由片60 判掉（见 §二十八.1）——「⛔ 跑」与「前提消失」是两件事，别把它们读成同一颗欠项。〕
> v1 只做一件事：<b>把 §十二 每一颗名字换成包里读到的真名，并把「谁负责哪一格」写成一颗能直接开工的形状</b>。本节里出现的每颗形参与键名，在上面四份正本里都有逐字行；⛔ 一颗是我发明的。

### 21.0 形状：v0 那节的哪几颗被现读改掉了

| # | v0（§十二）写的是 | 现读 | v1 处置 |
|---|---|---|---|
| 1 | 命令表里那颗 <c>scene.save</c>（JS 喊宿主存） | 宿主能自己取：api 逐字给着 <c>getSceneElements()</c>（返回 <c>readonly Ordered&lt;NonDeletedExcalidrawElement&gt;[]</c>）／<c>getAppState()</c>／<c>getFiles()</c>／<c>getName()</c>，另有 <c>serializeAsJSON(elements, Partial&lt;AppState&gt;, files, type)</c>⇒<c>string</c>，那颗 <c>type</c> 只有 <c>&quot;local&quot;</c> 与 <c>&quot;database&quot;</c> 两档 | <b>摘掉那颗命令</b>：存的时机与形状全在 C# 侧（§21.3），JS 侧连「喊存」都⛔ 必留 |
| 2 | 命令表里那颗 <c>scene.dirty</c>（JS 递一颗 bool） | <c>onChange</c> 每次都把<b>整颗现状</b>交出来（三颗实参，§20.3） | <b>摘掉</b>：脏位＝宿主拿「上一份 <c>serializeAsJSON</c> 的字节 ↔ 当场那颗」对差，⛔ 再信一颗递来的 bool |
| 3 | 两份名单里⛔ 有「粘贴」那一档 | 唯一的否决口是 <c>props.onPaste</c>，且<b>只有严格 <c>false</c> 才拦得住</b>、钩子抛错被 <c>catch</c> 吞（形6，§20.3 那颗逐字） | <b>新增一颗命令 <c>paste.query</c></b>（那颗钩是 <c>await</c> 的⇒桥可以回话），并把「钩⛔ 许抛、拒要显式返 <c>false</c>」写成硬线⑤（§21.1） |
| 4 | 「web 内⛔ IndexedDB 写——<c>LocalData</c> 层禁用」 | 那颗层⛔ 在：六颗持久化面两套语料各 0 颗（§20.4） | <b>句子改口</b>：没有第二主人可禁；剩下的活只有「宿主写，且只宿主一个主人写」 |
| 5 | 「订阅那五颗⛔ 退订句柄⇒桥要自己摘」 | <b>我写反了</b>：<c>emitter.d.ts</c> 逐字 <c>on(...): UnsubscribeCallback</c>，注释原文写着 <i>@returns unsubscribe function</i>，而 <c>UnsubscribeCallback = () =&gt; void</c>；api 那颗又是<b>表达式体箭头</b>（<c>onChange: (cb) =&gt; this.onChangeEmitter.on(cb)</c>）⇒ 句柄原样交给宿主；包<b>自己的</b>消费者就这么用：<c>hooks/useEmitter.ts</c> 里 <c>const unsubscribe = emitter.on(...); return () =&gt; { unsubscribe(); };</c> | <b>更正</b>：桥<b>存那五颗句柄</b>、<c>Closed</c> 里逐颗调（硬线⑥）；点验单加一格「摘完之后⛔ 再收到桥消息」（§21.6） |
| 6 | 「Spike-1 要装 Node＋npm install＝那是要他点头的外部动作」 | §十六／§十七 已办：本机运行时本来就活（§16.1），打包全落在<b>⛔ 随仓</b>的 <c>bin/WI83/npm0e/</c>（整棵 <c>bin/</c> 在 <c>.gitignore</c> 内），⛔ 全局装⛔ 改 PATH⛔ 动那棵树 | <b>这颗从「欠他」移到「已办」</b>；§九 剩下三颗仍归他（§21.7） |
| 7 | 「<c>embeddable</c>／远程 iframe 在导入那一刻降级为占位」——⛔ 说走哪颗开关 | 包里<b>有官方闸门</b>：<c>validateEmbeddable?</c> 的类型逐字是 boolean／字符串数组／RegExp／RegExp 数组／函数 五档，另有 <c>embeddableURLValidator(url, validateEmbeddable)</c> | 落地＝<b>那颗 prop 交 <c>false</c></b>（第一档就是 boolean）；宿主侧 CSP 与 <c>WebResourceRequested</c> 照旧当<b>第二道</b>（§17.4 那句「⛔ 能靠我们这份包⛔ 有远程 URL 过关」仍兑现） |
| 8 | §12.5 那张「摘哪几颗」的名单 | <c>UIOptions</c> 逐字四颗：那颗 <c>welcomeScreen</c> 的注释原文是 <i>@deprecated does nothing. Will be removed in 0.15</i> | <b>欢迎屏那颗 prop 是死的</b>：产物里 <c>WelcomeScreen</c> 现读 28 颗（§17.3）⇒只能走 CSS／运行时摘 DOM／打包期 alias 三档之一（§18.5 那三档照旧；片16 现读：0.18 ⛔ 有 <c>renderMainMenu</c> 那颗 prop（bundle 与 .d.ts 都 0 颗），第一杠杆是宿主把 <c>MainMenu</c> 当 Excalidraw 的子元素排进去＝整颗换掉内置表，§二十五.2） |

### 21.1 桥协议 v1：那两份名单逐颗换成真名

信封⛔ 改：<c>{ v, cmd, id, args }</c>＋回执多两颗（<c>ok</c>／<c>err</c>）。方向由那颗 <c>cmd</c> 的名字表定，⛔ 再造字段（v0 硬线②「多一颗键就拒」原样保留）。

| 方向 | 名字 | args（逐颗点名） | 对侧动作 | 拒／降级形状 |
|---|---|---|---|---|
| JS→C# | <c>bridge.ready</c> | ⛔ 参 | 回 theme／lang／feature 三颗值（§21.5） | 名字表外的整条拒＋记日志 |
| JS→C# | <c>paste.query</c> | 那颗 <c>data</c> 的<b>形状标签</b>（<c>elements</c>／<c>spreadsheet</c>／<c>text</c>／<c>errorMessage</c>，§20.3 那四档）＋字节数 | 走 <c>ClipboardPolicy</c> 那三层敏感判据，回 allow／deny | 钩子内部抛错＝<b>宿主自己 catch 成 deny</b>（⛔ 让引擎那颗 fail-open 替我们做主） |
| JS→C# | <c>export.bytes</c> | base64 产物＋档位枚举（png／svg／scene）＋那块板的<b>显示名 <c>suggested</c></b>（<b>片22 上链</b>：可缺、空串＝用户没起过名⇒宿主那颗「id＋时刻」的兜底名才是主人；只有 png／svg 那一路读它，<c>scene</c> 那一路⛔ 读—档名＝那颗 id）。<b>片23 摘掉了本节原本写着的「那颗会话 id」</b>：三拍都递、名单在册、宿主解进 <c>BoardEnvelope</c> 之后一颗读者也没有——留着它＝硬线②那张越权面上多一颗没人读的键，也正是 ✅P-212 那句「注释承诺了一个不存在的消费者」在同一颗包里的第二颗（要它，等它有了读者再带着读者一起回来） | 解字节→由 C# 定名与落点；需要「贴／背景」的再转 §下两颗 | 档位⛔ 在枚举⇒拒；字节数与解码后⛔ 成对相等⇒报「桥坏」 |
| JS→C# | <c>pin.request</c> | 一颗<b>已发过的 token</b> | 走 ClipIMG-P3 那颗接合贴到桌面 | token 认不出／已回收⇒拒 |
| JS→C# | <c>background.request</c> | ⛔ 参（哪张图宿主自己知道） | 交冻帧或剪贴板历史图，回字节 | — |
| JS→C# | <c>open.external</c> | 一条 http(s) URL | 交 <c>LauncherEx.TryOpenAsync</c>（系统浏览器），窗内⛔ 导航 | 非 http(s)／带凭据／⛔ 在白名单⇒拒 |
| C#→JS | <c>board.load</c> | 那份档的字节（base64）＋那颗 id | 页面按 §21.3 那<b>三步</b>递进引擎 | 会话已 Closed⇒整条丢并记一颗 |
| C#→JS | <c>theme.set</c>／<c>lang.set</c>／<c>feature.flags</c> | 一颗值（三颗同形） | 页面重挂那颗 prop（⛔ 两份真相，§12.5 那颗口径） | ⛔ 值＝不应用并回错 |
| C#→JS | <c>export.pick</c> | 档位枚举＋那颗 <c>skipInliningFonts</c> 的 bool（那颗形参逐字见 <c>utils/export.d.ts</c>） | 页面调 <c>exportToBlob</c>／<c>exportToSvg</c> 后走 <c>export.bytes</c> | 档位⛔ 在枚举⇒拒；那颗 bool 的效果<b>本腿⛔ 量</b>（§21.7） |

<b>硬线六条</b>（① ～ ③ 是 v0 那三条的现读版，④ ～ ⑥ 是本腿新增）：
① <b>路径永远⛔ 进 args</b>——落点目录与命名由 C# 定；这条从此有<b>正件</b>：片8 递 <c>..\..\evil.png</c>／绝对路径／空名三颗，宿主一律 <c>Path.GetFileName</c> 收进 <c>bridge/</c> 且落点⛔ 出目录（§18.4 越权那一格）。
② 命令白名单＋args <b>字段</b>白名单（多一颗键就拒，⛔ 忽略）。
③ 那颗 <c>cmd</c> 与那颗 <c>id</c> 的配对表只活在宿主一侧；回执超时＝报错，⛔ 静默重试第二次副作用。
④ <b>⛔ 拿桥当第二条数据通道</b>：现读预算＝空载 p50 0.4 ms、1 MB 31 ms、8 MB 177 ms（§18.6）⇒控制消息按亚毫秒算，字节走「一次一问」那一臂。
⑤ <b><c>onPaste</c> 那颗钩⛔ 许抛、拒必须显式返 <c>false</c></b>（形6 的极性钉死）⇒宿主侧那颗判定包一颗自己的 <c>catch</c>，把「判不成」归到 deny 那一档。
⑥ 五颗订阅的<b>退订句柄存进那颗会话表</b>，窗 <c>Closed</c> 里逐颗调（§21.0 第 5 行的更正）；那颗「代数计数」只当兜底，⛔ 替代它。

### 21.2 那 25 颗旋钮：用 8 颗、⛔ 用 15 颗、候选 2 颗（8＋15＋2＝25，逐颗点名；<b>片22 把 <c>getName</c> 从「用」挪进「⛔ 用」</b>，理由见那一行）

| 档 | 键（api 逐字） | 为什么 |
|---|---|---|
| 用（8） | <c>updateScene</c>、<c>addFiles</c>、<c>getSceneElements</c>、<c>getAppState</c>、<c>getFiles</c>、<c>id</c>、<c>history.clear</c>、<c>onChange</c> | 载档／存档／脏位／栈治理各一颗，⛔ 一颗多余（<b>档名那一颗走 <c>getAppState</c> 的 <c>name</c> 字段本尊</b>，⛔ 另一颗出口，见 ⛔用 那一行）；那颗 <c>addFiles</c> 逐字收 <c>BinaryFileData[]</c>⇒宿主自缩自编的图正是它的入参（§21.3） |
| 候选（2） | <c>scrollToContent</c>、<c>updateFrameRendering</c> | 前者等<b>检索沉淀批</b>（§八 第 5 批）「跳到那颗元素」才要；后者它的注释逐字要求<b>配合 <c>props.viewModeEnabled</c> 一起用</b>⇒归「只读板」那一格，⛔ 单独开 |
| ⛔ 用（15） | <c>getName</c>、<c>updateLibrary</c>、<c>resetScene</c>、<c>getSceneElementsIncludingDeleted</c>、<c>registerAction</c>、<c>refresh</c>、<c>setToast</c>、<c>setActiveTool</c>、<c>setCursor</c>、<c>resetCursor</c>、<c>toggleSidebar</c>、<c>onPointerDown</c>、<c>onPointerUp</c>、<c>onScrollChange</c>、<c>onUserFollow</c> | 逐颗理由：云库／协作是 §七 非目标；<c>resetScene</c>＝一板一窗、关窗即散，⛔ 一颗「就地清空」的动作；带删除的那份现状⛔ 存进档（宿主只认那颗 NonDeleted 版）；<c>registerAction</c>＝⛔ 往引擎菜单里加动作（「一个主人」，§18.5 那张行名单由宿主⛔ 扩）；<c>setToast</c>＝我们有 RV-1 那颗自绘提示卡（引擎那颗住在 <c>appState.toast</c>＝现读形状）；替用户选工具／画光标＝观感，⛔ 代码批自己定；滚／点⛔ 一格读数需要（一板一窗⇒<c>detectScroll</c> 默认那颗就够）；<b><c>getName</c>＝片22 现读 bundle：`"getName",()=>this.state.name||this.props.name||`${b("labels.untitled")}-${Cp()}``⇒它永远⛔ 回空</b>。递它当桥上的 <c>suggested</c>＝宿主那颗兜底名在链路上永远走不到（＝片21 欠项⑤ 那一族换了个名字重犯）；而 §21.3 早就逐字写着显示名是 <c>appState.name</c>（<c>string | null</c>）——表上写着「用哪颗旋钮」⛔ 等于那颗旋钮的返回值形状 |

⇒ 一句话形状：<b>桥的命令表（§21.1）与旋钮表（本节）是两件事</b>——JS→C# 那六颗里没有一颗是「画板动作」，画板动作全走宿主侧那 8 颗旋钮；这样那张越权测试才⛔ 有第二道缝。

### 21.3 存储适配器 v1：四颗真签名钉住的形状

- <b>载档天生是三步，⛔ 是一把交</b>（§21.0 第 1 行那颗类型钉的）：<c>SceneData</c> 逐字只有 <c>elements</c>／<c>appState</c>／<c>collaborators</c>／<c>captureUpdate</c> 四颗字段、<b>⛔ <c>files</c> 那一颗</b> ⇒ 序列＝档字节 → <c>loadFromBlob(blob, localAppState|null, localElements|null, fileHandle?)</c> → <c>updateScene({elements, appState, captureUpdate: NEVER})</c> → <c>addFiles(Object.values(files))</c>。v0 那句「updateScene 一把交」就地作废。
- <b>存</b>：<c>serializeAsJSON(getSceneElements(), 那颗 Partial&lt;AppState&gt;, getFiles(), &quot;local&quot;)</c> ⇒ 一颗 <c>string</c>。那颗第四颗形参逐字只有两档，桌面档<b>定 <c>&quot;local&quot;</c></b>（<c>&quot;database&quot;</c> 那一档归协作壳，非目标）。写盘走本仓既有那条原子写（读档⛔ 截零）。
- <b>图</b>：宿主自己缩到上限、自己编成 <c>BinaryFileData</c>（那颗 <c>dataURL</c> 带 <c>_brand</c>，⛔ 随手塞字符串）交 <c>addFiles</c>＝§19.3 出口①；两颗上限（长边与字节）<b>自己给</b>（形5：那条出口同时跳过引擎的 1440 减重）。那颗 <c>generateIdForFile?: (file: File) =&gt; string 或 Promise&lt;string&gt;</c> 也够宿主把 id 钉死（§19.3 现读）。
- <b>内嵌 分颗：定＝内嵌</b>（一颗档一板，图跟着 <c>files</c> 走 base64）。三条理由：引擎那颗 serialize 自带这个形状；分颗＝第二套身份＋第二个主人；备份链复用那颗「含图片」的开关（§坑23：⛔ 加新键）。代价照实写：一张 4K 图的 base64 是几百 KB 级，而<b>「体积↔张数」这条曲线⛔ 量过</b>（§20.8）（<b>片15 追记</b>：曲线已量－空档 237 字符、照片级 1440 每张 +0.32 MiB、噪声 1440 每张 +9.08 MiB、dataURL 字符＝字节 × 4/3，见 §二十二.3；<b>但曲线⛔ 等于上限</b>——允多大那颗数仍归他）⇒代码批第一刀＝先把曲线量出来再给上限，⛔ 先写常数（本仓那条「量程收成一处」的旧规矩）。<b>【片58 结案】</b>曲线与那颗数都到位了：上限住 <c>BoardPolicy.SceneBytesCeilingBytes</c>＝8 MiB，出处是四档真机读数（§二十二.3 末那条追记），闸门同时换成「自给的上限必须带一句盘上真有的正本」；⚠ 宿主这一侧交图那条路（§19.3 出口①「宿主自己缩到上限再编 <c>BinaryFileData</c>」）今天<b>还⛔ 接</b>——这颗上限管的是<b>进来的字节</b>，⛔ 是「我们会替谁压」，长边那一半另立 P-246。
- <b>那颗 <c>lastRetrieved</c></b>：注释原文说它用来决定删⛔ 用的文件，而「删」这个动作在包里<b>0 处</b>（§20.4）⇒ v1 明写：宿主<b>⛔ 拿它当删除依据</b>；那颗字段照引擎形状存，⛔ 自己加字段（＝漂移面）。
- <b>四拍 autosave</b>：停笔 1.5 s／窗失焦／<c>ProcessFailed</c> 之前／<c>Closed</c> 之前；另存一颗 <c>.bak</c>（只留最近一颗）；对账三分类⛔ 静默删（ClipIMG-1e 那一族直接复用）；未保存离开＝就地三颗按钮（存／丢／留下决定），⛔ 把「请先保存」写成用户作业（P-54）。
  <b>【片24 落码＝四拍都接上了线；片25 真机读数：其中两拍⛔ 通】</b>⚠ <b>就地改口</b>：这里原本写的是「四拍全通」——那句话只有<b>源码读数</b>（事件挂上了、分档判对了），片25 在沙盒里把四拍逐颗真机跑了一遍，<b>失焦</b>与<b>关窗</b>两拍通，<b>停笔</b>与<b>崩</b>两拍均⛔ 通（新条 <b>P-221</b>、<b>P-222</b>，读数 <c>bin/WI85/readings_25.txt</c>）。停笔那一拍由<b>页面</b>发起（宿主没得问）；失焦那一拍由宿主问（<c>export.pick</c>＋<c>kind:"scene"</c>）。<b>崩</b>那一拍<b>问不到东西</b>——那一刻页面已经死了，所以 §21.5 给它派的活是恢复而⛔ 是索取；<b>关窗</b>那一拍走 <c>AppWindow.Closing</c>：拦一次、窗先 <c>Hide()</c>（用户⛔ 等那两秒）、向页面索取一份快照并<b>有界</b>等它落地，到点照样关。那颗预算＝<c>BoardPolicy.CloseFlushBudgetMs</c>＝<b>2 000 ms</b>，来历是 §18.6 那颗往返表（控制消息 p95 0.7 ms、1 MB 单程 31 ms、8 MB 177 ms）——它同时是「关掉一扇画板」的可感上限。<b>⛔ 无上限＝那颗网页卡住时，用户的退出键被抵押在它手上</b>（P-54 那句口径的反面）。<b>⚠ 还有一条路⛔ 通</b>：从<b>主窗</b>退出走的是 <c>Environment.Exit(0)</c>，它⛔ 经过各扇窗的 <c>Closing</c> ⇒ 那一途上「关窗前那一拍」一次也走不到（新条 <b>P-220</b>：先给画板窗一位主人，再谈那一途）。<b>【片30 落地＝P-220 结案】</b>那位主人立成了：<c>BoardsWindows</c> 那份名册由 <c>BoardWindow</c>自己成对登记，收尾链<b>第一步</b> <c>await BoardsWindows.FlushAllForExitAsync()</c>（排在关组件之前＝那颗 5 秒硬退出看门狗只剩后面那一段要抢），<b>并发</b>等每扇各自的预算（⛔ 逐扇串＝把那颗预算乘上扇数），两条来路<b>共用同一颗</b>有界等待、差别只在⛔ 拦关闭与⛔ 贴卡。真机两臂各留一份正本：改前主窗臂那颗板压根没落盘，改后同一腿读到 <c>boards.close.flush.exit landed=True</c> 落在 <c>退出：开始收尾</c> 之后 <b>7 ms</b>（单扇）；<b>⚠ 落到盘的是空板</b>⇒带笔迹那一发换出新条 <b>P-229</b>。<b>【片31 结案＝✅P-229，⛔ 动一行业务码】</b>那一发补量到了：同一块板先接住那颗 2 元素种子档（<c>STARMARK_START_BOARD=last</c>＋<c>hasScene=True</c>），在画布上拉一笔，抬笔 <b>0.08 s</b> 之后关<b>主窗</b>⇒盘上那颗档 <c>858/2 颗 → 2708/3 颗</c>，新进那颗逐字 <c>freedraw</c>、包围盒 <c>316.0×105.0</c>＝这一笔按窗口 1440×879 折算的数；回音 <c>[2026-10-19T11:51:24.3028664+08:00] INFO boards.close.flush.exit id=261019-114119-a1b2 landed=True</c> 距收尾 <b>10 ms</b>、<c>落地 1 扇</c>、退出码 <c>0</c>。那颗 <c>landed=True</c> 单独⛔ 算验过（写成功／盘上本来就是它／兜底放行三档它都真）——判据要落在<b>元素颗数＋那颗新元素的几何</b>上。「未保存离开＝就地三颗按钮」照旧欠着——那一拍问不到东西时，程序能做的只有「照关＋日志」，三颗按钮要等能<em>停下来问一句</em>的那一批。
- <b>身份与命名</b>：文件名＝那颗稳定 id（<c>yymmdd-HHMMss-4hex</c>，v0 同）；<c>appState.name</c>（逐字 <c>string | null</c>）只当显示名，⛔ 当身份；板名是用户数据⇒⛔ 进语言表，而宿主自己那几句上屏话一律 <c>LocMessage</c>（界面语言那条线的口径直接适用）。
  <b>【片25 真机读数，四拍逐颗跑过一遍】</b>① <b>关窗</b>那一拍：三跑各把盘上那颗文件的元素颗数多写一颗（2→3→4），窗口正常消失、<b>⛔ 未处理异常</b>；② <b>失焦</b>那一拍：落笔后 2.5 s 盘上⛔ 动（因为停笔拍死着⇒归因干净），把前台换到主窗之后 <b>0.6 s</b> 落盘；③ <b>载档回读</b>（<c>STARMARK_START_BOARD＝last</c> 那一途，本批为取证新加的那颗钩子）：重开之后那两笔真回到画布上（暗像素尺 3645，照片 <c>shot_25_loadback.png</c>）；④ <b>停笔</b>那一拍：<b>三臂 bisect 定位到一行</b>——原样 12 s ⛔ 写、只跳去抖（那颗 if 留着）12 s ⛔ 写、连 if 一起拿掉 <b>0.50 s 就写</b> ⇒ 断点＝<b>页面侧把 <c>onChange</c> 的位置参数当成了「事件对象」，那颗判据恒假</b>（<b>P-221</b>）；⑤ <b>崩</b>那一拍：日志逐字给出 <c>kind=RenderProcessExited tier=ReloadPage</c> 与 <c>kind=BrowserProcessExited tier=RecreateWeb</c>，<b>新 renderer 与新浏览器根各自都起来了</b>，可两条路最后停在<b>同一颗白帧</b>（同一时刻两路拍照各读一次：<c>PrintWindow</c> 暗像素 0 与屏幕 DC 拷贝 0；杀之前两路都是 1787，一字不差）⇒ ⛔ 是尺造的白，而 <b>§21.5 那句「没一颗档位是把画板退成空白」被违反</b>（<b>P-222</b>）。⚠ 两条恢复路停在同一颗帧＝这一格⛔ 是「那一手选错」那么简单：片26 第一刀只给 <c>bridge.ready</c>／<c>NavigationCompleted</c>／<c>Source</c> 加日志把因由判成一颗，⛔ 直接换手。
  <b>【片26 落地腿＝④ 那颗停笔拍通了；⑤ 崩那一拍的真因量到并改掉】</b>④ 的修法⛔ 是"把那颗 if 修对"，是<b>把开闸的权从页面挪回宿主</b>：桥上新增一颗进向 <c>board.loaded</c>（<b>必带两颗数</b> <c>elements</c>／<c>canvas</c>，缺任何一颗＝<c>MissingField</c>），页面交回"档里数到的"与"当场从画布上数回的"，宿主拿<b>自己递进去的那颗可写颗数</b>与之比<b>三颗全等</b>才递 <c>autosave.arm</c>（<c>BoardPolicy.ShouldArmAfterLoad</c>）。真机读数：健康那一跑 <c>want=3／loaded=3／canvas=3</c> 之后 <b>2.01 s 盘上多一颗</b>（3771／3 → 4936／4），修后两跑 0/0/0 与 1/1/1 同样 2.01 s 落盘（⚠ 那颗是"松手→盘上多一颗"的整段，⛔ 等于页面 <c>AUTOSAVE_MS=1500</c> 那颗去抖）。⑤ 的真因＝<b>同一颗 DOM 容器上被造了第二棵、第三棵 React 根</b>（<c>theme.set</c>／<c>lang.set</c>／<c>feature.flags</c> 各走一遍「造新根再 render」）⇒ <b>§21.5 那句"⛔ 许回到空板"与"⛔ 许静默白屏"被同一只手违反</b>，而这颗坏桥两侧都⛔ 报错。逐颗读数、口径那一半的教训与两道闸门＝<b>§二十七</b>；剩下的那一半（崩后 t+2 那 ≈2 s 白帧、拒绝路径⛔ 上屏说话）＝<b>P-224</b>。
- <b>那颗「根」的主人（片23 落码）</b>：§21.3 那句「路径⛔ 是判据」判的是<b>命名与落点归谁</b>，而「用户数据住在哪」这件事本仓早就有主人（<c>UserDataPaths</c>：<c>STARMARK_SETTINGS_PATH</c> ＞ <c>STARMARK_DB_PATH</c> 的目录 ＞ <c>%APPDATA%\StarMark</c>）。板档目录现在问它要那颗子目录（<c>Subdirectory("boards")</c>，一颗<b>目录</b>出口与既有那三颗<b>档</b>出口同源），导出目录只在板目录下面再切一刀 <c>exports\</c>——画板那一族的档里⛔ 再拼一遍 <c>%APPDATA%</c>。<b>为什么值得为"整齐"动手</b：那份优先规则管不到的那一族⛔ 报错，它只是<b>永远写进真目录</b>，于是沙盒取证一跑就把他真目录里的 <c>boards\</c> 写脏——批次 SS 撞出来的正是同一件事（那颗坑的第五份抄本）。⚠ 那颗导出目录的<b>归属</b>还没裁：v0 §12.1 那张表写着「落『图片\StarMark 截图』同目录族」，v1 本节只把这句接成「由 C# 定名与落点」，而代码选了应用数据目录下面——用户从那儿<b>取不到</b>那张图，而界面上⛔ 一颗出口告诉他在哪 ⇒ <b>新条 P-219</b>（今天那颗路还⛔ 通：宿主只问 <c>kind:"scene"</c>，png／svg 的字节进不来，所以这一条归「接导出入口」那一批先裁再写）。

### 21.4 撤销栈、脏位、多窗（那颗 <c>captureUpdate</c> 注释逐字之后）

- <c>updateScene</c> 那颗 <c>captureUpdate</c> 的注释逐字给了三档，并明写 <b><c>@default CaptureUpdateAction.EVENTUALLY</c></b> ⇒ v1 定死：<b>载档与桥回写交 <c>NEVER</c>，用户动作交 <c>IMMEDIATELY</c></b>，另配 <c>history.clear</c>（types 逐字＝<c>App[&quot;resetHistory&quot;]</c>）。§20.7 那句「默认要清栈，否则 Ctrl+Z 把图退回空白」落在这两颗上。
- 脏位＝宿主那份字节对差（§21.0 第 2 行）；⛔ 另造计数器，⛔ 让 JS 递 bool。<b>【片22 落码】</b>＝<c>BoardPolicy.DiskAlreadyHolds(上一份, 这一份)</c>：逐颗相等⇒⛔ 写也⛔ 滚 <c>.bak</c>（滚一次就把上一份<b>真正不同</b>的退路顶成同件，代价落在用户数据上）。那颗「上一份」由宿主自己持，而<b>从 <c>.bak</c> 捡回来的那一档⛔ 点亮</b>——主档位置是空的，<c>LatestSceneId</c> 只认主档；「读的是主档还是备份」那颗旗由 <c>BoardSceneStore.TryLoad</c> 交回，UI 侧⛔ 自己再摸一遍盘＝第二个主人。
- 同档两扇窗：聚焦已有那扇（v0 不变，引擎<b>⛔ 跨窗机制</b>＝§20.4）；若将来真要把两份合，现成签名是 <c>reconcileElements(localElements, remoteElements, localAppState)</c> ⇒ <c>ReconciledExcalidrawElement[]</c>，版本戳现读 <c>getSceneVersion: (elements) =&gt; number</c>。

### 21.5 生命周期／崩溃／那颗注入面（片8 那五颗读数直接接进来）

- 一板一窗、按需建、<c>Closed</c> 里 <c>Dispose</c>；兜底＝按<b>自己那颗 pid 的子树</b>清 <c>msedgewebview2</c>（⛔ 杀不属于自己的进程）。这条从断言升成<b>可复现</b>：跑中按那颗沙盒 <c>--user-data-dir</c> 归因 7 颗、关窗后 0 颗、全局回到 12（§18.7）。
- <c>userDataFolder</c> 的验收格<b>写成现读那层</b>：<c>&lt;udf&gt;\EBWebView\Default\Local Storage</c>（⛔ 按 <c>&lt;udf&gt;\Default\…</c> 找＝名册 334 那颗坑）。
- <c>ProcessFailed</c> ⇒ 事件桥报＋<b>当场那份自动落盘</b>（§21.3 四拍之一）＋重载＋<c>board.load</c> 把刚那份递回去；重载之后⛔ 许回到空板，也⛔ 许静默白屏（§坑10）。
  <b>【片24 落码·就地改口】</b>那句「当场那份自动落盘」<b>做⛔ 到</b>：事件递到的那一刻渲染进程已经没了，宿主手上<b>没有</b>比盘上更新的一份可写（最近一次交回＝上一次 <c>export.bytes</c>，它已经在盘上或被 <c>DiskAlreadyHolds</c> 判成同一份）。这一拍真正付的钱是<b>恢复</b>，而恢复按档分（<c>BoardPolicy.RecoveryForFailure</c>，出处＝钉住那颗包 <c>Microsoft.Web.WebView2.Core.xml</c> 的逐字口径，1.0.3719.77 现读十颗档位）：<c>BrowserProcessExited</c>＝<b>重建控件</b>（官方逐字「The app has to recreate a new WebView to recover」）／<c>RenderProcessExited</c>＝<b>重载那一扇页</b>（「You can use the <c>Reload</c> method」）／<b>其余八档＝只上报</b>。八档里最该记住的是 <c>FrameRenderProcessExited</c>：官方写它「⛔ 影响顶层文档，只有部分子帧换成错误页」——<b>一重载就把用户整块板退掉了</b>，「看见进程没了就重载」听起来稳妥，恰好是最不稳妥的那一颗。名单<b>外</b>的名字（将来 SDK 多一档）一律退到只上报：<b>认不出来时的默认动作必须是"什么也⛔ 毁"</b>。重建与首开走<b>同一条</b>开路（两份开场代码＝第二个主人，第一号坏法是其中一份忘了递档），桥第二次 <c>ready</c> 时递进去的是<b>宿主见过的那一份</b>（<c>_seenScene</c>：首开与重载共用、写失败也算见过）而不是"盘上那份"（那颗是脏位的主人 <c>_lastWritten</c>，两问⛔ 同一问）。⚠ 本批读码读出的一颗真缺陷：<b>那颗"桥通了"的旗留在 true</b> 时，下一次失焦就会往死控件 <c>PostWebMessageAsJson</c>（COM 异常当场抛在 UI 线程上＝"画板自己崩给我看"）⇒ handler 第一件事是把它灭掉，这也是这一拍⛔ 能⛔ 装的最低要求。
  <b>【片26 读数·就地改口】</b>本批先按片25 那一刀把三处日志装上（<c>bridge.ready</c>／<c>NavigationCompleted</c>／<c>Source</c>），三条候选<b>各都量到"真发生了"</b>：<c>nav.done seq=1 ok=True</c>、<c>src</c> 就是那块板、二次 ready 与 <c>board.load</c> 都送达并回了话⇒因由⛔ 在这三颗里，而在<b>页面内部</b>：那棵第二根每挂一次就把宿主刚递进去的那块板拆掉。改成"那棵根只生一次"之后（<c>rerenderWith</c> 复用同一棵 root）：renderer 臂两跑崩后 <b>t+4／t+8 两路都读回崩前那一帧</b>（1497／1632 颗暗像素，与 base 同一颗 sha），root 臂（<c>RecreateWeb</c>）三拍两路都有内容。⚠ <b>"⛔ 许静默白屏"这一条只做到一半</b>：崩后 t+2 那一颗白帧仍在（换帧那一下 ≈2 s），且恢复与拒绝过程依然⛔ 上屏说话⇒登记成 <b>P-224</b>（默认处置＝先重试一次递档、仍不对就在画板窗里给一句话加一颗按钮）。<b>【片27 落地·就地改口】</b>那两半都做完了：拒之前由宿主<b>自己再递一次</b>档（额度＝<c>BoardPolicy.ArmRetryBudget</c>＝1，认不出那颗数＝⛔ 再递），仍对不上就在画板窗里贴那句话＋一颗"另存一份到别处"；决定动手恢复的两档（重载／重建控件）当场贴"这块板正在恢复"，而那句话只跟着<b>真递出去</b>的那一下；三圈用尽那档反过来——关掉写闸、说出"这块板没能回到画布上"、给出那颗按钮；关窗那一拍超时走右下角那张卡（窗在这一拍开头就 <c>Hide()</c>，画板里的面板⛔ 来得及看见）。全貌见 §27.6。
- 那颗键位共存：包侧 <c>handleKeyboardGlobally</c> 默认 <c>false</c>（§20.5 两处消费点）⇒引擎⛔ 抢 window 级键盘；仍欠两格量（画板焦点下 F1 那颗全局热键、以及 <c>Ctrl+S</c> 那一类浏览器级键），列进 §21.6。
- 首帧预算＝现读 <b>≈960 ms</b>（r3／r5 同档，§18.7）⇒「关窗即散」那颗体验说法以此为准（这是 §21.7 那颗拍板的材料，⛔ 是结论）。
- 出机三道：宿主注入 CSP；<c>WebResourceRequested</c>（现读那颗 filter 连 iframe 的 <c>Document</c> 请求都盖得住，§18.1）；<c>esm.sh</c> 永久 451 当最后一道——<b>⛔ 因为「配了那颗全局」就摘掉它</b>（形1′：那颗兜底永远排在名单最后，§19.2）；片16 补一颗：菜单那一路<b>⛔ 查动作注册表</b>（<c>executeAction</c> 收的是对象本身）⇒「同名 <c>registerAction</c> 改道菜单行」⛔ 成立——接管要么走 <c>UIOptions</c>（<c>canvasActions</c> 深合并、逐颗关），要么走宿主自排 <c>MainMenu</c>（§二十五.1／.3）。

### 21.6 §十 那张点验单：v1 从此有出处的格、新增的格

| 格 | 状态 |
|---|---|
| 导出四路各一遍→档名与路径归 C# | <b>有正件</b>：§18.4 (a)(b) 两臂成对相等（28 759 ↔ 28 759），且网页给的档名<b>会被丢</b>（<c>a.download</c> 被无视⇒<c>blob:</c> 的 UUID） |
| 桥越权（保存任意路径／命令／iframe url） | 硬线①②＋§18.4 那颗越权正件；<b>新增</b>一颗「伪造 <c>paste.query</c> 的 data 形状」 |
| 旧 embeddable 场景打开：iframe ⛔ 加载远程 | 新增官方出口＝<c>validateEmbeddable:false</c>（§21.0 第 7 行），宿主两道照旧；⚠ 「占位＋警告文案」那句仍要<b>我们自己写</b>（§18.9 现读：拦下来是颗空框） |
| 场景回环：保存→重开→undo 栈清空 | 从此有名字：<c>captureUpdate:NEVER</c> ＋ <c>history.clear</c>（§21.4） |
| 关窗 60 秒后进程树＝0／userData 落点＝改道后目录 | §18.7 已在一次性宿主上复现；落地批在同一颗产品窗上<b>再跑一次</b>（⛔ 拿片8 那跑顶替） |
| <b>新增</b>：摘完之后⛔ 再收到桥消息 | 硬线⑥那颗退订句柄；形状＝关窗后 5 秒内桥计数＝0（原 §20.2 那格由「宿主自己掐」改锚到「用现成的句柄」） |
| <b>新增</b>：拖一颗<b>照片味</b>的 4K PNG | ＝§19.5 第②臂：1440 之后进不进 4 MB（形2 到底挡谁）；同跑顺手量「体积↔张数」那颗曲线（§21.3 那颗上限的前置） |
| <b>新增</b>：断网全字体验证 | ＝§19.5 第①臂（那颗全局设／⛔ 设两跑）＋<c>WebAssembly.instantiate</c> 起没起＝§19.4(b) |

### 21.7 本节⛔ 判的（⛔ 从表里消失）与归属

- <b>归他三颗（口径与观感）</b>：§九 决策 2 生命周期档位（材料＝960 ms 首帧＋关窗归零可复现）；决策 3 Xiaolai 随⛔ 随包（209 分片／12.08 MB，且「随包＋那颗全局」两颗一起，§19.6 已改口）；决策 4 TTD／mermaid（那颗 alias stub 臂省 <b>41.8%＝3 497 832 颗字节</b>，代价＝「mermaid→画板」还能不能用⛔ 判，§17.3／§17.7）。
- <b>窗口面四颗（我跑，归我）</b>：§19.5 两臂；- <b>窗口面四颗（我跑，归我）</b>：<b>【片15 追记：四颗全部已跑完，读数在 §二十二 与 §二十三】</b>——其中 native FS 那颗的读数是<b>「门是开的」</b>（§二十二.6）⇒ 接管表要加一行「⛔ 管住网页自己那颗 picker」。原四颗：§19.5 两臂；<c>nativeFileSystemSupported</c> 在 WebView2 的分支（§20.6）；那颗 <c>skipInliningFonts</c> 的效果（§21.1 最后一行给它留的档）；菜单展开态那张照片（§18.5 那颗截图尺副作用）。
- <b>⛔ 量的一颗（写清楚，别当成设计）</b>：内嵌 base64 之后「一颗板多大 ↔ 几张图」的曲线——本节只<b>定了方向</b>（内嵌），上限那颗数<b>欠着</b>，代码批第一刀补。上限那颗数<b>欠着</b>，代码批第一刀补。<b>【片15 改口】</b>曲线已在片13 量出（§二十二.3），欠的变成两颗：<b>容差</b>（允许多大，归他）与<b>「打到没随包的字」那一档的现场</b>（归我，要先定一张常用字表）。
- <b>v1 状态＝待评审</b>〔片66 就地更正：这一行与本节 :1098 那句「已代裁开建」互相打脸，本档里那句旧话⛔ 删、只在这里改口：<b>v1 的状态＝已代裁开建，且片12～片65 已把它逐段落成代码</b>；今天看 v1 请以 §二十八 那份对账为准〕。评审过之前，§八 的第 2～6 批一颗⛔ 开工；评审后<b>第一颗交付＝§21.6 那张随包／接线清单</b>（宿主 HTML 那一行全局＋那颗虚拟站＋三道出机），⛔ 是 UI。

## 二十二、Spike-1b 第二腿（起窗现读 r6＋r7）：那颗 asset 开关一臂之差＝导出档自不自足、档体积↔图张数那条直线、中文在画布上认的是系统字面（批次 Phase C 片13，2026-10-17·一次性起窗＋只读取数，⛔ 产品码⛔ 建工程⛔ 改系统 PATH⛔ 装工具链⛔ 表⛔ 账本）

> 这一腿只答 §十九.5 欠的那四格，加一格新撞出来的（那颗字体子集化 worker）。跑法＝同一颗 <c>spike1b.exe</c>（⛔ 重编），r6 五臂＋r7 两臂，每一臂一颗进程、一个 <c>out_r6x/</c>·<c>out_r7x/</c> 目录、一张 <c>page_report.json</c>。两臂之间的差别只写在 <c>--query</c> 里（<c>&arm=…</c>／<c>&set=0|1</c>），那颗 <c>EXCALIDRAW_ASSET_PATH</c> 在 <c>index4.html</c>·<c>index5.html</c> 里、在 bundle <b>之前</b>落。全程 <c>--blocked 1</c>（宿主把外网掐了）。

### 22.1 那颗 asset 开关：两臂一字之差，差的是「导出的档自不自足」

| 读数 | <c>set=0</c>（⛔ 设） | <c>set=1</c>（设成 <c>"/"</c>） |
| --- | --- | --- |
| 起窗 | 965 ms painted | 965 ms painted |
| 资源面（<c>performance</c>） | 8 颗：<b>7 本地＋1 打 esm.sh</b> | 8 颗：<b>8/8 本地</b> |
| 那颗 <c>Virgil</c> 字面族 | <c>error</c>（NetworkError） | <c>loaded</c> |
| <c>exportToSvg</c> 默认档 | 651 字符、<b>1 颗 esm.sh 绝对地址</b>、0 颗 <c>data:</c> 字体、等了 12 ms | 2614 字符、0 颗 esm.sh、<b>1 颗 <c>data:</c> 字体内联</b>、等了 146 ms |

- 结论一句话：⛔ 设那颗开关时，画布<b>看起来</b>照常起（拉丁字走了系统回退，截图 <c>r6a_asset_unset.png</c> 与设了的那张只差手写体），而<b>导出的 SVG 里带着一颗断网机器打不开的绝对地址</b>——那张档发给同事就是一张坏图。设成 <c>"/"</c> 之后同一颗导出变自足（字体内联）。⇒ §十七 那条「随包字体不是可选装饰」在这一腿拿到了<b>导出面</b>的第二条证据：它同时管着「屏幕上长什么样」与「交出去的档能不能看」。
- 那颗 <c>esm.sh</c> 的地址是<b>包写死的</b>（<c>https://esm.sh/@excalidraw/excalidraw@0.18.0/dist/prod/fonts/Virgil/Virgil-Regular.woff2</c>），⛔ 我的代码拼的；<c>attempts</c> 那一栏在 <c>set=0</c> 臂里记到 <c>fetch</c>＋<c>fetch:ERR</c> 各一颗 ⇒ 网络面普查那把尺（§十八）在设了开关之后<b>一颗远程都不报</b>（<c>remoteHosts: {}</c>）。

### 22.2 新撞出来的一颗：那颗字体子集化 worker ⛔ 有 URL

- <c>set=1</c> 与 r7 两臂的 <c>errors</c> 里都有同一句：<c>Failed to use workers for subsetting, falling back to the main thread. WorkerUrlNotDefinedError: Worker URL is not defined!</c>
- 现场读数：那颗 <c>console.error</c> 出现时功能<b>没坏</b>——SVG 照样导出、字体照样内联，只是<b>从 12 ms 变 146 ms</b>（同一颗导出，两臂之差里混着这一颗）。⇒ 这是一颗「⛔ 给 worker 落 URL ⇒ 回落主线程」的降级，⛔ 是缺陷；但它是那条 4 MB 墙之外<b>第二颗</b>「随包要带的东西其实还有一份 JS worker」的线索。落地时要问的是：那份 worker 文件在包里叫什么、要不要一起随包（§22.10 欠项①）。

### 22.3 档体积 ↔ 图张数：那条曲线是<b>直线</b>（§21.7 那颗 <c>ImageCeilingsMeasured</c> 等的就是这几颗数）

现读（<c>serializeAsJSON(elements, appState, files, "local").length</c>，字符数；一颗 <c>dataURL</c> 就是档里那颗图的<b>全部</b>正文）：

| 输入 | 单颗 PNG 字节 | 单颗 dataURL 字符 | 1 张 | 2 张 | 5 张 | 10 张 | 每张增量 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 空档（⛔ 图） | — | — | 237 字符 | — | — | — | — |
| 照片渐变 1440×1440 | 247,668 B（0.24 MiB） | 330,246 | 331,412（0.32 MiB） | 0.63 | 1.58 | 3.16 MiB | <b>+0.32 MiB／张</b> |
| 噪声 1440×1440（最坏档） | 7,135,503 B（6.8 MiB） | 9,514,026 | 9.07 MiB | 18.15 | — | — | <b>+9.08 MiB／张</b> |
| 噪声 720×720 | 1,784,604 B（1.7 MiB） | 2,379,494 | 2.27 MiB | — | 11.35 | 22.7 MiB | <b>+2.27 MiB／张</b> |

- 三件事照实记：①<b>严格线性</b>，固定开销只有那 237 字符（空档），⛔ 任何「多张图共用一份正文」的机制；②那颗 <c>dataURL</c> 的字符数＝字节数 × <b>4/3</b>（三档输入各自都对得上，1.3333），所以「档多大」＝「图字节多大 × 1.33 × 张数」，一句话就能算；③引擎报的那颗 4 MiB 单文件上限<b>只在不可压缩那一档咬人</b>（照片级 1440 图 0.24 MiB 远不到，噪声 1440 图 6.8 MiB 直接超）⇒ 那颗上限⛔ 是「宽不宽」的问题，是「谁的图会撞上」的问题。
- 引擎<b>⛔ 动</b>宿主交进去的字节：<c>addFiles</c> 之后 <c>getFiles()</c> 里那颗 <c>dataURL</c> 与我交的<b>逐字节相同</b>（<c>sameAsSent: true</c>，三档输入都是），⛔ 缩放、⛔ 重编码、⛔ 摘 EXIF。⇒ §21.7 那句「上限必须由宿主自己守」现在是读数而⛔ 是推断：<b>引擎⛔ 会替我压</b>。
- 另一颗形状：同一张图改走 <c>updateScene({files: {…}})</c> 时，<c>getFiles()</c> 里<b>一颗都没有</b>（<c>keptChars: 0</c>，那颗元素停在 <c>status: "pending"</c>）。⇒ 宿主交图这条路是 <c>addFiles</c>；<c>updateScene</c> 的 <c>files</c> 参数按我交的那个形状没收到（⛔ 据此断言「该 API 无此能力」，只记「这条路⛔ 通」）。
- <b>【片58 追记：那条曲线换成了那颗上限，而它⛔ 等于「字节」那一半】</b>本节那颗「允多大」的数现在有主了：<c>BoardPolicy.SceneBytesCeilingBytes</c>＝<b>8 MiB</b>（一发进向字节，解<b>之前</b>判）。出处⛔ 是斜率换算，是另一把尺——「档体积 ↔ 那一拍按住 UI 线程多久」在真机上量出来：改前（停笔那一拍）垫 65536／2097152／8388608 颗字符＝盘上 67131／2098752／8390208 B，UI 线程拒收 <b>15.6／41.2／100.5 ms</b>（正本 <c>bin/WI98/readings_58_r4_210541.txt</c>）；改后（关窗那一拍）四档 <b>15.1／45.4／86.7／273.6 ms</b>，宿主那一拍 <c>tookMs</c> <b>182／305／631／1769</b>（正本 <c>bin/WI98/readings_58z_ladder2_221131.txt</c>）。⇒ 8 MiB 那一发 <c>631</c> ≈ 那颗 <c>CloseFlushBudgetMs</c>（2000）的三分之一，25 MiB 那一发已 <b>1769</b>＝预算的 88%（再往上就是丢那几笔）。⚠ <b>本节那条"dataURL 字符＝字节 × 4/3"现在被用成了判据</b>：那颗上界就按 <c>(颗数/4)*3</c> 算（⛔ 扫填充⇒边界上最多多算 2 颗字节）。⚠ 而那颗旗 <c>ImageCeilingsMeasured</c> 的名字比它的射程大——它今天只说<b>字节</b>量过，<b>长边</b>那一半照旧⛔ 读数（引擎自报的 1440 仍⛔ 是我们的），已另立 <b>P-246（开放）</b>。

### 22.4 <c>skipInliningFonts</c>：两颗读数，一颗干净小档

| 调用 | 字符数 | <c>@font-face</c> | <c>data:</c> 字体 | esm.sh 地址 |
| --- | --- | --- | --- | --- |
| <c>exportToSvg(默认)</c>（⛔ 设 asset） | 651 | 1 | 0 | 1 |
| <c>exportToSvg({skipInliningFonts:true})</c> | <b>516</b> | <b>0</b> | 0 | <b>0</b> |

- <c>exportToSvg.length === 1</c>（一个形参），那颗开关在<b>返回值里读得到效果</b>而⛔ 在参数表上（名册 296 那条方向：判一颗开关要看它改了出口的什么）。
- 落地口径待定（归他）：那颗「干净小档」导出的 SVG 在<b>没装那颗字面的机器</b>上会变成系统字面——省了 135 字符，代价是别人打开时⛔ 是那个风格。这颗与 §22.1 那条「随包字体管着导出面」是同一件事的两头。

### 22.5 菜单展开态：那颗 hamburger 十项全文＋「更多工具」四项（含 Mermaid）

- 左上那颗（<c>cls=dropdown-menu-button main-menu-trigger</c>，几何位置 16,16）展开后 DOM 里 10 颗可见项，逐字＝<c>Open Ctrl+O</c>／<c>Save to…</c>／<c>Export image… Ctrl+Shift+E</c>／<c>Find on canvas Ctrl+F</c>／<c>Help ?</c>／<c>Reset the canvas</c>／分节标题 <c>Excalidraw links</c> ＋ <c>GitHub</c>／<c>Follow us</c>／<c>Discord chat</c>／<c>Dark mode Shift+Alt+D</c>／分节标题 <c>Canvas background</c> ＋ 六颗色块（第一颗带选中环）。照片 <c>out_r7a/shots/r7a_menu_a.png</c>（44,779 B，与没开菜单那帧<b>不同一颗 sha</b>＝尺这次真拍到了）。
- 三处要在 §二十一 那张「宿主接管表」上落地的：<b>①</b> <c>Open</c>／<c>Save to…</c> 两项在 WebView2 里<b>是真能点的</b>（见 §22.6），点了会去走浏览器那套文件对话框，而我们的档在<b>我们</b>手里；<b>②</b> <c>GitHub</c>／<c>Follow us</c>／<c>Discord chat</c> 三颗是<b>出站链接</b>——断网机器上三颗点不开的按钮摆在主菜单里；<b>③</b> <c>Reset the canvas</c> 一颗是<b>无确认清盘</b>的动作，与 §21.5 那颗「撤销立场」直接冲突。
- 那颗「更多工具」（<c>App-toolbar__extra-tools-trigger</c>，955,20）展开 4 项、项宽一律 175 px：<c>Frame tool</c>／<c>Web Embed</c>／<c>Laser pointer</c>／<b><c>Mermaid to Excalidraw</c></b>。
- 那颗 <c>Mermaid</c> 点下去：<b>1 颗本地弹窗</b>，正文逐字「Mermaid to Excalidraw／Currently only Flowchart, Sequence, and Class Diagrams are supported…」，左边语法框预置一段 <c>flowchart TD</c>，右边<b>当场渲染</b>出七颗节点的手写体预览，加一颗 <c>Insert →</c> 与 <c>Ctrl Enter</c> 提示（照片 <c>r7a_mermaid.png</c>，70,156 B）。这一臂的 <c>newAttemptsCount = 0</c>＝<b>全程零网络请求</b>。⇒ §九 决策 4（摘 TTD／mermaid 省 41.8%）的代价要说清：那颗入口在这份 bundle 里<b>内置且离线可用</b>，摘它省的是字节，⛔ 是「本来会坏的东西」。

### 22.6 那颗 native FS：WebView2 <b>给</b>了这套 API（§十九.5 欠的那颗分支）

现读（虚拟主机 <c>https://spike.local</c>，<c>isSecureContext = true</c>）：<c>showOpenFilePicker = true</c>／<c>showSaveFilePicker = true</c>／<c>FileSystemHandle = true</c>／<c>a[download] = true</c>／<c>navigator.clipboard.write = true</c>；而 <c>navigator.storage.getDirectoryHandle</c> 那颗在<b>这一臂的形状检查里没探到</b>（<c>fsAccess: false</c>）。

- 含义：excalidraw 内部那颗 <c>nativeFileSystemSupported</c> 门在我们宿主里<b>是开的</b>⇒ §22.5 ① 那两项会走浏览器文件对话框，⛔ 走我们的桥。落地要么把两项摘掉／改道（<c>registerAction</c> 在那 25 颗旋钮里，见 §二十），要么接受「两套文件出口」。这颗是 §二十一 那张表上<b>由读数改口</b>的一处：v1 里写的是「宿主递档、网页⛔ 碰盘」，现在要加一句「⛔ 管住网页自己那颗 picker」。
- <c>fsAccess: false</c> 那一颗⛔ 能当结论：我这把尺探的是 <c>navigator.storage.getDirectoryHandle</c> 一颗成员在不在，⛔ 是「Origin Private FS 能不能用」——登记为<b>未判定</b>（名册 304 那条：发表「还剩几颗」前先说⛔ 覆盖哪些形状）。

### 22.7 画布三语：那颗 prop 叫 <c>langCode</c>，一交就全变（0 颗网络请求）

- <c>langCode: "zh-CN"</c> ⇒ <c>document.documentElement.lang</c> 当场变 <c>zh-CN</c>，静态条上 <b>35 颗标签里 34 颗变中文</b>，逐字：绘制后保持所选的工具栏状态 — Q／抓手（平移工具） — H／选择 — V 或 1／矩形 — R 或 2／菱形／椭圆／箭头／线条／自由书写／文字／插入图像／橡皮／更多工具／素材库（照片 <c>out_r7b/shots/r7b_lang_zh.png</c>）。<c>attemptsSummary.total = 0</c>＝<b>语言包在 bundle 里</b>，⛔ 需要网络。
- 那颗非中文的是 hamburger 自己（读出来是 class 名兜底，⛔ 是标签）。⇒ 界面语言那条线（WI-1～WI-82）在画布这一侧<b>⛔ 欠一整刀</b>：把 <c>Loc</c> 当前语言递成 <c>langCode</c> 就跟着走；欠的只剩「<b>换语言那一刻它真不真重贴</b>」（与我们那三颗机制 (C) 宿主同一问法，见 §22.10 欠项③）。
- 反面证据也要记：r7a 那一跑我先按 <c>langKey</c> 交，读数是「35 颗标签 0 颗汉字」——那⛔ 是「画布没中文」，是<b>那颗 prop 名字我猜错了、根本没落地</b>（把它喊回来的正是 <c>htmlLang</c> 还是 <c>en</c> 那一列）。

### 22.8 中文在画布上认的是<b>系统字面</b>（§九 决策 3 的新证据）

- 同一张照片上：拉丁 <c>Abc 123</c> 是手写体，汉字 <c>永字八法</c> 是<b>系统黑体</b>。字面族状态读数（r7b）＝<c>Assistant/loaded 4</c>、<c>Virgil/loaded 1</c>、<c>Excalifont loaded 1／unloaded 6</c>、<c>Xiaolai unloaded 209</c>（<b>一颗都没拉</b>）。
- 那颗 <c>document.fonts.check("16px Xiaolai")</c> 回 <b>true</b>，而同一刻那一族 209 面全 <c>unloaded</c> ⇒ 那颗 <c>check()</c> ⛔ 是「这颗字面上了屏没有」的尺（坑表 #666）。
- 导出的 SVG 里那颗中文文本节点的 <c>font-family</c> 实测＝<c>Virgil, Segoe UI Emoji</c>（<b>⛔ 列 Xiaolai</b>）⇒ 中文在导出面上落到 emoji／系统回退，这一条与 §22.4 那颗 <c>skipInliningFonts</c> 的口径题是同一颗：中文要不要手写体，是<b>随包 209 分片／12.08 MB</b> 那颗决策的内容，现在它有了屏幕证据＋档内证据两行。

### 22.9 这一腿的仪器账（⛔ 产品含义，只登记）

- 首帧：四臂报同一档读数 <b>960～965 ms painted</b>（与 §十八 那颗 960 ms 一致，⛔ 变化）。
- 那颗截图尺：r6e 三张照片<b>同一颗 sha</b> 且与「⛔ 开菜单」那两臂一字不差；r7a 同样开菜单、同样那颗尺，<b>拍到了</b>。差别＝r6e 在 <c>postMessage({kind:"shot"})</c> 之后<b>立刻</b>派发 Escape，而那颗尺是<b>异步</b>的（坑表 #663）。
- 三格 <c>ERR</c> 是我自己造的：同一臂的头一格已经读到 <c>hasInsert: false</c>，后面还是去调了那颗⛔ 存在的 <c>insertSceneElements</c>（坑表 #664）。

### 22.10 这一腿欠的那几格（⛔ 混进已读）

①那颗字体子集化 worker 的文件名／要不要随包（§22.2）——要一次<b>包内文件普查</b>＋一臂起窗对照；②<c>nativeFileSystemSupported</c> 为真时那两项菜单的<b>改道方案</b>（§22.6）——属 §二十一 那张表，待评审；③<b>换语言那一刻</b>画布真不真重贴（<c>langCode</c> 递进去之后改一次再看标签）——下一腿一臂就能量；④<c>set=1</c> 那臂<b>只有 4 面</b>被拉（Assistant×3＋Virgil），Excalifont 那 7 面要<b>什么动作</b>才拉（画一笔？改字体？）——这一格直接决定随包字体清单能⛔ 再砍；⑤深色底（<c>viewBackgroundColor</c> 换深色）下那颗菜单与工具条的照片——§22.5 那十项只在白底量过。

## 二十三、Spike-1b 第三腿（r8 两臂）：<c>langCode</c> 那一刻真重贴、中文手写体落在 <c>fontFamily=5</c> 那一档、深色底会吞掉宿主塞进去的字（批次 Phase C 片14，2026-10-17·一次性起窗＋只读取数，⛔ 产品码⛔ 建工程⛔ 装工具链⛔ 改系统 PATH⛔ 表⛔ 账本）

> 这一腿只还 §二十二.10 欠的那三格（③换语言那一刻／④那颗字面要什么动作才拉／⑤深色底那张照片），跑法照旧：同一颗 <c>spike1b.exe</c>（⛔ 重编）、<c>--blocked 1</c>、两臂各一颗进程（<c>out_r8a/</c>·<c>out_r8b/</c>），仪器＝<c>www/index6.html</c>＋<c>www/battery6.js</c>。

### 23.1 那颗 <c>langCode</c> 换的那一刻：同一颗实例上真实重贴，⛔ 需要重挂

现读（<c>r8-repaste</c>）＝<c>{sameReactRoot: true, islandSameNode: true, islandMarkSurvived: true, apiSameObject: true, changesDelta: 2}</c>，随后那一格（<c>r8-labels-en</c>）＝<c>{total: 35, cjk: 0, htmlLang: "en"}</c>，而换之前（<c>r8-labels-zh</c>）＝<c>{total: 35, cjk: 34, htmlLang: "zh-CN"}</c>。

- 三件事一次读完：①那颗 prop 是<b>活控</b>的（<c>root.render</c> 换 props 即可，画布那颗 DOM 岛与 <c>excalidrawAPI</c> 对象<b>都是同一颗</b>，我在岛上打的标记也活着）；②35 颗标签里那 34 颗<b>当场</b>从中文变英文，⛔ 一次重挂、⛔ 一次网络请求；③句柄不变⇒<b>场景内容不受影响</b>（<c>sceneStillThere: 1</c>）。
- 落地含义（进 §二十一 那张表）：画布这一侧的「换语言即重贴」<b>⛔ 欠我们那套机制 (C) 的手臂</b>——宿主只要把 <c>Loc.Current</c> 映射成 <c>langCode</c> 递进去就行。⚠ 这只读到<b>静态标签</b>那一族；<b>画布里已经写好的那句正文</b>（用户打的字）⛔ 会跟着翻，那是<b>刻意</b>的（那是用户内容）。
- ⚠ 仪器一处坏：我把「三格合起来那句结论」想写成一颗 <c>r8-repaste-verdict</c>，结果取错了层级（<c>step()</c> 交回的就是那颗值本身，我按 <c>{ok,r}</c> 那层去读⇒三颗 <c>null</c>）。读数在上两格里一颗没丢，那一格已就地修好＝坑表 #668。

### 23.2 中文手写体在哪一档：<c>fontFamily=5</c>，而 <c>document.fonts</c> 那五档探测一动不动

逐档改那颗文本的 <c>fontFamily</c>（1→5），每档读<b>导出的 SVG</b>（权威）＋字面族状态表：

| <c>fontFamily</c> | SVG 里的 <c>font-family</c> | SVG 字符数 | 内联 <c>data:</c> 字体 | 状态表 |
| --- | --- | --- | --- | --- |
| 1 | <c>Virgil, Segoe UI Emoji</c> | 2,614 | 1 | Xiaolai unloaded 209 |
| 2 | <c>Helvetica, Segoe UI Emoji</c> | 522 | 0 | 同上 |
| 3 | <c>Cascadia, Segoe UI Emoji</c> | 6,252 | 1 | 同上 |
| 4 | <c>Segoe UI Emoji</c> | 508 | 0 | 同上＋报 <c>Couldn't find registered fonts for font-family "4"</c> |
| <b>5</b> | <b><c>Excalifont, Xiaolai, Segoe UI Emoji</c></b> | 7,837 | <b>4</b> | 同上 |

- 那颗 5 摆上屏幕（<c>r8b</c>）之后状态表才动：<c>Xiaolai/loaded 3</c>（另 206 颗 unloaded）、<c>Excalifont/loaded 1</c>。照片 <c>out_r8b/shots/r8b_ff5_white.png</c> 是<b>同一帧两行 A/B</b>：上面那行（档 5）汉字<b>是手写体</b>，下面那行（档 1）汉字是系统黑体——两行同一句「永字八法 Abc 123」。
- 结论改口：§二十二.8 那句「中文在画布上认的是系统字面」<b>只对档 1 成立</b>；真实现场是<b>档 5 那一族才列 Xiaolai</b>。⇒「中文要不要手写体」⛔ 是随包⛔ 随包的二选一，而是<b>把⛔ 把那颗档摆进字体下拉</b>的口径题（他定）。

### 23.3 那颗「随包 12.08 MB」的分母：引擎按 <c>unicode-range</c> 逐片拉，一句常用汉字只用 3 片

- 屏幕那一跑真拉的 8 颗档（本机文件实测；<c>transferSize</c> 那一列<b>全 0</b>＝命中缓存，⛔ 能用它量，见坑表 #669）：Assistant 3 颗 60,932 B、Xiaolai <b>3 颗 135,612 B</b>（37,412＋47,540＋50,660）、Excalifont 1 颗 24,956 B、Virgil 1 颗 56,156 B，合计 <b>246.8 KB</b>。
- 全族对照：<c>Xiaolai</c> 209 颗＝<b>12.08 MB</b>（平均 59.2 KB／最大 82.1 KB），<c>Excalifont</c> 7 颗＝0.06 MB。
- ⇒ §九 决策 3 那条「209 分片／12.08 MB」的<b>前提</b>要补一句：那是「任意中文都要能写」的上限，⛔ 是「屏幕上出现中文」的开销；两者差<b>约 90 倍</b>（135,612 B ↔ 12,666,824 B（全族 12.08 MiB））。随包可以按<b>常用字那几片</b>裁，代价是「用户打到一个没随包的字」那一档——那一档现在⛔ 读数（⛔ 造：要量就得知道 unicode-range 覆盖表，登记为欠项）。

### 23.4 深色底：菜单照旧，而<b>宿主塞进去的那句字整句看不见</b>

- 现读（<c>r8-dark</c>）＝<c>{theme: "dark", bg: "#1a1a1a", itemsVisible: 10}</c>，照片 <c>out_r8a/shots/r8a_dark_menu.png</c>（43,543 B，与白底那颗不同一颗 sha）＝十项在深色菜单上照常可读，那颗「Dark mode」当场改口念 <c>Light mode Shift+Alt+D</c>，六颗背景色块换成深色档。
- <b>同一帧里那行「永字八法 Abc 123」整句不见了</b>：元素 <c>strokeColor</c> 还是 <c>#1e1e1e</c>，压在 <c>#1a1a1a</c> 上。⇒ 引擎<b>⛔ 替宿主翻元素描边色</b>（换 <c>viewBackgroundColor</c> 与 <c>theme</c> 都⛔ 翻）。这条只活在照片里：那一格的三颗 DOM 读数一颗都⛔ 报它（坑表 #671）。落地要么在<b>进档那一刻按底色算一次对比色</b>（与我们 <c>SurfaceReadability</c> 同一族判据），要么把「深色画布」那一档在 UI 上关掉——口径题，进 §二十一 待评审。

### 23.5 这一腿欠的（⛔ 混进已读）

①档 5 那一族<b>覆盖表</b>（哪几片对应哪些字）⇒ 才知道随包能裁到多小、以及「打到没随包的字」那一档长什么样；②深色底下元素描边的<b>对比色判据</b>归谁（Core 一颗⛔ 是两处）；③<c>langCode</c> 换语言那一刻<b>菜单开着</b>的时候会不会自己关（本腿是关了菜单才换的）；④那颗 <c>fontFamily=4</c> 报「⛔ 注册的字面族」——它在我们的字体下拉里<b>该⛔ 该出现</b>（现读：递进去会报 error 并落到 emoji 档）。

## 二十四、包内现读（⛔ 起窗）：那颗子集化 worker 的 URL 从哪儿来、字体那枚 enum 的真名、Xiaolai 覆盖表就在 bundle 里（批次 Phase C 片15，2026-10-17·只读源码腿，⛔ 起窗⛔ 产品码⛔ 建工程⛔ 装工具链⛔ 改系统 PATH⛔ 表⛔ 账本）

> §二十二.10 欠项①（那份 worker 到底叫什么、要不要随包）与 §二十三.5 欠项①（Xiaolai 覆盖表）都⛔ 需要起窗——两样都写在我<b>已经有</b>的那颗 bundle 里。这一腿只读 <c>bin/WI83/spike1b/www/spike1.min.js</c>（8,376,283 字节）与 <c>www/fonts/</c> 那 234 颗档。

### 24.1 那颗子集化 worker：URL 由打包期注入，我这份产物里它<b>恒为 undefined</b>，而 worker 的<b>代码本体就在同一颗 bundle 里</b>

逐字现读（<c>spike1.min.js</c>，偏移 2,168,641 起那一支）：

- <c>var Eht = {}; Ei(Eht, { WorkerUrl: () => F0i });</c> ⇒ 那颗模块只交出一颗名字 <c>WorkerUrl</c>。
- <c>Bht = {}, F0i = Bht.url ? new URL(Bht.url) : void 0;</c> ⇒ <b>那颗 <c>Bht</c> 在我这份 bundle 里是颗空对象</b>（打包期该注入的 <c>url</c> 没注进来）⇒ <c>WorkerUrl === undefined</c>。
- <c>typeof window > "u" && typeof self < "u" && (self.onmessage = async e => { switch (e.data.command) { case WmA.Subset: … }</c> ⇒ <b>worker 的正文与主线程代码在同一颗档里</b>，靠「⛔ 有 window 而有 self」那颗门只在 worker 环境里挂上。
- 那颗池子的门：<c>static create(t, i = {}) { if (!t) throw new p4t; if (!Ndt.url || t.toString() === Ndt.url) throw new w4t; … }</c> ⇒ <c>p4t</c>＝<c>WorkerUrlNotDefinedError</c>（URL 为空，<b>就是 r6b/r7a/r8a 三臂响的那一声</b>），<c>w4t</c>＝<c>WorkerInTheMainChunkError</c>（URL 就是主 chunk 自己＝那颗 worker ⛔ 单独成档）。
- 兜底那一支：<c>catch (s) { Hht = !1, dri() && (s instanceof p4t || s instanceof w4t) || console.error("Failed to use workers for subsetting, falling back to the main thread.", s), n(e, t) }</c> ⇒ <b>回落主线程那颗 <c>subsetToBase64</c>，功能照成</b>（§二十二.2 那颗 12 ms→146 ms 就是这一支的代价）。⚠ 而 <c>console.error</c> 在我们这份产物里<b>会响</b>（三臂都读到＝那颗 dev 判定 <c>dri()</c> 恒假，被编译掉了）⇒ 排障时别把那一声当故障。

**结论（欠项①就此关掉）**：随包<b>⛔ 需要额外的 worker 档</b>——今天这份产物里那颗 worker 从没被实例化过，全部子集化都在主线程做完，读得到、量得到。要<b>启用</b>它是<b>打包期</b>的事（把 worker 入口单独切一颗 chunk，再把 <c>Bht.url</c> 注成那颗 chunk 的 URL；<c>w4t</c> 就是防「URL 指回主 chunk」那道门），⛔ 运行时 prop、⛔ 桥命令。⇒ §二十一 那颗「随包清单」⛔ 因这颗 worker 加东西；§21.7 那颗性能读数（146 ms 一颗 SVG）的<b>解释</b>也从「未知降级」变成「已知且可选的优化」。

### 24.2 字体那枚 enum 的真名：档 5 叫 <c>Excalifont</c>，而 <c>Xiaolai</c> 是<b>它的中文回退</b>（⛔ 是「档 5」）

<c>Sl = { Virgil: 1, Helvetica: 2, Cascadia: 3, Excalifont: 5, Nunito: 6, "Lilita One": 7, "Comic Shanns": 8, "Liberation Sans": 9 }</c>，另 <c>NW = { Xiaolai: 100, "Segoe UI Emoji": 1000 }</c> 是<b>回退档</b>的登记表（<c>[NW.Xiaolai]: { metrics: {…}, fallback: !0 }</c>、emoji 那颗 <c>local: !0, fallback: !0</c>）。

- 这把 §二十三.2 那张表读通了：<b>档 5 就是 Excalifont</b>（包里那颗默认手写体），它的 CSS 栈是 <c>Excalifont, Xiaolai, Segoe UI Emoji</c> ⇒ 汉字<b>不是</b>「档 5 的字」，是<b>档 5 后面的那颗中文回退</b>；档 1 是 <c>Virgil</c>（同一处还标着 <c>deprecated: !0</c>），栈里<b>⛔ 带中文回退</b> ⇒ 汉字落系统。
- ⚠ 我上一腿把那颗档写成「档 5 那一族」，读起来像有一颗叫「5」的字面＝坑表 #672。也顺手撞出另一颗：<b>我 r6/r7/r8 三腿的探针元素一直用档 1</b>，而档 1 在包里是<b>已废弃</b>那颗 ⇒ 我那几张照片里「汉字是系统黑体」是<b>我自己选的档</b>造成的，⛔ 是引擎的默认（引擎默认＝档 5，也就是会走 Xiaolai 回退）＝坑表 #674。
- 那颗 <c>fontFamily=4</c> 报 <c>Couldn't find registered fonts for font-family "4"</c> 现在也对上了：<c>Sl</c> 里<b>没</b> 4 这一档（1/2/3/5/6/7/8/9），4 是空洞。⇒ 我们的字体下拉⛔ 许出现 4（§二十三.5 欠项④关掉）。

### 24.3 Xiaolai 的覆盖表<b>就在 bundle 里</b>：209 颗 URI 全被定义、205 颗抓到 <c>unicodeRange</c>、盖住 CJK 主区要 100 片＝5.43 MB

现读（同一颗 <c>spike1.min.js</c> ＋ <c>www/fonts/Xiaolai/</c> 逐档 <c>getsize</c>）：

| 读数 | 颗数 | 字节 |
| --- | --- | --- |
| 全族（档里定义、盘上都在） | 209 | <b>12.08 MB</b>（平均 59.2 KB／最大 82.1 KB） |
| 抓到 <c>uri:…,descriptors:{unicodeRange:"…"}</c> 那一条形状的 | 205 | 11.85 MB |
| 覆盖 <c>U+4E00–U+9FA5</c>（CJK 主区）那几片 | <b>100</b> | <b>5.43 MB</b>（省 55%） |
| 那 205 片合计覆盖的码位并集 | — | <b>8,293</b> 颗 |
| 屏幕上那句「永字八法 Abc 123」真拉的 | 3 | 135,612 B（§二十三.3） |

- ⇒ §九 决策 3 现在有<b>三档</b>可选，而⛔ 一档：全随 12.08 MB／只随主区 5.43 MB／只随「屏幕上出现过的那几片」135 KB 级。裁到多小是<b>他的</b>口径（观感＋装机体积），我这一腿只把<b>每档的字节</b>摆出来。
- ⚠ 那颗「205／209」⛔ 能读成「另 4 片用不上」：它只是<b>我那条正则</b>抓到 205 颗带 <c>unicodeRange</c> 的条目，另 4 颗 URI 在档里被定义了而<b> descriptor 的形状我⛔ 覆盖</b>＝坑表 #673。要说清那 4 颗得回去逐颗读它们的定义点，登记为欠项（片17 已读：那 4 颗是<b>死常量</b>——bundle 里 0 处 <c>uri:</c> 引用，§二十六.4）。
- 再往下裁（比如「3,500 常用字要几片」）需要<b>一张常用字表</b>——那是措辞与口径之外的新输入，⛔ 由我现编一张。

### 24.4 §二十一 那份 v1 里，本腿<b>就地改口</b>的三句

| v1 原句（§21.3／§21.5／§21.7） | 新读数 | 已就地补成 |
| --- | --- | --- |
| 「『体积↔张数』这条曲线<b>⛔ 量过</b>（§20.8）⇒代码批第一刀＝先把曲线量出来再给上限」 | 曲线量出来了：空档 237 字符、照片级 1440 每张 +0.32 MiB、噪声 1440 每张 +9.08 MiB、<c>dataURL</c> 字符＝字节 × 4/3（§二十二.3） | 补成「已量（片13），<b>容差那颗数仍归他</b>」——曲线⛔ 等于上限 |
| 「<b>窗口面四颗（我跑，归我）</b>：§19.5 两臂；<c>nativeFileSystemSupported</c> 分支；<c>skipInliningFonts</c> 效果；菜单展开态那张照片」 | 四颗全部有读数（片13／片14），其中 native FS 那颗的读数是<b>「门是开的」</b>（§二十二.6） | 补成「四颗已跑完＋那颗『⛔ 管住网页自己那颗 picker』进接管表」 |
| 「<b>⛔ 量的一颗</b>：内嵌 base64 之后『一颗板多大 ↔ 几张图』的曲线——上限那颗数<b>欠着</b>」 | 曲线⛔ 欠；欠的变成「允许多大」那颗<b>口径</b>与「没随包的字长什么样」那颗<b>读数</b> | 改口成两颗欠项：容差（归他）＋没随包那一档的现场（归我，要一张常用字表） |

### 24.5 这一腿欠的（⛔ 混进已读）

①那 4 颗⛔ 被我正则抓到的 Xiaolai URI 的 descriptor 形状（逐颗读定义点）；②启用 worker 那条路<b>真跑一次</b>（单独切一颗 chunk＋注 URL，看那颗 146 ms 掉到多少）——那是打包腿的活，v1 评审过之前⛔ 开工；③「打到没随包的字」那一档的现场（要先定常用字表）；④档 6/7/8/9（Nunito／Lilita One／Comic Shanns／Liberation Sans）四颗<b>在屏幕上的样子</b>——本腿只读到名字与号数，⛔ 拍过照。

## 二十五、Spike-1b 第四腿（r9 三臂）：那张「接管表」的第一杠杆是宿主自排 <c>MainMenu</c>，而⛔ 同名注册（批次 Phase C 片16，2026-10-17·一次性起窗＋只读取数，⛔ 产品码⛔ 建工程⛔ 装工具链⛔ 改系统 PATH⛔ 表⛔ 账本）

三臂一次跑（r9a＝UIOptions 三挂载对照、r9b＝宿主自排、r9c＝动作注册表），bundle 换 <c>spike2.min.js</c>：同一份包、entry 多交 <c>MainMenu</c> 一颗——8,375,632 字节／sha256 <c>22bcb625…</c>，比 spike1 少 893 字节＝少交 <c>exportToBlob/exportToCanvas/exportToClipboard</c> 三颗名字（<b>tree-shaking 跟着出口的名单走</b>）。⛔ 点内置 Open／Save（那颗原生选择框会挂住这一跑），只读 DOM＋点我们自己 perform 的行。第 0 道核对（复合）两跑现读：闲置 0.0 秒／光标 (960,599)／前台 'Endfield' 与闲置 0.2 秒／前台同——前台是另一件事的窗，照实记。

### 25.1 UIOptions 那颗表：关四行＋一块底色面板；<c>canvasActions</c> 是<b>深合并</b>

| 挂载 | 行数 | 与默认的差 |
| --- | --- | --- |
| 默认（不递 UIOptions） | <b>11</b>（＋底色面板一块，6 swatches） | — |
| 七颗 canvasActions＋<c>tools.image</c> 全 false | <b>7</b>（面板 6→0） | 少 Open／Export image…／Reset the canvas／Dark mode 四行＋面板 |
| 只给 <c>{canvasActions:{loadScene:false}}</c> | <b>10</b> | 只少 Open，其余一字不动 |

- 默认 11 行逐字：Open Ctrl+O｜Save to…｜Export image… Ctrl+Shift+E｜Find on canvas Ctrl+F｜Help ?｜Reset the canvas｜组头「Excalidraw links」｜GitHub｜Follow us(X)｜Discord chat｜Dark mode Shift+Alt+D。⚠ §22.5 的「十项」与本腿的「11 行」是<b>同一张菜单、两把尺的口径</b>：组头＋组内三颗外链在本尺算 4 行（组头那颗 ⛔ 是 menuitem 之外的形状）；「画布底色」那块面板是第 12 样可见东西，两把行尺都⛔ 数它（本腿另量 <c>canvas-background-label</c>／swatch 数）。
- 「Save」那颗（<c>saveToActiveFile</c>）<b>两份读数都⛔ 有</b>：那颗 item 的 predicate 还要求 <c>appState.fileHandle</c>（源码：`predicate:(e,t,i,n)=>!!n.props.UIOptions.canvasActions.saveToActiveFile&&!!t.fileHandle&&!t.viewModeEnabled`）——宿主管档⇒永远没有原生 fileHandle⇒默认就⛔ 上屏。⇒ §22.6 那句「Open／Save to… 两项真能点」里的「Save to…」其实是 <c>DefaultItems.Export</c>（开 JSON 导出对话框那颗），它<b>没配任何门</b>（源码无 isActionEnabled）⇒ <b>UIOptions 关⛔ 掉它</b>，接管只能走宿主表。
- <b>深合并</b>是这腿最值钱的一格：只递一颗键，邻居（Export image／Dark mode）全在 ⇒ 宿主递<b>部分表</b>是安全的；v1 若写过「必须交整表」照此改口。
- <c>tools.image:false</c>＝工具条 32→30：恰好少「Insert image — 9」一颗（testid <c>toolbar-image</c>；label＋input 成对去重后两行读数），其余一排（Hand／Selection／Rectangle／Diamond／Ellipse／Arrow／Line／Draw／Text／Eraser／锁定／More tools／缩放三颗／撤销重做／帮助／zen）一字未动。⚠ 陪跑：第一版尺只抓 <c>button</c>，0.18 的形状工具是 <c>label.ToolIcon＋input[type=radio]</c> ⇒「没变化」是<b>尺瞎</b>＝坑表 #675。
- 键盘那半边（源码判、⛔ 上真键）：<c>handleKeyDown</c> 的门按名字查表（<c>(l.name in t ? t[l.name] : !0)</c>）⇒ <c>loadScene:false</c> 会把 Ctrl+O 一起闸掉；真键那格未判（§25.3）。

### 25.2 宿主自排 <c>MainMenu</c>＝接管的第一杠杆（r9b）

- <c>&lt;Excalidraw&gt;&lt;MainMenu&gt;…&lt;/MainMenu&gt;&lt;/Excalidraw&gt;</c> 一挂：内置 11 行<b>整颗消失</b>，屏幕上只剩我排的四行（HOST-SAVE-CLICK／HOST-OPEN-CLICK／内置 LoadScene 复用／HOST-GUARD-TEN）。裁决者＝<c>YcA</c> 那颗 HOC（偏移 8,106,795）：宿主实例（⛔ 带 <c>__fallback</c>）把 <c>preferHost</c> 立起来，内置那颗 <c>__fallback:!0</c> 的实例渲染 null。
- 点我自己的行：<c>onSelect</c> 真走（计数器 +1）⇒ 桥的「存到桌面档」走这条路；点完菜单自动合上（第二颗行因此⛔ 量到点击＝「选择后合菜单」这条行为，照实记）。
- 内置行<b>可复用</b>：<c>MainMenu.DefaultItems.LoadScene</c> 在我自己那张表里照样渲染（<c>load-button</c> 在，谓词照跑）⇒「宿主的行＋挑几颗内置行」混合合法。
- 出口形状：包的公共出口里有 <c>MainMenu</c>（<c>dist/types/excalidraw/index.d.ts:29</c>：`export { MainMenu }`）；上一批那句「bundle 只交 10 颗」说的是<b>我 entry 的选择</b>（⛔ 包缺）——落地批的 entry 要加交这一颗。0.17 那颗 <c>renderMainMenu</c> prop 在 0.18 <b>⛔ 存在</b>（bundle 与 .d.ts 全文 0 颗）⇒ 引过它的句照此改口。

### 25.3 <c>registerAction</c>：同名⛔ 改道菜单；注册表自己⛔ 长出 UI（r9c）

- 同名注册（<c>toggleTheme</c>，perform＝我们的计数器）之后：菜单那行<b>一字不改</b>（「Dark mode Shift+Alt+D」）、点下去<b>主题真翻了</b>（light→dark）、我的 perform <b>0 次</b> ⇒ 菜单那一路⛔ 查注册表。根因（源码判）：<c>executeAction(e,…)</c> 收的是 <b>action 对象本身</b>、直接 <c>e.perform(…)</c>；菜单组件闭包里握的就是内置那颗对象。
- 全新注册（<c>spikeHostSave</c>＋keyTest Ctrl+Alt+K）之后：菜单 11→11、无任何新行 ⇒ <b>注册表自己⛔ 长出 UI 面</b>——自定义动作只有宿主自排 <c>MainMenu</c> 才有脸面。
- 键盘那一路：源码判（<c>handleKeyDown</c> 遍历 <c>Object.values(this.actions)</c>）同名注册<b>会</b>改键盘；⚠ 运行时那半格<b>未判</b>：合成 keydown（window＋document 两路）⛔ 触达引擎 handler（hitsDelta 0）——按「测不到就留未跑」记（坑表 #677），⛔ 写成结论。
- ⇒ 桥的设计结论：v1 §21.4 的撤销立场／落盘节拍照旧；<b>菜单面＝宿主 <c>MainMenu</c>（自己的行＋挑内置）＋ <c>UIOptions</c> 关内置</b>；<c>registerAction</c> 只当「给引擎注册键盘动作」的备用出口。§22.6 那三格（出站链接／无确认清盘／Open·Save 真可点）的处置从「关 prop」升级成「关 prop＋换表」。

### 25.4 这一腿欠的（⛔ 混进已读）

①Ctrl+O 真键那半格（<c>loadScene:false</c> 时键盘是否同闸）——合成事件⛔ 触达，要真键注入（画板焦点那一族）；②宿主 <c>MainMenu.Item</c> 的 <c>shortcut</c>／<c>icon</c> 参数与三语（引擎 UI 措辞归包管，我们自己的行要交什么语言）；③command palette 里同名／新动作有没有脸面（本腿⛔ 量）；④三颗外链／Reset 在「关 prop＋换表」之后的真机照片（属落地批，接 §22.6）。

## 二十六、Spike-1b 第五腿（r10 两臂）：宿主行认 shortcut/icon、zh 语料缺 palette 那颗键、字体档 6～9 全走本地、Xiaolai 那 4 颗是死常量（批次 Phase C 片17，2026-10-17·一次性起窗＋只读取数，⛔ 产品码⛔ 建工程⛔ 装工具链⛔ 改系统 PATH⛔ 表⛔ 账本）

两臂一次跑（r10a＝宿主自排表的行参数＋三语＋命令面、r10b＝字体档 6/7/8/9），bundle 沿用 <c>spike2.min.js</c>（片16 那颗，sha256 <c>22bcb625…</c>）。⛔ 点内置 Open／Save；第 0 道核对（复合）现读闲置 0.0～0.2 秒／前台 'Endfield'——前台是另一件事的窗，照实记。

### 26.1 宿主行参数＋三语（r10a）

- <c>MainMenu.Item</b> 认 <c>shortcut</c>（「Ctrl+Alt+S」上屏，与内置行同一颗样式槽）与 <c>icon</c>（hasIcon true），<c>onSelect</c> 真走（计数器 +1）⇒ 宿主接管表的行＝「内置行的全部观感能力＋我们自己的出口」（§25.4 ② 关掉）。
- <c>langCode:"zh-CN"</c> 时宿主表里的<b>内置行跟着翻</b>（load＝「打开 Ctrl+O」／help＝「帮助 ?」，CJK 判定 true×2）⇒ 内置行放进宿主表⛔ 冻结在英文（片14 那颗重贴机制在宿主表里照跑）。
- ⚠ 同一张表里 <b>「Command palette」那行在 zh-CN 下仍念英文</b>（同拍对照：帮助／打开都是中文）⇒ 包内 zh 语料缺 <c>commandPalette</c> 那颗键（或该行⛔ 走 <c>t()</c>；两可，读数只记「那行是英文」）。落地含义：接管表若复用 <c>DefaultItems.CommandPalette</c>，中文界面会露英文——要么交自己的行，要么进「包语料缺口」清单。

### 26.2 命令面的脸面＝未判（两条合成路⛔ 触达，名册 289／#677 同族）

- 路一（菜单点击 <c>DefaultItems.CommandPalette</c>）：行可见（对照件＝同一拍我的行 onSelect 真触发）⇒ 1.4 秒内弹层⛔ 出现（<c>.command-palette-dialog</c> 0、照片＝关着那张 <c>15b529a58a99</c> 与 final 同一颗）。
- 路二（合成 Ctrl+/，window＋document 两路——那颗监听是 <b>window 级 capture</b>，源码偏移 8,040,104）⇒ 同样⛔ 开。
- ⇒ 这格<b>未判</b>：要真键（SendInput 那族）或真鼠标。同表对照件（我的行）排除了「菜单整体不响应」，所以沉默是「那颗行的选择没触发」或「触发了⛔ 上屏」两可，⛔ 写结论。

### 26.3 字体档 6/7/8/9 的现场（r10b，§24.5 ④ 关掉）

| 档 | 拉的档 | SVG <c>font-family</c> |
| --- | --- | --- |
| 6 | 本地 <c>/fonts/Nunito/…woff2</c> | <c>Nunito, Segoe UI Emoji</c> |
| 7 | 本地 <c>/fonts/Lilita/…woff2</c> | <c>Lilita One, Segoe UI Emoji</c> |
| 8 | 本地 <c>/fonts/ComicShanns/…woff2</c> | <c>Comic Shanns, Segoe UI Emoji</c> |
| 9 | 本地 <c>/fonts/Liberation/…woff2</c> | <c>Liberation Sans, Segoe UI Emoji</c> |

每挂载 <c>dataFont</c>＝2（字面嵌进 SVG）；照片 <c>fam67_white.png</c>／<c>fam89_white.png</c>（白底）。随包目录 9 族全在：Assistant 4／Cascadia 1／ComicShanns 4／Excalifont 7／Liberation 1／Lilita 2／Nunito 5／Virgil 1／Xiaolai 209（共 234 颗／13,107,068 字节口径不变）。

### 26.4 §24.5 ① 关掉＝那 4 颗不是「没抓到的形状」，是<b>死常量</b>

- 静态复算（变量→注册点两把尺，⛔ 一把正则）：bundle 里 URI 常量共 <b>227</b> 颗（Xiaolai 209／Excalifont 7／Nunito 5／ComicShanns 4／Cascadia 1／Liberation 1）；Xiaolai 209 颗里 <b>205 颗</b>有 <c>uri:…,descriptors:{unicodeRange:"…"}</c> 注册条目，**4 颗**（压名 <c>ai/oi/si/gi</c>＝四颗 <c>Xiaolai-Regular-*.woff2</c>）全档 <b>0 处 <c>uri:</c> 引用</b>——注册数组的形状是「定义簇紧邻数组字面量」，那四颗不在任何数组里。
- ⇒ 片15 的「205／209」口径修正：<b>另 4 颗⛔ 是「我那条正则没抓到」，是它们根本没注册</b>。决策 3 的分母照旧按盘上 209 颗算（随包档数），但「运行时注册表认的」＝205；那 4 颗盘上在、运行时⛔ 会作 FontFace 拉 ⇒「只随主区」那一档的刀下得更准（先按注册表筛、再按 unicodeRange 筛）。

### 26.5 这一腿欠的（⛔ 混进已读）

①palette 脸面（真键 Ctrl+/ 或真鼠标）；②宿主自己的行在 ja 下的三语（本腿只量了 zh 的对照）；③外链三颗／Reset 在「关 prop＋换表」之后的真机照片（属落地批）。

## 二十七、片26 落地腿（三颗数开闸＋宿主那颗写闸＋只生一次的那棵根）：崩后那块板真回到屏幕，而盘上那份⛔ 再没被空场景盖掉（批次 Phase C 片26，2026-10-18·落地＋真机读数，⛔ 语言表⛔ 装机链）

### 27.1 现场：三跑各把盘上那份盖成一颗空档（那颗档本身是证据）

| 那一跑 | 开闸那颗回话的形状 | 崩前盘上 | 崩后盘上 | 退路 |
| --- | --- | --- | --- | --- |
| 05:52（修之前） | <b>无参</b>的 <c>board.loaded</c> | 2608 字节／2 元素 | <b>241／0</b> | 只剩 <c>.bak</c>（只留最近一份） |
| 06:15（带两颗数，口径还错） | <c>want=4／loaded=4／canvas=4</c>"全等" | 4936／4 | <b>241／0</b> | 同上 |
| 06:22（口径已对，真因未改） | <c>want=1／loaded=1／canvas=1</c> 全等 | 1441／1 | <b>241／0</b> | 同上 |

- 三跑的日志里都<b>先</b>有 <c>boards.arm.post via=loaded</c>（距二次 <c>bridge.ready</c> 13 ms／12 ms）<b>再</b>有那一次写盘⇒"开闸那一刻画布上真有那几颗"与"1.5～7 s 后它序列化出 0 颗"是<b>同一跑的两句真话</b>，中间一定有人把树拆了。
- 屏幕那两路（<c>PrintWindow(PW_RENDERFULLCONTENT)</c> ＋ 从屏幕 DC <c>BitBlt</c> 那块矩形）在同一时刻各拍一张：06:22 那一跑 t+2／t+4／t+8 两路都读到<b>暗=0</b>（白）；修后两跑 t+2 仍白，<b>t+4／t+8 两路读回崩前那一帧</b>（暗 1497 与 1632，sha 与 base 同一颗 <c>ff97b73e575e</c>／<c>6db104bded20</c>）。
- ⚠ 仪器口径的两条老实话（正本 <c>bin/WI86/readings_26.txt</c> 第五块）：A／B 两路的 <b>sha ⛔ 能互相比</b>（两条路径各自编码），可比的只有"有没有内容"那一维；"与崩前同一颗 sha"这一次⛔ 当旧帧疑——同一时刻<b>两路都</b>读到 0、随后又<b>两路都</b>读回那一颗，换过内容再换回来才叫回来了（旧帧读不出中间那颗 0，名册 <b>333</b> 的那把尺这次是反着用的）。

### 27.2 开闸那句从此<b>带数</b>：协议加一颗进向命令，判据住在 Core

- <c>BridgeProtocol</c>：进向名单加 <c>board.loaded</c>（<c>elements</c>／<c>canvas</c>），<c>RequiredFields</c> 同批加两颗⇒<b>一颗不给数的 <c>board.loaded</c> 是 <c>MissingField</c>，⛔ 是"它解掉了"</b>。那颗 <c>BoardEnvelope</c> 交 <c>int?</c> 两颗（读不到就 <c>null</c>，⛔ 折成 0）。
- <c>BoardPolicy.CountWritableElements</c>＝<b>会被写回盘上的那几颗</b>（跳 <c>isDeleted:true</c>）；读不懂（半截档／⛔ 那颗键／根⛔ 是对象）交 <c>null</c>。这颗口径的来历就是 06:15 那一跑：页面前一版把 <c>canvas</c> 取成 <c>getSceneElementsIncludingDeleted</c>，而序列化器走 <c>getSceneElements</c>⇒<b>两颗⛔ 同口径的数比 equality＝闸门看着在挡、其实挡的是另一件事</b>（§21.2 那颗旋钮表早把带删除的那颗出口列进「⛔ 用」并写了理由，<b>表上写着⛔ 用⛔ 等于落地不会破</b>）。
- <c>ShouldArmAfterLoad(want, loaded, canvas)</c>＝三颗全等；<b>任一颗认不出＝⛔ 开闸</b>（读不懂时唯一安全的动作是不写）。空板（0／0／0）算"回到了那块板"，⛔ 因此⛔ 写。
- ⚠ 这颗判据的证人第一版<b>测了⛔ 那一半</b>：九行断言把 null 全摆在另两颗非零的位置上，于是"把 null 折成 0"那颗坏<b>全绿</b>（台架 A3 臂把它撞出来）⇒补的那一行是 <c>ShouldArmAfterLoad(null, 0, 0)</c> 仍⛔ 开（＝"我读不懂"与"这块板是空的"在另两颗都是 0 时才分不开）。

### 27.3 真因＝同一颗 <c>#root</c> 上第二棵 React 根（这颗由量得，⛔ 由推理得）

- 现场形状：<c>rerenderWith()</c> 每次都 <c>L.createRoot(box).render(...)</c>，而 <c>bridge.ready</c> 那一刻 <c>theme.set</c>／<c>lang.set</c>／<c>feature.flags</c> 三颗初始值连着发出去⇒恢复那一趟<b>至少拆树两次</b>；React 18 只让最后一棵赢下那块 DOM，每挂一次＝一块全新的空画板，宿主刚递进去的那块被拆掉，随后被序列化回盘。
- 修＝<b>那棵根只生一次、后续都往它 render</b>（<c>if (!root) root = L.createRoot(box);</c>），三处设值共用 <c>rerenderWith</c>，首挂载也从它走（<c>rerenderWith({});</c>）。
- 为什么这一颗非⛔ 由闸门拦住：闸门只能证明"递进去那一刻"画布上有那块板；拆树发生在<b>之后</b>，且⛔ 报错、⛔ 抛、⛔ 上屏。⇒本仓那条口径的形状：<b>静默的坏比坏本身更坏</b>，而<b>拦住它的两道闸要分开放</b>（§27.4）。

### 27.4 两道闸门的形状（主闸⛔ 装在页面上）

- 页面那道 <c>hostArmed</c> 只包得住<b>停笔</b>一路（它写在 <c>onChange</c> 里）；<b>失焦</b>与<b>关窗</b>两拍是宿主问、页面答（<c>export.pick</c>→<c>runExport</c>→<c>deliver</c>），那条路上⛔ 有对方那道 if——写盘的笔本来就握在宿主手里。
- 宿主那颗 <c>_autosaveArmed</c>：<b>每一趟 <c>bridge.ready</c> 重新闩上</b>（新页面必须重新证明自己）；没开闸之前交回的场景字节一律 <c>boards.scene.write.refused notArmed</c> 拒掉——⛔ 落盘，也⛔ 记进 <c>_seenScene</c>（记了＝下一趟恢复会把这块空场景当"见过的那一份"递回画布）。
- 判据排在<b>档位分流之后</b>⇒png／svg 那两族一次性产物⛔ 被它挡；拒的那一支照样 <c>FinishClosingFlush()</c>⇒⛔ 让关窗那一拍白等满 <c>CloseFlushBudgetMs</c>（拒掉也是答案）。
- 证人与台架：本批新增 8 格（Core 纯函数 2＋协议 3 行 InlineData＋1 格，UI 形状 4 格）；台架 <c>bin/WI86/bench_26.py</c> r2＝M0 全绿／12 臂 BAD 0／被红过的格 8/8／还原核对 True。
- ⚠ <b>⛔ 验过的那一档（如实登记）</b>：<c>boards.arm.refused</c> 与 <c>boards.scene.write.refused</c> 这两条<b>拒</b>的路径本批在真机上一次也没撞到过（修完真因之后本就该撞不到）⇒它们只有形状证人与纯函数证人，那条静默面登记成 <b>P-224</b>。

### 27.5 这一腿欠的（⛔ 混进已读）

- <b>P-224</b>（<b>片27 落地结案</b>，见 §27.6）：拒那一刻⛔ 只写日志＝这块板从此不再落盘而屏幕上⛔ 一句话；连同崩后 t+2 那 ≈2 s 白帧（§21.5 那句"⛔ 许静默白屏"剩下的那一半）——那两条都做完了（(B) 先重试一次递档 ＋ (A) 仍不对就在画板窗里给一句话加一颗按钮）。(C) 那条本批⛔ 取。
- <b>✅P-220（<b>片30 落地结案</b>，落地形状见本节上方那段追记）</b>：那位主人立成了——名册 <c>BoardsWindows</c>＋收尾链第一步那次 <c>FlushAllForExitAsync()</c>；两臂各一份正本（<c>bin/WI90/readings_30_main_pre.txt</c> 那颗板压根没落盘 → <c>readings_30_main_post.txt</c> 多 <c>['261019-105529-065c.excalidraw']</c>）。<b>⚠ 换出来的新账是 P-229</b>：那一发只量到<b>空板</b>。
- <b>✅P-229（<b>片31 真机结案</b>）</b>：「最后几笔跟着退出一起进盘」这句话现在有读数了——接住那颗 2 元素种子档＋当场拉一笔＋抬笔 <b>0.08 s</b> 关主窗 ⇒ <c>858/2 颗 → 2708/3 颗</c>，新进那颗 <c>freedraw</c> 的包围盒 <c>316.0×105.0</c> 对上这一笔的折算（期望 <c>316.8×105.5</c>），回音 <c>[2026-10-19T11:51:24.3028664+08:00] INFO boards.close.flush.exit id=261019-114119-a1b2 landed=True</c>；同一份脚本的对照臂（关画板窗）<c>858/2 颗 → 2710/3 颗</c>。正本 <c>bin/WI90/readings_31_main_115116.txt</c>／<c>bin/WI90/readings_31_board_115054.txt</c>（⛔ 进库）。这一腿⛔ 新缺陷、⛔ 动业务码，改的六把尺记在坑表 #740～#745。
- <b>✅P-230（片32 登记并同批结案）＋那颗 ⚛ 的两半</b>：两扇同开（<c>STARMARK_START_BOARD=new,new</c>）各画两笔再关<b>主窗</b>＝
  名册那一趟<b>两扇都落地</b>（<c>落地 2 扇</c>、逐扇 <c>tookMs=[153, 152]</c>），"最坏那一扇决定总时长"这句
  第一次有读数：总 <c>163 ms</c> ↔ 最坏 <c>153 ms</c> ↔ 之和 <c>305 ms</c>（改前各扇只 3～22 ms 时差在噪声里，当时照实报"分不出"）。
  同一趟量到一颗<b>真缺陷</b>：抬笔后 <b>0.00 s</b> 内就走到那一拍⇒<b>最后画那一扇</b>那一笔进不了盘
  （那颗档停在 <c>1444 字节／1 颗</c>），而两句 <c>landed=True</c> 一切正常——
  门槛量在 20～40 ms 之间（40／80／150／250／300 ms 各一跑全进），settle 臂（⛔ 关窗）先证明那一笔<b>画得上</b>（<c>2/2</c>）。
  修法＝开口前让一段 <c>BoardPolicy.CloseFlushGraceMs = 150</c>（两条来路共用那颗 <c>FlushSnapshotAsync</c>），
  并给那一行补一列 <c>same=</c>；改后抬笔即关两跑各 <b>2/2</b>。正本 11 颗 <c>bin/WI90/readings_32_*.txt</c>（⛔ 进库）。
- <b>P-219</b>：导出落点归属还没裁（png／svg 接进来那一批先裁再写）。
- 「未保存离开＝就地三颗按钮」照旧欠着；那颗 <c>.bak</c> <b>只留最近一份</b>这件事⛔ 因 P-224 的 (C) 出路而变（那条出路本批⛔ 取）。
- ⚛ 本批⛔ 量过的分支：<b>多块板同开＝片32 已量（两颗都落地＋并发那对尺读到了，见 §27.5 末与 ✅P-230）</b>；还欠跨机器（别的 GPU／DPI／系统缩放）、深色主题下面板与白帧的观感、以及把 <c>feature.flags</c> 真开起来那一档。

### 27.6 片27 落地腿：那道闸门开始说话（P-224 结案）

- <b>为什么"拒"这件事必须上屏</b>：P-223 那道闸门修完之后它<b>拒绝得对</b>，可对的方式是把用户留在一块静止的画布前——这块板从此⛔ 再落盘，而他手上唯一的读数⛔ 是"我画的那些没了"。本仓口径（P-53／P-54 那一族）：静默的坏比坏本身更坏，且⛔ 把"看一眼日志／重启一次看看"当出口。
- <b>落的顺序＝先自愈，再开口</b>：Core 加 <c>ArmRetryBudget＝1</c> ＋ <c>ShouldRetryArm(tries)</c>（认不出那颗数＝⛔ 再递，与"读不懂就⛔ 开闸"同一颗口径）→ UI 在 <c>board.loaded</c> 三颗数对不上时由<b>宿主自己</b>再递一次 <c>board.load</c>（走与首开同一颗 <c>PostBoardLoad</c>，两份开场代码＝第二个主人）→ 额度用尽才贴面板。<b>一拒就喊</b>会把"其实再递一次就好"那种一次性时序报成坏了；<b>一直递下去</b>＝把同一块空画布反复刷给用户看。
- <b>那颗按钮的射程</b>：交出去的是<b>宿主见过的那一份</b>字节，宿主自己写盘（⛔ 走 web 侧，⛔ 新名字）；先问系统"另存为"，宿主调不通（提权跨完整性级别只是其中一种成因）就退到 <c>exports\</c> 并把那条路径念出来（<c>boards.copySavedBody</c> 的槽交<b>数据</b>）。面板只在手上有字节时才给那颗按钮——按了没用的按钮是另一种静默。
- <b>形状上新学的一颗事实</b>：<c>Window.Content</c> 只许<b>一个</b>主人。重建控件那一趟从前写 <c>Content = _web</c>；把面板挂进同一层之后，那一句会<b>连面板带它正要说的那句话一起摘掉</b>。改法＝换 <c>_stage</c> 里的孩子并插回 index 0（面板永远压在画布之上），<c>Content</c> 只在构造里指一次。⇒"⛔ 第二主人"由此有第二种坏法：<b>⛔ 是多写一处，而是重写一处把同层的邻居顶掉</b>——加住户那一批必须回头改旧住户的写法。
- <b>换语言那一刻</b>：面板两行正文与那颗按钮都由 <c>PasteNotice</c> 在 <c>Locale.Changed</c> 那一拍重贴（那颗按钮的字是构造时冻进去的 <c>Content</c>，⛔ 一起换＝半中半英那张脸，P-175 那一族的第三种形状）；订阅挂构造、摘在 <c>Closed</c>，全仓那张"主人名单"（<c>I18nWiringGateTests.ExactlyOneSubscriberRenamesTheStringsAndItRebuildsNothing</c>）跟着补上第十三位——<b>补名字⛔ 放宽</b>（名册 305 同一形状第二次）。
- <b>表里 8 颗新键</b>（三语各一列，表里现读总数 <b>2073</b> 颗）：<c>boards.recoveringBody</c>／<c>boards.stuckTitle</c>／<c>boards.stuckBody</c>／<c>boards.saveCopyAction</c>／<c>boards.copySavedBody</c>／<c>boards.copyFailedBody</c>／<c>boards.closeFlushTitle</c>／<c>boards.closeFlushBody</c>；那一族从此 <b>10</b> 颗，由 <c>BoardWordGateTests</c> 五格逐颗点名（表里各一行／代码里各有人问／三列互异／那颗槽＝实参数／同一颗键两次渲染跟着两次语言走）。
- <b>读数</b>：编译 <c>dotnet build StarMark.sln -c Debug -p:Platform=x64</c>＝0 Warning／0 Error；定向证人 <c>152/152</c>（含本批 9 格新证人）；全量＝<b>6275/6275</b>（红 0）；反向台架 <c>bin/WI87/bench_27.py</c>（r4，判红谓词与臂号守卫修好后整表重跑）＝M0 对照件全绿／14 臂合格／BAD 0／被红过的格 9/9／三档源码与快照逐字节还原核对通过
- ⚠ <b>那一档在片28 <b>兑现成一颗真缺陷</b>（照实记）</b>：把 <c>board.loaded</c> 的回话改坏一颗数（只改沙盒副本，⛔ 动业务码）之后，面板与那颗按钮<b>真机读到了</b>：三跑逐字同一条链 <c>ready hasScene=True → arm.refused want=2 loaded=3 canvas=2 tries=0 → arm.retry n=1 → arm.refused … tries=1</c>，屏幕上的字走 UIA 转储（标题 416×18／正文 416×35／Button「另存一份到别处」122×32）。<b>量的第一件事是那颗按钮看不见</b>：整框 0.00% 深色像素、最暗一颗亮度 250.0（底色 <c>#fafafa</c>、字 <c>#ffffff</c>），而同一次进程里标题那一行量到 8.07% ⇒ 阳性对照证明尺会动。修＝那颗按钮三样颜色一起钉死（✅P-225）；修后<b>亮色与深色两跑逐位相同</b>（dark_pct 8.99%／最暗 5% 均值 <c>#000000</c>／非白 21.64%）⇒ 点验单②那一格一并清掉，而"钉死＝两边都⛔ 跟着主题走"这句从此有读数。<b>按下去之后的那一腿在片29 更正并结案</b>：那颗「另存为」由<b>别的 PID</b> 持有（#724）。片28 那句"按【取消】之后盘上没写、日志没声＝那一问⛔ 回音"<b>是错的</b>——错的⛔ 是我读码的判断，而是那台尺：UIA <c>Invoke</c> 对公共对话框那颗 <c>Pane</c>【取消】rc=0 而<b>那颗框还开着</b>（#730 因此补了一颗"动作真发生了"的正件）。换成 <c>WM_CLOSE</c> 之后量到的真相＝旧代码<b>自己写一份 858 字节的副本进 exports\ 并在屏上念"已另存到 …"</b>（用户点的是取消）⇒ 改成 <c>BoardPolicy.ClassifyCopyPick</c> 三档：取消＝⛔ 写、只留一行 <c>boards.copy.declined</c>；框起不来才退兜底（✅P-226）。⚠ 改前改后各跑了同一双腿，而改后第一跑测的是<b>旧 dll</b>——那棵 <c>out/</c> 产物树⛔ 随 build 更新（#729）。另有一颗挂在 <b>P-227</b>：闸门拒掉的 <c>bytes=1510</c> 永远进不了 <c>_seenScene</c>，所以那颗按钮交的是<b>上一份可信的</b>而⛔ 是屏幕上这一块（行为刻意为之，措辞待裁）。
- <b>(C) 那条出路⛔ 取</b>：拒之前先另存一颗带时刻的副本，会同时改"板上多一颗档"与那颗 <c>.bak</c> <b>只留最近一份</b>这两件事——它与导出落点归属（P-219）同族，先裁再写。
- 仍欠（<b>片32 就地改口</b>：多块板同开那格量到了，代价是量出一颗真缺陷并同批修掉＝✅P-230）：P-219／「未保存离开＝就地三颗按钮」；那颗 ⚛ 只剩跨机器／深色主题观感／<c>feature.flags</c> 那一档。

【片59 追记·那颗上限的真机形状】两拍都量到<b>拒</b>：停笔那一拍 <c>too-big b64=11187060 decoded~=8390295 ceiling=8388608 kind=scene</c>（正本 <c>bin/WI98/readings_59_idle_234308.txt</c>）；关窗那一拍同一档 <c>scene.saved 行数=0</c> 而盘上那颗档停在 <c>890</c> 字节＝上一份没被盖（§21.7 那句「拒掉⛔ 等于清空」现在有了读数）。同一跑另量到两件事：① 停笔那一拍<b>不需要宿主请</b>（<c>asks=0</c> 而写照样落地）＝§22.4 那条「谁开的口」从此可由档内回读；② 痕里那颗 <c>landed=True</c> 在拒掉那一支仍报「落了地」——一句话两件事，另立 P-247。
⚠ 还有一条要写进 §22.3 的口径：片58 那四档梯子的<b>后两档</b>（8 MiB／25 MiB）今天由成品<b>复测为拒</b>，那一跑跑的是一颗<b>闸门还没生效的 Core</b>（沙盒对账当时只比 <c>StarMark.UI.dll</c>＝坑表 #841）。「长边」那一半照旧⛔ 读数：随包 bundle 里 <c>resizeImage</c>／<c>4194304</c>／<c>naturalWidth</c> 三处同名命中经逐处读上下文分别是提示句、React 优先级位、pica（导出缩放）——<b>命中⛔ 等于命中对的那处</b>（#842），P-246 继续开放。

【片60 追记·插图那一路的<b>可达性</b>（§21.7／§25.4 的补半·⛔ 起窗）】随包 bundle（4878772 颗字节／sha <c>923fa221d0a4fc22</c>）现读：那颗 <c>,px=1440</c> 与 <c>,Vw=4*1024*1024</c> 各只一颗声明（offset 2166993／2167001），<b>唯一</b>读它的消费点在 <c>initializeImage</c> 体里（offset 4768057，次序＝<b>先 pica 压长边、再判字节</b>；压失败只 <c>console.error</c> 后拿原颗过判据；<c>svg</c> 整支⛔ 过这两道；同 id 已有 <c>dataURL</c> 时两道全跳）。而 <c>initializeImage(</c> 的调用全档<b>只有一颗</b>（offset 4769096）＝<c>insertImageElement</c>，它起手 <c>if(!this.isToolSupported("image"))</c>，判据逐字 <c>this.props.UIOptions.tools?.[E]!==!1</c>（offset 4734622）⇒ 我们桥里那颗 <c>tools: { image: false }</c>（<c>board-bridge.js:208</c>〔片66 现读：那颗开关从 207 挪到 208，因为片64 在 <c>baseProps</c> 里多写了两颗旋钮〕，§25.4 那臂 32→30 的量到它）<b>把整条插档路关掉</b>：粘贴（4718099）／拖放（4773570）／工具条那颗按钮三处入口一并挡住，屏幕上那句话是引擎自家的 <c>Images are disabled.</c>。<b>对整合方案的意义</b>：①「贴图进画板」⛔ 是"引擎不支持"，是<b>我们自己关掉了那颗工具</b>——将来要接（P-242 那一族）就同时接上 1440 长边与 4 MiB 每张这两道，而宿主那颗 8 MiB 管的是「一发整档」，两颗口径⛔ 是一回事；② 我们<b>载档那一路</b>（载入三步 → <c>addFiles</c> → <c>addMissingFiles</c>）⛔ 经这两道＝档里的图字节照原样进出（片13 那条直线量的正是这条路）；③ 那颗开关的第二把扳机＝<c>case "feature.flags"</c>（<c>board-bridge.js:152</c>）交的是<b>整颗 UIOptions 替换</b>而⛔ 合并，宿主今天 0 处发（名字住在 <c>BridgeProtocol.cs:44／:78</c>）——将来任何"递半张表"的写法都会顺手把 <c>tools.image</c> 翻回来，那颗字段必须每次带全。正本：<c>bin/WI98/readings_60_bundle_r2_001740.txt</c>／<c>readings_60b_bundle_002040.txt</c>／<c>readings_60c_bundle_002145.txt</c>；结案口径＝账本 ✅P-246。

## 二十八、片66 对账腿（2026-10-21·⛔ 起窗⛔ 动一行业务码⛔ 动语言表⛔ 动随包资产）：§二十一 那份 v1 与<b>今天真跑着的那份代码</b>逐条对一遍

> 这一腿⛔ 评价设计好坏，只做一件事——把「<b>v1 说要什么</b>」与「<b>盘上那份代码现在是什么</b>」并排写清楚，每颗结论给一个我能<b>现读</b>的行号（正本＝<c>bin/WI98/fix66_docs.py</c> 里那张 <c>CITE</c> 表，收口那颗只读尺 <c>bin/WI98/vdoc_66.py</c> 逐颗回读那一行确认真的含着那颗字）。
> 三处<b>同档自相矛盾／行号漂移</b>已在原句就地补〔片66 就地更正〕，旧话⛔ 删（那是历史，删了下一位读者就查不到我什么时候说错过）：v1 状态那颗（:1098 说「已代裁开建」、:1201 还写「待评审」）、§19.5 那两臂那颗（:1102）、§二十七 末那颗 <c>board-bridge.js:207</c>。

### 28.1 §19.5 那两臂今天各在哪一格里

| 臂 | v1 挂的状态 | 今天 | 凭什么 |
|---|---|---|---|
| ①（<c>EXCALIDRAW_ASSET_PATH</c> 设／⛔ 设两跑） | ⛔ 跑 | <b>已跑</b> | 片13 那一腿就是这两臂（§二十二.1），读数在 §二十二；那颗全局今天写死在 <c>board.html</c> 里、在 bundle <b>之前</b>落 |
| ②（拖一颗 4K 照片味 PNG 进去，量 1440 之后进不进 4 MB） | ⛔ 跑 | <b>前提消失</b> | 桥的 <c>baseProps</c> 交 <c>tools: { image: false }</c>（<c>board-bridge.js:208</c>），引擎那颗 <c>isToolSupported("image")</c> 一假⇒粘贴／拖放／工具条那颗按钮<b>三处入口一并挡住</b>（§二十七 末那颗真机读数是 <c>Images are disabled.</c>） |

⚠ 「前提消失」与「⛔ 跑」⛔ 是同一颗欠项：前者要说清<b>是谁把它关着的、什么时候会被翻开</b>。那颗开关是<b>我们自己</b>交的，而 <c>feature.flags</c> 交的是<b>整颗 UIOptions 替换</b>（<c>board-bridge.js:152</c>）⇒宿主今天一颗也没发（0 处），将来谁「递半张表」就会顺手把 <c>tools.image</c> 翻回真——那一臂随之重新变成「⛔ 跑」。

### 28.2 §21.1 那两张名单换成现状（进向 8／出向 6，字段各有一颗主人）

- <b>进向</b>（<c>BridgeProtocol.cs:37</c> 那一行）八颗。v1 那张表写了六颗，后来加了两颗：<c>board.loaded</c>（片26 那颗带数的开闸回音）与 <c>bridge.reply</c>（片56 给「这一发发得出去吗」配的回音）。
  今天<b>页面真发的</b>四颗：<c>bridge.reply</c>（<c>board-bridge.js:43</c>）／<c>export.bytes</c>（<c>:67</c>）／<c>board.loaded</c>（<c>:131</c>）／<c>bridge.ready</c>（<c>:263</c>）。
  <c>open.external</c> 反过来：<b>宿主有 case（<c>BoardWindow.cs:495</c>）而页面一颗也没发</b>。剩下三颗（<c>paste.query</c>／<c>pin.request</c>／<c>background.request</c>）过了名字门、也⛔ 有主人，宿主只落一行痕 <c>boards.cmd.not-wired</c>（<c>:515</c>）＝v1 那句「⛔ 假 allow」今天兑现着。
- <b>出向</b>（<c>BridgeProtocol.cs:44</c>）六颗。v1 那张表写了四颗，后来加了 <c>autosave.arm</c>（片24）与那颗回音的分流。<c>export.pick</c>（<c>:895</c>）／<c>theme.set</c>／<c>lang.set</c>／<c>board.load</c>／<c>autosave.arm</c>（<c>:971</c>）五颗真发；<b><c>feature.flags</c> 在册而宿主 0 处发</b>。
- <b>字段</b>：<c>export.pick</c> 从 v1 那两颗变<b>四颗</b>（<c>kind</c>／<c>skipInliningFonts</c>／<c>exportScale</c>／<c>exportBackground</c>，<c>:82</c>＝片64 那两档交回用户）；v1 原本写在 <c>export.bytes</c> 里那颗「会话 id」由片23 摘掉（没人读）。

⇒ 一句口径：<b>名字在册⛔ 等于这条路通</b>。协议那张名单今天比回路宽出一颗（四颗⛔ 发话人＋一颗⛔ 主人也⛔ 消费），那是<b>故意的</b>——先登记再接线，接线那颗批次⛔ 许自己往门里塞新名字（硬线②那道门读的就是这张表）。

### 28.3 §21.1 那六条硬线逐条现状（落地 2／换形状 2／前提消失 2）

| 硬线 | 今天 | 现读凭据 |
|---|---|---|
| ① 路径永远⛔ 进 <c>args</c> | <b>落地</b> | 名单里一颗路径键也没；名字一律过 <c>SanitizeExportName</c> 再拼，落点由宿主定 |
| ② 命令名＋字段名两张白名单（多一颗键就拒） | <b>落地</b> | 进向那道在分发之前（<c>BoardWindow.cs</c> 的 <c>Check</c>→<c>switch</c>）；出向反过来也有一道，不过就<b>一颗字节也⛔ 发</b>（<c>PostWebMessageAsJson</c> 只 1 处，<c>:446</c>） |
| ③ <c>cmd</c>↔<c>id</c> 配对表只活宿主一侧；超时＝报错、⛔ 静默重试第二次副作用 | <b>换形状</b> | 今天<b>没有</b> id→回调那张表：<c>BoardEnvelope.Id</c> 解出来而⛔ 读者。取而代之的是<b>单槽闩</b>（<c>_exporting</c>＋<c>_exportGen</c> 代次号）与那一发的预算 <c>ExportAnswerBudgetMs</c>（<c>WatchExportAnswerAsync</c>，<c>:784</c>）；到点只报错、确实⛔ 重发第二发 ⇒ 那句「⛔ 静默重试」兑现，那句「配对表」⛔ 存在 |
| ④ ⛔ 拿桥当第二条数据通道 | <b>落地一半</b> | 字节走「一次一问」那一臂、且<b>解码之前</b>先问上限；「控制消息按亚毫秒算」那颗预算<b>只住在本档正文里</b>——代码⛔ 那颗常数也⛔ 那把尺 |
| ⑤ <c>onPaste</c> 那颗钩⛔ 许抛、拒必须显式返 <c>false</c> | <b>前提消失</b> | 桥里⛔ 装 <c>onPaste</c>（0 处），paste 走引擎默认；那颗「宿主自己 catch 成 deny」今天⛔ 需要也⛔ 存在（要等 <c>paste.query</c> 接线那一批，与 §28.2 那三颗一起裁） |
| ⑥ 五颗订阅的退订句柄存进会话表，<c>Closed</c> 里逐颗调 | <b>前提消失</b> | 页面<b>⛔ 走 <c>api</c> 订阅</b>：唯一那颗 <c>onChange</c> 是 React 的 <b>prop</b>（<c>board-bridge.js:179</c>），没句柄可存。宿主侧只摘<b>自己</b>那三颗事件＋那颗语言事件，再 <c>_web.Close()</c>（<c>:1680</c>）⇒ §21.6 那格「摘完之后⛔ 再收到桥消息」今天<b>⛔ 对手</b>（⛔ 是「过了」，是「没东西可摘」） |

### 28.4 §21.6 那张点验单八格现状（照实标 ⛔ 量／前提消失，⛔ 混进「已通过」）

- <b>导出四路</b>：今天<b>三路</b>——png／svg 两颗按钮 + <c>scene</c>（四拍里那一路）；<b>「复制为图」⛔ 接</b>（<c>exportToClipboard</c> 只在 <c>tools/boards/entry.js</c> 里被 re-export，0 处调用）。
- <b>桥越权</b>：路径那格、命令名那格<b>有证人</b>；v1 新加的那格「伪造 <c>paste.query</c> 的 data 形状」今天只对着一颗⛔ 消费的判定函数红（§28.3 ⑤）；<b>iframe／embeddable url 那格⛔ 证人</b>——见 §28.5。
- <b>旧 embeddable 场景打开⛔ 加载远程</b>：<b>⛔ 落地</b>〔片67 就地更正：这一格已落<b>能机检的那半</b>，屏幕上那句 <c>toast.unableToEmbed</c> 照旧欠拍＝§二十九.7〕。<c>validateEmbeddable</c> 在桥的 props 里 0 处（v1 §21.0 第 7 行那句「那颗 prop 交 <c>false</c>」今天没写）。
- <b>场景回环→undo 栈清空</b>：<c>captureUpdate: NEVER</c> 落地（载档那一路）；<c>history.clear</c> <b>⛔ 调用点</b>——「栈干净」今天由「一板一窗、每扇新页」间接兑现，⛔ 是那颗旋钮动过。
- <b>关窗 60 秒进程树=0／userData 落点</b>：落点那颗钉子落地并有证人；<b>60 秒那格在产品窗上⛔ 量过</b>（v1 自己写了「⛔ 拿片8 那跑顶替」，这句照旧算欠项）；§21.5 那句「兜底＝按自己 pid 的子树清 <c>msedgewebview2</c>」<b>⛔ 落</b>（那一族档里 0 处）。
- <b>F1／全局键共存</b>：<b>⛔ 有码证、⛔ 读数</b>（画板那一路压根碰⛔ 着键盘；全局键住在主窗那颗 <c>RegisterHotKey</c>）。这一格与 §28.5 那颗一起排真机腿。
- <b>多板窗同文件</b>：片32 量过两扇同开那一臂（✅P-230 那一节），冲突表行为＝聚焦已有那扇，v0 那句没改。
- <b>TTD／AI／Publish 那颗按钮在包内不存在或摘除</b>：随包资产冻结批（片18）＋接管表（§25）已经把菜单交给宿主自排，引擎那颗 <c>registerAction</c> 我们⛔ 用——这一格算<b>间接兑现</b>，屏幕上那颗「不存在」⛔ 单独拍过。

### 28.5 本腿新撞的一颗（⛔ 混进上面那些「⛔ 落地」）：<b>出机三道今天只落了一道</b>

v1 §21.5 写「出机三道：宿主注入 CSP；<c>WebResourceRequested</c>；<c>esm.sh</c> 永久 451 当最后一道」。现读：

- <b>第一道 ⛔</b>：<c>board.html</c> 整档 21 行，<b>CSP／<c>sandbox</c> 0 处</b>（一句 <c>window.EXCALIDRAW_ASSET_PATH = "/"</c> 加两颗 <c><script></c>）。
- <b>第二道 ⛔</b>：画板那一路的档里 <c>WebResourceRequested</c>／<c>NewWindowRequested</c> <b>0 处</b>；〔片67 就地更正：<c>WebResourceRequested</c> 已订上，但那是颗<b>只数⛔ 挡</b>的观察者（§二十九.4），<c>NewWindowRequested</c> 照旧 0 处⇒这一道仍算⛔ 落〕；唯一那颗出站约束是<b>名字</b>给 <c>open.external</c> 的那颗 http(s) 白名单（而它今天⛔ 发话人）。
- <b>第三道 在</b>：随包资产走那颗虚拟站映射（<c>SetVirtualHostNameToFolderMapping</c>，<c>BoardWindow.cs:412</c>），字体／locales 因此⛔ 打外网（§二十二.1 量的正是这一颗）。
- 引擎那颗官方闸门 <c>validateEmbeddable</c> 也<b>⛔ 交</b>（§28.4）。〔片67 就地更正：今天交了 <c>false</c>，且钉的是它住在 <c>baseProps()</c> 的<b>顶层</b>那一层（§二十九.2）；本行写成时是真的，留着当反面教材——那句「⛔ 交」正是那一腿现读出来的〕。

<b>可达性我这一腿判⛔ 了</b>：这一批⛔ 起窗，而「载一颗带 <c>embeddable</c> 元素的样档，看它请⛔ 请远程 URL」必须由那颗真机臂量。⛔ 拿「闸门没装」冒充「已经被人穿过」，也⛔ 拿「今天没人走得到」冒充「⛔ 需要装」——本仓唯一挡着远程那一路的，今天只有<b>那颗 <c>tools.image</c> 开关管⛔ 着 embeddable</b>（两颗⛔ 是同一颗旋钮）。⇒ 登记 <b>P-248（开放）</b>，处置＝官方那颗 prop 交 <c>false</c> ＋ 宿主订 <c>WebResourceRequested</c>／<c>NewWindowRequested</c> 白名单（两道一起做才算结），结案入口＝那一跑真载一颗样档并读那颗网络计数。

### 28.6 这一腿欠的（⛔ 混进已读）

- §28.4 那三格「⛔ 量」（关窗 60 秒・F1 共存・那颗「屏幕上⛔ 存在」的逐颗照片）照旧欠，全部要那颗窗，与 P-242／P-247 排同一条真机腿。
- <c>feature.flags</c> 与「整颗替换 vs 合并」那颗形状，本腿只在源码里判到「宿主 0 处发」；<b>谁会发、发半张表会怎样</b>还没量。
- 本批账本加减＝<b>新立 1（P-248）／结案 0</b>；计数器与真开放那颗数由只读尺 <c>bin/WI98/vdoc_66.py</c> 现取（⛔ 从上一批抄）。

## 二十九、片67 落地腿（2026-10-21·改码批·⛔ 起窗）：P-248 那两道里<b>今天能机检的那半</b>落了，读数那半照旧欠着

> 这一批⛔ 评价「该不该挡」，只做一件能做的事：把<b>不需要读数就成立</b>的那两处装上（引擎自家那颗 prop・宿主那颗分类判据），
> 再装上<b>会产出读数的那把尺</b>（一颗只数不挡的出站观察者）。挡住那一步<b>故意留在下一批</b>——理由写在 §29.4。

### 29.1 三处各改了哪一行（位置由脚本现取，尺 <c>vdoc_67.py</c> 再复算一遍）

| 层 | 今天多出来的东西 | 现读位置 | 为什么放这一层 |
|---|---|---|---|
| 页面（随包桥） | <c>validateEmbeddable: false</c> 作为 <c>baseProps()</c> 的<b>顶层 prop</b> | <c>tools/boards/board-bridge.js:216</c>（随包那份 <c>assets/boards/board-bridge.js:216</c>，两颗由片21 那颗漂移证人钉成同一份字节） | 判不过时引擎自己走 <c>toast.unableToEmbed</c>，档里那颗远程 iframe ⛔ 被渲染（v1 §21.0 第 7 行早就这么写） |
| Core（判据） | 一颗方案常数＋<b>三档</b>分类：随包／压根⛔ 出机／别处 | <c>src/StarMark.Core/Boards/BoardsAssetPolicy.cs:46</c>・<c>src/StarMark.Core/Boards/BoardsAssetPolicy.cs:59</c>・<c>src/StarMark.Core/Boards/BoardsAssetPolicy.cs:69</c>・<c>src/StarMark.Core/Boards/BoardsAssetPolicy.cs:72</c>・<c>src/StarMark.Core/Boards/BoardsAssetPolicy.cs:77</c> | 「这一发算哪一档」是<b>判据</b>，宿主只照它分类；名字（那颗虚拟站）⛔ 进 Core＝第二份主人（证人第五格扫全档） |
| 宿主（订阅） | <c>AddWebResourceRequestedFilter("*", All)</c> ＋那颗观察者，排在 <c>Navigate</c> <b>之前</b> | <c>src/StarMark.UI/Boards/BoardWindow.cs:425</c>・<c>src/StarMark.UI/Boards/BoardWindow.cs:427</c>・<c>src/StarMark.UI/Boards/BoardWindow.cs:453</c>（那颗起页在 <c>src/StarMark.UI/Boards/BoardWindow.cs:431</c>） | 先订再看：晚一行的那一发出机根本进不了尺，而那份名册正是下一批翻 deny 的唯一依据 |

那颗痕今天长这样（一颗<b>新</b>目标只念一行，三颗计数各有一条出口）：<c>boards.net.foreign n=・target=&lt;方案+主机&gt;・local=・inline=・id=</c>（<c>src/StarMark.UI/Boards/BoardWindow.cs:471</c>）。

### 29.2 那颗引擎 prop：钉的是<b>层</b>，⛔ 是「串在不在」

包里的消费点两处（片66 现读，本批重跑照旧）：<c>this.props.validateEmbeddable</c> 与 <c>$s(D, i.validateEmbeddable)</c>，
判不过走 <c>toast.unableToEmbed</c>。它交的是 boolean 那一档（包里逐字五档：boolean／字符串数组／RegExp／RegExp 数组／函数），
交 <c>false</c>＝全拒。

证人因此⛔ 只问「档里有没有那颗串」，而是<b>数花括号层数</b>：那颗 prop 必须与 <c>excalidrawAPI</c>／<c>onChange</c>
同层（第 1 层），而 <c>UIOptions</c> 里那些在第 2 层往下。台架两臂各打红一颗（正本 <c>bin/WI98/bench67_console_r1.txt</c>）：
A1 把值翻成 <c>true</c> ⇒ 只红「值那一格」；A2 在 <c>UIOptions</c> 里多塞一颗<b>同名而嵌在里层</b>的 prop（顶层那颗与那句 <c>: false</c> 照旧在场）
⇒ 只红「层那一格」而「值那一格」照旧绿。<b>少了 A2 这一臂，「层」那一格只是我数出来的一句自证。</b>

⚠ 这颗开关<b>管的是</b>那颗 prop 认不认远程 iframe，⛔ 等于「宿主⛔ 需要第二道」——v1 那句「兜底永远排在名单最后」照旧成立（§29.5）。

### 29.3 三档⛔ 够，第四档才是这一批的坏

最初那版只分「随包／别处」两档，一跑就把 <c>data:</c> 记进了「别处」。这一族的后果不是多一行痕，而是<b>下一批照那份名册翻 deny 时画布白屏</b>：
片21 定的存储口径＝<b>内嵌 base64</b>，画布上那些图今天全都是 <c>data:image/…</c>（§28.1 那颗 <c>tools.image</c> 关的是「用户往里拖图」那颗入口，
⛔ 是「档里那些图怎么表示」）。所以那张名单是<b>三档</b>（<c>src/StarMark.Core/Boards/BoardsAssetPolicy.cs:69</c>），且配一颗<b>禁项格</b>：<c>InlineUriSchemes</c> 里⛔ 许出现
<c>https</c>／<c>http</c>／<c>ws</c>／<c>wss</c>（台架 A6 就往那里塞一颗 <c>https</c>，那一格响）。

同族第二颗：⛔ 一颗计数只加不念。<c>local</c> 那一颗在最初的写法里进不了那句痕＝死出口（片23 摘 <c>sessionId</c> 那一族换了个身体）；
今天三颗一起念，因为「名册里一颗也没有」有两种脸（这页真⛔ 外呼・这把尺压根没开火），只看 <c>n=</c> 分不开（台架 A10 删掉那颗槽，证人响）。
同族第三颗：那颗虚拟站的<b>名字</b>今天仍只有一位主人（<c>src/StarMark.UI/Boards/BoardWindow.cs:108</c>）——Core 那颗判据收实参、证人在全仓 <c>src</c> 里扫那颗串，
多一处写死的名字＝换名字那一批只改一处而另一处静默失真（台架 A11 就是往宿主里塞一份 <c>uri.Host ==</c>）。

### 29.4 为什么这一批<b>只量</b>⛔ 挡（安全＞稳定＞性能 这一次怎么落地）

「挡」看着更安全，但它要一颗<b>白名单</b>，而白名单的每一项都要有读数。本批⛔ 起窗（桌面被他的窗占住，见 §29.7），
所以随包那棵树到底靠不靠一颗外站，今天<b>⛔ 知道</b>。拿一颗写死的名单去挡一条没读数的路＝把「画布白屏」的风险换成一句安心话，
而这一族坏在本仓有个名字：名册 347／349「解析器恰好拒⛔ 当地基」。⇒ 这一批交出的只有<b>会读数的那把尺</b>＋<b>判据</b>；
<c>e.Handled</c>／<c>e.Response</c> 一颗也⛔ 有，证人第七格钉的就是这件事（台架 A9 塞一手 <c>Handled = true</c>，那一格响）。

⚠ 这<b>⛔ 是</b>把 P-248 那条处置里否掉的 (C)「先量可达性再决定」翻回来。那颗量的是「<b>档里那颗远程 iframe 真请得到吗</b>」，
要造一颗风险样档摆到屏幕上⇒照旧⛔ 取，且它仍是本条的<b>结案入口</b>；本批量的是另一颗问题——「<b>我们自家那页合法依赖谁</b>」，
纯观察、⛔ 需要任何样档。⇒ 被拆开的只有 (B) 那一刀：「先订上并读数」与「再照名单翻 deny」两拍；名单到手前，
那道闸门的<b>形状</b>（三档分类＋唯一主人＋三颗计数各有出口）今天已经全部到位并可证。

### 29.5 P-248 还剩什么（<b>照旧开放</b>，结案入口没挪）

- <b>第一道</b>：<c>board.html</c> 今天 21 行，<c>CSP</c>／<c>sandbox</c> 现读 <b>0 处</b>⇒仍⛔ 注入。
  ⚠ 这一颗照旧欠着而<b>⛔ 单独补</b>：CSP 写歪（少一颗 <c>worker-src</c>／<c>blob:</c>）会把那颗 wasm 与那些 <c>data:</c> 图一起没，
  而「写歪了」这件事<b>只有起窗量得到</b>⇒它与 (B) 那一翻该排同一条腿（名单到手 ⇒ CSP 与 deny 一起改 ⇒ 同一条腿验「挡对了什么・没挡错什么」）。
- <b>第二道</b>：那颗观察者<b>在场而⛔ 挡</b>（§29.4）；<c>NewWindowRequested</c> 照旧 0 处（弹窗那一族今天只有「宿主只开白名单里的 http(s)」那半在守）。
- <b>读数</b>：<c>boards.net.foreign</c> 一颗也⛔ 取过——那一跑要那颗窗。
- <b>可达性</b>：档里带 <c>embeddable</c> 的样档到底请⛔ 请远程 URL，仍欠（与 P-242／P-247 排同一条真机腿）。
⇒ 本批账本加减＝<b>新立 0／结案 0</b>；P-248 的处置从「两道一起做」推进到「两道各落了能机检的那半」，计数器三串由成品现读（见那颗只读尺）。

### 29.6 读数表（每颗数带正本，⛔ 由我这句话担保）

| 那颗数 | 现读 | 正本 |
|---|---|---|
| 那颗桥的字节 | 15493 → <b>16338</b> 颗字节（清单 sha <c>c37e39b12148…</c>） | <c>bin/WI98/stage67_r1.txt</c>＋<c>git show HEAD:assets/boards/manifest.json</c> |
| 那两份 JS 的语法 | <c>node --check</c> 源件 rc=<b>0</b>・随包件 rc=<b>0</b> | <c>bin/WI98/nodecheck67_r1.txt</c> |
| 那颗 bundle | <b>4878772</b> 颗字节（本批⛔ 重打包＝与 HEAD 那颗逐颗相等） | 同上 |
| 随包整棵树 | 140 颗／11.29 MB | <c>bin/WI98/stage67_r1.txt</c> |
| 编译 | <b>0 警告／0 错误</b> | <c>bin/WI98/build67_r1.txt</c> |
| 那颗新证人定向 | <b>9</b> 颗通过（9 格：LocalAssetUriAcceptsOurVirtualHostInAnyCaseAndAnyPath／LocalAssetUriRefusesOtherHostsOtherSchemesAndLookAlikes／InlineSchemesAreTheirOwnClassNeitherLocalNorForeign／TraceTargetCarriesSchemeAndHostOnlyNeverAPath／TheVirtualHostNameHasExactlyOneOwnerInTheProduct／HostSubscribesTheObserverBeforeItNavigates／ObserverCountsThreeClassesAndBlocksNothing／BridgeTurnsOffTheEngineEmbeddableValidator／ThatValidatorSitsAtTheTopLevelOfBaseProps） | <c>bin/WI98/test67_target_r2.txt</c> |
| 反向台架 | <b>11 臂｜BAD=0／0</b>（M0 改前 912 颗绿） | <c>bin/WI98/bench67_console_r1.txt</c>・<c>bench67_console_r2.txt</c> |
| 全量 | <b>6430／6430（失败 0）</b>，耗时 14 m 17 s | <c>bin/WI98/test67_full_r1.txt</c> |

### 29.7 这一腿欠的（⛔ 混进已读）

- <b>那颗观察者的一次真读数</b>：开着画板走一遍（含打开一张带 <c>data:</c> 图的旧板），把 <c>local=</c>／<c>inline=</c>／<c>target=</c> 三列取到手——
  那是下一批翻 deny 的唯一入口，也是 P-248 的后半。今天桌面被占，⛔ 绕、⛔ 抢（名册 521 那一族），记欠项。
- <b>弹窗那一族</b>（<c>NewWindowRequested</c>）本批没碰：它该与 CSP 同一批装，因为两颗都⛔ 起窗判不了「挡错了什么」。
- 那颗 <c>validateEmbeddable</c> 的<b>屏幕脸</b>（打开一张带远程 iframe 的样档，看引擎那句 <c>toast.unableToEmbed</c> 上不上屏）⛔ 拍——
  源码与包内消费点已判到，屏幕上那句照旧欠。
