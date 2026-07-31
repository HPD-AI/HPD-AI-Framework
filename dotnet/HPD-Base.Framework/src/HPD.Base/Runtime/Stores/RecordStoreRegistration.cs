
namespace HPD.Base;

/// <summary>Binds one exact record-store instance to its Runtime identity and collections.</summary>
public sealed record RecordStoreRegistration
{
    /// <summary>Gets the Runtime-unique store identifier.</summary>
    public required string StoreId { get; init; }

    /// <summary>Gets the exact registered store instance.</summary>
    public required IRecordStore Store { get; init; }

    /// <summary>Gets the collection identifiers explicitly assigned to this store.</summary>
    public string[]? CollectionIds { get; init; }

    /// <summary>Gets health references contributed by this store registration.</summary>
    public HealthRefDescriptor[]? HealthRefs { get; init; }

    /// <summary>Gets diagnostic references contributed by this store registration.</summary>
    public DiagnosticRefDescriptor[]? DiagnosticRefs { get; init; }
}
