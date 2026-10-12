#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using StarMark.Abstractions.Clipboard;
using StarMark.Abstractions.I18n;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 「清理孤儿文件」那只确认框的<b>正文</b>只许住在文案表里（批次 WI-64 片2，✅P-193 收官的那一颗）。
/// <para>这一族与片1 那两行同源而形状不同：正文是<b>一颗胶水键套一串身份</b>——屏幕上那一整块由
/// 「目录那句／N 行名字／其余几颗没列出／合计／临时件／尾句」六类话拼成，而行与行之间那根换行<b>是布局</b>：
/// ⛔ 抄进表（规则 #100），也⛔ 因此把整块退成 <c>string</c>（那就在 Abstractions 里按<b>当时</b>的语言念完了，
/// 换语言后屏幕上还停着上一门那一句＝ P-194 那一族）。</para>
/// <para>所以除了片1 那几问（字在不在表里、颗数、槽数、每一行都有人问），这里多两问：
/// ⑦ <b>行结构</b>——哪一句落在第几行、尾句之前那一颗空行，它是那根换行的化身，形状尺对它完全失明；
/// ⑧ <b>整棵⛔ 一颗成品字</b>——把交回来的 <see cref="LocMessage"/> 走一遍，实参里不许出现含汉字的东西。</para>
/// <para>⚠ 射程：<c>ClipAssets.DescribeTitle</c>（卡片标题那颗兜底字）与 <c>ClipboardEntry.BuildSubtitle</c>
/// 由 ✅WI-64c 判归 <b>P-199</b>（成品字写进 <c>items.title</c>／<c>items.subtitle</c> 落库，⛔ 是瞬时面），
/// <c>ClipboardPolicy.ShouldRecordImage</c> 那四发拒绝理由只进日志（日志按既定口径⛔ 交表）——两样都⛔ 在本批。</para>
/// <para>⚠ 「0 颗孤儿且 0 个临时件」那一发⛔ 到得了屏幕（VM 在弹框之前就 <c>return</c> 了）。旧写法在那一发
/// 留一颗前导换行、新写法不留——那是这颗函数唯一一处<b>有意的</b>形状差，登记在此而⛔ 为它写断言
/// （验不了的分支⛔ 当测过＝既定口径）。</para>
/// </summary>
[Collection("LocaleGlobal")]
public sealed class ClipboardCleanupBodyWordGateTests : IDisposable
{
    private const string Assets = "src/StarMark.Abstractions/Clipboard/ClipAssets.cs";
    private const string ViewModel = "src/StarMark.UI/ViewModels/SettingsPageViewModel.Clipboard.cs";
    private static readonly string[] CodeFiles = [Assets, ViewModel];

    /// <summary>本族 7 颗，逐颗点名（计数型判据抓不到"换名"＝台架规则 #164）。</summary>
    private static readonly string[] Keys =
    [
        "clip.clean.head", "clip.clean.row", "clip.clean.more", "clip.clean.total",
        "clip.clean.temps", "clip.clean.tail", "clip.clean.lineJoin",
    ];

    /// <summary>纯胶水行：三列同形是刻意的（拼接位在 C# 那一侧），与 <c>settings.data.backup.lineJoin</c>／<c>clip.usage.line</c> 同族。</summary>
    private static readonly string[] Glue = ["clip.clean.lineJoin"];

    /// <summary>记号行：那一行只有「· 」＋一个文件名。中英同用中点 <c>·</c>，日文用片假名中点 <c>・</c> ⇒
    /// <b>zh==en 是刻意的</b>（⛔ 为了凑"三语互异"去换英文那个点，那一行就长出第二份排版判据）。</summary>
    private static readonly string[] Marker = ["clip.clean.row"];

    private readonly AppLanguage _before = Locale.Current;
    public void Dispose() => Locale.TrySet(_before);

    private static string CodeOf(string rel) => Code(ReadRepoFile(rel));

    private static string Quoted(string text) => "\"" + text + "\"";

    /// <summary>那一行按槽位顺序填上实参（只用于本档自己的读数，⛔ 拿它当"表里的措辞"）。</summary>
    private static string Filled(string key, params object[] args)
        => LocMessage.Of(key, args.Select(a => (object?)a).ToArray()).Render();

    /// <summary>整块正文在<b>当下这一拍的语言</b>里念出来的样子。</summary>
    private static string Body(IReadOnlyList<string> names, long nameBytes, int temps, long tempBytes, string folder)
        => ClipAssets.CleanupConfirmBody(names, nameBytes, temps, tempBytes, folder).Render();

    /// <summary>比上限还多三颗的那一发：截断那一行必须出现，而列出的行数⛔ 许超过上限。</summary>
    private static readonly string[] OverLimit =
        Enumerable.Range(0, ClipAssets.CleanupPreviewLimit + 3).Select(i => $"2026-10-12_0915_{i:x8}.png").ToArray();

    private const string Folder = @"D:\StarMark\clip";

    /// <summary>那一发形状<b>应当</b>念出来的那几行：逐行由表里那七颗键在当下语言里拼出来（⛔ 抄措辞）。</summary>
    private static string[] Expected(IReadOnlyList<string> names, long nameBytes, int temps, long tempBytes)
    {
        var shown = Math.Min(ClipAssets.CleanupPreviewLimit, names.Count);
        var lines = new List<string>();
        if (shown > 0)
        {
            lines.Add(Filled("clip.clean.head", Folder));
            for (var i = 0; i < shown; i++) lines.Add(Filled("clip.clean.row", names[i]));
            if (names.Count > shown) lines.Add(Filled("clip.clean.more", names.Count - shown));
            lines.Add(Filled("clip.clean.total", names.Count, ClipAssets.DescribeBytes(nameBytes)));
        }
        if (temps > 0) lines.Add(Filled("clip.clean.temps", temps, ClipAssets.DescribeBytes(tempBytes)));
        lines.Add(string.Empty);            // 尾句之前空一行：那是布局，由代码递进 {2} 槽
        lines.Add(Filled("clip.clean.tail"));
        return lines.ToArray();
    }

    // ────────── ① 归零（按方法体）＋宿主那一拍 ──────────

    /// <summary>
    /// 那颗工厂与它唯一那位宿主的方法体里，一颗屏幕字都不剩。
    /// <para>分母⛔ 在本格重复（<c>ClipboardAssetsWordGateTests</c> ①那格已经钉着"这把尺扫得到东西"，
    /// 同一问⛔ 两份主人＝规则 #86）；"这把尺真的会红"那一问由反向台架回答（<c>bin/WI64p2/bench_64p2.py</c>）。</para>
    /// </summary>
    [Fact]
    public void TheCleanupBodyFactoryAndItsHostSayNothingThemselves()
    {
        Assert.Empty(ScreenWords(MethodBody(CodeOf(Assets), "public static LocMessage CleanupConfirmBody(")));
        Assert.Empty(ScreenWords(MethodBody(CodeOf(ViewModel), "private async Task CleanClipboardAssetsAsync()")));

        // 同一把尺在这一档的<b>别处</b>仍读得到字：剩下那一颗是 DescribeTitle 的兜底字（归 P-199）。
        // ⚠ 那颗 1 是✅WI-64 片2 搬完七句之后的<b>现读</b>；它变成 0 的那天＝P-199 落了地，这一行要跟着改口径。
        Assert.Single(ScreenWords(CodeOf(Assets)));
    }

    // ────────── ② 形状禁项：交出身份，取词只在宿主那一拍 ──────────

    /// <summary>
    /// 回型是 <see cref="LocMessage"/>（⛔ 悄悄退成 <c>string</c>＝半搬最先长回来的地方），
    /// 工厂⛔ 自己取词，VM⛔ 取词而是把身份<b>贴</b>上去。
    /// <para>与①那格<b>成对</b>（规则 #124）：只钉字面量的话，有人把取词搬进那颗工厂、字照样是表里的；
    /// 只钉形状的话，有人把整句写死成中文字面量，形状尺看不见字面量。</para>
    /// </summary>
    [Fact]
    public void TheFactoryHandsAnIdentityAndTheHostPastesIt()
    {
        var assets = CodeOf(Assets);
        Assert.Contains("public static LocMessage CleanupConfirmBody(", assets, StringComparison.Ordinal);
        Assert.Equal(0, Count(assets, "public static string CleanupConfirmBody("));
        Assert.Equal(0, Count(assets, "StringBuilder"));     // 那颗成品字梯子跟着七句一起走了

        var body = MethodBody(assets, "public static LocMessage CleanupConfirmBody(");
        foreach (var banned in new[] { "Locale.T(", "Locale.Fmt(", ".Render()" })
            Assert.Equal(0, Count(body, banned));

        var vm = MethodBody(CodeOf(ViewModel), "private async Task CleanClipboardAssetsAsync()");
        Assert.Equal(1, Count(vm, "LocText.SetMessage(body, ClipAssets.CleanupConfirmBody("));
        Assert.Equal(1, Count(vm, ".CleanupConfirmBody("));   // 一位宿主：两个出口就会两份措辞、两张贴法
        Assert.Equal(0, Count(vm, "Text = "));                // ⛔ 构造时就把成品字塞进 TextBlock
        foreach (var banned in new[] { "Locale.T(", "Locale.Fmt(", ".Render()" })
            Assert.Equal(0, Count(vm, banned));
    }

    // ────────── ③ 名单双向对账 ──────────

    /// <summary>
    /// 7 颗每颗在表里恰好一行、在那两档里恰好点名一次；<b>反方向</b>也钉：表里 <c>clip.clean.</c> 那一族的
    /// 每一行都得有人问。另钉那颗框的<b>整个</b> <c>clip.clean*</c> 家族＝10 行（标题／两颗按钮早就是键，
    /// 片1 的读数说"只有正文还是裸字"——今天那一句作废，正文的七行也在表里了）。
    /// </summary>
    [Fact]
    public void EveryKeyIsDeclaredOnceAndNamedExactlyOnce()
    {
        Assert.Equal(7, Keys.Length);
        Assert.Equal(Keys.Length, Keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1, Keys.Count(k => Glue.Contains(k)));
        Assert.Equal(1, Keys.Count(k => Marker.Contains(k)));

        var codes = string.Join("\n", CodeFiles.Select(CodeOf));
        foreach (var key in Keys)
        {
            Assert.Single(UiStrings.All, e => e.Key == key);
            Assert.Equal(1, Count(codes, Quoted(key)));       // 一键一处主人（与④那条 watched==7 同口径）
        }

        var asked = Keys.ToHashSet(StringComparer.Ordinal);
        var unasked = UiStrings.All.Select(e => e.Key)
            .Where(k => k.StartsWith("clip.clean.", StringComparison.Ordinal))
            .Where(k => !asked.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.True(unasked.Count == 0, "表里多了没人问的行：" + string.Join("、", unasked));

        // 整族 census：正文 7 颗＋那只框的标题与两颗按钮（✅WI-61c 就交过表）＝10 行，一颗⛔ 多一颗⛔ 少。
        // ⚠ 名单按 Ordinal 排好再比：⛔ 靠写的那份顺序（'.' 排在 'C' 之前，点分那七颗全在前三带）。
        var whole = UiStrings.All.Select(e => e.Key)
            .Where(k => k.StartsWith("clip.clean", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(10, whole.Length);
        Assert.Equal(new[]
        {
            "clip.clean.head", "clip.clean.row", "clip.clean.more", "clip.clean.total", "clip.clean.temps",
            "clip.clean.tail", "clip.clean.lineJoin", "clip.cleanTitle", "clip.cleanPrimary", "clip.cleanCancel",
        }.OrderBy(k => k, StringComparer.Ordinal).ToArray(), whole);
    }

    // ────────── ④ 实参颗数＝那一行的槽数 ──────────

    /// <summary>每一处 <c>LocMessage.Of(本族的键…)</c> 递出的顶层实参数<b>等于那一行声明的槽数</b>
    /// （共用件 <see cref="SourceGate.KeyedCallSites"/> 按括号深度走，⛔ 用正则切跨行调用＝#155）。
    /// <para>少递一颗时表闸门全绿、编译也过，屏幕上那一行<b>整颗空白</b>（坑表 #393）。</para></summary>
    [Fact]
    public void EveryCallSiteHandsAsManyArgsAsItsRowDeclares()
    {
        var family = Keys.ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();
        var watched = 0;
        foreach (var rel in CodeFiles)
        {
            foreach (var (key, args, line) in KeyedCallSites(CodeOf(rel)))
            {
                if (!family.Contains(key)) continue;
                watched++;
                var slots = RowSlots(key);
                if (args != slots) offenders.Add($"{rel}:{line} 的 {key} 表里 {slots} 颗槽，代码递了 {args} 颗实参");
            }
        }
        Assert.Empty(offenders);
        Assert.Equal(7, watched);     // 七颗键各唯一一位主人：那颗胶水行也在工厂里被点名一次
    }

    // ────────── ⑤ 每一行三语答话、槽填满 ──────────

    /// <summary>每一颗：三列都非空、三语互异（那颗胶水行三列同形、那颗记号行 zh==en 都是刻意的），
    /// 且在三语下<b>真取一次词</b>——槽全填满、⛔ 把身份念给用户听。</summary>
    [Fact]
    public void EveryRowAnswersInThreeLanguagesAndFillsItsSlots()
    {
        var languages = new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese };
        var withSlots = 0;
        foreach (var key in Keys)
        {
            Assert.True(UiStrings.TryGet(key, out var entry), "表里没有 " + key);
            var texts = new[] { entry.Zh, entry.En, entry.Ja };
            foreach (var text in texts) Assert.False(string.IsNullOrWhiteSpace(text), key + " 有一列是空的");

            var slots = RowSlots(key);
            if (slots > 0) withSlots++;

            if (Glue.Contains(key))
            {
                // 纯胶水行：三列逐字同形（拼接位在 C# 那一侧，那一行⛔ 有词可翻），
                // 而槽的<b>顺序</b>是 0→2→1——那颗 {2} 是夹在两句中间的分隔符（换行由实参递进来＝规则 #100）。
                // ⛔ 在这里抄表里那一行：钉的是"除占位符之外一颗字符都不许多"与"填进去的顺序"，
                // 两句合起来就排除了"有人往胶水行里塞了个空格或连接词"（那会长出第二份拼装出处）。
                foreach (var text in texts) Assert.Equal(texts[0], text);
                Assert.Equal(0, texts[0].Count(c => !"{012}".Contains(c)));
                Assert.Equal("M0M2M1", Filled(key, "M0", "M1", "M2"));
                continue;
            }
            if (Marker.Contains(key))
            {
                Assert.Equal(texts[0], texts[1]);         // 中英那一行同形＝那颗中点是排版而⛔ 词
                Assert.NotEqual(texts[1], texts[2]);      // 日文换片假名中点
            }
            else
            {
                Assert.NotEqual(texts[0], texts[1]);
                Assert.NotEqual(texts[1], texts[2]);
                Assert.NotEqual(texts[0], texts[2]);
            }

            var args = Enumerable.Range(0, slots).Select(i => (object)("M" + i)).ToArray();
            var said = new List<string>();
            foreach (var language in languages)
            {
                Locale.TrySet(language);
                Assert.Equal(language, Locale.Current);   // ⛔ 拿 TrySet 的返回值当"换没换到"的探针（#112）
                var text = slots == 0 ? Locale.T(key) : Locale.Fmt(key, args);
                Assert.DoesNotContain("{", text, StringComparison.Ordinal);
                Assert.DoesNotContain("}", text, StringComparison.Ordinal);
                Assert.DoesNotContain("clip.", text, StringComparison.Ordinal);
                if (slots > 0) Assert.Contains("M0", text, StringComparison.Ordinal);
                said.Add(text);
            }
            Assert.NotEqual(said[0], said[2]);            // 记号行 zh==en，所以只钉"日文那一发⛔ 跟着英文走"
        }
        Assert.Equal(6, withSlots);   // 2026-10-12 现场（bin/WI64p2/rows_64p2.py 的「带槽 6 颗」同一口径）
    }

    // ────────── ⑥ 那一整块⛔ 半中半英 ──────────

    /// <summary>正文是<b>两层套娃</b>（胶水行套行、行套读数）。判据＝同一棵树在三种语言各<b>建</b>一遍、
    /// 又各自在三种语言下<b>贴</b>一次：贴在现在这一拍的必须逐字相等。
    /// <para>谁把某一句改成"建的时候就取词"，"在中文里建、在英文里贴"那一格就与"在英文里建、在英文里贴"分岔
    /// ——屏幕上剩下半中半英。①②那两格抓的是"字在不在表里"，这一格抓的是<b>什么时候去表里取</b>。</para></summary>
    [Fact]
    public void TheWholeBodyStandsInTheSameLanguageItWasPastedIn()
    {
        var languages = new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese };
        foreach (var paste in languages)
        {
            Locale.TrySet(paste);
            Assert.Equal(paste, Locale.Current);
            var said = Body(OverLimit, 9_000_000, 2, 64_000, Folder);

            foreach (var build in languages)
            {
                Locale.TrySet(build);
                var froze = ClipAssets.CleanupConfirmBody(OverLimit, 9_000_000, 2, 64_000, Folder);
                Locale.TrySet(paste);
                Assert.Equal(said, froze.Render());
            }

            Assert.DoesNotContain("{", said, StringComparison.Ordinal);
            Assert.DoesNotContain("}", said, StringComparison.Ordinal);
            Assert.DoesNotContain("clip.clean", said, StringComparison.Ordinal);
            Assert.Contains(Folder, said, StringComparison.Ordinal);        // 目录那颗读数进得去
            Assert.Contains(OverLimit[0], said, StringComparison.Ordinal);  // 名字本身⛔ 是一门语言的话
            Assert.Contains("11", said, StringComparison.Ordinal);          // 合计那颗读数进得去
        }
    }

    // ────────── ⑦ 行结构（那根换行的化身） ──────────

    /// <summary>形状尺对<b>行结构</b>完全失明：哪一句落在第几行、尾句之前那一颗空行、名单超出上限时
    /// 究竟列了几行——这三件事⛔ 住在任何一颗键里，它们住在"代码递给 {2} 槽的那根换行"上。
    /// <para>判据＝把整块按行切开，与<b>同一份表在当下语言里逐行拼出来的</b>那份比逐字相等：
    /// 只改措辞必须绿（两半都跟着表动），少一行、多一行、把顺序换一换必红。</para></summary>
    [Fact]
    public void TheBodyKeepsItsLineStructureInEveryLanguage()
    {
        var shapes = new (IReadOnlyList<string> Names, long Bytes, int Temps, long TempBytes)[]
        {
            (OverLimit, 9_000_000, 2, 64_000),
            (new[] { "a.png", "b.png", "c.png" }, 1_024, 0, 0),
            (Array.Empty<string>(), 0, 2, 64_000),
        };
        foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English, AppLanguage.Japanese })
        {
            Locale.TrySet(language);
            Assert.Equal(language, Locale.Current);
            foreach (var (names, bytes, temps, tempBytes) in shapes)
            {
                var actual = Body(names, bytes, temps, tempBytes, Folder).Split('\n');
                Assert.Equal(Expected(names, bytes, temps, tempBytes), actual);
                Assert.Equal(string.Empty, actual[^2]);                     // 尾句之前那一颗空行
                Assert.Equal(Filled("clip.clean.tail"), actual[^1]);        // 尾句永远是最后一行
                // 有名单时第一行永远是"动的是哪个目录"；没名单时那一行是临时件那句（⛔ 编一份空名单）。
                Assert.Equal(names.Count > 0
                    ? Filled("clip.clean.head", Folder)
                    : Filled("clip.clean.temps", temps, ClipAssets.DescribeBytes(tempBytes)), actual[0]);
                Assert.DoesNotContain("。。", string.Join("", actual), StringComparison.Ordinal);
            }

            // 截断那一问：列出的行数正好等于上限，一条都⛔ 许多（那份"清单"就没人读了）。
            // 名字互异 ⇒ 每一行那一句也互异，所以"落在名单里"这一问⛔ 数不清（未列出的那三颗的行⛔ 在场）。
            var lines = Body(OverLimit, 9_000_000, 2, 64_000, Folder).Split('\n');
            var rowLines = OverLimit.Select(n => Filled("clip.clean.row", n)).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(ClipAssets.CleanupPreviewLimit, lines.Count(rowLines.Contains));
            Assert.Contains(Filled("clip.clean.more", OverLimit.Length - ClipAssets.CleanupPreviewLimit), lines);
            Assert.DoesNotContain(Filled("clip.clean.temps", 2, ClipAssets.DescribeBytes(64_000)),
                Body(new[] { "a.png" }, 10, 0, 0, Folder), StringComparison.Ordinal);  // 没有临时件就⛔ 提它
        }
    }

    // ────────── ⑧ 整棵⛔ 一颗成品字 ──────────

    /// <summary>把交回来的那棵 <see cref="LocMessage"/> 走一遍：<b>每个节点都是表里的键</b>，
    /// 七颗在这一发形状里全被念到，而<b>实参里⛔ 有含汉字的东西</b>——那正是"已经取过词的成品字"，
    /// 本批要根除的就是它（形状尺只看方法体，看不见有人把半句藏在实参里递进来）。
    /// <para>同一问的另一半（那两根换行确实由实参递进来）也在这里：布局⛔ 抄进表（规则 #100），
    /// 也⛔ 硬编码进文案，它是那颗 <c>{2}</c> 槽的内容。</para></summary>
    [Fact]
    public void NothingInTheTreeIsAWordAlreadyTaken()
    {
        Locale.TrySet(AppLanguage.Chinese);
        var hits = new List<(string Key, object?[] Args)>();
        Walk(ClipAssets.CleanupConfirmBody(OverLimit, 9_000_000, 2, 64_000, Folder), hits);

        var family = Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var (key, args) in hits)
        {
            Assert.True(family.Contains(key), "树里冒出本族之外的键：" + key);
            foreach (var raw in args.OfType<string>())
                Assert.False(Cjk.IsMatch(raw), $"那颗实参是取过词的成品字：{raw}");
        }
        Assert.Equal(Keys.Length, hits.Select(h => h.Key).Distinct().Count()); // 七颗全上屏，一颗⛔ 写了没人念
        var gaps = hits.SelectMany(h => h.Args.OfType<string>()).Where(s => s.Length <= 2 && s.Contains('\n'))
            .Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "\n", "\n\n" }, gaps);   // 行分隔＋尾句前那一颗空行：布局只有这两颗
    }

    private static void Walk(LocMessage message, List<(string Key, object?[] Args)> hits)
    {
        var args = message.Args ?? Array.Empty<object?>();
        hits.Add((message.Key, args));
        foreach (var inner in args.OfType<LocMessage>()) Walk(inner, hits);
    }
}
