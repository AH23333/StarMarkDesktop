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
/// <para><b>这一版只有中文与英文两列</b>（用户裁的"先只做中＋英，日文随后"）：第三列在没有整列译文之前不加——
/// 加一列空的，就等于让语言选择器出现一颗选了之后仍然满屏中文的选项。
/// 要进新语言，先补齐整列，再动 <see cref="AppLanguages.Pickable"/>。</para>
/// <para>占位符只用位置式 <c>{0}</c>／<c>{1}</c>，各语言的占位符<b>集合必须相同</b>——
/// 这一条由 <c>I18nGateTests</c> 逐行比对钉住：换语序就得换编号，那正是"数字挂错句子"的来源（#234 那一族）。</para>
/// </summary>
public static class UiStrings
{
    /// <summary>一行＝一颗键在各语言里的写法。两列都是<b>必填</b>：编译器不允许空着，
    /// 于是"漏了一门的翻译"从一次线上发现变成一个编不过的错。</summary>
    public sealed record Entry(string Key, string Zh, string En);

    /// <summary>表。<b>顺序无意义</b>（不参与渲染），但同一分组的键写在一起：改动时一眼看得见漏了哪一门。</summary>
    public static IReadOnlyList<Entry> All { get; } = new[]
    {
        // ===== 顶栏那排页签（导航）=====
        new Entry("nav.tree", "文件夹", "Folders"),
        new Entry("nav.tags", "标签", "Tags"),
        new Entry("nav.activity", "动态", "Activity"),
        new Entry("nav.hidden", "隐藏", "Hidden"),
        new Entry("nav.snapshot", "快照", "Snapshots"),
        new Entry("nav.clipboard", "剪贴板", "Clipboard"),
        new Entry("nav.trending", "热榜", "Trending"),
        new Entry("nav.rss", "RSS", "RSS"),

        // ===== 顶栏按钮与提示 =====
        new Entry("top.sync", "同步", "Sync"),
        new Entry("top.syncCancel", "取消同步", "Cancel sync"),
        new Entry("top.widgetsTip", "桌面组件", "Desktop widgets"),
        new Entry("top.guideTip", "这一页怎么用", "How this page works"),
        new Entry("top.showHidden", "显示隐藏", "Show hidden"),

        // ===== 搜索框与工具栏 =====
        new Entry("search.placeholder",
            "搜索本地文件 / Star / 书签… 支持 ext: size: dm: folder:  (↑↓ 选择 · Enter 打开 · Ctrl+Enter 位置)",
            "Search local files / Stars / bookmarks… supports ext: size: dm: folder:  (↑↓ select · Enter open · Ctrl+Enter location)"),
        new Entry("sort.recent", "最近", "Recent"),
        new Entry("sort.starred", "Star 最近推送", "Star · latest push"),
        // 这一句是「档名说的排序键」与「数据里真正的排序键」之间唯一的现场解释（P-6），
        // 所以两列都必须说出 push：只翻档名、把这句留在中文，英文界面就只剩一个看不懂的词。
        new Entry("sort.starredTip", "按仓库最后一次 push 排（不是“我点 Star 的时间”——那个值 API 默认不回传）",
            "Sorted by the repo's last push — not when you starred it (the API doesn't return that by default)"),
        new Entry("sort.collected", "最近收藏", "Recently collected"),
        new Entry("sort.stars", "Stars 数", "Star count"),
        new Entry("sort.name", "名称", "Name"),
        new Entry("source.all", "全部", "All"),
        new Entry("source.allTip", "全部来源", "All sources"),
        new Entry("source.starTip", "Star", "Star"),
        new Entry("source.bookmarkTip", "收藏夹", "Bookmarks"),
        new Entry("source.fileTip", "本地文件（需先在设置开启本地磁盘搜索）", "Local files (turn on local disk search in Settings first)"),

        // ===== 「类型筛选」浮层 =====
        new Entry("kind.button", "类型筛选", "Type filter"),
        new Entry("kind.buttonTip", "按文件类型筛选本地文件；不输关键词也能浏览该类型的全部文件",
            "Filter local files by type; browse every file of a type without typing a keyword"),
        new Entry("kind.fileTypes", "文件类型", "File type"),
        new Entry("kind.folder", "文件夹", "Folder"),
        new Entry("kind.document", "文档", "Document"),
        new Entry("kind.picture", "图片", "Picture"),
        new Entry("kind.music", "音乐", "Music"),
        new Entry("kind.video", "视频", "Video"),
        new Entry("kind.archive", "压缩包", "Archive"),
        new Entry("kind.conditions", "条件", "Conditions"),
        // 表里的文本是<b>最终显示的字</b>（不过 XAML 解析器）⇒ 尖括号写原文，不写 `&gt;`——
        // 从表里取出的 `&gt;` 会照原样显示成四个字符。
        new Entry("kind.large", "大文件（>100 MB）", "Large files (>100 MB)"),
        new Entry("kind.recent", "最近改动（7 天内）", "Modified in the last 7 days"),
        new Entry("kind.noteScope", "仅作用于本地文件；「文件夹」与类型互斥，勾选后只列文件夹。",
            "Applies to local files only; “Folder” is exclusive with the types — tick it to list folders only."),
        new Entry("kind.noteSyntax", "也可直接在搜索框键入语法：ext:、size:、dm:、folder:",
            "You can also type the syntax straight into the search box: ext:, size:, dm:, folder:"),
        new Entry("kind.clear", "清除类型筛选", "Clear type filter"),

        // 这里的"语言"是 Star 的编程语言，不是界面语言——英文必须说出"仓库语言"，
        // 否则同一屏上两颗都叫 Language 的下拉，第二颗看起来像坏了。
        new Entry("searchLang.placeholder", "语言：全部", "Repo language: all"),
        new Entry("searchLang.tip", "按 Star 的编程语言筛选（对本地文件无效）",
            "Filter Stars by programming language (has no effect on local files)"),

        // ===== 首启那份引导的两个按键（批次 VC-3）=====
        new Entry("guide.primary", "看完整使用说明", "Read the full guide"),
        new Entry("guide.secondary", "知道了", "Got it"),
        // 首启向导的三步车（批次 WJ-1）。计数那句两列必须给同一组编号 {0}{1}——
        // 少给一个号，英文那版会当场抛 FormatException（Locale.Fmt 刻意不 catch，就是要它在这里红）。
        new Entry("guide.next", "下一步", "Next"),
        new Entry("guide.prev", "上一步", "Back"),
        new Entry("guide.jump", "带我去这一页", "Take me there"),
        new Entry("guide.step", "第 {0} / {1} 步", "Step {0} of {1}"),

        // ===== 设置页那八栏的标题（批次 WI-2 第一批）=====
        // 键的后缀＝页签身份（`SettingsTabIds`），不是屏幕上那几个字：跳转按身份走，标题跟着语言走。
        // 顺序即页签从左到右的顺序，闸门拿这张表与 XAML 的 Tag 两侧对账。
        new Entry("settings.tab.general", "常规", "General"),
        new Entry("settings.tab.guide", "使用说明", "Guide"),
        new Entry("settings.tab.ai", "AI 增强", "AI"),
        new Entry("settings.tab.widgets", "桌面工具（组件）", "Desktop tools"),
        new Entry("settings.tab.extend", "拓展功能", "Extensions"),
        new Entry("settings.tab.hotkeys", "快捷键", "Hotkeys"),
        new Entry("settings.tab.data", "数据", "Data"),
        new Entry("settings.tab.health", "健康与诊断", "Health & diagnostics"),

        // ===== 设置页自己的页眉（标题 + 右上角那颗返回）=====
        // 它们与八栏标题同一层级（页面外壳，不是某一栏的正文），所以同一批改掉；
        // 覆盘照片拍到「设置／返回」孤零零留在中文、而承诺句没点它，就是这一格要补的原因。
        new Entry("settings.pageTitle", "设置", "Settings"),
        new Entry("settings.back", "返回", "Back"),

        // ===== 设置页「常规」里那颗界面语言 =====
        new Entry("settings.languageTitle", "界面语言", "Interface language"),
        new Entry("settings.languageFollowSystem", "跟随系统", "Follow system"),
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
        new Entry("settings.languageNote",
            "改完立刻生效，不用重启。这一版换掉了主窗口顶部那一排（页签、搜索框、排序与筛选按钮）、"
            + "设置页的页眉与那八栏的标题，和「常规」「使用说明」「快捷键」「数据」「健康与诊断」「拓展功能」这六栏里由界面标记写出来的那排字。"
            + "还没铺开的是其余两栏（AI 增强／桌面工具）的正文、页面正文里的字"
            + "（含「暂无条目，请先同步数据」这类空状态）、工具栏那颗「语言：全部」下拉、软件内说明和日志，按批往前推。"
            + "同一栏里由程序拼出来或算出来的字也不在这批范围里：顶栏那行索引状态、「关于与更新」里那行检查结果、"
            + "快捷键这一栏的分类名与每一行的动作名和键位、健康页那两排下拉的选项、那一页「诊断」的整列标签、"
            + "数据这一栏那几行状态读数、备份列表那行标题与每行那颗按钮的字、那颗备份间隔下拉的选项、"
            + "拓展功能这一栏那五行状态读数（热榜／RSS／本地磁盘搜索／截屏／画布）、那行引擎占用大小的「约」字"
            + "与画布快捷键一览每行的动作名，"
            + "以及「语言 Top：」这类把句子拆成半句＋读数的排法。"
            + "另有两处不彻底：下拉框展开的列表当场换语言，但闭着的那一格要等你重新选一次才跟着换；"
            + "而上面那些程序写的字要翻，得先把它们改成按键取词，那是后面一批的事。",
            "Takes effect at once — no restart needed. Translated so far: the main window's top row "
            + "(nav tabs, the search box, the sort and filter buttons), the Settings page header, its eight "
            + "section titles, and everything the General, Guide, Hotkeys, Data, Health and Extensions screens write themselves. "
            + "Still Chinese for now: the bodies of the other two sections (AI, Desktop tools), "
            + "the page bodies (including empty-state lines), the language filter dropdown on the toolbar, "
            + "the in-app guide and the log — more sections land with each following pass. Words the program "
            + "assembles or computes are out of this batch's reach too: the index-status line on the top bar, "
            + "the last check result under About & updates, each hotkey category and every row's action name and "
            + "key, the two option lists on the Health page, the whole label column under Diagnostics there, "
            + "the status lines on the Data page, that backup list's own header and every row's button, "
            + "the options in the backup-interval dropdown, the five status lines on the Extensions page, the word "
            + "in front of the engine's size and every canvas shortcut's action name there, and the lines built as half a sentence plus a number. "
            + "Two things aren't complete: in a "
            + "dropdown the opened list switches at once while the closed box keeps its old label until you pick "
            + "again; and those program-written strings have to be reworked to read from the table before they can "
            + "switch — that's a later pass."),

        // ===== 「常规」那一栏的正文（批次 WI-2d，P-169 ① 第二刀）=====
        // 中文列<b>逐字取自改键前 XAML 里的那一句</b>：这一刀只搬地方，不顺手改措辞——
        // 改了字就没有任何东西能证明"屏幕上还是原来那句话"，而这一屏是用户每天看的。
        new Entry("settings.general.themeTitle", "主题", "Theme"),
        new Entry("settings.general.themeNote", "跟随系统 / 浅色 / 深色", "Follow system / Light / Dark"),
        // 「跟随系统」这一项与上面那颗语言下拉共用 settings.languageFollowSystem：
        // 两处写的是同一个决定，分成两行迟早只改一处。
        new Entry("settings.general.themeLight", "浅色", "Light"),
        new Entry("settings.general.themeDark", "深色", "Dark"),

        new Entry("settings.general.appearanceTitle", "外观 · 主窗口材质与不透明度",
            "Appearance · main window material and opacity"),
        new Entry("settings.general.appearanceNote",
            "给主窗口选毛玻璃材质（照搬 DeskBox 的 Desktop Acrylic / Mica 控制器）。桌面组件的材质与不透明度在「桌面工具（组件）」页，两者互不影响。",
            "Choose the frosted material for the main window (the Desktop Acrylic / Mica controllers, taken from DeskBox). Widget material and opacity are set on the “Desktop tools” page; the two are independent."),
        new Entry("settings.general.mainBackdrop", "主窗口材质", "Main window material"),
        // 英文列里 "Opaque" 必须与下面 material.opaque 那句的开头一致：这句在指着那颗选项说话。
        new Entry("settings.general.mainBackdropNote",
            "桌面组件的材质在「桌面工具（组件）」页单独设置，两边互不影响；选「实色」即不透明。",
            "Widget material is set separately on the “Desktop tools” page; the two are independent. Pick “Opaque” for a fully solid surface."),
        // 六颗材质的顺序＝枚举值，XAML 那行注释已经写明；这里换序不会报错，只会让设置档里的数字指向另一种材质。
        new Entry("settings.general.material.acrylicThin", "亚克力 · 薄（推荐，苹果风格）",
            "Acrylic · thin (recommended, Apple-style)"),
        new Entry("settings.general.material.mica", "云母 · Mica（更克制的桌面色调）",
            "Mica (a more restrained desktop tint)"),
        new Entry("settings.general.material.opaque", "实色（省资源，不使用材质）",
            "Opaque (no material, saves resources)"),
        new Entry("settings.general.material.micaAlt", "云母 · Alt（取更多桌面壁纸色调）",
            "Mica Alt (picks up more of the wallpaper)"),
        new Entry("settings.general.material.acrylicThick", "亚克力 · 厚（更浓的霜化，隐私更好）",
            "Acrylic · thick (heavier frost, better privacy)"),
        new Entry("settings.general.material.solid", "纯色 · Solid（带强调色，可调不透明度）",
            "Solid (accent-tinted, opacity adjustable)"),

        new Entry("settings.general.mainOpacity", "主窗口背景不透明度（仅纯色材质）",
            "Main window opacity (Solid material only)"),
        new Entry("settings.general.mainOpacityNote",
            "只影响主窗口的「纯色 · Solid」材质，与组件互不影响；其余材质用各自固定的透明度性格。",
            "Affects only the main window's Solid material, and nothing on the widgets; every other material keeps its own fixed opacity."),

        new Entry("settings.general.trayTitle", "托盘与呼出", "Tray and summoning"),
        new Entry("settings.general.autostart", "开机自动启动 StarMark", "Start StarMark at sign-in"),
        new Entry("settings.general.autostartNote", "登录 Windows 后自动常驻，不必每次手动打开。",
            "Already running once you're signed in — no need to open it each time."),
        new Entry("settings.general.trayResident", "常驻系统托盘", "Stay in the system tray"),
        new Entry("settings.general.trayResidentNote", "关闭时隐藏到托盘，进程常驻。",
            "Hiding it sends it to the tray and the process stays up."),
        new Entry("settings.general.globalHotkeys", "启用全局快捷键", "Global hotkeys"),
        new Entry("settings.general.globalHotkeysNote", "总开关；关闭时所有快捷键均不生效。",
            "Master switch; with it off no hotkey fires."),
        new Entry("settings.general.minimizeToTray", "关闭窗口时最小化到托盘", "Minimize to tray on close"),
        new Entry("settings.general.minimizeToTrayNote", "关闭主窗口不退出程序。",
            "Closing the main window doesn't quit the program."),

        new Entry("settings.general.githubTitle", "GitHub Stars 同步", "GitHub Stars sync"),
        new Entry("settings.general.githubToken", "Personal Access Token（可选，仅需 public_repo）",
            "Personal Access Token (optional, public_repo only)"),
        new Entry("settings.general.githubUser", "用户名（可选，用于显示）", "Username (optional, for display)"),

        new Entry("settings.general.aboutTitle", "关于与更新", "About & updates"),
        new Entry("settings.general.currentVersion", "当前版本", "Current version"),
        new Entry("settings.general.currentVersionNote",
            "这份产物是哪一个版本；构建时间在「健康与诊断」页的「诊断」里。",
            "Which version you're running; the build time is under “Diagnostics” on the Health & diagnostics page."),
        new Entry("settings.general.autoCheck", "每天自动检查一次", "Check once a day"),
        // 这一句是"自动那一发"与"点下去那一发"的边界，两列都必须把两件事分开说：
        // 只有 UpdateApplyWiringGateTests 那两格钉着它不许变回全局承诺（"本程序不做下载安装"）。
        new Entry("settings.general.autoCheckNote",
            "自动那一发只问「这个仓库发布了哪一版」：不下载、不改动本机文件，也不替换正在运行的程序。关掉这个开关后，只有点「立即检查」那一下才会联网。装新版是另一件事——只有点「立即更新」才真的下载并替换（换不成会自动退回原来那一版）。",
            "The automatic pass only asks “which version has this repo published”: it downloads nothing, touches no file on this machine and never replaces the running program. Switch this off and the only request is the one you make by pressing “Check now”. Installing is a separate act — only “Update now” downloads and swaps files (and it falls back to the old version by itself if the swap fails)."),
        new Entry("settings.general.checkNow", "立即检查", "Check now"),
        // 那颗按钮在跑的那一段换的是<b>键</b>不是字（见 SettingsPage.Updates.cs 卷首）：
        // 写死的字面量会在换语言那一刻被重贴成"空闲"那句，等于界面替一件还在跑的事改口。
        new Entry("settings.general.checking", "正在问…", "Asking…"),
        new Entry("settings.general.updateNow", "立即更新", "Update now"),
        new Entry("settings.general.cancelUpdate", "取消更新", "Cancel update"),
        new Entry("settings.general.openDownloads", "打开下载页", "Open the download page"),
        new Entry("settings.general.releaseNotes", "这一版的更新说明", "What's in this release"),

        // ===== 「使用说明」那一栏的这两句（批次 WI-2e，P-169 ① 第三刀）=====
        // 这一栏<b>不放说明正文</b>（正文只在 <c>PageGuides</c> 一处，批次 VC-3 定死的）：
        // 这两句说的都是"那颗按钮是干什么的"，所以它们是宿主的文字，不是第二份指南。
        new Entry("settings.guide.replay", "重看首启引导", "Watch the first-run guide again"),
        new Entry("settings.guide.replayNote",
            "那份引导只弹一次；跳过或关掉之后，从这里回来。",
            "It only appears once — if you skip it or close it, come back here."),

        // ===== 「快捷键」那一栏的说明与按键 =====
        // ⚠ 这一族的句子里<b>不许出现具体键位</b>（"Ctrl + Alt + Space"那种）：用户改过键之后它就成假话，
        // 而键位只有一个出处＝每一行自己显示的那份绑定（批次 VC 的裁决）。
        new Entry("settings.hotkeys.title", "全局快捷键", "Global hotkeys"),
        new Entry("settings.hotkeys.recordNote",
            "点击右侧按钮开始录制：按钮文字会清空并实时显示你按下的按键，最多 3 个键；录满 3 个键自动结束，只录了 1~2 个键时按 Esc 结束录入（一个键都没录时按 Esc 取消）。",
            "Press the button on the right to start recording: its label clears and shows the keys you press, up to 3. Recording ends by itself at 3 keys; after 1–2 keys, Esc finishes it (Esc with nothing recorded yet cancels)."),
        new Entry("settings.hotkeys.saveNote",
            "录制结束不会立即生效——必须点击「保存快捷键」后才注册生效（避免刚录完「隐藏主界面」就把窗口藏了）。Backspace / Delete 可清除该动作。",
            "Finishing a recording registers nothing — press “Save hotkeys” to make it live (otherwise re-recording “Hide main window” would hide the window on the spot). Backspace or Delete clears that action."),
        new Entry("settings.hotkeys.conflictNote",
            "同一组合可绑定多个动作——触发时会依次执行（例如一次同时呼出主界面并显示所有组件），这是允许的「冲突」，只会在动作右侧标记，你可以直接忽略。",
            "One combination can drive several actions — pressing it runs them in order (summoning the main window and showing every widget at once, say). Such “conflicts” are allowed, flagged to the right of the action, and safe to ignore."),
        new Entry("settings.hotkeys.save", "保存快捷键", "Save hotkeys"),
        new Entry("settings.hotkeys.discard", "撤销更改", "Discard changes"),
        new Entry("settings.hotkeys.reset", "恢复默认快捷键", "Restore factory hotkeys"),
        new Entry("settings.hotkeys.unsaved",
            "⚠ 有未保存的快捷键改动，点击「保存快捷键」后才会生效。",
            "⚠ Some hotkey changes aren't saved yet — they only register once you press “Save hotkeys”."),
        new Entry("settings.hotkeys.retry", "重试注册", "Try registering again"),
        new Entry("settings.hotkeys.mapTitle", "动作 → 快捷键", "Action → hotkey"),
        new Entry("settings.hotkeys.mapNote",
            "按分类折叠。展开某类后逐行点击右侧按钮录制；同一组合可绑定多个动作（允许的「冲突」只就地标注）。",
            "Grouped by category, all of them collapsed. Open a category and press the button on any row to record it; one combination can drive several actions (allowed “conflicts” are flagged in place)."),
        // 这一句换掉了 code-behind 里写死的那句"只保留默认的「Ctrl + Alt + Space（切换主界面）」"：
        // 那句既抄了一处键位（改过键就是假话），又把事实说错了——恢复默认还原的是<b>每一行</b>，
        // 出厂默认里有截图／贴图／穿透／画布那一整批，不是只留主界面那一颗。
        new Entry("settings.hotkeys.resetConfirm",
            "会把下面每一行的键位都还原成出厂默认——不止你改过的那几颗。这一步不能撤销。",
            "This resets every row below to its factory key, not just the ones you changed. It can't be undone."),

        // ===== 「数据」那一栏的正文（批次 WI-2f，P-169 ① 第四刀）=====
        // ⚠ 这一族里有一句带 Windows 路径，路径里的 <c>\S</c> 与 <c>\b</c> 在普通字符串里是转义起点
        // （<c>"\b"</c> 是一个退格符，屏幕上看不见、日志里也看不见，只有"备份目录那行怪怪的"这种报告）。
        // 所以那一行<b>必须写成 verbatim 字符串</b>，而 <c>NoRowCarriesAnInvisibleControlCharacter</c>
        // 给这件事配了证人——它读的是编进 dll 的实际字符，不是源码长相。
        new Entry("settings.data.backupTitle", "数据备份与恢复", "Backup & restore"),
        new Entry("settings.data.backupNote",
            "条目本体（书签/Star/文件）可重新同步，但手写笔记、标签体系、隐藏/置顶状态、桌面组件待办与随记全部不可重建。",
            "The items themselves (bookmarks / Stars / files) can be re-synced; handwritten notes, the tag system, hidden and pinned states, and the widgets' todos and quick notes cannot be rebuilt."),
        new Entry("settings.data.autoBackupNote",
            @"应用会自己备份：按下面设的间隔存一份到 %LOCALAPPDATA%\StarMark\backups。自动清理只动 auto- 开头的自动件，你手动导出的备份与恢复前快照都不在清理候选里。",
            "The app backs itself up: one copy per interval you set below, into %LOCALAPPDATA%\\StarMark\\backups. Automatic cleanup only touches auto- files — your manual exports and pre-restore snapshots are never candidates."),
        new Entry("settings.data.autoBackup", "自动备份", "Automatic backups"),
        new Entry("settings.data.autoBackupOffNote",
            "关掉之后程序不再自动落任何份，能回滚的只剩你手动导出的那些（导入前的「恢复前快照」仍然每次都留）。关掉不会删掉已经落盘的自动件。",
            "Switch this off and the app stops writing any automatic copy — the only things left to roll back to are the ones you exported by hand (the “pre-restore snapshot” taken before each import still happens). Turning it off deletes nothing already on disk."),
        new Entry("settings.data.autoBackupInterval", "自动备份间隔", "Backup interval"),
        new Entry("settings.data.autoBackupIntervalTip",
            "两份自动件之间的最小间隔。应用开着时每 10 分钟问一次「到点没」，所以实际落盘最多比到点晚十分钟。保留份数是固定的，间隔越短能回溯的天数就越少——这笔账由下面那行按你选的档算给你看。",
            "The minimum gap between two automatic copies. While the app is open it asks “is it time?” every 10 minutes, so a copy can land up to ten minutes late. How many copies are kept is fixed — the shorter the interval, the fewer days back you can reach; the line below works that out for the interval you picked."),
        new Entry("settings.data.restoreSnapshotNote",
            "导入会先自动生成「恢复前快照」（同上目录），任何恢复都可回滚：它就是下面列表里标着「恢复前快照」的那一行，点「用它回滚」即撤销这次导入。",
            "Before an import, the first thing that happens is a “pre-restore snapshot” in the same folder — which is what makes every restore reversible. It's the row labelled “pre-restore snapshot” below; “Roll back with this” undoes the import."),
        new Entry("settings.data.elevatedNote",
            "当前以管理员身份运行（由你启动时的选择决定，程序不会再自己提权）：系统文件对话框在这种会话里不可用，导出/导入会自动改用应用内的路径输入框，无需换权限或重启。",
            "Running elevated because you launched it that way (the app never elevates itself): the system file dialog isn't usable in such a session, so export and import fall back to the in-app path field — no permission changes, no restarts."),
        new Entry("settings.data.includeImages", "导出时带上剪贴板图片本体", "Include clipboard image files when exporting"),
        new Entry("settings.data.includeImagesNote",
            "带上就存成 .zip（清单 + 图片），只有一份文件、不会拷走一半。关掉则只带条目与文件名：恢复后这些图片条目要靠本机图片目录里还留着的文件才打得开，缺的会标成「文件缺失」。一张 4K 截图常有几百 KB，上限调到几百条时导出包会明显变大。",
            "With images you get a single .zip (manifest + images), so nothing is half-copied. Without them the backup carries entries and file names only: after a restore an image opens only if its file is still in this machine's image folder, and missing ones are marked “file missing”. A 4K screenshot is often a few hundred KB, so raising the cap to hundreds of entries makes the export noticeably bigger."),
        new Entry("settings.data.includeImagesOn", "带", "Include"),
        new Entry("settings.data.includeImagesOff", "不带", "Skip"),
        new Entry("settings.data.includeImagesTip",
            "只影响手动「导出备份」。恢复前快照与自动件始终不带图片本体——它们是回滚点，一份几十 MB 的库配几百 MB 的图会把「随时能撤一步」变成「每次恢复先填一盘磁盘」。恢复时会把包里的图片写回本机图片目录，已有同名文件不覆盖。",
            "Affects manual exports only. Pre-restore snapshots and automatic copies never carry image files — they are rollback points, and pairing a tens-of-MB database with hundreds of MB of images would turn “step back any time” into “fill a disk before every restore”. A restore writes bundled images back into this machine's image folder and never overwrites an existing file of the same name."),
        new Entry("settings.data.export", "导出备份…", "Export backup…"),
        new Entry("settings.data.import", "导入备份…", "Import backup…"),
        new Entry("settings.data.openFolder", "打开备份目录", "Open the backup folder"),
        new Entry("settings.data.deleteTip",
            "只删这一份导出文件，数据库本身不动；点下去会先确认一次，并说明这一份删掉之后有什么后果。",
            "Deletes this one exported file and nothing in the database. It asks for confirmation first and says what you lose by deleting it."),
        new Entry("settings.data.noBackups",
            "还没有任何备份文件（自动备份按上面设的间隔落盘，也可以点上面「导出备份」立刻来一份）。",
            "No backup files yet (automatic ones land on the interval set above, or press “Export backup…” for one right now)."),
        new Entry("settings.data.clipboardTitle", "剪贴板历史", "Clipboard history"),
        // 这句以前写死"最近 500 条"：批次 ClipIMG-1d 起上限归用户调（10–10000），
        // 卡片上再留一个数就是第二份事实，用户调高之后它当场变成假话（同族：坑表 #279）。
        new Entry("settings.data.clipboardNote",
            "开启后自动记录本机复制过的文本与文件列表（留多少条由下面「文本最多留多少条」那一格决定），在主窗「剪贴板」页点一条即可再复制回去。密码管理器复制的内容、以及私钥 / 登录令牌 / 银行卡号形态一律不入库。",
            "When on, text and file lists you copy on this machine are recorded (how many are kept is the “text entries” box below), and clicking one on the main window's Clipboard page copies it back. Anything copied from a password manager, plus private-key / auth-token / card-number shapes, never enters the store."),
        new Entry("settings.data.clipboardImageNote",
            "同时记录复制到的图片（屏幕截图、图片文件、以及在图上右键「复制图片」）。图片无法做敏感检查，开启即包含一切屏幕内容。",
            "Also records images you copy (screen captures, image files, and right-click “Copy image” on one). Images can't be scanned for sensitive content, so switching this on means everything that ever appears on screen."),
        new Entry("settings.data.imageMax", "图片最多留多少条", "How many images to keep"),
        new Entry("settings.data.imageMaxNote",
            "超出后按最近复制的时间淘汰，置顶的那几条不会被挤掉。一张 4K 截图的 PNG 常有几百 KB，调高会明显多占磁盘。",
            "Past the cap the oldest copies are dropped; pinned entries are never squeezed out. A 4K screenshot PNG is often a few hundred KB, so raising this costs disk space."),
        new Entry("settings.data.textMax", "文本最多留多少条", "How many text entries to keep"),
        new Entry("settings.data.textMaxNote",
            "默认 500 条，就是以前写死的那个数字；正文过长的只存前 32 KB。文本与图片各自一条线，互不挤占。",
            "500 by default — the number that used to be hardcoded; overly long bodies keep their first 32 KB. Text and images have separate caps and never evict each other."),
        new Entry("settings.data.recount", "重新统计占用", "Re-measure usage"),
        new Entry("settings.data.cleanOrphans", "清理孤儿与临时件", "Clean up orphans and partial files"),
        new Entry("settings.data.cleanOrphansTip",
            "删掉「不在历史里的那些文件」与没写完的临时件。点下去会先列名单让你确认，一条历史条目都不会动。",
            "Deletes the files “not in the history any more” plus half-written temporary ones. It lists them for confirmation first, and no history entry is touched."),

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
        new Entry("settings.extend.trendingTitle", "GitHub 热榜", "GitHub trending"),
        new Entry("settings.extend.trendingNote",
            "开启后导航栏「剪贴板」右侧出现「热榜」。抓取只在你打开那一页时发生（每个周期每天至多一次，失败时回读上次结果并说明）；条目默认不进库——点 ⭐ 才是给 GitHub 远端加星（需 Token），点 🔖 才存进本机收藏。",
            "Turn this on and “Trending” appears to the right of “Clipboard” in the nav rail. Nothing is fetched until you open that page (at most once a day per period; if a fetch fails you get the last result back and a reason). Entries stay out of the library by default — ⭐ stars the repository on GitHub itself (needs a token), 🔖 is what saves a copy locally."),
        new Entry("settings.extend.glanceNote",
            "在速览那个组件里显示热榜块（默认日榜）；关掉则该块消失、下面那块常看回到顶部。",
            "Shows a trending block in the glance widget (daily list by default); switch it off and it disappears, letting the usual block underneath move back to the top."),
        new Entry("settings.extend.glanceOn", "显示", "Show"),
        new Entry("settings.extend.glanceOff", "不显示", "Hide"),
        new Entry("settings.extend.trendingPeriodNote",
            "热榜页上选的周期会记住，下次打开还是那一档。没配 Token 也能浏览热榜（读侧不依赖 Token），只是 Star 按钮会提示要配 Token。",
            "The period you pick on the trending page is remembered and is still it next time. Browsing needs no token — only the Star button asks for one."),
        new Entry("settings.extend.openTrending", "打开热榜页", "Open the trending page"),
        new Entry("settings.extend.rssTitle", "网址来源（RSS / Atom）", "Feed sources (RSS / Atom)"),
        new Entry("settings.extend.rssNote",
            "开启后导航栏出现「RSS」：每个订阅源是它自己的一个文件夹，点条目直接在浏览器打开文章，只有点收藏那个动作才会进本机库（并放进这个源自己的那一层）。抓取只在你到那一页主动刷新时发生，没有后台定时。",
            "Turn this on and “RSS” appears in the nav rail: every feed is a folder of its own, clicking an entry opens the article in your browser, and only the bookmark action copies it into the local library (into that feed's own folder). Feeds are fetched when you go to that page and refresh — nothing runs in the background."),
        new Entry("settings.extend.rssEmpty", "还没有添加任何来源地址。", "No feed addresses added yet."),
        new Entry("settings.extend.rssOn", "启用", "Enable"),
        new Entry("settings.extend.rssOff", "停用", "Disable"),
        new Entry("settings.extend.rssUrlPlaceholder", "订阅地址（https://…/feed）", "Feed address (https://…/feed)"),
        new Entry("settings.extend.rssNamePlaceholder", "名字（留空＝用域名）", "Name (blank = use the domain)"),
        new Entry("settings.extend.add", "添加", "Add"),
        new Entry("settings.extend.openRss", "打开 RSS 页", "Open the RSS page"),
        new Entry("settings.extend.diskSearchTitle", "本地磁盘搜索", "Local disk search"),
        new Entry("settings.extend.diskSearchNote",
            "开启后可像 Everything 一样全盘秒搜本地文件，结果并入快捷搜索 / 主窗搜索。首次开启会自动准备 Everything（可能需要一次 UAC）。",
            "Once on, local files are searched across whole volumes as fast as Everything does, and the hits merge into the quick-search and main-window results. The first time you turn it on the app prepares Everything for you (which may take one UAC prompt)."),
        new Entry("settings.extend.engineUsage", "本地搜索引擎占用", "Search engine footprint"),
        new Entry("settings.extend.openEngineFolder", "打开所在目录", "Open its folder"),
        new Entry("settings.extend.engineDeleteNote",
            "删除只清理本地搜索引擎（Everything 及其 SDK），书签 / Star / 标签数据不受影响；即使误删，下次开启本地磁盘搜索会自动重新下载。",
            "Deleting clears only the search engine (Everything and its SDK) — bookmarks, Stars and tags are untouched; and if you delete it by mistake, the next time you turn local disk search on it downloads again."),
        new Entry("settings.extend.cleanMissing", "清掉这些行", "Clear those rows"),
        new Entry("settings.extend.rescan", "重新扫描本机文件条目", "Rescan local file entries"),
        new Entry("settings.extend.captureTitle", "截屏（截图 / 贴图 / 识字）", "Screen capture (snip / pin / OCR)"),
        new Entry("settings.extend.captureNote",
            "关掉之后：那三条发起框选的全局热键不再向系统注册（键位交还给当前窗口），托盘里那三项截图入口一并消失。已经钉在桌面上的贴图不受影响——显示／隐藏、忽略鼠标、关闭全部照常可用。",
            "Switched off, the three global hotkeys that start a region selection are unregistered (their keys go back to whatever window has focus) and the three capture entries leave the tray menu. Pins already on the desktop are unaffected — showing, hiding, ignoring the mouse and closing all still work."),
        new Entry("settings.extend.canvasTitle", "屏幕画布", "Screen canvas"),
        new Entry("settings.extend.canvasNote",
            // 中文列原本写死"画布内的<b>九条</b>快捷键"（批次 WD-3 那一批确实是九条）。这一刀搬字时逐字读那句，
            // 数了一下 `HotkeyActions.Canvas`：今天已经是<b>十三条</b>（后来 WK/WM/P3 各加了图形、复制、贴出）。
            // 条数没有出处可接（LocText 只会整句重贴，接不上那个数＝组合句那一族），所以这里<b>不写数</b>——
            // 留着"九条"就是一句屏幕上对不上的假话，而这一栏下面那张表会自己数给人看。
            "讲解时直接在屏幕上画（不冻结画面）。进入后是穿透态：PPT 翻页、点下层应用都不受影响，选画笔那一支、或按住 Ctrl+Alt 拖动才留痕。关掉之后托盘里这一项一并消失，画布那一组快捷键也不再占用系统组合键。",
            "Draw straight on the screen while explaining, without freezing the picture. It opens pass-through: paging a PPT or clicking the app underneath keeps working, and ink only appears once you pick the pen or hold Ctrl+Alt while dragging. Switching this off also drops the tray entry and stops the canvas shortcuts from holding system combinations."),
        new Entry("settings.extend.canvasInShotNote",
            "截图带画布：开着时，截屏抓到的那一帧包含画布上的笔迹（所见即所得）。关掉后截图前会先把那块玻璃收起来再抓、抓完立刻放回——画布上画的东西一点不少，只是截走的图里没有它。画布工具条上那三条产出动作不受这条影响（那三条本来就是要把笔迹留下）。",
            "Capture the canvas: while this is on, the frame a screenshot grabs includes whatever is drawn on the canvas (what you see is what is captured). With it off, that glass is lifted just before the grab and put straight back — nothing you drew is lost, the screenshot simply does not contain it. The three output actions on the canvas toolbar are unaffected; those keep the ink by design."),
        new Entry("settings.extend.canvasInShotOn", "带上", "Include"),
        new Entry("settings.extend.canvasInShotOff", "不带", "Skip"),
        new Entry("settings.extend.glowTitle", "光标那块圆多大", "How big the cursor circle is"),
        new Entry("settings.extend.glowNote",
            "画布上跟着光标的那团光，与幕布开着时鼠标周围那块亮区，用的是同一个半径（一根滑杆管两块圆）。单位是 DIP（与缩放无关的逻辑像素）：150% 的屏上实际像素数会是它的 1.5 倍。拖动立刻生效，不用点保存、不用重启。",
            "The halo that follows the cursor on the canvas and the bright area around the mouse while the curtain is up share one radius — a single slider for both circles. The unit is DIP (logical pixels, independent of scaling), so a 150% screen shows 1.5 times as many actual pixels. Dragging takes effect immediately: no save button, no restart."),
        new Entry("settings.extend.hotkeyListTitle",
            "快捷键一览（下面是当前真正生效的键位；要改请到「快捷键」那一栏里画布那一组）",
            "Shortcut list (below are the keys actually in effect right now; to change them, use the canvas group under the Hotkeys tab)"),

        // ===== 对话框与列表上那几个共用动词 =====
        // 与 settings.general.cancelUpdate 不是一句：那颗管的是"停掉正在跑的下载／替换"，这颗只是"不做了"。
        new Entry("dialog.cancel", "取消", "Cancel"),
        new Entry("dialog.delete", "删除", "Delete"),

        // ===== 「健康与诊断」那一栏的正文（批次 WI-2e）=====
        new Entry("settings.health.eyeRestTitle", "护眼 · 休息提醒", "Eye comfort · break reminders"),
        new Entry("settings.health.eyeRestNote",
            "连续工作一段时间后提醒你休息。开着才建那张定时表，关掉后不探测前台窗口、也不会显示任何东西。",
            "Nudges you to take a break after a stretch of work. The schedule only exists while it's on — switch it off and nothing in front gets checked and nothing appears on screen."),
        new Entry("settings.health.eyeRestInterval", "间隔多久提醒一次", "How often to remind"),
        new Entry("settings.health.eyeRestIntervalNote",
            "到点后还要看当前前台是不是全屏应用，所以实际时刻会有一两分钟浮动。",
            "At the due time it also checks whether a full-screen app is in front, so the actual moment drifts by a minute or two."),
        new Entry("settings.health.eyeRestNotice", "怎么提醒", "How it reminds"),
        new Entry("settings.health.eyeRestNoticeNote",
            "改完直接按下面的「试一试」就能看见这一档的效果，不用等满间隔。",
            "Change it, then press “Try the current settings” below to see that setting straight away — no need to wait out the interval."),
        new Entry("settings.health.deferFullscreen",
            "前台是全屏应用时让路：放 PPT / 放映 / 全屏游戏不被打断，稍后再探一次。",
            "Steps aside when a full-screen app is in front: presentations and games don't get interrupted, and it checks again a little later."),
        new Entry("settings.health.deferOn", "让路", "Yields"),
        new Entry("settings.health.deferOff", "不让路", "Doesn't"),
        new Entry("settings.health.eyeRestPreview", "试一试当前设置的效果", "Try the current settings"),
        // 原来这一句末尾写着"时钟上还会多显示一行「休息 HH:mm」"，那是<b>抄屏幕上那几个字</b>：
        // 组件那一侧的文字不在这一批的射程里，英文界面上那行仍然是中文，照抄就成了假话。
        // 所以两列都改成说"多显示一行下一次休息的时刻"——措辞改动登记在报告 §二百五十五。
        new Entry("settings.health.eyeRestClockHint",
            "想少两步：桌面「时钟」组件的右键菜单里就有护眼休息提醒那一节，开关、间隔、提醒形式与「试一试」在那儿改也一样生效（改的是同一份设置），时钟上还会多显示一行下一次休息的时刻。",
            "The shorter route: the desktop “Clock” widget's right-click menu has the same section. Its switch, interval, reminder style and “try it now” take effect from there too — it's the same setting — and the clock shows an extra line with the time of your next break."),
        new Entry("settings.health.scoreTitle", "收藏健康度", "Collection health"),
        new Entry("settings.health.scoreNote",
            "本地聚合，零网络。把「收藏夹腐败」变成可行动清单。",
            "Aggregated locally, no network. Turns a decaying collection into a list you can act on."),
        new Entry("settings.health.trend", "近 14 天新增", "Added in the last 14 days"),
        new Entry("settings.health.diagnosticsTitle", "诊断", "Diagnostics"),

        // ===== 开关两侧的这两个字 =====
        // 一颗键服务全应用那 22 对开关标签（现场数过：此刻全住在设置页，本批进了 7 对），
        // 而不是每颗开关一行：这两个词是同一个词汇，各写一份的下场就是同一屏上出现"开启／打开／启用"三种说法。
        // ⚠ 它们不许走 <c>LocText.Key</c>：ToggleSwitch 同时有 Header 与 Content，
        // 那颗键会照上面的排除规则去写 <c>Header</c>（＝开关上方多出一行字），OnContent 一个字都不动。
        new Entry("toggle.on", "开启", "On"),
        new Entry("toggle.off", "关闭", "Off"),
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

    /// <summary>按语言取那一列。两列都必填 ⇒ 这里没有"缺译回退"那条分支——那是第三门语言进来时才需要的事。</summary>
    public static string TextOf(Entry entry, AppLanguage language) => language switch
    {
        AppLanguage.English => entry.En,
        _ => entry.Zh,
    };

    public static IReadOnlyList<string> Keys() => All.Select(e => e.Key).ToList();
}
