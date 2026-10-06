#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StarMark.Abstractions.I18n;

/// <summary>
/// 界面文案的唯一出处（批次 WI-1）：一颗键一行，每一语言一列。
/// <para><b>为什么是这里、为什么不走 <c>.resw</c>＋<c>x:Uid</c>＋卫星程序集</b>：
/// ① 本仓的 <c>dotnet publish</c> 已知会漏东西（#237 漏的是 XAML 产物），而"漏一份语言资源"的表现不是崩，
///    是<b>某个语言界面上半中半英</b>——比崩难发现得多；表编进 dll 就没有那条路。
/// ② 切换要当场生效（P-54 的口径下"请重启"算缺陷），而 <c>x:Uid</c> 是<b>解析期</b>取值，换语言必然要整棵重解析。
/// ③ Core／Abstractions 里长出来的措辞（更新的结局那句话）将来也要进表，那一层看不见 WinUI 的资源索引。</para>
/// <para><b>列的规矩：只有整列齐了才加一列</b>（用户裁的"先只做中＋英，日文随后"在批次 WI-3 兑现）——
/// 加一列空的，就等于让语言选择器出现一颗选了之后仍然满屏中文的选项。
/// 要进新语言：先把整列填满，再动 <see cref="AppLanguages.Pickable"/>，顺序不能反。
/// 逐行核对由 <c>bin/WI3/apply_ja_column.py verify</c> 做（四列齐、三列占位符同集合、
/// 不许日文列逐字抄中文列——除非那颗键在 <c>SAME_AS_ZH</c> 里点名登记过理由）。</para>
/// <para>占位符只用位置式 <c>{0}</c>／<c>{1}</c>，各语言的占位符<b>集合必须相同</b>——
/// 这一条由 <c>I18nGateTests</c> 逐行比对钉住：换语序就得换编号，那正是"数字挂错句子"的来源（#234 那一族）。</para>
/// </summary>
public static class UiStrings
{
    /// <summary>一行＝一颗键在各语言里的写法。三列都是<b>必填</b>：编译器不允许空着，
    /// 于是"漏了一门的翻译"从一次线上发现变成一个编不过的错。
    /// <para>⚠ 这也意味着<b>新进表的行必须一次带齐三列</b>——这正是想要的效果：
    /// 想先挂中文再"回头补日文"的那一步，在这里过不去（P-168 ⑤ 作废的口径）。</para></summary>
    public sealed record Entry(string Key, string Zh, string En, string Ja);

    /// <summary>表。<b>顺序无意义</b>（不参与渲染），但同一分组的键写在一起：改动时一眼看得见漏了哪一门。</summary>
    public static IReadOnlyList<Entry> All { get; } = new[]
    {
        // ===== 顶栏那排页签（导航）=====
        new Entry("nav.tree", "文件夹", "Folders", "フォルダー"),
        new Entry("nav.tags", "标签", "Tags", "タグ"),
        new Entry("nav.activity", "动态", "Activity", "アクティビティ"),
        new Entry("nav.hidden", "隐藏", "Hidden", "非表示"),
        new Entry("nav.snapshot", "快照", "Snapshots", "スナップショット"),
        new Entry("nav.clipboard", "剪贴板", "Clipboard", "クリップボード"),
        new Entry("nav.trending", "热榜", "Trending", "トレンド"),
        new Entry("nav.rss", "RSS", "RSS", "RSS"),

        // ===== 顶栏按钮与提示 =====
        new Entry("top.sync", "同步", "Sync", "同期"),
        new Entry("top.syncCancel", "取消同步", "Cancel sync", "同期をキャンセル"),
        // 顶栏那一行状态（批次 WI-2m）：它平时写的是**程序当场算出来的话**，所以每一句都得住键，
        // 否则换语言那一刻要么贴着旧语言、要么被静态键盖掉正在跑的那件事（坑表 #276／#295）。
        new Entry("top.indexReady", "索引就绪", "Index ready", "索引準備完了"),
        new Entry("top.statusCancelling", "正在取消同步…", "Cancelling the sync…", "同期をキャンセル中…"),
        new Entry("top.statusSyncing", "同步中...", "Syncing...", "同期中..."),
        new Entry("top.statusFailed", "同步失败: {0}", "Sync failed: {0}", "同期失敗: {0}"),
        new Entry("top.syncDone", "同步完成: {0} 条 · {1}ms", "Sync done: {0} items · {1}ms", "同期完了: {0} 件 · {1}ms"),
        new Entry("top.syncPartial", "同步部分失败: 成功 {0} 条 · 失败 {1} 源 · {2}ms",
            "Sync partially failed: {0} items ok · {1} sources failed · {2}ms",
            "同期は一部失敗：成功 {0} 件 · 失敗 {1} 源 · {2}ms"),
        new Entry("top.syncDoneTitle", "索引同步完成", "Index sync finished", "索引の同期が完了しました"),
        new Entry("top.syncFailedTitle", "同步失败", "Sync failed", "同期に失敗"),
        new Entry("top.syncCancelledTitle", "已取消同步", "Sync cancelled", "同期をキャンセルしました"),
        new Entry("top.syncCancelledBody", "已取消同步：已拉完的源已入库，未拉完的下次同步继续补齐。",
            "Sync cancelled: the sources already pulled are stored; the rest come with the next sync.",
            "同期をキャンセルしました：取得済みの源は保存済み、未取得の源は次回以降の同期で補います。"),
        new Entry("top.widgetsTip", "桌面组件", "Desktop widgets", "デスクトップウィジェット"),
        new Entry("top.guideTip", "这一页怎么用", "How this page works", "このページの使い方"),
        new Entry("top.showHidden", "显示隐藏", "Show hidden", "非表示を表示"),

        // ===== 搜索框与工具栏 =====
        new Entry("search.placeholder",
            "搜索本地文件 / Star / 书签… 支持 ext: size: dm: folder:  (↑↓ 选择 · Enter 打开 · Ctrl+Enter 位置)",
            "Search local files / Stars / bookmarks… supports ext: size: dm: folder:  (↑↓ select · Enter open · Ctrl+Enter location)", "ローカルファイル / Star / ブックマークを検索… ext: size: dm: folder: に対応（↑↓ 選択 · Enter 開く · Ctrl+Enter 場所）"),
        new Entry("sort.recent", "最近", "Recent", "最近"),
        new Entry("sort.starred", "Star 最近推送", "Star · latest push", "Star · 最新の push 順"),
        // 这一句是「档名说的排序键」与「数据里真正的排序键」之间唯一的现场解释（P-6），
        // 所以两列都必须说出 push：只翻档名、把这句留在中文，英文界面就只剩一个看不懂的词。
        new Entry("sort.starredTip", "按仓库最后一次 push 排（不是“我点 Star 的时间”——那个值 API 默认不回传）",
            "Sorted by the repo's last push — not when you starred it (the API doesn't return that by default)", "リポジトリ最後の push 順（「Star を押した時刻」ではありません——この値は API が既定で返しません）"),
        new Entry("sort.collected", "最近收藏", "Recently collected", "最近ブックマーク"),
        new Entry("sort.stars", "Stars 数", "Star count", "Stars 数"),
        new Entry("sort.name", "名称", "Name", "名前"),
        new Entry("source.all", "全部", "All", "すべて"),
        new Entry("source.allTip", "全部来源", "All sources", "すべてのソース"),
        new Entry("source.starTip", "Star", "Star", "Star"),
        new Entry("source.bookmarkTip", "收藏夹", "Bookmarks", "ブックマーク"),
        new Entry("source.fileTip", "本地文件（需先在设置开启本地磁盘搜索）", "Local files (turn on local disk search in Settings first)", "ローカルファイル（先に設定でローカルディスク検索を有効にする必要があります）"),

        // ===== 「类型筛选」浮层 =====
        new Entry("kind.button", "类型筛选", "Type filter", "種類で絞り込み"),
        new Entry("kind.buttonTip", "按文件类型筛选本地文件；不输关键词也能浏览该类型的全部文件",
            "Filter local files by type; browse every file of a type without typing a keyword", "ローカルファイルを種類別で絞り込みます。キーワードを入力しなくてもその種類の全ファイルを閲覧できます"),
        new Entry("kind.fileTypes", "文件类型", "File type", "ファイルの種類"),
        new Entry("kind.folder", "文件夹", "Folder", "フォルダー"),
        new Entry("kind.document", "文档", "Document", "ドキュメント"),
        new Entry("kind.picture", "图片", "Picture", "画像"),
        new Entry("kind.music", "音乐", "Music", "音楽"),
        new Entry("kind.video", "视频", "Video", "動画"),
        new Entry("kind.archive", "压缩包", "Archive", "アーカイブ"),
        new Entry("kind.conditions", "条件", "Conditions", "条件"),
        // 表里的文本是<b>最终显示的字</b>（不过 XAML 解析器）⇒ 尖括号写原文，不写 `&gt;`——
        // 从表里取出的 `&gt;` 会照原样显示成四个字符。
        new Entry("kind.large", "大文件（>100 MB）", "Large files (>100 MB)", "大きいファイル（>100 MB）"),
        new Entry("kind.recent", "最近改动（7 天内）", "Modified in the last 7 days", "最近変更（7 日以内）"),
        new Entry("kind.noteScope", "仅作用于本地文件；「文件夹」与类型互斥，勾选后只列文件夹。",
            "Applies to local files only; “Folder” is exclusive with the types — tick it to list folders only.", "ローカルファイルのみに適用。「フォルダー」と種類は同時に選ばれません。選ぶとフォルダーだけ表示します。"),
        new Entry("kind.noteSyntax", "也可直接在搜索框键入语法：ext:、size:、dm:、folder:",
            "You can also type the syntax straight into the search box: ext:, size:, dm:, folder:", "検索ボックスに構文を直接入力もできます：ext:、size:、dm:、folder:"),
        new Entry("kind.clear", "清除类型筛选", "Clear type filter", "種類の絞り込みをクリア"),

        // 这里的"语言"是 Star 的编程语言，不是界面语言——英文必须说出"仓库语言"，
        // 否则同一屏上两颗都叫 Language 的下拉，第二颗看起来像坏了。
        new Entry("searchLang.placeholder", "语言：全部", "Repo language: all", "言語：すべて"),
        new Entry("searchLang.tip", "按 Star 的编程语言筛选（对本地文件无效）",
            "Filter Stars by programming language (has no effect on local files)", "Star のプログラミング言語で絞り込みます（ローカルファイルには効きません）"),

        // ===== 首启那份引导的两个按键（批次 VC-3）=====
        new Entry("guide.primary", "看完整使用说明", "Read the full guide", "使い方説明を最後まで見る"),
        new Entry("guide.secondary", "知道了", "Got it", "わかりました"),
        // 首启向导的三步车（批次 WJ-1）。计数那句两列必须给同一组编号 {0}{1}——
        // 少给一个号，英文那版会当场抛 FormatException（Locale.Fmt 刻意不 catch，就是要它在这里红）。
        new Entry("guide.next", "下一步", "Next", "次へ"),
        new Entry("guide.prev", "上一步", "Back", "前へ"),
        new Entry("guide.jump", "带我去这一页", "Take me there", "このページへ連れていく"),
        new Entry("guide.step", "第 {0} / {1} 步", "Step {0} of {1}", "{0} / {1} ステップ"),

        // ===== 设置页那八栏的标题（批次 WI-2 第一批）=====
        // 键的后缀＝页签身份（`SettingsTabIds`），不是屏幕上那几个字：跳转按身份走，标题跟着语言走。
        // 顺序即页签从左到右的顺序，闸门拿这张表与 XAML 的 Tag 两侧对账。
        new Entry("settings.tab.general", "常规", "General", "一般"),
        new Entry("settings.tab.guide", "使用说明", "Guide", "使い方説明"),
        new Entry("settings.tab.ai", "AI 增强", "AI", "AI 強化"),
        new Entry("settings.tab.widgets", "桌面工具（组件）", "Desktop tools", "デスクトップツール（ウィジェット）"),
        new Entry("settings.tab.extend", "拓展功能", "Extensions", "拡張機能"),
        new Entry("settings.tab.hotkeys", "快捷键", "Hotkeys", "ショートカットキー"),
        new Entry("settings.tab.data", "数据", "Data", "データ"),
        new Entry("settings.tab.health", "健康与诊断", "Health & diagnostics", "健全性と診断"),

        // ===== 设置页自己的页眉（标题 + 右上角那颗返回）=====
        // 它们与八栏标题同一层级（页面外壳，不是某一栏的正文），所以同一批改掉；
        // 覆盘照片拍到「设置／返回」孤零零留在中文、而承诺句没点它，就是这一格要补的原因。
        new Entry("settings.pageTitle", "设置", "Settings", "設定"),
        new Entry("settings.back", "返回", "Back", "戻る"),

        // ===== 设置页「常规」里那颗界面语言 =====
        new Entry("settings.languageTitle", "界面语言", "Interface language", "表示言語"),
        new Entry("settings.languageFollowSystem", "跟随系统", "Follow system", "システムに追随"),
        // 这一句是产品对用户的承诺，所以它必须说出**这一版真正没铺开的地方**。
        // 真机截图当场把第一版证伪了：它只点了"设置页正文／说明／日志"，而屏幕上还能看见
        // 页面正文（空状态那句）与工具栏那颗语言筛选下拉是中文——漏了两个最显眼的。
        // 英文列不许出现中文原文（`EveryRowCarriesBothLanguagesAndTheEnglishColumnIsReallyEnglish` 会红），
        // 所以那两处只能意译，不能像中文列那样把界面上的原句抄进来。
        // 第二版又被同一台真机证伪一次：漏了页眉那两句，以及排序下拉<b>闭着的那一格</b>不换语言
        // （展开的列表换了）。后者不是漏翻，是 WinUI 把闭合显示做成快照——所以它要说的是"不彻底"，
        // 而不是"下一批就好"：那条路要改的是条目本身的样子，见账本 P-169 与坑表 #274。
        // 第三版（批次 WI-2e）改的是<b>覆盖面</b>而不是措辞：四栏的标记已经进表，所以那句"只覆盖常规栏"
        // 成了假话（反方向的那种）；同时把"程序拼出来／算出来的字"单列一句说清，因为用户在同一栏里
        // 两种都会看见——快捷键那一栏上半段是英文、每一行还是中文，不写这句就等于藏着。
        // 第四版（批次 WI-2f）动的还是<b>覆盖面</b>：「数据」那一栏的标记也进表了，所以"其余四栏"成了三栏；
        // 同时把那几行程序写的读数点名到"数据栏的状态行与备份列表每行那颗按钮"——枚举读起来就是穷尽，
        // 少写一行等于把同一屏上还在的中文藏回去（WI-2e 那一版就是被照片证伪过一次）。
        // 第五版（批次 WI-2g）同一形状再来一次：「拓展功能」那一栏的标记进表，"其余三栏"成了两栏；
        // 那一屏上还有五行状态读数是 VM／code-behind 现写的中文（已逐条读到赋值点），不点名就等于藏着。
        // 第六版（批次 WI-2h）同一形状再来一次：「桌面工具」那一栏的标记进表，剩下的正文只剩一栏；
        // 而这一屏上的中文还有三样是程序现写的（每一类那行标题、展开后每行两颗按钮、布局那栏每行的名字与摘要）。
        new Entry("settings.languageNote",
            "改完立刻生效，不用重启。这一版换掉了主窗口顶部那一排（页签、搜索框、排序与筛选按钮）、"
            + "设置页的页眉与那八栏的标题，和「常规」「使用说明」「快捷键」「数据」「健康与诊断」「拓展功能」「桌面工具（组件）」「AI 增强」这八栏里由界面标记写出来的那排字。"
            + "还没铺开的是页面正文里的字（含「暂无条目，请先同步数据」这类空状态）、"
            + "工具栏那颗「语言：全部」下拉、软件内说明和日志，按批往前推。"
            + "同一栏里由程序拼出来或算出来的字也不在这批范围里：顶栏那行索引状态、「关于与更新」里那行检查结果、"
            + "快捷键这一栏的分类名与每一行的动作名和键位、健康页那两排下拉的选项、那一页「诊断」的整列标签、"
            + "数据这一栏那几行状态读数、备份列表那行标题与每行那颗按钮的字、那颗备份间隔下拉的选项、"
            + "拓展功能这一栏那五行状态读数（热榜／RSS／本地磁盘搜索／截屏／画布）、那行引擎占用大小的「约」字"
            + "与画布快捷键一览每行的动作名，桌面工具这一栏每一类组件那行标题与展开后每行两颗按钮的字，"
            + "开机加载那行说每颗约多少的读数、布局那一栏每行的名字与摘要，"
            + "AI 增强这一栏那行测试结果与那行用量读数、那些写着出厂数值与当前设置的只读展示、"
            + "通道下拉里由枚举现生成的那些通道名，还有那些把当场算出来的值写进标签的按钮"
            + "（写着要改用的模型名、写着上次整理好的条数），"
            + "以及「语言 Top：」这类把句子拆成半句＋读数的排法。"
            + "选项还没进表的那几颗下拉也照旧写着中文：热榜那排时间范围、搜索组件那三档排序、组件外观编辑器里那几颗。"
            + "上面这些由程序写出来的字与没进表的选项，要先改成按键取词才谈得上翻。",
            "Takes effect at once — no restart needed. Translated so far: the main window's top row "
            + "(nav tabs, the search box, the sort and filter buttons), the Settings page header, its eight "
            + "section titles, and everything the General, Guide, Hotkeys, Data, Health, Extensions, Desktop tools "
            + "and AI screens write themselves. "
            + "Still Chinese: the page bodies (including empty-state lines), the language filter dropdown on the toolbar, "
            + "the in-app guide and the log. Words the program "
            + "assembles or computes are out of this batch's reach too: the index-status line on the top bar, "
            + "the last check result under About & updates, each hotkey category and every row's action name and "
            + "key, the two option lists on the Health page, the whole label column under Diagnostics there, "
            + "the status lines on the Data page, that backup list's own header and every row's button, "
            + "the options in the backup-interval dropdown, the five status lines on the Extensions page, the word "
            + "in front of the engine's size and every canvas shortcut's action name there, on the Desktop tools page each widget "
            + "type's own header and the two buttons under it, the per-window cost line under the startup switch and every "
            + "layout row's name and summary, on the AI page the probe-result and usage lines, the three read-only rows "
            + "quoting built-in limits against the current settings, the channel names the dropdown builds from an enum "
            + "and those buttons whose labels carry a value computed on the spot (naming the model it would "
            + "switch to, or how many rows were saved in the last run), and the lines built as half a sentence plus a number. "
            + "Dropdowns whose options aren't in the table yet are still Chinese too: the hot-list range, the "
            + "search widget's three sort tiers and the ones in the widget appearance editor. Those "
            + "program-written strings and untabled options have to be reworked to read from the table first.", "変更はその場で反映され、再起動は不要です。この版ではメインウィンドウ上部の一段（タブ、検索ボックス、並び替えと絞り込みのボタン）を切り替えました。"),

        // ===== 「常规」那一栏的正文（批次 WI-2d，P-169 ① 第二刀）=====
        // 中文列<b>逐字取自改键前 XAML 里的那一句</b>：这一刀只搬地方，不顺手改措辞——
        // 改了字就没有任何东西能证明"屏幕上还是原来那句话"，而这一屏是用户每天看的。
        new Entry("settings.general.themeTitle", "主题", "Theme", "テーマ"),
        new Entry("settings.general.themeNote", "跟随系统 / 浅色 / 深色", "Follow system / Light / Dark", "システムに追随 / ライト / ダーク"),
        // 「跟随系统」这一项与上面那颗语言下拉共用 settings.languageFollowSystem：
        // 两处写的是同一个决定，分成两行迟早只改一处。
        new Entry("settings.general.themeLight", "浅色", "Light", "ライト"),
        new Entry("settings.general.themeDark", "深色", "Dark", "ダーク"),

        new Entry("settings.general.appearanceTitle", "外观 · 主窗口材质与不透明度",
            "Appearance · main window material and opacity", "外観 · メインウィンドウの材質と不透明度"),
        new Entry("settings.general.appearanceNote",
            "给主窗口选毛玻璃材质（照搬 DeskBox 的 Desktop Acrylic / Mica 控制器）。桌面组件的材质与不透明度在「桌面工具（组件）」那一栏，两者互不影响。",
            "Choose the frosted material for the main window (the Desktop Acrylic / Mica controllers, taken from DeskBox). Widget material and opacity are set on the “Desktop tools” page; the two are independent.", "メインウィンドウにすりガラスの材質を選びます（DeskBox の Desktop Acrylic / Mica コントローラーを踏襲）。デスクトップウィジェット材質と不透明度は「デスクトップツール（ウィジェット）」側で、両者は互に影響しません。"),
        new Entry("settings.general.mainBackdrop", "主窗口材质", "Main window material", "メインウィンドウの材質"),
        // 英文列里 "Opaque" 必须与下面 material.opaque 那句的开头一致：这句在指着那颗选项说话。
        // ⚠ 那句引用照抄的是**页签名**：可达性树现场读出的 TabItem 名字就是「桌面工具（组件）」，
        // 而这一栏第一张卡的标题与它逐字相同（settings.widgets.title），所以两个名字不是两件事。
        new Entry("settings.general.mainBackdropNote",
            "桌面组件的材质在「桌面工具（组件）」那一栏单独设置，两边互不影响；选「实色」即不透明。",
            "Widget material is set separately on the “Desktop tools” page; the two are independent. Pick “Opaque” for a fully solid surface.", "デスクトップウィジェットの材質は「デスクトップツール（ウィジェット）」側で個別に設定し、両者は互に影響しません。「単色」を選ぶと不透明になります。"),
        // 六颗材质的顺序＝枚举值，XAML 那行注释已经写明；这里换序不会报错，只会让设置档里的数字指向另一种材质。
        new Entry("settings.general.material.acrylicThin", "亚克力 · 薄（推荐，苹果风格）",
            "Acrylic · thin (recommended, Apple-style)", "アクリル · 薄（おすすめ、Apple 風）"),
        new Entry("settings.general.material.mica", "云母 · Mica（更克制的桌面色调）",
            "Mica (a more restrained desktop tint)", "マイカ · Mica（より落ち着いたデスクトップ色調）"),
        new Entry("settings.general.material.opaque", "实色（省资源，不使用材质）",
            "Opaque (no material, saves resources)", "単色（リソース節約、材質を使いません）"),
        new Entry("settings.general.material.micaAlt", "云母 · Alt（取更多桌面壁纸色调）",
            "Mica Alt (picks up more of the wallpaper)", "マイカ · Alt（デスクトップ壁紙の色調をより多く取ります）"),
        new Entry("settings.general.material.acrylicThick", "亚克力 · 厚（更浓的霜化，隐私更好）",
            "Acrylic · thick (heavier frost, better privacy)", "アクリル · 厚（より濃いフロスト、プライバシーにより良い）"),
        new Entry("settings.general.material.solid", "纯色 · Solid（带强调色，可调不透明度）",
            "Solid (accent-tinted, opacity adjustable)", "ソリッド · Solid（アクセントカラー付き、不透明度を調整できます）"),

        new Entry("settings.general.mainOpacity", "主窗口背景不透明度（仅纯色材质）",
            "Main window opacity (Solid material only)", "メインウィンドウ背景の不透明度（ソリッド材質のみ）"),
        new Entry("settings.general.mainOpacityNote",
            "只影响主窗口的「纯色 · Solid」材质，与组件互不影响；其余材质用各自固定的透明度性格。",
            "Affects only the main window's Solid material, and nothing on the widgets; every other material keeps its own fixed opacity.", "メインウィンドウの「ソリッド · Solid」にだけ影響し、ウィジェットとは互に影響しません。他の材質は各自固定の透明度の性格を持ちます。"),

        new Entry("settings.general.trayTitle", "托盘与呼出", "Tray and summoning", "トレイと呼び出し"),
        new Entry("settings.general.autostart", "开机自动启动 StarMark", "Start StarMark at sign-in", "Windows 起動時に StarMark を自動起動"),
        new Entry("settings.general.autostartNote", "登录 Windows 后自动常驻，不必每次手动打开。",
            "Already running once you're signed in — no need to open it each time.", "ログイン後に自動で常駐し、毎回手で開く必要はありません。"),
        new Entry("settings.general.trayResident", "常驻系统托盘", "Stay in the system tray", "システムトレイに常駐"),
        new Entry("settings.general.trayResidentNote", "关闭时隐藏到托盘，进程常驻。",
            "Hiding it sends it to the tray and the process stays up.", "オフのときはトレイに隠れ、プロセスは常駐します。"),
        new Entry("settings.general.globalHotkeys", "启用全局快捷键", "Global hotkeys", "グローバルショートカットキーを有効化"),
        new Entry("settings.general.globalHotkeysNote", "总开关；关闭时所有快捷键均不生效。",
            "Master switch; with it off no hotkey fires.", "マスタースイッチ。オフにするとすべてのショートカットキーが無効になります。"),
        new Entry("settings.general.minimizeToTray", "关闭窗口时最小化到托盘", "Minimize to tray on close", "ウィンドウを閉じたらトレイに最小化"),
        new Entry("settings.general.minimizeToTrayNote", "关闭主窗口不退出程序。",
            "Closing the main window doesn't quit the program.", "メインウィンドウを閉じてもプログラムは終了しません。"),

        new Entry("settings.general.githubTitle", "GitHub Stars 同步", "GitHub Stars sync", "GitHub Stars 同期"),
        new Entry("settings.general.githubToken", "Personal Access Token（可选，仅需 public_repo）",
            "Personal Access Token (optional, public_repo only)", "Personal Access Token（任意、public_repo のみ必要）"),
        new Entry("settings.general.githubUser", "用户名（可选，用于显示）", "Username (optional, for display)", "ユーザー名（任意、表示用）"),

        new Entry("settings.general.aboutTitle", "关于与更新", "About & updates", "情報と更新"),
        new Entry("settings.general.currentVersion", "当前版本", "Current version", "現在のバージョン"),
        new Entry("settings.general.currentVersionNote",
            "这份产物是哪一个版本；构建时间在「健康与诊断」页的「诊断」里。",
            "Which version you're running; the build time is under “Diagnostics” on the Health & diagnostics page.", "この成果物がどのバージョンか。ビルド時刻は「健全性と診断」ページの「診断」にあります。"),
        new Entry("settings.general.autoCheck", "每天自动检查一次", "Check once a day", "毎日自動で一度チェック"),
        // 这一句是"自动那一发"与"点下去那一发"的边界，两列都必须把两件事分开说：
        // 只有 UpdateApplyWiringGateTests 那两格钉着它不许变回全局承诺（"本程序不做下载安装"）。
        new Entry("settings.general.autoCheckNote",
            "自动那一发只问「这个仓库发布了哪一版」：不下载、不改动本机文件，也不替换正在运行的程序。关掉这个开关后，只有点「立即检查」那一下才会联网。装新版是另一件事——只有点「立即更新」才真的下载并替换（换不成会自动退回原来那一版）。",
            "The automatic pass only asks “which version has this repo published”: it downloads nothing, touches no file on this machine and never replaces the running program. Switch this off and the only request is the one you make by pressing “Check now”. Installing is a separate act — only “Update now” downloads and swaps files (and it falls back to the old version by itself if the swap fails).", "自動の回は「このリポジトリはどれかのバージョンを公開したか」だけを尋ねます：ダウンロードせず、本機のファイルも実行中のプログラムも置き換えません。このスイッチを切ると、「今すぐチェック」を押した時だけ通信します。新版を入れるのは別のこと——「今すぐ更新」を押して初めて実際にダウンロードして置き換えます（失敗したら自動で元のバージョンに戻ります）。"),
        new Entry("settings.general.checkNow", "立即检查", "Check now", "今すぐチェック"),
        // 那颗按钮在跑的那一段换的是<b>键</b>不是字（见 SettingsPage.Updates.cs 卷首）：
        // 写死的字面量会在换语言那一刻被重贴成"空闲"那句，等于界面替一件还在跑的事改口。
        new Entry("settings.general.checking", "正在问…", "Asking…", "照信中…"),
        new Entry("settings.general.updateNow", "立即更新", "Update now", "今すぐ更新"),
        new Entry("settings.general.cancelUpdate", "取消更新", "Cancel update", "更新を取り消す"),
        new Entry("settings.general.openDownloads", "打开下载页", "Open the download page", "ダウンロードページを開く"),
        new Entry("settings.general.releaseNotes", "这一版的更新说明", "What's in this release", "この版の更新内容"),

        // ===== 「使用说明」那一栏的这两句（批次 WI-2e，P-169 ① 第三刀）=====
        // 这一栏<b>不放说明正文</b>（正文只在 <c>PageGuides</c> 一处，批次 VC-3 定死的）：
        // 这两句说的都是"那颗按钮是干什么的"，所以它们是宿主的文字，不是第二份指南。
        new Entry("settings.guide.replay", "重看首启引导", "Watch the first-run guide again", "初回ガイドをもう一度見る"),
        new Entry("settings.guide.replayNote",
            "那份引导只弹一次；跳过或关掉之后，从这里回来。",
            "It only appears once — if you skip it or close it, come back here.", "あのガイドは一度だけ出ます。スキップや閉じたあと、ここから戻れます。"),

        // ===== 「快捷键」那一栏的说明与按键 =====
        // ⚠ 这一族的句子里<b>不许出现具体键位</b>（"Ctrl + Alt + Space"那种）：用户改过键之后它就成假话，
        // 而键位只有一个出处＝每一行自己显示的那份绑定（批次 VC 的裁决）。
        new Entry("settings.hotkeys.title", "全局快捷键", "Global hotkeys", "グローバルショートカットキー"),
        new Entry("settings.hotkeys.recordNote",
            "点击右侧按钮开始录制：按钮文字会清空并实时显示你按下的按键，最多 3 个键；录满 3 个键自动结束，只录了 1~2 个键时按 Esc 结束录入（一个键都没录时按 Esc 取消）。",
            "Press the button on the right to start recording: its label clears and shows the keys you press, up to 3. Recording ends by itself at 3 keys; after 1–2 keys, Esc finishes it (Esc with nothing recorded yet cancels).", "右側のボタンを押すと録音が始まり、ボタン文字が消えて押した鍵がリアルタイム表示されます。最大 3 鍵。3 鍵で自動終了、1～2 鍵のときは Esc で終了（1 鍵も録っていないときは Esc でキャンセル）。"),
        new Entry("settings.hotkeys.saveNote",
            "录制结束不会立即生效——必须点击「保存快捷键」后才注册生效（避免刚录完「隐藏主界面」就把窗口藏了）。Backspace / Delete 可清除该动作。",
            "Finishing a recording registers nothing — press “Save hotkeys” to make it live (otherwise re-recording “Hide main window” would hide the window on the spot). Backspace or Delete clears that action.", "録音が終わっても、すぐには有効になりません——「ショートカットキーを保存」を押して初めて登録されます（録りたての「メイン画面を隠す」ですぐ窓が隠れるのを避けるため）。Backspace / Delete でそのアクションを解除できます。"),
        new Entry("settings.hotkeys.conflictNote",
            "同一组合可绑定多个动作——触发时会依次执行（例如一次同时呼出主界面并显示所有组件），这是允许的「冲突」，只会在动作右侧标记，你可以直接忽略。",
            "One combination can drive several actions — pressing it runs them in order (summoning the main window and showing every widget at once, say). Such “conflicts” are allowed, flagged to the right of the action, and safe to ignore.", "同じ組み合わせを複数のアクションに割り当てられます——発動時は順に実行されます（例えばメイン画面を呼び出しながら全ウィジェットを表示する）。これは許された「衝突」で、アクションの右に印が付くだけで、そのまま無視して構いません。"),
        new Entry("settings.hotkeys.save", "保存快捷键", "Save hotkeys", "ショートカットキーを保存"),
        new Entry("settings.hotkeys.discard", "撤销更改", "Discard changes", "変更を取り消す"),
        new Entry("settings.hotkeys.reset", "恢复默认快捷键", "Restore factory hotkeys", "既定のショートカットキーに戻す"),
        new Entry("settings.hotkeys.unsaved",
            "⚠ 有未保存的快捷键改动，点击「保存快捷键」后才会生效。",
            "⚠ Some hotkey changes aren't saved yet — they only register once you press “Save hotkeys”.", "⚠ 未保存のショートカットキー変更があります。「ショートカットキーを保存」を押した後で有効になります。"),
        new Entry("settings.hotkeys.retry", "重试注册", "Try registering again", "登録を再試行"),
        new Entry("settings.hotkeys.mapTitle", "动作 → 快捷键", "Action → hotkey", "アクション → ショートカットキー"),
        new Entry("settings.hotkeys.mapNote",
            "按分类折叠。展开某类后逐行点击右侧按钮录制；同一组合可绑定多个动作（允许的「冲突」只就地标注）。",
            "Grouped by category, all of them collapsed. Open a category and press the button on any row to record it; one combination can drive several actions (allowed “conflicts” are flagged in place).", "分類ごとに折りたたまれています。ある分類を展開してから各行の右側ボタンで録音してください。同じ組み合わせを複数のアクションに割り当てられます（許された「衝突」はその場に印が付くだけ）。"),
        // 这一句换掉了 code-behind 里写死的那句"只保留默认的「Ctrl + Alt + Space（切换主界面）」"：
        // 那句既抄了一处键位（改过键就是假话），又把事实说错了——恢复默认还原的是<b>每一行</b>，
        // 出厂默认里有截图／贴图／穿透／画布那一整批，不是只留主界面那一颗。
        new Entry("settings.hotkeys.resetConfirm",
            "会把下面每一行的键位都还原成出厂默认——不止你改过的那几颗。这一步不能撤销。",
            "This resets every row below to its factory key, not just the ones you changed. It can't be undone.", "下の各行の鍵割り当てをすべて出荷時の既定に戻します——自分が触った数個だけではありません。この取り消しはできません。"),

        // ===== 「数据」那一栏的正文（批次 WI-2f，P-169 ① 第四刀）=====
        // ⚠ 这一族里有一句带 Windows 路径，路径里的 <c>\S</c> 与 <c>\b</c> 在普通字符串里是转义起点
        // （<c>"\b"</c> 是一个退格符，屏幕上看不见、日志里也看不见，只有"备份目录那行怪怪的"这种报告）。
        // 所以那一行<b>必须写成 verbatim 字符串</b>，而 <c>NoRowCarriesAnInvisibleControlCharacter</c>
        // 给这件事配了证人——它读的是编进 dll 的实际字符，不是源码长相。
        new Entry("settings.data.backupTitle", "数据备份与恢复", "Backup & restore", "データのバックアップと復元"),
        new Entry("settings.data.backupNote",
            "条目本体（书签/Star/文件）可重新同步，但手写笔记、标签体系、隐藏/置顶状态、桌面组件待办与随记全部不可重建。",
            "The items themselves (bookmarks / Stars / files) can be re-synced; handwritten notes, the tag system, hidden and pinned states, and the widgets' todos and quick notes cannot be rebuilt.", "条目本体（ブックマーク/Star/ファイル）は再同期できますが、手書きのメモ、タグ体系、非表示/ピン留めの状態、デスクトップウィジェートのToDoと走り書きは再構築できません。"),
        new Entry("settings.data.autoBackupNote",
            @"应用会自己备份：按下面设的间隔存一份到 %LOCALAPPDATA%\StarMark\backups。自动清理只动 auto- 开头的自动件，你手动导出的备份与恢复前快照都不在清理候选里。",
            "The app backs itself up: one copy per interval you set below, into %LOCALAPPDATA%\\StarMark\\backups. Automatic cleanup only touches auto- files — your manual exports and pre-restore snapshots are never candidates.", "アプリが自分でバックアップします：下の間隔で %LOCALAPPDATA%\\StarMark\\backups に保存します。自動クリーンアップは auto- で始まる自動分だけ動き、手で書き出したバックアップと復元前スナップショットは候補に入りません。"),
        new Entry("settings.data.autoBackup", "自动备份", "Automatic backups", "自動バックアップ"),
        new Entry("settings.data.autoBackupOffNote",
            "关掉之后程序不再自动落任何份，能回滚的只剩你手动导出的那些（导入前的「恢复前快照」仍然每次都留）。关掉不会删掉已经落盘的自动件。",
            "Switch this off and the app stops writing any automatic copy — the only things left to roll back to are the ones you exported by hand (the “pre-restore snapshot” taken before each import still happens). Turning it off deletes nothing already on disk.", "切るとプログラムは自動で何も保存しなくなり、戻せるのは手で書き出した分だけになります（取り込み前の「復元前スナップショット」は毎回そのまま残ります）。切っても既に保存済みの自動分は消えません。"),
        new Entry("settings.data.autoBackupInterval", "自动备份间隔", "Backup interval", "自動バックアップの間隔"),
        new Entry("settings.data.autoBackupIntervalTip",
            "两份自动件之间的最小间隔。应用开着时每 10 分钟问一次「到点没」，所以实际落盘最多比到点晚十分钟。保留份数是固定的，间隔越短能回溯的天数就越少——这笔账由下面那行按你选的档算给你看。",
            "The minimum gap between two automatic copies. While the app is open it asks “is it time?” every 10 minutes, so a copy can land up to ten minutes late. How many copies are kept is fixed — the shorter the interval, the fewer days back you can reach; the line below works that out for the interval you picked.", "自動分同士の最小間隔。アプリが開いている間は 10 分ごとに「時刻が来たか」を聞くので、実際の保存は最大 10 分遅れます。保存数は固定なので、間隔を短くするほど遡れる日数が減ります——この帳尻は下の行が選んだ段階で計算して見せます。"),
        new Entry("settings.data.restoreSnapshotNote",
            "导入会先自动生成「恢复前快照」（同上目录），任何恢复都可回滚：它就是下面列表里标着「恢复前快照」的那一行，点「用它回滚」即撤销这次导入。",
            "Before an import, the first thing that happens is a “pre-restore snapshot” in the same folder — which is what makes every restore reversible. It's the row labelled “pre-restore snapshot” below; “Roll back with this” undoes the import.", "取り込みの前に「復元前スナップショット」が自動で作られます（同じフォルダー）。どの復元も取り消せます：それが下のリストで「復元前スナップショット」と付いた行で、「これでロールバック」を押すとこの取り込みが元に戻ります。"),
        new Entry("settings.data.elevatedNote",
            "当前以管理员身份运行（由你启动时的选择决定，程序不会再自己提权）：系统文件对话框在这种会话里不可用，导出/导入会自动改用应用内的路径输入框，无需换权限或重启。",
            "Running elevated because you launched it that way (the app never elevates itself): the system file dialog isn't usable in such a session, so export and import fall back to the in-app path field — no permission changes, no restarts.", "今は管理者として実行中です（起動時のあなたの選択で、プログラムは自分で昇格しません）：システムのファイルダイアログはこのセッションでは使えないため、書き出し/取り込みはアプリ内のパス入力欄に自動で切り替わります。権限を変える必要も再起動もありません。"),
        new Entry("settings.data.includeImages", "导出时带上剪贴板图片本体", "Include clipboard image files when exporting", "バックアップにクリップボード画像本体も含める"),
        new Entry("settings.data.includeImagesNote",
            "带上就存成 .zip（清单 + 图片），只有一份文件、不会拷走一半。关掉则只带条目与文件名：恢复后这些图片条目要靠本机图片目录里还留着的文件才打得开，缺的会标成「文件缺失」。一张 4K 截图常有几百 KB，上限调到几百条时导出包会明显变大。",
            "With images you get a single .zip (manifest + images), so nothing is half-copied. Without them the backup carries entries and file names only: after a restore an image opens only if its file is still in this machine's image folder, and missing ones are marked “file missing”. A 4K screenshot is often a few hundred KB, so raising the cap to hundreds of entries makes the export noticeably bigger.", "含めると .zip になります（一覧 + 画像）。ファイルは一つだけで、半分持っていかれることはありません。切ると条目とファイル名だけになり、復元後それらの画像条目は本機の画像フォルダーに残っているファイルがないと開けず、欠けているものは「ファイル無し」と表示されます。4K スクリーンショットはよく数百 KB なので、上限を数百条に上げるとバックアップは明らかに大きくなります。"),
        new Entry("settings.data.includeImagesOn", "带", "Include", "付ける"),
        new Entry("settings.data.includeImagesOff", "不带", "Skip", "付けない"),
        new Entry("settings.data.includeImagesTip",
            "只影响手动「导出备份」。恢复前快照与自动件始终不带图片本体——它们是回滚点，一份几十 MB 的库配几百 MB 的图会把「随时能撤一步」变成「每次恢复先填一盘磁盘」。恢复时会把包里的图片写回本机图片目录，已有同名文件不覆盖。",
            "Affects manual exports only. Pre-restore snapshots and automatic copies never carry image files — they are rollback points, and pairing a tens-of-MB database with hundreds of MB of images would turn “step back any time” into “fill a disk before every restore”. A restore writes bundled images back into this machine's image folder and never overwrites an existing file of the same name.", "手の「バックアップ書き出し」にだけ影響します。復元前スナップショットと自動分は常に画像本体を付けません——それらはロールバック点で、数十 MB のライブラリに数百 MB の画像を添えると「いつでも一歩戻せる」が「復元のたびにまずディスクを埋める」になります。復元るときは中の画像を本機の画像フォルダーに書き戻し、同じ名前のファイルを上書きしません。"),
        new Entry("settings.data.export", "导出备份…", "Export backup…", "バックアップを書き出し…"),
        new Entry("settings.data.import", "导入备份…", "Import backup…", "バックアップを取り込み…"),
        new Entry("settings.data.openFolder", "打开备份目录", "Open the backup folder", "バックアップフォルダーを開く"),
        new Entry("settings.data.deleteTip",
            "只删这一份导出文件，数据库本身不动；点下去会先确认一次，并说明这一份删掉之后有什么后果。",
            "Deletes this one exported file and nothing in the database. It asks for confirmation first and says what you lose by deleting it.", "この書き出しファイルを一つ消すだけで、データベース本体は動きません。押すと確認が一度あり、これを消すと何が起きるか説明します。"),
        new Entry("settings.data.noBackups",
            "还没有任何备份文件（自动备份按上面设的间隔落盘，也可以点上面「导出备份」立刻来一份）。",
            "No backup files yet (automatic ones land on the interval set above, or press “Export backup…” for one right now).", "バックアップファイルはまだありません（自動バックアップは上の間隔で保存され、「バックアップを書き出し」を押せば今すぐ一台作れます）。"),
        new Entry("settings.data.clipboardTitle", "剪贴板历史", "Clipboard history", "クリップボード履歴"),
        // 这句以前写死"最近 500 条"：批次 ClipIMG-1d 起上限归用户调（10–10000），
        // 卡片上再留一个数就是第二份事实，用户调高之后它当场变成假话（同族：坑表 #279）。
        new Entry("settings.data.clipboardNote",
            "开启后自动记录本机复制过的文本与文件列表（留多少条由下面「文本最多留多少条」那一格决定），在主窗「剪贴板」页点一条即可再复制回去。密码管理器复制的内容、以及私钥 / 登录令牌 / 银行卡号形态一律不入库。",
            "When on, text and file lists you copy on this machine are recorded (how many are kept is the “text entries” box below), and clicking one on the main window's Clipboard page copies it back. Anything copied from a password manager, plus private-key / auth-token / card-number shapes, never enters the store.", "オンにすると本機でコピーしたテキストとファイル一覧を自動記録します（何条残すかは下の「テキストの最大保存数」で決まり）、メインウィンドウの「クリップボード」ページで一条押すと再度コピーできます。パスワードマネージャーがコピーした内容、および秘密鍵 / ログイントークン / 銀行カード番号の形は一切登録しません。"),
        new Entry("settings.data.clipboardImageNote",
            "同时记录复制到的图片（屏幕截图、图片文件、以及在图上右键「复制图片」）。图片无法做敏感检查，开启即包含一切屏幕内容。",
            "Also records images you copy (screen captures, image files, and right-click “Copy image” on one). Images can't be scanned for sensitive content, so switching this on means everything that ever appears on screen.", "コピーされた画像も記録します（スクリーンショット、画像ファイル、画像の上で右クリック「画像をコピー」）。画像は機密チェックができないので、オンにすると画面の内容がすべて含まれます。"),
        new Entry("settings.data.imageMax", "图片最多留多少条", "How many images to keep", "画像の最大保存数"),
        new Entry("settings.data.imageMaxNote",
            "超出后按最近复制的时间淘汰，置顶的那几条不会被挤掉。一张 4K 截图的 PNG 常有几百 KB，调高会明显多占磁盘。",
            "Past the cap the oldest copies are dropped; pinned entries are never squeezed out. A 4K screenshot PNG is often a few hundred KB, so raising this costs disk space.", "超えると最後にコピーした時刻順に淘汰され、ピン留めした数条は押し出されません。4K スクリーンショットの PNG はよく数百 KB なので、上げるとディスクを明らかに圧迫します。"),
        new Entry("settings.data.textMax", "文本最多留多少条", "How many text entries to keep", "テキストの最大保存数"),
        new Entry("settings.data.textMaxNote",
            "默认 500 条，就是以前写死的那个数字；正文过长的只存前 32 KB。文本与图片各自一条线，互不挤占。",
            "500 by default — the number that used to be hardcoded; overly long bodies keep their first 32 KB. Text and images have separate caps and never evict each other.", "既定 500 条、これは以前固定されていた数字です。本文が長すぎるものは先頭の 32 KB だけ保存。テキストと画像は別々の枠で、互いに押し合いません。"),
        new Entry("settings.data.recount", "重新统计占用", "Re-measure usage", "使用量を作り直す"),
        new Entry("settings.data.cleanOrphans", "清理孤儿与临时件", "Clean up orphans and partial files", "孤立ファイルと一時ファイルを整理"),
        new Entry("settings.data.cleanOrphansTip",
            "删掉「不在历史里的那些文件」与没写完的临时件。点下去会先列名单让你确认，一条历史条目都不会动。",
            "Deletes the files “not in the history any more” plus half-written temporary ones. It lists them for confirmation first, and no history entry is touched.", "「履歴にいないあのファイル」と書きかけの一時ファイルを消します。押すとまず一覧を出して確認を求め、履歴条目は一件も動きません。"),

        // ===== 「拓展功能」那一栏的正文（批次 WI-2g，P-169 ① 第五刀）=====
        // 口径与前几刀一致：中文列逐字照抄**屏幕上的那一句**，英文列是它的对译。
        // ⚠ 这一刀有<b>七句</b>没能逐字照抄，全因那句里<b>引用了屏幕上别处的几个字</b>（坑表 #280：
        //   两列一行的表里，抄了其中一列的另一句必然成假话——中文列抄是真话，英文列抄就是假话）：
        //   ① 画布工具条那三项（贴图／复制／存图）与托盘那三项（截图／贴图／识字）今天<b>还没进表</b>，
        //      屏幕上是死的中文 ⇒ 两列都改成<b>描述那些动作</b>，不加引号（「画笔」那一支同理）；
        //   ② 「屏幕画布」这颗热键<b>分组名</b>住在 Core（`HotkeyActions.CategoryOf`，程序写的字）⇒
        //      那句改成"请到「快捷键」那一栏里画布那一组"：「快捷键」<b>已进表</b>所以照旧加引号，
        //      分组名不进引号只描述，指针没丢、两句都还是真话；
        //   ③ 组件窗那两块（「今日速览」与「常看」）和 RSS 页上那颗「收藏到文件夹」「刷新」也还没进表
        //      ⇒ 同样改成描述（"速览那个组件""原来那块回到顶部""点收藏那个动作""主动刷新"）。
        //   导航栏那三项（剪贴板／热榜／RSS）是<b>已按键取词</b>的，所以两列各按本语言的名字写，不算抄。
        //   ⚠ 「按住 Ctrl+Alt 拖动才留痕」那颗 Ctrl+Alt <b>不是</b>用户可改的绑定：它是 `CtrlAltDown`
        //   直接轮询物理键（S2-g 定的"穿透态只抢 Ctrl+Alt 那一下"，有证人在 CanvasWiringGateTests），
        //   改了热键也不会变这句话，所以它不算"文案里抄全局热键"那一族。
        new Entry("settings.extend.trendingTitle", "GitHub 热榜", "GitHub trending", "GitHub トレンド"),
        new Entry("settings.extend.trendingNote",
            "开启后导航栏「剪贴板」右侧出现「热榜」。抓取只在你打开那一页时发生（每个周期每天至多一次，失败时回读上次结果并说明）；条目默认不进库——点 ⭐ 才是给 GitHub 远端加星（需 Token），点 🔖 才存进本机收藏。",
            "Turn this on and “Trending” appears to the right of “Clipboard” in the nav rail. Nothing is fetched until you open that page (at most once a day per period; if a fetch fails you get the last result back and a reason). Entries stay out of the library by default — ⭐ stars the repository on GitHub itself (needs a token), 🔖 is what saves a copy locally.", "オンにするとナビゲーションの「クリップボード」右側に「トレンド」が出ます。取得は該当ページを開いた時だけ（各周期で毎日最大一度、失敗時は前回の結果を読み上げて理由も説明）。条目は既定でライブラリに入りません——⭐ を押して初めて GitHub の遠端に星を付け（Token が必要）、🔖 を押して本機のコレクションに入ります。"),
        new Entry("settings.extend.glanceNote",
            "在速览那个组件里显示热榜块（默认日榜）；关掉则该块消失、下面那块常看回到顶部。",
            "Shows a trending block in the glance widget (daily list by default); switch it off and it disappears, letting the usual block underneath move back to the top.", "「今日のまとめ」ウィジェットの中にトレンドブロックを表示します（既定は今日のランキング）。オフにするとそのブロックが消え、下のよく見るものがトップに戻ります。"),
        new Entry("settings.extend.glanceOn", "显示", "Show", "表示する"),
        new Entry("settings.extend.glanceOff", "不显示", "Hide", "表示しない"),
        new Entry("settings.extend.trendingPeriodNote",
            "热榜页上选的周期会记住，下次打开还是那一档。没配 Token 也能浏览热榜（读侧不依赖 Token），只是 Star 按钮会提示要配 Token。",
            "The period you pick on the trending page is remembered and is still it next time. Browsing needs no token — only the Star button asks for one.", "トレンドページで選んだ周期は記憶され、次に開いても同じ段階です。Token を設定しなくてもトレンドは閲覧できます（読み取り側は Token に依存しません）、ただし Star ボタンは Token を要求します。"),
        new Entry("settings.extend.openTrending", "打开热榜页", "Open the trending page", "トレンドページを開く"),
        new Entry("settings.extend.rssTitle", "网址来源（RSS / Atom）", "Feed sources (RSS / Atom)", "URL ソース（RSS / Atom）"),
        new Entry("settings.extend.rssNote",
            "开启后导航栏出现「RSS」：每个订阅源是它自己的一个文件夹，点条目直接在浏览器打开文章，只有点收藏那个动作才会进本机库（并放进这个源自己的那一层）。抓取只在你到那一页主动刷新时发生，没有后台定时。",
            "Turn this on and “RSS” appears in the nav rail: every feed is a folder of its own, clicking an entry opens the article in your browser, and only the bookmark action copies it into the local library (into that feed's own folder). Feeds are fetched when you go to that page and refresh — nothing runs in the background.", "オンにするとナビゲーションに「RSS」が出ます：各購読ソースはそれ専用のフォルダーで、条目を押すとブラウザで記事を直接開き、ブックマークの動作を押して初めて本機ライブラリに入ります（そのソース自身の一段目に入ります）。取得は該当ページに行って手動で更新したときだけ、バックグラウンドの定時はありません。"),
        new Entry("settings.extend.rssEmpty", "还没有添加任何来源地址。", "No feed addresses added yet.", "まだソースのアドレスが一件もありません。"),
        new Entry("settings.extend.rssOn", "启用", "Enable", "有効"),
        new Entry("settings.extend.rssOff", "停用", "Disable", "無効"),
        new Entry("settings.extend.rssUrlPlaceholder", "订阅地址（https://…/feed）", "Feed address (https://…/feed)", "購読アドレス（https://…/feed）"),
        new Entry("settings.extend.rssNamePlaceholder", "名字（留空＝用域名）", "Name (blank = use the domain)", "名前（空欄＝ドメインを使う）"),
        new Entry("settings.extend.add", "添加", "Add", "追加"),
        new Entry("settings.extend.openRss", "打开 RSS 页", "Open the RSS page", "RSS ページを開く"),
        new Entry("settings.extend.diskSearchTitle", "本地磁盘搜索", "Local disk search", "ローカルディスク検索"),
        new Entry("settings.extend.diskSearchNote",
            "开启后可像 Everything 一样全盘秒搜本地文件，结果并入快捷搜索 / 主窗搜索。首次开启会自动准备 Everything（可能需要一次 UAC）。",
            "Once on, local files are searched across whole volumes as fast as Everything does, and the hits merge into the quick-search and main-window results. The first time you turn it on the app prepares Everything for you (which may take one UAC prompt).", "オンにすると Everything のように全体を即検索でローカルファイルを探せ、結果はクイック検索 / メインウィンドウ検索に合流します。初回のオン時には自動的に Everything の準備を進めます（UAC が一度出る場合があります）。"),
        new Entry("settings.extend.engineUsage", "本地搜索引擎占用", "Search engine footprint", "ローカル検索エンジンの使用量"),
        new Entry("settings.extend.openEngineFolder", "打开所在目录", "Open its folder", "あるフォルダーを開く"),
        new Entry("settings.extend.engineDeleteNote",
            "删除只清理本地搜索引擎（Everything 及其 SDK），书签 / Star / 标签数据不受影响；即使误删，下次开启本地磁盘搜索会自动重新下载。",
            "Deleting clears only the search engine (Everything and its SDK) — bookmarks, Stars and tags are untouched; and if you delete it by mistake, the next time you turn local disk search on it downloads again.", "削除はローカル検索エンジン（Everything とその SDK）だけを掃除し、ブックマーク / Star / タグのデータは無傷です。誤って消しても、次にローカルディスク検索をオンにすれば自動で再ダウンロードします。"),
        new Entry("settings.extend.cleanMissing", "清掉这些行", "Clear those rows", "これらの行を消す"),
        new Entry("settings.extend.rescan", "重新扫描本机文件条目", "Rescan local file entries", "本機のファイル条目を再スキャン"),
        new Entry("settings.extend.captureTitle", "截屏（截图 / 贴图 / 识字）", "Screen capture (snip / pin / OCR)", "スクリーンショット（切り取り / 貼り画像 / 文字認識）"),
        new Entry("settings.extend.captureNote",
            "关掉之后：那三条发起框选的全局热键不再向系统注册（键位交还给当前窗口），托盘里那三项截图入口一并消失。已经钉在桌面上的贴图不受影响——显示／隐藏、忽略鼠标、关闭全部照常可用。",
            "Switched off, the three global hotkeys that start a region selection are unregistered (their keys go back to whatever window has focus) and the three capture entries leave the tray menu. Pins already on the desktop are unaffected — showing, hiding, ignoring the mouse and closing all still work.", "切ると：あの三つの枠選択を開始するグローバルキーはシステムに登録されなくなり（キー割り当ては今のウィンドウに戻り）、トレイのその三つの起動入口も消えます。デスクトップに既に貼った貼り画像は無傷——表示／非表示、マウスを無視、閉じるがすべてそのまま使えます。"),
        new Entry("settings.extend.canvasTitle", "屏幕画布", "Screen canvas", "画面キャンバス"),
        new Entry("settings.extend.canvasNote",
            // 中文列原本写死"画布内的<b>九条</b>快捷键"（批次 WD-3 那一批确实是九条）。这一刀搬字时逐字读那句，
            // 数了一下 `HotkeyActions.Canvas`：今天已经是<b>十三条</b>（后来 WK/WM/P3 各加了图形、复制、贴出）。
            // 条数没有出处可接（LocText 只会整句重贴，接不上那个数＝组合句那一族），所以这里<b>不写数</b>——
            // 留着"九条"就是一句屏幕上对不上的假话，而这一栏下面那张表会自己数给人看。
            "讲解时直接在屏幕上画（不冻结画面）。进入后是穿透态：PPT 翻页、点下层应用都不受影响，选画笔那一支、或按住 Ctrl+Alt 拖动才留痕。关掉之后托盘里这一项一并消失，画布那一组快捷键也不再占用系统组合键。",
            "Draw straight on the screen while explaining, without freezing the picture. It opens pass-through: paging a PPT or clicking the app underneath keeps working, and ink only appears once you pick the pen or hold Ctrl+Alt while dragging. Switching this off also drops the tray entry and stops the canvas shortcuts from holding system combinations.", "解説中に直接画面へ描きます（画面を凍らせません）。入ると透過状態：PPT のめくり、下層アプリのクリックはどちらも影響せず、ペンの一支を選ぶか、Ctrl+Alt を押しながらドラッグして初めて痕が残ります。切るとトレイのこの項目も消え、キャンバスの一組のショートカットキーはシステムの組み合わせキーを占有しなくなります。"),
        new Entry("settings.extend.canvasInShotNote",
            "截图带画布：开着时，截屏抓到的那一帧包含画布上的笔迹（所见即所得）。关掉后截图前会先把那块玻璃收起来再抓、抓完立刻放回——画布上画的东西一点不少，只是截走的图里没有它。画布工具条上那三条产出动作不受这条影响（那三条本来就是要把笔迹留下）。",
            "Capture the canvas: while this is on, the frame a screenshot grabs includes whatever is drawn on the canvas (what you see is what is captured). With it off, that glass is lifted just before the grab and put straight back — nothing you drew is lost, the screenshot simply does not contain it. The three output actions on the canvas toolbar are unaffected; those keep the ink by design.", "スクリーンショットにキャンバスを載せる：オンだと、切り取った一枚にキャンバスの筆跡も入ります（見たままを撮る）。オフだと切り取り前にあのガラスを隠して撮り、撮り終えたらすぐ戻します——キャンバスに描いたものは一点も減りませんが、切り取った画像にはそれがありません。キャンバスツールバーの三つの出力動作はこの設定に影響されません（あの三つは元々筆跡を残すためのもの）。"),
        new Entry("settings.extend.canvasInShotOn", "带上", "Include", "載せる"),
        new Entry("settings.extend.canvasInShotOff", "不带", "Skip", "載せない"),
        new Entry("settings.extend.glowTitle", "光标那块圆多大", "How big the cursor circle is", "カーソルのあの丸の大きさ"),
        new Entry("settings.extend.glowNote",
            "画布上跟着光标的那团光，与幕布开着时鼠标周围那块亮区，用的是同一个半径（一根滑杆管两块圆）。单位是 DIP（与缩放无关的逻辑像素）：150% 的屏上实际像素数会是它的 1.5 倍。拖动立刻生效，不用点保存、不用重启。",
            "The halo that follows the cursor on the canvas and the bright area around the mouse while the curtain is up share one radius — a single slider for both circles. The unit is DIP (logical pixels, independent of scaling), so a 150% screen shows 1.5 times as many actual pixels. Dragging takes effect immediately: no save button, no restart.", "キャンバス上でカーソルに付く光の塊と、暗幕がオンのときのマウス周囲の明るい部分同じ半径を使います（一本のスライダーが二つの丸を管理）。単位は DIP（拡大縮小と無関係な論理ピクセル）：150% の画面では実際のピクセル数はその 1.5 倍。ドラッグで即反映、保存も再起動も不要です。"),
        new Entry("settings.extend.hotkeyListTitle",
            "快捷键一览（下面是当前真正生效的键位；要改请到「快捷键」那一栏里画布那一组）",
            "Shortcut list (below are the keys actually in effect right now; to change them, use the canvas group under the Hotkeys tab)", "ショートカットキー一覧（下は現在本当に有効なキー割り当て。変えるなら「ショートカットキー」の欄でキャンバスの一組へ）"),

        // ===== 「桌面工具（组件）」那一栏的正文（批次 WI-2h，P-169 ① 第六刀）=====
        // 口径与前五刀一致：中文列逐字照把**屏幕上的那一句**，英文列是它的对译。
        // ⚠ 这一栏下拉那六颗材质选项**不新建行**——它们与「常规」页主窗口那六颗是**同一批措辞**
        //   （逐字对过：六句中文一字不差），所以复用 settings.general.material.*；
        //   再造一份同名行＝同一句字两个出处，改一漏一（证人 TheWidgetMaterialListIsNotASecondCopy 钉住）。
        // ⚠ 这一刀有三句没能逐字照把，全因那句里**抄了屏幕上别处那几个字 / 一个数**（坑表 #280，第四面）：
        //   ① widgetsNote 原本写着「标题直接写明「已添加 N 个 / 未添加」」——那几个字是**程序算出来的行标题**
        //      （SettingsPage.Widgets.cs 那一排），不在这批射程、也没有英文对译 ⇒ 改成描述；
        //   ② layoutNote 原本写着「点右键 →「保存当前组件布局…」」——那颗菜单项由 code-behind 现建、
        //      同样没进表 ⇒ 改成描述“那排动作里有一颗”，指针没丢但不再逐字引用；
        //   ③ materialNote 原本结尾写着“（对齐 DeskBox **六种**材质）”——这个数是**第二个出处**（下拉里有几颗由
        //      XAML 数着）⇒ 删数（坑表 #289），改说“每一档的名字都说清了它是什么”。
        new Entry("settings.widgets.title", "桌面工具（组件）", "Desktop tools (widgets)", "デスクトップツール（ウィジェット）"),
        new Entry("settings.widgets.note", "在桌面添加独立小组件：下方按类型折叠，每一类的标题直接写着已经摆上几颗、一颗都没加就明说没加，展开后才是添加与各个实例的显示 / 移除。组件可拖动、边缘吸附、置顶、缩放；组件标题栏那颗关闭按钮只是把它从桌面上撤下，随时可在此或托盘菜单重新添加。",
            "Add standalone widgets to the desktop. They're grouped by type, and each header says how many are up (or that none are yet); open one to add it or to show / remove its instances. Widgets drag, snap to edges, stay on top and resize; the close button on a widget's title bar only takes it off the desktop — you can add it back here or from the tray menu at any time.", "デスクトップに独立した小さなウィジェットを追加します：下は種類ごとに折りたたまれ、各類の見出しはもう幾つ置いたかを直接書き、一個も追加していないなら追加していないと明言します。展開してから追加と各インスタンスの表示 / 削除。ウィジェットはドラッグ、端への吸着、最前面化、拡大縮小ができ、タイトルバーの閉じるボタンはデスクトップから下ろすだけで、いつでもここかトレイメニューで再追加できます。"),
        new Entry("settings.widgets.showAll", "全部显示", "Show all", "すべて表示"),
        new Entry("settings.widgets.hideAll", "全部隐藏", "Hide all", "すべて隠す"),
        new Entry("settings.widgets.startupTitle", "开机自动加载组件", "Load widgets at startup", "起動時にウィジェットを自動で読み込む"),
        new Entry("settings.widgets.startupNote",
            "关掉后开机不再把这些小窗口摆到桌面上，省下的内存按颗算（下面那行写着每颗约多少）。已添加的组件不会丢，随时就地可亮。",
            "Switched off, these small windows are no longer placed on the desktop at boot; the memory you save is counted per window (the line below says roughly how much). Widgets you've added aren't lost — light any of them back up right here.", "切ると起動時にこれらの小さな窓をデスクトップに置かず、節約できるメモリは個数分です（下の行が一個あたりおよそいくらと書かれています）。追加済みのウィジェットは失われず、いつでもその場で点けられます。"),
        new Entry("settings.widgets.loadOn", "加载", "Load", "読み込む"),
        new Entry("settings.widgets.loadOff", "不加载", "Don't load", "読み込まない"),
        new Entry("settings.widgets.appearanceTitle", "组件外观 · 材质与不透明度", "Widget look · material and opacity", "ウィジェットの外観 · 材質と不透明度"),
        new Entry("settings.widgets.appearanceNote",
            "为桌面组件选毛玻璃材质；这一页只管组件，主窗口的材质在「常规」页，两者互不影响。",
            "Pick the frosted material for your widgets. This section is widgets only — the main window's material lives under “General”, and the two don't affect each other.", "ウィジェートのすりガラス素材を選びます。このページはウィジェットだけのこと、メインウィンドウの素材は「一般」ページの方にあり、両者は互いに影響しません。"),
        new Entry("settings.widgets.materialLabel", "组件材质", "Widget material", "ウィジェットの素材"),
        new Entry("settings.widgets.materialNote", "亚克力最像 macOS；云母更克制；纯色带强调色且吃不透明度；实色最省资源。这一族与 DeskBox 同款，每一档的名字都说清了它是什么。",
            "Acrylic looks closest to macOS; Mica is more restrained; Solid carries the accent colour and responds to opacity; Opaque costs the least. Same family as DeskBox — each option's name says what it is.", "アクリルが macOS に最もよく似ます。マイカはもっと控えめ。単色はアクセントカラーを伴い、不透明度の影響も受けます。実色が最も資源を取りません。この一族は DeskBox と同款で、各段階の名前がそれを何であるか言い切っています。"),
        new Entry("settings.widgets.opacityLabel", "组件背景不透明度（仅纯色材质）",
            "Widget background opacity (Solid only)", "ウィジェット背景の不透明度（単色素材のみ）"),
        new Entry("settings.widgets.opacityNote",
            "只影响桌面组件的「纯色 · Solid」材质：越低越透、能看见桌面壁纸。主窗口的不透明度在「常规」页单独设置。",
            "Affects only the widgets' Solid material: lower is more see-through, with the wallpaper behind it. The main window's opacity is set separately under “General”.", "ウィジェットの「単色 · Solid」素材にだけ影響します：低いほど透けて、後ろのデスクトップ壁紙が見えます。メインウィンドウの不透明度は「一般」ページで個別に設定します。"),
        new Entry("settings.widgets.snapTitle", "边缘磁吸", "Edge snapping", "縁へのスナップ"),
        new Entry("settings.widgets.snapRowLabel", "拖动 / 缩放时自动吸附对齐", "Snap into line while dragging / resizing", "ドラッグ / リサイズ時に自動でスナップして揃える"),
        new Entry("settings.widgets.snapStrengthLabel", "磁吸强度", "Snap strength", "スナップの強さ"),
        new Entry("settings.widgets.snapStrengthNote",
            "距其它组件边线 / 屏幕边缘多近就贴上（像素）。数值越大越容易吸住，越大手感越「粘」；调小则更接近自由摆放。",
            "How close to another widget's edge or the screen edge before it sticks (pixels). Higher grabs more eagerly and feels stickier; lower lets you place things freely.", "他のウィジェートの縁線 / 画面の縁にどのくらい近づいたら貼り付くか（ピクセル）。数値が大きいほど吸い付きやすく、大きいほど手触りが「ねばっこ」くなります。小さくすれば自由に置けるようになります。"),
        new Entry("settings.widgets.snapSpacingLabel", "对齐间距", "Snap gap", "揃えた後の間隔"),
        new Entry("settings.widgets.snapSpacingNote",
            "吸住之后组件之间保留的缝隙（像素）。0＝边边紧贴，越大排得越松。",
            "The gap widgets keep after snapping (pixels). 0 means edge to edge; larger spreads them out.", "スナップした後にウィジェット同士が残す隙間（ピクセル）。0＝縁と縁がぴったり付き、大きいほどゆるく並びます。"),
        new Entry("settings.widgets.snapOnNote",
            "开启后：拖动组件会与其它组件的边线、屏幕边缘自动贴合；调整边缘时会同步对齐宽 / 高，让参差不齐的组件一次拉齐。想自己随手摆放就关掉它。",
            "While this is on, dragging a widget lines it up with the other widgets' edges and the screen edge; resizing aligns width / height too, so a ragged group squares up in one pull. Turn it off to place things by eye.", "オンにすると：ウィジェットをドラッグしている時、他のウィジェートの縁線、画面の縁へ自動で貼り付きます。縁の調整時には幅 / 高さも揃え、ばらばらに乱れたウィジェットを一発で引き揃えます。自分の勘で置きたいならオフにしてください。"),
        new Entry("settings.widgets.perfTitle", "性能模式 / 内存门禁", "Performance mode / memory gates", "パフォーマンスモード / メモリ閾値"),
        new Entry("settings.widgets.perfNote",
            "常驻应用按预算回收内存与缓存（DeskBox 同款 MemoryReclaimer）。低内存机器选「省资源」；需要精细控制选「自定义」。",
            "A resident app reclaims memory and cache against a budget (the same MemoryReclaimer idea DeskBox uses). Pick “Saver” on a low-memory machine; pick “Custom” to tune it yourself.", "常駐アプリは予算に従ってメモリとキャッシュを回収します（DeskBox と同款の MemoryReclaimer）。低メモリマシンでは「節約」を選んでください。細かく制御したいなら「カスタム」を。"),
        new Entry("settings.widgets.perfLabel", "性能模式", "Performance mode", "パフォーマンスモード"),
        new Entry("settings.widgets.perfBalanced", "均衡（默认，体验优先）", "Balanced (default, responsiveness first)", "バランス（既定、体験優先）"),
        new Entry("settings.widgets.perfSavings", "省资源（低内存 / 笔记本）", "Saver (low memory / laptops)", "節約（低メモリ / ノートパソコン）"),
        new Entry("settings.widgets.perfCustom", "自定义", "Custom", "カスタム"),
        new Entry("settings.widgets.budgetLabel", "进程内存预算（MB）", "Process memory budget (MB)", "プロセスのメモリ予算（MB）"),
        new Entry("settings.widgets.budgetNote", "超过即触发回收", "Reclaim starts once this is exceeded", "超えた時点で回収を開始します"),
        new Entry("settings.widgets.cacheLabel", "缓存最大条目数", "Cache entry limit", "キャッシュの最大保持数"),
        new Entry("settings.widgets.cacheNote", "图片 / 头像等有界缓存上限",
            "The cap for bounded caches (images / avatars and the like)", "画像 / アバターなど、有界キャッシュの上限"),
        new Entry("settings.widgets.layoutTitle", "布局方案", "Layout presets", "レイアウト方案"),
        new Entry("settings.widgets.layoutNote", "在组件窗口标题栏点右键，那排动作里有一颗能把当前这一屏存成纯模板：存下来的是各组件的位置、大小与外观配置（不含条目内容数据）。同一时刻只会启用一套布局，切换它的快捷键可在「快捷键」那一栏录制。", "Right-click a widget's title bar and one of those actions saves the current screen as a pure template: positions, sizes and appearance settings for every widget (never the items themselves). Only one layout is active at a time, and the hotkey that switches between them is recorded under the “Hotkeys” tab.", "ウィジェット窓のタイトルバーで右クリックすると、あの並びのアクションに一つ、現在のこの画面を純粋なテンプレートとして保存するものがあります：保存されるのは各ウィジェートの位置、大きさ、外観設定で、条目の中身データは含みません。同時に有効なレイアウトは一套だけ、それらを切り替えるショートカットキーは「ショートカットキー」の欄で録れます。"),
        new Entry("settings.widgets.applyLayout", "应用", "Apply", "適用"),
        new Entry("settings.widgets.noLayouts", "（暂无保存的布局）", "(no saved layouts yet)", "（保存済みのレイアウトはまだありません）"),
        // ===== 「AI 增强」那一栏（批次 WI-2i：标记进表，宽尺 39 → 0）=====
        // ⚠ 这一栏没有一句能复用：35 句逐字拿去表的中文列比过一遍，一处逐字相同都没有
        //   （最接近的两处是开关那两个字——这一栏写「开／关」，共用那颗键是「开启／关闭」，
        //   差一个字就不算同一句 ⇒ 这一栏改用共用的 toggle.on／toggle.off，屏幕上多出"启"这一个字，
        //   与 WI-2f 那次"开关两侧统一成开启／关闭"同口径）。
        // ⚠ 这一刀有四处**没能逐字照抄**（坑表 #280 那一族），前三种因都在"抄了屏幕上别处那几个字"上，
        //   第四种是照抄了一句**过期承诺**：
        //   ① fieldGuide 与 note 原本都写着按「测试连接」——那颗按钮的字今天自己也是一行表，
        //      抄进另一句＝同一句字两个出处，改一漏一 ⇒ 改成描述那颗按钮，不再逐字引用；
        //   ② whatToFill 原本写"这三样"、fieldGuide 原本写"三项"——那个数是**第二个出处**
        //      （这一栏有几格输入由 XAML 数着）⇒ 都不写数（坑表 #289）；
        //   ③ noKeyNote 原本写着「批量整理标签」——屏幕上没有一处叫这个名字（那颗按钮叫"开始整理"），
        //      引用一个不存在于屏幕的名字＝假话 ⇒ 改成描述那条功能；
        //   ④ note 原本还写着"批量整理标签的入口在下一批接上"——那是批次 A 留下的路线图，而那颗入口
        //      （整理条数／开始／停止／全部应用）今天就在同一栏里 ⇒ 逐字照抄会把一句过期承诺一起搬进表，
        //      而表从此是它唯一的出处（坑表 #297）⇒ 就地改成实话，并给这一族配了证人
        //      ShippedUiTextNeverPromisesAFutureBatch。
        // ⚠ 两颗按钮的字**不进表、留在 code-behind**：AiFillModelButton 与 AiResumeButton 的标签
        //   各带一个当场算出来的值（第一个可用模型名 / 上次整理好的条数）＝组合句那一族（P-169 单列着），
        //   而这一族还没有"按语言重算"的机制。标记里那两处字面量整颗摘掉（两颗按钮都由代码在现出来之前
        //   先把字写定），⛔ 不许给它们挂静态键——挂了键屏幕上是静态那句，代码随后又把它换成带数的那句，
        //   换语言那一刻会把带数的那句重贴回静态句＝界面替一件已经算完的事改口（坑表 #276）。
        new Entry("settings.ai.whatToFill", "每一项分别要填什么", "What each field actually wants", "各項目はそれぞれ何を入れるのか"),
        new Entry("settings.ai.fieldGuide",
            "服务商地址（Base URL）＝用哪家的接口，也可以填自建或兼容 OpenAI 协议的网关地址；API Key＝那家公司发给你的一串secret，用来认账和计费，等同密码——别贴进聊天群，也不要提交进仓库；模型名＝具体调哪个模型，决定能力与价格。这些都填好就按下面那颗测试按钮，程序会真的发一次请求验证，不通过就把原因写在旁边，不会静默失败。",
            "Base URL = whose API you're calling, or a self-hosted / OpenAI-compatible gateway; API key = the secret that provider issued you, used to bill and to authenticate — treat it as a password: never paste it into a chat, never commit it; model name = the exact model you call, which decides both capability and price. Fill these in and press the test button below: the app really sends a request, and if it fails it prints why right beside it instead of failing silently.", "プロバイダーアドレス（Base URL）＝どこの API を呼ぶかで、自前構築や OpenAI プロトコル互換のゲートウェイアドレスでも構いません。API Key＝あのプロバイダーが発行した一連の秘密で、課金と認証に使われ、パスワードと同格です——チャットグループに貼らず、リポジトリに提出もしないでください。モデル名＝具体的にどのモデルを呼ぶかで、能力と価格を決めます。これらを入力したら下のあのテストボタンを押してください。プログラムは本当に一回リクエストを発して検証し、通らなければ理由をそばに書き、静かに失敗することはありません。"),
        new Entry("settings.ai.deepseekGuide",
            "DeepSeek：platform.deepseek.com 注册 → 进「API Keys」→ 新建 → 当场复制（页面刷新后就再也看不见全文）。Base URL 填 https://api.deepseek.com ，模型名填 deepseek-chat 。",
            "DeepSeek: register at platform.deepseek.com → “API Keys” → create → copy it immediately (once the page refreshes you never see the full text again). Base URL https://api.deepseek.com , model name deepseek-chat .", "DeepSeek：platform.deepseek.com で登録 →「API Keys」→ 新規作成 → その場でコピー（ページを更新すると全文は二度と見られません）。Base URL に https://api.deepseek.com 、モデル名に deepseek-chat 。"),
        new Entry("settings.ai.kimiGuide",
            "月之暗面 Kimi：platform.moonshot.cn 注册 → 左侧「API 管理」→ 创建 API Key。Base URL 填 https://api.moonshot.cn/v1 ，模型名照官网列的填（如 kimi-k2-0905-preview）。",
            "Moonshot Kimi: register at platform.moonshot.cn → “API Management” on the left → create an API key. Base URL https://api.moonshot.cn/v1 , model name as listed on the site (e.g. kimi-k2-0905-preview).", "Moonshot Kimi：platform.moonshot.cn で登録 → 左の「API 管理」→ API Key を作成。Base URL に https://api.moonshot.cn/v1 、モデル名は公式サイトに列挙されている通りに入力（例 kimi-k2-0905-preview）。"),
        new Entry("settings.ai.zhipuGuide",
            "智谱 GLM：open.bigmodel.cn 注册并实名 → 「API Keys」→ 添加新的 API Key。Base URL 填 https://open.bigmodel.cn/api/paas/v4 ，模型名如 glm-4-plus 。",
            "Zhipu GLM: register and verify at open.bigmodel.cn → “API Keys” → add a new key. Base URL https://open.bigmodel.cn/api/paas/v4 , model name e.g. glm-4-plus .", "Zhipu GLM：open.bigmodel.cn で登録し実名認証 →「API Keys」→ 新しい API Key を追加。Base URL に https://open.bigmodel.cn/api/paas/v4 、モデル名は例 glm-4-plus 。"),
        new Entry("settings.ai.openaiGuide",
            "OpenAI：platform.openai.com 登录 → Settings → API keys → Create new secret key。Base URL 填 https://api.openai.com/v1 ，模型名如 gpt-4o-mini 。需要海外支付方式，公司网络常拦这个域名。",
            "OpenAI: sign in at platform.openai.com → Settings → API keys → Create new secret key. Base URL https://api.openai.com/v1 , model name e.g. gpt-4o-mini . Needs an overseas payment method, and corporate networks often block this domain.", "OpenAI：platform.openai.com にログイン → Settings → API keys → Create new secret key。Base URL に https://api.openai.com/v1 、モデル名は例 gpt-4o-mini 。海外の決済方法が必要で、会社のネットワークはこのドメインをよく遮ります。"),
        new Entry("settings.ai.localGuide",
            "本地或自建（Ollama、LM Studio、vLLM 等）：Base URL 填本机地址（如 http://localhost:11434/v1 ），API Key 多数实现要求非空但内容随意，模型名填本地已拉取的那个。",
            "Local or self-hosted (Ollama, LM Studio, vLLM …): Base URL is your machine's address (e.g. http://localhost:11434/v1 ); most implementations want a non-empty key but accept any text, and the model name must be one you've already pulled locally.", "ローカルまたは自前構築（Ollama、LM Studio、vLLM など）：Base URL に本機のアドレス（例 http://localhost:11434/v1 ）、API Key は多くの実装が空以外を要求しますが中身は何でも構いません。モデル名は本機に既に取得済みのものを入れます。"),
        new Entry("settings.ai.noKeyNote",
            "不配 Key 也能用这个软件：AI 只负责批量整理标签这一项功能。没配好时那条功能会明确灰着并说明原因，不会连累别的功能，也不会不打招呼就联网发数据。",
            "The app works without any key: AI only powers the bulk tag-organising feature. Until it's configured that feature is visibly greyed out with the reason, it never drags other features down, and it never sends data off the machine without asking.", "Key を設定しなくてもこのソフトウェアは使えます：AI はタグの一括整理という一つの機能だけを担当します。設定できていない時はその機能が明確にグレーアウトして理由も説明され、ほかの機能に迷惑をかけず、予告なく通信してデータを送ることもありません。"),
        new Entry("settings.ai.title", "AI 助手", "AI assistant", "AI アシスタント"),
        new Entry("settings.ai.note",
            "把模型通道接进来。本机装了 Ollama 就选 Ollama（不需要任何 Key）；有别的 OpenAI 兼容端点（各类代理、LM Studio 的开放服务）也可以填地址与 Key。通道接好之后，这一栏还有那颗测试按钮，以及批量整理标签的入口和它的超时、预算那几格。",
            "Wire up a model channel. If Ollama is installed locally, pick it (no key needed); any OpenAI-compatible endpoint (proxies, LM Studio's local server) also works with an address and key. Once a channel is in, this section also holds the test button, the bulk-organising entry point, and its timeout and budget fields.", "モデルのチャネルを接続します。本機に Ollama がインストールされていればそれを選んでください（Key は一切不要）。他の OpenAI 互換エンドポイント（各種プロキシ、LM Studio の公開サービス）でもアドレスと Key を入力すれば使えます。チャネルが繋がった後、この欄にはさらにあのテストボタン、タグの一括整理の入口、そしてそのタイムアウトと予算のいくつかの入力欄が残ります。"),
        new Entry("settings.ai.enable", "启用 AI 助手", "Turn on the AI assistant", "AI アシスタントを有効にする"),
        new Entry("settings.ai.channel", "通道", "Channel", "チャネル"),
        new Entry("settings.ai.model", "模型名", "Model name", "モデル名"),
        new Entry("settings.ai.modelPlaceholder", "如 qwen2.5:7b（要与服务侧列表一致）",
            "e.g. qwen2.5:7b (must match the service's list)", "例 qwen2.5:7b（サービス側のリストと一致していること）"),
        new Entry("settings.ai.ollamaUrl", "Ollama 地址", "Ollama address", "Ollama のアドレス"),
        new Entry("settings.ai.ollamaUrlPlaceholder", "留空＝http://127.0.0.1:11434",
            "blank = http://127.0.0.1:11434", "空欄＝http://127.0.0.1:11434"),
        new Entry("settings.ai.endpoint", "OpenAI 兼容端点（填到 /v1）", "OpenAI-compatible endpoint (up to /v1)", "OpenAI 互換エンドポイント（/v1 まで入力）"),
        new Entry("settings.ai.endpointPlaceholder", "留空＝https://api.openai.com/v1",
            "blank = https://api.openai.com/v1", "空欄＝https://api.openai.com/v1"),
        new Entry("settings.ai.apiKey", "API Key（选 Ollama 时不必填）", "API key (not needed for Ollama)", "API Key（Ollama を選んだ時は入力不要）"),
        new Entry("settings.ai.classifyModel", "分类模型名（留空＝沿用上面的模型）",
            "Classify model name (blank = use the one above)", "分類モデル名（空欄＝上のモデルをそのまま使う）"),
        new Entry("settings.ai.classifyModelPlaceholder", "如 qwen2.5:1.5b", "e.g. qwen2.5:1.5b", "例 qwen2.5:1.5b"),
        new Entry("settings.ai.testConnection", "测试连接", "Test connection", "接続テスト"),
        new Entry("settings.ai.notTested", "还没测过。", "Not tested yet.", "まだテストしていません。"),
        new Entry("settings.ai.scope", "整理多少条还没有标签的条目", "How many untagged items to organise", "タグが無い条目を何件整理するか"),
        new Entry("settings.ai.start", "开始整理", "Start", "整理を開始"),
        new Entry("settings.ai.stop", "停止", "Stop", "停止"),
        new Entry("settings.ai.applyAll", "全部应用", "Apply all", "すべて適用"),
        new Entry("settings.ai.timeout", "单批超时（秒，留空＝走默认）", "Per-batch timeout (seconds, blank = default)", "一バッチのタイムアウト（秒、空欄＝既定値）"),
        new Entry("settings.ai.budget", "月度 token 预算（到线自动暂停，手动恢复）",
            "Monthly token budget (pauses at the line; resume by hand)", "月次 token 予算（ラインに達したら自動で停止し、手動で再開します）"),
        new Entry("settings.ai.applyBudget", "应用预算", "Apply budget", "予算を適用"),
        new Entry("settings.ai.unpause", "恢复 AI", "Resume AI", "AI を再開"),
        new Entry("settings.ai.warnRatio", "预警比例（%，留空＝走默认）",
            "Warning ratio (%, blank = default)", "警告割合（％、空欄＝既定値）"),
        new Entry("settings.ai.usagePending", "用量还没读。", "Usage hasn't been read yet.", "使用量はまだ読み取れていません。"),
        new Entry("settings.ai.applyGroup", "应用这组", "Apply this group", "このグループを適用"),
        new Entry("settings.ai.rejectGroup", "不要", "Skip", "使わない"),

        // ===== 对话框与列表上那几个共用动词 =====
        // 与 settings.general.cancelUpdate 不是一句：那颗管的是"停掉正在跑的下载／替换"，这颗只是"不做了"。
        new Entry("dialog.cancel", "取消", "Cancel", "キャンセル"),
        new Entry("dialog.delete", "删除", "Delete", "削除"),
        new Entry("dialog.refresh", "刷新", "Refresh", "更新"),
        new Entry("dialog.retry", "重试", "Retry", "再試行"),

        // ===== 「热榜」那一页（批次 WI-2j）=====
        // 页面标题逐字与设置页那颗开关同名 ⇒ 复用 settings.extend.trendingTitle，⛔ 不造第二份措辞（坑表 #290）；
        // 那颗「停止」也复用 settings.ai.stop——屏幕上就是同一个动词，两处各写一行会变成"两份可各自被改"。
        new Entry("trending.note",
            "点标题 = 在浏览器打开仓库。⭐ 改的是 GitHub 远端的 Star（需 Token），🔖 才是存进本机收藏；没点过 ⭐ 或 🔖 的候选不进库、也搜不到。",
            "Clicking a title opens the repo in your browser. ⭐ changes the Star on GitHub itself (needs a token); 🔖 is what saves it to your local collection. Candidates you never starred or bookmarked aren't stored and aren't searchable.", "タイトルを押す＝ブラウザでそのリポジトリを開きます。⭐ は GitHub リモートの Star を変えます（Token が必要）、🔖 こそが本機ライブラリへ保存する動作です。⭐ も 🔖 も押していない候補はライブラリに入らず、検索にも出てきません。"),
        new Entry("trending.daily", "今日", "Today", "今日"),
        new Entry("trending.weekly", "本周", "This week", "今週"),
        new Entry("trending.monthly", "本月", "This month", "今月"),
        new Entry("trending.periodTip", "榜单周期（同一台机器上每个周期每天只抓一次，切回来不会重抓）",
            "List period (each period is fetched once per day on this machine; switching back doesn't refetch)", "ランキングの周期（同じ本機では各周期とも一日に一度しか取得しません。切り替えて戻っても再取得しません）"),
        new Entry("trending.refreshTip", "强制重抓本期（跳过“今天已抓过”的短路）",
            "Force a refetch of this period (skips the \"already fetched today\" shortcut)", "本期の再取得を強制します（「今日はもう取得済み」という短絡を飛び越えます）"),
        new Entry("trending.stopTip", "掐掉本次抓取，列表保持原样（取消不会被当成失败去走兜底）",
            "Cancels this fetch and leaves the list as-is (a cancel isn't treated as a failure, so no fallback kicks in)", "今回の取得を中断し、リストはそのままにします（中断は失敗として扱われないので、フォールバックは動きません）"),
        new Entry("trending.tokenButton", "去设置里配 Token", "Set up a token in Settings", "設定へ行って Token を設定"),
        // 这四句由 VM 交回**键＋实参**（批次 WI-2k：`LocMessage`），⛔ 不许在 VM 里拼成整句——
        // 拼那一刻语言就定死了，换语言没人回去重算（坑表 #276 那一族）。
        new Entry("trending.emptyDisabled",
            "热榜未开启：到「设置 → GitHub 热榜」打开后，这里才会抓取 GitHub 热榜。",
            "Trending is off: turn it on under Settings → GitHub Trending before this page fetches anything.", "熱榜がオフです：「設定 → GitHub 熱榜」でオンにすると、ここで GitHub 熱榜を取得するようになります。"),
        new Entry("trending.emptyNoCandidates",
            "本期没有拿到候选。可能是 GitHub 热榜页暂时不可用——点「重试」换一次试试。",
            "This period returned no candidates. GitHub's trending page may be unavailable right now — hit Retry to try again.", "本期は候補を取得できませんでした。GitHub の熱榜ページが一時的に使えない可能性があります——「再試行」を押して改めてお試しください。"),
        new Entry("trending.emptyNoMatch", "本期 {0} 个候选里没有匹配「{1}」的（标题 / 描述 / 语言）。清空筛选即可看全部。",
            "None of this period's {0} candidates match “{1}” (name / description / language). Clear the filter to see them all.", "本期 {0} 件の候補の中に「{1}」へ一致するものがありません（タイトル / 説明 / 言語）。フィルターを空にすれば全部見られます。"),
        new Entry("trending.emptyFailed", "抓取失败：{0}", "Fetch failed: {0}", "取得に失敗しました：{0}"),
        new Entry("trending.switchButton", "去设置里开启热榜", "Turn on Trending in Settings", "設定へ行って熱榜をオンにする"),
        new Entry("trending.filterPlaceholder",
            "在本期候选里筛选：仓库名 / 描述 / 语言（只筛这一页，不动主库、不影响榜单本身）",
            "Filter this period's candidates: repo name / description / language (this page only — your library and the list itself are untouched)", "本期の候補をフィルター：リポジトリ名 / 説明 / 言語（このページだけのこと、本機ライブラリとランキング自体には影響しません）"),

        // ===== 热榜那一页的状态行与两个动作的结果（批次 WI-2k：VM 交回键＋实参）=====
        // 状态行那句是套娃的：外层句子嵌「来源」与「N 分钟前」两颗读数。
        // 实参交出的是 LocMessage（还没取词），贴上去的那一刻才按当时的语言填空（LocMessage.ToString）。
        // ⛔ 不许在这里把两层拼成一句中文——那是坑表 #276 那一族。
        new Entry("trending.statusCancelled", "已取消本次抓取，列表保持原样。",
            "Fetch cancelled — the list stayed as it was.", "今回の取得をキャンセルしました。リストはそのままです。"),
        new Entry("trending.statusFailed", "抓取失败，下面给出的是原因；点「重试」可以再抓一次。",
            "The fetch failed; the reason is below. Hit Retry to try again.", "取得に失敗しました。下に示すのがその原因です。「再試行」を押すともう一度取得できます。"),
        new Entry("trending.statusStale", "抓取失败·已显示上次结果（{0}）",
            "Fetch failed · showing the last result ({0})", "取得失敗·前回の内容を表示中（{0}）"),
        new Entry("trending.statusCached", "{0}·{1}已缓存", "{0} · {1} in cache", "{0}·{1}キャッシュ済み"),
        new Entry("trending.statusViaAge", "{0}·{1}", "{0} · {1}", "{0}·{1}"),
        new Entry("trending.statusNotice", "{0}｜{1}", "{0} | {1}", "{0}｜{1}"),
        new Entry("trending.viaSearchApi", "来源：GitHub 搜索接口兜底（没有“本期新增星数”）",
            "Source: GitHub search API fallback (no “stars added this period”)", "出所：GitHub 検索 API によるフォールバック（「本期の新規 star 数」はありません）"),
        new Entry("trending.viaSearchApiShort", "来源：GitHub 搜索接口兜底",
            "Source: GitHub search API fallback", "出所：GitHub 検索 API によるフォールバック"),
        new Entry("trending.viaTrendingPage", "来源：GitHub 热榜页", "Source: GitHub trending page", "出所：GitHub 熱榜ページ"),
        new Entry("trending.viaCacheUnknown", "来源：上次缓存（未记录来源）",
            "Source: last cache (origin not recorded)", "出所：前回のキャッシュ（出所は記録されていません）"),
        // 动作结果那十句：静默的成功与静默的失败都算缺陷（P-54），所以每个出口都留一句，且一句都不许在代码里拼。
        new Entry("trending.badRepoStar", "这一行不是有效的 GitHub 仓库（owner/repo），无法 Star",
            "This row isn't a valid GitHub repo (owner/repo), so it can't be starred", "この行は有効な GitHub リポジトリ（owner/repo）ではないので、Star できません"),
        new Entry("trending.badRepoCollect", "这一行不是有效的 GitHub 仓库（owner/repo），无法收藏",
            "This row isn't a valid GitHub repo (owner/repo), so it can't be bookmarked", "この行は有効な GitHub リポジトリ（owner/repo）ではないので、ブックマークできません"),
        new Entry("trending.starred", "已 Star：{0}", "Starred: {0}", "Star しました：{0}"),
        new Entry("trending.unstarred", "已取消 Star：{0}", "Star removed: {0}", "Star を外しました：{0}"),
        new Entry("trending.starFailed", "Star 失败：{0}", "Starring failed: {0}", "Star 失敗：{0}"),
        new Entry("trending.uncollected", "已从本机收藏移除：{0}（不影响 GitHub 的 Star 状态）",
            "Removed from your local collection: {0} (GitHub's Star state is untouched)", "本機のブックマークから外しました：{0}（GitHub の Star 状態には影響しません）"),
        // 「这条本来就没在本机收藏里」刻意避开"不在本机"那三个字：那是剪贴板缺图那句事实的指纹
        // （ClipboardMissingImageTextGateTests），两件事各说各的，别共用一个字面串。
        new Entry("trending.neverCollected", "这条本来就没在本机收藏里：{0}",
            "That one wasn't in your local collection to begin with: {0}", "これはもとから本機のブックマークに入っていません：{0}"),
        new Entry("trending.collected", "已收进本机收藏：{0}（不影响 GitHub 的 Star 状态）",
            "Saved to your local collection: {0} (GitHub's Star state is untouched)", "本機のブックマークへ保存しました：{0}（GitHub の Star 状態には影響しません）"),
        new Entry("trending.uncollectFailed", "移出收藏失败：{0}", "Removing the bookmark failed: {0}", "ブックマークを外せませんでした：{0}"),
        new Entry("trending.collectFailed", "收进收藏失败：{0}", "Saving the bookmark failed: {0}", "ブックマークへ保存できませんでした：{0}"),

        // ===== 「多久以前」那四档（批次 WI-2k 从 DateTimeText 搬进表）=====
        // 档位（60 秒还是 2 分钟算"刚刚"、超过一天要不要落回日期）留在各宿主，这里只有措辞。
        // 措辞搬进表的原因：这四档会被嵌进热榜与 RSS 的状态行，写死在代码里＝外层翻了里面没翻。
        new Entry("time.justNow", "刚刚", "just now", "たった今"),
        new Entry("time.minutesAgo", "{0} 分钟前", "{0} minutes ago", "{0} 分前"),
        new Entry("time.hoursAgo", "{0} 小时前", "{0} hours ago", "{0} 時間前"),
        new Entry("time.daysAgo", "{0} 天前", "{0} days ago", "{0} 日前"),

        // ===== 「快照」「RSS」「剪贴板」那三页的标记（批次 WI-2j）=====
        // 复用不新建（坑表 #290）：页面标题「快照」＝nav.snapshot、「剪贴板历史」＝settings.data.clipboardTitle、
        // 「应用」＝settings.widgets.applyLayout、「删除」＝dialog.delete。
        new Entry("snapshot.note",
            "快照记下某一时刻「所有组件的布局 + 各自的数据（快捷入口 / 待办 / 随记 / 条目格查询）」。它是不可变的历史点：应用某快照 = 回到那一刻，并会先自动留一个「应用前」回滚点。在组件窗口标题栏点右键 →「保存当前布局与数据…」即可新建。",
            "A snapshot records every widget's layout plus each one's data (quick launch / todos / notes / library queries) at one moment. It's an immutable point in history: applying one returns you to that moment, and a \"before applying\" rollback point is written first. Create one from a widget window's title bar right-click menu → \"Save current layout & data…\".", "スナップショットはある一時点を記録します：「全ウィジェートのレイアウト＋それぞれのデータ（クイック起動 / ToDo / 走り書き / ライブラリの照会）」。それは変更できない歴史の一点です——あるスナップショットを適用＝あの時刻へ戻ることで、適用前にロールバック点を自動で残します。ウィジェットのタイトルバーで右クリック →「現在のレイアウトとデータを保存…」で作成できます。"),
        new Entry("snapshot.view", "查看", "View", "表示"),
        new Entry("snapshot.extractLayout", "提取布局", "Extract layout", "レイアウトを取り出す"),
        new Entry("snapshot.empty",
            "（还没有快照。在任意组件窗口标题栏右键 →「保存当前布局与数据…」创建第一个。）",
            "(No snapshots yet. Right-click any widget window's title bar → \"Save current layout & data…\" to make the first one.)", "（スナップショットはまだありません。任意のウィジェット窓のタイトルバーで右クリック →「現在のレイアウトとデータを保存…」で最初の一つを作成してください。）"),
        new Entry("rss.title", "RSS 订阅", "RSS subscriptions", "RSS 購読"),
        new Entry("rss.note",
            "点文件夹展开这个源的文章；点标题＝在浏览器打开这篇文章。只有点「收藏到文件夹」的那一条才会进本机库，并放进它自己的文件夹（RSS订阅 / 源名）；不点就不进库。打开这一页先摆本机缓存（rss-cache.json），自动补抓有自己的节奏——隔多久抓一次、哪几个到期了，都写在下面那一行状态里；要立刻看新的点「刷新」。",
            "Click a folder to open that feed's articles; clicking a title opens the article in your browser. Only entries you send with \"Bookmark to folder\" land in the local library, filed under their own two-level folder (the RSS folder, then the feed's name). This page shows the local cache (rss-cache.json) first; auto-refetch keeps its own rhythm — how often and which feeds are due are in the status line below. Hit \"Refresh\" to check right now.", "フォルダーを押すとそのフィードの記事を開きます。タイトルを押す＝ブラウザでその記事を開きます。「フォルダーへブックマーク」を押した一条だけが本機ライブラリへ入り、そのフィード専用のフォルダー（RSS購読 / フィード名）へ入れられます。押さなければライブラリに入りません。このページを開いたらまず本機キャッシュ（rss-cache.json）を並べ、自動の補取得は自分なりの間隔で回ります——どのくらいの間隔で取得するか、どのフィードが期限超過かは、下のあの状態行に書いてあります。新しいものを今すぐ見たいなら「更新」を押してください。"),
        new Entry("rss.refreshTip",
            "无视自动补抓的节奏，现在就去问一遍所有启用的源（源没变化时只花一个来回；再点一次就是停止，已经抓到的会留下）",
            "Checks every enabled source now, ignoring the auto-fetch rhythm (an unchanged feed costs one round trip; click again to stop, and whatever already arrived stays)", "自動補取得の間隔を無視して、今から有効な全フィードを尋ねに行きます（フィードに変化がなければ往復一回分だけ。もう一度押すと停止し、すでに取得したものは残ります）"),
        new Entry("rss.sourcesButton", "去设置改来源", "Edit sources in Settings", "設定へ行って来源を変える"),
        new Entry("rss.sourcesTip", "添加 / 停用 / 删除订阅地址，以及这一栏的总开关",
            "Add, disable or delete feed URLs, plus this section's master switch", "購読アドレスの追加 / 停止 / 削除、そしてこの欄の総スイッチ"),
        new Entry("rss.switchButton", "去设置里开启 RSS", "Turn on RSS in Settings", "設定へ行って RSS をオンにする"),

        // ===== RSS 那一页的状态行与收藏结果（批次 WI-2k）=====
        // 节奏那一句（"一天"／"{0} 小时"那一族）也住在这一组，由 RssFeedCache.GapMessage 交出。
        // ⛔ 不许把"一天"抄进下面任何一句——改了 AutoRefreshGap 就会留下四句假话（批次 RF-4 的口径）。
        new Entry("rss.gapMinutes", "{0} 分钟", "{0} minutes", "{0} 分"),
        new Entry("rss.gapHours", "{0} 小时", "{0} hours", "{0} 時間"),
        new Entry("rss.gapOneDay", "一天", "a day", "一日"),
        new Entry("rss.gapDays", "{0} 天", "{0} days", "{0} 日"),
        new Entry("rss.gapDaysPlus", "{0} 天多", "over {0} days", "{0} 日以上"),
        new Entry("rss.emptyDisabled",
            "RSS 未开启：到「设置 → 网址来源（RSS / Atom）」打开总开关，这里才会出现各个订阅源。",
            "RSS is off: turn on the master switch under Settings → Feed sources (RSS / Atom) to see your feeds here.", "RSS がオフです：「設定 → URL 来源（RSS / Atom）」で総スイッチをオンにすると、ここに各購読フィードが出るようになります。"),
        new Entry("rss.emptyNoSources",
            "还没有添加任何来源地址：到「设置 → 网址来源（RSS / Atom）」填一个订阅地址再回来。",
            "No feed URLs yet: add one under Settings → Feed sources (RSS / Atom), then come back.", "購読アドレスが一つも追加されていません：「設定 → URL 来源（RSS / Atom）」へ一つ入力してもどってください。"),
        new Entry("rss.statusCacheDue",
            "{0} 个来源，{1} 个启用中；上面摆的是本机缓存，{2} 个已超过{3}没抓（打开这一页时自动补抓，不用点按钮）。",
            "{0} feeds, {1} enabled; above is the local cache. {2} are overdue by more than {3} and get re-fetched when this page opens — no button needed.", "{0} 個の来源、{1} 個が有効。上に並んでいるのは本機キャッシュで、{2} 個は{3}を超えて未取得です（このページを開いた時に自動で補取得するので、ボタンは要りません）。"),
        new Entry("rss.statusCacheFresh",
            "{0} 个来源，{1} 个启用中；上面摆的是本机缓存，都在{2}之内抓过，不会再联网；要看新的点「刷新」。",
            "{0} feeds, {1} enabled; above is the local cache. All were fetched within {2}, so nothing goes online again — hit Refresh for new items.", "{0} 個の来源、{1} 個が有効。上に並んでいるのは本機キャッシュで、すべて{2}以内に取得済みです。もう通信しません。新しいものを見るなら「更新」を押してください。"),
        new Entry("rss.stopping", "正在停止…（已经抓到的会留在上面）",
            "Stopping… whatever already arrived stays above", "停止しています…（すでに取得したものは上に残ります）"),
        new Entry("rss.noSources", "还没有添加任何来源地址。", "No feed URLs have been added yet.", "購読アドレスが一つも追加されていません。"),
        new Entry("rss.roundFailed", "抓取没能完成：{0}", "The fetch didn't finish: {0}", "取得を完了できませんでした：{0}"),
        new Entry("rss.fetchingDue", "正在补抓 {0} 个已超过{1}的来源…",
            "Re-fetching {0} feeds older than {1}…", "{1}を超えた {0} 個の来源を補取得しています…"),
        new Entry("rss.fetchingAll", "正在抓取 {0} 个来源…（再点一下可以停）",
            "Fetching {0} feeds… (click again to stop)", "{0} 個のフィードを取得しています…（もう一度押すと止められます）"),
        new Entry("rss.summaryNone", "{0} 个来源里有 {1} 个通了，但没有给出任何条目",
            "{1} of {0} feeds responded, but none gave any entries", "{0} 個の来源のうち {1} 個が通りましたが、条目を一つも出しませんでした"),
        new Entry("rss.summaryAuto",
            "按「{0}一次」的节奏，本轮补抓了 {1} 个到期的来源、新增 {2} 条，现在共 {3} 条（其余是缓存）。要看最新的点「刷新」。",
            "Rhythm: {0} per pass. This round re-fetched {1} due feeds and added {2} entries, now {3} in total (the rest come from cache). Hit Refresh for the newest.", "「{0}に一回」の間隔で、本輪は期限が来た {1} 個を補取得し {2} 条を新たに加え、現在 {3} 条です（残りはキャッシュ）。最新を見るなら「更新」を押してください。"),
        new Entry("rss.summaryManual",
            "共 {0} 条（{1} 个源给了内容，本轮新增 {2} 条）。点条目直接跳文章，点「收藏到文件夹」才进库。",
            "{0} entries in total ({1} feeds provided content, {2} new this round). Click an entry to open the article; only Bookmark to folder saves it.", "合計 {0} 条（{1} 個のフィードが内容を出し、本輪 {2} 条を新たに追加）。条目を押すと直接記事へ飛び、「フォルダーへブックマーク」を押して初めてライブラリに入ります。"),
        new Entry("rss.summaryFailed", "　{0} 个源失败，原因写在对应那一行",
            "  {0} feeds failed — the reason is written on their row", "　{0} 個のフィードが失敗しました。理由はそれぞれ対応するあの行に書いてあります"),
        new Entry("rss.summaryStopped", "　已停止，还有 {0} 个源没抓（再点一次接着抓）",
            "  Stopped with {0} feeds left (click again to continue)", "　停止しました。{0} 個のフィードがまだ未取得です（もう一度押すと続きを取得します）"),
        new Entry("rss.cacheWriteFailed", "　缓存没能写进磁盘（下次进这一页还得重抓）",
            "  The cache wasn't written to disk (this page will refetch next time)", "　キャッシュをディスクへ書き込めませんでした（次にこのページへ入る時は再取得になります）"),
        // 附加那两句用的连接句：整句由 LocMessage 嵌套着当场重算，⛔ 不在代码里 += 字符串。
        new Entry("rss.statusJoin", "{0}{1}", "{0}{1}", "{0}{1}"),
        new Entry("rss.openFailed", "没能打开：{0}", "Couldn't open it: {0}", "開けませんでした：{0}"),
        new Entry("rss.stop", "停止抓取", "Stop fetching", "取得を停止"),
        new Entry("rss.alreadyCollected", "这一条已经在「{0}」里了；要改动请到「文件夹」页对应那一行",
            "That entry is already in “{0}” — to change it, use its row on the Folders page", "この条目はすでに「{0}」の中にあります。変更するなら「フォルダー」ページの対応するあの行へ"),
        new Entry("rss.noLink", "这一行没有链接，收藏不了（订阅源没给出条目地址）",
            "This row has no link, so it can't be bookmarked (the feed gave no entry URL)", "この行にはリンクがないのでブックマークできません（フィードが条目アドレスを出していません）"),
        new Entry("rss.collected", "已收进「{0}」：{1}", "Saved into “{0}”: {1}", "「{0}」へ保存しました：{1}"),
        new Entry("rss.collectedNoId", "已写进本机收藏，但仓储没有回 id（标题：{0}）",
            "Written into your local collection, but the store returned no id (title: {0})", "本機のブックマークへ書き込みましたが、ストレージが id を返しませんでした（タイトル：{0}）"),
        new Entry("rss.collectFailed", "收藏没能完成：{0}", "Bookmarking didn't finish: {0}", "ブックマークを完了できませんでした：{0}"),
        new Entry("clipboard.note",
            "点一条 = 再复制回剪贴板（可直接去粘贴）。右键可置顶、加标签、写笔记或删除。",
            "Clicking an entry copies it back to the clipboard, ready to paste. Right-click to pin, tag, annotate or delete.", "一条を押す＝クリップボードへコピーし戻します（そのまま貼り付けられます）。右クリックでピン留め、タグ追加、メモ記入、削除ができます。"),
        new Entry("clipboard.pause", "暂停记录", "Pause capture", "記録を一時的に止める"),
        new Entry("clipboard.pauseTip", "临时粘贴密码等私密内容时暂停；暂停期间不再记录新内容",
            "Pause while pasting private content such as passwords; nothing new is recorded while paused", "パスワードなど秘密性の高い内容を貼っている間だけ止めます。停止中は新しい内容を記録しません"),
        new Entry("clipboard.clear", "清空历史…", "Clear history…", "履歴をすべて消去…"),

        // ===== 「标签筛选」那一排（三页共用一句＝一份措辞，坑表 #290）=====
        // 「标签筛选」的标题与那颗「清除」今天同时住在标签页、文件夹树页与搜索页的标记里。
        new Entry("filter.tagTitle", "标签筛选", "Filter by tag", "タグによるフィルター"),
        new Entry("filter.tagRemoveTip", "移除该标签筛选", "Remove this tag filter", "このタグのフィルターを外す"),
        new Entry("filter.tagClear", "清除", "Clear", "解除"),
        new Entry("filter.tagClearAll", "清除全部标签筛选", "Clear all tag filters", "すべてのタグフィルターを解除"),


        // ===== 「健康与诊断」那一栏的正文（批次 WI-2e）=====
        new Entry("settings.health.eyeRestTitle", "护眼 · 休息提醒", "Eye comfort · break reminders", "目の保護 · 休息の知らせ"),
        new Entry("settings.health.eyeRestNote",
            "连续工作一段时间后提醒你休息。开着才建那张定时表，关掉后不探测前台窗口、也不会显示任何东西。",
            "Nudges you to take a break after a stretch of work. The schedule only exists while it's on — switch it off and nothing in front gets checked and nothing appears on screen.", "連続であらかじめの時間を使うと、休息を知らせます。オンにしている時だけあの予定表を作り、オフにすれば前景ウィンドウを探らず、何も表示しません。"),
        new Entry("settings.health.eyeRestInterval", "间隔多久提醒一次", "How often to remind", "どのくらいの間隔で知らせるか"),
        new Entry("settings.health.eyeRestIntervalNote",
            "到点后还要看当前前台是不是全屏应用，所以实际时刻会有一两分钟浮动。",
            "At the due time it also checks whether a full-screen app is in front, so the actual moment drifts by a minute or two.", "時刻が来た時点で、前景が全画面アプリかどうかも見る必要があるため、実際の時刻は一、二分ずれます。"),
        new Entry("settings.health.eyeRestNotice", "怎么提醒", "How it reminds", "知らせ方"),
        new Entry("settings.health.eyeRestNoticeNote",
            "改完直接按下面的「试一试」就能看见这一档的效果，不用等满间隔。",
            "Change it, then press “Try the current settings” below to see that setting straight away — no need to wait out the interval.", "変更後、下の「今の設定を試す」を押すとその段階の効果をその場で確認できます。間隔が満ちるのを待つ必要はありません。"),
        new Entry("settings.health.deferFullscreen",
            "前台是全屏应用时让路：放 PPT / 放映 / 全屏游戏不被打断，稍后再探一次。",
            "Steps aside when a full-screen app is in front: presentations and games don't get interrupted, and it checks again a little later.", "前景が全画面アプリの時は道を譲ります：プレゼン資料 / 動画再生 / フルスクリーンのゲームが妨げられず、あとでもう一度探ります。"),
        new Entry("settings.health.deferOn", "让路", "Yields", "譲る"),
        new Entry("settings.health.deferOff", "不让路", "Doesn't", "譲らない"),
        new Entry("settings.health.eyeRestPreview", "试一试当前设置的效果", "Try the current settings", "今の設定を試す"),
        // 原来这一句末尾写着"时钟上还会多显示一行「休息 HH:mm」"，那是<b>抄屏幕上那几个字</b>：
        // 组件那一侧的文字不在这一批的射程里，英文界面上那行仍然是中文，照抄就成了假话。
        // 所以两列都改成说"多显示一行下一次休息的时刻"——措辞改动登记在报告 §二百五十五。
        new Entry("settings.health.eyeRestClockHint",
            "想少两步：桌面「时钟」组件的右键菜单里就有护眼休息提醒那一节，开关、间隔、提醒形式与「试一试」在那儿改也一样生效（改的是同一份设置），时钟上还会多显示一行下一次休息的时刻。",
            "The shorter route: the desktop “Clock” widget's right-click menu has the same section. Its switch, interval, reminder style and “try it now” take effect from there too — it's the same setting — and the clock shows an extra line with the time of your next break.", "手順を二つ省きたいなら：デスクトップの「時計」ウィジェット右クリックメニューの中に、目の保護·休息の知らせの節があります。スイッチ・間隔・知らせ方・「今の設定を試す」をそこで変えても同じように効きます（同じ一份の設定を変えるのです）。さらに時計には次の休息の時刻が一行余分に表示されます。"),
        new Entry("settings.health.scoreTitle", "收藏健康度", "Collection health", "ライブラリの健全度"),
        new Entry("settings.health.scoreNote",
            "本地聚合，零网络。把「收藏夹腐败」变成可行动清单。",
            "Aggregated locally, no network. Turns a decaying collection into a list you can act on.", "本機で集計し、通信はゼロ。「ブックマークの劣化」を実行できるチェックリストに変えます。"),
        new Entry("settings.health.trend", "近 14 天新增", "Added in the last 14 days", "過去 14 日の新規追加"),
        new Entry("settings.health.diagnosticsTitle", "诊断", "Diagnostics", "診断"),

        // ===== 开关两侧的这两个字 =====
        // 一颗键服务全应用那 22 对开关标签（现场数过：此刻全住在设置页，本批进了 7 对），
        // 而不是每颗开关一行：这两个词是同一个词汇，各写一份的下场就是同一屏上出现"开启／打开／启用"三种说法。
        // ⚠ 它们不许走 <c>LocText.Key</c>：ToggleSwitch 同时有 Header 与 Content，
        // 那颗键会照上面的排除规则去写 <c>Header</c>（＝开关上方多出一行字），OnContent 一个字都不动。
        new Entry("toggle.on", "开启", "On", "オン"),
        new Entry("toggle.off", "关闭", "Off", "オフ"),
    };

    private static readonly Dictionary<string, Entry> ByKey = Build();

    private static Dictionary<string, Entry> Build()
    {
        var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var entry in All)
        {
            // 重名不是"后写覆盖前写"那种小事：被删掉的那一行还在某个 XAML 里被引用着，
            // 于是那一处悄悄换了话。这里当场炸，让它活不到运行期。
            if (map.ContainsKey(entry.Key))
                throw new InvalidOperationException($"界面文案的键重复：{entry.Key}");
            map.Add(entry.Key, entry);
        }
        return map;
    }

    public static bool TryGet(string key, out Entry entry) => ByKey.TryGetValue(key, out entry!);

    /// <summary>按语言取那一列。三列都必填 ⇒ 这里没有"缺译回退"那条分支——
    /// 真的少一列时编译器就不让过，所以运行期不必再猜。</summary>
    public static string TextOf(Entry entry, AppLanguage language) => language switch
    {
        AppLanguage.English => entry.En,
        AppLanguage.Japanese => entry.Ja,
        _ => entry.Zh,
    };

    public static IReadOnlyList<string> Keys() => All.Select(e => e.Key).ToList();
}
