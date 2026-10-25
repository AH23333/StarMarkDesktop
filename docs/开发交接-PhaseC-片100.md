# Phase C 片100 交接快照（2026-10-25・⛔ 提交）

## 一、本批状态

- **片100＝幻影更正批（用户拍板）——⑰ 登记的「照旧 ⛔ 闭」唯一剩项「片87 真机腿 (B) 那半格」查证后系幻影错账**：
  - 查证正件：P-249 标题＝`✅P-249（…片87 两半都落地・已结案）`；账本 P-249 ⑧＝(B) 三问读数（点带链元素只选中 ⛔ 开链・点气泡 URL ⇒ accepted=True｜opened=True＋虚站 GET=1・file:/// 支 rejected=True 屏上句当场 1 颗≈9.5 秒自退・画布 recover=0）・⑨＝r9–r12 旧装配坑・⑩＝「(B) 已量＝两半都落地⇒本条转 ✅」；git 证实结案提交 `4db151c`（2026-10-24 03:30・片87 真机腿）；报告 §四百六十九；正本 `bin/WI99/p249_r1/board87-读数-r14a1.txt`（尺 sha 1fc2164dd70d・装配 StarMark.UI.dll 前缀 df91953c33b6・四臂 BAD=0）。
  - 幻影全账（坑表 #981）：board-core 记忆档片90～99 十行＋living-pointer 片93～99 七行＋账本 ⑫～⑰ 六行＋报告 §476～§481 六行——没有一颗清单行回头跟 P-249 正本对过账；该腿属 P-249、从来不是 P-256 的欠项（清单行跨条目收编）。
  - 处置：账本 P-256 ⑱ 三行就地登记更正（⑫～⑰ 历史行一字不动）＋坑表 #981＋报告 §四百八十二；「照旧 ⛔ 闭」清单**清空**；⛔ 重跑真机腿（为已结案条目造第二份重复证据）。
- 落地：apply100.py 三档末尾二进制拼接（⑰ 锚 count==1・三档尾锚 endswith・#981/⑱/§482 预检零占位・孤 CR/LF 不动）；两笔提交（CJK add 走 python subprocess・commit -F D:\StarMarkTmp UTF-8 无 BOM）。
- HEAD `926cfbb`（两笔：`f315a79` 账本 ⑱＋坑表 #981／`926cfbb` 报告 §四百八十二）・未 push 225・⛔ push（push 一事一授权）。
- 工作区只剩 docs/开发交接-PhaseC-片93/95/96/97/98/99/100.md 七份交接快照未跟踪（⛔ 提交）。
- 记忆回写已完成（memwrite100.py）：board-core 片100 节（幻影更正・379741→382161 字节）・living-pointer-p256 片100 补记（24068→25028 字节）・MEMORY.md 38/39 行索引更新（三档全纯 LF 验过・先验字节后 decode）。

## 二、读数要点（正本全在 `bin/WI99/p100_r1/`・⛔ 提交）

- apply100.py→apply100-读数.txt（dry 后 --go）：账本 1624863→1627590 字节・CRLF 8028→8032・孤 CR 13390/LF 79 不动；坑表 1576226→1577808・CRLF 10202→10208・孤 CR 12953 不动；报告 2713909→2716730・CRLF 21133→21144・孤 CR 55828 不动。
- 两腿：闸门 39／0（test100-gate_r1.txt・25s）・计数器 257/125/132 VERDICT OK（count100_r1.txt）。本批零码改零测试改、只动三份文档 ⇒ 不重跑全量（沿片95/98/99 先例・上一全量读数＝片97 的 6487／0）。
- 正本清单：apply100.py・apply100-读数.txt・count100_r1.txt・test100-gate_r1.txt・memwrite100.py・memwrite100-读数.txt

## 三、接手就照这个顺序走（命令原文，⛔ 自写包装脚本）

每条先设临时目录：`$env:TEMP='D:\StarMarkTmp'; $env:TMP='D:\StarMarkTmp'; $env:DOTNET_CLI_HOME='D:\StarMarkTmp'`

1. 闸门族（期望 失败 0／通过 39／总 39・**跑一次换一颗 TAG 档名 ⛔ 覆盖证据**）：
   `cmd /c "dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --filter FullyQualifiedName~Pointer^|FullyQualifiedName~CrossProjectEvidence^|FullyQualifiedName~LedgerPointer > bin\WI99\p100_r1\test100-gate_r2.txt 2>&1"`
   （在 cmd /c 里 `|` 用 `^|` 转义免内层引号——PS 5.1 嵌套引号会被截断；直接在 PS 里跑则 `|` 必须带引号）
2. 计数器（期望 257／125／132・VERDICT OK・换 TAG 再跑）：
   `cmd /c "python -X utf8 bin\WI37C\count_ledger.py > bin\WI99\p100_r1\count100_r2.txt 2>&1"`
   （⚠ PS 5.1 的 `>` 把 stdout 落成 UTF-16；要裸 UTF-8 无 BOM 正本必须走 cmd /c 重定向）
3. 全量（期望 6487／0・≈10–17 分钟・超时 1200000ms）：
   `dotnet test StarMark.sln -c Debug -p:Platform=x64 -v m`

## 四、P-256 剩下的欠项（本批起＝无）

- 「照旧 ⛔ 闭」清单已于片100（账本 ⑱）**清空**——那颗「片87 真机腿 (B)」系幻影错账（P-249 早已 `4db151c` 量毕转 ✅）。
- 仍在册（归 P-249、非 P-256 欠项・「⛔ 动判据・只登记」照旧不动）：P-249 ⑩ 两颗未验证项——「那句坏话被已在场的那一句挡住」那一格（机检证人＝台架 A1）・「系统里没有能处理 http 的默认程序」那一支（boards.open.failed・本机造不出现场）。
- 下一批内容待用户拍板（Phase C 收官与否先问）。

## 五、红线（逐条都是用户逐字给过的约束，⛔ 自行放宽）

- ⛔ push；⛔ 中文分支名；仪器产物落 `bin\WI99\…`（⛔ 存 C 盘・记忆回写除外）；跑一次换一颗 TAG 档名（⛔ 覆盖上一跑证据）
- ⛔ 自写「一条命令」包装脚本；跑/构建用官方 dotnet 命令；每颗数由仪器打印 ⛔ 手打；搬尺要连形状一起搬（坑表 #978・#980）
- docs ⛔ 删历史证据、⛔ 放宽判据凑零；历史快照拿今天的存在性去罚昨天的话＝逼下一手改写证据（坑表 #160 口径）
- **「照旧欠」清单是活账不是模板（坑表 #981）**：每批照抄前先跟它指的那条记录的正本对一次账；清单行 ⛔ 跨条目收编（P-256 的清单只放 P-256 的欠项）；发现幻影就地登记更正、历史清单行一字不动
- CJK 档名 ⛔ 从 Bash argv 递（git add 用 python subprocess 传 unicode argv）；提交用 `git commit -F`＋D:\StarMarkTmp 下 UTF-8 无 BOM 临时档；交接快照与 bin 仪器 ⛔ 提交
- 计数器三颗数（257/125/132）⛔ 变（新立/翻牌前先跑计数器现读）；闸门新基线＝39／0（FallenPointer 34・PrefixBlind 0・Ambiguous 0・ProsePointer 10・BareShorthand 35/16・第三档散文 17/24/0）
- ⛔ 混分母：闸门 FallenPointer 34／census95 落空 16／⑦ 89 颗／跨库缺档 47／第三档散文颗口径 41 各是各的账；颗口径 41 ↔ 登记行颗口径 35 ⛔ 互相对账
- 活档正文引文纪律：在的指针留 `名:号` 形状、落空/缺档一律去形（「N 号」）；⛔ 写「第 N 行」（带数字的形状）・⛔ <c> 包裹裸号・⛔ 全角冒号＋号的落空形状——否则棘轮红
- 每条 dotnet 命令先设 `TEMP/TMP/DOTNET_CLI_HOME=D:\StarMarkTmp`
