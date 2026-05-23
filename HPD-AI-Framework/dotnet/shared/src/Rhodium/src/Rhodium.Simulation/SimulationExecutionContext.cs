using Rhodium.Kernel;

namespace Rhodium.Simulation;

public readonly ref struct SimulationExecutionContext
{
    public SimulationExecutionContext(RhodiumRuntime runtime, SimulationConfig config)
    {
        Runtime = runtime;
        Config = config;
    }

    public RhodiumRuntime Runtime { get; }
    public SimulationConfig Config { get; }
}
