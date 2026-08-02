using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace HPD.Base.AspNetCore.Tests.Endpoints;

public sealed class RegisteredReadEndpointTests
{
    [Fact]
    public async Task ConcreteRegisteredReadRouteBindsGeneratedMetadataAndReturnsTypedPageShape()
    {
        var registration = new TestReadRegistration();
        await using WebApplication app = await TestBaseApp.CreateAsync(configureServices: services =>
        {
            services.AddSingleton(new BaseReadRegistry(new Dictionary<string, IBaseReadRegistration> { [registration.Id] = registration }));
            services.AddSingleton<IBaseRegisteredReadRuntime, UnusedReadRuntime>();
        }, mapOpenApi: true);

        HttpResponseMessage response = await app.GetTestClient().PostAsync(
            "/base/reads/test-read?page=2&perPage=3",
            JsonContent.Create(new TestReadParameters { Search = "needle" }, TestReadJsonContext.Default.TestReadParameters));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"items\":[{\"value\":\"needle\"}]").And.Contain("\"page\":2").And.Contain("\"perPage\":3");
        registration.RequestedPage.Should().Be(new BaseReadPageRequest(2, 3));
        Endpoint endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.Single(item =>
            item.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "base.reads.test-read");
        endpoint.Metadata.GetMetadata<IAcceptsMetadata>()!.RequestType.Should().Be(typeof(TestReadParameters));
        endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Single(item => item.StatusCode == 200).Type.Should().Be(typeof(BasePage<TestReadRow>));

        HttpResponseMessage documentResponse = await app.GetTestClient().GetAsync("/base/openapi/base-public.json");
        using JsonDocument document = JsonDocument.Parse(await documentResponse.Content.ReadAsStringAsync());
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/base/reads/test-read").GetProperty("post");
        operation.GetProperty("requestBody").ValueKind.Should().Be(JsonValueKind.Object);
        operation.GetProperty("responses").EnumerateObject().Select(static response => response.Name)
            .Should().BeEquivalentTo("200", "400", "403", "424", "500");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredReadRejectsKnownAndChunkedOversizedBodiesWithTheSameSafeError(bool chunked)
    {
        var registration = new TestReadRegistration();
        await using WebApplication app = await TestBaseApp.CreateAsync(
            configureServices: services =>
            {
                services.AddSingleton(new BaseReadRegistry(new Dictionary<string, IBaseReadRegistration> { [registration.Id] = registration }));
                services.AddSingleton<IBaseRegisteredReadRuntime, UnusedReadRuntime>();
            },
            configureAspNetCore: options => options.Limits.MaxRequestBodyLength = 64);
        string json = "{\"search\":\"" + new string('x', 256) + "\"}";
        HttpContent content = chunked
            ? new UnknownLengthJsonContent(json)
            : new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await app.GetTestClient().PostAsync("/base/reads/test-read", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("base.http.body.invalidJson");
        body.Should().NotContain(new string('x', 32));
    }

    [Fact]
    public async Task RegisteredReadMalformedJsonReturnsOneFixedNonLeakingProblem()
    {
        var registration = new TestReadRegistration();
        await using WebApplication app = await TestBaseApp.CreateAsync(configureServices: services =>
        {
            services.AddSingleton(new BaseReadRegistry(new Dictionary<string, IBaseReadRegistration> { [registration.Id] = registration }));
            services.AddSingleton<IBaseRegisteredReadRuntime, UnusedReadRuntime>();
        });

        using var content = new StringContent("{\"search\":\"secret-fragment", System.Text.Encoding.UTF8, "application/json");
        HttpResponseMessage response = await app.GetTestClient().PostAsync("/base/reads/test-read", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("base.http.body.invalidJson");
        body.Should().Contain("Request body is not valid JSON.");
        body.Should().NotContain("secret-fragment");
        body.Should().NotContain("JsonException");
    }

    private sealed class TestReadRegistration : IBaseReadRegistration
    {
        public string Id => "test-read";
        public BaseRelationalReadPlan Plan { get; } = new()
        {
            Id = "test-read", Sources = [], Projection = [], Parameters = [],
            Budgets = new BaseRelationalReadBudgets { MaxResultRows = 10, MaxResultBytes = 1024, MaxOperations = 10 }
        };
        public JsonTypeInfo ParameterJsonTypeInfo => TestReadJsonContext.Default.TestReadParameters;
        public JsonTypeInfo RowJsonTypeInfo => TestReadJsonContext.Default.TestReadRow;
        public Type ResponseType => typeof(BasePage<TestReadRow>);
        public BaseReadPageRequest? RequestedPage { get; private set; }

        public ValueTask<BaseUntypedRegisteredReadResult> ExecuteAsync(IBaseRegisteredReadRuntime runtime, object parameters, BaseReadPageRequest page, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken)
        {
            RequestedPage = page;
            return ValueTask.FromResult(new BaseUntypedRegisteredReadResult
            {
                Status = OperationStatus.Ok,
                Items = [new TestReadRow { Value = ((TestReadParameters)parameters).Search }],
                Page = new PageInfo { Page = page.Page, PerPage = page.PerPage }
            });
        }
    }

    private sealed class UnusedReadRuntime : IBaseRegisteredReadRuntime
    {
        public ValueTask<OperationResult<BaseRegisteredReadEvaluation<TRow>>> ExecuteAsync<TParameters, TRow>(BaseReadDefinition<TParameters, TRow> definition, TParameters parameters, BaseReadPageRequest? page, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

file sealed class UnknownLengthJsonContent(string json) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(json)).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

internal sealed record TestReadParameters { public required string Search { get; init; } }
internal sealed record TestReadRow { public required string Value { get; init; } }

[JsonSerializable(typeof(TestReadParameters))]
[JsonSerializable(typeof(TestReadRow))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class TestReadJsonContext : JsonSerializerContext;
