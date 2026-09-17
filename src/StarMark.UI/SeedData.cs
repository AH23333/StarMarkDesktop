#nullable enable
using StarMark.Abstractions;
using StarMark.Data;

namespace StarMark.UI;

/// <summary>
/// 种子数据：首次启动时插入若干测试条目，便于验证搜索流程。
/// 后续接入 GitHub Stars / 书签 / Everything 同步后此 seeder 可移除。
/// </summary>
public static class SeedData
{
    public static async Task SeedIfEmptyAsync(IItemRepository repo, CancellationToken ct)
    {
        var counts = await repo.GetCountsByTypeAsync(ct);
        if (counts.Values.Sum() > 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var items = new List<Item>
        {
            new()
            {
                Type = ItemType.GitHubStar,
                Source = ItemSources.GitHub,
                SourceId = "repo-ragflow",
                Title = "RAGFlow - RAG 引擎",
                Subtitle = "infiniflow/ragflow",
                Uri = "https://github.com/infiniflow/ragflow",
                Description = "基于深度文档理解的 RAG 引擎，支持任何格式的数据。",
                StarsCount = 30000,
                CreatedAt = now,
                UpdatedAt = now,
                Tags = new() { "rag", "ai", "llm" },
                ExtraJson = """{"Language":"Python","topics":["rag","llm","nlp"]}""",
            },
            new()
            {
                Type = ItemType.GitHubStar,
                Source = ItemSources.GitHub,
                SourceId = "repo-ollama",
                Title = "Ollama - 本地 LLM 运行时",
                Subtitle = "ollama/ollama",
                Uri = "https://github.com/ollama/ollama",
                Description = "在本地运行 Llama 3, Mistral, Gemma 等大模型。",
                StarsCount = 100000,
                CreatedAt = now,
                UpdatedAt = now,
                Tags = new() { "llm", "ai", "local" },
                ExtraJson = """{"Language":"Go","topics":["llm","ai"]}""",
            },
            new()
            {
                Type = ItemType.Bookmark,
                Source = ItemSources.Chrome,
                SourceId = "bm-starmark-docs",
                Title = "StarMark 浏览器扩展文档",
                Subtitle = "github.com",
                Uri = "https://github.com/anthropics/anthropic-cookbook",
                Description = "Anthropic API 使用手册与示例。",
                CreatedAt = now,
                UpdatedAt = now,
                Tags = new() { "ai", "docs" },
            },
            new()
            {
                Type = ItemType.File,
                Source = ItemSources.FileSystem,
                SourceId = "file-test-1",
                Title = "技术文档.md",
                Subtitle = @"D:\Visual Studio Code\Something\StarMarkDesktop\docs",
                Uri = "file:///D:/Visual%20Studio%20Code/Something/StarMarkDesktop/docs/项目开发技术文档.md",
                Description = "StarMark 桌面端项目开发技术文档。",
                FileSize = 15000,
                CreatedAt = now,
                UpdatedAt = now,
            },
        };

        await repo.UpsertAsync(items, ct);

        // 给第一条打标签（验证标签 JOIN）
        if (items[0].Id > 0)
        {
            await repo.AddTagAsync(items[0].Id, "favorite", ct);
        }
    }
}
