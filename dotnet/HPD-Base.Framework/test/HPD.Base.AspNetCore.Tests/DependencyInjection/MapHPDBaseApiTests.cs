using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;

namespace HPD.Base.AspNetCore.Tests.DependencyInjection;

public sealed class MapHPDBaseApiTests
{
    [Fact]
    public void UnifiedBuilderMapIsSideEffectFreeBeforeHostStartup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        var items = BaseCollection<JsonElement>.Create(
            TestBaseApp.Collection(),
            HPDBaseJsonSerializerContext.Default.JsonElement,
            static _ => { });
        builder.Services.AddHPDBase(hpd => hpd
            .AddAspNetCore()
            .AddCollection(items));

        using var app = builder.Build();
        app.MapHPDBaseApi();

        app.Services.GetRequiredService<IRecordStoreRegistry>()
            .GetStoreForCollection("items")
            .Should()
            .BeNull();
        app.Services.GetRequiredService<IHPDBaseApplication>()
            .CurrentReadiness.State.Should().Be(BaseApplicationReadinessState.NotStarted);
    }

    [Fact]
    public async Task MapsConfiguredPrefixAndToggles()
    {
        await using var app = await TestBaseApp.CreateAsync(configureEndpoints: options =>
        {
            options.RoutePrefix = "/v1/base";
            options.MapRecords = false;
        });

        var client = app.GetTestClient();
        (await client.GetAsync("/v1/base/manifest")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/base/manifest")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/v1/base/collections/items/records")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminRoutesCarryAuthorizationMetadataWhenEnabled()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder().AddPolicy("admin", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        builder.Services.AddHPDBaseRuntime().AddHPDBaseAspNetCore().AddHPDBaseVolatileStore(options =>
        {
            options.StoreId = "primary";
            options.CollectionIds = ["items"];
            options.Collections = [TestBaseApp.Collection()];
        });

        await using var app = builder.Build();
        app.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseVolatileStore(app.Services);
        await app.Services.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
        app.MapHPDBaseApi(options => options.AdminPolicyName = "admin");
        await app.StartAsync();

        app.RouteEndpoints()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/base/admin/manifest")
            .Metadata
            .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
            .Should()
            .ContainSingle(data => data.Policy == "admin");
    }

    [Fact]
    public async Task RecordRoutesCarryAuthorizationMetadataWhenEnabled()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder().AddPolicy("records", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        builder.Services.AddHPDBaseRuntime().AddHPDBaseAspNetCore().AddHPDBaseVolatileStore(options =>
        {
            options.StoreId = "primary";
            options.CollectionIds = ["items"];
            options.Collections = [TestBaseApp.Collection()];
        });

        await using var app = builder.Build();
        app.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseVolatileStore(app.Services);
        await app.Services.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
        app.MapHPDBaseApi(options =>
        {
            options.RequireAuthorizationForRecordRoutes = true;
            options.RecordPolicyName = "records";
        });
        await app.StartAsync();

        app.RouteEndpoints()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/base/collections/{collectionId}/records"
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Metadata
            .OfType<IAuthorizeData>()
            .Should()
            .ContainSingle(data => data.Policy == "records");
    }

    [Fact]
    public async Task ControlPlanePresetRequiresRecordAndAdminAuthorizationAndHidesDiagnostics()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(HPDBasePolicies.Authenticated, policy => policy.RequireAssertion(_ => true))
            .AddPolicy(HPDBasePolicies.Admin, policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        builder.Services.AddHPDBaseRuntime().UseFailClosedPolicy().AddHPDBaseAspNetCore().AddHPDBaseVolatileStore(options =>
        {
            options.StoreId = "primary";
            options.CollectionIds = ["items"];
            options.Collections = [TestBaseApp.Collection()];
        });

        await using var app = builder.Build();
        app.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseVolatileStore(app.Services);
        await app.Services.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
        app.MapHPDBaseControlPlaneApi("/control");
        await app.StartAsync();

        app.RouteEndpoints()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/control/collections/{collectionId}/records"
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Metadata
            .OfType<IAuthorizeData>()
            .Should()
            .ContainSingle(data => data.Policy == HPDBasePolicies.Authenticated);

        app.RouteEndpoints()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/control/admin/manifest")
            .Metadata
            .OfType<IAuthorizeData>()
            .Should()
            .ContainSingle(data => data.Policy == HPDBasePolicies.Admin);

        app.RouteEndpoints()
            .Should()
            .NotContain(endpoint => endpoint.RoutePattern.RawText == "/control/diagnostics");

        app.RouteEndpoints()
            .Should()
            .Contain(endpoint => endpoint.RoutePattern.RawText == "/control/manifest");

        app.RouteEndpoints()
            .Should()
            .NotContain(endpoint => endpoint.RoutePattern.RawText == "/control/schema");

        app.RouteEndpoints()
            .Should()
            .NotContain(endpoint => endpoint.RoutePattern.RawText == "/control/collections");

        app.RouteEndpoints()
            .Should()
            .Contain(endpoint => endpoint.RoutePattern.RawText == "/control/admin/policy/explain");
    }

    [Fact]
    public void InvalidPrefixThrows()
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        var act = () => app.MapHPDBaseApi(options => options.RoutePrefix = "base");
        act.Should().Throw<ArgumentException>();
    }
}
