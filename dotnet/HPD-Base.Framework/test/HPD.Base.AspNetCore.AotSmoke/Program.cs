using System.Text.Json;
using HPD.Base;
using HPD.Base.AspNetCore;
using HPD.Base.AspNetCore.DependencyInjection;
using HPD.Base.InMemory.DependencyInjection;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Stores;
using HPD.Base.Schema;
using HPD.Base.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var verifyProjection = args.Contains("--verify", StringComparer.Ordinal);
var builder = WebApplication.CreateSlimBuilder(
    args.Where(argument => !string.Equals(argument, "--verify", StringComparison.Ordinal)).ToArray());

builder.Services.AddSingleton<IPolicyEvaluator, SmokePolicyEvaluator>();
builder.Services.AddHPDBaseRuntime()
    .AddHPDBaseAspNetCore()
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
                    Delete = true,
                    Upsert = true
                }
            }
        ];
    });

var app = builder.Build();
app.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(app.Services);
await app.Services.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
app.MapHPDBaseApi();

app.MapGet("/", () => "HPD.Base.AspNetCore AOT smoke");

if (verifyProjection)
{
    await VerifyProjectionAsync(app);
}
else
{
    await app.RunAsync();
}

static async Task VerifyProjectionAsync(WebApplication app)
{
    app.Urls.Add("http://127.0.0.1:0");
    await app.StartAsync();
    try
    {
        var addresses = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses;
        var address = addresses?.SingleOrDefault()
            ?? throw new InvalidOperationException("The AOT smoke server did not publish one loopback address.");
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        var upsertId = new RecordId("aot-upsert");
        var upsertResponse = await client.PutAsync(
            $"/base/collections/items/records/{upsertId.Value}:upsert",
            JsonContent.Create(
                new RecordUpsertRequest
                {
                    Id = upsertId,
                    CreatePayload = Payload("created"),
                    UpdatePayload = Patch("updated"),
                    UpdateMode = RecordUpsertUpdateMode.Patch,
                    Condition = RecordUpsertExistenceCondition.Any
                },
                HPDBaseJsonSerializerContext.Default.RecordUpsertRequest));
        Require(upsertResponse.StatusCode == System.Net.HttpStatusCode.Created, "Standalone upsert projection failed.");
        var upsert = await upsertResponse.Content.ReadFromJsonAsync(
            HPDBaseJsonSerializerContext.Default.RecordUpsertResult);
        Require(
            upsert?.Outcome == RecordUpsertOutcome.Created
            && upsert.Record.Id == upsertId,
            "Standalone upsert projection returned an invalid response.");

        var batchResponse = await client.PostAsync(
            "/base/records/batch",
            JsonContent.Create(
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
                                RequestedId = new RecordId("aot-batch"),
                                Payload = Payload("before")
                            }
                        },
                        new BaseRecordBatchItem
                        {
                            ItemId = "patch",
                            CollectionId = "items",
                            Kind = BaseRecordMutationKind.Patch,
                            RecordId = new RecordId("aot-batch"),
                            Patch = new RecordPatchRequest { Patch = Patch("after") }
                        }
                    ]
                },
                HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest));
        Require(batchResponse.StatusCode == System.Net.HttpStatusCode.OK, "Atomic batch projection failed.");
        var batch = await batchResponse.Content.ReadFromJsonAsync(
            HPDBaseJsonSerializerContext.Default.BaseRecordBatchResult);
        Require(
            batch?.Outcome == BaseRecordBatchOutcome.Committed
            && batch.Items.Length == 2
            && batch.Items.All(item => item.Disposition == BaseRecordBatchItemDisposition.Committed)
            && batch.Items[1].Record?.Payload.Fields?["title"].GetString() == "after",
            "Atomic batch projection returned an invalid response.");
        Require(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection fallback must be disabled.");
    }
    finally
    {
        await app.StopAsync();
    }
}

static RecordPayload Payload(string title)
{
    using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
    return new RecordPayload
    {
        Kind = RecordPayloadKind.Json,
        Json = document.RootElement.Clone()
    };
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
