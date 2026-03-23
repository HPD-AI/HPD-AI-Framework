using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Core.Serialization;
using Xunit;

namespace HPD.RAG.Core.Tests;

/// <summary>T-030 through T-034: MragJsonSerializerContext tests.</summary>
public class SerializationTests
{
    // T-030
    [Fact]
    public void MragJsonSerializerContext_HasTypeInfo_ForAllDtoTypes()
    {
        var ctx = MragJsonSerializerContext.Shared;

        Assert.NotNull(ctx.GetTypeInfo(typeof(MragDocumentDto)));
        Assert.NotNull(ctx.GetTypeInfo(typeof(MragChunkDto)));
        Assert.NotNull(ctx.GetTypeInfo(typeof(MragSearchResultDto)));
        Assert.NotNull(ctx.GetTypeInfo(typeof(MragGraphResultDto)));
        Assert.NotNull(ctx.GetTypeInfo(typeof(MragFilterNode)));
        Assert.NotNull(ctx.GetTypeInfo(typeof(MragMetricsDto)));
        Assert.NotNull(ctx.GetTypeInfo(typeof(MragFormat)));
    }

    // T-031
    [Fact]
    public void MragJsonSerializerContext_SerializesArray_OfChunks()
    {
        var chunks = new MragChunkDto[]
        {
            new() { DocumentId = "doc-1", Content = "Content 1" },
            new() { DocumentId = "doc-2", Content = "Content 2" },
            new() { DocumentId = "doc-3", Content = "Content 3" }
        };

        var json = JsonSerializer.Serialize(chunks, MragJsonSerializerContext.Shared.MragChunkDtoArray);
        var deserialized = JsonSerializer.Deserialize(json, MragJsonSerializerContext.Shared.MragChunkDtoArray);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.Length);
        Assert.Equal("doc-1", deserialized[0].DocumentId);
        Assert.Equal("doc-2", deserialized[1].DocumentId);
        Assert.Equal("doc-3", deserialized[2].DocumentId);
    }

    // T-032
    [Fact]
    public void MragJsonSerializerContext_SerializesJaggedArray_OfChunks()
    {
        var jagged = new MragChunkDto[][]
        {
            [new() { DocumentId = "doc-1", Content = "c1" }, new() { DocumentId = "doc-1", Content = "c2" }],
            [new() { DocumentId = "doc-2", Content = "c3" }]
        };

        var json = JsonSerializer.Serialize(jagged, MragJsonSerializerContext.Shared.MragChunkDtoArrayArray);
        var deserialized = JsonSerializer.Deserialize(json, MragJsonSerializerContext.Shared.MragChunkDtoArrayArray);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Length);
        Assert.Equal(2, deserialized[0].Length);
        Assert.Single(deserialized[1]);
        Assert.Equal("doc-1", deserialized[0][0].DocumentId);
        Assert.Equal("doc-2", deserialized[1][0].DocumentId);
    }

    // T-033
    [Fact]
    public void MragJsonSerializerContext_CamelCasePropertyNaming()
    {
        var chunk = new MragChunkDto
        {
            DocumentId = "doc-1",
            Content = "content"
        };

        var json = JsonSerializer.Serialize(chunk, MragJsonSerializerContext.Shared.MragChunkDto);

        Assert.Contains("\"documentId\"", json);
        Assert.DoesNotContain("\"DocumentId\"", json);
    }

    // T-034
    [Fact]
    public void MragJsonSerializerContext_NullsOmitted_InJson()
    {
        var chunk = new MragChunkDto
        {
            DocumentId = "doc-1",
            Content = "content",
            Context = null,
            Metadata = null
        };

        var json = JsonSerializer.Serialize(chunk, MragJsonSerializerContext.Shared.MragChunkDto);

        Assert.DoesNotContain("\"context\"", json);
        Assert.DoesNotContain("\"metadata\"", json);
    }
}
