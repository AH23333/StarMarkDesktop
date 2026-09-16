# DeskBox 桌面组件 · 借鉴与优化意见

**文档类型：** 技术调研 + 架构意见
**撰写日期：** 2026-09-16
**调研对象：** `DeskBox-ref/`（参考实现，v1.5.2 时代文档）
**面向对象：** StarMarkDesktop 第八轮迭代后的桌面组件体系（Widgets 2.0）
**配套文档：** [项目开发技术文档.md](./项目开发技术文档.md) · [架构健壮性分析与演进.md](./架构健壮性分析与演进.md) · [开发进度报告.md](./开发进度报告.md)

---

## 目录

- [零、结论摘要](#零结论摘要)
- [一、DeskBox 桌面组件的核心实现方式](#一deskbox-桌面组件的核心实现方式)
- [二、StarMarkDesktop 现状盘点](#二starmarkdesktop-现状盘点)
- [三、借鉴建议（按优先级）](#三借鉴建议按优先级)
- [四、不该照抄的部分 · StarMark 的差异化机会](#四不该照抄的部分--starmark-的差异化机会)
- [五、建议落地顺序](#五建议落地顺序)
- [六、风险清单](#六风险清单)

---

## 零、结论摘要

**一句话结论：** DeskBox 值得学的不是"它做了哪些组件"，而是**它如何把"桌面窗口"这件看似简单的事做成一套有明确契约、可测量、可回滚的工程体系**；而 StarMark 最该守住的差异点是**统一条目数据库**——这是 DeskBox 完全没有的东西，照抄 DeskBox 的文件格模型反而是战略倒退。

三条最关键的意见：

1. **现在就收敛扩展缝。** StarMark 当前 `WidgetKind` 已在 7 处产生分支（§二.3）。DeskBox 实测到"25 个文件、单文件 34 处分支"才回头治理，代价高昂。StarMark 只有 5 种组件，**这是成本最低的收敛窗口，错过就不会再来**。

2. **把二元置顶换成层级策略。** StarMark 目前只有 `Topmost=true/false`（`WindowInterop.SetTopmost` → `HWND_TOPMOST`/`HWND_NOTOPMOST`）。DeskBox 明确把"持久 TopMost"视为坑——它会造出"永远压屏"这类最难查的 bug，且安全网失效。应引入"瞬态浮起 + 相对层级回落"。

3. **在引入任何文件移动能力之前，先把信任机制建起来。** DeskBox 1.4.5 因拖放回执语义错误把用户的 `.lnk` 送进回收站，1.4.8 复盘里又记录了"用户以为卸载删了文件"的信任事故。StarMark 技术文档的 Phase 3 已规划 `MoveFileAction`——**这是全项目风险最高的单点**，需先补齐"移动前展示计划 + 可撤销 + 卸载可找回"三件事。

---

## 一、DeskBox 桌面组件的核心实现方式

> 本节全部结论来自 `DeskBox-ref/docs/`，主要出处：`articles/00-overview.md`、`architecture/current_architecture.md`、
> `architecture/[重要勿删]widget_zorder_lifecycle.md`、`architecture/widget_contribution_seam.md`、
> `architecture/[重要勿删]file_drag_stack_contract.md`、`architecture/native_context_menu_hosting.md`、
> `architecture/startup-policy.md`、`docs/memory-optimization-plan.md`、`articles/deskbox-1.4.8-release-reflection.md`。
> 凡文档未明确之处均标注"文档未明确"，不做推断。

### 1.1 产品世界观：不替换 Windows，真实路径优先

DeskBox 对"桌面组件"的定义是：**摆在桌面上的可操作小窗口，背后是真实能力而非视觉贴纸**。

它的总览文档从一个具体场景切入——"电脑刚开机时桌面很干净，用上几天，浏览器下载、微信附件、截图、导出文件和临时文档就会一点点冒出来"，然后指出用户只有三个选择：立刻找最终目录（打断工作）、先放着（桌面变成一堆没有名字的待办）、继续建文件夹（目录越来越细）。DeskBox 提供第四个：**先把内容放进用途明确的格子，等工作走到合适的地方再归档**。

由此推出一条贯穿全部设计的底线（原文）：

> "两者都围绕真实路径工作，避免界面上看着整齐，资源管理器里却找不到文件。"

**关于"引用不移动"这个术语，需要澄清一处认知偏差。** StarMark 的《项目开发技术文档》Phase 4 写作"DeskBox 风格格子布局；「引用不移动」语义"——但 DeskBox 文档中**并未使用"引用不移动"这一表述**。与这个概念对应的、DeskBox 真实存在的语义是分三层、且严格区分的：

| 层次 | DeskBox 原文语义 | 出处 |
|------|-----------------|------|
| **映射文件夹 = 引用** | "目录本身不会被复制，也不会换位置"，格子与资源管理器展示同一批文件；删除格子只删入口 | `01-file-widgets.md` |
| **自动叠放 = 纯投影** | "不会创建真实子文件夹。不会修改扩展名。不会移动文件。不会改变资源管理器中的目录结构" | `11-file-stacks-and-quicklook.md` |
| **附件关联原路径** | "只保存原路径，不复制文件" | `03-todo-widget.md` |

**这个区分对 StarMark 很重要**：DeskBox 不是在"移动"和"引用"之间二选一，而是**把三类语义分别命名、分别实现、分别写进文档**。StarMark 若只记一个笼统的"引用不移动"，在实现阶段会重新踩一遍语义混淆的坑。

### 1.2 窗口模型与层级管理（最值得抄的部分）

**基本形态：** "格子本质上是一组无边框 Win32 窗口"（`widget_zorder_lifecycle.md` §1）。UI 层是 WinUI 3，但层级与桌面行为全部走 Win32 原语。

**两个宿主类：** `QuickCaptureWidgetWindow`（随记专用）+ `ContentWidgetWindow`（File/Todo/Music/Weather/Search 统一宿主）；早期 file-only 的 `WidgetWindow` 已删除。

**核心技巧 1 —— 瞬态浮起（transient raise）：**
先 `SetWindowPos(hwnd, HWND_TOPMOST, ..., SWP_NOACTIVATE|SWP_SHOWWINDOW)`，紧跟 `SetWindowPos(hwnd, HWND_NOTOPMOST, ...)`。窗口因此停留在"普通层级带的最顶部"，**但不具备 TopMost 属性**——别的应用激活时能正常盖过它。这比持久 TopMost 干净得多。

**核心技巧 2 —— 逻辑状态与物理落点分离：**
`DesktopResting` 只是逻辑态（"已退出临时唤起"），**不等同于物理上的绝对底层**。物理落点由策略现算：

| 条件 | 物理落点 |
|------|---------|
| 外部应用在前台 | `BehindForeground` |
| DeskBox 自身在前台 | `PreservePeerOrder` |
| 桌面壳 / 无前台 | `DesktopBottom` |
| DesktopPinned 模式 | 桌面 Owner 内的兄弟排序 |

文档明确把"把回落等同于 `HWND_BOTTOM`"列为坑 #2——那会破坏页面之间的相对层级。

**核心技巧 3 —— 整组批量 Z 排序：**
多 widget 的全局位置**只由管理器按组确定**，单窗口只清自身状态。用 `BeginDeferWindowPos`/`DeferWindowPos` 以同一个前台根窗口为整组边界一次排列，失败再逐窗 `SetWindowPos` 兜底。配套的 `NormalizeIdleWidgetZOrder()` 只能整理组内顺序，**不得**调用 `MoveToDesktopBottom`/`SetWindowToBottom`。这消灭了"N 个窗口各自自救互相打架"的整类问题。

**防抖与看门狗：** 唤起后 160ms 抑制窗；200ms 恢复监视器 + 50ms 鼠标边沿采样器（`GetAsyncKeyState` **高位 0x8000**）；代际计数器使过期异步回调失效；`BeginInteraction/EndInteraction` 配对泄漏的 10s 看门狗。

**文档记录在案的 8 个坑**（`widget_zorder_lifecycle.md` §6，摘录最反直觉的几条）：
- `GetAsyncKeyState & 0x0001` **低位**检测不到跨进程点击，必须用高位；
- `SetForegroundWindow` 会**静默失败**（前台锁 / UIPI），必须查返回值；
- `BeginInteraction/EndInteraction` 配对泄漏会**永久堵死回落**；
- 两条宿主 × 两种层级模式 = 四象限都要过测试；
- `IsDeskBoxWindow` 按 PID 判定过宽。

**桌面固定（DesktopPinned，实验）：** 格子 attach 到 **WorkerW** 桌面容器；此后置顶/回落改为"桌面图标层内的兄弟排序"。`docs/releases/v1.4.6.md` 记录了关键时机：**启动必须等待 Explorer 的桌面图标宿主就绪后再 attach**。
> ⚠️ 文档**未描述** `WS_EX_TOOLWINDOW`、Alt-Tab 隐藏、`SetParent` 到 Progman/SHELLDLL_DefView 的具体写法。若要复现这些，需直接读源码（`src/DeskBox/Helpers/Win32Helper.cs`、`WidgetLayerService.cs`），不能从文档得出结论。

### 1.3 数据契约与持久化

**存储布局：** `%LocalAppData%/DeskBox/settings.json` + `data/widgets/{widgetId}/...`（Todo = `todo.json`，QuickCapture = 独立目录 + `images/` + `thumbnails/`）。**卸载不删用户数据。**

**序列化：** System.Text.Json **Source Generation**，27 个分域 `JsonSerializerContext`，反射型重载 **0**。关键判断是"**不为统一而统一**"——现有至少六种格式契约（Web 默认、camelCase 缩进、紧凑、字符串枚举、数字枚举…），**不能共用全局 options**。用契约测试**冻结调用清单**（65 处 / 29 文件）来防退化。

**分级容错：**
- 未知属性默认忽略，但未知**枚举名会抛** `JsonException` → 弹性 store 把损坏隔离在主文件之外 + 从备份恢复；
- `WidgetKindJsonConverter` 把未知值**降级为 `File`** 而不是整体失败；
- 版本化：QuickCapture v4、Todo v3、Glance v8、备份 schema 2 兼容 1。

**widget ↔ 条目契约 —— 两层模型（这是"叠放"的实现基础）：**
- **磁盘文件层**：`WidgetViewModel.Items`（事实来源）
- **叠放投影层**：`VisibleItems` / `_stackDisplayItems`

叠放**只持久化三类元数据**：`_stackMemberOverrides`、`_stackOrder`、名称/禁用/展开覆盖项。即"**显示单元是投影，原文件原地不动**"。

### 1.4 扩展机制（contribution seam）—— 一篇"先测量、再收敛"的范本

**当前主流程：**
`WidgetKind` → `WidgetRegistry` → `WidgetContentDescriptor` → `WidgetContentFactory` / `IWidgetContentProvider` → `IWidgetContent` → `ContentWidgetWindow` → `WidgetManager`

**新增一个组件的 14 步：** 确认 kind → 更新 descriptor → 保持 `WidgetRegistry` 不可创建 → 实现 `XxxWidgetContent` → `XxxWidgetViewModel` → `XxxWidgetStore` → `IWidgetContent` adapter → `XxxWidgetContentProvider` → 在 `WidgetContentFactory` 注册 → 本地化键 → 注册 `WidgetWindowProvider` → 补测试 → **才置为可创建** → 人工回归 F7/托盘/关闭/删除/重启恢复/主题。

**实测成本（2026-09-11）：** 25 个文件含 kind 分支，单文件最多 **34 处**（`WidgetManager.FeatureWidgets.cs`），设置页要动 **5 个 `SettingsViewModel` partial** + 12 个文化表。

**目标形态「一处声明 + 三处实现」：** contribution 描述符（唯一改动宿主公共表的地方）+ 内容实现/provider + 设置节 UserControl + 本地化键。

**最值得学的是它明确写出了"哪些分散是有意保留的"**：按 kind 列的策略表（紧凑隐私、预热、标题图标）**仍各加一行**——那是功能自己的策略；`WidgetRegistry` 的窗口能力表与描述符表**有意分离**；**不做第三方插件平台**。这种"先量化、再收敛、并诚实标注取舍"的做法，比空喊"插件化"有用得多。

### 1.5 交互模型：叠放 / 胶囊 / 分组

**文件栈（file stack）= 自动叠放。** 在文件格子内部把相似文件收成叠放组。与文件夹的本质区别：文件夹是磁盘真实目录，**叠放是 DeskBox 的一层投影**。按文件类型/日期/自定义扩展名分组，阈值 2/3/5；自定义规则**从上到下匹配、一个文件只进第一条命中规则**；一旦发生人工干预，自动叠放**按需转为手动**；成员不足 2 个时手动叠放解散。

**胶囊模式（capsule mode）—— 解决"全部展开太挤 / 全部关闭没入口"。**
> "胶囊留住的是入口"：文件仍在原位，待办保留下一条值得看的事，天气音乐给出关键摘要。

几个高价值细节：
- **三段式热区**：左侧图标区 = 识别 + 拖动，中间 = 查看/悬停展开，右侧 = 操作按钮 + 拖动手柄。"让移动、操作和查看各有自己的位置"。
- **锚点**：胶囊与展开格共享锚点，左侧向右展开、右侧向左展开，"尽量保持胶囊所在的角和边界稳定"。
- **内容模式**：关键信息 / 简要摘要 / 仅图标和标题；**隐私显示**（收起态隐藏待办、随记正文，用于共享屏幕，且明确不等于加密）。
- 悬停**响应**（多久展开）与**动画**（持续多久）是两组独立设置。
- 拖文件到胶囊 → 目标格临时展开确认落点 → 完成后**回到原收起状态**。

**组件分组（widget groups）—— 一次漂亮的自我否定。**
`requirements/widget-group-navigation-ux.md` 有一个"替代声明"：R0–R10 是唯一有效规范，下方 1000+ 行是被否决的历史方案（外部附着式导航条 + 三种样式 + Smart Stack 跟手手势）。最终定案：

1. **砍掉外部导航条**，只在标题栏内放一个常驻成员选择器。原文论证："把标签放到内容区会挤占文件列表和待办正文，单独再放一条导航栏又会让一个小格子多一层视觉负担。"
2. 点击是主路径，局部滚轮是可选增强，**键盘是完整等价路径**。
3. 防误触：热区 **150ms** 后启用；提交后冷却 **180–220ms**；冷却期输入 **latest-wins，不排队播放中间成员**。
4. **硬约束**：只有 Standard/Compact 标题栏可分组，Overlay/Hidden/System 禁用且不自动降级，**必须显示禁用原因**。
5. **零新增第三方依赖**（明令不得引入 Lottie、通用动画引擎、第三方标题栏组件）。
6. **微动效红线**："不动画窗口位置、尺寸、圆角、标题栏高度、背景壳、公共命令和拖动区"，"不使用整窗大幅滑动、旋转、缩放、3D 翻页、果冻弹簧或图标形变"。只有成员图标和文字在固定槽交叉淡化 100–140ms。
7. **9 步原子切换事务**：Begin → Capture → Prepare → **Present candidate（Loaded + 非零布局 + 挂载后两个合成帧）** → Validate → Persist → Commit → Retire → Rollback。首帧超时 900ms，目标准备超 150ms 才显示轻量进度。**核心目标：切换期间无空白帧。**

**关键概念区分**（文档专门辟 FAQ）：**格子组** = 让多个格子在同一表面中切换（共享位置尺寸，最多 8 成员）；**胶囊组合栏** = 只负责让多个独立胶囊靠在一起排列。混淆会导致产品概念崩塌。

### 1.6 拖放契约（全项目最重要的一份契约）

**铁律：两类操作绝不能混。**

| 类型 | 范围 | 是否动磁盘 |
|------|------|-----------|
| **内部编排** | 同格排序 / 加入叠放 / 移出叠放 | **否**，只改投影与持久化元数据 |
| **文件系统传输** | 跨格 / Explorer ↔ 格子 | **是**，真实复制/移动/建快捷方式 |

**19 条规则中最关键的 5 条：**

1. `RequestedOperation` **只能**是单个 `Move`，能力集合放 `AllowedOperations`——否则 Win10 每次拖出都弹"复制/移动/快捷方式"选择菜单。
2. **`DragOver` 可以临时接受 `Move`（让 WinUI 把 Drop 路由给目标），但内部 `Drop` 完成永不返回 `Move`**——否则原生 Shell 数据对象会把 `.lnk` 当成真实移动完成并清理进回收站。
3. 跨格由目标执行真实移动后通知源，源**必须返回 `None`**（返回 `Move` 会造成二次清理）。
4. 每次拖拽生成 `DragSessionId`，每次 `GetDragPayload` 都校验（防跨会话复用缓存）。
5. 空状态以 `Items.Count == 0` 为准，**不用 `VisibleItems`**。

**"拒绝即消费"原则：** 拖到不支持的目标上，OLE 层返回 `DROPEFFECT_NONE`，**绝不回退为导入**——因为回退=Move=会移动用户文件。宁可提示"X 无法打开此文件"。文档记录了推翻原决定的理由："文件不支持该打开就走了移动，不该移动，应提示"。

**拖到快捷方式上打开（v1.5.1）：** 最终选了**方案 B——把 drop 委托给 shell 自己的 `IDropTarget`**（`SHCreateItemFromParsingName` → `BindToHandler(BHID_SFUIObject, IID_IDropTarget)` → `DragEnter`/`Drop`），理由是"这就是 Explorer 拖到快捷方式上时跑的同一代码路径，**语义零漂移**"。方案 A（自己解析 `.lnk` 再拼命令行）被否决，因为"丢失提权标记/链接跟踪/AUMID，且引入手写命令行构造——**转义是本类功能最难查的缺陷源**"。

落地入口实际有**三个**：OLE `IDropTarget`、WinUI 路由 Drop、`WM_DROPFILES`，共用一把 1s 闩锁。

### 1.7 原生能力隔离与性能红线

**为什么进程外宿主右键菜单：** 进程内宿主 `IContextMenu` 会把第三方处理器 DLL 加载进主进程，"崩了整个 App 陪葬"（第三方 shell 扩展如 Locale Emulator / Dropbox / Box 是常见崩源）。改为 Rust 进程 `DeskBox.ThumbnailProxy.exe --context-menu-server`，stdin/stdout UTF-8 行协议。

实测结论值得抄：
- 常驻 server + 预热：菜单弹出 **2343ms → 299ms**；
- 点击无反应：`InvokeCommand` 后立即 exit → 加 2s 消息泵宽限；
- 子菜单为空：去掉 `TPM_NONOTIFY`（它抑制 `WM_INITMENUPOPUP`）；
- 关不掉：`cancel` + `WH_MOUSE_LL` 钩子，**钩子必须装在专职泵线程**——实测阻塞时整条桌面输入管线会卡 1–2s。

**内存预算（可测量的门禁）：**

| 指标 | 阈值 |
|------|------|
| 稳态私有内存 | ≈ **115MB**（Release / Native AOT 基线） |
| 20 循环 Private 净增 | **≤ 15MB** |
| 循环后空闲 60s 工作集回落 | **≥ 交互期增量的 50%** |
| 8 小时长稳 | 无持续单调爬坡 |
| GC 门槛 | 堆 ≥96MB 且分配增量 ≥32MB |
| **明确不要开** | Server GC（显著抬高基线） |

**性能审计（`performance-audit-20260907.md`）的高优先级问题：** 4–5 处错误的 `EnableDependentAnimation=true`（纯 Opacity，一行级改动收益最大）；分组标题切换动画动 `FrameworkElement.Width` 导致每帧完整 measure/arrange；目录枚举每条目 3–8 次冗余 stat；`DisplayAreaWatcherService` 2 秒常驻轮询而全库无 `WM_DISPLAYCHANGE` 处理；UI 线程 sync-over-async。

**最有价值的是"为了正确性而拒绝优化"的红线：**
- junction 解析**不做缓存**——"防 swap-during-drag 读到陈旧物理路径，正确性取舍"；
- 缩略图**进程外隔离**是硬约束，"不能放弃"；
- Win11 毛玻璃**不建议无条件降级**（与 frosted-glass-first 的既有决策冲突），只在帧预算真超支时临时降级；
- 被明确否决的 4 项内存优化：compositor 动画对象池（串场风险）、语言切换增量化（重显短暂旧语言）、全类型 `OnWindowLongHidden`（重显加载占位符）、隐藏窗口虚拟化（重显 1–2 秒"不可接受"）。

### 1.8 开发过程的教训（1.4.8 复盘）

**做对的：** 第一版只做一件事（把桌面文件收进格子）；克制减法，拒绝股票/新闻/视频/浏览器（"一个桌面整理工具，不能靠不断堆功能来证明价值"）；开源免费（"任何人都可以检查 DeskBox 对电脑做了什么"）；面对"低内存 vs 快响应"的矛盾不替用户做决定，而是给均衡/节省资源/自定义三模式。

**踩的坑（按严重性）：**

1. **信任崩塌事故（最严重）**——有用户卸载后以为桌面文件被删了（文件其实被移进了收纳目录）。作者的反省极尖锐：
   > "'文件还在'这句话，在这种时候没有太大意义。软件既然移动了用户的文件，就应该让人清楚地知道它们去了哪里，卸载之后又该怎么找到。"

2. **1.4.5 撤回事故**——`.lnk` 快捷方式在格子间拖动被误删进回收站。根因："拖放结束时，DeskBox 过早地告诉系统这是一次移动，系统便按照移动的规则清理源位置，把还没有完成搬运的快捷方式送进了回收站。" 修复后作者凌晨四点把**所有涉及移动/复制/删除的流程重新走了一遍**。

3. **性能是反馈最多的问题**——"桌面工具需要常驻。任务管理器里的数字一大，用户心里难免会打鼓"。

4. **为性能冒进的风险**——"尽可能补测试、反复验证，最后认为风险已经可以接受"——但"评估永远只能覆盖已经想到的场景"。

5. **AI 编程的责任边界**（对新项目尤其重要）：
   > "代码能运行，只是一天工作的开始。……这些判断不能交给 AI，测试、回归和发布以后的责任，也只能由我承担。"

6. **环境覆盖的不可能**——"独立开发最困难的地方，有时不是把功能做出来，而是你永远不知道，还有哪台电脑正在等着给你上一课。"

**演进顺序规律**（从 releases 扫读得出）：文件格子（最早，单一功能）→ 待办/随记/剪贴板 → 天气/音乐 → 搜索 → 叠放 → 胶囊 → **整理 + 格子组（同一批引入）** → 性能 / Native AOT 大改 → 拖放语义精修与增量渲染。
即：**先做"接住"，再做"整理"，再做"收纳与切换"，最后被迫回到"性能与正确性"。**

---

## 二、StarMarkDesktop 现状盘点

### 2.1 已具备的能力（第八轮迭代后）

| 能力 | 实现位置 | 评价 |
|------|---------|------|
| 每组件独立无边框工具窗 | `WidgetWindow : Window` | ✅ 与 DeskBox 模型一致（DeskBox 也是从单面板演进到独立窗口） |
| 不进 Alt-Tab / 任务栏 | `RemoveDefaultWindowFrame`：`SetBorderAndTitleBar(false)` + `WS_EX_TOOLWINDOW` + `AppWindow.IsShownInSwitchers=false` | ✅ **已做到 DeskBox 文档未描述的部分** |
| 标题栏拖动 + 右下角缩放 | `DragBar_*` / `ResizeThumb_*`（全用 Win32 物理像素） | ✅ 正确的选择，避免 DIP 与物理坐标混用 |
| 窗口吸附 | `WidgetSnapping.SnapMove`（移植自 DeskBox `WidgetSnapCalculator`） | ✅ 纯函数 + 9 个单测，质量高于多数同类实现 |
| 位置/尺寸/置顶持久化 | `widgets.json` v2：`Enabled` + `WindowConfigs` | ✅ 有版本化与 v1→v2 迁移，弹性容错（损坏回退默认）+ 临时文件原子替换 |
| 托盘入口 + 全局热键 | `TrayHost`（`Ctrl+Alt+Space`）+ `ToggleAllAsync` | ✅ 基础闭环完整 |
| 与统一条目库打通 | QuickLaunch 读 `_repo.GetPinnedAsync(8)` | ✅ **这是 StarMark 独有的优势，DeskBox 没有** |
| 拖入建快捷入口 | `SetupQuickLaunchDrop`（WebLink / ApplicationLink / StorageItems / Text 四类） | ⚠️ 见 §三 P1-2 |

### 2.2 与 DeskBox 的成熟度对照

| 维度 | DeskBox | StarMark | 差距 |
|------|---------|----------|------|
| 层级模型 | 逻辑态/物理落点分离 + 瞬态浮起 + 整组 Z 排序 | 二元 `Topmost` | **大** |
| 扩展缝 | 描述符驱动 + 契约测试（"一处声明三处实现"） | 枚举 + switch | **中**（现在收敛最便宜） |
| 数据隔离 | 每组件独立 store + 弹性隔离 + 未知枚举降级 | 单一 `widgets.json` 全量反序列化 | **中**（有数据丢失风险） |
| 持久化序列化 | 27 个分域 Source Generation context，反射 0 | `JsonSerializer` 默认反射 | 小（AOT 前不痛） |
| 拖放契约 | 19 条规则 + 会话 ID + 回执语义纪律 | WinUI 路由 Drop 单层 | **中**（接入文件格时是硬门槛） |
| 多显示器 / DPI | v1.4.6 多显示器布局记忆 | 存物理像素，无越界回收 | **中** |
| 性能预算 | 115MB / 20 循环 ≤15MB / 60s 回落 ≥50% | 无 | **中**（常驻类应用必补） |
| 备份恢复 | ZIP + SHA-256 清单 + 恢复前快照 | 无（组件数据不进备份） | **中** |
| 胶囊 / 分组 | 成熟 | 无 | 大（属规划内增量） |
| 进程外隔离 | Rust sidecar | 无 | 小（当前无高风险 native 依赖） |

### 2.3 扩展缝现状：已到收敛窗口

当前 `WidgetKind` 的分支点在源码中已扩散到至少 7 处：

```
WidgetStorage.KindTitle()            // 显示名
WidgetWindow.KindGlyph()             // 图标
WidgetStorage.DefaultWidth()         // 默认宽
WidgetStorage.DefaultHeight()        // 默认高
WidgetStorage.IsResizable()          // 可否缩放
WidgetWindow.BuildContent()          // switch 构建内容        ← 最重的一处
WidgetWindow 构造函数                // if (kind == QuickLaunch) / if (kind == Clock)
WidgetWindow.UpdateClockTimer()      // if (_kind != Clock) return
WidgetWindow.SetupQuickLaunchDrop()  // if (_kind != QuickLaunch) return
```

对照 DeskBox 的实测数据（25 文件 / 单文件 34 处），StarMark 现在**只有 5 种组件、约 7–9 处分支**——治理成本极低；但第九轮若按现有节奏加"天气/音乐/最近活动"三种，分支点会立刻翻倍。

---

## 三、借鉴建议（按优先级）

### P0-1 · 用描述符收敛扩展缝 ⭐ 最高性价比

**现状：** 新增一个组件要改 7+ 处（§二.3），且 `WidgetWindow` 构造函数里已有 `if (kind == WidgetKind.QuickLaunch)` 这类特判——这正是 DeskBox 演化到"单文件 34 处分支"的起点。

**建议：** 引入 `WidgetDescriptor` + `IWidgetContentProvider`，把"按 kind 分支"压缩到**一处声明、一处实现**。

```csharp
// StarMark.Core/Widgets/WidgetDescriptor.cs
public sealed record WidgetDescriptor(
    WidgetKind Kind,
    string Title,
    string Glyph,
    int DefaultWidth,          // DIP
    int DefaultHeight,         // DIP
    bool IsResizable,
    bool AllowMultiple = false // 预留：是否允许多实例
);

public interface IWidgetContentProvider
{
    UIElement Build(WidgetContentContext ctx);   // ctx: storage / repo / manager / window
    void OnShown();      // 替代构造函数里的 if (kind == Clock) 等启动钩子
    void OnHidden();     // 替代 UpdateClockTimer 里的停表逻辑
}
```

```csharp
// 唯一声明处
public static class WidgetCatalog
{
    public static IReadOnlyDictionary<WidgetKind, WidgetDescriptor> All { get; } = new Dictionary<WidgetKind, WidgetDescriptor>
    {
        [WidgetKind.QuickLaunch] = new(WidgetKind.QuickLaunch, "★ 快捷启动", "★", 320, 460, true),
        [WidgetKind.Clock]       = new(WidgetKind.Clock,       "🕒 时钟",   "🕒", 220, 150, false),
        // 新增组件：只加这一行 + 一个 Provider 类
    };

    public static IWidgetContentProvider GetProvider(WidgetKind kind) => /* DI 解析 */;
}
```

**配套（学 DeskBox 最关键的一步）：** 加一个**契约测试**，钉住"枚举里存在但没人能创建的 kind"：

```csharp
[Fact]
public void EveryKind_HasDescriptorAndProvider()
{
    foreach (var kind in Enum.GetValues<WidgetKind>())
    {
        Assert.True(WidgetCatalog.All.ContainsKey(kind), $"{kind} 缺少描述符");
        Assert.NotNull(WidgetCatalog.GetProvider(kind));
    }
}
```

**为什么现在做：** 5 种组件时改这个约半天；15 种时改这个是 DeskBox 那种 25 文件级重构。**这是全文档中投入产出比最高的一条。**

---

### P0-2 · 把二元置顶换成层级策略

**现状：** `ApplyTopmost()` → `WindowInterop.SetTopmost(..., HWND_TOPMOST / HWND_NOTOPMOST)`，用户手动开/关，持久化到 `WidgetConfig.Topmost`。

**问题：** 一旦置顶就是**持久 TopMost**。DeskBox 明确把这点列为坑——"瞬态置顶不是持久 TopMost，安全网无效"，且会造出"永远压屏"这类最难查的 bug（用户以为卡死、截图/录屏全被挡）。StarMark 的时钟/搜索组件天然需要"平时贴桌面、操作时浮起"，二元模型表达不了。

**建议：** 引入三态层级策略 + 瞬态浮起：

```csharp
public enum WidgetLayerMode
{
    Normal,        // 普通窗口，不干预
    Raised,        // 瞬态浮起：TOPMOST → 立刻 NOTOPMOST（DeskBox 技巧）
    DesktopPinned, // attach 到 WorkerW（实验，可选）
}
```

```csharp
/// <summary>DeskBox Win32Helper.BringWindowTemporarilyToFront 同款：
/// 先置 TOPMOST 再立刻取消，使窗口停在普通层级带顶部但不具备 TopMost 属性。</summary>
public static void RaiseTransient(Window window)
{
    var hwnd = GetHwnd(window);
    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}
```

**落地要点：**
1. `WidgetConfig.Topmost`(bool) 升级为 `LayerMode`(enum)，做好 v2→v3 迁移（见 P0-3，务必先备份）；
2. F7 / `Ctrl+Alt+Space` 唤起走 `RaiseTransient`，**不写持久化**；
3. 多个组件时由 `WidgetManager` **统一**决定 Z 序（学 DeskBox 的"整组批量"），单窗口只清自身状态；
4. **绝不要**在回落时用 `HWND_BOTTOM`——DeskBox 坑 #2。

---

### P0-3 · 拆分 `widgets.json`，消除"一处损坏、全部归零"

**现状：** 所有组件数据（Todos / Notes / Links / Enabled / WindowConfigs）在**一个** `widgets.json` 里。`WidgetStorage.Load()` 的 catch 是：

```csharp
catch
{
    // 损坏文件 → 回退默认（与 DeskBox ResilientJsonStore 同策略）
}
return Normalize(null);
```

**风险（具体且已存在）：** 只要 Todo 列表里出现一个无法反序列化的字段，**整份文件反序列化抛异常 → 返回全新默认对象 → 用户的待办、随记、快捷入口、窗口位置全部静默清零**。DeskBox 的做法是**每组件独立 store + 弹性隔离 + 未知枚举降级**，损坏只影响一个格子。

**建议（三步，可由小到大）：**

1. **立刻（成本最低）：加迁移前备份。** 当前 v1→v2 迁移会直接覆盖原文件且无 `.bak`。在 `Save()` 首次写入前，若 `File.Exists(_path)` 则 `File.Copy(_path, _path + ".bak")`。
2. **短期：按域拆文件。** `widgets.window.json`（窗口配置，丢了可重建）+ `widgets.todo.json` + `widgets.note.json` + `widgets.link.json`。任一损坏只影响一个域。
3. **中期：未知值降级而非整体失败。** 学 `WidgetKindJsonConverter`：未知 kind 降级为默认值并记录日志，而不是让整个 `Load()` 抛异常。

**顺带修一处小问题：** `WidgetManager.IsEnabled(kind)` 每次调用都 `_storage.Load()`（**磁盘读 + 全量反序列化**）。而 `BuildMenu()` 的 `Opening` 事件里对 5 个 kind 各调一次 —— 一次右键菜单 = 5 次磁盘读。建议加内存缓存 + dirty 标志（`Save()` 时置脏）。

---

### P0-4 · 多显示器与 DPI：保存物理像素的两个陷阱

**现状：** `ApplyInitialBounds()` 在有存档时**直接把 `_config.X/Y/Width/Height` 当物理像素用**；默认尺寸才乘 `scale`。

**两个具体问题：**

1. **DPI 变化后尺寸错乱。** 在 150% 缩放下保存（宽 480 物理像素 = 320 DIP），下次在 100% 显示器上启动 → 按 480 物理像素显示，视觉上大了 1.5 倍。
2. **越界后位置错乱。** `GetWorkArea(this)` 用 `MonitorFromWindow`，但此时窗口**尚未定位**，很可能返回主显示器 → 原本摆在副屏的组件被"夹"回主屏。

**建议：**
- **存 DIP + 存参考 DPI**：`WidgetConfig` 增加 `ScaleAtSave`（或统一以 DIP 存储，读取时按当前 `GetScale()` 还原）。
- **启动校验**：开机恢复时先判断存档矩形是否与**当前虚拟屏**（`SM_XVIRTUALSCREEN` / `EnumDisplayMonitors`）有足够交集；无交集则回落到主显示器默认级联位置。这是"外接显示器拔掉后再开机"最常见的投诉来源。
- 中期可学 DeskBox v1.4.6 的**多显示器布局记忆**，但优先级低于上面两条。

---

### P0-5 · 全局热键的 UIPI 降级

**现状：** `Ctrl+Alt+Space` 走 `RegisterHotKey`。

**DeskBox 的实测结论：** 当前台是**提权进程**（以管理员运行的程序）时，UIPI 会拦截 `WM_HOTKEY`，**只有 `WH_KEYBOARD_LL` 低级钩子能收到**。DeskBox 因此用 `RegisterHotKey` + `SetWindowSubclass` 接 `WM_HOTKEY`，并保留 `WH_KEYBOARD_LL` 兜底。

**建议：** 在 `TrayHost` 加热键失败检测——注册后连续 N 次无响应、或检测到前台进程为 High-IL 时，降级到低级钩子并提示用户。同时学 DeskBox 支持**热键可重录 + Esc 取消**，避免与输入法/其他软件冲突时无从调整。

---

### P1-1 · 胶囊模式（StarMark 最容易吃到红利的一项）

**理由：** StarMark 现有 5 个组件里，**时钟、搜索、待办**天然适合胶囊化。DeskBox 提出的核心矛盾——"全部展开会挤占空间，全部关闭又会失去入口"——在 StarMark 同样成立，而且 StarMark 的 QuickLaunch 已经在做"置顶条目"摘要，离胶囊只差一层。

**建议的最小实现：**
- 三段式热区（左=拖动 / 中=展开 / 右=操作）——**这一条直接抄，成本极低收益极高**；
- 胶囊锚点与展开格共享（StarMark 已经以左上角为基准，`MoveAndResize` 时保持左上角不动即可）；
- 拖文件到胶囊 → 临时展开 → 落点确认 → **回到原收起状态**；
- 隐私模式：收起态隐藏随记正文（StarMark 的随记可能含敏感内容，且用户常在共享屏幕场景使用）。

**不要一上来就做** DeskBox 那套 9 步原子切换事务 + 双合成帧 readiness——那是为"分组切换无空白帧"设计的，StarMark 暂无分组，先做静态胶囊即可。

---

### P1-2 · 拖放契约：在引入文件格之前先立规矩

**现状：** `QuickLaunch_DragOver` 一律 `AcceptedOperation = Copy`，`Drop` 后把路径转成 `file://` URI 存为 Link。

**当前没问题**（快捷入口本就是引用语义，且只写自己的 JSON）。**但一旦引入文件格就会立刻踩雷**，因为那时拖放会同时存在两种语义：

| 场景 | 语义 | 是否动磁盘 |
|------|------|-----------|
| 从桌面拖文件进 StarMark 文件格 | 文件系统传输（复制/移动） | **是** |
| 在格内调整顺序 / 加入叠放 | 内部编排 | **否** |
| 从格子拖出到微信/浏览器 | 文件系统传输 | 否（仅导出） |

**建议：现在就把两条纪律写进代码注释和文档，等做文件格时直接生效：**

1. **`RequestedOperation` 只能是单个 `Move`**，能力集合放 `AllowedOperations`——否则 Win10 每次拖出都弹"复制/移动/快捷方式"三选菜单。
2. **内部 `Drop` 完成永不返回 `Move`**。可以在 `DragOver` 里临时接受 `Move` 以让 WinUI 路由，但**完成回执必须是 `None`/`Copy`**。这是 DeskBox 1.4.5 把用户 `.lnk` 送进回收站的直接根因。
3. **"拒绝即消费"**：拖到不支持的目标上，返回 `None` + 提示，**绝不回退为导入**（回退=移动用户文件）。
4. **每次拖拽生成 `DragSessionId`**，防止跨会话复用缓存载荷。

**另外**：StarMark 目前只挂了 WinUI 路由 Drop。DeskBox 实测落地入口有**三个**（OLE `IDropTarget` / WinUI 路由 Drop / `WM_DROPFILES`），共用一把 1s 闩锁。若将来发现"从某些程序拖进来没反应"，优先查是否是另外两个入口缺失。

---

### P1-3 · 建立性能与内存门禁

**理由：** 桌面组件是**常驻**应用。DeskBox 1.4.8 复盘原话："任务管理器里的数字一大，用户心里难免会打鼓。"

**建议（直接套用 DeskBox 的可测量门禁，按 StarMark 规模酌减）：**

| 指标 | 建议阈值 | 备注 |
|------|---------|------|
| 稳态私有内存 | ≤ 150MB | DeskBox AOT 基线 115MB；StarMark 未上 AOT，可放宽 |
| 20 次显示/隐藏循环 Private 净增 | ≤ 15MB | 直接抄 |
| 循环后空闲 60s 工作集回落 | ≥ 增量的 50% | 直接抄 |
| 8 小时长稳 | 无单调爬坡 | 直接抄 |
| **不要开** Server GC | — | 显著抬基线 |

**StarMark 当前已做对的：** 时钟计时器在 `AppWindow.IsVisible == false` 时 `Stop()`（`UpdateClockTimer`），这是正确的常驻行为，保持。

**两个值得立刻改的小点：**
- `ToggleTodo` / `DeleteTodo` / `DeleteNote` 都调 `BuildContent()` **全量重建** UI——会丢滚动位置、造成闪烁。改为只更新受影响的行（或至少保留 `ScrollViewer` 偏移）。DeskBox 的性能审计里，"全量视觉树刷新"被定为 P0 问题。
- 未来若加文件格，**目录枚举的冗余 stat** 是 DeskBox 审计里的高优先级问题（每条目 3–8 次），设计阶段就要按"一次枚举拿全字段"来做。

---

### P1-4 · 备份与恢复：组件数据不能是孤岛

**现状（已核实）：** `src/` 下**不存在任何备份/恢复/快照代码**（grep `备份|Backup|Snapshot` 无命中），即 StarMark 目前**完全没有备份能力**。而 `widgets.json` 里存着用户手写的**待办与随记**——真实用户数据，丢失不可重建。

这意味着风险 R1（单文件损坏全清）与 R9（重装即失）目前**没有任何兜底**。这条的优先级实际上应上调到 P0。

**建议（学 DeskBox，但做最小版）：**
1. **恢复前自动快照**（DeskBox：替换前自动创建恢复前快照）——成本极低，收益极高；
2. **导出时包含**：窗口配置、待办、随记、快捷入口；**不包含**链接指向的实际文件（DeskBox 明确："映射了 `D:\Projects` 后不会把整个项目目录打进 ZIP"）；
3. **导入前校验**：结构 → 必要文件 → 可解析性，任一不过**不替换现有数据**；
4. **v2→v3 迁移前 `.bak`**（见 P0-3 第 1 条）。

---

### P2-1 · 组件分组（暂缓，但先把概念边界定清楚）

DeskBox 的经验是**分组必须复用同一个 HWND**（"稳定状态每组一个 HWND、一个实时成员；过渡最多 outgoing + incoming 两个"），而 StarMark 现在是**每 kind 一个独立 HWND**。直接照搬分组会与现有窗口模型冲突。

**建议：**
- **暂不做分组**，先把概念写进文档：StarMark 若要"组合"，可选路径是 (a) 学 DeskBox 改为单窗口多成员切换，或 (b) 只做"胶囊组合栏"（多个独立组件靠边对齐，不改窗口模型）——后者与 StarMark 现有架构兼容得多。
- 真要做分组时，**务必先读** `requirements/widget-group-navigation-ux.md` 的"替代声明"部分。那 1000+ 行被否决方案（外部导航条、Smart Stack 跟手手势）能省下大量试错。

---

### P2-2 · 进程外隔离（当前不必做，但要知道触发条件）

**触发条件：** 一旦 StarMark 接入**第三方 shell 扩展**（缩略图处理器、`IContextMenu`），就必须进程外隔离。DeskBox 的理由很硬：第三方 DLL 加载进主进程，"崩了整个 App 陪葬"，且这是已知高频崩溃源。

**当前状态：** StarMark 用的是 WinUI 原生控件 + 自研图标，**暂无需隔离**。

**但可以立刻抄的一条：** 《架构健壮性分析与演进》§六 已列"卡片右键直达 Windows 原生菜单（`ShowContextMenuAsync`）"——**做这个的时候，就是引入进程外隔离的时刻**，不要等崩了再改。

---

## 四、不该照抄的部分 · StarMark 的差异化机会

### 4.1 核心判断：StarMark 的组件应该是"条目格"，不是"文件格"

DeskBox 的全部设计围绕**文件收纳**展开（文件格子 → 叠放 → 整理 → 桌面组织）。这是它的起点，也是它的边界——它**没有**统一条目数据库，书签/GitHub Star/剪贴板这些异构数据源它处理不了。

StarMark 恰恰相反：它的地基是 `items` 统一条目表（文件/书签/GitHub Star/剪贴板四类同源）+ FTS5 两阶段查询 + Everything 集成（《项目开发技术文档》§二、§四）。**这是 DeskBox 没有的能力，也是 StarMark 唯一可能形成代差的地方。**

**具体建议：把"快捷启动格"升级为"条目格"，这是 DeskBox 做不到的形态：**

| 能力 | 说明 | 依赖（StarMark 已有） |
|------|------|---------------------|
| **标签格** | 把某个标签的条目直接摆到桌面，随库更新 | `item_tags` + `TagsPageViewModel` |
| **搜索结果格** | 钉住一条查询（如 `stars>500 AND topic:rag`），结果常驻桌面 | `SearchService` + FTS5 两阶段查询 |
| **最近活动格** | 按 `updated_at` 展示最近打开的条目 | `ActivityPageViewModel` |
| **置顶条目格** | 现状已有（`GetPinnedAsync(8)`），可扩展为分组置顶区 | `items.pinned`（第五轮已加） |

**一句话：** 抄 DeskBox 的**窗口与交互工程能力**，但组件内容走 StarMark 自己的**统一条目**路线。DeskBox 的"文件格 / 叠放 / 自动整理"是它的历史包袱，不是 StarMark 的目标形态。

### 4.2 `MoveFileAction` 是全项目风险最高的单点

《项目开发技术文档》§七 Phase 3 规划了 `MoveFileAction 移动文件`。**建议重新评估。**

DeskBox 的两起事故都是文件移动引起的（1.4.5 `.lnk` 误删进回收站、卸载后用户以为文件被删）。它的应对是三条可验收的机制：

1. **移动前展示"计划"而非"数量"**——扫描后列出：准备处理的文件、分类、**接收格子、最终真实路径**，用户可跳过/改目标，点确认才执行。原文："这种设计会多出一次确认，却把最重要的信息放到了移动前。你知道文件要去哪里，才更愿意让工具替你做重复的动作。"
2. **自动化"先建基线、只管新增、等待稳定"**——默认关闭；开启时把当前状态记为基线**不追溯已有文件**；只处理之后出现且通过稳定性检查（FS 事件防抖 3–5s，连续 2–3 次确认大小与 mtime 稳定）的新文件；"无法确认状态时也保持在桌面"。
3. **可枚举的安全边界**——文件夹/隐藏/系统/重解析点/云占位符/下载中/权限不足/单文件 >100MB 不处理；一次最多 200 个文件 / 100MB。

**对 StarMark 的具体建议：**
- **MVP 阶段 `MoveFileAction` 只做"打标签 / 写笔记 / 标记疑似重复"，不做真实移动。** StarMark 的价值主张是"统一检索 + 组织"，不是"整理桌面"——移动文件对它并非必要。
- 若确要做，**先把上面三条做成验收标准，再写第一行代码**。
- **规则绑定稳定 ID 而非显示名称**（DeskBox：`OrganizationRule` 绑 `WidgetId` 而非 `DisplayName`）——StarMark 现有 `rules` 表设计时需确认这一点；目标失效时规则应**暂停并提示**，不得静默改投。

### 4.3 不要照抄的三件事

| DeskBox 做法 | StarMark 不该跟 | 原因 |
|-------------|----------------|------|
| Native AOT + Rust sidecar + 进程外 COM | ❌ 暂缓 | DeskBox 为此投入了 70+ 篇 stage 报告；StarMark 当前无第三方 native 依赖，引入是纯负债 |
| 27 个分域 `JsonSerializerContext` | ❌ 现在不做 | 那是 AOT 的配套要求；StarMark 未上 AOT，先做 P0-3 的数据隔离更划算 |
| 文件整理 / 桌面组织 | ❌ 非差异化 | 见 §4.1、§4.2 |

---

## 五、建议落地顺序

```
第九轮（建议范围）
├─ P0-1 描述符收敛 + 契约测试          ← 半天，最高性价比，越晚越贵
├─ P0-3 widgets.json 迁移前 .bak + IsEnabled 缓存 + 数据按域拆分
├─ P0-4 DIP 存储 + 启动越界回收
└─ P1-4（提前）最小备份：恢复前快照 + widgets.json 纳入导出（当前零兜底，风险裸露）

第十轮
├─ P0-2 层级策略（瞬态浮起 + 管理器统一 Z 序）+ v2→v3 迁移
├─ P0-5 热键 UIPI 降级 + 可重录
└─ P1-3 性能门禁（先定测量方法，再优化）

第十一轮
├─ P1-1 胶囊模式（三段式热区优先）
├─ P1-2 拖放契约纪律（注释 + 文档，为文件格铺路）
└─ P1-4 完整备份：导出/导入校验、不打包链接指向的实际文件

之后 / 按需
├─ 差异化：标签格 / 搜索结果格 / 最近活动格（§4.1）
└─ P2-1 分组 或 胶囊组合栏
```

**判断标准：** 凡属"**会随组件数量线性变贵**"的事（扩展缝、数据隔离），越早做越便宜；凡属"**只在特定场景才需要**"的事（进程外隔离、AOT），等触发条件出现再做。

---

## 六、风险清单

| # | 风险 | 影响 | 现状 | 建议 |
|---|------|------|------|------|
| R1 | `widgets.json` 单文件损坏导致待办/随记/入口/位置全清 | **数据丢失** | 已存在 | P0-3 |
| R2 | v2→v3 迁移直接覆盖且无备份 | **数据丢失** | 已存在 | P0-3 第 1 条 |
| R3 | 保存物理像素 + DPI 变化 → 组件尺寸/位置错乱 | 体验 | 已存在 | P0-4 |
| R4 | 拔掉外接显示器后组件落到屏外 | 找不到组件 | 已存在 | P0-4 |
| R5 | 持久 TopMost 造成"永远压屏" | 体验 / 投诉 | 已存在 | P0-2 |
| R6 | `MoveFileAction` 移动用户文件（Phase 3） | **信任崩塌** | 规划中 | §4.2 |
| R7 | 扩展缝未收敛，组件增多后重构成本陡增 | 工期 | 已存在 | P0-1 |
| R8 | 引入第三方 shell 扩展后主进程被带崩 | 崩溃 | 未发生 | P2-2 |
| R9 | 组件数据不进备份，重装即失 | 数据丢失 | 已存在 | P1-4 |
| R10 | 无性能门禁，常驻后内存爬坡 | 口碑 | 已存在 | P1-3 |

---

## 附录：DeskBox 文档索引（供后续深入）

**优先读：**
- `docs/articles/00-overview.md` — 产品世界观，"真实路径优先"的出处
- `docs/architecture/[重要勿删]widget_zorder_lifecycle.md` — 层级机制 + 8 个坑
- `docs/architecture/[重要勿删]file_drag_stack_contract.md` — 19 条拖放规则 + 两层模型
- `docs/architecture/widget_contribution_seam.md` — 扩展缝的成本量化
- `docs/articles/deskbox-1.4.8-release-reflection.md` — 开发过程复盘，含两起事故

**按需读：**
- `docs/requirements/widget-group-navigation-ux.md` — 分组导航（注意顶部的"替代声明"）
- `docs/requirements/desktop-auto-organization.md` — 自动整理的安全边界
- `docs/requirements/adaptive-widget-animation.md` — 帧钟与降频策略
- `docs/architecture/native_context_menu_hosting.md` — 进程外右键菜单（引入原生菜单时必读）
- `docs/memory-optimization-plan.md` — 内存预算与验收门禁
- `docs/articles/performance-audit-20260907.md` — 性能问题清单

**不必读：** `docs/architecture/stage-reports/`（70+ 篇 AOT 迁移日志）、`docs/releases/`（可按需查时间线）。

---

**文档结束。**

> 本文所有关于 DeskBox 的结论均标注了出处文件；DeskBox 文档未明确之处均已在正文标注"文档未明确"。
> 涉及 `WS_EX_TOOLWINDOW` / Alt-Tab 隐藏 / `SetParent` 到桌面容器等实现细节，DeskBox 文档**未描述**，如需复现请直接参考其源码 `Win32Helper.cs` 与 `WidgetLayerService.cs`。
