using HPD.RAG.Core.DTOs;
using Xunit;

namespace HPD.RAG.Providers.Tests.RerankerProviders;

/// <summary>
/// T-076 and T-077: Tests for IReranker contract using FakeReranker (no real API calls).
/// </summary>
public sealed class FakeRerankerTests
{
    private static MragSearchResultDto MakeResult(string id, string content) =>
        new MragSearchResultDto
        {
            DocumentId = id,
            Content = content,
            Score = 0.0
        };

    // T-076 — RerankAsync returns results sorted by score descending
    [Fact]
    public async Task RerankAsync_ReturnsSortedByScoreDescending()
    {
        var reranker = new FakeReranker(0.3f, 0.9f, 0.1f, 0.7f);

        var candidates = new[]
        {
            MakeResult("doc-1", "first result"),
            MakeResult("doc-2", "second result"),
            MakeResult("doc-3", "third result"),
            MakeResult("doc-4", "fourth result"),
        };

        var results = await reranker.RerankAsync("query", candidates, topN: 4);

        Assert.Equal(4, results.Count);

        // Verify sorted descending
        for (int i = 0; i < results.Count - 1; i++)
        {
            Assert.True(results[i].Score >= results[i + 1].Score,
                $"results[{i}].Score ({results[i].Score}) should be >= results[{i + 1}].Score ({results[i + 1].Score})");
        }

        // First result should have the highest score (0.9 → doc-2)
        Assert.Equal("doc-2", results[0].DocumentId);
        Assert.Equal(0.9, results[0].Score, precision: 5);
    }

    // T-077 — RerankAsync trims to topN
    [Fact]
    public async Task RerankAsync_TrimsToTopN()
    {
        // 10 candidates, topN = 3
        var reranker = new FakeReranker(0.1f, 0.5f, 0.3f, 0.9f, 0.2f, 0.7f, 0.4f, 0.6f, 0.8f, 0.0f);

        var candidates = Enumerable.Range(1, 10)
            .Select(i => MakeResult($"doc-{i}", $"content {i}"))
            .ToList();

        var results = await reranker.RerankAsync("query", candidates, topN: 3);

        Assert.Equal(3, results.Count);

        // Verify sorted descending
        Assert.True(results[0].Score >= results[1].Score);
        Assert.True(results[1].Score >= results[2].Score);
    }
}
