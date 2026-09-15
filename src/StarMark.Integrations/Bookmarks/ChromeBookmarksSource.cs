#nullable enable
using StarMark.Abstractions;

namespace StarMark.Integrations.Bookmarks;

/// <summary>Chrome 书签源。读取 %LOCALAPPDATA%\Google\Chrome\User Data\Default\Bookmarks。</summary>
public sealed class ChromeBookmarksSource : BrowserBookmarksSource
{
    public override string SourceId => ItemSources.Chrome;
    public override string DisplayName => "Chrome 书签";
    protected override string BrowserSubPath => Path.Combine("Google", "Chrome");
}