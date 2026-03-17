using HPD.RAG.Core.Context;
using HPDAgent.Graph.Abstractions.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Handlers.Tests.Shared;

/// <summary>
/// Helper to construct a minimal MragPipelineContext suitable for unit tests.
/// </summary>
internal static class HandlerTestContext
{
    /// <summary>
    /// Minimal empty graph used as a placeholder in tests that do not need real graph execution.
    /// </summary>
    private static readonly Graph MinimalGraph = new()
    {
        Id = "test-graph",
        Name = "Test Graph",
        EntryNodeId = "START",
        ExitNodeId = "END",
        Nodes = new List<Node>
        {
            new() { Id = "START", Name = "Start", Type = NodeType.Start },
            new() { Id = "END",   Name = "End",   Type = NodeType.End }
        },
        Edges = new List<Edge>()
    };

    /// <summary>
    /// Creates a MragPipelineContext with the given service collection and optional run tags.
    /// </summary>
    public static MragPipelineContext Create(
        IServiceCollection? services = null,
        string pipelineName = "test-pipeline",
        string? collectionName = null,
        IReadOnlyDictionary<string, string>? runTags = null)
    {
        var sp = (services ?? new ServiceCollection()).BuildServiceProvider();
        return new MragPipelineContext(
            executionId: Guid.NewGuid().ToString(),
            graph: MinimalGraph,
            services: sp,
            pipelineName: pipelineName,
            collectionName: collectionName,
            runTags: runTags);
    }

    /// <summary>
    /// Creates a MragPipelineContext from an already-built IServiceProvider.
    /// </summary>
    public static MragPipelineContext CreateWithProvider(
        IServiceProvider sp,
        string pipelineName = "test-pipeline",
        string? collectionName = null,
        IReadOnlyDictionary<string, string>? runTags = null)
    {
        return new MragPipelineContext(
            executionId: Guid.NewGuid().ToString(),
            graph: MinimalGraph,
            services: sp,
            pipelineName: pipelineName,
            collectionName: collectionName,
            runTags: runTags);
    }
}
