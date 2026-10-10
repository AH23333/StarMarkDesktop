#nullable enable
using System;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-61d（P-185／P-188 第三刀·第三片）的闸门：<b>公共对话框工厂四颗入口的形参一律收「身份」，⛔ 再收成品字</b>。
/// <para>
/// 从前那四颗工厂（<c>PromptAsync</c>／<c>ConfirmAsync</c>／<c>MessageAsync</c>／<c>ShowContentAsync</c>）
/// 收的是 <c>string</c> ⇒ 每一颗调用方只能在<b>拼出这句话的那一刻</b>取词，于是全仓 76 处调用点
/// （<c>bin/WI61d/scope_61d.txt</c>）都是"停在上一门语言"的出口；工厂自己的默认字（「确定／取消／知道了」）
/// 更在任何一把屏幕尺的射程外（✅WI-52·坑表 #387）。现在形参是 <c>LocMessage</c>：键与实参交给工厂，
/// <b>贴到元素那一刻</b>才念成字，换语言由 <c>LocText</c> 那唯一一处全局订阅重贴（对话框开着的那段窗口期也能跟着换）。
/// </para>
/// <para>
/// <b>⛔ 把这一族判据交给"数汉字的尺"</b>：交完字之后 B 桶跨度内一行汉字都不剩（现场＝跨度 32 处／汉字 0 行），
/// "没有汉字"读起来既像做完了也像根本没做。真正说得出"身份有没有交出去"的是下面这几颗<b>形状</b>判据
/// （✅#124：换型必须一条语言读数＋一条形状禁项成对）。
/// </para>
/// <para>颗数一律按整颗 needle 现读，并把"是哪几处"写进注释（<c>bin/WI61d/spans_61d.txt</c>／
/// <c>bin/WI61d/keys_61d.txt</c>，2026-10-10 现场），⛔ 凭记忆（#159／#163）。</para>
/// </summary>
public sealed class DialogWordGateTests
{
    private const string Dialog = "src/StarMark.UI/Helpers/CenteredDialog.cs";
    private const string LocTextCs = "src/StarMark.UI/Helpers/LocText.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string CardVm = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";
    private const string CardXamlCs = "src/StarMark.UI/Controls/ItemCard.xaml.cs";
    private const string MenuCs = "src/StarMark.UI/Helpers/ItemContextMenu.cs";
    private const string BackupCs = "src/StarMark.UI/Views/SettingsPage.Backup.cs";
    private const string WidgetMenuCs = "src/StarMark.UI/Views/WidgetWindow.Menu.cs";
    private const string SnapshotSvcCs = "src/StarMark.UI/Services/WidgetManager.Snapshot.cs";
    private const string SnapshotPageCs = "src/StarMark.UI/Views/SnapshotPage.xaml.cs";
    private const string StatusCs = "src/StarMark.UI/MainWindow.Status.cs";
    private const string LocalDiskCs = "src/StarMark.UI/ViewModels/SettingsPageViewModel.LocalDisk.cs";

    /// <summary>那颗 <c>LocMessage title</c> 的六位主人：四颗公共入口＋<c>BuildWindow</c>＋<c>MakeTitle</c>。</summary>
    private static readonly string[] TitleOwners =
    {
        "LocMessage title,",
        "MessageAsync(LocMessage title, LocMessage message, Window? owner = null)",
        "BuildWindow(LocMessage title, LocMessage? message, double height, Window? owner)",
        "MakeTitle(LocMessage title)",
    };

    /// <summary>工厂把身份贴到元素的三扇门（标题／正文／按钮）——逐颗点名，颗数＝这份名单的长度。</summary>
    private static readonly string[] PasteCalls =
    {
        "LocText.SetMessage(tb, title);", "LocText.SetMessage(tb, message);", "LocText.SetMessage(btn, text);",
    };

    /// <summary>那颗「原样交出去」的门被点名的四档：实参都是<b>数据</b>（条目标题／路径输入框的示例／组件默认名）。</summary>
    private static readonly string[] RawValueHosts = { CardXamlCs, MenuCs, BackupCs, WidgetMenuCs };

    /// <summary>三条路各一格信箱：正文／悬停说明／占位符。合并＝贴提示时拿到正文的实参（坑表 #276 同族）。</summary>
    private static readonly string[] Mailboxes = { "ArgStore", "ToolTipArgStore", "PlaceholderArgStore" };

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>出现次数口径：整颗 needle 一起数（⛔ 按行或按字符类数＝另一族假读数，#149）。</summary>
    private static int Times(string haystack, string needle) =>
        haystack.Length == 0 || needle.Length == 0 ? 0 : haystack.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

    /// <summary>
    /// ①四颗工厂的形参全部交身份；<c>defaultText</c> 例外——那是<b>用户会改的值</b>（数据），
    /// 把它贴进重贴名单会在换语言那一刻盖掉他正在打的字。
    /// </summary>
    [Fact]
    public void TheFourDialogFactoriesTakeIdentitiesWhileTheTypedValueStaysData()
    {
        var code = CodeOf(Dialog);

        // 那颗 title 有六位主人（名单见上）：颗数钉死，⛔ 任何一位悄悄退回 string。
        Assert.Equal(6, Times(code, "LocMessage title"));
        foreach (var owner in TitleOwners) Assert.Contains(owner, code);

        Assert.Contains("Task<string?> PromptAsync(", code);
        Assert.Contains("Task<bool> ConfirmAsync(", code);
        Assert.Contains("Task<HostedDialogResult> ShowContentAsync(", code);

        // 其余形参现场：message 2 必选＋3 可选，三颗按钮名各三处（Prompt／Confirm／ShowContent）。
        Assert.Equal(3, Times(code, "LocMessage? message"));
        Assert.Equal(2, Times(code, "LocMessage message"));
        Assert.Equal(3, Times(code, "LocMessage? primaryText"));
        Assert.Equal(3, Times(code, "LocMessage? cancelText"));
        Assert.Equal(1, Times(code, "LocMessage? secondaryText"));
        Assert.Contains("LocMessage? placeholder = null,", code);
        Assert.Contains("string? defaultText = null,", code);
        Assert.Contains("MakeButton(LocMessage text, bool accent)", code);

        // 禁项：⛔ 形参退回 string（那等于把 76 处调用点一起放回"停在旧语言"那一族）。
        foreach (var back in new[] { "string title", "string? message", "string placeholder",
                                     "string? primaryText", "string? secondaryText", "string? cancelText" })
            Assert.True(Times(code, back) == 0, Dialog + " 里这颗形参又变回成品字了：" + back);
        // ⛔ defaultText 被"顺手换型"：它是数据，进名单就是覆盖用户刚敲的内容。
        Assert.Equal(0, Times(code, "LocMessage? defaultText"));
    }

    /// <summary>
    /// ②工厂内部只许通过 <c>LocText</c> 那几扇门把句子贴上去，⛔ 自己写 <c>Text＝</c>／<c>Content＝</c>——
    /// 那两类写法编译得过、屏幕上也对，只是换语言那一刻没人重贴（P-188 确证②的形状）。
    /// </summary>
    [Fact]
    public void TheFactoryPastesthroughLocTextAndNeverWritesWordsIntoElements()
    {
        var code = CodeOf(Dialog);

        Assert.Equal(PasteCalls.Length, Times(code, "LocText.SetMessage("));
        foreach (var call in PasteCalls) Assert.Contains(call, code);
        Assert.Equal(1, Times(code, "LocText.SetWindowTitle(win, title.Key, title.Args)"));
        Assert.Equal(1, Times(code, "LocText.SetPlaceholderKey(box, placeholder.Key, placeholder.Args"));

        foreach (var back in new[] { "Text = title", "Text = message", "Content = text", "PlaceholderText = placeholder" })
            Assert.True(Times(code, back) == 0, Dialog + " 又把成品字直接写进元素：" + back);
        // ⛔ 工厂自己取词：取词时刻一旦回到调用那一刻，形参收身份就只是摆设。
        Assert.Equal(0, Times(code, "Locale.T("));
    }

    /// <summary>③窗高看的是"有没有那句话"，而那句话现在是颗<b>键</b>：读 <c>.Key</c>／<c>is null</c>，⛔ 读整句。</summary>
    [Fact]
    public void TheWindowHeightReadsWhetherASentenceWasGivenNotItsWords()
    {
        var code = CodeOf(Dialog);
        Assert.Equal(1, Times(code, "message is null ? 0 : 48"));                       // PromptAsync
        Assert.Equal(2, Times(code, "string.IsNullOrWhiteSpace(message.Key) ? 0 : 60")); // Confirm＋Message
        Assert.Equal(0, Times(code, "IsNullOrWhiteSpace(message)"));                      // 那颗旧哨兵不许回来
    }

    /// <summary>
    /// ④带读数的占位符有自己的信箱（本批补上的 <c>PlaceholderArgStore</c>），且三条路各一格。
    /// <para>那颗提示键的 doc-comment 早写着"⛔ 等哪一条真带读数了再加"；形参换成身份之后，
    /// 「完整路径，例：{0}」就是第一颗带读数的占位符——不补信箱，填空就会拿到正文那颗信箱里的实参。</para>
    /// </summary>
    [Fact]
    public void ThePlaceholderHasItsOwnArgMailboxAndIsClearedWhenItCarriesNoReadout()
    {
        var code = CodeOf(LocTextCs);

        Assert.Equal(Mailboxes.Length, Times(code, "ConditionalWeakTable<DependencyObject, ArgsBox>"));
        foreach (var box in Mailboxes)
            Assert.Contains("private static readonly ConditionalWeakTable<DependencyObject, ArgsBox> " + box, code);

        Assert.Contains("public static void SetPlaceholderKey(DependencyObject element, string key, params object?[] args)", code);
        // 不带读数的那一路也过一次信箱：留着上一句的实参＝下一次换语言按旧读数填空。
        Assert.Equal(2, Times(code, "PlaceholderArgStore.Remove(element);"));

        var callback = SourceGate.MethodBody(code, "private static void OnPlaceholderKeyChanged");
        Assert.Contains("var args = PlaceholderArgStore.TryGetValue(d, out var box) ? box.Value : null;", callback);
        Assert.Contains("Register(d, target, key!, args);", callback);
        Assert.Contains("d.SetValue(target, Text(key!, args));", callback);   // 取词＋填空仍走 Text() 那唯一一处
        Assert.DoesNotContain("Register(d, target, key!, null)", callback);    // ⛔ 把实参丢掉＝换语言后填空拿到旧读数

        // 第一颗带读数的占位符确实带读数：表里一颗槽，调用处一份实参。
        Assert.Contains("new Entry(\"settings.data.pathExample\", \"完整路径，例：{0}\"", SourceGate.ReadRepoFile(Table));
        Assert.Equal(1, Times(CodeOf(BackupCs), "LocMessage.Of(\"settings.data.pathExample\", suggestedPath)"));
    }

    /// <summary>
    /// ⑤那颗「原样交出去」的门（<c>common.rawValue</c>＝三列都是 <c>{0}</c>）只许装<b>数据</b>。
    /// <para>口径：这把尺只数"点名了这颗门的那四档"（名单见 <see cref="RawValueHosts"/>，逐档恰好一处），
    /// 它⛔ 证明全仓没有第五档——第五档归 <c>I18nWiringGateTests.TheTableHasNoRowNothingAsksFor</c> 那条双向对账管
    /// （同一件事⛔ 再写第二把尺＝#169）。这里钉的是另外两件事：那颗行的三列形状，和"实参⛔ 是一句引号里的话"。</para>
    /// </summary>
    [Fact]
    public void TheRawValueDoorTakesDataAndEachNamedHostHasExactlyOne()
    {
        Assert.Contains("new Entry(\"common.rawValue\", \"{0}\", \"{0}\", \"{0}\")", SourceGate.ReadRepoFile(Table));

        var joined = string.Join("\n", RawValueHosts.Select(CodeOf));
        Assert.Equal(RawValueHosts.Length, Times(joined, "LocMessage.Of(\"common.rawValue\", "));
        foreach (var host in RawValueHosts)
            Assert.True(Times(CodeOf(host), "LocMessage.Of(\"common.rawValue\", ") == 1, host + " 那颗门的点名数变了");

        // ⛔ 把一句话塞进这颗门＝那句话又有了第二个出处（表里存成品字的旧毛病换了个地方长）。
        Assert.Equal(0, Times(joined, "LocMessage.Of(\"common.rawValue\", \""));
    }

    /// <summary>
    /// ⑥三颗"替调用方选键"的句厂<b>同批</b>改口（否则工厂收身份而它们回字符串＝同一句话两扇门）：
    /// 备份删除那句警告、快照应用失败那颗原因、条目卡那颗动作名。
    /// </summary>
    [Fact]
    public void TheThreeSentenceFactoriesHandOneIdentityEachToAllTheirHosts()
    {
        var cardVm = CodeOf(CardVm);
        var rows = CodeOf("src/StarMark.UI/ViewModels/SettingsPageViewModel.cs");

        Assert.Contains("public static LocMessage DeleteWarningFor(AutoBackupPolicy.BackupKind kind) => kind switch", rows);
        Assert.Contains("public LocMessage DeleteWarning => DeleteWarningFor(File.Kind);", rows);
        foreach (var back in new[] { "public string DeleteWarning", "static string DeleteWarningFor" })
            Assert.Equal(0, Times(rows, back));

        var svc = CodeOf(SnapshotSvcCs);
        Assert.Contains("public LocMessage? LastApplyError { get; private set; }", svc);
        Assert.Equal(0, Times(svc, "public string LastApplyError"));
        // 两颗宿主各自当场兜底那颗通用话（⛔ 把兜底那句写回字符串）。
        Assert.Contains("mgr.LastApplyError ?? LocMessage.Of(\"widget.menu.rollbackFailed\")", CodeOf(SnapshotPageCs));
        Assert.Contains("_manager.LastApplyError ?? LocMessage.Of(\"widget.menu.rollbackFailed\")", CodeOf(WidgetMenuCs));

        // 键的选择只此一处（三位宿主共用）；那颗薄包装只喂"当下就要字串"的 XAML 绑定与菜单行。
        Assert.Contains("public LocMessage OpenMenuMessage =>", cardVm);
        Assert.Contains("LocMessage.Of(ClipboardPolicy.OpensAsCopy(Type, Uri) ? \"card.open.asCopy\" : \"card.open.open\")", cardVm);
        Assert.Contains("public string OpenMenuText => OpenMenuMessage.Render();", cardVm);
        var hosts = string.Join("\n", new[] { CardVm, CardXamlCs, MenuCs }.Select(CodeOf));
        Assert.Equal(1, Times(hosts, "\"card.open.open\""));   // ⛔ 第二处选键＝措辞有了第二个主人
    }

    /// <summary>
    /// ⑦本批<b>刻意没动</b>的那一族（右下角提示卡贴不上屏幕时的兜底＝C 桶）：主窗那三颗出口仍收 <c>string</c>。
    /// <para>为什么不同批：它们写的是<b>常驻</b>那颗 <c>SyncInfoBar</c> 的 <c>Title</c>／<c>Message</c>，
    /// 换语言那一刻没人重贴 ⇒ 必须先给它配一条重贴手臂（顶栏那一行早已走 <c>SetStatusText(LocMessage?)</c> 那颗门，
    /// 是现成的先例）。只翻形参不翻手臂＝同一句提示半中半英，比原缺陷更难发现（✅WI-61c 那颗"没被半搬"的同一口径）。</para>
    /// <para>片2 交表时这一格要<b>一起改锚</b>，⛔ 悄悄放宽这把尺。</para>
    /// </summary>
    [Fact]
    public void TheNoticeCardFamilyWasNotHalfMoved()
    {
        var status = CodeOf(StatusCs);
        foreach (var sig in new[]
                 {
                     "public void ShowError(string title, string message)",
                     "public void ShowErrorWithAction(string title, string message, string actionLabel, Action onAction)",
                     "public void ShowNotice(string title, string message)",
                 })
            Assert.Contains(sig, status);
        Assert.Equal(3, Times(status, "(string title, string message"));

        // 那三颗都落到同一位宿主身上：常驻 InfoBar 的两个格子被直接写字（这就是手臂欠着的那一处）。
        var paste = SourceGate.MethodBody(status, "private void ShowInfoBar");
        Assert.Contains("SyncInfoBar.Title = title;", paste);
        Assert.Contains("SyncInfoBar.Message = message;", paste);
        Assert.Equal(0, Times(status, "LocText.SetMessage(SyncInfoBar"));

        // 顶栏那一行的门早就在（片2 的先例，⛔ 到那时再说"这条机制没有过"）。
        Assert.Contains("private void SetStatusText(LocMessage? message) => LocText.SetMessage(StatusText, message);", status);

        // 那四行的字还在这档（本批⛔ 动它们）。
        Assert.Contains("App.MainWindow?.ShowError(\"清理失败\", ex.Message);", CodeOf(LocalDiskCs));
    }
}
