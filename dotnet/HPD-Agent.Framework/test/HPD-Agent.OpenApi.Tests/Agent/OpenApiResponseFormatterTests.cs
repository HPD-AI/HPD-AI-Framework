using System.Text.Json;
using HPD.Agent.OpenApi;
using HPD.OpenApi.Core;

namespace HPD.Agent.OpenApi.Tests.Agent;

public class OpenApiResponseFormatterTests
{
    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static OpenApiOperationResponse MakeResponse(
        object? content,
        int statusCode = 200,
        JsonElement? expectedSchema = null) =>
        new() { Content = content, StatusCode = statusCode, ExpectedSchema = expectedSchema };

    private static string GetContentString(string result)
    {
        var envelope = Json(result);
        return envelope.GetProperty("content").GetString()!;
    }

    [Fact]
    public void FormatSuccess_SerializedAsJsonEnvelope()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""{"id":1,"name":"Alice"}""")),
            optimization: null);

        var envelope = Json(result);
        envelope.TryGetProperty("content", out _).Should().BeTrue();
        envelope.GetProperty("status").GetInt32().Should().Be(200);
    }

    [Fact]
    public void FormatSuccess_ResponseWithSchema_SchemaPreservedInEnvelope()
    {
        var schema = Json("""{"type":"object","properties":{"id":{"type":"integer"}}}""");

        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""{"id":1}"""), expectedSchema: schema),
            optimization: null);

        Json(result).TryGetProperty("expectedSchema", out _).Should().BeTrue();
    }

    [Fact]
    public void FormatSuccess_ResponseWithNoSchema_SchemaOmittedFromEnvelope()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""{"id":1}"""), expectedSchema: null),
            optimization: null);

        Json(result).TryGetProperty("expectedSchema", out _).Should().BeFalse();
    }

    [Fact]
    public void FormatSuccess_DataFieldSet_ExtractsFromEnvelope()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""{"data":[{"id":1},{"id":2}]}""")),
            new ResponseOptimizationConfig { DataField = "data" });

        var content = GetContentString(result);
        content.Should().Contain("[");
        content.Should().Contain("{\"id\":1}");
    }

    [Fact]
    public void FormatSuccess_DataFieldDotNotation_NavigatesNestedPath()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""{"result":{"items":[{"id":42}]}}""")),
            new ResponseOptimizationConfig { DataField = "result.items" });

        var content = GetContentString(result);
        content.Should().Contain("42");
        content.Should().NotContain("\"result\"");
    }

    [Fact]
    public void FormatSuccess_DataFieldPathMissing_OriginalContentReturned()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""{"other":true}""")),
            new ResponseOptimizationConfig { DataField = "nonexistent" });

        GetContentString(result).Should().Contain("\"other\"");
    }

    [Fact]
    public void FormatSuccess_FieldsToInclude_OnlyThoseFieldsRemain()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""{"id":1,"name":"Alice","secret":"hidden"}""")),
            new ResponseOptimizationConfig { FieldsToInclude = ["id", "name"] });

        var content = GetContentString(result);
        content.Should().Contain("\"id\"");
        content.Should().Contain("\"name\"");
        content.Should().NotContain("\"secret\"");
    }

    [Fact]
    public void FormatSuccess_FieldsToInclude_OnArrayOfObjects_FiltersEachElement()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""[{"id":1,"extra":"x"},{"id":2,"extra":"y"}]""")),
            new ResponseOptimizationConfig { FieldsToInclude = ["id"] });

        var content = GetContentString(result);
        content.Should().Contain("\"id\"");
        content.Should().NotContain("\"extra\"");
    }

    [Fact]
    public void FormatSuccess_FieldsToExclude_RemovesNamedFields()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""{"id":1,"internal_id":"xxx","name":"Alice"}""")),
            new ResponseOptimizationConfig { FieldsToExclude = ["internal_id"] });

        var content = GetContentString(result);
        content.Should().NotContain("internal_id");
        content.Should().Contain("\"id\"");
        content.Should().Contain("\"name\"");
    }

    [Fact]
    public void FormatSuccess_PerConfigMaxLength_TruncatesWithEllipsis()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""{"id":1,"name":"Alice","extra":"value"}""")),
            new ResponseOptimizationConfig { MaxLength = 10 });

        var content = GetContentString(result);
        content.Should().EndWith("...");
        content.Length.Should().Be(13);
    }

    [Fact]
    public void FormatSuccess_NonJsonContent_TruncatedIfTooLong()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse("this is a very long plain text response"),
            new ResponseOptimizationConfig { MaxLength = 10 });

        var content = GetContentString(result);
        content.Should().EndWith("...");
        content.Length.Should().Be(13);
    }

    [Fact]
    public void FormatSuccess_NonJsonContentWithinLimit_Unchanged()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse("short"),
            new ResponseOptimizationConfig { MaxLength = 100 });

        GetContentString(result).Should().Be("short");
    }

    [Fact]
    public void FormatSuccess_ExtractThenFilterThenTruncate_AppliedInOrder()
    {
        var result = OpenApiResponseFormatter.FormatSuccess(
            MakeResponse(Json("""{"data":[{"id":1,"secret":"abc"},{"id":2,"secret":"def"}]}""")),
            new ResponseOptimizationConfig
            {
                DataField = "data",
                FieldsToInclude = ["id"],
                MaxLength = 20
            });

        var content = GetContentString(result);
        content.Should().NotContain("\"secret\"");
        content.Should().Contain("\"id\"");
    }

    [Fact]
    public void FormatError_SerializedAsModelFacingJson()
    {
        var result = OpenApiResponseFormatter.FormatError(new OpenApiErrorResponse
        {
            StatusCode = 400,
            ReasonPhrase = "Bad Request",
            Body = """{"message":"bad"}"""
        });

        var envelope = Json(result);
        envelope.GetProperty("error").GetBoolean().Should().BeTrue();
        envelope.GetProperty("status").GetInt32().Should().Be(400);
        envelope.GetProperty("message").GetString().Should().Be("bad");
        envelope.GetProperty("body").GetString().Should().Contain("bad");
    }
}
