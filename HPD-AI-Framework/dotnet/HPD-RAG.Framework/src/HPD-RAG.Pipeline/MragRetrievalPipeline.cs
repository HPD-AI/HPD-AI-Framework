using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Events;
using HPD.Events.Core;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.Events;
using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Retrieval;
using HPD.RAG.Pipeline.Internal;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Orchestration;

namespace HPD.RAG.Pipeline;

/// <summary>
/// A compiled, ready-to-run retrieval pipeline produced by
/// <see cref="MragPipeline.BuildRetrievalAsync"/>.
///
/// Implements <see cref="IMragRetriever"/> so it can be passed directly to any consumer
/// (agent tool, middleware, background job) that accepts the retriever interface.
///
/// <para>
/// The pipeline is expected to end with a <c>FormatContextHandler</c> node
/// (handler name <see cref="MragHandlerNames.FormatContext"/>) that writes its
/// formatted context string to an output socket named <c>"Context"</c>.
/// <see cref="RetrieveAsync"/> reads that value from the finished context.
/// </para>
/// </summary>
public sealed class MragRetrievalPipeline : IMragRetriever
{
    private readonly Graph _graph;
    private readonly IServiceProvider _pipelineServices;

    /// <summary>Human-readable name of this pipeline.</summary>
    public string PipelineName { get; }

    internal MragRetrievalPipeline(string pipelineName, Graph graph, IServiceProvider pipelineServices)
    {
        PipelineName = pipelineName;
        _graph = graph;
        _pipelineServices = pipelineServices;
    }

    // ------------------------------------------------------------------ //
    // IMragRetriever                                                       //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Executes the retrieval pipeline for the given <paramref name="query"/> and returns
    /// the formatted context string produced by the terminal FormatContextHandler node.
    /// Uses the pipeline's built-in service provider.
    /// </summary>
    public Task<string> RetrieveAsync(string query, MragFilterNode? filter = null, CancellationToken ct = default)
        => RetrieveAsync(query, filter, _pipelineServices, ct);

    // ------------------------------------------------------------------ //
    // RetrieveAsync (application-provider overload)                       //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Executes the retrieval pipeline and returns the formatted context string.
    /// </summary>
    /// <param name="query">Natural-language query to retrieve context for.</param>
    /// <param name="filter">Optional filter to scope retrieval (folder, tags, etc.).</param>
    /// <param name="services">Application <see cref="IServiceProvider"/> with handler registrations.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> RetrieveAsync(
        string query,
        MragFilterNode? filter,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        var eventCoordinator = new EventCoordinator();
        var context = BuildContext(query, filter, services, eventCoordinator);

        var orchestrator = new GraphOrchestrator<MragPipelineContext>(
            services,
            checkpointStore: null);

        var executionTask = Task.Run(async () =>
        {
            try
            {
                await orchestrator.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                eventCoordinator.Emit(new GraphDiagnosticEvent
                {
                    Level = LogLevel.Error,
                    Source = "MragRetrievalPipeline",
                    Message = ex.Message,
                    Exception = ex
                });
            }
        }, ct);

        await executionTask.ConfigureAwait(false);

        return ExtractContextOutput(context);
    }

    // ------------------------------------------------------------------ //
    // RetrieveStreamingAsync                                               //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Executes the retrieval pipeline and streams <see cref="MragEvent"/> instances.
    /// Raw graph events are translated to strongly-typed MRAG retrieval events by
    /// <see cref="MragEventMapper.MapRetrievalEvent"/>.
    /// </summary>
    public async IAsyncEnumerable<MragEvent> RetrieveStreamingAsync(
        string query,
        IServiceProvider services,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var eventCoordinator = new EventCoordinator();
        var context = BuildContext(query, null, services, eventCoordinator);

        var orchestrator = new GraphOrchestrator<MragPipelineContext>(
            services,
            checkpointStore: null);

        var eventChannel = Channel.CreateUnbounded<Event>();
        using var coordinatorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        eventCoordinator.OnAny(evt =>
        {
            eventChannel.Writer.TryWrite(evt);
            return ValueTask.CompletedTask;
        });

        var coordinatorTask = eventCoordinator.RunAsync(coordinatorCts.Token);

        var executionTask = Task.Run(async () =>
        {
            try
            {
                await orchestrator.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                eventCoordinator.Emit(new GraphDiagnosticEvent
                {
                    Level = LogLevel.Error,
                    Source = "MragRetrievalPipeline",
                    Message = ex.Message,
                    Exception = ex
                });
            }
            finally
            {
                eventChannel.Writer.TryComplete();
                await coordinatorCts.CancelAsync().ConfigureAwait(false);
            }
        }, ct);

        await foreach (var evt in eventChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var mapped = MragEventMapper.MapRetrievalEvent(evt, PipelineName, query);
            if (mapped != null)
                yield return mapped;

            if (context.IsComplete || context.IsCancelled)
                break;
        }

        await executionTask.ConfigureAwait(false);
        await SuppressCancellationAsync(coordinatorTask).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ //
    // Helpers                                                              //
    // ------------------------------------------------------------------ //

    private MragPipelineContext BuildContext(string query, MragFilterNode? filter, IServiceProvider services, EventCoordinator ec)
    {
        var ctx = new MragPipelineContext(
            executionId: Guid.NewGuid().ToString("N"),
            graph: _graph,
            services: services,
            pipelineName: PipelineName,
            filter: filter)
        {
            EventCoordinator = ec
        };

        // Seed the query into the graph channels so the first node (EmbedQueryHandler etc.)
        // can read it via their InputSocket named "Query".
        ctx.Channels["query"].Set(query);
        ctx.Channels["Query"].Set(query);

        return ctx;
    }

    private static string ExtractContextOutput(MragPipelineContext context)
    {
        // Walk all node_output channels looking for the "Context" / "context" socket value.
        // FormatContextHandler emits output under socket name "Context".
        foreach (var channelName in context.Channels.ChannelNames)
        {
            if (!channelName.StartsWith("node_output:", StringComparison.Ordinal))
                continue;

            var outputs = context.Channels[channelName].Get<Dictionary<string, object>>();
            if (outputs == null) continue;

            if (outputs.TryGetValue("Context", out var val) && val is string s1)
                return s1;
            if (outputs.TryGetValue("context", out var val2) && val2 is string s2)
                return s2;
        }

        return string.Empty;
    }

    private static async Task SuppressCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
