# 开发交接・Phase C 片96（已收官・2026-10-24）

> 本档为交接快照，⛔ 提交（git 不跟踪）。片93/片95 那两份交接是历史现场，本档为现读权威。

## 一、此刻仓的状态（现读，⛔ 推断）

- HEAD `61fbf0e`（main）・未 push **216** 颗・⛔ push（push 一事一授权）
- 片96 两笔：`f9985ef`（账本 ⑭ 六条）／`61fbf0e`（报告 §四百七十八）
- 工作区干净（除交接快照与 bin/ 仪器产物，均 ⛔ 提交）
- 三条验证腿全绿：闸门族 38／0・计数器 257／125／132 VERDICT OK・全量 6487／0（13m37s）
- 闸门基线（十二格・LivingSourcePointerGateTests）：FallenPointer=34・ProsePointer=10・LedgerBareShorthand=35・DeepDiveBareShorthand=16・BannedPointers 38・RewrittenAnchors 65・BannedProsePointers 8・带前缀接不上 0・歧义 0

## 二、片96 办的是哪件事（已收官）

P-256 ⑬ 登记的第一笔未处置欠项收口＝活档 618 颗里坏 51 的现读逐颗定性（账本 20・深析 31；⑬ 登记的第一笔欠项・普查链最后一堆）：

- 不新立普查尺——数据全在 `bin/WI99/p995_r1/census95-逐颗_r2.txt` 活档组；本批两把新仪器（`bin/WI99/p996_r1/`・⛔ 提交）：`gate96-对账.py`（把 LivingSourcePointerGateTests 的 Pointer 三捕获组正则／Resolve 四步／Falls 两端点／Read 只并 CRLF **逐字搬来**跑复制闸门普查——首跑拒写那一次＝丢了捕获组，照原文带组搬回后全过）＋`npm96-读数.py`（32 颗 dist/dev 指针对 `bin/WI83/npm0e` 冻结树复称）
- **51 颗全数定性＝全数合法留下・零强改**：① 账本 20＝R7 断链回收表 9（表内自带现址自证）＋老条目 7 逐颗读回**全部带就地更正或自带时点**（P-10 旧路径自注＋647/652/657 号・P-15 SK-0 复核带 64 号・P-20 R7 盘点带 85 号・P-35 SK-0「本条正文的指针是旧的」带 122 号；P-20 的 CS 2026-09-21 读码与 git `b1e486c` 2026-09-22 搬家对得上＝读码在前搬家在后）＋ppInk 3（LedgerPointerGate 的 ExternalProjectFiles 豁免名单恰那三颗）＋⑦ 引例 1（dist/dev index.js 28411 号复称在＝resizeImageFile）——以现在时呈现失效定位而无更正的真漂移**一颗也无**；② 深析 31＋账本 1＝32 颗 dist/dev「缺档」＝census resolve 射程问题（根 ⛔ 射 bin），对 npm0e 冻结树复称 **32/32 界内端点非空**（正本 `npm96-读数.txt`・去重 24 颗・chunk 322 号＝DEFAULT_MAX_IMAGE_WIDTH_OR_HEIGHT 1440＝片60 那颗）
- **对账腿**（正本 `gate96-对账.txt`）：复制闸门普查复现 FallenPointer **34**；census 落空 16（越界 15＋空行 1・全在账本）**⊆ 34 多集配对逐颗都在**；闸门多出 18 颗＝**区间尾端点空行族**（census95 PAT 只捕首号・judge 只问首端点；闸门 Falls 查两端点；18/18 首非空尾空・如 BoardWindow.cs:183-185・GitHubClient.cs:46-48）＝两把尺各自自洽・18 颗全在棘轮 34 罩内（分母 ⛔ 混）；另一副已知分歧＝账本行号两口径（闸门 Read 只并 CRLF≈8082 行・census 连孤立 CR≈21471 行・原始字节 CRLF=8002／孤立 CR=13390／孤立 LF=79）——Falls 只看目标档（src 纯 LF）不受影响
- ⑭ 引文策略：在的指针 in-shape（恰 5 颗全预验・零新增 FallenPointer 匹配・落地后复跑 34 证实）、落空/缺档去形「N 号」；⑬ 的 L21470「照旧⛔ 闭」bullet **不就地改动**（沿 ⑫→⑬ 先例・前向读者看 ⑭）
- 正本全在 `bin/WI99/p996_r1/`（⛔ 提交）：gate96-对账・npm96-读数・apply96（二进制追加・自检恰 5 颗新增全在）・test96-gate_r1・count96_r1・test96_full_r1

## 三、接手就照这个顺序走（命令原文，⛔ 自写包装脚本）

每条先设临时目录：`$env:TEMP='D:\StarMarkTmp'; $env:TMP='D:\StarMarkTmp'; $env:DOTNET_CLI_HOME='D:\StarMarkTmp'`

1. 闸门族（期望 失败 0／通过 38／总 38）：
   `dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --filter FullyQualifiedName~Pointer^|FullyQualifiedName~CrossProjectEvidence^|FullyQualifiedName~LedgerPointer`
   （在 cmd /c 里 `|` 用 `^|` 转义免内层引号——PS 5.1 嵌套引号会被截断・本批踩过两发；直接在 PS 里跑则 `|` 必须带引号——账本 1768 那次 cmd 管道把整表作废）
2. 计数器（期望 257／125／132・VERDICT OK）：
   `cmd /c "python -X utf8 bin\WI37C\count_ledger.py > bin\WI99\p996_r1\count96_r1.txt 2>&1"`
   （⚠ PS 5.1 的 `>` 把 stdout 落成 UTF-16；要裸 UTF-8 无 BOM 正本必须走 cmd /c 重定向）
3. 全量（期望 6487／0・≈10–17 分钟・超时设 1200000ms）：
   `dotnet test StarMark.sln -c Debug -p:Platform=x64 -v m`

## 四、P-256 剩下的欠项（⛔ 判 ⛔ 动，登记在账本 ⑭）

- ⑦ 那 82 颗非 .cs/.xaml 带名号（上游 .ts／npm 包／跨仓三堆；其中 dist/dev 那 32 颗已复称在＝证据交齐・⑦ 整族照旧开放）
- 活档跨库缺档 47 颗（census 口径非坏・上游 .ts 族）
- ⑪①②（第三档那两族要不要装棘轮——先决＝「真行切分」）
- 那颗 `id6` 归属・片87 真机腿 (B) 那半格

## 五、红线（逐条都是用户逐字给过的约束，⛔ 自行放宽）

- ⛔ push；⛔ 中文分支名；仪器产物落 `bin\WI99\…`（⛔ 存 C 盘・记忆回写除外）；跑一次换一颗 TAG 档名（⛔ 覆盖上一跑证据）
- ⛔ 自写「一条命令」包装脚本；跑/构建用官方 dotnet 命令；每颗数由仪器打印 ⛔ 手打；搬尺要连形状一起搬（坑表 #978・#980）
- docs ⛔ 删历史证据、⛔ 放宽判据凑零；历史快照拿今天的存在性去罚昨天的话＝逼下一手改写证据（坑表 #160 口径）
- CJK 档名 ⛔ 从 Bash argv 递（git add 用 python subprocess 传 unicode argv）；提交用 `git commit -F`＋D:\StarMarkTmp 下 UTF-8 无 BOM 临时档；交接快照与 bin 仪器 ⛔ 提交
- ⑪①② 棘轮问题 ⛔ 判 ⛔ 动；计数器三颗数（257/125/132）⛔ 变（新立/翻牌前先跑计数器现读）
- ⛔ 混分母：闸门 FallenPointer 34／census95 落空 16／⑦ 82 颗各是各的账
- 活档正文引文纪律：在的指针留 `名:号` 形状、落空/缺档一律去形（「N 号」）；⛔ 写「第 N 行」・⛔ <c> 包裹裸号・⛔ 全角冒号＋号的落空形状——否则棘轮 34→39 红
