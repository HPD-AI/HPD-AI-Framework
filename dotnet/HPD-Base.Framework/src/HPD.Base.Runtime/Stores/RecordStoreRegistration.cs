using HPD.Base.Descriptors;
using HPD.Base.Stores;

namespace HPD.Base.Runtime.Stores;

public sealed record RecordStoreRegistration
{
    public required string StoreId { get; init; }
    public required IRecordStore Store { get; init; }
    public string[]? CollectionIds { get; init; }
    public HealthRefDescriptor[]? HealthRefs { get; init; }
    public DiagnosticRefDescriptor[]? DiagnosticRefs { get; init; }
}
