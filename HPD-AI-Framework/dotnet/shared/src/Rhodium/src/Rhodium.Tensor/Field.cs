namespace Rhodium.Tensor;

/// <summary>
/// Standard tensor field definitions for market data.
/// Raw fields are written by ingestion.
/// Adjusted fields are read by strategies.
/// </summary>
public static class Field
{
    // === Ingestion Layer (Writable) ===

    /// <summary>Raw open price (before corporate action adjustments).</summary>
    public static readonly VectorField<PriceF64> OpenRaw = new("OpenRaw");

    /// <summary>Raw high price (before corporate action adjustments).</summary>
    public static readonly VectorField<PriceF64> HighRaw = new("HighRaw");

    /// <summary>Raw low price (before corporate action adjustments).</summary>
    public static readonly VectorField<PriceF64> LowRaw = new("LowRaw");

    /// <summary>Raw close price (before corporate action adjustments).</summary>
    public static readonly VectorField<PriceF64> CloseRaw = new("CloseRaw");

    /// <summary>Raw volume (before corporate action adjustments).</summary>
    public static readonly VectorField<SizeF64> VolumeRaw = new("VolumeRaw");

    // === Factors (Source of Truth, Default 1.0) ===

    /// <summary>
    /// Split factor (inverse of split ratio).
    /// Convention: 2-for-1 split → 0.5
    /// </summary>
    public static readonly VectorField<FactorF64> SplitFactor = new("SplitFactor");

    /// <summary>
    /// Dividend scale factor (price multiplier).
    /// Convention: 10% dividend → 0.9
    /// </summary>
    public static readonly VectorField<FactorF64> DividendScale = new("DividendScale");

    // === Derived Scales (Computed During Ingestion) ===

    /// <summary>
    /// Price scale = SplitFactor * DividendScale.
    /// Precomputed to avoid per-tick division.
    /// </summary>
    public static readonly VectorField<FactorF64> PriceScale = new("PriceScale");

    /// <summary>
    /// Volume scale = 1 / SplitFactor.
    /// Precomputed to avoid per-tick division.
    /// </summary>
    public static readonly VectorField<FactorF64> VolumeScale = new("VolumeScale");

    // === Strategy Layer (Computed/Read-Only) ===

    /// <summary>Adjusted open price (Open = OpenRaw * PriceScale).</summary>
    public static readonly VectorField<PriceF64> Open = new("Open");

    /// <summary>Adjusted high price (High = HighRaw * PriceScale).</summary>
    public static readonly VectorField<PriceF64> High = new("High");

    /// <summary>Adjusted low price (Low = LowRaw * PriceScale).</summary>
    public static readonly VectorField<PriceF64> Low = new("Low");

    /// <summary>Adjusted close price (Close = CloseRaw * PriceScale).</summary>
    public static readonly VectorField<PriceF64> Close = new("Close");

    /// <summary>Adjusted volume (Volume = VolumeRaw * VolumeScale).</summary>
    public static readonly VectorField<SizeF64> Volume = new("Volume");
}
