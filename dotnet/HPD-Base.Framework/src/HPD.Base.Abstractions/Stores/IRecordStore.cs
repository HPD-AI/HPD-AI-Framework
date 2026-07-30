using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;

namespace HPD.Base.Stores;

/// <summary>Provides portable record reads and declares one store instance's capabilities.</summary>
public interface IRecordStore
{
    /// <summary>Gets the capabilities implemented by this exact store instance.</summary>
    StoreCapabilityDescriptor Capabilities { get; }

    /// <summary>Lists records through the provider's portable query implementation.</summary>
    ValueTask<OperationResult<RecordPage>> ListAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one record by its stable identifier.</summary>
    ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default);

}
