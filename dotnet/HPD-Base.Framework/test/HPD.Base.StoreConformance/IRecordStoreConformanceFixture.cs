namespace HPD.Base.StoreConformance;

/// <summary>
/// Provider-owned factory used by direct record store conformance suites.
/// </summary>
public interface IRecordStoreConformanceFixture
{
    string ProviderName { get; }

    StoreCapabilityDescriptor Capabilities { get; }

    CollectionDefinition Collection { get; }

    OperationContext Operation(BaseOperationKind operation, RecordId? id = null);

    ValueTask<IRecordStore> CreateStoreAsync(CancellationToken cancellationToken = default);

    ValueTask ResetAsync(CancellationToken cancellationToken = default);
}

public interface IRecordStoreConformanceSeeder
{
    ValueTask<RecordEnvelope> CreateRecordAsync(
        IRecordStore store,
        CollectionDefinition collection,
        string id,
        params (string Field, JsonElement Value)[] fields);
}

public interface IStreamingRecordStoreConformanceExpectations
{
    bool ExpectsSnapshotStreams { get; }

    bool ExpectsEnumerationCancellation { get; }
}
