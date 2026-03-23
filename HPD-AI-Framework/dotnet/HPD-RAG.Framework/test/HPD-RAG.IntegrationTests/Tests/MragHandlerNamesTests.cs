using HPD.RAG.Pipeline;
using Xunit;

namespace HPD.RAG.IntegrationTests.Tests;

/// <summary>
/// Group 3: MragHandlerNames constants — each test asserts the exact locked string value
/// for one constant. These tests act as a compile-time contract: if a constant changes,
/// the test fails and calls attention to the breaking change.
/// </summary>
public sealed class MragHandlerNamesTests
{
    // T-021
    [Fact]
    public void HandlerName_ReadDocuments_IsCorrect()
        => Assert.Equal("ReadDocuments", MragHandlerNames.ReadDocuments);

    // T-022
    [Fact]
    public void HandlerName_ReadMarkdown_IsCorrect()
        => Assert.Equal("ReadMarkdown", MragHandlerNames.ReadMarkdown);

    // T-023
    [Fact]
    public void HandlerName_EnrichImages_IsCorrect()
        => Assert.Equal("EnrichImages", MragHandlerNames.EnrichImages);

    // T-024
    [Fact]
    public void HandlerName_ChunkByHeader_IsCorrect()
        => Assert.Equal("ChunkByHeader", MragHandlerNames.ChunkByHeader);

    // T-025
    [Fact]
    public void HandlerName_ChunkSemantic_IsCorrect()
        => Assert.Equal("ChunkSemantic", MragHandlerNames.ChunkSemantic);

    // T-026
    [Fact]
    public void HandlerName_ChunkBySection_IsCorrect()
        => Assert.Equal("ChunkBySection", MragHandlerNames.ChunkBySection);

    // T-027
    [Fact]
    public void HandlerName_ChunkByToken_IsCorrect()
        => Assert.Equal("ChunkByToken", MragHandlerNames.ChunkByToken);

    // T-028
    [Fact]
    public void HandlerName_EnrichKeywords_IsCorrect()
        => Assert.Equal("EnrichKeywords", MragHandlerNames.EnrichKeywords);

    // T-029
    [Fact]
    public void HandlerName_EnrichSummary_IsCorrect()
        => Assert.Equal("EnrichSummary", MragHandlerNames.EnrichSummary);

    // T-030
    [Fact]
    public void HandlerName_EnrichSentiment_IsCorrect()
        => Assert.Equal("EnrichSentiment", MragHandlerNames.EnrichSentiment);

    // T-031
    [Fact]
    public void HandlerName_ClassifyChunks_IsCorrect()
        => Assert.Equal("ClassifyChunks", MragHandlerNames.ClassifyChunks);

    // T-032
    [Fact]
    public void HandlerName_WriteInMemory_IsCorrect()
        => Assert.Equal("WriteInMemory", MragHandlerNames.WriteInMemory);

    // T-033
    [Fact]
    public void HandlerName_EmbedQuery_IsCorrect()
        => Assert.Equal("EmbedQuery", MragHandlerNames.EmbedQuery);

    // T-034
    [Fact]
    public void HandlerName_VectorSearch_IsCorrect()
        => Assert.Equal("VectorSearch", MragHandlerNames.VectorSearch);

    // T-035
    [Fact]
    public void HandlerName_Rerank_IsCorrect()
        => Assert.Equal("Rerank", MragHandlerNames.Rerank);

    // T-036
    [Fact]
    public void HandlerName_FormatContext_IsCorrect()
        => Assert.Equal("FormatContext", MragHandlerNames.FormatContext);

    // T-037
    [Fact]
    public void HandlerName_EvalRelevance_IsCorrect()
        => Assert.Equal("EvalRelevance", MragHandlerNames.EvalRelevance);

    // Additional handler names beyond the spec minimum

    // T-037b
    [Fact]
    public void HandlerName_HybridSearch_IsCorrect()
        => Assert.Equal("HybridSearch", MragHandlerNames.HybridSearch);

    // T-037c
    [Fact]
    public void HandlerName_GenerateHypothetical_IsCorrect()
        => Assert.Equal("GenerateHypothetical", MragHandlerNames.GenerateHypothetical);

    // T-037d
    [Fact]
    public void HandlerName_DecomposeQuery_IsCorrect()
        => Assert.Equal("DecomposeQuery", MragHandlerNames.DecomposeQuery);

    // T-037e
    [Fact]
    public void HandlerName_MergeResults_IsCorrect()
        => Assert.Equal("MergeResults", MragHandlerNames.MergeResults);

    // T-037f
    [Fact]
    public void HandlerName_GraphRetrieve_IsCorrect()
        => Assert.Equal("GraphRetrieve", MragHandlerNames.GraphRetrieve);

    // T-037g
    [Fact]
    public void HandlerName_EvalGroundedness_IsCorrect()
        => Assert.Equal("EvalGroundedness", MragHandlerNames.EvalGroundedness);

    // T-037h
    [Fact]
    public void HandlerName_EvalFluency_IsCorrect()
        => Assert.Equal("EvalFluency", MragHandlerNames.EvalFluency);

    // T-037i
    [Fact]
    public void HandlerName_EvalCompleteness_IsCorrect()
        => Assert.Equal("EvalCompleteness", MragHandlerNames.EvalCompleteness);

    // T-037j
    [Fact]
    public void HandlerName_EvalBLEU_IsCorrect()
        => Assert.Equal("EvalBLEU", MragHandlerNames.EvalBLEU);

    // T-037k
    [Fact]
    public void HandlerName_WriteEvalResult_IsCorrect()
        => Assert.Equal("WriteEvalResult", MragHandlerNames.WriteEvalResult);
}
