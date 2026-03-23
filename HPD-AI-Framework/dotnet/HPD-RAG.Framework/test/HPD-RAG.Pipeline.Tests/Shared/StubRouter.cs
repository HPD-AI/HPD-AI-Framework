using HPD.RAG.Core.Context;
using HPD.RAG.Core.Pipeline;

namespace HPD.RAG.Pipeline.Tests.Shared;

/// <summary>
/// A stub IMragRouter&lt;string&gt; that always routes to port 0.
/// </summary>
internal sealed class StubRouter : IMragRouter<string>
{
    public int SelectedPort { get; set; } = 0;

    public Task<MragRouteResult> RouteAsync(string input, MragProcessingContext context, CancellationToken ct)
    {
        return Task.FromResult(MragRouteResult.To(SelectedPort, input));
    }
}
