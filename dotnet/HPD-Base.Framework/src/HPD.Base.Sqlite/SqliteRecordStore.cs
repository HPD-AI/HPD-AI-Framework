using HPD.Base;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Observability;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Results;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.Internal;
using HPD.Base.Sqlite.Observability;
using HPD.Base.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;

namespace HPD.Base.Sqlite;

/// <summary>Durable SQLite implementation of the HPD.BASE record store contract.</summary>
public sealed class SqliteRecordStore : IRevisionedRecordStore, IAsyncDisposable
{
    private readonly HPDBaseSqliteOptions _options;
    private readonly SqliteConnectionFactory _connections;
    private readonly SqliteSchemaInitializer _schema;
    private readonly SqliteNames _names;
    private readonly SemaphoreSlim _keepAliveGate = new(1, 1);
    private SqliteConnection? _keepAliveConnection;

    public SqliteRecordStore(IOptions<HPDBaseSqliteOptions> options)
        : this(options.Value)
    {
    }

    public SqliteRecordStore(HPDBaseSqliteOptions? options = null)
    {
        _options = options ?? new HPDBaseSqliteOptions();
        ValidateOptions(_options);
        _connections = new SqliteConnectionFactory(_options);
        _schema = new SqliteSchemaInitializer(_options);
        _names = new SqliteNames(_options);
        Capabilities = CreateCapabilities(_options);
    }

    public StoreCapabilityDescriptor Capabilities { get; }

    public async ValueTask DisposeAsync()
    {
        if (_keepAliveConnection is not null)
        {
            await _keepAliveConnection.DisposeAsync().ConfigureAwait(false);
            _keepAliveConnection = null;
        }

        _keepAliveGate.Dispose();
    }

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
            return MapSqlite<RecordPage>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return SqliteResultFactory.StoreError<RecordPage>(SqliteErrorCodes.SchemaMissing, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return SqliteResultFactory.StoreError<RecordPage>(SqliteErrorCodes.OperationCancelled, "SQLite operation was cancelled.");
        }
    }

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
            return MapSqlite<RecordEnvelope>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return SqliteResultFactory.StoreError<RecordEnvelope>(SqliteErrorCodes.SchemaMissing, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return SqliteResultFactory.StoreError<RecordEnvelope>(SqliteErrorCodes.OperationCancelled, "SQLite operation was cancelled.");
        }
    }

    public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(CollectionDefinition collection, RecordCreateRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceStoreAsync(
            HPDBaseTelemetrySpans.StoreCreate,
            BaseOperationKind.Create,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => CreateCoreAsync(collection, request, context, cancellationToken));

    private async ValueTask<OperationResult<RecordEnvelope>> CreateCoreAsync(CollectionDefinition collection, RecordCreateRequest request, OperationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        if (SqliteValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError) return collectionError;
        if (ValidateRegisteredCollection<RecordEnvelope>(collection.Id) is { } registrationError) return registrationError;
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey)) return SqliteResultFactory.Unsupported<RecordEnvelope>(SqliteErrorCodes.IdempotencyUnsupported, "Idempotency keys are not supported by HPD.BASE SQLite.", collection.Id);
        if (request.RequestedId is not null && !_options.AllowClientRequestedIds) return SqliteResultFactory.Unsupported<RecordEnvelope>(SqliteErrorCodes.RequestedIdUnsupported, "Client-requested ids are disabled for this SQLite store.", request.RequestedId.Value.Value);

        var id = request.RequestedId ?? new RecordId(NextRecordId());
        if (SqliteValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError) return idError;
        if (ValidatePayload<RecordEnvelope>(request.Payload) is { } payloadError) return payloadError;

        var now = Now(context);
        var payloadJson = SqliteRecordSerializer.Serialize(request.Payload);
        try
        {
            await using var connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await BeginImmediateAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {_names.Records}(collection_id, record_id, revision, created_at, updated_at, payload_json) VALUES ($collection, $id, 1, $created, $updated, $payload);";
            command.CommandTimeout = TimeoutSeconds();
            command.Parameters.AddWithValue("$collection", collection.Id);
            command.Parameters.AddWithValue("$id", id.Value);
            command.Parameters.AddWithValue("$created", now.ToString("O"));
            command.Parameters.AddWithValue("$updated", now.ToString("O"));
            command.Parameters.AddWithValue("$payload", payloadJson);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            var metadata = SqliteRecordMapper.Metadata(1, now, now, _options.StoreId);
            var envelope = new RecordEnvelope { CollectionId = collection.Id, Id = id, Payload = SqliteRecordSerializer.Deserialize(payloadJson), Metadata = metadata };
            return SqliteResultFactory.WithRevision(OperationResults.Created(envelope), metadata);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return SqliteResultFactory.DuplicateId<RecordEnvelope>(id.Value);
        }
        catch (SqliteException ex)
        {
            return MapSqlite<RecordEnvelope>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return SqliteResultFactory.StoreError<RecordEnvelope>(SqliteErrorCodes.SchemaMissing, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return SqliteResultFactory.StoreError<RecordEnvelope>(SqliteErrorCodes.OperationCancelled, "SQLite operation was cancelled.");
        }
    }

    public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(CollectionDefinition collection, RecordId id, RecordPatchRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceStoreAsync(
            HPDBaseTelemetrySpans.StorePatch,
            BaseOperationKind.Patch,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => PatchCoreAsync(collection, id, request, request.ExpectedRevision, context, cancellationToken));

    public ValueTask<OperationResult<RecordEnvelope>> PatchIfRevisionAsync(CollectionDefinition collection, RecordId id, RecordPatchRequest request, RevisionToken expectedRevision, OperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceStoreAsync(
            HPDBaseTelemetrySpans.StorePatchIfRevision,
            BaseOperationKind.Patch,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => PatchCoreAsync(collection, id, request, expectedRevision, context, cancellationToken));

    public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(CollectionDefinition collection, RecordId id, RecordReplaceRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceStoreAsync(
            HPDBaseTelemetrySpans.StoreReplace,
            BaseOperationKind.Replace,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => ReplaceCoreAsync(collection, id, request, request.ExpectedRevision, context, cancellationToken));

    public ValueTask<OperationResult<RecordEnvelope>> ReplaceIfRevisionAsync(CollectionDefinition collection, RecordId id, RecordReplaceRequest request, RevisionToken expectedRevision, OperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceStoreAsync(
            HPDBaseTelemetrySpans.StoreReplaceIfRevision,
            BaseOperationKind.Replace,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => ReplaceCoreAsync(collection, id, request, expectedRevision, context, cancellationToken));

    public ValueTask<OperationResult<DeleteResult>> DeleteAsync(CollectionDefinition collection, RecordId id, RecordDeleteRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
        HPDBaseSqliteTelemetry.TraceStoreAsync(
            HPDBaseTelemetrySpans.StoreDelete,
            BaseOperationKind.Delete,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => DeleteCoreAsync(collection, id, request, context, cancellationToken));

    private async ValueTask<OperationResult<DeleteResult>> DeleteCoreAsync(CollectionDefinition collection, RecordId id, RecordDeleteRequest request, OperationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        if (SqliteValidation.ValidateCollectionId<DeleteResult>(collection.Id) is { } collectionError) return collectionError;
        if (ValidateRegisteredCollection<DeleteResult>(collection.Id) is { } registrationError) return registrationError;
        if (SqliteValidation.ValidateRecordId<DeleteResult>(id.Value) is { } idError) return idError;
        if (!SqliteRecordMapper.TryParseRevision(request.ExpectedRevision, out var expected)) return SqliteResultFactory.Validation<DeleteResult>(SqliteErrorCodes.InvalidRevisionToken, "Expected revision must use the sqlite:{integer} format.", "expectedRevision");

        try
        {
            await using var connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await BeginImmediateAsync(connection, cancellationToken).ConfigureAwait(false);
            var existing = await ReadAsync(connection, collection.Id, id.Value, cancellationToken, transaction).ConfigureAwait(false);
            if (existing is null) return SqliteResultFactory.NotFound<DeleteResult>(id.Value);
            if (request.ExpectedRevision is not null && expected.ToString(CultureInfo.InvariantCulture) != existing.Metadata.Revision?.Value["sqlite:".Length..])
            {
                return SqliteResultFactory.RevisionConflict<DeleteResult>(request.ExpectedRevision.Value, existing.Metadata.Revision, id.Value);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {_names.Records} WHERE collection_id = $collection AND record_id = $id;";
            command.Parameters.AddWithValue("$collection", collection.Id);
            command.Parameters.AddWithValue("$id", id.Value);
            command.CommandTimeout = TimeoutSeconds();
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return OperationResults.Deleted(new DeleteResult { Id = id, Deleted = true, Previous = request.ReturnPrevious ? existing : null });
        }
        catch (SqliteException ex)
        {
            return MapSqlite<DeleteResult>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return SqliteResultFactory.StoreError<DeleteResult>(SqliteErrorCodes.SchemaMissing, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return SqliteResultFactory.StoreError<DeleteResult>(SqliteErrorCodes.OperationCancelled, "SQLite operation was cancelled.");
        }
    }

    private async ValueTask<OperationResult<RecordEnvelope>> PatchCoreAsync(CollectionDefinition collection, RecordId id, RecordPatchRequest request, RevisionToken? expectedRevision, OperationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ValidatePayload<RecordEnvelope>(request.Patch) is { } payloadError) return payloadError;
        return await MutateAsync(collection, id, expectedRevision, context, current => SqliteRecordSerializer.Merge(current, request.Patch), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OperationResult<RecordEnvelope>> ReplaceCoreAsync(CollectionDefinition collection, RecordId id, RecordReplaceRequest request, RevisionToken? expectedRevision, OperationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ValidatePayload<RecordEnvelope>(request.Payload) is { } payloadError) return payloadError;
        return await MutateAsync(collection, id, expectedRevision, context, _ => SqliteRecordSerializer.Clone(request.Payload), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OperationResult<RecordEnvelope>> MutateAsync(CollectionDefinition collection, RecordId id, RevisionToken? expectedRevision, OperationContext context, Func<RecordPayload, RecordPayload> mutate, CancellationToken cancellationToken)
    {
        if (SqliteValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError) return collectionError;
        if (ValidateRegisteredCollection<RecordEnvelope>(collection.Id) is { } registrationError) return registrationError;
        if (SqliteValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError) return idError;
        if (!SqliteRecordMapper.TryParseRevision(expectedRevision, out var expected)) return SqliteResultFactory.Validation<RecordEnvelope>(SqliteErrorCodes.InvalidRevisionToken, "Expected revision must use the sqlite:{integer} format.", "expectedRevision");

        try
        {
            await using var connection = await OpenInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await BeginImmediateAsync(connection, cancellationToken).ConfigureAwait(false);
            var existing = await ReadAsync(connection, collection.Id, id.Value, cancellationToken, transaction).ConfigureAwait(false);
            if (existing is null) return SqliteResultFactory.NotFound<RecordEnvelope>(id.Value);
            var currentRevision = long.Parse(existing.Metadata.Revision!.Value.Value["sqlite:".Length..], CultureInfo.InvariantCulture);
            if (expectedRevision is not null && expected != currentRevision)
            {
                return SqliteResultFactory.RevisionConflict<RecordEnvelope>(expectedRevision.Value, existing.Metadata.Revision, id.Value);
            }

            var nextRevision = currentRevision + 1;
            var now = Now(context);
            var nextPayload = mutate(existing.Payload);
            var payloadJson = SqliteRecordSerializer.Serialize(nextPayload);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"UPDATE {_names.Records} SET revision = $revision, updated_at = $updated, payload_json = $payload WHERE collection_id = $collection AND record_id = $id;";
            command.CommandTimeout = TimeoutSeconds();
            command.Parameters.AddWithValue("$revision", nextRevision);
            command.Parameters.AddWithValue("$updated", now.ToString("O"));
            command.Parameters.AddWithValue("$payload", payloadJson);
            command.Parameters.AddWithValue("$collection", collection.Id);
            command.Parameters.AddWithValue("$id", id.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            var metadata = SqliteRecordMapper.Metadata(nextRevision, existing.Metadata.CreatedAt!.Value, now, _options.StoreId);
            var envelope = existing with { Payload = SqliteRecordSerializer.Deserialize(payloadJson), Metadata = metadata };
            return SqliteResultFactory.WithRevision(OperationResults.Updated(envelope), metadata);
        }
        catch (SqliteException ex)
        {
            return MapSqlite<RecordEnvelope>(ex);
        }
        catch (InvalidOperationException ex)
        {
            return SqliteResultFactory.StoreError<RecordEnvelope>(SqliteErrorCodes.SchemaMissing, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return SqliteResultFactory.StoreError<RecordEnvelope>(SqliteErrorCodes.OperationCancelled, "SQLite operation was cancelled.");
        }
    }

    private async ValueTask<RecordEnvelope?> ReadAsync(SqliteConnection connection, string collectionId, string id, CancellationToken cancellationToken, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT collection_id, record_id, revision, created_at, updated_at, payload_json FROM {_names.Records} WHERE collection_id = $collection AND record_id = $id;";
        command.CommandTimeout = TimeoutSeconds();
        command.Parameters.AddWithValue("$collection", collectionId);
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? SqliteRecordMapper.ReadEnvelope(reader, _options.StoreId) : null;
    }

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

    private async ValueTask<SqliteTransaction> BeginImmediateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await HPDBaseSqliteTelemetry.TraceTransactionAsync(
            _options.StoreId,
            () => ValueTask.FromResult(connection.BeginTransaction(deferred: false))).ConfigureAwait(false);
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

    private static void ValidateOptions(HPDBaseSqliteOptions options)
    {
        if (!SqliteValidation.IsValidSchemaPrefix(options.SchemaPrefix)) throw new ArgumentException("SQLite schema prefix must contain only ASCII letters, digits, and underscores.", nameof(options));
        if (options.DefaultPageSize <= 0 || options.MaxPageSize <= 0 || options.DefaultPageSize > options.MaxPageSize) throw new ArgumentException("SQLite page size options are invalid.", nameof(options));
    }

    private static StoreCapabilityDescriptor CreateCapabilities(HPDBaseSqliteOptions options) => new()
    {
        StoreId = options.StoreId,
        StoreKind = "sqlite",
        StoreVersion = options.StoreVersion,
        Crud = new CrudCapability { List = true, Get = true, Create = true, Patch = true, Replace = true, Delete = true, IdAuthority = options.AllowClientRequestedIds ? IdAuthority.Hybrid : IdAuthority.Store, TimestampAuthority = TimestampAuthority.Store, Consistency = ConsistencyModel.Strong },
        Query = new QueryCapability
        {
            Filter = new FilterCapability { Supported = true, Operators = [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.LessThan, FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual], BooleanComposition = true, Not = true, NullChecks = true, MissingFieldChecks = true, NestedFieldPaths = false, ArrayMembership = false, MaxDepth = options.MaxFilterDepth, MaxNodes = options.MaxFilterNodes, ExecutionMode = QueryExecutionMode.Native },
            Sort = new SortCapability { Supported = true, MaxFields = options.MaxSortFields, NestedFieldPaths = false, NullOrdering = false, StableTieBreaker = true, DefaultSort = ["updatedAt", "id"] },
            Pagination = new PaginationCapability { Page = true, Offset = true, Cursor = false, DefaultLimit = options.DefaultPageSize, MaxLimit = options.MaxPageSize, CursorRequiresStableSort = false },
            Count = new CountCapability { SupportedModes = [QueryCountMode.None, QueryCountMode.IfAvailable, QueryCountMode.Exact], CountMayBeExpensive = false },
            Select = new SelectCapability { PayloadFields = true, SystemFields = false, NestedFieldPaths = false },
            Include = new QueryIncludeCapability { Supported = false, ExecutionMode = QueryExecutionMode.Unsupported }
        },
        Revision = new RevisionCapability { Supported = true, Guarantee = RevisionGuarantee.Store, Patch = true, Delete = true },
        Streaming = new StreamingCapability { Supported = false }
    };

    private OperationResult<T> MapSqlite<T>(SqliteException ex)
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

        return SqliteResultFactory.StoreError<T>(
            code,
            message,
            _options.StoreId,
            ex.SqliteErrorCode,
            ex.SqliteExtendedErrorCode,
            ex.Message);
    }
}
