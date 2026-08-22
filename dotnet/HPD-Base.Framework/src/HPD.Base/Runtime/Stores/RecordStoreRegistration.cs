
namespace HPD.Base;

/// <summary>Binds one exact record-store instance to its Runtime identity and collections.</summary>
public sealed record RecordStoreRegistration
{
    /// <summary>Gets the Runtime-unique store identifier.</summary>
    public required string StoreId { get; init; }

    /// <summary>Gets the exact registered store instance.</summary>
    public required IRecordStore Store { get; init; }

    /// <summary>
    /// Gets the internal atomic execution override while <see cref="Store"/> remains
    /// the authoritative provider capability surface.
    /// </summary>
    /// <remarks>
    /// Production registrations leave this value unset. Testing infrastructure may
    /// install a capability-transparent atomic interceptor without replacing or
    /// concealing provider-specific schema, administration, read, subject, or
    /// activation contracts.
    /// </remarks>
    internal IAtomicRecordStore? AtomicExecutionStore { get; init; }

    /// <summary>Gets the collection identifiers explicitly assigned to this store.</summary>
    public string[]? CollectionIds { get; init; }

    /// <summary>Gets health references contributed by this store registration.</summary>
    public HealthRefDescriptor[]? HealthRefs { get; init; }

    /// <summary>Gets diagnostic references contributed by this store registration.</summary>
    public DiagnosticRefDescriptor[]? DiagnosticRefs { get; init; }
}
