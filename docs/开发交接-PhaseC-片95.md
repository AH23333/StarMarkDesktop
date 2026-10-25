# 开发交接・Phase C 片95（已收官・2026-10-24）

> 本档为交接快照，⛔ 提交（git 不跟踪）。片93 那份交接（`docs/开发交接-PhaseC-片93.md`）仍是历史现场，本档为现读权威。

## 一、此刻仓的状态（现读，⛔ 推断）

- HEAD `a5c0fad`（main）・未 push **214** 颗・⛔ push（push 一事一授权）
- 片95 三笔：`1b931ae`（WidgetSnapshotTests 改口）／`8ff3120`（账本 ⑪③〔片95 更正〕＋⑬ 六条）／`a5c0fad`（报告 §四百七十七）
- 工作区干净（除本档与 bin/ 仪器产物，均 ⛔ 提交）
- 三条验证腿全绿：闸门族 38／0・计数器 257／125／132 VERDICT OK・全量 6487／0
- 闸门基线（十二格・LivingSourcePointerGateTests）：FallenPointer=34・ProsePointer=10・LedgerBareShorthand=35・DeepDiveBareShorthand=16・BannedPointers 38・RewrittenAnchors 65・BannedProsePointers 8・带前缀接不上 0・歧义 0

## 二、片95 办的是哪件事（已收官）

P-256 ⑫ 登记的第一笔未处置欠项收口＝片85 全仓普查剩下的两堆（历史快照 427・代码注释 38）＋片85 从没扫过的 `docs/双库一致性审计.md`（59 颗・自钉 HEAD `360ce50` 的 R6 快照）：

- 普查尺 `bin/WI99/p995_r1/census95.py`：判据逐字沿 `bin/WI99/census85.py`（PAT/SKIP/FROZEN/resolve/judge 一颗不改），只改输出层（自证 10 格・全量逐颗 dump・补扫审计）。r2 终读＝全仓 **1142 颗・坏 96**：活档 618（坏 51・归活指针腿 ⛔ 碰）・历史快照 427（坏 38）・代码注释 38（坏 5）・双库审计 59（坏 2）・冻结树指向 17（坏 0）
- 裁决：历史快照与双库审计**整档登记不罚**（行号只在那个读数时刻有效・双库审计对那颗 HEAD 负责）；代码注释坏 6 逐颗定性＝自证样例/自测串 4＋跨库引用 1＋**真漂移恰 1 颗**＝`WidgetSnapshotTests.cs:170` 三插槽注释 107-109→125-127（唯一改口・逐行验过）
- ⚠ 本批的一课（未进坑表・与坑表 #978 同族）：账本 ⑬ 初稿把 5 颗落空指针按「档名:行号」原形写进正文 ⇒ Pointer 正则（.cs/.xaml・**全角冒号也算**）全咬进 FallenPointer 棘轮 34→39 变红；按片90「号只留正本」去形改口（冒号→空格・逐字原形留在逐颗正本）后四颗棘轮一颗不动。裸简写正则须 `<c>` 包裹且区间只认全角 `～` ⇒ 裸 `:125`（无包裹）与 `125-127`（ASCII 连字符）天然安全
- 正本全在 `bin/WI99/p995_r1/`（⛔ 提交）：census95-读数/逐颗 _r1（改口前）与 _r2（改口后）・test95-gate_r1・count95_r1・test95_full_r1

## 三、接手就照这个顺序走（命令原文，⛔ 自写包装脚本）

每条先设临时目录：`$env:TEMP='D:\StarMarkTmp'; $env:TMP='D:\StarMarkTmp'; $env:DOTNET_CLI_HOME='D:\StarMarkTmp'`

1. 闸门族（期望 失败 0／通过 38／总 38）：
   `dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --filter "FullyQualifiedName~Pointer|FullyQualifiedName~CrossProjectEvidence|FullyQualifiedName~LedgerPointer"`
   （filter 里的 `|` 必须带引号——账本 1768 那次 cmd 管道把整表作废）
2. 计数器（期望 257／125／132・VERDICT OK）：
   `python -X utf8 bin/WI37C/count_ledger.py`
   （⚠ PS 5.1 的 `>` 把 stdout 落成 UTF-16；要裸 UTF-8 无 BOM 正本用 `cmd /c "python -X utf8 … > 档 2>&1"`——本批踩过一次）
3. 全量（期望 6487／0・≈10–17 分钟・超时设 1200000ms）：
   `dotnet test StarMark.sln -c Debug -p:Platform=x64 -v m`

## 四、P-256 剩下的欠项（⛔ 判 ⛔ 动，登记在账本 ⑬）

- 活档 618 颗里坏 51 的现读（账本 20・深析 31・大半是 excalidraw 上游引用族）＝归活指针腿自己的条目
- ⑦ 那 82 颗非 .cs/.xaml 带名号（上游 .ts／npm 包／跨仓三堆）
- ⑪①②（第三档那两族要不要装棘轮——先决＝「真行切分」）
- 那颗 `id6` 归属・片87 真机腿 (B) 那半格

## 五、红线（逐条都是用户逐字给过的约束，⛔ 自行放宽）

- ⛔ push；⛔ 中文分支名；仪器产物落 `bin\WI99\…`（⛔ 存 C 盘・记忆回写除外）；跑一次换一颗 TAG 档名（⛔ 覆盖上一跑证据）
- ⛔ 自写「一条命令」包装脚本；跑/构建用官方 dotnet 命令；每颗数由仪器打印 ⛔ 手打；搬尺要连形状一起搬（坑表 #978・#980）
- docs ⛔ 删历史证据、⛔ 放宽判据凑零；历史快照拿今天的存在性去罚昨天的话＝逼下一手改写证据（坑表 #160 口径）
- CJK 档名 ⛔ 从 Bash argv 递（git add 用 python subprocess 传 unicode argv）；提交用 `git commit -F`＋D:\StarMarkTmp 下 UTF-8 无 BOM 临时档；交接快照与 bin 仪器 ⛔ 提交
- ⑪①② 棘轮问题 ⛔ 判 ⛔ 动；计数器三颗数（257/125/132）⛔ 变（新立/翻牌前先跑计数器现读）
