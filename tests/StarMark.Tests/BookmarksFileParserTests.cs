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
    public void ParseFile_TempFile_Works()
    {
        var path = Path.Combine(Path.GetTempPath(), $"starmark_bm_{System.Guid.NewGuid():N}.json");
        File.WriteAllText(path, SampleJson);
        try
        {
            var entries = BookmarksFileParser.ParseFile(path);
            Assert.Equal(3, entries.Count);
            Assert.Contains(entries, e => e.Url == "https://github.com");
        }
        finally
        {
            File.Delete(path);
        }
    }
}