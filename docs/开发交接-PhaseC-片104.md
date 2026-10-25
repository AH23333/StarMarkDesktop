# Phase C 片104 交接快照（2026-10-25・⛔ 提交）

## 一、本批状态

- **片104＝P-248 真机腿收口——§28.4 结案入口最后两格照片量到**：一次 launch 两臂（`bin/WI99/p104_r1/board_104.py`・board_82 那套尺全搬）——**F1 共存**（板窗占前台发 F1 → 遮罩 0.5 秒现形盖满虚屏・第 2 发 `[Screenshot] 已有截图会话在进行，忽略这一次触发` INFO 逐字落日志・不叠会话・Esc 0.4 秒收・第 3 发再起 0.5 秒＝收掉后热键还在・板窗终照两帧块差 0/144）＋**那颗按钮不存在的照片**（纯净缺席正件 `104_board_absence.png` ⛔ 注入・非白 0.1214・片82 的 82_A0 带 P82 标记 ⛔ 复用・UIA 子树 15 控件针命中全 0・14 扇窗普查全净）。`BOARD104 rc=0 坏格=0`・输入桌面 Default（第 0 道核对 1 拍过）。⚠ 一颗环境读数照实记：Ctrl+Alt+P（画布·画笔）被别的程序占着（Win32 1409・每 10 秒重试）＝与本腿无关。
- **本条照旧开放＝翻牌另批拍板**（收口批≠翻牌批・沿 P-256/P-251 节奏）：结案入口三格全量到（关窗 60 秒进程树＝⑥・两格照片＝⑧⑨）⇒ 结案条件齐；翻牌那刀＝题行 ✅＋账本 ⑦ 那句「⛔ 结案」按新读数改判。
- 落地：账本 P-248 节内〔片104 真机腿〕段＋尾 ㉒ 三行＋报告 §四百八十六（apply104.py・三刀 dry 后 --go・片82 批账行锚 count==1・尾锚 endswith・孤 CR/LF 除追加处外不动）。
- ㉒ 撞号核过：账本既有 ㉒ 一处（枚举用法・非批账行），批账尾行锚 `- <b>㉒ 本批落地` 唯一；P-248 节内 ⑧⑨ 空号（probe104_ledger3.py）。
- **计数器基线不变＝257／127／130 VERDICT OK**（count104_r1.txt・零翻牌 ⇒ 三颗数 ⛔ 变）。
- 闸门族 **39／0**（test104-gate_r1.txt・17s）。本批零码改零测试改 ⇒ ⛔ 重跑全量（沿片95/98/99/100/101/103 先例・上一全量读数＝片102 的 6491／0）。
- HEAD `82d79de`（两笔：`385cf18` 账本〔片104 真机腿〕段＋㉒／`82d79de` 报告 §四百八十六）・未 push 234・⛔ push（push 一事一授权）。
- 工作区只剩 docs/开发交接-PhaseC-片93…104.md 十一份交接快照未跟踪（⛔ 提交）。
- 记忆回写已完成（memwrite104.py・dry 后 --go）：board-core 片104 节（387248→389442 字节・LF 1382→1389）・living-pointer-p256 片104 补记（27324→28117・LF 222→226）・MEMORY.md 38/39 行索引（13544→13709・LF 40 不变）——三档全纯 LF 先验原始字节再 decode。

## 二、读数要点（正本全在 `bin/WI99/p104_r1/`・⛔ 提交）

- 真机腿：board_104.py（跑成）＋readings_104_085936.txt＋board104_digest.txt＋uia104_board.txt＋Shot104\ 五帧（absence/mask1_0/board_under_mask/mask3_0/board_after）＋uia_probe_82A0.json＋p104_recon_r1.txt。
- 收尾链：probe104_ledger.py／probe104_ledger2.py／probe104_ledger3.py（账本/报告字节与 EOL 普查・㉒/⑧⑨ 撞号・四把锚唯一）→ apply104.py→apply104-dry.txt／apply104-go.txt（账本 1633053→1637172 字节・CRLF 8045→8055・孤 CR 13390/LF 79 不动；报告 2724033→2726617・CRLF 21178→21188・孤 CR 55828 不动）。
- count104_r1.txt＝**257／127／130 VERDICT OK**；test104-gate_r1.txt＝失败 0／通过 39／总 39（17s）。
- commit104-读数.txt（两笔 exit=0・log -2・status・未 push 234）；memwrite104-dry.txt／memwrite104-go.txt（三档字节/LF 前后账）。
- 正本清单：probe104_recon.py・probe104_ledger.py・probe104_ledger2.py・probe104_ledger3.py・board_104.py・readings_104_085936.txt・board104_digest.txt・uia104_board.txt・Shot104\ 五帧＋apply104.py・apply104-dry.txt・apply104-go.txt・count104_r1.txt・test104-gate_r1.txt・commit104.py・commit104-读数.txt・memwrite104.py・memwrite104-dry.txt・memwrite104-go.txt

## 三、接手就照这个顺序走（命令原文，⛔ 自写包装脚本）

每条先设临时目录：`$env:TEMP='D:\StarMarkTmp'; $env:TMP='D:\StarMarkTmp'; $env:DOTNET_CLI_HOME='D:\StarMarkTmp'`

1. 闸门族（期望 失败 0／通过 39／总 39・**跑一次换一颗 TAG 档名 ⛔ 覆盖证据**）：
   `cmd /c "dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 -v m --filter FullyQualifiedName~Pointer^|FullyQualifiedName~CrossProjectEvidence^|FullyQualifiedName~LedgerPointer > bin\WI99\p104_r1\test104-gate_r2.txt 2>&1"`
2. 计数器（期望 **257／127／130**・VERDICT OK・换 TAG 再跑）：
   `cmd /c "python -X utf8 bin\WI37C\count_ledger.py > bin\WI99\p104_r1\count104_r2.txt 2>&1"`
   （⚠ PS 5.1 的 `>` 把 stdout 落成 UTF-16；要裸 UTF-8 无 BOM 正本必须走 cmd /c 重定向）
3. 全量（期望 **6491／0**・≈10 分钟・超时 1200000ms・只在动测试档/码后才需要跑）：
   `dotnet test StarMark.sln -c Debug -p:Platform=x64 -v m`

## 四、Phase C 还开着的（只剩一颗）

- **P-248（出机三道）**：三道全装齐、真机八臂量过（片82）、结案入口三格照片全量到（片104）——**只剩翻牌那一刀**：题行 `## P-248（批次 Phase C 片66 登记·<b>开放</b>）` → `## ✅P-248（…·<b>已结案</b>）`（＝之后逐字保留）＋账本 ⑦ 那句「⛔ 结案。本条结案入口那三格还剩两格（⑥）」按新读数改判＋批账尾行＋报告尾节。另批拍板（收口批≠翻牌批）。
- P-249 ⑩ 两颗「⛔ 动判据・只登记」未验证项照旧（L3 held-by＝台架 A1・boards.open.failed 本机造不出现场）——归 P-249、⛔ 属 Phase C 欠项。
- （另三颗开放条目 P-209／P-210／P-217 系 WI 批次旧账、非 Phase C。）

## 五、红线（逐条都是用户逐字给过的约束，⛔ 自行放宽）

- ⛔ push；⛔ 中文分支名；仪器产物落 `bin\WI99\…`（⛔ 存 C 盘・记忆回写除外）；跑一次换一颗 TAG 档名（⛔ 覆盖上一跑证据）
- ⛔ 自写「一条命令」包装脚本；跑/构建用官方 dotnet 命令；每颗数由仪器打印 ⛔ 手打；搬尺要连形状一起搬（坑表 #978・#980）
- docs ⛔ 删历史证据、⛔ 放宽判据凑零；「照旧欠」清单是活账不是模板（坑表 #981）
- CJK 档名 ⛔ 从 Bash argv 递（git add 用 python subprocess 传 unicode argv）；提交用 `git commit -F`＋D:\StarMarkTmp 下 UTF-8 无 BOM 临时档；交接快照与 bin 仪器 ⛔ 提交
- **计数器基线＝257／127／130 VERDICT OK**（count104_r1.txt）；闸门基线＝39/0；**全量基线＝6491／0**（片102・test102-full_r1.txt）
- 翻牌批形状：收口批≠翻牌批（沿 P-256/P-251 节奏）；翻牌只动题行（＝之后逐字保留・节内各段一字不动）＋批账尾行＋报告尾节
- 批账尾行枚举（⑪…㉒）与各条目内部枚举撞号不算冲突（㉒ 在别处另有既有枚举用法）——批账行锚用 `- <b>㉒ 本批落地` 全行前缀保唯一
- 账本孤 CR 混排（孤 CR 13390）⇒ grep/rg 的 \n 行号与 read 工具行号不映射；找节内容用 python 探测尺
- ⛔ 混分母：闸门 FallenPointer 34／census95 落空 16／⑦ 89 颗／跨库缺档 47／第三档散文颗口径 41 各是各的账；颗口径 41 ↔ 登记行颗口径 35 ⛔ 互相对账
- 每条 dotnet 命令先设 `TEMP/TMP/DOTNET_CLI_HOME=D:\StarMarkTmp`
