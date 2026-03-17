using HPD.RAG.Core.Context;
using HPDAgent.Graph.Abstractions.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Core.Tests.Helpers;

/// <summary>
/// Creates minimal MragPipelineContext instances for unit tests.
/// </summary>
internal static class TestContextFactory
{
    /// <summary>
    /// Minimal Graph stub with no nodes or edges — sufficient for context construction in unit tests.
    /// </summary>
    public static HPDAgent.Graph.Abstractions.Graph.Graph EmptyGraph() =>
        new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = "test-graph",
            Name = "Test Graph",
            Nodes = Array.Empty<Node>(),
            Edges = Array.Empty<Edge>(),
            EntryNodeId = "START",
            ExitNodeId = "END"
        };

    public static IServiceProvider EmptyServices() =>
        new ServiceCollection().BuildServiceProvider();

    public static MragPipelineContext Create(
        string pipelineName = "Test Pipeline",
        string? collectionName = null,
        IReadOnlyDictionary<string, string>? runTags = null,
        string? corpusVersion = null)
    {
        return new MragPipelineContext(
            executionId: Guid.NewGuid().ToString(),
            graph: EmptyGraph(),
            services: EmptyServices(),
            pipelineName: pipelineName,
            collectionName: collectionName,
            runTags: runTags,
            corpusVersion: corpusVersion);
    }
}
