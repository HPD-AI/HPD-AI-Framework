using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

var dataSource = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-aot-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddHPDBase(builder =>
    {
        builder.ConfigureSchema(options =>
        {
            options.ApplicationId = "hpd.base.sqlite.aot";
            options.PlanProtectionKey = Enumerable.Repeat((byte)0x61, 32).ToArray();
        });
        builder.UseSqlite(options => { options.StoreId = "smoke.sqlite"; options.DataSource = dataSource; });
        builder.AddCollection(HPD.Base.BaseCollection.Define("items", SmokeJsonContext.Default.SmokeRecord, schema => schema.String("title", "title")));
        builder.ReplacePolicyEvaluator<SmokePolicyEvaluator>();
    });

    await using var provider = services.BuildServiceProvider();
    IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
    BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "smoke.sqlite" })).Value!;
    Require((await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess(), "Schema apply failed.");
    Require((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess(), "Application initialization failed.");

    var runtime = provider.GetRequiredService<IBaseRecordRuntime>();
    var principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Anonymous };
    var create = await runtime.CreateAsync("items", new RecordCreateRequest { Payload = Payload("hello") }, principal, Operation(BaseOperationKind.Create));
    Require(create.Status == OperationStatus.Created, "Create failed.");
    var journal = (ITransactionalMutationJournalStore)provider
        .GetRequiredService<IRecordStoreRegistry>()
        .GetStoreForCollection("items")!;
    var journalPage = await journal.ReadMutationJournalAsync(
        new BaseMutationJournalReadRequest { Limit = 10 });
    Require(journalPage.Entries.Length == 1, "Mutation journal append/read failed.");
    _ = JsonSerializer.Serialize(
        journalPage,
        HPD.Base.HPDBaseJsonSerializerContext.Default.BaseMutationJournalPage);

    var list = await runtime.ListAsync("items", new RecordQuery { Count = QueryCountMode.Exact }, principal, Operation(BaseOperationKind.List));
    Require(list.Status == OperationStatus.Ok && list.Value!.Count!.Total == 1, "List/count failed.");

    var relational = provider.GetRequiredService<IRelationalMetadataProvider>();
    var descriptor = await relational.GetStoreAsync(Operation(BaseOperationKind.List), VisibilityLevel.Admin);
    Require(descriptor.Status == OperationStatus.Ok && descriptor.Value is not null, "Relational descriptor failed.");
    _ = JsonSerializer.Serialize(descriptor.Value, HPDBaseSqliteJsonSerializerContext.Default.RelationalStoreDescriptor);
    _ = JsonSerializer.Serialize(
        new HPD.Base.Sqlite.HPDBaseSqliteOptions
        {
            StoreId = "serialized",
            Collections = [new CollectionDefinition
            {
                Id = "items", Name = "items", Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose, UnknownFields = UnknownFieldPolicy.Preserve
            }]
        },
        HPDBaseSqliteJsonSerializerContext.Default.HPDBaseSqliteOptions);

    var delete = await runtime.DeleteAsync("items", create.Value!.Id, new RecordDeleteRequest { ExpectedRevision = create.Value.Metadata.Revision, ReturnPrevious = true }, principal, Operation(BaseOperationKind.Delete));
    Require(delete.Status == OperationStatus.Deleted && delete.Value!.Previous is not null, "Delete failed.");
    Require(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection fallback must be disabled.");
}
finally
{
    foreach (var candidate in new[] { dataSource, dataSource + "-wal", dataSource + "-shm" })
    {
        if (File.Exists(candidate)) File.Delete(candidate);
    }
}

static OperationContext Operation(BaseOperationKind kind) => new() { Operation = kind, CollectionId = "items", Now = DateTimeOffset.UtcNow };

static RecordPayload Payload(string title)
{
    using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
    return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
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
    public SmokePolicyEvaluator() { }

    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
    }
}

internal sealed record SmokeRecord(string Title);

[System.Text.Json.Serialization.JsonSerializable(typeof(SmokeRecord))]
internal sealed partial class SmokeJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
