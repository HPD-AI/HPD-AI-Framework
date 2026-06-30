using HPD.Base.AspNetCore.EndpointMapping;
using HPD.Base.AspNetCore.Http;
using HPD.Base.Runtime.Policy.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.Text;

namespace HPD.Base.AspNetCore.Tests.Endpoints;

public sealed class AdminPolicyExplainEndpointTests
{
    [Fact]
    public async Task AdminPolicyExplainRoute_IsAbsentByDefault()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();

        var admin = await client.PostAsJsonAsync("/base/admin/policy/explain", new { });
        var publicRoute = await client.PostAsJsonAsync("/base/policy/explain", new { });

        admin.StatusCode.Should().Be(HttpStatusCode.NotFound);
        publicRoute.StatusCode.Should().Be(HttpStatusCode.NotFound);
        app.RouteEndpoints().Should().NotContain(endpoint => endpoint.RoutePattern.RawText == "/base/admin/policy/explain");
    }

    [Fact]
    public async Task AdminPolicyExplainRoute_ServiceGateDeniesAnonymousWhenRouteAuthorizationIsDisabled()
    {
        await using var app = await TestBaseApp.CreateAsync(configureEndpoints: options => options.MapAdminPolicyExplain = true);

        var response = await app.GetTestClient().PostAsJsonAsync("/base/admin/policy/explain", new BasePolicyExplainRequest
        {
            Operation = BasePolicyExplainOperation.Query,
            CollectionId = "items"
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        body.Should().Contain("base.policyExplain.unauthorized");
        body.Should().NotContain("explainId");
    }

    [Fact]
    public async Task AdminPolicyExplainRoute_CarriesAuthorizationMetadataWhenEnabled()
    {
        await using var app = await TestBaseApp.CreateAsync(configureEndpoints: options =>
        {
            options.RequireAuthorizationForAdminRoutes = true;
            options.MapAdminPolicyExplain = true;
        });

        var endpoint = app.RouteEndpoints().Single(endpoint => endpoint.RoutePattern.RawText == "/base/admin/policy/explain");

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should()
            .Contain(metadata => metadata.Policy == HPDBasePolicies.Admin);
    }

    [Fact]
    public async Task AdminPolicyExplainRoute_InvalidJsonReturnsValidationProblemDetails()
    {
        await using var app = await TestBaseApp.CreateAsync(
            configureServices: services => services.AddSingleton<IBaseHttpPrincipalMapper>(new FixedPrincipalMapper(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Admin
            })),
            configureEndpoints: options => options.MapAdminPolicyExplain = true);
        var content = new StringContent("{ nope", Encoding.UTF8, "application/json");

        var response = await app.GetTestClient().PostAsync("/base/admin/policy/explain", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        body.Should().Contain("base.policyExplain.request.invalidJson");
        body.Should().NotContain("explainId");
    }

    [Fact]
    public async Task AdminPolicyExplainRoute_UsesSourceGeneratedJsonMetadata()
    {
        await using var app = await TestBaseApp.CreateAsync(configureEndpoints: options => options.MapAdminPolicyExplain = true);

        app.HttpJsonOptions().SerializerOptions.GetTypeInfo(typeof(BasePolicyExplainRequest)).Should().NotBeNull();
        app.HttpJsonOptions().SerializerOptions.GetTypeInfo(typeof(BasePolicyExplainResponse)).Should().NotBeNull();
    }

    [Fact]
    public async Task AdminPolicyExplainRoute_AdminGetsJsonAndNoStoreMutation()
    {
        await using var app = await TestBaseApp.CreateAsync(
            configureServices: services => services.AddSingleton<IBaseHttpPrincipalMapper>(new FixedPrincipalMapper(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Admin
            })),
            configureEndpoints: options => options.MapAdminPolicyExplain = true);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/base/admin/policy/explain", new BasePolicyExplainRequest
        {
            Operation = BasePolicyExplainOperation.Create,
            CollectionId = "items",
            Create = new RecordCreateRequest
            {
                Payload = TestBaseApp.Payload(("title", "secret-title"))
            },
            Options = new BasePolicyExplainOptions { IncludeRedactedPayloadShape = true }
        });
        var json = await response.Content.ReadAsStringAsync();
        var explain = JsonSerializer.Deserialize<BasePolicyExplainResponse>(json, app.Services.GetRequiredService<IHPDBaseRuntime>().Json.Options);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().Should().Be("no-store");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        explain!.Outcome.Should().Be(BasePolicyExplainOutcome.Allowed);
        explain.Runtime!.StoreMutationExecuted.Should().BeFalse();
        explain.Redaction!.OmittedPayloadFields.Should().Contain("title");
        json.Should().NotContain("secret-title");
    }

    [Fact]
    public async Task AdminManifest_HidesPolicyExplainFromPublicViewAndShowsAdminRoute()
    {
        await using var app = await TestBaseApp.CreateAsync(
            configureServices: services => services.AddSingleton<IBaseHttpPrincipalMapper>(new FixedPrincipalMapper(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Admin
            })),
            configureEndpoints: options => options.MapAdminPolicyExplain = true);
        var client = app.GetTestClient();

        var publicManifest = await client.GetAsync("/base/manifest");
        var adminManifest = await client.GetAsync("/base/admin/manifest");
        var publicJson = await publicManifest.Content.ReadAsStringAsync();
        var adminJson = await adminManifest.Content.ReadAsStringAsync();

        publicJson.Should().NotContain("base.admin.policy.explain");
        publicJson.Should().NotContain("policy.explain.admin");
        publicJson.Should().NotContain("base.policyExplainRequest");
        publicJson.Should().NotContain("base.policyExplainResponse");
        adminJson.Should().Contain("base.admin.policy.explain");
        adminJson.Should().Contain("policy.explain.admin");
        adminJson.Should().Contain("base.policyExplainRequest");
        adminJson.Should().Contain("base.policyExplainResponse");
    }

    private sealed class FixedPrincipalMapper : IBaseHttpPrincipalMapper
    {
        private readonly PrincipalContext _principal;

        public FixedPrincipalMapper(PrincipalContext principal)
        {
            _principal = principal;
        }

        public ValueTask<PrincipalContext?> TryMapAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = httpContext;
            return ValueTask.FromResult<PrincipalContext?>(_principal);
        }
    }
}
