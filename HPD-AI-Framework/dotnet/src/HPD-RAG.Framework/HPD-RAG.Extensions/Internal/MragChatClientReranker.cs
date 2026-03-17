using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Providers.Reranker;
using Microsoft.Extensions.AI;

namespace HPD.RAG.Extensions.Internal;

/// <summary>
/// <see cref="IReranker"/> implementation backed by an <see cref="IChatClient"/>.
/// Scores each (query, passage) pair on a 0–10 scale via an LLM prompt.
/// Parse failures default to score 0.0 so all results are returned rather than throwing.
/// Results are sorted descending by score and trimmed to <c>topN</c>.
///
/// <para>
/// This is the Extensions-accessible counterpart to <c>ChatClientReranker</c> in
/// <c>HPD.RAG.Retrieval</c>, which is <c>internal sealed</c> and therefore
/// not reachable from this assembly.
/// </para>
/// </summary>
internal sealed class MragChatClientReranker : IReranker
{
    private readonly IChatClient _chatClient;

    internal MragChatClientReranker(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
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
            return (result, 0.0);
        }
    }
}
