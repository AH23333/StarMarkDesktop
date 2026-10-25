# 开发交接 · Phase C 片109（P-217 下半刀＋一颗新维度）

<b>这批在做什么</b>：界面语言线上 P-217 那一族的第二刀。片48 收了"数字紧跟复数名词"那一路的 10 行英文列，
本片收同一条 <c>StatusTextText</c> 上剩下的三行（它们其实也是 <c>feeds</c> 那一族），并<b>另立一颗新尺</b>量
「数字紧跟 be/have」那一维——旧尺结构上一颗也量不到。整表那 85 颗存量仍开着，交他裁。

## ① 这一批交了什么

| 那一半 | 落点 | 状态 |
| --- | --- | --- |
| 6 行英文列改与数量无关的摆法 | <c>src/StarMark.Abstractions/I18n/UiStrings.cs</c>（numstat 6/6・纯 LF・⛔ BOM） | ✅ 已落 |
| 两颗钉子＋一颗新维度格 | <c>tests/StarMark.Tests/RssPluralGateTests.cs</c>（四格→六格・0/24/24） | ✅ 已落 |
| 反向台架四臂 | <c>bin/WI99/p109_r1/bench_109.py</c>（BAD=0・正本同名读数.txt） | ✅ 跑过 |
| 真机五档 | <c>bin/WI99/p109_r1/cap_109.py</c>・正本 <c>cap109_r3.txt</c>（两把尺 0 命中・欠一句见 ④） | ✅ 跑过 |
| 复称与读数 | <c>census_post109.txt</c>／<c>gate109_*.txt</c>／<c>full109_r1.txt</c>／<c>counters109_r1.txt</c> | ✅ 已留 |

## ② 现读的那几颗数（谁也不许从别处抄）

- 表：2097 颗 <c>new Entry(</c>・本批⛔ 增删键；改的是六行的英文列正文。
- 只读尺：紧档候选 92 颗槽／涉及 81 行・计数槽（真缺陷）85・名字槽误认 1・判不出 6・可达 63／只进日志 12・RSS 那一族 1 颗 ['rss.gapDaysPlus']。
- 证人两颗钉子：形状尺 94（改前 100・由那一跑写下来）・新维度 14。
- 屏幕那五句：见账本 P-217 那一笔的五行逐字（正本 <c>cap109_r3.txt</c>）。
- 收口：编译 0 警告 0 错误・定向 0/24/24・邻族 0/920/920・全量 0/6505/6505・计数器 258／129／129／0（改前＝改后）。

## ③ 后面动手要守的口径

1. <b>那颗槽是数字还是名字，问中文列</b>（紧跟量词＝计数槽；被引号包着＝名字槽）——⛔ 猜调用点实参形状。
2. <b>候选数⛔ 等于缺陷数</b>：两把尺两颗数都要写进报告，颗数要带它那一跑的命令原文。
3. <b>钉子是双向的</b>：变多＝新写的英文句又犯了；变少＝有人真修掉一颗，要连同这颗钉子与账本 P-217 那笔账<b>一起</b>改。
4. <c>LocMessage</c> 的槽⛔ 被删：把数字藏起来也能让"两档读法相同"那半变绿，所以那颗 Theory 另钉一句"那句里还得有槽"。
5. 活档（账本）⛔ 写行号指针：写档名＋那颗逐字锚，行号只留在 <c>bin/**</c> 正本里。

## ④ 欠项与未验证（⛔ 写进发布说明）

- 真机<b>没</b>在屏幕上逐字读到那一句＝en-due-1 没逐字读到 ['Overdue by more than a day: 1']・en-due-2 没逐字读到 ['Overdue by more than a day: 2']。归因读在主人身上：那颗 <c>rss.statusCacheDue</c> 由 <c>ReloadSources()</c> 摆上屏后，
  同一条 <c>PrimeAsync</c>→<c>StartRoundAsync</c>→<c>RunRoundAsync</c> 链在<b>到期档</b>立刻把它盖成 <c>rss.fetchingDue</c>
  （新鲜档走到那句时 <c>targets.Count == 0</c> 直接 return・所以 <c>statusCacheFresh</c> 摆得住——两档差别就出处在这里）。
  离开再回来那一途⛔ 绕得过去：<c>OnNavigatedFrom</c> 会 <c>CancelLoading()</c>⇒回来时 <c>IsBusy</c> 已假＝又开一轮、又盖一次（r1／r2／r3 三跑同形）。
  它的措辞改动由<b>装配里那张表</b>量到（证人拿编译进装配的 <c>UiStrings.All</c> 在 1 与多两档各渲染一次）。
- ⚠ 由此量到一颗<b>观感题</b>（本批⛔ 动・归他裁）：那句"打开这一页会自动重抓、⛔ 用点按钮"的说明用户读不到（一闪即被顶掉）。
  要不要把它并进 <c>fetchingDue</c> 那句、或让前一句先停一拍＝措辞＋观感题，⛔ 由我在夜里替他定。
- 整表 85 颗计数槽（81 行）＝(A) 逐颗改措辞／(B) 表加复数机制／(C) 认了。<b>观感＋措辞题，交他裁</b>；名单在 <c>bin/WI92/census_48_post.txt</c>。
- <c>rss.gapDaysPlus</c> 那句在当前常数下不可达（<c>AutoRefreshGap＝24 小时</c>）；要量它得先动那颗常数＝另一批的事。
- 本机⛔ 验的那两格：那 6 句改后的观感（label 形 vs 自然句）＋深色主题/他机器上的排版（英文列改短了，重叠风险反而降低）。

## ⑤ 他那一格（点验单，逐格一句话）

| 格 | 在哪里 | 该看到什么 |
| --- | --- | --- |
| RSS 页顶（缓存是新的） | 主窗「RSS」页 | 一句以 <c>Feeds: N, enabled: N.</c> 开头、含 <c>within a day</c> 的话；⛔ 出现 <c>1 feeds</c>／<c>1 are</c> 那一形 |
| RSS 页顶（有源过期・正在补抓） | 主窗「RSS」页（有源超过一天没抓时） | <b>该看到</b> <c>Re-fetching N of the overdue feeds (older than a day)…</c>；<c>Overdue by more than a day: N</c> 那句按现读<b>只闪一下</b>就被顶掉——他若<b>看得见</b>它，说明我把那条链的时序读错了，请当场说一声 |
| 设置→AI 整理确认 | 那颗确认框正文 | <c>Items in total: N — settled by high-confidence rules…</c> |
| 设置→数据→备份（带剪贴板图片） | 打包完那句 | <c>Clipboard images in this package: N; written back: M…</c> |
| 设置→数据→清理（有文件被占） | 那句半失败 | <c>Deleted N; M still in use by another program…</c> |
| 日文/中文列 | 同两处 | 一字未动（本批⛔ 动 zh／ja 列） |

## ⑥ 提交链（本批）

起点 <c>afa82f42ff2f0fcc54ea9706de676eefe44c0b57</c> →（代码腿）… →（文档腿）… →（收口）…；⛔ push（一事一授权）。
