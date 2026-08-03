using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;

namespace HPD.Base.Sqlite;

/// <summary>Durable SQLite implementation of the HPD.BASE record store contract.</summary>
public sealed partial class SqliteRecordStore :
    IRecordMutationStore,
    IAtomicRecordStore,
    ITransactionalMutationJournalStore,
    IRelationalReadStore,
    IConsistentRecordIncludeStore,
    IBaseSchemaStore,
    IRecordStoreAdministration,
    IAsyncDisposable
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqliteConnectionFactory _connections;
    private readonly SqliteSchemaInitializer _schema;
    private readonly SqliteNames _names;
    private readonly SqlitePhysicalModel _physical;
    private readonly BaseQueryCursorCodec? _queryCursors;
    private readonly BaseOpaqueTokenProtector? _tokenProtector;
    private readonly ILogger<SqliteRecordStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ISqliteTransactionController _transactions;
    private readonly ISqliteSessionOperationController _sessionOperations;
    private readonly ISqliteTransactionResourceDisposer _transactionResourceDisposer;
    private readonly ISqliteSchemaCommandController _schemaCommands;
    private readonly ISqliteAdministrationOperationController _administrationOperations;
    private readonly SemaphoreSlim _keepAliveGate = new(1, 1);
    private readonly SemaphoreSlim _mutationExecutionSlots;
    private readonly SemaphoreSlim _administrationExecutionSlots;
    private readonly ConcurrentDictionary<long, Task> _quarantinedAdministration = new();
    private readonly SqliteSchemaGenerationGate _schemaGenerationGate = new();
    private readonly ConcurrentDictionary<long, QuarantinedMutation> _quarantinedMutations = new();
    private SqliteConnection? _keepAliveConnection;
    private long _nextQuarantinedMutationId;
    private long _nextQuarantinedAdministrationId;
    private long _schemaGeneration;
    private int _restoreInstallationActive;
    private int _disposed;

    /// <summary>
    /// Initializes a SQLite record store with the supplied options and host-owned logger factory.
    /// </summary>
    /// <param name="options">SQLite provider options.</param>
    /// <param name="loggerFactory">The host-owned logger factory.</param>
    public SqliteRecordStore(
        HPDBaseSqliteOptions options,
        ILoggerFactory loggerFactory)
        : this(options, loggerFactory, TimeProvider.System)
    {
    }

    internal SqliteRecordStore(
        HPDBaseSqliteOptions options,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        ISqliteTransactionController? transactions = null,
        ISqliteSessionOperationController? sessionOperations = null,
        ISqliteTransactionResourceDisposer? transactionResourceDisposer = null,
        ISqliteSchemaCommandController? schemaCommands = null,
        ISqliteAdministrationOperationController? administrationOperations = null,
        BaseOpaqueTokenProtector? tokenProtector = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        ValidateOptions(_options);
        _logger = loggerFactory.CreateLogger<SqliteRecordStore>();
        _timeProvider = timeProvider;
        _transactions = transactions ?? DefaultSqliteTransactionController.Instance;
        _sessionOperations =
            sessionOperations ?? DefaultSqliteSessionOperationController.Instance;
        _transactionResourceDisposer =
            transactionResourceDisposer ?? DefaultSqliteTransactionResourceDisposer.Instance;
        _schemaCommands = schemaCommands ?? DefaultSqliteSchemaCommandController.Instance;
        _administrationOperations = administrationOperations ?? DefaultSqliteAdministrationOperationController.Instance;
        _connections = new SqliteConnectionFactory(_options);
        RecoverRestoreMarkerIfPresent();
        _schema = new SqliteSchemaInitializer(_options);
        _names = new SqliteNames(_options);
        _physical = new SqlitePhysicalModel(_options);
        _queryCursors = tokenProtector is null ? null : new BaseQueryCursorCodec(tokenProtector, timeProvider);
        _tokenProtector = tokenProtector;
        Includes = new RecordIncludeExecutionCapability
        {
            Supported = true, MaxDepth = 3, MaxIncludes = 8,
            MaxRecords = Math.Min(1_000, _options.MaxPageSize), SnapshotConsistency = true,
        };
        _mutationExecutionSlots = new SemaphoreSlim(
            _options.MaxTrackedMutationExecutions,
            _options.MaxTrackedMutationExecutions);
        _administrationExecutionSlots = new SemaphoreSlim(
            _options.MaxQuarantinedAdministrationExecutions,
            _options.MaxQuarantinedAdministrationExecutions);
        bool administration = _options.AdministrationEnabled && _tokenProtector is not null && IsFileBacked(_options);
        AdministrationCapability = new BaseAdministrationCapability
        {
            Backup = administration,
            Validate = administration,
            Restore = administration,
            AdministrativePurge = true,
            OnlineBackup = administration,
            WritersBlockedDuringBackup = true,
            ReadersBlockedDuringBackup = true,
            RestoreRequiresExclusiveMaintenance = true,
            Durable = true,
            MaxArtifactBytes = administration ? _options.MaxBackupArtifactBytes : 0,
        };
        Capabilities = CreateCapabilities(_options, _queryCursors is not null, AdministrationCapability);
    }

    /// <inheritdoc />
    public BaseAdministrationCapability AdministrationCapability { get; }

    internal int QuarantinedMutationCount => _quarantinedMutations.Count;
    internal int QuarantinedAdministrationCount => _quarantinedAdministration.Count;
    internal bool RestoreRecoveryPending => IsFileBacked(_options) && File.Exists(RestoreMarkerPath());

    /// <inheritdoc />
    public StoreCapabilityDescriptor Capabilities { get; }

    /// <inheritdoc />
    public async ValueTask<BaseMutationJournalBounds> GetMutationJournalBoundsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var generationLease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await GetBoundsAsync(
            connection,
            MutationJournalCutoff(),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SqliteCursorState> ReadCursorStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string collectionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT next_append_position, purge_generation, COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key = 'restore_epoch'), 0) FROM {_names.Collections} WHERE collection_id = $collection;";
        command.CommandTimeout = TimeoutSeconds();
        command.Parameters.AddWithValue("$collection", collectionId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("SQLite collection cursor state is unavailable.");
        return new SqliteCursorState(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static BaseQueryCursorKey[] SqliteCursorKeys(RecordEnvelope record, QuerySort[] sort) =>
        sort.Select(item => SqliteCursorKey(record, item.Field)).ToArray();

    private static BaseQueryCursorKey SqliteCursorKey(RecordEnvelope record, string field)
    {
        if (field == "id") return new BaseQueryCursorKey(true, JsonString(record.Id.Value));
        if (field == "revision") return new BaseQueryCursorKey(true, record.Metadata.Revision?.Value ?? "0");
        if (field == "createdAt") return new BaseQueryCursorKey(true, JsonString(record.Metadata.CreatedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? ""));
        if (field == "updatedAt") return new BaseQueryCursorKey(true, JsonString(record.Metadata.UpdatedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? ""));
        Dictionary<string, JsonElement> fields = SqliteRecordSerializer.NormalizeObjectPayload(record.Payload).Fields ?? [];
        return fields.TryGetValue(field, out JsonElement value)
            ? new BaseQueryCursorKey(true, value.GetRawText())
            : new BaseQueryCursorKey(false, "null");
    }

    private static string JsonString(string value) =>
        "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    private static string QueryCursorErrorCode(BaseQueryCursorStatus status) => status switch
    {
        BaseQueryCursorStatus.ScopeMismatch => BaseQueryErrorCodes.CursorScopeMismatch,
        BaseQueryCursorStatus.QueryMismatch => BaseQueryErrorCodes.CursorQueryMismatch,
        BaseQueryCursorStatus.Expired => BaseQueryErrorCodes.CursorExpired,
        BaseQueryCursorStatus.VersionUnsupported => BaseQueryErrorCodes.CursorVersionUnsupported,
        BaseQueryCursorStatus.SchemaChanged => BaseQueryErrorCodes.CursorSchemaChanged,
        BaseQueryCursorStatus.RestoreInvalidated => BaseQueryErrorCodes.CursorRestoreInvalidated,
        BaseQueryCursorStatus.GuaranteeUnavailable => BaseQueryErrorCodes.CursorGuaranteeUnavailable,
        BaseQueryCursorStatus.DirectionUnsupported => BaseQueryErrorCodes.CursorDirectionUnsupported,
        BaseQueryCursorStatus.KeyTooLarge => BaseQueryErrorCodes.CursorKeyTooLarge,
        _ => BaseQueryErrorCodes.CursorInvalid
    };

    private readonly record struct SqliteCursorState(
        long AppendHighWater,
        long PurgeGeneration,
        long RestoreEpoch);

    private readonly record struct SqliteCursorRow(
        RecordEnvelope Envelope,
        long AppendPosition);

    /// <inheritdoc />
    public async ValueTask<BaseMutationJournalPage> ReadMutationJournalAsync(
        BaseMutationJournalReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.After.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Journal position cannot be negative.");
        if (request.Through is { } through && through.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Journal boundary cannot be negative.");
        if (request.Limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Journal read limit must be positive.");
        if (request.Limit > _options.MutationJournalMaxReadSize)
            throw new ArgumentOutOfRangeException(nameof(request), "Journal read limit exceeds the configured maximum.");

        await using var generationLease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
        var cutoff = MutationJournalCutoff();
        var bounds = await GetBoundsAsync(connection, cutoff, cancellationToken).ConfigureAwait(false);
        var highWatermark = request.Through ?? bounds.HighWatermark;
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT position, event_id, event_type, schema_version, occurred_at, tenant_id,
       operation, visibility, collection_id, record_id, before_json, after_json
FROM {_names.MutationJournal}
WHERE position > $after
  AND position <= $through
  AND julianday(occurred_at) >= julianday($cutoff)
ORDER BY position
LIMIT $limit;
""";
        command.CommandTimeout = TimeoutSeconds();
        command.Parameters.AddWithValue("$after", request.After.Value);
        command.Parameters.AddWithValue("$through", highWatermark.Value);
        command.Parameters.AddWithValue("$cutoff", cutoff);
        command.Parameters.AddWithValue("$limit", checked(request.Limit + 1));

        var entries = new List<BaseMutationJournalEntry>(request.Limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            entries.Add(ReadJournalEntry(reader));

        var hasMore = entries.Count > request.Limit;
        if (hasMore)
            entries.RemoveAt(entries.Count - 1);

        return new BaseMutationJournalPage
        {
            Entries = entries.ToArray(),
            HighWatermark = highWatermark,
            Earliest = bounds.Earliest,
            HasMore = hasMore
        };
    }

    /// <inheritdoc />
    public async ValueTask<BaseMutationJournalEntry?> FindMutationJournalEntryAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await using var generationLease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT position, event_id, event_type, schema_version, occurred_at, tenant_id,
       operation, visibility, collection_id, record_id, before_json, after_json
FROM {_names.MutationJournal}
WHERE event_id = $eventId
  AND julianday(occurred_at) >= julianday($cutoff);
""";
        command.CommandTimeout = TimeoutSeconds();
        command.Parameters.AddWithValue("$eventId", eventId);
        command.Parameters.AddWithValue("$cutoff", MutationJournalCutoff());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadJournalEntry(reader)
            : null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var acquiredSlots = 0;
        using var drainLifetime =
            new CancellationTokenSource(_options.QuarantinedMutationDrainTimeout);
        try
        {
            while (acquiredSlots < _options.MaxTrackedMutationExecutions)
            {
                await _mutationExecutionSlots
                    .WaitAsync(drainLifetime.Token)
                    .ConfigureAwait(false);
                acquiredSlots++;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown is bounded. Active or permanently blocked work remains quarantined.
        }

        var acquiredAdministrationSlots = 0;
        try
        {
            while (acquiredAdministrationSlots < _options.MaxQuarantinedAdministrationExecutions)
            {
                await _administrationExecutionSlots
                    .WaitAsync(drainLifetime.Token)
                    .ConfigureAwait(false);
                acquiredAdministrationSlots++;
            }
        }
        catch (OperationCanceledException)
        {
            // Administration cleanup is bounded by the same provider shutdown lifetime.
        }

        if (_keepAliveConnection is not null)
        {
            try
            {
                await _keepAliveConnection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Cleanup cannot change an already classified mutation outcome.
            }
            _keepAliveConnection = null;
        }

        _keepAliveGate.Dispose();
        if (acquiredSlots == _options.MaxTrackedMutationExecutions)
            _mutationExecutionSlots.Dispose();
        else if (acquiredSlots != 0)
            _mutationExecutionSlots.Release(acquiredSlots);
        if (acquiredAdministrationSlots == _options.MaxQuarantinedAdministrationExecutions)
            _administrationExecutionSlots.Dispose();
        else if (acquiredAdministrationSlots != 0)
            _administrationExecutionSlots.Release(acquiredAdministrationSlots);
    }

    private void TrackQuarantinedMutation(
        Task<bool> cleanup,
        object resourceOwner,
        string? requestIdentity)
    {
        var id = Interlocked.Increment(ref _nextQuarantinedMutationId);
        _quarantinedMutations[id] = new QuarantinedMutation(cleanup, resourceOwner, requestIdentity);
        _ = cleanup.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                if (completed.Status == TaskStatus.RanToCompletion
                    && completed.Result)
                {
                    _quarantinedMutations.TryRemove(id, out var ignored);
                    _ = ignored;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record QuarantinedMutation(
        Task<bool> Cleanup,
        object ResourceOwner,
        string? RequestIdentity);

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordPage>> ListAsync(CollectionDefinition collection, RecordQuery query, OperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceStoreAsync(
            HPDBaseTelemetrySpans.StoreList,
            BaseOperationKind.List,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => ListCoreAsync(collection, query, context, cancellationToken));

    private async ValueTask<OperationResult<RecordPage>> ListCoreAsync(CollectionDefinition collection, RecordQuery query, OperationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(query);
        if (SqliteValidation.ValidateCollectionId<RecordPage>(collection.Id) is { } collectionError) return collectionError;
        if (ValidateRegisteredCollection<RecordPage>(collection.Id) is { } registrationError) return registrationError;
        SqlitePhysicalModel.CollectionModel physicalCollection = _physical.Collection(collection.Id);
        SqliteQueryPlan shapePlan = HPDBaseSqliteTelemetry.TraceQueryPlan(
            _options.StoreId, collection.Id, query,
            () => new SqliteQueryPlanner(_options, physicalCollection).Plan(query));
        if (!shapePlan.Supported)
        {
            HPDBaseSqliteLog.QueryPlanRejected(_logger, "unsupported", SqliteErrorCodes.UnsupportedQuery);
            return SqliteResultFactory.Unsupported<RecordPage>(SqliteErrorCodes.UnsupportedQuery, "SQLite cannot safely execute this query shape before count/page.", string.Join(",", shapePlan.UnsupportedParts));
        }

        try
        {
            await using var generationLease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
            bool requestedCursor = query.Page?.Mode == QueryPaginationMode.Cursor;
            bool cursorCapable = _queryCursors is not null
                && query.Sort is { Length: > 0 }
                && query.Include is not { Length: > 0 };
            if (requestedCursor && !cursorCapable)
                return SqliteResultFactory.Unsupported<RecordPage>(
                    BaseQueryErrorCodes.CursorGuaranteeUnavailable,
                    "This SQLite query cannot provide cursor continuation.");
            if (requestedCursor && query.Page!.CursorDirection != QueryCursorDirection.After)
                return SqliteResultFactory.Validation<RecordPage>(
                    BaseQueryErrorCodes.CursorDirectionUnsupported,
                    "This SQLite query does not support the requested cursor direction.");

            await using SqliteTransaction? readTransaction = cursorCapable
                ? connection.BeginTransaction()
                : null;
            SqliteCursorState cursorState = cursorCapable
                ? await ReadCursorStateAsync(connection, readTransaction!, collection.Id, cancellationToken).ConfigureAwait(false)
                : default;
            QueryCursorGuarantee guarantee = collection.MutationMode is
                BaseCollectionMutationMode.AppendOnly or
                BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge
                    ? QueryCursorGuarantee.StableHistory
                    : QueryCursorGuarantee.Seek;
            BaseQueryCursorPayload? cursorPayload = null;
            if (requestedCursor && !string.IsNullOrWhiteSpace(query.Page!.Cursor))
            {
                BaseQueryCursorReadResult decoded = _queryCursors!.Unprotect(
                    query.Page.Cursor, query, query.Page.Limit ?? _options.DefaultPageSize,
                    _options.StoreId, collection.Id, context, cursorState.RestoreEpoch,
                    Volatile.Read(ref _schemaGeneration), guarantee, cursorState.PurgeGeneration);
                if (decoded.Status != BaseQueryCursorStatus.Valid)
                    return SqliteResultFactory.Validation<RecordPage>(
                        QueryCursorErrorCode(decoded.Status),
                        "The query cursor cannot be continued.");
                cursorPayload = decoded.Payload;
            }
            long? appendHighWater = guarantee == QueryCursorGuarantee.StableHistory && cursorCapable
                ? cursorPayload?.AppendHighWater ?? cursorState.AppendHighWater
                : null;
            var plan = HPDBaseSqliteTelemetry.TraceQueryPlan(
                _options.StoreId, collection.Id, query,
                () => new SqliteQueryPlanner(_options, physicalCollection).Plan(query, cursorPayload, appendHighWater));
            if (!plan.Supported)
            {
                HPDBaseSqliteLog.QueryPlanRejected(_logger, "unsupported", SqliteErrorCodes.UnsupportedQuery);
                return SqliteResultFactory.Unsupported<RecordPage>(SqliteErrorCodes.UnsupportedQuery, "SQLite cannot safely execute this query shape before count/page.", string.Join(",", plan.UnsupportedParts));
            }

            long? total = null;
            if (query.Count != QueryCountMode.None)
            {
                await using var count = connection.CreateCommand();
                count.Transaction = readTransaction;
                count.CommandText = plan.CountSql;
                count.CommandTimeout = TimeoutSeconds();
                plan.Bind(count);
                total = (long)(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = readTransaction;
            command.CommandText = plan.SelectSql;
            command.CommandTimeout = TimeoutSeconds();
            plan.Bind(command);

            var rows = new List<SqliteCursorRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                RecordEnvelope envelope = physicalCollection.ReadEnvelope(reader, _options.StoreId, out long appendPosition);
                rows.Add(new SqliteCursorRow(envelope, appendPosition));
            }

            var requestedLimit = plan.PageInfo.PerPage ?? plan.PageInfo.Limit ?? _options.DefaultPageSize;
            var hasMore = rows.Count > requestedLimit;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            string? nextCursor = null;
            if (hasMore && rows.Count != 0 && cursorCapable)
            {
                SqliteCursorRow last = rows[^1];
                try
                {
                    nextCursor = _queryCursors!.Protect(new BaseQueryCursorPayload
                    {
                        Guarantee = guarantee,
                        Direction = QueryCursorDirection.After,
                        RestoreEpoch = cursorState.RestoreEpoch,
                        SchemaGeneration = Volatile.Read(ref _schemaGeneration),
                        AppendHighWater = appendHighWater ?? 0,
                        PurgeGeneration = cursorState.PurgeGeneration,
                        Keys = SqliteCursorKeys(last.Envelope, query.Sort!),
                        RecordId = last.Envelope.Id.Value
                    }, query, requestedLimit, _options.StoreId, collection.Id, context);
                }
                catch (BaseQueryCursorKeyTooLargeException)
                {
                    return SqliteResultFactory.Validation<RecordPage>(
                        BaseQueryErrorCodes.CursorKeyTooLarge,
                        "The query ordering key exceeds the cursor bound.");
                }
            }
            var items = rows.Select(row => row.Envelope with { Payload = SqliteRecordSerializer.Select(row.Envelope.Payload, query.Select) }).ToArray();
            var page = plan.PageInfo with { HasMore = hasMore, NextCursor = nextCursor };
            return OperationResults.Ok(new RecordPage
            {
                Items = items,
                Page = page,
                Count = query.Count == QueryCountMode.None ? null : new CountInfo { Mode = query.Count, Total = total, IsExact = true }
            });
        }
        catch (SqliteException ex)
        {
            return MapSqlite<RecordPage>(BaseOperationKind.List, ex);
        }
        catch (InvalidOperationException ex)
        {
            return MapSchemaFailure<RecordPage>(ex);
        }
        catch (OperationCanceledException)
        {
            return SqliteResultFactory.StoreError<RecordPage>(SqliteErrorCodes.OperationCancelled, "SQLite operation was cancelled.");
        }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(CollectionDefinition collection, RecordId id, OperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceStoreAsync(
            HPDBaseTelemetrySpans.StoreGet,
            BaseOperationKind.Get,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => GetCoreAsync(collection, id, context, cancellationToken));

    private async ValueTask<OperationResult<RecordEnvelope>> GetCoreAsync(CollectionDefinition collection, RecordId id, OperationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (SqliteValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError) return collectionError;
        if (ValidateRegisteredCollection<RecordEnvelope>(collection.Id) is { } registrationError) return registrationError;
        if (SqliteValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError) return idError;

        try
        {
            await using var generationLease = await _schemaGenerationGate.AcquireSharedAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
            var record = await ReadAsync(connection, collection.Id, id.Value, cancellationToken).ConfigureAwait(false);
            return record is null ? SqliteResultFactory.NotFound<RecordEnvelope>(id.Value) : SqliteResultFactory.WithRevision(OperationResults.Ok(record), record.Metadata);
        }
        catch (SqliteException ex)
        {
            return MapSqlite<RecordEnvelope>(BaseOperationKind.Get, ex);
        }
        catch (InvalidOperationException ex)
        {
            return MapSchemaFailure<RecordEnvelope>(ex);
        }
        catch (OperationCanceledException)
        {
            return SqliteResultFactory.StoreError<RecordEnvelope>(SqliteErrorCodes.OperationCancelled, "SQLite operation was cancelled.");
        }
    }

    private async ValueTask<RecordEnvelope?> ReadAsync(
        SqliteConnection connection,
        string collectionId,
        string id,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null,
        int? commandTimeoutSeconds = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        SqlitePhysicalModel.CollectionModel collection = _physical.Collection(collectionId);
        command.CommandText = $"SELECT {collection.SelectList} FROM {collection.Table} WHERE record_id = $id;";
        command.CommandTimeout = commandTimeoutSeconds ?? TimeoutSeconds();
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? collection.ReadEnvelope(reader, _options.StoreId) : null;
    }

    private async ValueTask<EventReference> AppendMutationJournalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventId,
        BaseOperationKind operation,
        OperationContext context,
        string collectionId,
        RecordId recordId,
        VisibilityLevel visibility,
        RecordEnvelope? before,
        RecordEnvelope? after,
        CancellationToken cancellationToken,
        int commandTimeoutSeconds)
    {
        var occurredAt = Now(context);
        var type = EventType(operation);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
INSERT INTO {_names.MutationJournal}(
  event_id, event_type, schema_version, occurred_at, tenant_id, operation, visibility,
  collection_id, record_id, before_json, after_json)
VALUES(
  $eventId, $eventType, $schemaVersion, $occurredAt, $tenantId, $operation, $visibility,
  $collectionId, $recordId, $beforeJson, $afterJson);
""";
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.AddWithValue("$eventId", eventId);
        command.Parameters.AddWithValue("$eventType", type);
        command.Parameters.AddWithValue("$schemaVersion", BaseEventSchemaVersions.V1);
        command.Parameters.AddWithValue("$occurredAt", occurredAt.ToString("O"));
        command.Parameters.AddWithValue("$tenantId", (object?)context.TenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("$operation", (int)operation);
        command.Parameters.AddWithValue("$visibility", (int)visibility);
        command.Parameters.AddWithValue("$collectionId", collectionId);
        command.Parameters.AddWithValue("$recordId", recordId.Value);
        command.Parameters.AddWithValue("$beforeJson", SerializeSnapshot(before));
        command.Parameters.AddWithValue("$afterJson", SerializeSnapshot(after));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return new EventReference
        {
            EventId = eventId,
            Type = type,
            Stream = "base.mutations",
            PublishedAt = occurredAt,
            Guarantee = EventDeliveryGuarantee.Transactional
        };
    }

    private async ValueTask PruneMutationJournalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        int commandTimeoutSeconds)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
DELETE FROM {_names.MutationJournal}
WHERE julianday(occurred_at) < julianday($cutoff);

DELETE FROM {_names.MutationJournal}
WHERE position <= (
  SELECT CASE
    WHEN COALESCE(MAX(position), 0) > $maxEntries
    THEN COALESCE(MAX(position), 0) - $maxEntries
    ELSE 0
  END
  FROM {_names.MutationJournal}
);
""";
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.AddWithValue("$cutoff", now.Subtract(_options.MutationJournalRetention).ToString("O"));
        command.Parameters.AddWithValue("$maxEntries", _options.MutationJournalMaxEntries);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static object SerializeSnapshot(RecordEnvelope? record)
    {
        if (record is null)
            return DBNull.Value;

        var snapshot = new RecordSnapshot
        {
            CollectionId = record.CollectionId,
            Id = record.Id,
            Payload = record.Payload,
            Metadata = record.Metadata,
            Redacted = false
        };
        return JsonSerializer.Serialize(snapshot, HPDBaseJsonSerializerContext.Default.RecordSnapshot);
    }

    private static BaseMutationJournalEntry ReadJournalEntry(SqliteDataReader reader) => new()
    {
        Position = new BaseMutationJournalPosition(reader.GetInt64(0)),
        EventId = reader.GetString(1),
        Type = reader.GetString(2),
        SchemaVersion = reader.GetString(3),
        OccurredAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        TenantId = reader.IsDBNull(5) ? null : reader.GetString(5),
        Operation = (BaseOperationKind)reader.GetInt32(6),
        Visibility = (VisibilityLevel)reader.GetInt32(7),
        CollectionId = reader.GetString(8),
        RecordId = new RecordId(reader.GetString(9)),
        Before = DeserializeSnapshot(reader, 10),
        After = DeserializeSnapshot(reader, 11)
    };

    private static RecordSnapshot? DeserializeSnapshot(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : JsonSerializer.Deserialize(
                reader.GetString(ordinal),
                HPDBaseJsonSerializerContext.Default.RecordSnapshot);

    private async ValueTask<BaseMutationJournalBounds> GetBoundsAsync(
        SqliteConnection connection,
        string cutoff,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT
  MIN(CASE
    WHEN julianday(occurred_at) >= julianday($cutoff)
    THEN position
    ELSE NULL
  END),
  COALESCE(
    MAX(position),
    (SELECT seq FROM sqlite_sequence WHERE name = $journal),
    0),
  COALESCE((SELECT CAST(value AS INTEGER) FROM {_names.ProviderState} WHERE key = 'restore_epoch'), 0)
FROM {_names.MutationJournal};
""";
        command.CommandTimeout = TimeoutSeconds();
        command.Parameters.AddWithValue("$journal", _names.MutationJournal);
        command.Parameters.AddWithValue("$cutoff", cutoff);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var highWatermark = reader.GetInt64(1);
        var restoreEpoch = reader.GetInt64(2);
        var earliest = reader.IsDBNull(0)
            ? highWatermark == 0 ? 0 : checked(highWatermark + 1)
            : reader.GetInt64(0);
        return new BaseMutationJournalBounds(
            new BaseMutationJournalPosition(earliest),
            new BaseMutationJournalPosition(highWatermark),
            restoreEpoch);
    }

    private string MutationJournalCutoff() =>
        _timeProvider.GetUtcNow()
            .Subtract(_options.MutationJournalRetention)
            .ToString("O");

    private static string EventType(BaseOperationKind operation) => operation switch
    {
        BaseOperationKind.Create => BaseEventTypes.RecordCreated,
        BaseOperationKind.Patch => BaseEventTypes.RecordPatched,
        BaseOperationKind.Replace => BaseEventTypes.RecordUpdated,
        BaseOperationKind.Delete => BaseEventTypes.RecordDeleted,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Only record mutations can be journaled.")
    };

    private async ValueTask<SqliteConnection> OpenInitializedAsync(CancellationToken cancellationToken)
    {
        if (RestoreRecoveryPending && Volatile.Read(ref _restoreInstallationActive) == 0)
            throw new InvalidOperationException("HPD.BASE SQLite restore recovery is incomplete; the store is unavailable.");
        await EnsureKeepAliveAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        RegisterPortableRelationalFunctions(connection);
        if (!await _schema.HasRequiredSchemaAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("HPD.BASE SQLite schema is missing required parts.");
        }

        return connection;
    }

    private static void RegisterPortableRelationalFunctions(SqliteConnection connection)
    {
        connection.CreateCollation("HPD_BASE_DECIMAL", static (left, right) =>
            decimal.Parse(left, CultureInfo.InvariantCulture).CompareTo(decimal.Parse(right, CultureInfo.InvariantCulture)));
        connection.CreateAggregate<string?, DecimalAggregate, string>(
            "HPD_BASE_DECIMAL_SUM",
            default,
            static (state, value) => value is null
                ? state
                : new DecimalAggregate(state.Sum + decimal.Parse(value, CultureInfo.InvariantCulture), state.Count + 1),
            static state => state.Sum.ToString(CultureInfo.InvariantCulture),
            isDeterministic: true);
        connection.CreateAggregate<string?, DecimalAggregate, string?>(
            "HPD_BASE_DECIMAL_AVERAGE",
            default,
            static (state, value) => value is null
                ? state
                : new DecimalAggregate(state.Sum + decimal.Parse(value, CultureInfo.InvariantCulture), state.Count + 1),
            static state => state.Count == 0 ? null : (state.Sum / state.Count).ToString(CultureInfo.InvariantCulture),
            isDeterministic: true);
    }

    private readonly record struct DecimalAggregate(decimal Sum, long Count);

    internal async ValueTask InitializeUnacceptedSchemaForTestsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureKeepAliveAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _schema.InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnsureKeepAliveAsync(CancellationToken cancellationToken)
    {
        if (!_connections.IsMemoryDatabase() || _keepAliveConnection is not null)
        {
            return;
        }

        await _keepAliveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_keepAliveConnection is null)
            {
                _keepAliveConnection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (_keepAliveConnection is not null)
            {
                await _keepAliveConnection.DisposeAsync().ConfigureAwait(false);
                _keepAliveConnection = null;
            }

            throw;
        }
        finally
        {
            _keepAliveGate.Release();
        }
    }

    private async ValueTask<SqliteTransaction> BeginImmediateAsync(
        SqliteConnection connection,
        TimeSpan maximumWait,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SetBusyTimeoutAsync(connection, maximumWait, cancellationToken).ConfigureAwait(false);
        return await HPDBaseSqliteTelemetry.TraceTransactionAsync(
            _options.StoreId,
            () => ValueTask.FromResult(_transactions.BeginImmediate(connection))).ConfigureAwait(false);
    }

    private async ValueTask SetBusyTimeoutAsync(
        SqliteConnection connection,
        TimeSpan maximumWait,
        CancellationToken cancellationToken)
    {
        var boundedWait = maximumWait <= _options.BusyTimeout
            ? maximumWait
            : _options.BusyTimeout;
        var milliseconds = Math.Clamp(
            (long)Math.Ceiling(boundedWait.TotalMilliseconds),
            0,
            int.MaxValue);
        connection.DefaultTimeout = Math.Max(
            1,
            (int)Math.Ceiling(boundedWait.TotalSeconds));
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout={milliseconds.ToString(CultureInfo.InvariantCulture)};";
        command.CommandTimeout = TimeoutSeconds();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private OperationResult<T>? ValidateRegisteredCollection<T>(string collectionId)
    {
        var registered = _options.Collections.Select(static collection => collection.Id).Distinct(StringComparer.Ordinal).ToArray();
        return registered.Length == 0 || registered.Contains(collectionId, StringComparer.Ordinal)
            ? null
            : SqliteResultFactory.CapabilityUnavailable<T>(
                SqliteErrorCodes.CollectionNotRegistered,
                "Collection is not registered for this SQLite store.",
                "collection.binding",
                _options.StoreId,
                collectionId);
    }

    private OperationResult<T>? ValidatePayload<T>(RecordPayload payload)
    {
        try
        {
            var normalized = SqliteRecordSerializer.NormalizeObjectPayload(payload);
            if (normalized.Fields is null || normalized.Fields.Count == 0)
            {
                return SqliteResultFactory.Validation<T>(SqliteErrorCodes.InvalidField, "Payload field map must contain at least one field.", "payload");
            }

            foreach (var field in normalized.Fields ?? [])
            {
                if (SqliteValidation.ValidateFieldName<T>(field.Key) is { } fieldError)
                {
                    return fieldError;
                }
            }

            return null;
        }
        catch (InvalidOperationException ex)
        {
            return SqliteResultFactory.Validation<T>(SqliteErrorCodes.InvalidField, ex.Message, "payload");
        }
    }

    private static DateTimeOffset Now(OperationContext context) => context.Now == default ? DateTimeOffset.UtcNow : context.Now;

    private static bool IsFileBacked(HPDBaseSqliteOptions options)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(
                string.IsNullOrWhiteSpace(options.ConnectionString)
                    ? new SqliteConnectionFactory(options).BuildConnectionString()
                    : options.ConnectionString);
            return builder.Mode != SqliteOpenMode.Memory
                && !string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(builder.DataSource);
        }
        catch (ArgumentException) { return false; }
    }

    private static string CollectionIdForTelemetry(CollectionDefinition? collection) => collection?.Id ?? string.Empty;

    private static string NextRecordId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private int TimeoutSeconds() => Math.Max(1, (int)Math.Ceiling(_options.CommandTimeout.TotalSeconds));

    private int TimeoutSeconds(TimeSpan maximum) =>
        Math.Min(
            TimeoutSeconds(),
            Math.Max(1, (int)Math.Floor(maximum.TotalSeconds)));

    private static void ValidateOptions(HPDBaseSqliteOptions options)
    {
        if (!SqliteValidation.IsValidSchemaPrefix(options.SchemaPrefix)) throw new ArgumentException("SQLite schema prefix must contain only ASCII letters, digits, and underscores.", nameof(options));
        if (options.DefaultPageSize <= 0 || options.MaxPageSize <= 0 || options.DefaultPageSize > options.MaxPageSize) throw new ArgumentException("SQLite page size options are invalid.", nameof(options));
        if (options.MutationJournalRetention <= TimeSpan.Zero) throw new ArgumentException("SQLite mutation journal retention must be positive.", nameof(options));
        if (options.MutationJournalMaxEntries <= 0) throw new ArgumentException("SQLite mutation journal maximum entries must be positive.", nameof(options));
        if (options.MutationJournalMaxReadSize <= 0) throw new ArgumentException("SQLite mutation journal maximum read size must be positive.", nameof(options));
        if (options.MaxTrackedMutationExecutions <= 0) throw new ArgumentException("SQLite tracked mutation execution limit must be positive.", nameof(options));
        if (options.QuarantinedMutationDrainTimeout <= TimeSpan.Zero) throw new ArgumentException("SQLite quarantined mutation drain timeout must be positive.", nameof(options));
        if (options.MaxBackupArtifactBytes < 1024L * 1024 || options.MaxBackupArtifactBytes > 1024L * 1024 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.AdministrationAcquisitionTimeout < TimeSpan.FromSeconds(1) || options.AdministrationAcquisitionTimeout > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.NativeBackupCompletionWait < TimeSpan.FromSeconds(1) || options.NativeBackupCompletionWait > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.RestoreStagingTimeout < TimeSpan.FromSeconds(1) || options.RestoreStagingTimeout > TimeSpan.FromHours(2)) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.IntegrityCheckTimeout < TimeSpan.FromSeconds(1) || options.IntegrityCheckTimeout > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.MaxQuarantinedAdministrationExecutions is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(options));
    }

    private static StoreCapabilityDescriptor CreateCapabilities(HPDBaseSqliteOptions options, bool cursorEnabled, BaseAdministrationCapability administration) => new()
    {
        StoreId = options.StoreId,
        StoreKind = "sqlite",
        StoreVersion = options.StoreVersion,
        Read = new RecordReadCapability
        {
            List = true,
            Get = true,
            MaxPageSize = options.MaxPageSize
        },
        Mutation = new RecordMutationCapability
        {
            Create = true,
            Patch = true,
            Replace = true,
            Delete = true,
            IdAuthority = options.AllowClientRequestedIds ? IdAuthority.Hybrid : IdAuthority.Store,
            TimestampAuthority = TimestampAuthority.Store,
            Consistency = ConsistencyModel.Strong,
            MutationModes = Enum.GetValues<BaseCollectionMutationMode>(),
            AdministrativePurge = true,
        },
        Query = new QueryCapability
        {
            Filter = new FilterCapability { Supported = true, Operators = [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.LessThan, FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual], BooleanComposition = true, Not = true, NullChecks = true, MissingFieldChecks = true, NestedFieldPaths = false, ArrayMembership = false, MaxDepth = options.MaxFilterDepth, MaxNodes = options.MaxFilterNodes, ExecutionMode = QueryExecutionMode.Native },
            Sort = new SortCapability { Supported = true, MaxFields = options.MaxSortFields, NestedFieldPaths = false, NullOrdering = false, StableTieBreaker = true, DefaultSort = ["updatedAt", "id"] },
            Pagination = new PaginationCapability { Page = true, Offset = true, Cursor = cursorEnabled ? QueryCursorGuarantee.StableHistory : QueryCursorGuarantee.None, DefaultLimit = options.DefaultPageSize, MaxLimit = options.MaxPageSize, CursorRequiresStableSort = true },
            Count = new CountCapability { SupportedModes = [QueryCountMode.None, QueryCountMode.IfAvailable, QueryCountMode.Exact], CountMayBeExpensive = false },
            Select = new SelectCapability { PayloadFields = true, SystemFields = false, NestedFieldPaths = false },
            Include = new QueryIncludeCapability { Supported = true, MaxDepth = 3, BackRelations = true, IncludeFilters = true, IncludeSort = true, IncludeLimit = true, ExecutionMode = QueryExecutionMode.Native }
        },
        Revision = new RevisionCapability
        {
            Supported = true,
            Guarantee = RevisionGuarantee.Store,
            Patch = true,
            Replace = true,
            Delete = true
        },
        Batch = new StoreBatchCapability
        {
            Modes = [BaseRecordBatchExecutionMode.Atomic],
            MaxOperations = HPDBaseSqliteDefaults.MaximumBatchOperations,
            MaxCanonicalPayloadBytes = HPDBaseSqliteDefaults.MaximumBatchCanonicalPayloadBytes,
            MinimumAcquisitionTimeout = TimeSpan.FromSeconds(1),
            MinimumTransactionTimeout = TimeSpan.FromSeconds(1),
            MinimumCommitCompletionTimeout = TimeSpan.FromSeconds(1),
            TimeoutGranularity = TimeSpan.FromSeconds(1),
            Ordered = true,
            PartialResults = false,
            CrossCollectionAtomic = true,
            ReadYourWrites = true,
            Durable = true,
            TransactionalJournal = true,
            Isolation = BaseTransactionIsolation.Serializable,
            NestedTransactions = false,
            Savepoints = false
        },
        Upsert = options.AllowClientRequestedIds
            ? new StoreUpsertCapability
            {
                Atomic = true,
                UpdateModes =
                [
                    RecordUpsertUpdateMode.Patch,
                    RecordUpsertUpdateMode.Replace
                ],
                ExpectedRevision = true,
                ExistenceConditions = true
            }
            : null,
        AtomicRequest = new AtomicRequestCapability
        {
            Supported = true,
            Durability = BaseAtomicRequestDurability.Durable,
            DuplicateResultReplay = true,
            FingerprintConflictDetection = true,
            IndeterminateResolution = true,
            MaxIdentityBytes = 512,
            MaxReceiptBytes = 16_777_216,
            MinReceiptLifetime = TimeSpan.FromHours(1),
            MaxReceiptLifetime = TimeSpan.FromDays(90),
        },
        Administration = administration,
        Streaming = new StreamingCapability { Supported = false }
    };

    private OperationResult<T> MapSqlite<T>(BaseOperationKind operation, SqliteException ex)
    {
        var (code, message) = ex.SqliteErrorCode switch
        {
            5 => (SqliteErrorCodes.DatabaseBusy, "SQLite database is busy."),
            6 => (SqliteErrorCodes.DatabaseLocked, "SQLite database is locked."),
            8 => (SqliteErrorCodes.DatabaseReadOnly, "SQLite database is read-only."),
            10 => (SqliteErrorCodes.DatabaseIoError, "SQLite database I/O failed."),
            11 => (SqliteErrorCodes.DatabaseCorrupt, "SQLite database is corrupt."),
            13 => (SqliteErrorCodes.DatabaseFull, "SQLite database or disk is full."),
            14 => (SqliteErrorCodes.DatabaseCantOpen, "SQLite database could not be opened. Check that the data source path exists and is accessible."),
            19 => (SqliteErrorCodes.ConstraintFailed, "SQLite constraint failed."),
            23 => (SqliteErrorCodes.DatabaseAuthDenied, "SQLite database access was denied."),
            26 => (SqliteErrorCodes.DatabaseUnavailable, "SQLite database is not a valid SQLite database."),
            _ => (SqliteErrorCodes.DatabaseUnavailable, "SQLite database operation failed.")
        };

        switch (ex.SqliteErrorCode)
        {
            case 5:
                HPDBaseSqliteLog.DatabaseBusy(_logger, true, ex.SqliteErrorCode, ex.SqliteExtendedErrorCode);
                break;
            case 6:
                HPDBaseSqliteLog.DatabaseLocked(_logger, true, ex.SqliteErrorCode, ex.SqliteExtendedErrorCode);
                break;
            case 14:
                HPDBaseSqliteLog.DatabaseOpenFailed(_logger, code, ex.SqliteErrorCode, ex.SqliteExtendedErrorCode);
                break;
            case 19:
                break;
            default:
                HPDBaseSqliteLog.ProviderOperationFailed(
                    _logger,
                    HPDBaseSqliteLog.OperationKind(operation),
                    "store",
                    code,
                    ex.SqliteErrorCode,
                    ex.SqliteExtendedErrorCode);
                break;
        }

        return SqliteResultFactory.StoreError<T>(
            code,
            message,
            _options.StoreId,
            ex.SqliteErrorCode,
            ex.SqliteExtendedErrorCode,
            ex.Message);
    }

    private OperationResult<T> MapSchemaFailure<T>(InvalidOperationException exception)
    {
        HPDBaseSqliteLog.SchemaMissing(_logger, SqliteErrorCodes.SchemaMissing);
        return SqliteResultFactory.StoreError<T>(SqliteErrorCodes.SchemaMissing, exception.Message);
    }
}
