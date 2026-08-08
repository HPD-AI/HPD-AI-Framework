using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace HPD.Base.AspNetCore.Tests.DependencyInjection;

public sealed class EndpointAudienceMappingTests
{
    [Fact]
    public void MappingDoesNotInitializeApplication()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorizationBuilder().AddPolicy("records", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        var items = BaseCollection<JsonElement>.Create(TestBaseApp.Collection(), HPDBaseJsonSerializerContext.Default.JsonElement, static _ => { });
        builder.Services.AddHPDBase(hpd => hpd.AddAspNetCore().AddCollection(items));
        using var app = builder.Build();
        app.MapHPDBasePublicApi();
        app.MapHPDBaseApplicationApi(new() { AuthorizationPolicy = "records" });
        app.Services.GetRequiredService<IHPDBaseApplication>().CurrentReadiness.State.Should().Be(BaseApplicationReadinessState.NotStarted);
    }

    [Fact]
    public async Task PublicAndApplicationInventoriesAreDistinctAndExact()
    {
        await using WebApplication app = await TestBaseApp.CreateAsync();
        RouteEndpoint manifest = app.RouteEndpoints().Single(endpoint => endpoint.RoutePattern.RawText == "/base/manifest");
        HPDBaseEndpointDescriptor publicDescriptor = manifest.Metadata.GetRequiredMetadata<HPDBaseEndpointDescriptor>();
        publicDescriptor.Should().BeEquivalentTo(new HPDBaseEndpointDescriptor
        {
            EndpointId = "base.manifest",
            Audience = HPDBaseEndpointAudience.Public,
            Operation = HPDBaseEndpointOperation.MetadataRead,
            Capability = null
        });
        RouteEndpoint records = app.RouteEndpoints().Single(endpoint => endpoint.RoutePattern.RawText == "/base/collections/{collectionId}/records"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
        HPDBaseEndpointDescriptor application = records.Metadata.GetRequiredMetadata<HPDBaseEndpointDescriptor>();
        application.Audience.Should().Be(HPDBaseEndpointAudience.Application);
        application.Capability.Should().Be(HPDBaseCapabilities.RecordsRead);
        records.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(data => data.Policy == "test-application");
    }

    [Fact]
    public void InvalidPublicPrefixThrows()
    {
        using WebApplication app = WebApplication.CreateBuilder().Build();
        Action action = () => app.MapHPDBasePublicApi(options => options.RoutePrefix = "base");
        action.Should().Throw<ArgumentException>();
    }
}
