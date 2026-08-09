namespace HPD.Base;

/// <summary>Defines the caller's explicit vector consistency requirement.</summary>
public abstract record BaseVectorConsistencyRequirement
{
    private BaseVectorConsistencyRequirement() { }
    /// <summary>Requires the finite authoritative high-water captured for this request.</summary>
    public sealed record Current : BaseVectorConsistencyRequirement;
    /// <summary>Requires at least the authority represented by an opaque token.</summary>
    public sealed record AtLeast(BaseVectorConsistencyToken Token) : BaseVectorConsistencyRequirement;
    /// <summary>Allows a bounded amount of derived-index staleness.</summary>
    public sealed record BoundedStaleness(TimeSpan MaximumAge) : BaseVectorConsistencyRequirement;
    /// <summary>Uses the provider's currently available finite watermark.</summary>
    public sealed record Available : BaseVectorConsistencyRequirement;
}
