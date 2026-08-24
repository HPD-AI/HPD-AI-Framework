using System.Collections.Immutable;
using System.Globalization;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore : IBaseStudioInfrastructureInventoryStore
{
    internal async ValueTask PublishInfrastructureMaintenanceAsync(string maintenanceKind, string operationIdentity,
        BaseStudioInfrastructureState state, int progressBasisPoints, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await PublishInfrastructureAsync(connection, null, BaseStudioInfrastructureInventoryKind.Maintenance, state,
            maintenanceKind, operationIdentity, null, progressBasisPoints, 0, cancellationToken).ConfigureAwait(false);
    }

    internal ValueTask PublishInfrastructureMaintenanceAsync(SqliteConnection connection, SqliteTransaction transaction,
        string maintenanceKind, string operationIdentity, BaseStudioInfrastructureState state, int progressBasisPoints,
        CancellationToken cancellationToken) => PublishInfrastructureAsync(connection, transaction,
            BaseStudioInfrastructureInventoryKind.Maintenance, state, maintenanceKind, operationIdentity, null,
            progressBasisPoints, 0, cancellationToken);

    private async ValueTask PublishInfrastructureAsync(SqliteConnection connection, SqliteTransaction? transaction,
        BaseStudioInfrastructureInventoryKind kind, BaseStudioInfrastructureState state, string identity,
        string? secondaryIdentity, byte[]? checksum, long numberA, long numberB, CancellationToken cancellationToken)
    {
        long restore = await InfrastructureRestoreEpochAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {_names.StudioInfrastructureInventory}(kind,restore_epoch,schema_generation,observed_at,state,identity,secondary_identity,checksum_a,number_a,number_b,flag_a) VALUES($kind,$restore,$schema,$observed,$state,$identity,$secondary,$checksum,$a,$b,0);";
        command.Parameters.AddWithValue("$kind", (int)kind); command.Parameters.AddWithValue("$restore", restore);
        command.Parameters.AddWithValue("$schema", Volatile.Read(ref _schemaGeneration));
        command.Parameters.AddWithValue("$observed", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$state", (int)state); command.Parameters.AddWithValue("$identity", identity);
        command.Parameters.AddWithValue("$secondary", secondaryIdentity is null ? DBNull.Value : secondaryIdentity);
        command.Parameters.Add("$checksum", SqliteType.Blob).Value = checksum is null ? DBNull.Value : checksum;
        command.Parameters.AddWithValue("$a", numberA); command.Parameters.AddWithValue("$b", numberB);
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PublishBackupCompletedAsync(SqliteConnection connection, string requestIdentity, BaseBackupManifest manifest,
        CancellationToken cancellationToken)
    {
        byte[] digest = Convert.FromHexString(manifest.ProviderPayloadSha256);
        await PublishInfrastructureAsync(connection, null, BaseStudioInfrastructureInventoryKind.Backup,
            BaseStudioInfrastructureState.Completed, requestIdentity, null, digest,
            manifest.ProviderPayloadLength, 0, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PublishBackupAttemptAsync(string requestIdentity, BaseStudioInfrastructureState state,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await PublishInfrastructureAsync(connection, null, BaseStudioInfrastructureInventoryKind.Backup, state,
            requestIdentity, null, new byte[32], 0, 0, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PublishRestoreAttemptAsync(string requestIdentity, BaseStudioInfrastructureState state,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await PublishInfrastructureAsync(connection, null, BaseStudioInfrastructureInventoryKind.Restore, state,
            requestIdentity, null, new byte[32], 0, 0, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PublishRestoreCompletedAsync(SqliteConnection connection, string requestIdentity, BaseBackupManifest manifest,
        long resultRestoreEpoch, CancellationToken cancellationToken)
    {
        byte[] digest = Convert.FromHexString(manifest.ProviderPayloadSha256);
        await PublishInfrastructureAsync(connection, null, BaseStudioInfrastructureInventoryKind.Restore,
            BaseStudioInfrastructureState.Completed, requestIdentity, null, digest,
            resultRestoreEpoch, 0, cancellationToken).ConfigureAwait(false);
    }
    /// <inheritdoc />
    public BaseStudioInfrastructureInventoryCapability InfrastructureInventoryCapability { get; } =
        BaseStudioInfrastructureInventoryContract.Capability(durable: true);

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseCapturedStudioInfrastructureAuthority>> CaptureInfrastructureAuthorityAsync(
        BaseStudioInfrastructureInventoryRequirement request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!BaseStudioInfrastructureInventoryContract.Valid(request, InfrastructureInventoryCapability) ||
            !StringComparer.Ordinal.Equals(request.StoreId, _options.StoreId) || !StringComparer.Ordinal.Equals(request.StoreInstanceId, _options.StoreId))
            return InfrastructureFailure<BaseCapturedStudioInfrastructureAuthority>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Limits.AcquisitionDeadline);
        await using SqliteConnection connection = await _connections.OpenAsync(deadline.Token).ConfigureAwait(false);
        long restore = await InfrastructureRestoreEpochAsync(connection, null, deadline.Token).ConfigureAwait(false);
        long schema = Volatile.Read(ref _schemaGeneration);
        if (restore != request.RestoreEpoch || schema != request.SchemaGeneration)
            return InfrastructureFailure<BaseCapturedStudioInfrastructureAuthority>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization);
        await EnsureInfrastructureSchemaFactAsync(connection, restore, schema, deadline.Token).ConfigureAwait(false);
        long generation;
        await using (SqliteCommand high = connection.CreateCommand())
        { high.CommandText = $"SELECT COALESCE(MAX(sequence),0) FROM {_names.StudioInfrastructureInventory};"; generation = Convert.ToInt64(await high.ExecuteScalarAsync(deadline.Token).ConfigureAwait(false), CultureInfo.InvariantCulture); }
        string path = BaseStudioInfrastructureInventoryContract.Path(request.Kind);
        var accounting = new BaseStudioInfrastructureProviderAccounting { RowsRead = 2, EvidenceBytes = 64, TransientBytes = 64 };
        var receipt = new BaseStudioInfrastructureCaptureReceipt { ApplicationId = new(request.ApplicationId.AsSpan()), StoreId = new(_options.StoreId.AsSpan()), Kind = request.Kind,
            StoreInstanceId = new(_options.StoreId.AsSpan()), RestoreEpoch = restore, SchemaGeneration = schema, InventoryGeneration = generation,
            LogicalAccessPathId = path, Accounting = accounting, AuthorityChecksum = BaseStudioInfrastructureInventoryContract.AuthorityChecksum(request, generation, path) };
        return OperationResults.Ok<BaseCapturedStudioInfrastructureAuthority>(new InfrastructureAuthority(this, request with
        { ApplicationId = new(request.ApplicationId.AsSpan()), StoreId = new(request.StoreId.AsSpan()), StoreInstanceId = new(request.StoreInstanceId.AsSpan()), Limits = request.Limits with { } }, receipt));
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<IBaseStudioInfrastructureInventorySession>> OpenInfrastructureSessionAsync(
        BaseCapturedStudioInfrastructureAuthority authority, CancellationToken cancellationToken = default)
    {
        if (authority is not InfrastructureAuthority captured || !ReferenceEquals(captured.Owner, this) || !captured.TryOpen())
            return InfrastructureFailure<IBaseStudioInfrastructureInventorySession>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(captured.Requirement.Limits.SessionDeadline);
        SqliteConnection? connection = null; SqliteTransaction? transaction = null;
        try
        {
            connection = await _connections.OpenAsync(deadline.Token).ConfigureAwait(false);
            transaction = (SqliteTransaction)await connection.BeginTransactionAsync(deadline.Token).ConfigureAwait(false);
            long restore = await InfrastructureRestoreEpochAsync(connection, transaction, deadline.Token).ConfigureAwait(false);
            if (restore != captured.Receipt.RestoreEpoch || Volatile.Read(ref _schemaGeneration) != captured.Receipt.SchemaGeneration)
            { await transaction.DisposeAsync().ConfigureAwait(false); await connection.DisposeAsync().ConfigureAwait(false); return InfrastructureFailure<IBaseStudioInfrastructureInventorySession>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization); }
            return OperationResults.Ok<IBaseStudioInfrastructureInventorySession>(new InfrastructureSession(this, captured, connection, transaction));
        }
        catch { if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false); if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async ValueTask EnsureInfrastructureSchemaFactAsync(SqliteConnection connection, long restore, long schema, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = $"""
INSERT INTO {_names.StudioInfrastructureInventory}
(kind,restore_epoch,schema_generation,observed_at,state,identity,secondary_identity,checksum_a,number_a,number_b,flag_a)
SELECT 1,$restore,$schema,'1970-01-01T00:00:00.0000000Z',3,'sqlite.current',NULL,$checksum,0,0,0
WHERE NOT EXISTS (SELECT 1 FROM {_names.StudioInfrastructureInventory} WHERE kind=1 AND restore_epoch=$restore AND schema_generation=$schema);
""";
        command.Parameters.AddWithValue("$restore", restore); command.Parameters.AddWithValue("$schema", schema);
        command.Parameters.Add("$checksum", SqliteType.Blob).Value = BaseStudioInfrastructureInventoryContract.Hash(writer => { writer.Write("sqlite.schema.v1"); writer.Write(schema); }).ToArray();
        _ = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);

        var history = new List<(long Generation, string AppliedAt, int Outcome, string PlanId, byte[] Checksum)>();
        await using (SqliteCommand migrations = connection.CreateCommand())
        {
            migrations.CommandText = $"""
SELECT generation,applied_at,outcome,plan_id,checksum FROM {_names.SchemaHistory} history
WHERE NOT EXISTS (
  SELECT 1 FROM {_names.StudioInfrastructureInventory} inventory
  WHERE inventory.kind=2 AND inventory.restore_epoch=$restore AND inventory.identity=history.plan_id
)
ORDER BY generation;
""";
            migrations.Parameters.AddWithValue("$restore", restore);
            await using SqliteDataReader reader = await migrations.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                string checksum = reader.GetString(4);
                if (checksum.Length != 64 || checksum.Any(static value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                    throw new InvalidOperationException("The persisted schema-history checksum is invalid.");
                history.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), Convert.FromHexString(checksum)));
            }
        }
        foreach (var item in history)
        {
            await using SqliteCommand insert = connection.CreateCommand(); insert.CommandText = $"""
INSERT INTO {_names.StudioInfrastructureInventory}
(kind,restore_epoch,schema_generation,observed_at,state,identity,secondary_identity,checksum_a,number_a,number_b,flag_a)
SELECT 2,$restore,$generation,$applied,$state,$plan,NULL,$checksum,$from,$generation,0
WHERE NOT EXISTS (
  SELECT 1 FROM {_names.StudioInfrastructureInventory}
  WHERE kind=2 AND restore_epoch=$restore AND identity=$plan
);
""";
            insert.Parameters.AddWithValue("$restore", restore); insert.Parameters.AddWithValue("$generation", item.Generation);
            insert.Parameters.AddWithValue("$applied", item.AppliedAt); insert.Parameters.AddWithValue("$state", item.Outcome is 0 or 1 ? 3 : item.Outcome == 4 ? 6 : 4);
            insert.Parameters.AddWithValue("$plan", item.PlanId); insert.Parameters.Add("$checksum", SqliteType.Blob).Value = item.Checksum;
            insert.Parameters.AddWithValue("$from", item.Generation > 0 ? item.Generation - 1 : 0);
            _ = await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    private async ValueTask<long> InfrastructureRestoreEpochAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken token)
    { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = $"SELECT COALESCE(CAST(value AS INTEGER),0) FROM {_names.ProviderState} WHERE key='restore_epoch';";
      return Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture); }

    private sealed class InfrastructureAuthority(SqliteRecordStore owner, BaseStudioInfrastructureInventoryRequirement requirement,
        BaseStudioInfrastructureCaptureReceipt receipt) : BaseCapturedStudioInfrastructureAuthority(receipt)
    { private int _opened; internal SqliteRecordStore Owner { get; } = owner; internal BaseStudioInfrastructureInventoryRequirement Requirement { get; } = requirement;
      internal bool TryOpen() => Interlocked.CompareExchange(ref _opened, 1, 0) == 0; }

    private sealed class InfrastructureSession(SqliteRecordStore owner, InfrastructureAuthority authority, SqliteConnection connection, SqliteTransaction transaction)
        : IBaseStudioInfrastructureInventorySession
    {
        private int _disposed; private readonly CancellationTokenSource _lifetime = new(authority.Requirement.Limits.SessionDeadline);
        public async ValueTask<OperationResult<BaseStudioInfrastructurePage>> ReadPageAsync(BaseStudioInfrastructurePageRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (Volatile.Read(ref _disposed) != 0 || request.Take < 1 || request.Take > authority.Requirement.Limits.MaximumItems ||
                !BaseStudioInfrastructureInventoryContract.Position(authority.Requirement.Kind, request.After, out long after))
                return InfrastructureFailure<BaseStudioInfrastructurePage>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token); deadline.CancelAfter(authority.Requirement.Limits.PageDeadline);
            if (await owner.InfrastructureRestoreEpochAsync(connection, transaction, deadline.Token).ConfigureAwait(false) != authority.Receipt.RestoreEpoch ||
                Volatile.Read(ref owner._schemaGeneration) != authority.Receipt.SchemaGeneration)
                return InfrastructureFailure<BaseStudioInfrastructurePage>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization);
            await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = $"SELECT sequence,restore_epoch,schema_generation,observed_at,state,identity,secondary_identity,checksum_a,number_a,number_b,flag_a " +
                $"FROM {owner._names.StudioInfrastructureInventory} INDEXED BY {owner._names.StudioInfrastructureKindSequenceIndex} WHERE kind=$kind AND sequence>$after AND sequence<=$through ORDER BY sequence LIMIT $take;";
            command.Parameters.AddWithValue("$kind", (int)authority.Requirement.Kind); command.Parameters.AddWithValue("$after", after);
            command.Parameters.AddWithValue("$through", authority.Receipt.InventoryGeneration); command.Parameters.AddWithValue("$take", checked(request.Take + 1));
            var values = new List<BaseStudioInfrastructureItem>(request.Take + 1); await using SqliteDataReader reader = await command.ExecuteReaderAsync(deadline.Token).ConfigureAwait(false);
            while (await reader.ReadAsync(deadline.Token).ConfigureAwait(false)) values.Add(Item(authority.Requirement.Kind, owner._options.StoreId, reader));
            long rowsRead = values.Count; if (rowsRead > authority.Requirement.Limits.MaximumRowsRead)
                return InfrastructureFailure<BaseStudioInfrastructurePage>("base.studio.infrastructure.budgetExceeded", ErrorCategory.Validation);
            bool more = values.Count > request.Take; if (more) values.RemoveAt(values.Count - 1); ImmutableArray<BaseStudioInfrastructureItem> items = [.. values];
            long bytes = items.Sum(BaseStudioInfrastructureInventoryContract.Measure); if (bytes > authority.Requirement.Limits.MaximumEvidenceBytes || bytes > authority.Requirement.Limits.MaximumTransientBytes)
                return InfrastructureFailure<BaseStudioInfrastructurePage>("base.studio.infrastructure.budgetExceeded", ErrorCategory.Validation);
            BaseStudioInfrastructureBoundary? next = more && items.Length > 0 ? BaseStudioInfrastructureInventoryContract.Boundary(authority.Requirement.Kind, items[^1].Sequence) : null;
            var accounting = new BaseStudioInfrastructureProviderAccounting { RowsRead = rowsRead, EvidenceBytes = bytes, TransientBytes = bytes };
            return OperationResults.Ok(new BaseStudioInfrastructurePage { Items = items, Next = next, InventoryGeneration = authority.Receipt.InventoryGeneration,
                Accounting = accounting, PageChecksum = BaseStudioInfrastructureInventoryContract.PageChecksum(items, authority.Receipt.InventoryGeneration, next, accounting) });
        }
        public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _lifetime.Dispose(); await transaction.DisposeAsync().ConfigureAwait(false); await connection.DisposeAsync().ConfigureAwait(false); }
    }

    private static BaseStudioInfrastructureItem Item(BaseStudioInfrastructureInventoryKind kind, string store, SqliteDataReader reader)
    {
        long sequence = reader.GetInt64(0), restore = reader.GetInt64(1), schema = reader.GetInt64(2); DateTimeOffset observed = DateTimeOffset.ParseExact(reader.GetString(3),
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var state = (BaseStudioInfrastructureState)reader.GetInt32(4); string identity = reader.GetString(5); string? secondary = reader.IsDBNull(6) ? null : reader.GetString(6);
        ImmutableArray<byte> checksum = reader.IsDBNull(7) ? [] : [.. (byte[])reader[7]]; long a = reader.GetInt64(8), b = reader.GetInt64(9); bool flag = reader.GetInt64(10) != 0;
        BaseStudioInfrastructureItem value = kind switch
        { BaseStudioInfrastructureInventoryKind.SchemaGeneration => new BaseStudioSchemaGenerationItem { Kind=kind,Sequence=sequence,StoreId=store,RestoreEpoch=restore,SchemaGeneration=schema,ObservedAtUtc=observed,State=state,BaselineId=identity,SchemaChecksum=checksum,DriftDetected=flag,Checksum=[] },
          BaseStudioInfrastructureInventoryKind.Migration => new BaseStudioMigrationItem { Kind=kind,Sequence=sequence,StoreId=store,RestoreEpoch=restore,SchemaGeneration=schema,ObservedAtUtc=observed,State=state,MigrationId=identity,FromSchemaGeneration=a,ToSchemaGeneration=b,PlanChecksum=checksum,Checksum=[] },
          BaseStudioInfrastructureInventoryKind.Backup => new BaseStudioBackupItem { Kind=kind,Sequence=sequence,StoreId=store,RestoreEpoch=restore,SchemaGeneration=schema,ObservedAtUtc=observed,State=state,ArtifactId=identity,ArtifactDigest=checksum,ArtifactBytes=a,Checksum=[] },
          BaseStudioInfrastructureInventoryKind.Restore => new BaseStudioRestoreItem { Kind=kind,Sequence=sequence,StoreId=store,RestoreEpoch=restore,SchemaGeneration=schema,ObservedAtUtc=observed,State=state,RestoreRequestIdentity=identity,ArtifactDigest=checksum,ResultRestoreEpoch=a,Checksum=[] },
          BaseStudioInfrastructureInventoryKind.Maintenance => new BaseStudioMaintenanceItem { Kind=kind,Sequence=sequence,StoreId=store,RestoreEpoch=restore,SchemaGeneration=schema,ObservedAtUtc=observed,State=state,MaintenanceKind=identity,OperationIdentity=secondary ?? "unknown",ProgressBasisPoints=checked((int)a),Checksum=[] },
          _ => throw new InvalidDataException("Infrastructure inventory kind is corrupt.") };
        return value with { Checksum = BaseStudioInfrastructureInventoryContract.ItemChecksum(value) };
    }

    private static BaseError InfrastructureError(string code, ErrorCategory category) => new() { Code=code,Message="The infrastructure inventory operation could not be completed.",Category=category };
    private static OperationResult<T> InfrastructureFailure<T>(string code, ErrorCategory category) => category == ErrorCategory.Authorization
        ? OperationResults.PolicyDenied<T>(InfrastructureError(code, category)) : OperationResults.ValidationFailed<T>(InfrastructureError(code, category));
}
