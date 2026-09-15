#nullable enable
using StarMark.Abstractions;
using StarMark.Integrations.GitHub;

namespace StarMark.SmokeTest.Mocks;

/// <summary>
/// 模拟 GitHubSource：返回预定义的 starred 列表，不调用真实 GitHub API。
/// 用于验证 SyncCoordinator 流程：FetchAsync -> UpsertAsync -> SearchAsync 全链路。
/// </summary>
public sealed class MockGitHubSource : IItemSource
{
    private readonly List<Item> _mockItems;

    public MockGitHubSource()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _mockItems = new()
        {
            new()
            {
                Type = ItemType.GitHubStar,
                Source = ItemSources.GitHub,
                SourceId = "mock-repo-1",
                Title = "TestRepo1 · C#",
                Subtitle = "testuser/testrepo1",
                Uri = "https://github.com/testuser/testrepo1",
                Description = "A test repo for mock sync.",
                StarsCount = 1234,
                CreatedAt = now,
                UpdatedAt = now,
                Tags = new() { "csharp", "test" },
            },
            new()
            {
                Type = ItemType.GitHubStar,
                Source = ItemSources.GitHub,
                SourceId = "mock-repo-2",
                Title = "AnotherRepo · Rust",
                Subtitle = "testuser/anotherrepo",
                Uri = "https://github.com/testuser/anotherrepo",
                Description = "Rust project for StarMark mock.",
                StarsCount = 5678,
                CreatedAt = now,
                UpdatedAt = now,
                Tags = new() { "rust", "system" },
            },
        };
    }

    public string SourceId => ItemSources.GitHub;
    public string DisplayName => "Mock GitHub Stars";
    public bool IsAvailable => true;

    public Task<IReadOnlyList<Item>> FetchAsync(SyncContext ctx, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Item>>(_mockItems);

    public Task<IReadOnlyList<Item>> SearchAsync(string query, SearchFilter filter, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>());
}
