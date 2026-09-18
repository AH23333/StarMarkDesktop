# StarMarkDesktop

> 桌面端统一搜索 + 收藏中心：把 GitHub Stars、浏览器书签、本地文件、剪贴板、标签化管理器等"分散在十几个地方的有用东西"汇聚到一个搜索框里。
>
> 浏览器扩展版 [StarMark](#) 的功能增强重写版，定位是"装一次、长期常驻系统、跨源统一搜索"的本地工具。

[![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/)
[![Windows App SDK](https://img.shields.io/badge/Windows%20App%20SDK-2.4-blue.svg)](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-lightgrey.svg)](https://learn.microsoft.com/windows/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

---

## 目录

- [为什么做](#为什么做)
- [核心功能](#核心功能)
- [架构总览](#架构总览)
- [快速开始](#快速开始)
- [配置](#配置)
- [项目结构](#项目结构)
- [构建与运行](#构建与运行)
- [集成源](#集成源)
- [文档](#文档)
- [开发路线](#开发路线)
- [License](#license)

---

## 为什么做

开发者每天会"发现"几十个有用的东西：一篇博客、一个 GitHub repo、一个 StackOverflow 回答、一段命令行、一个本地文档。传统做法是：

- **浏览器书签** — 几千条之后没人翻
- **GitHub Stars** — Star 了就忘
- **Ditto / ClipX** — 剪贴板只按时间排列
- **TagSpaces / Eagle** — 文件资源管理器风格，搜索要打开软件
- **Everything** — 强，但只能搜文件名
- **Flow Launcher / Wox** — 启动器风格，索引会丢
- **Notion / Obsidian** — 重，搜索体验割裂

**没有任何一个工具能把上面这些源统一成一个搜索框。** StarMarkDesktop 就是为这件事而生：本地 SQLite + FTS5 全文索引，跨源去重混排，标签和笔记统一管理，常驻系统托盘随时呼出。

## 核心功能

- **跨源统一搜索** — 一个搜索框，同时搜本地文件、GitHub Stars、浏览器书签、剪贴板历史。FTS5 全文索引 + bm25 排序，毫秒级响应。
- **本地优先** — 所有数据存在本地 SQLite，不依赖云同步，不需要账号。GitHub PAT 仅用于拉取你自己的 starred 列表。
- **标签 + 笔记统一** — 给任何条目（不论来自哪个源）打标签、写笔记。笔记本身参与全文搜索。
- **热键呼出** — 全局热键 `Ctrl+Alt+Space` 随时呼出主窗口，类似 Spotlight / PowerToys Run；常驻系统托盘。
- **桌面小组件** — 对标 DeskBox 的独立无边框组件：★ 快捷启动（置顶条目 + 自定义入口，支持拖入文件/网址、从条目卡片发送）、🕒 时钟、✅ 待办、📝 随记、🔍 快捷搜索。设置页/托盘自由增减，窗口可拖动、边缘吸附、缩放、置顶，位置与状态持久化。
- **集成现有工具** — 复用 Everything 的索引能力、复用 Ditto 的剪贴板历史、复用 TagSpaces 的标签库，不重新发明轮子。
- **规则引擎** — 自动给新条目打标签、自动归档（Phase 3）。例如"所有 Rust repo 自动加 `rust` 标签"。

## 架构总览

```
┌───────────────────────────────────────────────────────────┐
│                       StarMark.UI (WinUI 3)               │
│   ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│   │ SearchBox│  │ Toolbar  │  │Card List │  │ Tray/Hotk│  │
│   └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘  │
│        │              │SyncBtn       │              │        │
└────────┼──────────────┼──────────────┼──────────────┼──────┘
         │              │              │              │
         ▼              ▼              ▼              ▼
   ┌─────────────────────────────────────────────────────┐
   │  StarMark.Core (Application Services)              │
   │  ├─ SearchService     (跨源搜索编排 + 去重 + 截断)    │
   │  └─ SyncCoordinator   (遍历 IItemSource → Upsert)   │
   └─────────────┬───────────────────────────────────────┘
                 │
   ┌─────────────┴───────────────────────────────────────┐
   │  StarMark.Integrations (IItemSource 实现)            │
   │  ├─ GitHubSource       (GitHub REST API, PAT)        │
   │  ├─ EverythingSource   (Everything CLI/IPC)          │
   │  ├─ DittoSource        (Ditto SQLite 直读, Phase 2) │
   │  ├─ BookmarkSource     (Chrome/Edge JSON, Phase 2)  │
   │  └─ ...                                              │
   └─────────────┬───────────────────────────────────────┘
                 │
   ┌─────────────┴───────────────────────────────────────┐
   │  StarMark.Data (SQLite + FTS5)                      │
   │  ┌──────────┐  ┌──────────┐  ┌──────────┐           │
   │  │ items    │  │items_fts │  │ tags     │            │
   │  │ (主表)   │◄─┤ (FTS5)   │  │ item_tags│           │
   │  └──────────┘  └──────────┘  └──────────┘           │
   │  ┌──────────┐  ┌──────────┐  ┌──────────┐           │
   │  │ notes    │  │ rules    │  │ sync_state│          │
   │  └──────────┘  └──────────┘  └──────────┘           │
   └─────────────────────────────────────────────────────┘
```

**关键设计：**

- **两阶段查询** — FTS5 MATCH 命中倒排索引（毫秒级）→ JOIN items 主表做数值过滤（stars 数、日期范围）。避免 FTS5 不擅长的数值比较。
- **统一 Item 模型** — 所有源的数据都映射到同一张 `items` 表，按 `(source, source_id)` 唯一索引保证幂等。
- **本地 + 实时混排** — FTS5 命中持久化数据 + 实时源（如 Everything）查询结果合并去重，兼顾速度与新鲜度。

## 快速开始

### 前置要求

- Windows 10 19041+ 或 Windows 11
- .NET 9 SDK
- （可选）[Everything 1.4+](https://www.voidtools.com/) — 用于本地文件搜索集成
- （可选）GitHub Personal Access Token — 用于 GitHub Stars 同步

### 1. 克隆仓库

```bash
git clone <repo-url>
cd StarMarkDesktop
```

### 2. 构建

```bash
# 必须显式指定 Platform=x64（Windows App SDK Self-Contained 模式要求）
dotnet build src\StarMark.UI\StarMark.UI.csproj -p:Platform=x64
```

### 3. 配置 GitHub Token（可选）

创建 `%APPDATA%\StarMark\github.json`：

```json
{
  "Token": "ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "Username": "your-github-username",
  "SyncIntervalSeconds": 3600
}
```

或用环境变量（开发期推荐）：

```powershell
$env:STARMARK_GITHUB_TOKEN = "ghp_xxx"
$env:STARMARK_GITHUB_USERNAME = "your-name"
```

Token 仅需 `public_repo` 或 `read:user` 范围（只读 starred 列表，无写权限）。

### 4. 运行

推荐通过 IDE 或 dotnet CLI 启动（会自动完成构建）：

```bash
# 方式一（推荐）：Visual Studio 中打开 StarMark.sln，选择 StarMark.UI 为启动项目后 F5
# 方式二：命令行
dotnet run --project src\StarMark.UI\StarMark.UI.csproj -p:Platform=x64
```

直接双击输出目录的 `src\StarMark.UI\bin\x64\Debug\net9.0-windows10.0.19041.0\StarMark.UI.exe`
也能运行（解包自包含部署，不依赖已安装的 Windows App Runtime），但它只是**构建产物**，
不是规范的开发入口：改代码后需要重新生成，且不会附加调试器。仅用于快速手工验证。

> **单实例**：应用通过命名互斥体 `Local\StarMark.Desktop.SingleInstance` 保证只运行一个实例；
> 再次启动会自动唤起已在运行的主窗口（含最小化到托盘的情况）后退出新进程。
> 因此调试时如果已有实例在运行（包括你手动双击启动的），F5 只会把旧窗口唤到前台——
> 调试前请先从托盘菜单「退出」旧实例。
>
> **托盘图标**：Windows 11 上首次运行图标可能进入任务栏右下角的溢出区，
> 可将其拖拽到任务栏常驻区。

首次启动会：
1. 在 `%APPDATA%\StarMark\starmark.db` 创建 SQLite schema
2. 写入种子数据（用于开发期验证）
3. 显示主窗口，搜索框可立即使用（全新安装默认不显示任何桌面组件，由用户自行添加）

点击顶栏 **"同步"** 按钮触发 GitHub Stars 拉取（需先配置 Token）。

## 桌面组件

桌面组件是一组独立的无边框小窗（对标 [DeskBox](https://github.com/Tianyu199509/DeskBox) 的 Widgets 模型），
数据保存在 `%APPDATA%\StarMark\widgets.json`（与 settings.json 同目录，可用 `STARMARK_SETTINGS_PATH` / `STARMARK_DB_PATH` 改目录）。

| 组件 | 内容 |
|------|------|
| ★ 快捷启动 | 数据库中已**置顶**的条目（最多 8 条）＋用户自定义入口；可把文件、文件夹、网址、文本直接**拖入**窗口，也可在条目卡片右键「发送到桌面 · 快捷启动」，或用窗口内 ＋ 表单手动添加 |
| 🕒 时钟 | 日期与秒级时钟（仅可见时计时） |
| ✅ 待办 | 增、勾选完成、删除；未完成在前 |
| 📝 随记 | 随手记录，`Ctrl+Enter` 保存 |
| 🔍 快捷搜索 | 输入回车后唤起主窗口并直接搜索 |
| 🏷️ 标签格 / 📌 搜索结果格 / 🕘 最近活动格 / ⏫ 置顶条目格 | **差异化条目格**（特性组件，默认关闭、需在设置页开启）：把某个标签、某条查询、最近更新或置顶条目常驻桌面 |
| 📅 今日速览 | **Phase C 首个新内容组件**：大号日期 + 农历（含闰月）+ 今日/下一个节日倒计时 + 常看条目。农历与清明节算法照搬 DeskBox 的 `GlanceFestivalService`（底层用 .NET 内置 `ChineseLunisolarCalendar`） |

通用能力：

- **添加/移除**：设置页「桌面组件」卡片逐组件开关；托盘右键「桌面组件」子菜单逐项勾选；主窗口顶栏组件按钮（▣ 图标）同款菜单；组件标题栏 ＋ 也可管理。
- **隐藏 vs 移除**：标题栏「—」是临时隐藏（实例保活，托盘/设置可一键恢复）；「✕」是停用并从启用集合移除。
- **拖动 / 吸附**：按住标题栏拖动，组件之间会边缘对齐/贴合（留 8px 间距），靠近屏幕边缘也会吸附；阈值 24px、垂直投影需重叠，避免远处窗口乱吸。
- **缩放 / 置顶**：右下角拖拽调整大小（含时钟，字号随窗口尺寸自适应）；图钉按钮或双击标题栏切换置顶，置顶状态按组件持久化。
- **重命名**：组件右键「重命名…」可给单个组件起名字，写回 `widgets.json` 持久化；标题栏与胶囊标题优先显示自定义名，留空即恢复组件类型默认名。
- **布局持久化与材质**：最后通过快捷键 / 托盘切换到的布局自动存为默认布局，隐藏后再显示或下次启动都恢复到该布局；组件右键可命名保存多套布局。材质照搬 DeskBox，共**六种**（设置页「外观 · 半透明材质」）：亚克力·薄 / 云母 / 实色 / 云母·Alt / 亚克力·厚 / **纯色·Solid**。前四种走 `DesktopAcrylicController` / `MicaController` 原生控制器（内容背景透明让霜化透出）；纯色与实色不挂控制器，由内容表面铺实色，其中**纯色按「背景不透明度」调 Alpha 并带强调色倾向**。材质会应用到主窗口所有界面区域（文件夹 / 标签 / 搜索 / 动态 / 隐藏 / 设置及右键菜单）。
- 全新安装默认不显示任何组件；旧版单面板若开启了「开机显示」，升级后自动迁移为启用全部五种组件。

## 配置

### 数据库路径

默认 `%APPDATA%\StarMark\starmark.db`。开发期可用环境变量覆盖：

```powershell
$env:STARMARK_DB_PATH = "D:\your\path\starmark.db"
```

### GitHub 同步

| 配置项 | 文件键 | 环境变量 | 默认值 |
|--------|--------|---------|--------|
| PAT Token | `Token` | `STARMARK_GITHUB_TOKEN` | （空，不同步） |
| 用户名 | `Username` | `STARMARK_GITHUB_USERNAME` | （空） |
| 同步间隔 | `SyncIntervalSeconds` | — | `3600`（1 小时） |
| 每页数 | `PageSize` | — | `100`（GitHub API max） |

### Everything 集成

无需配置。只要 Everything 1.4+ 已安装并运行，`EverythingSource` 自动通过 CLI 探测并查询。

## 项目结构

```
StarMarkDesktop/
├── docs/                              # 设计文档与进度报告
│   ├── 项目功能可行性分析.md          # 产品设想与可行性
│   ├── 项目开发技术文档.md            # 架构 / 数据模型 / 开发路线
│   └── 开发进度报告.md                # 当前迭代的工作记录
├── src/
│   ├── StarMark.Abstractions/         # 核心抽象（无依赖）
│   │   ├── ItemModel.cs               # Item / GitHubStarMeta / BookmarkMeta
│   │   ├── IItemRepository.cs         # 仓储接口 + SearchFilter + SearchResult
│   │   ├── IItemSource.cs             # 统一源接口
│   │   ├── IViewer.cs                 # 查看器接口（Phase 2）
│   │   └── IRuleAction.cs             # 规则引擎接口（Phase 3）
│   ├── StarMark.Data/                 # 数据访问层
│   │   ├── Schema.sql                 # SQLite + FTS5 schema
│   │   ├── DbConnectionFactory.cs     # 连接工厂
│   │   ├── MigrationRunner.cs         # Schema 迁移
│   │   └── ItemRepository.cs          # 两阶段查询 + 标签/笔记 CRUD
│   ├── StarMark.Integrations/         # 集成适配层
│   │   ├── Everything/                # Everything 1.4 适配器
│   │   │   ├── EverythingInterop.cs   # P/Invoke + CLI
│   │   │   ├── EverythingQueryQueue.cs# 查询队列 + 线程安全
│   │   │   └── EverythingSource.cs    # IItemSource 实现
│   │   └── GitHub/                    # GitHub Stars 适配器
│   │       ├── GitHubOptions.cs       # 配置（PAT / 同步间隔）
│   │       ├── GitHubClient.cs        # GitHub REST API 客户端
│   │       └── GitHubSource.cs        # IItemSource 实现
│   ├── StarMark.Core/                 # 应用服务层
│   │   ├── Search/SearchService.cs    # 跨源搜索编排 + 去重
│   │   ├── Sync/SyncCoordinator.cs    # 同步协调器
│   │   └── Widgets/                   # 桌面组件纯逻辑（无 UI 依赖，可单测）
│   │       ├── WidgetStorage.cs       # widgets.json v2：启用集合/窗口配置/待办/随记/入口 + v1 迁移
│   │       ├── WidgetSnapCalculator.cs # 边缘吸附算法（移植 DeskBox，含 sticky 迟滞）
│   │       ├── WidgetDescriptor.cs    # 组件类型描述符 + 注册表（唯一事实来源）
│   │       └── WidgetZOrderPolicy.cs  # 空闲期 Z 序策略（纯函数）
│   └── StarMark.UI/                   # WinUI 3 桌面端
│       ├── App.xaml(.cs)              # DI 容器 + 迁移 + 种子数据 + 单实例
│       ├── MainWindow.xaml(.cs)       # 主窗口（搜索框/工具栏/卡片/托盘入口）
│       ├── Services/WidgetManager.cs  # 组件窗口生命周期（启用/显隐/吸附/布局套用/默认布局）
│       ├── Services/HotkeyService.cs  # 全局快捷键录制/执行（含录制期挂起恢复）
│       ├── Views/WidgetWindow.xaml(.cs) # 单个组件的无边框亚克力窗口
│       ├── Helpers/WindowInterop.cs   # 无边框/置顶/圆角/工作区/拖动所需 Win32
│       ├── Helpers/Hotkey.cs          # 组合键模型与可读名称（OEM 标点键字面量映射）
│       ├── Helpers/WidgetAppearance.cs # 亚克力/Mica 控制器方案（DeskBox 式）
│       ├── Helpers/CenteredDialog.cs  # 全屏居中顶层保存布局命名弹窗
│       ├── Controls/ColumnFlowPanel.cs # 设置页两列自适应布局面板
│       ├── SeedData.cs                # 首次启动种子数据
│       ├── Themes/StarMarkTheme.xaml  # 主题色板（移植自浏览器扩展）
│       └── app.manifest               # DPI / Windows 版本声明
├── tests/
│   └── StarMark.SmokeTest/            # 端到端验证控制台
│       ├── Program.cs                 # 三模式：smoke / query / sync
│       └── Mocks/MockGitHubSource.cs  # GitHub 同步 mock
└── StarMark.sln                       # 解决方案文件
```

## 构建与运行

### 构建 UI

```bash
dotnet build src\StarMark.UI\StarMark.UI.csproj -p:Platform=x64
```

**注意：必须传 `-p:Platform=x64`**。Windows App SDK Self-Contained 模式要求显式指定架构，`dotnet build` 默认 `AnyCPU` 会触发 `WindowsAppSDKSelfContained requires a supported Windows architecture` 错误。

### 运行 SmokeTest（端到端验证）

```bash
# 全新临时 DB，跑 schema + seed + 搜索自检
dotnet run --project tests\StarMark.SmokeTest\StarMark.SmokeTest.csproj -p:Platform=x64

# 用 mock 验证 SyncCoordinator + GitHubSource 全链路
dotnet run --project tests\StarMark.SmokeTest\StarMark.SmokeTest.csproj -p:Platform=x64 -- sync

# 查询已存在的应用 DB（不写入，排查工具）
dotnet run --project tests\StarMark.SmokeTest\StarMark.SmokeTest.csproj -p:Platform=x64 -- query "%APPDATA%\StarMark\starmark.db" rag
```

### 发布 Release

```bash
dotnet publish src\StarMark.UI\StarMark.UI.csproj -c Release -p:Platform=x64
# 输出在 bin\x64\Release\net9.0-windows10.0.19041.0\publish\
```

## 集成源

| 源 | 状态 | 协议 | 说明 |
|----|------|------|------|
| **GitHub Stars** | ✅ MVP 完成 | GitHub REST API + PAT | 分页拉取 `/user/starred`，全量 upsert |
| **Everything** | 🚧 骨架 | CLI 子进程 / WM_COPYDATA IPC | 1.4 CLI 实现已可用，完整 IPC 待 Phase 1 收尾 |
| **Ditto 剪贴板** | ❌ Phase 2 | SQLite 直读 | 直读 Ditto 的 `clipdiary.dt` 数据库 |
| **浏览器书签** | ❌ Phase 2 | JSON 解析 | Chrome / Edge 书签文件解析 |
| **TagSpaces** | ❌ Phase 2 | 文件系统 + JSON sidecar | 复用 TagSpaces 的标签库 |
| **QuickLook** | ❌ Phase 2 | IPC 插件 | 空格键预览选中条目 |
| **Flow Launcher** | ❌ Phase 2 | 插件协议 | 作为 Flow Launcher 插件被调用 |

## 文档

- [项目功能可行性分析](docs/项目功能可行性分析.md) — 产品设想与可行性分析
- [项目开发技术文档](docs/项目开发技术文档.md) — 架构、数据模型、开发路线
- [开发进度报告](docs/开发进度报告.md) — 当前迭代工作记录与遗留问题
- [踩坑记录](docs/踩坑记录.md) — 构建/渲染/数据/Win32/快捷键/性能等踩坑全集

## 开发路线

### Phase 1（MVP，进行中）

- [x] 解决方案与项目骨架
- [x] SQLite + FTS5 数据模型与两阶段查询
- [x] Everything 适配器骨架（CLI 实现）
- [x] **GitHub Stars 同步**（本迭代完成）
- [x] **SyncCoordinator + 同步按钮接线**（本迭代完成）
- [x] WinUI 3 主窗口（搜索框 + 工具栏 + 卡片列表）
- [x] 托盘常驻 + 全局热键呼出（设置页即时生效）
- [x] DeskBox 式桌面组件（五种独立窗口：增减/吸附/置顶/缩放/快捷入口）
- [x] 单实例（重复启动唤起已有窗口）
- [ ] 接通 Everything 真实查询（CLI 路径已可用，需测试）

### Phase 2（集成扩展）

- [ ] Ditto 剪贴板集成（直读 SQLite）
- [ ] 浏览器书签导入（Chrome / Edge）
- [ ] TagSpaces 标签库复用
- [ ] QuickLook 预览集成
- [ ] Shell 右键菜单（文件资源管理器"加入 StarMark"）
- [ ] 增量同步（基于 `sync_state.last_synced_at`）

### Phase 3（智能化）

- [ ] 规则引擎（自动打标签、自动归档）
- [ ] AI 摘要（接入本地 LLM，给条目生成摘要）
- [ ] 跨设备同步（可选，基于 Git 仓库或 WebDAV）

## License

MIT
