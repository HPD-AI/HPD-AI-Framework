using System.Text.Json;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Serialization;
using Xunit;

namespace HPD.RAG.Core.Tests;

/// <summary>T-001 through T-007: DTO serialization tests.</summary>
public class DtoTests
{
    // T-001
    [Fact]
    public void MragDocumentDto_Roundtrips_ThroughJsonSerialization()
    {
        var original = new MragDocumentDto
        {
            Id = "doc-1",
            Elements =
            [
                new MragDocumentElementDto
                {
                    Type = "paragraph",
                    Text = "Hello world",
                    PageNumber = 1
                },
                new MragDocumentElementDto
                {
                    Type = "header",
                    Text = "Introduction",
                    HeaderLevel = 2,
                    PageNumber = 1
                },
                new MragDocumentElementDto
                {
                    Type = "image",
                    Base64Content = "aGVsbG8=",
                    MediaType = "image/png",
                    AlternativeText = "A test image",
                    PageNumber = 2
                }
            ]
        };

        var json = JsonSerializer.Serialize(original, MragJsonSerializerContext.Shared.MragDocumentDto);
        var deserialized = JsonSerializer.Deserialize(json, MragJsonSerializerContext.Shared.MragDocumentDto);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Elements.Length, deserialized.Elements.Length);

        var imgElem = deserialized.Elements[2];
        Assert.Equal("aGVsbG8=", imgElem.Base64Content);
        Assert.Equal(2, deserialized.Elements[1].HeaderLevel);
        Assert.Equal(2, imgElem.PageNumber);
    }

    // T-002
    [Fact]
    public void MragChunkDto_WithMetadata_RoundtripsJsonElement()
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alice", MragJsonSerializerContext.Shared.String),
            ["count"] = JsonSerializer.SerializeToElement(42, MragJsonSerializerContext.Shared.Int32),
            ["active"] = JsonSerializer.SerializeToElement(true, MragJsonSerializerContext.Shared.Boolean),
            ["tags"] = JsonSerializer.SerializeToElement(new[] { "a", "b" }, MragJsonSerializerContext.Shared.StringArray)
        };

        var original = new MragChunkDto
        {
            DocumentId = "doc-1",
            Content = "Some content",
            Metadata = metadata
        };

        var json = JsonSerializer.Serialize(original, MragJsonSerializerContext.Shared.MragChunkDto);
        var deserialized = JsonSerializer.Deserialize(json, MragJsonSerializerContext.Shared.MragChunkDto);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Metadata);
        Assert.Equal("Alice", deserialized.Metadata["name"].GetString());
        Assert.Equal(42, deserialized.Metadata["count"].GetInt32());
        Assert.True(deserialized.Metadata["active"].GetBoolean());
        Assert.Equal(2, deserialized.Metadata["tags"].GetArrayLength());
    }

    // T-003
    [Fact]
    public void MragSearchResultDto_Roundtrips_ThroughJsonSerialization()
    {
        var original = new MragSearchResultDto
        {
            DocumentId = "doc-1",
            Content = "Search result content",
            Context = "Section context",
            Score = 0.987654321,
            Metadata = new Dictionary<string, JsonElement>
            {
                ["key"] = JsonSerializer.SerializeToElement("value", MragJsonSerializerContext.Shared.String)
            }
        };

        var json = JsonSerializer.Serialize(original, MragJsonSerializerContext.Shared.MragSearchResultDto);
        var deserialized = JsonSerializer.Deserialize(json, MragJsonSerializerContext.Shared.MragSearchResultDto);

        Assert.NotNull(deserialized);
        Assert.Equal(original.DocumentId, deserialized.DocumentId);
        Assert.Equal(original.Content, deserialized.Content);
        Assert.Equal(original.Context, deserialized.Context);
        Assert.Equal(original.Score, deserialized.Score);
        Assert.NotNull(deserialized.Metadata);
        Assert.Equal("value", deserialized.Metadata["key"].GetString());
    }

    // T-004
    [Fact]
    public void MragGraphDtos_Roundtrip_ThroughJsonSerialization()
    {
        var nodeProp = JsonSerializer.SerializeToElement("propVal", MragJsonSerializerContext.Shared.String);
        var original = new MragGraphResultDto
        {
            IsTruncated = true,
            Nodes =
            [
                new MragGraphNodeDto
                {
                    Id = "node-1",
                    Label = "Person",
                    Properties = new Dictionary<string, JsonElement> { ["name"] = nodeProp }
                }
            ],
            Edges =
            [
                new MragGraphEdgeDto
                {
                    SourceId = "node-1",
                    TargetId = "node-2",
                    Type = "KNOWS",
                    Properties = new Dictionary<string, JsonElement> { ["since"] = JsonSerializer.SerializeToElement(2020, MragJsonSerializerContext.Shared.Int32) }
                }
            ]
        };

        var json = JsonSerializer.Serialize(original, MragJsonSerializerContext.Shared.MragGraphResultDto);
        var deserialized = JsonSerializer.Deserialize(json, MragJsonSerializerContext.Shared.MragGraphResultDto);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsTruncated);
        Assert.Single(deserialized.Nodes);
        Assert.Single(deserialized.Edges);
        Assert.Equal("node-1", deserialized.Nodes[0].Id);
        Assert.Equal("Person", deserialized.Nodes[0].Label);
        Assert.NotNull(deserialized.Nodes[0].Properties);
        Assert.Equal("propVal", deserialized.Nodes[0].Properties!["name"].GetString());
        Assert.Equal("KNOWS", deserialized.Edges[0].Type);
    }

    // T-005
    [Fact]
    public void MragMetricsDto_WithReasonsNull_Serializes()
    {
        var original = new MragMetricsDto
        {
            Scores = new Dictionary<string, double> { ["relevance"] = 0.9 },
            Reasons = null
        };

        var json = JsonSerializer.Serialize(original, MragJsonSerializerContext.Shared.MragMetricsDto);
        var deserialized = JsonSerializer.Deserialize(json, MragJsonSerializerContext.Shared.MragMetricsDto);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Reasons);
        Assert.DoesNotContain("\"reasons\"", json);
    }

    // T-006
    [Fact]
    public void MragChunkDto_NullMetadata_SerializesCleanly()
    {
        var original = new MragChunkDto
        {
            DocumentId = "doc-1",
            Content = "content",
            Metadata = null
        };

        var json = JsonSerializer.Serialize(original, MragJsonSerializerContext.Shared.MragChunkDto);

        Assert.DoesNotContain("\"metadata\"", json);
    }

    // T-007
    [Fact]
    public void MragDocumentElementDto_ImageElement_AllFieldsPresent()
    {
        var original = new MragDocumentElementDto
        {
            Type = "image",
            Base64Content = "aGVsbG8gd29ybGQ=",
            MediaType = "image/jpeg",
            AlternativeText = "A beautiful sunset"
        };

        var json = JsonSerializer.Serialize(original, MragJsonSerializerContext.Shared.MragDocumentElementDto);
        var deserialized = JsonSerializer.Deserialize(json, MragJsonSerializerContext.Shared.MragDocumentElementDto);

        Assert.NotNull(deserialized);
        Assert.Equal("aGVsbG8gd29ybGQ=", deserialized.Base64Content);
        Assert.Equal("image/jpeg", deserialized.MediaType);
        Assert.Equal("A beautiful sunset", deserialized.AlternativeText);
        Assert.Equal("image", deserialized.Type);
    }
}
