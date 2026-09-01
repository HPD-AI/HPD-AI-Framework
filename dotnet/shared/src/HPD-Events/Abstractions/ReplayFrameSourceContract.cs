#pragma warning restore CS1591

namespace HPD.Events;

/// <summary>Declares the stronger ordering, finality, and reuse promises required for complete frames.</summary>
/// <param name="TimestampOrder">The source's effective timestamp ordering promise.</param>
/// <param name="Finality">How the source proves no earlier timestamp can still arrive.</param>
/// <param name="Cardinality">Whether the source can create more than one read enumerator.</param>
public sealed record ReplayFrameSourceContract(ReplayTimestampOrder TimestampOrder, ReplayTimestampFinality Finality, ReplaySourceCardinality Cardinality);

/// <summary>Identifies a source's effective timestamp ordering contract.</summary>
public enum ReplayTimestampOrder : byte
{
    /// <summary>No complete-frame ordering promise is made.</summary>
    Unspecified,
    /// <summary>Effective timestamps never decrease.</summary>
    Nondecreasing
}

/// <summary>Identifies how a source establishes timestamp finality.</summary>
public enum ReplayTimestampFinality : byte
{
    /// <summary>The source cannot establish complete timestamp boundaries.</summary>
    None,
    /// <summary>Finite source completion establishes finality.</summary>
    Completion,
    /// <summary>Nondecreasing exclusive watermarks establish finality.</summary>
    ExclusiveWatermark
}

/// <summary>Identifies whether a source supports repeated enumeration.</summary>
public enum ReplaySourceCardinality : byte
{
    /// <summary>The source can be read only once.</summary>
    SingleUse,
    /// <summary>The source supplies a fresh enumerator for every read.</summary>
    Repeatable
}
