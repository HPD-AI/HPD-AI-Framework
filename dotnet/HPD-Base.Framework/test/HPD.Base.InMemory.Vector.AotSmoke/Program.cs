using System.Text.Json.Serialization;
using System.Diagnostics;
using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.InMemory.Vector.AotSmoke;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        WebApplicationBuilder host = WebApplication.CreateSlimBuilder();
        host.Services.AddHPDBase(builder => builder
            .AddPolicyAuthority<AllowAll>(new BasePolicyAuthorityDefinition
            {
                Id = "hpd.base.vector.aot.allow", Version = 1, OwningModuleId = "hpd.base.vector.aot",
                EvaluatorContractId = "hpd.base.vector.aot.policy", EvaluatorContractVersion = 1, CompositionOrder = 0,
            })
            .ConfigureVector(options => options.MaxTopK = 1_000)
            .AddCollection(InMemoryVectorRecord.Collection)
            .AddCollection(InMemoryUnrelatedRecord.Collection));
        host.Services.AddHPDBaseAspNetCore();
        host.Services.AddHPDBaseVectorAspNetCore();
        await using WebApplication app = host.Build();
        app.MapGroup("/base").MapHPDBaseVectorApplicationApi();
        IServiceProvider provider = app.Services;
        if (!(await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess()) return 2;
        PrincipalContext principal = new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "aot" };
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
        if (args is ["--capacity"])
            return await RunCapacityGateAsync(provider, session, principal);
        (await session.Collection(InMemoryVectorRecord.Collection).CreateAsync(RecordId.Create("one"), new InMemoryVectorRecord { Label = "one", Tenant = RecordId.Create("tenant-a"), Active = true, Priority = 7, Optional = null, Embedding = BaseVector.Create([1, 0]) })).RequireValue();
        (await session.Collection(InMemoryVectorRecord.Collection).CreateAsync(RecordId.Create("two"), new InMemoryVectorRecord { Label = "two", Tenant = RecordId.Create("tenant-b"), Active = false, Priority = 9, Optional = "present", Embedding = BaseVector.Create([0, 1]) })).RequireValue();

        BaseVectorResult<InMemoryVectorRecord> cosine = (await session.Collection(InMemoryVectorRecord.Collection)
            .Vector(InMemoryVectorRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0]))
            .Where(InMemoryVectorRecord.Fields.Active, true).WhereAny(InMemoryVectorRecord.Fields.Priority, 7L, 8L)
            .Where<string>(InMemoryVectorRecord.Fields.Optional, null!).Take(2).ExecuteAsync()).RequireValue();
        BaseVectorResult<InMemoryVectorRecord> euclidean = (await session.Collection(InMemoryVectorRecord.Collection)
            .Vector(InMemoryVectorRecord.VectorIndexes.Euclidean).Nearest(BaseVector.Create([1, 0]))
            .Where(InMemoryVectorRecord.Fields.Tenant, RecordId.Create("tenant-a")).OrWhere(InMemoryVectorRecord.Fields.Label, "two")
            .Take(2).ExecuteAsync()).RequireValue();
        BaseVectorConsistencyToken token = cosine.ConsistencyToken;
        BaseVectorResult<InMemoryVectorRecord> consistent = (await session.Collection(InMemoryVectorRecord.Collection)
            .Vector(InMemoryVectorRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0]))
            .WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(token)).Take(2).ExecuteAsync()).RequireValue();
        BaseVectorResult<InMemoryVectorRecord> dot = (await session.Collection(InMemoryVectorRecord.Collection)
            .Vector(InMemoryVectorRecord.VectorIndexes.Dot).Nearest(BaseVector.Create([1, 0])).Take(2).ExecuteAsync()).RequireValue();
        OperationResult<BaseVectorIndexStatus[]> statuses = await provider.GetRequiredService<IBaseVectorAdministration>().ListAsync();
        HealthDescriptor[][] health = await Task.WhenAll(provider.GetServices<IBaseHealthContributor>().Select(async contributor => await contributor.GetHealthAsync()));
        if (cosine.Matches is not [{ Record.Id.Value: "one" }] || euclidean.Matches is not [{ Record.Id.Value: "two" }] || consistent.Matches.Length != 2 || dot.Matches.Length != 2 ||
            !statuses.IsSuccess() || statuses.Value?.Length != 3 || health.SelectMany(static value => value).Any(static value => value.Status == HealthStatus.Unhealthy))
        {
            return 4;
        }
        return 0;
    }

    private static async Task<int> RunCapacityGateAsync(IServiceProvider provider, BaseSession session, PrincipalContext principal)
    {
        const int recordCount = 100_000;
        const int vectorCount = 999;
        const long maximumRetainedBytes = 512L * 1024 * 1024;
        var elapsed = Stopwatch.StartNew();
        Process process = Process.GetCurrentProcess();
        process.Refresh();
        long baseline = process.WorkingSet64;
        (await session.Collection(InMemoryUnrelatedRecord.Collection).CreateAsync(RecordId.Create("unrelated"), new InMemoryUnrelatedRecord { Value = "untouched" })).RequireValue();

        for (int offset = 0; offset < recordCount; offset += 100)
        {
            BaseBatchBuilder batch = session.Atomic();
            int end = Math.Min(recordCount, offset + 100);
            for (int index = offset; index < end; index++)
                batch.Create(InMemoryVectorRecord.Collection, RecordId.Create($"capacity-{index:D6}"), new InMemoryVectorRecord
                {
                    Label = $"record-{index:D6}", Tenant = RecordId.Create("capacity"), Active = true,
                    Priority = index, Optional = null, Embedding = index < vectorCount ? BaseVector.Create([1, index / 1000f]) : null,
                });
            (await batch.CommitAsync()).RequireValue().RequireCommitted();
        }

        InMemoryRecordStore store = provider.GetRequiredService<InMemoryRecordStore>();
        OperationResult<IInMemoryProjectionReadSession> firstCapture = await ((IInMemoryProjectionAuthority)store).CaptureAsync(CancellationToken.None);
        if (!firstCapture.IsSuccess() || firstCapture.Value is null) return 10;
        await using IInMemoryProjectionReadSession firstRoot = firstCapture.Value;
        BaseInMemoryProjectionIndexHandle firstHandle = firstRoot.ProjectionSnapshot.GetIndexHandles().Single(handle => handle.Index.Id == InMemoryVectorRecord.VectorIndexes.Cosine.Definition.Id);
        if (firstRoot.ProjectionSnapshot.GetIndexHandles().Any(handle => handle.Collection.Id == InMemoryUnrelatedRecord.Collection.Id) || firstRoot.ProjectionSnapshot.AuthoritativeRecordCounts[InMemoryUnrelatedRecord.Collection.Id] != 1) return 23;
        IReadOnlyDictionary<string, InMemoryVectorCarrier> firstCarriers = firstRoot.State.GetCarriers(firstHandle);
        if (firstCarriers.Count != vectorCount || !firstCarriers.TryGetValue("capacity-000000", out InMemoryVectorCarrier? firstCarrier)) return 14;
        int examined = 0;
        int previousExamined = -1;
        BaseInMemoryProjectionSourceCursor? scanCursor = null;
        do
        {
            OperationResult<BaseInMemoryProjectionSourcePage> page = await firstRoot.EnumerateProjectionSourceAsync(firstHandle, new BaseInMemoryProjectionSourceScanRequest(1_024, scanCursor), CancellationToken.None);
            if (!page.IsSuccess() || page.Value is null || page.Value.ExaminedSourceEntries is < 1 or > 1_024) return 15;
            examined = checked(examined + page.Value.ExaminedSourceEntries);
            if (examined <= previousExamined) return 16;
            previousExamined = examined;
            scanCursor = page.Value.Cursor;
        }
        while (scanCursor is not null);
        if (examined != recordCount) return 17;
        await session.Collection(InMemoryVectorRecord.Collection).ReplaceAsync(RecordId.Create("capacity-000000"), new InMemoryVectorRecord
        { Label = "mutation-one", Tenant = RecordId.Create("capacity"), Active = true, Priority = 0, Optional = null, Embedding = BaseVector.Create([1, 0]) });
        OperationResult<IInMemoryProjectionReadSession> secondCapture = await ((IInMemoryProjectionAuthority)store).CaptureAsync(CancellationToken.None);
        if (!secondCapture.IsSuccess() || secondCapture.Value is null) return 11;
        await using IInMemoryProjectionReadSession secondRoot = secondCapture.Value;
        BaseInMemoryProjectionIndexHandle secondHandle = secondRoot.ProjectionSnapshot.GetIndexHandles().Single(handle => handle.Index.Id == InMemoryVectorRecord.VectorIndexes.Cosine.Definition.Id);
        IReadOnlyDictionary<string, InMemoryVectorCarrier> secondCarriers = secondRoot.State.GetCarriers(secondHandle);
        if (secondCarriers.Count != vectorCount || !secondCarriers.TryGetValue("capacity-000000", out InMemoryVectorCarrier? secondCarrier) || secondCarrier.Revision == firstCarrier.Revision) return 18;
        await session.Collection(InMemoryVectorRecord.Collection).ReplaceAsync(RecordId.Create("capacity-000000"), new InMemoryVectorRecord
        { Label = "mutation-two", Tenant = RecordId.Create("capacity"), Active = true, Priority = 0, Optional = null, Embedding = BaseVector.Create([1, 0]) });

        OperationResult<BaseVectorIndexStatus[]> listed = await provider.GetRequiredService<IBaseVectorAdministration>().ListAsync();
        if (!listed.IsSuccess() || listed.Value is null) return 13;
        BaseVectorIndexStatus target = listed.Value.Single(status => status.VectorIndexId == InMemoryVectorRecord.VectorIndexes.Cosine.Definition.Id);
        BaseResult<BaseVectorRebuildResult> rebuilt = await provider.GetRequiredService<IHPDBaseAdministration>().RebuildVectorIndexAsync(new BaseVectorRebuildRequest
        {
            StoreId = "inmemory", Principal = principal, CollectionId = target.CollectionId, VectorIndexId = target.VectorIndexId,
            ExpectedGeneration = target.Generation, ExpectedPurgeGeneration = target.PurgeGeneration, Confirmation = "rebuild",
        });
        if (rebuilt is not BaseSuccess<BaseVectorRebuildResult> rebuiltSuccess || rebuiltSuccess.Value.PublishedGeneration != target.Generation + 1) return 19;
        BaseVectorResult<InMemoryVectorRecord> searchable = (await session.Collection(InMemoryVectorRecord.Collection)
            .Vector(InMemoryVectorRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Take(1_000).ExecuteAsync()).RequireValue();
        if (searchable.Matches.Length != vectorCount || searchable.VectorIndexGeneration != rebuiltSuccess.Value.PublishedGeneration) return 20;
        OperationResult<IInMemoryProjectionReadSession> currentCapture = await ((IInMemoryProjectionAuthority)store).CaptureAsync(CancellationToken.None);
        if (!currentCapture.IsSuccess() || currentCapture.Value is null) return 21;
        await using IInMemoryProjectionReadSession currentRoot = currentCapture.Value;
        BaseInMemoryProjectionIndexHandle currentHandle = currentRoot.ProjectionSnapshot.GetIndexHandles().Single(handle => handle.Index.Id == InMemoryVectorRecord.VectorIndexes.Cosine.Definition.Id);
        if (currentRoot.State.GetCarriers(currentHandle).Count != vectorCount || firstRoot.State.GetCarriers(firstHandle)["capacity-000000"].Revision != firstCarrier.Revision || secondRoot.State.GetCarriers(secondHandle)["capacity-000000"].Revision != secondCarrier.Revision) return 22;
        process.Refresh();
        long retainedBytes = Math.Max(0, process.WorkingSet64 - baseline);
        Console.WriteLine($"records={recordCount} vectors={vectorCount} searchable={searchable.Matches.Length} retainedRoots=2 scanPageMaximum=1024 examined={examined} retainedBytes={retainedBytes} elapsedSeconds={elapsed.Elapsed.TotalSeconds:F1}");
        return retainedBytes <= maximumRetainedBytes && elapsed.Elapsed < TimeSpan.FromHours(1) ? 0 : 12;
    }
}

[BaseCollection("inmemory_vector_records", typeof(InMemoryVectorJsonContext))]
[BaseVectorIndex("inmemory.vector.cosine", nameof(Embedding), VectorSpace = "inmemory.space.v1", Dimensions = 2, Function = BaseVectorFunction.CosineSimilarity, FilterFields = [nameof(Label), nameof(Tenant), nameof(Active), nameof(Priority), nameof(Optional)])]
[BaseVectorIndex("inmemory.vector.euclidean", nameof(Embedding), VectorSpace = "inmemory.space.v1", Dimensions = 2, Function = BaseVectorFunction.EuclideanDistance, FilterFields = [nameof(Label), nameof(Tenant)])]
[BaseVectorIndex("inmemory.vector.dot", nameof(Embedding), VectorSpace = "inmemory.space.v1", Dimensions = 2, Function = BaseVectorFunction.DotProductSimilarity)]
internal partial record InMemoryVectorRecord
{
    [BaseField("inmemory.vector.label")] public required string Label { get; init; }
    [BaseField("inmemory.vector.tenant")] public required RecordId Tenant { get; init; }
    [BaseField("inmemory.vector.active")] public required bool Active { get; init; }
    [BaseField("inmemory.vector.priority")] public required long Priority { get; init; }
    [BaseField("inmemory.vector.optional")] public string? Optional { get; init; }
    [BaseField("inmemory.vector.embedding", Operators = BaseFieldOperator.None)] public BaseVector? Embedding { get; init; }
}

[BaseCollection("inmemory_unrelated_records", typeof(InMemoryVectorJsonContext))]
internal partial record InMemoryUnrelatedRecord
{
    [BaseField("inmemory.unrelated.value")] public required string Value { get; init; }
}

[JsonSerializable(typeof(InMemoryVectorRecord))]
[JsonSerializable(typeof(InMemoryUnrelatedRecord))]
internal partial class InMemoryVectorJsonContext : JsonSerializerContext;

internal sealed class AllowAll : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}
