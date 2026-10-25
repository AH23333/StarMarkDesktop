# Phase C 片99 交接快照（2026-10-25・⛔ 提交）

## 一、本批状态

- **片99＝⑮ 登记的「照旧 ⛔ 闭」第二笔——⑪①② 两问裁决并收口（两问均用户拍板）**：
  - ① 第三档散文「第 N 行」＝**只钉总颗**——闸门新格 `TheThirdTierProseCensusStaysPinned`（`ThirdTier`＝报告/坑表/双库审计三档・`ThirdTierProseBaseline={17,24,0}`・颗口径・LF 口径〔名册 407〕・守卫 `Length==3` 双检）。全套镜像 ⛔（41 颗逐颗读回全是历史叙述且全部未带指称・三副 ProseMarkers 在第三档盖住 0 行・清零＝改写历史证据）。先决「真行切分」实测消解＝LF 与真行两口径十格全零差。
  - ② 三档 .cs 落空族＝**不装**——今日现读报告 25（登记 18・+7 逐颗归因全出自 §476–§480 合法历史叙述）・坑表 7・审计 3 零漂移；落空随合法叙述自然增长，钉上限＝与 ⑬ 整档登记不罚打架；「照旧 ⛔ 删历史证据・⛔ 修」维持。
  - **口径之别记死（账本 ⑰）**：登记读数 35＝probe93b 行颗口径（含散文的行数・一行两颗算一）；闸门是颗口径＝41（一行两颗算二・四颗双颗行）。两口径 ⛔ 许互相拿去对账（坑 #972 假差族）。
- 落地：账本 ⑰ 三行＋⑪ 末就地补〔片99 更正〕（apply99.py 二进制拼接・⑪ 锚 count==1・两档尾锚 endswith・孤 CR/LF 不动）；报告 §481；闸门 `8465fef`。
- HEAD `5f6a32a`（三笔：`8465fef` 闸门新格／`377c054` 账本 ⑰／`5f6a32a` 报告 §四百八十一）・未 push 223・⛔ push（push 一事一授权）。
- 工作区只剩 docs/开发交接-PhaseC-片93/95/96/97/98/99.md 六份交接快照未跟踪（⛔ 提交）。
- 记忆回写已完成（memwrite99.py）：board-core 片99 节・living-pointer-p256 片99 补记（新棘轮 17/24/0）・MEMORY.md 两行索引（全纯 LF 验过）。

## 二、读数要点（正本全在 `bin/WI99/p999_r1/`・⛔ 提交）

- probe99.py→probe99-读数.txt（五段）：① 正本判据全从 `LivingSourcePointerGateTests.cs` 解析（Prose 40 字・Pointer 67 字・3 副 ProseMarkers・Living 2 颗・基线 10/34）⛔ 手打；② daaec63（片93 探针时点树）逐 blob 移植自证——⑪ 登记读数逐格复现・对不上 0 格；③ 今日现读（报告 .cs 落空 18→25・在场 253→263・认不到 46→50；坑表 7/53/15・审计 3/30/4 零漂移；活档 10/0・34 复现）；④ 真行切分敏感度——LF 与真行两口径十格全零差・孤 CR 粘行（报告 91.2%・坑表 63.7%・最长 1534 字）不咬；⑤ 41 颗逐颗现场。
- probe99b.py（blob 定位辅探针）＋probe99c.py→probe99c-读数.txt：报告 .cs +7 逐颗归因＝六颗新引死指针＋一颗复引，全出自 §476–§480 合法历史叙述。
- apply99.py→apply99-读数.txt：账本 1621903→1624863 字节・CRLF 8024→8028・孤 CR 13390/LF 79 不动；报告 2711333→2713909・CRLF 21122→21133・孤 CR 55828 不动。
- 两腿：闸门 39／0（test99-gate_r1.txt・32s・新格 +1）・计数器 257/125/132 VERDICT OK（count99_r1.txt）。本批动闸门一颗测试档＋两份文档 ⇒ 不重跑全量（沿片95/98 先例・上一全量读数＝片97 的 6487／0）。
- 正本清单：probe99.py・probe99-读数.txt・probe99b.py・probe99c.py・probe99c-读数.txt・apply99.py・apply99-读数.txt・test99-gate_r1.txt・count99_r1.txt・memwrite99.py

## 三、接手就照这个顺序走（命令原文，⛔ 自写包装脚本）

每条先设临时目录：`$env:TEMP='D:\StarMarkTmp'; $env:TMP='D:\StarMarkTmp'; $env:DOTNET_CLI_HOME='D:\StarMarkTmp'`

1. 闸门族（期望 失败 0／通过 39／总 39・**跑一次换一颗 TAG 档名 ⛔ 覆盖证据**）：
   `cmd /c "dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --filter FullyQualifiedName~Pointer^|FullyQualifiedName~CrossProjectEvidence^|FullyQualifiedName~LedgerPointer > bin\WI99\p999_r1\test99-gate_r2.txt 2>&1"`
   （在 cmd /c 里 `|` 用 `^|` 转义免内层引号——PS 5.1 嵌套引号会被截断；直接在 PS 里跑则 `|` 必须带引号）
2. 计数器（期望 257／125／132・VERDICT OK・换 TAG 再跑）：
   `cmd /c "python -X utf8 bin\WI37C\count_ledger.py > bin\WI99\p999_r1\count99_r2.txt 2>&1"`
   （⚠ PS 5.1 的 `>` 把 stdout 落成 UTF-16；要裸 UTF-8 无 BOM 正本必须走 cmd /c 重定向）
3. 全量（期望 6487／0・≈10–17 分钟・超时 1200000ms）：
   `dotnet test StarMark.sln -c Debug -p:Platform=x64 -v m`

## 四、P-256 剩下的欠项（⛔ 判 ⛔ 动，登记在账本 ⑰）

- 片87 真机腿 (B) 那半格（**第 0 道核对＝他此刻在不在用电脑**；起沙盒前先按 ExecutablePath 数别人的实例）
- （⑪①② 本批闭合；id6 片98 闭合；⑦ 89 颗与跨库缺档 47 片97 结清）

## 五、红线（逐条都是用户逐字给过的约束，⛔ 自行放宽）

- ⛔ push；⛔ 中文分支名；仪器产物落 `bin\WI99\…`（⛔ 存 C 盘・记忆回写除外）；跑一次换一颗 TAG 档名（⛔ 覆盖上一跑证据）
- ⛔ 自写「一条命令」包装脚本；跑/构建用官方 dotnet 命令；每颗数由仪器打印 ⛔ 手打；搬尺要连形状一起搬（坑表 #978・#980）
- docs ⛔ 删历史证据、⛔ 放宽判据凑零；历史快照拿今天的存在性去罚昨天的话＝逼下一手改写证据（坑表 #160 口径）
- CJK 档名 ⛔ 从 Bash argv 递（git add 用 python subprocess 传 unicode argv）；提交用 `git commit -F`＋D:\StarMarkTmp 下 UTF-8 无 BOM 临时档；交接快照与 bin 仪器 ⛔ 提交
- 计数器三颗数（257/125/132）⛔ 变（新立/翻牌前先跑计数器现读）；闸门新基线＝39／0（FallenPointer 34・PrefixBlind 0・Ambiguous 0・ProsePointer 10・BareShorthand 35/16・**第三档散文 17/24/0**）
- ⛔ 混分母：闸门 FallenPointer 34／census95 落空 16／⑦ 89 颗／跨库缺档 47／第三档散文颗口径 41 各是各的账；颗口径 41 ↔ 登记行颗口径 35 ⛔ 互相对账
- 活档正文引文纪律：在的指针留 `名:号` 形状、落空/缺档一律去形（「N 号」）；⛔ 写「第 N 行」（带数字的形状）・⛔ <c> 包裹裸号・⛔ 全角冒号＋号的落空形状——否则棘轮红
- 每条 dotnet 命令先设 `TEMP/TMP/DOTNET_CLI_HOME=D:\StarMarkTmp`
