using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base.AspNetCore;
using HPD.Base;
using HPD.Base.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Vector.AspNetCore.Tests;

public sealed class VectorEndpointTests
{
    [Fact]
    public async Task Vector_routes_are_absent_by_default_and_explicit_mapping_has_exact_metadata()
    {
        await using WebApplication absent = await CreateAsync(mapVector: false);
        ((IEndpointRouteBuilder)absent).DataSources.SelectMany(static source => source.Endpoints).OfType<RouteEndpoint>().Should().NotContain(endpoint => endpoint.RoutePattern.RawText!.Contains("/vector/", StringComparison.Ordinal));

        await using WebApplication mapped = await CreateAsync(mapVector: true);
        RouteEndpoint query = ((IEndpointRouteBuilder)mapped).DataSources.SelectMany(static source => source.Endpoints).OfType<RouteEndpoint>().Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "hpd.base.vector.query");

        query.RoutePattern.RawText.Should().Be("/base/vector/{collectionId}/{vectorIndexId}/query");
        HPDBaseEndpointDescriptor descriptor = query.Metadata.GetMetadata<HPDBaseEndpointDescriptor>()!;
        descriptor.Audience.Should().Be(HPDBaseEndpointAudience.Application);
        descriptor.Operation.Should().Be(HPDBaseEndpointOperation.VectorQuery);
        descriptor.Capability.Should().Be(HPDBaseCapabilities.VectorQuery);
        query.Metadata.GetMetadata<IAcceptsMetadata>()!.RequestType.Should().Be(typeof(BaseVectorHttpQueryRequest));
        query.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(static metadata => metadata.StatusCode).Should().Contain([200, 400, 403, 404, 408, 409, 410, 413, 422, 424, 502, 504]);
    }

    [Fact]
    public async Task Chunked_request_body_cannot_bypass_the_vector_limit()
    {
        await using WebApplication app = await CreateAsync(mapVector: true);
        HttpClient client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/base/vector/unknown/unknown/query")
        {
            Content = new ChunkedContent("{\"vector\":[" + string.Join(',', Enumerable.Repeat("0", 10_000)) + "]}"),
        };
        request.Headers.TransferEncodingChunked = true;

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await response.Content.ReadAsStringAsync()).Should().Contain("base.vector.limitExceeded").And.NotContain(new string('x', 64));
    }

    [Theory]
    [InlineData("omit", false)]
    [InlineData("include", true)]
    public async Task Measure_disclosure_is_explicit_and_closed(string mode, bool expectedMeasure)
    {
        await using WebApplication app = await CreateAsync(mapVector: true);
        HttpResponseMessage response = await app.GetTestClient().PostAsJsonAsync(
            "/base/vector/http_vectors/http.vector.search/query",
            new { vector = new[] { 1f, 0f }, take = 1, measureDisclosure = mode, consistency = "current" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement match = json.RootElement.GetProperty("matches")[0];
        match.TryGetProperty("measure", out _).Should().Be(expectedMeasure);
    }

    [Theory]
    [InlineData("{\"vector\":[1,0],\"take\":1,\"consistency\":\"current\"}")]
    [InlineData("{\"vector\":[1,0],\"take\":1,\"measureDisclosure\":\"unknown\",\"consistency\":\"current\"}")]
    [InlineData("{\"vector\":[1,0],\"take\":1,\"measureDisclosure\":99,\"consistency\":\"current\"}")]
    public async Task Missing_or_invalid_measure_disclosure_fails_closed(string json)
    {
        await using WebApplication app = await CreateAsync(mapVector: true);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await app.GetTestClient().PostAsync("/base/vector/http_vectors/http.vector.search/query", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("base.vector.invalid");
    }

    private static async Task<WebApplication> CreateAsync(bool mapVector)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization(options => options.AddPolicy("application", policy => policy.RequireAssertion(static _ => true)));
        builder.Services.AddHPDBase(baseBuilder => baseBuilder
            .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 1, Key = new byte[32], IssueNotBefore = DateTimeOffset.UnixEpoch })
            .AddPolicyAuthority<AllowVectorPolicy>(new BasePolicyAuthorityDefinition
            {
                Id = "hpd.base.vector.endpoint.allow", Version = 1, OwningModuleId = "hpd.base.vector.tests",
                EvaluatorContractId = "hpd.base.vector.endpoint.policy", EvaluatorContractVersion = 1, CompositionOrder = 0,
            })
            .AddCollection(HttpVectorDocument.Collection)

            .UseTestVectorProvider());
        builder.Services.AddHPDBaseAspNetCore();
        builder.Services.AddHPDBaseVectorAspNetCore(options => options.MaxRequestBodyBytes = 16 * 1024);
        WebApplication app = builder.Build();
        app.UseAuthorization();
        RouteGroupBuilder group = app.MapHPDBaseApplicationApi(new HPDBaseApplicationEndpointOptions { AuthorizationPolicy = "application", MapRecords = true });
        if (mapVector) group.MapHPDBaseVectorApplicationApi();
        await app.StartAsync();
        (await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        app.Services.GetRequiredService<BaseTestVectorStore>().Seed(HttpVectorDocument.Collection.Id, HttpVectorDocument.VectorIndexes.Search.Definition.Id,
        [
            new BaseTestVectorEntry
            {
                Record = new RecordEnvelope
                {
                    CollectionId = HttpVectorDocument.Collection.Id,
                    Id = new RecordId("one"),
                    Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = new Dictionary<string, JsonElement> { [nameof(HttpVectorDocument.Label)] = JsonSerializer.SerializeToElement("one"), [nameof(HttpVectorDocument.Embedding)] = JsonSerializer.SerializeToElement(new[] { 1f, 0f }) } },
                    Metadata = new RecordMetadata { Revision = new RevisionToken("test:1") },
                },
                Vector = BaseVector.Create([1, 0]),
            },
        ]);
        return app;
    }

    private sealed class ChunkedContent(string value) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            return stream.WriteAsync(bytes).AsTask();
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}

[BaseCollection("http_vectors", typeof(HttpVectorJsonContext))]
[BaseVectorIndex("http.vector.search", nameof(HttpVectorDocument.Embedding), VectorSpace = "http.space.v1", Dimensions = 2, Function = BaseVectorFunction.CosineSimilarity)]
public partial record HttpVectorDocument
{
    [BaseField("http.vector.label")] public required string Label { get; init; }
    [BaseField("http.vector.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
}

[JsonSerializable(typeof(HttpVectorDocument))]
public partial class HttpVectorJsonContext : JsonSerializerContext;

public sealed class AllowVectorPolicy : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}
