using System.Text.Json;
using HPDAgent.Graph.Abstractions.Attributes;
using HPDAgent.Graph.Abstractions.Handlers;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Core.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Retrieval.Handlers;

/// <summary>
/// Decomposes a complex query into 2–5 focused sub-queries using an LLM.
/// The sub-queries can be searched independently and results merged via MergeResultsHandler.
/// Default retry: 3 attempts, JitteredExponential, 2–60s.
/// Default propagation: SkipDependents.
/// </summary>
[GraphNodeHandler(NodeName = "DecomposeQuery")]
public sealed partial class DecomposeQueryHandler : IGraphNodeHandler<MragPipelineContext>
{
    public static MragRetryPolicy DefaultRetryPolicy { get; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(2),
        Strategy = MragBackoffStrategy.JitteredExponential,
        MaxDelay = TimeSpan.FromSeconds(60)
    };

    public static MragErrorPropagation DefaultErrorPropagation { get; } = MragErrorPropagation.SkipDependents;

    private const string SystemPrompt =
        "You are a query decomposition assistant. Break the user's question into 2-5 focused, " +
        "self-contained sub-queries that together cover all aspects of the original question. " +
        "Respond ONLY with a JSON array of strings. No explanation, no markdown fences, no numbering.";

    public async Task<DecomposeQueryOutput> ExecuteAsync(
        MragPipelineContext context,
        [InputSocket(Description = "The complex query to decompose into sub-queries.")] string Query,
        CancellationToken cancellationToken = default)
    {
        var chatClient = context.Services.GetRequiredKeyedService<IChatClient>("mrag:decompose");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, Query)
        };

        var response = await chatClient
            .GetResponseAsync(messages, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var subQueries = TryParseSubQueries(response.Text ?? string.Empty, Query);

        return new DecomposeQueryOutput { SubQueries = subQueries };
    }

    /// <summary>
    /// Parses a JSON array of strings from the LLM response.
    /// Falls back to a single-element array with the original query when parsing fails.
    /// </summary>
    internal static string[] TryParseSubQueries(string raw, string originalQuery)
    {
        var trimmed = raw.Trim();

        // Strip markdown fences the model may have added despite the prompt instruction.
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
                trimmed = trimmed[..lastFence];
            trimmed = trimmed.Trim();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(trimmed, MragJsonSerializerContext.Shared.StringArray);
            if (parsed is { Length: > 0 })
                return parsed;
        }
        catch (JsonException)
        {
            // Fall through to fallback.
        }

        // Fallback: treat original query as a single sub-query rather than returning empty.
        return [originalQuery];
    }

    public sealed class DecomposeQueryOutput
    {
        [OutputSocket(Description = "Array of focused sub-queries derived from the original query.")]
        public required string[] SubQueries { get; init; }
    }
}
