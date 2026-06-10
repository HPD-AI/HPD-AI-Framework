using HPD.RAG.Core.Filters;
using HPD.Graph.Abstractions.Channels;
using HPD.Graph.Abstractions.Context;
using HPD.Graph.Abstractions.State;
using HPD.Graph.Core.Context;

namespace HPD.RAG.Core.Context;

/// <summary>
/// Execution context for all MRAG pipeline handlers.
/// Carries run-level state that is constant for the entire pipeline execution.
///
/// Key design: socket values carry data that flows and transforms between nodes
/// (MragChunkDto[], MragSearchResultDto[], float[] embeddings).
/// Context carries data that is CONSTANT for the whole run — collection name,
/// run tags, pipeline name. Tags are the clearest example: they live here
/// and writer handlers read them directly from context.RunTags, rather than
/// threading them through every socket.
/// </summary>
public sealed class MragPipelineContext : GraphContext
{
    /// <summary>Human-readable name for this pipeline execution (for logging/events).</summary>
    public string PipelineName { get; init; } = string.Empty;

    /// <summary>
    /// Default collection name for all vector store handlers in this run.
    /// Handlers may override this via their per-node Config.CollectionName.
    /// </summary>
    public string? CollectionName { get; init; }

    /// <summary>
    /// Tags to propagate to all chunks written during this run.
    /// Writer handlers read these at upsert time and merge them into each vector record.
    /// No intermediate handler needs to declare or pass tags as socket values.
    /// </summary>
    public IReadOnlyDictionary<string, string>? RunTags { get; init; }

    /// <summary>
    /// Corpus version identifier. Used for collection naming conventions (e.g. "docs_v2").
    /// Deferred to v2 for explicit API surface — collection naming conventions are sufficient in v1.
    /// </summary>
    public string? CorpusVersion { get; init; }

    /// <summary>
    /// Optional filter to scope retrieval results — translated to native vector store syntax
    /// by the handler's <see cref="IMragFilterTranslator"/> implementation.
    /// Null means no filter (retrieve broadly across all records).
    /// Use <see cref="MragFilter"/> to construct: MragFilter.Tag("folder", "/knowledge"),
    /// MragFilter.And(...), etc.
    /// </summary>
    public MragFilterNode? Filter { get; init; }

    public MragPipelineContext(
        string executionId,
        HPD.Graph.Abstractions.Graph.Graph graph,
        IServiceProvider services,
        string pipelineName,
        string? collectionName = null,
        IReadOnlyDictionary<string, string>? runTags = null,
        string? corpusVersion = null,
        MragFilterNode? filter = null,
        IGraphChannelSet? channels = null,
        IManagedContext? managed = null)
        : base(executionId, graph, services, channels, managed)
    {
        PipelineName = pipelineName;
        CollectionName = collectionName;
        RunTags = runTags;
        CorpusVersion = corpusVersion;
        Filter = filter;
    }

    /// <inheritdoc/>
    public override IGraphContext CreateIsolatedCopy()
    {
        var copy = new MragPipelineContext(
            ExecutionId,
            Graph,
            Services,
            PipelineName,
            CollectionName,
            RunTags,
            CorpusVersion,
            Filter,
            CloneChannelsInternal(),
            Managed)
        {
            CurrentLayerIndex = CurrentLayerIndex,
            EventCoordinator = EventCoordinator
        };

        foreach (var nodeId in CompletedNodes)
            copy.MarkNodeComplete(nodeId);

        return copy;
    }

    private HPD.Graph.Core.Channels.GraphChannelSet CloneChannelsInternal()
    {
        var cloned = new HPD.Graph.Core.Channels.GraphChannelSet();
        foreach (var channelName in Channels.ChannelNames)
        {
            if (channelName.StartsWith("node_output:"))
            {
                var outputs = Channels[channelName].Get<Dictionary<string, object>>();
                if (outputs != null)
                    cloned[channelName].Set(outputs);
            }
        }
        return cloned;
    }
}
