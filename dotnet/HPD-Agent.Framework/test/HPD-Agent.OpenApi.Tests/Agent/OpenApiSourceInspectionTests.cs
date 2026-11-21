using System.Text.Json;
using HPD.Agent.OpenApi;
using HPD.OpenApi.Core.Model;

namespace HPD.Agent.OpenApi.Tests.Agent;

public sealed class OpenApiSourceInspectionTests
{
    private static readonly RestApiOperation Operation = new()
    {
        Path = "/pets", Method = HttpMethod.Post
    };

    [Theory]
    [InlineData("{\"type\":[\"string\",\"null\"]}", "type")]
    [InlineData("{\"type\":\"object\",\"unevaluatedProperties\":false}", "unevaluatedProperties")]
    [InlineData("{\"oneOf\":[{\"type\":\"object\"},{\"type\":\"object\"}]}", "oneOf")]
    public void RejectsUnsupportedSourceBodySchema(string schema, string keyword)
    {
        using var document = JsonDocument.Parse("{\"paths\":{\"/pets\":{\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":" + schema + "}}}}}}}");
        var error = Assert.Throws<InvalidOperationException>(() => OpenApiSourceInspection.Validate(document.RootElement, Operation));
        Assert.Contains(keyword, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"name\":\"session\",\"in\":\"cookie\",\"schema\":{\"type\":\"string\"}}", "Cookie")]
    [InlineData("{\"name\":\"q\",\"in\":\"query\",\"style\":\"deepObject\",\"schema\":{\"type\":\"object\"}}", "style")]
    [InlineData("{\"name\":\"id\",\"in\":\"path\",\"schema\":{\"type\":\"array\"}}", "collection")]
    [InlineData("{\"name\":\"q\",\"in\":\"query\",\"allowReserved\":true,\"schema\":{\"type\":\"string\"}}", "allowReserved")]
    public void RejectsUnsupportedParameterMapping(string parameter, string detail)
    {
        using var document = JsonDocument.Parse("{\"paths\":{\"/pets\":{\"post\":{\"parameters\":[" + parameter + "]}}}}");
        var error = Assert.Throws<InvalidOperationException>(() => OpenApiSourceInspection.Validate(document.RootElement, Operation));
        Assert.Contains(detail, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUnsupportedBodyMediaType()
    {
        using var document = JsonDocument.Parse("""{"paths":{"/pets":{"post":{"requestBody":{"content":{"application/xml":{"schema":{"type":"string"}}}}}}}}""");
        var error = Assert.Throws<InvalidOperationException>(() => OpenApiSourceInspection.Validate(document.RootElement, Operation));
        Assert.Contains("content types", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsReferenceSiblingConstraints()
    {
        using var document = JsonDocument.Parse("""
            {"paths":{"/pets":{"post":{"requestBody":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Pet","maxProperties":1}}}}}}},
             "components":{"schemas":{"Pet":{"type":"object"}}}}
            """);
        var error = Assert.Throws<InvalidOperationException>(() => OpenApiSourceInspection.Validate(document.RootElement, Operation));
        Assert.Contains("reference siblings", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsRecursiveInputReference()
    {
        using var document = JsonDocument.Parse("""
            {"paths":{"/pets":{"post":{"requestBody":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Node"}}}}}}},
             "components":{"schemas":{"Node":{"type":"object","properties":{"next":{"$ref":"#/components/schemas/Node"}}}}}}
            """);
        var error = Assert.Throws<InvalidOperationException>(() => OpenApiSourceInspection.Validate(document.RootElement, Operation));
        Assert.Contains("recursive", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowsOrdinaryLocalReferenceAndDoesNotInspectExampleAsSchema()
    {
        using var document = JsonDocument.Parse("""
            {"paths":{"/pets":{"post":{"requestBody":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Pet"}}}}}}},
             "components":{"schemas":{"Pet":{"type":"object","properties":{"name":{"type":"string","example":{"unevaluatedProperties":false}}}}}}}
            """);
        OpenApiSourceInspection.Validate(document.RootElement, Operation);
    }
}
