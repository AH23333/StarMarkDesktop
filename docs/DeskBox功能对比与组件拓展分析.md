# DeskBox 功能对比与 StarMarkDesktop 组件拓展分析

**文档版本：** v1（功能视角）
**调研对象：** `DeskBox-ref/` 源码（README v1.5.2 全文 + 652 个 `.cs` 文件清单 + 关键文件 `WidgetContentDescriptor.cs` / `WidgetShell.xaml.cs` / `WidgetManager.*` / `*WidgetContent*` / `WidgetCompact*` / `WidgetGroup*`）
**分析对象：** StarMarkDesktop 当前组件能力（截至 2026-09-18 提交 + 本轮代码洞察 T1–T3）
**姊妹文档：** [DeskBox桌面组件借鉴与优化意见.md](./DeskBox桌面组件借鉴与优化意见.md)（工程视角：窗口层级/吸附/数据模型）、[代码洞察与重构计划.md](./代码洞察与重构计划.md)（架构/耦合/性能）、[StarMark扩展对比与补齐方案.md](./StarMark扩展对比与补齐方案.md)（与浏览器扩展对比）

---

## 0. 文档定位与方法

既有 `DeskBox桌面组件借鉴与优化意见.md`（v3）是**工程视角**——聚焦窗口层级、吸附算法、数据模型、XAML 化。本文是**功能视角**：

1. 盘点 DeskBox **实际具备的组件功能**（不止于文档，已读源码）；
2. 对照 StarMarkDesktop **当前组件能力**；
3. 给出三件套：**现有组件的优化/修改意见** + **功能拓展方向** + **可行性分析**。

**方法**：直接通读 `DeskBox-ref/` 源码。凡涉及 DeskBox 实现，均引用其具体源文件，便于后续按图索骥；StarMarkDesktop 现状以最新提交与既有文档标记为准（v3 文档中 ⬜ 项即视为未落地）。

---

## 1. DeskBox 组件功能全景（基于源码，非仅文档）

| # | 功能域 | DeskBox 关键源文件 | 能力要点 |
|---|--------|-------------------|----------|
| 1 | **文件组织 / 文件夹格** | `Controls/FileItem*.cs`、`Controls/WidgetContents/FileSurfaceContent.*`、`Services/FileService.*`、`Helpers/*NativeDrop*` | 真实文件夹/映射文件夹；图标/列表布局；手动/规则排序；手动与自动叠放（Stack）；原生 Shell 拖放（复制/移动/快捷方式）；QuickLook 预览；文件夹内建文件夹 |
| 2 | **组件分组** | `Services/WidgetManager.Groups.cs`、`Controls/WidgetGroupTitleSwitcher.*`、`Services/WidgetGroup*.cs` | 多格合并同表面切换成员；标题/滚轮/Ctrl+Tab 切换；可拆离/解散 |
| 3 | **待办与速记** | `Controls/WidgetContents/TodoWidgetContent.*`、`QuickCaptureSurfaceContent.*`、`Services/Todo*Service.cs`、`QuickCapture*Service.cs` | 双栏/单栏；到期/提醒/重复/颜色/Markdown/附件/筛选/批量；速记支持置顶、纸感、Markdown、附件 |
| 4 | **桌面搜索** | `Controls/WidgetContents/SearchWidgetContent.*`、`Services/EverythingSearchService.cs`、`Search*Service.cs` | 文件/应用/设置/笔记/待办统一搜；Everything IPC 合并 DeskBox 自有内容；全局搜索热键；筛选/排序/历史/收藏 |
| 5 | **Glance / 天气 / 音乐** | `GlanceWidgetContent.*`+`Glance*Service.cs`、`WeatherWidgetContent.*`+`Weather*Service.cs`、`MusicWidgetContent.*`+`Music*Service.cs` | Glance：日期/星期/农历/节日 + 背景图旋转；天气：MSN Weather + Open-Meteo 兜底；音乐：Windows 媒体会话控制/进度/系统音量/多会话切换 |
| 6 | **胶囊模式** | `Services/WidgetManager.CapsuleArrangement.cs`、`Services/WidgetCompact*`（约 20 个策略文件）、`Views/SettingsSections/CapsuleModeSettingsSection.xaml.cs` | 收起为智能胶囊；点击切换/悬停展开；三段式热区；隐私模式（收起态隐藏正文）；可组成胶囊组合栏 |
| 7 | **外观与材质** | `Services/WidgetMaterialVisualCalculator.cs`、`Views/WidgetWindowBase.Backdrop.cs`、`Services/WidgetBorderVisualCalculator.cs`、`Widget*Settings.cs` | Mica/亚克力；不透明度/边框/DWM 圆角/动画/标题栏/图标尺寸/文本尺寸；文本与单色控件可跟随主题或每组件自定义颜色 |
| 8 | **跨切面能力** | `Services/WidgetTopologyLayoutService.cs`、`PerformanceSettingsPolicy.cs`+`MemoryReclaimer.cs`、`AppUpdateService.cs`+`DeskBoxDataBackupService.cs`、`LocalizationService.cs`、`Services/GlobalHotkeyService.cs` | 每显示器拓扑布局（热插拔恢复）；性能模式（Balanced/Resource saver/Custom + 缓存预算）；更新/备份/诊断；12 语言本地化；全局热键多预设 |

**关键架构事实（来自源码，非文档）：**
- `WidgetContentDescriptor`（DeskBox）是**能力/生命周期描述符**：含 `ContentStage`(Implemented/Placeholder)、`Availability`(Available/Planned)、`IsFeatureWidget`(用户显式开启才可见)、`HasSettingsPage`/`SettingsSectionTag`、`WidgetChromeCategory`/`WidgetChromeMode`、`CanUseOverlayChrome`/`CanHideChrome`。—— StarMarkDesktop 的 `WidgetDescriptor` 仅含标题/字形/尺寸/`IsResizable`，差距在此。
- `WidgetShell` 是**可复用的 `UserControl`**，把标题栏/胶囊/分组导航/材质这些"外壳"与 `WidgetContents/*` 的"内容"彻底分离。—— StarMarkDesktop 的 `WidgetWindow` 是 `Window`，外壳逻辑在 `WidgetWindow.xaml.cs`（约 734 行 code-behind），无独立可复用 Shell。
- 组件内容通过 `IWidgetContentProvider` + `WidgetContentFactory` 提供，与 StarMarkDesktop 的 provider 分层思路一致（v3 已收敛），但 DeskBox 的描述符维度更细。

---

## 2. 功能对照矩阵

| 功能域 | DeskBox | StarMarkDesktop 当前 | 差距 |
|--------|---------|----------------------|------|
| 快捷启动/置顶入口 | 文件格 + 快捷方式 | ✅ QuickLaunch（置顶条目 + 拖入 + 自定义入口 + 复用 ItemCard + 组件内直搜） | ✅ 已复用 ItemCard + 组件内直搜（A-4） |
| 时钟 | Glance 内含时间 | ✅ Clock（可缩放、字号自适应） | 小（缺日期/农历） |
| 待办 | ✅ 完整（提醒/重复/附件/Markdown） | ⚠️ Todo（基础勾选，A-3 已并入统一 items 表） | **大**（原游离，A-3 已并入 items 表） |
| 速记/随记 | ✅ Quick Capture（纸感/Markdown/附件） | ⚠️ QuickNote（基础，A-3 已并入统一 items 表） | **大**（同上，A-3 已并入 items 表） |
| 搜索 | ✅ Everything IPC + 自有内容合并 | ✅ Search（Everything + 统一条目 FTS5 + 组件内直搜） | ✅ 组件内直搜 + 点击才开主窗（A-4） |
| 文件格/桌面组织 | ✅ 核心 | ❌ 无（刻意不做，见 §7） | 设计性缺失 |
| 组件分组 | ✅ | ❌ | 大（规划内增量） |
| 胶囊模式 | ✅ 成熟 | ❌ | 大（高频需求） |
| Glance（农历/节日） | ✅ | ❌（Clock 仅时间） | 中（低成本可补） |
| 天气 | ✅ | ❌ | 中（需外部 API） |
| 音乐控制 | ✅ | ❌ | 中（需原生媒体 API） |
| 每显示器布局 | ✅ 拓扑记忆 | ✅ DIP 相对存储 + 越界回收（Phase B-5） | 小（余热插拔运行时重定位） |
| 性能模式/门禁 | ✅ | ❌（无内存预算，P1-3 ⬜） | 中（常驻必补） |
| 外观细化 | ✅ 每组件颜色/文本尺寸/材质 | ⚠️ 全局亚克力 + 不透明度（已照搬控制器） | 中 |
| 备份/诊断 | ✅ 组件数据隔离备份 | ⚠️ 主库备份已做（P1-4），组件 `widgets.json` 隔离仍 ⬜(R4) | 中 |
| 多语言 | ✅ 12 语言 | ❌ 中文硬编码 | 大但**暂不做**（P2-10 判定） |
| 统一条目模型 | ❌（无此概念） | ✅ items 统一表 + FTS5 + 跨源 | **StarMark 代差优势** |

---

## 3. 当前组件优化 / 修改意见（针对已有的 5 个组件）

### 3.1 快捷启动（QuickLaunch）—— 复用 + 内联
- **复用 `ItemCard`**（A-4 ✅）：原 `LinkRow()` 手搓简化版卡片已移除，改为 `ItemsRepeater` + `ItemCard`，自动获得右键菜单、标签芯片、预览入口、发送到桌面，且视觉与主窗一致；自定义快捷入口用合成 Item + `IsLauncherMode` 隐藏会误写主库的操作。
- **组件内直搜**（A-4 ✅）：原 `Submit()` 唤起主窗搜索已移除，改为组件内直接调 `SearchService` 展示前 12 条，点击才开主窗（`WidgetManager.RequestGlobalSearch`）——这是 DeskBox 做不到、StarMark 独享的形态。
- 已 OK：拖入建入口、置顶/取消置顶、文件打开（`LauncherEx` 已修）。

### 3.2 时钟（Clock）—— 加 Glance 式信息
- 已可缩放 ✅、字号自适应 ✅。建议低成本升级为"微型 Glance"：显示日期/星期/农历/节日（纯本地计算，无网络），背景图旋转可后置。

### 3.3 待办（Todo）/ 随记（QuickNote）—— **核心优化：纳入统一条目模型**
- 现状二者存 `widgets.json`，**游离于 `items` 表之外**，不参与统一搜索、不能打标签、不能享用规则引擎——与项目第一核心亮点"统一条目模型"直接相悖（v3 §3.2 缺口三）。
- **推荐方案 A**（v3 §5.3）：新增 `ItemType.Todo`/`Note`，`source='local'`（永不参与同步覆盖）；`title`=正文自动入 FTS5；完成态用独立列/`extra_json` 避免与 `pinned` 语义混淆；`WidgetStorage` 增加 v2→v3 迁移并保留原 JSON 作 `.bak`。
- 收益：待办/随记可被统一搜索命中、可打标签、可进规则引擎——**这是 StarMark 结构性强于 DeskBox 的点，当前白白浪费**。

### 3.4 搜索（Search）—— 组件内体验对齐主窗
- 已与 Everything + 统一条目 FTS5 打通 ✅。优化点：
  - 组件内直搜（见 §3.1）；
  - 结果卡片复用 `ItemCard`（主窗已用，组件内未用）；
  - 字段高亮（`Highlighter` 已在主窗落地）组件内未启用。

### 3.5 全局外壳能力（已具备，需补强）
- ✅ 默认布局持久化、✅ 可自定义热键（最多 3 键/保存才生效/Esc 清除）、✅ 亚克力材质（照搬 DeskBox 控制器方案）、✅ 桌面层级挂载 + 瞬态浮起 + 吸附。
- ⬜ 缺：**性能模式**、**组件数据隔离备份**（v3 P1-3/P1-4；胶囊模式基础形态 / 每显示器拓扑布局已落地：B-1 / B-5）。

---

## 4. 功能拓展方向

按"是否差异化"分三档：

### A. 差异化必做（StarMark 独有能力，DeskBox 做不到）
全部基于已落地的统一条目模型（v3 §7.1），是护城河：

| 组件 | 说明 | 依赖（已有） |
|------|------|--------------|
| **标签格** | 某标签条目常驻桌面，随库更新 | `item_tags` + `TagsPageViewModel` ✅ |
| **搜索结果格** | 钉一条查询（如 `stars>500 AND topic:rag`）常驻 | `SearchService` + FTS5 ✅ |
| **最近活动格** | 按 `updated_at` 展示最近条目 | `activity` 表 ✅（P1-3 已落地） |
| **置顶条目格** | 现状已有，可扩展为分组置顶区 | `items.pinned` ✅ |

**一句话：抄 DeskBox 的窗口与交互工程，组件内容走 StarMark 自己的"统一条目"路线。**

### B. 抄工程能力（DeskBox 强项）
- **胶囊模式**：收起为胶囊、点击/悬停展开、隐私模式、组合栏。
- **组件分组**：同一表面切换成员（注意与 StarMark 每 kind 独立 HWND 模型冲突，见 §5）。
- **每显示器拓扑布局**：热插拔恢复位置/尺寸/分组。
- **性能模式**：Balanced/Resource saver/Custom + 缓存预算（常驻应用必补）。
- **外观细化**：每组件颜色/文本尺寸/材质/边框/圆角（在已照搬的亚克力控制器之上叠加）。

### C. 新内容组件（需评估外部依赖）
- **Glance**（强烈推荐）：纯本地日期/农历/节日 + 背景图旋转，低成本。
- **天气**（可选）：需 MSN/Open-Meteo 网络；隐私/缓存/离线兜底需设计。
- **音乐**（可选）：`Windows.Media.Control` 托管 API 做播放控制/进度/多会话；系统音量 DeskBox 用 Rust sidecar，StarMark **v1 可只做播放控制、音量留 v2**（避免引入原生依赖）。

### D. 暂缓 / 不建议
- **文件收纳格 / 桌面组织**：与 StarMark 主业（统一检索）冲突（v3 §7.2）。
- **多语言（12 语言）**：`resw` 改造面大、收益不明确，P2-10 已判定暂不做。
- **Native AOT + Rust sidecar + 27 个 JsonSerializerContext**：纯负债（v3 §7.3），当前无第三方 native 依赖不值得。

---

## 5. 可行性分析（逐功能）

工程量：S<1天 / M 1–3天 / L >3天或架构冲突。风险：低/中/高。

| 拓展项 | 依赖 | 工程量 | 风险 | 推荐 | DeskBox 参考实现 |
|--------|------|--------|------|------|------------------|
| 扩展描述符增强（对齐 `WidgetContentDescriptor`：Stage/Availability/FeatureWidget/Chrome 模式） | 现有 `WidgetDescriptor` | **S** | 低 | **强烈推荐**（解锁下列多项） | `Services/WidgetContentDescriptor.cs` |
| 标签格 / 搜索结果格 / 活动格 / 置顶格 | items/tags/SearchService（已有） | **S** | 低 | **强烈推荐** | `Services/*WidgetContentProvider.cs` + `WidgetContents/*` |
| 待办/随记入 `items` | `ItemType` 扩展 + 迁移 v2→v3 | **M** | 中（4 处 switch + 迁移） | **强烈推荐**（核心架构） | （StarMark 独有能力，无对应） |
| 复用 `ItemCard` + 组件内直搜 | R1/R3 已落 | **M** | 低 | 推荐 | `SearchWidgetContent` + `WidgetContents` |
| 胶囊模式 | 扩展描述符（CanHideChrome）+ Shell 重构 | **M–L** | 中 | 推荐 | `Services/WidgetCompact*` + `WidgetShell` |
| 组件分组 | 与每 kind HWND 模型冲突 | **L** | 高 | **折衷做"胶囊组合栏"** | `WidgetGroup*` + `WidgetGroupTitleSwitcher*` |
| 每显示器拓扑布局 | DIP 存储(P0-4) + 显示器监听 | **M** | 中 | 推荐 | `WidgetTopologyLayoutService.cs` |
| 性能模式 / 内存门禁 | 测量内存/缓存预算 | **M** | 低 | 推荐（常驻必补） | `PerformanceSettingsPolicy.cs` + `MemoryReclaimer.cs` |
| Glance（日期/农历/节日） | 纯本地计算 | **S** | 低 | 推荐 | `GlanceWidgetContent` + `Glance*Service.cs` |
| 天气 | 天气 API 网络/隐私/缓存 | **M** | 中 | 可选 | `WeatherWidgetContent` + `WeatherService.cs` |
| 音乐（播放控制优先） | `Windows.Media.Control` 托管 API | **M**（控制）/ **L**（音量） | 中 | 可选（音量 v2） | `MusicWidgetContent` + `MusicSessionService.cs` |
| 多语言 | `resw` 全量改造 | **L** | — | **暂不**（P2-10） | `LocalizationService.cs` |
| 文件收纳格 | 与主业冲突 | — | 高 | **不做**（§7.2） | `FileSurfaceContent.*` |
| 组件数据隔离备份 | 主库备份(P1-4)已做 | **S** | 低 | 推荐（补 R4） | `DeskBoxDataBackupService.cs` |

**关键判断：**
- **先做"扩展描述符增强"**——它几乎零成本，却是胶囊模式、特性开关、外观细化、分组的前提（对应代码洞察 T13/T14/T15 的落地目标）。
- **差异化四格 + 待办/随记入 items** 是投入产出比最高的两件事，且不依赖任何外部 API。
- **组件分组**风险高（HWND 模型冲突），优先用"胶囊组合栏"折衷，不碰同 HWND 多成员架构。

---

## 6. 建议落地顺序

```
Phase A · 价值最高、低风险（约 1 周）
├─ 扩展描述符增强（对齐 WidgetContentDescriptor 维度）        ← 解锁后续
├─ 差异化四格：标签格 / 搜索结果格 / 活动格 / 置顶格          ← StarMark 护城河
├─ 待办/随记入 items 表（方案 A，含 v2→v3 迁移 + .bak）       ← 核心架构
└─ QuickLaunch 复用 ItemCard + 搜索组件内直搜

Phase B · 工程能力（约 1–2 周）
├─ 胶囊模式（三段式热区 + 隐私 + 组合栏）                    ← DeskBox 高频需求
├─ 每显示器拓扑布局（DIP 存储 + 热插拔恢复）
├─ 性能模式 / 内存门禁（常驻必补）
└─ 外观细化（每组件颜色/文本尺寸/材质/边框/圆角）

Phase C · 新内容组件（按需）
├─ Glance（日期/农历/节日，低成本）                          ← 推荐先做
├─ 天气（需网络，可选）
└─ 音乐（播放控制优先，音量 v2）

暂缓 / 不做
├─ 组件分组 → 折衷"胶囊组合栏"
├─ 文件收纳格（与主业冲突）
├─ 多语言（P2-10 暂不）
└─ Native AOT + Rust sidecar（负债）
```

**排序逻辑：** ① 先补"统一条目"护城河（四格 + 待办入 items），这是 DeskBox 结构上做不到的；② 再抄工程能力（胶囊/布局/性能），这些是 DeskBox 已验证的交互范式；③ 最后才加新内容组件，按外部依赖成本排序（Glance 无依赖先做，天气/音乐靠后）。

---

## 7. 差异化原则（不要照抄）

**抄：**
- 窗口工程（已抄：层级挂载/瞬态浮起/吸附/亚克力控制器）
- 交互工程（胶囊、分组、性能门禁、扩展缝/描述符思想）
- 外观细化的"每组件可控"理念

**不抄：**
- 文件整理 / 桌面组织主业（§7.2，非 StarMark 差异化）
- Native AOT + Rust sidecar（§7.3，纯负债）
- 27 个分域 `JsonSerializerContext`（AOT 配套，当前无必要）
- 12 语言本地化（P2-10 判定暂不做）

**护城河一句话：** DeskBox 的全部设计围绕"文件收纳"，它没有统一条目数据库；StarMark 的地基是 `items` 统一表 + FTS5 两阶段查询 + Everything。组件内容应走"统一条目"路线（四格），而非 DeskBox 的"文件收纳"路线。

---

## 8. 与既有文档衔接

- 本文是 `DeskBox桌面组件借鉴与优化意见.md`（v3，工程视角）的**功能视角补篇**；v3 中的 R1/R2/R3/P0-1b/P0-4/P1-3 等工程项进度以 v3 为准（R1/R3 已落，R2/§5.2/§5.4/P0-4 仍 ⬜）。
- 与 `代码洞察与重构计划.md` 的 T4–T17 衔接：
  - "扩展描述符增强" = T13/T14/T15 的落地目标之一；
  - "性能模式/内存门禁" = T 系列性能优化；
  - "每显示器拓扑布局" = P0-4（DIP 存储 + 启动越界回收）；
  - "组件数据隔离备份" = R4（widgets.json 拆分 + 隔离）。
- 与 `StarMark扩展对比与补齐方案.md` 衔接：差异化四格依赖的"中文检索修好(P0-1)""活动表(P1-3)""多标签 AND(P1-A)"均已落地，故四格现在即可做。

---

## 9. 落地进度（截至 2026-09-18）

| 文档项 | 状态 | 落地说明 |
|--------|------|----------|
| A-1 扩展描述符增强 | ✅ 已完成 | `WidgetDescriptor` 补齐 `IsFeatureWidget`/`HasSettingsPage`/`DefaultChromeMode`/`CanHideChrome`/`CanUseOverlayChrome`，与 DeskBox `WidgetContentDescriptor` 维度对齐（原已有 `Stage`/`Availability`/`CanCreateWindow`/`ShowInCreateEntry`）。 |
| A-2 差异化四格 | ✅ 已完成 | 新增 4 个 `WidgetKind`（TagGrid/SearchResults/Activity/Pinned）+ `WidgetInstanceConfig.GridTag/GridQuery/GridTags` 配置字段（可空、向后兼容）+ 4 个描述符 + `WidgetContentFactory` 注册 + 共享 `ItemGridWidget`（UserControl + `ItemGridWidgetViewModel`）。四格全部查询统一 `items` 表（`GetAllAsync`/`SearchAsync`/`GetRecentAsync`/`GetPinnedAsync`）；标签格/搜索结果格支持组件内配置并持久化到 `widgets.json`。 |
| A-3 待办/随记入 items | ✅ 已完成 | 新增 `ItemType.Todo`/`Note` + `source='local'`（`LocalItemState.EncodeSourceId` 多实例隔离）+ `LocalItemsMigration` 幂等迁移（`.bak` 备份、旧字段保留）。工程量 M、风险中已落地（见第十六轮）。 |
| A-4 QuickLaunch 复用 ItemCard + 搜索组件内直搜 | ✅ 已完成 | 复用 `ItemCard`（`IsLauncherMode` 隐藏会误写主库的操作）+ 组件内直搜（调 `SearchService` 前 12 条，点击才开主窗）。见第十七轮。 |
| Phase B 工程能力 | 🔶 进行中 | 胶囊模式（B-1 ✅）+ 每显示器拓扑布局（B-5 ✅）/ 性能模式 / 外观细化。 |
| Phase C 新内容组件 | ⬜ 待做 | Glance（推荐先做）/ 天气 / 音乐。 |

**验收**：`dotnet build` 0 错 0 警；`dotnet test` 173/173 通过。四格已可经「新建组件」入口添加并按模式查询统一条目库。

---

**文档结束。**
> DeskBox 功能结论均来自 `DeskBox-ref/` 源码（README v1.5.2 + 652 个 .cs 文件 + 关键文件通读），关键实现引用具体源文件路径。
> StarMarkDesktop 现状以 2026-09-18 提交及既有文档标记（⬜=未落地）为准。
