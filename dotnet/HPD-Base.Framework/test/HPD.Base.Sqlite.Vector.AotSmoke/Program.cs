using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Sqlite.Vector.AotSmoke;

internal static class Program
{
    private static async Task<int> Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-vector-aot-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            WebApplicationBuilder host = WebApplication.CreateSlimBuilder();
            IServiceCollection services = host.Services.AddLogging();
            services.AddHPDBase(builder => builder.ConfigureSchema(options => { options.ApplicationId = "vector-aot"; options.PlanProtectionKey = Enumerable.Repeat((byte)0x31, 32).ToArray(); }).ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 1, Key = Enumerable.Repeat((byte)0x41, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch }).AddCollection(AotVectorRecord.Collection).UseStore(SqliteStore.Configure(options => { options.StoreId = "sqlite"; options.DataSource = path; })));
            services.AddSingleton<IPolicyEvaluator, AllowAll>();
            services.AddHPDBaseAspNetCore();
            services.AddHPDBaseVectorAspNetCore();
            await using WebApplication app = host.Build();
            app.MapGroup("/base").MapHPDBaseVectorApplicationApi();
            IServiceProvider provider = app.Services;
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            if (!(await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess()) return 2;
            if (!(await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess()) return 3;
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "aot" });
            if (!(await session.Collection(AotVectorRecord.Collection).CreateAsync(RecordId.Create("one"), new AotVectorRecord { Label = "one", Tenant = "a", Active = true, Embedding = BaseVector.Create([1, 0]) })).Status.IsSuccess()) return 4;
            if (!(await session.Collection(AotVectorRecord.Collection).CreateAsync(RecordId.Create("two"), new AotVectorRecord { Label = "two", Tenant = "b", Active = false, Embedding = BaseVector.Create([0, 1]) })).Status.IsSuccess()) return 4;
            BaseVectorResult<AotVectorRecord> cosine = (await session.Collection(AotVectorRecord.Collection).Vector(AotVectorRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Where(AotVectorRecord.Fields.Active, true).Take(2).ExecuteAsync()).RequireValue();
            BaseVectorResult<AotVectorRecord> consistent = (await session.Collection(AotVectorRecord.Collection).Vector(AotVectorRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(cosine.ConsistencyToken)).Take(2).ExecuteAsync()).RequireValue();
            BaseVectorResult<AotVectorRecord> euclidean = (await session.Collection(AotVectorRecord.Collection).Vector(AotVectorRecord.VectorIndexes.Euclidean).Nearest(BaseVector.Create([1, 0])).WhereAny(AotVectorRecord.Fields.Tenant, "a", "b").Take(2).ExecuteAsync()).RequireValue();
            OperationResult<BaseVectorIndexStatus[]> statuses = await provider.GetRequiredService<IBaseVectorAdministration>().ListAsync();
            BaseVectorIndexStatus target = statuses.Value!.Single(static value => value.VectorIndexId == AotVectorRecord.VectorIndexes.Cosine.Id);
            BaseResult<BaseVectorRebuildResult> rebuilt = await provider.GetRequiredService<IHPDBaseApplication>().Administration.RebuildVectorIndexAsync(new BaseVectorRebuildRequest { StoreId = "sqlite", Principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "aot" }, CollectionId = AotVectorRecord.Collection.Id, VectorIndexId = target.VectorIndexId, ExpectedGeneration = target.Generation, ExpectedPurgeGeneration = target.PurgeGeneration, Confirmation = "rebuild" });
            HealthDescriptor[][] health = await Task.WhenAll(provider.GetServices<IBaseHealthContributor>().Select(async contributor => await contributor.GetHealthAsync()));
            return cosine.Matches is [{ Record.Id.Value: "one" }] && consistent.Matches.Length == 2 && euclidean.Matches.Length == 2 && rebuilt.Status.IsSuccess() && health.SelectMany(static value => value).All(static value => value.Status != HealthStatus.Unhealthy) ? 0 : 5;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file);
        }
    }
}

[BaseCollection("aot_vectors", typeof(AotVectorJsonContext))]
[BaseVectorIndex("aot.vector.cosine", nameof(Embedding), VectorSpace = "aot.space.v1", Dimensions = 2, Function = BaseVectorFunction.CosineSimilarity, FilterFields = [nameof(Active)])]
[BaseVectorIndex("aot.vector.euclidean", nameof(Embedding), VectorSpace = "aot.space.v1", Dimensions = 2, Function = BaseVectorFunction.EuclideanDistance, FilterFields = [nameof(Tenant)])]
internal partial record AotVectorRecord
{
    [BaseField("aot.vector.label")] public required string Label { get; init; }
    [BaseField("aot.vector.tenant")] public required string Tenant { get; init; }
    [BaseField("aot.vector.active")] public required bool Active { get; init; }
    [BaseField("aot.vector.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
}

[JsonSerializable(typeof(AotVectorRecord))]
internal partial class AotVectorJsonContext : JsonSerializerContext;

internal sealed class AllowAll : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}
