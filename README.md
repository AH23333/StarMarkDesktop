# StarMark

> 桌面端统一搜索 + 收藏中心 + 常驻工具台：把 GitHub Stars、浏览器书签、本地文件、剪贴板历史、RSS、热榜这些"分散在十几个地方的有用东西"汇聚到一个搜索框里，
> 并在桌面上常驻一组可拖拽、可吸附、可缩放的组件与一套截图 / 贴图 / 屏幕画布工具。
>
> 本地优先：数据全在本机 SQLite（FTS5 全文索引），不需要账号，不依赖云同步。

[![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/)
[![Windows App SDK](https://img.shields.io/badge/Windows%20App%20SDK-2.4-blue.svg)](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2019041%2B-lightgrey.svg)](https://learn.microsoft.com/windows/)
[![Version](https://img.shields.io/badge/version-1.0.1-green.svg)](https://github.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

---

## 目录

- [为什么做](#为什么做)
- [核心功能](#核心功能)
- [桌面组件](#桌面组件)
- [屏幕工具](#屏幕工具截图--贴图--画布)
- [架构总览](#架构总览)
- [集成源](#集成源)
- [快速开始](#快速开始)
- [配置](#配置)
- [项目结构](#项目结构)
- [构建 · 测试 · 发布](#构建--测试--发布)
- [自动更新链](#自动更新链)
- [文档](#文档)
- [明确不做的事](#明确不做的事)
- [License](#license)

---

## 为什么做

开发者每天会"发现"几十个有用的东西：一篇博客、一个 GitHub repo、一个 StackOverflow 回答、一段命令行、一个本地文档。传统做法各有缺口：

- **浏览器书签** — 几千条之后没人翻
- **GitHub Stars** — Star 了就忘
- **Ditto / ClipX** — 剪贴板只按时间排列
- **Everything** — 强，但只搜文件名，且结果不在任何一个库里
- **Notion / Obsidian** — 重，搜索体验割裂

**没有任何一个工具能把这些源统一成一个搜索框。** StarMark 就是为这件事而生：本地 SQLite + FTS5，跨源去重混排，标签与笔记统一管理，常驻托盘随时呼出。
在此之上又长出了第二层——一组常驻桌面的效率组件与一套截图 / 贴图 / 屏幕标注工具，因为"看到的东西"和"要用的东西"本来就不该分在两个软件里。

## 核心功能

- **跨源统一搜索** — 一个搜索框同时搜 GitHub Stars、Chrome/Edge 书签、本地文件（Everything 索引）、内置剪贴板历史、待办与随记。
  FTS5 全文索引 + bm25 相关度排序，CJK 按字切分，毫秒级响应；输入防抖合并，不会每敲一键跑一遍全链路。
- **来源与类型筛选** — 顶栏按来源（Star / 书签 / 文件）过滤，文件结果可按类型（文件夹 / 文档 / 图片 / 音乐 / 视频 / 压缩包 / 大文件 / 最近改动）多选筛选，
  并支持原生排序档（最近 / Star 最近推送 / 最近收藏 / Stars 数 / 名称）——**标签会说出它实际按什么排**。
- **标签 + 笔记统一** — 任何条目（不论来自哪个源）都能打标签、写笔记、置顶、隐藏；笔记与标签本身参与全文搜索。
  标签读路径分隔符用 `char(31)`，避免标签文字里的逗号把标签切碎。
- **本地文件全盘搜索** — 复用 Everything 的索引能力，只做**文件名**检索，分页拉取不截断。
  开启「本地磁盘搜索」这颗开关时才准备：优先用**随应用分发**的官方 standard Everything 便携版副本（发布包里预置则零联网可用），
  没有副本就从 voidtools 官网下载兜底，落到 `%LOCALAPPDATA%\StarMark\everything\`，SDK（`Everything64.dll`）同理；
  随后拉起自带实例并等 IPC 窗口就绪。**刻意不指向你机器上那只外部 Everything**——repack 版本对外部 IPC 的响应不可控。
- **剪贴板历史（文本 + 图片）** — 内置采集器独立消息窗口，默认关闭；三层敏感拦截；图片按声明的上限落盘并轮转，删除条目时尽力删文件；
  支持「复制图片」「贴到桌面」。与 Ditto 的关系是**并列而非替代**：装了 Ditto 就复用它的历史，没装就用内置这一套。
- **GitHub 热榜 / RSS** — 主窗两个独立页：热榜解析 GitHub Trending（可直接 Star / 取消 Star），RSS 支持文件夹 + 条目 + 收藏入库、每天一次增量拉取并落盘缓存。
- **AI 批量整理标签** — 两条通道：**本机 Ollama**（免 Key）或任意 **OpenAI 兼容端点**（基址填到 `/v1`，需自己的 API Key）；
  先出分组预览再一次性落库（单事务），停止键真能停且停后不再消耗 token；不会产出"一个标签只有 1 条"的碎标签。
- **备份 / 快照** — 数据导出导入（可选把剪贴板图片一起打包）、**自动备份开关 + 频率可设**；组件布局与数据可存成多套快照，随时提取 / 应用 / 删除。
- **自动更新** — 从 GitHub Releases 检测新版，下载清单 + 签名 + 载荷，逐文件校验后由独立更新器事务替换、失败自动回滚（详见[自动更新链](#自动更新链)）。
- **热键呼出 + 托盘常驻** — 全局快捷键可在设置页录制即时生效，冲突时**只提示占用方、不阻碍注册**；托盘菜单按组件注册表生成。

## 桌面组件

一组独立的无边框小窗，数据在 `%APPDATA%\StarMark\widgets.json`。**首启只预置时钟**，其余由用户自行添加。

| 组件 | 内容 |
|------|------|
| ★ 快捷启动 | 纯快捷入口（不含置顶区）：拖入文件 / 文件夹 / 网址 / 文本即入库，卡片右键「发送到桌面 · 快捷启动」，也可用 ＋ 表单手动添加 |
| 🕒 时钟 | 日期与秒级时钟（仅可见时计时）；内建**闹钟**一节（表面直接显示闹钟、就地加改、开关不写回快照），右键可直接设**护眼** |
| ✅ 待办 | 增 / 勾选 / 删除；全部·未完成·已完成分段筛选带计数、优先级**颜色标记**、**截止日期**、删除后行内撤销条、拖拽排序 |
| 📝 随记 | 随手记录，`Ctrl+Enter` 保存 |
| 🔍 快捷搜索 | 内联轻量增强（排序 + 键盘导航），回车唤起主窗并直接搜索 |
| 🏷️ 标签 | 把某个标签的结果常驻桌面 |
| 📋 剪贴板 | 内置剪贴板历史的展示型组件（含同步与条目右键） |
| 🕘 最近活动 | 绿 / 红 / 黄指示增删改（仅展示） |
| ⏫ 置顶条目 | 置顶内容常驻桌面 |
| 📅 今日速览 | 大号日期 + 农历（含闰月）+ 节日倒计时 + 常看条目，可切换月历；含热榜块 |
| 🌤️ 天气 | Open-Meteo（免 Key）：实况 + 未来三天 + 今日逐时；°C/°F 与 mph 同步换算，放大后追加降水概率 / 紫外线 / 气压 / 日出日落（三档尺寸带滞回） |
| 🎵 音乐 | 走 Windows SMTC，不对接任何具体播放器；可切音源、进度条 seek、随机 / 列表循环 |
| 🧮 计算器 | 表达式求值 / 单位换算 / 常用速算（换算率是内置静态表，断网可用；刻意不含汇率） |
| 🌍 世界时钟 | 只用 Windows 自带时区 Id；缺时区的极端环境逐行给原因，不静默少一行 |
| ⏳ 倒计时 | 纪念日 / 倒计时，存"墙上时钟 + 时区"而非 UTC 瞬间（每年重复项跨时区不会错开一天） |
| 🍅 番茄钟 | 纯状态机在 Core；**可缩放**——把倒计时拉大成"桌面时钟"用 |
| 📊 系统监控 | P/Invoke 读数；**只在有可见实例时采样**，性能模式降频，2s 滑动平均 |

通用能力：

- **添加 / 移除**：设置页「桌面组件」按类型折叠逐项开关；托盘子菜单、主窗顶栏 ▣ 菜单、组件标题栏 ＋ 三处同款。
- **隐藏 vs 移除**：标题栏「—」临时隐藏（实例可回收，托盘 / 设置一键恢复）；「✕」停用并从启用集合移除。
- **拖动 / 吸附 / 缩放 / 置顶**：边缘对齐贴合（两根可调滑杆：吸附强度与对齐间距），右下角拖拽缩放，图钉或双击标题栏切换置顶，状态按组件持久化。
  按住 Ctrl 拖动标题栏可**同屏组件一起挪**。
- **重命名 / 键盘**：右键「重命名…」或选中后 **F2**；Esc 收起为胶囊、Enter/Space 展开回原位。
- **数据实时同步**：数据层每次写入广播变更，所有组件去抖重载——主界面改了，组件不会还是老的。
- **布局与材质**：快捷键 / 托盘最后切到的布局自动存为默认；组件右键可命名保存多套布局，也可"快照成贴"。
  材质共六种（亚克力·薄 / 云母 / 实色 / 云母·Alt / 亚克力·厚 / 纯色），主窗与组件**各自独立设置**；
  浅色模式实色不再是黑底。组件内部滚动条一律**藏条不关滚**（照样能滚）。
- **开机自动加载组件**：这笔性能取舍由用户自己选，不替他决定桌面长什么样。

## 屏幕工具（截图 / 贴图 / 画布）

- **截图 + 标注编辑器**：Snipaste 式两阶段交互（框选阶段就能画、可越出选框），矩形 / 椭圆 / 直线 / 箭头 / 折线 / 画笔 / 荧光笔 / 马赛克 / 文字，
  画完的标注可再移动、缩放、旋转；文字点选即编辑、可旋转，拖字不会越拖越大；带标注的复制 · 存图 · 贴图 · 识字四条出口；
  可切换"是否把画布笔迹截进去"；工具条是独立置顶窗，摆在框外。
- **文字识别（OCR）**：用系统引擎，拼回规则处理换行，质量改善含补边 / 放大 / 对比拉伸 / 换语言重试；日志会记下当时按哪种语言认的。
- **贴图**：贴到桌面可继续就地编辑（复用截图那条标注链）、拖动 / 缩放 / 旋转且工具条跟着走，放大跑出屏幕会收边找得回来，支持成组显示 / 隐藏 / 穿透。
- **屏幕画布与荧光笔**：默认穿透、按住即画的三态状态机；9 个带修饰键的全局热键；图形编辑（矩形 / 椭圆 / 直线 / 箭头 / 折线）；
  白板底、幕布（压暗非焦点区域）、光标光晕、快捷键面板永远压在画布之上；画完的笔迹可贴出成图。
- **护眼 / 休息提醒**：暗幕与强制休息解耦，预览可提前跳过，提醒形式可选，时钟组件右键可就地设置并"试一试"。
- **闹钟**：住在时钟组件里，表面那排与右键共用同一份动作表。

## 架构总览

```
┌──────────────────────────────────────────────────────────────┐
│  StarMark.UI (WinUI 3，解包部署)                              │
│  主窗（搜索 / 文件夹 / 标签 / 动态 / 隐藏 / 快照 / 剪贴板 /    │
│        热榜 / RSS / 设置）· 17 种组件窗 · 截图贴图画布 · 托盘  │
└───────────────┬──────────────────────────────────────────────┘
                │ 判据能测的尽量下沉，UI 侧只留源码形状闸门
   ┌────────────┴──────────────────────────────────────────────┐
   │  StarMark.Core（纯逻辑，可单测）                            │
   │  SearchService · SyncCoordinator · 备份 · 洞察             │
   │  Widgets/（描述符注册表 · 存储 · 吸附 · 快照 · 各组件判据） │
   │  AlarmPolicy · CountdownPolicy · FocusTimer · WeatherCode  │
   │  Ocr/ · Capture/ · Trending/ · Feed/ · Ai/                │
   └────────────┬──────────────────────────────────────────────┘
   ┌────────────┴──────────────────────────────────────────────┐
   │  StarMark.Integrations（IItemSource 实现 + 原生探测）      │
   │  GitHubSource · Chrome/EdgeBookmarksSource · DittoSource   │
   │  EverythingSource（IPC + 查询队列）· SystemMetricsProbe    │
   └────────────┬──────────────────────────────────────────────┘
   ┌────────────┴──────────────────────────────────────────────┐
   │  StarMark.Data（SQLite + FTS5）                            │
   │  items · items_fts · tags · item_tags · notes · sync_state │
   │  两阶段查询（FTS5 命中 → JOIN 主表做数值过滤）· 迁移        │
   └────────────┬──────────────────────────────────────────────┘
                │ 所有层共享
   ┌────────────┴──────────────────────────────────────────────┐
   │  StarMark.Abstractions（无依赖，不碰磁盘 / 不碰网络）      │
   │  Item 模型 · IItemSource · LocalFileIdentity · UriNormalizer│
   │  DateTimeText · NumberText · TextTrim · ItemCardPolicy     │
   │  OpenFailure · UpdatePolicy · StarLog · UserDataPaths      │
   └───────────────────────────────────────────────────────────┘

   StarMark.Updater ── 独立小进程，负责同盘两次改名的事务替换与回滚
   tools/StarMark.UpdateSigner ── 发布侧签名工具（私钥不在仓里）
```

**关键设计：**

- **统一 Item 模型** — 所有源映射到同一张 `items`，按 `(source, source_id)` 唯一索引保证幂等；`ItemType` 与 `WidgetChromeMode` 的持久化序号**钉值**，加类型不会挪老数据。
- **两阶段查询** — FTS5 MATCH 走倒排（毫秒级）→ JOIN 主表做数值过滤；谓词下进 CTE，非默认排序档不再被相关度截断。
- **实时 + 持久化混排** — FTS5 命中与实时源（Everything）结果合并去重，兼顾速度与新鲜度。
- **判据下沉** — 能在纯函数上判的事一律放 Abstractions/Core 并配单测；UI 层因为不被测试工程引用，靠**源码形状闸门**守（见[构建 · 测试 · 发布](#构建--测试--发布)）。
- **一条链只许一个主人** — 显示哪一格路径只有 `LocalFileIdentity` 一颗，磁盘事实唯一出处是 `LauncherEx.ExistsOnDisk`（刻意留在 UI：判据本体不许碰磁盘），
  打开失败的坏消息只有一条出口 `ItemCardActions.OpenUriAndReport`。
- **失败必须说得出原因** — 设置写盘失败不再静默，"请稍后重试"换成就地按钮，打不开一行就写一行日志（种类 + id + 那句话）。

## 集成源

| 源 | 状态 | 协议 | 说明 |
|----|------|------|------|
| **GitHub Stars** | ✅ 完成 | GitHub REST API + PAT | 分页拉取 `/user/starred`，条件请求（ETag）+ 可取消 |
| **Everything 本地文件** | ✅ 完成 | SDK IPC（`Everything64.dll`）+ 查询队列 | 开关式准备：随包副本优先、否则官网下载便携版到自有目录；只搜文件名，分页不截断 |
| **Chrome / Edge 书签** | ✅ 完成 | 解析浏览器书签文件 | 按文件夹分组入"文件夹"页 |
| **Ditto 剪贴板** | ✅ 完成 | 直读 Ditto 的 SQLite | 装了才复用；未装走内置历史（默认关闭） |
| **内置剪贴板历史** | ✅ 完成 | 独立消息窗口采集 | 文本 + 图片，三层敏感拦截，明文存储（不接 DPAPI，已定案） |
| **GitHub 热榜** | ✅ 完成 | 解析 Trending HTML + API | 可直接 Star / 取消 Star |
| **RSS** | ✅ 完成 | 自带取器 + 落盘缓存 | 文件夹 + 条目，收藏入库 |
| **AI 整理** | ✅ 完成 | OpenAI 兼容接口 | 需自己的 API Key，先预览再落库 |
| **规则引擎** | ❌ 未实现 | — | 只有 `IRuleAction` 接口，没有任何实现与接线 |
| **Shell 右键菜单** | ❌ 未实现 | — | 目录占位，未进解决方案 |

## 快速开始

### 前置要求

- Windows 10 19041+ 或 Windows 11，**x64**
- **.NET 9 Desktop Runtime**（产物是框架依赖：`.NET` 要装在机器上，Windows App SDK 运行时随包带）
- .NET 9 SDK（自行构建时）
- （可选）GitHub Personal Access Token — 只读 starred 列表与发 Release / 检查更新用
- （可选）Ditto — 装了才复用它的剪贴板历史

### 1. 克隆并构建

```bash
git clone <repo-url>
cd StarMarkDesktop

# 必须显式指定 Platform=x64：Windows App SDK Self-Contained 要求显式架构，
# 默认 AnyCPU 会触发 "WindowsAppSDKSelfContained requires a supported Windows architecture"
dotnet build StarMark.sln -c Debug -p:Platform=x64
```

### 2. 配置 GitHub Token（可选）

设置页里 Token 那格是 **PasswordBox**（不回显、不落日志、不进备份）。也可以手工创建 `%APPDATA%\StarMark\github.json`：

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

> ⚠ `github.json` 存的是凭据，**不要提交、不要打包、不要贴进日志**。

### 3. 运行

```bash
# 方式一（推荐）：Visual Studio 打开 StarMark.sln，以 StarMark.UI 为启动项目按 F5
# 方式二：命令行
dotnet run --project src\StarMark.UI\StarMark.UI.csproj -p:Platform=x64
```

首次启动会创建 `%APPDATA%\StarMark\starmark.db`，并只在桌面预置一个时钟组件。

> **单实例**：命名互斥体 `Local\StarMark.Desktop.SingleInstance` 保证只跑一个实例；再次启动会把已在运行的主窗口唤到前台后退出。
> 所以调试前请先从托盘菜单「退出」旧实例，否则 F5 只是把旧窗口顶到前面。
>
> **托盘图标**：Windows 11 上首次运行可能落进任务栏右下角溢出区，可拖到常驻区。

## 配置

| 位置 | 内容 |
|------|------|
| `%APPDATA%\StarMark\starmark.db` | 主库（SQLite + FTS5） |
| `%APPDATA%\StarMark\settings.json` | 应用设置（材质、热键、开关、性能档位…） |
| `%APPDATA%\StarMark\widgets.json` | 组件启用集合、窗口几何、待办 / 随记 / 快捷入口（v2，含 v1 迁移） |
| `%APPDATA%\StarMark\github.json` | GitHub 凭据（**敏感，勿入库**） |
| `%LOCALAPPDATA%\StarMark\logs` | 运行日志——**报障时最有用的那一处证据** |
| `%LOCALAPPDATA%\StarMark\ClipAssets` | 剪贴板图片文件 |

覆盖路径的环境变量（开发 / 取证用）：`STARMARK_DB_PATH`、`STARMARK_SETTINGS_PATH`。

常用开关：开机自动启动、开机自动加载组件、本地磁盘搜索、剪贴板历史（默认关）、截图功能总开关、屏幕画布总开关、
自动备份 + 间隔、更新检测与「立即更新」、性能档位、材质与主题、护眼与休息提醒。

## 项目结构

```
StarMark/
├── Directory.Build.props              # 全仓唯一版本号出处（改版本只改这一行）
├── docs/                              # 设计文档、蓝图、进度报告、待决策账本、踩坑记录
├── publish.ps1                        # 本地产物（内部调用 scripts\publish-tree.ps1）
├── scripts/
│   ├── publish-tree.ps1               # 产出"这一版的那棵树"：主程序 + 更新器，同一目录
│   ├── release-package.ps1            # 打发布三颗：载荷 zip + update-manifest.json + .sig
│   ├── publish-release.ps1            # 把三颗挂成 GitHub Release（先对账再公开）
│   ├── mem-probe.ps1                  # 内存取证
│   └── gc-ab-probe.ps1                # ServerGC A/B 取证
├── src/
│   ├── StarMark.Abstractions/         # 无依赖判据与模型（不碰磁盘、不碰网络）
│   ├── StarMark.Core/                 # 应用服务 + 组件纯逻辑（可单测）
│   ├── StarMark.Data/                 # SQLite + FTS5、仓储、迁移、连接工厂
│   ├── StarMark.Integrations/         # IItemSource 实现与原生探测
│   ├── StarMark.UI/                   # WinUI 3 主窗 / 组件窗 / 截图贴图画布 / 托盘
│   └── StarMark.Updater/              # 独立更新器小进程
├── tools/StarMark.UpdateSigner/       # 发布侧清单签名工具（私钥在仓外）
├── tests/
│   ├── StarMark.Tests/                # 主力测试工程（含源码形状闸门）
│   ├── StarMark.SmokeTest/            # 端到端验证控制台：smoke / query / sync
│   └── ...                            # Core/Data/Integrations 的分层测试
└── StarMark.sln
```

## 构建 · 测试 · 发布

```bash
# 全量构建（必须 x64）
dotnet build StarMark.sln -c Debug -p:Platform=x64

# 全量测试
dotnet test tests\StarMark.Tests\StarMark.Tests.csproj -c Debug -p:Platform=x64
```

- **测试工程不引用 `StarMark.UI`** ⇒ UI 侧的行为无法直接断言，这一半靠**源码形状闸门**守：
  测试从磁盘读源文件、抹掉注释后检查形状（某颗判据是否只有一处、某个已删的出口有没有回来、某段文字是否只在一个出处）。
  代价是**改了被闸门读的那份文件就得重编重量**，收益是"同一个事实写两遍"这类退化会被当场拦下。
- 每批收口都登记基线：`-t:Rebuild` 的警告数 + 全量测试通过数。
- 发布（按顺序，三颗资产缺一颗那条链就少一步）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-tree.ps1 -OutDir <绝对路径>
powershell -ExecutionPolicy Bypass -File scripts\release-package.ps1          # → dist\ 三颗
powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1 -Tag v1.0.2
```

  本机自测产物用 `publish.ps1`（它复用同一棵树的产出逻辑，避免"本机那份带更新器、发出去的那份没带"）。
  签名私钥在 `%USERPROFILE%\.starmark\update-signing.pk8.pem`，**不入仓、不打印、不打包**。

## 自动更新链

装好之后更新不需要重装：设置页「关于与更新」里点「立即更新」即可。

1. **探测** — 只读 GitHub Releases 最新标签，回答"有没有新版"，不下载、不改本机文件。
2. **审包** — 下载清单 + 签名，用内置公钥验 ECDSA P-256 签名，逐文件核对 SHA-256；下载 URL 由固定模板拼出（**刻意不读服务端给的地址**），
   https 宿主逐跳重验、路径越界拒绝。
3. **替换** — 由独立更新器进程做**同盘两次改名**的事务替换；任一步失败自动退回原来那一版。
4. 检测与替换是两件事：关掉自动检测不会有任何东西被改动，只有点「立即更新」才真的下载并替换。

## 文档

- [项目功能可行性分析](docs/项目功能可行性分析.md) — 产品设想与可行性
- [项目开发技术文档](docs/项目开发技术文档.md) — 架构、数据模型、开发路线
- [开发进度报告](docs/开发进度报告.md) — 逐批工作记录（每批署名、含真机点验单与遗留问题）
- [待决策事项](docs/待决策事项.md) — 待决策账本：开放项、已裁决、等真机三类分开计数
- [踩坑记录](docs/踩坑记录.md) — 构建 / 渲染 / 数据 / Win32 / 快捷键 / 性能 / 协作踩坑全集
- [本地磁盘搜索-实施蓝图](docs/本地磁盘搜索-实施蓝图.md) · [GitHub热榜-实施蓝图](docs/GitHub热榜-实施蓝图.md) ·
  [剪贴板图片历史实施方案](docs/剪贴板图片历史实施方案.md) · [屏幕标注系统整合架构方案](docs/屏幕标注系统整合架构方案.md)
- [DeskBox 功能对比与组件拓展分析](docs/DeskBox功能对比与组件拓展分析.md) ·
  [双库一致性审计](docs/双库一致性审计.md)（与浏览器扩展版的一致性）

## 明确不做的事

这几条不是遗漏，是**已定案**，翻案要先改账本再改码：

- **本地文件内容搜索** — 工作量剧增且不在产品范围内（用户裁决，✅P-157）。Everything 那一侧只做文件名检索，"索引进库"这条能力已按裁决整条拆除。
- **为连接外部进程而自我提权** — 永不做。提权只会让拖放与 IPC 更难对齐，程序改为显性化当前权限状态并给就地出口。
- **跨设备云同步** — 本地优先是产品前提；可选路线是基于 Git / WebDAV 的备份搬运，不是实时同步。
- **给剪贴板明文存储接 DPAPI** — 已定案不接（P-61）。
- **汇率换算** — 计算器只带内置静态换算表，断网可用。

## License

MIT
