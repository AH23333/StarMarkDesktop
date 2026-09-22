#nullable enable
using System;
using System.Linq;
using System.Text.Json.Nodes;
using StarMark.Abstractions;
using StarMark.Abstractions.Clipboard;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 剪贴板历史条目 ↔ 统一 <see cref="Item"/> 的映射与 extra_json 状态读写契约（批次 IA）。
/// <para>钉的是三件事：① 落库形状（type/source/键/正文放 Description ⇒ 第二行才进得了 FTS）；
/// ② 截断必须留可交代的确切长度（UI 要说"共 N 字"）；③ 坏 extra_json 只能回落默认值，
/// 绝不能把"记一条历史"这条日常路径炸掉。</para>
/// </summary>
public sealed class ClipboardEntryTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000);

    private static Item Build(string raw, string? app = "chrome")
        => ClipboardEntry.Build(raw, app, ClipboardEntry.FormatText, T);

    [Fact]
    public void Build_UniformItemShape_TypeSourceUriAndTimes()
    {
        var item = Build("hello world");

        Assert.Equal(ItemType.Clipboard, item.Type);            // 复用既有成员，不新增枚举值（EV 钉值契约）
        Assert.Equal(ItemSources.Clipboard, item.Source);       // 与 Ditto 的历史区分开：source 不同、type 相同
        Assert.StartsWith(ClipboardPolicy.SourceIdPrefix, item.SourceId);
        Assert.Equal(ClipboardPolicy.BuildSourceId("hello world"), item.SourceId);
        Assert.Equal(string.Empty, item.Uri);                   // 本地内容：点击=复制回剪贴板，没有可打开的 URI
        Assert.Equal(T.ToUnixTimeSeconds(), item.CreatedAt);
        Assert.Equal(T.ToUnixTimeSeconds(), item.UpdatedAt);
        Assert.Null(item.SyncedAt);                             // 本机采集，永不参与来源同步
        Assert.Empty(item.Tags);
    }

    [Fact]
    public void Build_BodyGoesIntoDescription_SoSecondLineIsSearchable()
    {
        // 只塞 Title 会让 search_text（title+description+notes+标签）里没有第二行 ⇒
        // "明明复制过却搜不到"。Ditto 源踩过同一坑，这里把它钉成映射契约。
        var item = Build("第一行\r\n第二行关键词");

        Assert.Contains("第二行关键词", item.Description);
        Assert.Equal("第一行\n第二行关键词", item.Description);   // 正文里 CRLF 已归一成 LF
        Assert.DoesNotContain('\n', item.Title);                // 标题只拿首行，正文保留换行（进 FTS 供搜第二行）
        Assert.Equal("第一行…", item.Title);
    }

    [Fact]
    public void Build_TruncatesBodyAndRecordsExactOriginalLength()
    {
        var original = new string('m', ClipboardPolicy.MaxStoredChars + 1234);
        var item = Build(original);

        Assert.Equal(ClipboardPolicy.MaxStoredChars, item.Description!.Length);
        Assert.True(ClipboardEntry.IsTruncated(item));
        Assert.Equal(original.Length, ClipboardEntry.FullLength(item));      // UI 靠这个说"共 N 字，仅存前 32 KB"
        // 键仍按<b>全文</b>算：两条"前 32 KB 相同、尾部不同"的长文不能互相覆盖
        Assert.Equal(ClipboardPolicy.BuildSourceId(ClipboardPolicy.NormalizeText(original)), item.SourceId);
    }

    [Fact]
    public void Build_NotTruncated_ReportsZeroFullLength()
    {
        var item = Build("正常长度的一段文本");
        Assert.False(ClipboardEntry.IsTruncated(item));
        Assert.Equal(0, ClipboardEntry.FullLength(item));
    }

    [Fact]
    public void Build_SubtitleCarriesAppAndShape()
    {
        var oneLine = Build("abcdefgh", app: "Code");
        Assert.Contains("Code", oneLine.Subtitle);
        Assert.Contains("8 字", oneLine.Subtitle);

        var multi = Build("a1\nb2\nc3", app: null);
        Assert.Contains("3 行", multi.Subtitle);
        Assert.Contains("未知来源", multi.Subtitle);            // 取不到前台进程也要说清楚，不能留空
    }

    [Fact]
    public void Extras_DefaultsOnFreshEntry()
    {
        var item = Build("some text", app: "firefox");
        Assert.Equal(1, ClipboardEntry.CopyCount(item));
        Assert.Equal("firefox", ClipboardEntry.App(item));
        Assert.Equal(ClipboardEntry.FormatText, ClipboardEntry.Format(item));
    }

    [Fact]
    public void Extras_FilesFormatRoundTrips()
    {
        var item = ClipboardEntry.Build("C:\\a.txt\nC:\\b.txt", "explorer", ClipboardEntry.FormatFiles, T);
        Assert.Equal(ClipboardEntry.FormatFiles, ClipboardEntry.Format(item));
        Assert.Equal("explorer", ClipboardEntry.App(item));
    }

    [Fact]
    public void MergeForReplay_BumpsCountKeepsForeignKeys_AndClearsStaleTruncation()
    {
        var oldTruncated = Build(new string('k', ClipboardPolicy.MaxStoredChars + 50));   // 之前存过一条超长
        Assert.True(ClipboardEntry.IsTruncated(oldTruncated));

        // 手动塞一个"别人"的键，模拟以后可能出现的扩展字段：合并必须原样带过去
        var obj = JsonNode.Parse(oldTruncated.ExtraJson!)!.AsObject();
        obj["mine"] = 42;
        oldTruncated.ExtraJson = obj.ToJsonString();

        var freshShort = Build("重新复制的短文本", app: "edge");
        var merged = ClipboardEntry.MergeForReplay(oldTruncated, freshShort, copyCount: 7);
        var probe = new Item { ExtraJson = merged };

        Assert.Equal(7, ClipboardEntry.CopyCount(probe));
        Assert.Equal("edge", ClipboardEntry.App(probe));                                   // 来源刷新到最后一次
        Assert.False(ClipboardEntry.IsTruncated(probe));                                   // 旧截断标记必须清掉
        Assert.Equal(0, ClipboardEntry.FullLength(probe));
        Assert.Equal(42, JsonNode.Parse(merged)![ "mine"]!.GetValue<int>());               // 未知键不被抹掉
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]                                    // 数组不是对象：必须走兜底而不是抛
    [InlineData("{\"clipCopyCount\":\"很多\"}")]          // 类型错乱
    [InlineData("{\"clipTruncated\":\"yes\"}")]
    public void Extras_CorruptJson_FallsBackToDefaultsWithoutThrowing(string? json)
    {
        var item = new Item { Type = ItemType.Clipboard, Source = ItemSources.Clipboard, ExtraJson = json };

        Assert.Equal(1, ClipboardEntry.CopyCount(item));
        Assert.Equal(ClipboardEntry.FormatText, ClipboardEntry.Format(item));
        Assert.Null(ClipboardEntry.App(item));
        Assert.False(ClipboardEntry.IsTruncated(item));
        Assert.Equal(0, ClipboardEntry.FullLength(item));
    }

    [Fact]
    public void CopyCount_NonPositiveStoredValueReadsAsOne()
    {
        // 手工把次数写成 0 或负数（坏备份）不能让条目在"按次数排序"上变成无穷小
        Assert.Equal(1, ClipboardEntry.CopyCount(new Item { ExtraJson = "{\"clipCopyCount\":0}" }));
        Assert.Equal(1, ClipboardEntry.CopyCount(new Item { ExtraJson = "{\"clipCopyCount\":-3}" }));
        Assert.Equal(9, ClipboardEntry.CopyCount(new Item { ExtraJson = "{\"clipCopyCount\":9}" }));
    }

    [Fact]
    public void Build_TitleNeverExceedsBudget_EvenWithControlChars()
    {
        var item = Build(string.Join("", Enumerable.Repeat("a\t", ClipboardPolicy.MaxTitleChars * 3)));
        Assert.True(item.Title.Length <= ClipboardPolicy.MaxTitleChars,
            $"标题 {item.Title.Length} 超出预算 {ClipboardPolicy.MaxTitleChars}");
    }
}
