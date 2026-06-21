using HPD.RAG.Evaluation.Handlers;
using HPD.RAG.Handlers.Tests.Shared;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.RAG.Handlers.Tests.Evaluation;

/// <summary>
/// T-108 — RelevanceEvalHandler calls the evaluator with the correct signature and returns an Output.
/// T-109 — GroundednessEvalHandler passes a GroundednessEvaluatorContext and returns an Output.
/// T-110 — CompletenessEvalHandler passes a CompletenessEvaluatorContext and returns an Output.
/// T-111 — RelevanceEvalHandler exposes non-null Score and Reason fields on its Output.
///
/// The ME.AI.Evaluation evaluators are sealed and call IChatClient internally.
/// FakeChatClient returns a response in the tagged format the evaluators parse:
///   <S0>chain of thought</S0><S1>reason text</S1><S2>4</S2>
/// This allows the NumericMetric to be populated without a real LLM.
/// Messages must end with a ChatRole.User message to satisfy TryGetUserRequest.
/// </summary>
public sealed class LlmEvalHandlerTests
{
    // Response text that all three evaluators can parse.
    // Format required by TryParseEvaluationResponseWithTags:
    //   <S0>thought chain</S0>  — informational, stored as diagnostic
    //   <S1>reason</S1>         — stored as metric.Reason
    //   <S2>4</S2>              — parsed as the numeric score
    private const string ValidEvalResponse =
        "<S0>Let's think step by step: the response addresses the question.</S0>" +
        "<S1>The response is relevant and complete.</S1>" +
        "<S2>4</S2>";

    private static IServiceProvider BuildServices(string response = ValidEvalResponse)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>("mrag:judge", new FakeChatClient(response));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Returns a minimal conversation where the last message is a User message.
    /// RelevanceEvaluator calls TryGetUserRequest which requires the last message
    /// to have ChatRole.User and non-empty text.
    /// </summary>
    private static ChatMessage[] MakeMessages(string userText = "What is the capital of France?")
        => [new ChatMessage(ChatRole.User, userText)];

    /// <summary>
    /// Returns a ChatResponse with non-empty text.
    /// RelevanceEvaluator rejects null/empty modelResponse.Text.
    /// </summary>
    private static ChatResponse MakeResponse(string text = "The capital of France is Paris.")
        => new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);

    /// <summary>
    /// T-108 — RelevanceEvalHandler.ExecuteAsync completes without throwing and returns a non-null Output.
    /// </summary>
    [Fact]
    public async Task RelevanceEvalHandler_CallsEvaluatorWithCorrectSignature()
    {
        var sp = BuildServices();
        var context = HandlerTestContext.CreateWithProvider(sp);
        var handler = new RelevanceEvalHandler();

        var output = await handler.ExecuteAsync(
            Messages: MakeMessages(),
            Response: MakeResponse(),
            context: context,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(output);
    }

    /// <summary>
    /// T-109 — GroundednessEvalHandler.ExecuteAsync accepts a grounding context string
    /// and completes without throwing.
    /// </summary>
    [Fact]
    public async Task GroundednessEvalHandler_PassesGroundednessContext()
    {
        var sp = BuildServices();
        var context = HandlerTestContext.CreateWithProvider(sp);
        var handler = new GroundednessEvalHandler();

        var output = await handler.ExecuteAsync(
            Messages: MakeMessages(),
            Response: MakeResponse(),
            context: context,
            GroundingContext: "France is a country in Western Europe. Its capital is Paris.",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(output);
    }

    /// <summary>
    /// T-110 — CompletenessEvalHandler.ExecuteAsync accepts a ground-truth string
    /// and completes without throwing.
    /// </summary>
    [Fact]
    public async Task CompletenessEvalHandler_PassesCompletenessContext()
    {
        var sp = BuildServices();
        var context = HandlerTestContext.CreateWithProvider(sp);
        var handler = new CompletenessEvalHandler();

        var output = await handler.ExecuteAsync(
            Messages: MakeMessages(),
            Response: MakeResponse(),
            GroundTruth: "The capital of France is Paris.",
            context: context,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(output);
    }

    /// <summary>
    /// T-111 — RelevanceEvalHandler output contains a numeric Score parsed from the
    /// fake judge response and a non-null Reason string.
    /// The FakeChatClient returns &lt;S2&gt;4&lt;/S2&gt; so Score must be 4.0.
    /// </summary>
    [Fact]
    public async Task RelevanceEvalHandler_OutputsScoreAndReason()
    {
        var sp = BuildServices();
        var context = HandlerTestContext.CreateWithProvider(sp);
        var handler = new RelevanceEvalHandler();

        var output = await handler.ExecuteAsync(
            Messages: MakeMessages(),
            Response: MakeResponse(),
            context: context,
            cancellationToken: CancellationToken.None);

        // The evaluator parsed <S2>4</S2> → Score = 4.0
        Assert.Equal(4.0, output.Score);
        // The evaluator parsed <S1>...</S1> → Reason is non-empty
        Assert.NotNull(output.Reason);
        Assert.NotEmpty(output.Reason);
    }
}
