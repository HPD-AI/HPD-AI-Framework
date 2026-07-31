using HPD.Base;
using HPD.Base.InMemory;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IPolicyEvaluator, SmokePolicyEvaluator>();
services.AddHPDBaseRuntime()
    .AddHPDBaseInMemoryStore(options =>
    {
        options.StoreId = "smoke.inmemory";
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
                    Delete = true,
                    Upsert = true
                },
                Fields =
                [
                    new FieldDefinition { Id = "title", Name = "title", Type = BaseFieldTypes.String }
                ]
            }
        ];
    });

using var provider = services.BuildServiceProvider();
provider.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(provider);
await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

var runtime = provider.GetRequiredService<IBaseRecordRuntime>();
var principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Anonymous };

var create = await runtime.CreateAsync("items", new RecordCreateRequest { Payload = Payload("hello") }, principal, Operation(BaseOperationKind.Create));
Require(create.Status == OperationStatus.Created, "Create failed.");

var patch = await runtime.PatchAsync(
    "items",
    create.Value!.Id,
    new RecordPatchRequest { Patch = Patch("patched"), ExpectedRevision = create.Value.Metadata.Revision },
    principal,
    Operation(BaseOperationKind.Patch));
Require(patch.Status == OperationStatus.Updated, "Patch failed.");

var delete = await runtime.DeleteAsync(
    "items",
    create.Value.Id,
    new RecordDeleteRequest { ExpectedRevision = patch.Value!.Metadata.Revision, ReturnPrevious = true },
    principal,
    Operation(BaseOperationKind.Delete));
Require(delete.Status == OperationStatus.Deleted && delete.Value?.Previous is not null, "Expected-revision delete failed.");

var batch = await runtime.BatchAsync(
    new BaseRecordBatchRequest
    {
        Mode = BaseRecordBatchExecutionMode.Atomic,
        Operations =
        [
            new BaseRecordBatchItem
            {
                ItemId = "create",
                CollectionId = "items",
                Kind = BaseRecordMutationKind.Create,
                Create = new RecordCreateRequest
                {
                    RequestedId = new RecordId("batch"),
                    Payload = Payload("batch-before")
                }
            },
            new BaseRecordBatchItem
            {
                ItemId = "patch",
                CollectionId = "items",
                Kind = BaseRecordMutationKind.Patch,
                RecordId = new RecordId("batch"),
                Patch = new RecordPatchRequest { Patch = Patch("batch-after") }
            }
        ]
    },
    principal,
    Operation(BaseOperationKind.Batch));
Require(
    batch.Status == OperationStatus.Ok
    && batch.Value?.Outcome == BaseRecordBatchOutcome.Committed
    && batch.Value.Items[1].Record?.Payload.Fields?["title"].GetString() == "batch-after",
    "Atomic read-your-writes batch failed.");

var upsert = await runtime.UpsertAsync(
    "items",
    new RecordUpsertRequest
    {
        Id = new RecordId("upsert"),
        CreatePayload = Payload("upsert-created"),
        UpdatePayload = Patch("upsert-updated"),
        UpdateMode = RecordUpsertUpdateMode.Patch,
        Condition = RecordUpsertExistenceCondition.Any
    },
    principal,
    Operation(BaseOperationKind.Upsert));
Require(
    upsert.Status == OperationStatus.Created
    && upsert.Value?.Outcome == RecordUpsertOutcome.Created,
    "Atomic upsert failed.");

var store = provider.GetRequiredService<InMemoryRecordStore>();
var streamCreate = await runtime.CreateAsync(
    "items",
    new RecordCreateRequest { Payload = Payload("stream") },
    principal,
    Operation(BaseOperationKind.Create));
Require(streamCreate.Status == OperationStatus.Created, "Canonical create failed.");

var streamed = 0;
var stream = await store.OpenStreamAsync(Collection(), new RecordQuery { Count = QueryCountMode.None }, Operation(BaseOperationKind.List));
Require(stream.Status == OperationStatus.Ok && stream.Value is not null, "Stream open failed.");
var openedStream = stream.Value ?? throw new InvalidOperationException("Stream open returned no value.");
await foreach (var _ in openedStream.Items)
{
    streamed++;
}

Require(streamed == 3, "Stream failed.");
Require(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection fallback must be disabled.");

static CollectionDefinition Collection() => new()
{
    Id = "items",
    Name = "items",
    Kind = BaseCollectionKinds.Document,
    SchemaMode = SchemaMode.Loose,
    UnknownFields = UnknownFieldPolicy.Preserve
};

static OperationContext Operation(BaseOperationKind kind) => new()
{
    Operation = kind,
    CollectionId = "items",
    Now = DateTimeOffset.UnixEpoch
};

static RecordPayload Payload(string title)
{
    using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
    return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
}

static RecordPayload Patch(string title)
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
