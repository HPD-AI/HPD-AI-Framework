using Rhodium.Primitives;

namespace Rhodium.Quant;

/// <summary>
/// Request for background quant computation.
/// Carries gating key (Sequence, BatchMapVersion) for deterministic re-entry.
/// </summary>
public readonly record struct QuantRequest(Sequence Sequence, int BatchMapVersion);
