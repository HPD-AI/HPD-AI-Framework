using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

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
    public void UnifiedAspNetCoreRegistrationIncludesRealtimeProjectionServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        builder.Services.AddHPDBase(hpd => hpd.AddRealtime().AddAspNetCore());
        using var app = builder.Build();

        app.Services.GetService<BaseRealtimeWebSocketEndpoint>().Should().NotBeNull();
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

        RouteEndpoint reconciliation = app.RouteEndpoints().Single(endpoint =>
            endpoint.RoutePattern.RawText == "/base/control/activations/reconcile");
        HPDBaseEndpointDescriptor control = reconciliation.Metadata.GetRequiredMetadata<HPDBaseEndpointDescriptor>();
        control.EndpointId.Should().Be("base.activation.reconcile");
        control.Audience.Should().Be(HPDBaseEndpointAudience.ControlPlane);
        control.Operation.Should().Be(HPDBaseEndpointOperation.ActivationReconcile);
        control.Capability.Should().Be(HPDBaseCapabilities.ActivationReconcile);
        reconciliation.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().Contain(data => data.Policy == "test-control-plane");
    }

    [Fact]
    public void InvalidPublicPrefixThrows()
    {
        using WebApplication app = WebApplication.CreateBuilder().Build();
        Action action = () => app.MapHPDBasePublicApi(options => options.RoutePrefix = "base");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void InventoryRejectsAValidLookingButWrongCapabilityTuple()
    {
        var builder = new RouteEndpointBuilder(
            static _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/base/collections/{collectionId}/records"),
            0);
        builder.Metadata.Add(new HttpMethodMetadata([HttpMethods.Get]));
        builder.Metadata.Add(new EndpointNameMetadata("base.records.list"));
        builder.Metadata.Add(new AuthorizeAttribute("records"));
        builder.Metadata.Add(new HPDBaseApplicationPolicyMetadata("records"));
        builder.Metadata.Add(new HPDBaseEndpointDescriptor
        {
            EndpointId = "base.records.list",
            Audience = HPDBaseEndpointAudience.Application,
            Operation = HPDBaseEndpointOperation.RecordRead,
            Capability = HPDBaseCapabilities.FilesDelete
        });
        var validator = new HPDBaseEndpointInventoryValidator(
            new DefaultEndpointDataSource(builder.Build()),
            []);

        Action action = validator.Validate;

        action.Should().Throw<InvalidOperationException>().WithMessage("base.http.endpoint.capabilityInvalid");
    }
}
