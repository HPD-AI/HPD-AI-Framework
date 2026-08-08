using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Base.Vector.SqliteVec;
using HPD.Base.Vector.AspNetCore;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Vector.AotSmoke;

internal static class Program
{
    private static async Task<int> Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-vector-aot-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            WebApplicationBuilder host = WebApplication.CreateSlimBuilder();
            IServiceCollection services = host.Services.AddLogging();
            services.AddHPDBase(builder => builder.ConfigureSchema(options => { options.ApplicationId = "vector-aot"; options.PlanProtectionKey = Enumerable.Repeat((byte)0x31, 32).ToArray(); }).ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 1, Key = Enumerable.Repeat((byte)0x41, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch }).AddCollection(AotVectorRecord.Collection).UseSqlite(options => { options.StoreId = "sqlite"; options.DataSource = path; }).AddVector().UseSqliteVec());
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
            if (!(await session.Collection(AotVectorRecord.Collection).CreateAsync(new RecordId("one"), new AotVectorRecord { Label = "one", Embedding = BaseVector.Create([1, 0]) })).Status.IsSuccess()) return 4;
            BaseVectorResult<AotVectorRecord> result = (await session.Collection(AotVectorRecord.Collection).Vector(AotVectorRecord.VectorIndexes.Search).Nearest(BaseVector.Create([1, 0])).Take(1).ExecuteAsync()).RequireValue();
            return result.Matches is [{ Record.Id.Value: "one" }] ? 0 : 5;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file);
        }
    }
}

[BaseCollection("aot_vectors", typeof(AotVectorJsonContext))]
[BaseVectorIndex("aot.vector.search", nameof(Embedding), VectorSpace = "aot.space.v1", Dimensions = 2, Function = BaseVectorFunction.CosineSimilarity)]
internal partial record AotVectorRecord
{
    [BaseField("aot.vector.label")] public required string Label { get; init; }
    [BaseField("aot.vector.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
}

[JsonSerializable(typeof(AotVectorRecord))]
internal partial class AotVectorJsonContext : JsonSerializerContext;

internal sealed class AllowAll : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}
