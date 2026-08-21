using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Sqlite.Text.AotSmoke;

internal static class Program
{
    private static async Task<int> Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-text-aot-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddLogging(); services.AddHPDBase(builder =>
            {
                builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x51, 32).ToArray()).UseStore(SqliteStore.Configure(options => options.DataSource = path));
                builder.AddPolicyAuthority<AllowAll>(new() { Id = "text.sqlite.aot.policy", Version = 1, OwningModuleId = "text.aot", EvaluatorContractId = "text.sqlite.aot.policy.v1", EvaluatorContractVersion = 1, CompositionOrder = 0 });
                builder.AddStaticGrantAuthority(new() { Id = BaseTextGrants.Query, Version = 1, OwningModuleId = "text.aot", SourceContractId = "text.aot.grants", SourceContractVersion = 1 }, new() { Id = BaseTextGrants.Query, ApplicationId = "hpd.base.application", Audience = HPDBaseEndpointAudience.Application, Subject = new() { Kind = AccessSubjectKind.User, Id = "aot" }, Action = BaseTextGrants.Query, Scope = new() { Kind = ResourceScopeKind.TextIndex, CollectionId = "aot_sqlite_text", TextIndexId = "aot.sqlite.text.content" } });
                builder.AddCollection(AotSqliteTextRecord.Collection);
            });
            await using ServiceProvider provider = services.BuildServiceProvider(); IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>(); BaseSchemaPlan plan = (await schemas.PlanAsync(new() { StoreId = "sqlite" })).Value!; if (!(await schemas.ApplyAsync(new() { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess() || !(await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess()) return 2;
            BaseCollectionSession<AotSqliteTextRecord> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "aot" }).Collection(AotSqliteTextRecord.Collection);
            (await collection.CreateAsync(new("one"), new() { Body = "SQLite portable phrase", State = "published" })).RequireValue(); BaseTextResult<AotSqliteTextRecord> result = (await collection.Text(AotSqliteTextRecord.TextIndexes.Content, BaseTextQuery.StartsWith("port")).Take(4).ExecuteAsync()).RequireValue(); return result.Matches is [{ Record.Id.Value: "one" }] ? 0 : 3;
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (string file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file); }
    }
}
[BaseCollection("aot_sqlite_text", typeof(AotSqliteTextJsonContext))]
[BaseTextIndex("aot.sqlite.text.content", Fields = [nameof(Body)], Weights = [2], FilterFields = [nameof(State)])]
internal partial record AotSqliteTextRecord { [BaseField("aot.sqlite.text.body")] public required string Body { get; init; } [BaseField("aot.sqlite.text.state")] public required string State { get; init; } }
[JsonSerializable(typeof(AotSqliteTextRecord))] internal partial class AotSqliteTextJsonContext : JsonSerializerContext;
internal sealed class AllowAll : IPolicyEvaluator { public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow()); }
