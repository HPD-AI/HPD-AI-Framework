namespace HPD.Base.Tests.Realtime;

internal static class TestServices
{
    public static async Task<ServiceProvider> CreateAsync(
        IPolicyEvaluator? evaluator = null,
        Action<HPD.Base.BaseRealtimeOptions>? configureRealtime = null,
        IEnumerable<BaseMutationJournalEntry>? journalEntries = null,
        bool enableDependencies = false,
        Action<HPD.Base.BaseDependencyOptions>? configureDependencies = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (enableDependencies)
            services.AddHPDBaseDependencies(options =>
            {
                options.ProtectionKey = Enumerable.Repeat((byte)0x5A, 32).ToArray();
                configureDependencies?.Invoke(options);
            });
        services.AddSingleton(evaluator ?? new AllowPolicyEvaluator());
        services.AddHPDBaseRuntime()
            .AddHPDBaseRealtime(configureRealtime)
            .AddHPDBaseInMemoryStore(options =>
            {
                options.StoreId = "primary";
                options.CollectionIds = ["items", "other"];
                options.Collections =
                [
                    new CollectionDefinition
                    {
                        Id = "items",
                        Name = "items",
                        Kind = BaseCollectionKinds.Document,
                        SchemaMode = SchemaMode.Loose,
                        UnknownFields = UnknownFieldPolicy.Preserve,
                        Fields =
                        [
                            new FieldDefinition { Id = "title", Name = "title", Type = BaseFieldTypes.String },
                            new FieldDefinition { Id = "secret", Name = "secret", Type = BaseFieldTypes.String, Hidden = true },
                            new FieldDefinition
                            {
                                Id = "writeOnly",
                                Name = "writeOnly",
                                Type = BaseFieldTypes.String,
                                Visibility = new FieldVisibilityAnnotation { WriteOnly = true }
                            }
                        ],
                        MutationMode = BaseCollectionMutationMode.Mutable
                    },
                    new CollectionDefinition
                    {
                        Id = "other",
                        Name = "other",
                        Kind = BaseCollectionKinds.Document,
                        SchemaMode = SchemaMode.Loose,
                        UnknownFields = UnknownFieldPolicy.Preserve
                    }
                ];
            });
        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HPD.Base.IRecordStoreRegistry>();
        registry.AddHPDBaseInMemoryStore(provider);
        if (journalEntries is not null)
        {
            var journal = new TestMutationJournalStore(
                provider.GetRequiredService<HPD.Base.IRecordStore>(),
                journalEntries);
            registry.Add(new HPD.Base.RecordStoreRegistration
            {
                StoreId = journal.Capabilities.StoreId,
                Store = journal,
                CollectionIds = ["items", "other"]
            });
        }
        await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
        return provider;
    }

    public static PrincipalContext Principal(string? tenantId = null, PrincipalAuthenticationState state = PrincipalAuthenticationState.Authenticated) => new()
    {
        AuthenticationState = state,
        SubjectKind = state == PrincipalAuthenticationState.Anonymous ? AccessSubjectKind.Anonymous : AccessSubjectKind.User,
        SubjectId = state == PrincipalAuthenticationState.Anonymous ? null : "user-1",
        CurrentTenantId = tenantId,
        TenantMemberships = tenantId is null ? null : [new TenantMembership { TenantId = tenantId }]
    };

    public static OperationContext Operation(string collectionId = "items", string? tenantId = null) => new()
    {
        Operation = BaseOperationKind.RealtimeSubscribe,
        CollectionId = collectionId,
        TenantId = tenantId,
        Now = DateTimeOffset.UtcNow
    };

    public static BaseRecordMutationEvent Event(
        string collectionId = "items",
        string recordId = "one",
        BaseOperationKind operation = BaseOperationKind.Create,
        string? tenantId = null,
        string type = "record.created") => new()
        {
            EventId = Guid.NewGuid().ToString("N"),
            Type = type,
            SchemaVersion = BaseEventSchemaVersions.V1,
            TenantId = tenantId,
            Visibility = VisibilityLevel.Public,
            Resource = new EventResource
            {
                Kind = EventResourceKind.Record,
                CollectionId = collectionId,
                RecordId = new RecordId(recordId)
            },
            Operation = operation,
            After = new RecordSnapshot
            {
                CollectionId = collectionId,
                Id = new RecordId(recordId),
                Payload = Payload(("title", "hello")),
                Metadata = new RecordMetadata(),
                IncludedFields = ["title"],
                Redacted = false
            }
        };

    public static BaseMutationJournalEntry JournalEntry(
        long position,
        string recordId,
        string title,
        string? tenantId = null) => new()
        {
            Position = new BaseMutationJournalPosition(position),
            EventId = $"event-{position}",
            Type = BaseEventTypes.RecordCreated,
            SchemaVersion = BaseEventSchemaVersions.V1,
            OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(position),
            TenantId = tenantId,
            Operation = BaseOperationKind.Create,
            Visibility = VisibilityLevel.Public,
            CollectionId = "items",
            RecordId = new RecordId(recordId),
            After = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId(recordId),
                Payload = Payload(("title", title), ("secret", "hidden")),
                Metadata = new RecordMetadata()
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
