#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.I18n;
using StarMark.Core.Widgets;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 布局／快照<b>名字这一列</b>「库里那一颗 ↔ 屏幕上那一句」的证人（批次 WI-67 片6，✅P-200 他裁 (A)＝中性化、显示侧念键）。
/// <para>为什么这一族⛔ 由 <c>WidgetLayoutTests</c>／<c>WidgetSnapshotTests</c> 顺手钉：那两档量的是<b>写侧</b>
/// （库里存了什么形状），这里量的是<b>读侧</b>（同一列念成什么、两颗念法会不会撞）。
/// 撞名那一刻⛔ 有任何一颗字落进档——只有这一档看得见它。</para>
/// </summary>
[Collection("LocaleGlobal")]      // 本格会当场换语言（三语各渲染一遍），⛔ 与别的换语言的证人交错跑（坑表 #517）
public sealed class WidgetDisplayNameTests : IDisposable
{
    private readonly AppLanguage _languageBefore = Locale.Current;

    /// <summary>跑完把语言<b>还回去</b>：测试是顺序跑的，上一档留在哪一门，下一档就读哪一门（名册 241 的姊妹面）。</summary>
    public void Dispose() => Locale.TrySet(_languageBefore);

    [Fact]
    public void Neutralize_KeepsOnlyWhatTheUserTyped()
    {
        Assert.Equal("", WidgetDisplayName.Neutralize(null));
        Assert.Equal("", WidgetDisplayName.Neutralize("   "));
        Assert.Equal("工作模式", WidgetDisplayName.Neutralize(" 工作模式 "));
    }

    /// <summary>读侧交的是「键＋裸实参」，三档形状各有各的键：兜底词／用户字面／带序号的兜底词。</summary>
    [Fact]
    public void Of_HandsKeyAndBareArgsForTheThreeShapes()
    {
        var blank = WidgetDisplayName.Of("", 1, WidgetDisplayName.LayoutFallbackKey);
        Assert.Equal("widget.name.layoutFallback", blank.Key);
        Assert.Null(blank.Args);                                   // 第一颗不带序号 ⇒ ⛔ 在这里备一副空括号

        var typed = WidgetDisplayName.Of("工作模式", 1, WidgetDisplayName.LayoutFallbackKey);
        Assert.Equal("common.rawValue", typed.Key);                // 用户自己打的字走表里既有的<b>原样出口</b>
        Assert.Equal(new object?[] { "工作模式" }, typed.Args);

        var second = WidgetDisplayName.Of("", 2, WidgetDisplayName.SnapshotFallbackKey);
        Assert.Equal("widget.name.dedupSuffix", second.Key);
        Assert.Equal("widget.name.snapshotFallback", ((LocMessage)second.Args![0]!).Key);
        Assert.Equal(2, (int)second.Args![1]!);                    // 序号交<b>裸实参</b>，⛔ 先拼成 " (2)"
    }

    /// <summary>
    /// 序号只由 <see cref="WidgetDisplayName.Assign"/> 按<b>整列</b>发（第一颗⛔ 带后缀）。
    /// 那一列若改由两位宿主各数一套，设置页与组件右键就把同一颗念成两个名字。
    /// </summary>
    [Fact]
    public void Assign_NumbersEachGroupFromTheSecondItemOn()
    {
        var words = WidgetDisplayName.Assign(
            new string?[] { "", null, "甲", "甲", "乙" }, WidgetDisplayName.LayoutFallbackKey);

        Assert.Equal(new[] { "widget.name.layoutFallback", "widget.name.dedupSuffix",
                             "common.rawValue", "widget.name.dedupSuffix", "common.rawValue" },
            words.Select(w => w.Key).ToArray());
        var secondWord = words[1];                                       // 空串与 null 同组：第二颗起才发序号
        Assert.Equal(WidgetDisplayName.LayoutFallbackKey, ((LocMessage)secondWord.Args![0]!).Key);
        Assert.Equal(2, (int)secondWord.Args![1]!);
        Assert.Equal("甲", ((LocMessage)words[3].Args![0]!).Render());          // 括号里那颗仍是<b>未取词的</b>句子
        Assert.Equal("乙", words[4].Render());                                  // 独一颗⛔ 被顺延
    }

    /// <summary>
    /// 三语各念一遍：同列两颗"没起名"要念成两句不同的话，而<b>用户真打了与兜底词同一颗字</b>的那颗也要分开——
    /// ⛔ 靠写侧防（库里那两行都是空串，一模一样），只在读侧成立。
    /// </summary>
    [Fact]
    public void BlankAndLiteralFallbackWordStayDistinctInAllThreeLanguages()
    {
        foreach (var lang in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(lang);
            Assert.Equal(lang, Locale.Current);                   // TrySet 回的是"变没变"，⛔"换没换到"（规则 #112）

            var literal = Locale.T(WidgetDisplayName.LayoutFallbackKey);   // 用户<b>真打了</b>那颗词（按当时语言取）
            var onScreen = WidgetDisplayName
                .Assign(new string?[] { "", "", literal }, WidgetDisplayName.LayoutFallbackKey)
                .Select(w => w.Render()).ToArray();

            Assert.True(onScreen.All(w => w.Length > 0), $"{lang} 那三颗念出来有空串");
            Assert.Equal(3, onScreen.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(" (2)", onScreen[1], StringComparison.Ordinal);      // 括号三语同形（半角＋一个空格）
        }
    }

    /// <summary>
    /// 分组键用 <c>OrdinalIgnoreCase</c>：库里 "Foo" 与 "foo" 是同一颗名字（写侧那颗碰撞集从来就这么认），
    /// 读侧若改成大小写敏感，屏幕上就会出现两行只差大小写的"不同"名字而写侧⛔ 拦不到（与 DJ／DR 同一把透镜）。
    /// </summary>
    [Fact]
    public void Assign_GroupsCaseInsensitiveLikeTheWriterDoes()
    {
        var words = WidgetDisplayName.Assign(new[] { "Foo", "foo" }, WidgetDisplayName.LayoutFallbackKey);
        Assert.Equal("common.rawValue", words[0].Key);
        Assert.Equal("widget.name.dedupSuffix", words[1].Key);
        Assert.Equal(2, (int)words[1].Args![1]!);
    }

    /// <summary>那一列的键＝<c>Id</c>：名字从这一版起可以重复（两行空串合法），⛔ 再拿名字当键。</summary>
    [Fact]
    public void ForLayoutsAndSnapshots_KeyByIdSoTwoBlankNamesBothGetAWord()
    {
        var words = WidgetDisplayName.ForLayouts(new[]
        {
            new WidgetLayout { Id = "a", Name = "" },
            new WidgetLayout { Id = "b", Name = "  " },
        });
        Assert.Equal(2, words.Count);
        Assert.Equal("widget.name.layoutFallback", words["a"].Key);
        Assert.Equal("widget.name.dedupSuffix", words["b"].Key);

        var snapWords = WidgetDisplayName.ForSnapshots(new[]
        {
            new WidgetSnapshot { Id = "s1", Name = "点" },
            new WidgetSnapshot { Id = "s2", Name = "点" },
        });
        Assert.Equal("widget.name.dedupSuffix", snapWords["s2"].Key);
    }

    /// <summary>
    /// 认不到的 Id <b>退回"不带序号"而⛔ 抛</b>：保存回执那一刻正是"这一列刚被入口自己改过"的时刻，
    /// 把一次成功的保存抛成一条"失败"提示＝对用户说了假话。
    /// </summary>
    [Fact]
    public void Word_FallsBackInsteadOfThrowingForAnUnknownId()
    {
        var words = WidgetDisplayName.ForLayouts(new[] { new WidgetLayout { Id = "known", Name = "甲" } });
        var fresh = new WidgetLayout { Id = "brand-new", Name = "" };

        var word = words.Word(fresh.Id, fresh.Name, WidgetDisplayName.LayoutFallbackKey);
        Assert.Equal(WidgetDisplayName.LayoutFallbackKey, word.Key);
        Assert.Null(word.Args);

        // 认得到的那颗照旧走表里算好的序号（⛔ 兜底臂把正路一起盖掉）。
        Assert.Equal("common.rawValue", words.Word("known", "甲", WidgetDisplayName.LayoutFallbackKey).Key);
    }
}
