using Rhodium.Events;
using Rhodium.Primitives;
using Rhodium.Simulation.Diagnostics;

namespace Rhodium.Simulation.Modules;

/// <summary>
/// Extends a simulation session with deterministic same-timestamp behavior.
/// </summary>
public interface ISimulationModule
{
    /// <summary>
    /// Resets module-local run state before a simulation starts.
    /// </summary>
    void Reset();

    /// <summary>
    /// Observes a replay/session event before the simulated exchanges and market projectors process it.
    /// </summary>
    void PreProcess(
        in FinanceEvent evt,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks);

    /// <summary>
    /// Runs during a simulation timestamp after due exchange work has settled.
    /// </summary>
    void Process(
        Instant now,
        ref SimulationModuleContext context,
        ref SimulationModuleSinks sinks);

    /// <summary>
    /// Appends module-owned diagnostics to the simulation result.
    /// </summary>
    void AppendDiagnostics(ref SimulationDiagnosticsBuilder diagnostics);
}

/// <summary>
/// Run-level simulation module.
/// </summary>
public interface ISessionSimulationModule : ISimulationModule
{
}

/// <summary>
/// Venue-scoped simulation module.
/// </summary>
public interface IVenueSimulationModule : ISimulationModule
{
    /// <summary>Venue this module is installed into.</summary>
    Venue Venue { get; }
}

/// <summary>
/// Instrument-scoped simulation module.
/// </summary>
public interface IInstrumentSimulationModule : ISimulationModule
{
    /// <summary>Instrument this module is installed into.</summary>
    Instrument Instrument { get; }
}
