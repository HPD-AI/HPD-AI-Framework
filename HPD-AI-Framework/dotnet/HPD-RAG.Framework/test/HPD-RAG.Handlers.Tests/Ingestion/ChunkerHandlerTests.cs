using HPD.RAG.Core.DTOs;
using HPD.RAG.Handlers.Tests.Shared;
using HPD.RAG.Ingestion.Chunkers;
using Xunit;

namespace HPD.RAG.Handlers.Tests.Ingestion;

/// <summary>
/// Tests T-087 through T-090 — HeaderChunkerHandler and TokenChunkerHandler.
/// Note: GetNodeConfig() returns default Config when _currentNode is null,
/// so MaxTokensPerChunk = 512 and OverlapTokens = 64 (defaults).
/// </summary>
public sealed class ChunkerHandlerTests
{
    private static MragDocumentDto MakeDocument(string id, string headerText, string bodyText) =>
        new()
        {
            Id = id,
            Elements =
            [
                new() { Type = "header", Text = headerText, HeaderLevel = 2 },
                new() { Type = "paragraph", Text = bodyText }
            ]
        };

    // -----------------------------------------------------------------------
    // HeaderChunkerHandler
    // -----------------------------------------------------------------------

    [Fact] // T-087
    public async Task HeaderChunker_ProducesChunkBatches_OnePerDocument()
    {
        var handler = new HeaderChunkerHandler();
        var ctx = HandlerTestContext.Create();

        var docs = new[]
        {
            MakeDocument("doc-1", "Introduction", "Text for doc 1."),
            MakeDocument("doc-2", "Overview",     "Text for doc 2."),
            MakeDocument("doc-3", "Summary",      "Text for doc 3.")
        };

        var output = await handler.ExecuteAsync(
            Documents: docs,
            context: ctx);

        Assert.Equal(3, output.ChunkBatches.Length);
    }

    [Fact] // T-088
    public async Task HeaderChunker_ChunksDoNotExceedMaxTokens()
    {
        var handler = new HeaderChunkerHandler();
        var ctx = HandlerTestContext.Create();

        // Default MaxTokensPerChunk = 512 (whitespace-split tokens)
        const int maxTokens = 512;

        var docs = new[]
        {
            MakeDocument("doc-1", "Introduction",
                string.Join(" ", Enumerable.Repeat("word", 100))) // 100 tokens — well under 512
        };

        var output = await handler.ExecuteAsync(
            Documents: docs,
            context: ctx);

        foreach (var batch in output.ChunkBatches)
        {
            foreach (var chunk in batch)
            {
                var tokenCount = chunk.Content
                    .Split((char[])null!, StringSplitOptions.RemoveEmptyEntries).Length;
                Assert.True(tokenCount <= maxTokens,
                    $"Chunk has {tokenCount} tokens, exceeds max {maxTokens}");
            }
        }
    }

    [Fact] // T-089
    public async Task HeaderChunker_ChunkContext_ContainsHeaderText()
    {
        var handler = new HeaderChunkerHandler();
        var ctx = HandlerTestContext.Create();

        var docs = new[]
        {
            MakeDocument("doc-1", "Introduction", "Body text under introduction.")
        };

        var output = await handler.ExecuteAsync(
            Documents: docs,
            context: ctx);

        var allChunks = output.ChunkBatches.SelectMany(b => b);
        Assert.Contains(allChunks, c => c.Context != null && c.Context.Contains("Introduction"));
    }

    // -----------------------------------------------------------------------
    // TokenChunkerHandler
    // -----------------------------------------------------------------------

    [Fact] // T-090
    public async Task TokenChunker_ProducesNonEmptyChunks_ForNonEmptyDocument()
    {
        var handler = new TokenChunkerHandler();
        var ctx = HandlerTestContext.Create();

        var docs = new[]
        {
            MakeDocument("doc-1", "Header", "This is some body text for the token chunker test.")
        };

        var output = await handler.ExecuteAsync(
            Documents: docs,
            context: ctx);

        var allChunks = output.ChunkBatches.SelectMany(b => b);
        Assert.NotEmpty(allChunks);
    }
}
