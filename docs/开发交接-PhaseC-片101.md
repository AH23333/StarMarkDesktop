# Phase C 片101 交接快照（2026-10-25・⛔ 提交）

## 一、本批状态

- **片101＝收官腿——P-256 翻牌结案**：「照旧 ⛔ 闭」清单自 ⑱（片100）起已空，本批把题上那颗「开放」摘掉、就地翻牌 ✅。
  - 题行整行换（行首锚「## P-256（开放」→ 行尾、＝之后那段逐字保留）：旧题 `## P-256（开放·片85 立·片86 校准）＝…` → 新题 `## ✅P-256（片85 立·片86 校准·片88～100 逐笔收口·片101 翻牌·<b>已结案</b>）＝…`。
  - 结案入口三档逐档核过全交齐：① 复称尺＋旧候选逐颗定性（id6 片98 闭合）・② 本仓号优先（片88 八颗换锚打头＋片91～97 逐笔收口）・③ 口径＋必红臂（「当场复称得到」＋两面棘轮＋文档闸门族）。
  - 仍在册两颗「⛔ 动判据・只登记」未验证项住 P-249 ⑩（L3 held-by＝台架 A1・boards.open.failed 本机造不出现场）——归 P-249、⛔ 属本条欠项。
- 落地：账本 ⑲ 三行＋题行翻牌＋报告 §四百八十三（apply101.py・尾锚 endswith・题行锚唯一・孤 CR/LF 不动）。
- HEAD `be218c6`（两笔：`2bc4757` 账本 ⑲＋翻牌／`be218c6` 报告 §四百八十三）・未 push 227・⛔ push（push 一事一授权）。
- 工作区只剩 docs/开发交接-PhaseC-片93/95/96/97/98/99/100/101.md 八份交接快照未跟踪（⛔ 提交）。
- 记忆回写已完成（memwrite101.py）：board-core 片101 节・living-pointer-p256 片101 补记（✅已结案）・MEMORY.md 38/39 行索引（三档全纯 LF 验过）。

## 二、读数要点（正本全在 `bin/WI99/p101_r1/`・⛔ 提交）

- apply101.py→apply101-读数.txt（dry 后 --go）：账本 1627590→1629475 字节・CRLF 8032→8036・孤 CR 13390/LF 79 不动；报告 2716730→2719167・CRLF 21144→21154・孤 CR 55828 不动。
- **翻牌后计数器新基线＝257／126／131 VERDICT OK**（count101_r1.txt・带 ✅ +1・不带 −1・等式照旧）。
- 闸门族 39／0（test101-gate_r1.txt・43s）。本批零码改零测试改、只动两份文档 ⇒ 不重跑全量（沿片95/98/99/100 先例・上一全量读数＝片97 的 6487／0）。
- 正本清单：apply101.py・apply101-读数.txt・count101_r1.txt・test101-gate_r1.txt・memwrite101.py・memwrite101-读数.txt

## 三、接手就照这个顺序走（命令原文，⛔ 自写包装脚本）

每条先设临时目录：`$env:TEMP='D:\StarMarkTmp'; $env:TMP='D:\StarMarkTmp'; $env:DOTNET_CLI_HOME='D:\StarMarkTmp'`

1. 闸门族（期望 失败 0／通过 39／总 39・**跑一次换一颗 TAG 档名 ⛔ 覆盖证据**）：
   `cmd /c "dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --filter FullyQualifiedName~Pointer^|FullyQualifiedName~CrossProjectEvidence^|FullyQualifiedName~LedgerPointer > bin\WI99\p101_r1\test101-gate_r2.txt 2>&1"`
2. 计数器（**新基线 257／126／131**・VERDICT OK・换 TAG 再跑）：
   `cmd /c "python -X utf8 bin\WI37C\count_ledger.py > bin\WI99\p101_r1\count101_r2.txt 2>&1"`
   （⚠ PS 5.1 的 `>` 把 stdout 落成 UTF-16；要裸 UTF-8 无 BOM 正本必须走 cmd /c 重定向）
3. 全量（期望 6487／0・≈10–17 分钟・超时 1200000ms）：
   `dotnet test StarMark.sln -c Debug -p:Platform=x64 -v m`

## 四、Phase C 还开着的两颗（收官剩下两笔）

- **P-248（出机三道）**：三道全装齐（片67～71 CSP／出站观察者／validateEmbeddable／NewWindowRequested／顶帧导航）、真机八臂量过（片82）；剩结案入口两格照片——**F1 共存**、**那颗按钮不存在的那张照片**（关窗 60 秒进程树那格片82 已量到）——共用一条真机腿（第 0 道核对＝他此刻在不在用电脑）。
- **P-251（冻结树行尾）**：已修那半＝.gitattributes 只圈随包两族取 -text（4 颗不等→0）；还开三半——① `-text` 扩到每棵被 sha 或字节对账钉住的产物（一档一档量・docs 存量 ⛔ 碰）・② 那颗新克隆尺进发布收口那一族（现在只在 bin/WI98 手动跑）・③ 定口径：凡「清单＋字节对账」的资产树，随包之前必须有一把新克隆尺。
- （另三颗开放条目 P-209／P-210／P-217 系 WI 批次旧账、非 Phase C。）

## 五、红线（逐条都是用户逐字给过的约束，⛔ 自行放宽）

- ⛔ push；⛔ 中文分支名；仪器产物落 `bin\WI99\…`（⛔ 存 C 盘・记忆回写除外）；跑一次换一颗 TAG 档名（⛔ 覆盖上一跑证据）
- ⛔ 自写「一条命令」包装脚本；跑/构建用官方 dotnet 命令；每颗数由仪器打印 ⛔ 手打；搬尺要连形状一起搬（坑表 #978・#980）
- docs ⛔ 删历史证据、⛔ 放宽判据凑零；历史快照拿今天的存在性去罚昨天的话＝逼下一手改写证据（坑表 #160 口径）
- **「照旧欠」清单是活账不是模板（坑表 #981）**：每批照抄前先跟它指的那条记录的正本对一次账；清单行 ⛔ 跨条目收编；发现幻影就地登记更正、历史清单行一字不动
- CJK 档名 ⛔ 从 Bash argv 递（git add 用 python subprocess 传 unicode argv）；提交用 `git commit -F`＋D:\StarMarkTmp 下 UTF-8 无 BOM 临时档；交接快照与 bin 仪器 ⛔ 提交
- **计数器新基线＝257／126／131**（片101 翻牌后・count101_r1.txt）；闸门基线＝39／0（FallenPointer 34・PrefixBlind 0・Ambiguous 0・ProsePointer 10・BareShorthand 35/16・第三档散文 17/24/0）
- ⛔ 混分母：闸门 FallenPointer 34／census95 落空 16／⑦ 89 颗／跨库缺档 47／第三档散文颗口径 41 各是各的账；颗口径 41 ↔ 登记行颗口径 35 ⛔ 互相对账
- 活档正文引文纪律：在的指针留 `名:号` 形状、落空/缺档一律去形（「N 号」）；⛔ 写「第 N 行」（带数字的形状）・⛔ <c> 包裹裸号・⛔ 全角冒号＋号的落空形状——否则棘轮红
- 每条 dotnet 命令先设 `TEMP/TMP/DOTNET_CLI_HOME=D:\StarMarkTmp`
