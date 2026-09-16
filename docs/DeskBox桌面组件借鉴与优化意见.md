# DeskBox 桌面组件 · 借鉴与优化意见

**文档版本：** v3
**调研对象：** `DeskBox-ref/`（参考实现，v1.5.2 时代文档）
**面向对象：** StarMarkDesktop 第八轮迭代后的桌面组件体系（Widgets 2.0）
**配套文档：** [项目功能可行性分析.md](./项目功能可行性分析.md) · [项目开发技术文档.md](./项目开发技术文档.md) · [架构健壮性分析与演进.md](./架构健壮性分析与演进.md) · [开发进度报告.md](./开发进度报告.md)

---

## v2 修订说明

v1 只对照了 DeskBox 文档与组件源码本身。v2 补读了《项目功能可行性分析》与《开发进度报告》，据此做了四处实质修订：

| # | 修订 | 原因 |
|---|------|------|
| 1 | **新增第一章「项目目标校准」** | 发现桌面组件在原规划里属 Phase 4，而项目在 Phase 1/2 阶段已做了两轮——需要重新论证它该排第几 |
| 2 | **新增第三章「三个关键缺口」与实测基线** | Everything 未入库 / 规则引擎未实现 / 组件数据游离于统一条目模型之外，直接决定组件有没有内容可摆 |
| 3 | **新增第四、五章「重构意见」与「实现方案」** | 应要求补充；WidgetWindow 全 code-behind 建 UI 是与项目其余部分最大的架构不一致 |
| 4 | **补入实测结果（含 1 个失败测试）** | 第八轮「未执行 build/test」；本次实测发现 `WidgetSnappingTests` 存在真实缺陷，已定位根因并给出修复 |

同时修正 v1 两处判断偏差：`MoveFileAction` 风险等级从「当前最高」下调为「远期设计约束」（规则引擎实际尚未实现）；v1 未提及的 `PreviewHost`、单实例、主题冻结等既有成果已补入。

## v3 修订说明（P0 项已按 DeskBox 源码落地）

v3 对应一次**直接拉取 DeskBox 源码复用**的实作，而非再次评估。四项已完成：

| # | 落地内容 | 复用的 DeskBox 源文件 | 结果 |
|---|---------|----------------------|------|
| 1 | 吸附算法重写为完整移植 | `Services/WidgetSnapCalculator.cs` + `tests/WidgetSnapCalculatorTests.cs` | 修复跨轴耦合缺陷；新增 sticky 迟滞、缩放边吸附、垂直中心距离打散；测试 82/82 全绿 |
| 2 | 扩展缝收敛为描述符 + 注册表 | `Services/WidgetContentDescriptor.cs`、`WidgetContentFactory.cs`、`WidgetRegistry.cs` | 6 处 `WidgetKind` switch 收敛为 1 份清单；新增组件只改一行 |
| 3 | 层级策略：挂载桌面图标层 | `Services/WidgetLayerService.cs`、`IdleWidgetZOrderPolicy.cs` | 组件真正落在桌面层；瞬态浮起替代持久置顶；空闲期整组排 Z 序 |
| 4 | 窗口宿主与内容创建解耦 | `Services/WidgetContentFactory.cs` 的 provider 分层 | `WidgetWindow` 不再持有组件类型分支 |

**重要认知修正：** v2 认为「当前持久 TopMost 会造出永远压屏的 bug」——实测 `WidgetConfig.Topmost` 默认为 `false`，是用户显式开关，该风险不成立。真正的缺口是**完全没有挂载桌面层**，组件只是普通窗口（点击桌面 / Win+D 会被挤走），这才是本次补齐的核心。

---

## 目录

- [零、结论摘要](#零结论摘要)
- [一、项目目标校准：桌面组件该排第几](#一项目目标校准桌面组件该排第几)
- [二、DeskBox 桌面组件的核心实现方式](#二deskbox-桌面组件的核心实现方式)
- [三、现状盘点与实测基线](#三现状盘点与实测基线)
- [四、重构意见](#四重构意见)
- [五、功能实现方案建议](#五功能实现方案建议)
- [六、借鉴建议（按校准后优先级）](#六借鉴建议按校准后优先级)
- [七、差异化：StarMark 不该照抄的部分](#七差异化starmark-不该照抄的部分)
- [八、建议落地顺序](#八建议落地顺序)
- [九、风险清单](#九风险清单)

---

## 零、结论摘要

**一句话结论：** 桌面组件做得早了，但方向没错——前提是让它服务于 StarMark 的核心目标（统一条目），而不是长成一个独立的桌面小工具集。当前最该做的不是继续打磨组件外壳，而是**先把本地文件灌进 `items` 表**，否则桌面上的格子摆的是整个库里最小的一撮数据。

四条关键意见：

1. **先补数据，再磨外壳。** Everything 目前只做实时搜索、不写 DB、不进文件夹树/标签/动态（《开发进度报告》§六 Phase 2 第 6 项）。统一条目模型缺了本地文件这块最大的拼图，桌面组件的内容价值因此被严重削弱。**这是投入产出比最高的一项，且是组件价值的前提。**

2. **待办/随记应纳入统一条目模型。** 当前它们存在 `widgets.json`，游离于 `items` 表之外——不参与统一搜索、不能打标签、不能享用规则引擎。StarMark 的核心亮点是"统一条目模型"，而组件恰恰在最该统一的地方开了口子。**把待办/随记变成 `ItemType.Todo/Note`，是 DeskBox 结构上做不到、StarMark 天生能做的事。**

3. **WidgetWindow 是全项目最大的架构不一致。** 1030 行 code-behind 手工 `new Button()` / `new StackPanel()`，而项目其余部分早已是 XAML + ViewModel + `x:Bind`。后果：无法用 XAML Hot Reload、无法复用 `ItemCard`、每次数据变更全量重建 UI。

4. **先修那个失败测试，再谈重构。** 第八轮未执行 build/test。本次实测：构建 **0 错误 0 警告**，但 **60 个测试中 1 个失败**（`WidgetSnappingTests.LeftEdge_AbutsToTargetRight_WithSpacing`）。根因是吸附算法的跨轴耦合，是真实产品 bug（拖动时窗口纵向意外跳动），不是测试写错。修复见 §3.3。

---

## 一、项目目标校准：桌面组件该排第几

### 1.1 原始定位：Phase 4 的一项「功能借鉴」

《项目功能可行性分析》§3.2「第二级：中期规划」里，DeskBox 只占一行：

| 项目 | 集成方式 | 核心价值 |
|------|---------|---------|
| **DeskBox 格子化界面** | 功能借鉴（"只引用不移动文件"设计） | 高度可定制的格子布局 |

对应 §五 路线图：**Phase 4（6-8 周，可行性「中」）**。

而产品真正的四大核心亮点是（§1.2）：

1. **统一条目模型** —— 本地文件 / 书签 / GitHub Star 同表，标签、笔记、搜索、规则全部跨源统一
2. **系统级深度集成** —— Shell 扩展、全局快捷键、托盘常驻
3. **Everything 极致搜索复用** —— 毫秒级、零索引维护
4. **跨源规则引擎** —— 文件条件与 URL 条件同一套语法

**结论：桌面组件在原规划里是 Phase 4 的"借鉴项"，不是核心差异化。**

### 1.2 实际情况：Phase 1/2 阶段提前做了两轮

| 阶段 | 规划内容 | 实际状态 |
|------|---------|---------|
| Phase 1 MVP | 数据模型 + Everything + 统一搜索 | ✅ 完成（含托盘/热键/单实例） |
| Phase 2 | GitHub Stars + 书签 + Ditto | ✅ 完成（+ QuickLook 预览 `PreviewHost`） |
| **Phase 3** | **规则引擎 + 自动整理 + 插件架构** | ❌ **未实现**（`rules`/`rule_runs` 表已建，`IRuleAction` 仅占位接口，无实现） |
| **Phase 4** | **DeskBox 格子化界面** | ✅ **已做两轮**（第七、八轮） |
| Phase 5 | Shell 扩展 + MCP | ❌ 未实现（`StarMark.ShellExtension` 仍为空目录） |

**即：项目跳过了 Phase 3（规则引擎），提前做了 Phase 4（桌面组件）。**

### 1.3 判断：方向没错，但要让它服务核心目标

提前做组件有合理理由（同栈 WinUI 3、可视化成果、用户可见度高），不做价值判断。但需要明确一点：

> **桌面组件只有两种命运：要么成为"统一条目"的桌面出口，要么变成一个与 StarMark 主业无关的独立小工具集。**

判断标准很简单：**桌面上的格子，能不能摆出 StarMark 库里的东西？**

现状是「半能」：

- ✅ 快捷启动格能展示 `GetPinnedAsync(8)` —— 但置顶条目只是全库很小一撮
- ❌ 待办/随记在 `widgets.json`，不在 `items` 表，与主库不通
- ❌ 本地文件不入库（Everything 只实时搜索），桌面格子摆不出本地文件
- ❌ 没有"标签格""搜索结果格"这类真正复用统一检索能力的组件

**所以后续所有建议，都服务于一个目标：把组件接回统一条目模型。**

---

## 二、DeskBox 桌面组件的核心实现方式

> 本节结论来自 `DeskBox-ref/docs/`，主要出处：`articles/00-overview.md`、`architecture/current_architecture.md`、
> `architecture/[重要勿删]widget_zorder_lifecycle.md`、`architecture/widget_contribution_seam.md`、
> `architecture/[重要勿删]file_drag_stack_contract.md`、`architecture/native_context_menu_hosting.md`、
> `architecture/startup-policy.md`、`docs/memory-optimization-plan.md`、`articles/deskbox-1.4.8-release-reflection.md`。
> 凡文档未明确之处均标注"文档未明确"。

### 2.1 产品世界观：不替换 Windows，真实路径优先

DeskBox 对"桌面组件"的定义：**摆在桌面上的可操作小窗口，背后是真实能力而非视觉贴纸**。它提供第四个选择——"先把内容放进用途明确的格子，等工作走到合适的地方再归档"。底线是（原文）：

> "两者都围绕真实路径工作，避免界面上看着整齐，资源管理器里却找不到文件。"

**关于"引用不移动"需澄清一处认知偏差。** 该表述出自 StarMark 的技术文档，**DeskBox 文档中并未使用**。DeskBox 真实存在的是三层严格区分的语义：

| 层次 | 语义 | 出处 |
|------|------|------|
| **映射文件夹 = 引用** | "目录本身不会被复制，也不会换位置"；删除格子只删入口 | `01-file-widgets.md` |
| **自动叠放 = 纯投影** | "不会创建真实子文件夹。不会修改扩展名。不会移动文件" | `11-file-stacks-and-quicklook.md` |
| **附件关联原路径** | "只保存原路径，不复制文件" | `03-todo-widget.md` |

**对 StarMark 的意义：** DeskBox 不是在"移动"与"引用"间二选一，而是**把三类语义分别命名、分别实现、分别写进文档**。只记一个笼统的"引用不移动"，实现阶段会重新踩语义混淆的坑。

### 2.2 窗口模型与层级管理（最值得抄的部分）

**基本形态：** "格子本质上是一组无边框 Win32 窗口"。UI 层 WinUI 3，层级与桌面行为全部走 Win32 原语。

**技巧 1 —— 瞬态浮起：** 先 `SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE|SWP_SHOWWINDOW)`，紧跟 `SetWindowPos(HWND_NOTOPMOST, ...)`。窗口停在**普通层级带的最顶部**但不具备 TopMost 属性——别的应用激活时能正常盖过。比持久 TopMost 干净得多。

**技巧 2 —— 逻辑状态与物理落点分离：** `DesktopResting` 只是逻辑态，物理落点由策略现算：

| 条件 | 落点 |
|------|------|
| 外部应用在前台 | `BehindForeground` |
| DeskBox 自身在前台 | `PreservePeerOrder` |
| 桌面壳 / 无前台 | `DesktopBottom` |
| DesktopPinned | 桌面 Owner 内兄弟排序 |

文档明确把"把回落等同于 `HWND_BOTTOM`"列为坑 #2。

**技巧 3 —— 整组批量 Z 排序：** 多 widget 全局位置**只由管理器按组确定**，单窗口只清自身状态。用 `BeginDeferWindowPos`/`DeferWindowPos` 以同一前台根窗口为整组边界一次排列，失败再逐窗兜底。

**防抖与看门狗：** 唤起后 160ms 抑制窗；200ms 恢复监视器 + 50ms 鼠标边沿采样器（`GetAsyncKeyState` **高位 0x8000**）；代际计数器使过期异步回调失效；交互配对泄漏的 10s 看门狗。

**记录在案的坑（最反直觉的几条）：** `GetAsyncKeyState & 0x0001` 低位检测不到跨进程点击；`SetForegroundWindow` 会静默失败（UIPI）必须查返回值；配对泄漏会**永久堵死回落**；`IsDeskBoxWindow` 按 PID 判定过宽。

**桌面固定：** 格子 attach 到 **WorkerW** 桌面容器；v1.4.6 起**启动必须等待 Explorer 桌面图标宿主就绪后再 attach**。
> ⚠️ 文档**未描述** `WS_EX_TOOLWINDOW`、Alt-Tab 隐藏、`SetParent` 到 Progman/SHELLDLL_DefView 的具体写法，需读源码（`Win32Helper.cs`、`WidgetLayerService.cs`）。

### 2.3 数据契约与持久化

**存储：** `%LocalAppData%/DeskBox/settings.json` + `data/widgets/{widgetId}/...`（Todo = `todo.json`，QuickCapture 独立目录）。**卸载不删用户数据。**

**序列化：** System.Text.Json **Source Generation**，27 个分域 `JsonSerializerContext`，反射型重载 **0**。关键判断是"**不为统一而统一**"——现有至少六种格式契约，**不能共用全局 options**；用契约测试**冻结调用清单**（65 处 / 29 文件）防退化。

**分级容错：** 未知属性忽略，但未知**枚举名会抛** `JsonException` → 弹性 store 隔离 + 从备份恢复；`WidgetKindJsonConverter` 把未知值**降级为 `File`** 而非整体失败。

**两层模型（叠放的实现基础）：**

- 磁盘文件层 `WidgetViewModel.Items`（事实来源）
- 叠放投影层 `VisibleItems` / `_stackDisplayItems`

叠放**只持久化三类元数据**：`_stackMemberOverrides`、`_stackOrder`、名称/禁用/展开覆盖项。**显示单元是投影，原文件原地不动。**

### 2.4 扩展缝：一篇"先测量、再收敛"的范本

**实测成本（2026-09-11）：** 25 个文件含 kind 分支，单文件最多 **34 处**，设置页要动 **5 个 `SettingsViewModel` partial** + 12 个文化表。

**目标形态「一处声明 + 三处实现」**：contribution 描述符 + 内容实现/provider + 设置节 UserControl + 本地化键。

**最值得学的是它明确写出"哪些分散是有意保留的"**：按 kind 的策略表仍各加一行（那是功能自己的策略）；窗口能力表与描述符表**有意分离**；**不做第三方插件平台**。

### 2.5 交互模型：叠放 / 胶囊 / 分组

**文件栈 = 自动叠放。** 与文件夹的本质区别：文件夹是磁盘真实目录，叠放是**投影**。按类型/日期/自定义扩展名分组，阈值 2/3/5；自定义规则**从上到下匹配、只进第一条命中规则**；人工干预后自动叠放**按需转为手动**。

**胶囊模式 —— 解决"全部展开太挤 / 全部关闭没入口"。**
> "胶囊留住的是入口"：文件仍在原位，待办保留下一条值得看的事。

高价值细节：**三段式热区**（左=识别+拖动 / 中=查看+悬停展开 / 右=操作+拖动手柄）；**锚点**共享（左侧向右展开、右侧向左展开）；**内容模式**三档 + **隐私显示**（收起态隐藏正文，用于共享屏幕，明确不等于加密）；悬停**响应**与**动画**是两组独立设置；拖文件到胶囊 → 临时展开确认 → 完成后**回到原收起状态**。

**组件分组 —— 一次漂亮的自我否定。** `requirements/widget-group-navigation-ux.md` 有"替代声明"：R0–R10 是唯一有效规范，下方 1000+ 行是被否决的历史方案（外部导航条 + 三种样式 + Smart Stack 跟手手势）。最终定案：

1. **砍掉外部导航条**，只在标题栏内放常驻成员选择器。原文："把标签放到内容区会挤占文件列表，单独再放一条导航栏又会让一个小格子多一层视觉负担。"
2. 点击主路径，局部滚轮可选增强，**键盘是完整等价路径**。
3. 防误触：热区 **150ms** 后启用；提交后冷却 **180–220ms**；冷却期 **latest-wins，不排队播放中间成员**。
4. **硬约束**：只有 Standard/Compact 标题栏可分组，禁用时不自动降级，**必须显示禁用原因**。
5. **零新增第三方依赖**（明令不得引入 Lottie、通用动画引擎、第三方标题栏组件）。
6. **微动效红线**："不动画窗口位置、尺寸、圆角、标题栏高度"；只有成员图标和文字在固定槽交叉淡化 100–140ms。
7. **9 步原子切换事务**：Begin → Capture → Prepare → **Present candidate（Loaded + 非零布局 + 挂载后两个合成帧）** → Validate → Persist → Commit → Retire → Rollback。首帧超时 900ms，超 150ms 才显示进度。**核心目标：切换期间无空白帧。**

**概念区分**：**格子组** = 多格子在同一表面切换（共享位置尺寸，最多 8 成员）；**胶囊组合栏** = 多个独立胶囊靠边排列。混淆会导致产品概念崩塌。

### 2.6 拖放契约（全项目最重要的一份契约）

**铁律：两类操作绝不能混。**

| 类型 | 范围 | 动磁盘？ |
|------|------|---------|
| **内部编排** | 同格排序 / 加入叠放 / 移出叠放 | **否**，只改投影与元数据 |
| **文件系统传输** | 跨格 / Explorer ↔ 格子 | **是**，真实复制/移动/建快捷方式 |

**19 条规则中最关键的 5 条：**

1. `RequestedOperation` **只能**是单个 `Move`，能力集合放 `AllowedOperations`——否则 Win10 每次拖出都弹三选菜单。
2. **`DragOver` 可临时接受 `Move`（让 WinUI 路由 Drop），但内部 `Drop` 完成永不返回 `Move`**——否则原生 Shell 把 `.lnk` 当真实移动完成并清理进回收站。
3. 跨格由目标执行真实移动后通知源，源**必须返回 `None`**（返回 `Move` 造成二次清理）。
4. 每次拖拽生成 `DragSessionId`，每次 `GetDragPayload` 校验。
5. 空状态以 `Items.Count == 0` 为准，**不用 `VisibleItems`**。

**"拒绝即消费"原则：** 拖到不支持的目标，返回 `DROPEFFECT_NONE`，**绝不回退为导入**——回退=Move=移动用户文件。文档记录了推翻原决定的理由："文件不支持该打开就走了移动，不该移动，应提示"。

**拖到快捷方式上打开（v1.5.1）：** 选了**把 drop 委托给 shell 自己的 `IDropTarget`**，理由"这就是 Explorer 拖到快捷方式上时跑的同一代码路径，**语义零漂移**"。自解析 `.lnk` 拼命令行的方案被否决："丢失提权标记/链接跟踪/AUMID，且引入手写命令行构造——**转义是本类功能最难查的缺陷源**"。

落地入口实际有**三个**：OLE `IDropTarget`、WinUI 路由 Drop、`WM_DROPFILES`，共用一把 1s 闩锁。

### 2.7 隔离与性能红线

**进程外宿主右键菜单：** 进程内宿主 `IContextMenu` 会把第三方处理器 DLL 加载进主进程，"崩了整个 App 陪葬"。改为 Rust 进程 + stdin/stdout UTF-8 行协议。实测：常驻 server + 预热使菜单弹出 **2343ms → 299ms**；`InvokeCommand` 后需 2s 消息泵宽限；去掉 `TPM_NONOTIFY`（它抑制 `WM_INITMENUPOPUP`）；**钩子必须装在专职泵线程**（实测阻塞时整条桌面输入管线卡 1–2s）。

**内存门禁：**

| 指标 | 阈值 |
|------|------|
| 稳态私有内存 | ≈ **115MB**（Release / Native AOT） |
| 20 循环 Private 净增 | **≤ 15MB** |
| 空闲 60s 工作集回落 | **≥ 交互期增量的 50%** |
| 8 小时长稳 | 无单调爬坡 |
| **明确不要开** | Server GC |

**最有价值的是"为正确性拒绝优化"的红线：** junction 解析**不做缓存**（防读到陈旧物理路径）；缩略图**进程外隔离**不可放弃；毛玻璃**不建议无条件降级**；明确否决 4 项内存优化（compositor 对象池、语言增量化、全类型隐藏释放、窗口虚拟化），理由全是"重显会闪/会慢/会串场"。

### 2.8 开发过程的教训（1.4.8 复盘）

**做对的：** 第一版只做一件事；克制减法（拒绝股票/新闻/视频/浏览器——"一个桌面整理工具，不能靠不断堆功能来证明价值"）；开源免费（"任何人都可以检查 DeskBox 对电脑做了什么"）；面对"低内存 vs 快响应"矛盾不替用户决定，给三种模式。

**踩的坑：**

1. **信任崩塌事故（最严重）** —— 用户卸载后以为桌面文件被删。作者反省：
   > "'文件还在'这句话，在这种时候没有太大意义。软件既然移动了用户的文件，就应该让人清楚地知道它们去了哪里，卸载之后又该怎么找到。"

2. **1.4.5 撤回事故** —— `.lnk` 被误删进回收站。根因："拖放结束时，DeskBox 过早地告诉系统这是一次移动，系统便按照移动的规则清理源位置。" 修复后作者凌晨四点把**所有移动/复制/删除流程重新走了一遍**。

3. **性能是反馈最多的问题** —— "桌面工具需要常驻。任务管理器里的数字一大，用户心里难免会打鼓。"

4. **AI 编程的责任边界** —— "代码能运行，只是一天工作的开始……这些判断不能交给 AI，测试、回归和发布以后的责任，也只能由我承担。"

5. **环境覆盖的不可能** —— "独立开发最困难的地方，有时不是把功能做出来，而是你永远不知道，还有哪台电脑正在等着给你上一课。"

**演进顺序：** 文件格子 → 待办/随记/剪贴板 → 天气/音乐 → 搜索 → 叠放 → 胶囊 → 整理 + 格子组 → 性能/AOT → 拖放语义精修。即**先"接住"，再"整理"，再"收纳与切换"，最后被迫回到"性能与正确性"**。

---

## 三、现状盘点与实测基线

### 3.1 已具备的能力

| 能力 | 实现 | 评价 |
|------|------|------|
| 每组件独立无边框工具窗 | `WidgetWindow` | ✅ 与 DeskBox 模型一致 |
| 不进 Alt-Tab / 任务栏 | `RemoveDefaultWindowFrame` + `WS_EX_TOOLWINDOW` + `IsShownInSwitchers=false` | ✅ **做到了 DeskBox 文档未描述的部分** |
| 拖动 / 缩放 / 边缘吸附 | `WidgetSnapping`（移植 `WidgetSnapCalculator`，纯函数 + 9 单测） | ✅ 设计正确，但有 1 个缺陷（§3.3） |
| 位置/尺寸/置顶持久化 | `widgets.json` v2 + v1→v2 迁移 + 原子写 | ✅ 有版本化意识 |
| 托盘入口 + 全局热键 + 单实例 | `TrayHost` + `Ctrl+Alt+Space` + 命名互斥体 | ✅ 闭环完整 |
| 与条目库打通 | QuickLaunch 读 `GetPinnedAsync(8)` | ⚠️ 只接了一小撮（见缺口一） |
| 拖入建快捷入口 | WebLink / ApplicationLink / StorageItems / Text 四类 | ✅ 当前规模够用 |
| 主窗口体系 | NavigationView + 5 页面 + MVVM + `PreviewHost` + CI | ✅ 基础扎实 |

### 3.2 三个关键缺口

#### 缺口一：本地文件不入库 —— 统一条目模型缺最大的一块

《开发进度报告》§六 Phase 2 第 6 项明确写：**"当前 Everything 仅实时搜索（不写 DB、不进文件夹树）"**。

后果链：

- `items` 表里 `ItemType.File` 只有种子数据等极少量条目
- 本地文件**不进**文件夹树、标签云、动态时间线、置顶
- 因此快捷启动格的 `GetPinnedAsync(8)` 只能摆出书签和 GitHub Star 中的置顶项
- **桌面组件上摆的，是整个库里最小的一撮数据**

这是「先补数据再磨外壳」这条结论的直接依据。

#### 缺口二：规则引擎未实现

`rules` / `rule_runs` 表已建（Schema.sql:120），`IRuleAction` 仅为占位接口，**无任何实现**。增量同步的 `ContinuationToken` 同样未启用（`GitHubSource.cs:13` 注释："本 MVP 暂未启用"）。

> 因此 v1 中把 `MoveFileAction` 列为"当前最高风险"是**判断偏差**——它属 Phase 3 远期事项。此处调整为**远期设计约束**（见 §7.2），不占用当前优先级。

#### 缺口三：组件数据游离在统一条目模型之外

`WidgetStorage` 把 Todos / Notes / Links 存在 `widgets.json`，与 `items` 表完全不通：

| 能力 | 主库条目 | 组件待办/随记 |
|------|---------|--------------|
| 统一搜索 | ✅ | ❌ |
| 打标签 | ✅ | ❌ |
| 置顶 | ✅ | ❌ |
| 规则引擎 | ✅（远期） | ❌ |
| 备份 | ❌（全项目都无） | ❌ |

**这恰恰与项目第一核心亮点"统一条目模型"相悖**——在最该统一的地方开了口子。解法见 §5.3。

### 3.3 实测基线（本次执行）

第八轮文档记载"按用户要求本轮**未执行 dotnet build/run**"。本次补测：

| 项目 | 结果 |
|------|------|
| `dotnet build StarMark.sln -p:Platform=x64` | ✅ **0 错误 0 警告** |
| `dotnet test StarMark.sln -p:Platform=x64` | ⚠️ **60 个测试：59 通过，1 失败** |

#### 失败测试：`WidgetSnappingTests.LeftEdge_AbutsToTargetRight_WithSpacing`

```
Assert.Equal() Failure: Values differ
Expected: 120
Actual:   100
```

**这是真实产品缺陷，不是测试写错。** 根因是吸附算法的**跨轴耦合**：

```csharp
// WidgetSnapping.SnapMove
var snappedX = SnapAxis(proposed, targets, workArea, horizontal: true,  ...);
var snappedY = SnapAxis(snappedX,  targets, workArea, horizontal: false, ...);
//                      ^^^^^^^^^ 第二轴传入的是【已吸附】的矩形
```

追踪（`threshold=24, spacing=8`），目标 `x:300..500, y:100..400`，候选 `x:530..730, y:120..320`：

1. **第一轴（水平）**：投影门限用 Y → 重叠，gap=0 ≤ 24 → 参与；`X=530` 对 `500+8=508`，delta=22 ≤ 24 → **X 吸附到 508**
2. **第二轴（垂直）**：投影门限用**已吸附的 X** → `508..708` vs `300..500`，gap=**8** ≤ 24 → **意外参与**（若用原始 `530..730`，gap=30 > 24，应跳过）
3. 于是 `Y=120` 对目标顶缘 `100`，delta=20 ≤ 24 → **Y 被拉到 100**

**用户可见后果：** 拖动组件横向吸附后，窗口会**在纵向上意外跳动**（最多 24px），与手势意图不符。

**修复方案（把"投影门限判定"与"要修改的矩形"解耦）：**

```csharp
public static RectInt32 SnapMove(
    RectInt32 proposed, IReadOnlyList<RectInt32> targets, RectInt32 workArea,
    int threshold = DefaultThreshold, int spacing = DefaultSpacing)
{
    // 第一轴：门限与被修改的都是 proposed
    var snappedX = SnapAxis(
        source: proposed, gate: proposed, targets, workArea,
        horizontal: true, threshold, spacing, static (r, edge) => r with { X = edge });

    // 第二轴：被修改的是 snappedX，但【门限仍用原始 proposed】
    var snappedY = SnapAxis(
        source: snappedX, gate: proposed, targets, workArea,
        horizontal: false, threshold, spacing, static (r, edge) => r with { Y = edge });

    return snappedY;
}

// SnapAxis 增加 gate 参数：PerpendicularGap 用 gate，四条边比较用 source
private static RectInt32 SnapAxis(
    RectInt32 source, RectInt32 gate, IReadOnlyList<RectInt32> targets, RectInt32 workArea, ...)
{
    // ...
    var perpendicularGap = horizontal
        ? PerpendicularGap(gate.Y, gate.Height, target.Y, target.Height)
        : PerpendicularGap(gate.X, gate.Width,  target.X,  target.Width);
    // ...
}
```

语义也更正确：**"这两个窗口是否大致在同一列/行"应由用户当前拖到的位置判定，而非吸附后的位置判定。** 已逐个手算复核，该修复不会破坏其余 8 个吸附用例。

#### ✅ v3 实作：已改为 DeskBox 原算法的完整移植（而非上面这段补丁）

v2 给出的补丁只解决了跨轴耦合。直接通读 DeskBox `WidgetSnapCalculator.cs` 后发现，**当初的移植是有损的**，遗漏了四项能力，且其中一项（投影门限）根本是移植时凭空发明的：

| 差异点 | DeskBox 原实现 | v2 前 StarMark | v3 处理 |
|-------|---------------|---------------|--------|
| 跨轴耦合 | 两轴均基于原始 `proposedBounds` | 第二轴传入已吸附矩形 ✗ | ✅ 对齐 |
| 垂直投影距离 | **只作打散条件**（同 delta 时的 tiebreaker） | 被当作门槛过滤（gap > 阈值直接跳过）✗ | ✅ 对齐（移除门限） |
| sticky 迟滞 | engage / release 双阈值，防止拖动抖动 | 无 ✗ | ✅ 补齐 |
| 缩放边吸附 | `ResolveResizeEdge` | 无 ✗ | ✅ 补齐 |
| 屏幕边缘 | 零间隙吸附，边距由调用方内缩 workArea | 内置 ±8 ✗ | ✅ 对齐 + `InsetWorkArea` |
| 阈值单位 | DIP × DPI 缩放（用户可配） | 固定物理像素 ✗ | ✅ 会话开始时按 DPI 换算 |

因此 `WidgetSnapping.cs` 已**删除**，替换为 `WidgetSnapCalculator.cs`（忠实移植）；`WidgetSnappingTests.cs` 已替换为 `WidgetSnapCalculatorTests.cs`——前 8 个用例是 DeskBox `WidgetSnapCalculatorTests` 的等价移植（权威规格），后 9 个为 StarMark 补充。

⚠️ **一处测试期望被修正，需知悉：** 原 `LeftEdge_AbutsToTargetRight_WithSpacing` 期望 `Y=120`（纵向不吸附），这是「投影门限」这个不存在于 DeskBox 的约束推导出来的期望值。按 DeskBox 语义，`Y=120` 与目标顶缘 `100` 相差 20 ≤ 阈值，属**真实的顶边对齐**，应吸附到 `100`。已改为 `Y=100`，并在测试注释中说明。

### 3.4 与 DeskBox 成熟度对照

| 维度 | DeskBox | StarMark | 差距 |
|------|---------|----------|------|
| 层级模型 | 逻辑态/物理落点分离 + 瞬态浮起 + 整组 Z 排序 | 二元 `Topmost` | **大** |
| UI 构建方式 | XAML + 描述符 + Provider | **1030 行 code-behind** | **大**（架构不一致） |
| 组件数据与主模型 | 各自独立 store（其本无统一模型） | 游离于 `items` 之外 | **大**（且背离核心亮点） |
| 扩展缝 | 描述符驱动 + 契约测试 | 枚举 + switch（约 7–9 处） | 中（5 种组件，收敛窗口） |
| 数据隔离 | 每组件独立 store + 弹性隔离 + 未知枚举降级 | 单一 `widgets.json` 全量反序列化 | 中（有全清风险） |
| 拖放契约 | 19 条规则 + 会话 ID + 回执纪律 | WinUI 路由 Drop 单层 | 中（接入文件格时是硬门槛） |
| 多显示器 / DPI | v1.4.6 布局记忆 | 存物理像素，无越界回收 | 中 |
| 性能预算 | 115MB / 20 循环 ≤15MB / 60s 回落 ≥50% | 无 | 中（常驻类应用必补） |
| 备份恢复 | ZIP + SHA-256 清单 + 恢复前快照 | **全项目无备份代码** | **大** |
| 胶囊 / 分组 | 成熟 | 无 | 大（规划内增量） |
| 进程外隔离 | Rust sidecar | 无 | 小（暂无高风险 native 依赖） |

---

## 四、重构意见

### R1 · WidgetWindow 从 code-behind 建 UI 改为 XAML + MVVM（最高优先级重构）

**现状：** `WidgetWindow.xaml` 只有一个空壳 `ContentHost` 网格，全部内容由 `WidgetWindow.xaml.cs` 用 C# 手工构建（1030 行）：

```csharp
private UIElement BuildTodo()
{
    var panel = new StackPanel { Padding = new Thickness(12, 8, 12, 12), Spacing = 4 };
    var input = new TextBox { PlaceholderText = "添加待办，回车确认…", FontSize = 12 };
    var listHost = new StackPanel();
    // ... 每一行手工 new Grid / CheckBox / TextBlock / Button
}
```

**问题：**

1. **与项目其余部分严重不一致** —— 主窗口早已是 XAML + ViewModel + `x:Bind`（第四轮还专门修过 `x:Bind` 默认 `OneTime` 导致 UI 不刷新的坑）。组件走回头路，等于放弃已积累的全部经验。
2. **无法用 XAML Hot Reload**，在无设计器的 WinUI 3 下这是主要开发效率来源（可行性分析 §2.2 已把"无设计器"列为确定约束）。
3. **无法复用 `ItemCard`** —— 项目已有可复用卡片 UserControl，组件的 `LinkRow()` 又手写了一遍简化版。
4. `x:Bind` 默认 `OneTime` 的坑会在组件里重演（当前手工赋值反而避开，但也失去绑定能力）。

**建议：**

- `WidgetWindow.xaml` 内为每种组件准备 `DataTemplate`（或 `ContentControl` + 模板选择器）
- 每种组件一个 ViewModel（`TodoWidgetViewModel` 等），`ObservableCollection` + `ItemsRepeater`
- 与 §六 P0-1 的描述符收敛**一并做**：描述符 = 声明，Provider/ViewModel = 实现，XAML 模板 = 视图

**继承既有教训（必须）：** 组件内所有动态绑定显式 `Mode=OneWay`（第四轮教训）；`ItemsRepeater` 内部**不能用 `{Binding}`**，必须用 `x:Bind`（第五轮教训：ItemsRepeater 不给条目设 DataContext，芯片点击曾全部静默失效）。

### R2 · 组件数据纳入统一条目模型（核心架构决策）

见 §5.3 的完整方案与两方案对比。

### R3 · 全量重建改为数据绑定 + 增量更新

**现状：** `ToggleTodo` / `DeleteTodo` / `DeleteNote` / `RebuildQuickLaunch` 都调用 `BuildContent()` **重建整棵 UI 树**。

**问题：** 丢滚动位置、闪屏、随条目数线性变慢。DeskBox 性能审计把"全量视觉树刷新"定为 P0 问题。

**建议：** 随 R1 一并解决——`ObservableCollection` + `ItemsRepeater` 后，勾选待办只需改一个属性。

### R4 · `widgets.json` 拆分与隔离

**现状风险（已存在）：** 单文件全量反序列化，catch 后 `return Normalize(null)`——**任一字段异常即导致待办、随记、快捷入口、窗口位置全部静默清零**。DeskBox 是每组件独立 store + 弹性隔离 + 未知枚举降级。

**建议（三步）：**

1. **立刻**：迁移前 `.bak`（当前 v1→v2 直接覆盖且无备份）
2. **短期**：按域拆 `widgets.window.json` / `widgets.todo.json` / `widgets.note.json` / `widgets.link.json`
3. **中期**：未知值降级而非整体失败（学 `WidgetKindJsonConverter`）

**顺带修一处热路径：** `WidgetManager.IsEnabled(kind)` 每次都 `_storage.Load()`（磁盘读 + 全量反序列化），而菜单 `Opening` 事件对 5 个 kind 各调一次——**一次右键菜单 = 5 次磁盘读**。加内存缓存 + dirty 标志即可。

> 若采纳 R2（待办/随记进 SQLite），本条自动简化为只剩窗口配置 + 快捷入口，风险面大幅缩小。

---

## 五、功能实现方案建议

### 5.1 先让 Everything 索引进库（组件价值的前提）

**这是所有建议里投入产出比最高的一项，优先级应高于组件打磨。**

现状：Everything 只做实时搜索（`SearchAsync` 路径），`FetchAsync` 不落库。

**最小可行方案（不必一上来就全量）：**

```csharp
// EverythingSource.FetchAsync：限定根目录 + 数量上限，落库为 ItemType.File
public async Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
{
    // 1. 只索引用户配置的根目录（设置页可配；默认桌面 + 下载 + 文档）
    // 2. 每次 -search "" -max N（如 5000），按 path 排序，用 ContinuationToken 续拉
    // 3. source_id 沿用既有 SHA256(path.ToLowerInvariant()) 前 8 字节 hex，保证幂等
}
```

**关键设计点：**

- **限定根目录 + 上限**，不要全盘。DeskBox 对自动整理也有硬上限（一次 ≤200 文件 / 100MB）。
- 复用既有 `ContinuationToken` 字段（已定义未启用），做增量拉取。
- 落库后本地文件自动获得：文件夹树、标签、置顶、动态、规则引擎 —— **组件立刻有了真正可摆的内容**。

**顺序建议：** 先把"用户指定目录"的本地文件灌进 `items` 表 → 快捷启动格立刻能摆本地文件 → 再做"标签格 / 搜索结果格"（§7.1）。

### 5.2 快捷启动格：绑定式 + 复用 ItemCard

**现状：** `LoadPinnedAsync` 手工 `host.Children.Add(LinkRow(...))`，刷新即全量重建。

**建议：**

```xml
<!-- WidgetWindow.xaml 内 -->
<ItemsRepeater ItemsSource="{x:Bind ViewModel.PinnedItems, Mode=OneWay}">
    <ItemsRepeater.ItemTemplate>
        <DataTemplate x:DataType="vm:ItemCardViewModel">
            <controls:ItemCard /><!-- 复用主窗口同款卡片 -->
        </DataTemplate>
    </ItemsRepeater.ItemTemplate>
</ItemsRepeater>
```

收益：复用 `ItemCard` 既有能力（右键菜单、标签芯片、预览入口、"发送到桌面·快捷启动"），视觉与交互跟主窗口一致，且自动获得虚拟化。

### 5.3 待办 / 随记：纳入统一条目模型（推荐方案 A）

这是「缺口三」的解法，也是 StarMark 相对 DeskBox 的结构性优势。

#### 方案对比

| | **方案 A：纳入 `items` 表** | **方案 B：维持独立 JSON** |
|---|---|---|
| 做法 | 新增 `ItemType.Todo` / `ItemType.Note`，`source='local'` | 现状不变，只做 JSON 拆分 |
| 统一搜索 | ✅ 待办/随记可被搜到 | ❌ |
| 打标签 / 置顶 | ✅ 复用既有能力 | ❌ 需另建一套 |
| 规则引擎（远期） | ✅ 自动受益 | ❌ |
| 与核心亮点一致 | ✅ 强化"统一条目模型" | ❌ 在核心处开口子 |
| 用户状态边界 | ⚠️ 需设计：`source='local'` 永不参与同步覆盖 | ✅ 天然隔离 |
| 迁移成本 | 中：v2→v3 迁移 + `ItemType` 扩展（约 4 处 switch） | 低 |
| 性能 | 略增（SQLite 完全承受） | 低 |

#### 推荐 A，五个关键设计点

1. **`source='local'` 是关键。** 现有架构已确立"用户状态（hidden/pinned/notes）同步永不覆盖"（第六轮修复）。待办/随记设为 `source='local'` 后天然不会被任何 `IItemSource` 覆盖——**无需改动 Upsert 逻辑，语义自洽**。
2. **`title` = 待办/随记正文**，`search_text` 自动纳入 FTS5（既有触发器），搜索开箱可用。
3. **完成状态不要复用 `pinned`。** 建议新增独立列或用 `extra_json`，避免语义混淆。
4. **迁移**：`WidgetStorage.Normalize` 增加 v2→v3，迁移进 `items` 并**保留原 JSON 作 `.bak`**。
5. **`ItemType` 扩展**需同步检查所有 switch（目前约 4 处）——这是 §六 P0-1 描述符收敛的又一理由。

**若时间紧张可折衷：** 先只把**随记**纳入（笔记本来就有 `notes` 表，语义最接近），待办暂留 JSON。

### 5.4 搜索格：复用 SearchService，而非"唤起主窗口"

**现状：** `Submit()` → `_manager.RequestGlobalSearch(q)` → 唤起主窗口搜索。

**建议：** 组件内直接调 `SearchService`，在组件内 `ItemsRepeater` 展示前 N 条，点击才打开主窗口。理由：

- 唤起主窗口是"重操作"，打断用户当前工作（与"桌面快速取用"初衷相悖）
- StarMark 有 FTS5 毫秒级检索 + Everything 实时源，组件内搜索完全可行
- 这才是 DeskBox 做不到的形态（DeskBox 搜索格只覆盖它自己的内容，跨源统一检索 StarMark 独享）

---

## 六、借鉴建议（按校准后优先级）

> 相对 v1 的变化：**新增 P0-0（修失败测试）与 P0-1b（Everything 入库）**；原 `MoveFileAction` 降级为远期约束。

### P0-0 · 先修 `WidgetSnapping` 跨轴耦合缺陷 ✅ **已完成（v3）**

- ~~**现状：** 60 测试 59 通过，1 失败；拖动时窗口纵向意外跳动~~
- **已完成：** 删除 `WidgetSnapping.cs`，替换为忠实移植的 `WidgetSnapCalculator.cs`；测试 82/82 全绿。详见 §3.3「v3 实作」小节
- **理由：** 在红色测试基线上做重构，无法区分"新引入的"与"本来就坏的"

### P0-1 · 用描述符收敛扩展缝 ✅ **已完成（v3）**

- ~~**现状：** `WidgetKind` 已在 7–9 处产生分支~~
- **已完成：** `WidgetDescriptor.cs`（元数据，Core 层，对应 DeskBox `WidgetContentDescriptor`）+ `WidgetRegistry.Default`（唯一事实来源）+ `WidgetContentFactory`（UI 层内容创建，对应 DeskBox provider 分层）。`KindTitle` / `KindGlyph` / `DefaultWidth` / `DefaultHeight` / `IsResizable` / `BuildContent` 六处分支已全部改为查表。
- **契约测试：** `WidgetRegistryTests.EveryEnumValue_HasDescriptor` 钉住"枚举里存在但未登记描述符"的情况；`UnknownKind_FallsBackWithoutThrowing` 保证存储层读未知类型不崩。
- **注意：** 构造函数内的组件特判（`QuickLaunch` 事件订阅 / `Clock` 计时器）尚未收敛，属下一步。
- **时机：** 5 种组件时约半天；等加了天气/音乐/活动就是 DeskBox 那种 25 文件级重构
- **与 R1 合并做**：描述符=声明，XAML 模板=视图，Provider/VM=实现

### P0-1b · Everything 索引进库（新增，价值最高）✅ **已实装（2026-09-16）**

见 §5.1。**这是"组件有没有内容可摆"的前提。**

**实装记录：** `EverythingSource.FetchAsync` 不再返回空——遍历 `FileIndexOptions.Roots`（默认桌面/下载/文档，设置页可配），以根目录路径作为 Everything 查询词（命中其下含子目录全部文件），每目录上限 `MaxCount`（默认 5000，CLI `-limit` 硬限已放开到 20000）；`source_id` 沿用 `SHA256(path.ToLowerInvariant())` 前 8 字节 hex 保证幂等，`SyncCoordinator` 自动 `UpsertAsync` 入库为 `ItemType.File`。Everything 未运行或根目录不存在时安全返回空。`SearchService` 已按 `Uri` 去重，入库后实时源与 FTS 结果不重复；带标签筛选时实时源虽被跳过，但已入库文件可正常参与标签筛选。设置页新增「本地文件索引」分组（根目录多行 + 每目录上限）。新增 `EverythingSourceTests` 4 例锁定 source_id 契约与空根行为。

### P0-2 · 二元置顶换层级策略 ✅ **已完成（v3）**

- **已完成：** 新增 `WidgetLayerService`（移植 DeskBox `WidgetLayerService.cs`）+ `WidgetZOrderPolicy`（Core 纯函数，移植 `IdleWidgetZOrderPolicy`）。组件窗口在 `Reveal()` 时挂载到 `SHELLDLL_DefView`，`Closed` 时脱离；拖动开始时瞬态浮起（`HWND_TOPMOST` 紧跟 `HWND_NOTOPMOST`）。
- **修正认知：** 持久 TopMost 实际默认是 `false`（用户显式开关），v2 所述"永远压屏"风险不成立；真正的缺口是**从未挂载桌面层**。
- **遗留：** Explorer 重启会销毁 DefView 及其拥有的窗口——DeskBox 文档记录过此坑，StarMark 尚未实现重启后的重挂载，见风险清单。
- **建议：** 三态 `WidgetLayerMode`（Normal / Raised / DesktopPinned）+ 瞬态浮起（TOPMOST→立刻 NOTOPMOST）；多组件由 `WidgetManager` 统一 Z 序；**回落绝不用 `HWND_BOTTOM`**

### P0-3 · `widgets.json` 拆分与隔离

见 R4。若采纳 R2 则风险面自动缩小。

### P0-4 · 多显示器与 DPI

- **DPI 陷阱：** 有存档时直接把 `X/Y/Width/Height` 当物理像素用（默认尺寸才乘 `scale`）——150% 下保存、100% 下启动会放大 1.5 倍
- **越界陷阱：** `GetWorkArea` 用 `MonitorFromWindow`，但窗口**尚未定位**很可能返回主显示器 → 副屏组件被"夹"回主屏
- **建议：** 存 DIP + 参考 DPI；启动校验存档矩形与当前虚拟屏是否有交集，无交集回落到主屏级联位

### P0-5 · 热键 UIPI 降级

`Ctrl+Alt+Space` 走 `RegisterHotKey`。DeskBox 实测：**前台为提权进程时 UIPI 拦截 `WM_HOTKEY`，只有 `WH_KEYBOARD_LL` 能收到**。建议加失败检测 + 低级钩子降级，并支持热键可重录 + Esc 取消。

### P1 · 后续

- **P1-1 胶囊模式**：时钟/搜索/待办天然适合。最小实现：三段式热区 + 锚点共享 + 拖放临时展开 + 隐私模式。**暂不做** DeskBox 那套 9 步原子切换（那是为分组设计的）。
- **P1-2 拖放契约**：当前没问题（快捷入口本就是引用语义）。但**一旦引入文件格立刻踩雷**——现在就把两条纪律写进注释：① `RequestedOperation` 只能单个 `Move`；② **内部 Drop 完成永不返回 `Move`**（1.4.5 事故根因）。另：DeskBox 实测落地入口有三个（OLE / WinUI 路由 / `WM_DROPFILES`），若发现"从某些程序拖进来没反应"优先查另两个。
- **P1-3 性能门禁**：常驻应用必补。建议阈值——稳态 ≤150MB（未上 AOT，可宽于 DeskBox 的 115MB）、20 循环净增 ≤15MB、空闲 60s 回落 ≥50%、不开 Server GC。**已做对的：** 时钟计时器在隐藏时 `Stop()`，保持。
- **P1-4 备份**：`src/` 下**无任何备份/快照代码**（已核实），风险裸露。最小版：恢复前快照 + `widgets.json` 纳入导出（不打包链接指向的实际文件）。

### P2 · 暂缓

- **P2-1 组件分组**：DeskBox 的分组要求复用同一 HWND，与 StarMark"每 kind 独立 HWND"冲突。可选路径：(a) 改单窗口多成员切换（大改）；(b) 只做"胶囊组合栏"（兼容现有模型）。**真要做时务必先读** `requirements/widget-group-navigation-ux.md` 的"替代声明"，那 1000+ 行被否决方案能省大量试错。
- **P2-2 进程外隔离**：当前无第三方 native 依赖，不必做。**触发条件**：一旦接入第三方 shell 扩展（含"卡片右键直达 Windows 原生菜单"这一《架构健壮性分析》§六已列事项），就是引入隔离的时刻。

---

## 七、差异化：StarMark 不该照抄的部分

### 7.1 组件内容应走"统一条目"路线，而非 DeskBox 的文件格

DeskBox 全部设计围绕**文件收纳**（文件格 → 叠放 → 整理 → 桌面组织）。它**没有**统一条目数据库，处理不了书签/GitHub Star/剪贴板这类异构源。

StarMark 恰恰相反：`items` 统一表 + FTS5 两阶段查询 + Everything 是它的地基。**这是唯一可能形成代差的地方。**

**建议的差异化组件（均依赖 §5.1 的 Everything 入库）：**

| 组件 | 说明 | 依赖（已有/在办） |
|------|------|-----------------|
| **标签格** | 某标签的条目直接摆桌面，随库更新 | `item_tags` + `TagsPageViewModel` ✅ |
| **搜索结果格** | 钉住一条查询（如 `stars>500 AND topic:rag`），结果常驻 | `SearchService` + FTS5 ✅ |
| **最近活动格** | 按 `updated_at` 展示最近条目 | `ActivityPageViewModel` ✅ |
| **置顶条目格** | 现状已有，可扩展为分组置顶区 | `items.pinned` ✅ |

**一句话：抄 DeskBox 的窗口与交互工程能力，组件内容走 StarMark 自己的统一条目路线。**

### 7.2 `MoveFileAction` 调整为远期设计约束（相对 v1 的修正）

v1 将其列为"当前最高风险"。核实后：规则引擎**尚未实现**（仅 schema + 占位接口），属 Phase 3 远期事项，故下调优先级。但约束本身依然成立，将来做时须满足：

1. **移动前展示"计划"而非"数量"** —— 列出准备处理的文件、分类、**最终真实路径**，可跳过/改目标。原文："你知道文件要去哪里，才更愿意让工具替你做重复的动作。"
2. **自动化"先建基线、只管新增、等待稳定"** —— 默认关闭；开启时不追溯已有文件；FS 事件防抖 3–5s，连续 2–3 次确认大小与 mtime 稳定；状态存疑即留在原地。
3. **可枚举的安全边界** —— 文件夹/隐藏/系统/重解析点/云占位/下载中/权限不足/单文件 >100MB 不处理；一次 ≤200 文件 / 100MB。
4. **MVP 阶段建议只做 `add_tag` / `set_note` / `mark_duplicate`，不做真实移动。** StarMark 的价值主张是"统一检索 + 组织"，不是"整理桌面"。
5. **规则绑定稳定 ID 而非显示名称**（DeskBox：`OrganizationRule` → `WidgetId` 而非 `DisplayName`）；目标失效时**暂停并提示**，不得静默改投。

### 7.3 不要照抄的三件事

| DeskBox 做法 | StarMark 不该跟 | 原因 |
|-------------|----------------|------|
| Native AOT + Rust sidecar + 进程外 COM | ❌ 暂缓 | DeskBox 为此投入 70+ 篇 stage 报告；当前无第三方 native 依赖，引入是纯负债 |
| 27 个分域 `JsonSerializerContext` | ❌ 现在不做 | AOT 配套要求；先做数据隔离更划算 |
| 文件整理 / 桌面组织 | ❌ 非差异化 | 见 §7.1、§7.2 |

---

## 八、建议落地顺序

```
第九轮 · 先把地基修平 ✅ 已完成（本次直接拉取 DeskBox 源码复用）
├─ ✅ P0-0  吸附算法改为完整移植 WidgetSnapCalculator（修复跨轴耦合 → 82/82 全绿）
├─ ✅ P0-1  描述符 + 注册表 + 内容工厂，收敛 6 处 WidgetKind 分支
├─ ✅ P0-2  层级策略：挂载桌面图标层 + 瞬态浮起 + 空闲期 Z 序
└─ ⬜ P0-3  widgets.json 迁移前 .bak + IsEnabled 磁盘读缓存

第九轮后续 / 下一次
├─ ⬜ P0-4  DIP 存储 + 启动越界回收
├─ ⬜ P1-4  最小备份：恢复前快照 + 组件数据纳入导出（当前零兜底）
└─ ⬜ Explorer 重启后重新挂载桌面层（见风险清单）

第十轮 · 让组件有内容可摆（价值最高）
├─ ✅ P0-1b Everything 索引进库（限定根目录 + 上限 + 增量续拉）
└─ 验证：本地文件出现在文件夹树/标签/动态/置顶，快捷启动格可摆本地文件

第十一轮 · 组件架构归位
├─ ✅ R1  WidgetWindow 改 XAML + MVVM（快捷启动格 2026-09-16；Search 2026-09-16；Todo/QuickNote/Clock 2026-09-17 全部迁移完成，组件重构收官）
│          （组件内绑定必须 Mode=OneWay、ItemsRepeater 内用 x:Bind；QuickLaunch 试点已验证）
├─ ✅ R3  全量重建 → ObservableCollection + ItemsRepeater（快捷启动格 LinksChanged 增量刷新 2026-09-16；Search 同模式 2026-09-16；Todo/QuickNote/Clock 同模式落地 2026-09-17）
└─ ✅ 多标签 AND 搜索桌面版：Search 组件改为 XAML + ViewModel，关键词 + 标签 chip 多选（AND）内联结果，空关键词+标签退化为按标签浏览（2026-09-16）

第十二轮后续 · 快捷启动格交互补强（2026-09-16 已修）
├─ ✅ 链接过多被遮挡：ContentHost 由 Grid 改为 StackPanel，外层 ScrollViewer 可靠按内容滚动
├─ ✅ 已置顶条目取消置顶：置顶行加 ✕ 按钮 → SetPinnedAsync(id,false) + 增量刷新
└─ ✅ 拖入文件/文件夹无法打开：LauncherEx.OpenAsync 对 file:// 改用 LaunchFileAsync / LaunchFolderAsync（LaunchUriAsync 对 file:// 静默失效）

第十二轮 · 接回统一条目模型（差异化）
├─ ⬜ R2 / §5.3  待办/随记纳入 items（建议先随记，待办折衷）
├─ ⬜ §5.2       快捷启动格复用 ItemCard
└─ ⬜ §5.4       搜索格改为组件内直搜

之后 / 按需
├─ ⬜ P0-5 热键 UIPI 降级 + 可重录
├─ ⬜ P1-1 胶囊模式（三段式热区优先）
└─ ⬜ P1-3 性能门禁（先定测量方法，再优化）
```

**排序逻辑：** ① 先让测试变绿（否则无法判断后续改动）；② 再补数据（组件价值的前提）；③ 再做架构归位（在数据之上重构才有意义）；④ 最后才打磨外壳与交互。

---

## 九、风险清单

| # | 风险 | 影响 | 现状 | 建议 |
|---|------|------|------|------|
| R1 | `widgets.json` 单文件损坏 → 待办/随记/入口/位置全清 | **数据丢失** | 已存在 | P0-3 / R4 |
| R2 | v2→v3 迁移直接覆盖且无备份 | **数据丢失** | 已存在 | P0-3 |
| R3 | 吸附算法跨轴耦合 → 拖动时纵向意外跳动 | 体验缺陷 | ✅ **已修复（v3）** | P0-0 |
| R4 | 保存物理像素 + DPI 变化 → 尺寸/位置错乱 | 体验 | 已存在 | P0-4 |
| R5 | 拔掉外接显示器后组件落到屏外 | 找不到组件 | 已存在 | P0-4 |
| R6 | ~~持久 TopMost 造成"永远压屏"~~（实测默认 false，用户显式开关，风险不成立） | — | ✅ **已澄清** | — |
| R6b | **Explorer 重启会销毁 SHELLDLL_DefView 及其拥有的组件窗口** | 组件消失 | **v3 新引入** | 监听 `TaskbarCreated`/`shellhook` 后调 `InvalidateDesktopCache()` 并重挂载，或降级为不挂载 |
| R7 | 本地文件不入库 → 统一条目模型缺最大一块，组件无内容可摆 | **核心价值** | 已解决（2026-09-16） | P0-1b |
| R8 | 组件数据游离于 `items` 之外，背离核心亮点 | **架构** | 已存在 | R2 / §5.3 |
| R9 | ~~WidgetWindow 全 code-behind，与项目其余部分不一致~~ | 可维护性 | ✅ **已解决（R3 组件重构收官，2026-09-17）** | R1 ✅ |
| R10 | ~~扩展缝未收敛，组件增多后重构成本陡增~~ | 工期 | ✅ **已收敛（v3）** | P0-1 |
| R11 | ~~全项目无任何备份能力~~，组件数据重装即失 | 数据丢失 | ✅ **已通过 P1-4 备份恢复解决（2026-09-16）** | P1-4 ✅ |
| R12 | 前台为提权进程时全局热键失效 | 体验 | 已存在 | P0-5 |
| R13 | `MoveFileAction` 移动用户文件（Phase 3 远期） | 信任崩塌 | 未实现 | §7.2 设计约束 |
| R14 | 无性能门禁，常驻后内存爬坡 | 口碑 | 已存在 | P1-3 |
| R15 | 引入第三方 shell 扩展后主进程被带崩 | 崩溃 | 未发生 | P2-2 |

---

## 附录：DeskBox 文档索引

**优先读：**

- `docs/articles/00-overview.md` — 产品世界观，"真实路径优先"出处
- `docs/architecture/[重要勿删]widget_zorder_lifecycle.md` — 层级机制 + 8 个坑
- `docs/architecture/[重要勿删]file_drag_stack_contract.md` — 19 条拖放规则 + 两层模型
- `docs/architecture/widget_contribution_seam.md` — 扩展缝成本量化
- `docs/articles/deskbox-1.4.8-release-reflection.md` — 复盘，含两起事故

**按需读：**

- `docs/requirements/widget-group-navigation-ux.md` — 分组导航（注意顶部"替代声明"）
- `docs/requirements/desktop-auto-organization.md` — 自动整理安全边界
- `docs/requirements/adaptive-widget-animation.md` — 帧钟与降频
- `docs/architecture/native_context_menu_hosting.md` — 引入原生菜单时必读
- `docs/memory-optimization-plan.md` — 内存预算与验收门禁
- `docs/articles/performance-audit-20260907.md` — 性能问题清单

**不必读：** `docs/architecture/stage-reports/`（70+ 篇 AOT 迁移日志）、`docs/releases/`（按需查时间线）。

---

**文档结束。**

> 关于 DeskBox 的结论均标注出处；文档未明确处均标注"文档未明确"。
> `WS_EX_TOOLWINDOW` / Alt-Tab 隐藏 / `SetParent` 到桌面容器等实现细节，DeskBox 文档**未描述**，需读其源码 `Win32Helper.cs` 与 `WidgetLayerService.cs`。
> 本版实测基线（构建 0 错误 0 警告、测试 59/60）于 2026-09-16 在本机执行，环境 net9.0-windows10.0.19041.0 / WindowsAppSDK 2.4.0。
