#nullable enable
using System;
using System.Globalization;

namespace StarMark.Core.Boards;

/// <summary>
/// 画板（excalidraw 那扇窗）在桌面这一侧的<b>身份、落盘节拍与撤销立场的唯一主人</b>（批次 Phase C 片12＝落地设计提案 v1 §21.3／§21.4 的落码）。
/// <para>
/// 这里只住<b>已经判过的事</b>：档名形状、一颗板一颗档的对应、四拍 autosave、
/// "载档与桥回写算⛔ 算一次用户动作"那颗裁决。三样东西刻意<b>⛔ 在这里出现</b>：
/// ① 那颗<b>自给</b>的字节上限：片58 之前它⛔ 存在，而"没量过"这件事由 <see cref="ImageCeilingsMeasured"/>
///    押着闸门（判据里⛔ 许出现猜测的数字）。片58 在真机上把「档体积 ↔ 那一拍按住界面多久」量出来了，
///    于是上限住进 <see cref="SceneBytesCeilingBytes"/>，每一颗数字的出处写在那一段里
///    （正本 <c>bin/WI98/readings_58z_ladder2_221131.txt</c>），闸门同时换成
///    「自给的上限必须带一句盘上真有的正本」（<c>EverySelfGivenCeilingCitesAReadingThatExists</c>）。
///    ⚠ 它<b>只</b>覆盖「字节」那一半：长边（画板里一张图多大像素）由片60 现读判掉了——那颗数在引擎侧有主人，
///    而读它的那条路被我们自己那颗 <c>tools:{image:false}</c> 整体关掉（见两颗 <c>EngineReported*</c> 那两段），
///    本档因此照旧只放着引擎自报的那两颗（名字里就写着 <c>EngineReported</c>）；
/// ② 那颗<b>成品路径</b>（片23 起根归 <c>UserDataPaths</c>，UI 侧只问它要那颗子目录）。<b>片39 给这一条改口</b>：
///    本档现在裁「<b>用户的产物进哪一族</b>」（<see cref="ExportsFolder"/>），但两棵根由宿主递进来——
///    本档⛔ 自己读环境变量、⛔ 认 <c>%APPDATA%</c>／<c>%USERPROFILE%</c>（层次倒挂，同它⛔ 认 WebView2 那颗 enum）；
/// ③ 上屏的句子（一律交 <c>LocMessage</c>，本档⛔ 成品字＝P-193/P-194 那条口径）。
/// </para>
/// </summary>
public static class BoardPolicy
{
    /// <summary>随包那颗引擎的减重长边（<b>现读</b>：<c>DEFAULT_MAX_IMAGE_WIDTH_OR_HEIGHT</c>，
    /// 定义在 <c>dist/dev/chunk-3KPV5WBD.js:322</c>＝1440）。<b>它是引擎那侧的读数，⛔ 我们给的上限</b>——
    /// 记在这里只为排障时对得上号（§19.3 那颗"too big 其实是解码失败"的形状就靠它对账）。</summary>
    /// <para><b>片60 把它射程的两头都读了一遍</b>（随包 <c>assets/boards/excalidraw.min.js</c>＝4 878 772 颗字节，
    /// sha 前缀 <c>923fa221d0a4fc22</c>；正本 <c>bin/WI98/readings_60_bundle_r2_001740.txt</c> 与
    /// <c>bin/WI98/readings_60c_bundle_002145.txt</c>）：那颗数在<b>成品 bundle</b> 里逐字只 1 处声明
    /// （<c>,px=1440</c>，offset 2166993），而唯一读它的消费点在 <c>initializeImage</c> 体里
    /// （<c>maxWidthOrHeight:px</c>，offset 4768057）；那条路只有一颗调用（<c>initializeImage(</c>，offset 4769096）
    /// ＝<c>insertImageElement</c>，而它起手先问 <c>isToolSupported("image")</c>
    /// （定义逐字 <c>this.props.UIOptions.tools?.[E]!==!1</c>，offset 4734622）——我们桥里
    /// <c>assets/boards/board-bridge.js:207</c> 递的正是 <c>tools: { image: false }</c>
    /// ⇒ <b>这一颗数今天在产品里一颗也不咬</b>：粘贴／拖放／工具条那三处入口一并被同一颗开关挡在外
    /// （开关关着的那颗屏幕正件＝<c>bin/WI83/spike1b/out_r9a/cells.json</c>，工具条 32→30 少 <c>toolbar-image</c>）。
    /// ⚠ 那颗开关的第二把扳机＝桥里 <c>case "feature.flags"</c>（<c>board-bridge.js:152</c>）：那一手交的是
    /// <b>整颗 UIOptions 替换</b>而⛔ 合并，宿主今天一颗也不发（名字只在 <see cref="BridgeProtocol"/> 的名册里）。</para>
    public const int EngineReportedLongEdgePx = 1440;

    /// <summary>随包那颗引擎的字节闸门（<b>现读</b>：<c>MAX_ALLOWED_FILE_BYTES</c>＝<c>4*1024*1024</c>，
    /// 同档 <c>:323</c>；那颗 <c>dist/spike1.min.js</c> 里逐字 <c>Ine=4*1024*1024</c>，⛔ 任何 prop 出口）。
    /// 同样⛔ 是我们自己给的上限。</summary>
    /// <para><b>片60 补上它在成品里的那一手</b>（同一颗 bundle，正本 <c>bin/WI98/readings_60_bundle_r2_001740.txt</c>）：
    /// <c>,Vw=4*1024*1024</c> 声明在 offset 2167001，判据在 <c>initializeImage</c> 体里
    /// <c>if(E.size>Vw)throw new Error(b("errors.fileTooBig",{maxSize:...}))</c>，
    /// ⚠ <b>次序是先按 <see cref="EngineReportedLongEdgePx"/> 压、压完才判字节</b>（压失败只 <c>console.error</c>
    /// 之后拿原颗去过判据；<c>svg</c> 那一支整支⛔ 过这两道；同一颗 id 已在 <c>this.files</c> 里带 <c>dataURL</c> 时两道全跳）。
    /// 它跟 <see cref="SceneBytesCeilingBytes"/> ⛔ 是一回事：那颗管「一张图」且住在我们走不到的路上，
    /// 这颗自给的管「一发整档」。而<b>我们自己的载档那一路</b>（载入三步 → <c>addFiles</c> → <c>addMissingFiles</c>）
    /// ⛔ 经过这两道＝档里那颗图字节照原样进出（片13 那条斜率量的正是这条路），所以给 <c>ImageCeilingsMeasured</c>
    /// 对账时⛔ 要拿引擎那两句冒充宿主的代价。</para>
    public const long EngineReportedFileCeilingBytes = 4L * 1024 * 1024;

    /// <summary>
    /// 「我们自己给的那颗字节上限量过没有」。翻成 true 之后，本档里每一颗<b>自给</b>的尺寸上限都得带一句
    /// 「读数出自哪一份正本」，而那颗正本<b>真在盘上</b>（守它的是 <c>EverySelfGivenCeilingCitesAReadingThatExists</c>）。
    /// <para>翻起来那一笔＝片58：档体积 ↔ 那一拍按住 UI 线程多久，这条曲线在真机上量出来了
    /// （尺＝<c>SendMessageTimeoutW(WM_NULL)</c> 的往返＋宿主自己报的那一列 <c>tookMs</c>）：
    /// <b>改前</b>（停笔那一拍、写⛔ 离开 UI 线程）<c>bin/WI98/readings_58_r4_210541.txt</c>
    /// ＝ 67131 B→15.6 ms／2098752 B→41.2／8390208 B→100.5（同档三拍 115.7／100.5／99.8）；
    /// <b>改后</b>（关窗那一拍）<c>bin/WI98/readings_58z_ladder2_221131.txt</c>
    /// ＝ 67133 B→15.1／2098752 B→45.4／8390209 B→86.7／26216001 B→273.6 ms，
    /// 宿主那一拍 <c>tookMs</c> <b>182／305／631／1769</b>（四档逐字 <c>tid=2 onUi=True closing=True</c>）。
    /// ⚠ <b>后两档由成品复测为「拒」</b>（见下面 <c>SceneBytesCeilingBytes</c> 那一段那两份片59 正本）——
    /// 那一跑跑的是一颗闸门还⛔ 生效的 <c>StarMark.Core</c>（沙盒对账当时只比了 UI 那颗 dll），
    /// 那两列因此只读作「<b>如果放它进来要花多久</b>」而⛔「今天这条路真走过的代价」。</para>
    /// <para>⚠ 三处口径⛔ 许混着读：① 改前是<b>停笔</b>那一拍、改后是<b>关窗</b>那一拍，两列数⛔ 是同一拍；
    /// ② 改后那一拍按住界面的 86.7 ms（8 MiB）比改前那 100.5 只降了一截——因为留下来的主要是<b>解码</b>
    /// 与那颗 base64 字符串过桥，⛔ 是写盘（写盘已经不在 UI 线程上，见 <c>SceneBytesCeilingBytes</c> 那一段）；
    /// ③ <b>长边那一半今天有了源码读数</b>（片60，⛔ 屏幕读数）：引擎自报的 1440 与 4 MB 照旧只是<b>对账用的读数</b>
    /// （上面两颗 <c>EngineReported*</c> 那两段写着它们的 offset 与唯一消费点），而片60 现读到的是
    /// <b>那两道住在 <c>initializeImage</c> 里、那条路被我们自家 <c>tools:{image:false}</c> 整体关掉</b>
    /// ⇒ 界面上今天唯一管得住一张图的还是下面这颗 <c>SceneBytesCeilingBytes</c>；
    /// ⚠ 「粘贴一张图会不会撞上引擎那句 <c>Images are disabled.</c>」<b>⛔ 上过量</b>（真机那三臂被第 0 道核对拦下＝欠项）。</para>
    /// </summary>
    public const bool ImageCeilingsMeasured = true;

    /// <summary>
    /// 一发<b>进向</b>字节的上限（base64 解<b>出来</b>之后允许多大；片58＝P-241 的②件）。
    /// <para><b>这颗数⛔ 是我拍的，三条都在正本里</b>：梯子四档的代价＝
    /// <c>bin/WI98/readings_58z_ladder2_221131.txt</c>（按住界面 15.1／45.4／86.7／273.6 ms，
    /// 对宿主自己报的 <c>tookMs</c> 182／305／631／1769），那颗闸门真开火的两份＝
    /// <c>bin/WI98/readings_59_idle_234308.txt</c>（停笔那一拍逐字 <c>too-big b64=11187060 decoded~=8390295
    /// ceiling=8388608 kind=scene</c>）与 <c>bin/WI98/readings_58z_8MiB_rerun_234533.txt</c>
    /// （同一档走关窗那一拍：<c>scene.saved</c> 行数 0，盘上那颗档停在 890 颗字节＝上一份没被盖）。
    /// ① 8 MiB 那一档盘上真字节 <b>8390209</b>（＝8 MiB ＋ 1601 颗 JSON 骨架）在<b>那一跑</b>整发落地 <c>tookMs=631</c>
    /// ≈ <see cref="CloseFlushBudgetMs"/>（2000）的三分之一——余量留得住；
    /// ② 下一档 25 MiB 已经 <c>1769</c>＝预算的 88%，再往上就开始<b>丢用户那几笔</b>（那一拍等不到答案，
    /// 窗照样关，而那发⛔ 进盘）；
    /// ③ <b>解码那一半挪不走</b>：base64 过桥与 <c>Convert.FromBase64String</c> 照旧压在 UI 线程上
    /// （写盘已经离开它，见 <c>BoardWindow.QueueSceneWrite</c>），所以撞上限那一发必须<b>在解之前</b>拦下。
    /// <b>取 8 MiB＝量到的档位里最后一颗还留得住余量的，并且把它当成上界⛔ 当成下界</b>：
    /// 那颗 8390209 的正件<b>比这里高 1601 颗字节，片59 量到它真的被拒了</b>（上面那两份片59 正本）——被拒的是"量到过的那一档及其以上"，
    /// 放进来的每一发都⛔ 贵过我手里最贵的那颗读数，⛔ 反过来拿那颗贴着预算的 25 MiB 当上限。</para>
    /// <para>⚠ 两处口径：① <see cref="InboundDecodedBytesUpperBound"/> ⛔ 扫填充，边界上最多多算 2 颗字节
    /// （＝恰好 8 MiB 那一发被判超时），在这颗量级上是 0.00002% 的误拒，换掉它要的是"解之前就得判完"；
    /// ② <c>tookMs</c> 是"从交出去到落地"整拍的墙钟，最慢那一列是"UI 线程拒收工作多久"，两列都⛔ 等于"写盘花了多久"。
    /// 屏幕上说哪句话由宿主那一头按档位分流（scene＝「这一扇已停止保存」那一族，png／svg＝「没能存成」那一族）。
    /// ⚠ <b>欠项（片59 更新）</b>：① 改后<b>停笔</b>那一拍已用同一把尺重测（<c>readings_59_idle2M_234456.txt</c>：
    /// 2 MiB 那一发 <c>WM_NULL</c> 最慢 <b>3.7 ms</b>，痕 <c>tid=2 onUi=True closing=False</c>，对照改前失焦那一拍 41.2 ms）；
    /// ② <b>真贴几张图</b>会不会撞上这颗上限仍⛔ 摆过现场（这里全是 <c>customData.pad</c> 垫出来的无图板）——
    ///    片60 现读到那条<b>摆出现场</b>的路今天根本走不到：用户能贴图的三处入口（粘贴／拖放／工具条那颗）
    ///    全被桥里 <c>tools: { image: false }</c>（<c>board-bridge.js:207</c>）挡在 <c>isToolSupported("image")</c> 外，
    ///    而<b>发向</b>名册里唯一能把它翻回来的那颗 <c>feature.flags</c> 今天<b>一颗也没有发过</b>
    ///    （<c>BoardWindow.cs</c> 里 <c>PostOutbound</c> 现读五颗＝export.pick／theme.set／lang.set／board.load／autosave.arm，
    ///    名册第六颗⛔ 主人）⇒ 这一格今天的真形状是「带图的板只能来自外来档或手改的档」，⛔ 是「量到了但不卡」；
    /// ③ 被这颗闸门拒掉的那一发正撞<b>用户关窗</b>那一拍，片61 量到了（<c>readings_61_big_r3.txt</c>：
    ///    同一档 <c>WM_CLOSE</c> ⇒ 痕逐字 <c>boards.close.flush.window … landed=True refused=True same=False tookMs=577</c>，
    ///    那一发 <c>b64=11186948 decoded~=8390211</c> 被拦在解之前，<c>scene.saved</c> 一行也没有，而盘上那颗档
    ///    <b>890→890 字节</b>＝拒⛔ 等于清空；<c>boards.close.revive on=refused</c> 一行在场，且同一拍之后那颗画板窗
    ///    <b>仍在可见顶层窗名单里</b>＝那句「这一扇已停止保存」有扇在场的窗挂着，⛔ 贴在正被拆的窗上＝<b>账本 P-247 那一半</b>）。
    ///    同一拍的另一档（2 MiB，⛔ 超限）当那颗新列的<b>反空转正件</b>（<c>readings_61_small_r2.txt</c>：
    ///    <c>landed=True refused=False tookMs=152</c>＋<c>bytes=2098752</c> 真落了一发，而那颗窗⛔ 再出现在名单里＝照常关成）
    ///    ——只有 <c>True</c> 一种读法的一列⛔ 是读数。</para>
    /// </summary>
    public const long SceneBytesCeilingBytes = 8L * 1024 * 1024;

    /// <summary>导出那两档的<b>名单与默认</b>（片64＝P-242 落地腿）。
    /// <para>名单就是引擎自家那份 <c>cd=[1,2,3]</c>；那颗缩放若⛔ 递给它，引擎按
    /// <c>cd.includes(devicePixelRatio)?devicePixelRatio:1</c> 算＝<b>跟着那台机器的 DPR 走</b>，
    /// 于是同一块板在 100% 与 200% 两块屏上导出两张不一样大的图。所以这里给一颗写死的默认（1×）：
    /// 尺寸由<b>这一发挑的档</b>定，而不由那台显示器的缩放定。
    /// 上面那两截逐字与三处 offset（2166977／2168217／2169095）都在 <c>bin/WI98/readings_62_bundle_r2.txt</c>
    /// 那份正本里躺着（随包档 4878772 颗字节／sha 前 12 颗 <c>923fa221d0a4</c>）。</para></summary>
    public static readonly int[] ExportScales = { 1, 2, 3 };

    /// <summary>没挑过缩放时用它。⛔ 取"沿引擎那颗默认"——那正是 P-242 记下的那颗缺陷的形状。</summary>
    public const int ExportScaleDefault = 1;

    /// <summary>那颗背景开关的默认：引擎自家 <c>exportBackground:!0</c>（offset 2169095 现读）＝开着，这里与它对齐。</summary>
    public const bool ExportBackgroundDefault = true;

    /// <summary>这颗缩放在册吗。名单唯一的主人就是 <see cref="ExportScales"/>，⛔ 在别处再抄一遍那三颗数字。</summary>
    public static bool IsKnownExportScale(int scale) => System.Array.IndexOf(ExportScales, scale) >= 0;

    /// <summary>那颗缩放按钮按下去之后的<b>下一档</b>（走完名单回第一档）。
    /// <para>判据住在这里而⛔ 长在 UI 那一档：测试工程⛔ 能引用 <c>StarMark.UI</c>，那颗循环只有落到这里才量得到
    /// （片43 那颗 <c>PickReadableScene</c> 同一条口径）。<b>认不出来的当前值</b>（外部改坏、或加档位时漏改一处）
    /// 回第一档——⛔ 把一颗屏幕上点不出来的野值留在原地。</para></summary>
    public static int NextExportScale(int current)
    {
        var at = System.Array.IndexOf(ExportScales, current);
        return at < 0 || at + 1 >= ExportScales.Length ? ExportScales[0] : ExportScales[at + 1];
    }

    /// <summary>那一发 base64 交回的字符串<b>至多</b>解出多少字节（上界：⛔ 扫填充，因此最多多算 2 颗字节）。</summary>
    public static long InboundDecodedBytesUpperBound(int base64Length) => (long)(base64Length / 4) * 3;

    /// <summary>那一发进向字节超上限了吗——<b>解之前</b>问（「解出来才知道」＝那一手的付担已经压在 UI 线程上了）。</summary>
    public static bool InboundBytesOverCeiling(int base64Length)
        => InboundDecodedBytesUpperBound(base64Length) > SceneBytesCeilingBytes;

    /// <summary>一颗板的档后缀（一颗档一板；图以内嵌 base64 跟着走，§21.3 那颗裁决）。</summary>
    public const string SceneExtension = ".excalidraw";

    /// <summary>上一份的形状：只留最近一颗，⛔ 滚动多份（v1 §21.3 那句"另存一颗 <c>.bak</c>"）。</summary>
    public const string BackupSuffix = ".bak";

    /// <summary>停笔多久算"这一笔收了"（autosave 那一拍，毫秒）。v1 §21.3 定 1.5 s。</summary>
    public const int AutosaveIdleMs = 1500;

    /// <summary>
    /// 关窗那一拍<b>最多</b>向页面等这份快照（毫秒，片24）。
    /// <para><b>为什么必须有上限</b>：那一拍拦下的是用户亲手点下的"关闭"。没有上限＝页面卡住时那扇窗<b>永远关不掉</b>，
    /// 那比丢最后几笔坏得多（P-54 那条口径的反面：⛔ 把用户的退出键抵押给一颗网页）。</para>
    /// <para><b>为什么是这一颗数</b>：§18.6 那把尺现读的桥往返＝控制消息 p50 <b>0.4</b> ms／p95 <b>0.7</b> ms，
    /// 1 MB 载荷单程 <b>31</b> ms、8 MB <b>177</b> ms。页面那颗 serialize 加这一趟往返，两秒是量到的最坏那一档的十倍以上；
    /// 而它同时是"关掉一扇画板"的<b>可感上限</b>——那一拍里窗已经 Hide，用户等的是进程收尾，⛔ 是一段看不见的钟。</para>
    /// </summary>
    public const int CloseFlushBudgetMs = 2000;

    /// <summary>
    /// 关窗／退出那一拍在<b>开口问之前</b>先让出这一段（毫秒，片32）。
    /// <para><b>为什么要有这一手</b>：用户松手后立刻关窗（点 X／按 Alt+F4／直接退程序）时，那一下抬笔的输入事件
    /// 还在 <c>系统队列→宿主 UI 线程→WebView2 浏览器进程→渲染进程</c> 这条链上走着，而页面交回的是<b>它此刻的</b>场景。
    /// 于是"问"本身会拿到一份<b>还没有那一笔</b>的旧场景，而宿主那一侧<b>听不出新旧</b>：盘上那颗档的字节没变，
    /// 而 <c>landed=True</c> 的几种真法里"盘上本来就是它"与"把同件又写一遍"本来就长得一样
    /// （改前那两跑⛔ 有 <c>same=</c> 这一列分它俩＝那一列是本批补的）。
    /// 片31 那句"landed=True 单独⛔ 算验过"在这里有了第四张脸：<b>它连"页面回的是旧账"都听不出来</b>。</para>
    /// <para><b>门槛是量出来的</b>（正本 <c>readings_32_main_*</c>，两扇同开、各画一笔再各补一笔）：
    /// 抬笔后 <b>0～20 ms</b> 就关窗那一跑<b>丢最后画那一扇的那一笔</b>（两跑同形，<c>landed=True tookMs=13/15</c> 两颗都报好话）；
    /// 隔 <b>40 ms</b> 关＝两颗档各对上自己那两笔；80／150／250／300 ms 各一跑同样 2/2；
    /// 对照臂（⛔ 关任何窗，只等那一拍自己落盘）给足时间两笔都进盘＝⛔ 是"那一笔压根没画上"。</para>
    /// <para><b>为什么是 150</b>：门槛落在 20～40 ms 之间，取观测量上界的约四倍留余量；它是 <see cref="CloseFlushBudgetMs"/>
    /// 的十三分之一，付在"窗已经 Hide 之后"的那段看不见的钟里，⛔ 换用户一次等待。这一手是<b>把丢笔的概率压到量不到的档</b>，
    /// ⛔ 是"保证渲染进程收下了"——页面没有"我处理到第几笔"可问（宿主也⛔ 知道有几笔），所以⛔ 往上叠一层假承诺。</para>
    /// </summary>
    public const int CloseFlushGraceMs = 150;

    /// <summary>
    /// 盘上那份是不是<b>已经</b>等于这颗字节（v1 §21.4 那颗脏位＝宿主那一侧的字节对差；⛔ 另造计数器、⛔ 让 JS 递 bool）。
    /// <para><b>这颗判定住在 Core</b>：它决定的不是"要不要调用一次写"，而是"<b>要不要滚 <see cref="BackupSuffix"/>"</b>——
    /// 每滚一次，上一份<b>真正不同</b>的 <c>.bak</c> 就被顶成这一份的同件，退路当场少一条。
    /// 失焦那一拍最常撞的正是"一个字节都没改"，所以这一判据的代价是直接落在用户数据上的。</para>
    /// <para><c>lastWritten</c> 为 null＝这扇窗里宿主还没写过、也没<b>从主档</b>载过（新板，或档是从 <c>.bak</c> 捡回来的）：
    /// 那时候盘上的主档要么不存在、要么不是页面此刻的样子，必写。</para>
    /// </summary>
    public static bool DiskAlreadyHolds(byte[]? lastWritten, byte[] next)
        => lastWritten is not null && lastWritten.AsSpan().SequenceEqual(next);

    /// <summary>
    /// 滚<b>这一次</b> <see cref="BackupSuffix"/> 会不会白丢一条退路（片37；§21.4 那颗脏位缺的另一半）。
    /// <para><see cref="DiskAlreadyHolds"/> 比的是<b>宿主内存里</b>那份「我上次写出去的字节」，而盘上那颗主档由不得它：
    /// 两扇同开同一颗板（片32 量过那张形状）、外部同步工具改档、上一发写失败之后重试——这三档里内存那份都落后于盘。
    /// 于是照样会滚一发「其实与盘上同件」的写：<c>.bak</c> 被顶成与新主档同一份，
    /// 上面那句<b>「退路当场少一条」</b>说的正是这件事——当时只堵了内存那一路，盘上那一路照旧漏。</para>
    /// <para><c>onDisk</c> 为 null＝主档不存在<b>或读不到</b>：读不到就判不了，而这一发的写多半也成不了
    /// （同一颗文件被人攥着），⛔ 拿一颗读不到的盘去顶掉唯一那条「真正不同」的退路。
    /// 与来件逐颗相等＝滚完 <c>.bak</c> 与新主档同件＝那条退路等于没有 ⇒ ⛔ 滚。其余照旧滚（这才是「上一份」）。</para>
    /// <para><b>片38 补上的另一半（P-234）</b>：光问「同不同」还不够，还得问<b>盘上那份还认不认得出是一颗板档</b>——
    /// 一颗 0 字节／半颗（截在 JSON 对象中间）的主档与来件<b>当然</b>不同，于是照滚，
    /// 而这一滚把唯一那条<b>还能打开</b>的 <c>.bak</c> 顶成了垃圾：<c>TryLoad</c> 的退路读的正是它。
    /// 「退路当场少一条」这句话在同一发上第二次成立，片37 只堵了第一次那一半。
    /// ⚠ 那颗形状判据＝<see cref="LooksLikeSceneDoc"/>，它⛔ 解 JSON——这颗只问「首尾那两个括弧还在不在」。</para>
    /// <para>判据住 Core＝「要不要动用户数据」这件事⛔ 由 UI 再算一遍；存储那一档只负责把盘上那份读给它看。</para>
    /// </summary>
    public static bool BackupRollNeeded(byte[]? onDisk, byte[] incoming)
        => onDisk is { Length: > 0 } && LooksLikeSceneDoc(onDisk) && !onDisk.AsSpan().SequenceEqual(incoming);

    /// <summary>
    /// 那串字节<b>还认得出是一颗板档</b>吗（片38；只问形状，⛔ 解析内容）。
    /// <para><b>为什么只问首尾</b>：这颗判据要答的是「这串字节还认不认得出<b>是引擎自己写出去的那一颗档的形状</b>」——
    /// 滚那一头拿它判「拿它去顶掉唯一那条退路<b>划不划算</b>」（片38），载入那一头拿它判「这一份⛔ 配递给页面」
    /// （片43／P-238：主档还在却是 0 字节／半颗时，旧形状把「读出一颗垃圾」折成「这颗板不存在」，于是另起一颗新板）。
    /// 两头共用同一颗判据＝「什么算一颗档」只许一个出处；而⛔ 在这里跑解析器：那颗判据的代价是每拍都要读一遍盘，
    /// 「内容坏到引擎解不开」那一问归页面那一次 <c>board.loaded</c> 的计数（对不上就走写闸拒那一族，✅P-224／✅P-237）。</para>
    /// <para>空／null／只有一个括弧＝认不出。首尾之外的那点空白（<c>\n</c>／<c>\t</c>／<c>\r</c>／空格）跳过——
    /// 序列化器自己⛔ 在两头留白，留白⛔ 是坏形状的理由。</para>
    /// <para>⚠ <b>量不到的那一维</b>：两颗档拼成一串（首尾括弧都在）判得出「还是档」而判不出「只有一颗」——
    /// 要判那颗得真去配一遍括弧（＝每拍读盘时跑一次解析）。那颗门路只有外部拼接一种，而漏判的代价只是「那一发照旧滚」
    /// ＝<b>回到本批改前的行为</b>，⛔ 是比改前更坏的新行为。⇒ 这一维<b>明知而⛔ 堵</b>，记进账本那条边界。</para>
    /// </summary>
    public static bool LooksLikeSceneDoc(byte[]? bytes)
    {
        if (bytes is null or { Length: 0 }) return false;
        var first = 0;
        var last = bytes.Length - 1;
        while (first < last && IsPadding(bytes[first])) first++;
        while (last > first && IsPadding(bytes[last])) last--;
        return bytes[first] == StartOfDoc && bytes[last] == EndOfDoc;
    }

    /// <summary>
    /// 盘上那两颗档里，<b>哪一份配当这块板递出去</b>（片43＝P-238；纯函数，读盘那一手归存储）。
    /// <para>改前的形状是「主档<b>存在</b>就交主档」，于是「主档还在、却当⛔ 一份档」（0 字节／截在半路／被人塞了别的）
    /// 那一档里，调用方把读回来的空串折成"这颗板不存在"而<b>另起一颗新板</b>——盘上唯一那份还能打开的
    /// <c>.bak</c> 一次也没被问过（真机逐字＝<c>bin/WI91/readings_38b_174633.txt</c>：<c>via=no-scene</c>、
    /// 我摆那颗档名⛔ 出现在任何一发里、那颗退路 sha 一字未动）。✅P-234 保住的是「⛔ 把退路滚成垃圾」，
    /// 本颗保住的是「<b>退路读得到头</b>」：同一条线上两半，缺哪一半那颗退路都不值钱。</para>
    /// <para>主档优先是刻意的：退路按定义是<b>上一版</b>，一颗好主档⛔ 被自己的 <c>.bak</c> 顶掉
    /// （<paramref name="fromBackup"/> 为真时调用方会把脏位清空、第一拍把这一份补回主档位置，见 ✅P-223 那一条线）。</para>
    /// <para>两颗都认不出＝交回 <c>false</c>（这一颗板在盘上没有能打开的一份），⛔ 在这里猜"半颗档里也许还有东西"：
    /// 那等于把一份引擎解不开的字节递给页面，症状会从"这块板没了"变成"这块板打不开又⛔ 说为什么"。</para>
    /// </summary>
    public static bool PickReadableScene(byte[]? main, byte[]? backup, out byte[] bytes, out bool fromBackup)
    {
        if (LooksLikeSceneDoc(main))
        {
            bytes = main!;
            fromBackup = false;
            return true;
        }
        if (LooksLikeSceneDoc(backup))
        {
            bytes = backup!;
            fromBackup = true;
            return true;
        }
        bytes = Array.Empty<byte>();
        fromBackup = false;
        return false;
    }

    private const byte StartOfDoc = (byte)'{';
    private const byte EndOfDoc = (byte)'}';

    /// <summary>
    /// 那颗档<b>位置在盘上、却一颗字节也拿不出</b>（P-239）——别的程序攥着它，或自家那次 <c>File.Move</c> 正在改名的瞬间。
    /// <para>为什么单独问这一颗：存储那一只读盘的手把「没这颗档」与「有这颗档却读不动」都折成 <c>null</c>，
    /// 于是载入那一头把两者说成同一句话——「这块板没了」——再当场<b>另起一块新板</b>。用户看到的是空画布，
    /// 而盘上那块板一颗没动；他⛔ 知道是自家的板还是别人的板。这一颗把两件事分开：<paramref name="present"/> 且
    /// <paramref name="bytes"/> 为 null ⇒ 这不是"没有板"，是"这块板现在拿不出来"。</para>
    /// <para>那颗判定住 Core 而⛔ 住在存储里：存储那一手只管"读到什么"（读不到就是 null，它⛔ 猜原因），
    /// 而"两件事该分开说"是<b>措辞与出口</b>那一层的判据——放错层就没人量得到（测试工程⛔ 引用 <c>StarMark.UI</c>）。</para>
    /// <para>⚠ 它认的只有"拿不出字节"那一档：<b>档在、读得到、却当⛔ 是一颗档</b>（0 字节／截在半路）归
    /// <see cref="PickReadableScene"/>（✅P-238），那一档有自己的那句话，⛔ 混进这颗里（混了＝两句措辞共用一颗判据，
    /// 改一句必染另一句）。</para>
    /// </summary>
    public static bool SceneFileIsBlocked(bool present, byte[]? bytes) => present && bytes is null;

    /// <summary>
    /// 右上角那一行按下<b>图</b>那一发之后，最多等页面回这份字节（毫秒，片56＝P-244 的第三处）。
    /// <para><b>为什么这颗数必须存在</b>：那一发问出去时宿主闩上 <c>_exporting</c> 并把两颗入口点灰，而改前的形状里
    /// <b>放开它的只有那份字节真交回来</b>。页面那句"这一办发不出去"既进不了名字闸门、也⛔ 有主人，
    /// 于是按一次「导出 SVG」就把同一行两颗入口<b>永远</b>点不亮（只有重载或重开窗才醒）——
    /// 一颗⛔ 回音的问＝一颗没有出口的问（P-226 那条"按下去要有回音"的下游）。</para>
    /// <para><b>为什么是这一颗数</b>：两端都在同一份正本着（<c>bin/WI95/readings_55_171814.txt</c>）——
    /// 好的那一发第 12 行 <c>最后一按 0.10 秒落</c>（PNG 从按下到盘上出现 ≤0.10 s，那是我那一跑的<b>轮询粒度</b>，
    /// 真值只会更早）；坏的那一发第 29 行 <c>最后一按 8.00 秒落=空</c>（SVG 到探针上限也没交回一颗字节）。
    /// 五秒＝量到的那一档的<b>五十倍</b>，落在"合法的那一发远远跑不完"与"坏掉的那一发⛔ 必等到天黑"之间；
    /// 它同时大于 <see cref="CloseFlushBudgetMs"/>（图那一发要过渲染器与字体内联，比一次 serialize 贵）。</para>
    /// <para>⚠ 这颗数<b>⛔ 是"SVG 修好之后要多久"的读数</b>——那一格归片56 的真机腿，量到更慢就把这一颗抬上去并留正本。</para>
    /// </summary>
    public const int ExportAnswerBudgetMs = 5000;

    /// <summary>
    /// 载入撞见那颗闸门时，<b>宿主自己</b>先重试几回（含头一发）、隔多久。
    /// <para>为什么由宿主重试而⛔ 把"请稍后重试"写在屏幕上让用户按（P-54 定过的口径）：撞见的那两档门路
    /// ——别人正攥着那颗档、自家那次改名正在半路——按秒就退了，一发重试就能救回来；而让用户去按那颗按钮，
    /// 是把程序自己做得到的一件事转成他的操作步数。</para>
    /// <para>颗数与间隔写在 Core：⛔ 住在调用点，否则下一扇窗各抄一份"我觉得三回够了"（这一族的前例＝
    /// <see cref="ArmRetryBudget"/>、<see cref="CloseFlushBudgetMs"/> 都在 Core）。</para>
    /// </summary>
    public const int LoadRetryTries = 3;
    public const int LoadRetryDelayMs = 200;

    /// <summary>那颗档头尾允许的那四颗空白（⛔ 认全 <c>char.IsWhiteSpace</c>：那颗认 Unicode 空白，而这里要的只是序列化器会写的那四颗）。</summary>
    private static bool IsPadding(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';

    /// <summary>
    /// 主干名里那颗时刻的格式（<c>yyMMdd-HHmmss</c> 那一颗形状，本地时刻）。
    /// <para><b>写成 <c>yyMMdd-HHmmss</c> 而不是直觉的 <c>yymmdd</c></b>：.NET 的自定义格式里
    /// <c>mm</c> 恒为<b>分钟</b>、月份只有 <c>MM</c>——那颗形状要的是"年月日-时分秒"，
    /// 用 <c>yymmdd</c> 会把分钟抄进月位，形状照样是 6 颗数字、<see cref="IsSceneId"/> 照样绿，
    /// 只有按颗拆开来对才能发现（本仓那条"⛔ 拿同一个构造函数当期望"的旧课，这一批撞在自家格式串上）。</para>
    /// </summary>
    private const string StampFormat = "yyMMdd-HHmmss";

    /// <summary>落盘的四拍（§21.3）。UI 侧每一拍都调同一颗保存出口，⛔ 各写一份时机判断。</summary>
    public enum SaveBeat
    {
        /// <summary>停笔 <see cref="AutosaveIdleMs"/> 毫秒。</summary>
        Idle,
        /// <summary>那扇窗失去焦点。</summary>
        LosingFocus,
        /// <summary>渲染进程刚挂了（重载之前那份必须留住，否则＝白屏之后还丢图）。</summary>
        RenderProcessFailed,
        /// <summary>窗 <c>Closed</c>（退到子进程之前）。</summary>
        ClosingWindow,
    }

    /// <summary>
    /// 那一拍<b>问得到</b>页面要一份快照（片24；四拍里只有两拍问得到）。
    /// <para><see cref="SaveBeat.Idle"/> 本来就是<b>页面自己</b>发起的那一拍——宿主"问"它等于把话回头递给自己；
    /// <see cref="SaveBeat.RenderProcessFailed"/> 那一刻页面已经死了，发出去的东西没人接（§21.5 给这一档的活是<b>重载＋把见过那份递回去</b>，⛔ 索取）。</para>
    /// <para><b>这颗判定为什么住 Core</b>：写调用点的人会本能地"每拍都问一次"，而这一条恰好是问不到的那两拍——
    /// 它⛔ 改变盘上的字节，只决定"要不要开一次口"，所以错了不报错，只是白等或白问。</para>
    /// </summary>
    public static bool BeatCanAskForSnapshot(SaveBeat beat)
        => beat is SaveBeat.LosingFocus or SaveBeat.ClosingWindow;

    /// <summary>渲染／浏览器那一族进程失效之后，宿主这一侧该付<b>哪一手</b>恢复动作（§21.5「重载之后⛔ 许回到空板，也⛔ 许静默白屏」）。</summary>
    public enum FailureRecovery
    {
        /// <summary>只上报：那一档要么自己会重起来，要么根本动不到画板；<b>重载反而会把用户画的东西退成错误页</b>。</summary>
        ReportOnly,
        /// <summary>页面没了而<b>那颗控件还在</b>：重载那一扇页，<c>bridge.ready</c> 再来时照旧把见过那份递回去。</summary>
        ReloadPage,
        /// <summary><b>控件本身没了</b>（官方表逐字：The app has to recreate a new WebView to recover）：只能拆掉重建一颗，⛔ 对着死的控件 Navigate。</summary>
        RecreateWeb,
    }

    /// <summary>
    /// 那颗进程失效的档位分到哪一手（片24）。名字交进来而⛔ 交 SDK 那颗 enum：Core⛔ 认得 WebView2，
    /// 分了这一档，UI 侧就只剩一颗 <c>switch</c> 的三条腿（判据与接线各归其主）。
    /// <para>出处＝<c>CoreWebView2ProcessFailedKind</c> 官方表的<b>逐字</b>恢复口径：
    /// <c>BrowserProcessExited</c>「The app has to recreate a new WebView to recover from this failure」／
    /// <c>RenderProcessExited</c>「You can use the <c>Reload</c> method to try to recover」／
    /// <c>UtilityProcessExited</c>·<c>GpuProcessExited</c>·<c>SandboxHelperProcessExited</c>「does <b>not</b> need to handle recovery」／
    /// <c>FrameRenderProcessExited</c>「does not affect the top-level document, only a subset of the subframes」⇒⛔ 重载（重载＝毁掉整块板）／
    /// <c>RenderProcessUnresponsive</c>「event will continue to be raised every few seconds」⇒只上报，⛔ 把一颗忙但活着的渲染器退了。</para>
    /// <para><b>认不出来的一律退到 <see cref="FailureRecovery.ReportOnly"/></b>：那颗表将来多出一档时，默认动作必须
    /// "什么也⛔ 毁"，而⛔ 是"先重载再说"。</para>
    /// <para><b>本仓钉住的那颗运行时现读</b>（WebView2 <c>1.0.3719.77</c> 的
    /// <c>Microsoft.Web.WebView2.Core.xml</c>，按那颗 enum 的 <c>F:</c> 成员数出来的十颗）＝
    /// <c>BrowserProcessExited</c>／<c>RenderProcessExited</c>／<c>RenderProcessUnresponsive</c>／<c>FrameRenderProcessExited</c>／
    /// <c>UtilityProcessExited</c>／<c>SandboxHelperProcessExited</c>／<c>GpuProcessExited</c>／<c>PpapiPluginProcessExited</c>／
    /// <c>PpapiBrokerProcessExited</c>／<c>UnknownProcessExited</c>——十颗全在证人里逐颗点了名（名单⛔ 靠记忆，见 <c>BoardPolicyTests</c> 那一格）。</para>
    /// </summary>
    public static FailureRecovery RecoveryForFailure(string processFailedKindName)
        => processFailedKindName switch
        {
            "BrowserProcessExited" => FailureRecovery.RecreateWeb,
            "RenderProcessExited" => FailureRecovery.ReloadPage,
            _ => FailureRecovery.ReportOnly,
        };

    /// <summary>
    /// 那份场景字节里有几颗<b>会被写回盘上的</b>元素（<b>认不出＝null，⛔ 替它猜一颗 0</b>）。
    /// <para><b>这颗数的口径就是本格的全部意义</b>：它⛔ 是"档里数组的长度"，而是"页面现在交得出几颗"——
    /// 页面那颗序列化器交的是 <c>API.getSceneElements()</c>，现读 bundle＝<c>scene.getNonDeletedElements()</c>
    /// （<c>isDeleted</c> 那颗被它滤掉），所以这里也跳掉 <c>isDeleted:true</c>。三颗比数的地方必须同吃一碗：
    /// 片26 第一版拿"数组整颗长度"当 <paramref name="canvas"/> 那一维的对照物（页面递的是 <c>getSceneElementsIncludingDeleted</c>），
    /// 崩后重载那一趟的读数三颗全等（want=4／loaded=4／canvas=4）而盘上仍被盖成 0 元素——那颗闸门比的是两颗
    /// ⛔ 同口径的数，挡了个⛔ 存在的形状（真读数与它的下一跑对照见 §二十一 片26 那一块）。</para>
    /// <para><b>为什么住 Core</b>：这颗数⛔ 只是诊断，它是开闸的<b>证据</b>（<see cref="ShouldArmAfterLoad"/>），
    /// 而它管着"要不要动那块板"。一颗当判据用的数⛔ 许住在解它的那一档里。</para>
    /// <para>认不出的形状一律 null：档不是 JSON、根不是对象、没有 <c>elements</c> 这一颗、那一颗不是数组。
    /// 把这四档都折成 0＝"这份档是空的"，而它真正的意思是"我读不懂"——按 <see cref="ShouldArmAfterLoad"/>
    /// 的口径，读不懂就⛔ 开闸。</para>
    /// </summary>
    public static int? CountWritableElements(byte[]? utf8Json)
    {
        if (utf8Json is not { Length: > 0 }) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(utf8Json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("elements", out var e)) return null;
            if (e.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            var n = 0;
            foreach (var el in e.EnumerateArray())
            {
                // 与那颗序列化器同口径：被标了删的进不了档，也就不该算进"画布上该有几颗"
                if (el.ValueKind == System.Text.Json.JsonValueKind.Object
                    && el.TryGetProperty("isDeleted", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.True)
                    continue;
                n++;
            }
            return n;
        }
        catch (Exception) { return null; }   // 半截档／坏编码：这一颗读不懂，交 null（⛔ 当"空的"）
    }

    /// <summary>
    /// 那颗"另存一份到别处"该交哪一份：手里那份（<c>loaded</c>＝宿主见过、也是盘上主档那一份），
    /// 还是写闸拒掉的那一份（<c>refused</c>＝用户此刻屏幕上那块）。
    /// <para><b>为什么会有这一问</b>：片33 两臂成对量到的——同一双腿在画布上拖一道，闸开那一臂把主档推成
    /// 3 颗可写元素（2809 字节），闸关那一臂里那一笔只走到 <c>boards.scene.write.refused notArmed bytes=2810</c>
    /// 就被退回去，而那颗按钮当时交出去的是 <b>2 颗／858 字节</b>＝载入那一份，屏幕上却说"已另存到 &lt;路径&gt;"。
    /// 少的正是用户刚画上去的那一笔。</para>
    /// <para><b>为什么⛔ 无条件要那一颗</b>：它是闸门判定"这一趟页面⛔ 回到了宿主给它的那块板"之后才交回的字节，
    /// P-223 量到的那颗形状（2608 字节／2 元素→241 字节／0 元素）就是它。所以这一颗只有一种资格：
    /// <b>数得出颗数、那颗数⛔ 是零、而且⛔ 比手里那份少</b>——空画布与读不懂的形状永远输，
    /// "同数或更多"才赢（同数时也交屏幕上那一份：那是用户看得见的那一块）。</para>
    /// <para>交出去也⛔ 动主档：这一份只写用户挑的那颗新档（<see cref="SceneFileName"/> 那条路⛔ 走它）。</para>
    /// </summary>
    public static bool ScreenSceneIsWorthSaving(byte[]? loaded, byte[]? refused)
    {
        var screen = CountWritableElements(refused);
        if (screen is null or 0) return false;
        return screen.Value >= (CountWritableElements(loaded) ?? 0);
    }

    /// <summary>
    /// 载档那一拍页面报回的两颗数，够不够宿主开那道闸（片26 给 P-223 的修法）。
    /// <para><b>要挡的现场是真机量到的那一颗</b>：崩溃重载之后 <c>loadFromBlob</c> 的 promise 解掉了、页面照实回话，
    /// 而画布交不出可写的那几颗——盘上那块 4 元素／4936 字节的板，8 s 内被盖成 0 元素／241 字节（<c>.bak</c> 只剩一份）。</para>
    /// <para><b>为什么"promise 解掉了"⛔ 是证据</b>：那句话报的是"我这颗解码跑完了"，
    /// 而闸门要的是"画布上现在有宿主递给它的那块板"。所以这里问三颗数：宿主递进去的可写颗数 <paramref name="want"/>、
    /// 页面从档里数到的 <paramref name="loaded"/>、页面<b>当场从画布上数回来的</b> <paramref name="canvas"/>。
    /// <b>三颗必须同口径</b>（都＝那颗序列化器交回的那一堆），否则比的是两颗不同的事。
    /// 三颗全等才开闸（⛔ "对不上就先信页面"——猜错的代价是拿空板盖掉那块好档）。</para>
    /// <para><b>任一颗认不出（null）＝⛔ 开闸</b>：那是"读不懂"，而读不懂时唯一安全的动作是不写。</para>
    /// </summary>
    public static bool ShouldArmAfterLoad(int? want, int? loaded, int? canvas)
        => want is int w && loaded == w && canvas == w;

    /// <summary>
    /// 开闸被拒之后，还许<b>再递几次档</b>（片27 给 P-224 的那一次自愈）。
    /// <para>为什么只有 1 颗：拒的那一格通常是"载档那一拍没落地"这一类<b>一次性的时序</b>，再递一次就过得去；
    /// 而一颗真坏掉的运行时多递几次只是把同一块空画布多刷几遍，代价落在用户的眼上。额度用完还是对不上，
    /// 程序手上就⛔ 剩"再来一次"了——那时唯一诚实的动作是<b>把这一扇不再落盘说出来，并给一颗出口</b>（⛔ "重启一次看看"）。</para>
    /// </summary>
    public const int ArmRetryBudget = 1;

    /// <summary>这一趟已经拒过 <paramref name="tries"/> 次，还许不许再递一次档（认不出来的颗数＝⛔ 再递，同"读不懂就不写"那条口径）。</summary>
    public static bool ShouldRetryArm(int tries) => tries >= 0 && tries < ArmRetryBudget;

    /// <summary>那颗"另存一份到别处"问完系统之后，宿主手里剩下的三种结果（片29·P-226）。</summary>
    public enum CopyPick
    {
        /// <summary>用户挑了一颗路径：照他挑的那颗写。</summary>
        Chosen,
        /// <summary>用户在那颗框里选了取消／把那颗框关了：<b>什么都不写</b>——他没要一份副本，替他写一颗就是越权。</summary>
        Declined,
        /// <summary>那颗框起不来（提权跨完整性级别只是其中一种成因）：退到导出目录，⛔ 把这事推回给用户（P-54 口径）。</summary>
        Unavailable,
    }

    /// <summary>
    /// 把"那颗框的结果"分成三档。<b>交回 <c>null</c> 有两种成因而⛔ 一种脸</b>：用户取消／宿主起不了框都长同一颗样，
    /// 而这一侧唯一分得开的信号是<b>它有没有抛</b>：抛了＝<see cref="CopyPick.Unavailable"/>，⛔ 抛而没路径＝<see cref="CopyPick.Declined"/>。
    /// <para>为什么这一刀要落在纯函数里（片28 那腿的真机读数才把它逼出来）：取消那一发在盘上写出了 858 字节的一份副本，
    /// 屏幕上还念了一句"已另存到 …"——两种 <c>null</c> 走同一条兜底路＝用户点"取消"却得到一颗他没要的档＋一句与事实不符的话。</para>
    /// </summary>
    public static CopyPick ClassifyCopyPick(bool pickerThrew, string? pickedPath)
        => pickerThrew ? CopyPick.Unavailable : pickedPath is not null ? CopyPick.Chosen : CopyPick.Declined;

    /// <summary>这一档结果许不许往盘上写一份（⛔ 挑的＝⛔ 写；那句"已另存到 …"只许跟着真写了的那一次说）。</summary>
    public static bool ShouldWriteCopy(CopyPick pick) => pick != CopyPick.Declined;

    /// <summary>
    /// 一扇画板窗在它那一生里最多<b>重建</b>几次控件（片24）。
    /// <para>为什么要有这一颗数：那颗运行时真坏掉的机器上，"重建→立刻又死"是一条<b>不需要谁帮忙就会一直跑</b>的循环，
    /// 每一圈还往日志里写一行——用户看到的是那扇窗既⛔ 恢复也⛔ 关掉。三圈的口径＝"给他一次改错的机会，再加两次"，
    /// 超过就没了一颗自愈能修的东西，剩下的是日志里那句 <c>boards.recover.givenup</c>。</para>
    /// </summary>
    public const int MaxRecreatePerWindow = 3;

    /// <summary>一颗场景是怎么进到画板里的——它决定<b>撤销栈</b>那道门（§21.4）。</summary>
    public enum SceneOrigin
    {
        /// <summary>用户在画板上画／改。</summary>
        UserEdit,
        /// <summary>宿主从档里载进来（首次打开、重载）。</summary>
        SceneLoad,
        /// <summary>宿主把字节回写进画板（例如把那颗 <c>files</c> 补齐、把敏感项摘掉）。</summary>
        BridgeWriteBack,
    }

    /// <summary>那颗 <c>captureUpdate</c> 的三档逐字名（包里 <c>CaptureUpdateAction</c> 的键名）。</summary>
    public static class Capture
    {
        /// <summary>立刻进撤销栈（用户动作）。</summary>
        public const string Immediately = "IMMEDIATELY";

        /// <summary>永远⛔ 进撤销栈（载档与桥回写）。</summary>
        public const string Never = "NEVER";

        /// <summary>那颗<b>包自己的默认</b>（<c>@default CaptureUpdateAction.EVENTUALLY</c> 逐字）。
        /// 记在这里是为了让"⛔ 设就是 EVENTUALLY"这件事看得见：载档如果偷懒不传，图就被塞进撤销栈，
        /// 用户一次 Ctrl+Z 就把整块板退成空白。</summary>
        public const string Eventualy = "EVENTUALLY";
    }

    /// <summary>
    /// 那颗来源该用什么 <c>captureUpdate</c>。裁决（v1 §21.4）：<b>用户动作＝IMMEDIATELY，载档与桥回写＝NEVER</b>；
    /// <b>⛔ 许把后两档回成那颗包默认的 EVENTUALLY</b>（<c>BoardPolicyTests.LoadsAndWriteBacksNeverEnterTheUndoStack</c>
    /// 钉的就是这一条）。
    /// </summary>
    public static string CaptureFor(SceneOrigin origin) => origin switch
    {
        SceneOrigin.UserEdit => Capture.Immediately,
        SceneOrigin.SceneLoad => Capture.Never,
        SceneOrigin.BridgeWriteBack => Capture.Never,
        _ => Capture.Never,
    };

    /// <summary>
    /// 生成一颗稳定 id：<c>yyMMdd-HHmmss-4hex</c>（文件名即 id，§12.2 那条口径）。
    /// <para>随机段由调用方给（UI 侧用 <c>RandomNumberGenerator</c>），本函数⛔ 自己取熵——
    /// 它是纯函数才能被证人逐字钉住，也才敢在迁移／对账里重算。</para>
    /// </summary>
    /// <param name="at">建板那一刻（本地时刻）。</param>
    /// <param name="rand0toFFFF">0–65535；越界＝抛（这是调用方的编程错，⛔ 静默取模——取模会让那 4 位变 3 位分布）。</param>
    public static string NewSceneId(DateTimeOffset at, int rand0toFFFF)
    {
        if (rand0toFFFF < 0 || rand0toFFFF > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(rand0toFFFF), "那颗随机段要落在 0–65535");
        return FormatStamp(at)
               + "-" + rand0toFFFF.ToString("x4", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 那颗时刻的<b>主干形状</b>（<see cref="StampFormat"/>）——NewSceneId 与导出兜底名共用同一颗主人；
    /// UI 层⛔ 自己再写一份格式串（DisplayDateFormatGate 的射程就是「UI 里⛔ 有格式字面量」）。
    /// </summary>
    public static string FormatStamp(DateTimeOffset at)
        => at.ToLocalTime().ToString(StampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// 那颗 id 的形状（也是"档名是不是我给的"那道判据——⛔ 拿标题当身份）。
    /// <para>只判<b>形状</b>：月份 99 这种"看着不对"的串照样通过，因为它只能来自更早一颗真的 <c>DateTimeOffset</c>
    /// 或被损坏的档名，两种都由对账那一族处理，⛔ 在这里补历法判定（那是第二份判据）。</para>
    /// <para><b>为什么逐颗字符判而不是 <c>int.TryParse</c></b>：写法先是 <c>TryParse(… NumberStyles.HexNumber …)</c>，
    /// 而那颗样式名义上含 <c>AllowLeadingSign</c> 与前后空白——本批实测两格（<c>…--123</c> 与 <c>…- ab</c>）
    /// 在 .NET 9 上确实被拒，但那"<b>恰好</b>拒"是解析器的口径，⛔ 是我的判据。
    /// 身份这件事的形状由本类自己说完：六位数字、连字符、六位数字、连字符、四颗十六进制，一颗不多不少。</para>
    /// </summary>
    public static bool IsSceneId(string? id)
        => id is { Length: 18 }
           && AllDigits(id, 0, 6) && id[6] == '-'
           && AllDigits(id, 7, 6) && id[13] == '-'
           && AllHex(id, 14, 4);

    private static bool AllDigits(string s, int at, int count)
    {
        for (var i = at; i < at + count; i++)
            if (!char.IsAsciiDigit(s[i])) return false;
        return true;
    }

    private static bool AllHex(string s, int at, int count)
    {
        for (var i = at; i < at + count; i++)
            if (!Uri.IsHexDigit(s[i])) return false;
        return true;
    }

    /// <summary>导出产物与救回来的副本那颗目录的<b>名字</b>（一片档名，⛔ 一句上屏话）。</summary>
    public const string ExportsDirectoryName = "StarMark 画板";

    /// <summary>
    /// 用户<b>产物</b>（导出的字节、那颗"另存一份到别处"的兜底副本）落进哪一颗目录——P-219 那颗裁决的落地腿。
    /// <para>两条规则挤在一颗判据里：<b>①平时进「图片」那一族</b>，与同仓那颗截图目录（<c>图片\StarMark 截图</c>）
    /// 同一个主人。理由⛔ 是"看起来整齐"，而是本仓定性过多次的那句话：<b>用户的产物必须落在他找得到的地方，
    /// 取不到就是程序的事</b>（P-53／P-54 那一句的两半）。片23 那一版把它切成 <c>板档目录\exports</c>——
    /// 那颗"根只许一位主人"的正解顺手把<b>产物</b>与<b>程序内部档</b>排到了同一棵树下，而用户在图片目录里翻不到自己刚救回来的那一份。
    /// <b>②那颗根被环境变量改道时跟着改道</b>（沙盒取证、单测改道），⛔ 往用户真目录里写一颗——
    /// 批次 SS 撞过的正是"抄本⛔ 认改道"那一族。</para>
    /// <para><b>②排在①之前</b>：那棵根一旦改道，"用户找得到的地方"就是那一棵；这时候还去拼图片目录＝把取证写进真档。
    /// <paramref name="picturesRoot"/> 与 <paramref name="redirectedDataRoot"/> 都由宿主取：Core⛔ 认特殊目录，
    /// 正如它⛔ 认 <c>%APPDATA%</c>，这里只裁「拿哪一棵、怎么拼」。</para>
    /// <para>那颗目录名<b>⛔ 跟着界面语言变</b>（所以它进本档而⛔ 进表）：一句上屏话换三种语言＝三颗目录，
    /// 用户反而一颗都找不到；名字在这里是<b>身份</b>，⛔ 措辞。同仓那颗截图目录也是这一个口径。</para>
    /// </summary>
    public static string ExportsFolder(string picturesRoot, string? redirectedDataRoot)
        => System.IO.Path.Combine(redirectedDataRoot ?? picturesRoot, ExportsDirectoryName);

    /// <summary>一颗板的档名（<c>&lt;id&gt;.excalidraw</c>）。</summary>
    public static string SceneFileName(string id) => Require(id) + SceneExtension;

    /// <summary>上一份的档名（<c>&lt;id&gt;.excalidraw.bak</c>）。</summary>
    public static string BackupFileName(string id) => SceneFileName(id) + BackupSuffix;

    /// <summary>那颗档名是不是一颗板档（对账三分类的"板在／档在而板缺"靠它认；<c>.bak</c> ⛔ 算）。</summary>
    public static bool IsSceneFileName(string? fileName)
        => fileName is not null
           && fileName.EndsWith(SceneExtension, StringComparison.OrdinalIgnoreCase)
           && !fileName.EndsWith(SceneExtension + BackupSuffix, StringComparison.OrdinalIgnoreCase)
           && IsSceneId(System.IO.Path.GetFileNameWithoutExtension(fileName));

    /// <summary>那颗上一份档名是不是一颗板档的备份。</summary>
    public static bool IsBackupFileName(string? fileName)
        => fileName is not null
           && fileName.EndsWith(SceneExtension + BackupSuffix, StringComparison.OrdinalIgnoreCase)
           && IsSceneId(fileName[..^(SceneExtension.Length + BackupSuffix.Length)]);

    private static string Require(string id)
    {
        if (!IsSceneId(id))
            throw new ArgumentException("那颗 id 形状⛔ 对：" + id, nameof(id));
        return id;
    }
}
