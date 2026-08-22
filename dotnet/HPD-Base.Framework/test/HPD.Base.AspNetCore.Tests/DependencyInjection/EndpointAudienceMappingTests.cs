using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace HPD.Base.AspNetCore.Tests.DependencyInjection;

public sealed class EndpointAudienceMappingTests
{
    [Fact]
    public async Task ActivationWorkerInventoryIsApplicationOnlyAndExact()
    {
        await using WebApplication app = await TestBaseApp.CreateAsync(
            configureEndpoints: options => options.MapActivations = true);

        string[] routes =
        [
            "/base/activations/enqueue",
            "/base/activations/claims/next",
            "/base/activations/claims/renew",
            "/base/activations/complete",
            "/base/activations/fail",
            "/base/activations/cancel",
            "/base/activations/receipts/resolve",
            "/base/activations/effects/begin",
            "/base/activations/effects/heartbeat",
            "/base/activation-executors/register",
            "/base/activation-executors/heartbeat",
            "/base/activation-executors/retire",
        ];
        RouteEndpoint[] workerEndpoints = app.RouteEndpoints()
            .Where(endpoint => routes.Contains(endpoint.RoutePattern.RawText, StringComparer.Ordinal))
            .ToArray();

        workerEndpoints.Should().HaveCount(routes.Length);
        workerEndpoints.Should().OnlyContain(endpoint =>
            endpoint.Metadata.GetRequiredMetadata<HPDBaseEndpointDescriptor>().Audience
                == HPDBaseEndpointAudience.Application);
        workerEndpoints.Should().OnlyContain(endpoint =>
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(data => data.Policy == "test-application"));

        HttpClient client = app.GetTestClient();
        HttpResponseMessage absent = await client.PostAsync(
            "/base/activations/claims/next",
            new StringContent(
                """{"definitionId":"private.worker","definitionVersion":1,"identity":{"scope":"test","operation":"claim","idempotencyKey":"claim-1","fingerprint":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]}}""",
                System.Text.Encoding.UTF8,
                "application/json"));
        absent.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);

        HttpResponseMessage open = await client.PostAsync(
            "/base/activations/claims/next",
            new StringContent(
                """{"definitionId":"private.worker","definitionVersion":1,"identity":{"scope":"test","operation":"claim","idempotencyKey":"claim-1","fingerprint":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]},"unknown":true}""",
                System.Text.Encoding.UTF8,
                "application/json"));
        open.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden,
            "invalid and undiscoverable definitions remain non-enumerating");
    }

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
