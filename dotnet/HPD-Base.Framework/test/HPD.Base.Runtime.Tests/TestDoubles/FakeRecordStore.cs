using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Stores;

namespace HPD.Base.Runtime.Tests;

internal class FakeRecordStore : IRecordStore
{
    protected readonly Dictionary<string, RecordEnvelope> Records = new(StringComparer.Ordinal);

    public FakeRecordStore(string storeId, CrudCapability? crud = null, RevisionCapability? revision = null)
    {
        Capabilities = new StoreCapabilityDescriptor
        {
            StoreId = storeId,
            StoreKind = BaseStoreKinds.Custom,
            StoreVersion = "test",
            Crud = crud ?? new CrudCapability
            {
                List = true,
                Get = true,
                Create = true,
                Patch = true,
                Replace = true,
                Delete = true
            },
            Query = new QueryCapability
            {
                Filter = new FilterCapability
                {
                    Supported = true,
                    BooleanComposition = true,
                    Not = true,
                    NullChecks = true,
                    MissingFieldChecks = true
                },
                Sort = new SortCapability { Supported = true, NullOrdering = true },
                Pagination = new PaginationCapability
                {
                    Page = true,
                    Offset = true,
                    Cursor = true,
                    MaxLimit = 1_000
                },
                Count = new CountCapability
                {
                    SupportedModes =
                    [
                        QueryCountMode.None,
                        QueryCountMode.IfAvailable,
                        QueryCountMode.Exact,
                        QueryCountMode.Estimated,
                        QueryCountMode.Limited
                    ]
                },
                Select = new SelectCapability { PayloadFields = true },
                Include = new QueryIncludeCapability
                {
                    Supported = true,
                    IncludeFilters = true,
                    IncludeSort = true,
                    IncludeLimit = true
                }
            },
            Revision = revision
        };
    }

    public StoreCapabilityDescriptor Capabilities { get; }
    public RecordQuery? LastListQuery { get; private set; }
    public int ListCalls { get; private set; }
    public int GetCalls { get; private set; }
    public int CreateCalls { get; private set; }
    public int PatchCalls { get; protected set; }
    public int ReplaceCalls { get; protected set; }
    public int DeleteCalls { get; private set; }
    public RecordCreateRequest? LastCreateRequest { get; private set; }
    public RecordPatchRequest? LastPatchRequest { get; protected set; }
    public RecordReplaceRequest? LastReplaceRequest { get; protected set; }

    public void AddRecord(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Records[record.Id.Value] = record;
    }

    public ValueTask<OperationResult<RecordPage>> ListAsync(CollectionDefinition collection, RecordQuery query, OperationContext context, CancellationToken cancellationToken = default)
    {
        ListCalls++;
        LastListQuery = query;
        return ValueTask.FromResult(new OperationResult<RecordPage>
        {
            Status = OperationStatus.Ok,
            Value = new RecordPage
            {
                Items = Records.Values.ToArray(),
                Page = new PageInfo { Limit = Records.Count, HasMore = false }
            }
        });
    }

    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(CollectionDefinition collection, RecordId id, OperationContext context, CancellationToken cancellationToken = default)
    {
        GetCalls++;
        return ValueTask.FromResult(Records.TryGetValue(id.Value, out var record)
            ? new OperationResult<RecordEnvelope> { Status = OperationStatus.Ok, Value = record }
            : new OperationResult<RecordEnvelope>
            {
                Status = OperationStatus.NotFound,
                Error = new BaseError { Code = "notFound", Message = "Not found.", Category = ErrorCategory.NotFound }
            });
    }

    public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(CollectionDefinition collection, RecordCreateRequest request, OperationContext context, CancellationToken cancellationToken = default)
    {
        CreateCalls++;
        LastCreateRequest = request;
        var id = request.RequestedId ?? new RecordId($"rec_{CreateCalls}");
        var record = new RecordEnvelope
        {
            CollectionId = collection.Id,
            Id = id,
            Payload = request.Payload,
            Metadata = new RecordMetadata()
        };
        Records[id.Value] = record;
        return ValueTask.FromResult(new OperationResult<RecordEnvelope>
        {
            Status = OperationStatus.Created,
            Value = record
        });
    }

    public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(CollectionDefinition collection, RecordId id, RecordPatchRequest request, OperationContext context, CancellationToken cancellationToken = default)
    {
        PatchCalls++;
        LastPatchRequest = request;
        return UpsertPayload(collection, id, request.Patch);
    }

    public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(CollectionDefinition collection, RecordId id, RecordReplaceRequest request, OperationContext context, CancellationToken cancellationToken = default)
    {
        ReplaceCalls++;
        LastReplaceRequest = request;
        return UpsertPayload(collection, id, request.Payload);
    }

    public ValueTask<OperationResult<DeleteResult>> DeleteAsync(CollectionDefinition collection, RecordId id, RecordDeleteRequest request, OperationContext context, CancellationToken cancellationToken = default)
    {
        DeleteCalls++;
        Records.TryGetValue(id.Value, out var previous);
        return ValueTask.FromResult(new OperationResult<DeleteResult>
        {
            Status = OperationStatus.Deleted,
            Value = new DeleteResult { Id = id, Deleted = Records.Remove(id.Value), Previous = request.ReturnPrevious ? previous : null }
        });
    }

    protected ValueTask<OperationResult<RecordEnvelope>> UpsertPayload(CollectionDefinition collection, RecordId id, RecordPayload payload)
    {
        var record = new RecordEnvelope
        {
            CollectionId = collection.Id,
            Id = id,
            Payload = payload,
            Metadata = new RecordMetadata()
        };
        Records[id.Value] = record;
        return ValueTask.FromResult(new OperationResult<RecordEnvelope>
        {
            Status = OperationStatus.Updated,
            Value = record
        });
    }
}
