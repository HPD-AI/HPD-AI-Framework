namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Slippage model type.
/// </summary>
public enum SlippageModelType : byte
{
    None = 0,
    VolumeProportional = 1
}

/// <summary>
/// Slippage simulation parameters.
/// </summary>
public sealed record SlippageParams(
    SlippageModelType Model,
    decimal BpsPerLotSize = 0m)
{
    public static readonly SlippageParams None = new(SlippageModelType.None);
}

/// <summary>
/// Fill behavior for partial fills.
/// </summary>
public enum FillBehavior : byte
{
    NoPartialFill = 0,
    PartialFillOnTrade = 1
}

/// <summary>
/// Bar processing order for OHLC prices.
/// </summary>
public enum BarOrderingMode : byte
{
    /// <summary>
    /// Fixed ordering: Always Open → High → Low → Close.
    /// Simple and deterministic (50% accuracy for H/L sequence).
    /// </summary>
    Fixed = 0,

    /// <summary>
    /// Adaptive ordering based on bar structure (research-backed).
    /// - If Open closer to High: Open → High → Low → Close
    /// - If Open closer to Low: Open → Low → High → Close
    /// Achieves 75-85% accuracy vs 50% with fixed ordering.
    /// Reference: https://gist.github.com/stefansimik/d387e1d9ff784a8973feca0cde51e363
    /// </summary>
    Adaptive = 1
}
