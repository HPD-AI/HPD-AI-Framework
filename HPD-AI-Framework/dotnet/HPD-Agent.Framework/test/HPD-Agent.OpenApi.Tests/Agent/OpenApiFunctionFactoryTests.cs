using System.Net;
using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.OpenApi;
using HPD.Events;
using HPD.Events.Core;
using HPD.OpenApi.Core;
using HPD.OpenApi.Core.Model;
using Microsoft.Extensions.AI;

namespace HPD.Agent.OpenApi.Tests.Agent;

/// <summary>
/// Tests for OpenApiFunctionFactory via the public CreateFunctions surface.
/// The factory is internal — accessed via InternalsVisibleTo("HPD-Agent.OpenApi.Tests").
/// </summary>
public class OpenApiFunctionFactoryTests
{
    // ────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────

    private static OpenApiOperationRunner MakeRunner(
        HttpStatusCode status = HttpStatusCode.OK,
        string responseBody = "{}") =>
        new(new HttpClient(new FakeHttpHandler(status, responseBody)));

    private static ParsedOpenApiSpec MakeSpec(params RestApiOperation[] operations) => new()
    {
        Operations = [..operations]
    };

    private static RestApiOperation MakeOp(
        string operationId = "listItems",
        string path = "/items",
        HttpMethod? method = null,
        List<RestApiParameter>? parameters = null) => new()
    {
        Id = operationId,
        Path = path,
        Method = method ?? HttpMethod.Get,
        ServerUrl = "https://api.example.com",
        Description = $"Description for {operationId}",
        Parameters = parameters ?? []
    };

    private static object? ReadProp(IReadOnlyDictionary<string, object?> props, string key) =>
        props.TryGetValue(key, out var v) ? v : null;

    private static async ValueTask<object?> InvokeFunctionAsync(
        AIFunction function,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken = default)
    {
        var hpdFunction = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function);
        return await hpdFunction.InvokeAsync(arguments, CreateContext(function), cancellationToken);
    }

    private static FunctionExecutionContext CreateContext(AIFunction function, string callId = "call-1")
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "OpenApiTestAgent");
        var session = new Session("session-1");
        var branch = new Branch("session-1") { Id = "main" };
        var agentContext = new AgentContext(
            "OpenApiTestAgent",
            "conversation-1",
            state,
            new EventCoordinator(),
            session,
            branch,
            CancellationToken.None);
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            callId,
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            toolharnessName: null,
            skillName: null);

        return new FunctionExecutionContext(
            beforeContext,
            new FunctionRequest
            {
                Function = function,
                CallId = callId,
                Arguments = new Dictionary<string, object?>(),
                State = state,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = agentContext.EventCoordinator
            });
    }

    private sealed class FakeHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    // ────────────────────────────────────────────────────────────
    // Function name generation
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateFunctions_OperationHasOperationId_NamedPrefixUnderscore()
    {
        var spec = MakeSpec(MakeOp("listPets"));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner(),
            namePrefix: "petstore");

        functions.Should().ContainSingle(f => f.Name == "petstore_listPets");
    }

    [Fact]
    public void CreateFunctions_NullPrefix_NameIsOperationIdOnly()
    {
        var spec = MakeSpec(MakeOp("listPets"));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner(),
            namePrefix: null);

        functions.Should().ContainSingle(f => f.Name == "listPets");
    }

    [Fact]
    public void CreateFunctions_OperationIdWithSpecialChars_InvalidCharsStripped()
    {
        var op = MakeOp("list-pets items!");
        var spec = MakeSpec(op);
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner());

        // "-", " ", "!" are invalid — stripped
        functions[0].Name.Should().Be("listpetsitems");
    }

    [Fact]
    public void CreateFunctions_NoOperationId_NameDerivedFromMethodAndPath()
    {
        var op = new RestApiOperation
        {
            Id = null,
            Path = "/pets/{petId}",
            Method = HttpMethod.Get,
            ServerUrl = "https://api.example.com"
        };
        var spec = MakeSpec(op);
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner());

        // Should be derived from GET + path segments in TitleCase
        // ToTitleCase("get")="Get", path segments "pets" and "{petId}" → stripped chars → "Pets" + "Petid"
        functions[0].Name.Should().StartWith("Get");
        functions[0].Name.Should().Contain("Pets");
    }

    // ────────────────────────────────────────────────────────────
    // Metadata stamping
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateFunctions_FlatMode_StampsParentContainerOnEachFunction()
    {
        var spec = MakeSpec(MakeOp("listPets"), MakeOp("createPet", method: HttpMethod.Post));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner(),
            namePrefix: "pet", parentContainer: "PetToolHarness", collapseWithinToolHarness: false);

        functions.Should().AllSatisfy(f =>
            ReadProp(f.AdditionalProperties, "ParentContainer").Should().Be("PetToolHarness"));
    }

    [Fact]
    public void CreateFunctions_AllFunctionsHaveSourceTypeOpenApi()
    {
        var spec = MakeSpec(MakeOp(), MakeOp("other", "/other"));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner());

        functions.Should().AllSatisfy(f =>
            ReadProp(f.AdditionalProperties, "SourceType").Should().Be("OpenApi"));
    }

    [Fact]
    public void CreateFunctions_OpenApiMetadataStamped()
    {
        var op = MakeOp("listPets", "/pets");
        var spec = MakeSpec(op);
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner());

        var fn = functions[0];
        ReadProp(fn.AdditionalProperties, "openapi.path").Should().Be("/pets");
        ReadProp(fn.AdditionalProperties, "openapi.method").Should().Be("GET");
        ReadProp(fn.AdditionalProperties, "openapi.operationId").Should().Be("listPets");
    }

    [Fact]
    public void CreateFunctions_ResponseOptimizationSet_NoMiddlewareHintsStampedOnFunction()
    {
        var config = new OpenApiConfig
        {
            ResponseOptimization = new ResponseOptimizationConfig
            {
                DataField = "data",
                FieldsToInclude = ["id", "name"],
                MaxLength = 1000
            }
        };
        var spec = MakeSpec(MakeOp());
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, config, MakeRunner());

        var fn = functions[0];
        ReadProp(fn.AdditionalProperties, "openapi.response.dataField").Should().BeNull();
        ReadProp(fn.AdditionalProperties, "openapi.response.maxLength").Should().BeNull();
    }

    [Fact]
    public void CreateFunctions_ResponseOptimizationNull_HintsAbsentOrNull()
    {
        var config = new OpenApiConfig { ResponseOptimization = null };
        var spec = MakeSpec(MakeOp());
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, config, MakeRunner());

        var fn = functions[0];
        ReadProp(fn.AdditionalProperties, "openapi.response.dataField").Should().BeNull();
    }

    [Fact]
    public void CreateFunctions_RequiresPermissionTrue_PropagatedToFunction()
    {
        var config = new OpenApiConfig { RequiresPermission = true };
        var spec = MakeSpec(MakeOp());
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, config, MakeRunner());

        var hpdFn = (HPDAIFunctionFactory.HPDAIFunction)functions[0];
        hpdFn.HPDOptions.RequiresPermission.Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────
    // CollapseWithinToolHarness
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateFunctions_CollapseWithinToolHarnessFalse_NoContainerFunction()
    {
        var spec = MakeSpec(MakeOp("a"), MakeOp("b", "/b"));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner(),
            namePrefix: "api", parentContainer: "MyToolHarness", collapseWithinToolHarness: false);

        // All functions, none are containers
        functions.Should().HaveCount(2);
        functions.Should().AllSatisfy(f =>
            ReadProp(f.AdditionalProperties, "IsContainer").Should().Be(false));
    }

    [Fact]
    public void CreateFunctions_CollapseWithinToolHarnessTrue_ContainerFunctionEmitted()
    {
        var spec = MakeSpec(MakeOp("a"), MakeOp("b", "/b"));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner(),
            namePrefix: "api", parentContainer: "MyToolHarness", collapseWithinToolHarness: true);

        // Should have container + 2 collapsed functions
        functions.Should().HaveCount(3);
        var container = functions.Single(f =>
            ReadProp(f.AdditionalProperties, "IsContainer") is true);
        container.Should().NotBeNull();
    }

    [Fact]
    public void CreateFunctions_CollapseWithinToolHarnessTrue_ContainerHasToolHarnessParentAndIndividualFunctionsHaveContainerParentToolHarness()
    {
        var spec = MakeSpec(MakeOp("a"), MakeOp("b", "/b"));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner(),
            namePrefix: "api", parentContainer: "MyToolHarness", collapseWithinToolHarness: true);

        // The container gets ParentContainer = "MyToolHarness" (the toolharness name)
        var container = functions.Single(f => ReadProp(f.AdditionalProperties, "IsContainer") is true);
        ReadProp(container.AdditionalProperties, "ParentContainer").Should().Be("MyToolHarness");

        // Individual collapsed functions get ParentToolHarness = container name, ParentContainer = null
        var nonContainers = functions.Where(f =>
            ReadProp(f.AdditionalProperties, "IsContainer") is not true).ToList();
        nonContainers.Should().AllSatisfy(f =>
            ReadProp(f.AdditionalProperties, "ParentToolHarness").Should().NotBeNull());
    }

    // ────────────────────────────────────────────────────────────
    // Throw-vs-return error bridging
    // ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(401)]
    [InlineData(408)]
    public async Task InvokedFunction_RetryableError_ThrowsOpenApiRequestException(int statusCode)
    {
        var spec = MakeSpec(MakeOp("getItem", "/items/1"));
        var runner = new OpenApiOperationRunner(
            new HttpClient(new FakeHttpHandler((HttpStatusCode)statusCode, "error")));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), runner);

        var args = new AIFunctionArguments();
        var act = async () => await InvokeFunctionAsync(functions[0], args);

        await act.Should().ThrowAsync<OpenApiRequestException>();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task InvokedFunction_ClientError_ReturnsModelFacingErrorNotThrows(int statusCode)
    {
        var spec = MakeSpec(MakeOp("getItem", "/items/1"));
        var runner = new OpenApiOperationRunner(
            new HttpClient(new FakeHttpHandler((HttpStatusCode)statusCode, """{"message":"bad"}""")));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), runner);

        var args = new AIFunctionArguments();
        var result = await InvokeFunctionAsync(functions[0], args);

        result.Should().BeOfType<string>();
        var envelope = JsonDocument.Parse((string)result!).RootElement;
        envelope.GetProperty("error").GetBoolean().Should().BeTrue();
        envelope.GetProperty("status").GetInt32().Should().Be(statusCode);
        envelope.GetProperty("message").GetString().Should().Be("bad");
    }

    [Fact]
    public async Task InvokedFunction_SuccessResponse_ReturnsModelFacingEnvelope()
    {
        var spec = MakeSpec(MakeOp("listItems"));
        var runner = MakeRunner(HttpStatusCode.OK, """[{"id":1}]""");
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), runner);

        var result = await InvokeFunctionAsync(functions[0], new AIFunctionArguments());

        result.Should().BeOfType<string>();
        var envelope = JsonDocument.Parse((string)result!).RootElement;
        envelope.GetProperty("status").GetInt32().Should().Be(200);
        envelope.GetProperty("content").GetString().Should().Contain("\"id\":1");
    }

    // ────────────────────────────────────────────────────────────
    // Schema building
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateFunctions_PathAndQueryParams_EmittedInSchema()
    {
        var op = MakeOp(parameters: [
            new RestApiParameter { Name = "petId", Type = "string", IsRequired = true, Location = RestApiParameterLocation.Path },
            new RestApiParameter { Name = "limit", Type = "integer", IsRequired = false, Location = RestApiParameterLocation.Query }
        ]);
        var spec = MakeSpec(op);
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner());

        var schema = functions[0].JsonSchema;
        schema.GetProperty("properties").TryGetProperty("petId", out _).Should().BeTrue();
        schema.GetProperty("properties").TryGetProperty("limit", out _).Should().BeTrue();
    }

    [Fact]
    public void CreateFunctions_RequiredParam_AppearsInRequiredArray()
    {
        var op = MakeOp(parameters: [
            new RestApiParameter { Name = "petId", Type = "string", IsRequired = true, Location = RestApiParameterLocation.Path }
        ]);
        var spec = MakeSpec(op);
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner());

        var required = functions[0].JsonSchema.GetProperty("required");
        required.EnumerateArray().Select(e => e.GetString()).Should().Contain("petId");
    }

    [Fact]
    public void CreateFunctions_OptionalParam_NotInRequiredArray()
    {
        var op = MakeOp(parameters: [
            new RestApiParameter { Name = "limit", Type = "integer", IsRequired = false, Location = RestApiParameterLocation.Query }
        ]);
        var spec = MakeSpec(op);
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner());

        var schema = functions[0].JsonSchema;
        var required = schema.GetProperty("required");
        required.EnumerateArray().Select(e => e.GetString()).Should().NotContain("limit");
    }

    [Fact]
    public void CreateFunctions_EnableDynamicPayloadFalse_SinglePayloadStringProperty()
    {
        var op = new RestApiOperation
        {
            Id = "createPet",
            Path = "/pets",
            Method = HttpMethod.Post,
            ServerUrl = "https://api.example.com",
            Payload = new RestApiPayload
            {
                MediaType = "application/json",
                Properties = [new RestApiPayloadProperty { Name = "name", Type = "string", IsRequired = true }]
            }
        };
        var config = new OpenApiConfig { EnableDynamicPayload = false };
        var spec = MakeSpec(op);
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, config, MakeRunner());

        var schema = functions[0].JsonSchema;
        schema.GetProperty("properties").TryGetProperty("payload", out var payloadProp).Should().BeTrue();
        payloadProp.GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void CreateFunctions_EmptySpec_ReturnsEmptyList()
    {
        var spec = new ParsedOpenApiSpec { Operations = [] };
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner());

        functions.Should().BeEmpty();
    }
}
