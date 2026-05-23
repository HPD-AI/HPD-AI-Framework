using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Venue-specific replay/simulation execution calibration.
/// These values tune fill price transforms; they do not model order admission or routing rules.
/// </summary>
public readonly record struct ExecutionCalibrationProfile(
    Venue Venue,
    SlippageParams Slippage,
    PriceImprovementParams PriceImprovement);
