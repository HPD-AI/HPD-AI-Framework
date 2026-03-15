using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Kernel;

/// <summary>
/// Concrete implementation of IBatchMap for strategy virtual universe mapping.
/// Maintains instrument-to-virtual-index mapping with variant support.
/// </summary>
public sealed class BatchMap : IBatchMap
{
    private readonly Dictionary<Instrument, (int Start, int Length)> _instrumentRanges = new();
    private readonly List<(Instrument Instrument, int VariantId)> _contexts = new();
    private TensorBasis _currentBasis;
    private int _version;

    public int Version => _version;
    public int TotalSize => _contexts.Count;
    public TensorBasis CurrentBasis => _currentBasis;

    /// <summary>
    /// Adds an instrument with the specified number of variants to the universe.
    /// Returns the starting virtual index for this instrument.
    /// </summary>
    public int AddInstrument(Instrument instrument, int variantCount = 1)
    {
        if (_instrumentRanges.ContainsKey(instrument))
            throw new InvalidOperationException($"Instrument {instrument} already exists in universe");

        var start = _contexts.Count;

        // Add all variants for this instrument
        for (int i = 0; i < variantCount; i++)
        {
            _contexts.Add((instrument, i));
        }

        _instrumentRanges[instrument] = (start, variantCount);

        // Update basis
        _currentBasis = new TensorBasis(_instrumentRanges.Count, variantCount);
        _version++;

        return start;
    }

    public (int Start, int Length) GetInstrumentRange(Instrument instrument)
    {
        if (!_instrumentRanges.TryGetValue(instrument, out var range))
            throw new KeyNotFoundException($"Instrument {instrument} not found in universe");

        return range;
    }

    public (Instrument Inst, int VariantId) GetContext(int virtualIndex)
    {
        if (virtualIndex < 0 || virtualIndex >= _contexts.Count)
            throw new IndexOutOfRangeException($"VirtualIndex {virtualIndex} out of range [0, {_contexts.Count})");

        var ctx = _contexts[virtualIndex];
        return (ctx.Instrument, ctx.VariantId);
    }

    public (Instrument Inst, int VariantId) SafeGetContext(int virtualIndex)
    {
        if (virtualIndex < 0 || virtualIndex >= _contexts.Count)
            return (Instrument.Unknown, 0);

        var ctx = _contexts[virtualIndex];
        return (ctx.Instrument, ctx.VariantId);
    }
}
