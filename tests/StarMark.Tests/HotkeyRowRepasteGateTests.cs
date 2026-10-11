#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using StarMark.Core.Hotkeys;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// <b>「快捷键」那一栏每一行的四句话，换语言那一刻必须跟着改口</b>（批次 WI-40-3b，P-175 缺口②·机制 (C)）。
/// <para>这一族的形状是：行由 <c>ItemsRepeater</c> ＋ <c>DataTemplate</c> 长出来，值由 code-behind 算。
/// 从前 code-behind 把<b>成品串</b>存进行模型，而模板上的 <c>i18n:LocText.Key</c> 只能带一颗<b>固定</b>键、
/// 带不了"这一行自己的那颗键"也带不了实参 ⇒ 换语言那一刻整栏一个字都不动，要重进那一栏才更新
/// （＝用户那句"使用说明要重进设置页才生效"的同一族，换了个表面）。</para>
/// <para><b>定案＝(C)</b>：行模型存「键＋实参」＋显示侧 getter，模板那四颗槽 <c>Mode=OneWay</c>，
/// 宿主在 <c>Locale.Changed</c> 那一拍把四颗显示属性重念一遍。⛔ <c>ElementPrepared</c> 那族方案（(A)／(A′)）：
/// <c>LocText</c> 的重贴名单按「元素＋那颗依赖属性」去重（<c>Helpers/LocText.cs:337-366</c>），
/// 而容器会被回收给<b>另一行数据</b> ⇒ 换语言时按<b>旧那行的实参</b>贴到<b>新那行</b>上＝串台，比没翻难发现（坑表 #447）。
/// 同一族形状在仓里已有可抄的出处＝<c>MonitorWordGateTests</c>（✅WI-60b 的 <c>MonitorRow</c>）。</para>
/// <para>证人分两类：源码形状（<c>StarMark.Tests</c> 引用不到 <c>StarMark.UI</c>）与<b>真跑的读数</b>
/// （连接符与那颗嵌套原因住在 Abstractions／Core，语言换了就当场量）。语言读数的期望那一侧一律<b>现取同一颗键</b>，
/// ⛔ 抄表里的措辞（规则 #130：抄的那份会跟着表落后，而"只改措辞"必须绿）。
/// 屏幕上到底是哪一版念哪一句，仍欠真机读数（#575 那一跑）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class HotkeyRowRepasteGateTests : IDisposable
{
    private const string RowsCs = "src/StarMark.UI/Views/SettingsPage.Hotkeys.cs";
    private const string PageCs = "src/StarMark.UI/Views/SettingsPage.xaml.cs";
    private const string PageXaml = "src/StarMark.UI/Views/SettingsPage.xaml";
    private const string CoreErrCs = "src/StarMark.Core/Hotkeys/HotkeyErrorText.cs";
    private const string ServiceCs = "src/StarMark.UI/Services/HotkeyService.cs";

    /// <summary>模板里那四颗"给人看的话"的槽位——名单就是这一族的定义，⛔ 只数不点。</summary>
    private static readonly string[] WordSlots = { "ActionName", "BindingText", "ConflictText", "RegisterErrorText" };

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => SourceGate.Code(SourceGate.ReadRepoFile(rel));

    /// <summary>模板那一段（注释先抹：注释里引用一颗槽名不算一处绑定）。</summary>
    private static string HotkeyTemplate()
    {
        var xaml = Regex.Replace(SourceGate.ReadRepoFile(PageXaml), @"<!--.*?-->", " ", RegexOptions.Singleline);
        return SourceGate.Between(xaml, "<DataTemplate x:Key=\"HotkeyRowTemplate\"", "</DataTemplate>");
    }

    /// <summary>
    /// ① <b>模板里那四颗话槽一律 <c>Mode=OneWay</c></b>，而那颗当 tag 用的动作 ID <b>保持 OneTime</b>
    /// （它是身份，⛔ 跟着语言变——✅WI-40-2 把分类名改成 ASCII 的同一件事，落在行这一侧）。
    /// <para>⛔ 回到裸写 <c>{x:Bind ActionName}</c>：默认就是 OneTime，等于把句子钉在建行那一刻的语言。</para>
    /// </summary>
    [Fact]
    public void TheHotkeyRowTemplateBindsEveryWordSlotOneWay()
    {
        var template = HotkeyTemplate();
        foreach (var slot in WordSlots)
            Assert.Contains("{x:Bind " + slot + ", Mode=OneWay}", template, StringComparison.Ordinal);
        Assert.Contains("{x:Bind Action}", template, StringComparison.Ordinal);
        Assert.DoesNotContain("{x:Bind Action, Mode=OneWay}", template, StringComparison.Ordinal);
        // 话槽的颗数＝上面那份名单的颗数：多一颗（又开一处显示）或少一颗（那句退回成品串）都在这里红
        Assert.Equal(WordSlots.Length,
            Regex.Matches(template, @"\{x:Bind [A-Za-z]+, Mode=OneWay\}").Count);
    }

    /// <summary>
    /// ② <b>行模型存的是"还没取词的那一句"，屏幕上那句由 getter 现算</b>。
    /// <para>⛔ 让字段与 getter 两处各留一份（旧形状是 <c>public string ActionName { get; set; }</c>）：
    /// 那就是把语言钉在赋值那一刻，本批要修的正是它。</para>
    /// </summary>
    [Fact]
    public void TheRowModelHoldsUnrenderedSentences()
    {
        var page = CodeOf(PageCs);
        foreach (var slot in WordSlots)
            Assert.Contains("public string " + slot + " =>", page, StringComparison.Ordinal);   // 显示侧只算不存

        // 四颗「键＋实参」的入口，与四颗 getter 一一对上（只数不点名＝抓不到"换名"，规则 #164）
        Assert.Equal(WordSlots.Length, SourceGate.Count(page, "public LocMessage? "));
        foreach (var name in new[] { "ActionNameMessage", "BindingMessage", "ConflictMessage", "RegisterErrorMessage" })
            Assert.Contains("public LocMessage? " + name, page, StringComparison.Ordinal);

        // 旧形状不许回来：成品串存进行模型的字段
        Assert.DoesNotContain("private string _bindingText", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private string _conflictText", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private string _registerErrorText", page, StringComparison.Ordinal);
        Assert.DoesNotContain("public string ActionName { get; set; }", page, StringComparison.Ordinal);

        // 重贴那颗只发通知，⛔ 自己再去算一句（算的地方是写的那几处，两处各算＝迟早分岔）
        var repaste = SourceGate.MethodBody(page, "public void Repaste()");
        foreach (var slot in WordSlots)
            Assert.Contains("OnChanged(nameof(" + slot + "))", repaste, StringComparison.Ordinal);
        Assert.DoesNotContain("Locale.T(", repaste, StringComparison.Ordinal);
        Assert.DoesNotContain(".Render()", repaste, StringComparison.Ordinal);
    }

    /// <summary>
    /// ③ <b>宿主那一拍只重贴，⛔ 重建那一栏</b>（✅WI-39 那条界线在这里第二次兑现：重贴≠重新采集）。
    /// <para><c>BuildHotkeyRows()</c> 会重读设置档、重取组件布局清单并整栏重建——换语言那一刻把它跑一遍，
    /// "字改口"就成了"页面闪一下、刚录到一半的键没了"。</para>
    /// </summary>
    [Fact]
    public void TheRepasteHandRebuildsNothing()
    {
        var body = SourceGate.MethodBody(CodeOf(RowsCs), "private void RepasteHotkeyRows()");
        Assert.Contains("foreach (var row in HotkeyRowsItems) row.Repaste();", body, StringComparison.Ordinal);
        foreach (var banned in new[] { "BuildHotkeyRows", "RefreshConflictMarks", "MarkRegistrationFailures",
                                        "HotkeyRowsItems.Clear", "GetLayouts", "new SettingsStore" })
            Assert.DoesNotContain(banned, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// ④ <b>"这句话有没有要说"读的是那句在不在，⛔ 读句子里的字</b>（坑表 #354 那一族的第二个表面）。
    /// <para>从前 <c>HasConflict =&gt; !string.IsNullOrEmpty(_conflictText)</c> 看着无害——那句是成品串，比空串确实等价。
    /// 现在屏幕上那句由 getter 现算，判据若去读 getter 就等于"每次问可见性都取一次词"，而这件事不需要词。</para>
    /// </summary>
    [Fact]
    public void TheFlagsAskWhetherTheRowHasSomethingToSay()
    {
        var page = CodeOf(PageCs);
        Assert.Contains("public bool HasConflict => _conflict is not null;", page, StringComparison.Ordinal);
        Assert.Contains("public bool HasRegisterError => _registerError is not null;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("string.IsNullOrEmpty(_conflict", page, StringComparison.Ordinal);
        Assert.DoesNotContain("string.IsNullOrEmpty(_registerError", page, StringComparison.Ordinal);
        Assert.Contains("public bool Bound", page, StringComparison.Ordinal);   // 计数那颗仍是真身份（WI-40-1 的形状没被冲掉）
    }

    /// <summary>
    /// ⑤ <b>每一处写这句话的地方都交「键＋实参」</b>——按<b>主人</b>点名，⛔ 只数总数
    /// （总数抓不到"这一路退回成品串、另一路多写一颗"这种一增一减）。
    /// <para>三件事各归其位：键位＝数据（走 <c>common.rawValue</c> 那道门）、"没绑"＝表里那句 <c>hotkey.unset</c>、
    /// 正在录且一个键都没按＝<c>null</c>（这一格本来就该空着）⇒ 三颗⛔ 并成一颗。</para>
    /// </summary>
    [Fact]
    public void EveryWritingSiteHandsAKeyNotAFinishedWord()
    {
        var rows = CodeOf(RowsCs);

        var build = SourceGate.MethodBody(rows, "private void BuildHotkeyRows()");
        Assert.Contains("ActionNameMessage = HotkeyActions.DisplayMessage(action, _layouts)", build, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"common.rawValue\", HotkeyDisplay.Display(g!))", build, StringComparison.Ordinal);
        Assert.Contains("LocMessage.Of(\"hotkey.unset\")", build, StringComparison.Ordinal);

        // 回显那颗也是数据：⛔ 因为"这句不归表管"就塞 null——那会让取消录制时还原不出原来那句
        Assert.Contains("LocMessage.Of(\"common.rawValue\", text)",
            SourceGate.MethodBody(rows, "private void UpdateRecordingText()"), StringComparison.Ordinal);
        Assert.Contains("row.BindingMessage = null;",
            SourceGate.MethodBody(rows, "private void BeginRecording(string action, Button btn, HotkeyRow row)"), StringComparison.Ordinal);
        Assert.Contains("row.BindingMessage = g.IsEmpty",
            SourceGate.MethodBody(rows, "private void FinishRecording()"), StringComparison.Ordinal);
        Assert.Contains("_recordingRow.BindingMessage = _recordingPrevMessage ?? LocMessage.Of(\"hotkey.unset\");",
            SourceGate.MethodBody(rows, "private void StopRecording(bool resetText)"), StringComparison.Ordinal);
        Assert.Contains("row.BindingMessage = LocMessage.Of(\"hotkey.unset\");",
            SourceGate.MethodBody(rows, "private void ClearBinding()"), StringComparison.Ordinal);

        // 占用那一句：原因由 Core 交键、指路那句也交键，两颗套在一起交给这一行
        var marks = SourceGate.MethodBody(rows, "private void MarkRegistrationFailures()");
        Assert.Contains("LocMessage.Of(\"hotkey.err.rowNotice\", why, HotkeyErrorText.RebindHint)", marks, StringComparison.Ordinal);
        Assert.Contains("var reasons = new Dictionary<string, LocMessage>();", marks, StringComparison.Ordinal);

        // 冲突那句：名单每一项还是键，连接号交键由 LocJoin 现取（坑表 #362）
        var conflict = SourceGate.MethodBody(rows, "private void RefreshConflictMarks()");
        Assert.Contains("LocMessage.Of(\"hotkey.conflict.shares\", new LocJoin(others, \"hotkey.conflict.join\"))",
            conflict, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Join(Locale.T(", conflict, StringComparison.Ordinal);

        // ⛔ 再有人往显示侧那四颗 getter 上赋值（getter 改回 setter 时这一条就先红）
        foreach (var slot in WordSlots)
            Assert.DoesNotContain(slot + " = ", rows, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑥ <b>Core 交出的是「键＋实参」，⛔ 在自己的调用点取词</b>（坑表 #362：词进了表而取词时机没进）。
    /// <para>注册失败的原因是<b>自动重试</b>那一族算出来的，一句话可能在换语言之前几分钟就拼好了 ⇒
    /// 它一旦是 <c>string</c>，屏幕上那句就永远停在那个时刻。</para>
    /// </summary>
    [Fact]
    public void CoreHandsBackItsReasonsUnrendered()
    {
        var core = CodeOf(CoreErrCs);
        Assert.Contains("public static LocMessage RegisterFailure(", core, StringComparison.Ordinal);
        Assert.Contains("public static LocMessage HookInstallFailure(", core, StringComparison.Ordinal);
        Assert.Contains("public static LocMessage RebindHint => LocMessage.Of(RebindHintKey);", core, StringComparison.Ordinal);
        // 取词一颗都不许发生在这里
        Assert.Equal(0, SourceGate.Count(core, "Locale.T("));
        Assert.Equal(0, SourceGate.Count(core, "Locale.Fmt("));
        Assert.Equal(0, SourceGate.Count(core, ".Render()"));

        Assert.Contains("public LocMessage Reason => HotkeyErrorText.RegisterFailure(ErrorCode, SiblingInstancePids);",
            CodeOf(ServiceCs), StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑦ <b>连接号在被贴上去的那一刻才取词</b>（<c>LocJoin</c> 的全部意义，真跑的读数而不只是形状）。
    /// <para>旧写法在 <c>RefreshConflictMarks</c> 里当场 <c>string.Join(Locale.T(…), others)</c>：整句交给界面时
    /// 连接号已被钉成"当时那一档语言的那一号"，换语言重贴只重算了外层，屏幕上剩下
    /// 英文句子配中文顿号。这一格量的就是那件事：<b>同一个</b> <c>LocJoin</c> 实例，换语言前后各念一次。</para>
    /// </summary>
    [Fact]
    public void TheJoinWordIsTakenWhenTheSentenceGetsPasted()
    {
        var joined = new LocJoin(new object?[]
        {
            LocMessage.Of("hotkey.act.canvas.pen"),
            LocMessage.Of("hotkey.act.canvas.clear"),
        }, "hotkey.conflict.join");

        Locale.TrySet(AppLanguage.Chinese);
        var zhJoinWord = Locale.T("hotkey.conflict.join");
        var zhPen = Locale.T("hotkey.act.canvas.pen");
        var zh = Locale.Fmt("hotkey.conflict.shares", joined);
        Locale.TrySet(AppLanguage.English);
        var enJoinWord = Locale.T("hotkey.conflict.join");
        var enPen = Locale.T("hotkey.act.canvas.pen");
        var en = Locale.Fmt("hotkey.conflict.shares", joined);

        // 期望那一侧全部现取同一颗键（⛔ 抄表里的措辞＝规则 #130），两颗同形时下面那几条就不成立，先把前提钉住
        Assert.NotEqual(zhJoinWord, enJoinWord);
        Assert.NotEqual(zhPen, enPen);
        Assert.Contains(zhJoinWord, zh, StringComparison.Ordinal);
        Assert.Contains(enJoinWord, en, StringComparison.Ordinal);
        Assert.DoesNotContain(zhJoinWord, en, StringComparison.Ordinal);   // ⛔ 英文句子配中文顿号

        // 名单里每一项也是当场取词的：交出的是两颗键，⛔ 两句已经念完的字
        Assert.Contains(enPen, en, StringComparison.Ordinal);
        Assert.DoesNotContain(zhPen, en, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑧ <b>套在里面的那句原因也跟着换</b>——这是 <c>hotkey.err.rowNotice</c> 那一格的失效形状：
    /// 外层进了表而内层那颗 <c>Reason</c> 是几分钟前拼好的成品串时，屏幕上就是<b>英文骨架配中文原因</b>。
    /// <para>读数＝同一颗 <see cref="LocMessage"/> 实例，换语言前后各 <c>Render()</c> 一次：
    /// 两档语言各自念出来的内层那句必须分别出现在自己那一份里，而⛔ 留在对方那一份里。</para>
    /// </summary>
    [Fact]
    public void TheNestedReasonFollowsTheLanguageOfTheMoment()
    {
        var notice = LocMessage.Of("hotkey.err.rowNotice",
            HotkeyErrorText.RegisterFailure(HotkeyErrorText.ErrorHotkeyAlreadyRegistered),
            HotkeyErrorText.RebindHint);

        Locale.TrySet(AppLanguage.Chinese);
        var zhInner = HotkeyErrorText.RegisterFailure(HotkeyErrorText.ErrorHotkeyAlreadyRegistered).Render();
        var zhHint = HotkeyErrorText.RebindHint.Render();
        var zh = notice.Render();
        Locale.TrySet(AppLanguage.English);
        var enInner = HotkeyErrorText.RegisterFailure(HotkeyErrorText.ErrorHotkeyAlreadyRegistered).Render();
        var en = notice.Render();

        Assert.NotEqual(zh, en);
        Assert.Contains(zhInner, zh, StringComparison.Ordinal);
        Assert.Contains(zhHint, zh, StringComparison.Ordinal);
        Assert.Contains(enInner, en, StringComparison.Ordinal);
        Assert.DoesNotContain(zhInner, en, StringComparison.Ordinal);   // 内层⛔ 停在上一次取词的那一刻
        Assert.DoesNotContain(zhHint, en, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑨ <b>空名单连出空串，一颗元素连出它自己</b>（<c>LocJoin</c> 那两颗边界，纯函数点得着的读数）。
    /// <para>"没有名单"与"名单里那一句是空的"在屏幕上必须是同一件事（都不占一行）；
    /// 一颗元素时⛔ 在句首句尾各挂一个连接号。</para>
    /// </summary>
    [Fact]
    public void TheJoinRendersEmptyForNoItemsAndItselfForOne()
    {
        Locale.TrySet(AppLanguage.English);
        Assert.Equal(string.Empty, new LocJoin(Array.Empty<object?>(), "hotkey.conflict.join").ToString());

        var one = new LocJoin(new object?[] { LocMessage.Of("hotkey.unset") }, "hotkey.conflict.join");
        // ⛔ 那颗键的词抄进这里：期望那一侧现取同一颗键，只改措辞必须绿（规则 #130）
        Assert.Equal(Locale.T("hotkey.unset"), one.ToString());

        // 交给 common.rawValue 那道门时念出来的还是同一句（门⛔ 改内容，只把"这不是语言内容"说出来）
        Assert.Equal(Locale.T("hotkey.unset"), LocMessage.Of("common.rawValue", one).Render());
    }

    /// <summary>
    /// ⑩ <b>对照臂：多加一颗合规的话槽必须绿</b>（颗数型判据的标配，规则 #108）。
    /// <para>①那格的 <c>Equal(4, …)</c> 若只由"数出来"担保，删掉一颗槽与新增一颗合规槽读数是同一个数——
    /// 这一格把"新增一颗"造出来（临时文本，⛔ 落进任何档），证明那颗计数⛔ 把"多一句要说"本身判成错。</para>
    /// </summary>
    [Fact]
    public void AnExtraConformingWordSlotStillPassesTheShapeRuler()
    {
        var template = HotkeyTemplate();
        var withOneMore = template.Replace(
            "Text=\"{x:Bind ConflictText, Mode=OneWay}\"",
            "Text=\"{x:Bind ConflictText, Mode=OneWay}\" ToolTip=\"{x:Bind RegisterErrorText, Mode=OneWay}\"");
        Assert.NotEqual(template, withOneMore);
        Assert.Equal(WordSlots.Length + 1,
            Regex.Matches(withOneMore, @"\{x:Bind [A-Za-z]+, Mode=OneWay\}").Count);
        foreach (var slot in WordSlots)
            Assert.Contains("{x:Bind " + slot + ", Mode=OneWay}", withOneMore, StringComparison.Ordinal);
    }
}
