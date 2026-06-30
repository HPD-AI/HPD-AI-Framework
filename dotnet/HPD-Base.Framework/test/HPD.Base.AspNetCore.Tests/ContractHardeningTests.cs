using System.Security.Claims;
using HPD.Base.AspNetCore.Configuration;
using HPD.Base.AspNetCore.EndpointMapping;
using HPD.Base.AspNetCore.Http;
using HPD.Base.AspNetCore.Results;
using HPD.Base.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore.Tests;

public sealed class ContractHardeningTests
{
    [Fact]
    public async Task ManifestResponsesEmitCorrelationAndDescriptorEtags()
    {
        await using var app = await TestBaseApp.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/base/manifest");
        request.Headers.Add(BaseHttpHeaders.CorrelationId, "corr-123");

        var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues(BaseHttpHeaders.CorrelationId).Should().ContainSingle().Which.Should().Be("corr-123");
        response.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordRouteDescriptorsRequireRuntimeRecordFeatures()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var manifestResponse = await app.GetTestClient().GetAsync("/base/manifest");
        var manifest = await ReadJson<BaseManifest>(app, manifestResponse.Content);

        var routes = manifest!.Projections!
            .Single(projection => projection.Id == "hpd.base.aspnetcore")
            .Routes!;

        routes.Single(route => route.OperationId == BaseRouteIds.RecordsList).RequiredFeatureIds.Should().Contain(BaseFeatureIds.RecordsList);
        routes.Single(route => route.OperationId == BaseRouteIds.RecordsQuery).RequiredFeatureIds.Should().Contain(BaseFeatureIds.RecordsQuery);
        routes.Single(route => route.OperationId == BaseRouteIds.RecordsGet).RequiredFeatureIds.Should().Contain(BaseFeatureIds.RecordsGet);
        routes.Single(route => route.OperationId == BaseRouteIds.RecordsCreate).RequiredFeatureIds.Should().Contain(BaseFeatureIds.RecordsCreate);
        routes.Single(route => route.OperationId == BaseRouteIds.RecordsPatch).RequiredFeatureIds.Should().Contain(BaseFeatureIds.RecordsPatch);
        routes.Single(route => route.OperationId == BaseRouteIds.RecordsReplace).RequiredFeatureIds.Should().Contain(BaseFeatureIds.RecordsReplace);
        routes.Single(route => route.OperationId == BaseRouteIds.RecordsDelete).RequiredFeatureIds.Should().Contain(BaseFeatureIds.RecordsDelete);
    }

    [Fact]
    public async Task RevisionAndIdempotencyHeaderConflictsReturnProblemDetails()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/base/collections/items/records")
        {
            Content = JsonContent.Create(new RecordCreateRequest
            {
                IdempotencyKey = "body",
                Payload = TestBaseApp.Payload(("title", "hello"))
            }, HPDBaseJsonSerializerContext.Default.RecordCreateRequest)
        };
        createRequest.Headers.Add(BaseHttpHeaders.IdempotencyKey, "header");
        var createResponse = await client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, "/base/collections/items/records/abc")
        {
            Content = JsonContent.Create(new RecordPatchRequest
            {
                ExpectedRevision = new RevisionToken("body"),
                Patch = TestBaseApp.Patch("title", "patched")
            }, HPDBaseJsonSerializerContext.Default.RecordPatchRequest)
        };
        patchRequest.Headers.TryAddWithoutValidation(BaseHttpHeaders.IfMatch, "\"header\"");
        var patchResponse = await client.SendAsync(patchRequest);

        patchResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await patchResponse.Content.ReadAsStringAsync();
        body.Should().Contain("base.http.revision.conflict");
    }

    [Fact]
    public async Task QueryAndRouteTransportLimitsAreEnforced()
    {
        await using var app = await TestBaseApp.CreateAsync(options =>
        {
            options.Limits.MaxQueryListItems = 1;
            options.Limits.MaxRouteIdLength = 3;
        });
        var client = app.GetTestClient();

        var queryResponse = await client.GetAsync("/base/collections/items/records?select=one,two");
        queryResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await queryResponse.Content.ReadAsStringAsync()).Should().Contain("base.http.query.tooManyListItems");

        var routeResponse = await client.GetAsync("/base/collections/items/records/abcd");
        routeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await routeResponse.Content.ReadAsStringAsync()).Should().Contain("base.http.recordId.invalid");
    }

    [Fact]
    public async Task PrincipalAndOperationContextCarryTenantAndClassifications()
    {
        await using var app = await TestBaseApp.CreateAsync(options => options.Auth.AdminRoleNames = ["base-admin"]);
        var principalFactory = app.Services.GetRequiredService<IBaseHttpPrincipalContextFactory>();
        var operationFactory = app.Services.GetRequiredService<IBaseHttpOperationContextFactory>();

        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "trace";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "u1"),
            new Claim("tenant_id", "tenant-a"),
            new Claim("tenant_ids", "tenant-a,tenant-b"),
            new Claim("role", "base-admin"),
            new Claim("azp", "svc-1")
        ], "test"));

        var principal = await principalFactory.CreateAsync(httpContext, HPDBaseEndpointKind.AdminMetadata);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.SchemaRead, "items");

        principal.AuthenticationState.Should().Be(PrincipalAuthenticationState.Admin);
        principal.SubjectKind.Should().Be(AccessSubjectKind.Admin);
        principal.TenantMemberships!.Select(membership => membership.TenantId).Should().BeEquivalentTo(["tenant-a", "tenant-b"]);
        operation.TenantId.Should().Be("tenant-a");
        operation.CorrelationId.Should().Be("trace");
    }

    private static async Task<T?> ReadJson<T>(WebApplication app, HttpContent content)
    {
        var json = await content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, app.Services.GetRequiredService<IHPDBaseRuntime>().Json.Options);
    }
}
