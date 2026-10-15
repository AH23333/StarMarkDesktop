#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 设置页的<b>摆位</b>守门（批次 WI-67 片13）。分类闸门管"这张卡住在哪一页"，这一族管
/// <b>卡里的名字与它能用的那颗控件有没有落在同一行上</b>——两处都是用户点名的观感缺陷，
/// 而编译器与 XAML 解析器对它们一律不出声：
/// <para><b>①</b> 带 <c>ColumnDefinitions</c> 的 Grid 里一颗元素没写 <c>Grid.Column</c> ⇒ 它掉进<b>第 0 列</b>，
/// 与那列上本来的邻居<b>重叠</b>。量到过：「Ollama 地址」输入格 327x56+790+464，而一句没落格的标题
/// 同为 790+464 起点、327x48 正好把它整个盖住。源码上只是"少写一个属性"，屏幕上就是一处糊掉的字。</para>
/// <para><b>②</b> 只有一格设置的卡把抬头留在 <c>StackPanel</c> 里、控件丢进下面那颗两列 Grid 的右格 ⇒
/// 左格空着，名字单独占一行。量到过：「主题」659x19+95+313 与它的下拉 188x40+570+346；
/// 「界面语言」659x19+790+313 与 188x40+1265+346。这一页 42 颗两列 Grid 里只有这两颗左格是空的，
/// 其余多行卡的抬头本就管着一排行，不算错位。</para>
/// <para>测试工程引用不到 <c>StarMark.UI</c>，所以这两条只能钉<b>源码形状</b>；屏幕上到底对不对齐，
/// 由 <c>bin/WI67/dump8_*</c> 那几份 UIA 转储按矩形量（同一批的读数）。</para>
/// </summary>
public sealed class SettingsLayoutGateTests
{
    private const string Xaml = "src/StarMark.UI/Views/SettingsPage.xaml";

    /// <summary>注释会替被改掉的摆位答题（#228 那一族：注释里写着一模一样的两行标记，闸门照样绿）。
    /// <para>抹掉时<b>把行数留下</b>：这一族的失败信息报的是行号，整段换空串会让行号指向别处（读数没错、地址错）。</para></summary>
    private static string Markup(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", m => new string('\n', m.Value.Length - m.Value.Replace("\n", string.Empty).Length), RegexOptions.Singleline);

    /// <summary>① <b>页内每一颗有列定义的 Grid，它的直接孩子都得自己说清落在哪一列</b>。
    /// <para>整档扫而⛔ 只点本批那两格的名：这一条坏掉的样子是"往后多加一颗元素忘了带列号"，
    /// 而下一次忘记的人不会正好忘在同一颗上。</para></summary>
    [Fact]
    public void NoElementOnTheSettingsPageLandsInAColumnWithoutNamingIt()
    {
        var offenders = new List<string>();
        foreach (var row in Rows(Markup(ReadRepoFile(Xaml))))
            foreach (var kid in row.Children)
                if (!kid.Attrs.Contains("Grid.Column="))
                    offenders.Add($"L{kid.Line} <{kid.Name}>（父 Grid 起于 L{row.Line}，列定义={row.Cols}）");

        Assert.True(offenders.Count == 0,
            "这些元素在有 ColumnDefinitions 的 Grid 里没写 Grid.Column＝掉进第 0 列、与那列的邻居重叠：\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>② 那两张"只有一个控件"的卡：名字与下拉同一行，名字在左格、下拉在右格。</summary>
    [Fact]
    public void TheThemeAndLanguageNamesSitInTheSameRowAsTheirPickers()
    {
        var rows = Rows(Markup(ReadRepoFile(Xaml)));
        foreach (var (combo, key) in new[]
                 {
                     ("ThemeSelect", "settings.general.themeTitle"),
                     ("LanguageSelect", "settings.languageTitle"),
                 })
        {
            var mine = rows.FindAll(r => r.Children.Exists(k => k.Name == "ComboBox" && k.Attrs.Contains("\"" + combo + "\"")));
            // 一颗控件只该作为<b>直接孩子</b>出现在它那一层；数出两颗以上＝上面那把尺把深度算错了。
            Assert.Single(mine);
            var row = mine[0];

            // 名字与列号必须落在<b>同一颗</b>元素上：把键留在行里、列号写到别的孩子上，分开数会一起绿。
            var labels = row.Children.FindAll(k => k.Name == "TextBlock" && k.Attrs.Contains("Grid.Column=\"0\""));
            Assert.Single(labels);
            Assert.Contains(key, labels[0].Attrs, StringComparison.Ordinal);
            Assert.Contains("Grid.Column=\"1\"",
                row.Children.FindAll(k => k.Name == "ComboBox").Single().Attrs, StringComparison.Ordinal);
        }
    }

    private sealed record Kid(string Name, string Attrs, int Line);

    private sealed record Row(string Cols, int Line, List<Kid> Children);

    /// <summary>每一颗<b>带列定义</b>的 Grid 与它的<b>直接</b>子元素。
    /// <para>⛔ 用"从起始标记到下一颗 <c>&lt;/Grid&gt;</c> 的那段文本"来数：嵌套的 Grid 会把段落切错，
    /// 于是"某颗孩子没落格"这件事会被算到别人头上。这里按起始/结束标记自己维护深度。</para></summary>
    private static List<Row> Rows(string xaml)
    {
        var done = new List<Row>();
        var stack = new Stack<Frame>();
        var line = 1;
        var scanned = 0;
        foreach (Match m in Regex.Matches(xaml, "<(/?[\\w.]+)([^>]*?)(/?)>", RegexOptions.Singleline))
        {
            for (var i = scanned; i < m.Index; i++) if (xaml[i] == '\n') line++;
            scanned = m.Index;

            var name = m.Groups[1].Value;
            var attrs = m.Groups[2].Value;
            if (name.StartsWith("/"))
            {
                var closed = name.Substring(1);
                if (stack.Count > 0 && stack.Peek().Name == closed) Emit(stack.Pop(), done);
                continue;
            }
            // 属性元素（<c>Grid.ColumnDefinitions</c> 那一族）⛔ 算成"这一格的孩子"，
            // 而它自己的孙子（<c>ColumnDefinition Width=…</c>）也⛔ 往上归给那颗 Grid ⇒ 单独占一层，出了它就丢。
            if (!name.Contains('.') && stack.Count > 0) stack.Peek().Children.Add(new Kid(name, attrs, line));

            string? cols = null;
            if (name == "Grid")
            {
                var cm = Regex.Match(attrs, "ColumnDefinitions=\"([^\"]*)\"");
                if (cm.Success) cols = cm.Groups[1].Value;
            }
            var frame = new Frame(name, cols, line, new List<Kid>());
            stack.Push(frame);
            if (m.Groups[3].Value == "/") Emit(stack.Pop(), done);
        }
        return done.FindAll(r => r.Cols.Length > 0);
    }

    private static void Emit(Frame frame, List<Row> sink)
    {
        if (frame.Name.Contains('.')) return;
        sink.Add(new Row(frame.Cols ?? string.Empty, frame.Line, frame.Children));
    }

    private sealed class Frame(string name, string? cols, int line, List<Kid> children)
    {
        public string Name { get; } = name;
        public string? Cols { get; } = cols;
        public int Line { get; } = line;
        public List<Kid> Children { get; } = children;
    }
}
