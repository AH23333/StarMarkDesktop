#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StarMark.Core.Hotkeys;

namespace StarMark.Core.Guide;

/// <summary>
/// 一段说明：一个标题加几行正文，外加"这一步能把用户带去哪儿"与"这一步配哪张示意图"。
/// <para>刻意不用富文本/markdown——三处宿主（首启向导、顶栏「？」的浮层、设置页「使用说明」栏）
/// 都只是"标题 + 项目符号"，中间格式只会多出一层要维护的渲染器。</para>
/// <para><paramref name="JumpTag"/> 与 <paramref name="Art"/> 都是<b>名字而不是内容</b>：跳转要落到
/// 真存在的页（对不上就是假按钮，用户点了没反应＝把活推回给他，P-54），示意图要画得出来
/// （对不上就是一片空白）。两者都由 <c>GuideGateTests</c> 对着 UI 那两份清单比，写错当场红。</para>
/// </summary>
public sealed record GuideSection(string Title, IReadOnlyList<string> Lines, string? JumpTag = null, string? Art = null);

/// <summary>
/// 一页的说明。<b>tag 与文字写在同一行</b>：原先是"列表按位置映射到 tag"，那正好是"两个槽位对调
/// 而谁也发现不了"的形状（列表顺序一改，说明就串页），所以把 tag 放进条目本身。
/// </summary>
public sealed record PageGuide(string Tag, string Title, IReadOnlyList<string> Lines);

/// <summary>
/// 应用内说明的<b>唯一文字出处</b>（批次 VC-3）。
/// <para><b>为什么住 Core</b>：同一批话有三个地方要说——首启引导、每页右上角的「？」、设置页的
/// 「使用说明」栏。写三份必然漂移（#123 那一族：一处事实多个出处），而漂移的表现是"三处说法不一样，
/// 用户不知道哪句作数"。住 Core 之后它可被单测：每一页有没有说明、有没有空行、
/// 新加一页而这里没补一条＝当场红（<c>GuideGateTests</c>）。</para>
/// <para><b>刻意不写具体按键</b>："F1 截图"这种句子是热键的第二个出处，用户在设置里改过键之后它就是假话。
/// 要提按键的地方只给<b>动作名清单</b>（<see cref="FirstRunGuide.HotkeyActionsToShow"/>），
/// 由 UI 那侧从绑定表现场渲染（<c>HotkeyDisplay</c> 是按键文本的唯一出处）。</para>
/// </summary>
public static class PageGuides
{
    /// <summary>设置页里那一栏的标题；<c>OpenSettingsPage</c> 与 <c>SelectTab</c> 都按这一串匹配，不许各写一份。</summary>
    public const string SettingsTabHeader = "使用说明";

    /// <summary>「？」在设置页上要说的那一页的键（与 <c>ViewModel.CurrentPageTag</c> 同一套字面）。</summary>
    public const string SettingsTag = "settings";

    /// <summary>搜索页的键：它不在侧栏里（输入即进），但「？」同样要说得出它是什么。</summary>
    public const string SearchTag = "search";

    /// <summary>每页说明。顺序即设置页「使用说明」里的呈现顺序。</summary>
    public static IReadOnlyList<PageGuide> InOrder { get; } = new[]
    {
        new PageGuide(SearchTag, "搜索", new[]
        {
            "在顶部搜索框里输入就进到这一页：本地文件、书签、Star、动态都在结果里，按类型分块。",
            "支持 ext:（扩展名）、size:（大小）、dm:（改动日期）、folder:（目录）这类过滤词，语法提示就在搜索框的占位文字里。",
            "↑↓ 选结果、Enter 打开、Ctrl+Enter 打开它所在的位置；一行右键还有复制路径、记录到本地、拖出去。",
        }),
        new PageGuide("tree", "文件夹", new[]
        {
            "把常用目录钉进来，浏览它们的文件与文件夹；条目上右键就是动作：打开、所在目录、置顶、打标签、隐藏、加笔记。",
            "文件也可以从资源管理器直接拖进桌面的「快捷启动」组件，拖进来即成为一条入口。",
            "磁盘上已经不存在的条目会被标出来，这一页给一键清理，不让你点进一条死路径。",
        }),
        new PageGuide("tags", "标签", new[]
        {
            "按标签看库：选一个或多个标签，下面列出带这些标签的条目；标签本身在这里新建与改名。",
            "条目上的标签与颜色都在右键菜单里；想一次整理一批，用设置「AI 增强」里的批量整理。",
        }),
        new PageGuide("activity", "动态", new[]
        {
            "最近的新增、修改、删除流水，只用来回看，不在这里改数据。",
            "绿＝新增、黄＝修改、红＝删除。",
        }),
        new PageGuide("hidden", "隐藏", new[]
        {
            "被隐藏的条目都收在这里，其它页不再显示它们。",
            "要放回去：在任意一行右键选恢复，或直接把这条删掉。",
        }),
        new PageGuide("snapshot", "快照", new[]
        {
            "存的是桌面组件的「布局 + 配置」，不含条目数据本身；换一套摆法之前先存一份，随时整桌还原。",
            "也可以只提取布局（保留当前数据），或在组件右键里就地保存当前这一份。",
        }),
        new PageGuide("clipboard", "剪贴板", new[]
        {
            "本机剪贴板历史：文本与图片都能记。默认关着，开关、图片上限与轮转都在设置「拓展功能」。",
            "一行右键：复制回去、打开、贴到桌面、发送到快捷启动。图片那行显示缩略图；文件已经不在这台机器上时会直接说出来，不放一张打不开的图占位。",
            "这条链不上传任何内容，记录都在这台机器的文件里。",
        }),
        new PageGuide("trending", "热榜", new[]
        {
            "GitHub 仓库热榜，可以直接在这里 star 或取消 star。",
            "这一页默认不显示，开关在设置；star 需要一个有权限的 GitHub 令牌，填在设置的凭据格里。",
        }),
        new PageGuide("rss", "RSS", new[]
        {
            "订阅源阅读：条目可以收藏进库，收藏后就出现在「文件夹」与「标签」那些页里。",
            "添加与管理源在设置「拓展功能」；总开关关着时这一页不显示。",
        }),
        new PageGuide(SettingsTag, "设置", new[]
        {
            "所有开关都在这几栏里：常规（主题、材质、托盘、更新）、AI 增强、桌面工具、拓展功能、快捷键、数据、健康与诊断。",
            "「数据」栏管备份与导出，也管这台机器上的库文件在哪；「快捷键」栏能逐条改键，冲突只提示不挡你注册。",
            "这一栏（使用说明）随时能重看首启那份引导。",
        }),
    };

    /// <summary>按页面 tag 取那一页的说明；没有对应说明时返回 null（调用方要说"这一页还没有说明"，不许静默给一个空面板）。</summary>
    public static PageGuide? For(string? tag)
        => tag is null ? null : ByTag.TryGetValue(tag, out var guide) ? guide : null;

    /// <summary>
    /// 「？」浮层与设置页那一栏要说的这一页。<b>取不到时返回一句实话，不返回空</b>：
    /// 空面板的表现是"这颗按钮坏了"，而"这一页还没有说明"至少让用户知道接下来该看哪儿。
    /// 兜底条目<b>不进 <see cref="Tags"/></b>——否则"每页都有说明"那条闸门会被自己骗过去。
    /// </summary>
    public static PageGuide ForOrSaySo(string? tag)
        => For(tag) ?? new PageGuide(tag ?? "—", "这一页还没有说明", new[]
        {
            "这里的说明还没补上。设置「使用说明」栏里有全部功能的文字版。",
        });

    private static readonly Dictionary<string, PageGuide> ByTag = ToMap();

    private static Dictionary<string, PageGuide> ToMap()
    {
        var map = new Dictionary<string, PageGuide>(System.StringComparer.Ordinal);
        foreach (var guide in InOrder)
        {
            // 同一个 tag 写两遍＝后面那条悄悄顶掉前面那条（表现是"改了说明没生效"），当场炸比静默好。
            map.Add(guide.Tag, guide);
        }
        return map;
    }

    /// <summary>全部页面 tag（闸门拿它对着「能导航到哪些页」那张表比：多一页少一页都要红）。</summary>
    public static IEnumerable<string> Tags => ByTag.Keys;
}

/// <summary>
/// 首启那一份引导（批次 VC-3 起是文字，批次 WJ-1 起是<b>分步向导</b>的内容源）。
/// <para><b>只说"从哪进去"，不教操作细节</b>——细节在每页的「？」与设置页「使用说明」栏里，
/// 把它们全塞进首启那一屏等于没人读。</para>
/// <para><b>标题里不写步数</b>："先知道这四件事"那种句子会把步数变成第二个出处：加一步就得记得改标题，
/// 忘了就是当着用户说假话。步数由向导从 <see cref="Steps"/> 现数（坑表 #272 那条口径的自我兑现）。</para>
/// </summary>
public static class FirstRunGuide
{
    public static string Title => "第一次打开 StarMark，先知道这几件事";

    /// <summary>向导的步子。顺序即呈现顺序；<c>JumpTag</c> 是"带我去这一页"要落到的页（null＝这一步没有页可去）。</summary>
    public static IReadOnlyList<GuideSection> Steps { get; } = new[]
    {
        new GuideSection("它是什么", new[]
        {
            "一个本机资料库加一组桌面小工具：文件与文件夹、书签、GitHub、热榜、RSS、剪贴板历史、截图与屏幕画布。",
            "数据都在这台机器的应用数据目录里，不登录、不上传；要同步是你自己在设置里开启并填凭据。",
        }, Art: GuideArt.Window),

        new GuideSection("先学搜索框：它是什么都能进", new[]
        {
            "顶部搜索框是全程序的主入口：输入就进到结果页，本地文件、书签、Star、动态都在里面，按类型分块。",
            "支持 ext:（扩展名）、size:（大小）、dm:（改动日期）、folder:（目录）这类过滤词，语法提示就在框子的占位文字里。",
            "↑↓ 选结果、Enter 打开、Ctrl+Enter 打开它所在的位置；一行右键还有复制路径、记录到本地、拖出去。",
        }, PageGuides.SearchTag, GuideArt.Search),

        new GuideSection("换页，以及每页右上角那颗「？」", new[]
        {
            "搜索框下面那一排用来换页：文件夹、标签、动态、隐藏、快照、剪贴板，还有默认收着的热榜与 RSS。",
            "每页右上角的「？」只讲你正看着的这一页，所以它不会拿别页的话来套这一页。",
            "关掉窗口不等于退出：它收进托盘，随时用呼出键叫回来（那颗键在设置「快捷键」里能改）。",
        }, "tree", GuideArt.Pages),

        new GuideSection("桌面上那几颗小工具", new[]
        {
            "顶栏的组件按钮新建桌面小工具；组件上右键换外观、存快照、隐藏。首次只给你一颗时钟，其余自己挑。",
            "闹钟与护眼提醒就长在时钟组件上：右键它即可设，不用去设置里翻。",
            "「快照」存的是整套布局加配置，不含条目数据；换一套摆法之前先存一份，随时整桌还原。",
        }, "snapshot", GuideArt.Widgets),

        new GuideSection("截图、贴图与屏幕画布", new[]
        {
            "截图之后能直接标注、识字、贴到桌面；贴图可以成组隐藏，也能交还鼠标（穿透）而不退出。",
            "屏幕画布是一块盖在桌面面上的透明玻璃，用来在演示或看文档时随手画。",
            "这两样的总开关与按键都在设置「拓展功能」与「快捷键」两栏里。",
        }, PageGuides.SettingsTag, GuideArt.Capture),

        new GuideSection("这几件事此刻的按键", new[]
        {
            "下面几行由程序按你的绑定表现场写出来：你在设置里改过键，这里就跟着改，不是抄来的一份。",
            "某个键被别的程序占了，它只提示你，不挡你把这一条设下去。",
        }, PageGuides.SettingsTag, GuideArt.Keys),
    };

    /// <summary>
    /// 引导末尾那一行"这几件事此刻的按键是什么"要列的动作，<b>只给动作名</b>：
    /// 按键文本由 UI 从绑定表现场渲染（用户改过键之后引导必须跟着改，写死就是假话）。
    /// </summary>
    public static IReadOnlyList<string> HotkeyActionsToShow { get; } = new[]
    {
        HotkeyActions.MainToggle,
        HotkeyActions.ScreenCapture,
        HotkeyActions.ScreenPin,
        HotkeyActions.ScreenPinToggleHidden,
        HotkeyActions.ScreenPinClickThrough,
        HotkeyActions.CanvasToggle,
    };
}

/// <summary>
/// 首启向导能引用的<b>示意图名字</b>（批次 WJ-1）。
/// <para><b>这里只有名字，没有几何</b>：形状住在 UI 那一侧（<c>GuideArtView</c>，与工具条图标同一族写法），
/// 因为画出来要依赖 XAML 图元。Core 留一张名字表，是为了让闸门能在<b>两侧</b>对账：
/// 某一步引用了一张没画过的图 ⇒ UI 那里少一个 case，当场红；画了一张没人引用的图 ⇒ 另一侧红。
/// "少一张图"在屏幕上的表现是一片空白，而这片空白不会有任何地方报错。</para>
/// <para>⛔ 图内一个汉字都不放（用户裁决）：放了字，图就得跟着界面语言走，
/// 而且 UI 一改字这张图立刻过期——那正是"假截图当引导"的坑。</para>
/// </summary>
public static class GuideArt
{
    public const string Window = "window";
    public const string Search = "search";
    public const string Pages = "pages";
    public const string Widgets = "widgets";
    public const string Capture = "capture";
    public const string Keys = "keys";

    /// <summary>全部画得出来的示意图名字（UI 的绘制器必须逐个有对应形状）。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Window, Search, Pages, Widgets, Capture, Keys };

    public static bool IsReal(string? name) => name is not null && All.Contains(name, StringComparer.Ordinal);
}
