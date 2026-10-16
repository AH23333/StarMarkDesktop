#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 播放模式那颗悬停说明的形状闸门（批次 WI-71 立尺、本批 WI-72 第一次落到这一族）。
/// <para>
/// <b>P-197 那一族的形状</b>：C# 里 <c>switch</c> 返回中文成品句，再由 <c>{x:Bind}</c> 或代码里那一句
/// <c>ToolTipService.SetToolTip</c> 交给屏幕。它既⛔ 住在语言表里（"表里每行有没有人问"那颗死键闸门问不到它），
/// 也⛔ 写在 XAML 里（扫标记的尺看不见 C#），还被"整句交给 <c>Label</c> 这一层"挡住（数汉字的尺只看那一行有没有把字交出去）
/// ⇒ 三把尺一致判它干净。这颗闸门钉的就是那一格<b>形状</b>。
/// </para>
/// </summary>
public class MusicModeWordGateTests
{
    private const string Mode = "src/StarMark.Core/Media/MusicPlaybackMode.cs";
    private const string Table = "src/StarMark.Abstractions/I18n/UiStrings.cs";
    private const string Widget = "src/StarMark.UI/Views/MusicWidget.xaml.cs";

    /// <summary>真正的「话」＝汉字或假名（全角标点⛔ 算，那颗归 ✅P-196 的连接位那一族）。</summary>
    private static readonly Regex Word = new(@"[぀-ヿ㐀-䶿一-鿿]", RegexOptions.Compiled);

    private static readonly string[] ModeKeys =
    {
        "widget.music.mode.normal",
        "widget.music.mode.shuffle",
        "widget.music.mode.repeat",
    };

    /// <summary>
    /// ① <b>那颗工厂交身份而⛔ 交句子</b>：整档的<b>字符串字面量</b>里一颗汉字／假名也没有。
    /// 同一格里递进一枚<b>正件</b>——同一把尺在农历那一档必须读出汉字，否则"没有"就是"看不见"（名册 289 同族）。
    /// </summary>
    [Fact]
    public void TheModeFactoryHandsOutKeysNotSentences()
    {
        Assert.Empty(WordLiterals(Mode));
        Assert.NotEmpty(WordLiterals("src/StarMark.Core/Widgets/GlanceCalendar.cs"));   // 正件：那档确有"闰／月"
        Assert.Equal(3, SourceGate.Count(SourceGate.Code(SourceGate.ReadRepoFile(Mode)), "=> \"widget.music.mode."));
    }

    /// <summary>
    /// ② <b>三颗键各一行、三列都非空</b>：表里少一行＝那一门语言念出键名本身（坑表 #280 那一族）。
    /// </summary>
    [Fact]
    public void TheThreeModeKeysAreInTheTableWithThreeVoices()
    {
        var table = SourceGate.Code(SourceGate.ReadRepoFile(Table));
        foreach (var key in ModeKeys)
        {
            Assert.Equal(1, SourceGate.Count(table, $"new Entry(\"{key}\","));
            var m = Regex.Match(table,
                "new Entry\\(\"" + Regex.Escape(key) + "\",\\s*\"([^\"]*)\",\\s*\"([^\"]*)\",\\s*\"([^\"]*)\"\\)");
            Assert.True(m.Success, $"{key} 那颗 Entry 读不出三列（写法变了，守门要先跟上）");
            Assert.All(new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value },
                v => Assert.False(string.IsNullOrWhiteSpace(v), $"{key} 有一列是空的"));
        }
    }

    /// <summary>
    /// ③ <b>宿主那一拍交键</b>：说明改由 <c>LocText.SetToolTipKey</c> 贴，Core 那句工厂只交身份。
    /// 出现两次（Core 一次＋宿主一次）＝一颗键⛔ 一个主人；旧写法那颗 <c>SetToolTip(ModeButton, …Label(…))</c> 回来就红。
    /// </summary>
    [Fact]
    public void TheHostAsksForKeyWhileTheButtonStillRepastes()
    {
        var widget = SourceGate.Code(SourceGate.ReadRepoFile(Widget));
        Assert.Equal(1, SourceGate.Count(widget, "LocText.SetToolTipKey(ModeButton, MusicPlaybackModeMath.ModeKey("));
        Assert.Equal(0, SourceGate.Count(widget, "ToolTipService.SetToolTip(ModeButton, MusicPlaybackModeMath"));
        Assert.Equal(1, SourceGate.Count(SourceGate.ReadRepoFile(Mode), "public static string ModeKey("));
    }

    /// <summary>那一档里含汉字／假名的<b>字面量</b>（先抹注释，所以注释里的中文⛔ 算）。</summary>
    private static string[] WordLiterals(string rel)
        => FormatScanner.Scan(SourceGate.ReadRepoFile(rel)).Literals
             .Where(s => Word.IsMatch(s)).ToArray();
}
