#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 标签编辑器的写入出口（批次 PA-3）。守两件事：
/// <b>①差集语义与"改完即所见"要对得上；②一次编辑只能有一次往返</b>——
/// 逐条调用时中间那次索引重建用的是半成品标签集，中途来一次搜索就会少命中，那是正确性问题不只是快慢。
/// </summary>
public sealed class TagEditTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbConnectionFactory _factory;
    private readonly ItemRepository _repo;

    public TagEditTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"starmark_tagedit_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(_dbPath);
        new MigrationRunner(_factory).EnsureSchema();
        _repo = new ItemRepository(_factory);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private async Task<long> New(string sourceId, params string[] tags)
    {
        var item = new Item
        {
            Type = ItemType.Bookmark,
            Source = ItemSources.Local,
            SourceId = sourceId,
            Title = "标题 年度报告",
            Description = "描述内容",
            Uri = "https://example.test/" + sourceId,
            Tags = tags.ToList(),
        };
        await _repo.RecordItemAsync(item, CancellationToken.None);
        return item.Id;
    }

    private async Task<string> SearchTextOfAsync(long id)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT search_text FROM items WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task SettingTheListRemovesWhatIsNotInIt()
    {
        var id = await New("te1", "留下", "去掉");

        var edit = await _repo.SetItemTagsAsync(id, new[] { "留下", "新增" }, CancellationToken.None);

        Assert.True(edit.Changed);
        Assert.Equal(1, edit.Added);
        Assert.Equal(1, edit.Removed);
        Assert.Equal(new[] { "新增", "留下" }, (await _repo.GetTagsForItemAsync(id, CancellationToken.None)).OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>索引必须是<b>最终状态</b>：新加的搜得到、去掉的搜不到。
    /// 逐条调用时中间那次重建会把"去掉的还在索引里"的状态留在库里。</summary>
    [Fact]
    public async Task SearchTextReflectsTheFinalTagSetOnly()
    {
        var id = await New("te2", "旧类目");

        await _repo.SetItemTagsAsync(id, new[] { "新类目" }, CancellationToken.None);

        var text = await SearchTextOfAsync(id);
        // 断言用二字组：CJK 展开的口径是"单字 + 相邻二字"，三个字的词不会以整串形式进索引
        // （查询侧同口径展开，所以搜"新类目"命中的正是"新类"/"类目"这些片段）。
        Assert.Contains("新类", text);
        Assert.DoesNotContain("旧类", text);      // 去掉的标签必须从索引里退场，否则被隐藏的内容还能搜到
        // 注意不能拿"类目"做断言：新旧两个标签共享这个二字组，它在索引里本来就该在。
        Assert.Contains("年度", text);           // 标题与描述仍在（重建不是覆盖成只有标签）
        Assert.Contains("描述", text);
    }

    [Fact]
    public async Task SavingUnchangedWritesNothingAndLeavesNoActivity()
    {
        var id = await New("te3", "甲", "乙");
        var before = await _repo.GetActivityAsync(500, CancellationToken.None);

        var edit = await _repo.SetItemTagsAsync(id, new[] { "乙", "甲" }, CancellationToken.None);

        Assert.False(edit.Changed);
        Assert.Equal(0, edit.Added + edit.Removed);
        Assert.Equal(2, edit.FinalTags.Count);
        var after = await _repo.GetActivityAsync(500, CancellationToken.None);
        Assert.Equal(before.Count, after.Count);       // 原样保存不该在时间线里留下"我改过"
    }

    [Fact]
    public async Task RemovingTheLastTagKeepsTheTagRowButDropsItFromTheCatalog()
    {
        var keep = await New("te4-keep", "共用");
        var id = await New("te4", "独用");

        await _repo.SetItemTagsAsync(id, Array.Empty<string>(), CancellationToken.None);

        // 全库没有 DELETE FROM tags（孤儿由 GetAllTagsAsync 的 INNER JOIN 挡掉）：解绑不删行
        Assert.Empty(await _repo.GetTagsForItemAsync(id, CancellationToken.None));
        var catalog = await _repo.GetAllTagsAsync(CancellationToken.None);
        Assert.DoesNotContain("独用", catalog.Select(row => row.Name));
        Assert.Contains("共用", catalog.Select(row => row.Name));
        Assert.NotEqual(0L, keep);
    }

    [Fact]
    public async Task BlankDuplicateAndCasedInputIsFoldedBeforeTheDiff()
    {
        var id = await New("te5", "工具");

        var edit = await _repo.SetItemTagsAsync(id, new[] { " 工具 ", "工具", "读书", "TOOL", "tool", "", "  " }, CancellationToken.None);

        // "工具"已有 → 不算新增；TOOL/tool 折叠成一个；末尾两个空白项被丢掉
        Assert.Equal(2, edit.Added);
        Assert.Equal(0, edit.Removed);
        Assert.Equal(new[] { "TOOL", "工具", "读书" },
            (await _repo.GetTagsForItemAsync(id, CancellationToken.None)).OrderBy(t => t, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AVirtualOrBogusIdIsANoOpNotAnError(long id)
    {
        var edit = await _repo.SetItemTagsAsync(id, new[] { "任何" }, CancellationToken.None);

        Assert.False(edit.Changed);
        Assert.Same(TagEditResult.Unchanged, edit);
    }

    [Fact]
    public async Task AnItemDeletedMeanwhileIsReportedAsUnchanged()
    {
        var id = await New("te6");
        await _repo.DeleteBySourceIdAsync(ItemSources.Local, "te6", CancellationToken.None);

        var edit = await _repo.SetItemTagsAsync(id, new[] { "标签" }, CancellationToken.None);

        Assert.False(edit.Changed);
        Assert.Empty(await _repo.GetTagsForItemAsync(id, CancellationToken.None));
    }

    /// <summary>往返闸门：一次编辑（加 5 删 3）= <b>1 次连接</b>；
    /// 而单标签原语 AddTagAsync/RemoveTagAsync 各自都要 1 次，"N 个标签就是 N 次"是结构性的，
    /// 不是实现不够努力——所以必须有一个批量出口，这就是 PA-3 存在的理由。</summary>
    [Fact]
    public async Task OneEditCostsOneConnectionWhileEachSingleTagCallCostsOneOfItsOwn()
    {
        var id = await New("te7", "旧一", "旧二", "旧三", "留一", "留二");
        var desired = new[] { "留一", "留二", "新一", "新二", "新三", "新四", "新五" };

        var counter = new DbActivityCounter();
        _factory.Counter = counter.Reset();
        await _repo.SetItemTagsAsync(id, desired, CancellationToken.None);
        var editOpens = counter.Connections;

        var single = new DbActivityCounter();
        _factory.Counter = single.Reset();
        await _repo.AddTagAsync(id, "单加一个", CancellationToken.None);
        await _repo.RemoveTagAsync(id, "单加一个", CancellationToken.None);
        var perCallOpens = single.Connections;
        _factory.Counter = null;

        Assert.Equal(1, editOpens);
        Assert.Equal(2, perCallOpens);                              // 一次加删就 2 次；八个标签就是 8 次
        Assert.True(editOpens < perCallOpens);
    }

    [Fact]
    public async Task TheEditLeavesExactlyOneModifyActivityPointingBackAtTheItem()
    {
        var id = await New("te8", "旧");

        await _repo.SetItemTagsAsync(id, new[] { "新" }, CancellationToken.None);

        var acts = await _repo.GetActivityAsync(50, CancellationToken.None);
        Assert.Single(acts, act => act.Kind == ActivityKind.ItemModify);
        var mine = acts.First(act => act.Kind == ActivityKind.ItemModify);
        Assert.Equal($"{ItemSources.Local}:te8", mine.ItemKey);
        Assert.Equal("标题 年度报告", mine.Title);
        Assert.NotNull(mine.Uri);
    }
}
