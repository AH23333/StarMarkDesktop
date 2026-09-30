#nullable enable
using System;
using System.Linq;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// P-40 的形状与接线闸门（批次 SM）：<b>这六颗单行状态写都必须收受影响行数</b>，
/// 而"没落到行上"时要说实话（返回值）＋不许广播＋界面不许翻旗/不许记活动。
/// <para>行为测（<see cref="StateWriteRowTests"/>）量的是真 SQLite 的读数；这里量的是<b>形状与接线</b>：
/// 摘掉行数检查、把 <c>Notify()</c> 挪回守卫之前、把那句"已经不在了"抄到第二个文件里、
/// 或者某处调用点又把返回值丢掉——都在这里红，且红要报得出该打开哪一个文件。</para>
/// </summary>
public sealed class StateWriteRowGateTests
{
    private const string Items = "src/StarMark.Data/ItemRepository.Items.cs";
    private const string Tags = "src/StarMark.Data/ItemRepository.Tags.cs";
    private const string Local = "src/StarMark.Data/ItemRepository.Local.cs";
    private const string Contract = "src/StarMark.Abstractions/IItemRepository.cs";
    private const string Judge = "src/StarMark.Abstractions/StateWriteNotice.cs";
    private const string CardActions = "src/StarMark.UI/Helpers/ItemCardActions.cs";

    /// <summary>那句"那一行已经不在了"的指纹：抄到第二处就该红（一处说一处的事，别共用字面串）。</summary>
    private const string GoneFingerprint = "没落上去：这条已经不在这台机器的库里了";

    /// <summary>六颗基元 → 所在文件 + 方法签名锚点（锚点含 <c>Task&lt;bool&gt;</c>，签名退化也红）。</summary>
    private static readonly (string File, string Signature, string Member)[] Primitives =
    {
        (Items, "public async Task<bool> SetHiddenAsync(long itemId, bool hidden, CancellationToken ct)", "SetHiddenAsync"),
        (Tags, "public async Task<bool> SetPinnedAsync(long itemId, bool pinned, CancellationToken ct)", "SetPinnedAsync"),
        (Tags, "public async Task<bool> SetNoteAsync(long itemId, string content, CancellationToken ct)", "SetNoteAsync"),
        (Tags, "public async Task<bool> AddTagAsync(long itemId, string tagName, CancellationToken ct)", "AddTagAsync"),
        (Tags, "public async Task<bool> RemoveTagAsync(long itemId, string tagName, CancellationToken ct)", "RemoveTagAsync"),
        (Local, "public async Task<bool> DeleteBySourceIdAsync(string source, string sourceId, CancellationToken ct = default)", "DeleteBySourceIdAsync"),
    };

    [Theory]
    [MemberData(nameof(PrimitiveCases))]
    public void EveryPrimitiveInspectsItsRowCountAndGuardsTheBroadcast(string file, string signature)
    {
        var body = SourceGate.Code(SourceGate.MethodBody(SourceGate.ReadRepoFile(file), signature));

        Assert.Contains("ExecuteNonQueryAsync(ct)", body, StringComparison.Ordinal);   // 这段确实被扫到了（不是空方法体骗绿）
        Assert.Contains("== 0", body, StringComparison.Ordinal);                        // 行数被读过
        Assert.Contains("return false;", body, StringComparison.Ordinal);               // 0 行有自己的出口
        Assert.Contains("return true;", body, StringComparison.Ordinal);                // 落到了也有自己的出口

        // 广播必须在"0 行就退出"那句之后：从前这里是"写完就 Notify"，0 行也照样惊动所有组件。
        Assert.True(body.IndexOf("return false;", StringComparison.Ordinal)
                    < body.IndexOf("DataChangeHub.Notify();", StringComparison.Ordinal),
            "Notify 又跑回行数守卫之前了＝0 行也在广播");
    }

    public static TheoryData<string, string> PrimitiveCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (file, signature, _) in Primitives) data.Add(file, signature);
        return data;
    }

    [Fact]
    public void TheContractSaysWhatFalseMeansForEveryOneOfThem()
    {
        // 接口是唯一的口径出处：返回值类型退化（Task<bool> → Task）就是"又能把 0 行当成功"。
        var code = SourceGate.Code(SourceGate.ReadRepoFile(Contract));

        foreach (var line in new[]
        {
            "Task<bool> AddTagAsync(long itemId, string tagName, CancellationToken ct);",
            "Task<bool> RemoveTagAsync(long itemId, string tagName, CancellationToken ct);",
            "Task<bool> SetNoteAsync(long itemId, string content, CancellationToken ct);",
            "Task<bool> SetHiddenAsync(long itemId, bool hidden, CancellationToken ct);",
            "Task<bool> SetPinnedAsync(long itemId, bool pinned, CancellationToken ct);",
            "Task<bool> DeleteBySourceIdAsync(string source, string sourceId, CancellationToken ct = default);",
        })
            Assert.Contains(line, code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRowGoneSentenceExistsOnlyInThatJudge()
    {
        var files = SourceGate.ReadRepoUnder("src").ToList();
        Assert.True(files.Count >= 200, $"只扫到 {files.Count} 个源文件（普查路径错了＝这条闸门在空转）");

        var hits = files
            .Select(f => (f.RelativePath, Count: SourceGate.Count(SourceGate.Code(f.Text), GoneFingerprint)))
            .Where(f => f.Count > 0)
            .ToList();
        Assert.Equal(new[] { (Judge, 1) }, hits.Select(h => (h.RelativePath, h.Count)).ToArray());
    }
    [Fact]
    public void CardActionsNeitherFlipTheFlagNorLogWhatDidNotHappen()
    {
        var file = SourceGate.ReadRepoFile(CardActions);

        // 置顶：先问"落到那一行了吗"，没落到就不翻旗、并当他说一句（不是靠下一次重载把人骗回来）。
        var pin = SourceGate.Code(SourceGate.MethodBody(file, "public static async void TogglePin"));
        Assert.Contains("if (!await GetRepo().SetPinnedAsync(", pin, StringComparison.Ordinal);
        Assert.Contains("StateWriteNotice.RowGone", pin, StringComparison.Ordinal);

        // 笔记：那句"修改"活动必须在守卫之后——库里没发生的事不能记进时间线。
        var note = SourceGate.Code(SourceGate.MethodBody(file, "public static async void EditNote"));
        Assert.Contains("StateWriteNotice.RowGone", note, StringComparison.Ordinal);
        Assert.True(note.IndexOf("StateWriteNotice.RowGone", StringComparison.Ordinal)
                    < note.IndexOf("await LogModify(vm)", StringComparison.Ordinal),
            "笔记没落到那一行时还会记一笔「修改」＝活动流里多了一条假事件");

        // 摘标签：用"有没有真删掉"决定要不要记，而不是整段丢掉返回值。
        var remove = SourceGate.Code(SourceGate.MethodBody(file, "public static async void RemoveTag"));
        Assert.Contains("if (unlinked) await LogModify(vm)", remove, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTwoOtherHostsAlsoReadTheResultBeforeTouchingTheView()
    {
        // 隐藏页那格：这页没有状态行可写原因，所以至少不能把行摘掉。
        var hidden = SourceGate.Code(SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.UI/ViewModels/HiddenPageViewModel.cs"),
            "private async Task RestoreAsync"));
        Assert.Contains("StateWriteNotice.RowGone", hidden, StringComparison.Ordinal);
        Assert.True(hidden.IndexOf("StateWriteNotice.RowGone", StringComparison.Ordinal)
                    < hidden.IndexOf("HiddenItems.Remove(item)", StringComparison.Ordinal),
            "0 行也照样把条目从列表里摘掉＝下次进这页它又回来");

        // 热榜收藏：0 行是合法的幂等，但回执得说"这条本来就没在本机收藏里"，活动那笔也别记。
        var trending = SourceGate.Code(SourceGate.MethodBody(
            SourceGate.ReadRepoFile("src/StarMark.UI/Helpers/TrendingItemActions.cs"),
            "public static async Task ToggleCollectAsync"));
        Assert.Contains("if (removed)", trending, StringComparison.Ordinal);
        Assert.Contains("这条本来就没在本机收藏里", trending, StringComparison.Ordinal);
    }
}
