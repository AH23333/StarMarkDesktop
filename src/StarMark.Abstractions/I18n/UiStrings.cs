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
        new Entry("settings.languageNote",
            "改完立刻生效，不用重启。这一版换掉了主窗口顶部那一排（页签、搜索框、排序与筛选按钮）、"
            + "设置页的页眉与那八栏的标题，和「常规」这一栏里由界面写出来的那排字；还没铺开的是其余七栏里的正文、"
            + "页面正文里的字（含「暂无条目，请先同步数据」这类空状态）、工具栏那颗「语言：全部」下拉、"
            + "软件内说明和日志，按批往前推。另有两处不彻底：状态读数那两行（顶栏的索引状态、"
            + "「关于与更新」里那行检查结果）是程序算出来的，不在这批的范围里；下拉框展开的列表当场换语言，"
            + "但闭着的那一格要等你重新选一次才跟着换。",
            "Takes effect at once — no restart needed. Translated so far: the main window's top row "
            + "(nav tabs, the search box, the sort and filter buttons), the Settings page header, its eight "
            + "section titles and everything the General screen itself writes. Still Chinese for now: the "
            + "bodies of the other seven sections, the page bodies (including empty-state lines), the language "
            + "filter dropdown on the toolbar, the in-app guide and the log — more sections land with each "
            + "following pass. Two things aren't complete: the "
            + "two status lines (index state on the top bar, the last check result under About & updates) are "
            + "computed by the program rather than written on the screen, and in a dropdown the opened list "
            + "switches at once while the closed box keeps its old label until you pick again."),

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

        // ===== 开关两侧的这两个字 =====
        // 一颗键服务全应用那 22 对开关标签（现场数过：此刻全住在设置页，本批进了 5 对），
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
