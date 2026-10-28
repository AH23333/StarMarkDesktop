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
