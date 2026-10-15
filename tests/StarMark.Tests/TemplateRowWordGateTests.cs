#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StarMark.Abstractions.I18n;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 批次 WI-67 片9c 第三刀（P-188 第七刀）的闸门：<b>住在 <c>DataTemplate</c> 行上的那两句话⛔ 再把语言冻在产地</b>。
/// <para>射程＝活动名表那 11 档（<see cref="ActVm"/>，两处宿主：<see cref="ActPageCs"/> 与 <see cref="GridCs"/>）
/// ＋世界时钟那一行的原因（<see cref="ClockVm"/>，宿主 <see cref="ClockCs"/>）。
/// 两者都不是"页级那一行"：模板上的 <c>i18n:LocText.Key</c> 只能带一颗<b>固定</b>键，带不了"这一行自己的那颗键"，
/// 所以走 ✅WI-40-3b 那条机制 (C)——<b>行模型持 <c>LocMessage</c> ＋显示侧 getter 绑 <c>Mode=OneWay</c> ＋宿主在
/// <c>Locale.Changed</c> 那一拍逐行 <c>Repaste()</c></b>（前两位宿主＝「快捷键」那一栏与 AI 预览那两行）。</para>
/// <para><b>⛔ 让行对象自己订阅 <c>Locale.Changed</c></b>：那些行会被反复重建（每秒刷新的时钟行、每次数据广播重建的活动流），
/// 挂上去没人摘＝每进一次多一份订阅（坑表 #386 那一族）⇒ 订阅归宿主，且与摘掉<b>成对</b>（④那一格逐档数）。</para>
/// <para>尺＝<see cref="SourceGate.ScreenWords"/>＋<see cref="SourceGate.Count"/>＋<see cref="UiStrings"/>；
/// 计数型判据一律<b>逐档点名</b>，⛔ 一个"看起来差不多"的总数（规则 #115 的仪器面）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class TemplateRowWordGateTests : IDisposable
{
    private const string ActVm = "src/StarMark.UI/ViewModels/ActivityItemViewModel.cs";
    private const string ClockVm = "src/StarMark.UI/ViewModels/WorldClockViewModel.cs";

    private const string ActPageCs = "src/StarMark.UI/Views/ActivityPage.xaml.cs";
    private const string GridCs = "src/StarMark.UI/Views/ItemGridWidget.xaml.cs";
    private const string ClockCs = "src/StarMark.UI/Views/WorldClockWidget.xaml.cs";

    // 第五刀那一族（「本机备份」那一列）：宿主是设置页，它<b>本来</b>就有订阅，这一刀只加第四组成对手臂。
    private const string BackupVm = "src/StarMark.UI/ViewModels/SettingsPageViewModel.cs";
    private const string BackupCs = "src/StarMark.UI/Views/SettingsPage.xaml.cs";

    private static readonly string[] Models = [ActVm, ClockVm];
    private static readonly string[] Hosts = [ActPageCs, GridCs, ClockCs];

    private static readonly string[] KindKeys =
    [
        "activity.kind.starAdd", "activity.kind.starRemove", "activity.kind.bookmarkAdd",
        "activity.kind.bookmarkRemove", "activity.kind.fileAdd", "activity.kind.fileRemove",
        "activity.kind.clipAdd", "activity.kind.clipRemove", "activity.kind.itemAdd",
        "activity.kind.itemModify", "activity.kind.itemDelete",
    ];

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => Code(ReadRepoFile(rel));
    private static string MarkupOf(string csRel) => ReadRepoFile(csRel[..^3]);

    // ────────── ① 屏幕上⛔ 留一句写死的汉字，也⛔ 在产地把句子念好 ──────────

    [Theory]
    [InlineData(ActVm)]
    [InlineData(ClockVm)]
    [InlineData(ActPageCs)]
    [InlineData(GridCs)]
    [InlineData(ClockCs)]
    public void ARowHostOrModelKeptNoHardCodedWords(string rel)
    {
        var words = ScreenWords(CodeOf(rel));
        Assert.True(words.Count == 0, $"{rel} 仍留着非日志的屏幕上汉字片段 {words.Count} 处：{string.Join(" / ", words.Take(5))}");
    }

    /// <summary>
    /// <b>两颗行模型交的都是那颗键</b>：屏幕上那一行由显示侧 getter <b>现算</b>，⛔ 有人把成品串存进字段。
    /// <para>⚠ 数的是<b>声明形状</b>而⛔ 属性名：属性由 <c>[ObservableProperty]</c> 生成，源码里只有 <c>_error</c>（坑表 #531）。</para>
    /// </summary>
    [Fact]
    public void TheRowModelsHoldTheKeyNotTheSentence()
    {
        var act = CodeOf(ActVm);
        Assert.Equal(1, Count(act, "public LocMessage KindMessage { get; }"));
        Assert.Equal(1, Count(act, "public string KindLabel => KindMessage.Render();"));
        Assert.Equal(0, Count(act, "public string KindLabel { get; }"));
        // ⚠ 那一行上有<b>两句</b>会念的话（事件名＋相对时间），宿主那一拍⛔ 只重念一颗：
        // 从前那颗时间在构造函数里取一次词，换语言后屏幕上就是"名字换成英文、时间还留着中文"那半行（片9c 第六刀）。
        Assert.Equal(1, Count(act, "public string AtText => RelativeTimeHelper.Format(At);"));
        Assert.Equal(0, Count(act, "public string AtText { get; }"));
        Assert.Equal(1, Count(act, "OnPropertyChanged(nameof(KindLabel));"));
        Assert.Equal(1, Count(act, "OnPropertyChanged(nameof(AtText));"));

        var clock = CodeOf(ClockVm);
        Assert.Equal(1, Count(clock, "[ObservableProperty] private LocMessage? _error;"));
        Assert.Equal(1, Count(clock, @"public string ErrorText => Error?.Render() ?? string.Empty;"));
        Assert.Equal(1, Count(clock, "public bool HasError => Error is not null;"));
        // 那颗"有没有话要说"⛔ 由念出来的那句反推（换语言那一刻它就是个字符串比较，坑表 #354）。
        Assert.Equal(0, Count(clock, "string.IsNullOrEmpty(Error)"));
    }

    /// <summary>
    /// <b>11 档事件名一颗键一个主人</b>：那张 switch 只认身份（<c>ActivityKind</c>）交出键，
    /// 全仓⛔ 第二处写死"新增书签"那一类中文（表里那 11 行是唯一出处）。
    /// </summary>
    [Fact]
    public void EveryActivityKindHasOneKeyAndNoSecondVoice()
    {
        var act = CodeOf(ActVm);
        // 10 档点名＋那颗 `_ =>` 兜底＝11 处；两处计数<b>相等</b>才算"每一档都交键"（⛔ 一处交字面量、一处交键）。
        Assert.Equal(11, Count(act, "=> LocMessage.Of(\"activity.kind."));
        Assert.Equal(11, Count(act, "LocMessage.Of(\"activity.kind."));
        Assert.Equal(11, KindKeys.Length);
        foreach (var key in KindKeys)
            Assert.Equal(1, Count(AskedCorpus(), @"""" + key + @""""));
        // 汉字⛔ 再出现在行模型这一侧（"收藏 Star"从前就住在这里）
        foreach (var word in new[] { "收藏", "取消", "新增书签", "删除书签", "新增文件", "移除文件", "剪贴板", "删除条目" })
            Assert.Equal(0, Count(act, word));
    }

    // ────────── ② 三份模板：那一行必须跟着那一拍走 ──────────

    /// <summary>
    /// <b>模板里那两行都绑成 <c>Mode=OneWay</c> 的显示侧 getter</b>（<c>x:Bind</c> 默认 OneTime＝值冻在元素建出来那一刻，
    /// 换语言后<b>一个字都不动</b>，要重开那一格才更新＝P-175 缺口②，也就是这一族最大的成因）。
    /// </summary>
    [Theory]
    [InlineData(ActPageCs, "KindLabel")]
    [InlineData(ActPageCs, "AtText")]
    [InlineData(GridCs, "KindLabel")]
    [InlineData(GridCs, "AtText")]
    [InlineData(ClockCs, "ErrorText")]
    [InlineData(BackupCs, "MetaText")]
    [InlineData(BackupCs, "RestoreLabel")]
    public void TheRowTemplateBindsTheReadoutNotTheFrozenString(string csRel, string readout)
    {
        var xaml = MarkupOf(csRel);
        Assert.Equal(1, Count(xaml, "{x:Bind " + readout + ", Mode=OneWay}"));
        Assert.Equal(0, Count(xaml, "{x:Bind " + readout + "}"));      // OneTime 那一形⛔ 回来
        if (readout == "ErrorText") Assert.Equal(0, Count(xaml, "{x:Bind Error, Mode=OneWay}"));
        if (readout == "KindLabel") Assert.Equal(0, Count(xaml, "{x:Bind KindLabel, Mode=OneTime}"));
        if (readout == "MetaText") Assert.Equal(0, Count(xaml, "{x:Bind KindLabel, Mode=OneWay}"));   // 摘要那颗⛔ 再单独绑一次名字
    }

    /// <summary>
    /// <b>第五刀那一族（「本机备份」列表的一行）＝机制 (C) 的第四位宿主</b>：行持两颗键（类别名＋按钮名），
    /// 三颗显示属性由 <c>Render()</c> 现算，宿主那一拍逐行 <c>Repaste()</c>，且与它<b>已有</b>的那三组订阅一样成对摘掉。
    /// <para>⚠ 这一档的其余汉字（<c>Safe(…) 的"哪项设置读失败了"</c>那一族）只进日志，<c>ScreenWords</c>⛔ 看不见这一档＝
    /// 判 <c>ScreenWords == 0</c> 会把仪器自己的口径当成缺陷（✅P-191 那一族的反面）。</para>
    /// </summary>
    [Fact]
    public void TheBackupRowIsTheFourthTemplateRowFamily()
    {
        var vm = CodeOf(BackupVm);
        Assert.Equal(1, Count(vm, "public LocMessage KindMessage { get; }"));
        Assert.Equal(1, Count(vm, "public LocMessage RestoreMessage { get; }"));
        Assert.Equal(1, Count(vm, "public string KindLabel => KindMessage.Render();"));
        Assert.Equal(1, Count(vm, "public string RestoreLabel => RestoreMessage.Render();"));
        Assert.Equal(3, Count(vm, "=> LocMessage.Of(\"settings.data.backup.kind"));      // 三档类别各一颗，⛔ 一处漏成交字面量
        Assert.Equal(1, Count(vm, "OnPropertyChanged(nameof(MetaText));"));
        // 汉字⛔ 再回那一行住：那三档类别名与两颗按钮名从前都写在这里。
        foreach (var word in new[] { "恢复前快照", "手动导出", "用它回滚", "恢复这份" })
            Assert.Equal(0, Count(vm, "\"" + word + "\""));
        Assert.Equal(0, Count(vm, "public string KindLabel { get; }"));                 // ⛔ 成品串存回字段（冻语言）

        var host = CodeOf(BackupCs);
        Assert.Equal(1, Count(host, "foreach (var row in ViewModel.Backups) row.Repaste();"));
        Assert.Equal(0, Count(host, "LocText.SetMessage(row"));                         // ⛔ 逐行挂进那份名单
        Assert.Equal(3, Count(host, "Locale.Changed += Repaste"));                      // 快捷键＋AI 组行＋备份那一列
        // 6：设置页那三组手臂写的是"先退再订"（同一页可能被反复导航回来，⛔ 挂两份），Loaded 三颗退＋Unloaded 三颗摘。
        Assert.Equal(6, Count(host, "Locale.Changed -= Repaste"));

        foreach (var key in new[] { "settings.data.backup.kindAuto", "settings.data.backup.kindPreRestore",
                                    "settings.data.backup.kindManual", "settings.data.backup.restore",
                                    "settings.data.backup.rollback" })
        {
            Assert.Equal(1, Count(AskedCorpus(), @"""" + key + @""""));                 // 一颗键一个主人
            Assert.True(UiStrings.TryGet(key, out var row), "表里没有这颗键：" + key);
            Assert.Empty(Cjk.Matches(row.En).Cast<Match>());
            Assert.NotEqual(row.Zh, row.Ja);
        }
    }

    // ────────── ③ 宿主那一拍：逐行重念，⛔ 新机制也⛔ 让行自己订阅 ──────────

    /// <summary>
    /// <b>三处宿主各把"换语言"这一拍接到自己那批行上</b>：一颗 <c>Repaste()</c> 调用点、一句"逐行"的循环，
    /// 且⛔ 在 <c>LocText</c> 名单里给每一行挂一颗（行数不定、行会重建＝名单变垃圾场，✅WI-45 同一颗口径）。
    /// </summary>
    [Theory]
    [InlineData(ActPageCs, "ViewModel.Activities")]
    [InlineData(GridCs, "ViewModel.Events")]
    [InlineData(ClockCs, "ViewModel.Rows")]
    public void EachHostRepronestesItsOwnRows(string csRel, string collection)
    {
        var code = CodeOf(csRel);
        Assert.Equal(1, Count(code, "foreach (var row in " + collection + ") row.Repaste();"));
        Assert.Equal(0, Count(code, "LocText.SetMessage(row"));      // ⛔ 逐行挂进那份名单
        // ⛔ 产地取词：宿主那一侧只通知，⛔ 自己念（念＝把语言冻在点那一下）。
        Assert.Equal(0, Count(code, "Locale.T(") + Count(code, "Locale.Fmt("));
    }

    /// <summary><b>订阅与摘掉成对</b>（✅P-198 那颗闸门族的本批面：这三处新挂的每一位都必须有一位对应的摘）。
    /// 世界时钟那处写在同一行 lambda 里——那把认得同行双符号的尺正是为这个形状装的。</summary>
    [Fact]
    public void EveryNewLocaleOwnerUnsubscribesWhatItSubscribed()
    {
        foreach (var rel in Hosts)
        {
            var code = CodeOf(rel);
            Assert.Equal(1, Count(code, "Locale.Changed += Repaste"));
            Assert.Equal(1, Count(code, "Locale.Changed -= Repaste"));
        }
    }

    // ────────── ④ 表里那一族⛔ 有死键，也⛔ 抄进另一档 ──────────

    /// <summary>语料⛔ 把表自己算进来（否则"这一行没人问"会被表里那一行自己满足＝仪器骗人，规则 #95/#102）。</summary>
    private static string AskedCorpus()
        => string.Join("\n", ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => f.Text));

    /// <summary>
    /// 汉字那一问数的是<b>抹注释后的正文</b>：doc-comment 里那句"从前这里写的是『本机没有这个时区：…』"是<b>给人的理由</b>，
    /// 拿原文数就会把理由读成第二个出口（规则 #131；坑表 #535 同一族）。
    /// </summary>
    private static string AskedCode()
        => string.Join("\n", ReadRepoUnder("src")
            .Where(f => !f.RelativePath.EndsWith("UiStrings.cs", StringComparison.Ordinal))
            .Select(f => Code(f.Text)));

    [Fact]
    public void EveryRowThisSliceShippedIsAskedForAndOnlyOnce()
    {
        Assert.Equal(1, Count(AskedCorpus(), @"""widget.clock.noZone"""));
        Assert.Equal(0, Count(AskedCode(), "本机没有这个时区"));   // 汉字⛔ 再回正文里住（表里那颗是唯一出处）
        Assert.Equal(0, Count(AskedCorpus(), "new Entry(\"widget.clock.noZone\""));   // ⛔ 把表自己算进语料：这一问在语料里必须是 0
    }

    // ────────── ⑤ 三语各说各的＋换实参就换读数 ──────────

    /// <summary>
    /// <b>那 12 行三列都填了、也⛔ 抄同一份</b>：en 列⛔ 一个汉字／一颗全角标点，ja 列≠zh 列，zh 念出来≠ja 念出来（"没翻"看着像翻了＝坑表 #526 那一族）。
    /// </summary>
    [Theory]
    [InlineData("widget.clock.noZone")]
    [InlineData("activity.kind.starAdd")]
    [InlineData("activity.kind.starRemove")]
    [InlineData("activity.kind.bookmarkAdd")]
    [InlineData("activity.kind.bookmarkRemove")]
    [InlineData("activity.kind.fileAdd")]
    [InlineData("activity.kind.fileRemove")]
    [InlineData("activity.kind.clipAdd")]
    [InlineData("activity.kind.clipRemove")]
    [InlineData("activity.kind.itemAdd")]
    [InlineData("activity.kind.itemModify")]
    [InlineData("activity.kind.itemDelete")]
    public void EachRowSaysItDifferentlyInEachLanguage(string key)
    {
        Assert.True(UiStrings.TryGet(key, out var row), "表里没有这颗键：" + key);
        Assert.Empty(Cjk.Matches(row.En).Cast<Match>());
        Assert.NotEqual(row.Zh, row.Ja);
        Assert.NotEqual(row.Zh, row.En);
        var said = new List<string>();
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            var one = LocMessage.Of(key, "Asia/Nowhere").Render();
            Assert.DoesNotContain("{0}", one, StringComparison.Ordinal);        // 填空没做＝屏幕上挂着键的形状
            Assert.Equal(said.Count + 1, said.Append(one).Distinct(StringComparer.Ordinal).Count());   // 三语⛔ 有两档说同一句
            said.Add(one);
        }
        Assert.Equal(3, said.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// <b>那颗带读数的原因：换个点位就换个句子，也⛔ 留着上一条</b>（与"整句照抄"那一半成对，规则 #124）。
    /// </summary>
    [Fact]
    public void TheZoneReasonChangesWhenTheZoneChanges()
    {
        Locale.TrySet(AppLanguage.English);
        var one = LocMessage.Of("widget.clock.noZone", "Mars/Phobos").Render();
        var other = LocMessage.Of("widget.clock.noZone", "Asia/Shanghai").Render();
        Assert.Contains("Mars/Phobos", one, StringComparison.Ordinal);
        Assert.DoesNotContain("Mars/Phobos", other, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", other, StringComparison.Ordinal);
        Assert.Equal(1, RowSlots("widget.clock.noZone"));
    }

    /// <summary>
    /// <b>✅WI-67 片10（P-202／P-203）＝屏幕读数一跑咬出来的两颗</b>：换语言那一拍要把「画布热键只读一览」那一排
    /// 一起重建（那颗是<b>建出来那一刻</b>把动作名念成字的），健康档位那颗则⛔ 存成品字、改交<b>键</b>＋显示侧 getter。
    /// <para>两格都落在形状判据的射程外那一格（与第六刀、名册 271 同一族）：屏幕上那句<b>没有写死汉字</b>——
    /// 它是从表里念出来的，所以"整档屏幕字归零"那把尺全绿而人看见的仍是旧语言。</para>
    /// </summary>
    [Fact]
    public void TheCanvasHotkeySheetAndHealthGradeRepasteOnTheSameBeat()
    {
        var armBody = MethodBody(Code(ReadRepoFile("src/StarMark.UI/Views/SettingsPage.Hotkeys.cs")), "private void RepasteHotkeyRows()");
        Assert.Equal(1, Count(armBody, "ViewModel.RefreshCanvasHotkeySheet();"));   // ⛔ 新开一位主人，挂到已有那一拍
        var health = Code(ReadRepoFile("src/StarMark.UI/ViewModels/SettingsPageViewModel.Health.cs"));
        Assert.Equal(3, Count(health, "LocMessage.Of(\"health.grade."));        // 三档各一颗键
        Assert.Equal(0, Count(health, "Locale.T(\"health.grade."));             // ⛔ 取词时机回到建行的那一刻
        Assert.Equal(1, Count(health, "public string ScoreGradeText => ScoreGrade?.Render() ?? string.Empty;"));
        Assert.Equal(1, Count(health, "OnPropertyChanged(nameof(ScoreGradeText));"));
        var xaml = ReadRepoFile("src/StarMark.UI/Views/SettingsPage.xaml");
        Assert.Equal(1, Count(xaml, "{x:Bind ViewModel.ScoreGradeText, Mode=OneWay}"));
        Assert.Equal(0, Count(xaml, "{x:Bind ViewModel.ScoreGrade,"));            // ⛔ 退回那颗只读属性
    }
}
