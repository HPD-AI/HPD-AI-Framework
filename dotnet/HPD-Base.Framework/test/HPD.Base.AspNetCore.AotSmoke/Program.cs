using System.Text.Json;
using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var verifyProjection = args.Contains("--verify", StringComparer.Ordinal);
var verifySelection = args.Contains("--verify-selection", StringComparer.Ordinal);
var builder = WebApplication.CreateSlimBuilder(
    args.Where(argument => argument is not "--verify" and not "--verify-selection").ToArray());

builder.Services.AddSingleton<IPolicyEvaluator, SmokePolicyEvaluator>();
builder.Services.AddAuthorizationBuilder().AddPolicy("application", policy => policy.RequireAssertion(_ => true));
var items = SmokeRecord.Collection;
builder.Services.AddHPDBase(hpd =>
{
    hpd.AddRealtime()
    .AddAspNetCore()
    .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
    {
        Id = 1,
        Key = Enumerable.Repeat((byte)0x37, 32).ToArray(),
        IssueNotBefore = DateTimeOffset.UnixEpoch,
    })
    .AddCollection(items);
    if (verifySelection)
    {
        hpd.ConfigureSelectionMutations(new HPDBaseSelectionMutationOptions { HostMaxima = SelectionLimits(), MaximumReceiptIdentityBytes = 512, MaximumEvidenceTokenBytes = 512, MaximumRouteNameBytes = 96, MaximumRequestBodyBytes = 1_048_576 })
            .AddSelectionOperationProfile(new BaseSelectionOperationProfile
            {
                Id = "aot-patch", Version = 1, ApplicationId = "hpd.base.application", CollectionId = "items",
                RequiredGrantId = "items.selection", MutationKind = BaseSelectionMutationKind.MergePatch,
                Limits = SelectionLimits(),
                HttpProjection = new BaseSelectionHttpProjection { Audience = BaseSelectionEndpointAudience.Application, RouteName = "aot-patch", MaximumRequestBodyBytes = 1_048_576, GenerateL41Client = false },
            });
    }
});

var app = builder.Build();
app.UseWebSockets();
app.MapHPDBasePublicApi();
app.MapHPDBaseApplicationApi(new HPDBaseApplicationEndpointOptions
{
    AuthorizationPolicy = "application",
    MapClientGeneration = true,
    MapRealtime = true
});

app.MapGet("/", () => "HPD.Base.AspNetCore AOT smoke");

if (verifyProjection || verifySelection)
{
    await VerifyProjectionAsync(app, verifySelection);
}
else
{
    await app.RunAsync();
}

static async Task VerifyProjectionAsync(WebApplication app, bool verifySelection)
{
    app.Urls.Add("http://127.0.0.1:0");
    await app.StartAsync();
    try
    {
        Require(
            (await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess(),
            "BASE application initialization failed.");
        var addresses = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses;
        var address = addresses?.SingleOrDefault()
            ?? throw new InvalidOperationException("The AOT smoke server did not publish one loopback address.");
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        if (!verifySelection)
        {
            using HttpResponseMessage generationResponse = await client.GetAsync("/base/client-generation");
            Require(generationResponse.StatusCode == System.Net.HttpStatusCode.OK, $"Client generation snapshot failed: {await generationResponse.Content.ReadAsStringAsync()}");
            using JsonDocument generation = JsonDocument.Parse(await generationResponse.Content.ReadAsByteArrayAsync());
            Require(generation.RootElement.GetProperty("protocol").GetProperty("protocolMajor").GetInt32() == 2
                && generation.RootElement.GetProperty("application").GetProperty("audience").GetString() == "application"
                && generation.RootElement.GetProperty("digest").GetString()?.StartsWith("sha256:", StringComparison.Ordinal) == true,
                "Client generation snapshot was invalid.");
        }

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

        if (verifySelection)
        {
        using HttpResponseMessage selectionResponse = await client.PostAsJsonAsync(
            "/base/selection-mutations/aot-patch/execute",
            new BaseMergePatchSelectionHttpRequest
            {
                Query = new BaseSelectionMutationHttpQuery
                {
                    Sort = [new QuerySort { Field = "id", Direction = QuerySortDirection.Asc }], Take = 1,
                },
                Patch = new RecordPatchRequest { Patch = Patch("selected") },
                PreviousState = BasePreviousStateRequirement.None,
            },
            HPDBaseAspNetCoreJsonSerializerContext.Default.BaseMergePatchSelectionHttpRequest);
        Require(selectionResponse.IsSuccessStatusCode, $"Selection mutation AOT projection failed: {await selectionResponse.Content.ReadAsStringAsync()}");
        using HttpResponseMessage invalidSelection = await client.PostAsync(
            "/base/selection-mutations/aot-patch/execute",
            new StringContent("{\"query\":{\"sort\":[],\"take\":1},\"patch\":{\"patch\":{\"kind\":\"fieldMap\",\"fields\":{}}},\"previousState\":{\"revision\":{\"kind\":\"none\"},\"fields\":[]}}", System.Text.Encoding.UTF8, "application/json"));
        Require(invalidSelection.StatusCode == System.Net.HttpStatusCode.BadRequest, "Malformed selection request did not fail closed.");
        }

        var batchRequest = new BaseRecordBatchRequest
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
                };
        async Task<HttpResponseMessage> SendBatchAsync()
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "/base/records/batch")
            {
                Content = JsonContent.Create(batchRequest, HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest),
            };
            message.Headers.Add(BaseHttpHeaders.IdempotencyKey, "aot-request-1");
            return await client.SendAsync(message);
        }
        var batchResponse = await SendBatchAsync();
        var duplicateResponse = await SendBatchAsync();
        Require(batchResponse.StatusCode == System.Net.HttpStatusCode.OK, "Atomic batch projection failed.");
        Require(duplicateResponse.StatusCode == System.Net.HttpStatusCode.OK, "Atomic batch duplicate projection failed.");
        Require(
            batchResponse.Headers.GetValues(BaseHttpHeaders.RequestDisposition).Single() == "committed"
            && duplicateResponse.Headers.GetValues(BaseHttpHeaders.RequestDisposition).Single() == "duplicate",
            "Atomic batch request disposition headers were invalid.");
        var batch = await batchResponse.Content.ReadFromJsonAsync(
            HPDBaseJsonSerializerContext.Default.BaseRecordBatchResult);
        Require(
            batch?.Outcome == BaseRecordBatchOutcome.Committed
            && batch.Items.Length == 2
            && batch.Items.All(item => item.Disposition == BaseRecordBatchItemDisposition.Committed)
            && batch.Items[1].Record?.Payload.Fields?["title"].GetString() == "after",
            "Atomic batch projection returned an invalid response.");
        var duplicateBatch = await duplicateResponse.Content.ReadFromJsonAsync(
            HPDBaseJsonSerializerContext.Default.BaseRecordBatchResult);
        Require(
            duplicateBatch?.RequestDisposition == BaseMutationRequestDisposition.Duplicate,
            "Atomic batch duplicate body was invalid.");
        var firstPageResponse = await client.GetAsync(
            "/base/collections/items/records?sort=item.title&cursor=&limit=1");
        if (firstPageResponse.StatusCode != System.Net.HttpStatusCode.OK)
            throw new InvalidOperationException(
                $"Cursor first page failed: {await firstPageResponse.Content.ReadAsStringAsync()}");
        RecordPage? firstPage = await firstPageResponse.Content.ReadFromJsonAsync(
            HPDBaseJsonSerializerContext.Default.RecordPage);
        string cursor = firstPage?.Page.NextCursor
            ?? throw new InvalidOperationException("Cursor first page did not return continuation.");
        var continuedResponse = await client.GetAsync(
            "/base/collections/items/records?sort=item.title&limit=1&cursor="
            + Uri.EscapeDataString(cursor));
        Require(continuedResponse.StatusCode == System.Net.HttpStatusCode.OK, "Cursor continuation failed.");
        RecordPage? continuedPage = await continuedResponse.Content.ReadFromJsonAsync(
            HPDBaseJsonSerializerContext.Default.RecordPage);
        Require(continuedPage?.Items.Length == 1, "Cursor continuation body was invalid.");
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

static BaseSelectionOperationLimits SelectionLimits() => new()
{
    MaximumQueryNodes = 32, MaximumQueryDepth = 8, MaximumLiteralValues = 64, MaximumSelectedRecords = 10,
    MaximumSelectedBytes = 1_000_000, MaximumProducedMutations = 10, MaximumQueryExecutions = 1, MaximumReadIntervals = 10,
    MaximumWrittenBytes = 1_000_000, MaximumFactBytes = 1_000_000, MaximumJournalBytes = 1_000_000,
    MaximumReceiptBytes = 1_000_000, MaximumRelationChecks = 100, MaximumUniqueConstraintChecks = 100,
    MaximumPreviousStateRequirements = 10, MaximumTransientBytes = 2_000_000, MaximumResultBytes = 100_000,
    AcquisitionTimeout = TimeSpan.FromSeconds(5), ExecutionTimeout = TimeSpan.FromSeconds(5), CallerCommitObservationTimeout = TimeSpan.FromSeconds(5),
};

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
            Outcome = PolicyOutcome.Allowed,
            Audit = new PolicyAuditInfo { MatchedGrantIds = ["items.selection"] },
        });
    }
}

[BaseCollection("items", typeof(SmokeJsonContext))]
internal sealed partial record SmokeRecord
{
    [BaseField("item.title")]
    public required string Title { get; init; }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(SmokeRecord))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class SmokeJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
