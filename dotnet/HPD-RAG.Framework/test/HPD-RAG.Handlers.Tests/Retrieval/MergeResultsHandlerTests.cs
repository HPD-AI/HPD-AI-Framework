using HPD.RAG.Core.DTOs;
using HPD.RAG.Handlers.Tests.Shared;
using HPD.RAG.Retrieval.Handlers;
using Xunit;

namespace HPD.RAG.Handlers.Tests.Retrieval;

/// <summary>
/// Tests T-104 and T-105 — MergeResultsHandler deduplication logic.
/// MergeResultsHandler explicitly implements IGraphNodeHandler but its typed ExecuteAsync
/// is public and can be called directly.
/// </summary>
public sealed class MergeResultsHandlerTests
{
    private static MragSearchResultDto MakeResult(string docId, double score, string content = "content") =>
        new() { DocumentId = docId, Content = content, Score = score };

    [Fact] // T-104
    public async Task MergeResults_DeduplicatesByDocumentId()
    {
        var handler = new MergeResultsHandler();
        var ctx = HandlerTestContext.Create();

        // doc-1 appears in both result sets
        var resultSets = new MragSearchResultDto[][]
        {
            [MakeResult("doc-1", 0.8), MakeResult("doc-2", 0.7)],
            [MakeResult("doc-1", 0.9), MakeResult("doc-3", 0.6)]
        };

        var output = await handler.ExecuteAsync(
            context: ctx,
            ResultSets: resultSets);

        var doc1Count = output.Results.Count(r => r.DocumentId == "doc-1");
        Assert.Equal(1, doc1Count);
    }

    [Fact] // T-105
    public async Task MergeResults_MaxScoreWins_OnDuplicate()
    {
        var handler = new MergeResultsHandler();
        var ctx = HandlerTestContext.Create();

        var resultSets = new MragSearchResultDto[][]
        {
            [MakeResult("doc-1", 0.8)],
            [MakeResult("doc-1", 0.9)]
        };

        var output = await handler.ExecuteAsync(
            context: ctx,
            ResultSets: resultSets);

        var doc1 = output.Results.Single(r => r.DocumentId == "doc-1");
        Assert.Equal(0.9, doc1.Score, precision: 6);
    }

    [Fact]
    public async Task MergeResults_ResultsSortedByScoreDescending()
    {
        var handler = new MergeResultsHandler();
        var ctx = HandlerTestContext.Create();

        var resultSets = new MragSearchResultDto[][]
        {
            [MakeResult("doc-a", 0.5), MakeResult("doc-b", 0.9)],
            [MakeResult("doc-c", 0.7)]
        };

        var output = await handler.ExecuteAsync(
            context: ctx,
            ResultSets: resultSets);

        Assert.Equal(3, output.Results.Length);
        // Scores should be descending
        for (int i = 0; i < output.Results.Length - 1; i++)
        {
            Assert.True(output.Results[i].Score >= output.Results[i + 1].Score,
                $"Score at [{i}] ({output.Results[i].Score}) should be >= score at [{i + 1}] ({output.Results[i + 1].Score})");
        }
    }

    [Fact]
    public async Task MergeResults_EmptyResultSets_ReturnsEmpty()
    {
        var handler = new MergeResultsHandler();
        var ctx = HandlerTestContext.Create();

        var output = await handler.ExecuteAsync(
            context: ctx,
            ResultSets: []);

        Assert.Empty(output.Results);
    }
}
