#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.I18n;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 画板那一路「这一扇现在对你说了什么」那一族的形状闸门（Phase C 片27·P-224 落地腿）。
/// <para>P-223 那道闸门修完之后，它拒绝得对，却<b>只对日志说话</b>：屏幕上是一块静止的画布，
/// 而那块板已经不再落盘了——用户能看见的唯一事实是"画上去的东西没了"。§21.5 那句"⛔ 许静默白屏"
/// 剩下的正是这一半：崩后那 ≈2 s 的白帧与拒到底之后的那块静止画布，都得<b>当场有句话</b>。</para>
/// <para>所以这一颗闸门守的是四件事：①那八句新话<b>住在表里且各一处主人</b>（⛔ 在代码里再拼一份成品句）；
/// ②三语各说各的（en 列⛔ 一个汉字，ja 列⛔ 照抄中文）；③那颗"已另存到 &lt;路径&gt;"的<b>槽数＝代码实际递出的实参数</b>
/// （槽多了少了都⛔ 崩，屏幕上是一句话里凭空少一段路径）；④同一颗键两次渲染跟着两次语言走（行为，
/// ①②③全绿而它红＝键进了表而那句话⛔ 会换语言）。</para>
/// <para>⚠ 与那一族旧话（<c>boards.openFailedTitle</c>／<c>boards.openFailedBody</c>）一起数：本档钉的是<b>整族</b>，
/// 下一批往这一族加一句而没在这儿登记，那颗总数就会红——这一族最坏的坏法是"加了一句话没人守它换语言"。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class BoardWordGateTests : IDisposable
{
    private const string WindowCs = "src/StarMark.UI/Boards/BoardWindow.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string Core = "src/StarMark.Core/Boards/BoardPolicy.cs";

    /// <summary>这一扇上屏那些话的名单：片27 落表的八颗＋片34 那颗「交的是手里那一份、⛔ 是屏幕上那块」＋
    /// 片39 那两颗（那颗「打开所在文件夹」与它坏掉时那一句）＋片40 那两颗（「继续编辑」与「仍然离开」）＋
    /// 片49 那四颗（那颗档现在拿不出来那一句的标题、正文与两颗按钮）＋
    /// 片54 那四颗（右上角那一行：两颗入口＋那句"已存到"与那句"没能存成"）＋
    /// 片64 那四颗（那两档交回用户：两颗扳机上的字＋那颗开关的两态）＋
    /// 片83 那一颗（那一行<b>第二</b>颗读数：那块板上有几颗外部内容永远不会出正文＝账本 P-255）＋
    /// <b>片87 那两颗</b>（那一发外链的两种"没开成"：宿主自己拒的／递出去而系统没开成＝账本 P-249 后半）＝廿八颗。
    /// <para>⚠ 这一族⛔ 只数那块面板：片54 起这一扇上有<b>两块</b>会说汉语的表面（面板＋右上角那一行），
    /// 名单钉的是"上屏的字"而⛔"住在哪一块"。另存的两种结果各一颗，带槽的那颗从此有两块表面（见 <see cref="KeyedBar"/>）。</para>
    /// <para>片64 那两颗扳机上的字（「缩放 2×」「背景 开」）与那句落点里念的两档是<b>同一颗键</b>：
    /// 一颗键⛔ 许有第二份措辞（P-13 那一族），所以那里只列一次，实参那一维交 <see cref="KeyedBar"/>。</para></summary>
    private static readonly string[] Panel =
    [
        "boards.recoveringBody", "boards.stuckTitle", "boards.stuckBody", "boards.saveCopyAction",
        "boards.copySavedBody", "boards.copySavedStaleBody", "boards.copyFailedBody",
        "boards.closeFlushTitle", "boards.closeFlushBody",
        "boards.openFolderAction", "boards.openFolderFailedBody",
        "boards.stayAction", "boards.leaveAction",
        "boards.blockedTitle", "boards.blockedBody", "boards.retryAction", "boards.freshBoardAction",
        "boards.exportPngAction", "boards.exportSvgAction",
        "boards.exportSavedBody", "boards.exportFailedBody",
        "boards.exportScaleAction", "boards.exportBackgroundAction", "boards.exportOn", "boards.exportOff",
        "boards.embedHiddenBody",
        "boards.linkBlockedBody", "boards.linkFailedBody",
    ];

    private readonly AppLanguage _before = Locale.Current;

    public void Dispose() => Locale.TrySet(_before);

    /// <summary>①八颗逐颗点名：表里<b>恰好一行</b>、画板那一档<b>至少问一次</b>。
    /// ⛔ 一颗进了表而屏幕上没人念（死键＝下一批以为这一族已经翻好了），也⛔ 有名字没行（运行时念出那颗键名本身）。
    /// 语料⛔ 把表自己算进去（规则 #95/#102：那一句在表里，"有没有人问"就会被它自己满足）。</summary>
    [Fact]
    public void EveryPanelSentenceHasOneRowAndSomebodyAsksForIt()
    {
        foreach (var key in Panel)
        {
            Assert.Equal(1, Count(ReadRepoFile(Table), "new Entry(\"" + key + "\""));
            Assert.True(Count(ReadRepoFile(WindowCs), "\"" + key + "\"") >= 1, "这颗键没人念：" + key);
        }
        // 正件：同一把尺在同一跑里读得到那两颗<b>本批没动</b>的旧话（它们的主人在别档，⛔ 被算成死键）
        var corpus = string.Join("\n", ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => f.Text));
        Assert.True(Count(corpus, "boards.openFailedTitle") >= 1);
        Assert.Equal(0, Count(corpus, "new Entry(\"boards.stuckBody\""));   // ⛔ 把表自己算进语料：这一问在语料里必须是 0
    }

    /// <summary>②三列各说各的：en 列一颗 CJK 也⛔ 有，ja 列⛔ 与中文逐字相同。
    /// <para>通用那颗 <c>I18nContractTests</c> 管整张表，这一格管<b>这一族</b>：本批那八颗里任何一列留空或照抄，
    /// 屏幕上就是"英文界面念出中文"或"日文界面念出中文"那两种被点验过的坏。</para></summary>
    [Fact]
    public void TheThreeColumnsSayThreeDifferentThings()
    {
        foreach (var key in Panel)
        {
            Assert.True(UiStrings.TryGet(key, out var row), "表里没有这颗键：" + key);
            Assert.False(string.IsNullOrWhiteSpace(row.Zh) || string.IsNullOrWhiteSpace(row.En)
                || string.IsNullOrWhiteSpace(row.Ja), key + " 有一列是空的");
            Assert.Empty(Cjk.Matches(row.En).Cast<System.Text.RegularExpressions.Match>());
            Assert.False(row.Ja == row.Zh, key + " 的日文列照抄了中文");
            Assert.False(row.En == row.Zh, key + " 的英文列照抄了中文");
        }
    }

    /// <summary>③槽数＝实参数。那三颗"已另存到／没能打开 &lt;路径&gt;"递的是<b>面板那颗副本的路径</b>（数据），
    /// 所以它们进实参而⛔ 拼进句子；其余那些⛔ 带槽——代码那一路也⛔ 给它们递实参（递了＝屏幕上多一颗没人念的数）。
    /// <para>这一半是这套机制独有的错法：实参与槽对不上时⛔ 崩，而是一句话里凭空少一段路。</para>
    /// <para>片54 起分<b>两块名单</b>：面板那三颗递 <c>_savedPath</c>，右上角那一行递 <c>_exportPath</c>（<see cref="KeyedBar"/>）。
    /// 名单⛔ 在这里数两遍（上面一颗数组与下面那颗 <c>Where</c> 各列一遍＝迟早只补一处），所以它是本档的两颗字段。</para></summary>
    private static readonly string[] KeyedPanel =
    [
        "boards.copySavedBody", "boards.copySavedStaleBody", "boards.openFolderFailedBody",
    ];

    /// <summary>那一行说的<b>带槽那四颗</b>（那颗路径＝<b>真落盘的那一颗产物</b>，那两档＝<b>那一发真用过的</b>两颗值）。
    /// <para>⚠ 那颗 <c>boards.openFolderFailedBody</c> 同时住在两块名单里：同一颗键⛔ 许有第二份措辞，
    /// 而"开不出去"那一句在两块表面上说的是各自那颗档——所以钉的是"实参从哪一颗字段来"，⛔ 是"这颗键归哪一块表面"。</para>
    /// <para>片64 起这一颗名单带<b>三列</b>：键・那颗数・交出去的实参逐字。⚠ 那颗数⛔ 再由这里数第二遍
    /// （名册 415：恒绿的断言数的是自己那份名单）——槽数一律现读表里那一行（<see cref="RowSlots"/>），
    /// 而实参那一列钉的是<b>代码里那一串逐字</b>，于是"递了颗什么"这一问有正本。</para></summary>
    private static readonly (string Key, int Slots, string[] Hands)[] KeyedBar =
    [
        // 那句落点从此<b>三颗</b>实参：那一发真落盘的档・问出去的缩放・问出去的背景。
        // ⚠ 三串分开钉而⛔ 一整串跨行钉：缩进一改就"读不到"，那颗"读不到"在这把尺上会被读成"代码写坏了"（名册 447 换脸）
        ("boards.exportSavedBody", 3,
        [
            "Phrase(\"boards.exportSavedBody\", _exportPath ?? string.Empty,",
            "Phrase(\"boards.exportScaleAction\", ScaleToken(_sentScale)),",
            "Phrase(\"boards.exportBackgroundAction\", Word(BackgroundKey(_sentBackground))))",
        ]),
        ("boards.openFolderFailedBody", 1, ["Phrase(\"boards.openFolderFailedBody\", _exportPath ?? string.Empty)"]),
        // 两颗扳机自己那行字：念的是<b>手里此刻</b>那颗（⛔ 是 _sent*——它们说的是"下一次会用什么"）
        ("boards.exportScaleAction", 1, ["Phrase(\"boards.exportScaleAction\", ScaleToken(_exportScale))"]),
        ("boards.exportBackgroundAction", 1,
            ["Phrase(\"boards.exportBackgroundAction\", Word(BackgroundKey(_exportBackground)))"]),
        // 片83＝那一行第二颗读数：那颗数＝Core 从<b>宿主见过的那一份字节</b>数出来的（账本 P-255）。
        // 钉这一串逐字是因为它<b>只在一处</b>被贴：换语言那一拍走同一颗 PasteHiddenEmbedNotice（⛔ 第二份措辞、⛔ 第二处拼法）。
        ("boards.embedHiddenBody", 1, ["Phrase(\"boards.embedHiddenBody\", count)"]),
    ];

    [Fact]
    public void TheSlotCountEqualsWhatThePanelHandsOver()
    {
        var code = Code(ReadRepoFile(WindowCs));
        foreach (var keyed in KeyedPanel)
        {
            Assert.Equal(1, RowSlots(keyed));
            Assert.Contains("Phrase(\"" + keyed + "\", _savedPath ?? string.Empty);", code, StringComparison.Ordinal);
        }
        foreach (var (key, slots, hands) in KeyedBar)
        {
            Assert.Equal(slots, RowSlots(key));
            foreach (var hand in hands)
                Assert.Contains(hand, code, StringComparison.Ordinal);
        }
        foreach (var key in Panel.Where(k => !KeyedPanel.Contains(k) && !KeyedBar.Any(b => b.Key == k)))
        {
            Assert.Equal(0, RowSlots(key));
            Assert.Equal(0, Count(code, "Phrase(\"" + key + "\""));
        }
        // 那颗提示卡走的也是同一颗口径：两句都⛔ 带槽，所以 LocMessage.Of 只收键
        foreach (var (key, args, _) in KeyedCallSites(code))
            Assert.Equal(RowSlots(key), args);
    }

    /// <summary>③b 片64 那一维的新坏：<b>两颗扳机与那句落点各念各的那一颗字段</b>。
    /// <para>「缩放 2×」那行字念 <c>_exportScale</c>（下一次要用的），而那句"已存到"念 <c>_sentScale</c>
    /// （那一发真用过的）。两行写成同一颗字段就是这一族的坏：他会在那一发还跑着的时候改挑档，
    /// 于是一句"已存到 <c>D:\a.png</c>（缩放 3×…）"指着一张 1× 的图（片32 那族"上一问的脸活到下一问"的另一副脸）。
    /// ⚠ 上面那条 <c>KeyedBar</c> 已经各钉了一串逐字，这一格钉的是<b>那一拍先于发问</b>：
    /// 记下两颗值的 <c>RememberSentExportKnobs()</c> 必须排在 <c>PostExportPick(kind)</c> 之前——
    /// 排在之后＝发出去的那一发用了 A、屏幕上记的是他改完之后的 B。</para></summary>
    [Fact]
    public void TheLandedSentenceReadsTheKnobsThatWereAskedWhileTheButtonsReadTheOnesHeld()
    {
        var code = Code(ReadRepoFile(WindowCs));
        var ask = SourceGate.MethodBody(code, "void ExportImageButton");
        Assert.True(ask.IndexOf("RememberSentExportKnobs();", StringComparison.Ordinal)
                    < ask.IndexOf("PostExportPick(kind);", StringComparison.Ordinal),
                    "那颗记录排在发问之后＝那一发问出去的与屏幕上念的两档可以不是同一颗");
        Assert.Equal(1, Count(code, "RememberSentExportKnobs();"));                    // 只许一位主人
        Assert.Equal(1, Count(code, "_sentScale = _exportScale;"));
        Assert.Equal(1, Count(code, "_sentBackground = _exportBackground;"));
        // 两列字段各归各的表面：扳机念手里那颗、落点念问出去那颗，两边都⛔ 空转成一颗字段
        Assert.Equal(2, Count(code, "ScaleToken(_exportScale)"));
        Assert.Equal(1, Count(code, "ScaleToken(_sentScale)"));
        Assert.Equal(2, Count(code, "BackgroundKey(_exportBackground)"));
        Assert.Equal(1, Count(code, "BackgroundKey(_sentBackground)"));
        // 那颗缩放只由那颗循环改、那颗开关只由那颗翻改变（⛔ 别处再写一遍＝第二个主人）
        Assert.Equal(1, Count(code, "_exportScale = BoardPolicy.NextExportScale(_exportScale);"));
        Assert.Equal(1, Count(code, "_exportBackground = !_exportBackground;"));
        // 那行痕也得念那一发真用的两颗（屏幕上那句话与日志里那行⛔ 许各说一件事）
        var saved = SourceGate.MethodBody(code, "void HandleExportBytes");
        foreach (var column in new[] { "scale=", "background=" })
            Assert.Contains(column, saved, StringComparison.Ordinal);
        Assert.Contains("boards.export.saved kind=", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("scale=\" + _exportScale", saved, StringComparison.Ordinal);
    }

    /// <summary>④行为格：同一颗键两次渲染跟着两次语言走，而那颗槽里的<b>实参留着不动</b>。
    /// 这一格是 P-224 的本体——那句"这一扇已停止保存"必须在用户换语言那一刻换成他能读的那一句，
    /// 否则坏话还挂着，只是换了一种他⛔ 读得懂的字。</summary>
    [Fact]
    public void ThePanelSentencesAreWordedAtPasteTime()
    {
        Assert.True(Locale.TrySet(AppLanguage.English));
        var en = Locale.Fmt("boards.copySavedBody", @"D:\boards\copy-1.excalidraw");
        Assert.True(Locale.TrySet(AppLanguage.Chinese));
        var zh = Locale.Fmt("boards.copySavedBody", @"D:\boards\copy-1.excalidraw");

        Assert.Contains("Saved a copy to D:\\boards\\copy-1.excalidraw", en, StringComparison.Ordinal);
        Assert.Contains("已另存到 D:\\boards\\copy-1.excalidraw", zh, StringComparison.Ordinal);
        Assert.True(Locale.TrySet(AppLanguage.Japanese));
        Assert.Contains("コピーを保存しました", Locale.T("boards.copySavedBody"), StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", Locale.T("boards.stuckBody"), StringComparison.Ordinal);   // 那颗没槽的句⛔ 留一颗空洞

        // 片64＝那句"已存到"从此<b>三颗</b>实参贴出来。这一格量的是"贴出来的成品读起来是一句话"，
        // 而⛔ 是"槽数对得上"（那一维归 ③）：三语各贴一颗同样的档，念出的两档必须<b>各说各的语言</b>、
        // 而那颗数字与乘号<b>三语同一颗</b>（它是数据，换三种语言⛔ 变出三颗缩放）。
        const string png = @"D:\boards\Exported\a.png";
        Assert.True(Locale.TrySet(AppLanguage.Chinese));
        // ⚠ 中文那一列那颗分隔号是「、」而<b>不是</b>「・」：整张表那把通用尺钉的就是"中文列里不许混进假名那一族"
        //（U+30FB 正落在假名段里＝一颗看着像中文的记号，现读 <c>I18nContractTests:57</c>；本批 M0 让它当场拦住一次）
        Assert.Equal(@"已存到 D:\boards\Exported\a.png（缩放 2×、背景 开）", Landed(png, 2, true));
        Assert.True(Locale.TrySet(AppLanguage.English));
        Assert.Equal(@"Saved to D:\boards\Exported\a.png (Scale 2×, Background On)", Landed(png, 2, true));
        Assert.True(Locale.TrySet(AppLanguage.Japanese));
        Assert.Equal(@"D:\boards\Exported\a.png に保存しました（拡大率 2×・背景色 オン）", Landed(png, 2, true));
        // 那颗开关翻到关：句子里那颗字<b>当场跟着换</b>（恒念"开"＝这句话替用户决定了他没挑的那一档）
        Assert.True(Locale.TrySet(AppLanguage.Chinese));
        Assert.Contains("背景 关", Landed(png, 1, false), StringComparison.Ordinal);
        Assert.Contains("缩放 1×", Landed(png, 1, false), StringComparison.Ordinal);
    }

    /// <summary>按宿主那一拍<b>同一副拼法</b>贴出那句落点（两颗扳机的字各自也是表里贴出来的）。
    /// <para>⚠ 这里⛔ 抄一遍成品句当期望：期望串逐字写在上头那一格里，这一颗只做"宿主那样贴"那一手。</para></summary>
    private static string Landed(string path, int scale, bool background)
        => Locale.Fmt("boards.exportSavedBody", path,
            Locale.Fmt("boards.exportScaleAction", scale + "×"),
            Locale.Fmt("boards.exportBackgroundAction",
                Locale.T(background ? "boards.exportOn" : "boards.exportOff")));

    /// <summary>⑤整族总数现读：这一族<b>三十</b>颗（面板那廿八颗＋片21 那两颗开窗失败的旧话）。
    /// <para>钉总数而⛔ 钉"至少"：下一批往这一族加一句而不带证人，这一格就会红——而那一颗新句子的换语言，
    /// 是这一族最容易漏的那一半（P-175 那一族在三扇宿主上各漏过一次）。</para></summary>
    [Fact]
    public void TheBoardFamilyHasExactlyThirtyRowsAndTheyAreNamed()
    {
        var keys = UiStrings.All.Select(e => e.Key)
            .Where(k => k.StartsWith("boards.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(30, keys.Length);
        Assert.Equal(Panel.Length + 2, keys.Length);                           // 上屏那廿八颗＋两颗旧话
        foreach (var key in Panel) Assert.Contains(key, keys);
        Assert.Contains("boards.openFailedTitle", keys);
        Assert.Contains("boards.openFailedBody", keys);
    }

    /// <summary>⑥那一屏上<b>可点</b>的控件⛔ 只钉面板的字色——三样一起钉，且禁项与正件在同一次比较里。
    /// <para>这一格是<b>量出来的</b>，⛔ 推理出来的：2026-10-19 片28 把那道写闸的拒绝造出现场（只改沙盒副本里
    /// 那颗 <c>board.loaded</c> 的回话，宿主那一侧⛔ 动一个字），日志逐字为证
    /// <c>boards.arm.refused want=2 loaded=3 canvas=2 tries=0</c> → <c>boards.arm.retry n=1</c> → 再拒 <c>tries=1</c>；
    /// 同一张 PrintWindow 上标题那一行有 <b>10.3%</b> 深色像素（＝两行正文真的上了屏），
    /// 而那颗按钮整框 <b>122×32 最暗只到 #fafafa</b>、标签框里 14% 的像素是 <b>#ffffff</b>
    /// ⇒ <b>白字画在 #fafafa 面板上＝那颗按钮在屏幕上是隐形的</b>。形状证人五格全绿而它看不见——
    /// 这正是片26／片27 两批登记为"⛔ 验到"的那一格，兑现的方式是它真是一条缺陷。</para>
    /// <para>所以这一格钉的⛔ 是"有这颗按钮"（那颗形状证人早就绿了），而是<b>它自己把字色钉死</b>：
    /// 面板那两行的黑字与那颗按钮的黑字按<b>同一颗尺</b>数，底色与描边各一颗；
    /// 同一次比较里放禁项（<c>ThemeResource</c>＝跟着主题走的那一类写法必须为 0），
    /// 于是那些 1 与那个 0 互为正负件（⛔ 一把只数 0 的尺）。</para>
    /// <para><b>片39 把计数从 3／1／1 抬到 4／2／2，片40 再抬到 6／4／4，片49 再抬到 8／6／6，
    /// 片54 再抬到 12／9／9，片64 再抬到 14／11／11，片83 只抬黑字那一颗（15／11／11）</b>：
    /// 那一行上多了<b>第二颗读数</b>（那块板上有几颗外部内容不会显示・账本 P-255）＝黑字加一颗而⛔ 按钮，
    /// 所以底色与描边那两列照旧——这一族最阴的坏法是"只抬一列"被读成"尺坏了"，
    /// 而这里三列各数一遍正好把它说清楚。这一屏上从此有<b>十一颗</b>可点的
    /// （面板那六颗＋右上角那一行的五颗＝两颗入口＋那颗出口＋那两颗扳机），
    /// 而"隐形"那一族⛔ 因为多加一颗就自己退场——每加一颗新按钮，黑字、底色、描边就各多一颗
    /// （十一颗按钮×三样＝33，加标题／正文／那一行那句落点三颗黑字＝黑字 14／底色 11／描边 11）。
    /// 新加的那颗同样⛔ 跟着主题走，所以这一格是<b>每加一颗都得跟着改数</b>的那类尺：漏改＝那颗新按钮吃主题刷＝又隐形一次。
    /// ⚠ 片64 那两颗扳机之所以走那颗<b>已钉死的 Button</b>而⛔ 下拉／⛔ 复选框，正是这一格的射程：
    /// 那两类控件的字色由主题模板给，进不了"这一族⛔ 用跟着主题走的刷子"那条禁项（真机读数欠着，见账本 P-242）。</para>
    /// <para><b>片87 那两句⛔ 动这一格那三颗数</b>：那一发外链的两种"没开成"都走 <c>offerCopy: false</c>，
    /// 面板上那颗「另存一份到别处」跟着收起、也⛔ 新造控件（归属那一维归 <c>BoardWindowBridgeGateTests</c>）。
    /// 这一句写在这里是因为这一格是<b>每加一颗都得跟着改数</b>的那类尺：把三列读成"上一批那三颗"而实际多了两颗按钮，
    /// 就正好漏掉这一族唯一能漏的那一格（名册 435：只抬一列会被读成尺坏了）。</para></summary>
    [Fact]
    public void TheOnlyActionableControlPinsItsOwnColors()
    {
        var body = MethodBody(Code(ReadRepoFile(WindowCs)), "private Grid BuildStage");
        // 十五颗黑字：标题／正文／那一行那句落点／那一行第二颗读数（片83）＋十一颗按钮
        //（片27 只钉了前两颗 ⇒ 后面每颗都⛔ 吃主题刷＝白字）
        Assert.Equal(15, Count(body, "Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black)"));
        Assert.Equal(11, Count(body, "Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))"));
        Assert.Equal(11, Count(body, "BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130))"));
        // 禁项：这一族⛔ 用"跟着主题走"的那一类刷子（面板与十一颗按钮都不跟着走，见 §27.6）
        Assert.Equal(0, Count(body, "ThemeResource"));
        Assert.Equal(0, Count(body, "Application.Current.Resources["));
    }

    /// <summary>⑦那颗<b>目录名</b>⛔ 进表（片39）。它是身份而⛔ 是措辞：一句上屏话换三种语言就变出三颗目录，
    /// 用户反而一颗都找不到；而名字一旦同时住在表里与 Core 里，改一处就分岔。
    /// <para>同一条口径仓里已有先例：<c>图片\StarMark 截图</c> 那两颗字也⛔ 跟着语言走。</para></summary>
    [Fact]
    public void TheProductFolderNameIsAnIdentityNotASentence()
    {
        var table = ReadRepoFile(Table);
        Assert.Equal(0, Count(table, "StarMark 画板"));
        // 名字只许住在 Core 那一处（UI 与表都⛔ 再抄一遍）
        Assert.Equal(1, Count(SourceGate.Code(ReadRepoFile(Core)), "StarMark 画板"));
    }
}
