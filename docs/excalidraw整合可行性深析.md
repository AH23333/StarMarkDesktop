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

一颗窗、一条通道、<b>两份名单</b>：JS→C# 的<b>命令白名单</b>（名单外一律拒并记日志），C#→JS 的<b>事件白名单</b>。
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
| autosave | 停笔 1.5 s 或窗失焦即存；<b>另存一份 <c>.bak</c></b>（只留最近一颗） | 引擎自带的 autosave 住在 <c>FileManager</c>／IndexedDB（§坑11），桌面接管后<b>时机归 C#</b> |
| 同文件多窗 | 打开时登记 <c>(id → pid 窗实例)</c>；第二扇要开同一颗 ⇒ <b>聚焦已有那扇</b>，⛔ 起第二份 | 引擎的 <c>tabSync</c> 只在<b>同源同浏览器</b>的标签间成立，跨两扇 WinUI 窗⛔ 自动同步＝数据竞争面 |
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
  ⛔ 静默白屏（§坑10；"点了没反应"是本仓定性过的坏）。
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
   （我不在他机器上静默装工具链）。钉版本要钉 <b>commit</b>：现读 <c>ed10ac7d / v0.18.0-429-ged10ac7d</c>。
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
