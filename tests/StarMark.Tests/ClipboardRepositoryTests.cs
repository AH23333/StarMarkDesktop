#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using StarMark.Data;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 剪贴板历史的仓储契约（批次 IB）。
/// <para>
/// 这一层最容易被写坏的地方不是 SQL，而是<b>"再次复制"与"用户手动状态"的相遇</b>：
/// 采集是后台行为，一旦它把条目按"新草稿"整体覆盖，用户加的置顶/隐藏/笔记/标签就会在一次
/// 普通复制之后凭空消失（而且没有任何提示）。本文件把这几条一个个钉住，
/// 外加轮转的两个方向：超上限要真裁、置顶必须裁不掉。
/// </para>
/// </summary>
public sealed class ClipboardRepositoryTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"starmark_clip_{Guid.NewGuid():N}.db");
    private readonly ItemRepository _repo;

    public ClipboardRepositoryTests()
    {
        var factory = new DbConnectionFactory(_db);
        new MigrationRunner(factory).EnsureSchema();
        _repo = new ItemRepository(factory);
    }

    public void Dispose()
    {
        foreach (var p in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(p); } catch { /* 临时库清理失败不影响断言 */ }
    }

    private static Item Draft(string raw, string app = "chrome", DateTimeOffset? now = null)
        => ClipboardEntry.Build(raw, app, ClipboardEntry.FormatText, now ?? DateTimeOffset.UtcNow);

    private Task<IReadOnlyList<Item>> History()
        => _repo.GetBySourceAsync(ItemSources.Clipboard, ItemType.Clipboard);

    [Fact]
    public async Task FirstCopy_InsertsOneRowWithCountOne()
    {
        var saved = await _repo.RecordClipboardAsync(Draft("第一次复制的内容"));

        Assert.True(saved.Id > 0);
        var rows = await History();
        Assert.Single(rows);
        Assert.Equal(1, ClipboardEntry.CopyCount(rows[0]));
        Assert.Equal("chrome", ClipboardEntry.App(rows[0]));
    }

    [Fact]
    public async Task SameTextAgain_UpdatesInPlaceAndBumpsCount_NotANewRow()
    {
        var first = await _repo.RecordClipboardAsync(Draft("重复复制\n第二行"));
        var second = await _repo.RecordClipboardAsync(Draft("重复复制\r\n第二行", app: "edge"));   // 换行风格不同也要认成同一条

        Assert.Equal(first.Id, second.Id);
        var rows = await History();
        Assert.Single(rows);
        Assert.Equal(2, ClipboardEntry.CopyCount(rows[0]));
        Assert.Equal("edge", ClipboardEntry.App(rows[0]));            // 来源刷新到最后一次复制
        Assert.Equal("重复复制\n第二行", rows[0].Description);        // 正文归一成 LF
    }

    [Fact]
    public async Task Replay_PreservesPinnedHiddenAndNotes()
    {
        var saved = await _repo.RecordClipboardAsync(Draft("要被标星的文本"));
        await _repo.SetPinnedAsync(saved.Id, true, CancellationToken.None);
        await _repo.SetNoteAsync(saved.Id, "这条很重要", CancellationToken.None);

        await _repo.RecordClipboardAsync(Draft("要被标星的文本", app: "Code"));

        var row = Assert.Single(await History());
        Assert.True(row.Pinned);
        Assert.Equal("这条很重要", row.Notes);
        Assert.Equal(2, ClipboardEntry.CopyCount(row));
    }

    [Fact]
    public async Task Replay_KeepsNoteAndTagWordsSearchable()
    {
        // 回归本仓的老坑（SetNote/改标签后 search_text 不重算 ⇒ "明明写了却搜不到"）：
        // 后台采集重写 title/description 之后，笔记词与标签词必须仍在索引里。
        var saved = await _repo.RecordClipboardAsync(Draft("一段普通文本"));
        await _repo.SetNoteAsync(saved.Id, "只出现在笔记里的词", CancellationToken.None);
        await _repo.AddTagAsync(saved.Id, "标签独有误", CancellationToken.None);
        Assert.NotEmpty((await _repo.SearchAsync("只出现在笔记里的词", new SearchFilter { MaxResults = 10 }, CancellationToken.None)).Items);

        await _repo.RecordClipboardAsync(Draft("一段普通文本"));

        var noteHits = await _repo.SearchAsync("只出现在笔记里的词", new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        var tagHits = await _repo.SearchAsync("标签独有误", new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        Assert.Contains(noteHits.Items, i => i.Id == saved.Id);
        Assert.Contains(tagHits.Items, i => i.Id == saved.Id);
    }

    [Fact]
    public async Task Replay_KeepsSecondLineSearchable()
    {
        var saved = await _repo.RecordClipboardAsync(Draft("标题行\n正文里的关键词甲"));
        await _repo.RecordClipboardAsync(Draft("标题行\n正文里的关键词甲"));

        var hits = await _repo.SearchAsync("关键词甲", new SearchFilter { MaxResults = 10 }, CancellationToken.None);
        Assert.Contains(hits.Items, i => i.Id == saved.Id);
    }

    [Fact]
    public async Task Rotation_DropsOldestUnpinned_AndNeverTouchesPinned()
    {
        var oldest = await _repo.RecordClipboardAsync(Draft("最旧的一条", now: DateTimeOffset.UtcNow.AddSeconds(-30)));
        await _repo.SetPinnedAsync(oldest.Id, true, CancellationToken.None);

        var mid = await _repo.RecordClipboardAsync(Draft("中间的一条", now: DateTimeOffset.UtcNow.AddSeconds(-20)), maxEntries: 1);
        var newest = await _repo.RecordClipboardAsync(Draft("最新的一条", now: DateTimeOffset.UtcNow.AddSeconds(-10)), maxEntries: 1);

        var ids = (await History()).Select(i => i.Id).ToList();
        Assert.Contains(oldest.Id, ids);        // 置顶：轮转裁不掉
        Assert.DoesNotContain(mid.Id, ids);     // 未置顶且更旧：出局
        Assert.Contains(newest.Id, ids);
    }

    [Fact]
    public async Task Rotation_KeepsAtLeastTheJustWrittenRow()
    {
        // maxEntries 被配成 0（坏设置）也不能把刚记的这条删掉——那等于"记了但什么都没留"
        var saved = await _repo.RecordClipboardAsync(Draft("上限配错也要留下"), maxEntries: 0);
        Assert.Contains(saved.Id, (await History()).Select(i => i.Id));
    }

    [Fact]
    public async Task Clear_RemovesClipboardOnlyAndReturnsCount()
    {
        await _repo.RecordClipboardAsync(Draft("待清空的剪贴板一条"));
        await _repo.RecordClipboardAsync(Draft("待清空的剪贴板两条"));
        var local = Draft("本地随记不参与清空");
        local.Type = ItemType.Note;
        local.Source = ItemSources.Local;
        local.SourceId = "inst|7";
        await _repo.UpsertLocalItemAsync(local);

        var deleted = await _repo.ClearClipboardHistoryAsync();

        Assert.Equal(2, deleted);
        Assert.Empty(await History());
        Assert.Contains((await _repo.GetAllAsync(new BrowseFilter { Limit = 100 }, CancellationToken.None)),
            i => i.Source == ItemSources.Local && i.SourceId == "inst|7");
    }

    [Fact]
    public async Task WrongSource_IsRejectedBecauseRotationIsSourceScoped()
    {
        var draft = Draft("来源写错");
        draft.Source = ItemSources.Local;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _repo.RecordClipboardAsync(draft));
        Assert.Contains("source", ex.Message);
    }

    [Fact]
    public async Task MissingSourceId_SkipsWithoutThrowing()
    {
        var draft = Draft("没有键");
        draft.SourceId = "   ";

        var returned = await _repo.RecordClipboardAsync(draft);

        Assert.Same(draft, returned);
        Assert.Empty(await History());
    }

    [Fact]
    public async Task HiddenEntries_StayOutOfHistoryList_ButSurviveReplay()
    {
        var saved = await _repo.RecordClipboardAsync(Draft("被隐藏的敏感文本"));
        await _repo.SetHiddenAsync(saved.Id, true, CancellationToken.None);

        Assert.Empty(await History());                                  // 列表口径与其他页一致：不含隐藏
        await _repo.RecordClipboardAsync(Draft("被隐藏的敏感文本"));     // 再次复制不得把隐藏状态冲掉

        var hidden = await _repo.GetHiddenAsync(CancellationToken.None);
        Assert.Contains(hidden, i => i.Id == saved.Id && i.Hidden);
        Assert.Equal(2, ClipboardEntry.CopyCount(hidden.First(i => i.Id == saved.Id)));
    }

    [Fact]
    public async Task TwoLongTextsSharingTruncatedBody_DoNotOverwriteEachOther()
    {
        // 截断后正文完全相同、只有尾部不同 ⇒ 只有"键按全文算"才不会互相覆盖
        var head = new string('t', ClipboardPolicy.MaxStoredChars);
        var a = await _repo.RecordClipboardAsync(Draft(head + "-甲"));
        var b = await _repo.RecordClipboardAsync(Draft(head + "-乙"));

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, (await History()).Count);
    }
}
