using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.Handlers.Tests.Shared;

internal sealed class FakeReranker : IReranker
{
    private readonly float[] _scores;

    public FakeReranker(params float[] scores) { _scores = scores; }

    public Task<IReadOnlyList<MragSearchResultDto>> RerankAsync(
        string query, IReadOnlyList<MragSearchResultDto> results, int topN, CancellationToken ct = default)
    {
        var scored = results.Zip(_scores, (r, s) => r with { Score = s })
            .OrderByDescending(r => r.Score).Take(topN).ToList();
        return Task.FromResult<IReadOnlyList<MragSearchResultDto>>(scored);
    }
}
