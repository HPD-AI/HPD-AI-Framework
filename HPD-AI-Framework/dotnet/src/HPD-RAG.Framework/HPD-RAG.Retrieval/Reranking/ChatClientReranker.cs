using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Providers.Reranker;
using Microsoft.Extensions.AI;

namespace HPD.RAG.Retrieval.Reranking;

/// <summary>
/// IReranker implementation that uses an IChatClient to score each (query, passage) pair.
/// Registered when AddRetrievalHandlers configures .UseReranker(r => r.UseChatProvider(...)).
///
/// Prompt: "Score the relevance of the following passage to the query on a scale of 0-10.
///          Reply with only the number."
/// Parse failure fallback: score 0.0 (ensures all results are returned rather than throwing).
/// Results are returned sorted descending by LLM score, trimmed to topN.
/// </summary>
internal sealed class ChatClientReranker : IReranker
{
    private readonly IChatClient _chatClient;

    public ChatClientReranker(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<IReadOnlyList<MragSearchResultDto>> RerankAsync(
        string query,
        IReadOnlyList<MragSearchResultDto> results,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (results.Count == 0)
            return [];

        var scoringTasks = results.Select(r => ScoreAsync(query, r, cancellationToken));
        var scored = await Task.WhenAll(scoringTasks).ConfigureAwait(false);

        return scored
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .Select(x => x.Result)
            .ToArray();
    }

    private async Task<(MragSearchResultDto Result, double Score)> ScoreAsync(
        string query,
        MragSearchResultDto result,
        CancellationToken cancellationToken)
    {
        var prompt =
            $"Query: {query}\n\n" +
            $"Passage: {result.Content}\n\n" +
            "Score the relevance of the following passage to the query on a scale of 0-10. " +
            "Reply with only the number.";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        try
        {
            var response = await _chatClient
                .GetResponseAsync(messages, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var raw = response.Text?.Trim() ?? string.Empty;
            var score = double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0.0;

            return (result, score);
        }
        catch
        {
            // Treat any LLM failure for a single passage as score 0.0,
            // so the passage appears last rather than crashing the whole rerank.
            return (result, 0.0);
        }
    }
}
