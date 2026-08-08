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
    public async Task Backup_validation_and_restore_preserve_vector_carriers_and_invalidate_old_tokens()
    {
        string temporaryDirectory = Path.GetFullPath(Path.GetTempPath());
        if (OperatingSystem.IsMacOS() && temporaryDirectory.StartsWith("/var/", StringComparison.Ordinal)) temporaryDirectory = "/private" + temporaryDirectory;
        string path = Path.Combine(temporaryDirectory, "hpd-base-vector-admin-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder
                .ConfigureSchema(options => { options.ApplicationId = "vector-admin-tests"; options.PlanProtectionKey = Enumerable.Repeat((byte)0x52, 32).ToArray(); })
                .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 5, Key = Enumerable.Repeat((byte)0x62, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch })
                .AddCollection(VectorDocument.Collection)
                .UseSqlite(options => { options.DataSource = path; options.StoreId = "sqlite"; options.AdministrationEnabled = true; })
                .AddVector()
                .UseSqliteVec());
            services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schema = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schema.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!;
            (await schema.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            var principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectId = "administrator" };
            BaseCollectionSession<VectorDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(principal).Collection(VectorDocument.Collection);
            (await collection.CreateAsync(new RecordId("a"), new VectorDocument { Title = "Before", Tenant = "one", Embedding = BaseVector.Create([1, 0]) })).RequireValue();
            BaseVectorConsistencyToken oldToken = (await collection.Vector(VectorDocument.VectorIndexes.Semantic).CaptureConsistencyAsync()).RequireValue();

            IHPDBaseAdministration administration = provider.GetRequiredService<IHPDBaseAdministration>();
            var artifact = new MemoryStream();
            BaseBackupManifest manifest = (await administration.CreateBackupAsync(artifact, new BaseBackupRequest { StoreId = "sqlite", Principal = principal })).RequireValue();
            artifact.Position = 0;
            (await administration.ValidateBackupAsync(artifact, new BaseBackupValidationRequest { StoreId = "sqlite", Principal = principal, ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest })).RequireValue();
            (await collection.ReplaceAsync(new RecordId("a"), new VectorDocument { Title = "After", Tenant = "one", Embedding = BaseVector.Create([0, 1]) })).RequireValue();
            artifact.Position = 0;
            (await administration.RestoreAsync(artifact, new BaseRestoreRequest
            {
                StoreId = "sqlite",
                Principal = principal,
                ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true,
            })).RequireValue();

            BaseVectorResult<VectorDocument> restored = (await collection.Vector(VectorDocument.VectorIndexes.Semantic).Nearest(BaseVector.Create([1, 0])).Take(1).ExecuteAsync()).RequireValue();
            restored.Matches.Single().Record.Value.Title.Should().Be("Before");
            BaseResult<BaseVectorResult<VectorDocument>> stale = await collection.Vector(VectorDocument.VectorIndexes.Semantic).Nearest(BaseVector.Create([1, 0])).WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(oldToken)).Take(1).ExecuteAsync();
            ((BaseFailure<BaseVectorResult<VectorDocument>>)stale).Error.Code.Should().Be(BaseVectorErrorCodes.ConsistencyScopeMismatch);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm", path + ".hpd-restore" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public void Dot_product_schema_is_rejected_before_provider_open()
    {
        var services = new ServiceCollection();

        Action registration = () => services.AddHPDBase(builder => builder
            .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 1, Key = new byte[32], IssueNotBefore = DateTimeOffset.UnixEpoch })
            .AddCollection(DotVectorDocument.Collection)
            .UseSqlite(options => options.DataSource = ":memory:")
            .AddVector()
            .UseSqliteVec());

        registration.Should().Throw<InvalidOperationException>();
    }

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
            (await session.Collection(VectorDocument.Collection).CreateAsync(new RecordId("e"), new VectorDocument { Title = "E", Tenant = "tie", Embedding = BaseVector.Create([0, 1]) })).RequireValue();
            (await session.Collection(VectorDocument.Collection).CreateAsync(new RecordId("d"), new VectorDocument { Title = "D", Tenant = "tie", Embedding = BaseVector.Create([0, 1]) })).RequireValue();

            BaseVectorConsistencyToken consistency = (await session.Collection(VectorDocument.Collection).Vector(VectorDocument.VectorIndexes.Semantic).CaptureConsistencyAsync()).RequireValue();

            BaseVectorResult<VectorDocument> result = (await session.Collection(VectorDocument.Collection).Vector(VectorDocument.VectorIndexes.Semantic).Nearest(BaseVector.Create([1, 0])).Where(VectorDocument.Fields.Tenant, "one").Take(2).WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(consistency)).ExecuteAsync()).RequireValue();

            result.Matches.Select(static match => match.Record.Id.Value).Should().Equal("a", "c");
            result.Matches.Select(static match => match.Rank).Should().Equal(1, 2);
            result.Accuracy.Should().Be(BaseVectorResultAccuracy.Exact);
            result.ConsistencyToken.ToString().Should().NotContain(result.ConsistencyToken.Encode());
            BaseVectorResult<VectorDocument> tied = (await session.Collection(VectorDocument.Collection).Vector(VectorDocument.VectorIndexes.Semantic).Nearest(BaseVector.Create([1, 0])).WhereAny(VectorDocument.Fields.Tenant, "tie").Take(1).ExecuteAsync()).RequireValue();
            tied.Matches.Select(static match => match.Record.Id.Value).Should().Equal("d");
            BaseVectorResult<VectorDocument> euclidean = (await session.Collection(VectorDocument.Collection).Vector(VectorDocument.VectorIndexes.Euclidean).Nearest(BaseVector.Create([1, 0])).Where(VectorDocument.Fields.Tenant, "missing").OrWhere(VectorDocument.Fields.Tenant, "one").WhereAny(VectorDocument.Fields.Tenant, "one", "tie").Take(2).ExecuteAsync()).RequireValue();
            euclidean.Matches.Select(static match => match.Record.Id.Value).Should().Equal("a", "c");
            euclidean.Matches.Should().OnlyContain(static match => match.Measure.Function == BaseVectorFunction.EuclideanDistance && match.Measure.Direction == BaseVectorMeasureDirection.LowerIsNearer);

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
            BaseVectorResult<VectorDocument> afterRebuild = (await session.Collection(VectorDocument.Collection).Vector(VectorDocument.VectorIndexes.Semantic).Nearest(BaseVector.Create([1, 0])).Where(VectorDocument.Fields.Tenant, "one").Take(2).ExecuteAsync()).RequireValue();
            afterRebuild.Matches.Select(static match => match.Record.Id.Value).Should().Equal("a", "c");
            afterRebuild.VectorIndexGeneration.Should().Be(2);
            BaseResult<BaseVectorResult<VectorDocument>> stale = await session.Collection(VectorDocument.Collection).Vector(VectorDocument.VectorIndexes.Semantic).Nearest(BaseVector.Create([1, 0])).Take(1).WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(consistency)).ExecuteAsync();
            (stale as BaseFailure<BaseVectorResult<VectorDocument>>)!.Error.Code.Should().Be(BaseVectorErrorCodes.ConsistencyScopeMismatch);

            BaseResult<BaseRecord<VectorDocument>> rejected = await session.Collection(VectorDocument.Collection).CreateAsync(new RecordId("zero"), new VectorDocument { Title = "Zero", Tenant = "one", Embedding = BaseVector.Create([0, 0]) });
            rejected.Status.Should().Be(OperationStatus.ValidationFailed);
            (await session.Collection(VectorDocument.Collection).GetAsync(new RecordId("zero"))).Status.Should().Be(OperationStatus.NotFound);

            await using (var drift = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                await drift.OpenAsync();
                await using Microsoft.Data.Sqlite.SqliteCommand table = drift.CreateCommand();
                table.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'b_v_%' ORDER BY name LIMIT 1;";
                string carrier = (string)(await table.ExecuteScalarAsync())!;
                await using Microsoft.Data.Sqlite.SqliteCommand corrupt = drift.CreateCommand();
                corrupt.CommandText = $"ALTER TABLE {carrier} DROP COLUMN vector;";
                await corrupt.ExecuteNonQueryAsync();
            }
            OperationResult<BaseSchemaObservedState> drifted = await provider.GetRequiredService<SqliteRecordStore>().InspectSchemaAsync(new BaseSchemaInspectionRequest { ApplicationId = "vector-tests", ExpectedLogicalChecksum = plan.TargetChecksum, Visibility = VisibilityLevel.Admin, InspectionTimeout = TimeSpan.FromSeconds(5) });
            drifted.Value!.Compatibility.Should().Be(BaseSchemaCompatibility.Drifted);
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
[BaseVectorIndex("vector_document.euclidean", nameof(Embedding), VectorSpace = "text.embedding.test.v1", Dimensions = 2, Function = BaseVectorFunction.EuclideanDistance, FilterFields = [nameof(Tenant)])]
public partial record VectorDocument
{
    [BaseField("vector_document.title")] public required string Title { get; init; }
    [BaseField("vector_document.tenant", Operators = BaseFieldOperator.Equal)] public required string Tenant { get; init; }
    [BaseField("vector_document.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
}

[JsonSerializable(typeof(VectorDocument))]
public partial class VectorTestJsonContext : JsonSerializerContext;

[BaseCollection("dot_vector_documents", typeof(DotVectorJsonContext))]
[BaseVectorIndex("dot.vector", nameof(DotVectorDocument.Embedding), VectorSpace = "dot.space.v1", Dimensions = 2, Function = BaseVectorFunction.DotProductSimilarity)]
public partial record DotVectorDocument
{
    [BaseField("dot.vector.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
}

[JsonSerializable(typeof(DotVectorDocument))]
public partial class DotVectorJsonContext : JsonSerializerContext;

internal sealed class AllowPolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}
