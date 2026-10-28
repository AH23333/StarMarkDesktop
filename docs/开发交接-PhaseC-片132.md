# 开发交接 · Phase C 片132（截图遮罩「没框就交出去」那一枝）

更新时刻：2026-10-28 14:36（本机）

## 一、这一片做了什么

- <b>立合并结案 ✅P-262</b>：截图遮罩上「交出去」那三颗键（<c>Enter</c> 复制／<c>Ctrl+C</c>／<c>Ctrl+S</c>，工具条四颗按钮同一条出口）在<b>还没有任何选区</b>时从前直接 <c>Settle(null)</c>＝取消整场：幕布一声不响地消失，无提示卡、无档、剪贴板零写、日志零行（真机 r6 逐格读数）。而同一颗窗口对"框太小"那一档是<b>有话</b>的（<c>ShowError</c>）。
- <b>三处同批改</b>：新键 <c>capture.select.none</c>（三语各一行）→ <c>CaptureGeometry.CommitRegion</c>（拖好的框 → 点亮着的那扇窗 → 都没有＝<c>null</c>）与 <c>CaptureGeometry.NoRegionProblem</c> → <c>Commit()</c> 只问判据：挂角标、会话留在原地；认下那扇窗时把那块落回 <c>_selection</c>。
- <b>证人</b>：<c>CaptureGeometryTests</c> 三格＋<c>CaptureNoticeWiringGateTests.CommittingBeforeABoxExistsSaysWhyInsteadOfEndingTheSession</c>＋Core 表里那颗数 1；反向台架两臂同改一把跑＝恰两颗红分居两格（45 格里通过 43）。<b>另改一颗邻族旧证人</b>：<c>AnnotationWordGateTests.TheJudgeHandsOutKeysAndNeverReadsThemItself</c> 把 <c>CaptureGeometry</c> 里 <c>LocMessage.Of("capture.</c> 的颗数钉在 4，本批的第五颗把它顶红 ⇒ 钉的数改 5 ＋另点名新那颗成员的声明（⛔ 放宽判据）。
- <b>真机复称（zh／en／ja 各三档，只用键盘）</b>：① 按住 Ctrl 关掉窗口检测（按键<b>之前</b>那份转储里没有那颗尺寸读数）⇒ 幕布还在＋那句三语逐字＋<c>PNG-STATE OK count=0</c>；② 放开 Ctrl 把指针移进一扇真窗 ⇒ 同一发交出那块（<c>count=1</c>・zh 那颗 220192 字节／1200 × 877＋『截图 · 已存图』）；③ 沙盒产物目录拒写 ACL ⇒『截图 · 存图失败』＋原因，而那一拍<b>我自己那把计数尺报 <c>DENIED</c></b> ⇒ "没多一颗"由 <c>/reset</c> 后同尺复称承担（<c>1↔1</c>）。正本 <c>D:\StarMarkTmp\p132\r8-zh.txt</c>／<c>r8-en.txt</c>／<c>r8-ja.txt</c>；改前那一跑 <c>r6-zh.txt</c>；r7 那一跑与 r8 同源，但 C 档那颗计数作废（坑表 #1095）。

## 二、顺带结掉与改口的旧账

- <b>量掉片131 那颗仪器欠项</b>：<c>capture.card.category</c> 三语逐字（『截图』／『Capture』／『スクリーンショット』）。片131 说"唯一不需要鼠标按下就能上屏的路是悬停说明"——本批更正为<b>那条路不必是悬停</b>。<b>P-194 那颗逐格覆盖照旧 26／未见 9</b>：现读名册里那颗早在片125 就按「ja 包含」记过 <c>HIT</c>，本批只是升级成三语抬头逐字；新键 <c>capture.select.none</c> 不在点名那 35 颗里。<b>⛔ 说成"截图族收完"</b>：<c>copiedTitle</c>／<c>copiedBody</c>（⛔ 写他剪贴板）・<c>categoryOcr</c>（成功即写剪贴板）・<c>disabledTitle</c>（不可达）・<c>failedTitle</c>／<c>encodeFailed</c>／<c>copyFailedTitle</c>（无安全确定性触发）照旧挂账。
- <b>#550 那颗待办的前提被我自己的两把尺否了</b>：XAML 可见属性里的汉字格数＝0、C# 上屏出口形状里的汉字行数＝0，P-188 的结案闸门已把 71 行"只进日志"钉住 ⇒ 那一档"剩下的屏幕字"已经没有可机检的活；本批因此改指读码新找到的静默失败。<b>下一手要么把 #550 按"机检面已饱和"结案，要么另找真缺陷</b>，⛔ 再为它造活。

## 三、收口读数（本批）

- 重编＝0 警告／0 错误（正本 <c>D:\StarMarkTmp\p132\build132-final.txt</c>・装配 14:21＝提交之后重编）；定向〈那颗钉数的闸门一族〉＝通过 55／失败 0（正本 <c>D:\StarMarkTmp\p132\targeted132-after-witness.txt</c>，命令原文抄在账本那条读数里）；反向台架＝45 格里恰 2 红（红名＝上面那两颗）；全量首跑＝失败 1／通过 6596（红那颗＝那颗钉住颗数的邻族证人，正本 <c>D:\StarMarkTmp\p132\full132.txt</c>）→ <b>修后全量收口＝通过 6597／失败 0／总 6597（正本 <c>D:\StarMarkTmp\p132\full132-final.txt</c>）＝基线 6593＋新证人四格</b>。
- 零泄漏核对：剪贴板三跑前后全等（<c>TEXTLEN=0</c>・只走存图那一档）・他 Pictures／LocalAppData／Roaming 三处照旧只数不改・沙盒三颗都停・ACL 三处 <c>/reset</c>・三颗含他屏幕内容的 PNG 逐颗删净（现读每颗 <c>left=0</c>）・光标 <c>906,552</c> 归位。
- 提交链：<c>3e89816</c>（码＋证人五档）＋<c>2158139</c>（那颗钉颗数的闸门跟着第五颗走）＋文档腿那颗；<b>未推</b>，⛔ push（一事一授权）。

## 四、仪器那一笔（⛔ 产品结论，只欠我自己）

- <c>SetCursorPos</c> 不入输入队列 ⇒ 玻璃收不到 <c>PointerMoved</c>（坑表 #1091）：造悬停／跟随类现场一律用会入队的注入。
- 转储时刻要对齐因果那一拍（#1092）・预检要照真跑那颗命令形状（#1093）・改文本⛔ 动行边界（#1094）・<b>拒绝写的 ACL 连枚举一起拒 ⇒ "数盘上多没多一颗"的尺要挪到 <c>/reset</c> 之后，且必须自带 <c>DENIED</c> 那一档</b>（#1095；同一族＝坑表 #456"⛔ 许没有'读不到'分支"）。
- 沙盒：只绑 <c>screen.capture</c>＝Ctrl+Alt+Shift+F12（<c>D:\StarMarkTmp\p132\stage-r6.py</c>／<c>stage-r7.py</c>），四把写／删开关全关，档根走 <c>STARMARK_SETTINGS_PATH</c>／<c>STARMARK_DB_PATH</c> 改道到 <c>D:\StarMarkTmp</c>；探针两版＝<c>probe133.ps1</c>（r7）与 <c>probe134.ps1</c>（r8，那颗计数尺修好的一版）。
