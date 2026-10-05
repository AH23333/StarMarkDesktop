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
**坑4 · 字体 esm.sh 远程回退（实测硬编码，fonts/ExcalidrawFontFace.ts:24）** —— `createUrls` 本地 URL 在前回退在后，但**打包后 import.meta.env.PKG_* 缺失时回退变 latest 远程**。处置：构建时注入 PKG_NAME/VERSION（或 patch 掉回退）；spike 必须做**断网全字体验证**（中/英/emoji/手写体逐族，含 Xiaolai 中文断网）。
**坑5 · woff2 子集化 wasm 也是 fetch 加载（subset/woff2/woff2-bindings.ts 实测）** —— 子集化管线断网失效=字体回退+首屏字形突变。处置同坑4：wasm 随包 + 加载路径本地化核验（该 wasm 是 Emscripten 产物，URL 可配置性 spike 实证）。
**坑6 · 字体授权未核完** —— 11 族里 Virgil/Excalifont/Lilita/Cascadia/Xiaolai/Assistant/Nunito…许可证不一（Xiaolai 是 OFL、Cascadia MIT 可再查）；**代码 MIT 不含资产授权**；随包分发前逐族写 NOTICE（桌面仓库有 NOTICE 先例=WordList.Dic 那一族惯例）；Emoji 目录实测 0KB（可能是索引样式声明）——spike 核实它到底是什么，别默认中文 emoji 能画。
**坑7 · Xiaolai 单族 12.4MB 的体积决策** —— 不打=中文手绘观感降级为系统字体（用户拿它就是因为中文好看）；打=dist 从 ~2MB 级跳到 ~15MB 级（未实测 bundle 总量，**数字要实测后才许写**）。倾向打但列为 §九决策 4。
**坑8 · 内存红线（铁律①）** —— WebView2 进程树驻留是常态。硬约束：画板窗**按需建窗、关窗即 Dispose environment/Process.Kill 兜底实测进程数归零**；默认功能关=零资源。这条要进验收（任务书既有横切第 8 类"功能默认关零资源"的直接适用）；"关窗后 msedgewebview2 还活着"的坑在别的项目是常客。userDataFolder 强制改道 %LOCALAPPDATA%\StarMark\wbview2（默认跟随 exe 目录=开发态可写性雷——本仓 NuGet/安装目录两类事故的镜像）。
**坑9 · 全局键位互杀** —— 画板前台时 Ctrl+S/滚轮缩放/Z/Ctrl+Z 会被全局键层截走或反向。处置：画板进窗口状态机（Hub 会话位或并列的"画板前台"事实），注册表 GetRegisterableHotkeyBindings 按会话摘除（**该机制现成，但画板没被登记为会话态时要接**）；web 内键盘事件不回冒到 WPF/UIA 全局钩子的核验在 spike。
**坑10 · 崩溃边界** —— ProcessFailed（渲染进程崩）现形="画板白了"。处置：ProcessFailed→事件桥报+当前场景自动落盘（场景快照防用户丢图）+重载（非静默）。宿主纪律"崩溃不能带走主程序"（本项目托盘/采集全按这条办）。
**坑11 · 存储接管=职责表不是"顺手换个写盘"** —— IndexedDB/tabSync/LocalStorage（实测 EditorLocalStorage/appState 等 3 处）属 app 壳；桌面接管后：autosave 时机、同文件多窗冲突（**tabSync 的跨标签同步在桌面多窗下不自动成立，多板窗=数据竞争面**）、文件名↔场景 id 映射、"未保存离开"拦截。**此表进设计批，先写后码**。
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
关键：`BridgeProtocol` 单一 schema 出处；`StorageAdapter` 归 C#（web 内无 IndexedDB 写——`LocalData` 层禁用，引擎 onChange 上抛，防坑11 双主）；`export` 落点策略归 C#；"桥无路径"（JS 不决定文件写哪；用户开文件=WebView 发起→C# 弹框→选后回字节或只读流）。

---

## 七、非目标（写死防蔓延）

协作房间、excalidraw+、AI 生图（TTD）、云库发布、embeddable 远程、登录/PWA/离线包自管（**桌面版=我们离线化**）、图片 URL 抓取、官方 share 链接、Mermaid→（mermaid **转成编辑对象** 那半本地可用，AI 生 mermaid 码不走 mermaid 生图那半**——engine 本地算，可用；写清，防误传）。

## 八、建议批形（拍板后才排，非承诺）

0. **Spike 批（裁决前唯一允许的动作）**：①runtime 探测+环境释放实测；②字体/wasm 断网全验；③导出/下载出口 3 法各试（Downloading 事件/桥回调/剪贴板中转）；④TTD/Publish 关按钮有效性；⑤桥消息往返时延实测。产物=裁决数字表，产品码不进。
1. 架构批（桥协议+存储适配器设计文档先行，过评审再码）
2. 宿主窗+引擎 bundle（先跑通空板，不含集成）
3. 存储接管批（boards/对账/autosave/冲突表逐项对 §坑11）
4. 集成批×3（截图→画板/画板→贴图/Board→快照；ClipIMG→背景）
5. 检索沉淀批（items 注册+search_text 场景抽字）
6. 入口/设置批（主题键位共存/备份开关/隐私声明/快捷键摘挂）
每批：任务书逐条对（#182）、真机点验进 §十、坑表续排。

## 九、三（四）个人拍板项（不定=后续全停）

1. **构建交付模式**：A 预编译 dist 随仓（推荐：守零构建链；成本=仓体积+版本人工钉）；B 构建期 Node（第二工具链进 CI；升级顺滑但新风险面）。
2. **生命周期档位**：关窗即散（内存派，默认推荐）vs 空闲 N 分钟（体验派）。
3. **Xiaolai 随包**（12.4MB 换中文手绘观感）：倾向"是"，但数字见坑7。
4. **D 表全拒认吗**：collab/云/AI/发布全不带（本分析默认建议），若你要 AI 生图（TTD 自带 BYOK 接用户 Key 的路径），它是**另一个议题另立批**——不许顺手进 embed（默认 Key 不配时它=死按钮）。

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
