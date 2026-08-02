using System.Text.Json;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteSchemaApplyAdversarialTests
{
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
}
