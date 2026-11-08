# 开发交接 · Phase C 片135（托盘「贴图组」五键真机收回・覆盖账严格复称）

更新时刻：2026-10-29 00:50（本机）

## 一、这一片做了什么

- <b>真机腿</b>：收片134 交接档「二、欠着与边界」里那两条——p128 五颗托盘键用第二路收回自量（expect 三档 15 行判据全中・CHECK 12/12×3・三语各 18 步），顺带把 #1102 点名的 p128 <c>idle128.ps1</c> 那颗 <c>{0:N1}</c> 潜伏雷先修再跑。账本 ✅P-194 节内插块＋报告 §五百一十七＋本档，同批落地；坑表本批零新增。
- <b>读树腿的形状</b>：staging 逐字复用片128 那副；新腿＝PostMessage 自家托盘窗（<c>TRAY_CALLBACK 0x8001</c>＋<c>WM_RBUTTONUP 0x0205</c>）开菜单 → <c>MN_GETHMENU</c> 全量 32 行 dump → <c>GetSubMenu</c> 结构降两层（父行＝唯一带子菜单的顶行・零文本匹配）→ Esc＋<c>WM_EXITMENULOOP</c> 零点击收单（滞留即 exit 17 拒报成功——卡死的 <c>TrackPopupMenuEx</c> 会堵死 F4／F5）；三态翻面＝F4（0x73）／F5（0x74）两颗全局热键＋pins 复称与行让位当见证。
- <b>量到的</b>：五键 × 三语逐字（<c>pin.groupHide</c>『隐藏这一组／Hide this group／このグループを隠す』・<c>pin.groupShow</c>『显示这一组／Show this group／このグループを表示』・<c>pin.groupThroughOff</c>『这组忽略鼠标／Ignore the mouse for this group／このグループをマウス無視』・<c>pin.groupThroughOn</c>『取消这组忽略鼠标／Stop ignoring the mouse for this group／このグループのマウス無視を解除』・<c>pin.groupClose</c> 渲染关行『关闭这组（1 张）／Close this group (1 pins)／このグループを閉じる（1 枚）』）＋四颗旁证（托盘父行渲染・组行渲染・<c>pin.group.name</c> 渲染・提示卡 <c>pin.card.groupedBody</c> 正文）。正本 <c>D:\StarMarkTmp\p128\run135-zh.txt</c>／<c>run135-en.txt</c>／<c>run135-ja.txt</c>。
- <b>覆盖账严格复称</b>：名册 35 颗——片125 实翻 3＝20・片128 实翻 1（<c>pin.groupLabel</c>）＝21・片131 实翻 3＝24・本片 +4＝<b>28／未见 7</b>；账本沿用「26／未见 9」虚高 2，病根＝片128 报告「20 → 23」（把不在名册的 <c>pin.card.groupedBody</c>／<c>pin.group.name</c> 也计入）；历史原文照旧不动。

## 二、欠着与边界

- <b>capture.card 挂账 7 颗照旧</b>（categoryOcr／copiedBody／copiedTitle／copyFailedTitle／disabledTitle／encodeFailed／failedTitle）：宿主在截图／识字那一路＝点验单⑥那一族。
- <b>观感点验照旧归他</b>（⛔ 替他点头）；归他的还有 #744（WI-82 Phase B 剩余）。
- <b>p128 仪器</b>：idle 尺已按 #1102 修成 <c>{0:F1}</c>；读树腿与 staging 腿三语全绿，正本可用。

## 三、收口读数（本批）

- 文档四闸门＝通过 23／失败 0（改前改后各一回，正本 <c>D:\StarMarkTmp\p128\docgates135.txt</c>）；账本计数器四串＝<b>264／139／125／0</b>・VERDICT OK（插块 ⛔ 立新号故四串不动）。
- 零泄漏核对：CLIPWIT pre==post（三语 TEXTLEN=5 TEXTSHA=2AE09E7F1A7A20A9）・PICS pre==post 各 9 颗（PICS-GUARD 相等=True×3）・收摊三跑各「台上还剩 0 颗 StarMark.UI」。
- 提交：本批只文档腿这一颗；照旧<b>未推</b>（未推 304），⛔ push。

## 四、仪器那一笔（⛔ 产品结论，只欠我自己）

- zh『这组忽略鼠标』是『取消这组忽略鼠标』的子串 ⇒ 菜单行判定一律行集精确匹配，包含式会假命中。
- 负见证在空读数上会空真 ⇒ 判定带非空守卫（读空时负见证不许算中）。
- 读数档名全带 135 后缀，⛔ 覆写片128 旧档；门 0 连趟未过时守门器只静候重试，⛔ 抢跑。

## 五、片136 接续抽验（2026-10-29，七键仍未收回）

- 接手核对：本轮起手与测试结束均为 `main`／`fb98c167b5760f4b842bacf6e71c330bd3c4e879`，工作树当时干净；另一个 StarMark 会话最近六次续接均失败且无工具调用，查询时无活动轮次、排队或后台代理。这不代表其全部历史无人接手。
- 本轮明确构建：`dotnet build StarMark.sln -c Debug -p:Platform=x64 --no-restore`，0 警告／0 错误，48.51 秒；随后运行下列定向命令，68 通过／0 失败／0 跳过。未跑全量或变异台架，未改产品源码。
- 沙盒：数据库与设置分别通过 `STARMARK_DB_PATH`、`STARMARK_SETTINGS_PATH` 指向 `D:\StarMarkTmp\p136`；剪贴板文字／图片采集、自动备份、开机加载组件、本地搜索及自动更新检查关闭。仅在此沙盒切换三语，启动 PID 8144；关闭主窗后进程已退出，同名 `StarMark.UI` 进程数为 0。
- 无选区抽验：英文 `Ctrl+S`、中文 `Enter`、日文 `Ctrl+C` 各一组，都在仍存活的遮罩上读到 `ErrorText`；原始 UIA 正本为 `D:\StarMarkTmp\p136\keyboard-evidence.json`。这只是三组样本，不是三语乘三键九格全覆盖，也不属于上面七颗 `capture.card` 的新增覆盖。
- 拖框前置未通过：三次 SDK `drag` 跨两次截图会话均未形成有效选区，重观察与点击也未使选区成立；尚无证据区分产品、自动化输入或环境原因，不能归因于用户干扰。本轮未进入复制／存图／OCR 的成功出口；未做剪贴板与真实图片目录的前后见证，不宣称已证实零泄漏。
- 七键前置分开记：`copiedTitle`／`copiedBody` 需有效像素编码并写入剪贴板成功；`categoryOcr` 由截图编辑器的 OCR 出口传入。`disabledTitle` 虽有服务闸门，但关闭总开关后托盘三入口被滤掉、对应热键不注册，普通入口不能据此造出该卡，排队／竞态未验证。`encodeFailed` 只对应编码函数因非正尺寸或短缓冲返回 `null`；实际编码异常重新抛出，走 `copyFailedTitle` 加异常正文，不能混算。`failedTitle` 的抓屏、监视器、建窗或裁图故障均未实造。
- 分支证据：`src/StarMark.UI/Services/ScreenshotService.cs`、`src/StarMark.UI/Views/CaptureOverlayWindow.Actions.cs`、`src/StarMark.Integrations/Capture/GdiScreenCapture.cs`、`src/StarMark.UI/MainWindow.Tray.cs`、`src/StarMark.UI/Helpers/SettingsStore.Hotkeys.cs`。以上是源码前置，不是故障分支已上屏的证明。
- 本轮严格覆盖仍为 28／35，未见 7；下一步先取得可靠的有效选区，再补复制与 OCR 真机证据。此次仅向本交接追加读数，未提交、未推送，HEAD 与未推 304 不变；前文片135读数保留原批次归属。

定向命令（在 StarMarkDesktop 根目录）：

```text
dotnet test tests/StarMark.Tests/StarMark.Tests.csproj -c Debug -p:Platform=x64 --no-build --no-restore --filter "FullyQualifiedName~CaptureGeometryTests|FullyQualifiedName~CaptureNoticeWiringGateTests|FullyQualifiedName~CaptureReasonGateTests" --logger "trx;LogFileName=capture-regression136.trx" --results-directory "D:/StarMarkTmp/p136"
```

结果正本：`D:\StarMarkTmp\p136\capture-regression136.trx`。本轮 `--no-build` 使用的是上述本轮成功构建后的同一棵源码，不能将这个前提省略后套用于旧产物。

## 六、片137 接续（2026-10-29・拖框释放终点＋空正文修复＋copyFailedTitle 三语收回）

- 接手核对：起手与收口均为 `main`／`fb98c167b5760f4b842bacf6e71c330bd3c4e879`；查询会话列表时另两颗 StarMark 会话（「StarMarkDesktop前端代码审查报告」「深度自检项目代码」）皆 `cold`・无活动轮次、无排队、无后台代理，本仓工作树只有本会话这一族改动。这不代表其全部历史无人接手。
- <b>拖框释放终点（产品腿）</b>：`src/StarMark.UI/Views/CaptureOverlayWindow.Pointer.cs` 那颗 `Root_PointerReleased` 从前清 `_awaitingRelease` 时用的是<b>最后一次移动事件</b>留下的预览——仪器正本 `D:\StarMarkTmp\p137\pointer-events.txt` 现读 PRESS／MOVE＝(594,625) 而 RELEASE＝(938,828)、`_selection` 仍 0×0。修法＝把归一化／夹回本屏／候选微抖动／画选区那一段抽成 `UpdateRegionDrag`，移动与松开<b>共用同一颗</b>，松开前先按本次释放坐标结算一次。两枚接线证人 `RegionReleaseAppliesItsOwnPositionBeforeConfirmation`／`RegionMoveAndReleaseShareClippingAndCandidateJitterRules` 先红（`release-red.trx`＝2 格全红），撤掉四行临时诊断后 `dotnet build StarMark.sln -c Debug -p:Platform=x64 --no-restore` 0 警告／0 错误 28.99 秒，定向 110 通过／0 失败／0 跳过（正本 `D:\StarMarkTmp\p137\capture-regression137.trx`）。真机正／反向拖框各 344 × 203，`Ctrl+S` 实际落盘两张：`StarMark 2026-10-29 110606.png`（824 字节・344×203・sha256 `2f086c1112a07dae3e0aab0b9022eed7c0ff538ef988bbb90f5636e5188a8a7c`）与 `StarMark 2026-10-29 110753.png`（124434 字节・1200×877・sha256 `a0cbca11c1459c64347c1a35eabc568d3af7ba095abab5913693925d196a0465`）。<b>后一张是微拖后真的保存成功，⛔ 记成「无选区拒绝」的正件</b>；那颗候选点击／微抖动的行为仍未判定。
- <b>失败卡正文空白（产品腿）</b>：独占剪贴板时 `Clipboard.SetContent` 抛的是 HRESULT `0x800401D0` 的 COMException 而 <c>Message</c> 为空，旧 `ReasonOf` 把别人的异常一律塞进 `common.rawValue` ⇒ 卡只剩标题（缺陷图 `D:\StarMarkTmp\p137\copy-failed-ja.png`・324×89・正文那一行什么都没有）。`src/StarMark.Abstractions/Capture/CaptureFailureException.cs` 的 `ReasonOf` 就地收三档：自家异常原样递 `Reason`・`0x800401D0` 交新键 `capture.clipboard.cannotOpen`・其余正文空白交新键 `capture.error.noMessage` 并带 `0x{HResult:X8}`，非空正文照旧走 `common.rawValue`。两颗新键⛔ 进那 35 颗分母。`ScreenshotService.CopyPixelsAsync` 与 `OcrService.TryCopyText` 两出口同批改用 `ReasonOf`，OCR 那颗 `out` 从 `string?` 换成 `LocMessage?` ⇒ 换语言重贴时嵌套那句跟着换。九格先红（`clipboard-reason-red.trx`＝9 格全红），solution 构建 0 警告／0 错误 64.94 秒后定向 133 通过／1 失败——那颗红是 `DialogWordGateTests` 旧计数 `rawValue` 5 颗（迁移后实际 4 颗），精确改 4 并补钉 `ReasonOf` 一颗，⛔ 放宽判据；改口后 `clipboard-reason-green-v2.trx`＝<b>134 通过／0 失败／0 跳过</b>。首跑那颗红留在 `clipboard-reason-green.trx` 里，⛔ 用文件名抹掉它。
- <b>真机腿（三语 × 两出口）</b>：图片复制失败卡与「识字成功→写剪贴板失败」卡都在屏幕上真的贴过并被独立读回——`copy-failed-ja-fixed-v4.png`（324×107）・`copy-failed-zh-fixed.png`（324×90）・`copy-failed-en-fixed.png`（324×90）；`ocr-copy-failed-en-fixed.png`（324×124）・`ocr-copy-failed-zh-fixed.png`（324×107）・`ocr-copy-failed-ja-fixed.png`（324×143）。OCR 那三发实认 66／28／37 字（en／zh／ja），只算「那句正文真的上屏」，⛔ 当识别准确率验收。日志逐条对齐：11:52:10／11:52:56／11:53:43 三发各有 `[Ocr] 写入剪贴板失败`＋完整本地化句子＋提示卡矩形；11:40:49／11:41:56／11:43:39／11:45:03 四发是图片出口。UI 读数正本 `D:\StarMarkTmp\p137\ui-evidence.json`＝20 条・标识 20 颗不重复・每条那颗 `image` 现读都在盘上。
- <b>覆盖账严格复称</b>：名册仍 35 颗——片136 收尾 28／未见 7；本片 `capture.card.categoryOcr` +1＝29、`capture.card.copyFailedTitle` +1＝<b>30／未见 5</b>（`copiedTitle`／`copiedBody`／`disabledTitle`／`encodeFailed`／`failedTitle`）。历史原文照旧不动。
- <b>零泄漏核对</b>：`clipboard-pre.json` 现读 6 颗格式；本片五把独占（`clipboard-hold-ja-fixed-v3`／`-v4`／`clipboard-hold-zh-fixed`／`clipboard-hold-en-fixed`／`clipboard-hold-ocr-fixed`）每把 `before == after` 且都等于 pre，stdout 全部 `HELD … CLOSED`・退出码 0；收摊再拍 `clipboard-post.json`＝6 颗格式・与 pre <b>逐颗摘要相等</b>。OCR 那一把改用 `hold-process` 模式（等 PID 16720 自己退出才松手），理由＝`OcrPipeline.ReadAsync` 全程没有超时上限，45 秒定长锁可能在识别还没写完时先释放、让下一次写覆盖用户原剪贴板。沙盒数据库／设置分别走 `STARMARK_DB_PATH`／`STARMARK_SETTINGS_PATH` 指 `D:\StarMarkTmp\p137`，四把写／删开关照旧关着；`Alt+F4` 正常退出（日志「===== StarMark 进程退出 pid=16720 =====」），收口 `Get-CimInstance` 现读 `StarMark.UI.exe` 进程数＝<b>0</b>，沙盒设置现读 `UiLanguageCode=ja`。
- <b>行尾三族（⛔ 拿 `git diff` 当尺）</b>：`numstat`＝交接档 19/0・`CaptureFailureException.cs` 9/7・`UiStrings.cs` 4/0・`OcrService.cs` 2/2・`ScreenshotService.cs` 1/1・`CaptureOverlayWindow.Pointer.cs` 8/0・`CaptureReasonGateTests.cs` 54/0・`DialogWordGateTests.cs` 2/1・`TwoStageCaptureTests.cs` 38/0。盘上现读（CRLF／LF-only／孤CR）：`CaptureFailureException.cs` 33/0/0（改前 31/0/0）・`UiStrings.cs` 0/3400/0（改前 0/3396/0）・`OcrService.cs` 0/113/0（改前同）・`ScreenshotService.cs` 379/0/0（改前同）・`CaptureOverlayWindow.Pointer.cs` 0/489/0（改前 0/481/0）・`CaptureReasonGateTests.cs` 243/0/0（改前 189/0/0）・`DialogWordGateTests.cs` 0/314/0・`TwoStageCaptureTests.cs` 0/341/0（后两颗改前未记）。本档只追加，删除列＝0。`git diff --check` 对报告那 14 行报 trailing whitespace＝那颗档在 HEAD 里按<b>字节</b>存（blob 现读 CRLF 21626・孤CR 55828・LF-only 24），每一行 CRLF 都被那颗尺读成行尾空白——片135 那次提交同形 24 行（`git diff --check HEAD~1 HEAD -- docs/开发进度报告.md`），⛔ 归责我这一批；交接档那颗走换行归一（HEAD blob＝LF 28・CRLF 0），同一条 `--check` 对它零命中。
- <b>文档四闸门</b>＝通过 23／失败 0／跳过 0 <b>五回全中</b>（追加交接前 `D:\StarMarkTmp\p137\docgates137.trx`・追加交接后 `docgates137-post.trx`・追加报告 §五百一十八后 `docgates137-report.trx`・收口最后一跑 `docgates137-final.trx`・两处改口之后再对盘上最终形状跑一回 `docgates137-close.trx`），filter 原文沿用 `D:\StarMarkTmp\p128\docgates135.py` 那四颗 `LedgerPointerGateTests`／`LedgerStatusNarrationGateTests`／`LivingSourcePointerGateTests`／`FrozenTreePointerGateTests`，⛔ 从「23」倒推身份；五跑都用 `--no-build`，前提＝本轮 solution 构建已成功。
- <b>报告腿</b>：`docs/开发进度报告.md` §五百一十八纯追加（节间一颗 `\r\n\r\n`・`numstat`＝14/0・字节 2846183→2855562・CRLF 21626→21640・LF-only 24 与孤CR 55828 一字未动・sha256 `af8f493095c1306b876e1a84223daaa77425e20aa135afe4fbbf06a697dbb2ef`）。账本 `docs/待决策事项.md` 本批一字未动（现读 1790877 字节・CRLF 8469・LF-only 96・孤CR 13390・sha256 `5d03532a863efa2119393ca80994c126c526109cd8123a83970cd25aa96542ad`＝片135 落盘后那颗），本批新立 0／结案 0 ⇒ 计数器四串⛔ 重跑、⛔ 报新数。
- <b>欠着与边界</b>：`copiedTitle`／`copiedBody` 要真写成功才能收，而本片全程占着剪贴板＝一次都写不进去（⛔ 为收两颗键去覆用户手上的东西）；`disabledTitle` 关总开关后托盘三入口被滤掉、对应热键不注册，普通入口造不出那张卡；`encodeFailed` 只对应 `EncodePngAsync` 因非正尺寸／短缓冲返回 `null`，真编码异常重新抛出走 `copyFailedTitle`；`failedTitle` 的抓屏／监视器／建窗／裁图故障均未实造。观感点验照旧归他，#744（WI-82 Phase B 剩余）也归他。
- <b>提交</b>：本批＝产品五档＋测试三档＋本交接档，照旧<b>未提交、未推送</b>（未推 304＝本轮 `git rev-list --count origin/main..HEAD` 现读），⛔ push。

## 七、片137 仪器那一笔（⛔ 产品结论，只欠我自己）

- <b>观察范围会替我造出「没上屏」的假象</b>：主窗不压住右下角那块时，`get_window_state` 连着三跑只回主窗；把主窗拖到 originX465／originY309 之后，同一颗失败出口立刻带回独立提示卡窗（id 787836・标题随语言变）。所以「SDK 没返回那张卡」⛔ 等于「产品没贴上屏幕」——产品侧的凭据是日志里那颗矩形（`1580,1077 324x107` 一族），缺席的枚举只说明我的观察范围不够。本条未查 SDK 内部实现，⛔ 把它写成 SDK 缺陷。
- <b>按图标位置猜那颗按钮＝会点错</b>：我先按像素猜「识字」，实际点中「贴图」（日志 `[Pin] Pinned：366 × 79`），已 `Alt+F4` 收掉那一颗、该发⛔ 计入 OCR 证据。改法＝先现读 `CaptureOverlayWindow.ToolBar.cs` 的入列顺序（copy→save→ocr，`_pinned` 分支还会多插一颗组按钮），再按那颗顺序定位。
- <b>仪器报错不等于动作没发生</b>：`node_repl` 两次把 `await nodeRepl` 读成 `awaitnodeRepl` 抛 ReferenceError，而 `Ctrl+C` 与 `get_window_state` 其实已经跑完、状态里就有那张卡。⛔ 重放复制（那会再点一次产品出口），改在下一 cell 取<b>已捕获</b>的 `state.screenshots` 单独 save＋emit。
- <b>独占锁的时长要由被等那件事的上限定</b：OCR 链没有超时上限 ⇒ 定长 45 秒的锁是拿我的预算去赌产品的耗时；改成等目标进程退出才释放，锁的边界就跟着真实生命周期走。
- <b>读数档名全带 137 后缀</b>，⛔ 覆写片128／片136 旧档；`--no-build` 那一句的前提（本轮已成功构建同一棵树）要一起写，⛔ 摘掉前提套旧产物。

## 八、片137 补刀（他点头放开剪贴板之后・copiedTitle／copiedBody 三语收回・覆盖账 30→32／未见 5→3）

- <b>这一发为什么归他点头</b>：`copiedTitle`／`copiedBody` 是剩下五颗里唯一「普通操作就能收到」的一对，但它要求截图<b>真的写进剪贴板成功一次</b>＝顶掉他手上那 6 颗格式（含 Chromium 两颗内部格式＋文字＋图片），而探针只存 sha256 摘要、⛔ 落正文＝顶掉之后我没法还原。我把这一格交给他选，他选的是「<b>允许真写一次（推荐）</b>」那一颗（我给的问题句逐字＝「要不要做这一发？」）。⇒ 这一笔是<b>他批准的覆写</b>，⛔ 写成「零泄漏」，也⛔ 外推成「他批准我以后任何覆写」。
- <b>量到的</b>：同一块选区 401 × 59 三语各一发，卡都是 324×90・都落在 `1580,1094`。逐字——ja「スクリーンショット · コピーしました」／「401 × 59 をクリップボードにコピーしました」（日志 12:17:36.9001234）・zh「截图 · 已复制」／「401 × 59 的画面已进剪贴板」（12:18:19.7153280）・en「Capture · Copied」／「401 × 59 is on the clipboard」（12:18:49.6369264）。图片正本 `D:\StarMarkTmp\p137\copied-ja.png`／`copied-zh.png`／`copied-en.png` 三张各自独立读回，日志三行三发一一对上；UI 正本 `ui-evidence.json`＝<b>23 条・标识 23 颗不重复・每条 `image` 现读都在盘上</b>。
- <b>覆盖账（本节为准）</b>：名册 35 颗——「六」那一节收尾是 30／未见 5（那是当时那一拍的读数，原文照旧不动），本节 +2＝<b>32／未见 3</b>，剩 `capture.card.disabledTitle`／`encodeFailed`／`failedTitle`。那三颗的前置照旧写在「二、欠着与边界」与「六」最后那颗 bullet 里（关总开关后托盘三入口被滤＋热键不注册／`EncodePngAsync` 返回 `null` 才叫编码失败／抓屏・监视器・建窗・裁图故障未实造）。
- <b>剪贴板这一笔的账</b>：点头之前先拍 `clipboard-pre2.json`＝6 颗格式・与本片起手那份 `clipboard-pre.json` <b>逐颗摘要相等</b>；三发真写之后 `clipboard-after-copied.json`＝6 颗格式＝`DataObject`＋`Preferred DropEffect`＋CF_BITMAP(2) 401×59・1 位面・32bpp・widthBytes 1604＋`Ole Private Data`＋CF_DIB(8) 94688 字节（sha256 `a06a355d…bdd0fd02`）＋CF_DIBV5(17) 94772 字节（sha256 `7eb24d9f…c9c31fb2`）。⇒ 他原来那 6 颗（13／Chromium RFH token／Chromium URL／16／1／7）<b>确认已被这张 PNG 顶掉</b>，这条就是那句「⛔ 为收两颗键去覆用户手上的东西」被解开之后的样子。
- <b>仪器那一笔（两颗，⛔ 产品结论）</b>：① 旧探针 `clipboard_probe.py` 那颗 `snapshot()` <b>只认 HGLOBAL</b>，而真写之后剪贴板里那颗 CF_BITMAP（format 2）⛔ 是 HGLOBAL ⇒ 当场 `RuntimeError: Format 2 is not readable as HGLOBAL`・退出码 1、证据档⛔ 落（那颗「输出档已存在即拒跑」＋「崩在写盘之前」的形状救了我一次：⛔ 留下半截假证据）。另造 `clipboard_probe2-137.py`＝逐格式先试 HGLOBAL 摘要、试不到再按 GDI 对象读宽高／位面／位深／widthBytes，⛔ 把「我这把尺读不到」写成「屏幕上／剪贴板里没有」。② 沙盒主窗 origin 621,465 时提示卡整块落在窗内 ⇒ `get_window_state(main)` 一次就带回独立卡窗（id 1705358），与「七」那颗观察范围同一族——那颗尺的射程由窗口位置决定，⛔ 由「没返回」推「没上屏」。
- <b>收口</b>：沙盒这颗 PID 16156（`STARMARK_DB_PATH`／`STARMARK_SETTINGS_PATH` 指 `D:\StarMarkTmp\p137`）`Alt+F4` 正常退出・`list_windows` 现读该 app 窗口 0 颗・`Get-CimInstance` 现读 `StarMark.UI.exe` 进程数＝<b>0</b>；文档四闸门在补刀之后再跑一回＝通过 23／失败 0／跳过 0（正本 `D:\StarMarkTmp\p137\docgates137-copied.trx`）；HEAD 仍 `fb98c167b5760f4b842bacf6e71c330bd3c4e879`・未推 304・<b>⛔ 提交、⛔ push</b>。

## 九、片137 补丁门腿（临时改产品码造出那三颗故障分支・收完就地还原并复称）

- <b>为什么只能这么量</b>：`encodeFailed`／`disabledTitle`／`failedTitle` 三颗按「六」与「二」的口径普通操作摆不出来（编码函数只有非正尺寸／短缓冲才交 `null`・关总开关后热键不注册且托盘三入口被滤・抓屏／监视器／建窗／裁图故障没实造过）。这一腿用<b>临时补丁构建</b>把那一支的门强行打开，只为量「那句词能不能上屏」。<b>⛔ 这一腿⛔ 证真实可达性</b>：那三颗在真实操作里走不走得到，照旧登记未验证，⛔ 把补丁门读数写进发布说明。
- <b>三臂的形状与读数</b>（每臂三语各一张卡・九张图全部本地独立读回・日志逐发对上）：
  · <b>encodeFailed</b>＝在 `GdiScreenCapture.EncodePngAsync` 首行之后插一颗魔数臂（`width == 401 && height == 59` 直接交 `null`），走的是真实那颗「编码器交 `null`」的同一个返回口，只是触发条件是我塞的。选区 401 × 59（UIA `SizeText` 现读同值）⇒ 卡 324×90／324×90／324×90：en「Capture · Copy failed／The image couldn't be encoded」（12:24:40.7584132）・zh「截图 · 复制失败／把画面编成图片时失败」（12:25:36.5077925）・ja「スクリーンショット · コピーに失敗しました／画像をエンコードできませんでした」（12:26:02.0460110）。正本 `encode-failed-{en,zh,ja}-patched.png`・那一跑装配 `StarMark.Integrations.dll` sha256 前缀 `03d6bec3`（构建时刻 12:23:03）。
  · <b>disabledTitle</b>＝在 `ScreenshotService.Start` 那颗 `CaptureGate.BlockOf(...)` 之后加一颗环境变量臂（`STARMARK_P137_FORCE=disabled` 时把结果强改成 `CaptureBlock.Disabled`），下面那句 `Report` 与 `CaptureGate.ReasonFor` 一个字没动。卡 324×143／107／107：ja「スクリーンショット · スクリーンショット機能はオフです／先に設定 → 拡張機能 で「スクリーンショット（キャプチャ／貼り付け／文字起こし）」をオンにしてください」（12:28:23.2149612）・en「Capture · Screen capture is turned off／First turn on Screenshot (capture / pin / OCR) in Settings → Extensions」（12:28:46.2306903）・zh「截图 · 截屏功能已关闭／要先在 设置 → 拓展功能 里打开「截屏（截图 / 贴图 / 识字）」」（12:29:06.8153461）。正本 `disabled-{ja,en,zh}-patched.png`。
  · <b>failedTitle</b>＝把那颗 `if (!captured.Ok || captured.Frame is not { } frame)` 多挂一条 `|| STARMARK_P137_FORCE == "grabfail"`。<b>⚠ 这一臂的正文⛔ 等于真实抓屏故障的正文</b>：`Grab()` 其实成功了、`captured.Error` 是 `null`，所以那句走的是兜底那颗 `capture.grab.noFrame`＝zh「系统没有返回画面」／en「The system returned no image」／ja「システムが画像を返しませんでした」；真实故障时那句由抓屏器自己交「键＋实参」。卡 324×90／90／109（12:29:46.2888499／12:30:09.2559235／12:30:32.5669296）。正本 `failed-{zh,en,ja}-patched.png`。
- <b>覆盖账怎么记（⛔ 与真实读数同账）</b>：名册 35 颗——真实操作收到的仍是 <b>32 颗</b>；这三颗记成「<b>补丁门已上屏・真实可达性未验证</b>」，⛔ 写「35／未见 0」。下一手要翻这一格，得先把那三支的真实入口做出来（或在点验单上明写「只能靠补丁门量」），⛔ 拿这一腿当结案。
- <b>还原复称（这一腿最重要的一颗闸）</b>：跑完三臂就地把两颗档改回去——`GdiScreenCapture.cs` 盘上现读 sha256 `bfb1bec138b4c292574e6a0a15e3446c9638f653125e12929cfde799dc067657`＝补丁前基线<b>逐字节相等</b>（CRLF 259→258・LF-only 0・孤CR 0）；`ScreenshotService.cs` sha256 `d4fb4aa7ac928778874ef04cc5557da9532ee64e023a6d10dad02a4e55a46e3e`＝补丁前基线相等（CRLF 381→379）。残留扫描：`TEMP-P137`／`STARMARK_P137_FORCE`／`block137` 在 `src/**/*.cs` ripgrep <b>零命中</b>（⚠ 第一回我用递归 `grep -rn` 扫 `src tests tools`，被 `bin`／`obj` 里的生成副本拖到超时——那颗尺⛔ 会自己筛构建目录，改用带 glob 的尺）。还原后重建 solution＝0 警告／0 错误 29.78 秒，定向证人 `restored-137.trx`＝<b>134 通过／0 失败／0 跳过</b>（两档 `git diff --numstat` 回到本批其余改动之内）。
- <b>沙盒三跑</b>：PID 12628（魔数臂）／11108（`disabled`）／7808（`grabfail`），数据库与设置仍指 `D:\StarMarkTmp\p137`，四把写／删开关照旧关着；三跑都 `Alt+F4` 正常退出、`list_windows` 现读该 app 窗口 0 颗、`Get-Process StarMark.UI` 计数 0。UI 正本 `ui-evidence.json`＝<b>32 条・标识全不重复・每条 `image` 现读都在盘上</b>（其中 9 条带 `-patched` 尾缀＝这一腿）。
- <b>仪器那一笔</b>：① 提示卡那颗复用窗只在<b>主窗压住右下角那块</b>时才被 `get_window_state` 带回来——本腿先把主窗拖到 originX825／663 与 744／444 再按 F1，九发一次都没漏；② 按钮一律按 UIA 身份点（`ID: SettingsButton`／`ID: LanguageSelect` 现读那颗 index 再点），语言弹层按那颗 200×180 独立窗的截图像素选行，⛔ 再按图标位置猜；③ 一次 `computer.click` 落在 (1103,20) 实际打开了「使用说明」弹层（那颗 `？`），Esc 收掉后改用元素身份——像素猜按钮这件事本批犯过两次，第二次才改成现读。

## 十、片137 可达性腿（把「键未摘而开关已关」那一格从⛔ 量过翻成量到了）

- <b>为什么这一腿值得单独跑</b>：「九」把 `disabledTitle` 记成「补丁门已上屏・真实可达性未验证」，而「能不能不靠补丁走到那一支」这件事本批一直⛔ 量过——账本里那颗欠项写的就是「开关改动那一拍会不会留下『键未摘而开关已关』的窗口・⛔ 量过」。这一格用还原后的构建就能量，⛔ 留在「未验证」里＝把我的懒惰写成产品的边界。
- <b>读数（正本 `D:\StarMarkTmp\p137\reachability137-读数.txt`）</b>：宿主＝本批还原后重建的那颗装配（`StarMark.UI.exe` mtime 12:34:06・PID 4084・<b>⛔ 任何环境变量门</b>）。① 基线：按 F1 ⇒ `get_window_state` 带回 `WinUI Desktop` 遮罩（键在册）。② 在 设置 → 拓展功能 把那颗截屏开关关掉（取法＝现读 UIA：`ID: CaptureStatusText` 那颗<b>前面</b>那颗 `切换开关`，⛔ 按像素猜），状态句当场转成「オフ：切り出しを開始する 3 つのキーはシステムに登録されず（F1、F3、未割り当て は前面のウィンドウに戻ります）、トレイの 3 項目も消えます。既にデスクトップに貼った画像は無傷で…」。③ <b>再按 F1 ⇒ `get_window_state` 只回 `StarMark` 一颗窗</b>：遮罩没起、提示卡也没出；日志 12:38:17～12:39:05 那三行「设置已保存」之间<b>一颗 `[Screenshot]` 都没有</b>＝`Start()` 根本没被叫到。④ 再开回来：状态句转「オン：切り取り F1・貼り画像 F3・文字認識 未割り当て（これは現在本当に効いているキー割り当て…）」⇒ F1 遮罩再次起（<b>双向都活</b>，⛔ 只量一个方向就下结论）。
- <b>判语（⛔ 产品结论，只欠我自己那一半）</b>：`CaptureGate.RegistersHotkey` 与 `ShowsTrayItem` 是<b>同一颗判据、两个读者</b>，而两个读者都在设置保存那一拍现投影 ⇒ <b>稳态下 `capture.card.disabledTitle` 没有入口</b>，那句词只能靠补丁门量到。这一格从此前的「⛔ 量过」翻成「<b>量到了・结论＝无入口</b>」；「九」那句「真实可达性未验证」对这颗键而言现在有了具体形状＝<b>已量到无入口</b>，⛔ 再写成一句空欠项。
- <b>要翻这一格得先定产品取向（归他）</b>：要么给一条「关着也注册」的哑键——设计注释明确反对（F1／F3 是裸功能键，替一个关掉的功能继续占着＝所有软件永久失去 Help／截图键，比「按了没反应」更糟）；要么就像现在这样把解释留在设置页那颗状态句里（它已经把「去哪打开・哪些键会还给系统」讲清）。我这一腿⛔ 替他裁这一条，只把「有没有入口」这件事量成读数。
- <b>沙盒还原</b>：`CaptureEnabled` 现读 `True`（关掉又开回来了）、`EnableTray=False`、`MinimizeToTray=False`、`UiLanguageCode=ja`；PID 4084 `Alt+F4` 正常退出、`Get-Process StarMark.UI` 计数 0。产品源码这一腿<b>一个字没动</b>（`git status` 仍只有本批那 10 颗改动），文档四闸门＝通过 23／失败 0／跳过 0（正本 `D:\StarMarkTmp\p137\docgates137-reachability.trx`）。

## 十一、片137 账本回写（那颗「⛔ 量过」的欠项现在有形状了）

- <b>动了哪一档</b>：`docs/待决策事项.md` 里 <c>P-194</c> 那一节的那颗子行「顺手挖到的一格欠项」之后，<b>就地插一颗同形子行</b>（`  · ` 起首・一行），写「片137 把那一格量到了」＋四件事：基线 F1 起遮罩・关掉开关后 <c>CaptureStatusText</c> 那句当场转・再按 F1 屏幕上什么都没发生（日志连一颗 <c>[Screenshot]</c> 都没有）・再开回来又起（双向）。结论收窄成「<b>稳态与改动那一拍都没有入口</b>」，并把「要把它变成用户真能看见的一句话」显式交成产品取向（哑键 vs. 设置页状态句），⛔ 我替这一格裁。
- <b>插法的闸门</b>：锚＝那颗子行句尾的原文（现读整档 <b>恰一颗</b>）＋锚后两颗字节必须是 <c>CRLF</c>；插入体由 <c>D:\StarMarkTmp\p137\ledger137-insert.md</c> 那份 LF 草稿转成 CRLF 后<b>按字节</b>拼（⛔ 文本模式重写整档——那颗档三族行尾并存：CRLF 8469・LF-only 96・孤CR 13390）。复称＝改后 CRLF 8470（＋1）・LF-only 96（等）・孤CR 13390（等）・`git diff --numstat`＝<b>1／0</b>（只增不减）・锚仍恰一颗・新行恰一颗；新档 sha256 `ca4916bafe2fc835e12f926f3de4c1c3979ab34fab5ce8fa5f68efb0e239099b`。
- <b>本笔新立 0／结案 0</b> ⇒ 账本计数器四串⛔ 重跑也⛔ 报新数（那颗尺的读数归它自己的批次）；文档四闸门＝通过 23／失败 0／跳过 0（正本 <c>D:\StarMarkTmp\p137\docgates137-ledger.trx</c>・那一跑读的是盘上最终形状，含本节与报告那四条追加）。⛔ 提交、⛔ push（未推 304・HEAD `fb98c167b5760f4b842bacf6e71c330bd3c4e879`）。

## 十二、片137 编码臂腿（把「普通操作走不到」从一句我相信落成一把会红的尺）

- <b>这一腿问的是什么</b>：「九」把 `encodeFailed`／`failedTitle` 记成「补丁门已上屏・真实可达性未验证」，「十」把 `disabledTitle` 量成了「稳态与改动那一拍都没有入口」。剩下这两颗⛔ 是真机能摆的（摆出来要拆掉一道守卫），所以这一腿换成<b>读码＋证人尺</b>：先追清「到底有哪几处会把像素交给那颗编码器」，再把这件事钉成有人改坏就红。
- <b>读码追到的调用点（三处・⛔ 两处）</b>：`CaptureOverlayWindow.Actions.cs` 那颗 `Commit(CommitAction.Copy)` 交 `final.Width, final.Height`（`FinalPixels()` 三条返回路）、`ScreenshotService.CopySelectionAsync` 交 `crop.Width, crop.Height`（`TryCrop` 的裁块）、以及<b>先前一直没算进来的一颗</b>＝`CanvasService.SnapshotToClipboard` 交 `shot.Width, shot.Height`（画布快照・category 是 `settings.extend.canvasTitle`）。三处的宽高与缓冲各由这些守卫兜着：`AnnotationPainter.Render` 的 `if (width <= 0 || height <= 0) throw new CaptureFailureException(LocMessage.Of("capture.canvas.badSize", width, height));`、`BitmapTransform.Crop` 的 `if (w <= 0 || h <= 0) throw ... "capture.geom.cropEmpty"`、`LayeredCanvasWindow` 构造函数那颗 `if (boundsPhys.Width <= 0 || boundsPhys.Height <= 0)`（`Width`／`Height` 是 get-only・建完就⛔ 会变），缓冲则一律由被问的那颗档自己按 `width * height * 4` 现配（`GdiScreenCapture` 那两处分配点），截图态的宽高下限另有 `MinSelectionSide = 3` 与 `Intersect` 那句 `if (right <= left || bottom <= top) return null;`。⇒ <b>结论：`encodeFailed` 结构不可达</b>，它是防御枝而⛔ 一条有人能走到的路。
- <b>落的两把证人</b>（`tests/StarMark.Tests/CaptureReasonGateTests.cs`・定向集从 134 变 <b>136</b>）：`EncodeFailureArmCallSitesAreTheThreeGuardedOnes`（用 `SourceGate.ReadRepoUnder("src")` 现读「哪几颗档里有 `CopyPixelsAsync(`」并把那颗<b>档名集合</b>钉成三颗，再逐颗钉上面那些守卫原文）＋`TheEncodeFailureRulerItselfBitesOnAKnownShape`（⛔ 死尺：注入一颗已知调用形状必须命中、注释里那颗冒充的串必须被抹掉、被问的七颗档现读都⛔ 像空档）。
- <b>变异臂两颗（新尺首跑全绿⛔ 算证过）</b>：M1＝把 `AnnotationPainter.Render` 那颗 `badSize` 守卫从盘上删掉 ⇒ 红的正中 `EncodeFailureArmCallSitesAreTheThreeGuardedOnes`，另一颗照绿（维度分得开・正本 `D:\StarMarkTmp\p137\encode-arm-m1.trx`）；M2＝在 `OcrService.cs` 里加一颗假调用点 ⇒ 同一颗尺红在 `Assert.Equal() Failure: Collections differ`（集合那一维真的在数・正本 `encode-arm-m2.trx`）。正件首跑 `encode-arm-green.trx`＝2／0，两臂还原重建后 `encode-arm-final.trx`＝<b>136／0／0</b>。
- ⚠ <b>这一腿最痛的一课（还原判据＝sha 逐字节相等）</b>：M2 的落地脚本用 `ob[:-1] + decoy + "}\n"` 拼档尾，多塞出一颗 `}`；还原时只按原样删掉 decoy ⇒ 盘上 sha 停在 `f92269e2…` 而⛔ 等于基线 `d18fd367…`，靠<b>改前先记 sha・改后再复称</b>当场抓回（补一刀去掉那颗多余括号才回到基线）。M1 更阴：删掉守卫后把同一行插回 `var output = ...` 之前，句子一字不差、功能等价，但守卫的<b>顺序</b>变了 ⇒ sha `431afd4f…` ⛔ 等于基线 `7f9c4a43…`，最后用 `git show HEAD:` 的字节写回才对上。⇒ 「看起来一样」「能编译」「测试全绿」三样都⛔ 是还原的凭据，<b>只有 sha 相等是</b>；变异臂的还原腿一律「改前记 sha → 改 → 跑 → 还原 → 复称必须等于那颗记下的数」。
- <b>`failedTitle` 那一颗的读数形状（⛔ 真机・⛔ 补丁）</b>：它的三条出口都是<b>需要外部故障</b>那一类——`GdiScreenCapture.CaptureVirtualScreen` 有八颗失败返回（`capture.grab.zeroSize`／`tooLarge`／`noScreenDc`／`noMemoryDc`／`noBitmap`／`blitFailed`／`readPixelsFailed`／`failed`）、`WindowInterop.ListMonitors()` 交回空表、以及建 `CaptureOverlayWindow` 抛异常那一条。正常桌面会话里用户操作⛔ 能叫到它们（拔屏／分辨率切换／GDI 资源耗尽才算），本机⛔ 实造 ⇒ 这颗键的"真实可达"记成「<b>代码上可达・需外部故障・⛔ 本机实造</b>」，而「九」那张补丁卡量到的正文是兜底那颗 `capture.grab.noFrame`（`Grab()` 其实成功、`Error` 为 `null`），两件事⛔ 混写。
- <b>覆盖账照旧口径</b>：真实操作收到 <b>32</b> 颗；`encodeFailed`＝结构不可达（现在有一把尺守着）、`disabledTitle`＝已量到无入口、`failedTitle`＝可达但需外部故障——三颗都⛔ 算「真实上屏过」，补丁门那九张卡单独记档。
- <b>收口</b>：solution 重建 0 警告／0 错误 40.43 秒；`git status` 仍只有本批那 11 颗改动（`AnnotationPainter.cs` 与 `OcrService.cs` 都回到各自基线字节）；文档四闸门＝通过 23／失败 0／跳过 0（正本 `D:\StarMarkTmp\p137\docgates137-encodearm.trx`）；HEAD 仍 `fb98c167b5760f4b842bacf6e71c330bd3c4e879`・未推 304・⛔ 提交 ⛔ push。

## 十三、片138 选区判据腿（「太小」那句为什么从没响过——答案是一颗参数递错了）

- <b>这一腿干了什么</b>：把片137 挂着的那句"`capture.select.tooSmall` 整条账本命中 0 次"从"仪器摆不出"翻成<b>产品缺陷＋已修</b>（✅P-264），并顺手判掉同一枝里那句"候选微抖动仍未判定"。
- <b>破局那一发手势长什么样（下一手要复现照这个来）</b>：⛔ 需要亚像素控制——只要<b>横向拖一条</b>（起点与终点同一行）就得到一条高 0 的选区。仪器换算现读：主窗/遮罩那颗截图 1228×768 对应物理 1920×1200 ⇒ 图像 1 px＝物理 1.5625 px，所以图像 (400,400)→(464,400) 就是物理 (625,625) 起点、100×0 那一条（产品自己的角标把这两个数念了出来，⛔ 我算的第二真源）。
- <b>修完之后哪里变了</b>：不对称细条（一边 ≥3、一边 <3）在<b>松开那一拍</b>就被 `ShowError` 挡下并挂角标；两边都 <3 仍走"按在候选上＝确认候选／按在空白＝静默清掉"那一枝（注释里那句分工照旧成立，真机读回 `SizeText`＝候选窗自己的尺寸）。提交那一拍（`Actions.cs`）现在也真会拒——两颗读者一字未动，是判据学会了看选区自己。
- <b>⛔ 别再把"全量全绿"当这一格的凭据</b>：这一格坏在<b>读者怎么递参</b>，而单测一直按契约递对的参数 ⇒ 6615 全绿与它⛔ 相关。新钉的那颗 `TwoStageCaptureTests.BothSelectionGatesHandTheMonitorAndTheGuardMeasuresTheSelection` 才是这一维的证人；改这两行调用点必红。
- <b>两颗仪器雷（本批现场踩到，下一手先读这两条）</b>：
  · <b>绿的那棵树⛔ 等于屏幕上那棵树</b>：改完 Core 只跑 `dotnet test` 会绿（它测自己那份拷贝），而 UI 输出树里那颗 `StarMark.Core.dll` 还是旧的（本批现读 `49ba83ae`・12:54 ↔ 已修 `c622f0ac`・13:24）⇒ 起沙盒前必须整解决方案重编，并<b>逐颗比 UI 输出树里那颗依赖 dll 的 sha == src 输出那颗</b>。同族第四犯（前三次见名册 435／480／799）。
  · <b>`list_windows` 会漏掉遮罩那扇窗</b>：F1 之后 SDK 只回主窗，我差点判成"热键没生效"；用 `EnumWindows` 现读那颗 pid 的可见顶层窗才看到 1920×1200 那一扇（hwnd 每跑都变，取号⛔ 猜）。⇒ 量遮罩之前先 EnumWindows 拿 hwnd，再 `get_window`＋`get_window_state`（坐标输入之前必须先取一次 state，否则 SDK 直接拒）。
- <b>还欠的（⛔ 假装收完）</b>：① `capture.select.offscreen` 仍⛔ 真机量到——它的前置是"与整块屏完全不相交"，而释放前选区已被 `Intersect(…, _monitor)` 夹回本屏 ⇒ 代码上可达・真机摆不出・⛔ 本机实造，与 `encodeFailed` 同格处置；② 多屏那一途（跨屏拖选、每屏各起一扇遮罩）本批⛔ 碰——本机只有一块 DISPLAY1；③ 那两句的<b>观感</b>（措辞那颗「的」与"松开就响"的手感）归他点验，我⛔ 改语言表原文。
- <b>本批读数</b>：先红 `toosmall137-red.trx` 3／1 → 改后 `toosmall137-green.trx` 112／0／0；解决方案重编 0 警告／0 错误 33.09 秒；账本计数尺双跑 264/139/125 → 265/140/125（VERDICT OK）；沙盒三跑 pid 3060／5232／20008 全部 `Alt+F4` 退出、进程数 0、窗口数 0；读数正本 `D:\StarMarkTmp\p137\toosmall139-读数.txt`。

- <b>全量收口</b>：`dotnet test StarMark.sln -c Debug -p:Platform=x64 --no-restore` ＝ <b>6617 通过／0 失败／0 跳过</b>（16 m 56 s・正本 `D:\StarMarkTmp\p137\fullclose138.trx`），基线 6615（片137 收口那颗）⇒ 差 +2 恰＝本批两颗新证人。⚠ 顺序：全量跑在文档三档最后追加<b>之前</b>，文档那一维由四闸门 23 格在追加之后单独复称（`docgates138-close.trx`）——下一手要拿"全量＋文档改动同一棵树"的读数，得再跑一次全量。

- <b>修完之后那颗正向对照（⛔ 让「改了判据」停在「没把正常操作一起改坏」这一步）</b>：同一具沙盒（pid 6436・已修那颗 `StarMark.Core.dll`）再走一发正常拖框：图像 (300,250)→(500,400)＝200×150 图像像素＝物理 313×234，`Ctrl+S` 之后日志 `[截图] 已存图：313 × 234` ＋ `[提示卡] 已贴上屏幕：截图 · 已存图（1580,1077 324x107）`，而落盘那颗 PNG 的 IHDR 现读<b>313 × 234</b>（两颗通道各数一次：日志那句与档头字节）。⇒ 正常选区照旧交得出去・缩放换算与产品自己念出来的数对上（图像 1 px＝物理 1.5625 px）。⚠ 另两格本具沙盒⛔ 摆得出：跨屏「大半在屏外」那一档（本机只有一块 DISPLAY1，指针出不了屏）与贴图态那条细条（要走 `Commit` 那颗 `!_pinned` 分支，本批⛔ 量）⇒ 照旧登记⛔ 真机读数，⛔ 拿单测绿顶掉。正本 `D:\StarMarkTmp\p138\regress-normal-selection-读数.txt`。

- <b>贴图态那一腿的半格读数（⛔ 把仪器的 limit 写成产品的错）</b>：F3 起贴图模式遮罩→拖一条正常框→松手，日志「[贴图] 已钉住：313 × 234（共 1 张，上限 12 张）」＋「贴图 · 已钉住」卡上屏，`EnumWindows` 现读那扇贴图窗矩形＝<b>(469,391) 313×234</b>＝释放那一块的原样（图像 300→469・250→391）⇒ 松手即钉那一途按 placement 精确落位，本批改的那颗守卫⛔ 伤到它（贴图态走 `!_pinned` 另一颗门）。另半格⛔ 判定：贴图 body 上「未拿笔拖动＝移动整张」（`Pin.cs` 与 `Pointer.cs` 两处注释都这么声明）我两发 drag （快・慢各一发）之后贴图矩形两次现读⛔ 动、截图里也⛔ 有笔迹——但<b>⛔ 就此判产品</b>：那扇窗是我用 `EnumWindows` 拿到的 hwnd 手工绑给 SDK 的（`list_windows` 认不到它，见上「两颗仪器雷」第二条），随后同一扇窗上 `click` 先报 `call get_window_state before issuing coordinate input`、显式带 `screenshotId` 又报 `unknown screenshotId` ⇒ <b>我那两发输入到底有没有落到这扇窗⛔ 证成</b>。已排除两条：贴图窗 exstyle＝`0x80`（只 TOOLWINDOW・⛔ WS_EX_TRANSPARENT）⇒ ⛔ 鼠标穿透；截图⛔ 有笔迹 ⇒ ⛔ 默认亮了笔。⇒ 这一格照旧登记⛔ 真机读数（观感类，归他点验最省），⛔ 立缺陷、⛔ 进发布说明。可复用一条：<b>手工 `get_window(hwnd)` 绑来的窗可以读、⛔ 可靠写</b>——要往某扇窗打坐标，先问它在不在 SDK 的注册表里（＝`list_windows` 认得它吗）。正本 `D:\StarMarkTmp\p138\pinmode138-读数.txt`。

## 十四、片138 可访问名腿（截图工具条那 16 颗按钮第一次念得出自己是谁・✅P-265）

- <b>为什么会撞上</b>：想量 PU（标注越框被裁）就得"按名字指到那颗画笔"，而 UIA 树里工具条 16 颗全是裸「按钮」——图标做 `Content`、只挂了 `ToolTip`。⇒ 下一手⛔ 想按名字点工具条，先确认这颗已办（本批）。
- <b>改了什么（只一行＋一颗 using）</b>：`CaptureOverlayWindow.ToolBar.cs` 的 `IconButton` 里，挂完 `ToolTipService.ToolTipProperty` 之后，把<b>同一颗</b> `tip` 再挂一次 `AutomationProperties.NameProperty`。⇒ 名字与悬停说明⛔ 两份文案；走 `LocText` 那份名单，换语言跟着重贴（真机 zh 跑念出的就是 zh 整句）。
- <b>下一手要接着办的两格（都在本档别处也挂了号）</b>：① 其余代码建的图标按钮（画布工具条、设置页那几排）同病嫌疑——现读口径＝`grep -rn "AutomationProperties" src/StarMark.UI --include=*.cs`（改前零命中），办之前先量"哪几排在 UIA 里念不出"，⛔ 凭档名猜；② PU 那一格要真机判，得<b>先按一次弹层把它关掉再画</b>（点工具那颗会开颜色／粗细栏，第一发按下被 light-dismiss 吞掉），且⛔ 把 Ctrl+S 排在补画之前（提交会关窗，hwnd 当场失效）。
- <b>仪器课（本腿三颗，都已进名册）</b>：`SourceGate.Code(x)` 收的是<b>文件正文</b>⛔ 路径（我把路径递进去，断言扫了那颗路径串，红得⛔ 代表产品）；变异臂还原只认 sha 逐字节（`12dc6cf5b81cf0c7…` 前后相等才算）；整解决方案重编＋比对 UI 输出树里那颗依赖 dll 的 sha，才允许起沙盒。
- <b>读数</b>：0 警告／0 错误 16.51 秒・定向 23／0（`a11y138-green2.trx`）・变异臂 1／22（`a11y138-mut.trx`）・真机前后各一跑（改前树＝裸「按钮」×16，改后＝16 颗各有整句名）・沙盒两跑退出后 `StarMark.UI` 进程数 0。正本 `D:\StarMarkTmp\p138\a11y138-读数.txt`。

## 十五、片139 画板工具条那一排（✅P-265 普查交回的第二宿主・两处各一行・真机 29 颗念得出）

- <b>普查怎么做的（下一手照这个口径，⛔ 起窗猜）</b>：现读代码里所有「用代码造出来的按钮」的 `Content` 是什么——带文本的（`MainWindow.Status.cs` 那颗、`BoardWindow.cs` 那五颗 `Word(...)`）名字白送；纯图标的才要办。结果只剩画板那两排：`CanvasToolbarWindow.xaml.cs` 的 `BuildShapeButtons`（五颗图形）与那一排色点（`Content` 是一颗圆 `Border`）。
- <b>已一并办到的（⛔ 再去找第二出口）</b>：截图那三排选择栏（图形栏／颜色点／粗细点）在 `CaptureOverlayWindow.Pickers.cs` 里，走的是<b>同一颗</b> `IconButton` 工厂 ⇒ ✅P-265 那一行已经覆盖它们。
- <b>改法与那颗坑</b>（<b>本节这条已被片140 改口</b>：这颗窗口⛔ 挂 `LocText.SetMessage`，那版被自家闸门 `CanvasBarWordGateTests` 全档禁掉；现在写的是「两处各登记键＋没渲染的实参，取词落在 `Refresh` 体内」）；图形那句的实参里嵌着工具名 ⇒ <b>必须交没渲染的那颗 `LocMessage`</b>，交成品字符串就是「换语言它一个字都⛔ 动」（✅WI-36／✅P-181 同一条口径，本批是它第三次咬人）。
- <b>真机口径</b>：改后 29 颗逐颗念得出（正本 `D:\StarMarkTmp\p138\canvas-a11y-139-读数.txt`）；改前那一半在片140 <b>补上了，而且⛔ 动 HEAD 树</b>：把取词那一行就地摘掉重编＝改前那一版的形状，UIA 现读 29 颗里恰 11 颗名字空（五颗图形＋六颗色点）；还原重编＝0 颗空 ⇒ 两半各有一跑、同一把尺。
- <b>起画布的前置（省下一手一次空跑）</b>：沙盒那颗 `CanvasEnabled` 默认在<b>副本</b>里是 false，不改它 Ctrl+Alt+D 只会回一句「屏幕画布已关闭」（那颗键是 `canvas.notice.boardClosedTitle`／`boardClosedBody`，本批顺带量到它真上屏说话）；改的是 `D:\StarMarkTmp\p137\settings.json`，他真设置档⛔ 动。
- <b>还欠的</b>：任务 #13（PU 那一格・⛔ 本具仪器在标注阶段建不起一笔，「撤销」三跑都照旧 disabled，正本 `D:\StarMarkTmp\p138\pu138-读数.txt`）・任务 #16（画板那块「写闸拒了」面板的三语逐字・只改沙盒副本里那颗 `board.loaded` 的回话，⛔ 动产品源码）。

## 十六、片140 画板工具条那一排的机制改口＋前后复称（自家闸门否掉第一版・同一把尺改前后各一跑・活口换语言四跑）

- <b>改口</b>：第十五节那两行 `LocText.SetMessage(button, 名字属性, …)` 被 `CanvasBarWordGateTests` 按整档禁掉 ⇒ ⛔ 放宽闸门，按它指出的机制重写：建条时登记「键＋没渲染的实参」两颗，取词落在 `Refresh` 体内那一发 `AutomationProperties.SetName(button, message.Render())`（这颗窗口的重贴节拍已经是 `Locale.Changed += Refresh`）。
- <b>证人跟着改口・两臂各红一颗</b>：登记颗数恰二・全档那颗名字调用恰一・且<b>必须落在 `Refresh` 体内</b>。臂一＝摘掉取词那一行 ⇒ 1 失败／23 通过；臂二＝把同一行挪进构造期（全档颗数照旧一颗）⇒ 也红，红在体内那一颗。两跑后按字节还原（sha 逐位相同・numstat 删除列 0）。
- <b>仪器那一笔（省下一手一次空跑）</b>：这扇窗用 Windows 自带的 UIA 客户端程序集就能读（PowerShell `Add-Type`），⛔ 装任何第三方件；两个坑：①画板工具条是 `WS_EX_TOOLWINDOW`（扩展样式 0x88）⇒ Computer Use 的名册不带它（`list_windows` 只回主窗、`get_window` 报句柄失效）；②UIA 自己报的句柄≠`EnumWindows` 那颗（现读 1574402 对 0x180602），拿后者 `FromHandle` 必抛 `ElementNotAvailable` ⇒ 先按 RawViewWalker 遍历桌面 children、用类名 `WinUIDesktopWin32WindowClass` ＋名字认出那扇窗再下钻。换语言那一发同理：按 `AutomationId=LanguageSelect` 认那颗组合框，<b>按序号</b>选条目（把中文词写在命令行会被 PS 5.1 按 ANSI 拆）。
- <b>读数</b>：改前（就地摘掉取词那一行重编）＝29 颗里恰 11 颗名字空（五颗图形＋六颗色点），还原重编＝0 颗空；活口 zh→en→ja→zh 四跑每次 29 颗一颗不落，英文态条子自己变宽 1099×90 → 1302×106。定向 103／0／0・整解决方案重编 0 警告／0 错误・沙盒退出后窗口 0 进程 0・正本 `D:\StarMarkTmp\p139\canvas-a11y-140-读数.txt`。
- <b>还欠的</b>：任务 #13（PU 那一格・⛔ 本具仪器在标注阶段建不起一笔）・任务 #16（画板那块「写闸拒了」面板三语逐字）・任务 #17（`canvas.notice.boardClosedTitle`／`boardClosedBody` 三语逐字・零补丁，把沙盒副本那颗 `CanvasEnabled` 折回 false 就走到）。

- <b>片140 收口读数</b>：`dotnet test` 带 `--logger` 那一跑（trx 名 `fullclose140b.trx`）＝ 失败 0／通过 6619／总计 6619（474.8 秒・trx 逐颗清点 Passed 6619／Failed 0／NotExecuted 0／Skipped 0），正本 `D:\StarMarkTmp\p140\fullclose140b.trx`。⚠ <b>两半分开记</b>：这一跑的读数出自<b>写下本条之前</b>的那棵树（代码已定稿、文档还没有这一条）；本条落盘后重跑的是<b>文档闸门那一档</b>，颗数与结果记在 `D:\StarMarkTmp\p139\canvas-a11y-140-读数.txt` 档尾，⛔ 把「全量 0 红」说成覆盖了改文档之后的树。

## 十七、片140 零补丁收「屏幕画布已关闭」三语逐字（任务 #17 结案・成对判据自己算・提示窗正文住在子 bridge 窗）

- 走法：把我那副本 `CanvasEnabled` 折回 false（三语各起一颗设置副本，⛔ 动他真档）→ 一发 Ctrl+Alt+D → 后台 watcher 盯那颗提示窗<b>可见性 False→True 那一翻</b>就抓 UIA 正文（那颗提示 10 秒自己消失、分两次工具调用一定扑空）。
- 三语逐字已收（见报告第五百二十三那条，正文与 `UiStrings.cs` 现读渲染<b>九格全对</b>；两臂验尺：跨语言比⇒3 红、换实参⇒1 红）。顺带收到那颗提示自己的第三行 `card.hint.plain`（三语各一颗，停留秒数那颗槽填的是 `KeepSeconds`＝10）。
- 仪器那一笔：①顶层句柄 `FromHandle` 对提示窗只回一颗空 Pane，正文在子窗 `Microsoft.UI.Content.DesktopChildSiteBridge` 上；②本机 python 的 `SendInput`（键）打不动那颗全局热键——`rc=1`、`sizeof(INPUT)` 修成 40、前台已确认、两侧都 Medium，而 Computer Use 同一发打得动 ⇒ 这条⛔ 是产品结论，是<b>我这具仪器的边界</b>，任务 #19 起手前先解决它。
- 收口：三跑各退出后进程数 0・`StarMark*` 计数 0・⛔ 改产品源码⛔ 动真设置档・正本 `D:\StarMarkTmp\p140\boardclosed-140-读数.txt`。
- <b>还欠的</b>：任务 #13／#19（PU 那一格——真鼠标注入这条新腿先要过 `SendInput` 那颗）、任务 #16（画板「写闸拒了」面板三语逐字・只改沙盒装配里那颗 `board.loaded` 的回话）。

## 十八、片140 画板「写闸拒了」面板三语逐字（任务 #16 结案・临时补丁门只改装配副本・九格成对）

- 走法（下一手照这个）：`patch_bridge_gate.py patch` 只把<b>装配副本</b> `Assets/Boards/board-bridge.js` 那句 `board.loaded` 回话改成 `elements: writable + 1` ⇒ 宿主三颗数对不上；`board_leg.py` 用 `STARMARK_START_BOARD=last` 起画板窗（托盘那一条 `OpenBoardsWindowAsync`，⛔ 另起主人）；额度＝`BoardPolicy.ArmRetryBudget = 1` ⇒ 拒两次就开口。跑完 `restore`，sha 逐位复称相等。
- 三语逐字已收（三串 × 三语＝9 格全对，正本 `D:\StarMarkTmp\p140\stuck-screen-140.json`；机制与读数正文见报告第五百二十四）。
- ⚠ 一颗⛔ 写成缺陷的读数：UIA 树里那三行各重列 5 次（连 web 侧五颗按钮一起重）＝同一子树被多个宿主层重列，屏幕上只有一颗面板（截图为证）⇒ 「树里数到几颗」⛔ 等于「屏幕上有几颗」。
- 仪器那一笔：①这扇窗的 XAML 正文只有 Computer Use 那把 UIA 尺读得到，我这侧 .NET Framework 客户端读顶层与子 bridge 都只回空 Pane；②沙盒日志目录跟着设置副本的数据根走（`StarLog.LogDirectory` ← `RedirectedDataRoot()`）⇒ 换副本目录就要换找日志的地方，『没日志』先怀疑自己的指针。
- <b>还欠的</b>：任务 #13／#19（PU 那一格——先过 `SendInput`(键) 那颗边界）、观感点验与 #744 归他。

## 十九、片140 PU（标注越出选区）那一格量到了（任务 #13／#19 结案・真鼠标＋UIA 按名字 armed・两半各一把尺）

- 换掉的两个零件：①注入<b>鼠标</b>走我这侧 `SendInput`（绝对坐标＋`MOUSEEVENTF_VIRTUALDESK`，按下→逐步移动→放开，完事把光标还回原处）——片138 那句「分层遮罩上按下／放开递不到」⛔ 再当通则用，这次三发三中；②armed 那颗笔改 <b>UIA 按名字前缀 Invoke</b>（`uia_press.ps1 -PrefixCodepoints 753b,7b14`），⛔ 摸坐标点图标。
- 判据两半：屏幕上＝沿预测线逐列问<b>暗红</b>（遮罩把框外那半压到约 60% 亮度：(139,10,21) vs 笔色 (232,17,35)），命中 202 列、上下平移 60 像素的两条对照带各 0 列；导出＝同选区两跑逐像素差 151 颗，全在框内那一段（x149..199／y49..61），两颗都 200×150 ⇒ 越出去那半不进导出（按「导出＝选区那块」的既有口径）。
- ⚠ 两颗差点写错的判词（都靠对照救回）：①第一版按「亮红」数框外 ⇒ 报「0 颗＝没画」；②单看一张时框内那颗 y125..138 的红块被当成笔画，两跑相同才认出是背景。⇒ 读数册里把「我认的是哪副颜色」写清。
- 结论：批次 PU 那句「标注可以越出选区」在输入与显示两半成立，只有导出按设计裁掉 ⇒ ⛔ 新缺陷、⛔ 新 P 号；越出去那半只以暗红出现这一观感归他点验。
- <b>还欠的</b>：只剩归他的两格（观感点验・#744）与 push 授权；仪器边界那两条（`SendInput`(键) 打不动全局热键・画板窗 XAML 只有 Computer Use 那把 UIA 尺读得到）已记进片140 仪器课。

## 二十、片141 主窗六颗念不出名字的控件（✅P-266・屏幕级普查尺立案・LocText 新添 NameKey）

- <b>这把尺下一批接着用</b>：`unnamed_census`（桌面 RawView＋按 rect 去重＋两把筛子各报颗数）与 `names_check`（按 AutomationId 逐颗念出 Name）都在 p141；改前 6／改后 0 两份读数与三语活口读数在同一颗读数册里。
- <b>机制</b>：`LocText.NameKey` 落点写死 <c>AutomationProperties.NameProperty</c>，取词只有 `Paste` 那一处，摘键走 `Drop`（⛔ 只清字留着注册项）。⚠ 那颗档是 <b>CRLF</b>、`MainWindow.xaml` 是 <b>LF-only</b>——变异臂的锚点两颗都要按各自行尾写，颗数守卫要问的是「⛔ 混进第二族」而⛔「那一族颗数相等」（我这两条各误挡一次）。
- <b>一颗增量教训</b>：`dotnet build StarMark.sln` 报「成功」而那颗新测试档⛔ 进装配（筛子跑出来是「没有匹配的用例」）⇒ 新加测试档后必须 `--list-tests` 数一颗，或定向跑那一颗名字；绿 build⛔ 等于新档编译进去了。
- <b>还欠的</b>：设置八页那 13 颗 Name（下一批）；归他的两格（观感点验・#744）与 push 授权。
## 二十一、片142 设置页十八颗念不出名字的控件借名落地（✅P-267・三语活口复称・一把旧普查尺改口）

- <b>这一批做了什么</b>：`SettingsPage.xaml` 里 18 颗可交互元素（六颗有名字的下拉・七根滑杆・三颗无名下拉・两颗 NumberBox）
  各在自己那一行尾部借到<b>该行标题已在用的那颗键</b>（`LocText.NameKey`），整批⛔ 新增表行・⛔ 新增行（numstat 18／18）。
  新证人 `SettingsNameKeyGateTests`（逐颗配对＋整页颗数 18＋本页⛔ 字面 AutomationProperties.Name）；
  两臂变异各红在该管那一维并逐字节还原。
- <b>真机怎么复称的</b>：沙盒＋主窗最大化，zh／en／ja 三跑各 rows=292・unnamed=33・infinite_dropped=35，
  那 18 颗（加 AI 页两颗＝20）三语各自念话、配不上对 0・空名 0・三语同形 0；
  两根自定义预算滑杆靠把性能模式选到「自定义」当场摆出来⇒ 本批⛔ 留「现场摆不出」那一格。
- <b>⚠ 一把旧尺改口（读下一批之前先看这条）</b>：片141 那两把普查尺对 ±∞ rect 会把<b>上一颗元素的 rect</b> 带进落盘、
  再被去重吞掉真命中（实测 327＝292＋35）⇒ 那份「念不出名字」清单<b>只漏报⛔ 误报</b>；
  「主窗六颗→0」那条另由逐颗打印 Name 的正尺撑账，⛔ 受影响。后继尺自报漏掉颗数。
- <b>还欠的</b>：屏幕上剩那 <b>33</b> 颗行按钮（热键页 16＋组件页 17，主人是行模板）＝下一刀，
  开工前先现读那一族的模板与行模型交不交出「标题的那颗键」；归他的两格（观感点验・#744）与 push 授权。

- <b>批末收口（片142）</b>：全量 6624／6624・账本计数器 268／143／125（VERDICT OK）・证人档改口后复跑六档 118 格全绿・HEAD fb98c16・未推 304・⛔ commit／⛔ push・StarMark 进程 0。
## 二十二、片143 代码建的那 33 颗 Expander 抬头借名落地（✅P-268・八页归零・新立 P-269）

- <b>这一批做了什么</b>：新增一颗扩展方法（<code>src/StarMark.UI/Helpers/ExpanderTitleName.cs</code>）把
  Expander 的可访问名交给 <code>LocText.NameKey</code>，热键页与组件页两颗建造处各改<b>结尾那一行</b>
  （<code>}.TitleNamed(抬头那颗键)</code>）⇒ 八张设置页"念不出名字的可交互元素"从 <b>33 颗</b>降到 <b>0</b>，
  三语（zh／en／ja）各 rows=290・unnamed=0・infinite_dropped=35。整批⛔ 新增表行・⛔ 第二份文案・⛔ 动行数
  （两颗建造档 numstat 各 1／1，行数 546／234 一字不动）。
- <b>⚠ 记下一把尺的射程</b>：那颗行号棘轮只问"端点在不在文件里"。第一版插了 4~5 行，它报的数从 32 <b>掉到 31</b>
  而⛔ 涨红＝另有几颗号漂到别的正文上却照旧存在。⇒ 换成"⛔ 动行数"的形状，两档先还原（sha 逐字节对过本轮记录的改前值）再落。
  下一批要往<b>有行号引用</b>的档里加行之前，先问这一句。
- 证人三格 <code>tests/StarMark.Tests/ExpanderNameKeyGateTests.cs</code>（配对・两颗宿主・全仓那颗 SetNameKey 只一处）
  ＋变异臂两臂各红在该管那一维，还原后逐字节相等。邻域 10 档 168 格全绿。
- <b>新立 P-269（开放）</b>：设置页「更新说明」那颗 XAML Expander 挂着 <code>LocText.Key</code>，按取词落点的候选顺序
  会贴中它的 <b>Content</b>（同有 Header）⇒ 抬头念不念得出／正文有没有被那句顶掉，<b>欠真机读数</b>；
  这一批试了一发「立即检查」，那一发没带回可读正文、元素在 UIA 树外 ⇒ 三个问句一条都答不了。点验单在 P-269 那条里，
  ⛔ 没有读数就改形状。
- <b>还欠的</b>：归他的两格（观感点验・#744）与 push 授权；批末收口颗数见下面那条。

- <b>批末收口（片143）</b>：全量 6627／6627・账本计数器 270／144／126（VERDICT OK）・真机三语各 rows=290・unnamed=0・infinite_dropped=35・HEAD fb98c16・未推 304・⛔ commit／⛔ push・StarMark 进程 0。批内两颗改口记在报告第五百二十八那颗收口读数里（开放项⛔ 戴✅・账本正文⛔ 写带点命名空间）。下一格＝P-269（那颗 XAML Expander 的落点，欠真机读数・点验单在账本里）。
## 二十三、片144 取色格并轨——两个宿主共用取色器（白衬与读数撤净・1 红学费）

- <b>这一批做了什么</b>：六档手术（cut144：WidgetAppearanceEditor 拆 HSV 十六步・SettingsPage.xaml・SettingsPage.xaml.cs・SettingsPageViewModel・MainWindow.Theme・WidgetAppearance）把取色格并轨——口径「两个宿主共用取色器」；白衬与它的读数已灭（点选反馈改由高亮排＋方图指示），hex 拼写四臂逐字钉（含高位补零）。证人 AccentPaneGateTests。
- <b>⚠ 1 红学费</b>：首跑 6628 格 1 红（23 m 28 s，作废）＝活档指针落空普查超基线（32＞31）⇒ fix_fallen 按 P-256 ③ 三处删号留锚（替换体带正文原句・不含换行・断言不引入新指针）；二跑 6628／6628（17 m 26 s）。
- <b>账本</b>：无新立条目；收口计数器切片重建＝270／144／126。
- <b>还欠的</b>：归他的两格（观感点验・#744）与 push 授权。

- <b>批末收口（片144）</b>：全量 6628／6628・账本计数器 270／144／126（切片重建 VERDICT OK）・HEAD 35645ff・未推 307・⛔ commit／⛔ push。
## 二十四、片145 P-269 就地改判结案＋新立 P-270（三坑交账）

- <b>这一批做了什么</b>：P-269 就地翻✅结案（改判段 1→5 行）；新立 P-270（结案脸）＝三坑交账，第三坑落在 SettingsPage.xaml——两颗行号指针按 P-256 ③ 删号留锚。+24 格全绿（6652／6652・10 m 19 s）。
- <b>色线口径</b>：「主题色只改背景」两次用户裁决（2026-10-30）钉住这一带——自加卡片背板被否并撤净（ControlSurface・卡片 brush 全仓 0 次，守卫钉住）。
- <b>还欠的</b>：归他的两格（观感点验・#744）与 push 授权。

- <b>批末收口（片145）</b>：全量 6652／6652・账本计数器 271／146／125（切片 VERDICT OK）・HEAD 35645ff・未推 307・⛔ commit／⛔ push。
## 二十五、片146 新立 P-271（六步真机点验）＋账本两处删号留锚

- <b>这一批做了什么</b>：新立 P-271（结案脸）＝六步真机点验单落账；账本两处删号留锚（备份信封块＋✅P-117 那行，P-256 ③ 口径）。+32 格全绿（6684／6684・11 m 27 s）。
- <b>还欠的</b>：归他的两格（观感点验・#744）与 push 授权。

- <b>批末收口（片146）</b>：全量 6684／6684・账本计数器 272／147／125（切片 VERDICT OK）・HEAD 35645ff・未推 307・⛔ commit／⛔ push。
## 二十六、片147 新立 P-272（ja-JP 那门课）＋BoardWindow 删号留锚

- <b>这一批做了什么</b>：新立 P-272（结案脸）＝ja-JP 那门课交账；账本指向 BoardWindow 的那批行号指针按 P-256 ③ 删号留锚。+8 格全绿（6692／6692・14 m 55 s）。
- <b>还欠的</b>：归他的两格（观感点验・#744）与 push 授权。

- <b>批末收口（片147）</b>：全量 6692／6692・账本计数器 273／148／125（切片 VERDICT OK）・HEAD 35645ff・未推 307・⛔ commit／⛔ push。
## 二十七、片148 接深夜断头：image.insert 三入口落账（P-273・P-274・P-275）＋p149 双树假红课（#1104）

- <b>这一批做了什么</b>：接深夜（19:07–19:41）image.insert 的断头——形状一刀未动、只把账走平：九红重钉（edit149_gates，⛔ 放宽一条判据）・账本 1328 换锚（删号留锚，活档行号棘轮 31↔31）・manifest 刷新（board-bridge.js 21660 字节・sha256 前缀 469a9c75・excalidraw.min.js 4878772 字节・140 档）。构建 0 错误（1 m 12 s）。
- <b>三条入口</b>：截图浮条（capture.bar.board）／卡片菜单（card.menu.toBoard）／右键镜像——都在用户明确点那一下之后才递；新键七颗、按 P-212 口径摘孤儿键两颗（boards.pinAction／boards.pinnedBody）。
- <b>⚠ 仪器课（#1104）</b>：裸 dotnet test 钻进 bin\Debug 陈树＝974 格 8 假红（比新树少 45 格，新证人档没装进去）；-p:Platform=x64 的新树才是真读数（boards 口径 1019 格 4 红・全量 9 红）⇒ 手术后邻域 314／314（64.8 s）・全量 6692／6692（1006.5 s＝16 m 46 s）。
- <b>账本三颗新立（全开放）</b>：P-273＝画板前台全局键位共存（默认两套键位现读不相交；欠的那一臂＝画板前台且无截图会话时按 F1／F3／F4／F5，问 StarMark 动作起没起、画布有没有同时做事——(b) 为真才接会话态摘挂，为假就地结案）；P-274＝改口条（宿主注入 image 那条路真机是通的，坏读数出在夹具少一颗 id；「要他拍」取消）；P-275＝收尾立（欠三条入口的真机走查）。
- <b>本收口批的修口与裁决</b>：P-275 的 16 m 45 s 改 16 m 46 s（对齐 trx Times 1006.5 s）、尾句改「⛔ push＋分码腿／文档腿两笔提交」；工作树账本纯 CRLF（零游离），HEAD blob 里那批游离 \r（crcrlf 6710・lone_cr 6680・lone_lf 96）是片130 前的旧账——文档腿提交顺带洗净（numstat 预读 8935／8625）。
- <b>还欠的</b>：P-273 那一臂真机（问法在上面）・P-275 三链真机走查・归他的两格（观感点验・#744）与 push 授权。

- <b>批末收口（片148）</b>：全量 6692／6692（16 m 46 s）・邻域 314／314（64.8 s）・账本计数器 276／148／128（VERDICT OK）・真开放 P-273／P-274／P-275・HEAD 35645ff・未推 307・本批收口后分码腿／文档腿两笔提交（⛔ push）・StarMark 进程 0。
## 二十八、片150 P-275 真机三链走查收官：image.insert 四通道读数齐＋坑表 #1105

- <b>这一批做了什么</b>：P-275 欠的真机走查补齐——沙盒 bin6（HEAD 74c1f85 建物含三链终码）＋空根 data6，每跑第 0 道（他人实例 0＋GetLastInputInfo）后动 SendInput／UIA；三条入口（卡上菜单／右键镜像／截图浮条）各按四通道（屏幕位图／盘上 scene 往返／日志／成功卡）收数；读数正本 bin/WI99/p150_walk_readings.txt（证据帧在 D:\StarMarkTmp\p150\・⛔ 提交）；账本 P-275 就地结案（276／149／127 VERDICT OK・docgates 四尺 PASS）。
- <b>三链读数一句话版</b>：链一＋链二——测试图 160×100 经『放到画板上』两入口上画布，magenta=13076・bbox 落画布正中（两颗同位叠放），scene 1802→3359 B／files 带 id＋dataURL，两发 insert.posted bytes=422＋scene.saved，成功沉默；链三——托盘菜单（rows=31・index=5）→拖框 672×422→toBoard，第三颗 (376,209) 视口正中盖住前两颗（magenta 13076→1288，残余＝捕获区自带缩略图切片），insert.posted bytes=21449，成功卡『截图 · 已放上画板』『672 × 422 的画面已放上画板』。
- <b>仪器课（#1105＋一颗只登记）</b>：①SetForegroundWindow 返回 True≠真到前台——链一首截经 Boards rect 截到主窗缩略图（z 序假象）；取证口径＝SetFG→GetForegroundWindow 验回执→再截（坑表 #1105）。②浮条 toBoard UIA 名字前缀命中的是内层图标元素（27×23）无 InvokePattern——改真点击元素中心 (1010,742)（P-265 若指外层按钮则两说不矛盾，只记读数）。
- <b>还欠的</b>：P-273 那一臂（画板前台且无截图会话时按 F1／F3／F4／F5：问 (a) StarMark 动作起没起（应当起）(b) 画布有没有同时做事——(b) 为真才接会话态摘挂，为假就地结案）・P-274 改口条・归他的两格（观感点验・#744）与 push 授权。

- <b>批末收口（片150）</b>：走查零码改・账本计数器 276／149／127（VERDICT OK・docgates 四尺 PASS）・真开放 P-273／P-274・HEAD 74c1f85（文档腿提交前一刻）・未推 309・本批文档腿一笔提交・⛔ push。
## 二十九、片151 ✅P-276：画板导出 PNG → 贴桌面（同一发两种下落）＋坑表 #1106

- 沙盒：`D:\StarMarkTmp\p151\bin`＝本工作树 `dotnet build -p:Platform=x64` 产物<b>整份新拷</b>（⛔ 复用上一批 bin5——名册 435 那一族"沙盒产物树随 build 更新＝重跑测的还是旧二进制"）；数据根 `data`（拷自 p146\data5）；第 0 道＝他人实例 0 颗＋`GetLastInputInfo` 1244 s；PID 5664；收摊 REMAIN=0、EnumWindows 现读 StarMark/Boards 标题窗 0 颗。
- 三读一句话版：读一＝那一行上第五颗按钮在场且排在 PNG 与 SVG 之间（ja 念「デスクトップに貼り付け」・左界 1010→775 px・与引擎工具条各在一层）；读二＝点它 → 桌面正中起贴图窗＋`boards.pin.sent bytes=9989 w=400 h=180` 一颗＋导出目录零颗＋「打开所在文件夹」照旧点不亮；读三＝再点「PNG として出力」→ `boards.export.saved kind=png bytes=9989` 一颗＋目录恰好一颗＋贴图名册没多第二张＋那句落点回到 `exportSavedBody`。
- 本批唯一要紧的一格：那颗落点位清在 `ReportExport`（三条下落的共同收口处）而⛔ 清在成功那一支；证人三面钉死。
- **坑表 #1106**：往 `switch` 的枚举里加一臂，第一件事是问 `default:` 现在替谁干活。截图浮条「放上画板」第一版接在 switch 外面，而那颗 switch 的 `default:` 是「钉到桌面」——点一次得到两张落点（画板一张＋桌面一张）。全绿时是坏的：台架不覆盖 UI 分派，抓到它只靠"写完照读一遍那张表"。口径：能落成"每一臂各有自己的 case"就⛔ 让任何一臂住在 default 里，正件逐字钉 `case CommitAction.Board:` 在场。
- **只登记（⛔ 进坑表编号的一课）**：Computer Use 返回图的尺寸⛔ 等于显示尺寸——按显示坐标 (1305,78) 递点击报 `coordinate is outside the current target window bounds`，现读返回图 1255×768（显示 1792×1024，系数 0.700）。口径＝坐标一律按<b>当场那颗返回图</b>的 width/height 换算后再递。
## 三十、片151 第二腿 ✅P-277：快画快照 → 画板（深析 §四 八格全落）＋一条"注释⛔ 替读数背书"

- 沙盒：同一副 p151 树（本工作树 build 产物整份新拷）。Ctrl+Alt+D 起画布（canvas.toggle 默认绑法）→ 点「マウス透過」收穿透 → 真鼠标拖两笔 → UIA Invoke 那颗「キャンバス」。收摊点「✕ 終了」→ Stop-Process REMAIN=0 → EnumWindows 现读 0 颗 StarMark/Canvas 标题窗。
- 四通道：条子 rect (192,20)-(1727,126)（宽 1535／屏 1920・✕ 出口右界 1717・新按钮 rect 1341,30 94×32 夹在貼り付け与画像保存之间）；日志 insert.posted bytes=25447 w=457 h=357；盘上那块板 2514→37444・rolled=True；屏幕上那块板多出一张"红叉叠真实桌面"，玻璃上两笔原样在。
- **一颗尺的形状**：数装配里的字符串要按被数那一物的形状认——方法名在 #Strings 堆里是 UTF-8，字符串字面量是 UTF-16。拿 UTF-16 去数 <code>SnapshotToBoard</code> 得 0，据此会误判"新代码没进装配"；两头各数一遍才说"在不在"。
- 一颗只登记：真鼠标点击那颗按钮⛔ 动、日志零行，UIA Invoke 当场中（P-265／#1105 同族）。取证先走 Invoke。
## 三十一、片151 第三腿 ✅P-273：引擎快捷键整张表抄完・F1～F5 一颗也没有（八格全落）

- 取证路：STARMARK_START_BOARD=new 起一块空板 → 点画板窗右下角那颗「?」（UIA 里无名 ⇒ 按返回图坐标 (0.973,0.955) 点）→ 帮助框「キーボードショートカット」三屏滚到底逐颗抄 → 收摊 Stop-Process REMAIN=0、EnumWindows 0 颗。
- 结论：函数键只有 PgUp／PgDn；F1～F5 一颗也没有 ⇒ 那一臂读数为假 ⇒ 按该条自己写的收口口径结案，⛔ 建"画板前台摘全局键"那台机器。
- 顺带：面板里「画像を挿入」的键帽 9 是灰的＝引擎自标不可用，与 tools:{image:false} 源码读数在屏幕上对上；宿主注入那一路照走 ⇒ 两件事⛔ 互相牵连（P-274 改判又多一个通道）。
- 一颗⛔ 翻前案的仪器读数：WebView2 里<b>引擎自己的 DOM 节点在 UIA 树上无名</b>（宿主那六颗按钮反倒有名）⇒ 凡要点引擎内部控件，只能按<b>返回图坐标</b>递，⛔ element_index；这与 #1105（SetFG True≠真前台）同一族——可见⛔ 等于可寻。
## 三十二、片152 ✅P-278：第六族翻案（第三条链活着・六格归零・四颗数字框名量尽）＋坑表 #1107

- 沙盒：同一副 p151 树（本工作树 build 产物整份新拷两次・第二次为线程档那一发实验）。第 0 道＝他人实例 0 颗、IDLE_SEC=201.4；PID 28132(ja)→26828(en)→1704(en)；日志走沙盒数据根 D:\StarMarkTmp\p151\data\logs（他那颗真日志目录最新仍是 20261027＝⛔ 被碰）。
- 取证腿：settings.json 里 UiLanguageCode 切 en → 重启 → sweep.ps1 逐档（tabs 八格／nav 六页／all 全部顶层窗）现读每个元素自己的 Name，按 CJK 判红；osAids 那五颗（SystemMenuBar／Minimize／Maximize／Close／Item 1）另桶＝Windows 自己的字，⛔ 记在应用账上。
- 正件（链跟自表走）：ja 档 [ポップアップ] 一颗；反件（链⛔ 管到的）：四颗 NumberBox 名里 最小/最大 一字不改，且是在 thread=True procList=en-US 那一拍之后复称的。
- **坑表 #1107**：⚠ 分类器的形状⛔ 是事实——同一把 CJK 尺在 ja 档报 71～117 颗"残留"，其实那一屏本来就该是日文（0x4E00–0x9FFF 把汉字一并算进去）。换语言档复称之前先问"这一档里 CJK 本来该是几颗"，并且给那一档另找一颗<b>只属于该语言</b>的形状当正件（这里是片假名那颗），⛔ 拿中日同形那颗（[垂直]）充数。同一族还有一发：GetProcessPreferredUILanguages 按三参声明 ⇒ 读回 0 颗，据此几乎写下"表是空的"；那颗 API 的真签名是四参，0 是我的仪器坏。
- 一颗闸门形状：撤下的实验⛔ 只留判词——Assert.DoesNotContain("SetThreadPreferredUILanguages") 与 WI-34 对 ResourceContext 的处置同形（死路连"试一下"的死代码都不留）。
## 三十三、片153 ✅P-279＋P-280：待办那一行的两处屏幕读数（截止列冻在装载・行名念整份记录）＋坑表 #1108（UIA 根窗的孩子⛔ 是全集）

- 沙盒：同一副 p151 树，本轮三次整份新拷（修前／Fix A／Fix A＋B）。第 0 道＝他人实例 0 颗、IDLE_SEC=201.4；PID 27828→2600→21468→27736；收摊 REMAIN=0、EnumWindows ENUM_ROWS=0。
- 夹具：`D:\StarMarkTmp\p153\seed153.py` 往沙盒 widgets.json 加一颗 Kind 1 待办组件、库里插三条本地待办（截止 −2／0／+1 天，`extra_json` 的 `due` 走 `LocalItemState` 那颗键）；改前那份留 `widgets.json.pre-p153.bak`。⛔ 动他的真库与真设置；这颗夹具⛔ 清掉，留给下一把复称同一族（P-280 那四处的点验单直接用得上）。
- 就地切档的腿：`switch153.ps1`（UIA 打开设置页 → `LanguageSelect` 那颗 `ExpandCollapse` → 选中「日本語」）。⛔ 重启进程去读——重启会把"重算"与"重新装载"混成一件事，那一发证⛔ 了换语言那一刻的行为。
- **坑表 #1108**：可达性普查⛔ 拿 `AutomationElement.RootElement` 的孩子当全集。组件窗是 `WS_EX_TOOLWINDOW`（Win32 现读 `ex=128`），控制视图与<b>原始视图</b>两路都数不到它，主窗的 `Descendants` 里 `ControlType.Window` 也是 0 颗——而进程日志写着「桌面组件恢复（2 个实例）」并提交了首个组合窗。入口＝`EnumWindows` 按 pid 收 hwnd → `AutomationElement.FromHandle(hwnd)`。同一族第二次记（名册旧那条是 Computer Use SDK 的窗枚举），差别在这回连 UIA 自己的原始视图也聋。
- 一颗形状的账（仪器编码）：PowerShell 默认 OEM 码页重定向出来的读数档里，日文正文是坏字节——同一发读数用 `[Console]::OutputEncoding=UTF8` 再收一次才可逐字引用；报"改口了"之前先问"我这那份文件能不能解码"。
- 尺的口径：`bin/WI61b2/spoken_61b2.py` 两列从此共用 `code_lines()`（跳行注释），念键那一列从前不跳 ⇒ 四档各多算一颗；期望全部交命令行（`--expect=50/12 --policy=14`），⛔ 把跑次-specific 的数焊回档里。
- 两颗假修法各留一颗反件：容器 Style 里的 `AutomationProperties.Name` Setter（真机＝无效，`DoesNotContain` 钉住）、覆写 `TodoRow.ToString()`（会连带改掉日志里那份整行 dump）。正件住模板根 `{x:Bind Text}`。
## 三十五、片154 ✅P-281：设置页那一栏的布局行名（同一次访问里换语言停在上一门）＋一颗"没拷树就复称"的坑

- 沙盒：p151 树，本轮两次整份新拷；PID 28152；收摊 REMAIN=0。夹具 `D:\StarMarkTmp\p154\seed154.py`＝widgets.json 的 Layouts/Snapshots 各加一颗 <code>Name:""</code>（让派生名上场），改前留 `widgets.json.pre-p154.bak`。
- 腿（三条脚本各一件事）：`opentab154.ps1` 开设置页并选栏 → `tab154.ps1 -Aid TabGeneral/TabWidgets` <b>按 AutomationId</b> 选栏（名字是本地化的，切档后就⛔ 认得了）→ `switch154.ps1` 只动那颗下拉 → `dumpmain154.ps1` 只读⛔ 导航。⚠ 在已经开着设置页时再点 `SettingsButton`＝把页切回主窗，那一发表现为 `NO_LANGUAGE_COMBO`。
- 正件：修前栏目头 `レイアウト方案` 而那一行 `Unnamed layout`（同发其余全日文＝对照形状）；修后同一发行念 `無名のレイアウト`。⛔ 重启、⛔ 重新导航那一页，否则"重算"与"重装"混成一件事。
- **坑（⛔ 占号轴，与 #1108 同批不同事）**：修前那一发其实跑的是<b>上一份装配</b>——我只编译没 `cp` 到沙盒树就复称，差点把"改了⛔ 生效"记进正本。硬动作：**复称链固定成 build → cp → launch → 读**，缺一环就⛔ 报"修后照旧"。
- 第三格（快照页）⛔ 屏幕读数、由结构答：`SnapshotPage` 构造函数与 `OnNavigatedTo` 各 `BuildRows();`，而全仓⛔ 一处 `NavigationCacheMode` ⇒ 每次进入重建；这颗结构事实由证人里那条"全层⛔ 出现 NavigationCacheMode"钉住（开了缓存就红）。⚠ 沙盒里两次想从设置页直接跳「快照」那一格都没成（页面开着时 `NavSnapshotItem` ⛔ 在树里），所以这一格⛔ 写成读数。
## 三十六、片155 ✅P-282：条目格那几行的标题冻在装载那一刻（同进程就地切档・修后读到 画像）

- 沙盒：p151 树，整份新拷一次（复称链 build→cp→launch→读，⛔ 再断一环）；PID 5552；收摊 REMAIN=0。
- 夹具 <code>D:\StarMarkTmp\p155\seed155.py</code>：widgets.json 加一颗 Kind 6（剪贴板格）＋库里插一行 <code>type='clipboard'／source='clipboard'／title=''</code> 且 <code>extra_json</code> 标 <code>clipFormat=image, clipWidth=640, clipHeight=360, clipBytes=45000, clipApp=p155-fixture</code>。⛔ 动真剪贴板、⛔ 开采集器——念的活在这一格，库里那条只是记录。改前那份＝<code>widgets.json.pre-p155.bak</code>。
- 前后读数：修前 <code>WidgetTitle</code> 已念「クリップボード」而行念 <code>Image</code>（同拍待办窗三行已是 <code>明日／今日／2日超過</code>＝对照形状）；修后同一发行念 <code>画像</code>。⚠ 副标题三列同形 ⇒ 它不改口⛔ 是证据（同一条 MapRows 重念过两颗）。
- 一颗形状的账（这一族⛔ 进 LocText 名单时怎么写守卫）：条目行与活动行同为"行数不定、随数据重建"，但重念法不同——活动行模型自带 <code>Repaste()</code>，条目行要回源头（<code>MapRows</code> 一颗把两处映射并起来，装载与重念共用）。落点必走 <code>ApplyRows</code>（按 id 最小差异），⛔ Clear 重建：常驻桌面的一格换语言闪白是新造的一条坏。
- 活动流那一支早退时必须 <code>_lastItems = Array.Empty&lt;Item&gt;()</code>：两种格共用一套 <code>Items</code> 语义，留着上一模式那份会在换语言那一刻被插回屏上（这颗清场由证人计数钉住）。
## 三十七、片156 ✅P-283：快捷搜索那一格的结果行（三处映射并成一颗主人・按索引替换）＋坑表 #1109／#1110

- 形状与前三批同一族（行模型持成品字），只是宿主换成那颗<b>每格自建 VM</b> 的组件：三条搜索路径各写一份映射 ⇒ 本批并成一颗 <code>RowOf</code>＋<code>PushRow</code> 记账 <code>_rowsSource</code>，对外 <code>ReresolveDisplay()</code> 按索引替换（⛔ <code>Clear()</code>：吃掉选中位置；⛔ 再发起搜索：那一趟含 Everything 实时源）。视图一对挂摘手臂（挂＝构造／摘＝<code>Unloaded</code>）。
- 取证腿：沙盒数据根 <code>D:\StarMarkTmp\p151\data</code>（沿用片151 那棵树，复称链 build→cp→launch→读）；widgets.json 加 Kind 4 与 Kind 9 两颗组件，改前那份留 <code>widgets.json.pre-p156.bak</code>；库里 seeded 那颗空标题图片行沿用片155 那颗（id 10）。正本 <code>bin/WI99/p156_quicksearch_row_readings.txt</code>。
- 下一把（第五处＝速览「常看条目」那一列）的欠项写在 P-280：先造一条<b>能进那一列</b>的行——⚠ 先前那句「画板行把它挤出去」归因错了，现读＝那一列开头有道「uri 空即跳过」的筛子，而新写的图片行标题＝宽高串、uri＝那张 PNG 的地址（只有宽或高缺时标题才回空串）⇒ 夹具要照新形状造：<code>uri</code> 指一颗<b>真实存在</b>的 PNG（缺文件会被启动对账打上"图已不在"那一档，屏上念的就⛔ 是这一族要问的字）、标题留空、来源应用名也⛔ 带（那样副标题那一槽念的是会变口的键）⇒ 同一发同时问两颗。再问同一发就地切档改不改口；在那之前⛔ 写成缺陷、⛔ 顺手照前四处改。
- **坑表 #1109**：待裁清单里那颗宿主的<b>名字</b>也是抄写通路——上一批把①的后半写成「搜索那一页・VM 是单例」，本批逐调用点现读：两颗显示出口在那颗页的 VM 里 0 处命中，宿主其实是快捷搜索那一格（每格自建 VM）。**Why**：归属词是从上一批的叙述抄的，⛔ 从代码读；错一格会把下一批的取证腿领去开不该开的窗，本批就因为先数那颗页多花一趟。**How to apply**：清单里"哪一屏／哪一格／哪颗 VM"这类词，落笔与接手时各用一次调用点普查现读；发现上一批写错就地更正并同批记坑。
- **坑表 #1110**：搜索类取证的<b>两颗前置</b>——直接插库的行进不了全文索引（那颗搜索走 <code>items_fts</code> 的 <code>MATCH</code>），要 <code>VALUES('rebuild')</code>；写进输入框后那颗框的回读值报空，而结果真出来了。**Why**：两者都会把"夹具／注入没生效"读成"应用坏了"。**How to apply**：插库后先 rebuild 再取证；判断"有没有输进去"⛔ 问那颗框自己的 Value，要问被问对象（结果列表有没有行数）。

## 三十八、片157 ✅P-284＋P-280 摘牌：速览「常看条目」那一列（一颗画法・重贴补第四拍）＋坑表 #1113／#1114／#1115／#1116

- 形状与前四格同一族，但宿主是<b>code-behind 自己折行</b>的那一族（⛔ 是 VM 里的行模型）：装载那一拍把两颗显示出口当场折进 <code>Button</code> 里的一块面板，而重贴那一拍只做日期／月历／热榜三件事 ⇒ 栏头改口、行照旧。修法＝抽一颗 <code>PaintItems()</code> 给装载与重贴共用，并把上一轮取回的那份持在 <code>_lastRecent</code>（换语言那一刻按那一份重铺，⛔ 再打库）。
- ⚠ 这一批⛔ 动订阅：那颗档早就是 <code>Locale.Changed</code> 的主人，只往现成 handler 里加一拍 ⇒ 那颗分母尺与两族名单颗数⛔ 变（55|24|31|17）。以后判断"这批要不要补分母"先问<b>有没有新挂一颗手臂</b>，⛔ 问"这批是不是同一族"。
- 夹具形状这一格是现读换来的（⛔ 沿用上一批那颗行）：那一列有道「uri 空即跳过」的筛子＋新行标题＝宽高串 ⇒ 旧形状进不来、新形状问不到。下一把若要量<b>别的</b>宿主上同一族，照这条问三件事：那道筛子拦不拦、那颗槽在真实形状里到底会不会变口、以及那一行会不会被别的规则改写成第三种话（"图已不在"那一档）。
- **坑表 #1113**：沙盒启动⛔ 手写。那颗改道信号是<b>两颗环境变量</b>（settings・db），仓边／仪器目录早有现成的启动档带着它们；我这次自己写了一发 <code>Start-Process</code>，漏了 ⇒ 进程读他真目录并把 <code>settings.json</code> 写回真档（语言被改成 en・已按原值改回 zh・原值证据＝那进程自己在真日志写的 zh-CN）。**Why**：⛔ 带改道信号＝⛔ 是"这次运气好没写坏"，而是<b>整个会话都在他的真数据上跑</b>。**How to apply**：任何真机取证一律走那颗 fail-closed 的启动档（目标⛔ 在仪器根下→拒・两颗档不在场→拒・启动后在沙盒日志里读不到"自己那一跑 pid 的会话行"→杀掉再拒）；正件＝沙盒日志里那一行，⛔ 是"窗口起来了"。
- **坑表 #1114**：计数证人⛔ 让"声明那一颗"混进数。<code>Assert.Equal(2, Count(view, "_lastRecent = new List&lt;Item&gt;();"))</code> 现读 3＝字段声明也写了同一种字面。**Why**：那颗计数问的是"两支清场都在"，被声明顶高一位就永远看不出少写一支。**How to apply**：给那颗数的字面留一种<b>只有那两支用得上的形状</b>（这里把声明改成 <code>Array.Empty&lt;Item&gt;()</code>），并<b>把多的那一颗单独钉住</b>（同批加一颗断言钉声明那一句）。
- **坑表 #1115**：注释里⛔ 重打被尺扫描的字面量。为解释"类型名要写全"，我把 <code>new SearchResultItem(</code> 原样写进 XML 注释，而那颗按字面找构造点的尺读<b>整档原文、⛔ 抹注释</b> ⇒ 当场多出一颗"实参只有 1 段"的假构造点，把尺染红。**Why**：注释对读者是解释，对那把尺是正文。**How to apply**：要指那串字面就换成"类型名＋左括号"这种⛔ 构成同一形状的写法；新加解释后先跑<b>那把被解释的尺</b>，别等全量。
- **坑表 #1116**：⛔ 拿代码注释当盘上的事实。读 <code>WidgetStorage.Load</code> 看到那颗 Save 只写在"档不在场"那一支，就该推"普通启动⛔ 写 widgets.json"——而他真目录那颗档的 mtime 明明动了。两发对照实验（沙盒连起两次、⛔ 点任何东西）量到 <b>mtime 动、size 与 sha256 不动</b> ⇒ 结论＝应用自己会按原字节重写，"重写"与"改内容"是两件事。**How to apply**：推论与盘不符时，以盘为准并<b>造一发不需要精密控制的对照</b>；报"我没改他东西"要分两层：内容层（sha）与在场层（mtime）。

## 三十九、片158 ✅P-196 裁・结案（⛔ 动代码的裁决腿）＋坑表 #1117

- 这一批的产出＝<b>一句话裁决</b>：胶水键族"机制一份"已由片116 落地并钉住（`LocMessage.Fold`＋`LocFoldGateTests`），剩下的"表里三行要不要并成一行"是<b>产品语气</b>——按"⛔ 扰正常使用"那条界裁成⛔ 并，本条摘牌。并键的代价现读给死：英文界面 `settings.ai.usage.featureJoin` 用分号、`trending.notice.join` 用逗号，日文用顿号 vs 分号 ⇒ 并了就会改屏幕上看得见的标点。
- ⚠ 裁决⛔ 等于把话写死：那颗 `TheGlueKeysThemselvesAreNotMergedByThisBatch`（住在 `LocFoldGateTests`）今天仍钉着"这三行没并"＝哪天有人顺手并了，它会红并逼他回来改这条裁决（⛔ 靠正文里那句话守）。
- 下一把的入口⛔ 在本条：普查那条线（P-194）今天三层现读 <b>12 档／50 处</b>，逐颗定性里剩下的"上屏而⛔ 重贴"那一族才是活口子（RSS 页那行 tooltip 的<b>实参</b>是当场念成的成品字＝外层会重贴、内层⛔ 会＝半句旧话）。跑法照 P-280 那五格的样板：先一发真机读数定裁，再决定改哪一层。
- **坑表 #1117**：数"同形有几颗"⛔ 按名字 grep，要先问<b>正文那颗数的是哪一维</b>。表里名字带 `join` 的 6 行 vs 本条登记的 3 颗——差的 3 行交的是裸标点（⛔ 带槽），还有 `ocr.body.join` 是 `{0}{1}`（无标点）。**Why**：按名字数会把"形状⛔ 同"的行算进来，再拿那个数去说"登记时数错了"＝改动一条本来正确的正文。**How to apply**：给那一族写死形状判据（两颗槽・三列只换那颗标点），并在正文里把形状与颗数一同留下；颗数差只是线索，结论要读产那句话的那把尺。

## 四十、片159 P-194 定裁腿：RSS 行 tooltip 实参真机三拍（B/C 皆整句新语＝「该路径无法复现」）＋坑表 #1118／#1119／#1120

- 定裁结论：P-194「上屏而⛔ 重贴」的活口子里，RSS 页那行 tooltip 的实参是当场念成的成品字（外层会重贴・内层不会＝「半句旧话」候选）。真机三拍（沙盒 bin159・正本 <code>D:\StarMarkTmp\p159\reading159.txt</code>）：A（zh 基线）第3读＝『https://example.invalid/probe-a.xml　收藏会放进「RSS订阅 / ProbeFeedA」』；B（切 English 回 RSS）第1读＝『Bookmarks that go into “RSS subscriptions / ProbeFeedA”: https://example.invalid/probe-a.xml』；C（切 日本語 回 RSS）第1读＝『https://example.invalid/probe-a.xml　保存先「RSS購読 / ProbeFeedA」』——B/C 皆整句新语 ⇒ 真实用户路径（设置页切换→返回→回 RSS）上「半句旧话」走不出页重建关，登记「该路径无法复现」。
- 修码批入口（不在本条）：结论句「修码批改按其余 20 档调用点逐颗读数再定」——P-194 落地口径五条别绕（改回型＝全仓调用点同批改・机器事实走实参⛔ 抄进句子・一把尺⛔ 够），跑法照 P-280 那五格的样板：先一发真机读数定裁，再决定改哪一层。
- ⚠ 一起笔事故与找回：变异台架还原步求值序截断清零 UiStrings.cs ⇒ git checkout 回收 ⇒ qoder 会话未提交 4 颗键失踪 ⇒ 码腿 125fb17 后 HEAD 断裂；fixup 44246f2 从 qoder 转录按时间戳取 Edit 原文放回（插入行一律取转录原文⛔ 手抄・top.themeTip ⛔ 复活），复称 wiring 93/93・五尺 26/26・全量 6707/6707（x64）。
- **坑表 #1118**：还原步「开 wb 在前・读在后」的求值序＝当场清零源档——<code>io.open(path, 'wb').write(io.open(path, 'rb').read()...)</code> 先截断再读 ⇒ 读回空・写回空。**Why**：同一表达式里两个打开步，求值序与书写直觉相反；「sha 还原一致=False」那行要当事故读；回收＝销毁工作树未提交态（git checkout 让未提交的 4 颗键一并失踪）。**How to apply**：写回一律「先整读进变量・后开 wb 落盘」；事故后先拿转录／残留盘点差集再回收；找回从转录按时间戳取 Edit 原文、锚行在场恰一次后插回。
- **坑表 #1119**：write 工具落盘的 .ps1 无 BOM＝PS 5.1 按系统 ANSI 码页解（老账 253 ③「401」的同款真因）。**Why**：write 工具落盘不带 BOM；「脚本能起」与「CJK 串解对」之间没有报错。**How to apply**：新写／改写每颗 .ps1 落盘后读头三字节验 EF BB BF，不在场就字节保真补上再跑。
- **坑表 #1120**：tooltip 弹层认属⛔ 按窗类或进程名猜——按 pid 收窄（GetWindowThreadProcessId）再数；开启前置＝真前台＋先真实一步挪出主窗＋真实一步落点。**Why**：PointerEntered 是边界事件，「光标在矩形内」≠「已触发」；弹层窗类 PopupWindowSiteBridge 与宿主同 pid 异 hwnd。**How to apply**：hovername 先真实一步挪出主窗再传送近元素、落点也走真实一步；收窗枚举按 pid==目标收窄＋IsWindowVisible 过滤。

## 四十一、片160 P-194 修码批：三层念键残余 12 档／50 处逐颗定性＝零必修・P-194 翻牌就地结案（零源码零测试）

- 修码批结论：两把普查尺同跑同得三层 50 处／12 档（正本 bin/WI61b2/spoken_160.txt 与 bin/WI65/spoken_160.txt）；12 档逐颗读到调用点与消费者，零必修，全部有归属（账本 P-194 节片160 那一块逐条给依据）——只进日志 14／重建路 12／宿主已接线 16／每实例自愈 5／计数键 1／现念表＋证人 1／他条账上（P-172）1。
- 判据模式＝WI-65a 读码批口径：结构判据（挂/摘成对、每次打开重建、每秒重算）＋既有证人（订阅配对族、重建计数、签名边界）＋片153-159 已落盘真机读数（✅P-279／✅P-282／✅P-283／✅P-284／片159 三拍）；零新探针。
- 仪器一笔：code 那把尺（bin/WI65/spoken_65.py）那句 assert 把登记年基线焊在表达式里——命令行给对期望也红，与自身「期望由命令行给」契约相冲（#438 同族）；就地修成「无期望才要求基线」，复跑全绿；raw 尺原件⛔ 动。
- 收口读数：账本计数器 285／162／123 VERDICT OK（✅ +1＝P-194 翻牌）・五道文档闸门 26/26（x64）。
- 下一刀：本链余开放 P-189（剩 節分——立春要第二处读数・码腿无需真机；春分／秋分已落＝片115）与 P-192（结案入口卡环境读数＝Ditto 副标题三语／无 OCR 包机器／造不出的防御臂＝非本机可动）；#575 三语覆盘需真机。⛔ commit（片160 文档腿未领）、push（未推 313）。

## 四十二、片161 ✅P-189 節分落齐：三颗全落就地结案（官制年表 2020–2027・立春行互证 8/8・表外宁缺）

- 取数：官方暦要項八个年页（2020–2027）直读＋单元格级验证；正本 D:\StarMarkTmp\p161\fetch161.txt；两处读数＝節分行正本・立春行经官方规则「立春が定まれば節分もその前日として定まる」互证 8/8；官方只到 2027 ⇒ 表外一律不出（⛔ 寿星公式外推）。
- 落点：RuleKind.JapanSetsubun＋八行年表＋TryJapanSetsubunDay（表外 false）＋日文清单一行＋键 glance.festival.setsubun＋可达性棘轮 51→52；证人三颗（年表逐行／表外不出／立春行互证）。
- 读数：定向 74／74・docgates 26/26（x64）・全量 6716／6716（x64・19 m 7 s）・计数器 285／163／122 VERDICT OK。
- 下一刀：本链后台可动项清零——余开放 P-192（非本机可动＝Ditto 副标题、无 OCR 包机器、造不出的防御臂）与 #575（三语覆盘需真机＝用户占机）；待用户裁决或释放机器。⛔ commit（片161 文档腿未领）、push（未推 314）。

## 四十三、片162 P-192 结案入口①量到：授权装 Ditto・装机路径 bug 修掉・三语真机读数（未结案）

- 装机：winget 3.25.113.0；库 %APPDATA%\Ditto\Ditto.db（一次现值原样重设零损失触发落行）。
- 修复：候选表领头改装机现形（Ditto 目录下 Ditto.db・两根目录）＋旧形照留；FindDbPath 取第一存在；三档注释改口；证人 Candidates_LeadWithTheShapeTodaysInstallerActuallyWrites；定向九族 68／68（x64）。
- 三语读数：zh『Ditto 剪贴板』／en『Ditto clipboard』／ja『Ditto クリップボード』・每跑同步 1 秒・剪贴板伪根计数 (1)・副标题命中恰 1；正本 D:\StarMarkTmp\p162\measure_zh.txt／measure_en.txt／measure_ja.txt。
- 剪贴板纪律：真剪贴板内容一字未进证据档（长度＋sha256 前缀 49・76f51a2ea66c・三跑同值）。
- 读数：docgates 26/26（x64・正本 D:\StarMarkTmp\p162\docgates162.txt）・计数器 285／163／122 VERDICT OK（0 立 0 结）；P-192 ⛔ 翻牌（剩 ⑴⑵⑶ 三臂皆非本机可动）。
- 下一刀：余开放 P-192 三防御臂与 #575（三语覆盘需真机＝用户占机）。⛔ commit（片162 未领）、push（未推 316）。

## 四十四、片163 #575 三语覆盘收官：速览那一屏三语真机逐字全绿・透窗污染堵死・热榜双原因格挂账

- 三跑：zh 29 步／28.8 s・en 28 步／28.1 s・ja 42 步／52.6 s（freshStart 各起・Profile-{zh,en,ja}・库零行・Kind=9 @60,60 320x400）；标题／节令行／观测集／导航／CAL 行廓三语各正；正本 D:\StarMarkTmp\p163\ev\readout163_zh.txt／readout163_en.txt／readout163_ja.txt。
- 透窗污染堵死：初跑 8 格假阳性（6/7/12/13/19/20/27/28）⇒ parkmain 挪主窗 (-2400,-2400)（rect≠组件矩形过滤）＋backing163 垫 RGB(38,38,39) 实底窗（z 序紧贴・⛔ 抢前台）⇒ 假阳性归零・三语全绿；仪器顺修两 bug（补 BOM＝#1119 同款再犯・$h/$H 改 $bh）・烟测 v2 全绿（EnumWindows 按 pid 验 rect・杀后 IsWindow=False）；坑表 #1121／#1122。
- 热榜双原因格挂账（报告 §四百九十九 ①②③ 同例・归他・真机才判）：两腿各 200（github_trending 1.99 s・api_search 0.87 s）＋库可写＝现场造不出；P-189／P-196 账本补记已落；报告 §四百九十七 ① 20 号就地更正 21 号（官制年表 2027 春分＝3 月 21 日・实测 d21 亮 d20 ⛔）。
- 读数：docgates 26/26（x64・正本 D:\StarMarkTmp\p163\docgates163.txt）・计数器 285／163／122 VERDICT OK（0 立 0 结）・本批零码腿。
- 下一刀：余开放 P-192（⑴⑵⑶ 皆非本机可动）。⛔ commit（片163 未领）、push（未推 318）・HEAD 0d44830。

## 四十五、片164 浪一结案腿①：产品取舍七颗「批次内已决」欠账翻牌（P-74/75/76/77/78/79/81・连落 20 刀第 1 刀）

- 三浪排已授权（commit 逐刀自动・⛔ push 照旧）：本刀把正文已载终断而从未 ✅ 的七颗产品取舍翻牌——P-74/75/76/77/78/79/81；各节重开条件留册・正文一字未动；点验四证人全过（DittoDbLocationTests・零 TimestampConverter 残留・TryCopyText・MyPictures 两处）。
- 读数：docgates 26/26（x64・正本 D:\StarMarkTmp\p164\docgates164.txt）・计数器 285／170／115 VERDICT OK（✅+7）・零码腿。
- 下一刀：片165 浪一结案腿②（P-87/88/91/92/94/96/100）。⛔ push（未推 319）・HEAD b549b6f。

## 四十六、片165 浪一结案腿②：AI/RSS/性能取舍七颗「已决」欠账翻牌（P-87/88/91/92/94/96/100・连落 20 刀第 2 刀）

- 七颗「已决」从未 ✅ 的欠账翻牌——P-87/88/91/92/94/96/100；各节重开条件留册・正文一字未动；点验三证人全过（SourcesAreFetchedConcurrentlyButBounded・GetCollectedRssLinksAsync 三处贯通・MaxParallel=4）。
- 读数：docgates 26/26（x64・正本 D:\StarMarkTmp\p164\docgates165.txt）・计数器 285／177／108 VERDICT OK（✅+7）・零码腿。
- 下一刀：片166 浪一结案腿③（P-83/84/97/106/107/110/111）。⛔ push（未推 320）・HEAD 7f2d313。

## 四十七、片166 浪一结案腿③：标注/屏幕取舍七颗「已决」欠账翻牌（P-83/84/97/106/107/110/111・连落 20 刀第 3 刀）

- 七颗「已决」从未 ✅ 的欠账翻牌——P-83/84/97/106/107/110/111；各节重开条件留册・正文一字未动；点验三证人全过（RotatedTextBoundsCoverEveryPixelItPaints 五角度・GdiTextDrawer.DrawRotated・CursorCircleRadiusDip 读写链贯通）。
- 读数：docgates 26/26（x64・正本 D:\StarMarkTmp\p164\docgates166.txt）・计数器 285／184／101 VERDICT OK（✅+7）・零码腿。
- 下一刀：片167 浪一结案腿④（P-104/109/113/114/115/124/156）。⛔ push（未推 321）・HEAD 540ee56。

## 四十八、片167 浪一结案腿④：作废/改判/说明书七颗欠账翻牌（P-104/109/113/114/115/124/156・连落 20 刀第 4 刀）

- 七颗作废/改判/已落地而从未 ✅ 的欠账翻牌——P-104/109/113/114/115/124/156；各节重开/翻案条件留册・正文一字未动；点验四证人全过（PinWindow 机制零残留・ClampNotice・NormalizeTimeoutSeconds・丢弃回报重载守门）。
- 读数：docgates 26/26（x64・正本 D:\StarMarkTmp\p164\docgates167.txt）・计数器 285／191／94 VERDICT OK（✅+7）・零码腿。
- 下一刀：片168 浪一结案腿⑤（P-62b/66/101/108/61）。⛔ push（未推 322）・HEAD a236929。

## 四十九、片168 浪一结案腿⑤收官：已闭环/定案/改判残尾五颗翻牌（P-61/62b/66/101/108・连落 20 刀第 5 刀）

- 五颗已闭环/已定案/改判结案欠账翻牌——P-61/62b/66/101/108；各节重开/验收条件留册・正文一字未动；点验五证人全过（三笔历史 commit 85a0f9a・27bbc8a・8ae41f0＋TrayWidgetMenuTests／首启种子两测试／targets 覆盖／提权守门测试・src 零 DPAPI）。
- 浪一（结案腿）至此收官：33 颗已决欠账经片164–168 五刀全数翻牌・计数器 285／196／89 VERDICT OK（✅+5 本刀）。读数：docgates 26/26（x64・正本 D:\StarMarkTmp\p164\docgates168.txt）。
- 下一刀：片169 转浪二码修腿第 1 刀（P-3 ActivityKind 兜底色）。⛔ push（未推 323）・HEAD b8d250e。

## 五十、片169 浪二码修腿①：P-3 ActivityKind 未映射缺中性兜底色——按 (B) 口径落地（连落 20 刀第 6 刀）

- 活动归类 switch 兜底臂「其余全算增」改归中性 Neutral（灰）——11 档全显枚举、不新增 ActivityKind 成员、三色一分未动；转换器中性档解析 SystemFillColorNeutralBrush、外层回落同改中性。
- 守门：ActivityNeutralFallbackGateTests 三例（全显枚举／兜底不冒充增／三色不动）3/3；UI 编译 0 警 0 错（x64）；docgates 26/26（正本 D:\StarMarkTmp\p164\docgates169.txt）；计数器 285／197／88 VERDICT OK（✅+1）。
- 下一刀：片170 浪二码修腿②（P-13 StarLog CR/LF 剥离）。⛔ push（未推 324）・HEAD 2616c7f。

## 五十一、片170 浪二腿②：P-13 StarLog CR/LF 日志伪造翻牌（连落 20 刀第 7 刀）

- P-13 读节发现已是 CD 批（2026-09-21・02a8edd）按 (A1) 落地的存量——标题没翻＝「改掉了没回写」欠账；点验四证人（commit 在链・SanitizeMessage／StackTail 在仓・InternalsVisibleTo 在册・StarLogTests 13 例）全过后翻牌结案，正文一字未动。
- 读数：docgates 26/26（x64・正本 D:\StarMarkTmp\p164\docgates170.txt）・计数器 285／198／87 VERDICT OK（✅+1）・零码腿。
- 下一刀：片171 浪二码修腿（P-15 AddTagAsync 标签首尾空白）。⛔ push（未推 325）・HEAD 414dc76。

## 五十二、片171 浪二码修腿③：P-15 AddTagAsync 归一守卫落地（连落 20 刀第 8 刀）

- AddTagAsync 顶部加兄弟路径同口径守卫：全空白＝拒（false＝没变化・不广播）、带首尾空白折净名再落库；签名不动・UI 现路径零行为变化（TagEditor 本就 trim 拒空）・守的是未来非 UI 直调者。
- 守门：StateWriteRowTests 补两例（空白名折净名＋同挂报没变化／全空白 no-op 且 tags 表零行）；标签族定向 46/46（x64）；docgates 26/26（正本 D:\StarMarkTmp\p164\docgates171.txt）；计数器 285／199／86 VERDICT OK（✅+1）。
- 挤落修复：R7 快照表两颗行号指针被本刀插行挤落（落空棘轮 30→32 红）→ 删号留锚（P-15/P-40 两行）回 30。
- 下一刀：片172 浪二码修腿④（P-20 ParseDropFiles 符号翻转）。⛔ push（未推 326）・HEAD bacd5cd。

## 五十三、片172 浪二码修腿④：P-20 符号翻转翻牌（连落 20 刀第 9 刀）

- P-13 先例再现：R7 盘点已判前提消失——解析体随批次 IC（`b1e486c`）搬家＋pFiles 先在 uint 域夹住再强转 int，符号翻转与上界两路皆死；守门测（uint.MaxValue→空表不抛等三例）同册；解析族定向 31/31（x64）。
- 残渣（pFiles<20 读头部垃圾串）有界不抛・维持 CS 判定不动；本刀零码改零测改・正文一字未动；docgates 26/26（正本 D:\StarMarkTmp\p164\docgates172.txt）；计数器 285／200／85 VERDICT OK（✅+1）。
- 下一刀：片173 浪二码修腿⑤（P-27 代理对护栏）。⛔ push（未推 327）・HEAD e598dd3。

## 五十四、片173 浪二码修腿⑤：P-27 代理对截断护栏收口（连落 20 刀第 10 刀）

- 旧账五处全闭环（SB/SC 合流 TextTrim・RV 删托盘通知出口・ETag 明示绕行）；本刀补收横向复现三颗漏网：标签收口・整理行体・超长正文截断改道 TextTrim.Cut/Ellipsize（三处单行替换・零行移）。
- 守门：TextTrimTests 补改道宿主行为测两例（行体/标签/正文截断退一格・截后必无未配对代理）；定向五类 162/162（x64）；docgates 26/26（正本 D:\StarMarkTmp\p164\docgates173.txt）；计数器 285／201／84 VERDICT OK（✅+1）。
- 下一刀：片174 浪二码修腿⑥（P-39/P-55 HttpClient 超时同根）。⛔ push（未推 328）・HEAD b03a7dc。

## 五十五、片174 浪二码修腿⑥：P-39/P-55 超时与可取消双翻牌（连落 20 刀第 11 刀）

- 同根两颗（Timeout 不设＋同步不可取消）两半皆已落地：GZ `fced27c`（自建分支 20 秒）＋HF `839bbcf`（顶栏取消入口・取消与失败分开报・取消轮回滚 ETag 检查点・删第二条不可取消同步路径）；守门定向 11/11（x64）；本刀零码改零测改・只补标题记账滞后。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates174.txt）；计数器 285／203／82 VERDICT OK（✅+2）。
- 下一刀：片175 浪二码修腿⑦（P-50 Downloads 目录）。⛔ push（未推 329）・HEAD e5cb9a8。

## 五十六、片175 浪二码修腿⑦：P-50 下载目录两段归宿翻牌（连落 20 刀第 12 刀）

- 两段归宿：HA `c35ac63` 按 (B) 落地（OneDrive 三形态＋AddIfUsable）→ VQ `4038e15` 按用户裁决把「索引进库」整条链拆掉（Everything 只留实时搜索・三件套卡片移除）；守门 StaleFileRowGateTests 钉拆后形状・定向 24/24（x64）；本刀零码改零测改。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates175.txt）；计数器 285／204／81 VERDICT OK（✅+1）。
- 下一刀：片176 浪二码修腿⑧（P-53 设置写盘失败隐身）。⛔ push（未推 330）・HEAD 73bb1df。

## 五十七、片176 浪二码修腿⑧：P-53 设置写盘失败隐身翻牌（连落 20 刀第 13 刀）

- 随 HC `d04fcdf` 落地：LastWriteError（成功复位・同锁）→ 保存二选一（Info 复位／HasSaveError＋语言键红字常驻标题下方）；死代码绑活；不阻止关页不弹模态；日志不再谎报。词闸门定向 9/9（x64）；本刀零码改零测改。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates176.txt）；计数器 285／205／80 VERDICT OK（✅+1）。
- 下一刀：片177 浪二码修腿⑨（P-103 单实例残留）。⛔ push（未推 331）・HEAD 073ad53。

## 五十八、片177 浪二码修腿⑨：P-103 单实例残留窄缝翻牌（连落 20 刀第 14 刀）

- 取向 (C) 已定在册且由现役代码实现：窄缝分支带原因退出并落日志（宁可一次「双击没反应」也不结束还在显示东西的进程）；绊线行「但按标题没能唤起它的窗口」在役＝重开条件第一现场已在岗；判定逻辑 InstanceHandoffTests 定向 14/14（x64）；单实例整体＝互斥体（b010498）＋属主校验＋残留回收三臂。重开条件不撤：日志出现那一行 ⇒ 落 (A)；伴随「组件在但点不到」⇒ 升级 (B)+(A)。本刀零码改零测改。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates177.txt）；计数器 285／206／79 VERDICT OK（✅+1）。
- 下一刀：片178 浪二码修腿⑩（P-121）。⛔ push（未推 332）・HEAD 7fc010b。

## 五十九、片178 浪二码修腿⑩：P-121 剩余半边 AI 请求超时翻牌（连落 20 刀第 15 刀）

- 扩展仓 `e1015f9`：三 chat 路径挂 180 s 请求超时（与桌面 180 s/批 同数对齐）；超时 ⇒ AiTimeoutError（ai.timeout・按失败处理）；暂停 ⇒ AbortError 透传照旧优雅暂停；Ollama 超时不再误报「无法连接」。扩展侧五格新测・npm test 150/150（+5）・tsc 通过。reconcileStars 半已闭（UN・`28c9e31`）⇒ 两半皆闭整体结案。
- 坑与修：扩展仓用户预暂存的 README/package.json 首提被裹挟（6 档）——软重置拆开重提为恰 4 档・用户两档回暂存区原状；该仓 git status 首列 M＝已暂存，add 前必须先看有没有别人的暂存。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates178.txt）；计数器 285／207／78 VERDICT OK（✅+1）。
- 下一刀：片179 浪二码修腿⑪（P-125）。⛔ push（未推 333）・HEAD f8e5f99。

## 六十、片179 浪二码修腿⑪：P-125 第四条阈值无数据出口翻牌（连落 20 刀第 16 刀）

- 按默认处置 A（先不记）结案：schema 零 new_tags・ClassifyRunner newTags 内存即弃・冒烟工具第四条如实「本档测不到」——三证在册；账本定向 16/16（x64）。重开条件不撤：aiusage 报出前三条任何越线（尤其 estimated 占比 > 50%）⇒ 按 C 落一列，不用 B。本刀零码改零测改。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates179.txt）；计数器 285／208／77 VERDICT OK（✅+1）。
- 下一刀：片180 浪二码修腿⑫（P-129）。⛔ push（未推 334）・HEAD 73bfe8f。

## 六十一、片180 浪二码修腿⑫：P-129 判据能力边界翻牌（连落 20 刀第 17 刀）

- 按默认处置结案（不付编译器级成本）：① NumberText 类注释互指在册；② FloatHoleGateTests 钉可判定半边（地板断言 ≥800・实测 1287 孔 0 违规）；③ 升级条件不撤：真查出分岔 ⇒ 升级活缺陷再谈类型级检查。「变量名那一格」仍是边界（SG2 台架量过全绿），默认不为其付 Roslyn 成本。数字闸门三套定向 56/56（x64）；本刀零码改零测改。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates180.txt）；计数器 285／209／76 VERDICT OK（✅+1）。
- 下一刀：片181 浪二码修腿⑬（P-171・浪二最后一颗）。⛔ push（未推 335）・HEAD 773d134。

## 六十二、片181 浪二码修腿⑬（浪二收官）：P-171 文案写死条数翻牌（连落 20 刀第 18 刀）

- P-171 选项 C 兑现：证人 TheCaptureToggleSentencesNameTheSameCountTheGateLists——从发起框选判据现场数名单，托盘配对表与状态行实参同一份；短句与开关两句数词逐语相符、占位符逐语零起连续；⛔ 不把 3 抄进判据（#289 口径）。反向台架两格各红一次。canvasInShotNote 半边已删数（表里无数字）无需证人。重开条件在册：入口增删 ⇒ 证人红 ⇒ 句子与数词表人手同步改；B 路线落地 ⇒ 证人按新机制改写。
- I18nWiringGateTests 定向 94／94（x64・证人前 93）；本刀只加一颗测试・零产品码改。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates181.txt）；计数器 285／210／75 VERDICT OK（✅+1）。
- 下一刀：片182 浪三①（C/D 类挂账注＋20 刀对账收尾）。⛔ push（未推 336）・HEAD 28c834c。

## 六十三、片182 浪三①：C 类挂账注 11 颗（连落 20 刀第 19 刀）

- C 类 11 颗逐颗挂账注已插各节标题行后（P-24/46/85/90/95/98/99/102/112/145/153）——等待条件逐颗转述、重开/翻案条件原样在册、题行不翻牌；引擎新加 NOTES 腿（不翻牌插注・同一套五词形/第N行/占位符/仓档名自检）。
- D 类批量盘点：余开放 75＝C 类 11＋⛔挡 4（P-56/170/172/192）＋既有积压 60（长期/架构/性能/产品/热榜/i18n 铺开）——普查正本 D:\StarMarkTmp\p164\open_census.txt；P-51/P-52 维持原状待拍板。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates182.txt）；计数器 285／210／75 VERDICT OK（0 立 0 结）；census 30。
- 下一刀：片183 浪三②（20 刀对账收官）。⛔ push（未推 337）・HEAD 18318e1。

## 六十四、片183 浪三②收官：20 刀对账全绿（连落 20 刀第 20 刀・收官）

- 对账器 recon183 十四验项全 PASS（正本 D:\StarMarkTmp\p164\recon183.txt）：20 笔 commit（164–182 十九笔在链按序＋本刀一笔）／报告节 §550–§五六九恰 20／交接节 §四十五–§六十四恰 20／翻牌 47/47 逐锚 ✅ 在位（浪一 33＋浪二 14・原锚归零）／挂账注 11/11 未翻／算术 163+47=210・122−47=75（起点 §549 在册・标题恒 285）／docgates 逐刀档在盘／census 30 恒定。
- 三浪合账：浪一 33 翻牌（5 刀）／浪二 14 颗码修或翻牌（13 刀・含扩展仓 e1015f9）／浪三 11 注＋对账（2 刀）；余开放 75 三层账在册（C 11 注／⛔挡 4／积压 60）・P-51/P-52 待拍板・C 类逐颗等用户/真机。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates183.txt）；计数器 285／210／75 VERDICT OK（0 立 0 结）。
- 连落 20 刀收官（下一批另立）。⛔ push（未推 338）・HEAD b16ab58。

## 六十五、片184 P-51/P-52 记账滞后翻牌（用户拍板收两颗）

- P-51 ✅：body 随 HB（`5b33cfd`）＋BK 改判（`ee8a589`：总开关＋间隔 6／24／72／168 小时）早已落地・标题补 ✅；P-52 ✅：body 随 GY（`38c5304`：`preemptPrevious`・后台索引不被前台键入取消）落地・标题补 ✅——两颗正文一字未动・配套测试在册。
- P-52 真机验收项留正文：开启本地磁盘搜索后看「索引进库」条目数与目录内文件数量级相符。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates184.txt）；计数器 285／212／73 VERDICT OK（✅+2）；census 30。
- 下一刀：B 类码修批开批分诊（tags= 无真机标记的候选约 13 颗逐颗读体定批）。⛔ push（未推 339）・HEAD 22b211e。

## 六十六、片185 B 类码修批分诊腿：四颗记账滞后翻牌（P-12/P-49/P-54/P-56）

- 分诊结论（13 颗候选逐颗读体）：记账滞后翻牌 4 颗（本刀：P-12/P-49 前提消失・P-54/P-56 落地未翻）＋真码修待拍板 6 颗（P-8 (A)／P-9 (A)／P-16 (A)／P-22 (B)／P-31 (A)+(C)／P-38 (A)——正文各带建议）＋产品拍板 2 颗（P-4／P-58）＋不动留痕 4 颗（P-23/P-120/P-126/P-139）＋挂账维持 1 颗（P-112 片182 注准确）。
- 翻牌 4 颗：P-12/P-49 前提消失（GQ `03e78d6`・R7 盘点在册）・P-54（HD `bc1e847`）・P-56（HG `b17e242`）——正文一字未动。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates185.txt）；计数器 285／216／69 VERDICT OK（✅+4）；census 30。
- 下一刀：等用户对 6 颗真码修逐颗拍板（P-8/P-9/P-16/P-22/P-31/P-38）后分刀落。⛔ push（未推 340）・HEAD 6798e5b。

## 六十七、片186 真码修批第 1/6 刀：P-22 (B) 搜索去重键对齐存储层 BINARY 不变式

- 修法：统一搜索合并去重单比较器拆双集合——file 键（file:// 归一本地路径）忽略大小写不变（Windows 路径口径）；其余 URI 与 source|source_id 回退键改 Ordinal 对齐存储层 BINARY；库里仅路径大小写不同的两条 http 行不再被搜索吞一条。
- 证人测 3 条：http 大小写兄弟都活・file 大小写塌一・真同条 http 仍去重；SearchServiceTests 定向 16/16（x64）。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates186.txt）；计数器 285／217／68 VERDICT OK（✅+1）；census 30。
- 下一刀：片187＝P-8 (A) 语言筛选 SQL 语言比较归一。⛔ push（未推 341）・HEAD 9a5ca05。

## 六十八、片187 真码修批第 2/6 刀：P-8 (A) 语言筛选 SQL 比较与下拉归一值三处口径对齐

- 修法：关键词搜索腿＋浏览腿两处 SQL 语言比较由 BINARY 原样相等改 trim + NOCASE——库里原样存的非规范大小写语言串不再被下拉归一值筛成 0 结果；与内存兜底三处口径一致；type 闸门与本地文件豁免原样保留。
- 证人测 2 条：搜索腿归一值命中非规范大小写存量（护栏：别的语言照旧筛掉）・浏览腿大小写＋空白变体命中；相关三套件定向 64/64（x64）。
- LivingSourcePointer 落空棘轮 30→28（#1020 第五次）：两串 P-33 旧号（ItemRepository.Search.cs:66-67）反撞非空＝本就没对着所句处，删号留锚（锚念 var innerTail = relevanceFirst）；正本 D:\StarMarkTmp\p164\diffcensus187.txt。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates187.txt）；计数器 285／218／67 VERDICT OK（✅+1）；census 28。
- 下一刀：片188＝P-9 (A) 备份还原计数诚实化（两 Import 回传实命中行数・虚报改告警）。⛔ push（未推 342）・HEAD 773487c。

## 六十九、片188 真码修批第 3/6 刀：P-9 (A) 备份还原计数诚实化——两 Import 回传实命中・虚报改告警

- 修法：用户状态/条目-标签关联两个 Import 回传实命中数（接口 Task 改 Task<long>），RestoreResult 按实命中填、实命中小于提供数时 StarLog.Warn 还原告警；关联按「对上了（含已在）」数——幂等重跑不算失配；UPDATE/INSERT 写入语义一字未动。
- 证人测 3 条：两 Import 回传实命中（0 行如实回 0・幂等重跑不假 0）・用户状态键漂移照实少报・关联键漂移照实少报；BackupServiceTests 定向 26/26（x64）。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates188.txt）；计数器 285／219／66 VERDICT OK（✅+1）；census 28。
- 下一刀：片189＝P-16 (A) WidgetStorage.Save→bool＋四封装透传失败＋UI 失败提示。⛔ push（未推 343）・HEAD ec8a5ae。

## 七十、片189 真码修批第 4/6 刀：P-16 (A) WidgetStorage 四封装透传未落盘＋UI 失败提示——null/false 不再冒充成功

- 修法：四个 CRUD 封装按 Save 实情报数（存布局/追加快照没落盘交回 null・删布局/删快照透传落盘结果）；Manager 五条存删出口先清后记「没写进磁盘」那句身份；三页四宿主凭它分辨「没有内容」与「没写进磁盘」，后者明说「改动未保存」；表添三键。
- 证人测 4 条＋接线闸门 3 条（四封装透传・五出口清记与四宿主计数・表三键恰一行且四列齐）；Widget 全滤定向 280/280（x64）。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates189.txt）；计数器 285／220／65 VERDICT OK（✅+1）；census 28。
- 顺手收两笔：账本 P-200 三颗 MakeUniqueName 调用点指针行移改对（census 29→28）；片173 漏挪的 EllipsisFamily 名单补记（AiClassify.cs 挪进改道名单・8→9 家・8/8 绿）。
- 下一刀：片190＝P-38 (A) SourceSyncResult 三态＋展示层着色（SyncCoordinator）。⛔ push（未推 344）・HEAD 54d51c5。

## 七十一、片190 真码修批第 5/6 刀：P-38 (A) SourceSyncResult 三态＋展示层着色——「不可用」不再与「同步成功」同形

- 修法：「不可用」照旧记 `Success=true`，第三态另立门户——`SourceSyncResult` 增 `Skipped` 旗与现算 `Status`（成功/跳过/失败三分，不落库不存档）；协调器双读数 `FailedCount`/`SkippedCount` 各走各的挑行手臂，跳过源逐颗列名，「为什么没参与」复用既有「源不可用」键；状态页着色三分：全绿照旧・有败照旧红・无败有跳走中性灰卡（表添一键「部分源未参与本轮同步」）。
- 证人测 3 条＋接线闸门 4 条（`Status` 现算不存档・跳过旗只在不可用分支抬・灰卡只在无败有跳时接线・表键三列互异零槽）；定向 274/274・全量 6747/6747（x64）。
- docgates 26/26（正本 D:\StarMarkTmp\p164\docgates190.txt）；计数器 285／221／64 VERDICT OK（✅+1）；census 28。
- 顺手收两笔：账本 13 颗行号指针行移改对（census 29→28）；交接档工作树行尾回掣 CRLF（片189 补记脚本写飞成 LF・仓库 blob 无恙）。
- 下一刀：片191＝P-31 (A)+(C) 写事务 `BEGIN IMMEDIATE`＋重入闸门（DbConnectionFactory＋事务点）。⛔ push（未推 345）・HEAD b1ff80e。

## 七十二、片191 真码修批第 6/6 刀（收口）：P-31 (A)+(C) 写事务 `BEGIN IMMEDIATE`＋同步重入排队闸门

- 修法 (A)：`DbConnectionFactory` 增 `ImmediateTxn` 与 `BeginImmediate`——18 处写事务从 deferred `BeginTransaction()` 换 `BEGIN IMMEDIATE`，删 19 行「开事务后手工挂 `conn.Transaction`」旁路；写锁在 `BEGIN` 那一拍占住——`SQLITE_BUSY_SNAPSHOT`(517) 的「读后升级」窗口根除，`busy_timeout` 只管真等待。
- 修法 (C)：`SyncAllAsync` 装排队闸门 `SemaphoreSlim(1,1)`——门口 `ThrowIfCancellationRequested()` 回绝已取消令牌（`OperationCanceledException` 本型与「闸没等到」的 `TaskCanceledException` 分口径）、闸被占留排队日志、`WaitAsync` 排队・`try/finally Release`；体抽 `SyncAllCoreAsync`。
- 证人测 6 条＋被牵动 3 测随行；定向 280/280・全量 6753/6753（x64）；docgates 26/26（正本 D:\StarMarkTmp\p164\docgates191.txt）；计数器 285／222／63 VERDICT OK（✅+1）；census 27。
- 顺手：账本 7 处行号指针 +29 改对；对账表旧区间反撞非空＝#1020 第六次（census 28→27）。
- 下一刀：真码修批六刀收口。对账表那片冻结快照攒了本批六刀的行移，下一轮对账刷新优先；其余挂账照账本名册。⛔ push（未推 346）・HEAD b1c29f1。

## 七十三、片192 对账刷新轮：对账表 33 行按六刀后真身整片重定位——落空棘轮 27→26（真修）

- 上节预告的行移欠账一次结清（码零改动）：只读探针先对拍旧文正本（REGION-MATCH 33/33）并逐行读码核对 33 行新引用才落笔；六颗改判行缀「⇒ ✅ 结案，见本条」与已翻主条对齐（P-8/P-11/P-16/P-22/P-31/P-38）、三颗 R7 快照旧号删号留锚、P-57/P-58 两行整行改写、其余 22 颗逐颗改号（正本 D:\StarMarkTmp\p164\diffcensus192.txt：33 行改号全表＋改盘脚印断言）。
- LivingSourcePointer 落空棘轮 27→26＝真修（码零改动，两读数间唯一变量＝对账表本身，⛔ 归进 #1020 那族「巧合」）；基线按读数改小 27→26（⛔ 手打）。
- 读数：docgates 26/26（x64・正本 D:\StarMarkTmp\p164\docgates192.txt）；计数器 285／222／63 VERDICT OK（±0）；census 26；全量 6753/6753（x64・码零改动）。
- 下一刀：还原 qoder 的片193 工作面（WIP 快照 15 档 sha256 核对还原）；其余挂账照账本名册。⛔ push（未推 347）・HEAD e51a472。

## 七十四、片193 P-32 站点①：托盘窗口类"每轮重注册＋类里留着第一份绑实例的委托"（＋坑表 #1123／#1124／#1125）

- 接手第一步照旧是<b>现读编号轴</b>：这颗仓在我上一批之后被别的会话推了 36 颗提交（片151～190），报告／交接／坑表三套号全换了，我那三批的账也已由他们落账并放回我那次截断事故走丢的四颗键。
- 站点①修法＝<b>沿用仓里已修好的同一族形状</b>（`ClipboardWatcher.EnsureClassRegistered` 那一套：类别只注册一次・委托静态保活・派发看静态目标），⛔ 另开第二套机制、⛔ 走 `GWL_USERDATA`。站点②（`DittoSource` 假异步・吞 `ct`）仍开放，下一把若要接：那一改的射程是"按键路径可取消"，要先定"UI 线程⛔ 等"的判据长什么样（⛔ 顺手把 ReadAsync 改成 Task.Run 就算修好）。
- 真机还欠那一格（写进 P-32 的点验单，⛔ 写进发布说明）：`设置→一般→常驻托盘` 关→开一次，再右键托盘图标看菜单；本批证到的是机制根因（派发归属），⛔ 是屏幕上那颗菜单。
- **坑表 #1123**：三套编号轴（报告节号／交接节号／坑表号）会<b>被并行会话整体换掉</b>——本批动笔前现读到的是 五百七十六・七十一・#1122，而本会话记忆里那三批还是 五百四十二・三十七・#1117。**Why**：⛔ 现读就按记忆往下编号，会撞号或跳号，而撞号＝两份正文互相顶掉，比⛔ 写更坏。**How to apply**：每次接手先跑一把"三套轴各最大几颗"的只读探针（三行 grep 就够），再决定这一批叫什么。
- **坑表 #1124**：冒烟／探针那类"打印一个数"的尺也要<b>配一发变异</b>。本批 `tray` 那一跑第一次打印"第二轮实例收到 1 次"——那还⛔ 是证据；把派发改回"第一颗实例占着不放"（等价旧行为）后同一发读到 <b>0 次</b>并非零退出，才算证到它会说"不"。**Why**：只在修后跑一次的探针，分不清"修好了"与"探针永远报 ok"。**How to apply**：任何新落地的运行时探针，当批跑一次"坏形状"对照，并把两发的数字与退出码一起记进正本；还原一律整档写回原字节＋sha 核对。
- **坑表 #1125**：⛔ 在测试里另抄一份互操作 struct。本批那颗前提尺一开始自带一份 `WNDCLASSEX` 复刻，漏了尾颗 `hIconSm` ⇒ `cbSize` 报小了 8 字节，结果<b>第一次注册就失败</b>（atom==0），读数看上去像"Win32 前提不成立"、其实是尺自己坏了。**Why**：复刻的形状一旦与被问对象⛔ 同，测的就⛔ 是它。**How to apply**：能用被问对象自己的类型与入口就用它（本仓 `NativeMethods` 对测试装配可见＝`InternalsVisibleTo` 早有）；红在"我以为会绿的那一步"时，先怀疑尺的形状再怀疑世界。

- ⚠ 两条本批自查：① <b>片158 给 P-196 写的状态正文当时挂在 ✅P-284 那块里</b>（追加落到了档尾、题行没跟着搬），本批搬回；往活档"某一条目"补正文，锚要取那颗条目的<b>行尾</b>，⛔ 取整档档尾（那颗计数器与那颗指针尺都⛔ 看得见这种错位＝静默）。② 沙盒夹具里带<b>日期</b>的那几行会随日历变老：待办三行的截止是 10-31 播的绝对刻度，11-02 再扫就读成 昨日／2日超過／4日超過——⛔ 回归失效，但期望表必须跟着重播；每支 UPDATE 打完都要报 <code>rowcount</code>（第一次播错列＝改了 0 行而⛔ 报错）。**坑表 #1126**：活档条目"补正文"要按<b>条目行尾</b>落点（⛔ 档尾）；**#1127**：写库的 UPDATE 必须报 rowcount，"这条语句跑了"⛔ 等于"那几行改了"。

- 〔并轨补记〕本批＝qoder「片191（Tray）」移植到合并线＝片193；其坑表 #1123-#1127 已按其正文落进踩坑记录（批次标 片193・2026-11-02）。合并线读数：docgates 26/26（x64・正本 D:\StarMarkTmp\p164\docgates193.txt）・census 26・计数器 285/222/63・全量 6755/6755・⛔ push（未推 1）。
- 下一刀：片194＝P-32 站点②（Ditto 令牌下传＋两出口统一 Task.Run＋取消上抛）——三颗改档（DittoDatabaseReader・DittoSource・LivingSourcePointerGateTests）在 WIP 快照、DittoCancellationGateTests 在 staging 待放回，账本 P-20/P-26 改口、P-32 题✅＋Ditto bullets、对账表 R7 行、P-256 档尾〔片194 追加〕同批落。⛔ push（未推 1）。

## 七十五、片194 P-32 站点②：Ditto 那颗源「Task 形・同步体・令牌走到门口就被扔掉」——两处都有据＋棘轮第七次改小

- 接手第一步照旧现读编号轴：报告到五百七十九、交接到七十四、坑表到 #1127；本批＝qoder「片193（Ditto）」移植到合并线＝<b>片194</b>，正文只改刀号・本行换我方轴，仪器目录与台架正本照其时间线保名（`bench193`・`p157`＝其号・⛔ 改名）。
- 站点②的<b>账本原话先被现读否掉一半</b>：那句"实时搜索逐按键内联在 <b>UI 线程</b>调用 `SearchAsync`"早已不成立——`SearchService` 那颗实时源派发是 `Task.Run(() => s.SearchAsync(…), ct)`，注释自己就点着"Ditto/书签源同理是同步文件 I/O…留在调用线程上时一次键入就是一次整窗无响应"；`FetchAsync` 那一腿也是源内 `Task.Run`。真剩下两件事＝<b>令牌被扔掉</b>（调用方 `WaitAsync(ct)` 解了等，那轮 Ditto 全表 `LIKE '%…%'` 扫描＋每颗 clip 一颗 `Data` 点查照跑完＝逐按键各留一条在跑的整表扫描）・<b>两颗公共出口形状不一</b>（`Fetch` 自己 offload、`Search` 靠别人替它 offload）。
- 修法＝<b>沿用仓里那一族现成形状</b>（循环边界 `ct.ThrowIfCancellationRequested()`：`SyncCoordinator`／`ItemRepository.Local`／`RssPageViewModel`；上抛那道 `catch (OperationCanceledException) { throw; }`：本仓二十来处）：两颗公共出口统一 `Task.Run(…, ct)`・令牌下传进 `ReadRecent`／`Search` 的两颗循环（逐行取回＋逐颗补附件各一道）・门口先回绝（⛔ 再付"枚举进程＋读别的进程模块"那笔探测税）・<b>那颗"查询失败就交空表"的兜错前面各加一道上抛</b>——否则被掐掉的一次读降级成空表，`SyncCoordinator` 记成 `Success=true, PulledCount=0`＝正是 P-26/P-28 点名的那一族（本批⛔ 动那两条账，只把"取消"这条路分出去）。
- 有据＝两把尺＋一臂对照：源码尺 `DittoCancellationGateTests.BothPublicExitsLeaveTheCallingThreadAndHandTheTokenToTheQuery`／`TheReaderCarriesTheTokenIntoBothLoopsAndLetsCancellationEscape`・运行时尺 `Reader_HandsBackCancellation_InsteadOfAnEmptyTable`（两形）／`Reader_StillReads_WhenNobodyCancels`／`BothSourceExits_HandTheCancellationBack`＋对照臂 `BothSourceExits_StillReturnRows_WhenNothingCancels`。两臂变异各真红一次（正本 `bench193.py`／`bench193.txt`，还原后 sha 与原始一致）：<b>A 摘掉 `ReadRecent` 那道上抛⇒红 2 颗</b>（运行时读到"空表"＋源码尺计数）・<b>B 把 `SearchAsync` 退回同步体＋`Task.FromResult`⇒红 1 颗</b>（源码尺）。⚠ B 臂下运行时那颗照旧绿＝xUnit 的 `ThrowsAnyAsync` 连同步抛出一起收 ⇒ "有没有离开调用线程"这一维<b>只由源码尺管</b>，账本里明写它⛔ 能用运行时读作证。
- 活档连带三处：账本 P-32 题行转 ✅（不带 ✅ 那颗数 <b>64→63</b>・标题 285／带 ✅ 222・VERDICT OK）・P-26 那串被本批平移压进空行的旧号<b>删号留锚</b>・P-20 注记与对账表那格里"该文件现 187 行／211-219"两句一并改口（棘轮 27→26，来历记在 P-256 档尾〔片193 追加〕，正本 D:\StarMarkTmp\p157\fallen_probe157.py）。
- **坑表 #1128**：往活源档<b>加行</b>会让一颗"越档尾＝落空"的死指针<b>反撞非空</b>＝从"报红"升格成"说谎"。本批 Ditto 两档加行 ⇒ `DittoDatabaseReader.cs` 那两串旧区间不再被棘轮点名，而它压着的正文与 `ParseDropFiles` 毫不相干；同一批另一串却被平移压进空行＝真落空。两颗都只能靠<b>逐颗现读</b>点名（只读尺 D:\StarMarkTmp\p157\fallen_probe157.py），处置一律删号留锚。棘轮只问端点在不在，它天生看不见"落在无关正文上"这副脸（#1020 第七次）。
- **坑表 #1129**：红名提取器⛔ 认 xUnit 那一行 `[xUnit.net 00:00:01.29]     类.名 [FAIL]` ⇒ 台架报"RED・红名=[]"。汇总行只给颗数，<b>归责要靠点名</b>：本批靠两份 `.out` 正本手工重抽才对上（A 红 2・B 红 1，与汇总行逐颗相等）。＝仪器的<b>报错分支</b>第二次没被走过（名册 734 那一族）。
- **坑表 #1130**：中途崩掉的台架会让<b>上一跑的读数冒充本次</b>。那颗 log 原本攒到批末一次写，脚本在臂与臂之间抛了 `TypeError` ⇒ 我读到的"RED 2 颗"其实是上一跑的档；靠 mtime（档 11:24:56 vs 本次 11:28:55）才抓住。改法＝起手覆盖旧档＋<b>读数逐条 flush 落盘</b>＋批末再补一行"BENCH DONE"，看盘第一颗问mtime。
- **坑表 #1131**：here-doc 递 python <b>又折掉一层反斜杠</b>——写进账本那条仪器路径里的 `\f` 被解成 0x0C，活档出现"p157allen_probe157.py"这种断口。字节扫（FF／NUL／CR／LF 四颗）当场抓到并就地补回。规矩照旧：往活档递反斜杠一律 `chr(92)` 拼或 r-string，<b>写完立刻按字节扫</b>（同一族第八次）。
- **坑表 #1132**：新落一把运行时尺之前先问<b>"这一维到底由谁作证"</b>。本批那条"离开调用线程"的维度，运行时腿<b>结构上量不到</b>（同步抛出与故障任务在 `ThrowsAnyAsync` 里同形）⇒ 只能由源码尺管；这句话现在写在账本与测试卷首，⛔ 日后拿那颗绿去充"真机也验过 offload"。

- 〔并轨补记〕本批＝qoder「片193（Ditto）」移植到合并线＝片194；其坑表 #1128-#1132 已按其正文落进踩坑记录（批次标 片194・2026-11-02）。其「棘轮 27→26・〔片193 追加〕・计数器 285/222/63・文档尺 35/35」等读数属其时间线；合并线对应＝26→24・〔片194 追加〕・285/223/62・docgates 26/26（那步 27→26＝我片192 对账刷新轮先落过）。合并线读数：Ditto 一族 36/36・census 24・全量 6763/6763（24.8 min・x64）・⛔ push（未推 1）。
- 下一刀：片195＝qoder「片194（P-48）」移植＝真机读数定裁的纯文档批——每键重绘只有一发采样宽・列表只 realize 12 张，读数三件（最坏一次 UIA 读 16~27 ms・realize 12 张・帧差 t=333 那一发 24/120）＋翻案阈值三格写进账本 P-48，⛔ 只写「看着不卡」（坑表 #1133-#1137）；不碰码、免全量。

## 七十六、片195 P-48 真机读数定裁：每键重绘只有一发采样宽・列表只 realize 12 张（＋坑表 #1133～#1137）

- 接手第一步照旧现读编号轴：报告到五百八十、交接到七十五、坑表到 #1132；本批＝qoder「片194（P-48）」移植到合并线＝<b>片195</b>（纯文档批・码零改动・免全量），正文只改刀号・本行换我方轴，仪器目录与读数正本照其时间线保名（`p194`＝其号・`readings194`＝其正本・`seed194.py` 其造・⛔ 改名）。
- 这一刀的产出是<b>“不动”，但要把“不动”量出来</b>：读数三件（最坏一次 UIA 读 16~27 ms・realize 12 张・帧差 t=333 那一发 24/120）＋翻案阈值三格写进账本 P-48，⛔ 只写“看着不卡”。
- 借这一批把“屏幕计数尺⛔ 靠猜”立住：<code>ItemCard.xaml</code> 没有 <code>AutomationProperties</code> ⇒ 先跑 ControlType 直方图认形状（Button 101・Text 77・Pane 6…），再决定按什么数卡；帧差尺另配一颗<b>有卡／无卡</b>正件对照（19/120），否则 0 读数分不清“没动”与“看不见”。
- **坑表 #1133**：帧取样要盖住<b>被测量的窗口</b>。第一版只在 +150／+230／+930 ms 取样，报 0/400＝看着像“没有闪烁”——真相是合并窗 150 ms ＋搜索＋重绑落在 T0+~330 ms，三发全在窗外。改成一发 60 ms 的时间序列才拿到那一发。<b>How to apply:</b> 时间类判据先确认取样窗覆盖被测量的整段，再用“结果集真会变”的正件对照验尺不聋。
- **坑表 #1134**：<code>AutomationElement.Get(i)</code> <b>是 1-based</b>。我用 0-based 循环 ⇒ 交出 188 行<b>全空</b>读数（CT／NAME／RID 皆空）＝仪器聋，⛔ 是被测者空。改 <code>foreach ($e in $all)</code> 直接迭代。<b>How to apply:</b> 任何“逐颗读 UIA”的新尺，第一跑要先看到已知存在的元素（如 <code>SearchBox</code>）非空。
- **坑表 #1135**：从 PowerShell 5.1 订阅 UIA <code>StructureChanged</code> 收不到任何事件（计数器恒 0），而 <code>RemoveAutomationEventHandler</code> 也⛔ 是 <code>AutomationElement</code> 的方法 ⇒ 这条路<b>现场摆不出</b>。脚本自己报了 <code>INSTRUMENT_DEAF_ABORT_READINGS_INVALID</code> 并作废全部读数。<b>How to apply:</b> 换测量学（帧差＋忙碌下限），并把“臂摆不出”显式写成一行读数，⛔ 留一句“以后再说”。
- **坑表 #1136**：这套 WinUI3 栈里 <code>$e.Current.RuntimeId</code> 读回 <b>null</b>（<code>.GetType()</code> 抛 InvokeMethodOnNull）⇒ “同一颗卡换了容器没有”⛔ 能用身份量。要问容器生命周期，只能改问可见扰动窗口或 UI 线程忙碌下限。
- **坑表 #1137**：<code>.ps1</code> 里写 CJK 查询（无 BOM）被 PS 5.1 按 ANSI 解 ⇒ 送到应用的是坏码 ⇒ 搜索<b>合法地 0 命中</b>、realized=0，看着像“应用搜不到”。同一族第三犯（#1108／#1121）。<b>How to apply:</b> 屏幕探针一律 ASCII-only，要中文语料就写进 seed 库里那条。

- 〔并轨补记〕本批＝qoder「片194（P-48）」移植到合并线＝片195（纯文档批）；其坑表 #1133-#1137 已按其正文落进踩坑记录（批次标 片195・2026-11-03）。合并线读数：census 24（±0）・计数器 285/223/62 VERDICT OK・docgates 26/26・全量免跑（承片194 6763/6763・码零改动）・⛔ push（未推 3）。
- 下一刀：片196＝L4《file:// UNC 凭据泄漏》实现批（决策方 2026-11-03 定案＝cb0533e：LaunchGuard 增本地性判定与 IsSafeShellSelectTarget 双闸串联・门测八颗・⛔ 占 P 号・计数器 ⛔ 动）；其后片197＝qoder「片195（P-64）」码五档移植。

## 七十七、片196 决策 L4：UNC 凭据泄漏落地

- 接手第一步照旧现读编号轴：账本档尾 📌 决策笔（L4・cb0533e）＋决策正本 docs/决策-L4-UNC凭据泄漏.md——门测八颗表就是验收表，正本「状态」已改「已实现（片196）」。
- 这一刀动了三颗码档：LaunchGuard.cs（IsLocalOnlyPath＋档头销账）、ItemCardActions.cs（IsShellSelectTarget 三闸串联）、LaunchGuardTests.cs（+8）；全量 6771/6771（gate196.txt・testfull196.out 在 p194）。
- ⛔ 把 IsLocalOnlyPath 的 URI 分支当用户路径判据：真实链路里它收到的是 TryExistingPath 还原后的路径（两斜杠 file://C:/x 走 raw 格＝盘符形态照旧放行）；URI 分支拒 host「C」是纵深防御按决策表 host 规则的字面执行（坑表 #1138）。
- 🔶 同面未动：打开/预览/拖出/失联扫描四处仍可触 UNC 读（决策正本只圈「打开位置」）——要收先回决策方正本另立条目，⛔ 接手顺手收。
- 下一刀：片197＝qoder「片195（P-64）」码五档移植＋报文 §583/交接七十八（期全量 6776）。

## 七十八、片197 P-64：「这一行没写进去」得当面说出来——抛异常那一支接上 0 行那一支的同一颗出口

- 接手第一步照旧现读编号轴：报告到五百八十三、坑表到 #1138；本批＝qoder「片195（P-64）」移植到合并线＝片197（四刀最后一刀），其快照 docs 止于 P-48 批 ⇒ 本篇由我新写；快照六档 sha256 逐一过 manifest_final.txt。
- 这一刀动了五颗码档＋一颗新测档：StateWriteNotice（+WriteFailed・与 RowGone 分立）、UiStrings（+state.writeFailed 三语两槽）、HiddenPageViewModel／ItemCardActions（四颗「单行状态写」宿主的 catch 接 ShowError——先当面说、再记日志）、新档 HiddenRestoreNoticeGateTests（5 颗・挂 LocaleGlobal 串行集）；门测 5/5・全量 6776/6776（gate197.txt・testfull197.out 在 p194）。
- ⚠ 修法没落「常驻状态位」那形：四颗宿主全接 0 行那一支早有的右下角提示条（同一颗 ShowError）——⛔ 同一颗失败在两处宿主各一副样式；账本 P-64 题行转 ✅（片197 落），「属 UI 装配内改动、不可机检」那半句已过期（5 颗门测就是机检）。
- ⚠ ItemCardActions 快照基线早于片196：只取其三颗 catch 增量，L4 三闸（IsLocalOnlyPath→拒引号→磁盘存在）与 OpenLocation 内联注①②③原样保留——读这档时 ⛔ 拿快照对整档 diff；HiddenPageViewModel／UiStrings 与快照字节相同（3482／512405），SmokeTest 免移植。
- 下一刀：四刀移植线到此收口——无排队刀；P-4/P-58/P-9(B)/C 类挂账/P-112 归用户定夺。⛔ push（未推 5）。

## 七十九、片198 P-4 定案 (B)＋P-24 确认 (A)：两颗题行齐翻 ✅（用户裁决批）

- 接手第一步照旧现读编号轴：报告到五百八十四、坑表到 #1138（#1139 已排队随片199 落）；本批＝用户裁决批（原话「p4：B，其他均允许，你现在就可以真机测试」·2026-11-03）。
- P-4＝(B) 维持现状：参数原序保留即最终口径，同一资源仅参数顺序不同的两条被接受为两行；证人测 <code>Normalize_PreservesQueryParamOrder</code> 钉保序（谁排序谁红）；翻案条件＝真机读到裂行证据再按 (A) 配全库 source_id 重算迁移。P-24＝(A) 确认：保留字按普通词字面匹配、空格词隐式 AND、不实现布尔算符即最终口径（码零改动・BC-2 已配测）。
- 读数：门测 UriNormalizerTests 37/37・全量 6777/6777・census 24・计数器 285/226/59（两题行齐翻：带 ✅ 224→226／不带 61→59）・docgates 26/26。
- ⚠ 仪器坑 #1139（排队）：cmd「echo EXITCODE=%ERRORLEVEL%&gt;&gt; file」的「0&gt;&gt;」绑 fd 0 重定向，EXITCODE 行不落档（197/198 同病）——判绿认汇总行「已通过! 失败: 0」，修法「&gt;&gt;file echo …」前置重定向。
- ⚠ 工作树里已躺着 199 的码（趁 198 套跑预落）：<code>BackupRepository.cs</code> 两处 Normalize＋<code>BackupServiceTests.cs</code> 证人测④⑤——接手 199 时那两档是<b>已改未测</b>态，先跑 gate199 再动账本。
- 下一刀：片199＝P-9(B)（期全量 6779）；其后片200＝P-58 信封上浮（设计定案在册）・片201＝P-145 (A) 验签钉哈希；真机批授权已给（P-112＋P-32 托盘点验＋P-46 (A) 取证＋P-85/95/102 观感截图）。⛔ push（未推 6）。

## 八十、片199 P-9 (B)：还原侧查找键与条目写入口径归一（证人测④⑤・坑表 #1139 落）

- 接手第一步照旧现读编号轴：报告到五百八十五、坑表到 #1139；本批＝P-9 (B) 根因修（用户裁决原话「其他均允许」·2026-11-03——覆盖 11-01 只授权 (A) 那笔）。
- 码两处：<code>ImportUserStateAsync</code> @sid 先 <code>Normalize</code>（<code>BackupRepository.cs:143</code>）・<code>ImportItemTagLinksAsync</code> INSERT 与 probe 共用同一颗归一 sid（<code>:219</code>）——两把尺子同一颗键；非 http(s) 键原样透传。证人测④⑤用 www.github.com→github.com 漂移键：先自证写侧归一前提（<code>item.SourceId</code> 断言）再断言还原命中（修前 0）＋幂等重跑不假 0。
- 读数：门测 28/28（gate199.txt）・全量 6779/6779（testfull199.out）・census 24・计数器 285/226/59（±0・不翻题行——P-9 片188 已 ✅，本批只补 (B) bullet）・docgates 26/26（docgates199.txt）。
- 坑表 #1139：cmd「echo EXITCODE=%ERRORLEVEL%&gt;&gt; file」的「0&gt;&gt;」绑 fd 0——EXITCODE 行不落档（197/198 同病）；判绿认汇总行「已通过! 失败: 0」；修法「&gt;&gt;file echo …」前置重定向（testfull199.cmd 起启用）。
- 下一刀：片200＝P-58 信封上浮（设计定案在册：<code>FetchResult</code> 信封・GitHubClient <code>LastFetchTruncated</code>・UiStrings <code>top.syncDoneTruncated/top.syncTruncatedTitle/sync.src.truncated</code> 三语・InfoBar 截断走 Informational・<code>SyncCheckpointOrderGateTests:28</code> 锚随签改）；其后片201＝P-145 (A) 验签钉哈希；真机批（P-112＋P-32 托盘点验＋P-46 (A) 取证＋P-85/95/102 观感截图）授权已给。⛔ push（未推 7）。

## 八十一、片200 P-58：Truncated 信封上浮——超限那轮从日志抬到汇总句与灰卡（对账刷新轮 census 24→21）

- 接手第一步照旧现读编号轴：报告到五百八十六、坑表到 #1139；本批＝P-58 信封上浮（P-57 (A) 落码时截断只到日志，本批整链抬到界面），P-58 题行随本批翻 ✅。
- 生产面：<code>IItemSource.FetchAsync</code> 回执＝<code>FetchResult</code> 信封（载荷＋<code>Truncated</code> 旗・<code>Full()</code> 给本地源）；<code>GitHubClient.LastFetchTruncated</code>（<code>GetAllStarredAsync</code> 内收旗・truncated 臂复位检查点后举旗＋Warn）；<code>GitHubSource</code> 同一拍读走；Ditto／Everything／Bookmarks 交 <code>Full</code>；<code>SyncSummary</code> 三链（top.syncPartial＞top.syncDoneTruncated＞top.syncDone）＋<code>TruncatedCount</code>／<code>TruncatedLines()</code>＋灰卡合订 <code>SkippedOrTruncatedLines()</code>（<code>lineJoin</code> 全 src 一处取词——主窗自取会红 ReasonWord ②，本批已收）；UiStrings 三键（<code>top.syncDoneTruncated</code> 3 槽英文 label 形・<code>top.syncTruncatedTitle</code>／<code>sync.src.truncated</code> 0 槽）。
- 测面：TriState 10 Facts（含行为④⑤与灰卡形状③两颗新钉）、ReasonWord 15 键、CheckpointOrder／StaleFileRow 锚随签改、测桩/调用 15 处信封化（LazyClient 假源补 <code>Assert.False(fetch.Truncated)</code>）。
- 读数：gate200 325/325・gate200b 57/57・全量 6783/6783（testfull200.out・EXITCODE=0）・census 21（对账刷新轮 #1020 第八次：四串换号・三串自然愈合・p164/diffcensus200.txt・闸基线 24→21）・计数器 285/227/58（P-58 翻 ✅）・docgates 26/26（docgates200.txt）。
- 下一刀：片201＝P-145 (A) 下载验签钉哈希（安装器 Everything-1.4.1.1028.x64-Setup.exe・便携包 1024 双钉 SHA-256・SDK 滚动走 WinVerifyTrust＋Subject 含 voidtools）；其后真机批（P-112＋P-32 托盘点验＋P-46 (A) 取证＋P-85/95/102 观感截图）。⛔ push（未推 8）。

## 八十二、片201 P-145 (A)：Everything 下载点验——两包钉 SHA-256＋SDK 验 voidtools 签名，三臂 fail 一律删净降级

- 接手第一步照旧现读编号轴：报告到五百八十七、坑表到 #1140；本批＝P-145 (A) 下载点验（P-29 纵深缺口里「未校验原生 DLL 就地加载」那半），P-145 题行未翻——重开条件里真机配合（装／不装 Everything 各跑一遍）那半留真机批。
- 生产面：新档 <code>EverythingDownloadTrust</code>——<code>InstallerSha256</code>（874e87dc…8149）／<code>PortableZipSha256</code>（4be08517…e8f5）双钉常量（2026-11-03 官网实测・升级版本号时随 URL 一起换钉）；<code>VerifySha256</code>（大小写不敏感・兜安装器臂非原子直写截断）；<code>IsAuthenticodeTrusted</code>（WinVerifyTrust・UI_NONE＋REVOKE_NONE・VERIFY 后 CLOSE 收状态句柄）；<code>IsVoidtoolsSigned</code>（链验过＋Subject 含 voidtools）；internal <code>TryGetSignerSubject</code>（CryptographicException→null）。<code>EverythingQueryQueue</code> 三臂：安装器臂哈希不过＝删包＋false（验在 Process.Start 前）；SDK 臂验签不过＝删 DLL 与 zip（留缓存＝下次 File.Exists 跳过验签・必须删净）＋false；便携包臂缓存同验・不过＝删包＋null。三臂 fail 全走「删净＋日志＋降级」。
- 测面：<code>EverythingDownloadTrustTests</code> 十二格（行为六＋形状六；正件＝coreclr.dll 内嵌签——notepad 是 catalog 签名文件模式不认；形状钉：双钉 64-hex・哈希/URL 全 src 单出处・三验点在位・WinVerifyTrust 声明恰一处）。
- 途中坑：WinVerifyTrust 第三参＝WINTRUST_DATA（内含 union choice 指到 FILE_INFO）⛔ 直传 FILE_INFO——直传则错误码随内存布局漂移且照常返回（三探针同错・坑表 #1140）；正件夹具用内嵌 Authenticode 的（coreclr.dll），System32 系列多为 catalog 签名。
- 读数：gate201 67/67・全量 6795/6795（testfull201.out・EXITCODE=0・16 m 36 s＝6783＋12）・census 21（census_post201.txt・零指针改动）・计数器 285/227/58 VERDICT OK（P-145 账本子节不进计数器）・docgates 26/26（docgates201.txt）。
- 下一刀：真机批（P-145 真机配合：装／不装 Everything 各跑一遍＋P-112 截屏开关＋P-32 托盘点验＋P-46 (A) 取证＋P-85/95/102 观感截图）。⛔ push（未推 9）。

## 八十三、片202 真机批收口：六题行齐翻（P-85/95/98/102/105/112）＋P-145 两腿三真缺陷交修批＋新登记 P-285～P-288（坑表 #1141～#1149）

- 接手第一步照旧现读编号轴：报告到五百八十七、交接到八十二、坑表到 #1140；本批＝真机批收口（零码改・纯文档批・全量免跑承片201 6795/6795）。
- 观感四颗（P-95 纯图标／P-85 抗锯齿／P-102 缩放把手／P-98 Snipaste 手感）用户裁决「符合预期」齐翻 ✅；P-105「工具条不会缩小・当前效果就可以」⇒ (A) 现状收口（⚠ 正文 fit 那句与真机现读的出入照实记档・不充当重开）；P-112「不收」转正式口径。
- P-145 两腿读数全对（真 DLL 验签=0・CN=voidtools PTY LTD・zip 非 PE 正常・装好那腿端到端查询通・用户机三件哈希与备份一致）＋三真缺陷交修批：①验签握锁 0x80092003 假阴性（先松手再验）②同锁 File.Delete 被吞＝「已删」说谎（删除核销）③EnableAsync 丢 SDK 臂结果（接回去向）；题行仍不翻——(A) 运维口径归用户。
- 新登记 P-285～P-288：Boards 顶栏遮挡（建议收菜单）・BoardsEnabled 关不关画板窗（读码坐实）・语言不跟随画板（订阅链通・断点待定位）・Snipaste 口径钉死＋显式「选择」工具＋工具条视觉精修。
- 坑表 #1141～#1149 九枚落踩坑记录（验签握锁假阴性＋同锁删除说谎／EnableAsync 丢弃／Win11 XAML 任务栏 RawViewWalker＋溢出浮层／auto-hide 唤栏 ~0.9 s／主窗隐藏 rect=∞／WinRT 前台静默／#32768 UIA 0 项／PS int 先算后拼／无 BOM CJK 码点 -join）。
- 读数：docgates202 26/26・census 21（±0）・计数器 289/233/56 VERDICT OK（带 ✅ 227→233・新立 4）。⛔ push（未推 11）。
- 下一刀：三缺陷修批（P-145 链路三处）→ 画板批（P-285～P-288）；P-145 题行翻不翻归用户拍板。

## 八十四、片203 P-145 三真缺陷修批：先松手再验＋删除核销＋EnableAsync 两臂接回就绪（坑 #1141/#1142 落地）

- 接手第一步照旧现读编号轴：报告到五百八十八、交接到八十三、坑表到 #1149；本批＝三缺陷修批（码改两档：<code>EverythingDownloadTrust</code> 新 TryDelete・<code>EverythingQueryQueue</code> SDK/安装器/便携包三臂＋EnableAsync）。
- ①先松手再验：块形 using 收干净写句柄与 ZipArchive 读锁再验签（原先 using var 跨验签＝0x80092003 假阴性・删 zip 从未真成过）；②删除核销：TryDelete（Delete 后 File.Exists 复核）三臂失败路径全走它、真实结果进日志；③EnableAsync 块体化＋<code>sdkReady && ownedRunning</code> 合取・未就绪打 Warn（设置页调用方本就吃 Failed 态，零 UI 改动）。
- 测面 <code>EverythingDownloadTrustTests</code> 12→16 格（行为⑦＋形状⑤′⑦⑧⑨）；读数：gate203 71/71・全量 6799/6799（＝6795＋4・18 m 28 s）・docgates203 26/26・census 21（±0）・计数器 289/233/56 VERDICT OK（持平）。⛔ push（未推 12）。
- 下一刀：画板批（P-285～P-288 一族）；P-145 题行翻不翻仍归用户拍板。

## 八十五、片204 画板批：P-285 收合钮＋P-286 关开关逐窗收＋P-287 发侧读数日志（开放）＋P-288 核对注（开放）＋P-289 倒置判据新立同批结案（坑 #1150）

- 接手第一步照旧现读编号轴：报告到五百八十九、交接到八十四、坑表到 #1149；本批＝画板批（码改五档＋闸门四档；新立 P-289 同批结案）。
- ①P-285：BuildStage「⋯」收合钮默认收起・点翻板（U+22EF 语言中立・色尺 18/13/13・六 add 顺序不动）；②P-286：名册 CloseAll（锁开窗集合快照、逐扇 Close＝各自关窗协商・单扇失败记 Warn）＋开关变更处理在刷托盘后挂「关＝逐扇收」；③P-287：ReapplyAppearance 加 <code>boards.lang.push code=…</code> 发侧读数日志（有而没换＝桥/引擎・缺＝宿主）；条目保持开放待真机；④P-288：activeTool=selection 待 bundle 快证；两建议归后续批；条目保持开放；⑤P-289：读档判据倒置修（is { } d && … == true）＋闸门整形钉死（禁项读抹注释后代码）。
- 测面四闸：BoardsSettings（加强＋新 Fact）・BoardAppearance（改名＋新 Fact）・BoardWindowBridge（新 Fact）・BoardWord（色尺 18/13/13）；读数：gate204 1142/1142・全量 6802/6802・docgates204 26/26・census 21（±0）・计数器 290/236/54 VERDICT OK。⛔ push（未推 13）。
- 下一刀：用户手测三步（P-285 观感／P-286 关开关收窗／P-287 真机切语言读日志定界）→ 按读数定 P-287 断点或结案。

## 八十六、片205 画板批：深浅色真因修（页侧粘性 props・坑 #1151）＋P-287 换语言链双修（开放待复测）＋P-290 同板去重新立同批结案＋P-285 侧查注（归片206）

- 接手第一步照旧现读编号轴：报告到五百九十、交接到八十五、坑表到 #1150、账本到 P-289；本批＝画板批（码改四档＋闸门两档；新立 P-290 同批结案）。
- ①深浅色真因：页侧每一发外观消息只带自己那一颗键、从 baseProps() 重拼一挂 props——后发抹先发（末挂只剩 langCode ⇒ 恒浅色）；修＝粘性档 stickyProps 整份并挂（board-bridge.js 双份同步＋manifest 条目更新）。
- ②P-287 双修（待复测）：Locale 多播改逐户喊逐户兜（抛户留痕 <code>locale.changed.subscriber.threw</code>・根因另立一案）；画板窗入口读数 <code>boards.locale.tap</code>＋三颗重贴逐颗兜（<code>boards.locale.paste.threw</code>）。复测三层日志定界：链抛痕／入口读数／发侧读数。
- ③P-290：同板连点两下＝双引擎冷启卡顿＋双写同一档；名册先查后起（lock (_open)・FirstOrDefault・Activate＋dedupe 留痕）；新起⛔ 去重。
- ④P-285 侧查注（归片206）：菜单 DOM 读数齐（main-menu-trigger・dropdown-menu-group・dropdown-menu-item base・portal 挂 body＝MutationObserver）；注入落点＝桥侧＋两颗新协议名（export.request／scale.request）。
- 测面：BoardAppearance 三新 Fact＋I18nContract 一行为 Fact（OneThrowingSubscriberMustNotSilenceTheRestOfTheChain）；读数：gate205 1188/1188・全量 6806/6806・docgates205 26/26・census 21（±0）・计数器 291/237/54 VERDICT OK。⛔ push（未推 14）。
- 下一刀：用户复测四步（深色开板／切语言读三层日志／同板连点／看 134604 笔迹）→ 按读数定 P-287 翻牌或续修；片206＝菜单注入重做（协议名＋桥 MutationObserver）。

## 八十七、片206 画板批：P-285 重做结案（五动作搬进引擎主菜单・协议进 10／出 8）＋P-287 真机复测翻牌＋P-291 装载路活态新立同批修结案＋P-292 代码关窗协商新立同批修结案（坑 #1152）

- 接手第一步照旧现读编号轴：报告到五百九十一、交接到八十六、坑表到 #1151、账本到 P-290；本批＝画板批（码改六档＋测试面六档；新立 P-291／P-292 同批结案）。
- ①P-285 重做：五颗动作搬进引擎主菜单（扳手钮底下・组题 StarMark）——桥侧 MutationObserver 兜挂点＋标记属性查重＋五项序 png→pin→svg→scale→background＋点完替用户合扳手钮；宿主四颗新 case＋PushMenuLabels 六键整推（半份＝菜单冻在旧话上）；协议进 8→10／出 7→8（export.request〔必带 kind〕／scale.request 进向・menu.set 出向）；色尺 18/13/13→12/7/7；侧查注挂点（⛔ body portal・按需挂应用 DOM）／方向两处就地更正。
- ②P-287 翻牌 ✅：真机开着画板就地切语言、画布跟着换（片205 双修后链路通）；三层定界日志口径留档。
- ③P-291：深色开旧板仍白＝装载那一拍只递一颗参、缺了「缺键拿哪一份 appState 兜」那颗第二参（引擎按自家恒 light 默认补）；修＝现读活 appState 递进去；装载路那一行未另立闸、归下一批画板闸补钉。
- ④P-292（坑 #1152）：WinUI 代码调 Window.Close() 不响 AppWindow.Closing（两套关窗各响各的钟）——关开关那条臂静默关窗；修＝BeginCloseFlush 公共身体＋CloseThroughNegotiation（桥活＝与按×同一副・桥死直接关）＋名册逐扇改道；LeaveAnyway 注释因果链被证伪（行为碰巧无恙・归下批读码对齐）。
- 测面六档：BoardWindowBridgeGate（TheBarRow 整格换成 TheEngineMainMenu 九格＋NeverInvent 四新 case＋ThePanelSpeaks 签名＋TheExportLands ④⑤＋TheTwoExportKnobs ⑧＋排序错误消息修）・BoardBridgeProtocol（名单 10/8＋③ kind 化＋新 TheMenuRequestFamily）・BoardWordGate（色尺 12/7/7＋两 token 各 2→1＋doc 段）・BoardPinExitGate（TheFifthExitIsWiredAtAllThreePlaces 重写）・BoardsSettingsGate（CloseAll 体钉改道）・BoardLinkGate（⑦重写：八颗数组＋头注三行整段＋idle 只剩 paste.query）。
- 读数：gate206 1189/1189（2 m 3 s）・全量 6807/6807（17 m 29 s）・docgates206 26/26（p206/docgates206.txt）・census 26→21（五颗旧指针顶漂＝删号留锚就地更正）・计数器 293/240/53 VERDICT OK（题行 291→293・结案 237→240・开放 54→53）。⛔ push（未推 15）。
- 下一刀：用户手测三步（深色开旧板／关开关看那一问／引擎菜单五动作观感）；P-288 两愿、P-291 装载路补钉、LeaveAnyway 注释对齐归后续批。

## 八十八、片207 画板＋截图批：P-285 白条残余收进（folder.request 进 11・menu.set 扩十键・注入项双类内芯就地修）＋P-288 两愿落地结案（显式「选择」工具・组选组动组删・工具条精修）＋P-292 LeaveAnyway 注释读码对齐（坑 #1153 全量假红）

- 接手第一步照旧现读编号轴：报告到五百九十三、交接到八十八、坑表到 #1153、账本到 P-292；本批两腿（画板腿＝P-285 白条残余；截图腿＝P-288 两愿）＋P-292 读码对齐；码改 15 档＋测试面 10 档＋新档 1。
- 画板腿（P-285 白条残余）：右上角那一行白条整块退役——「打开所在文件夹」收进引擎主菜单第六颗动作项（folder.request・Inbound 10→11・资格 folderEnabled 只认 _exportPath 真落盘的那一份），两行读数（status／hidden）做成只读行挂同一组（空话＝整行收起）；menu.set 六键→十键整推；注入项就地修双类 dropdown-menu-item dropdown-menu-item-base＋__text 内芯（真机量到的黑字小号脸）。
- 截图腿建议一（显式「选择」工具）：工具条 BarRow 首位新「选择」钮（UI-only _selectTool・⛔ AnnotationTool 枚举加名・与 SetTool 互斥・光标 Arrow）；点选单条／拖框圈一组（band 三件套・主选 hits[^1]）；组拖每帧整块重画＋节流、松手 ReplaceMany 一步历史（组删／方向键组臂同口径）；Esc 三级（组→单→无）；与「点住即拖」并存——选择态点回文字那条＝松手进文字编辑；多选提示条 capture.selectionTipMany 三语在表。
- 截图腿建议二（工具条精修）：BarNormal 0x00 透明（悬停态走 WinUI 默认模板 PointerOver＝绕开 PointerEntered += 封锁）、CornerRadius 4、Separator Height 15 居中、按钮 Padding 5,3,5,3、BarRow Spacing 3——不改语义不增按钮。
- P-292 读码对齐（行为不动）：LeaveAnyway doc 注释改口——那一记 Close() 走代码关窗直通路、⛔ 不路过 OnAppWindowClosing 那口钟（#1152 平台事实），「第二拦」无从谈起；旧注因果链已证伪、只对齐口径；闸门只钉方法体、doc 改口不破钉。
- 测试面：新档 CaptureSelectToolGateTests（11 格）＋InkDocTests／CaptureGeometryTests 各 +2＋CaptureOverlayGateTests／PinEditTests／TwoStageCaptureTests 锚点消歧＋CaptureReasonGateTests 宿主计数钉 4（批量与单条共用 ShowResizeFailed）＋CaptureNoticeWiring resizeFailed 恰 1 恢复＋画板腿四档随协议 11／8・menu.set 十键整形。
- 读数：build207 0 错 0 警；gate207 1189/1189；gate207cap 545/545（17 m 10 s・过滤器补 CaptureNoticeWiring）；全量 6822/6822（22 m 22 s・TEMP 重定向 D 盘后）；docgates207 26/26；census 21；计数器 293/241/52 VERDICT OK（结案 240→241・开放 53→52）。
- 下一刀：用户手测四步（①画板右上角无白条＋引擎主菜单六项含「打开所在文件夹」；②截图链选择钮点选／拖框圈组／组拖组删；③Esc 三级退选＋多选提示条；④工具条观感）；P-291 装载路那一行补钉仍归后续批。⛔ push（未推 16）。

## 八十九、片208 画布侧对标批：P-288 画布「选择」工具（CanvasService.Select.cs 新分段・点选/框选/组拖一步历史・选中框只进提交缓冲・逐边裁剪）＋画布工具条图标化（BarGlyphs 共库 17・Iconize 窄路・取词表 7 退役 7 新增）＋旋转把手修复（TryBeginGrab 把手先测・坑 #1154 MethodBody 切片带签名行）

- 接手第一步照旧现读编号轴：报告到五百九十四、交接到八十九、坑表到 #1154、账本到 P-292；本批单腿＝P-288 画布侧（「选择」＋工具条图标化）＋旋转把手顺带修；码改 18 档＋新档 2（CanvasService.Select.cs・CanvasSelectGateTests.cs）。
- 画布「选择」：UI-only _selectTool 一盏灯（⛔ CanvasTool 枚举加名）＋选中集按实例存＋橡皮筋 _bandTo；点选问 AnnotationPainter.HitTest（容差 8）・命中已选组＝拖整组・空白拖＝框选；组拖节流重烤持久层（墨跟手走）・松手 ReplaceMany 一步历史；空框取消（余像连同脏区由 DropSelection 收——先清 _bandTo 会滞留一帧）；选中框与橡皮筋只画提交缓冲（持久层之上・光晕之前）＝快照/存图/贴图不带；画进 clip 四条边各自求交（⛔ box∩clip 再画交边——clip 常只盖中间会画假线漏真边）；Undo/Redo/ClearAll 先 DropSelection 兜陈旧实例；Esc 第一级认选中那组（CloseWorkInProgress）。
- 图标化：BarGlyphs 共库 5 形状＋12 动作记号（17 glyph・两宿主共用）；XAML 动作钮全换记号・Iconize 一条窄路贴面＋登记无障碍名（每颗只交键）・免挂说明只 Board/Curtain/Halo/Pin 四颗；取词表 7 退役 7 新增（canvas.btn.{toBoard,undo,redo,clear,halo,copy,exit} 退役・canvas.bar.tip.{select,clear,copy,save,exit}＋canvas.status.{select,selectThrough} 新进・board/through/curtain/save 仍在岗）。
- 旋转把手（截图链）：TryBeginGrab 先测选中条把手再落全档命中——按把手那一按就是旋转，不再被全档命中截走；证人 TheHeldMarksHandlesWinThePressBeforeTheFullHitTest（gaterotate 94/94 先行）。
- 闸门面：新档 CanvasSelectGateTests（14 格）＋CanvasBarWordGateTests 事实②③重写（下界 20・免挂四颗）＋CanvasWiringGateTests 名单扩 10＋TwoStageCaptureTests Iconize 钉＋CaptureOverlayGateTests 共库 5→17＋CaptureSelectToolGateTests 把手 Fact＋RssPluralGateTests BeVerb 16→17（新英文行 {0} is active＝按钮名固定格⛔ 随槽变）；坑 #1154＝MethodBody 切片带签名行（“第一句必须是 X”先跳开括号——InnerBody helper・SourceGate 本体不动）。
- 读数：build208 0 错 6 警（存量分析器警非本批引入）；gaterotate 94/94；gate208 157/157；全量 6837/6837（testfull208b.out・14 m 3 s・TEMP 重定向 D 盘）；docgates208 26/26；census 21（P-111 行内号换号 175-177→195-197 后回基线）；计数器 293/241/52 VERDICT OK。
- 下一刀：用户手测五步（①画布「选择」钮点选/框选/组拖・Esc 或再点取消；②Ctrl+Z 一步整组回原位；③图标化工具条＋状态行「选择中…」；④选中状态下存图/复制产物不带蓝框；⑤截图链旋转把手那一按就是旋转）；P-291 装载路那一行补钉仍归后续批。⛔ push（未推 17）。

## 九十、片209 P-291 装载路补钉批：片206 修了 applyBoardLoad 兜底递活态 appState 那一行、当批未立闸——本批补上（BoardAppearanceGateTests 新格 TheLoadFallsBackToTheLiveAppStateForKeysTheSceneFileDoesNotCarry・三钉：兜底行取引擎现读恰 1・第二参递进去恰 1・单参形状禁回），产品码零改动纯闸批

- 接手第一步照旧现读编号轴：报告到五百九十五、交接到九十、坑表到 #1154（本批零动）、账本到 P-292；本批单腿＝P-291 装载路补钉（账本 P-291 条目「归下一批画板闸补钉」那句的下一批＝本批，已落地）。顺带记档：P-288 画布侧用户手测通过（选择／图标化／切语言／截图链四绿；Ctrl+Alt+U 键位与存图落点两问均系既有设计——画布全局撤销键＝Ctrl+Alt+U ⛔ Ctrl+Z・存图不弹框直落图片库 StarMark Screenshots 名册）。
- 补的钉：单参形状 L.loadFromBlob(blob) 悄悄回来时从前全仓无一格会红（片206 修了行为、当批未立闸）；现在三钉齐——var live = API && API.getAppState ? API.getAppState() : null; 恰 1（⛔ 宿主另拼假活态）、L.loadFromBlob(blob, live) 恰 1、单参形状禁回（回来＝P-291 根因原样复活）。档里自己的键由场景档赢＝引擎侧行为，闸外；桥双份同步照旧有闸（BoardSceneStoreGateTests ⑦，本批零改动照绿）。
- 读数：gate209 190/190（Board 族十档过滤）；全量 6838/6838（testfull209.out・21 m 4 s・EXITCODE=0・TEMP 重定向 D 盘）＝6837＋1（新格）；产品码零改动、坑表零动（无新坑）。
- 下一刀：无排队刀——账本开放条目逐颗核对后按用户定夺。⛔ push（未推 18）。

## 九十一、片210 账本翻牌批：52 颗开放条目全数拍板——翻 11 颗（B组 6＋P-46＋P-1/60/86/99）、30 颗拍板注记、新立 P-293/P-294，纯文档批

- 接手第一步照旧现读编号轴：报告到五百九十六、交接到九十一、坑表到 #1154（本批零动）、账本到 P-294；计数器现读＝295/252/43（bin/WI37C/count_ledger.py 现跑・VERDICT OK）。
- 拍板结果全在账本（每颗条目头一颗〔片210〕注记）：工程刀队列（已授权/已拍板）＝P-30+89 DPAPI（优先）→ C 组性能批（P-45/43/48/23/36/47）→ P-28B′＋P-44 → P-26A → P-25+57A → P-34B → P-59 加「📋」档 → P-5 落地 → P-6A → P-7A → P-126 先A；功能刀队列＝P-293 GLM 真测链路（P-30 先行）→ P-294 AI 仪表盘 → P-63 剪贴板浮层 → P-80 贴图保留 → P-82 语言选择器 → P-93 拖放回执（性能批后・参考 deskbox）→ P-120 扩展仓修（已授权）；P-46 维持现行、(B) 否决，已翻牌。
- 读数：docgates210 26/26；census 21（＝基线）；计数器 295/252/43 VERDICT OK；坑表零动（无新坑）；产品码零改动。
- 下一刀：P-30+89 DPAPI 封装（A 案已备好：明文回落＋首读透明升级＋机检判据）。⛔ push（未推 19）。

## 九十二、片211 P-30+89 落地：github.json 的 PAT 与 settings.json 的 AI Key 走同一颗 DPAPI 封装（SecretProtector・明文回落＋首读透明升级・解不开当「没配」）

- 接手第一步照旧现读编号轴：报告到五百九十七、交接到九十二、坑表到 #1154（本批零动）、账本到 P-294；计数器现读＝295/254/41（bin/WI37C/count_ledger.py 现跑・VERDICT OK）。
- 事实源：src/StarMark.Abstractions/SecretProtector.cs（「dpapi1:」哨兵・Protect/Unprotect/IsCipher）＋GitHubOptions.Save 影子副本落密文・Load 回落＋升级＋SettingsStore.Ai.cs 的 SaveAiSettings/LoadAiSettings 同口径；包＝System.Security.Cryptography.ProtectedData 9.0.0（Abstractions）；证人＝SecretAtRestGateTests 8 测（含「影子副本逐格对账」「封装出口恰一对」两颗源码闸）。
- 存量档语义：旧明文档首读当场升级为密文档（不丢同档字段）；跨机器/跨账号拷档解不开→当没配（重填）；真档升级在应用下次读取时自动发生，无需手动迁移。
- 读数：全量 6846/6846（基线 6838＋新 8）；docgates211 26/26；census 21（＝基线）；坑表零动（无新坑）；产品码＝5 文件（新增 SecretProtector.cs／SecretAtRestGateTests.cs；改 Abstractions.csproj／GitHubOptions.cs／SettingsStore.Ai.cs）。
- 下一刀：C 组性能批（P-45/43/48/23/36/47，均已授权）。⛔ push（未推 20）。

## 九十三、片212 C 组性能批落地：备份全链可取消、四源可用性三态一把尺、GitHubClient 所有权反转、主窗搜索增量同步（P-45/23/36/47/48 翻牌）

- 接手第一步照旧现读编号轴：报告到五百九十八、交接到九十三、坑表到 #1154（本批零动）、账本到 P-294；计数器现读＝295/259/36（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK）。

- 事实源：BackupRepository/MigrationRunner（CancellationToken 透传＋「先查再写」放 try 外＋zip 臂 temp+Move 原子形）＋BackupService/BackupEnvelope＋BackupCancelGateTests；SourceAvailabilityText（三态单把尺）＋IItemSource.AvailabilityKind 默认成员＋四源覆写＋SourceAvailabilityTextTests/DiagnosticsServiceTests；GitHubClient（_ownsHttp＋构造缺席才加）＋GitHubClientTests/GitHubSourceLazyClientTests；SearchPageViewModel（SyncByReference 深比增量・键退 Uri）＋SearchResultsMinimalDiffGateTests/SearchTypingMergeTests。

- 行为语义：备份取消不删完好旧档（先查再写＋原子落盘），取消回执分两档（没写出档／半档已删）；注入的 HttpClient 归建它的一方（GitHubClient 只收自建那只）；搜索结果 reuse 判据＝深比防 stale、Everything 虚拟条目 Id=0 所以键退 Uri；四源可用性经 AvailabilityKind 三态一把尺对齐。

- 闸测须知（再动须同步）：BackupCancelGateTests 断言恰 2 处 TryDeletePartial＋恰 2 处先查再写＋repo ≥8 处取消检查；SearchTypingMergeTests 钉死 RunSearchAsync(bool append) 签名＋Query.Trim()（194/194 保持）；DiagWordGate 三 kind 行在 SourceAvailabilityText 内自洽；ClipboardBackupAttachmentGateTests 数 BackupService.cs 里方法名出现次数（注释也不许写方法全名）；census 新基线 20（片212 21→20・出典在 LivingSourcePointerGateTests para＋账本 P-256 追加）。

- 读数：全量 6865/6865（基线 6846＋新 19）；docgates212 26/26（47/47）；census 20（＝新基线）；坑表零动（无新坑）；产品码＝18 src＋8 tests（新增 BackupCancelGateTests／SearchResultsMinimalDiffGateTests・改 6）＋四活档（本批账本含两颗指针修与 P-256 追加）。

- 下一刀：P-28B′＋P-44（队列次位）。⛔ push（未推 20）。

## 九十四、片213 源读失败分级（SourceReadException）、DataChangeHub interests 分发、速览复制链接不点写（P-28B′/P-44/P-26/P-295 翻牌）

- 接手第一步照旧现读编号轴：报告到五百九十九、交接到九十四、坑表到 #1155、账本到 P-295；计数器现读＝296/263/33（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK）。

- 事实源：src/StarMark.Abstractions/IItemSource.cs 尾部 SourceReadException（message＋inner 两 ctor）＋DittoDatabaseReader（busy_timeout=5000・连接 Dispose 后置 null・两颗查询吞空改抛）＋BrowserBookmarksSource.FetchAsync（attempted/failed/lastError 计数・全败才抛・部分失败跳过＋Warn）＋SyncCoordinator（catch SourceReadException 排 GitHubApiException 后・ErrorKind=Read・sync.src.read zh/en/ja）＋新 SourceReadFailureGateTests；DataChangeKind [Flags]（None/Items/Settings/All）＋Subscribe(callback, interests) hub 侧过滤（默认 All）＋Notify(kind) 必填＋25 处调用站点＋DataChangeHubTests 重写 8 测；ItemCardActions.CopyUri(vm)——GlanceWidget 的 Card_CopyRequested 改走它、XAML 接线不动＋新 ClipboardCopyLinkGateTests。

- 行为语义：「读失败」（库坏/被独占）与「还没写完/未配置」分级——后者照旧宽松交空；书签源全败才抛、部分失败跳过；速览点「复制链接」只复制不点写，点卡片本体仍 ReuseAsync；hub 按 interests 分发——DataChangeReloader 默认订 Items、GlanceWidget 唯一 interests All 全收、Canvas 热榜速览开关唯一 Notify(Settings) 源。

- 闸测须知（再动须同步）：DittoCancellationGateTests 已改口（result.Clear 计 0・throw new SourceReadException 计 2）；SourceReadFailureGateTests 形状闸（busy_timeout 恰 1・吞空 Clear 恰 0・改抛恰 2・catch 次序 github<read<blanket）；DataChangeHubTests 钉 Subscribe 过滤/默认 All/Notify 必填；StateWriteRowTests/StateWriteRowGateTests/BackupCancelGateTests 字面串随 Notify 签名；SyncReasonWordGateTests NewKeys 16（含 sync.src.read・计数 12）；ClipboardCopyLinkGateTests 钉「Card_CopyRequested 不许再接回 ReuseAsync」；census 新基线 19（片213 20→19・出典在 LivingSourcePointerGateTests 片213 para＋账本 P-256 报备）。

- 读数：全量 6880/6880（基线 6865＋新 15・20 m 42 s）；docgates213 47/47（EXITCODE=0）；census 19（＝片213 新基线）；坑表 +1（#1155 PS 5.1 UTF8 WriteAllText 带 BOM）；产品码＝16 src＋9 tests（新增 SourceReadFailureGateTests／ClipboardCopyLinkGateTests・改 7）＋四活档。

- 下一刀：片214＝P-5B/P-6A/P-25+57A/P-7A/P-34B（队列次位）。⛔ push（未推 22）。

## 九十五、片214 GitHub 取真 starred_at、collected 改读 synced_at、剪贴板第 5 档、撤销回挂标签与置顶（P-6A/P-34B/P-59/P-7A/P-25＋57A/P-126A 七颗翻牌）

- 接手第一步照旧现读编号轴：报告到六百、交接到九十五、坑表到 #1156、账本到 P-295（本轮无新立账・七颗翻牌 ✅：P-6/P-7/P-25/P-34/P-57/P-59/P-126）；计数器现读＝296/270/26（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK）。

- 事实源：GitHubClient.GetStarredPageAsync（请求级 Accept: application/vnd.github.star+json・[{starred_at, repo}] 信封・缺 repo 抛 GitHubApiException・StarredAtRaw）＋GitHubSource.MapToItem（ParseUnixTime(repo.StarredAtRaw) ?? now・弃 pushed_at 代理）＋UiStrings sort.starred/starredTip＋IItemSource.Sort 口径注释；ItemRepository upsert ON CONFLICT 臂不刷 synced_at＋Items/Search 两处 COALESCE(i.synced_at, i.created_at) DESC＋InsightsService.AnchorDate (Item, clock) 读 SyncedAt ?? CreatedAt；MainWindow.xaml SourceClipboard＋SetSourceButtonsHighlight 第 5 对＋SearchPageViewModel 两映射（"clipboard" => ItemType.Clipboard・浏览模式同口透传）＋source.clipboardTip；TodoWidgetViewModel.UndoDeleteAsync（AddTagAsync 逐个回挂＋SetPinnedAsync・按 RETURNING 盖回 id）＋新档 LocalItemUndoRoundTripTests＋TodoWriterWiringGateTests 新 Fact。

- 行为语义：「最近 Star」按用户点 Star 时刻排序（换 media type 令 ETag 换血→首轮全量重拉一次属预期）；「最近收藏」与新增趋势按本地入库时刻（首见钉住・存量行尖峰随 14 天窗口滚出）；来源段第 5 档「📋」摘出剪贴板；待办删除→撤销回挂标签与置顶；5000 封顶轮复位 ETag＋带条数 Warn＋灰卡「没拉全」名单＝截断全链可见。

- 闸测须知（再动须同步）：GitHubTruthInLabelingGateTests 现五环（标签↔数据两向都拦）；GitHubCancelSafetyTests/GitHubClientPagingTests 假桩信封用 $$$""" 裸串（CS9007 坑 #1156）；LivingSourcePointerGateTests RewrittenAnchors 65 颗两颗换新锚（starred_at 真值来自／application/vnd.github.star+json）；I18nWiringGateTests 提示键 9（source.clipboardTip）；TodoWriterWiringGateTests 撤销闸用 SourceGate.Between 截段（多行 lambda 表达式体⛔ MethodBody）；census 基线 19 保平（出典 LivingSourcePointerGateTests 片213 para 不动）。

- 读数：build 0 错；全量 6890/6890（片213 6880→6890・24 m 36 s）；docgates214 47/47（EXITCODE=0）；census 19（保平）；计数器 296/270/26 VERDICT OK（七颗翻牌・未标记 33→26）；坑表 +1（#1156）。

- 下一刀：片215＝P-5B stale 位（v7 版本化迁移 ALTER TABLE items ADD COLUMN stale INTEGER NOT NULL DEFAULT 0＋ColumnExists 幂等守卫＋CurrentVersion 6→7；VQ 模板「先报条数再删、删完重扫」；可对账源＝书签族（真全量），GitHub(5000 顶)/Ditto(MaxSync=500)/Everything(分页) ⛔；stale 位写入者只有扫描按钮与同步对账）。⛔ push（未推 22）。

## 九十六、片215 stale 位软删对账：Schema v7 双轨迁移・双门对账・灰卡・一键清（P-5 一颗翻牌）

- 接手第一步照旧现读编号轴：报告到六百零一、交接到九十六、坑表到 #1157、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-5）；计数器现读＝296/271/25（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 26→25）。

- 事实源：`ItemRepository.Stale.cs`（与同目录其余 `ItemRepository.*.cs` 是同一个类 partial——按 SQLite 访问面分文件，不是一个新抽象层；`ReconcileStaleAsync`/`CountStaleAsync`/`DeleteStaleAsync`）；`MigrationRunner`（`CurrentVersion` 7・`MigrateV7`・`ColumnExists` 幂等）；`Schema.sql`（新库直带 stale 列）；`SyncCoordinator.cs:107` 对账双门（`source.SupportsStaleReconciliation && !fetch.Truncated`）・`IItemSource.cs:87` 默认 false・`BrowserBookmarksSource.cs:69` 唯一 opt-in；`ItemCardViewModel` `IsStale`（:299）/`StaleTip`（:303）＋`BoolToOpacityConverter`（0.55）；`SettingsPageViewModel.StaleBookmarks.cs`（`RefreshStaleBookmarksAsync` 计数/重扫・`CleanStaleBookmarksAsync` 报条数→确认→删→重扫）；7 颗新 i18n 键（6 颗 settings.extend.stale*＋`card.tip.stale`）。

- 行为语义：浏览器删书签→下一轮同步该行标灰（tooltip「这条在来源清单里已经不在…」）・搜索照旧搜得着；书签在浏览器里恢复→下一轮同步清位回正常；GitHub（5000 顶）／Ditto（`MaxSync`）／Everything（分页）一律不进对账（双门拦）——「这次没出现」在截断源那里不等于「上游删了」；local 的 RSS 收藏书签永不被对账置位（作用域排除）；设置卡清理先居中报条数、确认框明说标签与笔记会一并删、删完重扫；`stale` 位写入者只有对账与一键清两个，`UpsertOne` 永不写这列。

- 闸测须知（再动须同步）：`StaleBookmarkGateTests` 十事实（`staleNote` 键==0 反拦——说明行刻意没有，谁加回来先撞 `SettingsNoteTrimGateTests` 长度再撞这里）；`SyncWriteTxnGateTests` 写事务总钉 20；`RssPluralGateTests` 冻结 94（{0} 后禁相邻裸复数名词——staleCount 走 label 形先例）；`SettingsDataWordGateTests` `SetMessage` 总钉 19；`SettingsNoteTrimGateTests` 697 字/41 行（预算 700・`ZhCapPerNote`=26）；`MigrationRunnerTests` 老库测试须走 DROP COLUMN 模拟（坑 #1157）；`SyncCoordinatorTests` `FakeSource` 两钮（`TruncatedFlag`/`Reconciles`）；census 基线 19 保平。

- 读数：build 0 错；全量 6911/6911（片214 6890→6911・23 m 39 s）；docgates215 47/47；census 19；计数器 296/271/25 VERDICT OK；坑表 #1157 止。

- 下一刀：真机验两批——片214 三项（P-6A 带 Token ETag 换血・P-59 来源段第 5 档「📋」・P-7A 删除待办后撤销回挂标签与置顶）＋片215 一项（浏览器删书签→同步→卡片变灰→设置卡计数＋一键清全链）；之后的刀待拍。⛔ push（未推 23）。

## 九十七、片216 队列清理第一刀：热榜六颗点验翻牌＋P-139 收口（七颗翻牌・零码改）

- 接手第一步照旧现读编号轴：报告到六百零二、交接到九十七、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・七颗翻牌 ✅：P-67/P-68/P-69/P-70/P-71/P-72/P-139）；计数器现读＝296/278/18（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 25→18）。

- 队列清理总口径（用户指示「继续，我暂不测试，你清空队列后我统一测试」）：未标记 18 颗分诊——落刀队列：P-43（widgets 半边：PersistBounds/EndCoordinatedMove/ApplyGeometryCore 循环内落盘・牵 P-16/P-40 时序）、P-63（全局热键唤起剪贴板选择器浮层・默认键先问用户）、P-80（贴图跨会话持久化：PNG 落盘＋元数据＋启动重建・上限 12 张）、P-82（识字识别语言选择器：候选取自 AvailableRecognizerLanguages・默认用户配置语言・按截图/识字分别记住・空文案带当前语言）、P-93（拖放回执・落地口径在册・deskbox 路径待用户）、P-120（扩展仓 classify.ts 截断加可见提示・仓路径待定位）、P-170（选项 A：语言写入点补取证日志・零行为改动）、P-293（GLM 远程 API 真测链路・P-30 DPAPI 前置已随片211 落地・key 落盘沿 P-89 口径）、P-168/169/172/192（i18n 大工单按批）；挂起队列：P-2（Everything FL 真机）、P-73（热榜两行观感・蓝图 §10 ⑩）、P-90（GLM 真测后再感受）、P-147（用户离线备份私钥）、P-153（两处藏条观感一句话）、P-294（仪表盘细案待拍板）。

- 本刀点验锚（供复核，全部在仓）：`App.xaml.cs` 热榜 DI（`GetSyncStateAsync`/`SetSyncStateAsync`・`trending:` 前缀）；`Integrations/Trending/TrendingFetcher`（tokenProvider 现取）；`MainWindow.ApplyTrendingNavVisibility`；`SettingsPageViewModel.Canvas.cs` 的 `AskTrendingGlanceAsync`；`SettingsStore.Feeds.cs` 的 `LoadTrendingEnabled`（`Load() is { } d && d.TrendingEnabled == true`＝默认关）；`TrendingService` 的 `IsSameLocalDay`；`BackupEnvelope.cs` 的 `BackupPayload`（items/userState/tags/itemTags/widgetsJson 五样）；批次 KK `22bfa74`（热榜语言下拉摘除）；`SettingsStore.Performance.cs` 的 `LoadWidgetsLoadOnStartup`/`SaveWidgetsLoadOnStartup`；`WidgetManager.Lifecycle.cs` 的 `ShowNow`。

- 读数：docgates216 47/47；census 19；flipcheck PASS（加行 14 行 0 禁词・whole 128 同态）；计数器 296/278/18 VERDICT OK。

- 下一刀：队列清理第二刀＝P-82 识字「识别语言」选择器（实现口径＝重开条件行：候选取自 `AvailableRecognizerLanguages`、默认仍是用户配置语言、按截图/识字分别记住、「这块画面里没有认出文字」文案改带当前语言）；P-293 GLM 远程 API 随后（P-89 注记在册：key 落盘沿 P-30 DPAPI 口径）。⛔ push（未推 24）。

## 九十八、片217 队列清理第二刀：P-82 识字「识别语言」选择器落地（两流两槽・显式语言关自动换语言・死码拆除）

- 接手第一步照旧现读编号轴：报告到六百零三、交接到九十八、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-82）；计数器现读＝296/279/17（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 18→17）。

- P-82 实现锚（供复核，全部在仓）：`OcrPipeline.ReadAsync` 的 `language is null && OcrImagePrep.ShouldRetryWithOtherLanguage` 闸（显式语言关自动换语言）；`SettingsStore.Capture` 的 `LoadCaptureOcrLanguage`／`SaveCaptureOcrLanguage`／`LoadScreenOcrLanguage`／`SaveScreenOcrLanguage`＋`OcrLanguageOrNone` 在场校验（写入侧只修剪）；`CaptureOverlayWindow.Actions` 的 `OcrLanguage` 选槽判据（`_mode == CaptureMode.Ocr` 走识字槽，其余走截图槽）；`CaptureOverlayWindow.Pickers` 的 `ToggleOcrLanguagePicker`（工具条右键・`GroupChip` 新增 selected 参）；`MainWindow.Tray` 的 `OcrLanguageMenu`／`RunOcrLanguageCommand`（900..999 号段・999 封顶避开 `PinGrouping` TagBase・越界忽略这一次点击）；`OcrText.ResultProblem(text, language)` 双句（新键 `ocr.text.noTextBy`）；死码 `CopyTextFromSelectionAsync` 已拆（`ocr.card.category` 的正主在 `Pin.cs` 贴图右键菜单）。

- 真机统一测清单（P-82 项，随片214/215/216 那批一起测）：① F1 截图→工具条「识字」右键弹语言栏（跟随系统＋语言包逐颗・当前项亮蓝）；② 选一门后识字→提示卡「按 <语言> 识别」报所选语言；③ 显式语言认空区域→文案带该语言且不再自动换语言重试；④ 贴图工具条右键同栏（与截图流共用截图槽）；⑤ 托盘「识字」→「识字识别语言」子菜单勾选态与生效（只改识字槽）；⑥ 两条入口互不覆盖（截图槽/识字槽分开记住）；⑦ capture 总开关关→托盘直认行与语言子菜单一起消失。

- 读数：build 0 错 0 警；定向 245/245；全量 6913/6913（13 m 09 s）；docgates217 47/47；census 19；flipcheck PASS（加行 2 行 0 禁词・whole 128 同态）；计数器 296/279/17 VERDICT OK。

- 下一刀：队列清理第三刀＝P-80 贴图跨会话持久化（拍板在册：每张 PNG 落盘＋元数据＋启动重建・上限 12 张）；随后 P-170 取证日志（选项 A・零行为改动）→P-43 widgets 半边→P-293 GLM 远程 API（key 落盘沿 P-30 DPAPI 口径）。⛔ push（未推 25）。

## 九十九、片218 队列清理第三刀：P-80 贴图跨会话持久化落地（每张＝PNG＋一行元数据・启动重建・退出不再是没了）

- 接手第一步照旧现读编号轴：报告到六百零四、交接到九十九、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-80）；计数器现读＝296/280/16（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 17→16）。

- P-80 实现锚（供复核，全部在仓）：`PinStore`（七格 PinRecord・Load 三态・Save tmp→Move・降级拒写・Normalize 不截量）；`PinManager` 持久化区（Store/Records/PngWriting/PngPending/_restoring/_shutdown/_persistFailNoticed 七字段＋PersistNew/Upsert/QueuePinPng/WritePinPngFileAsync/ReportPersistFailure/DropPersisted/RestorePersisted/RestoreOne/UnionMonitorBounds/PruneOrphanPngs 十方法）；Pin 档五正门尾钩＋五 getter（Zoom/Opacity/PinContent/ContentWidth/ContentHeight）＋`ApplyPersistedState`；Selection 档的 Rebake 尾钩；Pointer 档的 Root_PointerReleased 钩；MainWindow 的 ExitApp 首行 `EnterShutdown`＋组件恢复后单独 enqueue `RestorePersisted`；UiStrings 两新键 `pin.card.persistFailedTitle`/`pin.card.persistFailedBody`（三列）。已知残留竞态在册：in-flight PNG 写完成于关窗删除那一拍之后→孤儿 PNG→下次启动 `PruneOrphanPngs` 收走。

- 真机统一测清单（P-80 项，随片214–217 那批一起测）：① F3 贴一张→退出应用→重启→按原位/缩放/透明度回来；② 收起（F4/双击）的贴图重启后照旧收着回来（托盘可唤回）；③ 穿透贴图重启后照旧穿透；④ 拖动/缩放/调透明度后退出重启→最后状态回来；⑤ 关掉一张→重启→那张不回来（记录与 PNG 都摘了）；⑥ pins 目录里 pins.json 与 pin-*.png 一一对应；⑦ 落盘失败 toast（把存档目录设只读）本会话只弹一次其后节流；⑧ 上限 12 张贴满重启→12 张回来、第 13 张的记录不删（关掉一张再重启→它回来）。

- 读数：build 0 错 0 警（7 警全在未触碰旧档，非本刀引入）；定向 313/313；全量 6935/6935（12 m 22 s）；docgates218 47/47（翻牌后复测）；census 19；flipcheck PASS（加行 2 行 0 禁词・whole-档 128 同态）；计数器 296/280/16 VERDICT OK。

- 下一刀：队列清理第四刀＝P-170 取证日志（选项 A・零行为改动）→P-43 widgets 半边→P-293 GLM 远程 API（key 落盘沿 P-30 DPAPI 口径）→P-63 默认键先问。⛔ push（未推 26）。

## 一百、片219 队列清理第四刀：P-170 语言写入取证落地（选项 A・零行为改动・拿到读数之前不猜因）

- 接手第一步照旧现读编号轴：报告到六百零五、交接到一百、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-170）；计数器现读＝296/281/15（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 16→15）。

- P-170 实现锚（供复核，全部在仓）：SettingsPage 的 I18n 分部那一手（LanguageSelect_SelectionChanged）守卫后两行取证——「写入取证」＝旧值 LoadUiLanguageCode→新值・SelectedIndex・增删项计数・IsLoaded（先于 SetUiLanguageCode）；「生效取证」＝Locale.TrySet 回值（后于写入）；零行为改动（TrySet 回值接进变量・读旧值走既有读路径）；闸门证人＝I18nWiringGateTests.LanguageWritesCarryForensicEvidence（三序两态＋SetUiLanguageCode 全仓名册恰两处：定义＋这一手）。

- 真机统一测清单（P-170 项，随片214–218 那批一起测）：① 进设置页动那颗语言下拉→日志里两行取证（写入：旧→新・状态；生效：TrySet 回值）当场可查；② 账本那笔沙盒 zh→en 写入若再现→按取证行判「谁在什么状态下写什么」，再定 B 护栏要不要；③ 沙盒（STARMARK_DB_PATH 重定向）与真机两份日志对照答「是否只在沙盒发生」；④ 改完语言退出重启→设置档与界面语言一致（P-168 族回归）。

- 读数：build 0 错（7 警全旧档既有，非本刀引入）；定向 318/318（十四族闸，`gates219.txt`）；全量 6936/6936（13 m 11 s，`full219.txt`）；docgates219 47/47（翻牌后复测）；census 19；flipcheck PASS（加行 2 行 0 禁词・whole-档 128 同态）；计数器 296/281/15 VERDICT OK。

- 下一刀：队列清理第五刀＝P-43 widgets 半边→P-293 GLM 远程 API（key 落盘沿 P-30 DPAPI 口径）→P-63 默认键先问→P-93。⛔ push（未推 27）。

## 一百零一、片220 队列清理第五刀：P-43 widgets 半边收口（套布局/还原快照建新实例不再循环整档写读）

- 接手第一步照旧现读编号轴：报告到六百零六、交接到一百零一、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-43）；计数器现读＝296/282/14（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 15→14）。

- P-43 实现锚（供复核，全部在仓）：WidgetManager.Layout 的 ApplyGeometryCore——循环里不再 _storage.Save/_storage.Load，新建实例只进待落盘存档（data.Instances.Add）＋ShowInternal(inst.Id, data)；WidgetManager.Lifecycle 的 ShowInternal/ShowNow 增可选 pendingData（默认 null 走原读盘路径，其余调用点零改动）；落盘由三个调用方收尾（套布局/还原快照 dataRestore/启动恢复）。闸门证人＝WidgetGeometryPersistGateTests.LayoutApplyNeverTouchesTheDiskInsideTheLoop。

- 真机统一测清单（P-43 widgets 项，随片214–219 那批一起测）：① 套一套「条目多于在册实例」的布局（或快照）→缺失的实例当场新建上屏、外观按布局/快照带出；② 套完立刻重启→摆位/外观/内容与套用时一致（收尾一次落盘没丢）；③ 套用后再 Ctrl+拖动→协同移动照旧一次落盘；④ 还原快照后各组件数据（待办/随记/入口）照旧回位（ApplySnapshotAsync 映射半边未动）。

- 读数：build 0 错（7 警全旧档既有，非本刀引入）；定向 282/282（Widget 族，`gates220.txt`）；全量 6937/6937（15 m 38 s，`full220.txt`）；docgates220 47/47（翻牌后复测）；census 19；flipcheck PASS（加行 2 行 0 禁词・whole-档 128 同态）；计数器 296/282/14 VERDICT OK。

- 下一刀：队列清理第六刀＝P-293 GLM 远程 API（key 落盘沿 P-30 DPAPI 口径）→P-63 默认键先问→P-93。⛔ push（未推 28）。

## 一百零二、片221 队列清理第六刀：P-293 远程 GLM 真测链路收口（真测全绿・ListsModel :latest 误补已修）

- 接手第一步照旧现读编号轴：报告到六百零七、交接到一百零二、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-293）；计数器现读＝296/283/13（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 14→13）。

- P-293 实现锚（供复核，全部在仓）：远程通道＝OpenAiCompatibleProvider＋AiGateway.Of；key 落盘＝SettingsStore.Ai→SecretProtector DPAPI（片211）；本刀改动＝AiWire.ListsModel（Abstractions：先按原样精确对一次，:latest 宽容只留无标签名兜底）＋AiLayerTests.RemoteColonlessNamesMatchExactlyBeforeTagTolerance（4 例）。真测 harness 在仓外（GlmProbe console 工程，⛔ 进仓），可照片221 报告那段重建。

- 统一测清单（P-293 项）：设置页 AI 栏启用远程模式——Provider 选 OpenAI 兼容・BaseUrl 填 open.bigmodel.cn/api/paas/v4・Model 填 glm-5.3-flash（glm-4-flash 已退役）・ApiKey 填那颗 .env 里的值；点「测试连接」看 probe 绿（应报「模型在服务侧列表里」）；再跑一次真实「整理/摘要」看中文往返与耗时；顺手确认 key 落盘后设置档里是 DPAPI 哨兵而非明文。

- 读数：build 0 错；定向 967/967（Ai 超集闸，`gates221.txt`）＋新 Theory 4/4；全量 6941/6941（12 m 58 s，`full221.txt`）；docgates221 47/47（翻牌后复测）；census 19；flipcheck PASS（加行 2 行 0 禁词）；计数器 296/283/13 VERDICT OK。

- 下一刀：队列清理第七刀＝P-63 默认键先问（动工前先问用户默认键方案）→P-93。⛔ push（未推 29）。

## 一百零三、片222 队列清理第七刀：P-93 拖放回执落地（三岔当面说・同槽覆盖・不用秒表）

- 接手第一步照旧现读编号轴：报告到六百零八、交接到一百零三、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-93）；计数器现读＝296/284/12（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 13→12）。

- P-93 实现锚（供复核，全部在仓）：回执槽＝WidgetWindow.QuickLaunchDrop 的 SetupQuickLaunchDrop（回执 Border＋TextBlock 与遮罩同树建一次）＋SayDropReceipt（LocText.SetMessage・同槽覆盖）＋ReceiptFor（三岔翻译一处）；数据面＝WidgetManager.QuickLaunch 的 LauncherAddOutcome record＋AddPathsToLauncherAsync 回程换结局三件套＋LauncherInstanceExistsAsync（只在净增 0 那趟探一次）＋AddLinkWithOutcomeAsync（单链接三支同一份判据）；选择器出口（QuickLaunchWidget.AddPickedAsync）机械适配；五句回执话＝UiStrings 的 widget.drop.receipt.*（三语）。

- 统一测清单（P-93 项）：往快捷启动拖一批新文件→窗内见「已添加 N 个」；原样再拖一次→「N 个都已经在快捷启动里」；拖一批已在的＋一批新的→回执说净增数；拖不动（实例没了／拿不到路径）看原因句；顺手看回执槽不遮入口、后一次覆盖前一次。

- 读数：build 0 错；定向 179/179（五族，`gates222.txt`）；RssPlural 24/24；全量 6944/6944（11 m 30 s，`full222.txt`）；docgates222 47/47（翻牌后复测）；census 19；flipcheck PASS（加行 2 行 0 禁词）；计数器 296/284/12 VERDICT OK。

- 下一刀：队列清理第八刀＝P-120（动工前先读账本拍板行定形状）。两颗用户裁决项在队：P-63 默认键、P-294 仪表盘细案——随统一测问用户。⛔ push（未推 30）。

## 一百零四、片223 队列清理第八刀：P-120 扩展仓截断可见提示（第 2001 条起不再静默跳过）

- 接手第一步照旧现读编号轴：报告到六百零九、交接到一百零四、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-120）；计数器现读＝296/285/11（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 12→11）。

- 扩展仓已定位：`D:\Visual Studio Code\Something\StarMark`（wxt＋vitest）。P-120 落点（供复核，全部在扩展仓）：`src/core/ai/classify.ts`（ClassifyResult.truncated＋runClassifyLoop 截断计数＋读侧正规化＋导入补 0）、`src/entrypoints/options/panels/AiPanel.tsx`（完成通知追加＋常驻 warn-text 提示）、`src/core/locales/{zh-CN,en,ja}.json`（opt.ai.clsTruncated）；测＝classify.test.ts 新 describe 两格；commit `7ab89b6`（未推 2・该仓用户预暂存的 README/package.json 已按 pathspec 绕开）。

- 统一测清单（P-120 项・扩展侧）：扩展页塞 2001 条可见条目跑一轮 AI 整理——看完成通知与 state 行下方常驻提示都报「另有 1 条未参与本轮」；再跑一轮 51 条确认无误报提示；三语各看一眼词条。

- 读数：扩展 `npm test` 152/152（+2）・`tsc --noEmit` 0 错；桌面零码改零测改；docgates223 47/47；census 19；flipcheck PASS（加行 2 行 0 禁词）；计数器 296/285/11 VERDICT OK。

- 下一刀：队列清理第九刀＝i18n 按批（P-168／169／172／192，动工前先读各条拍板行定批形状）。两颗用户裁决项在队：P-63 默认键、P-294 仪表盘细案——随统一测问用户。⛔ push（桌面未推 31・扩展未推 2）。

## 一百零五、片224 队列清理第九刀：P-63 剪贴板选择器浮层（默认 Ctrl+Alt+V，对标 Win+V）

- 接手第一步照旧现读编号轴：报告到六百一十、交接到一百零五、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-63）；计数器现读＝296/286/10（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 11→10）。

- P-63 落点（供复核）：`src/StarMark.Core/Hotkeys/HotkeyActions.cs`（const＋All()＋CategoryOf＋DisplayMessage case）、`src/StarMark.Core/Hotkeys/HotkeyBindings.cs`（VK_V＋Defaults 条目）、`src/StarMark.Abstractions/I18n/UiStrings.cs`（三颗词条）、`src/StarMark.UI/Views/ClipboardSelectorWindow.xaml(.cs)`（新窗）、`src/StarMark.UI/App.xaml.cs`（handler 注册・主界面族后）；测＝`tests/StarMark.Tests/ClipboardSelectorGateTests.cs`（+7）。账本 5405 行那颗指向 HotkeyActions 的旧指针已按新行位修回（census 19 保形）。

- 统一测清单（P-63 项・真机）：按 Ctrl+Alt+V 唤起浮层（Win+V 系统行为不受影响）；输入即筛、↑↓ 选择、回车复制并收起、点一条复制并收起；Esc／点到别处收起；复制失败时窗口留着亮原因；设置页改绑后旧键不再占；主窗剪贴板页与浮层数据同源（同一只 LoadCommand）。

- 读数：build224 0 错；gates224 374/374；全量 6951/6951（基线 6944＋7）；docgates224 47/47；census 19；flipcheck PASS（加行 2 行 0 禁词）；计数器 296/286/10 VERDICT OK。

- 下一刀：队列清理第十刀＝P-294 AI 请求仪表盘（用户裁决已到手：设置页 AI 栏新卡＝最近请求明细＋汇总读数＋按天趋势小图，三项全选；动工前先读账本拍板行与 AI 请求链路定数据源）。其后 i18n 按批（P-168／169／172／192）→P-172→P-192。⛔ push（桌面未推 32・扩展未推 2）。

## 一百零六、片225 队列清理第十刀：P-294 设置页 AI 仪表盘（全期汇总＋按日趋势条＋最近请求明细）

- 接手第一步照旧现读编号轴：报告到六百一十一、交接到一百零六、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-294）；计数器现读＝296/287/9（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 10→9）。

- P-294 落点（供复核，全部在仓）：`src/StarMark.Abstractions/IAiUsageRepository.cs`（RecentAsync＋ByDaySinceAsync＋AiUsageDay record＋AiDashboardSnapshot record＋AiUsageTrend.LastDays 纯函数）、`src/StarMark.Data/AiUsageRepository.cs`（两读实现）、`src/StarMark.UI/Services/AiClassifyService.cs`（DashboardSnapshotAsync 一次读齐）、`src/StarMark.UI/Views/SettingsPage.AiDashboard.cs`（新档：卡装配＋行类「键＋实参」Repaste）、`src/StarMark.UI/Views/SettingsPage.xaml`（AI 栏卡列新卡三段）、`SettingsPage.Ai.cs`／`SettingsPage.xaml.cs`（接线：进栏初始化＋整理跑完跟一拍）、`src/StarMark.Abstractions/I18n/UiStrings.cs`（10 颗 settings.ai.dashboard.* 三语）；测＝`tests/StarMark.Tests/AiDashboardGateTests.cs`（新 9）＋`AiUsageTests`（+5）＋`I18nContractTests`（rowTokens 名册登记）。账本 7028 行那颗指向 SettingsPage.Ai.cs 的旧指针已随翻牌按新行位修回（census 19 保形）。

- 统一测清单（P-294 项・真机）：进设置页 AI 栏看仪表盘卡三段——全期汇总行（次数／token／估算条数／最近一条时刻）、近两星期按日趋势条（条高随量、hover 看日／token／次数）、最近请求明细（时刻／功能与模型／进出台 token／估算标记）；跑一次真实整理后不重启、回设置页看明细与趋势跟新；空账本看空态句；三语各扫一眼。

- 读数：build225 0 错；定向（~AiUsage|~AiDashboard）30/30；gates225 首跑 1819/3（~GateTests 1822 例）・修毕；全量两跑：首跑 6963/2（I18nContractTests 两红・修毕）・复跑 6965/6965（基线 6951＋14）；docgates225 23/23（四类・翻牌后复测；六类宽口径 35/35）；census 19；flipcheck PASS（加行 2 行 0 禁词・回改与 HEAD 同态）；计数器 296/287/9 VERDICT OK。

- 下一刀：队列清理第十一刀＝i18n 按批（账本 P-168／P-169／P-172／P-192 四颗，动工前先读各条拍板行定批形状与先后）。真机验收随统一测（P-63 浮层清单照旧＋P-294 仪表盘三段）。⛔ push（桌面未推 33・扩展未推 2）。

## 一百零七、片226 队列清理第十一刀：i18n 四账收官（P-168 ⑦ A/B 实测＋P-172 翻牌＋P-169 收官注记）

- 接手第一步照旧现读编号轴：报告到六百一十二、交接到一百零七、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-172）；计数器现读＝296/288/8（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 9→8）。

- i18n 四账现状（供复核）：P-168＝⑦ 三条件齐（①RY/RZ✅②A/B 数字落账＝收益≈0：包体 +107 字节、启动中位 1.12 vs 0.925 秒淹没在 0.80–1.69 秒跑间极差里、内存 ≈1 MB；③产品口径归他——「英文／日文系统首启要不要继续自动跟系统语」，现状跟；开关一开则 `CurrentUICulture.Name` 恒空串→自动探测失效→落中文默认，已表态用户不受影响）——本条不翻牌只等 ③；P-169＝内容清单全✅＋17 句清零＋收官注记（不翻牌・尾巴留统一测）；P-172＝题行翻牌 ✅（末刀＝批次 WI-31 补录）；P-192＝代码侧无量可挖，三臂（pngStillBad/canceled・badSize/shortBuffer・noLanguagePack）归他点验。本轮改动落点只有两档：docs/待决策事项.md（三处：⑦ 补录／P-169 收官注记／P-172 题行翻牌）＋tests/StarMark.Tests/I18nWiringGateTests.cs（段头注释改锚・零例数变化）；A/B 产物与探针 CSV 在 D:\StarMarkTmp\p226\（临时・不进仓）。

- docgates 宽口径基线勘正＝**34/34**（片225 报 35 系笔误；六类静态例数 4+3+13+3+8+3＝34・静态运行双证）；四类 23/23 照旧为准。

- 统一测清单（本轮入账的尾巴）：⑦ 若产品拍板「不再跟系统语」→ 改 `StarMark.UI.csproj` 的 `InvariantGlobalization`（现注释在 PropertyGroup 尾）＋重跑 A/B 冒烟；P-169 尾巴：AI 组行（要真 AI 跑）、托盘菜单、安装器向导（纯 Win32・UIA 够不着）、同步中关窗卡；P-192 三臂读数（无 OCR 语言包的机器／损坏 png 两防御臂）。

- 读数：publish A/B 双绿；定向 I18nWiringGateTests 95/95；全量 6965/6965（19 m 24 s）；docgates 四类 23/23・宽 34/34；census 19；计数器 296/288/8 VERDICT OK。

- 下一刀：队列已清——等用户三件裁决（⑦ 条件③ 产品口径／P-192 三臂点验／统一测排期），期间可接新指示。⛔ push（桌面未推 34・扩展未推 2）。

## 一百零八、片227 队列清理第十二刀：P-168 翻牌（⑦ 随用户拍板「语言首次启动自动跟系统语」永久关死）

- 接手第一步照旧现读编号轴：报告到六百一十三、交接到一百零八、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-168）；计数器现读＝296/289/7（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・未标记 8→7）。

- ⑦ 结案口径（供复核）：用户拍板「语言首次启动自动跟系统语」⇒ `InvariantGlobalization` 永久不开、系统语自动探测原样保留（英文／日文 Windows 新装首启自动跟系统语＝已交付行为，改动它要产品明示）；csproj 注释更正缓到下次真动 csproj 的批。账本落点＝P-168 尾两处（⑦ 结案段＋题行翻牌尾注）；本刀零代码零测试改动，docgates 34/34（宽口径・含 census 19）收口、不跑全量。

- 队列状态：可动项全清。开放头行剩七颗：P-2／P-73／P-90／P-147／P-153（用户裁决／长期记录）＋P-169（不翻牌・尾巴＝AI 组行・托盘菜单・安装器向导・同步中关窗卡，留统一测）＋P-192（⑴ pngStillBad／canceled・⑵ badSize／shortBuffer 防御臂・⑶ noLanguagePack 要一台没装 OCR 包的机器——三臂本机造不出，归他点验・⛔ 进发布说明直到点验）。

- 下一刀：等统一测排期或新指示。⛔ push（桌面未推 35・扩展未推 2）。

## 一百零九、片228 P-192 ⑶ 收官：noLanguagePack 三语真机读数（用户授权「卸本机 OCR→测→装回」）

- 接手第一步照旧现读编号轴：报告到六百一十四、交接到一百零九、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・P-192 追加片228 块＝14 行）；计数器现读＝296/289/7（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・本批加减 0/0）。

- P-192 ⑶ 口径（供复核）：用户授权「卸本机 OCR→测→装回」⇒ 探针（仓外・D:\StarMarkTmp\p228\OcrProbe・走 Locale.TrySet→AvailableLanguages→OcrPipeline.ReadAsync 真链）拿到无包窗口的三语 noLanguagePack 逐字读数，⇄ UiStrings 三列逐字相等（readout228.py 7/7）；四包 dism 装回全 code=0・装后复测与卸前基线逐字一致。正本全在 D:\StarMarkTmp\p228\（读数档＋能力侧档案＋复算尺）。capability 正名＝`Language.OCR~~~<tag>~0.0.1.0`（⛔ LanguageFeatures.OCR*／TextRecognition*，本机查全空）；FOD 装回每颗 22-25 分钟且 WU 下载阶段零进度条＝「像卡死」的常态，客户端死了 TiWorker 照样接着装完。

- P-192 剩余：⑴ pngStillBad／canceled（本机造不出）・⑵ badSize／shortBuffer（防御臂）两臂四颗照旧登记未验证・⛔ 进发布说明（名册 465 族口径）；翻牌与否归用户裁。⑶ 已收＝三语文案＋设置路径指引均验过。

- 队列状态：开放头行剩七颗照旧——P-2／P-73／P-90／P-147／P-153（用户裁决／长期记录）＋P-169（不翻牌・尾巴＝AI 组行・托盘菜单・安装器向导・同步中关窗卡，留统一测）＋P-192（剩 ⑴⑵ 两臂未验证尾巴）。等统一测排期或新指示。⛔ push（桌面未推 36・扩展未推 2）。

## 一百一十、片229 队列清理第十三刀：P-192 翻牌（三臂可测项全量到・用户裁决结案）

- 接手第一步照旧现读编号轴：报告到六百一十五、交接到一百一十、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-192）；计数器现读＝296/290/6（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・开放头行剩六颗）。

- P-192 结案口径（供复核）：用户裁决「翻牌结案」⇒ 三臂可测项全量到（① 片162・② 片121/123・③ 片228）；⑴⑵ 四颗 by-design 不可触发＝登记未验证收口（⛔ 进发布说明・名册 465 族）＝哪天自然出现时看提示语言对不对即可。题行翻牌照 ✅P-168 范式（✅ 前缀＋尾注・历史不倒改）。

- 队列状态：开放头行剩六颗——P-2／P-73／P-90／P-147／P-153（用户裁决／长期记录）＋P-169（不翻牌・尾巴＝AI 组行・托盘菜单・安装器向导・同步中关窗卡，留统一测）。账本可动项全清，等统一测排期或新指示。

- 下一刀：等统一测排期或新指示。⛔ push（桌面未推 37・扩展未推 2）。

## 一百一十一、片230 P-2 真机腿收官：本地磁盘搜索 C′ 三臂全过（用户授权真机跑腿・裁决翻牌）

- 接手第一步照旧现读编号轴：报告到六百一十六、交接到一百一十一、坑表到 #1157（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-2）；计数器现读＝296/291/5（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・开放头行剩五颗）。

- P-2 结案口径（供复核）：用户授权真机跑腿＋裁决「翻牌结案」⇒ C′ 三臂全过——开＝自带实例接管默认实例（对提权 Lite 优雅降级）／搜＝秒出全盘文件结果（手滑那笔 0 命中 GetLastError=0 恰证 IPC 真走通）／关＝即时剔除・引擎留存无异常；FL 时代四格清单随设计迁 C′（✅P-41）作废（勘误只进新段・历史行不倒改）。用户的 Everything Lite 与其服务全程未动・设置回腿前原状。跑的构建＝当日 HEAD 43b8b48 的 Release win-x64（build=2026-11-07 17:31）。

- 队列状态：开放头行剩五颗——P-73／P-90／P-153（用户裁决／等真机一句话）＋P-147（私钥备份风险・长期记录）＋P-169（界面语言全量铺工单・用户已放行＝下一刀开工）。

- 下一刀：P-169 开工——按普查读数 406（XAML 可见属性）＋175（C# 界面字面量・下界）分六批铺表推进；175 那半动手时以「该文件全数过一遍」为准，不拿 175 承诺。⛔ push（桌面未推 38・扩展未推 2）。
## 一百一十二、片231 P-169 统一测开跑：腿①②三语全过・安装器两颗编好・库落点事故如实落账

- 接手第一步照旧现读编号轴：报告到六百一十七、交接到一百一十二、坑表到 #1158（本轮新增 1：沙盒腿两 env 必齐）、账本到 P-295（本轮无新立账・无翻牌——P-169 片231 补录只记读数与器材）；计数器现读＝296/291/5（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・开放头行仍五颗）。

- 修复注记（只修结构・内容不倒改）：上节（一百一十一＝片230）落账时标题行被吞——「## 」前缀＋节题＋编号轴弹头丢失，只剩「一百一十一、坑表到 #1157…」半截；本批按报告 §616 同题机械复原标题与弹头前缀，节内其余行一字未动。同节「下一刀：P-169 开工——按普查读数 406＋175 分六批铺表推进」那行是抄旧模板带进来的陈旧话（六批铺表早已全兑现＝片226 收官注记），按「勘误只进新段」不倒改，以本节队列状态为准。

- 片231 口径（供复核）：P-169 四尾巴＝AI 组行・托盘菜单・安装器向导・同步中关窗卡。腿①（AI 组行）＋腿②（同步中关窗卡）三语真机全过（读数在 P-169 片231 补录①②）；③托盘④安装器点验单已交用户（安装器两颗：as-shipped＋picker，全在 D:\StarMarkTmp\p169\out\）。事故：两腿 runner 漏设 STARMARK_DB_PATH ⇒ 库落真实库（59 真实标题外发 GLM・三趟例行同步打在真实库・无标签落盘・无 stale 误标・真实 settings/github 未动）；用户裁决「不重跑、如实落账」；两 runner 已补 env（坑表 #1158）。

- 队列状态：开放头行仍五颗——P-73／P-90／P-153（用户裁决／等真机一句话）＋P-147（私钥备份风险・长期记录）＋P-169（不翻牌・剩 ③托盘④安装器两条人眼尾巴——点验单已交，等读数）。

- 下一刀：等用户 ③④ 人眼读数回来定 P-169 翻牌；或新指示。⛔ push（桌面未推 39・扩展未推 2）。
## 一百一十三、片232 队列清理第十四刀：P-169 翻牌（四尾巴读数全收・用户裁决结案）

- 接手第一步照旧现读编号轴：报告到六百一十八、交接到一百一十三、坑表到 #1158（本轮无新增）、账本到 P-295（本轮无新立账・一颗翻牌 ✅：P-169）；计数器现读＝296/292/4（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・开放头行剩四颗）。

- P-169 结案口径（供复核）：用户裁决「翻牌结案」⇒ 四尾巴全收（①腿①②腿②片231 三语全过＋③托盘④安装器点验单随用户裁决收讫）；内容侧片226 收官注记早已全兑现。题行翻牌照 ✅P-2 范式（✅ 前缀＋尾注・历史不倒改）。

- 队列状态：开放头行剩四颗——P-73／P-90／P-153（用户裁决／等真机一句话）＋P-147（私钥备份风险・长期记录）。账本可动项全清。同会话接续开工：用户指示两条新线——①AI 分类省 token（保质量前提下降本）②AI 链路韧性（客户端断联／服务商断联／额度不够的优雅处理）——下一刀登记新账后动工。

- 下一刀：登记＋调研两条新线（AI 分类 token 降本・AI 链路韧性），先读现链路再立方案；用户已放行连续开发。⛔ push（桌面未推 40・扩展未推 2）。
## 一百一十四、片233 立账两颗：P-296（AI 分类省 token）＋P-297（AI 链路韧性）

- 接手第一步照旧现读编号轴：报告到六百一十九、交接到一百一十四、坑表到 #1158（本轮无新增）、账本到 P-297（本轮新立 2：P-296／P-297）；计数器现读＝298/292/6（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・开放头行六颗：P-73／P-90／P-147／P-153／P-296／P-297）。

- P-296 口径：AI 分类省 token 只走「行级去重」——发给模型的行完全相同的条目并成一行问一遍、标签扇出全体同款；质量判据＝同款拿到的标签与不去重时逐字一致（恒等式非近似）。批加大／系统段再瘦／结果缓存三条分叉已否并记档内。

- P-297 口径：批内重试（可重试型每批最多三试・退避 Retry-After 优先、上限 60s、默认 5s/15s）＋连续两批全败熔断＋致命型即停（未配置/Key 拒/模型名不对/额度耗尽/协议不认）＋额度耗尽独立分型（402 或 429 带余额字样 ⇒ QuotaExhausted）。叫停语义零改动。文案债（ai.failure.rateLimited 承诺了不存在的重试）随落地刀补真。

- 下一刀：P-297 落地（AiFailures／AiResults／AiHttp／AiProviders／ClassifyRunner／UiStrings／AiChannelWordGateTests／AiClassifyTests／AiLayerTests），随后 P-296 落地。⛔ push（桌面未推 40・扩展未推 2）。

## 一百一十五、片234：P-297 落地（AI 链路韧性——批内重试/退避/熔断＋额度耗尽独立分型）

- 接手第一步照旧现读编号轴：报告到六百二十、交接到一百一十五、坑表到 #1159、账本到 P-297（P-297 落地未翻牌）；计数器现读＝298/292/6（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・开放头行六颗：P-73／P-90／P-147／P-153／P-296／P-297）。

- 本刀七源码＋三测试：AiFailures（QuotaExhausted 分型＋IsRetryable 分界）・AiResults（AiReply.RetryAfterSeconds 尾参）・AiHttp（RetryAfterOf 只认秒数形态・原样交不夹上限）・AiProviders（两 Read 传参）・ClassifyRunner（批内三试・退避・熔断・SkippedBatches/AbortReason・wait 尾参可注入）・UiStrings（三键三语：quotaExhausted/aborted 新・rateLimited 改口还债）・AiClassifyService（FirstError 优先 AbortReason）；测试 AiLayerTests（+8 例）／AiClassifyTests（重写 1＋新增 6）／AiChannelWordGateTests（roster 52・签名钉随 record 进化・carriesDetail 加 QuotaExhausted）。

- 判据六条全钉：瞬时失败一试救回・三试全败不扩轮・连败熔断零调用・Retry-After 被读被尊重有上限・退避中叫停照收・限流文案与实现一致（坑 #1159 还债）。真机观感归用户统一测，翻牌随用户裁决。

- 下一刀：片235 P-296 落地（ClassifyPrompt 行级去重纯函数＋AiClassifyService 接线＋状态行「去重后 M 条」新 settings.ai 键＋AiWordGateTests roster 71→72/73）。⛔ push（桌面未推 41・扩展未推 2）。

## 一百一十六、片235：P-296 落地（AI 分类省 token——同款条目行级去重・只问一遍・标签扇出）

- 接手第一步照旧现读编号轴：报告到六百二十一、交接到一百一十六、坑表到 #1160、账本到 P-297（P-296/P-297 均落地未翻牌）；计数器现读＝298/292/6（仓根跑 bin/WI37C/count_ledger.py 现跑・VERDICT OK・开放头行六颗：P-73／P-90／P-147／P-153／P-296／P-297）。

- 本刀三源码＋一文档键：AiClassify.cs（ClassifyPrompt 加 DedupedItems record＋DistinctByLine＋FanOut 两颗纯函数）・AiClassifyService.cs（OrganiseAsync：Split 后 DistinctByLine・代表者进批・两处 Merge 前 FanOut〔检查点那处也扇出〕・状态行 planPlain/planRuled 传 deduped 可空尾参）・SettingsPage.Ai.cs（预估段同刀去重）；UiStrings.cs 新键 settings.ai.service.deduped 三语（初名 dedupedNote 踩说明族预算射程・坑 #1160 改名）＋planPlain/planRuled 各加槽。

- 测试三档：AiClassifyTests 新增 4（三胞胎一行／ItemLine 非 Title〔40 字截断后同行〕／零重复恒等／95 同款 ⇒ 2 行 1 批）／AiWordGateTests roster 71→72／AiSettingsWiringGateTests 新钉 DedupSitsBetweenSplitAndEveryMergeAndTheEstimateSeesItToo（执行＋预估两处同刀・两处 FanOut・预估漏刀＝「弹窗说 15K 实际花 40K」马甲回来即红）。

- 判据四条全钉：①同款标签恒等（ItemLine 一字不差・扇出复制）；②去重量如实上报（deduped 可空半句）；③零重复零差（每组一条・扇出恒等）；④批数/token 预估跟去重后行数（预估段同刀・wiring 钉守）。真机观感归用户统一测，翻牌随用户裁决。

- 下一刀：用户放行连续开发——code review 本片或深度测试（候选：去重与 O2 规则先后次的再核・Estimate 与实际 token 的抽样对账）。⛔ push（桌面未推 41・扩展未推 2）。
