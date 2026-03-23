using HPD.RAG.Evaluation.Handlers;
using HPD.RAG.Handlers.Tests.Shared;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.RAG.Handlers.Tests.Evaluation;

/// <summary>
/// Tests T-112 and T-113 — BLEUEvalHandler.
/// BLEUEvalHandler is purely algorithmic — no chat client is involved.
/// </summary>
public sealed class BLEUEvalHandlerTests
{
    [Fact] // T-112
    public async Task BLEUEvalHandler_DoesNotCallChatClient()
    {
        var fakeClient = new FakeChatClient("ignored");
        var handler = new BLEUEvalHandler();
        var ctx = HandlerTestContext.Create();

        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, "The quick brown fox")]);
        var references = new[] { "The quick brown fox jumps over the lazy dog" };

        await handler.ExecuteAsync(
            Response: response,
            References: references,
            context: ctx);

        // BLEUEvalHandler never calls an IChatClient — this is a purely algorithmic handler.
        Assert.Equal(0, fakeClient.CallCount);
    }

    [Fact] // T-113
    public async Task BLEUEvalHandler_ReturnsScoreBetween0And1()
    {
        var handler = new BLEUEvalHandler();
        var ctx = HandlerTestContext.Create();

        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, "The quick brown fox")]);
        var references = new[] { "The quick brown fox jumps over the lazy dog" };

        var output = await handler.ExecuteAsync(
            Response: response,
            References: references,
            context: ctx);

        Assert.True(output.Score >= 0.0 && output.Score <= 1.0,
            $"BLEU score {output.Score} is not in [0.0, 1.0]");
    }

    [Fact]
    public async Task BLEUEvalHandler_IdenticalHypothesisAndReference_ReturnsHighScore()
    {
        var handler = new BLEUEvalHandler();
        var ctx = HandlerTestContext.Create();

        var text = "the quick brown fox jumps over the lazy dog";
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
        var references = new[] { text };

        var output = await handler.ExecuteAsync(
            Response: response,
            References: references,
            context: ctx);

        Assert.True(output.Score > 0.9, $"Expected score > 0.9 for identical strings, got {output.Score}");
    }

    [Fact]
    public async Task BLEUEvalHandler_EmptyHypothesis_ReturnsZero()
    {
        var handler = new BLEUEvalHandler();
        var ctx = HandlerTestContext.Create();

        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, "")]);
        var references = new[] { "some reference text" };

        var output = await handler.ExecuteAsync(
            Response: response,
            References: references,
            context: ctx);

        Assert.Equal(0.0, output.Score);
    }
}
