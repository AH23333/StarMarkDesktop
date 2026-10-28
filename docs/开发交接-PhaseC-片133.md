# 开发交接 · Phase C 片133（快捷入口「添加」那一发的三种没做成）

更新时刻：2026-10-28 16:20（本机）

## 一、这一片做了什么

- <b>立合并结案 ✅P-263</b>：快捷启动那颗「添加」表单在<b>没做成</b>时屏幕上零线索——① 地址认不出＝只把焦点收回输入格；② <c>AddLinkAsync</c> 那颗 <c>bool</c> 掉在 <c>await</c> 的返回值里＝净增 0 时表单收起、列表没变；③ <c>Enter</c> 那一途 <c>_ = SubmitAddLinkAsync()</c>＝整发丢进 fire-and-forget（里面一抛没人看），按钮那一途 <c>async void</c>＝一抛交给分发器（同一件事两副脸）。那颗输入格自己的占位话写着「粘贴后回车」⇒ Enter 是产品承诺过的手势。
- <b>三处同批改</b>：四颗新键 <c>widget.ql.add.badUri</c>／<c>.duplicate</c>／<c>.notStored</c>／<c>.failed({0})</c>（三语各一行）→ 一条 <c>SubmitAddLink()</c> 出口收两条进路并接住异常 → 那颗 <c>bool</c> 被问住后分两句说（判「已经在列表里」当场问屏幕上那一列：新添 <c>QuickLaunchWidgetViewModel.HasLink</c>，与落库去重共用 <c>OrdinalIgnoreCase</c>）→ 没做成⛔ 收起表单、重新开表先擦句。
- <b>证人</b>：新格 <c>QuickLaunchDropBatchGateTests.TheAddFormSaysWhyNothingGotAddedOnBothRoutes</c>（含"收表单那一行只许住在成功那一支"）＋<c>PageWordGateTests</c> 点名那串 35 → 39 颗＋那颗 ③ 格两句新钉。
- <b>真机复称（zh／en／ja 各三档＝九格，UIA 全程、⛔ 一次鼠标位移）</b>：① 认不出那档三语逐字上屏＋表单没退＋字还在；② 已经在列表里那档三语逐字＋已有行计数照旧 1；③ 成功那档作回归腿（表单收起・<c>example.com</c> 起成新行・上一发句子不残留）。正本 <c>D:\StarMarkTmp\p133\r9-zh.txt</c>／<c>r9-en.txt</c>／<c>r9-ja.txt</c>。

## 二、欠着与边界（⛔ 说成"这一发管了落盘失败"）

- <b>两颗键只有证人没有读数</b>：<c>widget.ql.add.notStored</c>（要"这张组件不在这份档里"那颗分支，在本调用点之外不可达）与 <c>widget.ql.add.failed</c>（要真抛一次异常）⇒ 照旧挂账，⛔ 写进发布说明。
- <b>同族没动的两处</b>：① <c>WidgetStorage.Save</c> 被拒时 <c>AddLinksAsync</c> 仍交回<b>非零</b>条数 ⇒ 屏幕会显示一条重启就掉的入口（账本 <b>P-16〔已核实〕</b>的下游；改它要同批改拖放与选择器两条批量出口的语义，另立一批）；② <c>WidgetWindow.QuickLaunch_Drop</c> 里"文本拖进来但认不出"那一档同样<b>连日志都没留一行</b>，而它上方那颗「拖了没反应」的分水岭证词纪律正写着不许这样 ⇒ 那是这一族的下一格。
- <b>他那一格照旧欠着</b>：#744（WI-82 Phase B 剩余）与 #801（✅P-215 白衬＋hex 的观感点验）归他；本批四颗新措辞的<b>观感</b>也归他（我⛔ 替他点头，与 ✅P-217 同口径）。

## 三、收口读数（本批）

- 重编＝0 警告／0 错误（<c>D:\StarMarkTmp\p133\build133.txt</c>）；定向〈QuickLaunch＋PageWord＋LogOnly＋I18n＋LocalPath＋两颗指针闸门〉＝通过 189／失败 0（<c>targeted133.txt</c>）；反向台架＝两臂各红恰一颗、还原后字节全等（<c>mut133.txt</c>）；全量＝<b>见 <c>full133.txt</c></b>（那一跑的读数抄进账本与报告后不再重跑）。
- 零泄漏核对：沙盒三语各自改道到 <c>D:\StarMarkTmp\p133\sdk</c>・四把写／删开关全关・<c>EnableGlobalHotKey False</c>・他那两头 <c>%LOCALAPPDATA%\StarMark</c> 最新仍是 10-27・剪贴板一字未动・三跑跑完进程数 0・光标没动。
- 提交链：<c>6001b7f</c>（码＋证人）＋文档腿那颗；<b>未推</b>，⛔ push（一事一授权）。

## 四、仪器那一笔（⛔ 产品结论，只欠我自己）

- <c>subprocess.run(env=…)</c> 整颗替换环境 ⇒ 每一臂都 BUILD-FAILED（坑表 #1097，#1093 的又一副脸：台架自己坏时看起来像我的证人没用）。
- UIA 按 <c>ProcessIdProperty</c> 找窗对 unpackaged WinUI3 报 0 扇 ⇒ 认窗改走 <c>EnumWindows</c>＋<c>FromHandle</c>，失败路径自带 <c>DUMP</c>（#1098；另：PS 5.1 取不到 <c>FocusPattern</c> 那颗类型）。
- "前台不是我的窗就⛔ 按键"那道闸门要配"当场把归属拿回来再问一次"那一拍；而我先写进注释的那句"前台是那颗子窗"被现读 <c>top=True</c> 否掉 ⇒ 假设⛔ 等于事实（#1099）。
- 沙盒：造现场靠 <c>widgets.json</c> 里预置<b>一颗已存在的入口</b>（否则第二档「已经在这张列表里」根本量不到），三语各一份档案、跑完即退。
