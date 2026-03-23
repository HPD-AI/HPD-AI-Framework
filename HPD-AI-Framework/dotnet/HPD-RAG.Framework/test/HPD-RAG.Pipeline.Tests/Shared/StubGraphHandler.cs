using HPD.RAG.Core.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;

namespace HPD.RAG.Pipeline.Tests.Shared;

/// <summary>
/// A configurable <see cref="IGraphNodeHandler{MragPipelineContext}"/> for unit tests.
///
/// Allows tests to:
/// - Register a handler with any <see cref="HandlerName"/> in a DI container that is passed as
///   the <c>services</c> argument to <see cref="MragIngestionPipeline.RunStreamingAsync"/> or
///   <see cref="MragRetrievalPipeline.RetrieveAsync"/>. The <see cref="GraphOrchestrator"/> resolves
///   handlers from that services argument by matching <see cref="HandlerName"/>, so a
///   <see cref="StubGraphHandler"/> with the correct name will be executed for any node whose
///   handler name matches.
/// - Supply a fixed output dictionary that will appear in the downstream channels and event outputs.
/// - Optionally throw to exercise failure paths.
/// - Inspect whether the handler was called and how many times.
/// </summary>
internal sealed class StubGraphHandler : IGraphNodeHandler<MragPipelineContext>
{
    private int _callCount;

    /// <summary>The handler name this stub is registered for.</summary>
    public string HandlerName { get; }

    /// <summary>Fixed port-0 output dictionary emitted on each successful call.</summary>
    public Dictionary<string, object> Outputs { get; set; } = new();

    /// <summary>When non-null, ExecuteAsync throws this exception instead of returning Success.</summary>
    public Exception? ThrowOnExecute { get; set; }

    /// <summary>Number of times <see cref="ExecuteAsync"/> was called.</summary>
    public int CallCount => _callCount;

    public StubGraphHandler(string handlerName, Dictionary<string, object>? outputs = null)
    {
        HandlerName = handlerName;
        if (outputs != null)
            Outputs = outputs;
    }

    public Task<NodeExecutionResult> ExecuteAsync(
        MragPipelineContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);

        if (ThrowOnExecute != null)
            throw ThrowOnExecute;

        var result = NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>(Outputs),
            duration: TimeSpan.Zero,
            metadata: new NodeExecutionMetadata { StartedAt = DateTimeOffset.UtcNow });

        return Task.FromResult<NodeExecutionResult>(result);
    }
}
