#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「这一行没写进去」得当面说出来的闸门（P-64・批次 片197）。
/// <para>现场：隐藏条目页点「还原」，库里那一行写不进去（SQLite 忙／盘上出错）时，界面<b>什么都不说</b>——
/// 行还留在列表里（IJ 轮把"先摘再吞"改成"落库成功才摘"之后，它至少不骗人了），可那句原因只进日志。
/// 同一颗失败在这颗应用里其实早有出口：0 行那一支走的是右下角提示条（<see cref="StateWriteNotice"/>）。</para>
/// <para>裁＝<b>把抛异常那一支也接到同一颗出口上</b>，而⛔ 给这一页再长一行自定义状态位：
/// 同一颗失败在两处宿主各一副样式，等于给下一批留第三处（P-64 原话"不要一页一个自定义提示样式"）。
/// 措辞住在表里（<c>state.writeFailed</c>），交出去的是<b>键＋实参</b>：动作词与那句语言中性的异常正文各占一颗槽。</para>
/// <para>三把尺各问一维：① 表里那一行有三张 voice 且两颗槽都在（少一列＝那一门语言念出键名本身）；
/// ② 每一颗"单行状态写"的宿主（隐藏页 1 颗＋卡片动作 3 颗：隐藏／置顶／笔记）都把这句交给提示条，
/// 且<b>先当面说、再记日志</b>（顺序钉＝防有人把 ShowError 挪到 return 之后）；
/// ③ 运行时三门语言各念一遍，实参进槽而⛔ 被句子吞掉。</para>
/// <para>这一档挂 <c>[Collection("LocaleGlobal")]</c>：③那格会当场换进程级语言，⛔ 与别的换语言的证人交错跑
/// （坑表 #1026 第二次：<see cref="I18nWiringGateTests.EveryLocaleMutatingTestClassIsInTheSerialCollection"/> 当场点名）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class HiddenRestoreNoticeGateTests : IDisposable
{
    private const string HiddenPage = "src/StarMark.UI/ViewModels/HiddenPageViewModel.cs";
    private const string CardActions = "src/StarMark.UI/Helpers/ItemCardActions.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string Notice = "src/StarMark.Abstractions/StateWriteNotice.cs";
    private const string Key = "state.writeFailed";

    private readonly AppLanguage _before = Locale.Current;

    public void Dispose() => Locale.TrySet(_before);

    /// <summary>① 表里那一行：恰一颗、三列都非空、三列都带 <c>{0}</c> 与 <c>{1}</c> 两颗槽。</summary>
    [Fact]
    public void TheWriteFailureSentenceIsInTheTableWithThreeVoicesAndBothSlots()
    {
        var table = Code(ReadRepoFile(Table));
        Assert.Equal(1, Count(table, $"new Entry(\"{Key}\","));

        var m = Regex.Match(table,
            "new Entry\\(\"" + Regex.Escape(Key) + "\",\\s*\"([^\"]*)\",\\s*\"([^\"]*)\",\\s*\"([^\"]*)\"\\)");
        Assert.True(m.Success, $"{Key} 那颗 Entry 读不出三列（写法变了，守门要先跟上）");
        foreach (var column in new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value })
        {
            Assert.False(string.IsNullOrWhiteSpace(column), $"{Key} 有一列是空的");
            Assert.Contains("{0}", column, StringComparison.Ordinal);
            Assert.Contains("{1}", column, StringComparison.Ordinal);
        }
        // 三列⛔ 互相抄：一门语言念另一门的话＝那一列其实是空的
        Assert.NotEqual(m.Groups[1].Value, m.Groups[2].Value);
        Assert.NotEqual(m.Groups[2].Value, m.Groups[3].Value);
    }

    /// <summary>② 每一颗"单行状态写"的宿主都把这句交给提示条——一颗不落（只修页面、留卡片那一半＝症状还在）。</summary>
    [Fact]
    public void EverySingleRowStateWriteSaysTheFailureOnScreen()
    {
        var page = Code(ReadRepoFile(HiddenPage));
        var actions = Code(ReadRepoFile(CardActions));

        Assert.Equal(1, Count(page, $"App.MainWindow?.ShowError(StateWriteNotice.Title(word), StateWriteNotice.WriteFailed(word, ex.Message));"));
        Assert.Equal(3, Count(actions, $"App.MainWindow?.ShowError(StateWriteNotice.Title(word), StateWriteNotice.WriteFailed(word, ex.Message));"));
        // 现读那颗 3＝卡片这一族里三颗单行状态写（隐藏／置顶／笔记），与下面那颗 RowGone 的 3 同一批宿主：
        // 一颗失败两种下落（0 行＝行没了・抛异常＝写不进去），两下的出处各一颗，⛔ 哪一半缺宿主。
        Assert.Equal(3, Count(actions, "StateWriteNotice.RowGone("));
        Assert.Equal(1, Count(page, "StateWriteNotice.RowGone("));
        Assert.Equal(3, Count(actions, "StateWriteNotice.WriteFailed("));
        Assert.Equal(1, Count(page, "StateWriteNotice.WriteFailed("));

        // 禁项：宿主⛔ 自造那句字面量（措辞只许住在表里一颗）
        Assert.DoesNotContain("写不进去", page, StringComparison.Ordinal);
        Assert.DoesNotContain("写不进去", actions, StringComparison.Ordinal);
        Assert.Equal(1, Count(ReadRepoFile(Table), "写不进去"));
    }

    /// <summary>②′ 顺序钉：先当面说、再记日志（把 ShowError 挪到 return 之后＝屏幕上又只剩一句"没反应"）。</summary>
    [Fact]
    public void TheVisibleSentenceComesBeforeTheLogLine()
    {
        foreach (var rel in new[] { HiddenPage, CardActions })
        {
            var src = Code(ReadRepoFile(rel));
            var spoken = src.IndexOf("StateWriteNotice.WriteFailed(", StringComparison.Ordinal);
            var logged = src.IndexOf("StarLog.Error($\"还原隐藏条目失败", StringComparison.Ordinal);
            var logged2 = src.IndexOf("StarLog.Error($\"切换隐藏状态失败", StringComparison.Ordinal);
            Assert.True(spoken >= 0, rel + " 读不到那句可见交代");
            Assert.True(
                (logged < 0 || logged > spoken) && (logged2 < 0 || logged2 > spoken),
                rel + "：日志排在那句交代之前＝这一档的失败又回到只进日志那一形");
        }
    }

    /// <summary>③ 三门语言各念一遍：实参进槽、句子不留花括号。</summary>
    [Fact]
    public void EachLanguageRendersTheActionAndTheReason()
    {
        var reason = "SQLITE_BUSY: database is locked";
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            Assert.Equal(language, Locale.Current);          // ⛔ 拿 TrySet 的返回值当"换没换到"（#112）

            var action = Locale.Fmt("card.restore.word");
            var text = Locale.Fmt(Key, action, reason);
            Assert.DoesNotContain("{", text, StringComparison.Ordinal);
            Assert.DoesNotContain("}", text, StringComparison.Ordinal);
            Assert.Contains(action, text, StringComparison.Ordinal);   // 动作词（还原／Restore…）进槽
            Assert.Contains(reason, text, StringComparison.Ordinal);   // 异常那句中性事实进槽
            said.Add(text);
        }
        Assert.Equal(3, said.Count);                          // 三句互不相同＝真的念了三张 voice
    }

    /// <summary>那颗出口全仓唯一：话的出处只有 <see cref="StateWriteNotice"/>，宿主只递身份。</summary>
    [Fact]
    public void TheSentenceHasOneOwner()
    {
        var notice = Code(ReadRepoFile(Notice));
        Assert.Equal(1, Count(notice, $"LocMessage.Of(\"{Key}\", action, reason)"));
        Assert.Equal(1, Count(notice, "public static LocMessage WriteFailed("));
    }
}
