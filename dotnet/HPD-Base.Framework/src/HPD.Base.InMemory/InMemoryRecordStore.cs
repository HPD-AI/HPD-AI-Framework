using HPD.Base.InMemory.Configuration;
using HPD.Base.InMemory.Internal;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Results;
using HPD.Base.Schema;
using HPD.Base.Stores;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base.InMemory;

/// <summary>
/// Process-local, thread-safe, non-durable HPD.BASE record store implementation.
/// </summary>
public sealed class InMemoryRecordStore : IRevisionedRecordStore, IStreamingRecordStore
{
    private readonly HPDBaseInMemoryOptions _options;
    private readonly InMemoryStoreState _state = new();

    /// <summary>
    /// Initializes a new store using configured options.
    /// </summary>
    /// <param name="options">The configured InMemory options.</param>
    public InMemoryRecordStore(IOptions<HPDBaseInMemoryOptions> options)
        : this(options.Value)
    {
    }

    /// <summary>
    /// Initializes a new store using the supplied options, or defaults when omitted.
    /// </summary>
    /// <param name="options">The InMemory options.</param>
    public InMemoryRecordStore(HPDBaseInMemoryOptions? options = null)
    {
        _options = options ?? new HPDBaseInMemoryOptions();
        ValidateOptions(_options);
        Capabilities = CreateCapabilities(_options);
    }

    /// <inheritdoc />
    public StoreCapabilityDescriptor Capabilities { get; }

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordPage>> ListAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (InMemoryValidation.ValidateCollectionId<RecordPage>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (ValidateUnsupportedQuery<RecordPage>(query, allowCount: true) is { } queryError)
        {
            return ValueTask.FromResult(queryError);
        }

        StoredRecord[] snapshot;
        lock (_state.Gate)
        {
            snapshot = GetCollectionOrNull(collection.Id)?.RecordsById.Values
                .OrderBy(record => record.Sequence)
                .ThenBy(record => record.Id.Value, StringComparer.Ordinal)
                .ToArray() ?? [];
        }

        var filtered = new List<StoredRecord>(snapshot.Length);
        foreach (var record in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query.Filter is null || MatchesFilter(record, query.Filter))
            {
                filtered.Add(record);
            }
        }

        var sortedResult = ApplySort<RecordPage>(filtered, query.Sort);
        if (sortedResult.Result is not null)
        {
            return ValueTask.FromResult(sortedResult.Result);
        }

        var sorted = sortedResult.Value!;
        var total = sorted.Count;
        var pageResult = ApplyPage<RecordPage>(sorted, query, out var pageInfo);
        if (pageResult.Result is not null)
        {
            return ValueTask.FromResult(pageResult.Result);
        }

        var page = pageResult.Value!;
        var items = ApplySelect(page, query.Select)
            .Select(RecordCloneHelpers.CloneEnvelope)
            .ToArray();

        var recordPage = new RecordPage
        {
            Items = items,
            Page = pageInfo,
            Count = query.Count == QueryCountMode.None
                ? null
                : new CountInfo { Mode = query.Count, Total = total, IsExact = true }
        };

        return ValueTask.FromResult(OperationResults.Ok(recordPage));
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        cancellationToken.ThrowIfCancellationRequested();

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        lock (_state.Gate)
        {
            var record = GetCollectionOrNull(collection.Id)?.RecordsById.GetValueOrDefault(id.Value);
            if (record is null)
            {
                return ValueTask.FromResult(InMemoryResultFactory.NotFound<RecordEnvelope>(id.Value));
            }

            var envelope = RecordCloneHelpers.CloneEnvelope(record);
            return ValueTask.FromResult(InMemoryResultFactory.WithRevision(OperationResults.Ok(envelope), record.Metadata));
        }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
        CollectionDefinition collection,
        RecordCreateRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return ValueTask.FromResult(InMemoryResultFactory.Unsupported<RecordEnvelope>(
                InMemoryErrorCodes.IdempotencyUnsupported,
                "Idempotency keys are not supported by HPD.BASE InMemory.",
                collection.Id));
        }

        var normalizedCreatePayload = NormalizeObjectPayload<RecordEnvelope>(request.Payload);
        if (normalizedCreatePayload.Value is not { } payload)
        {
            return ValueTask.FromResult(normalizedCreatePayload.Result!);
        }

        foreach (var field in payload.Fields ?? [])
        {
            if (InMemoryValidation.ValidateFieldName<RecordEnvelope>(field.Key) is { } fieldError)
            {
                return ValueTask.FromResult(fieldError);
            }
        }

        lock (_state.Gate)
        {
            var id = request.RequestedId ?? new RecordId(NextRecordId());
            if (request.RequestedId is not null && !_options.AllowClientRequestedIds)
            {
                return ValueTask.FromResult(InMemoryResultFactory.Unsupported<RecordEnvelope>(
                    InMemoryErrorCodes.RequestedIdUnsupported,
                    "Client-requested ids are disabled for this InMemory store.",
                    id.Value));
            }

            if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
            {
                return ValueTask.FromResult(idError);
            }

            var state = GetOrCreateCollection(collection.Id);
            if (state.RecordsById.ContainsKey(id.Value))
            {
                return ValueTask.FromResult(InMemoryResultFactory.DuplicateId<RecordEnvelope>(id.Value));
            }

            var now = Now(context);
            var revision = NextRevision();
            var metadata = new RecordMetadata
            {
                CreatedAt = now,
                UpdatedAt = now,
                Revision = revision,
                ETag = ETag(revision),
                StoreId = _options.StoreId
            };
            var record = new StoredRecord(collection.Id, id, payload, metadata, ++_state.NextSequence);
            state.RecordsById.Add(id.Value, record);

            var result = OperationResults.Created(RecordCloneHelpers.CloneEnvelope(record));
            return ValueTask.FromResult(InMemoryResultFactory.WithRevision(result, metadata));
        }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        PatchCoreAsync(collection, id, request, request.ExpectedRevision, context, cancellationToken);

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        ReplaceCoreAsync(collection, id, request, request.ExpectedRevision, context, cancellationToken);

    /// <inheritdoc />
    public ValueTask<OperationResult<DeleteResult>> DeleteAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordDeleteRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (InMemoryValidation.ValidateCollectionId<DeleteResult>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<DeleteResult>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        lock (_state.Gate)
        {
            var state = GetCollectionOrNull(collection.Id);
            if (state is null || !state.RecordsById.TryGetValue(id.Value, out var current))
            {
                return ValueTask.FromResult(InMemoryResultFactory.NotFound<DeleteResult>(id.Value));
            }

            if (request.ExpectedRevision is { } expected && !RevisionEquals(current.Metadata.Revision, expected))
            {
                return ValueTask.FromResult(InMemoryResultFactory.RevisionConflict<DeleteResult>(expected, current.Metadata.Revision, id.Value));
            }

            var previous = request.ReturnPrevious ? RecordCloneHelpers.CloneEnvelope(current) : null;
            state.RecordsById.Remove(id.Value);
            var result = OperationResults.Deleted(new DeleteResult
            {
                Id = id,
                Deleted = true,
                Previous = previous
            });
            return ValueTask.FromResult(InMemoryResultFactory.WithRevision(result, current.Metadata));
        }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordEnvelope>> PatchIfRevisionAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        RevisionToken expectedRevision,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        PatchCoreAsync(collection, id, request, expectedRevision, context, cancellationToken);

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordEnvelope>> ReplaceIfRevisionAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        RevisionToken expectedRevision,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        ReplaceCoreAsync(collection, id, request, expectedRevision, context, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<RecordEnvelope> StreamAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.EnableStreamingCapability)
        {
            throw new InvalidOperationException("Streaming is disabled for this HPD.BASE InMemory store.");
        }

        if (ValidateUnsupportedQuery<RecordEnvelope>(query, allowCount: false) is { } queryError)
        {
            throw new InvalidOperationException(queryError.Error?.Message ?? "Stream query is unsupported.");
        }

        var queryWithoutCount = query with { Count = QueryCountMode.None };
        var list = await ListAsync(collection, queryWithoutCount, context, cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess() || list.Value is null)
        {
            yield break;
        }

        var yielded = 0;
        foreach (var item in list.Value.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_options.MaxStreamItems is { } maxItems && yielded >= maxItems)
            {
                yield break;
            }

            yielded++;
            yield return RecordCloneHelpers.CloneEnvelope(new StoredRecord(
                item.CollectionId,
                item.Id,
                item.Payload,
                item.Metadata,
                0));
        }
    }

    private ValueTask<OperationResult<RecordEnvelope>> PatchCoreAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        RevisionToken? expectedRevision,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        if (ValidateExpectedRevisionInputs<RecordEnvelope>(request.ExpectedRevision, expectedRevision) is { } revisionInputError)
        {
            return ValueTask.FromResult(revisionInputError);
        }

        if (request.Patch.Kind != RecordPayloadKind.FieldMap)
        {
            return ValueTask.FromResult(InMemoryResultFactory.Unsupported<RecordEnvelope>(
                InMemoryErrorCodes.PatchUnsupportedShape,
                "Portable InMemory patch requires a field-map payload.",
                id.Value));
        }

        if (request.Patch.Fields is null || request.Patch.Fields.Count == 0)
        {
            return ValueTask.FromResult(InMemoryResultFactory.Validation<RecordEnvelope>(
                InMemoryErrorCodes.EmptyPatch,
                "Patch must contain at least one top-level field.",
                id.Value));
        }

        foreach (var field in request.Patch.Fields)
        {
            if (InMemoryValidation.ValidateFieldName<RecordEnvelope>(field.Key) is { } fieldError)
            {
                return ValueTask.FromResult(fieldError);
            }
        }

        lock (_state.Gate)
        {
            var state = GetCollectionOrNull(collection.Id);
            if (state is null || !state.RecordsById.TryGetValue(id.Value, out var current))
            {
                return ValueTask.FromResult(InMemoryResultFactory.NotFound<RecordEnvelope>(id.Value));
            }

            if (expectedRevision is { } expected && !RevisionEquals(current.Metadata.Revision, expected))
            {
                return ValueTask.FromResult(InMemoryResultFactory.RevisionConflict<RecordEnvelope>(expected, current.Metadata.Revision, id.Value));
            }

            var existingFields = ToFieldMap<RecordEnvelope>(current.Payload);
            if (existingFields.Value is not { } fields)
            {
                return ValueTask.FromResult(existingFields.Result!);
            }

            foreach (var field in request.Patch.Fields)
            {
                fields[field.Key] = field.Value.Clone();
            }

            var updatedPayload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
            var updated = MutateRecord(current, updatedPayload, context);
            state.RecordsById[id.Value] = updated;
            var result = OperationResults.Updated(RecordCloneHelpers.CloneEnvelope(updated));
            return ValueTask.FromResult(InMemoryResultFactory.WithRevision(result, updated.Metadata));
        }
    }

    private ValueTask<OperationResult<RecordEnvelope>> ReplaceCoreAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        RevisionToken? expectedRevision,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        if (ValidateExpectedRevisionInputs<RecordEnvelope>(request.ExpectedRevision, expectedRevision) is { } revisionInputError)
        {
            return ValueTask.FromResult(revisionInputError);
        }

        var normalizedReplacePayload = NormalizeObjectPayload<RecordEnvelope>(request.Payload);
        if (normalizedReplacePayload.Value is not { } payload)
        {
            return ValueTask.FromResult(normalizedReplacePayload.Result!);
        }

        foreach (var field in payload.Fields ?? [])
        {
            if (InMemoryValidation.ValidateFieldName<RecordEnvelope>(field.Key) is { } fieldError)
            {
                return ValueTask.FromResult(fieldError);
            }
        }

        lock (_state.Gate)
        {
            var state = GetCollectionOrNull(collection.Id);
            if (state is null || !state.RecordsById.TryGetValue(id.Value, out var current))
            {
                return ValueTask.FromResult(InMemoryResultFactory.NotFound<RecordEnvelope>(id.Value));
            }

            if (expectedRevision is { } expected && !RevisionEquals(current.Metadata.Revision, expected))
            {
                return ValueTask.FromResult(InMemoryResultFactory.RevisionConflict<RecordEnvelope>(expected, current.Metadata.Revision, id.Value));
            }

            var updated = MutateRecord(current, payload, context);
            state.RecordsById[id.Value] = updated;
            var result = OperationResults.Updated(RecordCloneHelpers.CloneEnvelope(updated));
            return ValueTask.FromResult(InMemoryResultFactory.WithRevision(result, updated.Metadata));
        }
    }

    private StoredRecord MutateRecord(StoredRecord current, RecordPayload payload, OperationContext context)
    {
        var revision = NextRevision();
        var metadata = current.Metadata with
        {
            UpdatedAt = Now(context),
            Revision = revision,
            ETag = ETag(revision)
        };
        return current with
        {
            Payload = RecordCloneHelpers.ClonePayload(payload),
            Metadata = metadata
        };
    }

    private InMemoryCollectionState GetOrCreateCollection(string collectionId)
    {
        if (_state.Collections.TryGetValue(collectionId, out var collection))
        {
            return collection;
        }

        collection = new InMemoryCollectionState();
        _state.Collections.Add(collectionId, collection);
        return collection;
    }

    private InMemoryCollectionState? GetCollectionOrNull(string collectionId) =>
        _state.Collections.GetValueOrDefault(collectionId);

    private string NextRecordId() => $"mem:{++_state.NextRecordId:x16}";

    private RevisionToken NextRevision() => new($"mem:{++_state.NextRevision:x16}");

    private static string ETag(RevisionToken revision) => $"\"{revision.Value}\"";

    private static DateTimeOffset Now(OperationContext context) =>
        context.Now == default ? DateTimeOffset.UtcNow : context.Now;

    private static bool RevisionEquals(RevisionToken? left, RevisionToken right) =>
        left is { } current && string.Equals(current.Value, right.Value, StringComparison.Ordinal);

    private static OperationResult<T>? ValidateExpectedRevisionInputs<T>(RevisionToken? requestRevision, RevisionToken? methodRevision)
    {
        if (requestRevision is { } request
            && methodRevision is { } method
            && !string.Equals(request.Value, method.Value, StringComparison.Ordinal))
        {
            return InMemoryResultFactory.Validation<T>(
                InMemoryErrorCodes.ExpectedRevisionConflict,
                "Expected revision inputs must match when both request and method revisions are supplied.");
        }

        return null;
    }

    private static PayloadNormalizeResult<T> NormalizeObjectPayload<T>(RecordPayload payload)
    {
        if (payload is null)
        {
            return PayloadNormalizeResult<T>.Failure(InMemoryResultFactory.Validation<T>(
                InMemoryErrorCodes.PayloadRequired,
                "A record payload is required."));
        }

        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            return PayloadNormalizeResult<T>.Success(new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = RecordCloneHelpers.CloneFields(payload.Fields)
            });
        }

        if (payload.Json.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return PayloadNormalizeResult<T>.Failure(InMemoryResultFactory.Validation<T>(
                InMemoryErrorCodes.ObjectPayloadRequired,
                "JSON record payloads must be objects."));
        }

        var fields = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
        foreach (var property in payload.Json.EnumerateObject())
        {
            fields[property.Name] = property.Value.Clone();
        }

        return PayloadNormalizeResult<T>.Success(new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = fields
        });
    }

    private static PayloadFieldsResult<T> ToFieldMap<T>(RecordPayload payload)
    {
        var normalized = NormalizeObjectPayload<T>(payload);
        return normalized.Value is null
            ? PayloadFieldsResult<T>.Failure(normalized.Result!)
            : PayloadFieldsResult<T>.Success(RecordCloneHelpers.CloneFields(normalized.Value.Fields));
    }

    private static QueryResult<List<StoredRecord>, T> ApplySort<T>(
        List<StoredRecord> records,
        QuerySort[]? sort)
    {
        if (sort is null || sort.Length == 0)
        {
            return QueryResult<List<StoredRecord>, T>.Success(records);
        }

        foreach (var sortField in sort)
        {
            foreach (var record in records)
            {
                if (TryReadField(record.Payload, sortField.Field, out var sortValue)
                    && sortValue.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    return QueryResult<List<StoredRecord>, T>.Failure(InMemoryResultFactory.Validation<T>(
                        InMemoryErrorCodes.InvalidQuery,
                        "Object and array values cannot be used as sort keys.",
                        sortField.Field));
                }
            }
        }

        records.Sort((left, right) =>
        {
            foreach (var sortField in sort)
            {
                var leftPresent = TryReadField(left.Payload, sortField.Field, out var leftValue);
                var rightPresent = TryReadField(right.Payload, sortField.Field, out var rightValue);
                var compare = CompareSortValues(leftPresent, leftValue, rightPresent, rightValue, sortField.Nulls);
                if (compare != 0)
                {
                    return sortField.Direction == QuerySortDirection.Desc ? -compare : compare;
                }
            }

            return string.Compare(left.Id.Value, right.Id.Value, StringComparison.Ordinal);
        });

        return QueryResult<List<StoredRecord>, T>.Success(records);
    }

    private QueryResult<StoredRecord[], T> ApplyPage<T>(
        List<StoredRecord> snapshot,
        RecordQuery query,
        out PageInfo pageInfo)
    {
        var page = query.Page;
        pageInfo = new PageInfo();
        var limit = page?.Limit ?? page?.PerPage ?? _options.DefaultPageSize;

        int offset;
        switch (page?.Mode)
        {
            case QueryPaginationMode.Offset:
                offset = page.Offset ?? 0;
                break;
            case QueryPaginationMode.Cursor:
                if (!DecodeCursor<T>(query, limit, out offset, out var error))
                {
                    return QueryResult<StoredRecord[], T>.Failure(error!);
                }

                break;
            case QueryPaginationMode.Page:
            default:
                offset = ((page?.Page ?? 1) - 1) * limit;
                break;
        }

        var items = snapshot.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + items.Length;
        pageInfo = new PageInfo
        {
            Page = page?.Mode is null or QueryPaginationMode.Page ? page?.Page ?? 1 : null,
            PerPage = page?.Mode is null or QueryPaginationMode.Page ? limit : null,
            Offset = page?.Mode == QueryPaginationMode.Offset ? offset : null,
            Limit = page?.Mode == QueryPaginationMode.Offset ? limit : null,
            Cursor = page?.Cursor,
            NextCursor = nextOffset < snapshot.Count ? EncodeCursor(query, limit, nextOffset) : null,
            HasMore = nextOffset < snapshot.Count
        };
        return QueryResult<StoredRecord[], T>.Success(items);
    }

    private static bool DecodeCursor<T>(
        RecordQuery query,
        int limit,
        out int offset,
        out OperationResult<T>? error)
    {
        offset = 0;
        error = null;
        var cursor = query.Page?.Cursor;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        try
        {
            var bytes = DecodeBase64Url(cursor);
            var text = Encoding.UTF8.GetString(bytes);
            var parts = text.Split(':');
            if (parts is ["v1", var shape, var encodedLimit, var encodedOffset]
                && string.Equals(shape, QueryShapeHash(query, limit), StringComparison.Ordinal)
                && int.TryParse(encodedLimit, NumberStyles.None, CultureInfo.InvariantCulture, out var cursorLimit)
                && cursorLimit == limit
                && int.TryParse(encodedOffset, NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                && offset >= 0)
            {
                return true;
            }
        }
        catch (FormatException)
        {
        }

        error = InMemoryResultFactory.Validation<T>(
            InMemoryErrorCodes.InvalidQuery,
            "Cursor is malformed or unsupported.");
        return false;
    }

    private static string EncodeCursor(RecordQuery query, int limit, int offset) =>
        EncodeBase64Url(Encoding.UTF8.GetBytes(
            string.Create(
                CultureInfo.InvariantCulture,
                $"v1:{QueryShapeHash(query, limit)}:{limit}:{offset}")));

    private static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        var padding = base64.Length % 4;
        if (padding != 0)
        {
            base64 = base64.PadRight(base64.Length + 4 - padding, '=');
        }

        return Convert.FromBase64String(base64);
    }

    private static string QueryShapeHash(RecordQuery query, int limit)
    {
        var builder = new StringBuilder();
        builder.Append("limit=").Append(limit).Append(';');
        builder.Append("count=").Append(query.Count).Append(';');
        AppendFilterShape(builder, query.Filter);
        AppendSortShape(builder, query.Sort);
        AppendStringArrayShape(builder, "select", query.Select);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash.AsSpan(0, 12));
    }

    private static void AppendFilterShape(StringBuilder builder, FilterExpression? filter)
    {
        if (filter is null)
        {
            builder.Append("filter=null;");
            return;
        }

        builder.Append("filter=(")
            .Append(filter.Kind).Append(',')
            .Append(filter.Field).Append(',')
            .Append(filter.Operator).Append(',');
        AppendQueryValueShape(builder, filter.Value);
        AppendQueryValueArrayShape(builder, filter.Values);
        foreach (var child in filter.Children ?? [])
        {
            AppendFilterShape(builder, child);
        }

        builder.Append(')');
    }

    private static void AppendSortShape(StringBuilder builder, QuerySort[]? sort)
    {
        builder.Append("sort=[");
        foreach (var item in sort ?? [])
        {
            builder.Append(item.Field).Append(',')
                .Append(item.Direction).Append(',')
                .Append(item.Nulls).Append(';');
        }

        builder.Append("];");
    }

    private static void AppendStringArrayShape(StringBuilder builder, string name, string[]? values)
    {
        builder.Append(name).Append("=[");
        foreach (var value in values ?? [])
        {
            builder.Append(value).Append(';');
        }

        builder.Append("];");
    }

    private static void AppendQueryValueArrayShape(StringBuilder builder, QueryValue[]? values)
    {
        builder.Append('[');
        foreach (var value in values ?? [])
        {
            AppendQueryValueShape(builder, value);
        }

        builder.Append(']');
    }

    private static void AppendQueryValueShape(StringBuilder builder, QueryValue? value)
    {
        if (value is null)
        {
            builder.Append("null;");
            return;
        }

        builder.Append(value.Kind).Append(':')
            .Append(value.String).Append(':')
            .Append(value.Boolean?.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Integer?.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Number?.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Decimal).Append(':')
            .Append(value.DateTime?.ToString("O", CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Id).Append(':');
        AppendQueryValueArrayShape(builder, value.Array);
        builder.Append(';');
    }

    private static StoredRecord[] ApplySelect(StoredRecord[] records, string[]? select)
    {
        if (select is null || select.Length == 0)
        {
            return records;
        }

        return records.Select(record =>
        {
            var root = new SelectNode();
            foreach (var fieldPath in select)
            {
                if (TryReadField(record.Payload, fieldPath, out var selectedValue))
                {
                    AddSelectedValue(root, fieldPath, selectedValue);
                }
            }

            return record with
            {
                Payload = new RecordPayload
                {
                    Kind = RecordPayloadKind.FieldMap,
                    Fields = MaterializeSelectedFields(root)
                }
            };
        }).ToArray();
    }

    private static void AddSelectedValue(SelectNode root, string fieldPath, JsonElement value)
    {
        var parts = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var part in parts)
        {
            if (!current.Children.TryGetValue(part, out var child))
            {
                child = new SelectNode();
                current.Children.Add(part, child);
            }

            current = child;
        }

        current.Value = value.Clone();
    }

    private static Dictionary<string, JsonElement> MaterializeSelectedFields(SelectNode root)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var child in root.Children)
        {
            fields[child.Key] = MaterializeSelectedValue(child.Value);
        }

        return fields;
    }

    private static JsonElement MaterializeSelectedValue(SelectNode node)
    {
        if (node.Children.Count == 0 && node.Value is { } value)
        {
            return value.Clone();
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteSelectedObject(writer, node);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteSelectedObject(Utf8JsonWriter writer, SelectNode node)
    {
        writer.WriteStartObject();
        foreach (var child in node.Children)
        {
            writer.WritePropertyName(child.Key);
            if (child.Value.Children.Count == 0 && child.Value.Value is { } value)
            {
                value.WriteTo(writer);
            }
            else
            {
                WriteSelectedObject(writer, child.Value);
            }
        }

        writer.WriteEndObject();
    }

    private OperationResult<T>? ValidateUnsupportedQuery<T>(RecordQuery query, bool allowCount)
    {
        if (query.Include is { Length: > 0 })
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Includes are not supported by HPD.BASE InMemory.");
        }

        if (query.Extensions is { Length: > 0 })
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Query extensions are not supported by HPD.BASE InMemory.");
        }

        if (query.RequestDependencyToken)
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Dependency tokens are not supported by HPD.BASE InMemory.");
        }

        if (!allowCount && query.Count != QueryCountMode.None)
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Streaming does not support count modes.");
        }

        if (query.Count is QueryCountMode.Estimated or QueryCountMode.Limited)
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Estimated and limited count modes are not supported by HPD.BASE InMemory.");
        }

        if ((query.Sort?.Length ?? 0) > _options.MaxSortFields)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query contains too many sort fields.");
        }

        if ((query.Select?.Length ?? 0) > _options.MaxSelectFields)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query contains too many selected fields.");
        }

        foreach (var selectedField in query.Select ?? [])
        {
            var segments = selectedField.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0
                || selectedField.StartsWith(".", StringComparison.Ordinal)
                || selectedField.EndsWith(".", StringComparison.Ordinal)
                || selectedField.Contains("..", StringComparison.Ordinal)
                || segments.Any(segment => InMemoryValidation.ValidateFieldName<T>(segment) is not null))
            {
                return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Selected payload fields must be valid field paths.");
            }
        }

        if (query.Page?.Limit is < 0 || query.Page?.PerPage is < 0 || query.Page?.Offset is < 0)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query pagination values must be non-negative.");
        }

        if (query.Page?.Page is <= 0)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Page mode is one-based.");
        }

        if ((query.Page?.Limit ?? query.Page?.PerPage) is { } requestedLimit && requestedLimit > _options.MaxPageSize)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query page size exceeds the InMemory store limit.");
        }

        if (query.Filter is not null)
        {
            var nodeCount = 0;
            if (ValidateFilter<T>(query.Filter, depth: 1, ref nodeCount) is { } filterError)
            {
                return filterError;
            }
        }

        return null;
    }

    private OperationResult<T>? ValidateFilter<T>(FilterExpression filter, int depth, ref int nodeCount)
    {
        nodeCount++;
        if (depth > _options.MaxFilterDepth || nodeCount > _options.MaxFilterNodes)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query filter exceeds InMemory limits.");
        }

        var error = filter.Kind switch
        {
            FilterNodeKind.Extension => InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Filter extensions are not supported by HPD.BASE InMemory."),
            FilterNodeKind.Compare when filter.Operator is FilterOperator.Like or FilterOperator.NotLike => InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Like and not-like filters are not supported by HPD.BASE InMemory."),
            FilterNodeKind.Compare when string.IsNullOrWhiteSpace(filter.Field) || filter.Value is null => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Compare filters require a field and value."),
            FilterNodeKind.In when string.IsNullOrWhiteSpace(filter.Field) || filter.Values is not { Length: > 0 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "In filters require a field and values."),
            FilterNodeKind.In when filter.Values!.Length > _options.MaxInValues => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "In filters contain too many values."),
            FilterNodeKind.Between when string.IsNullOrWhiteSpace(filter.Field) || filter.Values is not { Length: 2 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Between filters require a field and exactly two values."),
            FilterNodeKind.IsNull or FilterNodeKind.IsDefined when string.IsNullOrWhiteSpace(filter.Field) => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Field filter nodes require a field."),
            FilterNodeKind.Not when filter.Children is not { Length: 1 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Not filters require exactly one child."),
            FilterNodeKind.And or FilterNodeKind.Or when filter.Children is not { Length: > 0 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Boolean filters require children."),
            _ => null
        };

        if (error is not null)
        {
            return error;
        }

        foreach (var child in filter.Children ?? [])
        {
            if (ValidateFilter<T>(child, depth + 1, ref nodeCount) is { } childError)
            {
                return childError;
            }
        }

        return null;
    }

    private static bool MatchesFilter(StoredRecord record, FilterExpression filter) =>
        filter.Kind switch
        {
            FilterNodeKind.True => true,
            FilterNodeKind.False => false,
            FilterNodeKind.Not => filter.Children is [{ } child] && !MatchesFilter(record, child),
            FilterNodeKind.And => filter.Children is { Length: > 0 } children && children.All(child => MatchesFilter(record, child)),
            FilterNodeKind.Or => filter.Children is { Length: > 0 } children && children.Any(child => MatchesFilter(record, child)),
            FilterNodeKind.Compare => MatchesCompare(record, filter),
            FilterNodeKind.In => MatchesIn(record, filter),
            FilterNodeKind.Between => MatchesBetween(record, filter),
            FilterNodeKind.IsNull => TryReadField(record.Payload, filter.Field, out var value) && value.ValueKind == JsonValueKind.Null,
            FilterNodeKind.IsDefined => TryReadField(record.Payload, filter.Field, out _),
            _ => false
        };

    private static bool MatchesCompare(StoredRecord record, FilterExpression filter)
    {
        if (!TryReadField(record.Payload, filter.Field, out var fieldValue) || filter.Value is null)
        {
            return false;
        }

        return filter.Operator switch
        {
            FilterOperator.Equal => ValueEquals(fieldValue, filter.Value),
            FilterOperator.NotEqual => !ValueEquals(fieldValue, filter.Value),
            FilterOperator.LessThan => CompareValues(fieldValue, filter.Value) is < 0,
            FilterOperator.LessThanOrEqual => CompareValues(fieldValue, filter.Value) is <= 0,
            FilterOperator.GreaterThan => CompareValues(fieldValue, filter.Value) is > 0,
            FilterOperator.GreaterThanOrEqual => CompareValues(fieldValue, filter.Value) is >= 0,
            FilterOperator.Contains => ContainsValue(fieldValue, filter.Value),
            FilterOperator.NotContains => !ContainsValue(fieldValue, filter.Value),
            FilterOperator.StartsWith => fieldValue.ValueKind == JsonValueKind.String
                && filter.Value.String is { } prefix
                && (fieldValue.GetString() ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal),
            FilterOperator.EndsWith => fieldValue.ValueKind == JsonValueKind.String
                && filter.Value.String is { } suffix
                && (fieldValue.GetString() ?? string.Empty).EndsWith(suffix, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool MatchesIn(StoredRecord record, FilterExpression filter)
    {
        if (!TryReadField(record.Payload, filter.Field, out var fieldValue) || filter.Values is null)
        {
            return false;
        }

        return fieldValue.ValueKind == JsonValueKind.Array
            ? fieldValue.EnumerateArray().Any(item => filter.Values.Any(queryValue => ValueEquals(item, queryValue)))
            : filter.Values.Any(queryValue => ValueEquals(fieldValue, queryValue));
    }

    private static bool MatchesBetween(StoredRecord record, FilterExpression filter)
    {
        if (!TryReadField(record.Payload, filter.Field, out var fieldValue)
            || filter.Values is not [{ } lower, { } upper])
        {
            return false;
        }

        return CompareValues(fieldValue, lower) is >= 0 && CompareValues(fieldValue, upper) is <= 0;
    }

    private static bool TryReadField(RecordPayload payload, string? fieldPath, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return false;
        }

        var parts = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            if (payload.Fields?.TryGetValue(parts[0], out value) != true)
            {
                return false;
            }
        }
        else
        {
            value = payload.Json;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[0], out value))
            {
                return false;
            }
        }

        for (var index = 1; index < parts.Length; index++)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[index], out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValueEquals(JsonElement fieldValue, QueryValue queryValue)
    {
        if (queryValue.Kind == QueryValueKind.Null)
        {
            return fieldValue.ValueKind == JsonValueKind.Null;
        }

        if (TryDecimal(fieldValue, out var fieldDecimal) && TryDecimal(queryValue, out var queryDecimal))
        {
            return fieldDecimal == queryDecimal;
        }

        if (queryValue.Kind == QueryValueKind.Boolean && fieldValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return fieldValue.GetBoolean() == queryValue.Boolean;
        }

        var fieldString = ScalarString(fieldValue);
        var queryString = ScalarString(queryValue);
        return fieldString is not null
            && queryString is not null
            && string.Equals(fieldString, queryString, StringComparison.Ordinal);
    }

    private static int? CompareValues(JsonElement fieldValue, QueryValue queryValue)
    {
        if (TryDecimal(fieldValue, out var fieldDecimal) && TryDecimal(queryValue, out var queryDecimal))
        {
            return fieldDecimal.CompareTo(queryDecimal);
        }

        if (queryValue.DateTime is { } queryDate
            && fieldValue.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(fieldValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fieldDate))
        {
            return fieldDate.ToUniversalTime().CompareTo(queryDate.ToUniversalTime());
        }

        var fieldString = ScalarString(fieldValue);
        var queryString = ScalarString(queryValue);
        return fieldString is null || queryString is null
            ? null
            : string.Compare(fieldString, queryString, StringComparison.Ordinal);
    }

    private static bool ContainsValue(JsonElement fieldValue, QueryValue queryValue)
    {
        if (fieldValue.ValueKind == JsonValueKind.Array)
        {
            return fieldValue.EnumerateArray().Any(item => ValueEquals(item, queryValue));
        }

        return fieldValue.ValueKind == JsonValueKind.String
            && queryValue.String is { } text
            && (fieldValue.GetString() ?? string.Empty).Contains(text, StringComparison.Ordinal);
    }

    private static int CompareSortValues(
        bool leftPresent,
        JsonElement left,
        bool rightPresent,
        JsonElement right,
        QueryNullOrder nullOrder)
    {
        var leftNull = !leftPresent || left.ValueKind == JsonValueKind.Null;
        var rightNull = !rightPresent || right.ValueKind == JsonValueKind.Null;
        if (leftNull || rightNull)
        {
            if (leftNull && rightNull)
            {
                return 0;
            }

            return nullOrder == QueryNullOrder.First
                ? leftNull ? -1 : 1
                : leftNull ? 1 : -1;
        }

        if (TryDecimal(left, out var leftDecimal) && TryDecimal(right, out var rightDecimal))
        {
            return leftDecimal.CompareTo(rightDecimal);
        }

        return string.Compare(ScalarString(left), ScalarString(right), StringComparison.Ordinal);
    }

    private static bool TryDecimal(JsonElement element, out decimal value)
    {
        value = default;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryDecimal(QueryValue queryValue, out decimal value)
    {
        value = default;
        return queryValue.Kind switch
        {
            QueryValueKind.Integer when queryValue.Integer is { } integer => TryAssign(integer, out value),
            QueryValueKind.Number when queryValue.Number is { } number && double.IsFinite(number) => TryAssign((decimal)number, out value),
            QueryValueKind.Decimal when queryValue.Decimal is { } text => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryAssign(decimal input, out decimal value)
    {
        value = input;
        return true;
    }

    private static string? ScalarString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };

    private static string? ScalarString(QueryValue queryValue) =>
        queryValue.Kind switch
        {
            QueryValueKind.String => queryValue.String,
            QueryValueKind.Id => queryValue.Id,
            QueryValueKind.Integer => queryValue.Integer?.ToString(CultureInfo.InvariantCulture),
            QueryValueKind.Number => queryValue.Number?.ToString(CultureInfo.InvariantCulture),
            QueryValueKind.Decimal => queryValue.Decimal,
            QueryValueKind.Boolean => queryValue.Boolean?.ToString(),
            QueryValueKind.DateTime => queryValue.DateTime?.ToString("O", CultureInfo.InvariantCulture),
            _ => null
        };

    private static void ValidateOptions(HPDBaseInMemoryOptions options)
    {
        ValidateStableId(options.StoreId, nameof(options.StoreId));
        ValidateStableId(options.ModuleId, nameof(options.ModuleId));
        ValidateStableId(options.HealthRefId, nameof(options.HealthRefId));
        ValidateStableId(options.DiagnosticRefId, nameof(options.DiagnosticRefId));
        if (options.DefaultPageSize <= 0 || options.MaxPageSize <= 0 || options.DefaultPageSize > options.MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(options.DefaultPageSize), "Default and maximum page sizes must be positive and ordered.");
        }

        if (options.CollectionIds.Any(id => !InMemoryValidation.IsValidIdText(id)))
        {
            throw new ArgumentException("Collection ids must be non-empty and contain no control characters.", nameof(options.CollectionIds));
        }

        if (options.CollectionIds.Length > 0 && options.Collections is { Length: > 0 } collections)
        {
            var configured = options.CollectionIds.Order(StringComparer.Ordinal).ToArray();
            var contributed = collections.Select(collection => collection.Id).Order(StringComparer.Ordinal).ToArray();
            if (!configured.SequenceEqual(contributed, StringComparer.Ordinal))
            {
                throw new ArgumentException("CollectionIds and Collections must contain the same collection ids when both are configured.", nameof(options.Collections));
            }
        }
    }

    private static void ValidateStableId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl))
        {
            throw new ArgumentException("Identifier values must be trimmed and contain no control characters.", parameterName);
        }
    }

    private static StoreCapabilityDescriptor CreateCapabilities(HPDBaseInMemoryOptions options) => new()
    {
        StoreId = options.StoreId,
        StoreKind = BaseStoreKinds.InMemory,
        StoreVersion = options.StoreVersion,
        Crud = new CrudCapability
        {
            List = true,
            Get = true,
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
            Filter = new FilterCapability
            {
                Supported = true,
                Operators =
                [
                    FilterOperator.Equal,
                    FilterOperator.NotEqual,
                    FilterOperator.LessThan,
                    FilterOperator.LessThanOrEqual,
                    FilterOperator.GreaterThan,
                    FilterOperator.GreaterThanOrEqual,
                    FilterOperator.Contains,
                    FilterOperator.NotContains,
                    FilterOperator.StartsWith,
                    FilterOperator.EndsWith
                ],
                BooleanComposition = true,
                Not = true,
                NullChecks = true,
                MissingFieldChecks = true,
                NestedFieldPaths = true,
                ArrayMembership = true,
                MaxDepth = options.MaxFilterDepth,
                MaxNodes = options.MaxFilterNodes,
                MaxSerializedLength = options.MaxSerializedQueryLength,
                ExecutionMode = QueryExecutionMode.Native
            },
            Sort = new SortCapability
            {
                Supported = true,
                MaxFields = options.MaxSortFields,
                NestedFieldPaths = true,
                NullOrdering = true,
                StableTieBreaker = true
            },
            Pagination = new PaginationCapability
            {
                Page = true,
                Offset = true,
                Cursor = true,
                DefaultLimit = options.DefaultPageSize,
                MaxLimit = options.MaxPageSize,
                CursorRequiresStableSort = true
            },
            Count = new CountCapability
            {
                SupportedModes = [QueryCountMode.None, QueryCountMode.IfAvailable, QueryCountMode.Exact]
            },
            Select = new SelectCapability
            {
                PayloadFields = true,
                NestedFieldPaths = true
            }
        },
        Revision = new RevisionCapability
        {
            Supported = true,
            Guarantee = RevisionGuarantee.Store,
            Patch = true,
            Delete = true
        },
        Streaming = new StreamingCapability
        {
            Supported = options.EnableStreamingCapability,
            MaxItems = options.MaxStreamItems,
            RequiresStableSort = true
        }
    };

    private readonly record struct PayloadNormalizeResult<T>(RecordPayload? Value, OperationResult<T>? Result)
    {
        public static PayloadNormalizeResult<T> Success(RecordPayload payload) => new(payload, null);
        public static PayloadNormalizeResult<T> Failure(OperationResult<T> result) => new(null, result);
    }

    private readonly record struct PayloadFieldsResult<T>(Dictionary<string, System.Text.Json.JsonElement>? Value, OperationResult<T>? Result)
    {
        public static PayloadFieldsResult<T> Success(Dictionary<string, System.Text.Json.JsonElement> fields) => new(fields, null);
        public static PayloadFieldsResult<T> Failure(OperationResult<T> result) => new(null, result);
    }

    private readonly record struct QueryResult<TValue, TResult>(TValue? Value, OperationResult<TResult>? Result)
        where TValue : class
    {
        public static QueryResult<TValue, TResult> Success(TValue value) => new(value, null);
        public static QueryResult<TValue, TResult> Failure(OperationResult<TResult> result) => new(null, result);
    }

    private sealed class SelectNode
    {
        public JsonElement? Value { get; set; }
        public Dictionary<string, SelectNode> Children { get; } = new(StringComparer.Ordinal);
    }
}
