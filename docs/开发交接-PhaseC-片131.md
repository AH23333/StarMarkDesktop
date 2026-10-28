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
  点名三串指向启动装配档的号因本批平移而落空（其中一串两处）・另一串旧区间反过来撞成非空＝#1020 那句「巧合」的另一副脸。
  正本 `D:\StarMarkTmp\p131\census131.py`（仓外仪器）。
- 换锚＋基线改小到 32 之后：那颗闸门一族**通过 13／失败 0** 跑了两回（追加账本前后各一回，
  正本 `pointer-gate-after.txt`／`pointer-gate-after2.txt`）。账本计数器现读 **261／136／125・VERDICT OK**（`bin/WI37C/count_ledger.py`）。
- ⚠ **本批我自己犯的一次**：用 Edit 工具改 `docs/待决策事项.md` ⇒ 整档行尾被归一（HEAD blob 的 13390 颗孤立 CR 全没、
  `git diff --numstat` 报 8416/8416＝整档重写）。已按 blob 原样写回并改在字节上做（`redo131_ledger.py`），
  复称三族＝改前后 8415/96/13390 一颗不差、numstat 回到 6/4。坑表 #1085 那一族第三次以上复发。
- ⛔ 跑变异台架（照 2026-10-24 那道令）。

## 三、真机腿现状（zh 隔离实例）

- 夹具：`D:\StarMarkTmp\p131\sdk-20261028-r2\{zh,en,ja}`（17 颗设置・18 条热键绑定里只 `screen.capture` 非空＝Ctrl+Alt+Shift+F10・
  `widgets.json` 空实例・四把写/删开关全关・`MinimizeToTray=false`）。r1 从未起过；r2 只起过 zh（pid 3368，仍在跑）。
- **日志真的落在 D**：`<profile>\logs\starmark-20261028.log` 首行 `pid=3368 elev=False`（＝本批那颗改道生效，不是只测了取值）。
- 遮罩起得来（热键每一跑都开得出）。⚠ 那颗遮罩**不在 `list_windows` 里**，只能从主窗那次 `get_window_state`
  的相关截图拿到它自己的 `window`——上一批据此以为「遮罩自己消失了」，其实是那一颗句柄换了。
- **键盘递得到**（量到）：在遮罩上按 Escape ⇒ 那一跑之内遮罩就没了（`alive=false`）；`Ctrl+S` 之后同样关掉。
- **鼠标递不稳**（量到）：SDK 的 `drag` 三回都没产出任何选区；`click` 有时先带一颗 Moved（放大镜＋候选高亮＋`SizeText`
  念出 1200×877＝那颗候选窗的尺寸），有时**什么 Moved 都不带**（最后一跑：点击之后整棵 UIA 树与点击前逐字相同）。
- ⚠ **上一批那句「一次普通 click 产出选区」是我读错了**：`SizeText` 会在**只悬停到候选窗**时就出现（`UpdateDetected`
  里就 `DrawSelection(cand)`），它⛔ 等于「选区已确认」。最后一跑按「提示句有没有从『拖动框选…』换成标注态那句」
  复核＝提示句一字未改 ⇒ 那颗 `Ctrl+S` 落进 `Commit` 的 `_selection is null` 分支＝**正当的取消**（`Settle(null)`），
  所以「按了存图没反应」⛔ 是缺陷。要判存图必须先把选区**确认**下来。
- 取消那一格（量到）：遮罩关掉之后 profile 里 **0 颗 PNG・`StarMark Screenshots` 目录也没建出来**；
  他的真 Pictures 今天 0 颗新 PNG（那颗 `StarMark 截图` 是旧名遗留）⇒ 沙盒⛔ 漏写他的数据。
- ⛔ 做到的：一次真存图（`capture.card.savedTitle` 那两句）・`saveWriteFailed` 造型・三语屏幕读数・en／ja 两跑。
  ⛔ 跑 Enter／Ctrl+C（不写他的剪贴板）・⛔ 动 ACL。
- ⚠ 归责：日志里 11:38:39–11:41:04 有 40+ 行「设置已保存」＋主题/材质来回切（`Light`↔`Default`、Acrylic/Solid/Mica/
  AcrylicBase），**那些页我一页都没点过**；遮罩冻帧里还有一颗输入法候选窗与 Qoder 那颗「正在使用你的电脑」横幅。
  ⇒ 那段时间有人在这颗沙盒实例上动手。所有「拖框没反应」的读数都⛔ 能排除这一层，只⛔ 记成现象。

## 四、接续顺序（下一腿怎么做才对）

1. **先把选区确认下来**，两条已读码确认的路：
   (a) 让鼠标先停在某颗候选窗上（要有一颗 Moved）**再**按下＋放开 ⇒ `BeginCandidatePress` 预填＋放开即确认；
   (b) 按下→移动→放开（真拖框）。SDK 的 `drag` 这一跑⛔ 送出可用的 Moved，所以 (a) 更现实：
   可以先 `click` 一次（拿候选高亮）→ 再 `click` 同一颗（`再点一次`＝确认）→ 读提示句是否已换成标注态那句 ⇒ 才动 `Ctrl+S`。
   判据＝**提示句变了** ＋ 工具条那颗独立置顶窗出现（它同样只从主窗的相关截图里看得见）。
2. 存图成功那一格：读 `StarMark Screenshots` 里那颗 PNG 的真尺寸＋提示卡那两句（`capture.card.savedTitle`／正文带路径）。
3. `saveWriteFailed` 造型：在预测的那颗 PNG 路径上**先占一颗同名目录**（`File.Exists` 为假・`File.Create` 才失败），
   ⛔ 用「把根目录做成一颗档」那副造型（它只会走异常那支，量不到那颗键）。
4. 换 en／ja 各跑一遍（先干净关掉 zh・确认那颗单实例互斥已放开）。
5. 全量再称一次（本批只跑过定向与那一族），然后小步提交、更新进度报告与记忆。
6. ⛔ 执行 `D:\StarMarkTmp\p131\stage131.py`／`run131.py`／`uia_131.ps1`／`run_all.cmd`（旧的那几支会动剪贴板、
   改 ACL、删 PNG、走 PowerShell UIA）。
7. 归他那一半照旧：观感与措辞的点验、✅P-217 那颗 en 复数名单。
