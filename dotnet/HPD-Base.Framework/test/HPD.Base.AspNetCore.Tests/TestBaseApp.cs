namespace HPD.Base.AspNetCore.Tests;

internal static class TestBaseApp
{
    public static async Task<WebApplication> CreateAsync(
        Action<HPD.Base.AspNetCore.HPDBaseAspNetCoreOptions>? configureAspNetCore = null,
        IPolicyEvaluator? policyEvaluator = null,
        Action<IServiceCollection>? configureServices = null,
        Action<HPD.Base.AspNetCore.HPDBaseEndpointOptions>? configureEndpoints = null,
        Action<HPD.Base.AspNetCore.HPDBaseOpenApiOptions>? configureOpenApi = null,
        Action<HPD.Base.AspNetCore.HPDBaseOpenApiEndpointOptions>? configureOpenApiEndpoints = null,
        bool mapOpenApi = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(policyEvaluator ?? new AllowPolicyEvaluator());
        configureServices?.Invoke(builder.Services);
        if (mapOpenApi)
            builder.Services.AddHPDBaseOpenApi(configureOpenApi);
        builder.Services.AddHPDBaseRuntime()
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
        app.MapHPDBaseApi(options =>
        {
            options.RequireAuthorizationForAdminRoutes = false;
            configureEndpoints?.Invoke(options);
        });
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
        Operations = new CollectionOperationMatrix
        {
            List = true,
            Get = true,
            Create = true,
            Patch = true,
            Replace = true,
            Upsert = true,
            Delete = true
        }
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
