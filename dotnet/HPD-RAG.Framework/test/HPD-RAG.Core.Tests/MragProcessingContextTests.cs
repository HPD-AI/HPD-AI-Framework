using System.Reflection;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.Tests.Helpers;
using Xunit;

namespace HPD.RAG.Core.Tests;

/// <summary>T-015 through T-016: MragProcessingContext tests.</summary>
public class MragProcessingContextTests
{
    private static MragProcessingContext CreateProcessingContext(
        string pipelineName = "Test Pipeline",
        string? collectionName = "my-collection",
        IReadOnlyDictionary<string, string>? runTags = null,
        string? corpusVersion = "v1")
    {
        var pipeline = TestContextFactory.Create(
            pipelineName: pipelineName,
            collectionName: collectionName,
            runTags: runTags,
            corpusVersion: corpusVersion);

        // MragProcessingContext has an internal constructor, create via reflection
        var ctor = typeof(MragProcessingContext)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, [typeof(MragPipelineContext)])!;
        return (MragProcessingContext)ctor.Invoke([pipeline]);
    }

    // T-015
    [Fact]
    public void MragProcessingContext_ExposesCorrectProperties()
    {
        var tags = new Dictionary<string, string> { ["env"] = "test" };
        var ctx = CreateProcessingContext(
            pipelineName: "My Pipeline",
            collectionName: "col-1",
            runTags: tags,
            corpusVersion: "v3");

        Assert.Equal("My Pipeline", ctx.PipelineName);
        Assert.Equal("col-1", ctx.CollectionName);
        Assert.Equal("v3", ctx.CorpusVersion);
        Assert.NotNull(ctx.RunTags);
        Assert.Equal("test", ctx.RunTags["env"]);
        Assert.NotEmpty(ctx.ExecutionId);
    }

    // T-016: Compilation test implemented as reflection test.
    // MragProcessingContext must NOT expose Graph, Channels, CompletedNodes, or Services.
    [Fact]
    public void MragProcessingContext_DoesNotExposeGraphInternals()
    {
        var type = typeof(MragProcessingContext);
        var publicProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        Assert.DoesNotContain("Graph", publicProperties);
        Assert.DoesNotContain("Channels", publicProperties);
        Assert.DoesNotContain("CompletedNodes", publicProperties);
        Assert.DoesNotContain("Services", publicProperties);
    }
}
