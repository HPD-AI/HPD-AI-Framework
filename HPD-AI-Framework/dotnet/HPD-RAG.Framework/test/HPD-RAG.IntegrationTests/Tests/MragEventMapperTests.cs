using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Events;
using HPD.RAG.Pipeline;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using System.Reflection;
using Xunit;

namespace HPD.RAG.IntegrationTests.Tests;

/// <summary>
/// Group 5: Event mapper tests — verify MragEventMapper maps raw HPD.Graph events to the
/// correct strongly-typed MRAG domain events.
///
/// MragEventMapper is internal. Tests invoke it via reflection so that the public API
/// contract (pipeline → event type) is verified without requiring InternalsVisibleTo.
/// Each test constructs a raw graph event, invokes the mapper via reflection, and asserts
/// the returned MragEvent type and key properties.
/// </summary>
public sealed class MragEventMapperTests
{
    private const string PipelineName = "test-pipeline";

    // Cached reflection handles — resolved once per test class instantiation.
    private static readonly Assembly _pipelineAssembly =
        typeof(MragPipeline).Assembly;

    private static readonly Type _mapperType =
        _pipelineAssembly.GetType("HPD.RAG.Pipeline.Internal.MragEventMapper")!;

    private static readonly Type _ingestionCtxType =
        _pipelineAssembly.GetType("HPD.RAG.Pipeline.Internal.IngestionEventContext")!;

    private static readonly MethodInfo _mapIngestion =
        _mapperType.GetMethod("MapIngestionEvent",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo _mapRetrieval =
        _mapperType.GetMethod("MapRetrievalEvent",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo _mapEvaluation =
        _mapperType.GetMethod("MapEvaluationEvent",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    // ------------------------------------------------------------------ //
    // Helper                                                               //
    // ------------------------------------------------------------------ //

    /// <summary>Creates a fresh IngestionEventContext via reflection with an optional collection name.</summary>
    private static object MakeIngestionCtx(string collection = "test-collection")
    {
        var ctx = Activator.CreateInstance(_ingestionCtxType)!;
        _ingestionCtxType.GetProperty("CollectionName")!.SetValue(ctx, collection);
        return ctx;
    }

    /// <summary>Invokes MapIngestionEvent(rawEvent, pipelineName, ctx) via reflection.</summary>
    private static MragEvent? InvokeMapIngestion(object raw, object ctx)
        => (MragEvent?)_mapIngestion.Invoke(null, [raw, PipelineName, ctx]);

    /// <summary>Invokes MapRetrievalEvent(rawEvent, pipelineName, query) via reflection.</summary>
    private static MragEvent? InvokeMapRetrieval(object raw, string query = "test query")
        => (MragEvent?)_mapRetrieval.Invoke(null, [raw, PipelineName, query]);

    /// <summary>Invokes MapEvaluationEvent(rawEvent, pipelineName, accumulator) via reflection.</summary>
    private static MragEvent? InvokeMapEvaluation(object raw,
        List<IReadOnlyDictionary<string, double>>? accumulator = null)
    {
        accumulator ??= [];
        return (MragEvent?)_mapEvaluation.Invoke(null, [raw, PipelineName, accumulator]);
    }

    // ------------------------------------------------------------------ //
    // Sanity: reflection wiring works                                      //
    // ------------------------------------------------------------------ //

    [Fact]
    public void ReflectionSetup_MapperTypeResolved()
    {
        Assert.NotNull(_mapperType);
        Assert.NotNull(_ingestionCtxType);
        Assert.NotNull(_mapIngestion);
        Assert.NotNull(_mapRetrieval);
        Assert.NotNull(_mapEvaluation);
    }

    // T-047
    [Fact]
    public void MapIngestionEvent_GraphExecutionStarted_YieldsIngestionStartedEvent()
    {
        var raw = new GraphExecutionStartedEvent { NodeCount = 3 };
        var ctx = MakeIngestionCtx("test-collection");

        var result = InvokeMapIngestion(raw, ctx);

        var typed = Assert.IsType<IngestionStartedEvent>(result);
        Assert.Equal(PipelineName, typed.PipelineName);
        Assert.Equal(3, typed.DocumentCount);
        Assert.Equal("test-collection", typed.CollectionName);
    }

    // T-048
    [Fact]
    public void MapIngestionEvent_GraphExecutionCompleted_YieldsIngestionCompletedEvent()
    {
        var raw = new GraphExecutionCompletedEvent { Duration = TimeSpan.FromSeconds(2) };
        var ctx = MakeIngestionCtx();
        _ingestionCtxType.GetProperty("WrittenChunks")!.SetValue(ctx, 8);
        _ingestionCtxType.GetProperty("TotalDocumentCount")!.SetValue(ctx, 2);

        var result = InvokeMapIngestion(raw, ctx);

        var typed = Assert.IsType<IngestionCompletedEvent>(result);
        Assert.Equal(PipelineName, typed.PipelineName);
        Assert.Equal(8, typed.WrittenChunks);
        Assert.Equal(TimeSpan.FromSeconds(2), typed.Duration);
    }

    // T-049
    [Fact]
    public void MapIngestionEvent_NodeCompleted_ReaderHandler_YieldsDocumentReadEvent()
    {
        var outputs = new Dictionary<string, object>
        {
            ["DocumentId"] = "/docs/readme.md",
            ["ElementCount"] = 5
        };
        var raw = new NodeExecutionCompletedEvent
        {
            NodeId = "read-node",
            HandlerName = MragHandlerNames.ReadMarkdown,
            Duration = TimeSpan.FromMilliseconds(100),
            Result = NodeExecutionResult.Success.Single(
                new Dictionary<string, object>(outputs),
                TimeSpan.FromMilliseconds(100),
                new NodeExecutionMetadata()),
            Outputs = outputs
        };
        var ctx = MakeIngestionCtx();

        var result = InvokeMapIngestion(raw, ctx);

        var typed = Assert.IsType<DocumentReadEvent>(result);
        Assert.Equal("/docs/readme.md", typed.DocumentId);
        Assert.Equal(5, typed.ElementCount);
    }

    // T-050
    [Fact]
    public void MapIngestionEvent_NodeCompleted_ChunkerHandler_YieldsChunkingCompletedEvent()
    {
        var outputs = new Dictionary<string, object>
        {
            ["DocumentId"] = "doc-1",
            ["ChunkCount"] = 7
        };
        var raw = new NodeExecutionCompletedEvent
        {
            NodeId = "chunk-node",
            HandlerName = MragHandlerNames.ChunkByHeader,
            Duration = TimeSpan.FromMilliseconds(50),
            Result = NodeExecutionResult.Success.Single(
                new Dictionary<string, object>(outputs),
                TimeSpan.FromMilliseconds(50),
                new NodeExecutionMetadata()),
            Outputs = outputs
        };
        var ctx = MakeIngestionCtx();

        var result = InvokeMapIngestion(raw, ctx);

        var typed = Assert.IsType<ChunkingCompletedEvent>(result);
        Assert.Equal(7, typed.ChunkCount);
    }

    // T-051
    [Fact]
    public void MapIngestionEvent_NodeCompleted_EnricherHandler_YieldsEnrichmentCompletedEvent()
    {
        var outputs = new Dictionary<string, object>
        {
            ["DocumentId"] = "doc-1",
            ["ChunkCount"] = 4
        };
        var raw = new NodeExecutionCompletedEvent
        {
            NodeId = "enrich-node",
            HandlerName = MragHandlerNames.EnrichKeywords,
            Duration = TimeSpan.FromMilliseconds(200),
            Result = NodeExecutionResult.Success.Single(
                new Dictionary<string, object>(outputs),
                TimeSpan.FromMilliseconds(200),
                new NodeExecutionMetadata()),
            Outputs = outputs
        };
        var ctx = MakeIngestionCtx();

        var result = InvokeMapIngestion(raw, ctx);

        var typed = Assert.IsType<EnrichmentCompletedEvent>(result);
        Assert.Equal(MragHandlerNames.EnrichKeywords, typed.EnricherName);
    }

    // T-052
    [Fact]
    public void MapIngestionEvent_NodeCompleted_WriterHandler_YieldsDocumentWrittenEvent()
    {
        var outputs = new Dictionary<string, object>
        {
            ["DocumentId"] = "doc-1",
            ["ChunkCount"] = 3,
            ["CollectionName"] = "my-collection"
        };
        var raw = new NodeExecutionCompletedEvent
        {
            NodeId = "write-node",
            HandlerName = MragHandlerNames.WriteInMemory,
            Duration = TimeSpan.FromMilliseconds(30),
            Result = NodeExecutionResult.Success.Single(
                new Dictionary<string, object>(outputs),
                TimeSpan.FromMilliseconds(30),
                new NodeExecutionMetadata()),
            Outputs = outputs
        };
        var ctx = MakeIngestionCtx();

        var result = InvokeMapIngestion(raw, ctx);

        var typed = Assert.IsType<DocumentWrittenEvent>(result);
        Assert.Equal("doc-1", typed.DocumentId);
        Assert.Equal(3, typed.ChunkCount);
        Assert.Equal("my-collection", typed.CollectionName);
    }

    // T-053
    [Fact]
    public void MapIngestionEvent_NodeCompleted_Failure_YieldsDocumentFailedEvent()
    {
        var exception = new InvalidOperationException("handler failed");
        var raw = new NodeExecutionCompletedEvent
        {
            NodeId = "read-node",
            HandlerName = MragHandlerNames.ReadMarkdown,
            Duration = TimeSpan.FromMilliseconds(10),
            Result = new NodeExecutionResult.Failure(
                exception,
                ErrorSeverity.Fatal,
                IsTransient: false,
                Duration: TimeSpan.FromMilliseconds(10))
        };
        var ctx = MakeIngestionCtx();

        var result = InvokeMapIngestion(raw, ctx);

        var typed = Assert.IsType<DocumentFailedEvent>(result);
        Assert.Same(exception, typed.Exception);
        Assert.Equal("read-node", typed.NodeId);
    }

    // T-054
    [Fact]
    public void MapIngestionEvent_UnknownEvent_YieldsMragRawGraphEvent()
    {
        var raw = new GraphDiagnosticEvent
        {
            Level = LogLevel.Information,
            Source = "test",
            Message = "some log message"
        };
        var ctx = MakeIngestionCtx();

        var result = InvokeMapIngestion(raw, ctx);

        var typed = Assert.IsType<MragRawGraphEvent>(result);
        Assert.Same(raw, typed.UnderlyingEvent);
    }

    // T-055
    [Fact]
    public void MapRetrievalEvent_VectorSearch_YieldsVectorSearchCompletedEvent()
    {
        var outputs = new Dictionary<string, object>
        {
            ["ResultCount"] = 10,
            ["CollectionName"] = "search-collection",
            ["TopK"] = 10
        };
        var raw = new NodeExecutionCompletedEvent
        {
            NodeId = "search-node",
            HandlerName = MragHandlerNames.VectorSearch,
            Duration = TimeSpan.FromMilliseconds(60),
            Result = NodeExecutionResult.Success.Single(
                new Dictionary<string, object>(outputs),
                TimeSpan.FromMilliseconds(60),
                new NodeExecutionMetadata()),
            Outputs = outputs
        };

        var result = InvokeMapRetrieval(raw);

        var typed = Assert.IsType<VectorSearchCompletedEvent>(result);
        Assert.Equal(10, typed.ResultCount);
        Assert.Equal("search-collection", typed.CollectionName);
    }

    // T-056
    [Fact]
    public void MapRetrievalEvent_FormatContext_YieldsContextFormattedEvent()
    {
        var outputs = new Dictionary<string, object>
        {
            ["Format"] = "markdown",
            ["TokenEstimate"] = 512
        };
        var raw = new NodeExecutionCompletedEvent
        {
            NodeId = "format-node",
            HandlerName = MragHandlerNames.FormatContext,
            Duration = TimeSpan.FromMilliseconds(5),
            Result = NodeExecutionResult.Success.Single(
                new Dictionary<string, object>(outputs),
                TimeSpan.FromMilliseconds(5),
                new NodeExecutionMetadata()),
            Outputs = outputs
        };

        var result = InvokeMapRetrieval(raw);

        var typed = Assert.IsType<ContextFormattedEvent>(result);
        Assert.Equal("markdown", typed.Format);
        Assert.Equal(512, typed.TokenEstimate);
    }

    // T-057
    [Fact]
    public void MapEvaluationEvent_BackfillStarted_YieldsEvalStartedEvent()
    {
        var artifactKey = ArtifactKey.FromPath("eval", "corpus");
        var raw = new BackfillStartedEvent
        {
            ArtifactKey = artifactKey,
            TotalPartitions = 5,
            PartitionsToProcess = 5
        };

        var result = InvokeMapEvaluation(raw);

        var typed = Assert.IsType<EvalStartedEvent>(result);
        Assert.Equal(5, typed.ScenarioCount);
        Assert.Equal(PipelineName, typed.PipelineName);
    }

    // T-058
    [Fact]
    public void MapEvaluationEvent_BackfillCompleted_YieldsEvalCompletedEvent()
    {
        var artifactKey = ArtifactKey.FromPath("eval", "corpus");
        var raw = new BackfillCompletedEvent
        {
            ArtifactKey = artifactKey,
            Duration = TimeSpan.FromSeconds(10),
            SuccessfulPartitions = 4,
            FailedPartitions = 1
        };

        var accumulator = new List<IReadOnlyDictionary<string, double>>
        {
            new Dictionary<string, double> { ["Relevance"] = 0.8 },
            new Dictionary<string, double> { ["Relevance"] = 0.9 }
        };

        var result = InvokeMapEvaluation(raw, accumulator);

        var typed = Assert.IsType<EvalCompletedEvent>(result);
        Assert.Equal(1, typed.FailedCount);
        Assert.True(typed.AverageScores.ContainsKey("Relevance"));
        Assert.Equal(0.85, typed.AverageScores["Relevance"], precision: 5);
    }
}
