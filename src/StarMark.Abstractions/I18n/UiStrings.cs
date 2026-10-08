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
        new Entry("kind.buttonCount", "类型筛选 · {0}", "Type filter · {0}", "種類で絞り込み · {0}"),
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
        new Entry("settings.shared.opacityScope", "只对『纯色』材质生效", "Only the Solid material uses this", "『単色』素材にだけ有効"),
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
        // 第七版（批次 WI-35）动的还是<b>覆盖面</b>：前两样（类型标题与展开后那排按钮）已改挂键，欠格清单里
        // 那半句必须一起删——这句是界面在报"还剩哪儿没翻"，留着它就是当着用户多报两处已翻好的地方（#297 同族）。
        
        // ===== 「常规」那一栏的正文（批次 WI-2d，P-169 ① 第二刀）=====
        // 中文列<b>逐字取自改键前 XAML 里的那一句</b>：这一刀只搬地方，不顺手改措辞——
        // 改了字就没有任何东西能证明"屏幕上还是原来那句话"，而这一屏是用户每天看的。
        new Entry("settings.general.themeTitle", "主题", "Theme", "テーマ"),
                // 「跟随系统」这一项与上面那颗语言下拉共用 settings.languageFollowSystem：
        // 两处写的是同一个决定，分成两行迟早只改一处。
        new Entry("settings.general.themeLight", "浅色", "Light", "ライト"),
        new Entry("settings.general.themeDark", "深色", "Dark", "ダーク"),

        new Entry("settings.general.appearanceTitle", "外观 · 主窗口材质与不透明度",
            "Appearance · main window material and opacity", "外観 · メインウィンドウの材質と不透明度"),
        new Entry("settings.general.appearanceNote", "只管主窗口，不碰组件", "Main window only; widgets keep their own setting", "メインウィンドウのみ。ウィジェットは別"),
                    new Entry("settings.general.mainBackdrop", "主窗口材质", "Main window material", "メインウィンドウの材質"),
        // 英文列里 "Opaque" 必须与下面 material.opaque 那句的开头一致：这句在指着那颗选项说话。
        // ⚠ 那句引用照抄的是**页签名**：可达性树现场读出的 TabItem 名字就是「桌面工具（组件）」，
        // 而这一栏第一张卡的标题与它逐字相同（settings.widgets.title），所以两个名字不是两件事。
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
        
        new Entry("settings.general.trayTitle", "托盘与呼出", "Tray and summoning", "トレイと呼び出し"),
        new Entry("settings.general.autostart", "开机自动启动 StarMark", "Start StarMark at sign-in", "Windows 起動時に StarMark を自動起動"),
                new Entry("settings.general.trayResident", "常驻系统托盘", "Stay in the system tray", "システムトレイに常駐"),
                new Entry("settings.general.globalHotkeys", "启用全局快捷键", "Global hotkeys", "グローバルショートカットキーを有効化"),
        new Entry("settings.general.globalHotkeysNote", "关掉后所有快捷键失效，只能从托盘呼出", "Off disables every hotkey — summon the app from the tray", "切ると全ショートカット無効。トレイから起動できます"),
                    new Entry("settings.general.minimizeToTray", "关闭窗口时最小化到托盘", "Minimize to tray on close", "ウィンドウを閉じたらトレイに最小化"),
        
        new Entry("settings.general.githubTitle", "GitHub Stars 同步", "GitHub Stars sync", "GitHub Stars 同期"),
        new Entry("settings.general.githubToken", "Personal Access Token（可选，仅需 public_repo）",
            "Personal Access Token (optional, public_repo only)", "Personal Access Token（任意、public_repo のみ必要）"),
        new Entry("settings.general.githubUser", "用户名（可选，用于显示）", "Username (optional, for display)", "ユーザー名（任意、表示用）"),

        new Entry("settings.general.aboutTitle", "关于与更新", "About & updates", "情報と更新"),
        new Entry("settings.general.currentVersion", "当前版本", "Current version", "現在のバージョン"),
                new Entry("settings.general.autoCheck", "每天自动检查一次", "Check once a day", "毎日自動で一度チェック"),
        // 这一句是"自动那一发"与"点下去那一发"的边界，两列都必须把两件事分开说：
        // 只有 UpdateApplyWiringGateTests 那两格钉着它不许变回全局承诺（"本程序不做下载安装"）。
        new Entry("settings.general.autoCheckNote", "自动那一发只问发布了哪一版；只有点「立即更新」才真的下载并替换", "The automatic pass only asks which version was published; only “Update now” downloads and replaces files", "自動の回は公開バージョンを確認するだけ。「今すぐ更新」を押して初めてダウンロードと差し替えが行われます"),
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
        new Entry("settings.hotkeys.recordNote", "点按钮后按键，最多 3 个；Esc 结束或取消", "Press the button, then up to 3 keys; Esc ends or cancels", "ボタン後、最大3鍵。Esc で終了または中止"),
                    new Entry("settings.hotkeys.saveNote", "按『保存快捷键』才生效；Backspace 清除", "Live only after Save hotkeys; Backspace clears that action", "「保存」で有効に。Backspace で解除"),
                    new Entry("settings.hotkeys.conflictNote", "同一颗键可挂多个动作，触发时依次跑", "One key can drive several actions; they run in order", "同じキーに複数動作。発動時は順に実行"),
                    new Entry("settings.hotkeys.save", "保存快捷键", "Save hotkeys", "ショートカットキーを保存"),
        new Entry("settings.hotkeys.discard", "撤销更改", "Discard changes", "変更を取り消す"),
        new Entry("settings.hotkeys.reset", "恢复默认快捷键", "Restore factory hotkeys", "既定のショートカットキーに戻す"),
        new Entry("settings.hotkeys.unsaved",
            "⚠ 有未保存的快捷键改动，点击「保存快捷键」后才会生效。",
            "⚠ Some hotkey changes aren't saved yet — they only register once you press “Save hotkeys”.", "⚠ 未保存のショートカットキー変更があります。「ショートカットキーを保存」を押した後で有効になります。"),
        new Entry("settings.hotkeys.retry", "重试注册", "Try registering again", "登録を再試行"),
        new Entry("settings.hotkeys.mapTitle", "动作 → 快捷键", "Action → hotkey", "アクション → ショートカットキー"),
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
        new Entry("settings.data.backupNote", "笔记、标签、隐藏/置顶与组件数据不可重建", "Notes, tags, hidden/pinned states and widget data cannot be rebuilt", "メモ・タグ・非表示/ピン留め・ウィジェットデータは再構築できません"),
                    new Entry("settings.data.autoBackupNote", "按下面的间隔自己存一份，只清自动件", "Saves one copy per interval below; only automatic copies are pruned", "下の間隔で自動保存。自動分だけ整理します"),
                    new Entry("settings.data.autoBackup", "自动备份", "Automatic backups", "自動バックアップ"),
        new Entry("settings.data.autoBackupOffNote", "关掉后只剩手动导出与导入前快照可回滚", "With it off, only your exports and pre-restore snapshots remain", "切ると手動書き出しと復元前スナップショットだけが残ります"),
                    new Entry("settings.data.autoBackupInterval", "自动备份间隔", "Backup interval", "自動バックアップの間隔"),
        new Entry("settings.data.autoBackupIntervalTip",
            "两份自动件之间的最小间隔。应用开着时每 10 分钟问一次「到点没」，所以实际落盘最多比到点晚十分钟。保留份数是固定的，间隔越短能回溯的天数就越少——这笔账由下面那行按你选的档算给你看。",
            "The minimum gap between two automatic copies. While the app is open it asks “is it time?” every 10 minutes, so a copy can land up to ten minutes late. How many copies are kept is fixed — the shorter the interval, the fewer days back you can reach; the line below works that out for the interval you picked.", "自動分同士の最小間隔。アプリが開いている間は 10 分ごとに「時刻が来たか」を聞くので、実際の保存は最大 10 分遅れます。保存数は固定なので、間隔を短くするほど遡れる日数が減ります——この帳尻は下の行が選んだ段階で計算して見せます。"),
        new Entry("settings.data.restoreSnapshotNote", "导入前自动留一份，可就地回滚", "An import takes its own snapshot first, so it can be undone", "取り込み前に自動で残すので、その場で戻せます"),
                    new Entry("settings.data.elevatedNote", "管理员会话里自动改用应用内输入框，不用换权限", "In an elevated session export and import use the in-app field — no permission changes", "管理者セッションではアプリ内の入力欄に自動切替。権限変更は不要"),
                    new Entry("settings.data.includeImages", "导出时带上剪贴板图片本体", "Include clipboard image files when exporting", "バックアップにクリップボード画像本体も含める"),
        new Entry("settings.data.includeImagesNote", "开着打包成 .zip 会明显变大；关只带条目与文件名，缺的标「文件缺失」", "Images make the .zip noticeably larger; off carries file names only and gone ones read as file missing", "含めると .zip がかなり大きくなります。オフは項目とファイル名だけで、無いものは「ファイル欠損」の表示"),
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
        new Entry("settings.data.clipboardNote", "密码管理器内容、私钥/令牌/卡号一律不入库", "Password-manager copies and key / token / card shapes never enter the store", "パスワード管理アプリの内容・秘密鍵/トークン/カード番号は一切登録しません"),
                    new Entry("settings.data.clipboardImageNote", "图片没法做敏感检查：开着＝屏幕内容全记", "Images can't be checked for sensitive content, so this captures whatever appears on screen", "画像は機密検査不可。オンだと画面のものが全部対象です"),
                    new Entry("settings.data.imageMax", "图片最多留多少条", "How many images to keep", "画像の最大保存数"),
        new Entry("settings.data.imageMaxNote", "一张 4K 截图常有几百 KB", "A 4K screenshot is often a few hundred KB", "4K スクリーンショットは数百 KB になることがあります"),
                    new Entry("settings.data.textMax", "文本最多留多少条", "How many text entries to keep", "テキストの最大保存数"),
        new Entry("settings.data.textMaxNote", "正文过长只存前 32 KB", "Long bodies keep only their first 32 KB", "本文が長い場合は先頭 32 KB だけ保存"),
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
        new Entry("settings.extend.trendingNote", "打开那一页才抓；条目默认不进库", "Fetched only when you open that page; entries stay out of the library", "その頁を開いた時だけ取得。項目は既定でライブラリに入りません"),
                    new Entry("settings.extend.glanceNote", "在速览组件里显示热榜块", "Shows a trending block in the glance widget", "今日のまとめにトレンドブロックを表示"),
                    new Entry("settings.extend.glanceOn", "显示", "Show", "表示する"),
        new Entry("settings.extend.glanceOff", "不显示", "Hide", "表示しない"),
                new Entry("settings.extend.openTrending", "打开热榜页", "Open the trending page", "トレンドページを開く"),
        new Entry("settings.extend.rssTitle", "网址来源（RSS / Atom）", "Feed sources (RSS / Atom)", "URL ソース（RSS / Atom）"),
        new Entry("settings.extend.rssNote", "没有后台定时：到那一页点刷新才抓", "No background timer — it fetches when you refresh that page", "バックグラウンド定時はなし。あの頁で更新を押した時だけ"),
                    new Entry("settings.extend.rssEmpty", "还没有添加任何来源地址。", "No feed addresses added yet.", "まだソースのアドレスが一件もありません。"),
        new Entry("settings.extend.rssOn", "启用", "Enable", "有効"),
        new Entry("settings.extend.rssOff", "停用", "Disable", "無効"),
        new Entry("settings.extend.rssUrlPlaceholder", "订阅地址（https://…/feed）", "Feed address (https://…/feed)", "購読アドレス（https://…/feed）"),
        new Entry("settings.extend.rssNamePlaceholder", "名字（留空＝用域名）", "Name (blank = use the domain)", "名前（空欄＝ドメインを使う）"),
        new Entry("settings.extend.add", "添加", "Add", "追加"),
        new Entry("settings.extend.openRss", "打开 RSS 页", "Open the RSS page", "RSS ページを開く"),
        new Entry("settings.extend.diskSearchTitle", "本地磁盘搜索", "Local disk search", "ローカルディスク検索"),
        new Entry("settings.extend.diskSearchNote", "第一次开会装搜索引擎，可能弹一次授权", "The first time it installs the search engine; Windows may ask for consent once", "初回は検索エンジンを導入。Windows が一度確認する場合があります"),
                    new Entry("settings.extend.engineUsage", "本地搜索引擎占用", "Search engine footprint", "ローカル検索エンジンの使用量"),
        new Entry("settings.extend.openEngineFolder", "打开所在目录", "Open its folder", "あるフォルダーを開く"),
        new Entry("settings.extend.engineDeleteNote", "只删搜索引擎，书签/Star/标签不动", "Removes only the search engine; bookmarks, Stars and tags stay", "検索エンジンだけ削除。ブックマーク/Star/タグは無傷"),
                    new Entry("settings.extend.cleanMissing", "清掉这些行", "Clear those rows", "これらの行を消す"),
        new Entry("settings.extend.rescan", "重新扫描本机文件条目", "Rescan local file entries", "本機のファイル条目を再スキャン"),
        new Entry("settings.extend.captureTitle", "截屏（截图 / 贴图 / 识字）", "Screen capture (snip / pin / OCR)", "スクリーンショット（切り取り / 貼り画像 / 文字認識）"),
        new Entry("settings.extend.captureNote", "关后三条热键交还系统；已钉的贴图不受影响", "Off returns the three hotkeys; pins already on the desktop are unaffected", "切ると3つのキーを返します。既に貼った画像はそのまま"),
                    new Entry("settings.extend.canvasTitle", "屏幕画布", "Screen canvas", "画面キャンバス"),
        new Entry("settings.extend.canvasNote", "进去是穿透态：点下层应用不受影响", "It starts click-through — the windows below still work", "開始時は透過状態。下のウィンドウも操作できます"),
                    new Entry("settings.extend.canvasInShotNote", "关＝截图前先收起画布：笔迹还在，图里没有", "Off lifts the canvas during the grab: your drawing stays, it just isn't in the shot", "オフは切り取り前にキャンバスを隠します。描画は残り、写りません"),
                    new Entry("settings.extend.canvasInShotOn", "带上", "Include", "載せる"),
        new Entry("settings.extend.canvasInShotOff", "不带", "Skip", "載せない"),
        new Entry("settings.extend.glowTitle", "光标那块圆多大", "How big the cursor circle is", "カーソルのあの丸の大きさ"),
        new Entry("settings.extend.glowNote", "一根滑杆同时管画布光晕与幕布亮区", "One slider drives both the cursor halo and the curtain's bright area", "スライダー一本で光暈と暗幕の明るい部分を制御"),
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
        new Entry("settings.widgets.note", "标题栏的关闭只是从桌面撤下，这里能加回来", "A widget's close button only takes it off the desktop; add it back here", "閉じるはデスクトップから下ろすだけ。ここで再追加できます"),
                    new Entry("settings.widgets.showAll", "全部显示", "Show all", "すべて表示"),
        new Entry("settings.widgets.hideAll", "全部隐藏", "Hide all", "すべて隠す"),
        new Entry("settings.widgets.startupTitle", "开机自动加载组件", "Load widgets at startup", "起動時にウィジェットを自動で読み込む"),
        new Entry("settings.widgets.startupNote", "只影响开机那一次，已添加的不会丢", "Affects boot only; added widgets are not lost", "起動時のみ。追加済みは消えません"),
                    new Entry("settings.widgets.loadOn", "加载", "Load", "読み込む"),
        new Entry("settings.widgets.loadOff", "不加载", "Don't load", "読み込まない"),
        new Entry("settings.widgets.appearanceTitle", "组件外观 · 材质与不透明度", "Widget look · material and opacity", "ウィジェットの外観 · 材質と不透明度"),
        new Entry("settings.widgets.appearanceNote", "只管组件，不碰主窗口", "Widgets only; the main window keeps its own setting", "ウィジェットのみ。メインウィンドウは別"),
                    new Entry("settings.widgets.materialLabel", "组件材质", "Widget material", "ウィジェットの素材"),
                new Entry("settings.widgets.opacityLabel", "组件背景不透明度（仅纯色材质）",
            "Widget background opacity (Solid only)", "ウィジェット背景の不透明度（単色素材のみ）"),
                new Entry("settings.widgets.snapTitle", "边缘磁吸", "Edge snapping", "縁へのスナップ"),
        new Entry("settings.widgets.snapRowLabel", "拖动 / 缩放时自动吸附对齐", "Snap into line while dragging / resizing", "ドラッグ / リサイズ時に自動でスナップして揃える"),
        new Entry("settings.widgets.snapStrengthLabel", "磁吸强度", "Snap strength", "スナップの強さ"),
        new Entry("settings.widgets.snapStrengthNote", "越大越容易吸住", "Higher sticks sooner", "大きいほど吸い付きやすい"),
                    new Entry("settings.widgets.snapSpacingLabel", "对齐间距", "Snap gap", "揃えた後の間隔"),
        new Entry("settings.widgets.snapSpacingNote", "0＝边边紧贴", "0 means edge to edge", "0＝縁がぴったり"),
                            new Entry("settings.widgets.perfTitle", "性能模式 / 内存门禁", "Performance mode / memory gates", "パフォーマンスモード / メモリ閾値"),
        new Entry("settings.widgets.perfNote", "选『自定义』才展开下面那两格", "Only Custom shows the two fields below", "『カスタム』で下の2欄が表示されます"),
                    new Entry("settings.widgets.perfLabel", "性能模式", "Performance mode", "パフォーマンスモード"),
        new Entry("settings.widgets.perfBalanced", "均衡（默认，体验优先）", "Balanced (default, responsiveness first)", "バランス（既定、体験優先）"),
        new Entry("settings.widgets.perfSavings", "省资源（低内存 / 笔记本）", "Saver (low memory / laptops)", "節約（低メモリ / ノートパソコン）"),
        new Entry("settings.widgets.perfCustom", "自定义", "Custom", "カスタム"),
        new Entry("settings.widgets.budgetLabel", "进程内存预算（MB）", "Process memory budget (MB)", "プロセスのメモリ予算（MB）"),
        new Entry("settings.widgets.budgetNote", "超过即触发回收", "Reclaim starts once this is exceeded", "超えた時点で回収を開始します"),
        new Entry("settings.widgets.cacheLabel", "缓存最大条目数", "Cache entry limit", "キャッシュの最大保持数"),
                new Entry("settings.widgets.layoutTitle", "布局方案", "Layout presets", "レイアウト方案"),
        new Entry("settings.widgets.layoutNote", "存的是位置与外观，不含条目数据", "Saves positions and appearance, never item data", "位置と外観を保存し、項目データは含みません"),
                    new Entry("settings.widgets.applyLayout", "应用", "Apply", "適用"),
        new Entry("settings.widgets.noLayouts", "（暂无保存的布局）", "(no saved layouts yet)", "（保存済みのレイアウトはまだありません）"),
        // 批次 WI-35：「桌面工具」那一栏由 code-behind 现建的那排字（每一类标题下展开后的三颗按钮、
        // 实例那一行的序号与置顶尾巴、标题上那颗计数）。实例行用<b>一颗骨架＋一颗尾巴</b>而不是两句整话：
        // "实例 3（置顶）" 的尾巴在哪种语言里都贴着序号，拆成两句就得在代码里拼分隔符（P-171 那个形状）。
        new Entry("settings.widgets.addRow", "添加组件", "Add widget", "ウィジェットを追加"),
        new Entry("settings.widgets.showOne", "显示", "Show", "表示"),
        new Entry("settings.widgets.removeOne", "移除", "Remove", "削除"),
        new Entry("settings.widgets.instanceLine", "实例 {0}{1}", "Instance {0}{1}", "インスタンス {0}{1}"),
        new Entry("settings.widgets.instanceTopmost", "（置顶）", " (on top)", "（最前面）"),
        new Entry("settings.widgets.noneAdded", "未添加", "None added", "未追加"),
        new Entry("settings.widgets.addedCount", "已添加 {0} 个", "{0} added", "{0} 件追加済み"),
        new Entry("settings.widgets.deleteLayoutTitle", "删除布局", "Delete layout", "レイアウトを削除"),
        new Entry("settings.widgets.deleteLayoutBody",
            "确定删除布局「{0}」？绑定给它切换的快捷键也会同时失效。",
            "Delete the layout “{0}”? The hotkey bound to switch to it stops working too.",
            "レイアウト「{0}」を削除しますか？それに割り当てたショートカットキーも無効になります。"),
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
        new Entry("settings.ai.noKeyNote", "不配 Key 也能用：AI 只管批量整理标签", "The app works without a key: AI only does bulk tag organising", "Key なしでも使えます。AI はタグの一括整理だけ"),
                    new Entry("settings.ai.whatToFill", "每一项分别要填什么", "What each field is for", "各項目の意味"),
        new Entry("settings.ai.title", "AI 助手", "AI assistant", "AI アシスタント"),
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
        new Entry("settings.ai.applyGroup", "应用这组", "Apply this group", "このグループを適用"),
        new Entry("settings.ai.rejectGroup", "不要", "Skip", "使わない"),

        // ===== 对话框与列表上那几个共用动词 =====
        // 与 settings.general.cancelUpdate 不是一句：那颗管的是"停掉正在跑的下载／替换"，这颗只是"不做了"。
        new Entry("dialog.cancel", "取消", "Cancel", "キャンセル"),
        new Entry("dialog.delete", "删除", "Delete", "削除"),
        new Entry("dialog.refresh", "刷新", "Refresh", "更新"),
        new Entry("dialog.retry", "重试", "Retry", "再試行"),
        new Entry("dialog.save", "保存", "Save", "保存"),
        new Entry("dialog.add", "添加", "Add", "追加"),
        new Entry("dialog.close", "关闭", "Close", "閉じる"),
        new Entry("state.title", "{0}没生效", "{0} didn't take effect", "{0}は反映されませんでした"),
        new Entry("state.rowGone", "{0}没落上去：这条已经不在这台机器的库里了（可能在别处被删掉）。刷新这一页就能看到它的现状。", "{0} didn't stick: this row is no longer in this machine's library (it may have been deleted elsewhere). Refresh this page to see its current state.", "{0}は反映されませんでした：この行はこの端末のライブラリにありません（別の場所で削除された可能性があります）。ページを更新すると現状がわかります。"),
        new Entry("card.menu.openLoc", "打开所在位置", "Open location", "場所を開く"),
        new Entry("card.menu.copyImage", "复制图片", "Copy image", "画像をコピー"),
        new Entry("card.menu.preview", "预览", "Preview", "プレビュー"),
        new Entry("card.menu.record", "记录到本地", "Save to library", "ローカルに記録"),
        new Entry("card.menu.sendTo", "发送到桌面 · 快捷启动", "Send to desktop · Quick launch", "デスクトップへ送る · クイック起動"),
        new Entry("card.menu.focus", "开始专注这一项", "Focus on this item", "この項目に集中する"),
        new Entry("card.tip.pinned", "已置顶", "Pinned", "ピン留め済み"),
        new Entry("card.tip.size", "文件大小", "File size", "ファイルサイズ"),
        new Entry("card.tip.tagOpen", "查看该标签的全部条目", "Show items with this tag", "このタグの項目を表示"),
        new Entry("card.tip.tagRemove", "移除标签", "Remove tag", "タグを削除"),
        new Entry("card.tip.tagAdd", "添加标签", "Add tag", "タグを追加"),
        new Entry("card.tip.tagPlus", "＋ 标签", "+ Tag", "＋ タグ"),
        new Entry("card.tip.preview", "预览（QuickLook）", "Preview (QuickLook)", "プレビュー（QuickLook）"),
        new Entry("card.tip.pin", "置顶（浏览视图排最前）", "Pin (top of browse view)", "ピン留め（一覧の先頭）"),
        new Entry("card.hide.show", "显示", "Show", "表示"),
        new Entry("card.hide.hide", "隐藏", "Hide", "非表示"),
        new Entry("card.pin.pin", "置顶", "Pin", "ピン留め"),
        new Entry("card.pin.unpin", "取消置顶", "Unpin", "ピン留めを解除"),
        new Entry("card.open.open", "打开", "Open", "開く"),
        new Entry("card.open.asCopy", "复制到剪贴板", "Copy to clipboard", "クリップボードにコピー"),
        new Entry("card.copy.link", "复制链接/路径", "Copy link or path", "リンク／パスをコピー"),
        new Entry("card.copy.content", "复制内容", "Copy content", "内容をコピー"),
        new Entry("card.delete.fileRow", "删除这一行", "Remove this row", "この行を削除"),
        new Entry("card.delete.history", "删除这条历史", "Delete this history", "この履歴を削除"),
        new Entry("card.pinImage.ok", "贴到桌面", "Pin to desktop", "デスクトップへ貼り付け"),
        // 「那张图的文件不在了」一族（批次 WI-2y，P-172 第五行＋灰路同批收回）：事实半句一颗、宿主骨架各一颗。
        // 骨架按各语言自己的句法排——{0} 是完整从句，硬套中文属格位在日英会散架（那正是豁免被收回的理由）。
        new Entry("clip.miss.clause", "文件已不在本机", "the file is no longer on this device", "ファイルがこの PC 上に存在しません"),
        new Entry("clip.miss.reuse", "这条图片的{0}，复制不回去（条目仍保留，可置顶或删除）", "This image cannot be copied back — {0}. The entry stays; you can pin it or delete it.", "この画像は{0}ため、コピーし戻せません（項目は残ります — ピン留めか削除ができます）。"),
        new Entry("clip.miss.banner", "图片{0}（条目仍保留，可直接删掉这一条；这一张复制不回去）", "{0} — the entry stays and you can delete this row, but this picture cannot be copied back.", "{0}（項目は残っています。この行は削除できますが、この画像はコピーし戻せません）"),
        new Entry("card.missRow", "本机的这个{0}（{1}），打开没有东西可开——条目还留在库里，可以删掉这一行", "{0} — this local entry cannot be opened ({1}). The record stays in your library; you can delete this row.", "{0}（{1}）— 開ける対象がありません。記録はライブラリに残っており、この行を削除できます。"),
        new Entry("card.pinImage.missing", "贴到桌面（这张图的{0}）", "Pin to desktop — {0}", "デスクトップへ貼り付け — {0}"),
        // 「剪贴板历史页」状态行与空态（批次 WI-30，P-172 该行兑现）：状态行的话在动作那一刻现取＝当前语言；
        // 暂停那颗是 ToggleButton 绑定（只拿 bool，提示语走 code-behind 写状态行——挂键会成双主人，#276 口径）。
        new Entry("clip.empty.off", "剪贴板历史未开启：到「设置 → 剪贴板历史」打开后，从这里复制过的内容会自动出现在这里。", "Clipboard history is off. Turn it on under Settings → Clipboard History, and anything you copy will show up here.", "クリップボード履歴はオフです。「設定 → クリップボード履歴」でオンにすると、コピーした内容がここに自動表示されます。"),
        new Entry("clip.empty.none", "还没有记录到任何复制内容。", "No copies have been recorded yet.", "コピーの記録はまだありません。"),
        new Entry("clip.status.load", "加载失败：{0}", "Failed to load: {0}", "読み込みに失敗しました: {0}"),
        new Entry("clip.status.noBody", "这条历史没有可复制的正文", "This entry has no text to copy", "この履歴にはコピーできる本文がありません"),
        new Entry("clip.status.copied", "已复制到剪贴板：{0}", "Copied to the clipboard: {0}", "クリップボードにコピーしました: {0}"),
        new Entry("clip.status.copyFail", "复制失败：{0}", "Copy failed: {0}", "コピーに失敗しました: {0}"),
        new Entry("clip.status.imgFail", "这张图没能复制出去：{0}", "This image could not be copied out: {0}", "この画像をコピーできませんでした: {0}"),
        new Entry("clip.status.imgCopied", "已把图片复制回剪贴板：{0}（去目标程序按粘贴即可）", "Image back on the clipboard: {0} — paste it in the target app", "画像をクリップボードに戻しました：{0}（貼り付け先でペーストしてください）"),
        new Entry("clip.status.imgCopyFail", "图片复制失败：{0}", "Image copy failed: {0}", "画像のコピーに失敗しました: {0}"),
        new Entry("clip.status.clearFail", "清空失败：{0}（列表未变动，可再点一次）", "Clear failed: {0} — the list is unchanged; you can try again", "消去に失敗しました: {0}（一覧は変更されていません。再試行できます）"),
        new Entry("clip.status.cleared", "已清空 {0} 条剪贴板历史", "Cleared {0} clipboard history entries", "クリップボード履歴 {0} 件を消去しました"),
        new Entry("clip.pause.on", "已暂停记录：期间不再写入新内容。要恢复请再点一次。", "Recording is paused: nothing new is captured until you switch it back on.", "記録を一時停止しました。再開するにはもう一度押してください。"),
        new Entry("clip.pause.off", "已恢复记录。", "Recording is back on.", "記録を再開しました。"),
        new Entry("clip.dialog.clearTitle", "清空剪贴板历史", "Clear clipboard history", "クリップボード履歴を消去"),
        new Entry("clip.dialog.clearBody", "将删除本机记录的 {0} 条复制内容（含你置顶的条目），删除后无法恢复。书签 / Star / 待办 / 随记不受影响。", "This deletes {0} recorded copies on this machine — including pinned entries — and cannot be undone. Bookmarks, Stars, todos and notes are untouched.", "この PC の記録 {0} 件（ピン留めを含む）を削除すると戻せません。ブックマーク / Star / 待办 / メモは影響を受けません。"),
        new Entry("clip.dialog.clearGo", "清空", "Clear", "消去"),
        // 今日速览的月题（批次 WI-31，P-172 收官）：主行与月历头部**同一个键**——此前两处各写一份
        // $"{年}年{月}月"，正是"同一句话两个主人"的现行案。en 走"2026/10"：不补零、不造 12 颗月名装平行
        //（组件位读得出就够；带格式的 {1:00} 会让占位符闸门眼盲——形状守闸门的，不是反过来）。
        new Entry("widget.glance.ym", "{0}年{1}月", "{0}/{1}", "{0}年{1}月"),
        // 健康页四行统计的外壳（批次 WI-32，P-169 组合句一族的统计半）：<b>整句进表、读数当实参</b>——
        // 旧排法把「语言 Top：」当标记半句、读数另绑一颗，正是 languageNote 点名的那种拆句。
        new Entry("health.stat.lang", "语言 Top：{0}", "Top languages: {0}", "言語 Top：{0}"),
        new Entry("health.stat.domains", "独立域名：{0}", "Distinct domains: {0}", "独立ドメイン：{0}"),
        new Entry("health.stat.dup", "疑似重复 Top：{0}", "Top likely duplicates: {0}", "重複候補 Top：{0}"),
        new Entry("health.stat.tag", "标签 Top：{0}", "Top tags: {0}", "タグ Top：{0}"),
        new Entry("health.stat.none", "（无）", "(none)", "（なし）"),
        // 健康页扣分文字整链（批次 WI-33，组合句一族的下半）：因子名/明细/外壳/档位/摘要全进表——
        // Core 从此交 LocMessage（键＋实参），界面在重算那一刻渲染；"英文骨架夹中文肉"在这页没有落脚处。
        new Entry("health.factor.duplicates", "疑似重复条目", "Likely duplicates", "重複の疑い"),
        new Entry("health.detail.duplicates", "{0} 组疑似重复（如：{1}）", "{0} likely-duplicate groups (e.g. {1})", "{0} 件の重複候補（例：{1}）"),
        new Entry("health.factor.untagged", "未打标签比例偏高", "Too many items without tags", "タグ未設定の割合が高い"),
        new Entry("health.detail.untagged", "{0}/{1}（{2}）的条目没有标签", "{0}/{1} items ({2}) have no tags", "{0}/{1} 件（{2}）にタグがありません"),
        new Entry("health.factor.stale", "大量条目长期未整理", "Many items untouched for a long time", "長期未整理の項目が多い"),
        new Entry("health.detail.stale", "{0}/{1}（{2}）超过 {3} 天未更新", "{0}/{1} ({2}) not updated for over {3} days", "{0}/{1} 件（{2}）が {3} 日以上更新されていません"),
        new Entry("health.deduction.row", "• {0}： -{1} 分（{2}）", "• {0}: -{1} pts ({2})", "• {0}： -{1} 点（{2}）"),
        new Entry("health.grade.good", "健康", "Healthy", "良好"),
        new Entry("health.grade.ok", "一般，可优化", "Fair — worth tidying", "まずまず、改善可"),
        new Entry("health.grade.bad", "需整理", "Needs tidying", "整理が必要"),
        new Entry("health.summary.good", "收藏整理良好，继续保持", "Collection is well kept — carry on", "コレクションは良好です。この調子で"),
        new Entry("health.summary.todo", "待优化：{0}", "To tidy: {0}", "これから整理：{0}"),
        new Entry("health.join.list", "、", ", ", "、"),
        new Entry("health.error", "健康度计算失败：{0}", "Health calculation failed: {0}", "健康度計算に失敗しました: {0}"),
        new Entry("card.open.fail", "打不开这一行", "Can't open this row", "この行を開けません"),
        new Entry("card.deleteRow.remove", "从库里删掉这一行", "Remove it from the library", "ライブラリからこの行を削除"),
        new Entry("card.deleteRow.done", "已删掉这一行", "Row removed", "行を削除しました"),
        new Entry("card.deleteRow.already", "这一行本来就不在库里了", "This row is no longer in the library", "この行はすでにライブラリにありません"),
        new Entry("card.deleteRow.doneBody", "磁盘上的文件没有被改动（它本来也已经不在了）。", "The file on disk was left untouched (it was already gone).", "ディスク上のファイルには触れていません（もともと存在しません）。"),
        new Entry("card.deleteRow.alreadyBody", "可能刚被清掉过；刷新一下就看不到它了。", "It may have just been cleared; refresh and it will be gone.", "直前に整理された可能性があります。更新すると表示されなくなります。"),
        new Entry("card.delete.fail", "删除失败", "Delete failed", "削除できませんでした"),
        new Entry("card.delete.notInLib", "这条历史还没入库，没有可删除的记录。", "Not in the library yet, nothing to delete.", "まだライブラリにないため、削除する記録がありません。"),
        new Entry("card.delete.gone", "这条记录已经不在历史里了（可能刚被清空或被新内容挤掉）。", "No longer in the history (it may have been cleared or pushed out by newer items).", "もう履歴にありません（消去されたか、新しい項目に押し出された可能性があります）。"),
        new Entry("card.copyImage.fail", "没能复制图片", "Couldn't copy the image", "画像をコピーできませんでした"),
        new Entry("card.pinImage.fail", "没能贴出这张图", "Couldn't pin this image", "この画像を貼り付けできませんでした"),
        new Entry("card.image.unreadable", "这张图读不出来。", "The image can't be read.", "画像を読み取れませんでした。"),
        new Entry("card.note.title", "编辑笔记", "Edit note", "ノートを編集"),
        new Entry("card.note.placeholder", "写下笔记...", "Write a note...", "ノートを書く..."),
        new Entry("card.note.save", "保存笔记", "Save note", "ノートを保存"),
        new Entry("card.tags.title", "编辑标签", "Edit tags", "タグを編集"),
        new Entry("card.tags.quickAdd", "快速添加标签", "Quick-add tags", "タグをクイック追加"),
        new Entry("card.restore.word", "还原（取消隐藏）", "Restore (unhide)", "復元（非表示を解除）"),
        new Entry("guide.page.search", "搜索", "Search", "検索"),
        new Entry("guide.page.tree", "文件夹", "Folders", "フォルダー"),
        new Entry("guide.page.tags", "标签", "Tags", "タグ"),
        new Entry("guide.page.activity", "动态", "Activity", "アクティビティ"),
        new Entry("guide.page.hidden", "隐藏", "Hidden", "非表示"),
        new Entry("guide.page.snapshot", "快照", "Snapshots", "スナップショット"),
        new Entry("guide.page.clipboard", "剪贴板", "Clipboard", "クリップボード"),
        new Entry("guide.page.trending", "热榜", "Trending", "トレンド"),
        new Entry("guide.page.rss", "RSS", "RSS", "RSS"),
        new Entry("guide.page.settings", "设置", "Settings", "設定"),
        new Entry("guide.page.search.1", "在顶部搜索框里输入就进到这一页：本地文件、书签、Star、动态都在结果里，按类型分块。", "Type into the search box at the top and you land here: local files, bookmarks, stars and activity all show up in the results, grouped by type.", "上部の検索ボックスに入力するとこのページに移ります：ローカルファイル・ブックマーク・スター・アクティビティが種類ごとに分かれて表示されます。"),
        new Entry("guide.page.search.2", "支持 ext:（扩展名）、size:（大小）、dm:（改动日期）、folder:（目录）这类过滤词，语法提示就在搜索框的占位文字里。", "Filters like ext: (extension), size: (size), dm: (modified date) and folder: (directory) are supported; the syntax hints live in the search box placeholder.", "ext:（拡張子）・size:（サイズ）・dm:（更新日）・folder:（フォルダー）などのフィルターに対応しています。書式のヒントは検索ボックスのプレースホルダーにあります。"),
        new Entry("guide.page.search.3", "↑↓ 选结果、Enter 打开、Ctrl+Enter 打开它所在的位置；一行右键还有复制路径、记录到本地、拖出去。", "↑↓ to pick a result, Enter to open it, Ctrl+Enter to open its location; right-click a row for copy path, save to library and drag out.", "↑↓ で結果を選び、Enter で開き、Ctrl+Enter で場所を開きます。行を右クリックするとパスのコピー・ライブラリに記録・ドラッグアウトもできます。"),
        new Entry("guide.page.tree.1", "把常用目录钉进来，浏览它们的文件与文件夹；条目上右键就是动作：打开、所在目录、置顶、打标签、隐藏、加笔记。", "Pin the folders you use often and browse their files; right-click an item for actions: open, containing folder, pin, tag, hide, add note.", "よく使うフォルダーをピン留めして中のファイルを閲覧できます。項目を右クリックすると、開く・場所を開く・ピン留め・タグ付け・非表示・ノート追加の操作ができます。"),
        new Entry("guide.page.tree.2", "文件也可以从资源管理器直接拖进桌面的「快捷启动」组件，拖进来即成为一条入口。", "You can also drag files straight from Explorer onto the desktop Quick launch widget; dropping them creates an entry.", "エクスプローラーからデスクトップの「クイック起動」ウィジェットへ直接ドラッグしても登録できます。"),
        new Entry("guide.page.tree.3", "磁盘上已经不存在的条目会被标出来，这一页给一键清理，不让你点进一条死路径。", "Entries that no longer exist on disk are marked, and this page offers one-click cleanup so you never click into a dead path.", "ディスクに存在しなくなった項目は印が付きます。このページからワンクリックで整理でき、無効なパスを開こうとすることがありません。"),
        new Entry("guide.page.tags.1", "按标签看库：选一个或多个标签，下面列出带这些标签的条目；标签本身在这里新建与改名。", "Browse the library by tag: pick one or more tags and matching items are listed below; tags themselves are created and renamed here.", "タグでライブラリを絞り込みます。タグを一つ以上選ぶと、そのタグを持つ項目が下に表示されます。タグの作成と名前の変更もここでできます。"),
        new Entry("guide.page.tags.2", "条目上的标签与颜色都在右键菜单里；想一次整理一批，用设置「AI 增强」里的批量整理。", "An item's tags and colors live in its context menu; to tidy a batch at once, use the AI organize feature in Settings.", "項目のタグと色は右クリックメニューにあります。まとめて整理したいときは設定の「AI 機能」のバッチ整理を使います。"),
        new Entry("guide.page.activity.1", "最近的新增、修改、删除流水，只用来回看，不在这里改数据。", "A feed of recent additions, edits and deletions — for looking back only; nothing is edited here.", "最近の追加・変更・削除の履歴です。見返すだけのもので、ここでデータを変更することはありません。"),
        new Entry("guide.page.activity.2", "绿＝新增、黄＝修改、红＝删除。", "Green = added, yellow = changed, red = deleted.", "緑＝追加、黄＝変更、赤＝削除。"),
        new Entry("guide.page.hidden.1", "被隐藏的条目都收在这里，其它页不再显示它们。", "Hidden items are collected here and no longer show up on other pages.", "非表示にした項目はここに集まり、他のページには表示されなくなります。"),
        new Entry("guide.page.hidden.2", "要放回去：在任意一行右键选恢复，或直接把这条删掉。", "To bring one back: right-click any row and choose restore, or just delete the entry.", "戻すには：任意の行を右クリックして「復元」を選ぶか、その項目を削除します。"),
        new Entry("guide.page.snapshot.1", "存的是桌面组件的「布局 + 配置」，不含条目数据本身；换一套摆法之前先存一份，随时整桌还原。", "Saves the desktop widgets' layout plus configuration — not the item data itself; save one before rearranging and restore the whole desk anytime.", "デスクトップウィジェットの「レイアウト＋設定」を保存します（項目データ自体は含みません）。並べ替える前に保存しておけば、いつでも元に戻せます。"),
        new Entry("guide.page.snapshot.2", "也可以只提取布局（保留当前数据），或在组件右键里就地保存当前这一份。", "You can also extract just the layout (keeping current data), or save the current one in place from a widget's context menu.", "レイアウトだけを取り出す（現在のデータを残す）ことも、ウィジェットの右クリックから現在の状態を保存することもできます。"),
        new Entry("guide.page.clipboard.1", "本机剪贴板历史：文本与图片都能记。默认关着，开关、图片上限与轮转都在设置「拓展功能」。", "Local clipboard history: both text and images are captured. Off by default; the switch, image limits and rotation live in Settings under Extensions.", "この端末のクリップボード履歴です。テキストも画像も記録できます。既定ではオフで、スイッチ・画像の上限・ローテーションは設定の「拡張機能」にあります。"),
        new Entry("guide.page.clipboard.2", "一行右键：复制回去、打开、贴到桌面、发送到快捷启动。图片那行显示缩略图；文件已经不在这台机器上时会直接说出来，不放一张打不开的图占位。", "Right-click a row: copy back, open, pin to desktop, send to Quick launch. Image rows show a thumbnail; if the file is gone from this machine it says so instead of showing a broken image.", "行を右クリック：再コピー・開く・デスクトップへ貼り付け・クイック起動へ送る。画像の行にはサムネイルが表示され、ファイルがこの端末にない場合はその旨がそのまま示されます。"),
        new Entry("guide.page.clipboard.3", "这条链不上传任何内容，记录都在这台机器的文件里。", "This pipeline uploads nothing; the records stay in files on this machine.", "この仕組みは一切アップロードせず、記録はすべてこの端末のファイルの中にあります。"),
        new Entry("guide.page.trending.1", "GitHub 仓库热榜，可以直接在这里 star 或取消 star。", "GitHub repository trending — you can star or unstar right here.", "GitHub リポジトリのトレンドです。ここで直接スターの付け外しができます。"),
        new Entry("guide.page.trending.2", "这一页默认不显示，开关在设置；star 需要一个有权限的 GitHub 令牌，填在设置的凭据格里。", "This page is hidden by default; the switch is in Settings. Starring needs a GitHub token with permission, entered in the credentials field in Settings.", "このページは既定で非表示です。スイッチは設定にあります。スターには権限のある GitHub トークンが必要で、設定の資格情報欄に入力します。"),
        new Entry("guide.page.rss.1", "订阅源阅读：条目可以收藏进库，收藏后就出现在「文件夹」与「标签」那些页里。", "Feed reading: entries can be collected into the library, after which they appear on the Folders and Tags pages.", "フィードの閲覧です。項目はライブラリに收藏でき、收藏後は「フォルダー」や「タグ」のページにも現れます。"),
        new Entry("guide.page.rss.2", "添加与管理源在设置「拓展功能」；总开关关着时这一页不显示。", "Add and manage feeds in Settings under Extensions; when the master switch is off this page is hidden.", "フィードの追加と管理は設定の「拡張機能」で行います。マスタースイッチがオフのとき、このページは表示されません。"),
        new Entry("guide.page.settings.1", "所有开关都在这几栏里：常规（主题、材质、托盘、更新）、AI 增强、桌面工具、拓展功能、快捷键、数据、健康与诊断。", "Every switch lives in these tabs: General (theme, material, tray, updates), AI, Desktop tools, Extensions, Hotkeys, Data, Health & diagnostics.", "すべてのスイッチはこのタブにあります：一般（テーマ・マテリアル・トレイ・更新）、AI 機能、デスクトップツール、拡張機能、ショートカット、データ、健康と診断。"),
        new Entry("guide.page.settings.2", "「数据」栏管备份与导出，也管这台机器上的库文件在哪；「快捷键」栏能逐条改键，冲突只提示不挡你注册。", "The Data tab handles backup and export, and where the library files live on this machine; the Hotkeys tab edits keys one by one — conflicts only warn, never block.", "「データ」タブはバックアップと書き出し、そしてこの端末のライブラリファイルの場所を管理します。「ショートカット」タブではキーを一つずつ変更でき、競合は警告のみで登録は妨げられません。"),
        new Entry("guide.page.settings.3", "这一栏（使用说明）随时能重看首启那份引导。", "This tab (the guide) lets you re-read the first-run walkthrough anytime.", "このタブ（使い方ガイド）から、初回起動のガイドはいつでも見直せます。"),
        new Entry("guide.first.title", "第一次打开 StarMark，先知道这几件事", "First time opening StarMark — a few things worth knowing", "初めて StarMark を開くとき、まず知っておきたいこと"),
        new Entry("guide.first.s1.title", "它是什么", "What it is", "これは何か"),
        new Entry("guide.first.s1.1", "一个本机资料库加一组桌面小工具：文件与文件夹、书签、GitHub、热榜、RSS、剪贴板历史、截图与屏幕画布。", "A local library plus a set of desktop widgets: files and folders, bookmarks, GitHub, trending, RSS, clipboard history, screenshot and screen canvas.", "ローカルの資料ライブラリとデスクトップウィジェットの組です：ファイルとフォルダー、ブックマーク、GitHub、トレンド、RSS、クリップボード履歴、スクリーンショットと画面キャンバス。"),
        new Entry("guide.first.s1.2", "数据都在这台机器的应用数据目录里，不登录、不上传；要同步是你自己在设置里开启并填凭据。", "Your data stays in this machine's app-data directory — no sign-in, no uploads; syncing is something you switch on yourself in Settings and provide credentials for.", "データはすべてこの端末のアプリデータフォルダーにあります。サインインもアップロードもしません。同期は設定で自分で有効にして資格情報を入力した場合だけ行われます。"),
        new Entry("guide.first.s2.title", "先学搜索框：它是什么都能进", "Start with the search box: it goes everywhere", "まず検索ボックスから：すべてへの入り口"),
        new Entry("guide.first.s2.1", "顶部搜索框是全程序的主入口：输入就进到结果页，本地文件、书签、Star、动态都在里面，按类型分块。", "The search box at the top is the app's main entrance: type and you land on the results page — local files, bookmarks, stars and activity, grouped by type.", "上部の検索ボックスがアプリ全体の主な入り口です。入力すると結果ページに移り、ローカルファイル・ブックマーク・スター・アクティビティが種類ごとに表示されます。"),
        new Entry("guide.first.s2.2", "支持 ext:（扩展名）、size:（大小）、dm:（改动日期）、folder:（目录）这类过滤词，语法提示就在框子的占位文字里。", "Filters like ext: (extension), size: (size), dm: (modified date) and folder: (directory) are supported; syntax hints are in the box's placeholder.", "ext:（拡張子）・size:（サイズ）・dm:（更新日）・folder:（フォルダー）などのフィルターに対応しています。書式のヒントはボックスのプレースホルダーにあります。"),
        new Entry("guide.first.s2.3", "↑↓ 选结果、Enter 打开、Ctrl+Enter 打开它所在的位置；一行右键还有复制路径、记录到本地、拖出去。", "↑↓ to pick, Enter to open, Ctrl+Enter to open its location; right-click a row for copy path, save to library and drag out.", "↑↓ で選び、Enter で開き、Ctrl+Enter で場所を開きます。行を右クリックするとパスのコピー・ライブラリに記録・ドラッグアウトもできます。"),
        new Entry("guide.first.s3.title", "换页，以及每页右上角那颗「？」", "Switching pages, and the “?” on every page", "ページの切り替えと、各ページ右上の「？」"),
        new Entry("guide.first.s3.1", "搜索框下面那一排用来换页：文件夹、标签、动态、隐藏、快照、剪贴板，还有默认收着的热榜与 RSS。", "The row under the search box switches pages: Folders, Tags, Activity, Hidden, Snapshots, Clipboard — plus Trending and RSS, hidden by default.", "検索ボックスの下の列でページを切り替えます：フォルダー、タグ、アクティビティ、非表示、スナップショット、クリップボード。既定で隠れているトレンドと RSS もあります。"),
        new Entry("guide.first.s3.2", "每页右上角的「？」只讲你正看着的这一页，所以它不会拿别页的话来套这一页。", "The “?” at the top right only talks about the page you're looking at, so it never recycles another page's text for this one.", "右上の「？」は今見ているページだけを説明します。他のページの説明が流用されることはありません。"),
        new Entry("guide.first.s3.3", "关掉窗口不等于退出：它收进托盘，随时用呼出键叫回来（那颗键在设置「快捷键」里能改）。", "Closing the window is not quitting: it hides to the tray and you call it back with the toggle key anytime (that key is editable in Settings → Hotkeys).", "ウィンドウを閉じても終了ではありません：トレイに格納され、呼び出しキーでいつも戻せます（そのキーは設定の「ショートカット」で変更できます）。"),
        new Entry("guide.first.s4.title", "桌面上那几颗小工具", "The desktop widgets", "デスクトップのウィジェット"),
        new Entry("guide.first.s4.1", "顶栏的组件按钮新建桌面小工具；组件上右键换外观、存快照、隐藏。首次只给你一颗时钟，其余自己挑。", "The widgets button in the top bar creates desktop widgets; right-click a widget to restyle it, snapshot it or hide it. You start with just a clock and pick the rest yourself.", "上部バーのウィジェットボタンでデスクトップウィジェットを追加します。ウィジェットを右クリックすると外見の変更・スナップショット保存・非表示ができます。最初は時計だけがあり、残りは自分で選びます。"),
        new Entry("guide.first.s4.2", "闹钟与护眼提醒就长在时钟组件上：右键它即可设，不用去设置里翻。", "Alarms and eye-rest reminders live on the clock widget itself: right-click it to set them, no digging through Settings.", "アラームと休憩リマインダーは時計ウィジェット自体にあります。右クリックで設定でき、設定ページを探す必要はありません。"),
        new Entry("guide.first.s4.3", "「快照」存的是整套布局加配置，不含条目数据；换一套摆法之前先存一份，随时整桌还原。", "Snapshots save the whole layout plus configuration, not item data; save one before rearranging and restore the whole desk anytime.", "スナップショットはレイアウト全体と設定を保存します（項目データは含みません）。並べ替える前に保存しておけば、いつでも元に戻せます。"),
        new Entry("guide.first.s5.title", "截图、贴图与屏幕画布", "Screenshot, pins and the screen canvas", "スクリーンショット・貼り付け・画面キャンバス"),
        new Entry("guide.first.s5.1", "截图之后能直接标注、识字、贴到桌面；贴图可以成组隐藏，也能交还鼠标（穿透）而不退出。", "After a screenshot you can annotate, OCR and pin to desktop; pins can hide as a group or hand the mouse back through (click-through) without quitting.", "スクリーンショットはそのまま注釈・文字起こし・デスクトップへ貼り付けができます。貼り付けた画像はグループで非表示にしたり、終了せずにマウスを透過させたりできます。"),
        new Entry("guide.first.s5.2", "屏幕画布是一块盖在桌面面上的透明玻璃，用来在演示或看文档时随手画。", "The screen canvas is a pane of clear glass over the desktop, for sketching while presenting or reading documents.", "画面キャンバスはデスクトップの上に重なる透明なガラス板で、発表や資料閲覧中にさっと描き込むためのものです。"),
        new Entry("guide.first.s5.3", "这两样的总开关与按键都在设置「拓展功能」与「快捷键」两栏里。", "The master switches and hotkeys for both live in Settings under Extensions and Hotkeys.", "この二つのマスタースイッチとキーは、設定の「拡張機能」と「ショートカット」のタブにあります。"),
        new Entry("guide.first.s6.title", "这几件事此刻的按键", "The current keys for these actions", "これらの操作の現在のキー"),
        new Entry("guide.first.s6.1", "下面几行由程序按你的绑定表现场写出来：你在设置里改过键，这里就跟着改，不是抄来的一份。", "The lines below are written live from your binding table: change a key in Settings and this changes too — it is not a copied snapshot.", "下の行はバインディング情報からその場で書き出されます。設定でキーを変更すればここも変わります。写しではありません。"),
        new Entry("guide.first.s6.2", "某个键被别的程序占了，它只提示你，不挡你把这一条设下去。", "If a key is taken by another program you get a warning — it never blocks you from saving the binding.", "キーが他のプログラムに占有されている場合は警告が出るだけで、登録が妨げられることはありません。"),
        new Entry("guide.missing.title", "这一页还没有说明", "No guide for this page yet", "このページのガイドはまだありません"),
        new Entry("guide.missing.line", "这里的说明还没补上。设置「使用说明」栏里有全部功能的文字版。", "The guide for this page hasn't been written yet. The Settings guide tab has the full text version.", "このページの説明はまだ用意できていません。設定の「使い方ガイド」タブに全機能の文章版があります。"),
        new Entry("guide.hotkey.bound", "{0}：{1}", "{0}: {1}", "{0}：{1}"),
        new Entry("guide.hotkey.unbound", "{0}：没绑定按键（在设置「快捷键」里给一颗）", "{0}: no key bound (assign one in Settings → Hotkeys)", "{0}：キーが未割り当てです（設定の「ショートカット」で割り当ててください）"),
        new Entry("guide.hotkey.disabled", "（全局热键现在关着：在设置「快捷键」里打开才生效。）", "(Global hotkeys are currently off: enable them in Settings → Hotkeys for these to work.)", "（グローバルホットキーは現在オフです：設定の「ショートカット」で有効にすると機能します。）"),
        new Entry("guide.hotkey.section", "这几件事的按键", "Keys for these actions", "これらの操作のキー"),
        new Entry("hotkey.act.main.toggle", "切换主界面（显示 / 隐藏）", "Toggle main window (show / hide)", "メイン画面の切り替え（表示／非表示）"),
        new Entry("hotkey.act.main.show", "显示主界面", "Show main window", "メイン画面を表示"),
        new Entry("hotkey.act.main.hide", "隐藏主界面", "Hide main window", "メイン画面を非表示"),
        new Entry("hotkey.act.widgets.showall", "显示所有组件", "Show all widgets", "すべてのウィジェットを表示"),
        new Entry("hotkey.act.widgets.hideall", "隐藏所有组件", "Hide all widgets", "すべてのウィジェットを非表示"),
        new Entry("hotkey.act.widgets.toggleall", "切换所有组件（显示 / 隐藏）", "Toggle all widgets (show / hide)", "すべてのウィジェットを切り替え（表示／非表示）"),
        new Entry("hotkey.act.widgets.topmostall", "切换所有组件置顶（置顶 / 不置顶）", "Toggle always-on-top for all widgets (on / off)", "すべてのウィジェットの最前面表示を切り替え（オン／オフ）"),
        new Entry("hotkey.act.screen.capture", "截图（框选区域）", "Screenshot (select region)", "スクリーンショット（範囲選択）"),
        new Entry("hotkey.act.screen.pin", "贴图（框选后钉在桌面）", "Pin to desktop (select then pin)", "デスクトップへ貼り付け（範囲選択してピン留め）"),
        new Entry("hotkey.act.screen.pinhidden", "切换贴图显示 / 隐藏（全部）", "Toggle pin visibility (all)", "貼り付け画像の表示を切り替え（すべて）"),
        new Entry("hotkey.act.screen.pinthrough", "切换贴图鼠标穿透（全部）", "Toggle pin click-through (all)", "貼り付け画像のマウス透過を切り替え（すべて）"),
        new Entry("hotkey.act.screen.ocr", "识字（框选区域并复制文字）", "OCR (select region and copy text)", "文字起こし（範囲を選択してテキストをコピー）"),
        new Entry("hotkey.act.canvas.toggle", "屏幕画布（开启 / 关闭）", "Screen canvas (on / off)", "画面キャンバス（オン／オフ）"),
        new Entry("hotkey.act.canvas.through", "画布 · 交出 / 收回鼠标（穿透切换）", "Canvas · hand off / take back the mouse (click-through)", "キャンバス · マウスの受け渡し（透過切り替え）"),
        new Entry("hotkey.act.canvas.pen", "画布 · 画笔（留痕，未开画布时先开）", "Canvas · pen (strokes stay; starts the canvas if off)", "キャンバス · ペン（描画が残る。オフのときは先に開きます）"),
        new Entry("hotkey.act.canvas.highlighter", "画布 · 荧光笔（按住即画、松开即透）", "Canvas · highlighter (draw while held, see-through when released)", "キャンバス · 蛍光ペン（押している間だけ描画、離すと透過）"),
        new Entry("hotkey.act.canvas.eraser", "画布 · 橡皮", "Canvas · eraser", "キャンバス · 消しゴム"),
        new Entry("hotkey.act.canvas.board", "画布 · 白板底（整屏白底 / 恢复透明）", "Canvas · whiteboard backdrop (full white / back to clear)", "キャンバス · ホワイトボード（画面を白く／透明に戻す）"),
        new Entry("hotkey.act.canvas.curtain", "画布 · 幕布（压暗整屏，只亮鼠标那一圈）", "Canvas · curtain (dim the screen, keep a light circle at the cursor)", "キャンバス · カーテン（画面を暗くし、カーソルの周りだけ明るく）"),
        new Entry("hotkey.act.canvas.undo", "画布 · 撤销上一笔", "Canvas · undo last stroke", "キャンバス · ひとつ前のストロークを取り消す"),
        new Entry("hotkey.act.canvas.redo", "画布 · 重做刚撤销的那一笔", "Canvas · redo the undone stroke", "キャンバス · 取り消したストロークをやり直す"),
        new Entry("hotkey.act.canvas.clear", "画布 · 清空笔迹", "Canvas · clear all strokes", "キャンバス · ストロークをすべて消す"),
        new Entry("hotkey.act.canvas.save", "画布 · 存为图片（屏幕 + 笔迹）", "Canvas · save as image (screen + strokes)", "キャンバス · 画像として保存（画面＋ストローク）"),
        new Entry("hotkey.act.canvas.copy", "画布 · 复制到剪贴板（屏幕 + 笔迹）", "Canvas · copy to clipboard (screen + strokes)", "キャンバス · クリップボードにコピー（画面＋ストローク）"),
        new Entry("hotkey.act.canvas.pin", "画布 · 贴到桌面（笔迹那一块，钉回原位）", "Canvas · pin to desktop (the stroked area, pinned in place)", "キャンバス · デスクトップへ貼り付け（ストローク部分を元の位置に）"),
        new Entry("hotkey.act.layout", "切换布局 · {0}", "Switch layout · {0}", "レイアウト切り替え · {0}"),
        new Entry("hotkey.act.layoutGone", "切换布局（已删除）", "Switch layout (deleted)", "レイアウト切り替え（削除済み）"),
        new Entry("hotkey.act.widget.toggle", "切换组件 · {0}（显示 / 隐藏）", "Toggle widget · {0} (show / hide)", "ウィジェット切り替え · {0}（表示／非表示）"),
        new Entry("hotkey.act.widget.create", "新建组件 · {0}", "New widget · {0}", "ウィジェットを追加 · {0}"),
        new Entry("hotkey.act.widget.show", "显示组件 · {0}", "Show widget · {0}", "ウィジェットを表示 · {0}"),
        new Entry("hotkey.act.widget.hide", "隐藏组件 · {0}", "Hide widget · {0}", "ウィジェットを非表示 · {0}"),
        new Entry("hotkey.err.occupiedBySiblings", "本程序还另开着 {0} 个实例（pid {1}），这条键此刻由那边响应（按它动的就是那个实例）；这个实例会自己继续重试，那边退出后自动接回来", "StarMark is also running {0} more instance(s) here (pid {1}); that copy is the one responding to this key. This instance keeps retrying and takes over automatically once the other exits.", "このアプリのインスタンスがあと {0} 個動いています（pid {1}）。このキーはそちらが応答しています。こちらは自動で再試行し、相手が終了すると引き継ぎます"),
        new Entry("hotkey.err.pidJoin", "、", ", ", "、"),
        new Entry("hotkey.err.occupied", "该组合键已被其它程序占用", "This key combination is already taken by another program", "このキーの組み合わせは他のプログラムに占有されています"),
        new Entry("hotkey.err.registerDenied", "系统拒绝注册（Win32 错误 {0}）", "The system refused to register it (Win32 error {0})", "システムが登録を拒否しました（Win32 エラー {0}）"),
        new Entry("hotkey.err.hookDenied", "系统拒绝了键盘钩子的安装（Win32 错误 5，通常是安全软件或组策略拦截）", "The system refused to install the keyboard hook (Win32 error 5 — usually security software or group policy)", "システムがキーボードフックのインストールを拒否しました（Win32 エラー 5。セキュリティソフトやグループポリシーによるものが一般的）"),
        new Entry("hotkey.err.hookCreate", "键盘钩子创建失败（Win32 错误 1401）", "Keyboard hook creation failed (Win32 error 1401)", "キーボードフックの作成に失敗しました（Win32 エラー 1401）"),
        new Entry("hotkey.err.hookFailed", "键盘钩子安装失败（Win32 错误 {0}）", "Keyboard hook installation failed (Win32 error {0})", "キーボードフックのインストールに失敗しました（Win32 エラー {0}）"),
        new Entry("hotkey.err.rebindHint", "要现在就归这个实例用：点这一行那颗显示键位的按钮换一个组合，再点「保存快捷键」。", "To claim it for this instance right now: click the key button on this row to pick another combination, then click Save hotkeys.", "今すぐこちらで使うには：この行のキー表示ボタンをクリックして別の組み合わせを選び、「ショートカットを保存」をクリックしてください。"),
        new Entry("hotkey.err.rowNotice", "提示：{0}（设置已保存，占用者退出后自动生效）。{1}", "Note: {0} (settings are saved; it takes effect automatically once the other program exits). {1}", "ヒント：{0}（設定は保存済み。占有側が終了すると自動で有効になります）。{1}"),
        new Entry("hotkey.err.summary", "提示：{0} 个组合键此刻没归这个实例（{1}）。这些快捷键已经保存，程序会每 {2} 秒自己再注册一次，对方一退出就生效——不需要你做任何事；急着现在就要生效可以点「重试注册」，具体每一行怎么换一个键，写在该行的提示里。", "Note: {0} key combination(s) are not held by this instance right now ({1}). These hotkeys are already saved — the app retries registration every {2} seconds on its own and takes over once the other program exits, so you don't need to do anything; if you need it working right away, click Retry registration. How to change the key on each row is written in that row's hint.", "ヒント：{0} 個のキーの組み合わせが現在このインスタンスにありません（{1}）。これらのショートカットは保存済みで、アプリが {2} 秒ごとに自動で再登録し、相手が終了すると引き継ぎます。操作は不要です。今すぐ有効にしたい場合は「再登録」をクリックしてください。各行のキーの変更方法はその行のヒントに書かれています。"),
        new Entry("hotkey.err.summaryItem", "{0}：{1}", "{0}: {1}", "{0}：{1}"),
        // ===== 快捷键那一栏的"这一行绑没绑"那一族（批次 WI-40-1，P-175 缺口③）=====
        // ⚠ 两颗"没绑"是<b>两个表面各自的字</b>：设置页那一格是按钮上的占位（未设置），画布那两处是只读一览里的状态（未绑定）。
        //   合成一颗就会改掉其中一个表面的用词——那不是翻译，是改产品（坑表 #350：颗粒度按"这句会不会不一样"划）。
        //   并列号单独立行：它与 health／trending 那两族同形（中日都用顿号、en 用半角逗号），所以同形豁免要点名登记。
        new Entry("hotkey.unset", "未设置", "Not set", "未設定"),
        new Entry("hotkey.unbound", "未绑定", "Not bound", "未割り当て"),
        new Entry("hotkey.conflict.shares", "⚠ 与其它动作共用该组合：{0}", "⚠ Shares this combination with other actions: {0}", "⚠ この組み合わせは他の動作と共用しています：{0}"),
        new Entry("hotkey.conflict.join", "、", ", ", "、"),
        new Entry("hotkey.record.hookRetry", "{0}——因此收不到你按下的键。再点一次这个按钮即可重试。", "{0} — so the keys you press won't reach the recorder. Click this button again to try once more.", "{0}——そのため押したキーが届きません。このボタンをもう一度クリックすれば再試行できます。"),
        // ===== 快捷键那一栏的分组标题（批次 WI-40-2，P-175 缺口①）=====
        // 从前这几颗词由 Core 现拼（`CategoryOf` 直接返回中文串），而同一颗串又被当**分组身份**用
        // ——换语言那一刻同一批动作会跑到另一个组名底下（坑表 #353）。身份从此是 ASCII，词只住在这里。
        // ⚠ 组件那一族的分组标题⛔ 在这里另起键：名字的唯一真源是注册表那颗（`widget.kind.*`，托盘与组件窗都读它）。
        new Entry("hotkey.cat.main", "主界面", "Main window", "メイン画面"),
        new Entry("hotkey.cat.widgets", "组件总控", "All widgets", "ウィジェット一括"),
        new Entry("hotkey.cat.screen", "截图 / 贴图 / 识字", "Screenshot / pin / OCR", "スクリーンショット／貼り付け／文字起こし"),
        new Entry("hotkey.cat.canvas", "屏幕画布", "Screen canvas", "画面キャンバス"),
        new Entry("hotkey.cat.layout", "布局方案", "Layouts", "レイアウト"),
        new Entry("hotkey.cat.other", "其它", "Other", "その他"),
        new Entry("hotkey.cat.boundCount", "（{0} 项已绑定）", "({0} bound)", "（{0} 件バインド済み）"),
        // ===== 画布那五个 partial 的提示句族（批次 WI-45，P-176）=====
        // `Report` 是建好即弃的瞬时提示卡（批次 RV-1），⛔ 挂 `LocText` 键——那张卡下一刻就不在了，
        // 把它记进重贴名单只是让名单里多几条再也贴不出去的空壳。所以这一族的取词**在调用点当场发生**，
        // 合法且唯一；要点是字⛔ 住在代码里。
        // ⚠ 「屏幕画布」这个名字⛔ 在这些句子里再抄一份：它是设置页那一行（`settings.extend.canvasTitle`）
        //   与托盘那一项（`tray.canvas`）的名字，抄进句子＝第二个出处，日后改名只改到一半（坑表 #353 同族）
        //   ⇒ 句子交位置，名字由调用点当实参递进来。
        new Entry("canvas.act.through", "交出 / 收回鼠标", "Hand off / take back the mouse", "マウスの受け渡し"),
        new Entry("canvas.act.undo", "撤销上一笔", "Undo the last stroke", "直前のストロークを取り消す"),
        new Entry("canvas.act.redo", "重做那一笔", "Redo the undone stroke", "取り消したストロークをやり直す"),
        new Entry("canvas.act.clear", "清空笔迹", "Clear all strokes", "ストロークをすべて消す"),
        new Entry("canvas.act.save", "存为图片", "Save as image", "画像として保存"),
        new Entry("canvas.act.copy", "复制到剪贴板", "Copy to clipboard", "クリップボードにコピー"),
        new Entry("canvas.act.pin", "贴到桌面", "Pin to desktop", "デスクトップへ貼り付け"),
        new Entry("canvas.notice.boardClosedTitle", "屏幕画布已关闭", "Screen canvas is off", "スクリーンキャンバスはオフです"),
        new Entry("canvas.notice.boardClosedBody", "要在 设置 → 拓展功能 里打开「{0}」；打开后这条快捷键就回来了",
                  "Turn on “{0}” in Settings → Extensions — this hotkey comes back once it is on.",
                  "設定 → 拡張機能 で「{0}」をオンにしてください。オンにすればこのショートカットが戻ります。"),
        new Entry("canvas.notice.openFailedTitle", "画布打不开", "Can't open the canvas", "キャンバスを開けません"),
        // 「一个显示器都没报告」这条**关于机器的事实**归抓帧那一路（批次 WI-46a）：画布与截图都问同一件事，
        // 而画布出图本来就经 `ScreenshotService.CaptureWithoutCanvas` ⇒ ⛔ 两处各立一行同名的话。
        new Entry("canvas.notice.noLayers", "每一屏的透明层都没能建起来（原因见日志）",
                  "No transparent layer could be created on any screen (see the log for why)",
                  "どの画面にも透過レイヤーを作成できませんでした（原因はログを参照）"),
        new Entry("canvas.notice.noMainWindow", "主窗还不存在（应用还没起完？）",
                  "The main window doesn't exist yet (is the app still starting up?)",
                  "メインウィンドウがまだ存在しません（起動途中ですか？）"),
        new Entry("canvas.notice.notRunningTitle", "画布没开着", "The canvas isn't open", "キャンバスが開いていません"),
        new Entry("canvas.notice.notResponsiveTitle", "画布现在按不动", "The canvas isn't taking input right now", "キャンバスは今入力を受け付けません"),
        new Entry("canvas.notice.captureInFlight", "截图进行中", "a screenshot is in progress", "スクリーンショット撮信中"),
        new Entry("canvas.notice.panelClosedTitle", "画布已经关了", "The canvas is closed", "キャンバスは閉じています"),
        new Entry("canvas.notice.panelIsPartOfCanvas", "快捷键面板是{0}的一部分，先把它重新打开",
                  "The shortcut panel is part of {0} — open it again first",
                  "ショートカットパネルは{0}の一部です。まずそれを開き直してください"),
        new Entry("canvas.notice.panelFailedTitle", "快捷键面板没打开", "The shortcut panel didn't open", "ショートカットパネルが開きませんでした"),
        new Entry("canvas.hotkey.notRunning", "「{0}」要先打开{1}（{2}，或托盘菜单里的「{3}」）",
                  "“{0}” needs {1} open first ({2}, or “{3}” in the tray menu)",
                  "「{0}」を使うには{1}を開いている必要があります（{2}、またはトレーのメニューの「{3}」）"),
        new Entry("canvas.hotkey.disabled", "「{0}」要先在 设置 → 拓展功能 里打开{1}",
                  "“{0}” needs {1} turned on in Settings → Extensions",
                  "「{0}」を使うには設定 → 拡張機能 で{1}をオンにしてください"),
        new Entry("canvas.shot.pinFailedTitle", "贴图失败", "Couldn't pin it", "貼り付けに失敗しました"),
        new Entry("canvas.shot.copyFailedTitle", "复制失败", "Copy failed", "コピーに失敗しました"),
        new Entry("canvas.shot.saveFailedTitle", "存图失败", "Save failed", "保存に失敗しました"),
        new Entry("canvas.shot.noInk", "板上还没有笔迹可贴——「贴图」贴的就是笔迹那一块；要截整块屏幕请用截图（{0}），框选后再贴出",
                  "Nothing is drawn on the board, so there is no area to pin — “pin” takes exactly the stroked part; " +
                  "to grab the whole screen use Capture ({0}) and pin it after selecting",
                  "ボードに筆跡がないため、貼り付ける領域がありません。「ピン」が貼るのは筆跡のあった部分です。" +
                  "画面全体を撮るにはスクリーンショット（{0}）で範囲選択後貼り付けてください"),
        new Entry("canvas.shot.screenOutOfFrame", "这一块屏在截到的画面外面（显示器可能刚被拔掉）",
                  "This screen fell outside the captured image (a monitor may have just been unplugged)",
                  "この画面は取得した画像の外にあります（モニターが抜かれた可能性があります）"),
        // ===== 截图那一路：卡片句 + 遮罩窗上那颗错误角标（批次 WI-46a，P-177 第一刀）=====
        // 与画布那一族同一口径：提示卡是建好即弃的瞬时表面，取词**在调用点当场发生**，⛔ 挂 `LocText` 键
        // （#WI-45 那段注释：记进重贴名单只会多几条再也贴不出去的空壳）。遮罩窗那颗角标同理——它只在出事那一刻写一次。
        // ⚠ 卡片底部那一行（`card.hint.*`）**不在这一批**：它是每一张卡共用的句子，提前交出去＝还没交完的那几族
        //   每张卡从此半英半中 ⇒ 它归提示卡这一族的最后一刀（P-177 收尾）。
        // ⚠ 同形但不同的失败⛔ 合成一颗（卷首"颗粒度按这句会不会不一样划"）：这里的 `copyFailedTitle`／`saveFailedTitle`
        //   与画布那两颗 `canvas.shot.*FailedTitle` 中文同形，说的是两件事——画布那是"根本没东西可交"，
        //   这里是"像素到手了、剪贴板／磁盘那一步坏了"。反之 `grab.noFrame`／`grab.noMonitors` 是同一件机器事实，
        //   上一批为画布临时起的两颗同名行已并进这里（`CanvasService.Snapshot`／`Host` 两处调用点一起改指）。
        new Entry("capture.card.category", "截图", "Capture", "スクリーンショット"),
        new Entry("capture.card.disabledTitle", "截屏功能已关闭", "Screen capture is turned off", "スクリーンショット機能はオフです"),
        new Entry("capture.card.failedTitle", "截图失败", "Capture failed", "スクリーンショットに失敗しました"),
        new Entry("capture.card.copyFailedTitle", "复制失败", "Copy failed", "コピーに失敗しました"),
        new Entry("capture.card.encodeFailed", "把画面编成图片时失败", "The image couldn't be encoded", "画像をエンコードできませんでした"),
        new Entry("capture.card.copiedTitle", "已复制", "Copied", "コピーしました"),
        new Entry("capture.card.copiedBody", "{0} 的画面已进剪贴板", "{0} is on the clipboard", "{0} をクリップボードにコピーしました"),
        new Entry("capture.card.savedTitle", "已存图", "Saved", "保存しました"),
        new Entry("capture.card.saveFailedTitle", "存图失败", "Couldn't save it", "保存に失敗しました"),
        new Entry("capture.card.saveWriteFailed", "写入图片文件时出错（检查磁盘空间与目录权限）",
                  "The image file couldn't be written (check disk space and folder permissions)",
                  "画像ファイルを書き込めませんでした（ディスク容量とフォルダの権限を確認してください）"),
        new Entry("capture.grab.noFrame", "系统没有返回画面", "The system returned no image", "システムが画像を返しませんでした"),
        new Entry("capture.grab.noMonitors", "系统没有报告任何显示器", "The system reported no monitors", "システムがディスプレイを返しませんでした"),
        new Entry("capture.grab.noDisplay", "本机没有可用的显示器画面", "This machine has no usable display image", "このPCに利用可能なディスプレイ画面がありません"),
        new Entry("capture.grab.outsideFrame", "选区不在已捕获的画面范围内", "The selection is outside the captured image", "選択範囲は取得した画像の外にあります"),
        new Entry("capture.grab.frameNotShown", "这一帧没能铺到屏幕上（内存不足或像素缓冲尺寸不符）——按 Esc 结束",
                  "This frame couldn't be put on screen (out of memory, or the pixel buffer size doesn't match) — press Esc to end",
                  "このフレームを画面に表示できませんでした（メモリ不足、またはピクセルバッファのサイズ不一致）。Esc で終了してください"),
        new Entry("capture.select.offscreen", "选区落在所有显示器之外，没有可截取的内容",
                  "The selection fell outside every monitor, so there is nothing to capture",
                  "選択範囲がすべてのディスプレイの外にあるため、切り取れる内容がありません"),
        new Entry("capture.select.tooSmall", "选区太小（{0} × {1}），请按住的拖动再放开",
                  "The selection is too small ({0} × {1}) — hold and drag, then release",
                  "選択範囲が小さすぎます（{0} × {1}）。押したままドラッグしてから離してください"),
        new Entry("capture.strip.composeFailed", "标注没能合成：{0}", "The annotations couldn't be composed: {0}", "注釈を合成できませんでした：{0}"),
        new Entry("capture.strip.cropFailed", "选区那块画面没能裁出来：{0}", "The selected area couldn't be cropped: {0}", "選択範囲を切り抜できませんでした：{0}"),
        new Entry("capture.strip.mosaicFailed", "打码预览没能刷到屏幕上：{0}", "The mosaic preview couldn't be drawn: {0}", "モザイクのプレビューを更新できませんでした：{0}"),
        new Entry("capture.strip.resizeFailed", "这一改画不出来：{0}", "That change couldn't be drawn: {0}", "この変更は描画できませんでした：{0}"),
        new Entry("capture.strip.drawFailed", "标注没能画上去：{0}", "The annotation couldn't be drawn: {0}", "注釈を描画できませんでした：{0}"),
        new Entry("capture.strip.textNoFocus", "这一行字还没拿到键盘焦点：点一下那个描边的输入框再打字（Enter 换行，Esc 结束编辑）",
                  "This line doesn't have the keyboard yet — click the outlined box, then type (Enter for a new line, Esc to finish editing)",
                  "この行はまだキーボードフォーカスを持っていません。枠のある入力欄をクリックしてから入力してください（Enter で改行、Esc で編集終了）"),
        // 卡片抬头那一颗"谁在说话"。⚠ 贴图那颗在这一批就立好：`CaptureOverlayWindow.Actions` 里
        // `_pinned ? 贴图 : 截图` 是一整颗三元表达式，只交一半＝贴出去的那张卡半英半中；
        // 贴图那一族自己的句子归下一刀，但**必须复用这一行**，⛔ 另起同名键。
        new Entry("pin.card.category", "贴图", "Pin", "貼り付け"),
        // ===== 识字那一路的提示卡句（批次 WI-46b，P-177 第二刀）=====
        // 与截图那一批同一口径：瞬时卡 ⇒ 取词在调用点当场发生，⛔ 挂 `LocText`。
        // ⚠ `ocr.how.*` 那五颗是**一条读数被拆成三片**：从前 `OcrRead.How` 把"，"写死在每个片段开头，
        //   于是"引擎没报语言、又放大了"那一格会输出开头带一颗全角逗号的句子（屏幕上就是「，放大 2×」）。
        //   现在片段自己⛔ 带分隔符，分隔符单独一颗键（先例＝`hotkey.conflict.join`，也⛔ 并入片段——并入就改掉了各语言的用词）。
        new Entry("ocr.card.category", "识字", "OCR", "文字認識"),
        new Entry("capture.card.categoryOcr", "截图识字", "Capture OCR", "スクリーンショット文字認識"),
        new Entry("pin.card.categoryOcr", "贴图识字", "Pin OCR", "貼り付け文字認識"),
        new Entry("ocr.card.failedTitle", "没能识别", "Couldn't read it", "認識できませんでした"),
        new Entry("ocr.card.noReason", "识别引擎没有给出原因", "the OCR engine gave no reason", "認識エンジンが理由を返しませんでした"),
        new Entry("ocr.card.emptyTitle", "没认出文字", "No text found", "文字が見つかりませんでした"),
        new Entry("ocr.card.copyFailedBody", "认出 {0} 字但没能写进剪贴板：{1}",
                  "Recognised {0} characters but couldn't put them on the clipboard: {1}",
                  "{0} 文字認識しましたが、クリップボードに書き込めませんでした：{1}"),
        new Entry("ocr.card.copiedTitle", "已复制文字", "Text copied", "テキストをコピーしました"),
        new Entry("ocr.card.copiedBody", "{0} 字{1}：", "{0} characters{1}: ", "{0} 文字{1}："),
        new Entry("ocr.text.noText", "这块画面里没有认出文字（图形、手写或太小的字都不在这套引擎的能力里）",
                  "This image has no text the engine can read (graphics, handwriting and very small text are all outside what it does)",
                  "この画像には読める文字がありません（図形・手書き・小さすぎる文字はこのエンジンの能力外です）"),
        new Entry("ocr.how.byLanguage", "按 {0}", "using {0}", "{0} で認識"),
        new Entry("ocr.how.upscale", "放大 {0}×", "upscaled {0}×", "{0} 倍に拡大"),
        new Entry("ocr.how.swapped", "已换语言", "language retried", "言語を切り替え"),
        new Entry("ocr.how.join", "，", ", ", "、"),
        // 括号形状只许一个主人：Core 的 `HowParenthesized` 与 UI 里那份 `Paren` 从前各写一份（同一动作两份实现），
        // 现在只有 Core 那一份，词也从表里取——⛔ en 那两列用全角括号（那不是我写的那门字的排版）。
        new Entry("ocr.how.paren", "（{0}）", " ({0})", "（{0}）"),
        // ===== 卡片自己那一行 footer 与主窗那张卡（批次 WI-47，P-177 收尾＋P-178）=====
        // ⚠ 这一批⛔ 与前面三刀同批：footer 是**每一张卡**共用的那一行，提前交出去＝还没交完的那几族每张卡半英半中
        //   （WI-46a 判定 3 的口径）。停留秒数仍只有 `KeepSeconds` 一个主人——那两行都收 `{n}` 实参，⛔ 在表里写死"10 秒"。
        new Entry("card.hint.withAction", "点一下打开{0} · {1} 秒后自动消失",
                  "Click to open {0} · disappears in {1} s", "{0} をクリックで開きます · {1} 秒で自動的に消えます"),
        new Entry("card.hint.plain", "点一下关闭 · {0} 秒后自动消失",
                  "Click to dismiss · disappears in {0} s", "クリックで閉じます · {0} 秒で自動的に消えます"),
        // 动作标签：正文由供话者递进来，那颗 `链接` 只是"没给名字"时的兜底（⛔ 反推那颗当状态判断，#361 同族）。
        new Entry("card.action.link", "链接", "link", "リンク"),
        new Entry("card.action.releasePage", "发布页", "release page", "リリースページ"),
        // ⚠ 正文那颗 `{0}` 交的是**当前绑定**的键位文本（`CanvasService.BindingText`），⛔ 抄一颗键名进句子：
        //   那颗键用户可改，改了之后这句就在教人去按一颗哑键（P-178；同一禁项在批次 VC 已为说明文字定死）。
        new Entry("main.bgRunningTitle", "StarMark 正在后台运行", "StarMark is running in the background", "StarMark はバックグラウンドで実行中です"),
        new Entry("main.bgRunningBody", "{0} 随时呼出窗口", "Press {0} anytime to bring the window back", "{0} でいつでもウィンドウを呼び出せます"),
        // ===== 贴图与贴图组那一路的提示卡句（批次 WI-46c，P-177 第三刀）=====
        // 同一口径：卡片那三样（抬头／标题／正文）都在做事那一刻写一次 ⇒ 取词落在调用点，⛔ 挂 `LocText`（WI-45／46a／46b）。
        // ⚠ 抬头那颗「贴图」⛔ 在这一批再立一次——它就是 WI-46a 为 `_pinned ? 贴图 : 截图` 立好的 `pin.card.category`；
        //   一个词两个出处＝改了其中一处，屏幕上同一族卡的抬头从此各说各话。
        // ⚠ `pin.group.name` 顶掉了从前那颗 `PinGrouping.NamePrefix`：组**身份**是序号，"组 3"只是它的一种念法；
        //   旧写法把念法存进建组那一刻的快照 ⇒ 换语言后托盘与条上那几行仍念旧语言（同形先例＝`hotkey.gate.*`）。
        new Entry("pin.card.groupCategory", "贴图组", "Pin group", "貼り付けグループ"),
        new Entry("pin.card.limitTitle", "已达上限", "Limit reached", "上限に達しました"),
        new Entry("pin.card.limitBody", "已经有 {0} 张贴图，最多 {1} 张——先关掉不用的再贴（贴图会把那块画面常驻在内存里）",
                  "There are already {0} pins and {1} is the limit — close one you no longer need (a pin keeps that part of the screen in memory)",
                  "すでに {0} 枚貼り付けています。上限は {1} 枚です。不要なものを先に閉じてください（貼り付けは画面の一部をメモリに保持します）"),
        new Entry("pin.card.noneTitle", "没有可操作的贴图", "No pin to act on", "操作する貼り付けがありません"),
        new Entry("pin.card.noneBody", "现在没有贴图，这一条没有可操作的对象",
                  "There is no pin on screen, so this command has nothing to act on",
                  "貼り付けがないため、このコマンドは対象がありません"),
        new Entry("pin.card.pinnedTitle", "已钉住", "Pinned", "貼り付けました"),
        new Entry("pin.card.pinnedBody", "（共 {0} 张，上限 {1} 张）", " ({0} in total, limit {1})", "（計 {0} 枚、上限 {1} 枚）"),
        new Entry("pin.card.pinFailedTitle", "钉住失败", "Couldn't pin it", "貼り付けに失敗しました"),
        new Entry("pin.card.throughMissTitle", "穿透未跟上新贴图", "The new pin didn't follow click-through", "新しい貼り付けがマウス無視に追随しませんでした"),
        new Entry("pin.card.throughMissBody", "系统拒绝了这一张的扩展样式，其余几张仍然穿透",
                  "The system refused the extended style for this one; the others still ignore the mouse",
                  "このウィンドウの拡張スタイルをシステムが拒否しました。他は引き続きマウスを無視します"),
        new Entry("pin.card.throughPartialTitle", "穿透未能全部生效", "Click-through didn't apply to every pin", "マウス無視が全てには適用されませんでした"),
        new Entry("pin.card.throughPartialBody", "{0} / {1} 张（{2}）拒绝了这个改动（系统拒绝了扩展样式），在那几张的工具条上再点一次穿透",
                  "{0} of {1} pins ({2}) refused this change (the system rejected the extended style) — click click-through again on those toolbars",
                  "{1} 枚中 {0} 枚（{2}）がこの変更を拒否しました（システムが拡張スタイルを却下）。それらのツールバーでマウス無視をもう一度押してください"),
        new Entry("pin.card.groupFullTitle", "不能再分组", "Can't group further", "これ以上グループ化できません"),
        new Entry("pin.card.groupedTitle", "已分组", "Grouped", "グループ化しました"),
        // {0} 交的是 `pin.groupLabel` 拼好的"组 1（1 张）"——"名字后面带张数"这个形状⛔ 再写第二份（托盘那行读的也是它）。
        new Entry("pin.card.groupedBody", "{0}。托盘「{1}」里能整组隐藏 / 穿透 / 关闭",
                  "{0}. Under \"{1}\" in the tray you can hide, click-through and close the whole group",
                  "{0}。トレイの「{1}」でグループごと収納／マウス無視／閉じができます"),
        new Entry("pin.card.groupGoneTitle", "那一组已经不在了", "That group is gone", "そのグループはありません"),
        // 两行分开的理由＝这句会不会不一样（卷首颗粒度）：并组那条还要说"这张没动过"，托盘那条不必。
        new Entry("pin.card.goneBeforeBody", "{0} 在本次操作前被关掉，这张仍按原样留在桌面上",
                  "{0} was closed before this action; this pin stays on the desktop as it was",
                  "{0} はこの操作の前に閉じられました。この貼り付けはそのままデスクトップに残っています"),
        new Entry("pin.card.goneOnClickBody", "{0} 在本次点击前被关掉", "{0} was closed before this click", "{0} はこのクリックの前に閉じられました"),
        new Entry("pin.card.joinedTitle", "已并入", "Joined", "グループに追加しました"),
        new Entry("pin.card.leftTitle", "已移出组", "Left the group", "グループから外れました"),
        new Entry("pin.card.leftBody", "{0} 还剩 {1} 张", "{0} now has {1} pins left", "{0} は残り {1} 枚です"),
        new Entry("pin.card.groupClosedTitle", "已关闭这一组", "Group closed", "このグループを閉じました"),
        new Entry("pin.card.scopeAll", "全部贴图", "all pins", "すべての貼り付け"),
        new Entry("pin.group.name", "组 {0}", "Group {0}", "グループ {0}"),
        // 「贴图组」「关闭这组」两处引用都交键：这句话指的是屏幕上真实存在的那两行，改了那两个名字⛔ 让这里继续念旧的。
        new Entry("pin.group.limitBody", "最多分 {0} 组，先关掉一组（托盘「{1}」里那条「{2}」）",
                  "You can have at most {0} groups — close one first (the \"{2}\" row under \"{1}\" in the tray)",
                  "グループは最大 {0} 個です。先に一つ閉じてください（トレイの「{1}」にある「{2}」）"),
        new Entry("hotkey.gate.captureDisabled", "要先在 设置 → 拓展功能 里打开「截屏（截图 / 贴图 / 识字）」", "First turn on Screenshot (capture / pin / OCR) in Settings → Extensions", "先に設定 → 拡張機能 で「スクリーンショット（キャプチャ／貼り付け／文字起こし）」をオンにしてください"),
        new Entry("hotkey.gate.captureBusy", "已经有一次截图在场，先把它结束（Esc 取消）再截下一张", "A screenshot session is already running — finish it first (Esc to cancel) before starting another", "すでにスクリーンショットが実行中です。先に終了して（Esc でキャンセル）から次を撮ってください"),
        new Entry("hotkey.gate.canvasBusySheet", "截图进行中，这条快捷键要等截图结束才归画布", "A screenshot is in progress — this hotkey goes back to the canvas once it ends", "スクリーンショットが進行中です。このショートカットは終了後にキャンバスに戻ります"),
        new Entry("hotkey.gate.canvasDisabled", "要先在 设置 → 拓展功能 里打开「屏幕画布」", "First turn on Screen canvas in Settings → Extensions", "先に設定 → 拡張機能 で「画面キャンバス」をオンにしてください"),
        new Entry("rss.src.neverFetched", "还没抓过", "Not fetched yet", "まだ取得していません"),
        new Entry("rss.src.cacheLine", "上次抓取 {0} · 共 {1} 条 · {2}", "Last fetched {0} · {1} entries · {2}", "前回の取得 {0} · 全 {1} 件 · {2}"),
        new Entry("rss.src.dueNow", "现在到期，会自动补抓", "due now — it will be re-fetched automatically", "今が更新時刻です。自動で再取得します"),
        new Entry("rss.src.dueIn", "约 {0} 小时后自动刷新", "auto-refresh in about {0} h", "約 {0} 時間後に自動更新"),
        new Entry("rss.src.notInRound", "没有参与这一轮", "Not part of this round", "このラウンドには含まれていません"),
        new Entry("rss.src.stopped", "没抓到它（已停止或整轮到时）", "Not fetched (stopped, or the round ran out of time)", "取得できませんでした（停止中、またはラウンドの時間切れ）"),
        new Entry("rss.src.disabled", "已停用，这一轮没有抓", "Disabled — not fetched this round", "無効化されているため、このラウンドでは取得しませんでした"),
        new Entry("rss.src.notModified", "没有新内容（源说未变化）", "No new content (the feed says not modified)", "新しい内容はありません（フィードが未変更と応答）"),
        new Entry("rss.src.failed", "失败：{0}", "Failed: {0}", "失敗：{0}"),
        new Entry("rss.src.emptyOk", "通了，但没有条目", "Reached, but no entries", "接続できましたが、項目がありません"),
        new Entry("rss.src.fetched", "抓到 {0} 条", "Fetched {0} entries", "{0} 件を取得"),
        new Entry("rss.src.grew", "，缓存共 {0} 条", ", {0} entries in the cache in total", "。キャッシュには合計 {0} 件"),
        new Entry("rss.src.stillThere", "，仍然摆着缓存里的 {0} 条", ", still showing {0} cached entries", "。キャッシュの {0} 件を引き続き表示中"),
        new Entry("upd.card.checkTitle", "检查更新", "Update check", "更新の確認"),
        new Entry("upd.card.newVersionTitle", "发现新版本", "New version found", "新しいバージョンが見つかりました"),
        new Entry("upd.verdict.newer", "有新版本 {0}（本机 {1}）", "New version available: {0} (local {1})", "新しいバージョンがあります {0}（ローカル {1}）"),
        new Entry("upd.verdict.upToDate", "已经是最新（{0}）", "Already up to date ({0})", "すでに最新です（{0}）"),
        new Entry("upd.verdict.localAhead", "本机版本 {0} 比远端 {1} 更新（这通常是预发布或自己编的版）", "Local version {0} is newer than remote {1} (usually a prerelease or a local build)", "ローカルのバージョン {0} はリモート {1} より新しくなっています（プレリリースか自分でビルドしたものの可能性）"),
        new Entry("upd.verdict.nothingPublished", "这个仓库还没有发布过版本", "This repository has not published any release yet", "このリポジトリはまだリリースを公開していません"),
        new Entry("upd.verdict.repoNotVisible", "查不到仓库 {0}（改名或私有而没配凭据）", "Cannot see repository {0} (renamed, or private without credentials)", "リポジトリ {0} を参照できません（改名、または資格情報のないプライベート）"),
        new Entry("upd.verdict.unauthorized", "凭据被拒：GitHub Token 已失效或作用域不够，重新填一次即可", "Credentials rejected: the GitHub token has expired or lacks scope — fill it in again", "資格情報が拒否されました：GitHub トークンが失効しているかスコープが足りません。再入力してください"),
        new Entry("upd.verdict.rateLimited", "GitHub 暂时限流（匿名每小时 {0} 次），{1} 小时后会自己再问一次", "GitHub is rate limiting for now ({0}/h anonymously); it will ask again in {1} h", "GitHub がレート制限中です（匿名で毎時 {0} 回）。{1} 時間後に自動で再確認します"),
        new Entry("upd.verdict.notReachable", "这台机器现在连不上 GitHub（离线或代理挡着），下次联网会自己补问", "This machine can't reach GitHub right now (offline or proxied); it will ask again once online", "この端末から GitHub に接続できません（オフラインかプロキシ）。接続後に自動で確認します"),
        new Entry("upd.verdict.timedOut", "GitHub 没在限时内回答，过一会儿会自己再问一次", "GitHub didn't answer in time; it will ask again shortly", "GitHub が制限時間内に応答しませんでした。しばらくして自動で再確認します"),
        new Entry("upd.verdict.serverError", "GitHub 那边报错了，过一会儿会自己再问一次", "GitHub reported an error; it will ask again shortly", "GitHub 側でエラーが発生しました。しばらくして自動で再確認します"),
        new Entry("upd.verdict.unreadableTag", "远端那个版本标签读不懂（{0}），所以不猜新旧", "Can't parse the remote tag ({0}) — not guessing new or old", "リモートのタグを読み取れません（{0}）。新旧は判定しません"),
        new Entry("upd.verdict.localVersionUnknown", "本机版本号读不到（这份产物没写版本号），无法比较", "Local version is missing (this build has no version) — cannot compare", "ローカルのバージョンが読み取れないため（このビルドには版本なし）、比較できません"),
        new Entry("upd.verdict.noResult", "检查更新没有结果", "The update check returned nothing", "更新の確認結果がありません"),
        new Entry("upd.verdict.emptyTag", "空", "empty", "空"),
        new Entry("upd.fetch.fetched", "更新包已经取到，校验也过了（还没开始装）", "Package fetched and verified (install not started)", "更新パッケージを取得し、検証も通りました（インストールは未開始）"),
        new Entry("upd.fetch.assetMissing", "这一版没带更新包（发布时漏传了清单或载荷），只能打开发布页看", "This release has no package (manifest or payload wasn't uploaded) — open the release page instead", "このリリースには更新パッケージがありません（公開時に漏れ）。リリースページをご確認ください"),
        new Entry("upd.fetch.addressRefused", "更新地址拼不出来：仓库或标签的写法认不出来", "Cannot build the download URL: repository or tag format not recognized", "ダウンロード URL を組み立てられません：リポジトリかタグの形式を認識できません"),
        new Entry("upd.fetch.offHostRedirect", "下载中途被引到了 GitHub 之外的站点，已当场拒绝", "The download was redirected off GitHub — refused on the spot", "ダウンロード中に GitHub 以外のサイトへ転送されたため、即時に拒否しました"),
        new Entry("upd.fetch.tooLarge", "取回的字节超过这一档上限，没当它是更新包", "The download exceeded this tier's size limit — not treated as an update package", "取得したバイト数がこの段階の上限を超えたため、更新パッケージとして扱いません"),
        new Entry("upd.fetch.manifestUnreadable", "更新清单读不懂（多半是清单格式比这个程序新）", "Can't read the update manifest (probably newer than this app)", "更新マニフェストを読み取れません（多くはこのアプリより新しい形式です）"),
        new Entry("upd.fetch.notReachable", "这台机器现在连不上 GitHub（离线或代理挡着），下次联网会自己补问", "Can't reach GitHub right now (offline or proxied); it will retry once online", "この端末から GitHub に接続できません（オフラインかプロキシ）。接続後に自動で再試行します"),
        new Entry("upd.fetch.timedOut", "GitHub 没在限时内把包送完，过一会儿会自己再试", "GitHub didn't finish sending the package in time; it will retry shortly", "GitHub が制限時間内にパッケージを送り切れませんでした。しばらくして自動で再試行します"),
        new Entry("upd.fetch.serverError", "GitHub 那边报错了，过一会儿会自己再试", "GitHub reported an error; it will retry shortly", "GitHub 側でエラーが発生しました。しばらくして自動で再試行します"),
        new Entry("upd.fetch.unauthorized", "凭据被拒：GitHub Token 已失效或作用域不够，重新填一次即可", "Credentials rejected: the GitHub token has expired or lacks scope — fill it in again", "資格情報が拒否されました：GitHub トークンが失効しているかスコープが足りません。再入力してください"),
        new Entry("upd.fetch.rateLimited", "GitHub 暂时限流，{0} 小时后会自己再问一次", "GitHub is rate limiting for now; it will ask again in {0} h", "GitHub がレート制限中です。{0} 時間後に自動で再確認します"),
        new Entry("upd.fetch.diskWriteFailed", "本机临时目录写不下去（磁盘满或没权限），更新没有开始", "Can't write to the local temp directory (disk full or no permission) — update not started", "一時フォルダーに書き込めません（ディスク満杯か権限なし）。更新は開始されていません"),
        new Entry("upd.fetch.noResult", "取更新包没有结果", "The package fetch returned nothing", "パッケージの取得結果がありません"),
        new Entry("upd.integrity.trusted", "这份包签得对、字节也对得上清单", "The package signature is good and its bytes match the manifest", "パッケージの署名は正しく、バイトもマニフェストと一致しています"),
        new Entry("upd.integrity.signatureInvalid", "签名对不上：这份清单不是我们发出去的，已拒绝", "Signature mismatch: this manifest was not published by us — rejected", "署名が一致しません：このマニフェストは当方が公開したものではありません。拒否しました"),
        new Entry("upd.integrity.manifestUnreadable", "更新清单读不懂（多半是清单格式比这个程序新），没装", "Can't read the update manifest (probably newer than this app) — not installed", "更新マニフェストを読み取れません（多くはこのアプリより新しい形式）。インストールしませんでした"),
        new Entry("upd.integrity.schemaUnsupported", "清单格式比这个程序新，先升级程序再更新，没装", "The manifest format is newer than this app — upgrade the app first; not installed", "マニフェストの形式がこのアプリより新しいです。先にアプリを更新してください。インストールしませんでした"),
        new Entry("upd.integrity.fieldRejected", "清单里有认不出的值（路径越界、哈希形状不对、或没有主程序），已拒绝", "The manifest contains unrecognized values (path outside bounds, bad hash shape, or no main executable) — rejected", "マニフェストに認識できない値があります（パス越界・ハッシュ形式不正・主プログラムなし）。拒否しました"),
        new Entry("upd.integrity.versionMismatch", "清单写的版本与要取的那一版不是同一版，没装", "The manifest's version differs from the one being fetched — not installed", "マニフェストのバージョンが取得対象と一致しません。インストールしませんでした"),
        new Entry("upd.integrity.manifestPredatesRelease", "这份清单比它要发布的那一版还早得多，不像是为它签的，没装", "This manifest predates the release it ships by far — not signed for it; not installed", "このマニフェストは対象リリースよりはるかに古く、そのために署名されたものとは思えません。インストールしませんでした"),
        new Entry("upd.integrity.rollbackRefused", "这一版不比本机现在的更新，装了是往回退，所以没装", "This version is not newer than the local one — installing would be a downgrade; not installed", "このバージョンは現在のものより新しくないため、インストールすると downgrade になります。しませんでした"),
        new Entry("upd.integrity.localVersionUnknown", "本机版本号读不到，判断不了新旧，所以没装", "Local version is unreadable — cannot judge new or old; not installed", "ローカルのバージョンが読み取れず、新旧を判定できないためインストールしませんでした"),
        new Entry("upd.integrity.packageHashMismatch", "包的内容与清单里的哈希对不上（可能被换过），已拒绝", "Package content doesn't match the manifest hash (possibly tampered) — rejected", "パッケージの内容がマニフェストのハッシュと一致しません（改ざんの可能性）。拒否しました"),
        new Entry("upd.integrity.packageSizeMismatch", "包的大小与清单里说的不一致，已拒绝", "Package size doesn't match the manifest — rejected", "パッケージのサイズがマニフェストと一致しません。拒否しました"),
        new Entry("upd.integrity.noResult", "更新包没有通过校验，没装", "The package failed verification — not installed", "更新パッケージは検証を通らず、インストールしませんでした"),
        new Entry("upd.stage.staged", "更新包已经摊开并逐颗对过账（还没开始换文件）", "The package has been unpacked and every file accounted for (no files replaced yet)", "更新パッケージを展開し、全ファイルを照合しました（まだ置き換えていません）"),
        new Entry("upd.stage.notTrusted", "这份包没通过校验，压根不该来摊——一步都没往下走", "This package failed verification and should never have been unpacked — nothing proceeded", "このパッケージは検証を通らず、展開されるべきではありませんでした。何も進んでいません"),
        new Entry("upd.stage.packageUnreadable", "更新包不是一颗读得开的压缩包（多半是发布机上打包那一步错了），没装", "The package isn't a readable archive (likely a packaging mistake on the release machine) — not installed", "更新パッケージは読めるアーカイブではありません（公開側のパッケージングミスの可能性）。インストールしませんでした"),
        new Entry("upd.stage.fileMissing", "清单列出的文件在包里找不到，没装", "A file listed in the manifest is missing from the package — not installed", "マニフェスト記載のファイルがパッケージに見つかりません。インストールしませんでした"),
        new Entry("upd.stage.unexpectedEntry", "包里有清单没记的文件（或重名条目），已拒绝", "The package contains files not listed in the manifest (or duplicate entries) — rejected", "パッケージにマニフェスト未記載のファイル（または重複エントリ）があります。拒否しました"),
        new Entry("upd.stage.fileHashMismatch", "摊出来的文件与清单里的哈希对不上（可能被换过），已拒绝", "An unpacked file doesn't match the manifest hash (possibly tampered) — rejected", "展開されたファイルがマニフェストのハッシュと一致しません（改ざんの可能性）。拒否しました"),
        new Entry("upd.stage.payloadUnavailable", "那颗更新包在这台机器上读不到了（下载完被本机清理掉是常事），更新没有开始——本机什么都没改，再查一次会重新下载", "The downloaded package is no longer readable on this machine (it's common for the temp copy to be cleaned up) — update not started; nothing was changed, and checking again will re-download", "ダウンロード済みのパッケージがこの端末で読めなくなりました（一時コピーの削除はよくあること）。更新は開始されておらず、本機は変更されていません。再確認すると再ダウンロードします"),
        new Entry("upd.stage.diskWriteFailed", "本机写不出暂存文件（磁盘满或没权限），更新没有开始", "Can't write staging files (disk full or no permission) — update not started", "一時ファイルを書き込めません（ディスク満杯か権限なし）。更新は開始されていません"),
        new Entry("upd.stage.noResult", "更新包没摊开，没装", "The package was never unpacked — not installed", "更新パッケージは展開されず、インストールしませんでした"),
        new Entry("upd.updater.success", "新版本已经换好并起来了", "The new version has been swapped in and started", "新しいバージョンの入れ替えが完了し、起動しました"),
        new Entry("upd.updater.invalidRequest", "这条更新指令本身不对（路径不对、跨了盘、或不像一棵摊好的树），安装目录没动", "The update instruction itself is invalid (bad path, cross-drive, or not a staged tree) — install directory untouched", "更新指示自体が不正です（パス不正・ドライブ跨ぎ・展開済みツリーでない）。インストール先は変更されていません"),
        new Entry("upd.updater.parentStillRunning", "旧的那一版迟迟没有退出，替换没有开始——安装目录还是原来的", "The old version hasn't exited — swap not started; the install directory is untouched", "旧バージョンが終了しないため、入れ替えは開始されていません。インストール先は元のままです"),
        new Entry("upd.updater.oldTreeNotMoved", "旧目录此刻挪不动，替换没有开始——本机还是原来那一版，程序也已经重新起来；这一版没装上，下次检查更新还要从头再下一遍", "The old directory can't be moved right now — swap not started. This machine is still on the old version and the app has restarted; this version wasn't installed and the next update check will download it from scratch", "旧フォルダーが現在動かせないため、入れ替えは開始されていません。この端末は still 旧バージョンのまま、アプリも再起動済みです。このバージョンは未インストールで、次の確認時に再度ダウンロードになります"),
        new Entry("upd.updater.rolledBack", "这次更新没换成，已经退回原来那一版并重新起来了", "The update didn't go through — rolled back to the previous version and restarted", "更新は完了せず、以前のバージョンに戻して再起動しました"),
        new Entry("upd.updater.rolledBackButNotRunning", "这次更新没换成，文件已退回原来那一版，但它没能自己起来——程序此刻没开着", "The update didn't go through — files rolled back to the previous version, but the app didn't restart on its own; it is not running now", "更新は完了せず、ファイルは以前のバージョンに戻りましたが、アプリは自動で起動しませんでした。現在起動していません"),
        new Entry("upd.updater.untouchedButNotRunning", "本机一个字节都没改，可原来那一版也没能重新起来——程序此刻没开着（日志里写着挪不动的原因）", "Not a single byte was changed on this machine, but the previous version didn't restart either — the app is not running now (the log says why it couldn't be moved)", "この端末は 1 バイトも変更されていませんが、以前のバージョンも再起動できませんでした。アプリは現在起動していません（ログに理由があります）"),
        new Entry("upd.updater.rollbackFailed", "替换中途摔了，而且没能退回原样：请重新安装本程序（旧文件还留在盘上，日志里写着在哪儿）", "The swap failed midway and couldn't roll back: please reinstall this app (the old files are still on disk; the log says where)", "入れ替えの途中で失敗し、元に戻せませんでした：本アプリを再インストールしてください（旧ファイルはディスクに残っています。ログに場所があります）"),
        new Entry("upd.updater.unknown", "更新器没能说清走到了哪一步：程序此刻开不开得起来，它自己也不知道（日志的最后几行写着它知道的）", "The updater couldn't say which step it reached: whether the app starts now is unknown even to it (the last lines of the log say what it knows)", "アップデータはどの段階まで進んだかを報告できませんでした。アプリが起動できるかどうかも不明です（ログの末尾にわかる範囲があります）"),
        new Entry("upd.updater.noResult", "替换没有完成，没动安装目录", "The swap didn't complete — install directory untouched", "入れ替えは完了せず、インストール先は変更されていません"),
        new Entry("upd.launch.started", "替换已经交给更新器，程序马上会重开", "The swap has been handed to the updater — the app will restart shortly", "入れ替えはアップデータに引き継がれました。アプリはまもなく再起動します"),
        new Entry("upd.launch.invalidRequest", "这条更新指令不对，没有去起更新器", "The update instruction is invalid — the updater wasn't started", "更新指示が不正なため、アップデータを起動しませんでした"),
        new Entry("upd.launch.notStarted", "这台机器上的更新器没能跑起来，这次没有自动装——程序还是原来那一版", "The updater on this machine failed to start — nothing was auto-installed; the app is still the old version", "この端末のアップデータが起動できず、自動インストールは行われませんでした。アプリは以前のバージョンのままです"),
        new Entry("upd.launch.noResult", "更新器没有起来，没动安装目录", "The updater never started — install directory untouched", "アップデータは起動せず、インストール先は変更されていません"),
        new Entry("upd.apply.nothingApplied", "这次什么都没换，程序还是原来那一版", "Nothing was swapped this time — the app is still the old version", "今回は何も置き換えられず、アプリは以前のバージョンのままです"),
        new Entry("upd.apply.noResult", "更新没有开始，本机什么都没改", "The update never started — nothing was changed on this machine", "更新は開始されておらず、この端末は変更されていません"),
        new Entry("upd.phase.downloading", "正在下载新版本（这台机器上不改动任何东西）…", "Downloading the new version (nothing is changed on this machine)…", "新しいバージョンをダウンロード中（この端末は変更されません）…"),
        new Entry("upd.phase.inspecting", "正在核对这份包的签名与逐颗哈希…", "Verifying the package signature and per-file hashes…", "パッケージの署名と各ファイルのハッシュを確認中…"),
        new Entry("upd.phase.staging", "正在把新版本摊开（安装目录还没动）…", "Unpacking the new version (install directory untouched)…", "新しいバージョンを展開中（インストール先は未変更）…"),
        new Entry("upd.phase.handingOff", "正在把替换交给更新器…", "Handing the swap to the updater…", "入れ替えをアップデータに引き継いでいます…"),
        new Entry("upd.phase.preparing", "正在准备更新…", "Preparing the update…", "更新を準備中…"),
        new Entry("upd.progress.noTotal", "{0} 已下 {1}", "{0} — {1} downloaded", "{0} {1} をダウンロード済み"),
        new Entry("upd.progress.withTotal", "{0} 已下 {1}／共 {2}（{3}%）", "{0} — {1} of {2} downloaded ({3}%)", "{0} {1}／全 {2} をダウンロード済み（{3}%）"),
        new Entry("upd.cancelled", "这次更新停在半路，本机什么都没改；要装的话再点一次「立即更新」。", "This update stopped halfway — nothing was changed on this machine; to install, click Update now again.", "この更新は途中で停止し、この端末は変更されていません。インストールするには「今すぐ更新」をもう一度クリックしてください。"),
        new Entry("upd.manifest.missing", "清单没写生成时间（这一份不判新鲜度）", "The manifest has no generation time (freshness not judged)", "マニフェストに生成時刻がありません（新鮮度は判定しません）"),
        new Entry("upd.manifest.today", "清单是今天签的", "The manifest was signed today", "マニフェストは今日署名されたものです"),
        new Entry("upd.manifest.recent", "清单签于 {0}", "The manifest was signed {0}", "マニフェストの署名は {0}"),
        new Entry("upd.manifest.stale", "清单签于 {0}（超过 {1} 天，值得核一眼远端挂的是不是很久以前那一份）", "The manifest was signed {0} (over {1} days old — worth checking the remote still points at that same old one)", "マニフェストの署名は {0}（{1} 日超。リモートが古いままのファイルを指していないか確認する価値があります）"),

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
        // 「热榜句厂」一族（批次 WI-2z，P-172 的 Notice 生产者行）：兜底/退避/缓存的成品句全部进表。
        // headline 与从句的连接骨架（compose/join）也是语言内容——"中文括号夹英文从句"正是它长出来的。
        new Entry("trending.fetch.httpBad", "第 1 次请求被拒（{0}），已按 Retry-After 退避重试", "First request was refused (HTTP {0}); retried after a Retry-After backoff", "1回目のリクエストは拒否されました（{0}）— Retry-After に従いバックオフで再試行しました"),
        new Entry("trending.fetch.retryFailed", "重试后仍未成功", "Still failed after the retry", "再試行後も成功しませんでした"),
        new Entry("trending.fetch.htmlFailed", "热榜页请求失败：{0}", "the trending page request failed: {0}", "熱榜ページの要求に失敗しました: {0}"),
        new Entry("trending.fetch.htmlZero", "热榜页解析为 0 条（页面可能改版或被拦截成验证页）", "the trending page parsed to 0 items (it may have changed or an interstitial is blocking it)", "熱榜ページの解析結果は 0 件でした（改版または検証ページの可能性があります）"),
        new Entry("trending.fetch.fallbackApi", "热榜页抓取失败，已用 GitHub 搜索接口兜底", "the trending page failed to fetch; fell back to the GitHub search API", "熱榜ページの取得に失敗したため GitHub 検索 API にフォールバックしました"),
        new Entry("trending.fetch.apiEmpty", "搜索接口没有返回符合该周期/语言的仓库", "the search API returned no repos for that period/language", "検索 API はその期間/言語のリポジトリを返しませんでした"),
        new Entry("trending.fetch.bothEmpty", "热榜页与 GitHub 搜索接口都没能给出结果", "neither the trending page nor the GitHub search API returned a result", "熱榜ページも GitHub 検索 API も結果を返しませんでした"),
        new Entry("trending.fetch.allFailed", "热榜抓取失败", "trending fetch failed", "熱榜の取得に失敗しました"),
        new Entry("trending.notice.write", "缓存未能写入本机，下次打开仍会重新抓取", "the cache could not be written locally — next open will fetch again", "キャッシュをローカルに書き込めませんでした。次回開いたとき再取得されます"),
        new Entry("trending.notice.compose", "{0}（{1}）", "{0} ({1})", "{0}（{1}）"),
        new Entry("trending.notice.join", "；", ", ", "；"),
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
        new Entry("settings.health.eyeRestNote", "开着才建定时表、才探测前台窗口", "Only while on does it keep a schedule or look at the foreground", "オンの時だけ予定表を作り、前景も確認します"),
                    new Entry("settings.health.eyeRestInterval", "间隔多久提醒一次", "How often to remind", "どのくらいの間隔で知らせるか"),
        new Entry("settings.health.eyeRestIntervalNote", "到点先看是否全屏，时刻会有一两分钟浮动", "It checks for a full-screen app first, so the moment drifts a minute or two", "全画面かを見るため、時刻は一、二分ずれます"),
                    new Entry("settings.health.eyeRestNotice", "怎么提醒", "How it reminds", "知らせ方"),
                new Entry("settings.health.deferFullscreen", "前台是全屏应用时让路", "Steps aside while a full-screen app is in front", "前景が全画面アプリなら譲る"),
                    new Entry("settings.health.deferOn", "让路", "Yields", "譲る"),
        new Entry("settings.health.deferOff", "不让路", "Doesn't", "譲らない"),
        new Entry("settings.health.eyeRestPreview", "试一试当前设置的效果", "Try the current settings", "今の設定を試す"),
        // 原来这一句末尾写着"时钟上还会多显示一行「休息 HH:mm」"，那是<b>抄屏幕上那几个字</b>：
        // 组件那一侧的文字不在这一批的射程里，英文界面上那行仍然是中文，照抄就成了假话。
        // 所以两列都改成说"多显示一行下一次休息的时刻"——措辞改动登记在报告 §二百五十五。
        new Entry("settings.health.eyeRestClockHint", "时钟组件右键也有这一节，改的是同一份设置", "The clock widget's menu has the same section — the same settings", "時計ウィジェットのメニューにも同じ節があります（同じ設定）"),
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
        // 组件窗的标记（批次 WI-2n）：一句一行、三列一次带齐（`Entry` 四参必填＝先挂中文回头补日文编不过）。⛔ 这里只放**标记上那句静态话**——代码当场拼的那一族走 LocMessage，见 P-172。
        new Entry("widget.gl.calendar", "月历", "Month view", "月表示"),
        new Entry("widget.gl.calendarTip", "在这一格里翻看整月（含节日标记）", "Tap this to page through the whole month (festival days are marked)", "このボタンで当月をめくれる（祝日印つき）"),
        new Entry("widget.gl.prevMonth", "上一月", "Previous month", "前の月"),
        new Entry("widget.gl.nextMonth", "下一月", "Next month", "次の月"),
        new Entry("widget.gl.today", "今天", "Today", "今日"),
        new Entry("widget.gl.todayTip", "回到本月", "Back to this month", "今月に戻る"),
        new Entry("widget.gl.dotLegend", "· 有圆点 = 当天是节日", "· A dot marks a festival day", "· 点印＝その日は祝日"),
        new Entry("widget.gl.trendingTitle", "GitHub 热榜 · 今日", "GitHub Trending · Today", "GitHub トレンド · 今日"),
        new Entry("widget.gl.trendingRefreshTip", "强制重抓今日榜（每个周期每天至多自动抓一次）", "Force a refetch of today's list (each period auto-fetches at most once a day)", "今日の榜を強制再取得（各期間は1日1回まで自動）"),
        new Entry("widget.gl.frequent", "常看", "Frequently seen", "よく見る"),
        new Entry("widget.gl.empty", "暂无条目", "Nothing here yet", "項目なし"),
        new Entry("widget.calc.tabCompute", "计算", "Calculate", "計算"),
        new Entry("widget.calc.exprHint", "2*(3+4)^2 · 支持 pi/e/%/千分位", "2*(3+4)^2 · pi / e / % / thousands separators supported", "2*(3+4)^2 · pi / e / % / 桁区切り対応"),
        new Entry("widget.calc.copy", "复制结果", "Copy result", "結果をコピー"),
        new Entry("widget.calc.clear", "清空", "Clear", "クリア"),
        new Entry("widget.calc.clearHistory", "清空历史", "Clear history", "履歴を消去"),
        new Entry("widget.calc.historyHint", "历史（点一下填回输入框）", "History (click to refill the box)", "履歴（クリックで入力欄に戻る）"),
        new Entry("widget.calc.historyEmpty", "还没有记录：算好后按回车。", "No records yet - press Enter after a calculation.", "まだ記録なし：計算後に Enter。"),
        new Entry("widget.calc.tabQuick", "常用", "Quick", "常用"),
        new Entry("widget.calc.tabConvert", "换算", "Convert", "換算"),
        new Entry("widget.calc.swapUnits", "交换单位", "Swap units", "単位を入れ替える"),
        new Entry("widget.calc.rateNote", "换算率为内置静态表，断网可用；不含实时汇率。", "Rates come from a built-in static table, so this works offline; no live exchange rates.", "換算率は内蔵の静的表＝オフラインでも使えます。実時間為替は含みません。"),
        new Entry("widget.sw.searchHint", "关键词，边输入边搜索…", "Keyword - searches as you type…", "キーワード（入力しながら検索…）"),
        new Entry("widget.sw.search", "搜索", "Search", "検索"),
        new Entry("widget.sw.sortTitle", "排序", "Sort", "並び順"),
        new Entry("widget.sw.keysHint", "↑↓ 选择 · 回车打开", "Up / Down selects, Enter opens", "↑↓で選択・Enterで開く"),
        new Entry("widget.sw.tagsTitle", "标签", "Tags", "タグ"),
        new Entry("widget.sw.clearTags", "清除标签", "Clear tags", "タグを解除"),
        new Entry("widget.sw.results", "结果", "Results", "結果"),
        new Entry("widget.music.source", "切换音源", "Switch player", "プレイヤーを切替"),
        new Entry("widget.music.sourceTip", "选择要控制的播放器（默认跟随系统当前播放）", "Choose which player to control (follows the system's current playback by default)", "操作するプレイヤーを選択（既定はシステムの再生に沿う）"),
        new Entry("widget.music.seekTip", "点击或拖动可跳转进度", "Click or drag to seek", "クリック／ドラッグで再生位置を移動"),
        new Entry("widget.music.prev", "上一首", "Previous track", "前の曲"),
        new Entry("widget.music.play", "播放", "Play", "再生"),
        new Entry("widget.music.pause", "暂停", "Pause", "一時停止"),
        new Entry("widget.music.next", "下一首", "Next track", "次の曲"),
        new Entry("widget.ql.title", "快捷入口", "Quick links", "ショートカット"),
        new Entry("widget.ql.nameHint", "名称（可留空）", "Name (optional)", "名前（空欄可）"),
        new Entry("widget.ql.uriHint", "网址 / 文件路径（粘贴后回车）", "URL / file path (paste, then Enter)", "URL / ファイルパス（貼り付けて Enter）"),
        new Entry("widget.ql.add", "添加", "Add", "追加"),
        new Entry("widget.ql.pickFile", "选择文件…", "Choose file…", "ファイルを選択…"),
        new Entry("widget.ql.pickFolder", "选择文件夹…", "Choose folder…", "フォルダを選択…"),
        new Entry("widget.weather.viewTip", "在多日概览与今日逐时之间切换", "Switch between the multi-day overview and today's hourly view", "数日概览と今日の1時間表示を切替"),
        new Entry("widget.weather.unitTip", "温度单位（华氏模式下风速同时换成 mph）", "Temperature unit (Fahrenheit also switches wind speed to mph)", "温度単位（華氏では風速も mph に変わる）"),
        new Entry("widget.weather.pickCity", "点击上方城市名，选择要查看天气的城市", "Tap the city name above to choose which city's weather to show", "上の都市名をクリックして、天気を見る都市を選ぶ"),
        new Entry("widget.cd.deleteTip", "删除这一条", "Delete this entry", "この項目を削除"),
        new Entry("widget.cd.nameHint", "名称（留空则按日期自动起名）", "Name (blank = named from the date)", "名前（空欄なら日付から自動）"),
        new Entry("widget.cd.yearly", "每年重复（纪念日）", "Repeat every year (anniversary)", "毎年繰り返し（記念日）"),
        new Entry("widget.cd.save", "保存", "Save", "保存"),
        new Entry("widget.cd.add", "＋ 新建截止日 / 纪念日", "＋ New due date / anniversary", "＋ 新しい期限日／記念日"),
        new Entry("widget.mon.none", "未选择任何指标，点下面按钮勾回来", "No metrics selected - use the buttons below to pick some", "指標未選択：下のボタンで選ぶ"),
        new Entry("widget.mon.pick", "选择指标", "Choose metrics", "指標を選択"),
        new Entry("widget.mon.cpu", "CPU 占用", "CPU usage", "CPU 使用率"),
        new Entry("widget.mon.mem", "内存占用", "Memory usage", "メモリ使用率"),
        new Entry("widget.mon.net", "网速", "Network speed", "通信速度"),
        new Entry("widget.win.collapseTip", "收起 / 展开为胶囊", "Collapse / expand to a pill", "折りたたむ／カプセルに展開"),
        new Entry("widget.win.pinTip", "置顶显示", "Keep on top", "手前に表示"),
        new Entry("widget.win.unpinTip", "取消置顶", "Stop keeping on top", "手前から解除"),
        new Entry("widget.win.addTip", "添加/管理组件", "Add / manage widgets", "ウィジェット追加・管理"),
        new Entry("widget.win.hideTip", "隐藏（托盘/设置中可恢复）", "Hide (restorable from the tray or settings)", "非表示（トレイ／設定で復帰できる）"),
        new Entry("widget.win.removeTip", "移除组件（设置中可重新添加）", "Remove this widget (re-add it from settings)", "ウィジェットを削除（設定から再追加できる）"),
        // 窗口标题前缀（Alt+Tab／任务切换器那一格）。⚠ 这里接的是<b>名字</b>而不是"图标 名字"：
        // 图标归标题栏那颗元素一个出口，名字里再夹一颗就是"两个图标"（坑表 #337）。
        new Entry("widget.win.windowTitle", "StarMark 组件 - {0}", "StarMark widget - {0}", "StarMark ウィジェット - {0}"),
        // ===== 组件窗右键那一族（批次 WI-37-A）=====
        // 中文列<b>逐字取自改键前 code-behind 里的那一句</b>：这一刀只搬地方，不顺手改措辞。
        // 菜单每次右键重建 ⇒ 构建那一刻 Locale.T 就够（WI-2o 那条口径），⛔ 不给它挂附带属性。
        new Entry("widget.menu.removeOne", "移除本组件", "Remove this widget", "このウィジェットを削除"),
        new Entry("widget.menu.main", "打开 StarMark 主窗口", "Open the StarMark window", "StarMark のメインウィンドウを開く"),
        new Entry("widget.menu.manage", "管理组件…", "Manage widgets…", "ウィジェット管理…"),
        new Entry("widget.menu.rename", "重命名…", "Rename…", "名前を変更…"),
        new Entry("widget.menu.appearance", "外观…", "Appearance…", "外観…"),
        new Entry("widget.menu.saveLayout", "保存当前组件布局…", "Save the current layout…", "現在のレイアウトを保存…"),
        new Entry("widget.menu.applyLayout", "应用布局", "Apply layout", "レイアウトを適用"),
        // 布局与快照那两行列的是"名字（摘要）"——两处同一形状，⛔ 各写一行迟早只改一处。
        new Entry("widget.menu.nameWithSummary", "{0}（{1}）", "{0} ({1})", "{0}（{1}）"),
        new Entry("widget.menu.saveSnapshot", "保存当前布局与数据…", "Save layout & data…", "レイアウトとデータを保存…"),
        new Entry("widget.menu.applySnapshot", "应用快照", "Apply snapshot", "スナップショットを適用"),
        new Entry("widget.menu.applyFailed", "未能应用快照", "Couldn’t apply the snapshot", "スナップショットを適用できませんでした"),
        new Entry("widget.menu.applyFailedBody", "{0}——为防数据丢失已中止，当前状态未改动。",
            "{0} — stopped to protect your data; nothing was changed.", "{0}——データ保護のため中止し、変更は加えていません。"),
        new Entry("widget.menu.rollbackFailed", "生成「应用前」回滚点失败",
            "Failed to create the “before applying” rollback point", "「適用前」ロールバック地点を作成できませんでした"),
        new Entry("widget.menu.manageSnapshots", "管理快照…", "Manage snapshots…", "スナップショット管理…"),
        new Entry("widget.menu.saveLayoutTitle", "保存当前组件布局", "Save the current layout", "現在のレイアウトを保存"),
        new Entry("widget.menu.saveLayoutBody",
            "记下当前屏幕上所有可见组件的位置、大小与各自的外观配置（材质/颜色等），形成一套可复用的模板。不含任何条目内容数据（待办/随记/快捷入口/笔记）。应用布局时，不属于该布局的组件会被隐藏（内容保留）。",
            "Records every visible widget's position, size and own appearance (material, colour, …) as a reusable template. "
            + "It holds no item data (todos / notes / quick launch / memos). Applying a layout hides the widgets that aren't part of it (their data stays).",
            "現在表示中のすべてのウィジェットについて、位置・サイズ・それぞれの外観（材質／色など）を、再利用できるテンプレートとして残します。項目のデータ（ToDo／走り書き／クイック起動／メモ）は含みません。レイアウトを適用すると、そのレイアウトに属さないウィジェットは隠れます（内容は残ります）。"),
        new Entry("widget.menu.saveLayoutHint", "例如：工作模式", "e.g. Work mode", "例：作業モード"),
        new Entry("widget.menu.noneVisible", "当前没有可见的组件", "No widgets are visible", "表示中のウィジェットがありません"),
        new Entry("widget.menu.noLayoutContent", "没有可保存的布局内容。", "There is nothing to save.", "保存できるレイアウト内容がありません。"),
        new Entry("widget.menu.saveSnapshotTitle", "保存当前布局与数据", "Save layout & data", "レイアウトとデータを保存"),
        new Entry("widget.menu.saveSnapshotBody",
            "把当前屏幕上所有组件的位置、外观，连同各自的数据（快捷入口 / 待办 / 随记 / 条目格查询）一起存成一个不可变快照点。之后「应用快照」即回到这一刻，并会先自动生成一个「应用前」回滚点。",
            "Saves every widget's position and appearance together with its own data (quick launch / todos / notes / library queries) "
            + "as an immutable point. “Apply snapshot” later returns to this moment, after making a “before applying” rollback point first.",
            "現在画面上のすべてのウィジェットの位置・外観を、それぞれのデータ（クイック起動／ToDo／走り書き／ライブラリの照会）とともに、変更できない一点として保存します。後で「スナップショットを適用」するとこの時点に戻り、その前に「適用前」ロールバック地点が自動で作られます。"),
        new Entry("widget.menu.saveSnapshotHint", "例如：上线前", "e.g. Before a release", "例：リリース前"),
        new Entry("widget.menu.noneWidgets", "当前没有组件", "There are no widgets", "ウィジェットがありません"),
        new Entry("widget.menu.noSnapshotContent", "没有可保存的快照内容。", "There is nothing to snapshot.", "スナップショットにできる内容がありません。"),
        new Entry("widget.menu.snapshotSaved", "已保存快照", "Snapshot saved", "スナップショットを保存しました"),
        new Entry("widget.menu.snapshotSavedBody",
            "「{0}」已记录（{1}）。可在主窗口「快照」页或右键「应用快照」里回到这一刻。",
            "“{0}” is recorded ({1}). Return to this moment from the “Snapshots” page or the right-click “Apply snapshot”.",
            "「{0}」を記録しました（{1}）。メインウィンドウの「スナップショット」ページか、右クリックの「スナップショットを適用」からこの時点に戻れます。"),
        new Entry("widget.menu.snapshotFailed", "保存快照失败", "Saving the snapshot failed", "スナップショットの保存に失敗しました"),
        new Entry("widget.menu.snapshotFailedBody", "读取组件数据时出错，快照未保存：{0}",
            "Widget data could not be read, so nothing was saved: {0}", "ウィジェットのデータ読み取りに失敗したため、保存されませんでした：{0}"),
        new Entry("widget.menu.renameTitle", "重命名组件", "Rename this widget", "ウィジェットの名前を変更"),
        new Entry("widget.menu.renameBody", "给这个组件取个名字（留空则恢复默认名称「{0}」）。",
            "Give this widget a name (leave it empty to restore the default “{0}”).",
            "このウィジェットに名前を付けてください（空欄なら既定名の「{0}」に戻ります）。"),
        new Entry("widget.chrome.collapse", "收起为胶囊", "Collapse into a pill", "カプセルに折りたたむ"),
        new Entry("widget.chrome.expand", "展开组件", "Expand the widget", "ウィジェットを展開"),
        new Entry("widget.chrome.hideFrame", "隐藏外壳（仅内容）", "Hide the frame (content only)", "枠を隠す（内容のみ）"),
        new Entry("widget.chrome.showFrame", "显示外壳（标题栏）", "Show the frame (title bar)", "枠を表示（タイトルバー）"),
        new Entry("widget.chrome.privacy", "隐私模式（胶囊态隐藏标题）", "Privacy mode (hide the title in pill form)", "プライバシーモード（カプセル時にタイトルを隠す）"),
        // 占位那一行是这块内容<b>唯一的表面</b>（没有别的元素画图标），所以标题里那颗 glyph 在这里是唯一的图标。
        // ⚠ 措辞只说今天的事实：`ShippedUiTextNeverPromisesAFutureBatch` 那颗闸门不许界面文本写"暂未实现"
        // ——那一句话是在替程序承诺一件还没做的事（本批第一版就是被它当场咬住的）。
        new Entry("widget.notImplemented", "「{0}」没有可显示的内容", "“{0}” has nothing to show", "「{0}」は表示できる内容がありません"),

        // 快捷启动拖放那两句（批次 WI-37-A）：遮罩里那句挂键（树里的元素），
        // 跟随光标那句由 DragOver 现取词（系统画的，不是树里的元素）。术语与 widget.kind.quickLaunch 同一份。
        new Entry("widget.drop.hint", "松开以添加到快捷启动", "Release to add to Quick launch", "ドロップしてクイック起動に追加"),
        new Entry("widget.drop.caption", "添加到快捷启动", "Add to Quick launch", "クイック起動に追加"),

        // ===== 护眼 · 休息提醒整节（批次 WI-37-B）=====
        // 这一族过去住在 Core：`EyeRestPolicy.IntervalLabels`／`NoticeLabels`／`NoticeHint` 直接拼中文成品串，
        // 于是下拉、状态行、幕布、组件菜单四处各自钉死一份语言（＝P-172 那一族）。Core 现在只交「键＋读数」，
        // 句子只在这里有一份。
        // ⚠ <b>间隔这六档是一档一颗键，⛔ 不是一颗 "{0} 分钟" 加实参</b>：这一排走的是「数据项＋模板」，
        // 而模板那颗 TextBlock 只能交出一颗键——实参信箱由代码 `LocText.SetKey(el, key, args)` 填，
        // 模板没有那个机会。真机英文档上这就是坏的一格：闭合的格子显示成 "(0) min"（形状闸门全绿）。
        // 秒数不受此限（它只出现在代码里挂键的那几句：幕布、状态行、提示卡，那些都走 SetKey/SetMessage）。
        new Entry("health.eyeRest.interval.15", "15 分钟", "15 min", "15 分"),
        new Entry("health.eyeRest.interval.20", "20 分钟", "20 min", "20 分"),
        new Entry("health.eyeRest.interval.30", "30 分钟", "30 min", "30 分"),
        new Entry("health.eyeRest.interval.45", "45 分钟", "45 min", "45 分"),
        new Entry("health.eyeRest.interval.60", "60 分钟", "60 min", "60 分"),
        new Entry("health.eyeRest.interval.90", "90 分钟", "90 min", "90 分"),
        // 提示卡那两句是屏幕上的字（批次 RV-1 自绘那张卡，四个降级出口共用）⇒ 同样只许有一份。
        new Entry("health.card.title", "该休息一下了", "Time for a break", "そろそろ休憩です"),
        new Entry("health.card.body", "已连续工作约 {0}。看看远处，放松一下眼睛。",
            "You've been working for about {0}. Look into the distance and relax your eyes.",
            "約 {0} 連続で作業しました。遠くを見て目を休めましょう。"),
        new Entry("health.eyeRest.notice.card", "只发一张右下角提示卡（什么都不盖）",
            "Just a card in the lower-right corner (nothing covers the screen)",
            "右下にカードを出すだけ（画面を何も覆わない）"),
        new Entry("health.eyeRest.notice.curtain", "盖一层暗幕（点一下屏幕即可提前结束）",
            "Dim the screen (click anywhere to end it early)", "暗幕を覆う（画面をクリックすると早めに終わる）"),
        new Entry("health.eyeRest.notice.forced", "盖一层暗幕且 {0} 秒不可跳过（强制休息）",
            "Dim the screen and hold it for {0} s (forced break)", "暗幕を覆い {0} 秒は飛ばせない（強制休憩）"),
        new Entry("health.eyeRest.hint.forced", "这一轮是强制休息：{0} 秒走完会自动恢复，中途不能提前结束。",
            "This round is a forced break: it restores after {0} s and can't be ended early.",
            "今回は強制休憩です：{0} 秒で自動的に戻り、途中で終了できません。"),
        new Entry("health.eyeRest.hint.skippable", "想提前结束就点一下屏幕任意处；什么都不做的话，时间到了会自动恢复。",
            "Click anywhere to end it early; do nothing and it restores on its own.",
            "早めに終えるには画面のどこかをクリック。何もしなければ時間になると自動的に戻ります。"),
        // 幕布上那三行：标题、为什么提醒、怎么退出——<b>一行一颗键，换行交给布局</b>。
        // ⛔ 不在表里写 `\n` 把两句合成一句：那一字符落进表行就是控制字符（闸门 NoRowCarriesAnInvisibleControlCharacter），
        // 而且两种落法在代码里都得拼串，拼串那一刻语言就被钉死了。退出那句另挂一颗键，与设置页那行同源（EyeRestPolicy）。
        new Entry("health.curtain.title", "让眼睛休息一下", "Rest your eyes", "目を休めましょう"),
        new Entry("health.curtain.reason", "你已经连续工作约 {0}。把视线挪到几米外的地方，让睫状肌松一松",
            "You've been working for about {0}. Let your eyes focus on something a few metres away.",
            "約 {0} 連続で作業しました。数メートル先に目を向け、ピント筋を休めましょう。"),

        // 设置页那一行状态（VM 算出来的话 ⇒ 交「键＋读数」，由 LocText 那颗门当场贴；见 WI-2k 的门）
        new Entry("health.status.off", "已关闭：不建定时器、不探测前台窗口，屏幕上不会出现任何东西。",
            "Off: no timer is created and no foreground window is probed — nothing appears on screen.",
            "オフ：タイマーも前面ウィンドウの確認もせず、画面には何も出ません。"),
        new Entry("health.status.on", "连续工作约 {0} 分钟后：{1}{2}。{3}{4}",
            "After about {0} of work: {1}{2}. {3}{4}", "約 {0} 連続で作業すると：{1}{2}。{3}{4}"),
        new Entry("health.status.deferOn", "前台是全屏应用（放 PPT / 放映）时自己让路",
            "it steps aside when a fullscreen app is in front (slides, video)",
            "全画面アプリ（プレゼン／動画）の前では引っ込み"),
        new Entry("health.status.deferOff", "全屏应用下也照常提醒", "it reminds even in fullscreen apps",
            "全画面アプリでも普通に提醒"),
        new Entry("health.status.nextDue", "下一次大约 {0}。", "Next one around {0}.", "次回はおよそ {0}。"),
        new Entry("health.status.notRunning", "开关是开着的，但节拍表没挂上（原因见日志）——当前不会提醒。",
            "It's switched on, but the schedule isn't armed (see the log) — nothing will fire now.",
            "オンですが周期タイマーが設定されていません（理由はログ）。今は提醒しません。"),
        new Entry("health.preview.curtain", "已演一次：暗幕已经盖上。{0}",
            "Previewed: the dim layer is up. {0}", "試しました：暗幕が出ました。{0}"),
        new Entry("health.preview.card", "已演一次：右下角那张提示卡真的贴上了屏幕（贴不上时才会退到主窗提示条）。",
            "Previewed: the corner card really reached the screen (only if that fails does it fall back to the window banner).",
            "試しました：右下のカードが実際に出ました（出せなかったときだけ主窓のバーに退避します）。"),
        new Entry("health.preview.busy", "幕布还盖着屏，等这一轮结束再按（能不能提前点掉按上面那一档的说法）。",
            "The dim layer is still up — wait for this round, then press again (whether you can click it away depends on the option above).",
            "暗幕はまだ出ています。この回が終わってから押してください（クリックで消せるかは上の選択肢次第）。"),
        new Entry("health.preview.off", "没演成：护眼开关没打开时不建节拍表，也就没有可演的东西（先开启本卡片顶部的开关）。",
            "Nothing to preview: with the switch off there is no schedule and nothing to show (turn on the switch at the top of this card first).",
            "実行できませんでした：オフでは周期タイマーを作らないため、試すものはありません（まずこのカード上部のスイッチをオンに）。"),

        // 时钟组件右键里那一节（菜单每次右键重建 ⇒ 构建那一刻现取词）
        new Entry("widget.health.section", "护眼 · 休息提醒", "Eye care · Break reminders", "目のケア · 休憩リマインド"),
        new Entry("widget.health.toggle", "开启休息提醒", "Turn on break reminders", "休憩リマインドをオン"),
        new Entry("widget.health.gap", "间隔：{0}", "Interval: {0}", "間隔：{0}"),
        new Entry("widget.health.notice", "提醒形式：{0}", "Reminder style: {0}", "提醒の形：{0}"),
        new Entry("widget.health.tryIt", "试一试当前效果（不动节拍）",
            "Preview the current effect (doesn't change the schedule)", "今の効果を試す（周期は変わらない）"),
        new Entry("widget.health.tryItNeedsOn", "试一试（要先开启休息提醒，上面第一项）",
            "Preview (turn on break reminders first, the item above)", "試す（まず上の項目で休憩リマインドをオンに）"),
        new Entry("widget.health.nextDue", "下一次大约 {0}（到点前还会看一眼前台是否全屏）",
            "Next one around {0} (it checks whether you're fullscreen first)", "次回はおよそ {0}（出る前に全画面かを確認）"),
        new Entry("widget.health.notRunning", "开关已开，但计时器没起来（设置页「健康与诊断」有原因）",
            "It's on, but the timer isn't running (the Health page explains why)",
            "オンですがタイマーが動いていません（理由は設定の「健康と診断」）"),
        new Entry("widget.health.off", "未开启", "Not turned on", "オフ"),
        new Entry("widget.health.full", "完整设置（全屏让路 / 下一次几点）…",
            "Full settings (fullscreen defer / when the next one is)…", "詳しい設定（全画面譲路／次回時刻）…"),
        new Entry("widget.health.previewFailed", "没演成", "Couldn't preview", "試せませんでした"),
        new Entry("widget.health.previewBlocked", "上一轮的暗幕还盖着屏，等这一轮结束再按。",
            "The previous dim layer is still up — wait for it, then press again.",
            "前の暗幕まだ出ています。終わってからもう一度押してください。"),
        new Entry("widget.health.previewNoBeat", "节拍表没挂上（原因见日志），先在本节第一项开启休息提醒。",
            "No schedule is armed (see the log) — turn on break reminders, the first item in this section.",
            "周期タイマーが設定されていません（理由はログ）。まずこの節の第一項目でオンにしてください。"),


        new Entry("widget.menuShowAll", "全部显示", "Show all", "すべて表示"),
        new Entry("widget.menuHideAll", "全部隐藏", "Hide all", "すべて非表示"),
        new Entry("widget.menuToggleAll", "显示/隐藏全部组件", "Show or hide all widgets", "すべてのウィジェットを表示／非表示"),
        new Entry("widget.menuManage", "在设置中管理…", "Manage in Settings…", "設定で管理…"),
        new Entry("tray.show", "显示 StarMark", "Show StarMark", "StarMark を表示"),
        new Entry("tray.widgetsMenu", "桌面组件", "Desktop widgets", "デスクトップウィジェット"),
        new Entry("tray.settings", "设置", "Settings", "設定"),
        new Entry("tray.exit", "退出", "Exit", "終了"),
        new Entry("tray.tip", "StarMark", "StarMark", "StarMark"),
        new Entry("tray.screenshot", "截图（框选区域）", "Capture (select an area)", "スクリーンショット（範囲選択）"),
        new Entry("tray.pin", "贴图（框选后钉在桌面）", "Pin (select, then stick to the desktop)", "ピン（範囲選択してデスクトップに固定）"),
        new Entry("tray.ocr", "识字（框选后复制文字）", "OCR (select, then copy the text)", "文字認識（範囲選択してコピー）"),
        new Entry("tray.canvas", "屏幕画布（讲解时在屏幕上画）", "Screen canvas (draw over the screen)", "スクリーンキャンバス（画面に描き込む）"),
        new Entry("tray.pinsShowHideNone", "显示 / 收起所有贴图（当前没有贴图）", "Show or collapse all pins (none right now)", "すべてのピンを表示／収納（現在 0 枚）"),
        new Entry("tray.pinsShowAll", "显示所有贴图", "Show all pins", "すべてのピンを表示"),
        new Entry("tray.pinsHideAll", "收起所有贴图", "Collapse all pins", "すべてのピンを収納"),
        new Entry("tray.pinsThrough", "贴图忽略鼠标", "Ignore mouse for pins", "ピンのマウス無視"),
        new Entry("tray.pinsCloseAll", "关闭所有贴图", "Close all pins", "すべてのピンを閉じる"),
        new Entry("tray.topmostAll", "所有组件置顶 / 不置顶", "Toggle topmost for all widgets", "全ウィジェットの最前面切り替え"),
        new Entry("tray.themeDefault", "主题 · 跟随系统", "Theme · follow the system", "テーマ · システムに従う"),
        new Entry("tray.themeLight", "主题 · 浅色", "Theme · light", "テーマ · ライト"),
        new Entry("tray.themeDark", "主题 · 深色", "Theme · dark", "テーマ · ダーク"),
        new Entry("tray.perfBalanced", "性能 · 均衡", "Performance · balanced", "パフォーマンス · バランス"),
        new Entry("tray.perfSaver", "性能 · 省资源", "Performance · resource saver", "パフォーマンス · 省リソース"),
        new Entry("tray.autostart", "开机自动启动", "Start with Windows", "Windows 起動時に自動開始"),
        new Entry("tray.hotkeysOn", "全局快捷键已启用", "Global hotkeys enabled", "グローバルホットキー有効"),
        new Entry("tray.checkUpdate", "检查更新", "Check for updates", "更新を確認"),
        new Entry("tray.groupParent", "贴图组（整组隐藏 / 忽略鼠标 / 关闭）", "Pin groups (hide / ignore mouse / close)", "ピングループ（一括収納／マウス無視／閉じる）"),
        new Entry("pin.groupShow", "显示这一组", "Show this group", "このグループを表示"),
        new Entry("pin.groupHide", "隐藏这一组", "Hide this group", "このグループを隠す"),
        new Entry("pin.groupThroughOn", "取消这组忽略鼠标", "Stop ignoring the mouse for this group", "このグループのマウス無視を解除"),
        new Entry("pin.groupThroughOff", "这组忽略鼠标", "Ignore the mouse for this group", "このグループをマウス無視"),
        // 批次 WI-46c：这一行交**纯短语**。从前它带"（{0} 张）"，于是卡片正文里"名字后面带张数"那个形状
        // 有了第二个出处（`pin.groupLabel` 是它的主人）；现在张数由 GroupLabel 拼一次。
        new Entry("pin.groupClose", "关闭这组", "Close this group", "このグループを閉じる"),
        new Entry("pin.groupLabel", "{0}（{1} 张）", "{0} ({1} pins)", "{0}（{1} 枚）"),
        new Entry("widget.kind.quickLaunch", "快捷启动", "Quick launch", "クイック起動"),
        new Entry("widget.kind.todo", "待办", "To-do", "ToDo"),
        new Entry("widget.kind.quickNote", "随记", "Quick note", "クイックメモ"),
        new Entry("widget.kind.clock", "时钟", "Clock", "時計"),
        new Entry("widget.kind.search", "快捷搜索", "Quick search", "クイック検索"),
        new Entry("widget.kind.tagGrid", "标签", "Tags", "タグ"),
        new Entry("widget.kind.clipboard", "剪贴板", "Clipboard", "クリップボード"),
        new Entry("widget.kind.activity", "最近活动", "Recent activity", "最近の操作"),
        new Entry("widget.kind.pinned", "置顶条目", "Pinned items", "ピン留め項目"),
        new Entry("widget.kind.glance", "今日速览", "Today at a glance", "今日の一目"),
        new Entry("widget.kind.weather", "天气", "Weather", "天気"),
        new Entry("widget.kind.music", "音乐", "Music", "音楽"),
        new Entry("widget.kind.calc", "计算器", "Calculator", "電卓"),
        new Entry("widget.kind.worldClock", "世界时钟", "World clock", "世界時計"),
        new Entry("widget.kind.countdown", "倒计时", "Countdown", "カウントダウン"),
        new Entry("widget.kind.focus", "番茄钟", "Focus timer", "ポモドーロ"),
        // ===== 番茄钟与倒计时"到点了"那两张卡的句子（批次 WI-47，P-177 收尾）=====
        // ⚠ 抬头那颗「番茄钟」⛔ 再立一行——就是上面 `widget.kind.focus`（组件名只有一个主人）。
        new Entry("widget.countdown.dueTitle", "倒计时到点", "Countdown reached", "カウントダウンの時刻になりました"),
        new Entry("widget.focus.endedFocusing", "专注结束", "Focus session ended", "集中が終わりました"),
        new Entry("widget.focus.endedResting", "休息结束", "Break ended", "休憩が終わりました"),
        new Entry("widget.focus.staleBody", "时间早已到点（机器可能睡眠过），不自动进休息",
                  "It was due long ago (the machine may have slept), so no break was started",
                  "随分前に時刻を迎えていました（マシンがスリープした可能性）、休憩には入りません"),
        new Entry("widget.focus.restNowBody", "该休息一下了", "Time for a break", "休憩の時間です"),
        new Entry("widget.focus.nextRoundBody", "休息结束，可以开始下一轮", "Break over — ready for another round", "休憩が終わりました。次のラウンドを始められます"),
        new Entry("widget.kind.systemMonitor", "系统监控", "System monitor", "システムモニター"),
        new Entry("widget.pomo.taskHint", "这一轮在做什么？（可留空）", "What are you doing this round? (optional)", "今回やること（空欄可）"),
        new Entry("widget.pomo.stop", "停止", "Stop", "停止"),
        new Entry("widget.pomo.focus", "专注", "Focus", "フォーカス"),
        new Entry("widget.pomo.rest", "休息", "Break", "休憩"),
        new Entry("widget.todo.inputHint", "添加待办，回车确认…", "Add a todo, press Enter to confirm…", "ToDo を追加、Enter で確定…"),
        new Entry("widget.todo.colorTip", "颜色标记", "Colour marker", "色マーク"),
        new Entry("widget.todo.dueTip", "截止日期", "Due date", "期限日"),
        new Entry("widget.todo.undo", "撤销", "Undo", "元に戻す"),
        new Entry("widget.grid.apply", "应用", "Apply", "適用"),
        new Entry("widget.note.hint", "随手记一笔，Ctrl+Enter 保存…", "Jot something down; Ctrl+Enter saves…", "走り書き、Ctrl+Enter で保存…"),
        new Entry("widget.note.save", "保存随记", "Save note", "メモを保存"),
        new Entry("widget.clock.addZone", "＋ 添加时区…", "＋ Add a time zone…", "＋ タイムゾーン追加…"),
        new Entry("widget.clock.removePin", "移除这个点位", "Remove this clock", "この時計を削除"),
        // 时钟上那一行护眼读数（批次 WI-44）。⚠ 这一行由组件的定时器**每秒重算**，所以取词就在调用点当场发生即合法
        // （与瞬时提示卡同理，⛔ 挂 LocText：那一行不是靠重贴活的，是靠下一拍）。
        new Entry("widget.clock.restNow", "休息中", "On break", "休憩中"),
        new Entry("widget.clock.nextRest", "休息 {0}", "Break at {0}", "休憩 {0}"),

        // ────────── 星期名的三副面孔（批次 WI-37-C）──────────
        // 位序＝(int)DayOfWeek（Sunday＝0），与 AlarmPolicy.BitFor／MonthGrid.FirstSlot 同一套序号口径。
        // 全称（时钟、今日速览）／短名（闹钟那一排、待办截止日）／词根（月历表头：一格一个字，那是布局，
        // ⛔ 不许把整词塞进表头）。中文的「周日＝周＋日」「星期日＝星期＋日」这一层拼法由表自己守住
        // （证人 WeekdayNameGateTests.EachChineseSpellingIsItsStemWithItsPrefix —— 代码里再拼一次就是第二份真值）。
        new Entry("time.weekday.full.0", "星期日", "Sunday", "日曜日"),
        new Entry("time.weekday.full.1", "星期一", "Monday", "月曜日"),
        new Entry("time.weekday.full.2", "星期二", "Tuesday", "火曜日"),
        new Entry("time.weekday.full.3", "星期三", "Wednesday", "水曜日"),
        new Entry("time.weekday.full.4", "星期四", "Thursday", "木曜日"),
        new Entry("time.weekday.full.5", "星期五", "Friday", "金曜日"),
        new Entry("time.weekday.full.6", "星期六", "Saturday", "土曜日"),
        new Entry("time.weekday.short.0", "周日", "Sun", "日"),
        new Entry("time.weekday.short.1", "周一", "Mon", "月"),
        new Entry("time.weekday.short.2", "周二", "Tue", "火"),
        new Entry("time.weekday.short.3", "周三", "Wed", "水"),
        new Entry("time.weekday.short.4", "周四", "Thu", "木"),
        new Entry("time.weekday.short.5", "周五", "Fri", "金"),
        new Entry("time.weekday.short.6", "周六", "Sat", "土"),
        // 英文那一列只有首字母：月历那一格装得下一个字，装得下三个字母就装不下别的列（同一格里"更清楚"买不回来）。
        new Entry("time.weekday.stem.0", "日", "S", "日"),
        new Entry("time.weekday.stem.1", "一", "M", "月"),
        new Entry("time.weekday.stem.2", "二", "T", "火"),
        new Entry("time.weekday.stem.3", "三", "W", "水"),
        new Entry("time.weekday.stem.4", "四", "T", "木"),
        new Entry("time.weekday.stem.5", "五", "F", "金"),
        new Entry("time.weekday.stem.6", "六", "S", "土"),

        // ────────── 闹钟那一族（批次 WI-37-C：Core 交"还没取词的那句"，界面在贴的那一刻取词）──────────
        // 三条状态句各自带着"本程序运行时才提醒"这一句实话（⛔ 只在其中一条上写＝另外两条读起来像系统闹钟）；
        // 证人 AlarmWiringGateTests 按表里那一列逐条数，不再数代码里的字面量。
        new Entry("alarm.timeInvalid", "时间无效", "Invalid time", "時刻が無効"),
        new Entry("alarm.days.once", "单次", "Once", "1 回だけ"),
        new Entry("alarm.days.daily", "每天", "Daily", "毎日"),
        new Entry("alarm.days.weekdays", "工作日", "Weekdays", "平日"),
        new Entry("alarm.days.weekends", "周末", "Weekends", "週末"),
        // 快捷档之外（"只有周三和周六"）才是逐天勾出来的组合：一颗键只管一种条数，⛔ 一条模板配七个槽再拿空格填空——
        // 那会在句尾留下看不见的空格，也会让"少一个槽"这种错读起来像正常。
        // 日文那一列用「・」接（中文与英文用空格）：⇒ 这一族不是"两语同形的胶水行"，日文列照旧要过那颗复印机闸门。
        new Entry("alarm.days.pick1", "{0}", "{0}", "{0}"),
        new Entry("alarm.days.pick2", "{0} {1}", "{0} {1}", "{0}・{1}"),
        new Entry("alarm.days.pick3", "{0} {1} {2}", "{0} {1} {2}", "{0}・{1}・{2}"),
        new Entry("alarm.days.pick4", "{0} {1} {2} {3}", "{0} {1} {2} {3}", "{0}・{1}・{2}・{3}"),
        new Entry("alarm.days.pick5", "{0} {1} {2} {3} {4}", "{0} {1} {2} {3} {4}", "{0}・{1}・{2}・{3}・{4}"),
        new Entry("alarm.days.pick6", "{0} {1} {2} {3} {4} {5}", "{0} {1} {2} {3} {4} {5}", "{0}・{1}・{2}・{3}・{4}・{5}"),
        // 一行闹钟的全部设定：钟点（＋名字）· 重复档。名字是用户打的字，不参与翻译，所以两种形状各一颗键。
        new Entry("alarm.line", "{0} {1} · {2}", "{0} {1} · {2}", "{0} {1}・{2}"),
        new Entry("alarm.lineNoLabel", "{0} · {1}", "{0} · {1}", "{0}・{1}"),
        new Entry("alarm.row.offTitle", "（已关）{0}", "{0} (off)", "{0}（オフ）"),
        new Entry("alarm.pending.one", "闹钟 {0} 待确认", "Alarm {0} awaits confirmation", "アラーム {0} 確認待ち"),
        new Entry("alarm.pending.many", "闹钟 {0} 等 {1} 条待确认", "Alarm {0}: {1} rounds await confirmation", "アラーム {0}：{1} 件確認待ち"),
        // 解析输入的五句：越界要说得出越了哪一边界（"格式不对"那种红等于没答）。
        new Entry("alarm.input.empty", "请写时间，例如 7:30 起床", "Write a time, e.g. 7:30 Wake up", "時刻を入力（例：7:30 起床）"),
        new Entry("alarm.input.shape", "开头请写成 7:30 或 07:30（后面可以跟名字）", "Start with 7:30 or 07:30 (a name may follow)", "冒頭に 7:30 または 07:30（名前の続き可）"),
        new Entry("alarm.input.minuteTwo", "分钟要写两位，例如 7:05", "Minutes need two digits, e.g. 7:05", "分は 2 桁（例：7:05）"),
        new Entry("alarm.input.hourRange", "小时要在 0–23 之间（24:00 请写 0:00）", "Hours must be 0–23 (write 0:00 for midnight)", "時は 0–23（24:00 は 0:00）"),
        new Entry("alarm.input.minuteRange", "分钟要在 0–59 之间", "Minutes must be 0–59", "分は 0–59"),
        new Entry("alarm.unreadable", "看不懂这个时间。", "I can't read that time.", "時刻を読み取れません。"),
        // 菜单与对话框（两个入口共用同一份动作表，批次 VO）。
        new Entry("alarm.preset.once", "单次（叫一次就关）", "Once (turns itself off)", "1 回だけ（終了後オフ）"),
        new Entry("alarm.toggle.on", "开着", "On", "オン"),
        new Entry("alarm.repeat.title", "重复：{0}", "Repeat: {0}", "繰り返し：{0}"),
        new Entry("alarm.repeat.byDay", "按星期勾", "Pick days", "曜日を選ぶ"),
        new Entry("alarm.edit", "改时间或名字…", "Change time or name…", "時刻・名前を変更…"),
        new Entry("alarm.testFire", "试响一次（不动到点记录）", "Ring once now (schedule untouched)", "今 1 回鳴らす（予定は不変）"),
        new Entry("alarm.remove", "删掉这一条", "Delete this alarm", "このアラームを削除"),
        new Entry("alarm.add.title", "加一条闹钟", "Add an alarm", "アラームを追加"),
        new Entry("alarm.add.message", "写钟点，后面可以跟名字。例如「7:30 起床」。", "Write a time; a name may follow, e.g. “7:30 Wake up”.", "時刻を書き、後に名前（例：7:30 朝の支度）。"),
        new Entry("alarm.add.placeholder", "7:30 起床", "7:30 Wake up", "7:30 朝の支度"),
        new Entry("alarm.add.failed", "没加上", "Not added", "追加できませんでした"),
        new Entry("alarm.edit.title", "改这条闹钟", "Edit this alarm", "このアラームを編集"),
        new Entry("alarm.edit.message", "写钟点，后面可以跟名字。留空或取消都不改动。", "Write a time; a name may follow. Blank or Cancel changes nothing.", "時刻を入力。空欄かキャンセルで変更しません。"),
        new Entry("alarm.edit.kept", "没改", "Unchanged", "変更しませんでした"),
        // 组件右键那一节
        new Entry("alarm.section", "闹钟", "Alarms", "アラーム"),
        new Entry("alarm.section.pending", "闹钟 · {0} 条待确认", "Alarms · {0} awaiting confirmation", "アラーム・{0} 件確認待ち"),
        new Entry("alarm.status.pending", "到点了：按下面「停止待确认」就收（本程序运行时才提醒）", "It's due: press “Stop pending” below (this program only rings while it runs)", "時刻です：下の「確認停止」で消えます（本アプリ起動中のみ）"),
        new Entry("alarm.status.due", "下一次大约 {0}（本程序运行时才提醒）", "Next around {0} (this program only rings while it runs)", "次回 約 {0}（本アプリ起動中のみ）"),
        new Entry("alarm.status.empty", "还没有闹钟：加一条，例如「7:30 起床」（本程序运行时才提醒）", "No alarms yet: add one, e.g. “7:30 Wake up” (this program only rings while it runs)", "アラーム未設定：追加を（例：7:30 起床）（本アプリ起動中のみ）"),
        new Entry("alarm.stop.label", "停止待确认", "Stop pending", "確認停止"),
        new Entry("alarm.stop.button", "{0}（{1}）", "{0} ({1})", "{0}（{1}）"),
        new Entry("alarm.add.item", "加一条…", "Add…", "追加…"),
        new Entry("alarm.add.full", "加一条（最多 {0} 条，先删一条）", "Add (limit {0}; delete one first)", "追加（最大 {0} 件、先に 1 件削除）"),
        // 时钟表面那一排（常驻元素 ⇒ 走 LocText 重贴，⛔ 只在建行那一刻取词）
        new Entry("alarm.face.addFirst", "＋ 加一条闹钟", "＋ Add an alarm", "＋ アラーム追加"),
        new Entry("alarm.face.add", "＋ 闹钟", "＋ Alarm", "＋ アラーム"),
        new Entry("alarm.face.max", "闹钟最多 {0} 条", "Alarms limit {0}", "アラームは最大 {0} 件"),
        new Entry("alarm.face.maxTip", "先删一条再加", "Delete one before adding", "1 件削除してから追加"),
        new Entry("alarm.face.addTip", "加一条，例如「7:30 起床」（本程序运行时才提醒）", "Add one, e.g. “7:30 Wake up” (this program only rings while it runs)", "追加（例：7:30 起床）（本アプリ起動中のみ）"),
        new Entry("alarm.face.stopTip", "确认掉所有还在等的轮次（本程序运行时才提醒）", "Confirm every round still waiting (this program only rings while it runs)", "保留中の全回を確認（本アプリ起動中のみ）"),
        new Entry("alarm.face.onTip", "开着：点一下关掉这一条", "On: click to switch this one off", "オン：クリックでこの 1 件をオフ"),
        new Entry("alarm.face.offTip", "关着：点一下开回来", "Off: click to switch it back on", "オフ：クリックでこの 1 件をオン"),
        new Entry("alarm.face.pendingSuffix", "{0} · 待确认", "{0} · awaiting confirmation", "{0}・確認待ち"),
        new Entry("alarm.face.rowTip", "改时间、重复、试响或删掉", "Change time, repeat, ring or delete", "時刻・繰り返し・試鳴ら・削除"),
        new Entry("alarm.notify.body", "到点了。点时钟上那一排的「{0}」就收，右键「{1}」里也能改。", "It's due. Press “{0}” on the clock face, or edit it under “{1}” in the right-click menu.", "時刻です。時計面の「{0}」で消せます。右クリックの「{1}」でも変更可。"),
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
