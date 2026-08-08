using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base.Sqlite;
using HPD.Base.Vector.SqliteVec;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Vector.SqliteVec.Tests;

public sealed class SqliteVecEndToEndTests
{
    [Fact]
    public async Task Mutation_filter_rank_and_hydration_share_authoritative_state()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-vector-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options => { options.ApplicationId = "vector-tests"; options.PlanProtectionKey = Enumerable.Repeat((byte)0x51, 32).ToArray(); })
                .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 4, Key = Enumerable.Repeat((byte)0x61, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch })
                .AddCollection(VectorDocument.Collection)
                .UseSqlite(options => { options.DataSource = path; options.StoreId = "sqlite"; })
                .AddVector()
                .UseSqliteVec());
            services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schema = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schema.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schema.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            var principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "tester" };
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);

            (await session.Collection(VectorDocument.Collection).CreateAsync(new RecordId("a"), new VectorDocument { Title = "A", Tenant = "one", Embedding = BaseVector.Create([1, 0]) })).RequireValue();
            (await session.Collection(VectorDocument.Collection).CreateAsync(new RecordId("b"), new VectorDocument { Title = "B", Tenant = "two", Embedding = BaseVector.Create([0, 1]) })).RequireValue();
            (await session.Collection(VectorDocument.Collection).CreateAsync(new RecordId("c"), new VectorDocument { Title = "C", Tenant = "one", Embedding = BaseVector.Create([0.8f, 0.2f]) })).RequireValue();

            BaseVectorConsistencyToken consistency = (await session.Collection(VectorDocument.Collection).Vector(VectorDocument.VectorIndexes.Semantic).CaptureConsistencyAsync()).RequireValue();

            BaseVectorResult<VectorDocument> result = (await session.Collection(VectorDocument.Collection).Vector(VectorDocument.VectorIndexes.Semantic).Nearest(BaseVector.Create([1, 0])).Where(VectorDocument.Fields.Tenant, "one").Take(2).WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(consistency)).ExecuteAsync()).RequireValue();

            result.Matches.Select(static match => match.Record.Id.Value).Should().Equal("a", "c");
            result.Matches.Select(static match => match.Rank).Should().Equal(1, 2);
            result.Accuracy.Should().Be(BaseVectorResultAccuracy.Exact);
            result.ConsistencyToken.ToString().Should().NotContain(result.ConsistencyToken.Encode());

            IBaseVectorAdministration administration = provider.GetRequiredService<IBaseVectorAdministration>();
            BaseVectorIndexStatus status = (await administration.GetAsync(VectorDocument.Collection.Id, VectorDocument.VectorIndexes.Semantic.Definition.Id)).Value!;
            status.Generation.Should().Be(1);
            status.AppliedThrough.Value.Should().BeGreaterThan(0);
            BaseVectorRebuildResult rebuilt = (await provider.GetRequiredService<IHPDBaseAdministration>().RebuildVectorIndexAsync(new BaseVectorRebuildRequest
            {
                StoreId = "sqlite",
                Principal = principal,
                CollectionId = VectorDocument.Collection.Id,
                VectorIndexId = VectorDocument.VectorIndexes.Semantic.Definition.Id,
                ExpectedGeneration = status.Generation,
                ExpectedPurgeGeneration = status.PurgeGeneration,
                Confirmation = "REBUILD VECTOR INDEX",
            })).RequireValue();
            rebuilt.PublishedGeneration.Should().Be(2);
            (await administration.GetAsync(VectorDocument.Collection.Id, VectorDocument.VectorIndexes.Semantic.Definition.Id)).Value!.Generation.Should().Be(2);
            BaseResult<BaseVectorResult<VectorDocument>> stale = await session.Collection(VectorDocument.Collection).Vector(VectorDocument.VectorIndexes.Semantic).Nearest(BaseVector.Create([1, 0])).Take(1).WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(consistency)).ExecuteAsync();
            (stale as BaseFailure<BaseVectorResult<VectorDocument>>)!.Error.Code.Should().Be(BaseVectorErrorCodes.ConsistencyScopeMismatch);

            BaseResult<BaseRecord<VectorDocument>> rejected = await session.Collection(VectorDocument.Collection).CreateAsync(new RecordId("zero"), new VectorDocument { Title = "Zero", Tenant = "one", Embedding = BaseVector.Create([0, 0]) });
            rejected.Status.Should().Be(OperationStatus.ValidationFailed);
            (await session.Collection(VectorDocument.Collection).GetAsync(new RecordId("zero"))).Status.Should().Be(OperationStatus.NotFound);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }
}

[BaseCollection("vector_documents", typeof(VectorTestJsonContext))]
[BaseVectorIndex("vector_document.semantic", nameof(Embedding), VectorSpace = "text.embedding.test.v1", Dimensions = 2, Function = BaseVectorFunction.CosineSimilarity, FilterFields = [nameof(Tenant)])]
public partial record VectorDocument
{
    [BaseField("vector_document.title")] public required string Title { get; init; }
    [BaseField("vector_document.tenant", Operators = BaseFieldOperator.Equal)] public required string Tenant { get; init; }
    [BaseField("vector_document.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
}

[JsonSerializable(typeof(VectorDocument))]
public partial class VectorTestJsonContext : JsonSerializerContext;

internal sealed class AllowPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}
