using System.Runtime.CompilerServices;
using HPD.Events.Core;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.Events;
using HPD.RAG.Pipeline.Internal;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Orchestration;

namespace HPD.RAG.Pipeline;

/// <summary>
/// A compiled, ready-to-run ingestion pipeline produced by
/// <see cref="MragPipeline.BuildIngestionAsync"/>.
///
/// <para>Holds an HPD.Graph <see cref="Graph"/> and exposes two execution modes:</para>
/// <list type="bullet">
///   <item><see cref="RunAsync"/> — awaits completion, discards events.</item>
///   <item><see cref="RunStreamingAsync"/> — streams <see cref="MragEvent"/> instances
///         as the pipeline progresses, following the AgentWorkflowInstance event-coordinator pattern.</item>
/// </list>
///
/// <para>
/// Recognised input keys (passed via <c>inputs</c> dictionary):
/// <list type="bullet">
///   <item><c>"file_paths"</c> — <c>string[]</c> — documents to ingest (required by reader nodes)</item>
///   <item><c>"tags"</c> — <c>Dictionary&lt;string,string&gt;?</c> — run tags merged into all chunks</item>
///   <item><c>"collection"</c> — <c>string?</c> — override the target collection for this run</item>
/// </list>
/// </para>
/// </summary>
public sealed class MragIngestionPipeline
{
    private readonly Graph _graph;
    private readonly IServiceProvider _pipelineServices;

    /// <summary>Human-readable name of this pipeline.</summary>
    public string PipelineName { get; }

    internal MragIngestionPipeline(string pipelineName, Graph graph, IServiceProvider pipelineServices)
    {
        PipelineName = pipelineName;
        _graph = graph;
        _pipelineServices = pipelineServices;
    }

    // ------------------------------------------------------------------ //
    // RunAsync                                                             //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Executes the ingestion pipeline to completion.
    /// Events are discarded; use <see cref="RunStreamingAsync"/> to observe progress.
    /// </summary>
    /// <param name="inputs">
    /// Run inputs. Recognised keys: <c>"file_paths"</c> (<c>string[]</c>),
    /// <c>"tags"</c> (<c>Dictionary&lt;string,string&gt;</c>), <c>"collection"</c> (<c>string</c>).
    /// </param>
    /// <param name="services">
    /// Application <see cref="IServiceProvider"/> containing handler registrations
    /// (keyed <c>IChatClient</c>, <c>IEmbeddingGenerator</c>, <c>IVectorStore</c>,
    /// user-defined <c>IMragProcessor</c> / <c>IMragRouter</c> implementations, etc.).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RunAsync(
        Dictionary<string, object> inputs,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        await foreach (var _ in RunStreamingAsync(inputs, services, ct).ConfigureAwait(false))
        {
            // drain the event stream; caller doesn't need events
        }
    }

    // ------------------------------------------------------------------ //
    // RunStreamingAsync                                                    //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Executes the ingestion pipeline and streams <see cref="MragEvent"/> instances as it progresses.
    ///
    /// <para>
    /// Implementation follows the AgentWorkflowInstance event coordinator pattern:
    /// graph execution starts in a background task, and events are consumed from
    /// <see cref="EventCoordinator.ReadAllAsync"/> until completion.
    /// </para>
    ///
    /// <para>
    /// Raw graph events are translated to strongly-typed MRAG ingestion events by
    /// <see cref="MragEventMapper.MapIngestionEvent"/>. After the main loop, synthetic
    /// <see cref="DocumentSkippedEvent"/> instances are emitted for every document
    /// that was submitted but not written (e.g. unchanged documents deduped by the writer).
    /// </para>
    /// </summary>
    public async IAsyncEnumerable<MragEvent> RunStreamingAsync(
        Dictionary<string, object> inputs,
        IServiceProvider services,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var eventCoordinator = new EventCoordinator();
        var context = BuildContext(inputs, services, eventCoordinator);

        // Build the per-run ingestion context for the mapper
        inputs.TryGetValue("collection", out var collectionObj);
        var ingestionCtx = new IngestionEventContext
        {
            CollectionName = (collectionObj as string) ?? string.Empty
        };

        // Resolve submitted file paths for skipped-document detection
        string[]? submittedFilePaths = null;
        if (inputs.TryGetValue("file_paths", out var fpObj) && fpObj is string[] fps)
            submittedFilePaths = fps;

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
                    Source = "MragIngestionPipeline",
                    Message = ex.Message,
                    Exception = ex
                });
            }
        }, ct);

        await foreach (var evt in eventCoordinator.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var mapped = MragEventMapper.MapIngestionEvent(evt, PipelineName, ingestionCtx);
            if (mapped != null)
                yield return mapped;

            if (context.IsComplete || context.IsCancelled)
                break;
        }

        await executionTask.ConfigureAwait(false);

        // Emit synthetic DocumentSkippedEvent for every submitted document that was
        // not recorded as written by the writer handler.
        if (submittedFilePaths != null)
        {
            foreach (var filePath in submittedFilePaths)
            {
                if (!ingestionCtx.WrittenDocumentIds.Contains(filePath))
                {
                    yield return new DocumentSkippedEvent
                    {
                        PipelineName = PipelineName,
                        DocumentId   = filePath,
                        Reason       = "unchanged"
                    };
                }
            }
        }
    }

    // ------------------------------------------------------------------ //
    // Context construction                                                 //
    // ------------------------------------------------------------------ //

    private MragPipelineContext BuildContext(
        Dictionary<string, object> inputs,
        IServiceProvider services,
        EventCoordinator eventCoordinator)
    {
        inputs.TryGetValue("collection", out var collectionObj);
        inputs.TryGetValue("tags", out var tagsObj);

        var collection = collectionObj as string;
        var tags = tagsObj as IReadOnlyDictionary<string, string>
                ?? (tagsObj as Dictionary<string, string>);

        var ctx = new MragPipelineContext(
            executionId: Guid.NewGuid().ToString("N"),
            graph: _graph,
            services: services,
            pipelineName: PipelineName,
            collectionName: collection,
            runTags: tags)
        {
            EventCoordinator = eventCoordinator
        };

        // Seed file paths into the graph channel if provided
        if (inputs.TryGetValue("file_paths", out var filePathsObj) && filePathsObj is string[] filePaths)
        {
            ctx.Channels["file_paths"].Set(filePaths);
        }

        return ctx;
    }
}
