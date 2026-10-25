# Phase C 片97 交接快照（2026-10-24・⛔ 提交）

## 一、本批状态

- **片97＝P-256 ⑭ 登记的头两笔未处置欠项同批收口**：⑦ 那 82 颗非 .cs/.xaml 带名号逐颗定性（今天现读 89 颗全数复称在・真漂移 0・问句答完＝不换锚）＋census95 口径跨库缺档 47 颗今天重数恰等（＝⑭ 登记零变动）。
- HEAD `57f1bd3`（两笔：`1757d04` 账本 ⑮ 七条／`57f1bd3` 报告 §四百七十九）・未 push 218・⛔ push（push 一事一授权）。
- 工作区只剩 docs/开发交接-PhaseC-片93/95/96/97.md 四份交接快照未跟踪（⛔ 提交）。
- 记忆回写已完成：board-core 片97 节・living-pointer-p256 片97 补记・MEMORY.md 索引两行（全纯 LF）。

## 二、读数要点（正本全在 `bin/WI99/p997_r1/`・⛔ 提交）

- ⑦ 家族今天 89＝账本 23・深析 66：深析 66 零消失；账本侧多出的 7 颗全在 census90b 扫描之后落盘的正文里（⑥ 那颗 ExcalidrawFontFace.ts 24 号＋⑦ bullet 自例 5 颗（blob 37・woff2 178・index.js 28411・github 73・backup 93 号）＋⑬ 那颗 chunk 322 号）＝82＋7。
- B 段四棵树复称 89/89 界内端点非空・真漂移 0：仪器先读 EXC 38・SM 15・落空 1——normalize.ts 40-45 号被 EXC-first 归堆规则先咬上游 scene/normalize.ts（18 行小档）＝**归堆误咬**，人工复称补判真目标＝扩展侧 src/core/normalize.ts（账本那句明写「扩展」・www.github.com 归一＋http 强制 https）⇒ 修正后 EXC 37・SM 16・NPM 34・DOCS 2・复称在 89／落空 0。
- 树锚：上游 checkout `ed10ac7d` 脏 0／扩展侧 `28c9e31` 引用面 .ts 未脏／npm0e index.js 33143 行・chunk 25696 行・index.d.ts 46 行。
- D 段跨库缺档今天恰 47（账本 19・深析 28・全 .ts・分母与 A 节 ⛔ 混）＝⑭ 登记恰等零变动；C 段 parse 两处小疤只当辅助（census90b 行状没咬上致「新增」假读数・npm96 行状漏「～」区间行），人工对账已补（89＝82＋7・47＝47・npm96 32 颗去重 24・今 24・恰等）。
- 三条验证腿：闸门 38／0（**追加后复跑**・test97-gate_r2.txt）・计数器 257/125/132 VERDICT OK（**追加后复跑**・count97_r2.txt）・全量 6487／0（**追加前读数**・test97_full_r1.txt・17m11s・零码改零测试改 ⇒ 追加后由闸门 r2 罩着）。
- 账本行号两口径照旧：read/grep 与 python text 各是各的行数——定位一律按内容 ⛔ 行号。
- 正本清单：census97.py・census97-读数_r1.txt・census97-console_r1.txt・apply97.py・apply97-读数.txt・test97-gate_r1/r2.txt・count97_r1/r2.txt・test97_full_r1.txt

## 三、接手就照这个顺序走（命令原文，⛔ 自写包装脚本）

每条先设临时目录：`$env:TEMP='D:\StarMarkTmp'; $env:TMP='D:\StarMarkTmp'; $env:DOTNET_CLI_HOME='D:\StarMarkTmp'`

1. 闸门族（期望 失败 0／通过 38／总 38・**跑一次换一颗 TAG 档名 ⛔ 覆盖证据**）：
   `cmd /c "dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --filter FullyQualifiedName~Pointer^|FullyQualifiedName~CrossProjectEvidence^|FullyQualifiedName~LedgerPointer > bin\WI99\p997_r1\test97-gate_r3.txt 2>&1"`
   （在 cmd /c 里 `|` 用 `^|` 转义免内层引号——PS 5.1 嵌套引号会被截断；直接在 PS 里跑则 `|` 必须带引号）
2. 计数器（期望 257／125／132・VERDICT OK・换 TAG 再跑）：
   `cmd /c "python -X utf8 bin\WI37C\count_ledger.py > bin\WI99\p997_r1\count97_r3.txt 2>&1"`
   （⚠ PS 5.1 的 `>` 把 stdout 落成 UTF-16；要裸 UTF-8 无 BOM 正本必须走 cmd /c 重定向）
3. 全量（期望 6487／0・≈10–17 分钟・超时 1200000ms）：
   `dotnet test StarMark.sln -c Debug -p:Platform=x64 -v m`

## 四、P-256 剩下的欠项（⛔ 判 ⛔ 动，登记在账本 ⑮）

- 那颗 `id6` 归属
- ⑪①②（第三档那两族要不要装棘轮——先决＝「真行切分」）
- 片87 真机腿 (B) 那半格
- （⑦ 89 颗与跨库缺档 47 本批结清；若上游／扩展侧 checkout 挪了 HEAD 或 npm0e 重装，重跑 `bin/WI99/p997_r1/census97.py` 即可重数）

## 五、红线（逐条都是用户逐字给过的约束，⛔ 自行放宽）

- ⛔ push；⛔ 中文分支名；仪器产物落 `bin\WI99\…`（⛔ 存 C 盘・记忆回写除外）；跑一次换一颗 TAG 档名（⛔ 覆盖上一跑证据）
- ⛔ 自写「一条命令」包装脚本；跑/构建用官方 dotnet 命令；每颗数由仪器打印 ⛔ 手打；搬尺要连形状一起搬（坑表 #978・#980）
- docs ⛔ 删历史证据、⛔ 放宽判据凑零；历史快照拿今天的存在性去罚昨天的话＝逼下一手改写证据（坑表 #160 口径）
- CJK 档名 ⛔ 从 Bash argv 递（git add 用 python subprocess 传 unicode argv）；提交用 `git commit -F`＋D:\StarMarkTmp 下 UTF-8 无 BOM 临时档；交接快照与 bin 仪器 ⛔ 提交
- ⑪①② 棘轮问题 ⛔ 判 ⛔ 动；计数器三颗数（257/125/132）⛔ 变（新立/翻牌前先跑计数器现读）
- ⛔ 混分母：闸门 FallenPointer 34／census95 落空 16／⑦ 89 颗／跨库缺档 47 各是各的账
- 活档正文引文纪律：在的指针留 `名:号` 形状、落空/缺档一律去形（「N 号」）；⛔ 写「第 N 行」・⛔ <c> 包裹裸号・⛔ 全角冒号＋号的落空形状——否则棘轮 34→39 红
- 每条 dotnet 命令先设 `TEMP/TMP/DOTNET_CLI_HOME=D:\StarMarkTmp`
