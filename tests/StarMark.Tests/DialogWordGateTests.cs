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
    private const string NoticeWin = "src/StarMark.UI/Views/NoticeCardWindow.cs";
    private const string Reporter = "src/StarMark.UI/Services/TrayReporter.cs";
    private const string DttCs = "src/StarMark.Abstractions/DateTimeText.cs";
    private const string CountdownCs = "src/StarMark.UI/Views/CountdownWidget.xaml.cs";
    private const string ClockCs = "src/StarMark.UI/Views/ClockWidget.xaml.cs";
    private const string PinCs = "src/StarMark.UI/Services/PinManager.cs";
    private const string ShotCs = "src/StarMark.UI/Services/CanvasService.Snapshot.cs";
    private const string ScreenCs = "src/StarMark.UI/Services/ScreenshotService.cs";
    private const string HubCs = "src/StarMark.UI/Services/AnnotationHub.cs";
    private const string UpdateCs = "src/StarMark.UI/Helpers/UpdateScheduler.cs";
    private const string OcrCs = "src/StarMark.UI/Services/OcrService.cs";

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
    /// ⑦右下角提示卡那一族<b>整条搬完</b>（批次 WI-61d 片2＝P-185 的 C 桶）：三颗出口收 <c>LocMessage</c>，
    /// 而<b>常驻</b>那颗 <c>SyncInfoBar</c> 的两个格子也走"键＋实参进注册项"那条手臂。
    /// <para>片1 立这一格时说的就是"只翻形参不翻手臂＝同一句提示半中半英"。今天两半一起翻了 ⇒
    /// 这一格换成<b>钉住新形状</b>：既不许退回直接赋成品字，也不许把取词拆成第二份实现。</para>
    /// </summary>
    [Fact]
    public void TheNoticeCardFamilyIsMovedWhole()
    {
        var status = CodeOf(StatusCs);
        foreach (var sig in new[]
                 {
                     "public void ShowError(LocMessage title, LocMessage message)",
                     "public void ShowErrorWithAction(LocMessage title, LocMessage message, LocMessage actionLabel, Action onAction)",
                     "public void ShowNotice(LocMessage title, LocMessage message)",
                 })
            Assert.Contains(sig, status);
        Assert.Equal(0, Times(status, "(string title, string message"));   // ⛔ 任何一位主人退回成品字

        // 常驻元素：两颗格子各一条注册项；直接赋字＝换语言那一刻那张卡继续挂着旧话。
        var paste = SourceGate.MethodBody(status, "private void ShowInfoBar");
        Assert.Contains("LocText.SetMessage(SyncInfoBar, InfoBar.TitleProperty, title);", paste);
        Assert.Contains("LocText.SetMessage(SyncInfoBar, InfoBar.MessageProperty, message);", paste);
        Assert.Equal(0, Times(paste, "SyncInfoBar.Title = "));
        Assert.Equal(0, Times(paste, "SyncInfoBar.Message = "));

        // 那颗就地按钮的名字同一条路；顶栏那扇门既没被绕开也没被换成第二份实现。
        Assert.Contains("LocText.SetMessage(button, actionLabel);", status);
        Assert.Contains("private void SetStatusText(LocMessage? message) => LocText.SetMessage(StatusText, message);", status);

        // 提示卡本体：三句都住在**复用**的那扇窗上 ⇒ 全部进注册项（<c>HideCard</c> 不关窗）。
        var win = CodeOf(NoticeWin);
        Assert.Contains("public bool Apply(LocMessage title, LocMessage message)", win);
        Assert.Contains("LocText.SetMessage(_title, title);", win);
        Assert.Contains("LocText.SetMessage(_body, message);", win);
        Assert.Contains("LocText.SetMessage(_hint, HintFor(_actionUrl is not null, _actionLabel));", win);
        Assert.Equal(0, Times(win, "_title.Text = "));
        Assert.Equal(0, Times(win, "_body.Text = "));
        Assert.Equal(0, Times(win, "_hint.Text = "));

        // 「谁在说话 · 说了什么」那句抬头的分隔符也交表（✅P-178"一个主人"同一条口径）。
        var reporter = CodeOf(Reporter);
        Assert.Contains("public static LocMessage CardTitle(LocMessage category, LocMessage title)", reporter);
        Assert.Contains("LocMessage.Of(\"card.title.withCategory\", category, title)", reporter);

        // 那四行裸字面在本档一个字都不剩（它们现在只在 UiStrings 里有一份）。
        var disk = CodeOf(LocalDiskCs);
        Assert.Equal(0, Times(disk, "\"已经没有这些行了\""));
        Assert.Equal(0, Times(disk, "\"清理失败\""));
    }

    /// <summary>
    /// ⑧本批<b>只交门⛔ 交身份</b>的那几颗：句子由 Core／Abstractions 当场念完再回一句成品字，UI 侧只能
    /// 把它们原样交出去（<c>common.rawValue</c>）。名单逐颗点名＋颗数钉死——
    /// <b>⛔ 把那种句子塞进键位</b>：那读起来像做完了，实际是把"停在上一门语言"伪装成"已经交表"。
    /// <para>P-194 收这一族时要连着这一格一起改（✅#178：登记"还剩多少"要同时报两串数）。</para>
    /// </summary>
    [Fact]
    public void TheCrossLayerSentencesEnterThroughTheRawValueDoor()
    {
        var doors = new[]
        {
            (CountdownCs, "LocMessage.Of(\"common.rawValue\", CountdownPolicy.TitleText(item))", 1),
            (ClockCs, "LocMessage.Of(\"common.rawValue\", AlarmPolicy.LineOf(item))", 1),
            // ✅WI-65 片3 第二刀：「贴图上限」那颗判据自己交「键＋实参」⇒ 那道门撤了（撤门⛔ 留下这一格当它还在）。
            //   这里剩下的那一颗 <c>full</c> 是<b>组上限</b>那句（<c>PinGrouping.LimitProblem</c> 仍当场念完，归后面那刀）。
            (PinCs, "LocMessage.Of(\"common.rawValue\", full)", 1),
            (ShotCs, "LocMessage.Of(\"common.rawValue\", shot.Reason)", 3),
            // 同批改掉的另外三颗门：ScreenshotService 那颗 CaptureGate.ReasonFor、AnnotationHub 那两颗 why
            //（两道闸门的判据现在交「键＋实参」，直接递进提示卡）。
            // ✅WI-65 片3 第二刀：识字那两张卡不再拼成品字；这里剩下的那颗是<b>认出来的那段文字本身</b>＝机器文本。
            (OcrCs, "LocMessage.Of(\"common.rawValue\", OcrText.Preview(read.Text))", 1),
            // 引擎连原因都没给时那颗 <c>ocr.card.noReason</c> 交键，而<b>引擎自己那句话</b>的数据门挪到了
            // 它进来的那一处（<c>src/StarMark.Core/Ocr/OcrPipeline.cs</c>，Core 那一档由 OcrReadTests 钉）。
            // 同批新立的那一颗：相对时间那句的第五臂是语言中性的 ISO 日，没有词可念 ⇒ 只能走这道数据门。
            (DttCs, "LocMessage.Of(\"common.rawValue\",", 1),
        };
        foreach (var (file, needle, want) in doors)
            Assert.True(Times(CodeOf(file), needle) == want,
                $"{file}：{needle} 应命中 {want} 处，现场 {Times(CodeOf(file), needle)} 处"
                + "（这道门的主人换了就得连这一格一起改）");
    }
}
