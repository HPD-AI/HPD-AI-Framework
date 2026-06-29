using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HPD.Base;
using HPD.Base.Descriptors;
using HPD.Base.Events;
using HPD.Base.Health;
using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Capabilities;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Health;
using HPD.Base.Runtime.Operations;
using HPD.Base.Runtime.Results;
using HPD.Base.Runtime.Schema;
using HPD.Base.Runtime.Serialization;
using HPD.Base.Runtime.Stores;
using HPD.Base.Schema;
using HPD.Base.Serialization;
using HPD.Base.Stores;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<IBaseDescriptorContributor, SmokeDescriptorContributor>();
services.AddSingleton<IPolicyEvaluator, SmokePolicyEvaluator>();
services.AddSingleton<IBaseHealthContributor, SmokeHealthContributor>();
services.AddSingleton<IBaseDiagnosticContributor, SmokeDiagnosticContributor>();
services.AddSingleton<IBaseJsonTypeInfoContributor, SmokeJsonTypeInfoContributor>();
services.AddHPDBaseRuntime(options => options.ManifestVersion = "aot-smoke");

using var provider = services.BuildServiceProvider();
var store = new SmokeRecordStore();
provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
{
    StoreId = store.Capabilities.StoreId,
    Store = store,
    CollectionIds = [SmokeDescriptorContributor.CollectionId]
});

await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

var runtime = provider.GetRequiredService<IHPDBaseRuntime>();
var validation = await runtime.ValidateAsync();
Require(validation.Succeeded, "Runtime validation failed.");

var principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Anonymous };
var operation = new OperationContext
{
    Operation = BaseOperationKind.SchemaRead,
    CollectionId = SmokeDescriptorContributor.CollectionId,
    Now = DateTimeOffset.UnixEpoch
};

var manifest = await runtime.Descriptors.GetExpandedManifestAsync(new BaseManifestExpansionRequest
{
    Principal = principal,
    Operation = operation,
    View = VisibilityLevel.Public,
    Expand = ["schema", "capabilities", "health", "diagnostics", "collections"]
});
Require(manifest.IsSuccess() && manifest.Value?.Schema is not null, "Expanded manifest did not include schema.");

var schema = await runtime.Schema.GetSchemaAsync(principal, operation, VisibilityLevel.Public);
Require(schema.IsSuccess() && schema.Value?.Collections?.Length == 1, "Schema read failed.");

var capabilities = await runtime.Capabilities.GetCapabilitiesAsync(principal, operation, VisibilityLevel.Public);
Require(capabilities.IsSuccess(), "Capability read failed.");
Require(runtime.Capabilities.SupportsFeature("records.crud", SmokeDescriptorContributor.CollectionId), "Feature support lookup failed.");

var records = runtime.Records;
var listBefore = await records.ListAsync(SmokeDescriptorContributor.CollectionId, new RecordQuery(), principal, Operation(BaseOperationKind.List), CancellationToken.None);
Require(listBefore.IsSuccess() && listBefore.Value?.Items.Length == 0, "Initial list failed.");

var create = await records.CreateAsync(SmokeDescriptorContributor.CollectionId, CreateRequest("hello"), principal, Operation(BaseOperationKind.Create), CancellationToken.None);
Require(create.Status == OperationStatus.Created && create.Events?.Length == 1, "Create failed.");
var id = create.Value!.Id;

var get = await records.GetAsync(SmokeDescriptorContributor.CollectionId, id, principal, Operation(BaseOperationKind.Get), CancellationToken.None);
Require(get.IsSuccess() && get.Value is not null, "Get failed.");

var patch = await records.PatchAsync(SmokeDescriptorContributor.CollectionId, id, PatchRequest("patched"), principal, Operation(BaseOperationKind.Patch), CancellationToken.None);
Require(patch.Status == OperationStatus.Updated && patch.Events?.Length == 1, "Patch failed.");

var replace = await records.ReplaceAsync(SmokeDescriptorContributor.CollectionId, id, ReplaceRequest("replacement"), principal, Operation(BaseOperationKind.Replace), CancellationToken.None);
Require(replace.Status == OperationStatus.Updated && replace.Events?.Length == 1, "Replace failed.");

var listAfter = await records.ListAsync(SmokeDescriptorContributor.CollectionId, new RecordQuery(), principal, Operation(BaseOperationKind.List), CancellationToken.None);
Require(listAfter.IsSuccess() && listAfter.Value?.Items.Length == 1, "List after mutation failed.");

var delete = await records.DeleteAsync(SmokeDescriptorContributor.CollectionId, id, new RecordDeleteRequest { ReturnPrevious = true }, principal, Operation(BaseOperationKind.Delete), CancellationToken.None);
Require(delete.Status == OperationStatus.Deleted && delete.Events?.Length == 1 && delete.Value?.Previous is not null, "Delete failed.");

var health = await runtime.Health.GetHealthAsync(principal, operation, VisibilityLevel.Public);
Require(health.IsSuccess() && health.Value?.Length > 0, "Health read failed.");

var diagnostics = await runtime.Diagnostics.GetDiagnosticsAsync(principal, operation, VisibilityLevel.Public);
Require(diagnostics.IsSuccess() && diagnostics.Value?.Length > 0, "Diagnostics read failed.");

var json = provider.GetRequiredService<IBaseJsonOptionsProvider>().Options;
Require(json.IsReadOnly, "Runtime JSON options must be frozen.");
Require(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection fallback must be disabled.");

var manifestTypeInfo = (JsonTypeInfo<ExpandedBaseManifest>)json.GetTypeInfo(typeof(ExpandedBaseManifest));
var serialized = JsonSerializer.Serialize(manifest.Value!, manifestTypeInfo);
Require(!string.IsNullOrWhiteSpace(serialized), "Runtime JSON serialization failed.");

var smokeTypeInfo = (JsonTypeInfo<SmokePayload>)json.GetTypeInfo(typeof(SmokePayload));
var smokeSerialized = JsonSerializer.Serialize(new SmokePayload { Title = "smoke-json" }, smokeTypeInfo);
Require(smokeSerialized.Contains("smoke-json", StringComparison.Ordinal), "App JSON contributor composition failed.");

static OperationContext Operation(BaseOperationKind kind) => new()
{
    Operation = kind,
    CollectionId = SmokeDescriptorContributor.CollectionId,
    Now = DateTimeOffset.UnixEpoch
};

static RecordCreateRequest CreateRequest(string title) => new()
{
    Payload = JsonPayload(title)
};

static RecordPatchRequest PatchRequest(string title) => new()
{
    Patch = FieldMapPayload(title)
};

static RecordReplaceRequest ReplaceRequest(string title) => new()
{
    Payload = JsonPayload(title)
};

static RecordPayload JsonPayload(string title)
{
    using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
    return new RecordPayload
    {
        Kind = RecordPayloadKind.Json,
        Json = document.RootElement.Clone()
    };
}

static RecordPayload FieldMapPayload(string title)
{
    using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
    return new RecordPayload
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = new Dictionary<string, JsonElement>
        {
            ["title"] = document.RootElement.GetProperty("title").Clone()
        }
    };
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class SmokeDescriptorContributor : IBaseDescriptorContributor
{
    public const string CollectionId = "items";

    public string Id => "smoke";

    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        builder.AddCollection(new CollectionDefinition
        {
            Id = CollectionId,
            Name = CollectionId,
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
            },
            Fields =
            [
                new FieldDefinition { Id = "title", Name = "title", Type = BaseFieldTypes.String }
            ]
        });

        builder.AddCapabilities(new CapabilityDescriptor
        {
            DescriptorVersion = "aot-smoke",
            RuntimeId = "hpd.base.runtime",
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = "records",
                    FamilyVersion = "1.0",
                    Status = CapabilityStatus.Available,
                    Features =
                    [
                        new CapabilityFeatureDescriptor
                        {
                            FeatureId = "records.crud",
                            Version = "1.0",
                            Status = CapabilityStatus.Available,
                            SupportLevel = SupportLevel.Required,
                            Scope = CapabilityScope.Collection,
                            AppliesTo = [CollectionId]
                        }
                    ]
                }
            ]
        });

        builder.AddHealth(new HealthDescriptor
        {
            Id = "runtime",
            Scope = HealthScope.Runtime,
            Status = HealthStatus.Healthy,
            CheckedAt = DateTimeOffset.UnixEpoch,
            Summary = "Runtime healthy.",
            PublicSafe = true,
            Visibility = VisibilityLevel.Public
        });

        builder.AddDiagnostic(new DiagnosticDescriptor
        {
            Id = "runtime.diagnostic",
            Code = "runtime.ready",
            Severity = DiagnosticSeverity.Info,
            Message = "Runtime ready.",
            PublicMessage = "Runtime ready.",
            Category = DiagnosticCategory.Configuration,
            Visibility = VisibilityLevel.Public,
            EmittedAt = DateTimeOffset.UnixEpoch
        });
    }
}

internal sealed class SmokePolicyEvaluator : IPolicyEvaluator
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

internal sealed class SmokeRecordStore : IRecordStore
{
    private readonly Dictionary<string, RecordEnvelope> _records = new(StringComparer.Ordinal);
    private int _nextId;

    public StoreCapabilityDescriptor Capabilities { get; } = new()
    {
        StoreId = "smoke",
        StoreKind = BaseStoreKinds.Custom,
        StoreVersion = "aot",
        Crud = new CrudCapability
        {
            List = true,
            Get = true,
            Create = true,
            Patch = true,
            Replace = true,
            Delete = true,
            IdAuthority = IdAuthority.Store
        },
        Query = new QueryCapability
        {
            Filter = new FilterCapability { Supported = true, BooleanComposition = true },
            Sort = new SortCapability { Supported = true },
            Pagination = new PaginationCapability { Page = true, Offset = true, Cursor = true, MaxLimit = 100 },
            Count = new CountCapability { SupportedModes = [QueryCountMode.None, QueryCountMode.IfAvailable] },
            Select = new SelectCapability { PayloadFields = true },
            Include = new QueryIncludeCapability { Supported = true }
        }
    };

    public ValueTask<OperationResult<RecordPage>> ListAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = query;
        _ = context;
        return ValueTask.FromResult(new OperationResult<RecordPage>
        {
            Status = OperationStatus.Ok,
            Value = new RecordPage
            {
                Items = _records.Values.ToArray(),
                Page = new PageInfo { Limit = _records.Count, HasMore = false }
            }
        });
    }

    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = collection;
        _ = context;
        return ValueTask.FromResult(_records.TryGetValue(id.Value, out var record)
            ? new OperationResult<RecordEnvelope> { Status = OperationStatus.Ok, Value = record }
            : new OperationResult<RecordEnvelope>
            {
                Status = OperationStatus.NotFound,
                Error = new BaseError { Code = "notFound", Message = "Not found.", Category = ErrorCategory.NotFound }
            });
    }

    public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
        CollectionDefinition collection,
        RecordCreateRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = Envelope(collection.Id, new RecordId($"rec_{++_nextId}"), request.Payload);
        _records[record.Id.Value] = record;
        return ValueTask.FromResult(new OperationResult<RecordEnvelope> { Status = OperationStatus.Created, Value = record });
    }

    public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = Envelope(collection.Id, id, request.Patch);
        _records[id.Value] = record;
        return ValueTask.FromResult(new OperationResult<RecordEnvelope> { Status = OperationStatus.Updated, Value = record });
    }

    public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = Envelope(collection.Id, id, request.Payload);
        _records[id.Value] = record;
        return ValueTask.FromResult(new OperationResult<RecordEnvelope> { Status = OperationStatus.Updated, Value = record });
    }

    public ValueTask<OperationResult<DeleteResult>> DeleteAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordDeleteRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = collection;
        _ = context;
        _records.TryGetValue(id.Value, out var previous);
        var deleted = _records.Remove(id.Value);
        return ValueTask.FromResult(new OperationResult<DeleteResult>
        {
            Status = OperationStatus.Deleted,
            Value = new DeleteResult { Id = id, Deleted = deleted, Previous = request.ReturnPrevious ? previous : null }
        });
    }

    private static RecordEnvelope Envelope(string collectionId, RecordId id, RecordPayload payload) => new()
    {
        CollectionId = collectionId,
        Id = id,
        Payload = payload,
        Metadata = new RecordMetadata()
    };
}

internal sealed class SmokeHealthContributor : IBaseHealthContributor
{
    public string Id => "smoke.health";

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new[]
        {
            new HealthDescriptor
            {
                Id = "smoke.health",
                Scope = HealthScope.Runtime,
                Status = HealthStatus.Healthy,
                CheckedAt = DateTimeOffset.UnixEpoch,
                Summary = "Contributor healthy.",
                PublicSafe = true,
                Visibility = VisibilityLevel.Public
            }
        });
    }
}

internal sealed class SmokeDiagnosticContributor : IBaseDiagnosticContributor
{
    public string Id => "smoke.diagnostic";

    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new[]
        {
            new DiagnosticDescriptor
            {
                Id = "smoke.diagnostic",
                Code = "smoke.ready",
                Severity = DiagnosticSeverity.Info,
                Message = "Smoke ready.",
                PublicMessage = "Smoke ready.",
                Category = DiagnosticCategory.Configuration,
                Visibility = VisibilityLevel.Public,
                EmittedAt = DateTimeOffset.UnixEpoch
            }
        });
    }
}

internal sealed record SmokePayload
{
    public required string Title { get; init; }
}

[JsonSerializable(typeof(SmokePayload))]
internal sealed partial class SmokeJsonSerializerContext : JsonSerializerContext;

internal sealed class SmokeJsonTypeInfoContributor : IBaseJsonTypeInfoContributor
{
    public string Id => "smoke.json";

    public string Version => "1.0";

    public void AddTo(IBaseJsonTypeInfoRegistry registry)
    {
        registry.AddResolver(Id, SmokeJsonSerializerContext.Default);
    }
}
