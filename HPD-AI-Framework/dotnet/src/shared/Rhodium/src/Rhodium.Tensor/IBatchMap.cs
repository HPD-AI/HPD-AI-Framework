using Rhodium.Primitives;

namespace Rhodium.Tensor;

/// <summary>
/// Maps (Instrument, Variant) to VirtualIndex and provides reverse lookup.
/// Tracks universe topology changes via Version property.
/// </summary>
public interface IBatchMap
{
    /// <summary>
    /// Monotonic universe version.
    /// MUST increment on any of:
    /// - instrument added/removed
    /// - reordering of virtual indices
    /// - variant permutation changes
    ///
    /// Used for topology safety and quant gating (invalidate stale handles/results).
    /// </summary>
    int Version { get; }

    /// <summary>
    /// Total number of virtual indices in the universe.
    /// </summary>
    int TotalSize { get; }

    /// <summary>
    /// Current tensor basis (rectangular projection).
    /// </summary>
    TensorBasis CurrentBasis { get; }

    /// <summary>
    /// Get the range of virtual indices for a given instrument.
    /// Returns (start index, length).
    /// </summary>
    (int Start, int Length) GetInstrumentRange(Instrument instrument);

    /// <summary>
    /// Get the context (Instrument, VariantId) for a given virtual index.
    /// </summary>
    (Instrument Inst, int VariantId) GetContext(int virtualIndex);

    /// <summary>
    /// Safe context lookup for initialization/padding.
    /// Returns Unknown instrument if index is out of bounds.
    /// </summary>
    (Instrument Inst, int VariantId) SafeGetContext(int virtualIndex)
    {
        if (virtualIndex >= TotalSize) return (Instrument.Unknown, 0);
        return GetContext(virtualIndex);
    }
}
