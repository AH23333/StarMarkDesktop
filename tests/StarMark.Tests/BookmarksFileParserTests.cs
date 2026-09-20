#nullable enable
using System.IO;
using Xunit;
using StarMark.Integrations.Bookmarks;

namespace StarMark.Tests;

/// <summary>
/// Chrome/Edge 书签 JSON 解析测试：
/// 层级路径链 / 根目录排除 / 时间换算 / 非书签结构容错 / 缺失文件。
/// </summary>
public sealed class BookmarksFileParserTests
{
    private const string SampleJson = """
    {
      "roots": {
        "bookmark_bar": {
          "type": "folder",
          "name": "Bookmarks bar",
          "children": [
            { "type": "url", "name": "GitHub", "url": "https://github.com", "date_added": "13300000000000000" },
            {
              "type": "folder",
              "name": "技术",
              "children": [
                { "type": "url", "name": "Rust", "url": "https://doc.rust-lang.org", "date_added": "13300012345678901" }
              ]
            }
          ]
        },
        "other": {
          "type": "folder",
          "name": "Other bookmarks",
          "children": [
            { "type": "url", "name": "孤岛", "url": "https://isolated.example", "date_added": "13300020000000000" }
          ]
        }
      }
    }
    """;

    [Fact]
    public void ParseJson_TracksFolderPathChains()
    {
        var entries = BookmarksFileParser.ParseJson(SampleJson);

        var rust = Assert.Single(entries, e => e.Url == "https://doc.rust-lang.org");
        // 根级文件夹名不参与路径
        Assert.Equal(new[] { "技术" }, rust.FolderPaths);

        var gh = Assert.Single(entries, e => e.Url == "https://github.com");
        Assert.Empty(gh.FolderPaths);

        var isolated = Assert.Single(entries, e => e.Url == "https://isolated.example");
        Assert.Empty(isolated.FolderPaths);
    }

    [Fact]
    public void ParseFile_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(BookmarksFileParser.ParseFile(Path.Combine(Path.GetTempPath(), "does_not_exist.json")));
    }

    [Fact]
    public void ParseJson_NonBookmarkJson_ReturnsEmpty()
    {
        Assert.Empty(BookmarksFileParser.ParseJson("{ \"foo\": 1 }"));
        Assert.Empty(BookmarksFileParser.ParseJson(""));
        Assert.Empty(BookmarksFileParser.ParseJson("not json"));
    }

    [Fact]
    public void FromChromeTime_ConvertsToUnixSeconds()
    {
        // 13300000000000000 微秒（FILETIME）≈ 1982-07-07T…
        var unix = BookmarksFileParser.FromChromeTime(13300000000000000L);
        Assert.True(unix > 300_000_000 && unix < 1_800_000_000, $"unix={unix} 应在 1970s-2020s 范围");
    }

    [Fact]
    public void ParseJson_NumericDateAdded_DoesNotThrowAndKeepsWholeTree()
    {
        // 回归 R6#1：date_added 为「未加引号的数字」时，旧实现 d.GetString() 抛
        // InvalidOperationException → 整棵树解析中断、上层吞异常后静默返回空（整份书签导不进来）。
        // 修复后：按 ValueKind 安全取值，数字型也正常解析，且不再牵连其它条目。
        const string json = """
        {
          "roots": {
            "bookmark_bar": {
              "type": "folder", "name": "bar", "children": [
                { "type": "url", "name": "A", "url": "https://a.example", "date_added": 13300000000000000 },
                { "type": "url", "name": "B", "url": "https://b.example", "date_added": "13300000000000000" }
              ]
            }
          }
        }
        """;
        var entries = BookmarksFileParser.ParseJson(json);

        Assert.Equal(2, entries.Count);   // 两条都在，未因数字型 date_added 崩掉整棵树
        var a = Assert.Single(entries, e => e.Url == "https://a.example");
        var b = Assert.Single(entries, e => e.Url == "https://b.example");
        // 数字型与字符串型解析出的收藏时间一致（同一微秒值 → 同一 Unix 秒）
        Assert.Equal(b.BookmarkedAt, a.BookmarkedAt);
        Assert.True(a.BookmarkedAt > 0);
    }

    [Fact]
    public void ParseJson_NonStringTypeNode_IsSkippedNotFatal()
    {
        // type 非字符串（异常导出）不得抛；应安全跳过该节点，其余照常解析。
        const string json = """
        { "roots": { "bookmark_bar": { "type": "folder", "name": "bar", "children": [
            { "type": 42, "name": "坏节点", "url": "https://bad.example" },
            { "type": "url", "name": "好", "url": "https://good.example" }
        ] } } }
        """;
        var entries = BookmarksFileParser.ParseJson(json);
        var good = Assert.Single(entries);
        Assert.Equal("https://good.example", good.Url);
    }
}