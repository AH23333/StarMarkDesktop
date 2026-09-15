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
- **热键呼出** — 全局热键（Phase 2）随时呼出搜索窗口，类似 Spotlight / PowerToys Run。
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

```bash
# 方式一：直接启动 exe
.\src\StarMark.UI\bin\x64\Debug\net9.0-windows10.0.19041.0\StarMark.UI.exe

# 方式二：dotnet run
dotnet run --project src\StarMark.UI\StarMark.UI.csproj -p:Platform=x64
```

首次启动会：
1. 在 `%APPDATA%\StarMark\starmark.db` 创建 SQLite schema
2. 写入种子数据（用于开发期验证）
3. 显示主窗口，搜索框可立即使用

点击顶栏 **"同步"** 按钮触发 GitHub Stars 拉取（需先配置 Token）。

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
│   │   └── Sync/SyncCoordinator.cs    # 同步协调器
│   └── StarMark.UI/                   # WinUI 3 桌面端
│       ├── App.xaml(.cs)              # DI 容器 + 迁移 + 种子数据
│       ├── MainWindow.xaml(.cs)       # 主窗口（搜索框/工具栏/卡片）
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

## 开发路线

### Phase 1（MVP，进行中）

- [x] 解决方案与项目骨架
- [x] SQLite + FTS5 数据模型与两阶段查询
- [x] Everything 适配器骨架（CLI 实现）
- [x] **GitHub Stars 同步**（本迭代完成）
- [x] **SyncCoordinator + 同步按钮接线**（本迭代完成）
- [x] WinUI 3 主窗口（搜索框 + 工具栏 + 卡片列表）
- [ ] 托盘常驻 + 全局热键呼出
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
