using System.Net;
using FluentAssertions;
using HPD.Base.AspNetCore;
using HPD.Base.Vector.AspNetCore;
using HPD.Base.Vector.Testing;
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

    private static async Task<WebApplication> CreateAsync(bool mapVector)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization(options => options.AddPolicy("application", policy => policy.RequireAssertion(static _ => true)));
        builder.Services.AddHPDBase(baseBuilder => baseBuilder
            .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 1, Key = new byte[32], IssueNotBefore = DateTimeOffset.UnixEpoch })
            .AddVector()
            .UseTestVectorProvider());
        builder.Services.AddHPDBaseAspNetCore();
        builder.Services.AddHPDBaseVectorAspNetCore(options => options.MaxRequestBodyBytes = 16 * 1024);
        WebApplication app = builder.Build();
        app.UseAuthorization();
        RouteGroupBuilder group = app.MapHPDBaseApplicationApi(new HPDBaseApplicationEndpointOptions { AuthorizationPolicy = "application", MapRecords = true });
        if (mapVector) group.MapHPDBaseVectorApplicationApi();
        await app.StartAsync();
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
