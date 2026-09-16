# StarMark 浏览器扩展对比与补齐方案

> **性质：** 差距审查 + 可落地方案。姊妹文档：《DeskBox桌面组件借鉴与优化意见.md》（窗口工程）、《项目功能可行性分析.md》（项目目标）。
> **对象：** `StarMark`（Chrome MV3 扩展，WXT + React + Dexie + MiniSearch）→ `StarMarkDesktop`（WinUI 3 + C# + SQLite FTS5）。
> **方法：** 通读扩展 `docs/` 四份文档、`src/core/` 全部 22 个模块、`src/entrypoints/` 全部 8 个入口；逐项在 Desktop `src/` 下用检索核实「有没有」，关键项做实测复现。
> **日期：** 2026-09-16

---

## 摘要

扩展在**数据工程**上明显领先，Desktop 在**系统集成**上明显领先，二者不是同一个竞争维度。真正需要补齐的只有 **11 项**，其中 2 项属于「不修就是缺陷」：

| | 差距 | 判定依据 |
|---|---|---|
| **P0-1** | 中文全文检索大面积失效 | **实测**：现分词器下 8 个中文查询 **5 个漏召回**；**已修复**（2026-09-16，见 §1.5） |
| **P0-2** | 备份能力为零 | `src/` 全库检索无 `Backup`/`Restore` 实现；`notes` 表、`widgets.json` 无兜底 |
| P1-3 | 「动态」页是假的 | `ActivityPageViewModel` 实为 `GetRecentAsync(200)`，无法表达「取消 Star」这类已消失事件 | **已落地**（2026-09-16）：`activity` 表 + 新增事件写入点 + 500 条环形裁剪；活动页改读 `activity` 表 |
| P1-4 | 同步无检查点 / 无 ETag / 无限流 | `sync_state` 表只存 `schema_version`；`GitHubSource` 注释里的 `last_synced_at` 从未写入 |
| P1-5 | 书签 URL 未归一化 | `BookmarkItemFactory` 直接 `SourceId = e.Url`，同页多变体必成多条 | **已落地**（2026-09-16）：`UriNormalizer` 纯函数 + 入库前归一 + 存量去重迁移 v4（按归一键合并标签/笔记） |
| P2-6 | 无洞察 / 健康度 | 全库无 `Insight`/`Health`/`Statistic` | **已落地**（2026-09-16）：`InsightsService.BuildHealthReport` 纯函数（扣分制 100 起，重复/未打标签/长期未整理）+ 设置页「收藏健康度」区块（评分+颜色阈值+自绘近 14 天柱状图+语言/域名/重复/标签概要） |
| P2-7 | 无结果分段 / 无字段高亮 / 搜索态导航留白 | 结果平铺 100 条无结构；命中位置无视觉提示；搜索时导航条空占一截 |
| — | ✅ 已有 120ms 输入防抖 | `MainWindow.xaml.cs:322`（v1 曾误判为缺失，已更正） |
| **P1-A** | 无多标签 AND 搜索 | `SearchFilter` 无 `Tags` 字段；搜索页无法按标签过滤，点标签会跳页且只能选一个 |
| **P1-B** | 标签编辑是裸 TextBox 弹窗 | `ItemCardActions.EditTags` 无 chip / 无历史建议 / 删标签靠编辑文本；且写库是 N+1 |
| P2-8 | 无诊断页 | 设置页仅 4 组：主题 / 托盘 / 组件 / GitHub |
| P2-9 | 无增量统计 | 每次聚合全表扫 |
| P2-10 | 无多语言 | 硬编码中文 |
| — | `notes` 表是死表 | Schema 建了但**无任何代码引用**（顺带发现） |

**不要反向对齐的部分：** Desktop 的键盘导航（↑↓/Enter/Ctrl+Enter）、Everything 本地文件源、Ditto 剪贴板源、`PreviewHost` 预览、桌面组件、系统托盘——扩展全都没有。这些是 Desktop 的立身之本，且扩展明确把「文件源」列为 T3「浏览器扩展无法优雅承载」。

---

## 一、P0-1 中文全文检索：实测 5/8 漏召回

### 1.1 现状与实测

`Schema.sql:44-50` 的 FTS5 定义：

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS items_fts USING fts5(
    title, search_text,
    content='items', content_rowid='id',
    tokenize='porter unicode61'      -- 注释称「兼顾英文词干还原与中文按字分词」
);
```

**注释是错的。** `unicode61` 把连续的 CJK 字符视为**一个** token，不是「按字分词」。用 SQLite 3.53.1 实测（`tokenize='porter unicode61'`，4 条中文样本）：

```
实际 token 表: ['about','c','note','search','starmark','winui',
                '如何整理收藏夹','搜索笔记工具','桌面组件开发','统一搜索']
```

中文整串各成一个 token。于是：

| 查询 | 现用 `porter unicode61` + 前缀 `*` | 期望 |
|---|---|---|
| `笔记` | ❌ | 命中「搜索笔记工具」 |
| `收藏` | ❌ | 命中「如何整理收藏夹」 |
| `组件` | ❌ | 命中「桌面组件开发」 |
| `借鉴` | ❌ | 命中「借鉴意见」 |
| `笔记工具` | ❌ | 命中「搜索笔记工具」 |
| `搜索` | ✅ 仅因文本**恰好以它开头** | — |
| `统一` | ✅ 同上 | — |
| `桌面组件` | ✅ 同上 | — |

**规律：只有「从字段开头起的连续前缀」能命中，中间子串全灭。** 命中率 3/8，且这 3 个是巧合。

### 1.2 为什么 trigram 不够（已实测）

换 `tokenize='trigram'` 是网上常见建议，但对中文**只对 3 字及以上生效**：

| 查询 | trigram |
|---|---|
| `桌面组件`（4 字） | ✅ |
| `笔记工具`（4 字） | ✅ |
| `笔记`（2 字） | ❌ |
| `搜索`（2 字） | ❌ |
| `统一`（2 字） | ❌ |

中文 2 字词极高频（笔记/搜索/收藏/组件/标签/同步），**trigram 会漏掉最常用的一类查询**。不推荐单独使用。

### 1.3 推荐方案：写入侧展开「单字 + 相邻二元组」

对齐扩展 `search/indexer.ts` 的 `cjkAwareTokenize`。核心优势：**FTS 表、触发器、`content='items'` 结构一个字都不用改**，只改 `Item.SearchText` 的拼接逻辑。

**写入侧**（`Item.SearchText` 生成处）：

```csharp
// StarMark.Core/Search/CjkTokenizer.cs
public static string Expand(string? text)
{
    if (string.IsNullOrWhiteSpace(text)) return string.Empty;
    var sb = new StringBuilder();
    foreach (var seg in SplitRegex.Split(text))
    {
        if (seg.Length == 0) continue;
        var s = seg.ToLowerInvariant();
        sb.Append(s).Append(' ');
        if (!CjkRegex.IsMatch(s)) continue;          // 非 CJK：整词即可
        var ch = s.Where(c => CjkRegex.IsMatch(c.ToString())).ToArray();
        foreach (var c in ch) sb.Append(c).Append(' ');                       // 单字
        for (int i = 0; i + 1 < ch.Length; i++) sb.Append(ch[i]).Append(ch[i + 1]).Append(' '); // 二元组
    }
    return sb.ToString().Trim();
}
```

**查询侧**（`ItemRepository.BuildFtsQuery`）：CJK 查询**只取二元组/单字，不取整串**，用 AND 连接。整串进 AND 会让 4 字以上查询自我淘汰（我的第一版探针就踩了这个坑）。

**实测结果（同一批样本，10 个查询）：**

```
[OK] '笔记'    tokens=['笔记']            -> 搜索笔记工具
[OK] '搜索'    tokens=['搜索']            -> 搜索笔记工具, StarMark 统一搜索
[OK] '收藏'    tokens=['收藏']            -> 如何整理收藏夹
[OK] '桌面组件' tokens=['桌面','面组','组件'] -> 桌面组件开发, DeskBox 桌面组件借鉴意见
[OK] '统一'    tokens=['统一']            -> StarMark 统一搜索
[OK] '笔记工具' tokens=['笔记','记工','工具'] -> 搜索笔记工具
[OK] '组件'    tokens=['组件']            -> 桌面组件开发, 借鉴意见
[OK] 'search'  /  'DeskBox'  /  '借鉴'   -> 全部命中
命中 10 / 漏 0
```

### 1.4 落地步骤

1. 新增 `CjkTokenizer.Expand()`（纯函数，配单元测试，可直接移植扩展 `indexer.test.ts` 的用例）
2. `Item.SearchText` 拼接处调用 `Expand`
3. `BuildFtsQuery` 增加 CJK 分支（注意现状：含非字母数字的词走引号短语分支，中文查询正落在这里，必须改）
4. **一次性全量重建**：`INSERT INTO items_fts(items_fts) VALUES('rebuild')`
5. 修正 `Schema.sql:42` 那句错误注释

**成本：** 约半天 + 一次重建。**风险：** 索引体积膨胀（二元组约 2×），1 万条条目量级下可忽略。

### 1.5 实装记录（2026-09-16，已完成）

**落地位置**（与 §1.4 的步骤一一对应，仅一处按实测调整）：

1. `StarMark.Abstractions/Text/CjkTokenizer.cs` —— 纯函数，**放在 Abstractions 而非 Core**：
   `Core` 依赖 `Data`，若放 Core 则 `Data` 反向依赖会成环；`FolderPathUtil` 已有先例
2. `ItemRepository.UpsertAsync`：`item.SearchText = CjkTokenizer.ExpandForIndex(...)`
3. `ItemRepository.BuildFtsQuery` 改为 `CjkTokenizer.SplitForQuery` + 逐词元转义；
   CJK 词元**不加 `*`**（已是完整二元组，加前缀通配会过度匹配）
4. `MigrationRunner` 新增 **v3**：按与 Upsert 同口径重算存量 `search_text`，再
   `INSERT INTO items_fts(items_fts) VALUES('rebuild')` 从外部内容表重灌索引
5. `Schema.sql:42` 的错误注释已改写，并加注「不要换 trigram（实测 2 字词全灭）」

**与方案的一处偏离：不发送「整串」token。**
§1.3 的方案是「整串 + 单字 + 相邻二元组」。实装时去掉了整串，理由有二：
① 查询侧从不发整串（见铁律），故整串对召回**零贡献**，只占索引空间；
② 对 2 字串，整串与二元组**完全重复**，会虚增词频、轻微扭曲 bm25 排序。
（`ExpandForIndex_DoesNotEmitWholeRun` 用例钉死此约束。）

**回归测试**（`tests/StarMark.Tests/CjkSearchTests.cs`，13 例）：
- 分词器：CJK 判定（中日韩）、展开产物、非 CJK 原样保留、长串不爆
- **查询侧铁律**：`SplitForQuery("笔记工具")` 必须恰好是 `["笔记","记工","工具"]`，
  断言 `DoesNotContain("笔记工具")` —— 整串一旦回到查询侧，4 字以上查询立刻全灭
- 端到端：`Theory` 覆盖 8 个中文子查询（中间子串 / 尾部子串 / 跨词边界 / 4 字 / 2 字词 /
  跨两条命中），全部断言精确结果集；另有无匹配、英文前缀、特殊字符三组
- 迁移：`MigrateV3_RebuildsLegacySearchText` 先把 `search_text` 还原成未展开形态、
  版本退回 2、重建索引，断言「笔记」**查不到**；执行 v3 后断言**查得到**

测试 **93 → 113 通过，0 警告 0 错误**。

---

## 二、P0-2 备份：当前零兜底

### 2.1 现状

`src/` 全库检索 `Backup|Restore|Snapshot` 只命中 `HiddenPage`（恢复隐藏条目）与 `WidgetManager`，**没有任何数据备份实现**。而无兜底的数据至少有四类：

| 数据 | 位置 | 可重建性 |
|---|---|---|
| 用户笔记 | `items.notes` | ❌ 不可重建 |
| 标签体系 | `tags` / `item_tags` | ❌ 不可重建 |
| 桌面组件待办 / 随记 | `widgets.json` | ❌ 不可重建 |
| 隐藏 / 置顶状态 | `items.hidden` / `pinned` | ❌ 不可重建 |

条目本体（书签 / Star / 文件）可重新同步，但**用户手动产生的元数据全部不可重建**。重命名数据库文件、升级失败、误触清空，都会一次性带走。

### 2.2 扩展的做法与它的两个坑

`backup.ts` 导出信封：

```ts
{ app: 'starmark', version: 2, exportedAt: number, items: StarItem[] }
```

- 可选口令加密：AES-256-GCM，PBKDF2-SHA-256 **100,000 次**迭代
- 导入语义 = **纯替换**（`items.clear()` → `upsertItems`）
- UI 侧仅一个 `window.confirm` 显示条数

**两个必须避开的坑：**

1. **salt 与 IV 恒为全零。** `backup.ts:45-46` 声明了 `new Uint8Array(16)` / `new Uint8Array(12)` 却从未调用 `crypto.getRandomValues`。同一口令每次导出密钥相同、IV 重复，**AES-GCM 的安全性前提被直接破坏**。移植时务必用 `RandomNumberGenerator.Fill()`。
2. **无校验和、无版本兼容分支、无导入前快照。** `version` 字段写了但导入时没做任何分支判断。

### 2.3 建议方案

```csharp
// StarMark.Core/Backup/BackupService.cs
public sealed record BackupEnvelope(
    string App,            // "starmark-desktop"
    int Version,           // 1
    long ExportedAt,
    string Checksum,       // SHA-256，覆盖下面所有载荷 → 防截断/损坏
    IReadOnlyList<Item> Items,
    IReadOnlyList<TagRecord> Tags,
    IReadOnlyList<ItemTagRecord> ItemTags,
    WidgetSnapshot? Widgets);   // widgets.json 原文，读不懂也不丢

public sealed record EncryptionEnvelope(
    string Enc,            // "aes-256-gcm"
    int Kv,                // 1
    byte[] Salt,           // ← 必须随机 16 字节
    byte[] Iv,             // ← 必须随机 12 字节
    byte[] Data);
```

四条硬性规则：

1. **导入前自动快照**：写入 `%LOCALAPPDATA%\StarMark\backups\pre-restore-<ts>.json`，先落盘再清空。扩展没有这一步，是最该补的。
2. **校验和前置**：`Checksum` 不匹配直接拒绝导入，不要等 `Upsert` 崩到一半。
3. **语义分层**：条目（可重新同步）与用户元数据（不可重建）分开存，即使是「仅恢复元数据」也能做。
4. **UI 用 `ContentDialog` + 条数确认**，不要 `window.confirm` 级别的交互；危险区独立红色分区（扩展 `options/App.tsx:443` 的 `.panel.danger` 值得抄）。

**成本：** 约 1～1.5 天。**注意：** 别顺手把加密也做了——它是可选的，先做明文 + 快照 + 校验和，加密放第二轮。

---

## 三、P1-3「动态」页是假的

`ActivityPageViewModel.LoadAsync()`：

```csharp
var items = await _repository.GetRecentAsync(200, ...);   // 按 updated_at 排序的条目
```

注释写着「对应浏览器扩展 activity tab」，但语义完全不同。扩展有独立 `activity` 表与 4 种事件类型：

```ts
ActivityKind = 'star_add' | 'star_remove' | 'bookmark_add' | 'bookmark_remove'
// 写入后 >500 条按 at 升序裁剪最旧的（activity.ts:8-12）
```

**差别在哪：** 「取消 Star」「删除书签」这类事件的主体已经从 `items` 里消失了，`GetRecentAsync` 永远看不到它们。用户打开「动态」想看的是「这段时间我增删了什么」，现在看到的是「最近更新的 200 条条目」——后者在同步后基本等于「全部条目按时间排序」，信息量接近零。

**方案：**

```sql
CREATE TABLE IF NOT EXISTS activity (
    id INTEGER PRIMARY KEY, at INTEGER NOT NULL,
    kind TEXT NOT NULL, item_id INTEGER, title TEXT NOT NULL, uri TEXT);
CREATE INDEX IF NOT EXISTS idx_activity_at ON activity(at DESC);
```

写入点三处：`UpsertAsync` 新增分支（新增条目）、同步对账的 `stripSource` 分支（移除来源）、用户显式删除。裁剪逻辑照搬扩展的 500 条环形缓冲。

**成本：** 半天。**价值：** 顺带解决「同步到底干了什么」这个排查刚需。

---

## 四、P1-4 同步：无检查点、无 ETag、无限流

### 4.1 现状（三处证据）

1. `SyncCoordinator.SyncAllAsync` 是**一次性全量**：遍历源 → `FetchAsync` → `UpsertAsync`，无阶段、无落盘、无断点。
2. `sync_state` 表在 Schema 里建了，但代码里只有 `MigrationRunner` 用它存 `schema_version`（`SELECT value FROM sync_state WHERE key='schema_version'`）。**同步状态一行都没写。**
3. `GitHubSource.cs:35` 注释称「此处 `last_synced_at` 写入 DB 的 `sync_state` 表」——**实际没有这行代码**；第 13 行注释也自认「增量同步通过 `SyncContext.ContinuationToken`……本 MVP 暂未启用」。

### 4.2 扩展的做法

`sync/github.ts` 是一个五阶段可恢复状态机：

```
VALIDATE → FETCH_PAGES → RECONCILE → TAG_INDEX → DONE
```

- **每拉一页就落一次检查点**（`github.ts:104-108`），并 `await sleep(0)` 让出事件循环
- **条件请求**：带 `If-None-Match` / `If-Modified-Since`，命中 304 立即收工
- **错误分类**：401 → `AUTH_ERROR`，403/429 → `RATE_LIMITED`，错误写回检查点供 UI 展示，下次可从断点续跑
- **上限保护**：`MAX_PAGES = 200`（= 2 万个 Star）

### 4.3 Desktop 的具体缺口

| 项 | 扩展 | Desktop |
|---|---|---|
| ETag / 304 条件请求 | ✅ | ✅（第一步已落地）`GitHubClient.GetStarredPageAsync` 首页带 `If-None-Match`，命中 304 直接返回空短路整轮拉取 |
| 限流处理 | ✅ 解析 `Retry-After` 并分类 | ✅（第一步已落地）429 / 403+X-RateLimit-Remaining:0 分类为 `RateLimit`；未解析 `Retry-After` |
| 401 识别 | ✅ | ✅（第一步已落地）401 分类为 `Auth`，`SyncCoordinator` 给 UI 可操作文案 |
| 分页上限 | 200 页 | `maxPages = 50`（`GitHubClient.cs`） |
| 断点续跑 | ✅ 每页落检查点 | ❌（第二步阶段机未做） |

### 4.4 建议：分两步，先做低成本高收益的

**第一步（约半天，收益最大）**——不动架构，只加三件事：

```csharp
// 1. 条件请求：命中 304 直接返回空，省掉整轮拉取
request.Headers.TryAddWithoutValidation("If-None-Match", etag);
// 2. 错误分类：把 401/403/429 从 Exception 里分出来，给 UI 可操作的文案
// 3. 落 last_synced_at + etag 到 sync_state（把 GitHubSource:35 的注释兑现）
```

GitHub 限额 5000 次/小时，一个 5000 Star 的账号全量拉取要 50 次请求；带 ETag 的增量轮询在无变化时**只花 1 次**。这是投入产出比最高的一项。

**第一步实现记录（2026-09-16，已合并）**：
- `GitHubClient`：新增 `CachedETag` 属性与可选 `HttpClient? http = null` 构造函数参数；`GetStarredPageAsync` 仅首页带 `If-None-Match`，命中 `304 NotModified` 返回空短路整轮；非成功响应经 `Classify` 分类为 `GitHubErrorKind`（Auth / RateLimit / Forbidden / Server / Unknown），`403 + X-RateLimit-Remaining:0` 归为限流；首页 `ETag` 响应头回写 `CachedETag`。
- `GitHubSource`：构造函数注入 `IItemRepository`；`FetchAsync` 拉取前载入 `github:etag`，拉取后回写 `github:etag` 与 `github:last_synced_at`（Unix 秒），兑现原注释。
- `SyncCoordinator`：新增 `SourceSyncErrorKind` 枚举与 `SourceSyncResult.ErrorKind`；`catch (GitHubApiException)` 映射为可操作中文文案（更新 Token / 等待限流恢复 / 检查权限等）。
- 测试：`GitHubClientTests`（8 例）覆盖 304 短路、ETag 捕获、401/429/403 限流/403 权限/5xx 分类、条件请求头下发。全量测试 147 例通过。

**第二步（约 2 天）**——把 `SyncCoordinator` 改成阶段机，`sync_state` 存 `JsonSerializer` 化的检查点。桌面端不像 MV3 的 Service Worker 会被系统杀掉，紧迫性低于扩展，可以缓。

---

## 五、P1-5 书签 URL 未归一化

`BookmarkItemFactory.MapItem`：

```csharp
SourceId = e.Url,     // 原样入库，无归一化
```

扩展 `normalize.ts` 的规则：

1. 去 `#hash`
2. `http:80` / `https:443` 默认端口清零
3. `hostname.toLowerCase()`
4. 删 11 个追踪参数：`utm_source/medium/campaign/term/content`、`fbclid`、`gclid`、`mc_cid`、`mc_eid`、`ref_source`
5. GitHub 专用：`github.com` 及子域的 pathname 收窄到 `/{owner}/{repo}`；`search` 含 `tab=` 时清空 query
6. 去尾斜杠

**后果：** 同一仓库的 `github.com/a/b`、`github.com/a/b/`、`github.com/a/b?tab=readme` 会变成 3 条独立条目，用户打的标签和笔记**分裂在三条记录上**。再加上 `idx_items_source ON items(source, source_id)` 是唯一索引，去重也就无从谈起。

**方案：** 新建 `UriNormalizer`（纯函数，可单测），在 `MapItem` 入库前归一；同时写一次性迁移脚本，把已存在的重复项按归一化键合并（保留最早 `created_at`，合并 `tags`，非空 `notes` 冲突时保留较长者并留日志）。

**成本：** 半天。**建议：** 与 P1-3 的 activity 改造一起做，都动 `Upsert` 路径。

### 实装记录（2026-09-16）

- `StarMark.Abstractions/UriNormalizer.cs` 纯函数：去 `#hash`、默认端口清零、host 小写、删 11 个追踪参数、GitHub `pathname` 收窄到 `/{owner}/{repo}` 且 `tab=` 时清空 query、去尾斜杠；仅对 http(s) 生效，file:// 等原样返回，幂等
- `BookmarkItemFactory.MapItem` 入库前对 `SourceId`/`Uri` 归一
- `ItemRepository.UpsertOne` 顶部也对 `SourceId` 归一（所有 http(s) 源统一受益，幂等）
- 一次性迁移 `MigrationRunner.MigrateV4`（schema v4）：按 `(source, 归一化 key)` 分组，每组保留最早 `created_at` 条目，合并标签（`INSERT OR IGNORE`）与笔记（非空冲突保留较长者），删除其余并把 keeper 的 `source_id` 归一到标准键；幂等

---

## 六、P2 级差距

### P2-6 洞察 / 健康度（扩展最有「产品感」的一屏，Desktop 完全空缺）

`insights.ts` 的 `buildHealthReport(items, days=14, t)` —— **扣分制，起点 100**：

| 因子 | 判据 | 扣分 |
|---|---|---|
| 疑似重复 | 按「归一化标题」分组，`length >= 2` 成组 | `min(40, 组数 × 10)` |
| 未打标签 | `untagged/total > 0.2` 才罚 | `min(25, floor(ratio × 50))` |
| 长期未整理 | `Date.now() - updatedAt > 180 天`，且 `staleRatio > 0.4` 才罚 | `min(15, floor(ratio × 30))` |

另输出语言分布 Top8、14 天新增趋势（按 `starredAt ?? createdAt` 归到日期）、独立域名数、标签直方图、重复项 Top10。

**为什么值得做：** 纯本地聚合、零外部依赖、零网络，是 Desktop 相对扩展**本该更强**却反而没有的一块。而且它能把「收藏夹腐败」这件用户有感觉但说不清的事变成可行动的清单。

**方案：** `InsightsService`（纯函数，输入 `IReadOnlyList<Item>`，输出 `HealthReport`，可直接移植 `insights.test.ts`）；UI 在设置页加一个区块，评分用大字号 + 颜色阈值（≥80 绿 / ≥50 黄 / <50 红），柱状图用 `ItemsRepeater` 自绘即可，不必引图表库。**成本约 1 天。**

### P2-7 搜索体验三件事

| 项 | 扩展 | Desktop | 方案 |
|---|---|---|---|
| 输入防抖 | **120ms**（`App.tsx:172`） | ✅ **已有** 120ms `DispatcherTimer`（`MainWindow.xaml.cs:322`）。**此项无需改动**（v1 文档误判为"无防抖"，已更正） | — |
| 结果分段 | 「精确匹配 / 相关结果」两段 + 域名聚合 + `dup` 徽章 | 平铺 `MaxResults=100`，无结构 | 照搬 `selectors.ts:56-107` 约 30 行；WinUI 侧用 `CollectionViewSource` + `IsSourceGrouped` |
| 排序维度 | 6 种（相关/最近/最近 Star/最近收藏/Stars/名称）+ 语言筛选 | 3 种（最近/Stars/名称），无语言维度 | 加 `ComboBox` + 一个 `Where`，成本低 |

扩展判定「精确匹配」的规则值得原样抄：

```
isStrong = title.startsWith(needle) || url.includes(needle)
// 开启 sourceAware 时，书签若 title.includes(needle) 也提升为 strong
```

### P2-8 诊断页（半天，性价比极高）

扩展设置页有独立的「诊断」区块：`phase / page / ETag 开关 / 上次完成 / 上次错误 / 书签全量遍历时间 / 索引版本`。Desktop 设置页目前只有 4 组：主题、托盘与呼出、桌面组件、GitHub Stars 同步。

建议在设置页末尾加一组只读信息：各源 `IsAvailable`、上次同步时间与条数、FTS 索引行数、DB 文件路径与体积、schema 版本。**这是排查用户报障时最省沟通成本的一屏。**

### P2-9 增量统计

扩展用单行 `meta` 表维护 `AppMeta{total, stars, bookmarks, hidden, tagged, tags: Record<string,number>}`，由 `applyItemDelta()` 增量更新，侧栏计数零扫描。Desktop 每次聚合都全表扫。

**方案：** SQLite 触发器维护计数表，或写入侧维护。**当前数据量下优先级低**，等条目过万再做不迟。

### P2-10 多语言

扩展内置 zh-CN / ja / en 三语字典，`t()` 三级回退（lang → zh-CN → key），核心层通过注入 `TFunc` 保持纯函数可测。

**我的建议是暂不做。** 桌面端面向的是本机用户，且 WinUI 的 `resw` 资源体系改造面覆盖所有 XAML 与 C# 字符串，成本高而收益不明确。真要做，先把 `InsightsService` 这类纯函数保持「文本由调用方注入」的约定（扩展就是这么做的），为将来留口子即可。

### 顺带发现：`notes` 表是死表

`Schema.sql:95` 建了 `notes(id, item_id, content, created_at, updated_at)`，但**全库无任何代码引用**——真正的笔记存在 `items.notes` 列（`ItemRepository:242` 更新的是 `items SET notes = @content`）。属于 schema 债，建议在下次迁移里删掉或明确其用途，避免后来者误用。

---

## 六·补 交互层差距（第二轮补充审查）

> **状态：本节四项已全部实装（2026-09-16）。** 构建 0 错误 0 警告，测试 82 → 93。
> 新增文件：`Controls/TagEditor.xaml(.cs)`、`Core/Text/Highlighter.cs`、`Helpers/HighlightHelper.cs`、
> `tests/StarMark.Tests/HighlighterTests.cs`。详见 §实装记录。

> 首轮审查偏重数据层，漏掉了几处交互细节。第二轮针对「标签交互 / 搜索反馈 / 布局」补查，
> 同时更正首轮的一处误判。

### 更正：Desktop 已有 120ms 输入防抖

首轮判定「无防抖」**是错的**。`MainWindow.xaml.cs:319-348` 的 `SearchBox_TextChanging` 用
`DispatcherTimer` 做了 120ms 防抖，且配合 `PushToolbarToContent`。此项两边等价，无需改动。

---

### G-A 多标签 AND 搜索（Desktop 缺失）

扩展的 `search` 请求带 `tags` 参数，worker 侧：

```ts
const hasTags = (tags) => !opts.tags || opts.tags.every((t) => (tags ?? []).includes(t))
// search-worker.ts:157 —— 命中集、字面兜底集、文件夹树（:233）三处统一应用
```

即**标签过滤与关键词是叠加的**，可以「搜 `rust` 且同时带 `ai` 和 `read-later` 两个标签」。

**Desktop 现状：** `SearchFilter` 只有 `MaxResults / IncludeSize / IncludeDate / IncludeHidden / Type / Sort`，
**没有 Tags 字段**。搜索页完全无法按标签过滤。点卡片上的标签走的是
`Card_TagFilterRequested → NavigateTo("tags", tag)`——**跳到标签页并筛选单个标签**，
既丢了当前关键词，也只能选一个标签。

`TagsPageViewModel` 内部倒是有 `_activeFilters` 集合支持多选，但那是标签页自己的浏览态，
跟搜索是两条互不相通的路径。

**方案：** 见 §实现 A。

---

### G-B 标签编辑交互：弹窗 TextBox vs 实时 chip + 建议

这是桌面版交互最明显落后的一处。

**扩展**（`App.tsx:1002-1059`）在卡片内内联展开，四件事同时成立：

1. **实时成 chip**：`draft.split(/[,，\s]+/)` 边打字边把已输入内容渲染成彩色 chip
2. **点 chip 即删**：`onClick={() => setDraft(draftTags.filter(x => x !== tg).join(', '))}`
3. **历史标签建议**：`allTags.filter(t => 不在条目上 && 不在草稿里).slice(0, 24)`，
   点一下即追加 `{draft.trim()}, {t}`
4. **中英文逗号与空白**都当分隔符

**Desktop**（`ItemCardActions.EditTags` / `AddTag`）是 `ContentDialog` 里塞一个裸 `TextBox`：

```csharp
var box = new TextBox { Text = string.Join(", ", vm.Tags),
                        PlaceholderText = "逗号分隔多个标签，如: ai, llm" };
```

- 看不到已有哪些标签（只有一行逗号分隔的纯文本）
- 无历史标签建议，全靠手打 → 同一概念容易打成 `ai` / `AI` / `Ai` 三份
- 删一个标签要在文本里精确找到并删掉，还容易漏逗号
- 「快速添加标签」是另一个独立的空输入框弹窗，同样无建议

此外 `EditTags` 的写库是 **N+1 往返**：先 `foreach RemoveTagAsync` 逐条删，
再 `foreach AddTagAsync` 逐条加。标签多的时候是可感知的卡顿。

**方案：** 见 §实现 B。

---

### G-C 搜索无任何字段高亮

扩展 `App.tsx:1109-1119` 的 `highlight()` 至少高亮首个匹配片段（先 `escapeHtml` 再包 `<mark>`）。
Desktop 的 `ItemCard` 标题/副标题就是纯 `TextBlock`，**搜到的词在哪儿完全看不出来**。

在配合 P0-1（中文检索修好）之后，高亮的缺失会更刺眼——用户能搜到了，但不知道为什么这条命中。

**方案：** 见 §实现 C。

---

### G-D 搜索态导航留白

扩展的做法是**整行替换**：

```tsx
{!searching && indexReady && (<div className="tabs">…</div>)}
```

搜索时 tab 行从 DOM 移除，同时上方出现 `.toolbar`（排序/来源/域名聚合/来源感知）。
因为是 flex 纵向布局，tab 行消失后结果列表自然上移填满，**不留空白**。

Desktop 是 `NavigationView PaneDisplayMode="Top"`（`MainWindow.xaml:103`），
搜索时执行 `NavView.SelectedItem = null`（`MainWindow.xaml.cs:333`）——
只是**取消选中**，导航条本身仍占位。而且 `ContentFrame` 是 NavigationView 的 Content，
没法在导航条折叠时上移。结果就是搜索时顶部空出一截。

**方案：** 见 §实现 D。

---

### 扩展的待修缺陷清单（可作为补丁回灌）

用户授权「为扩展打补丁」。以下三项是审查中确认的真实缺陷，优先级从高到低：

| # | 缺陷 | 位置 | 说明 |
|---|---|---|---|
| 1 | **备份 salt / IV 恒为全零** | `backup.ts:45-46` | 声明了 `new Uint8Array(16)` / `(12)` 但从未 `getRandomValues`。同口令每次导出密钥相同、IV 重复，**AES-GCM 的 IV 唯一性前提被破坏**。应改为 `crypto.getRandomValues(salt)` |
| 2 | **`SearchHit` 不导出 score / match terms** | `search/protocol.ts:28-43` | MiniSearch 内部算了 BM25 分值但协议层丢弃，导致 UI 无法按分值排序或展示匹配度，`groupHits` 只能用 `startsWith`/`includes` 做定性分区 |
| 3 | **限流只上报不重试** | `api/github.ts` + `sync/github.ts:141` | 解析了 `Retry-After` 挂到错误对象上，但调用方只把状态写成 `RATE_LIMITED` 给用户看，**没有退避重试** |

> 第 1 项是安全性问题，建议优先。三项都已在本文件 §2.2 与本节标注，
> Desktop 侧实现时按「不复制缺陷」处理即可。

---

### 实装记录（2026-09-16）

四项均已落地，与上方方案基本一致，三处按实现需要做了调整：

**实现 A · 多标签 AND 搜索 —— 已实装**
- `SearchFilter.Tags` + `HasTags`；`ItemRepository` 新增 `BuildTagClause` / `BindTagParams`，
  每个标签一个参数化 `EXISTS`（走 `idx_item_tags_tag`），搜索与浏览两条路径共用
- **顺带修掉一个真 bug**：`GetAllAsync` 原用 `JOIN + t.name IN(...) + GROUP BY`，
  **实际是 OR 语义**——标签页多选标签时结果反而变多，与 UI 暗示的「收窄」完全相反。
  已改为同一套 `EXISTS` 判定，并用 `BrowseFilter_TagsAreAndSemantics` 用例钉死
- `SearchService`：带标签过滤时**跳过实时源**（Everything 返回的本地文件未入库因而无标签，
  参与合并会让筛选结果混进一堆无标签文件）；空关键词 + 有标签时退化为「按标签浏览」
- `SearchPageViewModel.ActiveTags` + `AddTagFilter` / `RemoveTagFilter` / `ClearTagFilters`；
  `SearchPage` 顶部吸顶标签筛选条（chip 带 ✕ + 一键清除）
- 卡片标签点击由「跳到标签页且只能选一个」改为**在搜索页内叠加过滤**，不再丢失关键词

**实现 B · TagEditor —— 已实装**
- `Controls/TagEditor.xaml(.cs)`：已选 chip 区（点击即删）+ 输入框 + 历史标签建议（上限 24）
- **与扩展的实现差异（有意为之）**：扩展把输入框当唯一真源、草稿文本实时解析成 chip；
  本控件改为「提交式」——逗号/回车落成 chip，空输入退格删最后一个。
  这样 chip 区始终干净可见，不必在一长串原始文本里找标签
- `EditTags` 写库由「全删再全加」的 N+1 改为**差集更新**；`AddTag` 复用同一控件，
  因此同样享有历史建议（避免把 `ai` 打成 `AI` 造成同义标签分裂）

**实现 C · 搜索高亮 —— 已实装**
- 纯函数 `Highlighter.Split` 放 Core（可单测），UI 层 `HighlightHelper` 只负责
  把片段刷进 `TextBlock.Inlines` 的附加属性
- 策略：先试完整查询串，未命中再退化为逐个词（长词优先）——
  搜 "rust async" 时两者都能亮，而不是整串找不到就全不亮
- `ItemCard` 标题与副标题改走 Inlines（保留 `TextTrimming`）
- 新增 `HighlighterTests` 9 例（含中文中间子串、大小写保持原样、只高亮首个命中）

**实现 D · 搜索态导航 —— 已实装**
- `ContentFrame` 从 `NavigationView.Content` 移出，改挂 `Grid.Row="5"`；
  NavView 独占 `Row 4`（`Height="Auto"`）。搜索/设置时 `SetNavVisible(false)` 整条折叠，
  该行塌缩为 0，内容区上移填满
- 清空关键词时恢复导航栏，避免用户被困在搜索页

---

### 第二轮补充的实现方案

**实现 A · 多标签 AND 搜索**

1. `SearchFilter` 增加 `IReadOnlyList<string>? Tags { get; init; }`
2. `ItemRepository.SearchAsync` / `BrowseAsync` 的 WHERE 追加（每个标签一个 `EXISTS`，参数化）：
   ```sql
   AND (@tag0 IS NULL OR EXISTS (
        SELECT 1 FROM item_tags it0 JOIN tags t0 ON t0.id = it0.tag_id
        WHERE it0.item_id = i.id AND t0.name = @tag0))
   ```
   —— 用「计数相等」也能做，但 `EXISTS` 逐条拼更直观且能走 `idx_item_tags_tag`
3. `SearchPageViewModel` 增加 `ObservableCollection<string> ActiveTags` +
   `AddTagFilter` / `RemoveTagFilter` / `ClearTagFilters`，任一变更触发重新搜索
4. `SearchPage.xaml` 顶部加**吸顶标签筛选条**（对齐扩展 `.tag-banner` 的
   `position: sticky; top:0`）：显示当前生效标签，每个带 ✕，末尾一个「清除」

**实现 B · 标签编辑重做**

新建 `Controls/TagEditor.xaml`（UserControl），内部三段：
`已选 chip 行`（实时解析 + 点击删除）→ `输入框` → `建议行`（历史标签，排除已选，上限 24，点击追加）。

- 建议来源：`IItemRepository.GetAllTagsWithCountsAsync()`（已存在）
- `ItemCardActions.EditTags` / `AddTag` 改为把 `TagEditor` 塞进 `ContentDialog.Content`
- 写库改为**差集更新**：只 `Remove` 真正减少的、只 `Add` 真正新增的，消除 N+1
- 保留现有 `RemoveTag`（卡片上点 ✕ 直接删）这条快捷路径

**实现 C · 搜索高亮**

- 新增 `Helpers/HighlightHelper.cs`：`BuildInlines(string text, string query)` 返回 `IList<Inline>`
  （`Run` 普通 + `Run` 带 `SystemFillColorCautionBrush` 前景 + 中等字重）
- `ItemCardViewModel` 暴露 `TitleInlines` / `SubtitleInlines`，由 `SearchPage` 在搜索后设置关键词
- 注意：`TextBlock` 用 Inlines 后不能再走 `Text` 属性绑定，需改 `ItemCard.xaml`
- 只高亮**首个**匹配片段（与扩展一致），避免长文本性能问题

**实现 D · 搜索态导航**

`MainWindow.xaml` 结构调整：

```xml
<Grid.RowDefinitions>… Auto / Auto / Auto / Auto / Auto / * </Grid.RowDefinitions>
<NavigationView x:Name="NavView" Grid.Row="4" PaneDisplayMode="Top" …>   <!-- 不再承载内容 -->
    <NavigationView.MenuItems>…</NavigationView.MenuItems>
</NavigationView>
<Frame x:Name="ContentFrame" Grid.Row="5"/>
```

搜索时 `NavView.Visibility = Visibility.Collapsed` → Row 4（`Height="Auto"`）塌缩为 0，
内容区上移填满。返回浏览态再恢复 `Visible`。

---

## 七、Desktop 已领先的部分（不要反向对齐）

审查时也反向看了一遍，这几项 Desktop 明显强于扩展，**不要因为扩展没有就"对齐"回去**：

| 能力 | Desktop | 扩展 |
|---|---|---|
| 键盘导航 | ✅ ↑↓ 选择 / Enter 打开 / Ctrl+Enter 定位 / Esc 清空 | ❌ 全仓无（只有 `autoFocus`） |
| 本地文件源 | ✅ Everything 实时查询 | ❌ 明确定位为 T3「浏览器扩展无法优雅承载」 |
| 剪贴板源 | ✅ Ditto | ❌ |
| 内容预览 | ✅ `PreviewHost` | ❌ |
| 桌面组件 | ✅ 5 种 + 吸附 + 层级策略 | ❌ 无此形态 |
| 系统集成 | ✅ 托盘、全局热键、单实例互斥体 | 受限于扩展沙箱 |
| 用户状态保护 | ✅ `hidden`/`pinned`/`notes` 同步永不覆盖（`ItemRepository:416`） | ✅ 同等（`mergePreserving`） |
| 标签云带计数 + 过滤横幅 | ✅ 已实现 | ✅ 同等 |

**用户状态保护这一条两边都做对了**，不用动。

---

## 八、建议落地顺序

```
第一轮 · 修地基（约 2 天）
├─ P0-1  中文检索：CjkTokenizer + BuildFtsQuery + 一次性 rebuild   ✅ 已完成
└─ P0-2  备份：明文导出/导入 + 导入前快照 + 校验和（加密留到第二轮）  ← 下一个

第二轮 · 数据正确性（约 1.5 天）
├─ P1-3  真·活动流：activity 表 + 3 处写入点 + 500 条裁剪
└─ P1-5  UriNormalizer + 存量重复项合并迁移

第三轮 · 同步质量（约 0.5 天，可独立提前）
└─ P1-4  第一步：ETag/304 + 401/429 分类 + last_synced_at 落库  【✅ 2026-09-16 已落地】

第四轮 · 交互补齐（第二轮审查，约 2 天）
├─ 实现 A  多标签 AND 搜索 + 吸顶标签筛选条          （P1-A）
├─ 实现 B  TagEditor：实时 chip + 历史建议 + 差集写库 （P1-B）
├─ 实现 C  搜索结果字段高亮                          （P2-7）
└─ 实现 D  ContentFrame 移出 NavigationView，搜索态折叠（P2-7）

第五轮 · 产品感（约 2 天）
├─ P2-6  InsightsService 健康度 + 设置页区块  【✅ 2026-09-16 已落地】
├─ P2-7  结果分段（精确 / 相关）
└─ P2-8  诊断区块

按需 / 暂缓
├─ P2-9   增量统计（条目过万再做）
├─ P2-10  多语言（建议不做，核心层留 TFunc 口子）
└─ 死表 notes 清理
```

**排序逻辑：** ① 先修「不修就是错的」（检索失效、零备份）；② 再修「数据本身不可信」（假活动流、重复条目）；③ 然后修「同步的健壮性」（ETag 是半天换最大收益）；④ 最后才加「锦上添花」（洞察、防抖、诊断）。

**与既有计划的衔接：**《DeskBox桌面组件借鉴与优化意见.md》给的第十轮是「Everything 索引进库」。本文件的 P0-1 应当**排在它之前**——如果中文检索是坏的，把更多本地文件灌进 `items` 只会放大这个问题。

---

## 附录：本次审查的可复现依据

| 结论 | 复现方式 |
|---|---|
| 现分词器 5/8 漏召回 | 建 `fts5(tokenize='porter unicode61')` 表，插入 4 条中文样本，查询 `笔记`/`收藏`/`组件`/`借鉴`/`笔记工具` |
| token 表证据 | `CREATE VIRTUAL TABLE v USING fts5vocab(t,'row'); SELECT term FROM v` → 中文整串各成一 token |
| trigram 对 2 字词失效 | 同法建 `tokenize='trigram'` 表，`笔记`/`搜索`/`统一` 均无结果，`桌面组件` 有 |
| 推荐方案 10/10 | 写入侧展开单字+二元组，查询侧二元组 AND |
| Desktop 无备份 | `grep -rln "Backup\|Restore\|Snapshot" src/ --include=*.cs` 仅命中 `HiddenPage`/`WidgetManager` |
| 动态页是假的 | `ActivityPageViewModel.LoadAsync` → `GetRecentAsync(200)` |
| `sync_state` 未用于同步 | `grep -rn "sync_state" src/` 仅 `MigrationRunner` 的 `schema_version` |
| `notes` 表无引用 | `grep -rn "FROM notes\|INTO notes" src/` 无结果 |

> 扩展侧所有结论均来自 `StarMark/src/core/` 与 `src/entrypoints/` 源码，关键处标注了文件名与行号；扩展自身已知缺陷（备份 salt/IV 恒零）已在 §2.2 标出，移植时勿照抄。
