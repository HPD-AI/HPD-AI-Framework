using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.OpenApi;
using HPD.OpenApi.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.OpenApi.Tests.Agent;

/// <summary>
/// Tests for ResponseOptimizationMiddleware.AfterFunctionAsync.
/// Uses a minimal AgentContext helper to avoid pulling the full test infrastructure.
/// </summary>
public class ResponseOptimizationMiddlewareTests
{
    // ────────────────────────────────────────────────────────────
    // Infrastructure
    // ────────────────────────────────────────────────────────────

    private static AfterFunctionContext MakeContext(
        AIFunction? function = null,
        object? result = null,
        Exception? exception = null)
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conv-1", "TestAgent");
        var events = new HPD.Events.Core.EventCoordinator();
        var session = new Session("conv-1");
        var branch = new Branch("conv-1");
        var agentCtx = new AgentContext("TestAgent", "conv-1", state, events, session, branch, default);
        return agentCtx.AsAfterFunction(function, "call-1", result, exception, new AgentRunConfig());
    }

    private static AIFunction MakeOpenApiFunction(
        string operationId = "listItems",
        string? dataField = null,
        IList<string>? fieldsToInclude = null,
        IList<string>? fieldsToExclude = null,
        int maxLength = 0)
    {
        var props = new Dictionary<string, object?>
        {
            ["openapi.operationId"] = operationId,
            ["openapi.response.dataField"] = dataField,
            ["openapi.response.fieldsToInclude"] = fieldsToInclude,
            ["openapi.response.fieldsToExclude"] = fieldsToExclude,
            ["openapi.response.maxLength"] = maxLength,
            ["IsContainer"] = false,
            ["SourceType"] = "OpenApi"
        };
        return HPDAIFunctionFactory.Create(
            (_, _) => Task.FromResult<object?>(null),
            new HPDAIFunctionFactoryOptions
            {
                Name = "test_" + operationId,
                AdditionalProperties = props
            });
    }

    private static AIFunction MakeNonOpenApiFunction() =>
        HPDAIFunctionFactory.Create(
            (_, _) => Task.FromResult<object?>(null),
            new HPDAIFunctionFactoryOptions { Name = "regularFunction" });

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static OpenApiOperationResponse MakeResponse(
        object? content,
        int statusCode = 200,
        JsonElement? expectedSchema = null) =>
        new() { Content = content, StatusCode = statusCode, ExpectedSchema = expectedSchema };

    /// <summary>Parses the serialized envelope result and extracts the "content" string.</summary>
    private static string GetContentString(object? result)
    {
        var envelope = Json((string)result!);
        return envelope.GetProperty("content").GetString()!;
    }

    /// <summary>Parses the serialized envelope result and extracts "content" as a JsonElement.</summary>
    private static JsonElement GetContentJson(object? result)
    {
        var envelope = Json((string)result!);
        // Content may be a JSON string (if processed/truncated) or an embedded object
        var contentProp = envelope.GetProperty("content");
        return contentProp.ValueKind == JsonValueKind.String
            ? Json(contentProp.GetString()!)
            : contentProp;
    }

    // ────────────────────────────────────────────────────────────
    // Guard conditions
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterFunction_FunctionWithoutOpenApiOperationId_ResultUnchanged()
    {
        var mw = new ResponseOptimizationMiddleware();
        var expected = MakeResponse(Json("""{"id":1,"name":"Alice"}"""));
        var ctx = MakeContext(function: MakeNonOpenApiFunction(), result: expected);

        await mw.AfterFunctionAsync(ctx, default);

        // Result should be unchanged — the middleware only acts on OpenAPI functions
        ctx.Result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task AfterFunction_FailedFunction_ResultUnchanged()
    {
        var mw = new ResponseOptimizationMiddleware();
        var original = MakeResponse(Json("""{"id":1}"""));
        var ctx = MakeContext(
            function: MakeOpenApiFunction(),
            result: original,
            exception: new InvalidOperationException("boom"));

        await mw.AfterFunctionAsync(ctx, default);

        ctx.Result.Should().BeSameAs(original);
    }

    [Fact]
    public async Task AfterFunction_NullResult_NoException()
    {
        var mw = new ResponseOptimizationMiddleware();
        var ctx = MakeContext(function: MakeOpenApiFunction(), result: null);

        var act = () => mw.AfterFunctionAsync(ctx, default);

        await act.Should().NotThrowAsync();
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task AfterFunction_NonOpenApiOperationResponseResult_Ignored()
    {
        // If something other than OpenApiOperationResponse ends up as result, middleware skips it
        var mw = new ResponseOptimizationMiddleware();
        var ctx = MakeContext(function: MakeOpenApiFunction(), result: Json("""{"id":1}"""));

        await mw.AfterFunctionAsync(ctx, default);

        // Not an OpenApiOperationResponse — result unchanged
        ctx.Result.Should().BeOfType<JsonElement>();
    }

    // ────────────────────────────────────────────────────────────
    // Envelope serialization
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterFunction_SuccessResponse_SerializedAsJsonEnvelope()
    {
        var mw = new ResponseOptimizationMiddleware();
        var fn = MakeOpenApiFunction();
        var ctx = MakeContext(function: fn, result: MakeResponse(Json("""{"id":1,"name":"Alice"}""")));

        await mw.AfterFunctionAsync(ctx, default);

        ctx.Result.Should().BeOfType<string>();
        var envelope = Json((string)ctx.Result!);
        envelope.TryGetProperty("content", out _).Should().BeTrue();
        envelope.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task AfterFunction_ResponseWithSchema_SchemaPreservedInEnvelope()
    {
        var mw = new ResponseOptimizationMiddleware();
        var fn = MakeOpenApiFunction();
        var schema = Json("""{"type":"object","properties":{"id":{"type":"integer"}}}""");
        var ctx = MakeContext(function: fn, result: MakeResponse(Json("""{"id":1}"""), expectedSchema: schema));

        await mw.AfterFunctionAsync(ctx, default);

        var envelope = Json((string)ctx.Result!);
        envelope.TryGetProperty("expectedSchema", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AfterFunction_ResponseWithNoSchema_SchemaOmittedFromEnvelope()
    {
        var mw = new ResponseOptimizationMiddleware();
        var fn = MakeOpenApiFunction();
        var ctx = MakeContext(function: fn, result: MakeResponse(Json("""{"id":1}"""), expectedSchema: null));

        await mw.AfterFunctionAsync(ctx, default);

        var envelope = Json((string)ctx.Result!);
        // ExpectedSchema is null → JsonIgnore(WhenWritingNull) → not in output
        envelope.TryGetProperty("expectedSchema", out _).Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────
    // Data field extraction
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterFunction_DataFieldSet_ExtractsFromEnvelope()
    {
        var mw = new ResponseOptimizationMiddleware();
        var fn = MakeOpenApiFunction(dataField: "data");
        var ctx = MakeContext(function: fn, result: MakeResponse(Json("""{"data":[{"id":1},{"id":2}]}""")));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().Contain("[");
        content.Should().Contain("{\"id\":1}");
    }

    [Fact]
    public async Task AfterFunction_DataFieldDotNotation_NavigatesNestedPath()
    {
        var mw = new ResponseOptimizationMiddleware();
        var fn = MakeOpenApiFunction(dataField: "result.items");
        var ctx = MakeContext(function: fn, result: MakeResponse(Json("""{"result":{"items":[{"id":42}]}}""")));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().Contain("42");
        content.Should().NotContain("\"result\"");
    }

    [Fact]
    public async Task AfterFunction_DataFieldPathMissing_OriginalContentReturned()
    {
        var mw = new ResponseOptimizationMiddleware();
        var fn = MakeOpenApiFunction(dataField: "nonexistent");
        var ctx = MakeContext(function: fn, result: MakeResponse(Json("""{"other":true}""")));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().Contain("\"other\"");
    }

    // ────────────────────────────────────────────────────────────
    // Field filtering (whitelist)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterFunction_FieldsToInclude_OnlyThoseFieldsRemain()
    {
        var mw = new ResponseOptimizationMiddleware();
        var fn = MakeOpenApiFunction(fieldsToInclude: ["id", "name"]);
        var ctx = MakeContext(function: fn, result: MakeResponse(Json("""{"id":1,"name":"Alice","secret":"hidden"}""")));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().Contain("\"id\"");
        content.Should().Contain("\"name\"");
        content.Should().NotContain("\"secret\"");
    }

    [Fact]
    public async Task AfterFunction_FieldsToInclude_OnArrayOfObjects_FiltersEachElement()
    {
        var mw = new ResponseOptimizationMiddleware();
        var fn = MakeOpenApiFunction(fieldsToInclude: ["id"]);
        var ctx = MakeContext(function: fn, result: MakeResponse(Json("""[{"id":1,"extra":"x"},{"id":2,"extra":"y"}]""")));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().Contain("\"id\"");
        content.Should().NotContain("\"extra\"");
    }

    // ────────────────────────────────────────────────────────────
    // Field filtering (blacklist)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterFunction_FieldsToExclude_RemovesNamedFields()
    {
        var mw = new ResponseOptimizationMiddleware();
        var fn = MakeOpenApiFunction(fieldsToExclude: ["internal_id"]);
        var ctx = MakeContext(function: fn,
            result: MakeResponse(Json("""{"id":1,"internal_id":"xxx","name":"Alice"}""")));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().NotContain("internal_id");
        content.Should().Contain("\"id\"");
        content.Should().Contain("\"name\"");
    }

    // ────────────────────────────────────────────────────────────
    // Length truncation
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterFunction_ContentExceedsDefaultMaxLength_TruncatedWithEllipsis()
    {
        var mw = new ResponseOptimizationMiddleware { DefaultMaxLength = 20 };
        var fn = MakeOpenApiFunction();
        var ctx = MakeContext(function: fn,
            result: MakeResponse(Json("""{"id":1,"name":"Alice","extra":"value"}""")));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().EndWith("...");
        content.Length.Should().Be(23); // 20 + "..."
    }

    [Fact]
    public async Task AfterFunction_PerConfigMaxLength_OverridesDefault()
    {
        var mw = new ResponseOptimizationMiddleware { DefaultMaxLength = 10000 };
        var fn = MakeOpenApiFunction(maxLength: 10);
        var ctx = MakeContext(function: fn,
            result: MakeResponse(Json("""{"id":1,"name":"Alice","extra":"value"}""")));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().EndWith("...");
        content.Length.Should().Be(13); // 10 + "..."
    }

    [Fact]
    public async Task AfterFunction_NonJsonContent_TruncatedIfTooLong()
    {
        var mw = new ResponseOptimizationMiddleware { DefaultMaxLength = 10 };
        var fn = MakeOpenApiFunction();
        var ctx = MakeContext(function: fn, result: MakeResponse("this is a very long plain text response"));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().EndWith("...");
        content.Length.Should().Be(13); // 10 + "..."
    }

    [Fact]
    public async Task AfterFunction_NonJsonContentWithinLimit_Unchanged()
    {
        var mw = new ResponseOptimizationMiddleware { DefaultMaxLength = 100 };
        var fn = MakeOpenApiFunction();
        var ctx = MakeContext(function: fn, result: MakeResponse("short"));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().Be("short");
    }

    // ────────────────────────────────────────────────────────────
    // Pipeline ordering: extract → filter → truncate
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterFunction_ExtractThenFilterThenTruncate_AppliedInOrder()
    {
        var mw = new ResponseOptimizationMiddleware { DefaultMaxLength = 30 };
        var fn = MakeOpenApiFunction(
            dataField: "data",
            fieldsToInclude: ["id"],
            maxLength: 20);

        var ctx = MakeContext(function: fn,
            result: MakeResponse(Json("""{"data":[{"id":1,"secret":"abc"},{"id":2,"secret":"def"}]}""")));

        await mw.AfterFunctionAsync(ctx, default);

        var content = GetContentString(ctx.Result);
        content.Should().NotContain("\"secret\"");
        content.Should().Contain("\"id\"");
    }
}
