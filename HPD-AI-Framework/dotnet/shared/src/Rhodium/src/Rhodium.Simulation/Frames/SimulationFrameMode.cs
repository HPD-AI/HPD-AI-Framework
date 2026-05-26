namespace Rhodium.Simulation.Frames;

/// <summary>
/// Controls optional local struct-frame projection for a simulation run.
/// </summary>
public enum SimulationFrameMode
{
    Disabled,
    MarketData,
    Execution,
    Diagnostics,
    All
}
