using System.Text.Json;
using System.Collections.Immutable;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteSchemaApplyAdversarialTests
{
    [Fact]
    public async Task PopulatedCollectionsCanAddAlterAndRemoveGeneratedScalarIndexes()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-schema-index-evolution-" + Guid.NewGuid().ToString("N") + ".db");
        const string applicationId = "schema-index-evolution";
        try
        {
            CollectionDefinition withoutIndex = IndexedCollection([]);
            await using (SqliteRecordStore initial = SqliteTestFactory.Create(new HPDBaseSqliteOptions
            {
                DataSource = path, StoreId = "sqlite", Collections = [withoutIndex],
            }, initializeSchema: false))
            {
                BaseSchemaPreparedPlan prepared = (await initial.PrepareSchemaPlanAsync(Preparation(applicationId))).Value!;
                (await ApplyAsync(initial, prepared, applicationId)).IsSuccess().Should().BeTrue();
                (await initial.CreateAsync(withoutIndex, new RecordCreateRequest
                {
                    RequestedId = new RecordId("seed"), Payload = Payload("alpha"),
                }, OperationContext(BaseOperationKind.Create))).IsSuccess().Should().BeTrue();
            }

            BaseLogicalIndexDefinition addedIndex = Index(unique: true, equalLiteral: null);
            CollectionDefinition withIndex = IndexedCollection([addedIndex]);
            await ApplyEvolutionAsync(path, applicationId, withIndex, BaseSchemaOperationKind.AddIndex, 1, "target", "target-2");

            BaseLogicalIndexDefinition alteredIndex = Index(unique: true, equalLiteral: "alpha") with { Version = 2 };
            CollectionDefinition altered = IndexedCollection([alteredIndex]);
            await ApplyEvolutionAsync(path, applicationId, altered, BaseSchemaOperationKind.AlterIndex, 2, "target-2", "target-3");

            await ApplyEvolutionAsync(path, applicationId, withoutIndex, BaseSchemaOperationKind.RemoveIndex, 3, "target-3", "target-4");

            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE 'b_i_%';";
            Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task BusyFailureWithFailedRollbackReturnsIndeterminate()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-schema-busy-" + Guid.NewGuid().ToString("N") + ".db");
        var commands = new BusyThenFailRollbackController();
        var options = new HPDBaseSqliteOptions
        {
            DataSource = path,
            StoreId = "sqlite",
            Collections = [SqliteTestFactory.Collection()],
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(options, schemaCommands: commands, initializeSchema: false);
            OperationResult<BaseSchemaPreparedPlan> prepared = await store.PrepareSchemaPlanAsync(Preparation("schema-busy-app"));
            OperationResult<BaseSchemaApplyResult> result = await ApplyAsync(store, prepared.Value!, "schema-busy-app");

            result.Error!.Code.Should().Be(BaseSchemaErrorCodes.MigrationIndeterminate);
            result.Value.Should().BeNull();
            commands.RollbackAttempts.Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task CallerCancellationWithFailedRollbackReturnsIndeterminateInsteadOfClaimingCancellation()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-schema-cancel-" + Guid.NewGuid().ToString("N") + ".db");
        using var cancellation = new CancellationTokenSource();
        var commands = new CancelThenFailRollbackController(cancellation);
        var options = new HPDBaseSqliteOptions
        {
            DataSource = path,
            StoreId = "sqlite",
            Collections = [SqliteTestFactory.Collection()],
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(options, schemaCommands: commands, initializeSchema: false);
            var observed = new BaseSchemaObservedState
            {
                StoreId = "sqlite", Generation = 0, Compatibility = BaseSchemaCompatibility.Unknown,
                Assets = [], MigrationState = BaseSchemaMigrationState.None,
            };
            OperationResult<BaseSchemaPreparedPlan> prepared = await store.PrepareSchemaPlanAsync(new BaseSchemaPreparationRequest
            {
                ApplicationId = "schema-cancel-app",
                LogicalDelta = [new BaseSchemaLogicalOperation { Kind = BaseSchemaOperationKind.CreateCollection, LogicalId = "c:items" }],
                ObservedState = observed,
                Classification = BaseSchemaPlanClassification.SafeStructural,
                ExpectedGeneration = 0,
                TargetChecksum = "target",
                PreparationTimeout = TimeSpan.FromSeconds(1),
            });
            BaseSchemaPreparedPlan plan = prepared.Value!;
            var envelope = new BaseSchemaProviderVerifiedEnvelope
            {
                PlanId = "plan", TargetBaselineId = "baseline", ApplicationId = "schema-cancel-app", StoreId = "sqlite",
                PersistedStoreInstanceId = plan.PersistedStoreInstanceId, ProviderId = plan.ProviderId,
                ProviderVersion = plan.ProviderVersion, PlannerVersion = plan.PlannerVersion,
                Classification = BaseSchemaPlanClassification.SafeStructural, LogicalPlanDigest = "logical",
                ProviderApplyArtifactDigest = plan.ProviderApplyArtifactDigest,
                CreatedAt = DateTimeOffset.UnixEpoch, ExpiresAt = DateTimeOffset.UnixEpoch.AddYears(100),
            };

            OperationResult<BaseSchemaApplyResult> result = await store.ApplySchemaAsync(new BaseSchemaProviderApplyRequest
            {
                VerifiedPlanEnvelope = JsonSerializer.SerializeToUtf8Bytes(envelope, HPDBaseJsonSerializerContext.Default.BaseSchemaProviderVerifiedEnvelope),
                ProviderApplyArtifact = plan.ProviderApplyArtifact,
                ExpectedGeneration = 0,
                ExpectedTargetChecksum = "target",
                LeaseTimeout = TimeSpan.FromSeconds(1),
                ApplyTimeout = TimeSpan.FromSeconds(1),
                CommitCompletionTimeout = TimeSpan.FromSeconds(1),
            }, cancellation.Token);

            result.Error!.Code.Should().Be(BaseSchemaErrorCodes.MigrationIndeterminate);
            result.Value.Should().BeNull();
            commands.RollbackAttempts.Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed class CancelThenFailRollbackController(CancellationTokenSource cancellation) : ISqliteSchemaCommandController
    {
        private int _begun;
        public int RollbackAttempts { get; private set; }

        public async ValueTask ExecuteAsync(SqliteConnection connection, string sql, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (sql == "ROLLBACK;")
            {
                RollbackAttempts++;
                throw new SqliteException("secret rollback failure", 1);
            }
            if (sql == "BEGIN IMMEDIATE;")
            {
                await DefaultSqliteSchemaCommandController.Instance.ExecuteAsync(connection, sql, timeout, cancellationToken);
                _begun = 1;
                return;
            }
            if (_begun != 0)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            await DefaultSqliteSchemaCommandController.Instance.ExecuteAsync(connection, sql, timeout, cancellationToken);
        }
    }

    private sealed class BusyThenFailRollbackController : ISqliteSchemaCommandController
    {
        private int _begun;
        public int RollbackAttempts { get; private set; }

        public async ValueTask ExecuteAsync(SqliteConnection connection, string sql, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (sql == "ROLLBACK;")
            {
                RollbackAttempts++;
                throw new SqliteException("secret rollback failure", 1);
            }
            if (sql == "BEGIN IMMEDIATE;")
            {
                await DefaultSqliteSchemaCommandController.Instance.ExecuteAsync(connection, sql, timeout, cancellationToken);
                _begun = 1;
                return;
            }
            if (_begun != 0) throw new SqliteException("secret busy failure", 5);
            await DefaultSqliteSchemaCommandController.Instance.ExecuteAsync(connection, sql, timeout, cancellationToken);
        }
    }

    private static BaseSchemaPreparationRequest Preparation(string applicationId) => new()
    {
        ApplicationId = applicationId,
        LogicalDelta = [new BaseSchemaLogicalOperation { Kind = BaseSchemaOperationKind.CreateCollection, LogicalId = "c:items" }],
        ObservedState = new BaseSchemaObservedState
        {
            StoreId = "sqlite", Generation = 0, Compatibility = BaseSchemaCompatibility.Unknown,
            Assets = [], MigrationState = BaseSchemaMigrationState.None,
        },
        Classification = BaseSchemaPlanClassification.SafeStructural,
        ExpectedGeneration = 0,
        TargetChecksum = "target",
        PreparationTimeout = TimeSpan.FromSeconds(1),
    };

    private static ValueTask<OperationResult<BaseSchemaApplyResult>> ApplyAsync(
        SqliteRecordStore store,
        BaseSchemaPreparedPlan plan,
        string applicationId)
    {
        var envelope = new BaseSchemaProviderVerifiedEnvelope
        {
            PlanId = "plan", TargetBaselineId = "baseline", ApplicationId = applicationId, StoreId = "sqlite",
            PersistedStoreInstanceId = plan.PersistedStoreInstanceId, ProviderId = plan.ProviderId,
            ProviderVersion = plan.ProviderVersion, PlannerVersion = plan.PlannerVersion,
            Classification = BaseSchemaPlanClassification.SafeStructural, LogicalPlanDigest = "logical",
            ProviderApplyArtifactDigest = plan.ProviderApplyArtifactDigest,
            CreatedAt = DateTimeOffset.UnixEpoch, ExpiresAt = DateTimeOffset.UnixEpoch.AddYears(100),
        };
        return store.ApplySchemaAsync(new BaseSchemaProviderApplyRequest
        {
            VerifiedPlanEnvelope = JsonSerializer.SerializeToUtf8Bytes(envelope, HPDBaseJsonSerializerContext.Default.BaseSchemaProviderVerifiedEnvelope),
            ProviderApplyArtifact = plan.ProviderApplyArtifact,
            ExpectedGeneration = 0,
            ExpectedTargetChecksum = "target",
            LeaseTimeout = TimeSpan.FromSeconds(1),
            ApplyTimeout = TimeSpan.FromSeconds(1),
            CommitCompletionTimeout = TimeSpan.FromSeconds(1),
        });
    }

    private static async ValueTask ApplyEvolutionAsync(
        string path, string applicationId, CollectionDefinition collection, BaseSchemaOperationKind kind,
        long generation, string baseline, string target)
    {
        await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions
        {
            DataSource = path, StoreId = "sqlite", Collections = [collection],
        }, initializeSchema: false);
        BaseSchemaObservedState observed = (await store.InspectSchemaAsync(new BaseSchemaInspectionRequest
        {
            ApplicationId = applicationId, ExpectedLogicalChecksum = baseline, InspectionTimeout = TimeSpan.FromSeconds(5),
        })).Value!;
        BaseSchemaPreparedPlan prepared = (await store.PrepareSchemaPlanAsync(new BaseSchemaPreparationRequest
        {
            ApplicationId = applicationId,
            LogicalDelta = [new BaseSchemaLogicalOperation { Kind = kind, LogicalId = "i:items:item.by-title" }],
            ObservedState = observed, Classification = BaseSchemaPlanClassification.SafeStructural,
            ExpectedGeneration = generation, BaselineChecksum = baseline, TargetChecksum = target,
            PreparationTimeout = TimeSpan.FromSeconds(5),
        })).Value!;
        var envelope = new BaseSchemaProviderVerifiedEnvelope
        {
            PlanId = "plan-" + generation, TargetBaselineId = "baseline-" + (generation + 1), ApplicationId = applicationId,
            StoreId = "sqlite", PersistedStoreInstanceId = prepared.PersistedStoreInstanceId,
            ProviderId = prepared.ProviderId, ProviderVersion = prepared.ProviderVersion, PlannerVersion = prepared.PlannerVersion,
            Classification = BaseSchemaPlanClassification.SafeStructural, LogicalPlanDigest = "logical-" + generation,
            ProviderApplyArtifactDigest = prepared.ProviderApplyArtifactDigest,
            CreatedAt = DateTimeOffset.UnixEpoch, ExpiresAt = DateTimeOffset.UnixEpoch.AddYears(100),
        };
        OperationResult<BaseSchemaApplyResult> applied = await store.ApplySchemaAsync(new BaseSchemaProviderApplyRequest
        {
            VerifiedPlanEnvelope = JsonSerializer.SerializeToUtf8Bytes(envelope, HPDBaseJsonSerializerContext.Default.BaseSchemaProviderVerifiedEnvelope),
            ProviderApplyArtifact = prepared.ProviderApplyArtifact, ExpectedGeneration = generation,
            ExpectedBaselineChecksum = baseline, ExpectedTargetChecksum = target,
            LeaseTimeout = TimeSpan.FromSeconds(5), ApplyTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5),
        });
        applied.IsSuccess().Should().BeTrue(applied.Error?.Code + ":" + applied.Error?.Message);
        (await store.GetAsync(collection, new RecordId("seed"), OperationContext(BaseOperationKind.Get))).IsSuccess().Should().BeTrue();
    }

    private static CollectionDefinition IndexedCollection(BaseLogicalIndexDefinition[] indexes)
    {
        BaseScalarCodecAuthority codec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.String);
        var constraints = new BaseScalarConstraintSet { MaximumUtf8Bytes = 64 };
        return new CollectionDefinition
        {
            Id = "items", Name = "items", Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
            Fields = [new FieldDefinition
            {
                Id = "item.title", ApplicationName = "Title", WireName = "title", Type = BaseFieldTypes.String,
                Presence = BaseFieldPresence.Required, Nullability = BaseFieldNullability.NonNullable,
                ScalarKind = BaseScalarKind.String, ScalarCodec = codec, ScalarConstraints = constraints,
                ScalarConstraintChecksum = BaseGeneratedSchemaRegistration.ScalarConstraintChecksum(
                    "items", "item.title", BaseFieldPresence.Required, BaseFieldNullability.NonNullable, codec, constraints),
            }],
            Indexes = indexes,
        };
    }

    private static BaseLogicalIndexDefinition Index(bool unique, string? equalLiteral)
    {
        BaseIndexPredicateNode predicate = equalLiteral is null
            ? new BaseIndexPredicateNode { Id = BaseIndexPredicateId.Create("root"), Kind = BaseIndexPredicateNodeKind.True }
            : new BaseIndexPredicateNode
            {
                Id = BaseIndexPredicateId.Create("root"), Kind = BaseIndexPredicateNodeKind.Equal, FieldOrdinal = 0,
                Literal = new BaseCanonicalScalarLiteral
                {
                    Kind = BaseScalarKind.String,
                    Codec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.String),
                    CanonicalBytes = System.Text.Encoding.UTF8.GetBytes(equalLiteral).ToImmutableArray(),
                },
            };
        return new BaseLogicalIndexDefinition
        {
            Id = BaseLogicalIndexId.Create("item.by-title"), Version = 1, CollectionId = "items", Unique = unique, StoreRequired = true,
            Parts = [new BaseLogicalIndexPart { FieldOrdinal = 0, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue }],
            MembershipPredicate = new BaseIndexPredicateRegistry { Root = BaseIndexPredicateId.Create("root"), Nodes = [predicate], Checksum = default },
            Checksum = default,
        };
    }

    private static RecordPayload Payload(string title)
    {
        using JsonDocument document = JsonDocument.Parse($"{{\"title\":\"{title}\"}}");
        return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = document.RootElement.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value.Clone()) };
    }

    private static OperationContext OperationContext(BaseOperationKind kind) => new()
    {
        Operation = kind, CollectionId = "items", Now = DateTimeOffset.UnixEpoch,
    };
}
