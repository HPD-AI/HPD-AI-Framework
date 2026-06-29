using HPD.Base;
using HPD.Base.InMemory;
using HPD.Base.InMemory.DependencyInjection;
using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Operations;
using HPD.Base.Runtime.Stores;
using HPD.Base.Schema;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

var services = new ServiceCollection();
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
                    Delete = true
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

var store = provider.GetRequiredService<InMemoryRecordStore>();
var streamCreate = await store.CreateAsync(Collection(), new RecordCreateRequest { Payload = Payload("stream") }, Operation(BaseOperationKind.Create));
Require(streamCreate.Status == OperationStatus.Created, "Direct create failed.");

var streamed = 0;
await foreach (var _ in store.StreamAsync(Collection(), new RecordQuery { Count = QueryCountMode.None }, Operation(BaseOperationKind.List)))
{
    streamed++;
}

Require(streamed == 1, "Stream failed.");
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
