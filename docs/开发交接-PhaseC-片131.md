# 开发交接 · Phase C 片131（截图真机验证与日志隔离）

> 本档随批更新。凡与盘上现状冲突，以盘上为准。

## 一、本批改动与边界

1. **日志目录跟着那颗改道信号走**（三档）：`src/StarMark.Abstractions/StarLog.cs` 的 `LogDirectory` 改为
   `DirectoryOverride` → 设置档改道根 → 库改道根 → 兜底 `LocalApplicationData/StarMark/logs`；
   `src/StarMark.UI/App.xaml.cs` 的启动错误框不再自己重算那条路径，改念 `StarLog.LogDirectory`；
   `tests/StarMark.Tests/UserDataPathsTests.cs` 加一颗 `LogsFollowTheRedirectPreserveTheirDefaultAndRespectAnExplicitOverride`。
   边界：那颗信号只在**进程起手**读一次（运行期改环境变量不搬日志）。⛔ 动缓存/净化/节流那一族。
2. **活档行号指针那颗棘轮 33 → 32**（两档）：`tests/StarMark.Tests/LivingSourcePointerGateTests.cs` 的
   `FallenPointerBaseline` 改小到 32 并写下来历；`docs/待决策事项.md` 里四颗指向启动装配档的行号指针按 P-256 ③ 口径
   **删号留锚**，并在 P-256 那一节末追加本批那一句。

## 二、本轮跑过的验证（读数）

- 定向 `UserDataPathsTests`：通过 13／失败 0（正本 `D:\StarMarkTmp\p131\log-redirect-tests.txt`）。
- 解决方案重编：0 警告／0 错误（正本 `D:\StarMarkTmp\p131\log-redirect-build.txt`）。
- **全量＝通过 6589／失败 1／总 6590（11 m 15 s）**（正本 `D:\StarMarkTmp\p131\full131-log-redirect.txt`）：
  那颗红＝`LivingSourcePointerGateTests.TheFallenPointerCensusIsTheOneThisBatchReconciled`（现读 36↔基线 33）。
- 差集复称：另造一把只读尺（逐字搬那颗闸门四步），对 HEAD 的 blob 与盘上各跑一遍＝**33↔36**，
  点名三串（四颗现场）因本批 `App.xaml.cs` 少两行而落空・另一串旧区间**反过来撞成非空**（⛔ 算修复）。
  正本 `D:\StarMarkTmp\p131\census131.py`（仓外仪器）。
- 换锚＋基线改小到 32 之后：那颗闸门一族**通过 13／失败 0** 跑了两回（追加账本前后各一回，
  正本 `pointer-gate-after.txt`／`pointer-gate-after2.txt`）。账本计数器现读 **261／136／125・VERDICT OK**。
- ⚠ **本批我自己犯的一次**：用 Edit 工具改 `docs/待决策事项.md` ⇒ 整档行尾被归一（HEAD blob 的 13390 颗孤立 CR 全没、
  `git diff --numstat` 报 8416/8416＝整档重写）。已按 blob 原样写回并改在字节上做（`redo131_ledger.py`），
  复称三族＝改前后 8415/96/13390 一颗不差、numstat 回到 6/4。这是坑表 #1085 那一族的又一次复发（第三次以上）。
- ⛔ 跑变异台架（照 2026-10-24 那道令）。

## 三、真机腿现状（zh 隔离实例）

- 夹具：`D:\StarMarkTmp\p131\sdk-20261028-r2\{zh,en,ja}`（17 颗设置・18 条热键绑定里只 `screen.capture` 非空＝Ctrl+Alt+Shift+F10・
  `widgets.json` 空实例・四把写/删开关全关・`MinimizeToTray=false`）。r1 从未起过；r2 只起过 zh。
- 起手：`STARMARK_SETTINGS_PATH`／`STARMARK_DB_PATH` 指向该 profile，直接跑刚编出的 x64 那颗 exe，pid 3368。
- **日志真的落在 D**：`<profile>\logs\starmark-20261028.log` 首行 `pid=3368 elev=False`（＝本批那颗改道生效，不是只测了取值）。
- 遮罩起得来（热键两回都开得出），放大镜读数随坐标动（`X: 938 Y: 625` ↔ 截图坐标 600,400 × 1.5625）。
- **SDK 的 `drag` 不产出选区**（两回），而**一次普通 `click` 产出选区**（`SizeText 1200 × 877`・选区＝自动检测到的那颗候选）。
  主窗普通控件的对照跑过了：坐标点击让 `NavHiddenItem` 变 `(selected)`、页面切到「没有隐藏的条目」⇒ 注入通道本身是通的。
  ⚠ 那颗遮罩**不在 `list_windows` 里**，只能从主窗那次 `get_window_state` 的相关截图拿到它自己的 `window`。
- ⚠ **归责未定，⛔ 记成产品缺陷**：日志里 11:38:39–11:41:04 有 40+ 行「设置已保存」＋主题/材质来回切（`Light`↔`Default`、
  Acrylic/Solid/Mica/AcrylicBase），**那些页我一页都没点过**；遮罩冻帧里还有一颗输入法候选窗与 Qoder 那颗
  「正在使用你的电脑」横幅。⇒ 那段时间有人在这颗沙盒实例上动手，两次「拖框没反应」与遮罩自己消失都可能是同一件事。
- 取消那一格（量到）：遮罩关掉之后 profile 里 **0 颗 PNG・`StarMark Screenshots` 目录也没被建出来**。
- ⛔ 做到的：一次真存图（`Ctrl+S` 那一发每次都没递到已经不在的遮罩上）・`saveWriteFailed` 那颗造型・
  三语 `capture.card.*` 的屏幕读数・en／ja 两跑。⛔ 没跑 Enter／Ctrl+C（不写他的剪贴板）・⛔ 没动 ACL。
- 沙盒那颗实例**还在跑**（pid 3368，主窗导航页被我点到「隐藏」那一页，那颗 profile 是弃置的）。

## 四、接续顺序

1. 挑他确实空闲的那一拍（或他点头继续），按「一次 `click` 出选区」那条已经量到的路走 `Ctrl+S`：
   读 `StarMark Screenshots` 里那颗 PNG 的真尺寸＋提示卡那两句（`capture.card.savedTitle`／正文带路径）。
2. 同一条腿补 `saveWriteFailed`：在预测的那颗 PNG 路径上**先占一颗同名目录**（`File.Exists` 为假・`File.Create` 才失败），
   ⛔ 用「把根目录做成一颗档」那副造型（它只会走异常那支，量不到那颗键）。
3. 换 en／ja 各跑一遍（先干净关掉 zh・确认那颗单实例互斥已放开）。
4. 全量再称一次（本批只跑过定向与那一族），然后小步提交、更新进度报告与记忆。
5. ⛔ 执行 `D:\StarMarkTmp\p131\stage131.py`／`run131.py`／`uia_131.ps1`／`run_all.cmd`（旧的那几支会动剪贴板、
   改 ACL、删 PNG、走 PowerShell UIA）。
6. 归他那一半照旧：观感与措辞的点验、✅P-217 那颗 en 复数名单。
