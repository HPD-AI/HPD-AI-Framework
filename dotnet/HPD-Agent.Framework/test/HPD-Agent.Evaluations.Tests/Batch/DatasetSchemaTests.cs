// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Agent.Evaluations.Batch;

namespace HPD.Agent.Evaluations.Tests.Batch;

public sealed class DatasetSchemaTests
{
    [Fact]
    public void GenerateJsonSchema_IncludesDatasetEnvelope()
    {
        var schema = Dataset<string>.GenerateJsonSchema();

        using var document = JsonDocument.Parse(schema);
        var root = document.RootElement;

        root.GetProperty("$schema").GetString().Should().Be("http://json-schema.org/draft-07/schema#");
        root.GetProperty("type").GetString().Should().Be("object");
        root.GetProperty("properties").TryGetProperty("cases", out _).Should().BeTrue();
        root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Contain("cases");
    }

    [Fact]
    public void GenerateJsonSchema_IncludesInputTypeProperties()
    {
        var schema = Dataset<SearchInput>.GenerateJsonSchema();

        using var document = JsonDocument.Parse(schema);
        var inputProperties = document.RootElement
            .GetProperty("properties")
            .GetProperty("cases")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("input")
            .GetProperty("properties");

        inputProperties.TryGetProperty("query", out var query).Should().BeTrue();
        query.GetProperty("type").GetString().Should().Be("string");

        inputProperties.TryGetProperty("max_results", out var maxResults).Should().BeTrue();
        maxResults.GetProperty("type").GetString().Should().Be("integer");
    }

    [Fact]
    public void GenerateJsonSchema_WithJsonTypeInfo_IncludesInputTypeProperties()
    {
        var schema = Dataset<SearchInput>.GenerateJsonSchema(DatasetSchemaJsonContext.Default.SearchInput);

        using var document = JsonDocument.Parse(schema);
        var inputProperties = document.RootElement
            .GetProperty("properties")
            .GetProperty("cases")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("input")
            .GetProperty("properties");

        inputProperties.TryGetProperty("query", out var query).Should().BeTrue();
        query.GetProperty("type").GetString().Should().Be("string");

        inputProperties.TryGetProperty("max_results", out var maxResults).Should().BeTrue();
        maxResults.GetProperty("type").GetString().Should().Be("integer");
    }

    [Fact]
    public void GenerateJsonSchema_IncludesEvaluatorDeclarations()
    {
        var schema = Dataset<string>.GenerateJsonSchema();

        using var document = JsonDocument.Parse(schema);
        var rootProperties = document.RootElement.GetProperty("properties");

        rootProperties.GetProperty("evaluators")
            .GetProperty("items")
            .GetProperty("oneOf")
            .EnumerateArray()
            .Should().HaveCount(2);

        var caseProperties = rootProperties
            .GetProperty("cases")
            .GetProperty("items")
            .GetProperty("properties");

        caseProperties.TryGetProperty("evaluators", out _).Should().BeTrue();
    }

    internal sealed record SearchInput(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("max_results")] int MaxResults);
}

[JsonSerializable(typeof(DatasetSchemaTests.SearchInput))]
internal sealed partial class DatasetSchemaJsonContext : JsonSerializerContext;
