using HPD.Base;
using HPD.Base.Events;
using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Observability;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Results;
using HPD.Base.Schema;
using HPD.Base.Serialization;
using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.Internal;
using HPD.Base.Sqlite.Observability;
using HPD.Base.Sqlite.Observability.Logging;
using HPD.Base.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace HPD.Base.Sqlite;

/// <summary>Durable SQLite implementation of the HPD.BASE record store contract.</summary>
public sealed partial class SqliteRecordStore :
    IRecordMutationStore,
    IAtomicRecordStore,
    ITransactionalMutationJournalStore,
    IAsyncDisposable
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqliteConnectionFactory _connections;
    private readonly SqliteSchemaInitializer _schema;
    private readonly SqliteNames _names;
    private readonly ILogger<SqliteRecordStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ISqliteTransactionController _transactions;
    private readonly SemaphoreSlim _keepAliveGate = new(1, 1);
    private SqliteConnection? _keepAliveConnection;

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
        ISqliteTransactionController? transactions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        ValidateOptions(_options);
        _logger = loggerFactory.CreateLogger<SqliteRecordStore>();
        _timeProvider = timeProvider;
        _transactions = transactions ?? DefaultSqliteTransactionController.Instance;
        _connections = new SqliteConnectionFactory(_options);
        _schema = new SqliteSchemaInitializer(_options);
        _names = new SqliteNames(_options);
        Capabilities = CreateCapabilities(_options);
    }

    /// <inheritdoc />
    public StoreCapabilityDescriptor Capabilities { get; }

    /// <inheritdoc />
    public async ValueTask<BaseMutationJournalBounds> GetMutationJournalBoundsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await GetBoundsAsync(
            connection,
            MutationJournalCutoff(),
            cancellationToken).ConfigureAwait(false);
    }

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
        if (_keepAliveConnection is not null)
        {
            await _keepAliveConnection.DisposeAsync().ConfigureAwait(false);
            _keepAliveConnection = null;
        }

        _keepAliveGate.Dispose();
    }

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

        var plan = HPDBaseSqliteTelemetry.TraceQueryPlan(_options.StoreId, collection.Id, query, () => new SqliteQueryPlanner(_options).Plan(collection.Id, query));
        if (!plan.Supported)
        {
            HPDBaseSqliteLog.QueryPlanRejected(_logger, "unsupported", SqliteErrorCodes.UnsupportedQuery);
            return SqliteResultFactory.Unsupported<RecordPage>(SqliteErrorCodes.UnsupportedQuery, "SQLite cannot safely execute this query shape before count/page.", string.Join(",", plan.UnsupportedParts));
        }

        try
        {
            await using var connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
            long? total = null;
            if (query.Count != QueryCountMode.None)
            {
                await using var count = connection.CreateCommand();
                count.CommandText = plan.CountSql;
                count.CommandTimeout = TimeoutSeconds();
                plan.Bind(count);
                total = (long)(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = plan.SelectSql;
            command.CommandTimeout = TimeoutSeconds();
            plan.Bind(command);

            var rows = new List<RecordEnvelope>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(SqliteRecordMapper.ReadEnvelope(reader, _options.StoreId));
            }

            var requestedLimit = plan.PageInfo.PerPage ?? plan.PageInfo.Limit ?? _options.DefaultPageSize;
            var hasMore = rows.Count > requestedLimit;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            var items = rows.Select(row => row with { Payload = SqliteRecordSerializer.Select(row.Payload, query.Select) }).ToArray();
            var page = plan.PageInfo with { HasMore = hasMore };
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
        command.CommandText = $"SELECT collection_id, record_id, revision, created_at, updated_at, payload_json FROM {_names.Records} WHERE collection_id = $collection AND record_id = $id;";
        command.CommandTimeout = commandTimeoutSeconds ?? TimeoutSeconds();
        command.Parameters.AddWithValue("$collection", collectionId);
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? SqliteRecordMapper.ReadEnvelope(reader, _options.StoreId) : null;
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
    0)
FROM {_names.MutationJournal};
""";
        command.CommandTimeout = TimeoutSeconds();
        command.Parameters.AddWithValue("$journal", _names.MutationJournal);
        command.Parameters.AddWithValue("$cutoff", cutoff);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var highWatermark = reader.GetInt64(1);
        var earliest = reader.IsDBNull(0)
            ? highWatermark == 0 ? 0 : checked(highWatermark + 1)
            : reader.GetInt64(0);
        return new BaseMutationJournalBounds(
            new BaseMutationJournalPosition(earliest),
            new BaseMutationJournalPosition(highWatermark));
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
        await EnsureKeepAliveAsync(cancellationToken).ConfigureAwait(false);
        var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (_options.AutoInitialize)
        {
            await _schema.InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        else if (_options.FailIfSchemaMissing && !await _schema.HasRequiredSchemaAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            throw new SqliteException("HPD.BASE SQLite schema is missing.", 1);
        }

        return connection;
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
        var registered = _options.CollectionIds.Concat((_options.Collections ?? []).Select(collection => collection.Id)).Distinct(StringComparer.Ordinal).ToArray();
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
    }

    private static StoreCapabilityDescriptor CreateCapabilities(HPDBaseSqliteOptions options) => new()
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
            Consistency = ConsistencyModel.Strong
        },
        Query = new QueryCapability
        {
            Filter = new FilterCapability { Supported = true, Operators = [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.LessThan, FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual], BooleanComposition = true, Not = true, NullChecks = true, MissingFieldChecks = true, NestedFieldPaths = false, ArrayMembership = false, MaxDepth = options.MaxFilterDepth, MaxNodes = options.MaxFilterNodes, ExecutionMode = QueryExecutionMode.Native },
            Sort = new SortCapability { Supported = true, MaxFields = options.MaxSortFields, NestedFieldPaths = false, NullOrdering = false, StableTieBreaker = true, DefaultSort = ["updatedAt", "id"] },
            Pagination = new PaginationCapability { Page = true, Offset = true, Cursor = false, DefaultLimit = options.DefaultPageSize, MaxLimit = options.MaxPageSize, CursorRequiresStableSort = false },
            Count = new CountCapability { SupportedModes = [QueryCountMode.None, QueryCountMode.IfAvailable, QueryCountMode.Exact], CountMayBeExpensive = false },
            Select = new SelectCapability { PayloadFields = true, SystemFields = false, NestedFieldPaths = false },
            Include = new QueryIncludeCapability { Supported = false, ExecutionMode = QueryExecutionMode.Unsupported }
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
