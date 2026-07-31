using System.Text.Json;
using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var verifyProjection = args.Contains("--verify", StringComparer.Ordinal);
var builder = WebApplication.CreateSlimBuilder(
    args.Where(argument => !string.Equals(argument, "--verify", StringComparison.Ordinal)).ToArray());

builder.Services.AddSingleton<IPolicyEvaluator, SmokePolicyEvaluator>();
var items = BaseCollection<JsonElement>.Create(
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
    },
    HPDBaseJsonSerializerContext.Default.JsonElement,
    static _ => { });
builder.Services.AddHPDBase(hpd => hpd
    .AddAspNetCore()
    .AddCollection(items));

var app = builder.Build();
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
        if (upsertResponse.StatusCode != System.Net.HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Standalone upsert projection failed: {await upsertResponse.Content.ReadAsStringAsync()}");
        }
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
