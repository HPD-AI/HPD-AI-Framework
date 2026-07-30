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
services.AddLogging();
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

var upsert = await records.UpsertAsync(
    SmokeDescriptorContributor.CollectionId,
    new RecordUpsertRequest
    {
        Id = new RecordId("aot_upsert"),
        CreatePayload = FieldMapPayload("upsert-created"),
        UpdatePayload = FieldMapPayload("upsert-updated"),
        UpdateMode = RecordUpsertUpdateMode.Patch,
        Condition = RecordUpsertExistenceCondition.Any
    },
    principal,
    Operation(BaseOperationKind.Upsert),
    CancellationToken.None);
Require(
    upsert.IsSuccess()
    && upsert.Value?.Outcome == RecordUpsertOutcome.Created
    && upsert.Events?.Length == 1,
    "Upsert failed.");

var batch = await records.BatchAsync(
    new BaseRecordBatchRequest
    {
        Mode = BaseRecordBatchExecutionMode.Atomic,
        Operations =
        [
            new BaseRecordBatchItem
            {
                ItemId = "first",
                CollectionId = SmokeDescriptorContributor.CollectionId,
                Kind = BaseRecordMutationKind.Create,
                Create = new RecordCreateRequest
                {
                    RequestedId = new RecordId("aot_batch_1"),
                    Payload = FieldMapPayload("batch-one")
                }
            },
            new BaseRecordBatchItem
            {
                ItemId = "second",
                CollectionId = SmokeDescriptorContributor.CollectionId,
                Kind = BaseRecordMutationKind.Create,
                Create = new RecordCreateRequest
                {
                    RequestedId = new RecordId("aot_batch_2"),
                    Payload = FieldMapPayload("batch-two")
                }
            }
        ]
    },
    principal,
    Operation(BaseOperationKind.Batch),
    CancellationToken.None);
Require(
    batch.IsSuccess()
    && batch.Value?.Outcome == BaseRecordBatchOutcome.Committed
    && batch.Value.Items.Length == 2,
    "Atomic batch failed.");

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
                Upsert = true,
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

internal sealed class SmokeRecordStore : IAtomicRecordStore
{
    private readonly Dictionary<string, RecordEnvelope> _records = new(StringComparer.Ordinal);
    private int _nextId;

    public StoreCapabilityDescriptor Capabilities { get; } = new()
    {
        StoreId = "smoke",
        StoreKind = BaseStoreKinds.Custom,
        StoreVersion = "aot",
        Read = new RecordReadCapability
        {
            List = true,
            Get = true,
            MaxPageSize = 100
        },
        Mutation = new RecordMutationCapability
        {
            Create = true,
            Patch = true,
            Replace = true,
            Delete = true,
            IdAuthority = IdAuthority.Hybrid,
            TimestampAuthority = TimestampAuthority.Runtime,
            Consistency = ConsistencyModel.Strong
        },
        Query = new QueryCapability
        {
            Filter = new FilterCapability { Supported = true, BooleanComposition = true },
            Sort = new SortCapability { Supported = true },
            Pagination = new PaginationCapability { Page = true, Offset = true, Cursor = true, MaxLimit = 100 },
            Count = new CountCapability { SupportedModes = [QueryCountMode.None, QueryCountMode.IfAvailable] },
            Select = new SelectCapability { PayloadFields = true },
            Include = new QueryIncludeCapability { Supported = true }
        },
        Batch = new StoreBatchCapability
        {
            Modes = [BaseRecordBatchExecutionMode.Atomic],
            MaxOperations = 100,
            MaxCanonicalPayloadBytes = 1_048_576,
            MinimumAcquisitionTimeout = TimeSpan.FromMilliseconds(10),
            MinimumTransactionTimeout = TimeSpan.FromMilliseconds(10),
            MinimumCommitCompletionTimeout = TimeSpan.FromMilliseconds(10),
            TimeoutGranularity = TimeSpan.FromMilliseconds(10),
            Ordered = true,
            PartialResults = true,
            CrossCollectionAtomic = true,
            ReadYourWrites = true,
            Isolation = BaseTransactionIsolation.Serializable
        },
        Upsert = new StoreUpsertCapability
        {
            Atomic = true,
            UpdateModes = [RecordUpsertUpdateMode.Patch, RecordUpsertUpdateMode.Replace],
            ExistenceConditions = true
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

    public ValueTask<RecordMutationExecutionResult> ExecuteSingleAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(processor, cancellationToken);

    public ValueTask<RecordMutationExecutionResult> ExecuteAtomicAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(processor, cancellationToken);

    private async ValueTask<RecordMutationExecutionResult> ExecuteCoreAsync(
        IAtomicMutationProcessor processor,
        CancellationToken cancellationToken)
    {
        var staged = _records.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal);
        var session = new SmokeSession(this, staged);
        var processing = await processor.ProcessAsync(session, cancellationToken);
        session.Close();
        if (processing.Outcome == AtomicMutationProcessingOutcome.Failed)
        {
            return new RecordMutationExecutionResult(
                RecordMutationExecutionOutcome.RollbackConfirmed,
                processing,
                processing.Error);
        }

        _records.Clear();
        foreach (var pair in staged)
            _records.Add(pair.Key, pair.Value);
        return new RecordMutationExecutionResult(
            RecordMutationExecutionOutcome.Committed,
            processing);
    }

    private static RecordEnvelope Envelope(string collectionId, RecordId id, RecordPayload payload) => new()
    {
        CollectionId = collectionId,
        Id = id,
        Payload = payload,
        Metadata = new RecordMetadata()
    };

    private sealed class SmokeSession(
        SmokeRecordStore owner,
        Dictionary<string, RecordEnvelope> records) : IAtomicRecordSession
    {
        private bool _active = true;

        public void Close() => _active = false;

        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
            CollectionDefinition collection,
            RecordId id,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            return ValueTask.FromResult(records.TryGetValue(id.Value, out var record)
                ? new OperationResult<RecordEnvelope>
                {
                    Status = OperationStatus.Ok,
                    Value = record
                }
                : new OperationResult<RecordEnvelope>
                {
                    Status = OperationStatus.NotFound,
                    Error = Error("notFound", ErrorCategory.NotFound)
                });
        }

        public ValueTask<OperationResult<RecordMutationSessionResult>> CreateAsync(
            CollectionDefinition collection,
            RecordCreateRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            var id = request.RequestedId ?? new RecordId($"rec_{++owner._nextId}");
            if (records.ContainsKey(id.Value))
                return ValueTask.FromResult(Failure("conflict", ErrorCategory.Conflict));
            var after = Envelope(collection.Id, id, request.Payload);
            records[id.Value] = after;
            return ValueTask.FromResult(Result(
                OperationStatus.Created,
                collection,
                context,
                BaseCommittedRecordMutationKind.Create,
                null,
                after));
        }

        public ValueTask<OperationResult<RecordMutationSessionResult>> PatchAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordPatchRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            if (!records.TryGetValue(id.Value, out var before))
                return ValueTask.FromResult(Failure("notFound", ErrorCategory.NotFound));
            var after = Envelope(collection.Id, id, Merge(before.Payload, request.Patch));
            records[id.Value] = after;
            return ValueTask.FromResult(Result(
                OperationStatus.Updated,
                collection,
                context,
                BaseCommittedRecordMutationKind.Patch,
                before,
                after));
        }

        public ValueTask<OperationResult<RecordMutationSessionResult>> ReplaceAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordReplaceRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            if (!records.TryGetValue(id.Value, out var before))
                return ValueTask.FromResult(Failure("notFound", ErrorCategory.NotFound));
            var after = Envelope(collection.Id, id, request.Payload);
            records[id.Value] = after;
            return ValueTask.FromResult(Result(
                OperationStatus.Updated,
                collection,
                context,
                BaseCommittedRecordMutationKind.Replace,
                before,
                after));
        }

        public ValueTask<OperationResult<RecordMutationSessionResult>> DeleteAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordDeleteRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            if (!records.Remove(id.Value, out var before))
                return ValueTask.FromResult(Failure("notFound", ErrorCategory.NotFound));
            var delete = new DeleteResult
            {
                Id = id,
                Deleted = true,
                Previous = request.ReturnPrevious ? before : null
            };
            return ValueTask.FromResult(Result(
                OperationStatus.Deleted,
                collection,
                context,
                BaseCommittedRecordMutationKind.Delete,
                before,
                null,
                delete));
        }

        private static OperationResult<RecordMutationSessionResult> Result(
            OperationStatus status,
            CollectionDefinition collection,
            RecordMutationSessionContext context,
            BaseCommittedRecordMutationKind committed,
            RecordEnvelope? before,
            RecordEnvelope? after,
            DeleteResult? delete = null) => new()
        {
            Status = status,
            Value = new RecordMutationSessionResult
            {
                Record = after,
                Delete = delete,
                Mutation = new BaseRecordMutationFact
                {
                    ItemId = context.ItemId,
                    RequestedOperation = context.RequestedOperation,
                    CommittedOperation = committed,
                    UpsertOutcome = context.RequestedOperation == BaseRecordMutationKind.Upsert
                        ? committed == BaseCommittedRecordMutationKind.Create
                            ? RecordUpsertOutcome.Created
                            : RecordUpsertOutcome.Updated
                        : null,
                    Collection = collection,
                    Event = new EventReference
                    {
                        EventId = context.EventId,
                        Type = committed switch
                        {
                            BaseCommittedRecordMutationKind.Create => BaseEventTypes.RecordCreated,
                            BaseCommittedRecordMutationKind.Patch => BaseEventTypes.RecordPatched,
                            BaseCommittedRecordMutationKind.Replace => BaseEventTypes.RecordUpdated,
                            BaseCommittedRecordMutationKind.Delete => BaseEventTypes.RecordDeleted,
                            _ => throw new InvalidOperationException()
                        },
                        Guarantee = EventDeliveryGuarantee.BestEffort
                    },
                    Before = before,
                    After = after,
                    Delete = delete
                }
            }
        };

        private static OperationResult<RecordMutationSessionResult> Failure(
            string code,
            ErrorCategory category) => new()
        {
            Status = category == ErrorCategory.NotFound
                ? OperationStatus.NotFound
                : OperationStatus.Conflict,
            Error = Error(code, category)
        };

        private static BaseError Error(string code, ErrorCategory category) => new()
        {
            Code = code,
            Message = "Smoke mutation failed.",
            Category = category
        };

        private static RecordPayload Merge(RecordPayload before, RecordPayload patch)
        {
            if (before.Kind != RecordPayloadKind.FieldMap
                || patch.Kind != RecordPayloadKind.FieldMap)
            {
                return patch;
            }

            var fields = new Dictionary<string, JsonElement>(
                before.Fields ?? [],
                StringComparer.Ordinal);
            foreach (var pair in patch.Fields ?? [])
                fields[pair.Key] = pair.Value;
            return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
        }

        private void EnsureActive()
        {
            if (!_active)
                throw new InvalidOperationException("Session is closed.");
        }
    }
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
