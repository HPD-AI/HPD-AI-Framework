using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite;

public sealed partial class SqliteRecordStore : IBaseStudioEvidenceStore
{
    /// <inheritdoc />
    public BaseStudioEvidenceCapability EvidenceCapability { get; } = BaseStudioEvidenceContract.RecordMutationCapability();
    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseCapturedStudioEvidenceAuthority>> CaptureAuthorityAsync(
        BaseStudioEvidenceRequirement request, BaseOwnedScopeSeekAuthority scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(scope);
        if (!BaseStudioEvidenceContract.Valid(request) || scope.Kind != request.Scope.Kind || scope.ProtectedIndexDigest.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(scope.ProtectedIndexDigest.AsSpan(), request.ProtectedScopeSeekChecksum.AsSpan()))
            return Failure<BaseCapturedStudioEvidenceAuthority>("base.studio.authorityMismatch", ErrorCategory.Authorization);
        if (request.Kind != BaseStudioEvidenceKind.RecordMutation || request.Limits.MaximumItems > EvidenceCapability.MaximumItems ||
            request.Limits.MaximumRowsRead > EvidenceCapability.MaximumRowsRead || request.Limits.MaximumIntervals > EvidenceCapability.MaximumIntervals ||
            request.Limits.MaximumEvidenceBytes > EvidenceCapability.MaximumEvidenceBytes || request.Limits.MaximumTransientBytes > EvidenceCapability.MaximumTransientBytes)
            return OperationResults.Unsupported<BaseCapturedStudioEvidenceAuthority>(Error("base.studio.evidence.unsupported", ErrorCategory.Unsupported));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Limits.AcquisitionDeadline);
        await using SqliteConnection connection = await _connections.OpenAsync(deadline.Token).ConfigureAwait(false);
        long restore;
        await using (SqliteCommand epoch = connection.CreateCommand())
        {
            epoch.CommandText = $"SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key='restore_epoch'),0);";
            restore = Convert.ToInt64(await epoch.ExecuteScalarAsync(deadline.Token).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        long generation;
        await using (SqliteCommand high = connection.CreateCommand())
        {
            high.CommandText = $"SELECT COALESCE(MAX(position),0) FROM {_names.MutationJournal};";
            generation = Convert.ToInt64(await high.ExecuteScalarAsync(deadline.Token).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        var receipt = new BaseStudioEvidenceCaptureReceipt { ApplicationId = new string(request.ApplicationId.AsSpan()), Kind = request.Kind,
            StoreIdentity = new string(_options.StoreId.AsSpan()), RestoreEpoch = restore, IndexGeneration = generation,
            LogicalAccessPathId = BaseStudioEvidenceContract.RecordMutationPath, ProtectedScopeSeekChecksum = [.. request.ProtectedScopeSeekChecksum],
            AuthorityChecksum = BaseStudioEvidenceContract.AuthorityChecksum(request, _options.StoreId, restore, generation, BaseStudioEvidenceContract.RecordMutationPath) };
        return OperationResults.Ok<BaseCapturedStudioEvidenceAuthority>(new Authority(this, BaseStudioEvidenceContract.Freeze(request), receipt));
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<IBaseStudioEvidenceSession>> OpenSessionAsync(BaseCapturedStudioEvidenceAuthority authority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is not Authority captured || !ReferenceEquals(captured.Owner, this) || !captured.TryOpen())
            return Failure<IBaseStudioEvidenceSession>("base.studio.authorityMismatch", ErrorCategory.Authorization);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(captured.Request.Limits.SessionDeadline);
        SqliteConnection? connection = null; SqliteTransaction? transaction = null;
        try
        {
            connection = await _connections.OpenAsync(deadline.Token).ConfigureAwait(false);
            transaction = (SqliteTransaction)await connection.BeginTransactionAsync(deadline.Token).ConfigureAwait(false);
            await using SqliteCommand pin = connection.CreateCommand(); pin.Transaction = transaction;
            pin.CommandText = $"SELECT COALESCE(MAX(position),0) FROM {_names.MutationJournal};";
            _ = await pin.ExecuteScalarAsync(deadline.Token).ConfigureAwait(false);
            return OperationResults.Ok<IBaseStudioEvidenceSession>(new Session(this, captured, connection, transaction));
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false);
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Authority(SqliteRecordStore owner, BaseStudioEvidenceRequirement request, BaseStudioEvidenceCaptureReceipt receipt)
        : BaseCapturedStudioEvidenceAuthority(receipt)
    {
        private int _opened;
        internal SqliteRecordStore Owner { get; } = owner;
        internal BaseStudioEvidenceRequirement Request { get; } = request;
        internal bool TryOpen() => Interlocked.CompareExchange(ref _opened, 1, 0) == 0;
    }

    private sealed class Session(SqliteRecordStore owner, Authority authority, SqliteConnection connection, SqliteTransaction transaction) : IBaseStudioEvidenceSession
    {
        private int _disposed;
        private readonly CancellationTokenSource _lifetime = new(authority.Request.Limits.SessionDeadline);
        public async ValueTask<OperationResult<BaseStudioEvidencePage>> ReadPageAsync(BaseStudioEvidencePageRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (Volatile.Read(ref _disposed) != 0 || request.Take < 1 || request.Take > authority.Request.Limits.MaximumItems ||
                !BaseStudioEvidenceContract.Position(request.After, out long after))
                return Failure<BaseStudioEvidencePage>("base.studio.evidence.authorityMismatch", ErrorCategory.Authorization);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token); deadline.CancelAfter(authority.Request.Limits.PageDeadline);
            long restore;
            await using (SqliteCommand epoch = connection.CreateCommand())
            {
                epoch.Transaction = transaction;
                epoch.CommandText = $"SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM {owner._names.ProviderState} WHERE key='restore_epoch'),0);";
                restore = Convert.ToInt64(await epoch.ExecuteScalarAsync(deadline.Token).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
            if (restore != authority.Receipt.RestoreEpoch)
                return Failure<BaseStudioEvidencePage>("base.studio.authorityMismatch", ErrorCategory.Authorization);
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            bool isRecord = authority.Request.Parent is BaseStudioRecordEvidenceSubject;
            string parent = isRecord ? " AND record_id=$record" : "";
            string accessIndex = isRecord ? $" INDEXED BY {owner._names.MutationJournalScopeIndex}" : string.Empty;
            string scoped = authority.Request.Scope.Kind == BaseSubjectScopeKind.Global ? " AND tenant_id IS NULL" : " AND tenant_id=$scopeValue";
            command.CommandText = $"SELECT position,event_id,occurred_at,operation,collection_id,record_id FROM {owner._names.MutationJournal}{accessIndex} " +
                $"WHERE entry_kind=0 AND position>$after AND position<=$through AND collection_id=$collection{scoped}{parent} ORDER BY position LIMIT $limit;";
            command.Parameters.AddWithValue("$after", after); command.Parameters.AddWithValue("$through", authority.Receipt.IndexGeneration);
            command.Parameters.AddWithValue("$collection", Collection(authority.Request.Parent));
            if (authority.Request.Scope.Kind != BaseSubjectScopeKind.Global) command.Parameters.AddWithValue("$scopeValue", authority.Request.Scope.Value!);
            if (authority.Request.Parent is BaseStudioRecordEvidenceSubject record) command.Parameters.AddWithValue("$record", record.RecordId.Value);
            command.Parameters.AddWithValue("$limit", checked(request.Take + 1));
            var items = new List<BaseStudioEvidenceItem>(request.Take + 1);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(deadline.Token).ConfigureAwait(false);
            while (await reader.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                long position = reader.GetInt64(0); string eventId = reader.IsDBNull(1) ? "" : reader.GetString(1);
                DateTimeOffset observed = reader.IsDBNull(2) ? DateTimeOffset.UnixEpoch : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                int operation = reader.IsDBNull(3) ? -1 : reader.GetInt32(3); string collection = reader.GetString(4); string recordId = reader.GetString(5);
                var item = new BaseStudioRecordMutationEvidenceItem { Kind = BaseStudioEvidenceKind.RecordMutation,
                    OrderingTuple = BaseStudioEvidenceContract.Tuple(position), ObservedAtUtc = observed.ToUniversalTime(),
                    SemanticKind = Enum.IsDefined(typeof(BaseOperationKind), operation) ? Semantic((BaseOperationKind)operation) : BaseStudioEvidenceSemanticKind.Transition,
                    CollectionId = collection, RecordId = RecordId.Create(recordId), Revision = null, EvidenceId = eventId, EvidenceChecksum = [] };
                items.Add(item with { EvidenceChecksum = BaseStudioEvidenceContract.ItemChecksum(item) });
            }
            long rowsRead = items.Count; if (rowsRead > authority.Request.Limits.MaximumRowsRead)
                return Failure<BaseStudioEvidencePage>("base.studio.evidence.budgetExceeded", ErrorCategory.Validation);
            bool more = items.Count > request.Take; if (more) items.RemoveAt(items.Count - 1);
            ImmutableArray<BaseStudioEvidenceItem> frozen = [.. items]; long bytes = frozen.Sum(BaseStudioEvidenceContract.Measure);
            if (bytes > authority.Request.Limits.MaximumEvidenceBytes || bytes > authority.Request.Limits.MaximumTransientBytes)
                return Failure<BaseStudioEvidencePage>("base.studio.evidence.budgetExceeded", ErrorCategory.Validation);
            BaseStudioEvidenceBoundary? next = more && frozen.Length > 0 ? BaseStudioEvidenceContract.Boundary(authority.Request.Kind, frozen[^1].OrderingTuple) : null;
            ImmutableArray<byte> lower = request.After?.CanonicalTuple ?? BaseStudioEvidenceContract.Tuple(after);
            ImmutableArray<byte> upper = BaseStudioEvidenceContract.Tuple(authority.Receipt.IndexGeneration == long.MaxValue ? long.MaxValue : authority.Receipt.IndexGeneration + 1);
            ImmutableArray<BaseStudioEvidenceReadInterval> intervals = [new() { LogicalAccessPathId = BaseStudioEvidenceContract.RecordMutationPath,
                ProtectedScopeSeekChecksum = [.. authority.Request.ProtectedScopeSeekChecksum],
                LowerInclusive = lower, UpperExclusive = upper,
                Checksum = BaseStudioEvidenceContract.IntervalChecksum(BaseStudioEvidenceContract.RecordMutationPath, authority.Request.ProtectedScopeSeekChecksum, lower, upper) }];
            var accounting = new BaseStudioEvidenceProviderAccounting { RowsRead = rowsRead, Intervals = 1, EvidenceBytes = bytes, TransientBytes = bytes };
            var page = new BaseStudioEvidencePage { Items = frozen, Next = next, IndexGeneration = authority.Receipt.IndexGeneration, Intervals = intervals,
                Accounting = accounting, PageChecksum = BaseStudioEvidenceContract.PageChecksum(frozen, authority.Receipt.IndexGeneration, next, intervals, accounting) };
            return OperationResults.Ok(page);
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _lifetime.Dispose(); await transaction.DisposeAsync().ConfigureAwait(false); await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string Collection(BaseStudioEvidenceSubject subject) => subject switch
    { BaseStudioCollectionEvidenceSubject value => value.CollectionId, BaseStudioRecordEvidenceSubject value => value.CollectionId, _ => throw new InvalidOperationException() };
    private static BaseError Error(string code, ErrorCategory category) => new() { Code = code, Message = "The durable evidence operation could not be completed.", Category = category };
    private static OperationResult<T> Failure<T>(string code, ErrorCategory category) => category switch
    { ErrorCategory.Authorization => OperationResults.PolicyDenied<T>(Error(code, category)), ErrorCategory.Store => OperationResults.StoreError<T>(Error(code, category)), _ => OperationResults.ValidationFailed<T>(Error(code, category)) };
    private static BaseStudioEvidenceSemanticKind Semantic(BaseOperationKind value) => value switch
    { BaseOperationKind.Create => BaseStudioEvidenceSemanticKind.Created, BaseOperationKind.Patch => BaseStudioEvidenceSemanticKind.Patched,
      BaseOperationKind.Replace => BaseStudioEvidenceSemanticKind.Replaced, BaseOperationKind.Delete => BaseStudioEvidenceSemanticKind.Deleted,
      _ => BaseStudioEvidenceSemanticKind.Transition };
}
