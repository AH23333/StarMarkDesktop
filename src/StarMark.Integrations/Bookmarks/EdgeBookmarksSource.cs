#nullable enable
using StarMark.Abstractions;

namespace StarMark.Integrations.Bookmarks;

/// <summary>Edge 书签源。读取 %LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Bookmarks。</summary>
public sealed class EdgeBookmarksSource : BrowserBookmarksSource
{
    public EdgeBookmarksSource() { }

    /// <summary>指定书签文件路径（测试/自定义用）。</summary>
    public EdgeBookmarksSource(string bookmarksPath) : base(bookmarksPath) { }

    public override string SourceId => ItemSources.Edge;
    protected override string BrowserSubPath => Path.Combine("Microsoft", "Edge");
}