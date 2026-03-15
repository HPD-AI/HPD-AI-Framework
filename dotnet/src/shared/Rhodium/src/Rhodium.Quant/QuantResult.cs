using Rhodium.Primitives;

namespace Rhodium.Quant;

/// <summary>
/// Result payload for gated re-entry from background quant computations.
/// </summary>
public sealed class QuantResult
{
    /// <summary>
    /// Sequence number when the computation was initiated.
    /// Used for gating - result is only accepted if sequence matches.
    /// </summary>
    public Sequence Sequence { get; init; }

    /// <summary>
    /// BatchMap version when the computation was initiated.
    /// Used for topology safety - result is only accepted if version matches.
    /// </summary>
    public int BatchMapVersion { get; init; }

    /// <summary>
    /// Name or identifier of the computation that produced this result.
    /// </summary>
    public string ComputationName { get; init; } = string.Empty;

    /// <summary>
    /// The actual result data.
    /// Type depends on the specific computation performed.
    /// </summary>
    public object? Data { get; init; }
}
