using Microsoft.AspNetCore.Routing;

namespace HPD.Base.AspNetCore.Tests.DependencyInjection;

public sealed class MapHPDBaseApiTests
{
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
        builder.Services.AddHPDBaseRuntime().AddHPDBaseAspNetCore().AddHPDBaseInMemoryStore(options =>
        {
            options.StoreId = "primary";
            options.CollectionIds = ["items"];
            options.Collections = [TestBaseApp.Collection()];
        });

        await using var app = builder.Build();
        app.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(app.Services);
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
    public void InvalidPrefixThrows()
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        var act = () => app.MapHPDBaseApi(options => options.RoutePrefix = "base");
        act.Should().Throw<ArgumentException>();
    }
}
