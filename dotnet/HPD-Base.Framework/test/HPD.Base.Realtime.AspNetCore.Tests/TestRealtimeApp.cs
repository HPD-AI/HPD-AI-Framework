namespace HPD.Base.Realtime.AspNetCore.Tests;

internal static class TestRealtimeApp
{
    public static async Task<WebApplication> CreateAsync(Action<HPD.Base.Realtime.Configuration.BaseRealtimeOptions>? configureRealtime = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        builder.Services.AddHPDBaseRuntime()
            .AddHPDBaseAspNetCore()
            .AddHPDBaseRealtime(configureRealtime)
            .AddHPDBaseRealtimeAspNetCore()
            .AddHPDBaseInMemoryStore(options =>
            {
                options.StoreId = "primary";
                options.CollectionIds = ["items"];
                options.Collections =
                [
                    new CollectionDefinition
                    {
                        Id = "items",
                        Name = "items",
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
                            Delete = true
                        }
                    }
                ];
            });

        var app = builder.Build();
        app.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(app.Services);
        await app.Services.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
        app.MapHPDBaseRealtime();
        await app.StartAsync();
        return app;
    }

    public static BaseRecordMutationEvent Event() => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        Type = "record.created",
        SchemaVersion = BaseEventSchemaVersions.V1,
        Visibility = VisibilityLevel.Public,
        Resource = new EventResource
        {
            Kind = EventResourceKind.Record,
            CollectionId = "items",
            RecordId = new RecordId("one")
        },
        Operation = BaseOperationKind.Create,
        After = new RecordSnapshot
        {
            CollectionId = "items",
            Id = new RecordId("one"),
            Payload = Payload(("title", "hello")),
            Metadata = new RecordMetadata()
        }
    };

    private static RecordPayload Payload(params (string Name, string Value)[] fields)
    {
        var json = "{" + string.Join(",", fields.Select(field => $"\"{field.Name}\":\"{field.Value}\"")) + "}";
        using var document = JsonDocument.Parse(json);
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
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
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = PolicyOutcome.Allowed
        });
    }
}
