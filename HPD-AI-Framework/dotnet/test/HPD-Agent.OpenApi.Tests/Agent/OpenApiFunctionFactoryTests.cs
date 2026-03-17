using System.Net;
using System.Text.Json;
using HPD.Agent.OpenApi;
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
            namePrefix: "pet", parentContainer: "PetToolkit", collapseWithinToolkit: false);

        functions.Should().AllSatisfy(f =>
            ReadProp(f.AdditionalProperties, "ParentContainer").Should().Be("PetToolkit"));
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
    public void CreateFunctions_ResponseOptimizationSet_HintsStampedOnFunction()
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
        ReadProp(fn.AdditionalProperties, "openapi.response.dataField").Should().Be("data");
        (ReadProp(fn.AdditionalProperties, "openapi.response.maxLength") as int?).Should().Be(1000);
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
    // CollapseWithinToolkit
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateFunctions_CollapseWithinToolkitFalse_NoContainerFunction()
    {
        var spec = MakeSpec(MakeOp("a"), MakeOp("b", "/b"));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner(),
            namePrefix: "api", parentContainer: "MyToolkit", collapseWithinToolkit: false);

        // All functions, none are containers
        functions.Should().HaveCount(2);
        functions.Should().AllSatisfy(f =>
            ReadProp(f.AdditionalProperties, "IsContainer").Should().Be(false));
    }

    [Fact]
    public void CreateFunctions_CollapseWithinToolkitTrue_ContainerFunctionEmitted()
    {
        var spec = MakeSpec(MakeOp("a"), MakeOp("b", "/b"));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner(),
            namePrefix: "api", parentContainer: "MyToolkit", collapseWithinToolkit: true);

        // Should have container + 2 collapsed functions
        functions.Should().HaveCount(3);
        var container = functions.Single(f =>
            ReadProp(f.AdditionalProperties, "IsContainer") is true);
        container.Should().NotBeNull();
    }

    [Fact]
    public void CreateFunctions_CollapseWithinToolkitTrue_ContainerHasToolkitParentAndIndividualFunctionsHaveContainerParentToolkit()
    {
        var spec = MakeSpec(MakeOp("a"), MakeOp("b", "/b"));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), MakeRunner(),
            namePrefix: "api", parentContainer: "MyToolkit", collapseWithinToolkit: true);

        // The container gets ParentContainer = "MyToolkit" (the toolkit name)
        var container = functions.Single(f => ReadProp(f.AdditionalProperties, "IsContainer") is true);
        ReadProp(container.AdditionalProperties, "ParentContainer").Should().Be("MyToolkit");

        // Individual collapsed functions get ParentToolkit = container name, ParentContainer = null
        var nonContainers = functions.Where(f =>
            ReadProp(f.AdditionalProperties, "IsContainer") is not true).ToList();
        nonContainers.Should().AllSatisfy(f =>
            ReadProp(f.AdditionalProperties, "ParentToolkit").Should().NotBeNull());
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
        var act = async () => await functions[0].InvokeAsync(args);

        await act.Should().ThrowAsync<OpenApiRequestException>();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task InvokedFunction_ClientError_ReturnsErrorResponseNotThrows(int statusCode)
    {
        var spec = MakeSpec(MakeOp("getItem", "/items/1"));
        var runner = new OpenApiOperationRunner(
            new HttpClient(new FakeHttpHandler((HttpStatusCode)statusCode, """{"message":"bad"}""")));
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), runner);

        var args = new AIFunctionArguments();
        var result = await functions[0].InvokeAsync(args);

        result.Should().BeOfType<OpenApiErrorResponse>();
        ((OpenApiErrorResponse)result!).StatusCode.Should().Be(statusCode);
    }

    [Fact]
    public async Task InvokedFunction_SuccessResponse_ReturnsOpenApiOperationResponse()
    {
        var spec = MakeSpec(MakeOp("listItems"));
        var runner = MakeRunner(HttpStatusCode.OK, """[{"id":1}]""");
        var functions = OpenApiFunctionFactory.CreateFunctions(spec, new OpenApiConfig(), runner);

        var result = await functions[0].InvokeAsync(new AIFunctionArguments());

        // Runner wraps successful responses in OpenApiOperationResponse for middleware processing
        result.Should().BeOfType<OpenApiOperationResponse>();
        var response = (OpenApiOperationResponse)result!;
        response.StatusCode.Should().Be(200);
        response.Content.Should().BeOfType<JsonElement>();
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
