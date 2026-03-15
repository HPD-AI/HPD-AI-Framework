using System.Runtime.CompilerServices;
using Rhodium.Kernel;
using Rhodium.Tensor;

namespace Rhodium.Platform.Extensions;

/// <summary>
/// User-defined indicator fields for platform strategies.
/// </summary>
public static class Fields
{
    /// <summary>RSI with 14-period window.</summary>
    public static readonly VectorField<FactorF64> RSI_14 = new("RSI_14");
}

/// <summary>
/// Zero-cost scalar data accessors for strategy hot paths.
/// All methods are aggressively inlined for maximum performance.
/// </summary>
public static class DataExtensions
{
    /// <summary>
    /// Gets the close price for the specified asset.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetClose(this ref TradingEngine engine, AssetId id)
        => engine.Tensors.GetScalar(Field.Close, id).Value;

    /// <summary>
    /// Gets the open price for the specified asset.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetOpen(this ref TradingEngine engine, AssetId id)
        => engine.Tensors.GetScalar(Field.Open, id).Value;

    /// <summary>
    /// Gets the high price for the specified asset.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetHigh(this ref TradingEngine engine, AssetId id)
        => engine.Tensors.GetScalar(Field.High, id).Value;

    /// <summary>
    /// Gets the low price for the specified asset.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetLow(this ref TradingEngine engine, AssetId id)
        => engine.Tensors.GetScalar(Field.Low, id).Value;

    /// <summary>
    /// Gets the volume for the specified asset.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetVolume(this ref TradingEngine engine, AssetId id)
        => engine.Tensors.GetScalar(Field.Volume, id).Value;

    /// <summary>
    /// Gets the RSI(14) indicator value for the specified asset.
    /// Must call RegisterIndicator(Fields.RSI_14) during OnInitialize.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetRsi14(this ref TradingEngine engine, AssetId id)
        => engine.Tensors.GetScalar(Fields.RSI_14, id).Value;
}
