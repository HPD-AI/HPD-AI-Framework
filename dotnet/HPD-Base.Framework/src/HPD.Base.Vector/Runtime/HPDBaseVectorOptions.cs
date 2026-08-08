namespace HPD.Base;

/// <summary>Configures bounded provider-neutral vector execution.</summary>
public sealed class HPDBaseVectorOptions
{
    /// <summary>Gets or sets the maximum vector dimensions.</summary>
    public int MaxDimensions { get; set; } = 4_096;
    /// <summary>Gets or sets the maximum top-K.</summary>
    public int MaxTopK { get; set; } = 100;
    /// <summary>Gets or sets the maximum declared filter fields.</summary>
    public int MaxFilterFields { get; set; } = 16;
    /// <summary>Gets or sets the provider/query deadline.</summary>
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the consistency wait deadline.</summary>
    public TimeSpan ConsistencyWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the issued consistency-token lifetime.</summary>
    public TimeSpan ConsistencyTokenLifetime { get; set; } = TimeSpan.FromHours(24);
    /// <summary>Gets or sets the active-plus-quarantined operation cap.</summary>
    public int MaxActiveAndQuarantinedOperations { get; set; } = 8;
    /// <summary>Gets or sets the shutdown drain deadline.</summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the administration deadline.</summary>
    public TimeSpan AdministrationTimeout { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Gets or sets the maximum concurrent rebuilds per store.</summary>
    public int MaxConcurrentRebuilds { get; set; } = 1;
    /// <summary>Gets or sets the explicit default used only by derived-journal providers.</summary>
    public BaseVectorConsistencyRequirement? DerivedProviderDefaultConsistency { get; set; }

    internal void Validate()
    {
        if (MaxDimensions is < 1 or > 32_768 || MaxTopK is < 1 or > 1_000 || MaxFilterFields is < 1 or > 32 || MaxActiveAndQuarantinedOperations is < 1 or > 64 || MaxConcurrentRebuilds is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(HPDBaseVectorOptions), "One or more vector limits are outside their supported range.");
        InRange(ProviderTimeout, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(2), nameof(ProviderTimeout));
        InRange(ConsistencyWaitTimeout, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(2), nameof(ConsistencyWaitTimeout));
        InRange(ConsistencyTokenLifetime, TimeSpan.FromMinutes(1), TimeSpan.FromDays(30), nameof(ConsistencyTokenLifetime));
        InRange(ShutdownDrainTimeout, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1), nameof(ShutdownDrainTimeout));
        InRange(AdministrationTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), nameof(AdministrationTimeout));
        if (DerivedProviderDefaultConsistency is not null and not BaseVectorConsistencyRequirement.Available and not BaseVectorConsistencyRequirement.BoundedStaleness)
            throw new ArgumentException("A derived-provider default must be Available or BoundedStaleness.", nameof(DerivedProviderDefaultConsistency));
        if (DerivedProviderDefaultConsistency is BaseVectorConsistencyRequirement.BoundedStaleness bounded && bounded.MaximumAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DerivedProviderDefaultConsistency));
    }
    private static void InRange(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string name) { if (value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name); }
}

internal sealed record HPDBaseVectorSnapshot(int MaxDimensions, int MaxTopK, int MaxFilterFields, TimeSpan ProviderTimeout, TimeSpan ConsistencyWaitTimeout, TimeSpan ConsistencyTokenLifetime, int MaxActiveAndQuarantinedOperations, TimeSpan ShutdownDrainTimeout, TimeSpan AdministrationTimeout, int MaxConcurrentRebuilds, BaseVectorConsistencyRequirement? DerivedProviderDefaultConsistency);
