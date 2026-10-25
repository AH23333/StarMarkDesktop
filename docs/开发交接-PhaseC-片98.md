# Phase C 片98 交接快照（2026-10-24・⛔ 提交）

## 一、本批状态

- **片98＝P-256 ⑮ 登记的「照旧 ⛔ 闭」第一笔——那颗 id6 归属收口**：P-15「可机检定位」里的「EditTagsAsync 分支」系首次登记即误标——本仓从来不存在叫 EditTagsAsync 的方法（git grep 全仓唯一命中＝账本那句自己・src 限定 HEAD 与 2398688 均零）；登记时点 739-740 号实为 ReplaceLocalItemsForInstanceAsync（同档 681 号起）的标签循环。
- 处置＝沿 P-10／P-15(SK-0)／P-20／P-35 先例就地更正：P-15 末补〔片98 更正〕注（历史原文一字不动＋现址锚 `ItemRepository.Snapshots.cs:104-105`・`ItemRepository.cs:174-177`）＋账本 ⑯ 三条＋报告 §480。误名只伤出处标注、不伤定性（唯 AddTagAsync 裸进、兄弟路径皆先归一・今日逐处复称仍成立）。
- HEAD `1283f01`（两笔：`57d035e` 账本 ⑯／`1283f01` 报告 §四百八十）・未 push 220・⛔ push（push 一事一授权）。
- 工作区只剩 docs/开发交接-PhaseC-片93/95/96/97/98.md 五份交接快照未跟踪（⛔ 提交）。
- 记忆回写已完成：board-core 片98 节・living-pointer-p256 片98 补记・MEMORY.md 索引两行（全纯 LF 验过）。

## 二、读数要点（正本全在 `bin/WI99/p998_r1/`・⛔ 提交）

- probe98.txt（只读探针・五段）：① EditTagsAsync 今日树 src/tests/tools/scripts 零命中・git grep HEAD 与 2398688 全仓唯一命中＝账本那句自己・src 限定均零；② 2398688 ItemRepository.cs（1098 行）逐行归属：236＝AddTagAsync・245/255-256＝其 SQL・681＝ReplaceLocalItemsForInstanceAsync・739-740＝其拒空＋Trim・884-887＝UpsertOne Trim 循环；③ 今日现址：Tags.cs 64 号（查询 73-74・INSERT 83-84）／Snapshots.cs 104-105 号／ItemRepository.cs 174-177 号／BackupRepository.cs 166-167 号逐字仍准；④ review 三份合计 85・unresolved 恰 1＝id6（proposedOld/proposedNew 均 null）；⑤ apply 前置锚点自检全过（拼接锚唯一・两档尾锚在）。
- apply98.py＝中段二进制拼接（拼接锚「也不升格。\r\r\r\n」全档唯一・锚后随 `\r\r\r\n## P-16` 验过）＋末尾追加；账本 1619249→1621903 字节・CRLF 8017→8024・孤 CR/LF (13390, 79) 不动；报告 2709128→2711333・CRLF 21111→21122。apply98-读数.txt＝--go 正本。
- 两腿：闸门 38／0（test98-gate_r1.txt・25s）・计数器 257/125/132 VERDICT OK（count98_r1.txt）。本批零码改零测试改 ⇒ 不重跑全量（沿片95 先例・上一全量读数＝片97 的 6487／0）。
- 正本清单：probe98.py・probe98.txt・apply98.py・apply98-读数.txt・test98-gate_r1.txt・count98_r1.txt

## 三、接手就照这个顺序走（命令原文，⛔ 自写包装脚本）

每条先设临时目录：`$env:TEMP='D:\StarMarkTmp'; $env:TMP='D:\StarMarkTmp'; $env:DOTNET_CLI_HOME='D:\StarMarkTmp'`

1. 闸门族（期望 失败 0／通过 38／总 38・**跑一次换一颗 TAG 档名 ⛔ 覆盖证据**）：
   `cmd /c "dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --filter FullyQualifiedName~Pointer^|FullyQualifiedName~CrossProjectEvidence^|FullyQualifiedName~LedgerPointer > bin\WI99\p998_r1\test98-gate_r2.txt 2>&1"`
   （在 cmd /c 里 `|` 用 `^|` 转义免内层引号——PS 5.1 嵌套引号会被截断；直接在 PS 里跑则 `|` 必须带引号）
2. 计数器（期望 257／125／132・VERDICT OK・换 TAG 再跑）：
   `cmd /c "python -X utf8 bin\WI37C\count_ledger.py > bin\WI99\p998_r1\count98_r2.txt 2>&1"`
   （⚠ PS 5.1 的 `>` 把 stdout 落成 UTF-16；要裸 UTF-8 无 BOM 正本必须走 cmd /c 重定向——本批 apply98-读数.txt 踩过一次、已转码修复，勿再踩）
3. 全量（期望 6487／0・≈10–17 分钟・超时 1200000ms）：
   `dotnet test StarMark.sln -c Debug -p:Platform=x64 -v m`

## 四、P-256 剩下的欠项（⛔ 判 ⛔ 动，登记在账本 ⑯）

- ⑪①②（第三档那两族要不要装棘轮——先决＝「真行切分」）
- 片87 真机腿 (B) 那半格
- （id6 本批闭合；⑦ 89 颗与跨库缺档 47 片97 结清）

## 五、红线（逐条都是用户逐字给过的约束，⛔ 自行放宽）

- ⛔ push；⛔ 中文分支名；仪器产物落 `bin\WI99\…`（⛔ 存 C 盘・记忆回写除外）；跑一次换一颗 TAG 档名（⛔ 覆盖上一跑证据）
- ⛔ 自写「一条命令」包装脚本；跑/构建用官方 dotnet 命令；每颗数由仪器打印 ⛔ 手打；搬尺要连形状一起搬（坑表 #978・#980）
- docs ⛔ 删历史证据、⛔ 放宽判据凑零；历史快照拿今天的存在性去罚昨天的话＝逼下一手改写证据（坑表 #160 口径）
- CJK 档名 ⛔ 从 Bash argv 递（git add 用 python subprocess 传 unicode argv）；提交用 `git commit -F`＋D:\StarMarkTmp 下 UTF-8 无 BOM 临时档；交接快照与 bin 仪器 ⛔ 提交
- ⑪①② 棘轮问题 ⛔ 判 ⛔ 动；计数器三颗数（257/125/132）⛔ 变（新立/翻牌前先跑计数器现读）
- ⛔ 混分母：闸门 FallenPointer 34／census95 落空 16／⑦ 89 颗／跨库缺档 47 各是各的账
- 活档正文引文纪律：在的指针留 `名:号` 形状、落空/缺档一律去形（「N 号」）；⛔ 写「第 N 行」・⛔ <c> 包裹裸号・⛔ 全角冒号＋号的落空形状——否则棘轮 34→39 红
- 每条 dotnet 命令先设 `TEMP/TMP/DOTNET_CLI_HOME=D:\StarMarkTmp`
