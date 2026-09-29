#nullable enable
using System;
using System.Collections.Generic;

namespace StarMark.Abstractions;

/// <summary>
/// 条目卡片"哪个动作该出现"的单一判据（纯函数，口径同 <c>ClipboardPolicy</c>）。
/// <para>
/// 为什么收在这里而不是各写各的：卡片与右键菜单一度是<b>逐处</b>写 <c>IsLauncherMode … Invert</c>，
/// 每加一类"外部来的、不该写主库的行"就要在十来处各补一个条件——漏掉一处就是一个点了会误写库的按钮。
/// 现在宿主只需要回答"这行是什么"，显示与否由这里的真值表决定（可单测，改坏了会红）。
/// </para>
/// </summary>
public static class ItemCardPolicy
{
    /// <summary>这一行是不是 GitHub 热榜候选（外部数据、默认不入库）。</summary>
    public static bool IsTrendingRepo(string? source)
        => string.Equals(source, ItemSources.Trending, StringComparison.Ordinal);

    /// <summary>这一行是不是 RSS 源里的候选（外部数据、默认不入库，D4 口径）。与热榜同一族，但动作集不同。</summary>
    public static bool IsRssCandidate(string? source)
        => string.Equals(source, ItemSources.Rss, StringComparison.Ordinal);

    /// <summary>
    /// 是否显示"库管理"类动作（置顶 / 隐藏 / 编辑笔记 / 编辑标签 / 发送到桌面 / 删除）。
    /// <para>三种宿主合成的行都要关掉：快捷启动入口（<paramref name="isLauncherMode"/>）、热榜候选
    /// （<paramref name="isTrendingRepo"/>）与 RSS 候选（<paramref name="isRssCandidate"/>）——
    /// 前两者<b>有</b>会被写进库：对 Id=0 的候选执行置顶会经"按需登记"把它塞进 items，
    /// 直接违背"这两类默认不入库"。</para>
    /// </summary>
    public static bool ShowsLibraryActions(bool isLauncherMode, bool isTrendingRepo, bool isRssCandidate = false)
        => !isLauncherMode && !isTrendingRepo && !isRssCandidate;

    /// <summary>是否显示热榜专属动作（⭐Star / 🔖收进收藏）。反过来也成立：只有热榜候选才有这两个按钮。</summary>
    public static bool ShowsTrendingActions(bool isTrendingRepo) => isTrendingRepo;

    /// <summary>
    /// 是否显示「收藏」这一项：热榜候选与 RSS 候选<b>共用同一个按钮位</b>，但落点与文案各不相同
    /// （热榜收进本机书签；RSS 收进"RSS订阅 / 源名"那一层，见 <see cref="RssCollectTip"/>）。
    /// <para>刻意只留一个判据：两处各写一遍的话，加一类候选就会只补上一处，另一处变成一个
    /// 点了没有对应动作的死项。Star 不在这里——RSS 没有远端可 Star。</para>
    /// </summary>
    public static bool ShowsCollect(bool isTrendingRepo, bool isRssCandidate) => isTrendingRepo || isRssCandidate;

    /// <summary>收藏动作的文字（按行种类分流）。</summary>
    public static string CollectLabelFor(bool isRssCandidate, bool collected)
        => isRssCandidate ? RssCollectLabel(collected) : CollectLabel(collected);

    /// <summary>收藏动作的 tooltip（按行种类分流）。RSS 那一份要说清落到哪个文件夹，故多带一个参数。</summary>
    public static string CollectTipFor(bool isRssCandidate, bool collected, string folderPath)
        => isRssCandidate ? RssCollectTip(collected, folderPath) : CollectTip(collected);

    /// <summary>
    /// 是否提供「发送到桌面 · 快捷启动」。它写的是<b>组件配置</b>而不是主库，所以 <see cref="ShowsLibraryActions"/>
    /// 没管它，但两类合成行同样不该出现：启动器行本来就在快捷启动里（发过去＝自己给自己再加一条），
    /// 热榜候选则会把"顺手一发"变成一个长期存在的入口——用户只是想看看这个仓库。
    /// </summary>
    public static bool CanSendToLauncher(bool isLauncherMode, bool isTrendingRepo, bool hasUri, bool isRssCandidate = false)
        => hasUri && ShowsLibraryActions(isLauncherMode, isTrendingRepo, isRssCandidate);

    /// <summary>
    /// 「预览」这一项是否出现。<b>RSS 候选不给</b>：源里的条目要先抓网页正文才能预览，而用户明确
    /// "暂不提供预览，网页解析太麻烦" ⇒ 与其给一个十有八九打不开的按钮，不如没有（点条目直接跳文章）。
    /// 热榜不给是另一个理由（它只有仓库主页，预览等于再开一次浏览器）——两件事别混成一个判据。
    /// </summary>
    public static bool ShowsPreview(bool isRssCandidate) => !isRssCandidate;

    /// <summary>RSS 那一行的动作文案。<b>已经收藏过的不再提供"从库里移除"</b>（P-88 的裁决：
    /// 这一栏的职责是"把看中的收进来"，移除属于资料库自己的页面），所以收过之后它是一颗说明性的灰按钮，
    /// 不是可点的开关。</summary>
    public static string RssCollectLabel(bool collected) => collected ? "已收藏" : "收藏到文件夹";

    public static string RssCollectTip(bool collected, string folderPath) => collected
        ? $"已经在「{folderPath}」里了。要移除请到资料库对应那一行"
        : $"作为书签存进本机，并放进「{folderPath}」（第一次收藏这个源时会建出这一层文件夹）";

    /// <summary>
    /// 条目类型 → 行首图标，<b>全应用唯一出处</b>（批次 RX，P-123 第一条）。
    /// <para>为什么收在这里：这条映射一度有三份——库管理卡片一张、两张组件各一张——而且已经漂移过两次：
    /// ① <see cref="ItemType.File"/> 在卡片是 📄、在组件是 📁，同一类条目在两个界面读成两种东西；
    /// ② 剪贴板条目立项那次只补了卡片那张表，两张组件表把它渲染成了兜底图标（报告 §IG 那条口径）。</para>
    /// <para><b>File 取 📁 而不是 📄 的依据</b>：这一类<b>含文件夹</b>——Everything 的结果与"拖入即登记"
    /// 都会落目录（<c>LocalFileIdentity.FromPath</c> 的注释自己写了"目录或根"），「类型」多选里还有
    /// 一档"只看文件夹"；而主窗的来源分段控件早就把这一类标成 📁（<c>MainWindow.xaml</c> 的 SourceFile）。
    /// 两处既有事实都指向 📁，卡片那个 📄 是少数派。</para>
    /// </summary>
    public static string GlyphOf(ItemType type) => type switch
    {
        ItemType.GitHubStar => "⭐",
        ItemType.Bookmark => "🔖",
        ItemType.File => "📁",
        ItemType.Clipboard => "📋",
        ItemType.Todo => "✅",
        ItemType.Note => "📝",
        // 兜底不许长得像任何一种"用户状态"：旧的两张组件表用 📌，于是漏补臂的类型在界面上看起来像被置顶了——
        // 一个假信号比一个中性点难发现得多。走到这里只可能是库里存着本二进制不认识的类型序号（降级打开新库）。
        _ => "•",
    };

    /// <summary>Star 按钮的图标与文字：已 Star 必须一眼可辨（它是"再点会取消"的信号）。</summary>
    public static string StarGlyph(bool starred) => starred ? "★" : "☆";

    public static string StarLabel(bool starred) => starred ? "已 Star" : "Star";

    /// <summary>
    /// Star 的 tooltip：把"点下去会发生什么"说全，包括"已 Star 是本地已同步列表的判定，
    /// 刚在网页上 star 的要等下次同步才认得"——这句不写，用户会把界面当成不准确的实时状态。
    /// </summary>
    public static string StarTip(bool starred, bool hasToken)
        => (starred ? "已在你的 Star 列表中，点击取消 Star" : "Star 这个仓库（需 GitHub Token）")
           + (hasToken ? string.Empty : "；当前未配置 Token，点击会提示失败")
           + "。判定取自本机已同步的 Star 列表，网页上刚 Star 的要等下次同步才显示。";

    /// <summary>
    /// 是否提供「复制图片」（把位图数据放进剪贴板，区别于"复制的是文字/路径"那一颗）。
    /// 判据只有一件事：<b>这一行指的是本机一个图片文件</b>。
    /// <para>为什么<b>不看条目类型</b>：文件夹树的结果行、剪贴板图片行（Uri 就是我们那张 PNG，见
    /// <c>ClipboardEntry.UriOf</c>）与一条指向本机图片的书签说的是同一件事——"这里有一张图"。
    /// 按类型列白名单就会长出第二种"什么算图片行"：书签里出现 <c>file://…/a.png</c> 是常见事，
    /// 漏掉它＝用户只能先打开图片再另存，而那正是这一项要消灭的绕行。预览（<c>PreviewHost</c>）
    /// 同样是看 Uri 的形状而不是看类型。</para>
    /// <para><b>不在这里 stat 文件</b>：菜单构建是每行一次的批处理，而"文件这会儿还在不在"由动作自己
    /// 报告（带原因）。判据只回答"这类行有没有这个动作"，掺进时态事实就会分出两种说法。</para>
    /// </summary>
    public static bool CanCopyAsImage(string? uri)
    {
        // 路径解析与动作那一侧共用同一句（LocalFileIdentity）：两处各剥一次前缀，
        // 就会出现"菜单里有这一项、点下去说没有路径"。
        if (!LocalFileIdentity.TryPathFromUri(uri, out var path)) return false;
        return IsBitmapDecodableExtension(System.IO.Path.GetExtension(path));
    }

    /// <summary>
    /// 「复制图片」认的扩展名：<b>只列 Windows 自带解码器保证认得的那些</b>。
    /// <para>为什么不跟随 <c>PreviewHost</c> 那份显示用清单（它有 <c>.webp/.ico</c>）：预览只要"画得出一格"，
    /// 这一项要的是"编得成位图交出去"。webp / heic 要靠后装的图像扩展才解得开，ico 系统根本没有解码器——
    /// 给它们出这一颗就是出一颗点了只会报错的死项（与"RSS 候选不给预览"同一取舍）。
    /// 真机若证明某台机器上 webp 确实解得开，加重启条件的理由记在实施方案 §6，不在这里赌。</para>
    /// </summary>
    public static bool IsBitmapDecodableExtension(string? extension) => !string.IsNullOrEmpty(extension)
        && LocalImageExtensions.Contains(extension);

    /// <summary>集合本身也是判据的一部分：加一类格式只有这一处可改，两处（清单与谓词）不能各写一半。</summary>
    private static readonly HashSet<string> LocalImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tif", ".tiff",
    };

    /// <summary>
    /// 卡片的「更新时间」行与「★ 星数」行是否出现。热榜候选两行都要关，理由各不相同：
    /// <para>① 候选没有"本机更新时间"，<c>UpdatedAt</c> 缺省是 0 ⇒ 不关就会在卡片右上角印出
    /// <c>1970-01-01</c>——把一个缺值显示成一个看起来完全真实的日期（与"不知道不许画成 0"同一条口径）。</para>
    /// <para>② 星数已经和语言、本期新增一起写在副标题里了（组件只读副标题，必须带），
    /// 卡片再单独一行 ★ 就是同一件事说两遍。</para>
    /// <para>RSS 候选同理关掉：它的时间是<b>源给的发布时间</b>，已经写在副标题里，而 <c>UpdatedAt</c>
    /// 是"本机更新时间"、对一条没进过库的候选根本不存在——留着就会在卡片右上角印出一个 1970-01-01。</para>
    /// </summary>
    public static bool ShowsTimeAndStarsLines(bool isTrendingRepo, bool isRssCandidate = false)
        => !isTrendingRepo && !isRssCandidate;

    public static string CollectLabel(bool collected) => collected ? "移出收藏" : "收进收藏";
    public static string CollectTip(bool collected)
        => collected
            ? "已在本机收藏（书签）里，点击移除；不会动 GitHub 的 Star 状态"
            : "把它作为书签存进本机收藏；不会动 GitHub 的 Star 状态";

    /// <summary>
    /// 「贴到桌面」的摆放判据（ClipIMG-P3 的纯函数半格）。
    /// <para><b>历史图没有"原位"</b>：截图 F3 贴图时摆放＝当时框选的那块矩形，而把一张已在磁盘上的图
    /// 贴出去，只剩"用户手边那块屏"——<paramref name="workX/workY/workWidth/workHeight"/> 由调用方取
    /// <b>光标所在屏的物理工作区</b>（与提示卡同一出处：多屏时贴主屏角落，副屏用户看不见，RV 已证）。
    /// 图大于工作区时贴左上角（<c>Math.Max(0,…)</c>）：负的半屏偏移会把整张图推出屏外，
    /// 而推出去的那半没有任何找回入口——贴图窗只在屏幕上才可拖。</para>
    /// </summary>
    public static Capture.IntRect CenteredPlacement(
        int workX, int workY, int workWidth, int workHeight, int imageWidth, int imageHeight)
        => new(workX + Math.Max(0, (workWidth - imageWidth) / 2),
               workY + Math.Max(0, (workHeight - imageHeight) / 2),
               Math.Max(0, imageWidth), Math.Max(0, imageHeight));
}
