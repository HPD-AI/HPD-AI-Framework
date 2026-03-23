using System.Text.Json;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Handlers.Tests.Shared;
using HPD.RAG.Ingestion.Enrichers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.RAG.Handlers.Tests.Ingestion;

/// <summary>
/// Tests T-091 through T-093 — KeywordEnricherHandler and SummaryEnricherHandler.
/// Both handlers resolve IChatClient from keyed DI — we register FakeChatClient.
/// </summary>
public sealed class EnricherHandlerTests
{
    private static MragChunkDto MakeChunk(string docId, string content) =>
        new() { DocumentId = docId, Content = content };

    // -----------------------------------------------------------------------
    // KeywordEnricherHandler
    // -----------------------------------------------------------------------

    [Fact] // T-091
    public async Task KeywordEnricher_AddsKeywordsMetadata()
    {
        // The fake client returns one line of JSON per chunk.
        // KeywordEnricherHandler processes one batch call and maps one line per chunk.
        var fakeClient = new FakeChatClient("""["keyword1","keyword2"]""");

        var services = new ServiceCollection();
        services.AddKeyedSingleton<Microsoft.Extensions.AI.IChatClient>("mrag:enricher:keywords", fakeClient);
        var ctx = HandlerTestContext.Create(services);

        var handler = new KeywordEnricherHandler();
        var chunks = new[]
        {
            MakeChunk("doc-1", "Some content about AI search.")
        };

        var output = await handler.ExecuteAsync(Chunks: chunks, context: ctx);

        Assert.NotEmpty(output.Chunks);
        var enrichedChunk = output.Chunks[0];
        Assert.NotNull(enrichedChunk.Metadata);
        Assert.True(enrichedChunk.Metadata!.ContainsKey("keywords"),
            "Expected 'keywords' key in chunk Metadata");

        var keywordsEl = enrichedChunk.Metadata["keywords"];
        Assert.Equal(JsonValueKind.Array, keywordsEl.ValueKind);
        Assert.Equal(2, keywordsEl.GetArrayLength());
    }

    [Fact] // T-092
    public async Task SummaryEnricher_AddsSummaryMetadata()
    {
        var fakeClient = new FakeChatClient("A summary.");

        var services = new ServiceCollection();
        services.AddKeyedSingleton<Microsoft.Extensions.AI.IChatClient>("mrag:enricher:summary", fakeClient);
        var ctx = HandlerTestContext.Create(services);

        var handler = new SummaryEnricherHandler();
        var chunks = new[]
        {
            MakeChunk("doc-1", "Some long content that needs summarizing.")
        };

        var output = await handler.ExecuteAsync(Chunks: chunks, context: ctx);

        Assert.NotEmpty(output.Chunks);
        var enrichedChunk = output.Chunks[0];
        Assert.NotNull(enrichedChunk.Metadata);
        Assert.True(enrichedChunk.Metadata!.ContainsKey("summary"),
            "Expected 'summary' key in chunk Metadata");

        var summaryEl = enrichedChunk.Metadata["summary"];
        Assert.Equal("A summary.", summaryEl.GetString());
    }

    [Fact] // T-093
    public async Task KeywordEnricher_WhenChatClientThrows_PropagatesException()
    {
        // The error-propagation mode is Isolate (per the handler's DefaultPropagation).
        // When the IChatClient throws, the handler propagates the exception.
        // The orchestrator would handle isolation; here we just verify an exception is thrown.
        var throwingClient = new ThrowingChatClient();

        var services = new ServiceCollection();
        services.AddKeyedSingleton<Microsoft.Extensions.AI.IChatClient>("mrag:enricher:keywords", throwingClient);
        var ctx = HandlerTestContext.Create(services);

        var handler = new KeywordEnricherHandler();
        var chunks = new[] { MakeChunk("doc-1", "Content.") };

        // The handler propagates the HttpRequestException thrown by the fake client.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => handler.ExecuteAsync(Chunks: chunks, context: ctx));
    }

    // -----------------------------------------------------------------------
    // Private helper: a chat client that always throws
    // -----------------------------------------------------------------------

    private sealed class ThrowingChatClient : Microsoft.Extensions.AI.IChatClient
    {
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Simulated transient error");

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Microsoft.Extensions.AI.ChatClientMetadata Metadata => new("throwing", null, null);
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? serviceKey = null) where TService : class => null;
    }
}
