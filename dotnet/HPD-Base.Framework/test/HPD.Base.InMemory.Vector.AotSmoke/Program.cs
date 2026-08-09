using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.InMemory.Vector.AotSmoke;

internal static class Program
{
    private static async Task<int> Main()
    {
        WebApplicationBuilder host = WebApplication.CreateSlimBuilder();
        host.Services.AddHPDBase(builder => builder
            .ReplacePolicyEvaluator<AllowAll>()
            .AddCollection(InMemoryVectorRecord.Collection));
        host.Services.AddHPDBaseAspNetCore();
        host.Services.AddHPDBaseVectorAspNetCore();
        await using WebApplication app = host.Build();
        app.MapGroup("/base").MapHPDBaseVectorApplicationApi();
        IServiceProvider provider = app.Services;
        if (!(await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess()) return 2;
        PrincipalContext principal = new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "aot" };
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
        (await session.Collection(InMemoryVectorRecord.Collection).CreateAsync(new RecordId("one"), new InMemoryVectorRecord { Label = "one", Tenant = new RecordId("tenant-a"), Active = true, Priority = 7, Optional = null, Embedding = BaseVector.Create([1, 0]) })).RequireValue();
        (await session.Collection(InMemoryVectorRecord.Collection).CreateAsync(new RecordId("two"), new InMemoryVectorRecord { Label = "two", Tenant = new RecordId("tenant-b"), Active = false, Priority = 9, Optional = "present", Embedding = BaseVector.Create([0, 1]) })).RequireValue();

        BaseVectorResult<InMemoryVectorRecord> cosine = (await session.Collection(InMemoryVectorRecord.Collection)
            .Vector(InMemoryVectorRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0]))
            .Where(InMemoryVectorRecord.Fields.Active, true).WhereAny(InMemoryVectorRecord.Fields.Priority, 7L, 8L)
            .Where<string>(InMemoryVectorRecord.Fields.Optional, null!).Take(2).ExecuteAsync()).RequireValue();
        BaseVectorResult<InMemoryVectorRecord> euclidean = (await session.Collection(InMemoryVectorRecord.Collection)
            .Vector(InMemoryVectorRecord.VectorIndexes.Euclidean).Nearest(BaseVector.Create([1, 0]))
            .Where(InMemoryVectorRecord.Fields.Tenant, new RecordId("tenant-a")).OrWhere(InMemoryVectorRecord.Fields.Label, "two")
            .Take(2).ExecuteAsync()).RequireValue();
        BaseVectorConsistencyToken token = cosine.ConsistencyToken;
        BaseVectorResult<InMemoryVectorRecord> consistent = (await session.Collection(InMemoryVectorRecord.Collection)
            .Vector(InMemoryVectorRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0]))
            .WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(token)).Take(2).ExecuteAsync()).RequireValue();
        BaseVectorResult<InMemoryVectorRecord> dot = (await session.Collection(InMemoryVectorRecord.Collection)
            .Vector(InMemoryVectorRecord.VectorIndexes.Dot).Nearest(BaseVector.Create([1, 0])).Take(2).ExecuteAsync()).RequireValue();
        OperationResult<BaseVectorIndexStatus[]> statuses = await provider.GetRequiredService<IBaseVectorAdministration>().ListAsync();
        HealthDescriptor[][] health = await Task.WhenAll(provider.GetServices<IBaseHealthContributor>().Select(async contributor => await contributor.GetHealthAsync()));
        if (cosine.Matches is not [{ Record.Id.Value: "one" }] || euclidean.Matches.Length != 2 || consistent.Matches.Length != 2 || dot.Matches.Length != 2 ||
            !statuses.IsSuccess() || statuses.Value?.Length != 3 || health.SelectMany(static value => value).Any(static value => value.Status == HealthStatus.Unhealthy)) return 4;
        return 0;
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
    [BaseField("inmemory.vector.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
}

[JsonSerializable(typeof(InMemoryVectorRecord))]
internal partial class InMemoryVectorJsonContext : JsonSerializerContext;

internal sealed class AllowAll : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}
