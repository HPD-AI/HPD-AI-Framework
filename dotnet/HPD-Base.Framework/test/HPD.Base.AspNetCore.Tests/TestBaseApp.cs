namespace HPD.Base.AspNetCore.Tests;

internal static class TestBaseApp
{
    public static async Task<WebApplication> CreateAsync(
        Action<HPD.Base.AspNetCore.HPDBaseAspNetCoreOptions>? configureAspNetCore = null,
        IPolicyEvaluator? policyEvaluator = null,
        Action<IServiceCollection>? configureServices = null,
        Action<TestEndpointOptions>? configureEndpoints = null,
        Action<HPD.Base.AspNetCore.HPDBaseOpenApiOptions>? configureOpenApi = null,
        Action<HPD.Base.AspNetCore.HPDBaseOpenApiEndpointOptions>? configureOpenApiEndpoints = null,
        bool mapOpenApi = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("test-application", policy => policy.RequireAssertion(_ => true))
            .AddPolicy("test-control-plane", policy => policy.RequireAssertion(_ => true));
        IPolicyEvaluator installedPolicy = policyEvaluator ?? new AllowPolicyEvaluator();
        builder.Services.AddSingleton<IBaseHttpPrincipalMapper, TestPrincipalMapper>();
        configureServices?.Invoke(builder.Services);
        if (mapOpenApi)
            builder.Services.AddHPDBaseOpenApi(configureOpenApi);
        builder.Services.AddHPDBaseRuntime()
            .UsePolicyAuthority("hpd.base.application", new BasePolicyAuthorityDefinition
            {
                Id = "test.policy", Version = 1, OwningModuleId = "test",
                EvaluatorContractId = "test.policy.evaluator", EvaluatorContractVersion = 1,
                CompositionOrder = 0,
            }, installedPolicy)
            .AddHPDBaseAspNetCore(configureAspNetCore)
            .AddHPDBaseInMemoryStore(options =>
            {
                options.StoreId = "primary";
                options.CollectionIds = ["items"];
                options.Collections = [Collection()];
            });

        var app = builder.Build();
        app.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(app.Services);
        await app.Services.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
        var endpoints = new TestEndpointOptions();
        configureEndpoints?.Invoke(endpoints);
        app.MapHPDBasePublicApi(options =>
        {
            options.RoutePrefix = endpoints.RoutePrefix;
            options.MetadataMode = HPDBasePublicMetadataMode.Full;
            options.MapDiagnostics = true;
        });
        if (endpoints.MapRecords || endpoints.MapActivations)
            app.MapHPDBaseApplicationApi(new HPDBaseApplicationEndpointOptions
            {
                RoutePrefix = endpoints.RoutePrefix,
                AuthorizationPolicy = "test-application",
                MapRecords = true,
                MapRegisteredReads = false,
                MapActivations = endpoints.MapActivations,
            });
        var control = app.MapGroup(endpoints.RoutePrefix).RequireAuthorization(endpoints.ControlPlanePolicy);
        control.MapHPDBaseControlPlaneEndpoints(
            app,
            new HPDBaseControlPlaneEndpointSelection
            {
                MapRecords = false,
                MapRegisteredReads = true,
                MapAdministration = true,
                MapPolicyExplain = endpoints.MapPolicyExplain
            },
            (endpoint, _) => endpoint.RequireAuthorization(endpoints.ControlPlanePolicy));
        if (mapOpenApi)
            app.MapHPDBaseOpenApi(configureOpenApiEndpoints);
        await app.StartAsync();
        return app;
    }

    public static CollectionDefinition Collection(string id = "items") => new()
    {
        Id = id,
        Name = id,
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        Fields =
        [
            new FieldDefinition
            {
                Id = "title",
                ApplicationName = "title", WireName = "title",
                Type = BaseFieldTypes.String,
            },
        ],
        MutationMode = BaseCollectionMutationMode.Mutable
    };

    public static RecordPayload Payload(params (string Name, string Value)[] fields)
    {
        var json = "{" + string.Join(",", fields.Select(field => $"\"{field.Name}\":\"{field.Value}\"")) + "}";
        using var document = JsonDocument.Parse(json);
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

    public static RecordPayload Patch(string name, string value)
    {
        using var document = JsonDocument.Parse($"{{\"{name}\":\"{value}\"}}");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, JsonElement>
            {
                [name] = document.RootElement.GetProperty(name).Clone()
            }
        };
    }
}

internal sealed class TestPrincipalMapper(DefaultBaseHttpPrincipalMapper generic) : IBaseHttpPrincipalMapper
{
    public ValueTask<PrincipalContext> MapAsync(
        Microsoft.AspNetCore.Http.HttpContext httpContext,
        HPDBaseEndpointDescriptor endpoint,
        CancellationToken cancellationToken = default) =>
        generic.MapAsync(httpContext, endpoint.Audience == HPDBaseEndpointAudience.ControlPlane
            ? endpoint with { Audience = HPDBaseEndpointAudience.Application }
            : endpoint, cancellationToken);
}

internal sealed class TestEndpointOptions
{
    public string RoutePrefix { get; set; } = "/base";
    public bool MapRecords { get; set; } = true;
    public bool MapPolicyExplain { get; set; }
    public bool MapActivations { get; set; }
    public string ControlPlanePolicy { get; set; } = "test-control-plane";
}

internal sealed class AllowPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = PolicyOutcome.Allowed
        });
    }
}
