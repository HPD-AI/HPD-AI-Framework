using System.Text.Json;
using HPD.Base.Records;
using HPD.Base.Runtime.Policy.Admin;
using HPD.Base.Runtime.Stores;
using HPD.Base.Stores;

namespace HPD.Base.Auth.HPDAuth.Tests.Policy;

public sealed class HPDAuthBasePolicyExplainIntegrationTests
{
    [Fact]
    public async Task ExplainRedactsHPDAuthTenantFilterAndSummarizesReadMask()
    {
        using var provider = BuildProvider(options =>
        {
            options.RequireHPDAuthServices = false;
            options.AllowAdminBypass = false;
            options.CollectionRules =
            [
                new HPDAuthBaseCollectionRule
                {
                    CollectionId = "items",
                    ReadRoles = ["Reader"],
                    TenantFieldPath = "tenantId",
                    ReadExcludeFields = ["secret"]
                }
            ];
        });
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Query,
                CollectionId = "items",
                Options = new BasePolicyExplainOptions { IncludeConstraintAst = true }
            },
            Principal(["Reader"], tenantId: "tenant-secret"),
            OperationModeAdmin());
        var json = JsonSerializer.Serialize(result.Value, HPD.Base.Runtime.Serialization.HPDBaseRuntimeJsonSerializerContext.Default.BasePolicyExplainResponse);

        result.Status.Should().Be(OperationStatus.Ok);
        result.Value!.Outcome.Should().Be(BasePolicyExplainOutcome.AllowedWithConstraints);
        result.Value.Constraints!.RecordFilter!.Summary.Should().Contain("tenantId");
        result.Value.Constraints.ReadMask!.Mode.Should().Be(FieldMaskMode.Exclude);
        result.Value.Constraints.ReadMask.Exclude.Should().Contain("secret");
        json.Should().NotContain("tenant-secret");
        json.Should().Contain("redacted:string");
    }

    [Fact]
    public async Task ExplainSummarizesHPDAuthWriteMaskWithoutLeakingPayloadValues()
    {
        using var provider = BuildProvider(options =>
        {
            options.RequireHPDAuthServices = false;
            options.AllowAdminBypass = false;
            options.CollectionRules =
            [
                new HPDAuthBaseCollectionRule
                {
                    CollectionId = "items",
                    WriteRoles = ["Editor"],
                    WriteIncludeFields = ["title", "body"]
                }
            ];
        });
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest
            {
                Operation = BasePolicyExplainOperation.Create,
                CollectionId = "items",
                Create = new RecordCreateRequest { Payload = Payload(("title", "payload-secret")) },
                Options = new BasePolicyExplainOptions { IncludeRedactedPayloadShape = true }
            },
            Principal(["Editor"]),
            OperationModeAdmin());
        var json = JsonSerializer.Serialize(result.Value, HPD.Base.Runtime.Serialization.HPDBaseRuntimeJsonSerializerContext.Default.BasePolicyExplainResponse);

        result.Status.Should().Be(OperationStatus.Ok);
        result.Value!.Constraints!.WriteMask!.Mode.Should().Be(FieldMaskMode.IncludeOnly);
        result.Value.Constraints.WriteMask.Include.Should().Contain(["title", "body"]);
        result.Value.Redaction!.OmittedPayloadFields.Should().Contain("title");
        json.Should().NotContain("payload-secret");
    }

    [Fact]
    public async Task ExplainFailsClosedWhenHPDAuthServicesAreRequiredButMissing()
    {
        using var provider = BuildProvider(options =>
        {
            options.RequireHPDAuthServices = true;
            options.AllowAdminBypass = false;
            options.CollectionRules =
            [
                new HPDAuthBaseCollectionRule
                {
                    CollectionId = "items",
                    ReadRoles = ["Reader"],
                    TenantFieldPath = "tenantId"
                }
            ];
        }, detectedHost: false);
        var service = provider.GetRequiredService<IBasePolicyExplainService>();

        var result = await service.ExplainAsync(
            new BasePolicyExplainRequest { Operation = BasePolicyExplainOperation.Query, CollectionId = "items" },
            Principal(["Reader"], tenantId: "tenant-secret"),
            OperationModeAdmin());

        result.Status.Should().Be(OperationStatus.Ok);
        result.Value!.Outcome.Should().Be(BasePolicyExplainOutcome.Denied);
        result.Value.Decision!.ReasonCode.Should().Be("hpd.auth.base.missingAuthServices");
    }

    private static ServiceProvider BuildProvider(Action<HPDBaseHPDAuthOptions> configure, bool detectedHost = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor());
        if (detectedHost)
        {
            services.AddSingleton<IHPDAuthBaseHostIntegrationStatus>(new DetectedHostIntegrationStatus());
        }

        services.AddHPDBaseHPDAuth(configure);
        services.AddHPDBaseRuntime();
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync().AsTask().GetAwaiter().GetResult();
        var store = new ExplainStore();
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = store.Capabilities.StoreId,
            Store = store,
            CollectionIds = ["items"]
        });

        return provider;
    }

    private static PrincipalContext Principal(string[] roles, string? tenantId = null) => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Admin,
        SubjectKind = AccessSubjectKind.Admin,
        SubjectId = "admin-1",
        CurrentTenantId = tenantId,
        Roles = roles,
        Subjects = [new AccessSubject { Kind = AccessSubjectKind.Admin, Id = "admin-1" }]
    };

    private static OperationContext OperationModeAdmin() => new()
    {
        Operation = BaseOperationKind.AdminInspect,
        CollectionId = "items",
        Mode = OperationMode.Admin,
        Now = DateTimeOffset.UnixEpoch
    };

    private static RecordPayload Payload(params (string Name, string Value)[] fields)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            using var document = JsonDocument.Parse($"\"{field.Value}\"");
            values[field.Name] = document.RootElement.Clone();
        }

        return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = values };
    }

    private sealed class CollectionContributor : IBaseDescriptorContributor
    {
        public string Id => "collections";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(new CollectionDefinition
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
            });
        }
    }

    private sealed class DetectedHostIntegrationStatus : IHPDAuthBaseHostIntegrationStatus
    {
        public bool HPDAuthServicesDetected => true;
        public string? Source => "test";
        public string[] MissingRequiredServiceNames => [];
    }

    private sealed class ExplainStore : IRecordStore
    {
        public StoreCapabilityDescriptor Capabilities { get; } = new()
        {
            StoreId = "explain",
            StoreKind = BaseStoreKinds.Custom,
            StoreVersion = "test",
            Crud = new CrudCapability
            {
                List = true,
                Get = true,
                Create = true,
                Patch = true,
                Replace = true,
                Delete = true
            },
            Query = new QueryCapability
            {
                Filter = new FilterCapability
                {
                    Supported = true,
                    BooleanComposition = true,
                    Not = true,
                    NullChecks = true,
                    MissingFieldChecks = true
                },
                Sort = new SortCapability { Supported = true, NullOrdering = true },
                Pagination = new PaginationCapability
                {
                    Page = true,
                    Offset = true,
                    Cursor = true,
                    MaxLimit = 1_000
                },
                Count = new CountCapability
                {
                    SupportedModes =
                    [
                        QueryCountMode.None,
                        QueryCountMode.IfAvailable,
                        QueryCountMode.Exact,
                        QueryCountMode.Estimated,
                        QueryCountMode.Limited
                    ]
                },
                Select = new SelectCapability { PayloadFields = true },
                Include = new QueryIncludeCapability
                {
                    Supported = true,
                    IncludeFilters = true,
                    IncludeSort = true,
                    IncludeLimit = true
                }
            }
        };

        public ValueTask<OperationResult<RecordPage>> ListAsync(CollectionDefinition collection, RecordQuery query, OperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(CollectionDefinition collection, RecordId id, OperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(CollectionDefinition collection, RecordCreateRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(CollectionDefinition collection, RecordId id, RecordPatchRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(CollectionDefinition collection, RecordId id, RecordReplaceRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<OperationResult<DeleteResult>> DeleteAsync(CollectionDefinition collection, RecordId id, RecordDeleteRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
