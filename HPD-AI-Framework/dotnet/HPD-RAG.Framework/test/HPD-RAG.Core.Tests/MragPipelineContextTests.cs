using HPD.RAG.Core.Context;
using HPD.RAG.Core.Tests.Helpers;
using Xunit;

namespace HPD.RAG.Core.Tests;

/// <summary>T-008 through T-014: MragPipelineContext tests.</summary>
public class MragPipelineContextTests
{
    // T-008
    [Fact]
    public void MragPipelineContext_CanBeConstructed_WithRequiredArgs()
    {
        var ctx = TestContextFactory.Create(pipelineName: "MyPipeline");

        Assert.NotNull(ctx);
        Assert.Equal("MyPipeline", ctx.PipelineName);
    }

    // T-009
    [Fact]
    public void MragPipelineContext_RunTags_IsNullByDefault()
    {
        var ctx = TestContextFactory.Create();

        Assert.Null(ctx.RunTags);
    }

    // T-010
    [Fact]
    public void MragPipelineContext_RunTags_IsReadOnly()
    {
        var tags = new Dictionary<string, string> { ["env"] = "prod" };
        var ctx = TestContextFactory.Create(runTags: tags);

        // The property type is IReadOnlyDictionary<string, string> — cannot call .Add() at compile time.
        // Verify it is assignable to IReadOnlyDictionary<string, string>
        IReadOnlyDictionary<string, string>? readOnly = ctx.RunTags;
        Assert.NotNull(readOnly);
        Assert.Equal("prod", readOnly["env"]);
    }

    // T-011
    [Fact]
    public void MragPipelineContext_CreateIsolatedCopy_PreservesRunTags()
    {
        var tags = new Dictionary<string, string> { ["userId"] = "u-1" };
        var ctx = TestContextFactory.Create(runTags: tags);

        var copy = (MragPipelineContext)ctx.CreateIsolatedCopy();

        Assert.Same(ctx.RunTags, copy.RunTags);
    }

    // T-012
    [Fact]
    public void MragPipelineContext_CreateIsolatedCopy_PreservesPipelineName()
    {
        var ctx = TestContextFactory.Create(
            pipelineName: "Ingestion Pipeline",
            collectionName: "docs",
            corpusVersion: "v2");

        var copy = (MragPipelineContext)ctx.CreateIsolatedCopy();

        Assert.Equal(ctx.PipelineName, copy.PipelineName);
        Assert.Equal(ctx.CollectionName, copy.CollectionName);
        Assert.Equal(ctx.CorpusVersion, copy.CorpusVersion);
    }

    // T-013
    [Fact]
    public void MragPipelineContext_CreateIsolatedCopy_CompletedNodesAreCopied()
    {
        var ctx = TestContextFactory.Create();
        ctx.MarkNodeComplete("node-a");
        ctx.MarkNodeComplete("node-b");

        var copy = (MragPipelineContext)ctx.CreateIsolatedCopy();

        Assert.Contains("node-a", copy.CompletedNodes);
        Assert.Contains("node-b", copy.CompletedNodes);
    }

    // T-014
    [Fact]
    public void MragPipelineContext_CreateIsolatedCopy_MutatingCopyDoesNotAffectOriginal()
    {
        var ctx = TestContextFactory.Create();
        ctx.MarkNodeComplete("node-a");

        var copy = (MragPipelineContext)ctx.CreateIsolatedCopy();
        copy.MarkNodeComplete("node-copy-only");

        Assert.DoesNotContain("node-copy-only", ctx.CompletedNodes);
    }
}
