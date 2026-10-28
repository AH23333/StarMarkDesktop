# 开发交接 · Phase C 片134（托盘路第二路复测・片131 点验单收回）

更新时刻：2026-10-28 23:05（本机）

## 一、这一片做了什么

- <b>纯文档腿</b>：收片131 交接档「四、接续顺序」第 1 条那张点验单——新一代仪器（UIA 只留在读卡树，认窗／开菜单／点击全走 Win32）把 S／F 三语六档整张量回，逐字读数在 <c>D:\StarMarkTmp\p131\run131-zh.txt</c>／<c>run131-en.txt</c>／<c>run131-ja.txt</c>（15 STEP＝S 腿 8＋F 腿 7）。账本 ✅P-194 节内插块＋报告 §五百一十六＋坑表 #1100～#1103＋片131 档 §五 更正节＋本档，五处同批。
- <b>三条拆法</b>：①认窗第二路＝<c>FindWindowExW</c> 直取那颗 <c>HWND_MESSAGE</c> 的 <c>StarMarkTrayMessageWindow</c>（逐线程清点与 UIA 的普查盲区＝坑表 #1100），<c>GetWindowThreadProcessId</c> 核 pid；②开菜单＝往自家托盘窗 <c>PostMessage</c> 递 <c>TRAY_CALLBACK 0x8001</c>＋<c>WM_RBUTTONUP 0x0205</c>，不碰他的任务栏；③菜单 UIA 零格（#1101）⇒ <c>MN_GETHMENU(0x01E1)</c> 梯读 <c>rows=31</c>＋<c>GetMenuItemRect</c> 真坐标点行 5（三语同 <c>index=5</c>＝『截图（框选区域）』那一发）。
- <b>量到的</b>：S 卡三行（『截图 · 已存图』／『Capture · Saved』／『スクリーンショット · 保存しました』＋『672 × 422 → …』＋hint）・F 卡（存图失败三语＋<c>saveWriteFailed</c> 三语逐字）・PNG 三颗 31328／32535／237972B（sha=8a03d979c8703870／282aad2a6dd579fe／605947a6867e3b94）・卡名三语（『StarMark 提示』／『StarMark notice』／『StarMark お知らせ』）・expect 三档各 6 行喂进判据。F 造法＝ICACLS <c>/deny Everyone:(OI)(CI)(WD,AD)</c> 只挡那颗目录的写、跑完 <c>/remove:d</c> 即清。

## 二、欠着与边界

- <b>悬停说明（ToolTip）照旧挂账</b>：新一代尺仍唤不出（#1101 的边界：可勾行 24／25 的勾态没量；hover 那族照旧走片128 的尺）。
- <b>p128 那五颗托盘键照旧挂他的点验单</b>：p128 expect 三档已刷新 15 行（10-28 18:45），下一片用这第二路收回自量。
- <b>同雷潜伏</b>：p128 <c>idle128.ps1:29</c> 那颗 <c>{0:N1}</c>（坑表 #1102 同一颗雷）——下一片动 p128 仪器时先修。
- <b>归他那一半照旧</b>：观感与措辞的点验、#744（WI-82 Phase B 剩余）。

## 三、收口读数（本批）

- 文档四闸门＝通过 23／失败 0（改前改后各一回，正本 <c>D:\StarMarkTmp\p131\docgates134.txt</c>）；账本计数器四串＝<b>264／139／125／0</b>・VERDICT OK（<c>bin/WI37C/count_ledger.py</c>，插块 ⛔ 立新号故四串不动）。
- 零泄漏核对：CLIPWIT pre==post（zh／en <c>TEXTLEN=8049 TEXTSHA=D957F14FFE5A334B</c>・ja <c>TEXTLEN=49 TEXTSHA=0293615D9B02239C</c>）・PICS pre==post 各 9 颗（PICS-GUARD 相等=True×3）・ICACLS 跑完即清・收摊三跑各「台上还剩 0 颗 StarMark.UI」。
- 提交：本批只文档腿这一颗；照旧<b>未推</b>（未推 303），⛔ push。

## 四、仪器那一笔（⛔ 产品结论，只欠我自己）

- message-only 窗＝逐线程清点与 UIA 的普查盲区（#1100）：认窗要走 <c>FindWindowExW</c> 直取＋pid 核对那第二路。
- <c>TrackPopupMenuEx</c> 弹出的菜单 UIA 零格（#1101）⇒ <c>MN_GETHMENU(0x01E1)</c>＋<c>GetMenuItemRect</c> 那把梯；边界＝可勾行 24／25 勾态没量。
- PowerShell <c>{0:N1}</c> 自带千分位 ⇒ 下游 <c>float()</c> 炸（#1102）：读数格式一律 <c>{0:F1}</c>。
- Add-Type 里裸 <c>[DllImport]</c> 默认 private ⇒ 外面调不着（#1103）：P/Invoke 一律显式 <c>public</c>。
