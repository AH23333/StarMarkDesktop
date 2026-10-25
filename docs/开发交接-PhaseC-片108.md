# 开发交接·Phase C 片108（P-257 (C) 那块面板排成两行・真机两格照片量到・同批翻牌 ✅）

准绳：`docs/待决策事项.md` ✅P-257（本批结案）・正本全在 `bin/WI99/p108_r1/`・代码腿 `ac238c2`・文档腿另报・⛔ push（一事一授权）。

## 一、这一批改了什么（一句话＋三臂读数）

把 P-257 裁决那条 (C) 落地：**那块画板面板内部排成两行**——第一行照旧（那句话＋它那六颗出口・主人 `_noticeKey`），第二行新挂一颗 `_noticeLinkBody`・专放外链那两句坏话（`boards.linkBlockedBody`／`boards.linkFailedBody`）・主人是新增那颗 `_linkNoticeKey`・由那颗八秒定时器（`BoardPolicy.LinkNoticeMs`＝8000 ms）自己收。于是「点链没开成」那句不再被第一行那句不带计时器的旧话永久静音。

| 臂 | 面板第一行 | 递出去的那一发 | 痕 | 屏幕上（PrintWindow 墨覆盖率＋UIA 文本颗数） |
| --- | --- | --- | --- | --- |
| C-control（对照・正件） | 空着 | 那颗 `file:///` 链 | `boards.open.rejected`＋`boards.notice.link key=boards.linkBlockedBody over=none` | 「画板只放行」**1 颗・墨 0.187628**・落点 (388,268)・气泡 282 |
| B-two-lines（结案格） | 挂着「这一扇已停止保存」 | 同一颗 `file:///` 链 | 同一拍先 `boards.open.rejected`（1）后 `boards.notice.link key=boards.linkBlockedBody over=boards.stuckBody`（1） | 旧话 1 颗・墨 0.117239 ＋ **坏话 1 颗・墨 0.145635 同屏**・落点 (334,518)・气泡 108・`refused=[924]` |
| 同上・等过八秒（附格） | 照旧挂着 | （同一发） | 那颗定时器那一拍 | **坏话 0 颗・旧话照旧 1 颗（墨 0.117239）・那块边框也没跟着收**＝第二行自己退而⛔ 碰第一行 |

⇒ 前两行＝片107 那一格（旧话在场・坏话 **0 颗**）的反面；第三行是结案入口之外我自己认的一格，此前只有源码形状尺钉着、屏幕上量不到。
⇒ 三跑各自 VERDICT 坏格=0（正本 `board108_r1.txt`／`board108_r2.txt`／`board108_r3.txt`）、两行同屏那颗数三跑逐颗相等。

## 二、代码落点（下一批要接着改就先读这一节）

- `src/StarMark.UI/Boards/BoardWindow.cs`：两颗新字段 `_noticeMain`／`_noticeLinkBody`＋那颗新主人 `_linkNoticeKey`；`BuildStage` 里 `_noticeMain = panel;` 与 `rows.Children.Add(_noticeLinkBody)`，那颗 Border 的 `Child = rows`；`ShowNotice` 只亮第一行；`HideNotice` 改成「收第一行・第二行还在就整块别收」；`PasteNotice` 加第二行那颗 switch（两行各自的键各自重贴）；`ShowLinkNotice` 开头那句 `return` 摘掉、改写 `_linkNoticeKey`、换 trace；`HideLinkNotice` 只认 `LinkNoticeKeys` 那两句、⛔ 再调 `HideNotice();`。
- 计数尺同批改口（颗数＝当场现读）：`PasteNotice();` 5→6・`HideNotice();` 5→4・BuildStage 黑字 15→16（底色与边框那两列照旧 11＝新增的是**一行字**而⛔ 一颗按钮）。
- 证人：`BoardWindowBridgeGateTests.TheLinkFailureGetsItsOwnPanelLineAndRetractsItself`（10 格）；邻族 `BoardWordGateTests`／`BoardLinkGateTests` 各改锚。
- 反向台架九臂（`bench108_r1.py`・正本 `bench108-读数.txt`）：BAD=0・extra=0・每臂还原 sha 与改前相等；A9 那一臂钉的是「注释里引那颗已改名的证人」只咬 `TestPointerGateTests` 一把（射程＝只扫 `src/**.cs`・形状要 `XxxTests.Method`）。
- ⚠ 那颗 trace `boards.notice.held` 整串摘除（现读源档里 0 颗）；「谁占着第一行」这一维从此由 `boards.notice.link` 的 `over=` 当场报出来。

## 三、这一批怎么复跑（官方命令・⛔ 包装脚本）

1. 编译：`dotnet build StarMark.sln -c Debug -p:Platform=x64`（那颗 sln ⛔ 叫 StarMarkDesktop.sln）。
2. 定向证人：`dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --nologo --filter "FullyQualifiedName~BoardWindowBridgeGateTests|FullyQualifiedName~BoardWordGateTests|FullyQualifiedName~BoardLinkGateTests"`。
3. 文档闸门那一族（8 档 48 格・命令原文记在 `gate108_docs_r1.txt` 档首）：那颗 filter 逐字抄 `gate108_docs_r1.txt` 第一行。
4. 真机腿：先 `python -X utf8 bin/WI88/stage_28.py 1 zh` 起沙盒（四把写/删开关照旧关），再 `python -X utf8 bin/WI99/p108_r1/board_108.py --run=r4 [--only=B-two-lines] [--expire]`。⚠ 那颗 `--only` 要 `=` 那一副形状（裸 `--only X` 会炸在自己的 argv 解析上）；⚠ 借来的原语里那颗 `L.LEDGER` 盆与帧／转储前缀本批已改道到 `p108_r1`（照原样搬会把上一批的正本覆写＝坑表 #997）；起手必 `others_gate` ＋ `sweep_my_orphans`（单实例会把这一跑顶成「画板窗没起来」）。
5. 只读尺：`python -X utf8 bin/WI99/p108_r1/pointer_r1.py`＝把 `LivingSourcePointerGateTests.FallenPointers()` 那副判据就地重走・并自证两数（现树 34↔改前那版源码 34）再交差集。

## 四、那颗计数器账与欠项

- `bin/WI37C/count_ledger.py` 现读 **258／129／129**（总／结案／开放・VERDICT OK）；第二副口径 **149／86／63**。本批翻牌一颗＝P-257。
- 收口读数：编译 0 警告 0 错误・定向 66/66（`gate108_r1.txt`）・文档闸门那一族 **48/0**（`gate108_docs_r2.txt`／`gate108_docs_r3.txt`）。全量＝**6494 格里 6493 通过・1 失败**（`full108_r1.txt`），失败那颗是本批自己的文档棘轮（裸简写 36↔35），改口后只重跑了同一族 48 格 ⇒ ⚠ **那颗全量的「全绿」已补跑**＝`bin/WI99/p108_r1/full108_r2.txt`：判语逐字＝已通过! - 失败: 0，通过: 6494，已跳过: 0，总计: 6494，持续 15 m 36 s；命令原文与那颗 HEAD `5096504` 记在档首，起跑时工作树现读 0 颗改动＝吃的是这三笔提交落下的那一棵树。
- ⚠ `boards.open.failed`／`boards.linkFailedBody` 那一支在本机照旧**造不出**（4025 字 https 本机开得成＝系统里有默认程序）⇒ 照旧记**未验证**、⛔ 进发布说明（归 P-249 ⑩・⛔ 因本批结案而消失）。
- ⚠ 本批**没**重跑片107 那颗 A-failface（真开一扇浏览器窗那一臂）：结案入口只要「占用者在场时点链⇒两句各 1 颗」＋对照，那一臂的读数早已在片107 的 `board107_r3.txt`；重复跑＝在他桌面上多递一发出去。
- ⚠ 本批真机 r3 那一跑里「起跑后新出现的顶层窗」列出了一颗**他手上的音乐窗**（pid 差集⛔ 落在我这两颗进程上＝本跑⛔ 递出去）；那一指前空闲只读到 1.9 秒与 1.3 秒，闸门各等了一拍再问、归属核对三拍全指向我自己那扇窗，读数与前两跑逐颗相等，故未重跑。
- 交接文档这一族自片93 起逐批落盘，但**片94 那一跑没留单颗交接档**（本批一并入库时现读如此・⛔ 事后补一份冒充当时的快照）。
