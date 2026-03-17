using HPD.RAG.Core.Context;
using HPD.RAG.Core.Pipeline;

namespace HPD.RAG.Pipeline.Tests.Shared;

/// <summary>
/// A stub IMragProcessor&lt;string, string&gt; used in unit tests for AddProcessor wiring.
/// </summary>
internal sealed class StubProcessor : IMragProcessor<string, string>
{
    public string? LastInput { get; private set; }
    public string? LastPipelineName { get; private set; }

    public Task<string> ProcessAsync(string input, MragProcessingContext context, CancellationToken ct)
    {
        LastInput = input;
        LastPipelineName = context.PipelineName;
        return Task.FromResult($"processed:{input}");
    }
}
