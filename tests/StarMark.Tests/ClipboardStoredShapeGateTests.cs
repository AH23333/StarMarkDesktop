#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.I18n;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// ✅P-199（他裁 (A)：库里只存中性形状）的落地证人（片114）。
/// <para>钉五件事：① 写侧 <c>Build</c>/<c>BuildImage</c> 落库的副标题一颗语言词都不剩
/// （四颗词「未知来源/N 行/N 字/N+ 字」交显示侧对空值当场念）；② 显示侧三出口
/// （<c>TitleDisplayOf</c>/<c>SubtitleMessageOf</c>/<c>SubtitleDisplayOf</c>）三语各念一遍，
/// zh 列与旧库形状<b>逐字相同</b>（中文界面零视觉变化）；③ 建贴同语言（交出去的是键＋实参，
/// 语言定死在贴上那一刻）；④ 旧形状行照存词照念（⛔ 迁移历史行），他源行/外部库行不受牵连；
/// ⑤ 四判据（搜索强弱/AI 射程/重复组/健康读数）逐颗证明改完仍判同一件事。</para>
/// <para>这一档挂 <c>[Collection("LocaleGlobal")]</c>：②③会当场换语言，⛔ 与别的换语言的证人交错跑。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class ClipboardStoredShapeGateTests : IDisposable
{
    private static readonly DateTimeOffset T = DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000);

    private const string Entry = "src/StarMark.Abstractions/Clipboard/ClipboardEntry.cs";
    private const string Card = "src/StarMark.UI/ViewModels/ItemCardViewModel.cs";
    private const string SearchWidget = "src/StarMark.UI/ViewModels/SearchWidgetViewModel.cs";
    private const string GridWidget = "src/StarMark.UI/ViewModels/ItemGridWidgetViewModel.cs";
    private const string Glance = "src/StarMark.UI/Views/GlanceWidget.xaml.cs";

    /// <summary>本族 8 颗，逐颗点名（计数型判据抓不到"换名"＝台架规则 #164）。</summary>
    private static readonly string[] Keys =
    [
        "clip.title.image", "clip.sub.unknown", "clip.sub.text1", "clip.sub.text2",
        "clip.sub.lines", "clip.sub.chars", "clip.sub.charsMore", "clip.sub.image",
    ];

    /// <summary>三列同形的<b>纯骨架键</b>（拼接位在 C# 那一侧，语序由整行决定），
    /// 与 <c>clip.usage.full</c> 同族，⛔ 收进"三语读数必须互异"那一问。</summary>
    private static readonly string[] Glue = ["clip.sub.text1", "clip.sub.text2", "clip.sub.image"];

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => Code(ReadRepoFile(rel));

    private static string Quoted(string text) => "\"" + text + "\"";

    private static Item TextRow(string raw, string? app = "Code")
        => ClipboardEntry.Build(raw, app, ClipboardEntry.FormatText, T);

    private static Item ImageRow(string? app = "Code")
        => ClipboardEntry.BuildImage("c-9f2a3b8c", new ClipboardEntry.ImageMeta(
            "a.png", "a_thumb.jpg", 3840, 2160, 4_194_304), app, T);

    // ────────── ① 写侧：库里只存中性形状 ──────────

    /// <summary>
    /// 写侧落库的副标题恒为空串（词交显示侧），标题的中性形状一颗没动：
    /// 首行折叠（FTS 与搜索强弱分段都吃它）与图片行的 <c>W×H</c> 照旧。
    /// </summary>
    [Fact]
    public void WriteSideStoresNoVocabularyInSubtitleOrTitleFallback()
    {
        Assert.Equal(string.Empty, TextRow("abcdefgh").Subtitle);
        Assert.Equal(string.Empty, TextRow("a1\nb2\nc3", app: null).Subtitle);
        Assert.Equal(string.Empty, TextRow(new string('m', ClipboardPolicy.MaxStoredChars + 10)).Subtitle);
        Assert.Equal(string.Empty, ImageRow().Subtitle);
        Assert.Equal(string.Empty, ImageRow(app: null).Subtitle);

        var raw = "第一行标题\r\n第二行";
        var normalized = ClipboardPolicy.NormalizeText(raw);
        var body = ClipboardPolicy.Truncate(normalized, out _);
        Assert.Equal(ClipboardPolicy.BuildTitle(body), TextRow(raw).Title);
        Assert.Equal("3840×2160", ImageRow().Title);
    }

    // ────────── ② 显示侧：三语各念一遍，zh 与旧库形状逐字相同 ──────────

    /// <summary>
    /// 四颗词由显示出口在<b>贴上那一刻的语言</b>里念出来；zh 那三发与旧写侧冻进库里的形状
    /// 逐字相同——中文界面零视觉变化是这一批的回归底线。
    /// </summary>
    [Fact]
    public void DisplaySideSpeaksAllFourWordsInThePastingLanguage()
    {
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal("Code · 8 字", ClipboardEntry.SubtitleDisplayOf(TextRow("abcdefgh")));
        Assert.Equal("未知来源 · 3 行 · 8 字", ClipboardEntry.SubtitleDisplayOf(TextRow("a1\nb2\nc3", app: null)));
        Assert.Equal("Code · 3840×2160 · 4 MB", ClipboardEntry.SubtitleDisplayOf(ImageRow()));
        Assert.Equal("未知来源 · 3840×2160 · 4 MB", ClipboardEntry.SubtitleDisplayOf(ImageRow(app: null)));

        Locale.TrySet(AppLanguage.English);
        Assert.Equal("Code · Chars: 8", ClipboardEntry.SubtitleDisplayOf(TextRow("abcdefgh")));
        Assert.Equal("Unknown source · Lines: 3 · Chars: 8",
            ClipboardEntry.SubtitleDisplayOf(TextRow("a1\nb2\nc3", app: null)));
        // 图片那一发三语同形是刻意的：来源/几何/体积全是中性事实，骨架「·」三列同形（Glue 那颗）。
        Assert.Equal("Code · 3840×2160 · 4 MB", ClipboardEntry.SubtitleDisplayOf(ImageRow()));

        Locale.TrySet(AppLanguage.Japanese);
        Assert.Equal("Code · 8 文字", ClipboardEntry.SubtitleDisplayOf(TextRow("abcdefgh")));
        Assert.Equal("取得元不明 · 3 行 · 8 文字",
            ClipboardEntry.SubtitleDisplayOf(TextRow("a1\nb2\nc3", app: null)));
    }

    /// <summary>
    /// 截断那一发：行数读 <c>clipLines</c>、字数读 <c>clipFullLength</c>——正文只剩前 32 KB，
    /// 数正文数不出原文的行数（数出来是错的，比缺更糟）。
    /// </summary>
    [Fact]
    public void TruncatedRowsReadTheNeutralCountKeysNotTheBody()
    {
        var raw = string.Join("\n", Enumerable.Range(1, 9).Select(i => new string('m', ClipboardPolicy.MaxStoredChars / 3)));
        var item = TextRow(raw);
        Assert.True(ClipboardEntry.IsTruncated(item));

        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal($"Code · 9 行 · {raw.Length}+ 字", ClipboardEntry.SubtitleDisplayOf(item));
        Locale.TrySet(AppLanguage.English);
        Assert.Equal($"Code · Lines: 9 · Chars: {raw.Length}+", ClipboardEntry.SubtitleDisplayOf(item));
    }

    /// <summary>
    /// 空标题图片行（手改过 extra 的防御臂）：当年那颗「图片」兜底的替代口——当场念键，
    /// ⛔ 冻进 <c>items.title</c>。真实采集链恒有尺寸（<c>MinImageEdge</c> 门禁），标题从不为空。
    /// </summary>
    [Fact]
    public void EmptyTitleImageRowSpeaksTheKeyInThePastingLanguage()
    {
        var handMade = new Item
        {
            Type = ItemType.Clipboard,
            Source = ItemSources.Clipboard,
            Title = string.Empty,
            ExtraJson = "{\"clipFormat\":\"image\"}",
        };
        Locale.TrySet(AppLanguage.Chinese);
        Assert.Equal("图片", ClipboardEntry.TitleDisplayOf(handMade));
        Locale.TrySet(AppLanguage.English);
        Assert.Equal("Image", ClipboardEntry.TitleDisplayOf(handMade));
        Assert.Equal("3840×2160", ClipboardEntry.TitleDisplayOf(ImageRow()));
    }

    // ────────── ③ 建贴同语言：交出去的是键＋实参 ──────────

    /// <summary>
    /// 那颗 <see cref="LocMessage"/> 在算出来的那一刻<b>不带语言</b>：贴上去那一刻说什么语言，
    /// 屏幕上就是什么语言——谁把取词搬进算的那一刻，这格就红（坑表 #276／#393 那一族）。
    /// </summary>
    [Fact]
    public void TheMessageRendersInTheLanguageSpokenAtPasteTime()
    {
        var multi = TextRow("a1\nb2\nc3", app: null);
        Locale.TrySet(AppLanguage.Chinese);
        var frozen = ClipboardEntry.SubtitleMessageOf(multi);          // 算的那一刻＝中文
        Locale.TrySet(AppLanguage.English);
        Assert.Equal("Unknown source · Lines: 3 · Chars: 8", frozen!.Render());   // 贴的那一刻＝英文
        Locale.TrySet(AppLanguage.Japanese);
        Assert.Equal("取得元不明 · 3 行 · 8 文字", frozen!.Render());           // 再贴一次＝日文

        // 存词行同样不许在算的那一刻定死语言（common.rawValue 只是透传，不取词）：
        var legacy = new Item
        {
            Type = ItemType.Clipboard, Source = ItemSources.Clipboard,
            Subtitle = "旧形状 · 3 行 · 6 字", ExtraJson = "{}",
        };
        Locale.TrySet(AppLanguage.English);
        Assert.Equal("旧形状 · 3 行 · 6 字", ClipboardEntry.SubtitleMessageOf(legacy)!.Render());
    }

    // ────────── ④ 旧形状行照念（⛔ 迁移）；他源行/外部库行不受牵连 ──────────

    /// <summary>
    /// 旧形状行的副标题是<b>用户数据</b>：三语下都照原文念，不重算、不追改——
    /// 「⛔ 迁移历史行」那条红线。他源行（书签/Star）照原文，空的他源行交 null（调用方自己决定）；
    /// Ditto 行（type 相同但库是别人的）⛔ 当场算——外部库的词不归我们念。
    /// </summary>
    [Fact]
    public void LegacyRowsKeepTheirStoredWordsAndForeignRowsAreUntouched()
    {
        var legacy = new Item
        {
            Type = ItemType.Clipboard, Source = ItemSources.Clipboard,
            Subtitle = "chrome · 3 行 · 6 字",
            ExtraJson = "{\"clipFormat\":\"text\",\"clipApp\":\"chrome\"}",
        };
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            Assert.Equal("chrome · 3 行 · 6 字", ClipboardEntry.SubtitleDisplayOf(legacy));
        }

        var bookmark = new Item { Type = ItemType.Bookmark, Source = ItemSources.Chrome, Subtitle = "github.com" };
        Assert.Equal("github.com", ClipboardEntry.SubtitleDisplayOf(bookmark));

        var foreign = new Item { Type = ItemType.Bookmark, Source = ItemSources.Chrome, Subtitle = string.Empty };
        Assert.Null(ClipboardEntry.SubtitleMessageOf(foreign));

        var ditto = new Item { Type = ItemType.Clipboard, Source = ItemSources.Ditto, Subtitle = string.Empty };
        Assert.Null(ClipboardEntry.SubtitleMessageOf(ditto));
    }

    // ────────── ⑤ 四判据：改完仍判同一件事 ──────────

    /// <summary>
    /// 四颗判据（搜索强弱分段／AI 整理射程／重复组键／健康页重复读数）吃的全是
    /// <see cref="Item.Title"/> 而⛔ 吃 Subtitle——副标题从来不进 <c>search_text</c>
    /// （title+description+notes+标签），也不进重复组键。写侧这一批只动过 Subtitle（空串）与
    /// 图片空标题兜底：①标题逐字节没动（四判据的输入不变）；②那三处判据代码从不读 Subtitle
    /// （搬运后也不会开始读）；③AI 射程的排除还在（<c>InScope</c> 刻意不含 Clipboard）。
    /// </summary>
    [Fact]
    public void TheFourJudgmentsStillJudgeTheSameThing()
    {
        var raw = "第一行标题\r\n第二行";
        var normalized = ClipboardPolicy.NormalizeText(raw);
        var body = ClipboardPolicy.Truncate(normalized, out _);
        Assert.Equal(ClipboardPolicy.BuildTitle(body), TextRow(raw).Title);   // 判据①③④的输入一颗没变
        Assert.Equal("3840×2160", ImageRow().Title);

        Assert.Equal(0, Count(CodeOf("src/StarMark.Core/Search/SearchService.cs"), ".Subtitle"));
        Assert.Equal(0, Count(CodeOf("src/StarMark.Core/Insights/InsightsService.cs"), ".Subtitle"));
        Assert.Equal(0, Count(CodeOf("src/StarMark.Core/Ai/ClassifyRules.cs"), ".Subtitle"));

        var scope = MethodBody(CodeOf("src/StarMark.Core/Ai/ClassifyRules.cs"), "InScope =");
        Assert.DoesNotContain("Clipboard", scope, StringComparison.Ordinal);   // 判据②：射程排除还在
    }

    // ────────── ⑥ 表键名单双向对账 ──────────

    /// <summary>
    /// 8 颗每颗在表里恰好一行、在 <see cref="ClipboardEntry"/> 里恰好点名一次；
    /// <b>反方向</b>也钉：表里 <c>clip.title.*</c>／<c>clip.sub.*</c> 的每一行都得有人问。
    /// 卡片那颗自己的兜底键由 VM 用构造时算好的 <c>IsClipboardImageRow</c> 判（⛔ 再解一遍 extra_json）。
    /// </summary>
    [Fact]
    public void EveryKeyIsDeclaredOnceAndNamedExactlyOnce()
    {
        Assert.Equal(8, Keys.Length);
        Assert.Equal(Keys.Length, Keys.Distinct(StringComparer.Ordinal).Count());

        var entry = CodeOf(Entry);
        foreach (var key in Keys)
        {
            Assert.Single(UiStrings.All, e => e.Key == key);
            Assert.Equal(1, Count(entry, Quoted(key)));
        }
        Assert.Equal(1, Count(CodeOf(Card), Quoted("clip.title.image")));

        var asked = Keys.ToHashSet(StringComparer.Ordinal);
        var unasked = UiStrings.All.Select(e => e.Key)
            .Where(k => k.StartsWith("clip.title.", StringComparison.Ordinal)
                        || k.StartsWith("clip.sub.", StringComparison.Ordinal))
            .Where(k => !asked.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.True(unasked.Count == 0, "表里多了没人问的行：" + string.Join("、", unasked));
    }

    /// <summary>
    /// 每一颗：三列都非空、非骨架键三语互异、三语下真取一次词（槽全填满、⛔ 把身份念给用户听）；
    /// 骨架键三列同形逐字相等。四颗带槽键的实参颗数＝槽数（少递一颗＝屏幕上整空白，坑表 #393）。
    /// </summary>
    [Fact]
    public void EveryRowAnswersInThreeLanguagesAndFillsItsSlots()
    {
        foreach (var key in Keys)
        {
            Assert.True(UiStrings.TryGet(key, out var entry), "表里没有 " + key);
            var texts = new[] { entry.Zh, entry.En, entry.Ja };
            foreach (var text in texts) Assert.False(string.IsNullOrWhiteSpace(text), key + " 有一列是空的");

            var slots = RowSlots(key);
            if (Glue.Contains(key))
            {
                // 三列同形是刻意的：骨架「·」写在表列里（⛔ 在 C# 里拼——分隔符也是语言内容），
                // 与 clip.usage.full 那类<b>纯拼接</b>行不同，这里三列带着同一副骨架，⛔ 收进"三语互异"。
                foreach (var text in texts) Assert.Equal(texts[0], text);
                for (var i = 0; i < slots; i++)
                    Assert.Contains("{" + i + "}", texts[0], StringComparison.Ordinal);
                continue;
            }
            Assert.NotEqual(texts[0], texts[1]);
            Assert.NotEqual(texts[1], texts[2]);

            var args = Enumerable.Range(0, slots).Select(i => (object)("M" + i)).ToArray();
            var said = new List<string>();
            foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
            {
                Locale.TrySet(language);
                Assert.Equal(language, Locale.Current);
                var text = slots == 0 ? Locale.T(key) : Locale.Fmt(key, args);
                Assert.DoesNotContain("{", text, StringComparison.Ordinal);
                Assert.DoesNotContain("}", text, StringComparison.Ordinal);
                Assert.DoesNotContain("clip.", text, StringComparison.Ordinal);   // ⛔ 把身份念给用户听
                if (slots > 0) Assert.Contains("M0", text, StringComparison.Ordinal);
                said.Add(text);
            }
            Assert.NotEqual(said[0], said[1]);
            Assert.NotEqual(said[1], said[2]);
        }

        // 实参颗数＝槽数：嵌套套娃那一发（clip.sub.text2 套 clip.sub.lines）由 KeyedCallSites 按括号深度走。
        var offenders = new List<string>();
        var family = Keys.ToHashSet(StringComparer.Ordinal);
        var watched = 0;
        foreach (var (key, args, line) in KeyedCallSites(CodeOf(Entry)))
        {
            if (!family.Contains(key)) continue;
            watched++;
            if (args != RowSlots(key)) offenders.Add($"ClipboardEntry.cs:{line} 的 {key} 表里 {RowSlots(key)} 颗槽，代码递了 {args} 颗实参");
        }
        Assert.Empty(offenders);
        Assert.Equal(5, watched);   // 8 颗里 5 颗以字面键递进 Of(；chars/charsMore 是三元选键、title.image 在 Locale.T——那三颗由上面的渲染证人逐颗钉
    }

    // ────────── ⑦ 出口 census：每个显示宿主都走共享出口 ──────────

    /// <summary>
    /// 卡片那一族全走 <see cref="ItemCardViewModel"/>（构造时算一次）；三个非卡片出口
    /// （搜索组件三处、条目格一处、今日速览一处）各自接线。写侧那对同族函数已删——
    /// 重命名回归会在这里红。数字⛔ 手打（规则 #476）。
    /// </summary>
    [Fact]
    public void EveryDisplayHostUsesTheSharedExits()
    {
        Assert.Equal(1, Count(CodeOf(Card), "ClipboardEntry.SubtitleMessageOf("));
        Assert.Equal(3, Count(CodeOf(SearchWidget), "ClipboardEntry.SubtitleDisplayOf("));
        Assert.Equal(1, Count(CodeOf(GridWidget), "ClipboardEntry.SubtitleDisplayOf("));
        Assert.Equal(1, Count(CodeOf(Glance), "ClipboardEntry.SubtitleDisplayOf("));

        var entry = CodeOf(Entry);
        Assert.Equal(0, Count(entry, "BuildSubtitle"));
        Assert.Equal(0, Count(entry, "BuildImageSubtitle"));
    }

    // ────────── ⑧ 改口读数：假承诺撤回 ──────────

    /// <summary>
    /// ✅P-199 改口：三处 doc 许过的「状态行」出口从未存在（那几发 reason 只进日志——
    /// <c>ClipboardNative.ReadImage</c> 的调用点把它们交给 <c>StarLog.Info</c>）。
    /// 假承诺按原文撤回；<c>ClipboardPolicy</c> 里那三处<b>真</b>状态行（ClipboardPage 的话）不在射程，照留。
    /// </summary>
    [Fact]
    public void TheFalseStatusRowPromisesAreRetracted()
    {
        foreach (var rel in new[]
                 {
                     "src/StarMark.Abstractions/Clipboard/ClipboardPolicy.cs",
                     "src/StarMark.Abstractions/Clipboard/ClipboardPayload.cs",
                     "src/StarMark.Abstractions/Clipboard/ClipboardPayload.Png.cs",
                 })
        {
            Assert.Equal(0, Count(ReadRepoFile(rel), "状态行与日志"));
        }
    }
}
