#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using StarMark.Abstractions;
using StarMark.Abstractions.Text;
using StarMark.Data;

namespace StarMark.Tests;

/// <summary>
/// 中文全文检索回归测试。
/// </summary>
/// <remarks>
/// FTS5 用 <c>tokenize='porter unicode61'</c>，而 unicode61 把一段连续 CJK 视为
/// <b>一个</b> token。若不展开，索引里「搜索笔记工具」只有 1 个 token，
/// 搜「笔记」「工具」这类中间子串全部落空。以下用例确保该缺陷不回归。
/// </remarks>
public sealed class CjkSearchTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;

    public CjkSearchTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_cjk_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    // ────────────────────────── 分词器纯函数 ──────────────────────────

    [Fact]
    public void IsCjk_CoversChineseJapaneseKorean()
    {
        Assert.True(CjkTokenizer.IsCjk('搜'));
        Assert.True(CjkTokenizer.IsCjk('あ'));   // 平假名
        Assert.True(CjkTokenizer.IsCjk('カ'));   // 片假名
        Assert.True(CjkTokenizer.IsCjk('한'));   // 韩文
        Assert.False(CjkTokenizer.IsCjk('A'));
        Assert.False(CjkTokenizer.IsCjk('1'));
        Assert.False(CjkTokenizer.IsCjk(' '));
    }

    /// <summary>
    /// 批次 DY：承 DB→DX 透镜（审到**分支进入 + 边界值**粒度）。<c>IsCjk</c> 是六段区间的析取
    /// （<c>CjkTokenizer.cs:39-44</c>），既有 <see cref="IsCjk_CoversChineseJapaneseKorean"/> 只进入
    /// CJK 基本区 / 平假名 / 片假名 / 韩文 四类，而 <b>CJK 扩展A(0x3400-0x4DBF)、兼容表意(0xF900-0xFAFF)、
    /// 半角片假名(0xFF66-0xFF9F) 三段从未有 True 用例进入</b>。回归面真实且<b>静默</b>：若把某段上下界写反
    /// （<c>&gt;=</c>→<c>&gt;</c> 漏段首）或误扩（<c>&lt;=</c>→<c>&lt;</c>）乃至整段漏并，
    /// <c>ExpandForIndex</c>/<c>SplitForQuery</c>/<c>ContainsCjk</c> 对相应脚本会静默按「非 CJK 原样」处理——
    /// 用户标题/随记里的兼容表意字（如「豈」）或半角片假名（ｱ）失去中文式子串展开、且
    /// <see cref="SplitForQuery_CjkTokensNeverCarryFts5Metachars"/> 赖以成立的「词元要么纯 CJK 要么不含 CJK」前提被破坏，现有测全绿无感。
    /// 与 DF 同类（钉的是<b>布尔范围归属逻辑与闭区间两侧</b>，非 Describe 式字面码→串表·注水）。
    /// </summary>
    [Theory]
    // CJK 扩展 A：段首/段尾含、越界假
    [InlineData('\u3400', true)]   // 段首下界（含）
    [InlineData('\u4DBF', true)]   // 段尾上界（含）
    [InlineData('\u4DC0', false)]  // 越上界一个码点（易筋经符号区）
    // CJK 兼容表意文字
    [InlineData('\uF900', true)]   // 段首（含）
    [InlineData('\uFAFF', true)]   // 段尾（含）
    [InlineData('\uFB00', false)]  // 越上界（拉丁连字 "ﬀ"）
    // 半角片假名
    [InlineData('\uFF66', true)]   // ｱ 段首（含）
    [InlineData('\uFF9F', true)]   // 段尾（含）
    [InlineData('\uFFA0', false)]  // 越上界
    public void IsCjk_ThreePreviouslyUnenteredRanges_HaveInclusiveBoundaries(char c, bool expected)
        => Assert.Equal(expected, CjkTokenizer.IsCjk(c));

    [Fact]
    public void ExpandForIndex_EmitsSinglesAndBigrams()
    {
        string expanded = CjkTokenizer.ExpandForIndex("搜索");

        // 单字 + 相邻二元组；整串不发送（查询侧从不发整串，发了纯属浪费索引）
        Assert.Equal(new[] { "搜", "索", "搜索" },
                     expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void ExpandForIndex_DoesNotEmitWholeRun()
    {
        string expanded = CjkTokenizer.ExpandForIndex("搜索笔记工具");

        Assert.DoesNotContain("搜索笔记工具", expanded);
        // 但二元组必须完整覆盖任意子串
        Assert.Contains("笔记", expanded);
        Assert.Contains("记工", expanded);
        Assert.Contains("工具", expanded);
    }

    [Fact]
    public void ExpandForIndex_PreservesNonCjkVerbatim()
    {
        string expanded = CjkTokenizer.ExpandForIndex("C# WinUI 桌面");

        // 非 CJK 部分原样保留（含 '#'
        Assert.StartsWith("C# WinUI ", expanded);
        // CJK 部分被展开
        Assert.Contains("桌面", expanded);
        Assert.Contains("桌", expanded);
        Assert.Contains("面", expanded);
    }

    [Fact]
    public void ExpandForIndex_HandlesLongRun()
    {
        var longRun = new string('搜', 200);
        string expanded = CjkTokenizer.ExpandForIndex(longRun);

        Assert.DoesNotContain(longRun, expanded);
        Assert.Contains("搜", expanded);      // 单字
        Assert.Contains("搜搜", expanded);    // 二元组
    }

    [Fact]
    public void SplitForQuery_NeverEmitsWholeRun()
    {
        // 铁律：整串绝不进查询侧，否则 4 字以上中文查询会自我淘汰
        var tokens = CjkTokenizer.SplitForQuery("笔记工具");

        Assert.Equal(new[] { "笔记", "记工", "工具" }, tokens);
        Assert.DoesNotContain("笔记工具", tokens);
    }

    [Fact]
    public void SplitForQuery_SingleCharEmitsItself()
    {
        Assert.Equal(new[] { "书" }, CjkTokenizer.SplitForQuery("书"));
    }

    [Fact]
    public void SplitForQuery_HandlesMixedAndMultipleParts()
    {
        var tokens = CjkTokenizer.SplitForQuery("winui 桌面组件");

        Assert.Equal(new[] { "winui", "桌面", "面组", "组件" }, tokens);
    }

    /// <summary>
    /// 回归 EP：查询侧曾只 split ASCII 空格，全角空格（U+3000，微软拼音/搜狗全角模式的空格键）
    /// 或不换行空格（U+00A0，网页/Word 粘贴标题常见）被当正文并入词元，交给 FTS5 成一个空短语
    /// → 整条查询静默 0 命中。修复后按 char.IsWhiteSpace 断词，与索引侧 unicode61 分隔符集对齐。
    /// </summary>
    [Theory]
    [InlineData("笔记　工具")] // U+3000 全角空格
    [InlineData("笔记 工具")] // U+00A0 不换行空格
    public void SplitForQuery_TreatsUnicodeSpaceAsSeparator(string keyword)
    {
        var tokens = CjkTokenizer.SplitForQuery(keyword);

        // 铁律：Unicode 空白的行为必须与 ASCII 空格完全一致——两段独立 CJK 串各成一个二元组，
        // 既不跨空白拼「记工」，也不残留任何纯空白词元。旧实现只 split U+0020 → 空白被并入词元、
        // 交给 FTS5 成空短语 → 整查询 0 命中（tokens=["笔记", <空白>, "工具"]），下列断言在旧实现下即失败。
        Assert.Equal(CjkTokenizer.SplitForQuery("笔记 工具"), tokens);
        Assert.Equal(new[] { "笔记", "工具" }, tokens);
        Assert.All(tokens, t => Assert.DoesNotMatch(@"\s", t));
    }

    // ────────────────────────── 端到端 FTS 检索 ──────────────────────────

    private static readonly (string Title, string SourceId)[] Corpus =
    {
        ("搜索笔记工具", "c1"),
        ("StarMark 统一搜索", "c2"),
        ("如何整理收藏夹", "c3"),
        ("C# WinUI 桌面组件开发", "c4"),
    };

    private async Task SeedAsync()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(Corpus.Select(c => new Item
        {
            Type = ItemType.Bookmark,
            Source = "cjk-test",
            SourceId = c.SourceId,
            Title = c.Title,
        }).ToArray(), CancellationToken.None);
    }

    private async Task<IReadOnlyList<string>> SearchTitlesAsync(string keyword)
    {
        var repo = new ItemRepository(_factory);
        var result = await repo.SearchAsync(keyword, new SearchFilter { MaxResults = 20 }, CancellationToken.None);
        return result.Items.Select(i => i.Title).ToList();
    }

    /// <summary>这些查询在旧实现（未展开）下全部落空或大量漏召回。</summary>
    [Theory]
    [InlineData("笔记", "搜索笔记工具")]                       // 中间子串
    [InlineData("工具", "搜索笔记工具")]                       // 尾部子串
    [InlineData("搜索", "搜索笔记工具", "StarMark 统一搜索")]  // 跨两条
    [InlineData("收藏", "如何整理收藏夹")]
    [InlineData("整理收藏", "如何整理收藏夹")]                 // 跨词边界
    [InlineData("组件", "C# WinUI 桌面组件开发")]
    [InlineData("桌面组件", "C# WinUI 桌面组件开发")]          // 4 字查询
    [InlineData("统一", "StarMark 统一搜索")]                  // 2 字词
    public async Task Search_ChineseSubstring_IsRecallable(string keyword, params string[] expected)
    {
        await SeedAsync();
        var titles = await SearchTitlesAsync(keyword);

        Assert.Equal(expected.OrderBy(x => x), titles.OrderBy(x => x));
    }

    [Fact]
    public async Task Search_ChineseNoMatch_ReturnsEmpty()
    {
        await SeedAsync();
        var titles = await SearchTitlesAsync("量子物理");

        Assert.Empty(titles);
    }

    [Fact]
    public async Task Search_EnglishPrefix_StillWorks()
    {
        await SeedAsync();
        var titles = await SearchTitlesAsync("WinUI");

        Assert.Contains("C# WinUI 桌面组件开发", titles);
    }

    [Fact]
    public async Task Search_SpecialCharKeyword_DoesNotThrow()
    {
        await SeedAsync();
        // 'C#' 含非字母数字字符，走引号转义分支
        var titles = await SearchTitlesAsync("C#");

        Assert.NotNull(titles);
    }

    // ────────────────────────── 迁移 v3 ──────────────────────────

    /// <summary>
    /// 回归 EP（端到端）：用户以全角空格（或粘贴含不换行空格）分隔关键词查「笔记 X 工具」时，
    /// 旧实现把该空白当正文并入词元 → FTS5 空短语 → 明明库里有匹配却返回 0 条；修复后应命中含这两词的标题。
    /// </summary>
    [Theory]
    [InlineData('　')] // 全角空格
    [InlineData(' ')] // 不换行空格
    public async Task Search_KeywordJoinedByUnicodeSpace_IsRecallable(char space)
    {
        await SeedAsync();
        var titles = await SearchTitlesAsync("笔记" + space + "工具");

        Assert.Contains("搜索笔记工具", titles);
    }

    [Fact]
    public async Task MigrateV3_RebuildsLegacySearchText()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "legacy", SourceId = "L1", Title = "搜索笔记工具" },
        }, CancellationToken.None);

        // 人为把 search_text 还原成「未展开」的旧形态，并把版本号退回 2，
        // 模拟升级前的存量库
        using (var conn = _factory.Open())
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE items SET search_text = '搜索笔记工具';";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO sync_state(key, value) VALUES('schema_version', '2')
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                cmd.ExecuteNonQuery();
            }
            // 用旧文本重建索引，确保起点确实是「坏的」
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO items_fts(items_fts) VALUES('rebuild');";
                cmd.ExecuteNonQuery();
            }
        }

        Assert.Empty(await SearchTitlesAsync("笔记"));   // 迁移前：落空

        new MigrationRunner(_factory).EnsureSchema();     // 触发 v3

        Assert.Equal(new[] { "搜索笔记工具" }, await SearchTitlesAsync("笔记"));
    }

    // ────────────────────────── 非 CJK↔CJK 交界（v5） ──────────────────────────

    [Fact]
    public void ExpandForIndex_SeparatesNonCjkFromCjkRun()
    {
        // 旧实现把 "2023" 与首字 "年" 并成 "2023年" 一个 token → 搜「年」落空。
        // 修复后交界补空格，首字独立成词。
        string expanded = CjkTokenizer.ExpandForIndex("2023年度报告");
        var tokens = expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("2023", tokens);   // 数字段独立
        Assert.Contains("年", tokens);      // CJK 首字独立，不再被并入 "2023年"
        Assert.DoesNotContain("2023年", tokens);
    }

    [Fact]
    public void ExpandForIndex_SeparatesAtEveryNonCjkCjkJunction()
    {
        // 双交界 "CJK→非CJK→CJK"（年report年）：两个交界都要各自成词。
        // 第一段的 CJK→非CJK 由 AppendRun 行尾空格隔开；第二段非 CJK→CJK 必须再补一个空格。
        string expanded = CjkTokenizer.ExpandForIndex("年report年");
        var tokens = expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(new[] { "年", "report", "年" }, tokens);
    }

    [Theory]
    [InlineData("年\"x\"年")]      // CJK 夹带被引号包裹的片段
    [InlineData("搜索(笔记)")]     // 圆括号
    [InlineData("词*语")]          // 通配符
    [InlineData("中文:列名")]      // 冒号（FTS5 列过滤前缀）
    [InlineData("混排-a-b")]       // 连字符
    public void SplitForQuery_CjkTokensNeverCarryFts5Metachars(string keyword)
    {
        // BuildFtsQuery 对「含 CJK 的词元」走**原样输出、不加引号转义**的分支。其安全前提是：
        // SplitForQuery 产出的词元要么纯 CJK、要么完全不含 CJK——绝不能把 CJK 与 FTS5 元字符
        // (" * ( ) : - ^) 混在同一词元里，否则未转义的特殊字符会漏进 MATCH 表达式破坏语法/语义。
        // CjkTokenizer 是被高频改动的热文件，此不变式此前无测钉死，锁住以防未来改动破坏该前提。
        var specials = new[] { '"', '*', '(', ')', ':', '-', '^' };
        foreach (var t in CjkTokenizer.SplitForQuery(keyword))
        {
            if (!CjkTokenizer.ContainsCjk(t)) continue;
            foreach (var c in specials)
                Assert.DoesNotContain(c.ToString(), t);
        }
    }

    private async Task SeedOneAsync(string title, string sourceId)
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "cjk-test", SourceId = sourceId, Title = title },
        }, CancellationToken.None);
    }

    /// <summary>数字/字母紧贴中文的标题：跨界子串必须可召回（旧实现全 0 命中）。</summary>
    [Theory]
    [InlineData("2023年度报告", "年")]
    [InlineData("2023年度报告", "2023年")]
    [InlineData("2023年度报告", "2023")]
    [InlineData("2023年度报告", "报告")]
    [InlineData("WinUI桌面", "桌")]
    [InlineData("WinUI桌面", "桌面")]
    public async Task Search_MixedScriptBoundary_IsRecallable(string title, string keyword)
    {
        await SeedOneAsync(title, "mix-" + title.Length + keyword);
        var titles = await SearchTitlesAsync(keyword);

        Assert.Contains(title, titles);
    }

    /// <summary>
    /// v5 迁移：存量行 search_text 仍是旧的「并词」形态（如 "2023年 度 报 告 年度 度报 报告"，
    /// 首字被并入 2023）时，EnsureSchema 必须按新口径重算并 rebuild，令「年」可搜。
    /// </summary>
    [Fact]
    public async Task MigrateV5_ReindexesJunctionGluedRows()
    {
        var repo = new ItemRepository(_factory);
        await repo.UpsertAsync(new[]
        {
            new Item { Type = ItemType.Bookmark, Source = "legacy5", SourceId = "L5", Title = "2023年度报告" },
        }, CancellationToken.None);

        // 人为退回旧展开形态（首字与数字并词、无行首分隔），并把版本降到 4，模拟 v5 前的库
        using (var conn = _factory.Open())
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE items SET search_text = '2023年 度 报 告 年度 度报 报告 ' WHERE source='legacy5';";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO sync_state(key, value) VALUES('schema_version', '4')
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO items_fts(items_fts) VALUES('rebuild');";
                cmd.ExecuteNonQuery();
            }
        }

        Assert.DoesNotContain("2023年度报告", await SearchTitlesAsync("年")); // 迁移前：首字被并词，落空

        new MigrationRunner(_factory).EnsureSchema(); // 触发 v5

        Assert.Contains("2023年度报告", await SearchTitlesAsync("年"));
    }
}
