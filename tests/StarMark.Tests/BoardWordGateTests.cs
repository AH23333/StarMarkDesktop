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
    /// <b>片54 那四颗</b>（右上角那一行：两颗入口＋那句"已存到"与那句"没能存成"）＝廿一颗。
    /// <para>⚠ 这一族⛔ 只数那块面板：片54 起这一扇上有<b>两块</b>会说汉语的表面（面板＋右上角那一行），
    /// 名单钉的是"上屏的字"而⛔"住在哪一块"。另存的两种结果各一颗，带槽的那颗从此有两块表面（见 <see cref="KeyedBar"/>）。</para></summary>
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

    /// <summary>那一行说的两句里带槽的那两颗（那颗路径＝<b>真落盘的那一颗产物</b>）。
    /// <para>⚠ 那颗 <c>boards.openFolderFailedBody</c> 同时住在两块名单里：同一颗键⛔ 许有第二份措辞，
    /// 而"开不出去"那一句在两块表面上说的是各自那颗档——所以钉的是"实参从哪一颗字段来"，⛔ 是"这颗键归哪一块表面"。</para></summary>
    private static readonly string[] KeyedBar =
    [
        "boards.exportSavedBody", "boards.openFolderFailedBody",
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
        foreach (var keyed in KeyedBar)
        {
            Assert.Equal(1, RowSlots(keyed));
            Assert.Contains("Phrase(\"" + keyed + "\", _exportPath ?? string.Empty)", code, StringComparison.Ordinal);
        }
        foreach (var key in Panel.Where(k => !KeyedPanel.Contains(k) && !KeyedBar.Contains(k)))
        {
            Assert.Equal(0, RowSlots(key));
            Assert.Equal(0, Count(code, "Phrase(\"" + key + "\""));
        }
        // 那颗提示卡走的也是同一颗口径：两句都⛔ 带槽，所以 LocMessage.Of 只收键
        foreach (var (key, args, _) in KeyedCallSites(code))
            Assert.Equal(RowSlots(key), args);
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
    }

    /// <summary>⑤整族总数现读：这一族<b>二十一</b>颗（面板那十九颗＋片21 那两颗开窗失败的旧话）。
    /// <para>钉总数而⛔ 钉"至少"：下一批往这一族加一句而不带证人，这一格就会红——而那一颗新句子的换语言，
    /// 是这一族最容易漏的那一半（P-175 那一族在三扇宿主上各漏过一次）。</para></summary>
    [Fact]
    public void TheBoardFamilyHasExactlyTwentyThreeRowsAndTheyAreNamed()
    {
        var keys = UiStrings.All.Select(e => e.Key)
            .Where(k => k.StartsWith("boards.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(23, keys.Length);
        Assert.Equal(Panel.Length + 2, keys.Length);                           // 上屏那廿一颗＋两颗旧话
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
    /// 片54 再抬到 12／9／9</b>：这一屏上从此有<b>九颗</b>可点的（面板那六颗＋右上角那一行的三颗），
    /// 而"隐形"那一族⛔ 因为多加一颗就自己退场——每加一颗新按钮，黑字、底色、描边就各多一颗
    /// （九颗按钮×三样＝27，加标题／正文／那一行那句落点三颗黑字＝黑字 12／底色 9／描边 9）。
    /// 新加的那颗同样⛔ 跟着主题走，所以这一格是<b>每加一颗都得跟着改数</b>的那类尺：漏改＝那颗新按钮吃主题刷＝又隐形一次。</para></summary>
    [Fact]
    public void TheOnlyActionableControlPinsItsOwnColors()
    {
        var body = MethodBody(Code(ReadRepoFile(WindowCs)), "private Grid BuildStage");
        // 十二颗黑字：标题／正文／那一行那句落点＋九颗按钮（片27 只钉了前两颗 ⇒ 后面每颗都⛔ 吃主题刷＝白字）
        Assert.Equal(12, Count(body, "Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black)"));
        Assert.Equal(9, Count(body, "Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))"));
        Assert.Equal(9, Count(body, "BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 130, 130, 130))"));
        // 禁项：这一族⛔ 用"跟着主题走"的那一类刷子（面板与九颗按钮都不跟着走，见 §27.6）
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
