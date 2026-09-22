# 开发交接 · 本地文件检索（批次 FL）

> 生成时间：2026-09-21 18:29 (+0800) · 状态：**代码已落地并编译通过，未提交、未真机验证**（应用户要求暂停）。
> HEAD 参照：`9e07114`（批次 EW）。本轮全部改动仍在工作树，尚未 `git commit`。

---

## 1. 本轮目标转向的由来

- 上一阶段（批次 EX 计划）本欲补 `stars>500` 查询 DSL 解析器。**用户明确否决**：
  > "不需要添加类似 stars>500 的查询条件，当前 star 搜索功能已完备，需要完成的是本地文件检索。"
- 因此 EX（stars 解析）计划**作废**，未写任何相关代码。本轮改为「完成本地文件检索」。

## 2. 现状核实（读码结论，全部 `[已核实]`）

本地文件检索的**后端链路其实已经全部建好**，只是**没有 UI 开关、用户无法开启**：

- 实时源 `EverythingSource : IItemSource`
  - `src/StarMark.Integrations/Everything/EverythingQueryQueue.cs:95`
  - `IsAvailable => _options.Enabled && EverythingInterop.IsRunning()`（`:114`）——**每次查询实时读** `_options.Enabled`。
  - `SearchAsync`（`:146`）真异步经 `EverythingQueryQueue`；`FetchAsync`（`:121`）按配置的根目录把文件索引进库为 `ItemType.File`。
  - `EnsureReadyAsync`（`:210`）：下载 Everything SDK DLL →（主程序未运行时）静默安装 Everything。
- DI 已注册为实时源，故 `SearchService` 会把本地文件并入统一搜索：
  - `src/StarMark.UI/App.xaml.cs:113-117`（`AddSingleton<IItemSource>(... EverythingSource)`）。
- 开关配置链（P0-1b）：
  - `FileIndexOptions.Enabled` 于建库时一次性读 `SettingsStore.LoadLocalDiskSearchEnabled()`（`App.xaml.cs:110`），**默认 false**。
  - 启动 `EnsureReadyAsync` 也只在 `LoadLocalDiskSearchEnabled()` 为真时跑（`App.xaml.cs:168`）。
- **断点根因**：`SettingsStore.SaveLocalDiskSearchEnabled`（`src/StarMark.UI/Helpers/SettingsStore.cs:399`）**在 UI 层零调用者** → 该标志永远无法被翻成 true → 整条本地文件搜索功能对用户不可达。
- 待决策 **P-2** 早已登记此缺口（`docs/待决策事项.md:30-32`）：① 设置页无开关；② `FileIndexOptions.Enabled` 建库期快照、运行期改配置不热更新。选项 A（加开关+提示重启）/ B（即时生效联动起停）/ C（服务侧按开关启停）。

## 3. 本轮已做（批次 FL · 采纳 P-2 方案 B「即时生效」）

**改动 3 个文件，全部在 `StarMark.UI`（非可机检层，见 §5）：**

### 3.1 `src/StarMark.UI/ViewModels/SettingsPageViewModel.cs`
- 新增两个 `[ObservableProperty]`：`LocalDiskSearchEnabled`（bool）、`LocalDiskSearchStatus`（string），及私有 `_suppressLocalDiskApply`（回灌初值时抑制副作用）。
- 新增 `partial void OnLocalDiskSearchEnabledChanged(bool value)`：
  - `_settings.SaveLocalDiskSearchEnabled(value)` 落盘；
  - `App.Services.GetRequiredService<FileIndexOptions>().Enabled = value` **实时翻位单例**（无需重启，因 `IsAvailable` 实读）；
  - 开启时后台 `Task.Run(() => EverythingSource.EnsureReadyAsync())` 懒起准备 Everything；`DispatcherQueue.TryEnqueue` 回填状态文本；
  - 关闭时仅置 `Enabled=false`（不动已装 Everything）；
  - 全程 try/catch + `StarLog` 降级。
- `LoadFromStore` 末尾：`_suppressLocalDiskApply` 包裹下回灌 `LocalDiskSearchEnabled` + 初始状态文本。
- `Save()`：保存 roots/上限后，把它们**即时推回** `FileIndexOptions` 单例（`fo.Roots = LoadFileIndexRoots().ToList(); fo.MaxCount = LoadMaxFileIndexCount();`），使后台重扫无需重启。

### 3.2 `src/StarMark.UI/Views/SettingsPage.xaml`
- 「本地文件索引」卡片标题改为「本地磁盘搜索」，顶部新增 `ToggleSwitch`（`IsOn="{x:Bind ViewModel.LocalDiskSearchEnabled, Mode=TwoWay}"`）+ 状态 `TextBlock`（OneWay 绑 `LocalDiskSearchStatus`）；原 roots/上限控件保留，并加一句说明区分「全盘实时搜索」与「索引进库目录」。

### 3.3 `docs/待决策事项.md`（上一轮遗留、**仍未提交**）
- 对 **P-35** 的过度声明做了两处更正：收窄影响面为「乱码路径仅影响显示/打开目标，不污染 `source_id`」（`DittoSource.cs:60` 的 `SourceId = $"ditto:{clip.Id}"` 证伪了旧的「source_id 被污染」说法）+ 修正 P-35 标题。

## 4. 验证状态

- ✅ **编译**：`dotnet build StarMark.sln -c Debug`（x64）→ **0 警告 0 错误**。
- ⬜ **单元/契约测试**：**本轮未跑**。FL 只改 `StarMark.UI`，而 `tests/StarMark.Tests` 仅覆盖 Abstractions/Data/Core/Integrations，**不含 UI** → 测试基线理论上不受影响（HEAD 基线 718/718，见记忆）。提交前应跑一次 x64 全量作检查点（按「降低全量测试频率」，仅在提交点跑）。
- ⬜ **真机功能验证 `[v]`（沙箱不可做）**：开关翻转、Everything 下载/安装/UAC、全盘搜出本地文件、关闭回落——**均需用户真机构建运行验证**。
- ⬜ **未提交**：三处改动仍在工作树（`git status` 见 §6）。

## 5. 为什么这属于 `[v]` 而未走「四闸门自主落地」

四闸门要求改动可机检（Core/Data/Abstractions/Integrations）。本轮是**用户显式指令添加功能**，且逻辑核心落在 UI（设置页 VM + XAML）与原生副作用（Everything 安装、IPC），本质 `[v]`：我能编译自证类型正确，但无法在此沙箱运行 WinUI/装 Everything/发 IPC。运行时正确性交给用户硬件验收。

## 6. 下次开发如何接续

**工作树当前状态：**
```
 M docs/待决策事项.md                              (P-35 更正，未提交)
 M src/StarMark.UI/ViewModels/SettingsPageViewModel.cs   (FL 开关，未提交)
 M src/StarMark.UI/Views/SettingsPage.xaml               (FL 开关，未提交)
?? LICENSE                                               (非本人产物，勿提交)
```

**接续步骤：**
1. 真机构建运行应用：设置 →「本地磁盘搜索」开 → 观察状态文本 → 快捷搜索/主窗搜本地文件 → 关闭回落。（验收清单见 `docs/本地磁盘搜索-实施蓝图.md` §5）
2. 通过后**提交批次 FL**：按显式路径 `git add` 上述两 UI 文件（+ 一并带上 `docs/待决策事项.md` 的 P-35 更正），CJK heredoc 提交信息，署名「FL (+0800)」；**不要** `git add -A`、**不要**提交 `LICENSE`。
3. 更新 `docs/前端主题与材质切换审查报告.md` 批次导航（`AH→EW` → `AH→EX? 或直接 FL`；本轮建议标为 **FL**，并把 EX 记为「计划取消」）+ 追加 FL 批次正文 + 时间戳；把 P-2 状态从 🔵 更新为「已按方案 B 落地（真机待验）」。
4. 项目记忆 `memory/project-starmark-audit.md` + `MEMORY.md` 索引行同步（导航批次号、P-2 状态）。
5. 跑一次 x64 全量 `dotnet test StarMark.sln -c Debug --no-build` 确认 718/718（或更新值）绿。

**已知残留 / 可选增强（非本轮阻塞）：**
- `EnsureReadyAsync` 安装 Everything 后**未显式拉起主程序进程**（`EnsureEverythingInstalledAsync` 只跑安装器 `/S`）；若真机发现装完仍 `IsRunning()=false`，需补「安装后启动 Everything.exe」。
- 更宏大的「SYSTEM 提权后台服务 + 私有 Everything 实例 + 命名管道 IPC」方案（蓝图 §2、批次 AC-D1..D4）仍是独立大工程，与本轮「复用用户可见 Everything」路线并存；是否升级由用户定。
- 实时全盘搜索经 `EverythingSource.SearchAsync`（查 Everything 自建的全盘索引），**不依赖** roots 配置；roots 仅约束「索引进库」的文件树/标签持久化。

## 7. 本轮回退方式

若要放弃本轮改动：`git checkout -- src/StarMark.UI/ViewModels/SettingsPageViewModel.cs src/StarMark.UI/Views/SettingsPage.xaml`（`docs/待决策事项.md` 的 P-35 更正可另行保留）。
