# Phase C 片102 交接快照（2026-10-25・⛔ 提交）

## 一、本批状态

- **片102＝P-251 三半收口——新克隆那把尺进常跑闸门**（结案入口三半全交齐；翻牌腿另批拍板、本批零翻牌）：
  - **① 覆盖逐档量全**：探测尺 probe102_attr.py（正本 p102_attr_r1.txt・VERDICT OK）把仓内被「清单＋字节对账」钉住的树量全——只有 assets/boards（141 颗含清单本档）与 tools/boards（5 颗）两棵：check-attr text 146 颗全 unset（-text 的报法・没圈到报 unspecified）・盘上字节对 HEAD blob 146 颗全相等（hash-object --no-filters 对 ls-tree 的 blob sha）・清单 140 项零不符・主人对逐字节相等・两树 status 零漂移。另两类读字节的闸门不属这口径（图标族＝ICO 容器形状・安装器族＝BOM/CRLF 编码形状）；docs/** 混排存量照 ① 原文 ⛔ 碰。
  - **② 尺进常跑闸门**：新档 tests/StarMark.Tests/BoardsFreshCheckoutFidelityGateTests.cs 三格——worktree add --detach 新落干净工作树走真转换管线（⛔ git show／git archive＝不走转换量不到）・新树逐档对它自己的 manifest（bytes＋sha256）＋主人两档复称・逐颗 check-attr unset 棘轮（机制面：autocrlf 关着的机上删掉 -text 第一格照绿、这格照样红）・口径句钉在 .gitattributes。先红后绿存证：test102-prered_r1.txt 失败 1／通过 2 → test102-gate3_r1.txt 3／3。
  - **③ 口径**：.gitattributes 头注新增两行（629→904 字节・纯 LF 保持）——凡「清单＋字节对账」的资产树，随包之前必须有一把新克隆尺；那把尺是常跑闸门 BoardsFreshCheckoutFidelityGateTests。
- 落地：apply102.py（dry→--go・四刀）——账本 P-251 节收口行＋⑳ 三行（1629475→1631650 字节・CRLF 8036→8041・孤 CR 13390/LF 79 不动）＋报告 §四百八十四（2719167→2722309・CRLF 21154→21168・孤 CR 55828 不动）。
- **全量新基线＝6491／0**（test102-full_r1.txt・9m34s）：片97 的 6487＋片99 census 新标 1＋本批三格 3＝6491，＋4 归属链条已核。
- HEAD `102bd8a`（三笔：`9f94897` 闸门三格＋.gitattributes 口径／`357cd4d` 账本收口行＋⑳／`102bd8a` 报告 §四百八十四）・未 push 230・⛔ push（push 一事一授权）。
- 工作区只剩 docs/开发交接-PhaseC-片93/95/96/97/98/99/100/101/102.md 九份交接快照未跟踪（⛔ 提交）。
- 记忆回写已完成（memwrite102.py・dry 后 --go）：board-core 片102 节（383416→385911 字节・LF 1367→1375）・living-pointer-p256 片102 补记（26005→26710・LF 213→218）・MEMORY.md 38/39 行索引（13450→13564・LF 40 不变）——三档全纯 LF 先验原始字节再 decode。

## 二、读数要点（正本全在 `bin/WI99/p102_r1/`・⛔ 提交）

- 探测：p102_recon_r1.txt（P-251 节 \n 行 7934..7941・账本/报告/.gitattributes 三档字节与 EOL 普查）＋p102_attr_r1.txt（① 的 VERDICT OK 正本）。
- 先红后绿：test102-prered_r1.txt（口径句未进 .gitattributes 时第三格当场红・失败 1／通过 2）→ test102-gate3_r1.txt（后绿 3／3）。
- 计数器 **257／126／131 VERDICT OK**（count102_r1.txt・零翻牌・不动）；闸门族 **39／0**（test102-gate_r1.txt・25s・新测试文本过全部红线）；全量 **6491／0**（test102-full_r1.txt・9m34s）。
- 提交读数：commit102-out.txt（三笔 exit=0・log -3・status・未 push 230）；memwrite102-dry.txt／memwrite102-go.txt（三档字节/LF 前后账）。
- 正本清单：probe102_recon.py・probe102_attr.py・apply102.py・commit102.py・memwrite102.py＋p102_recon_r1.txt・p102_attr_r1.txt・apply102-dry.txt・apply102-go.txt・test102-prered_r1.txt・test102-gate3_r1.txt・test102-gate_r1.txt・count102_r1.txt・test102-full_r1.txt・commit102-out.txt・memwrite102-dry.txt・memwrite102-go.txt

## 三、接手就照这个顺序走（命令原文，⛔ 自写包装脚本）

每条先设临时目录：`$env:TEMP='D:\StarMarkTmp'; $env:TMP='D:\StarMarkTmp'; $env:DOTNET_CLI_HOME='D:\StarMarkTmp'`

1. 闸门族（期望 失败 0／通过 39／总 39・**跑一次换一颗 TAG 档名 ⛔ 覆盖证据**）：
   `cmd /c "dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --filter FullyQualifiedName~Pointer^|FullyQualifiedName~CrossProjectEvidence^|FullyQualifiedName~LedgerPointer > bin\WI99\p102_r1\test102-gate_r2.txt 2>&1"`
2. 新克隆尺三格（期望 失败 0／通过 3／总 3・worktree 真转换・走 Path.GetTempPath() scratch）：
   `cmd /c "dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --filter FullyQualifiedName~BoardsFreshCheckoutFidelityGateTests > bin\WI99\p102_r1\test102-gate3_r2.txt 2>&1"`
3. 计数器（**基线 257／126／131**・VERDICT OK・换 TAG 再跑）：
   `cmd /c "python -X utf8 bin\WI37C\count_ledger.py > bin\WI99\p102_r1\count102_r2.txt 2>&1"`
   （⚠ PS 5.1 的 `>` 把 stdout 落成 UTF-16；要裸 UTF-8 无 BOM 正本必须走 cmd /c 重定向）
4. 全量（期望 **6491／0**・≈10 分钟・超时 1200000ms）：
   `dotnet test StarMark.sln -c Debug -p:Platform=x64 -v m`

## 四、Phase C 还开着的（收官剩两腿）

- **P-248（出机三道）**：三道全装齐、真机八臂量过（片82）；剩结案入口两格照片——**F1 共存**、**那颗按钮不存在的那张照片**——共用一条真机腿（第 0 道核对＝他此刻在不在用电脑）。
- **P-251（冻结树行尾）**：结案入口三半本批已全收口（①②③・读数见上）；**剩翻牌腿待拍板**（沿 P-256 收口批≠翻牌批节奏・零翻牌 257／126／131）。
- P-249 ⑩ 在册两颗「⛔ 动判据・只登记」未验证项（L3 held-by＝台架 A1・boards.open.failed 本机造不出现场）照旧不动——归 P-249。
- （另三颗开放条目 P-209／P-210／P-217 系 WI 批次旧账、非 Phase C。）

## 五、红线（逐条都是用户逐字给过的约束，⛔ 自行放宽）

- ⛔ push；⛔ 中文分支名；仪器产物落 `bin\WI99\…`（⛔ 存 C 盘・记忆回写除外）；跑一次换一颗 TAG 档名（⛔ 覆盖上一跑证据）
- ⛔ 自写「一条命令」包装脚本；跑/构建用官方 dotnet 命令；每颗数由仪器打印 ⛔ 手打；搬尺要连形状一起搬（坑表 #978・#980）
- docs ⛔ 删历史证据、⛔ 放宽判据凑零；「照旧欠」清单是活账不是模板（坑表 #981）
- CJK 档名 ⛔ 从 Bash argv 递（git add 用 python subprocess 传 unicode argv）；提交用 `git commit -F`＋D:\StarMarkTmp 下 UTF-8 无 BOM 临时档；交接快照与 bin 仪器 ⛔ 提交
- **计数器基线＝257／126／131 VERDICT OK**（零翻牌・不动）；闸门族基线＝**39／0**；**全量新基线＝6491／0**（6487＋片99 census 1＋片102 三格 3・归属已核）；新克隆尺三格基线＝3／0
- 新闸门文本红线：零 `名.cs:号`・零 `第\d+行`・零 `<c>:N</c>`/`<c>N～M</c>` 裸简写・零 .cs/.xaml/.ps1/.sln 提名（LedgerPointer 只认这四类）；账本/报告里指名测试用裸类名 `BoardsFreshCheckoutFidelityGateTests`（沿 `LivingSourcePointerGateTests` 先例）；CrossProjectEvidence 只扫 src/
- 新克隆尺正本形状＝`git worktree add --detach` 走真转换管线（沿 clone71_check.py）；⛔ git show／git archive（不走转换量不到病）；worktree scratch 用 `Path.GetTempPath()＋Guid`・finally 清场
- xUnit 陷阱：`Assert.Equal(string,string,message)` 无此重载（CS1503）——用 `Assert.True(x.SequenceEqual(y), msg)`
- 账本孤 CR 混排（孤 CR 13390）⇒ grep/rg 的 \n 行号与 read 工具行号不映射；找节内容用 python 探测尺
- ⛔ 混分母：闸门 FallenPointer 34／census95 落空 16／⑦ 89 颗／跨库缺档 47／第三档散文颗口径 41 各是各的账；颗口径 41 ↔ 登记行颗口径 35 ⛔ 互相对账
- 每条 dotnet 命令先设 `TEMP/TMP/DOTNET_CLI_HOME=D:\StarMarkTmp`
