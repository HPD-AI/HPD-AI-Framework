using HPD.Events;
using HPD.Events.Struct;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Diagnostics;

/// <summary>Venue-level diagnostics captured for one simulation run.</summary>
/// <param name="Venue">Simulated venue.</param>
/// <param name="Status">Final venue market status.</param>
/// <param name="AccountType">Venue account type.</param>
/// <param name="BaseCurrency">Base currency used by the venue account.</param>
/// <param name="InstrumentCount">Number of instrument engines active at the venue.</param>
/// <param name="SubmittedCommands">Total commands submitted to the venue.</param>
/// <param name="AcceptedOrders">Total accepted orders.</param>
/// <param name="RejectedOrders">Total rejected orders.</param>
/// <param name="FilledOrders">Total filled orders.</param>
/// <param name="CancelledOrders">Total cancelled orders.</param>
/// <param name="ExpiredOrders">Total expired orders.</param>
/// <param name="Cash">Final account cash.</param>
/// <param name="AvailableCash">Final available cash.</param>
/// <param name="ReservedCash">Final cash reserved for open orders.</param>
/// <param name="PendingSettlement">Final pending settlement amount.</param>
/// <param name="PendingSettlementCount">Number of pending settlement entries.</param>
/// <param name="PendingAssetDeliveryQuantity">Quantity pending asset delivery.</param>
/// <param name="PendingAssetDeliveryCount">Number of pending asset delivery entries.</param>
/// <param name="OrderPolicy">Order admission policy used by the venue.</param>
/// <param name="SimulationPolicy">Venue behavior policy used by the simulation.</param>
public sealed record VenueSimulationDiagnostics(
    Venue Venue,
    MarketStatus Status,
    AccountType AccountType,
    Currency BaseCurrency,
    int InstrumentCount,
    int SubmittedCommands,
    int AcceptedOrders,
    int RejectedOrders,
    int FilledOrders,
    int CancelledOrders,
    int ExpiredOrders,
    Money Cash,
    Money AvailableCash,
    Money ReservedCash,
    Money PendingSettlement,
    int PendingSettlementCount,
    Qty PendingAssetDeliveryQuantity,
    int PendingAssetDeliveryCount,
    SimulationOrderPolicy OrderPolicy,
    SimulationVenuePolicy SimulationPolicy);

/// <summary>Instrument-level diagnostics captured for one simulated instrument engine.</summary>
/// <param name="Instrument">Instrument represented by the engine.</param>
/// <param name="Status">Final instrument market status.</param>
/// <param name="MatchingFidelity">Matching fidelity used by the engine.</param>
/// <param name="OrderPolicy">Order admission policy applied to the instrument.</param>
/// <param name="SimulationPolicy">Venue behavior policy applied to the instrument.</param>
/// <param name="MarkPrice">Final mark price when available.</param>
/// <param name="CloseMark">Final close mark when available.</param>
/// <param name="OpenOrders">Number of open orders remaining.</param>
/// <param name="AcceptedOrders">Total accepted orders.</param>
/// <param name="RejectedOrders">Total rejected orders.</param>
/// <param name="FilledOrders">Total filled orders.</param>
/// <param name="CancelledOrders">Total cancelled orders.</param>
/// <param name="ExpiredOrders">Total expired orders.</param>
public sealed record InstrumentSimulationDiagnostics(
    Instrument Instrument,
    MarketStatus Status,
    MatchingFidelity MatchingFidelity,
    SimulationOrderPolicy OrderPolicy,
    SimulationVenuePolicy SimulationPolicy,
    Price? MarkPrice,
    Price? CloseMark,
    int OpenOrders,
    int AcceptedOrders,
    int RejectedOrders,
    int FilledOrders,
    int CancelledOrders,
    int ExpiredOrders);

/// <summary>Replay-turn quiescence counters.</summary>
/// <param name="MaxIterations">Largest number of iterations observed for one timestamp.</param>
/// <param name="TotalIterations">Total replay-turn iterations across the run.</param>
public sealed record QuiescenceDiagnostics(
    int MaxIterations,
    int TotalIterations);

/// <summary>Command latency counters for simulated venues.</summary>
/// <param name="CommandCount">Number of sampled commands.</param>
/// <param name="MinEntryLatency">Minimum sampled entry latency.</param>
/// <param name="MaxEntryLatency">Maximum sampled entry latency.</param>
/// <param name="AverageEntryLatency">Average sampled entry latency.</param>
public sealed record LatencyDiagnostics(
    int CommandCount,
    Duration MinEntryLatency,
    Duration MaxEntryLatency,
    Duration AverageEntryLatency);

/// <summary>Replay timing information for a simulation run.</summary>
/// <param name="ReplayStart">First replay timestamp processed.</param>
/// <param name="ReplayEnd">Last replay timestamp processed.</param>
/// <param name="FinalClock">Final session clock time.</param>
/// <param name="ReplayEventCount">Total replay events processed after filtering.</param>
public sealed record RunTimingDiagnostics(
    Instant? ReplayStart,
    Instant? ReplayEnd,
    Instant FinalClock,
    int ReplayEventCount);

/// <summary>Diagnostics emitted by one simulation module.</summary>
/// <param name="ModuleName">Module type name.</param>
/// <param name="PreProcessCalls">Number of pre-process calls.</param>
/// <param name="ProcessCalls">Number of timestamp process calls.</param>
/// <param name="EmittedEvents">Number of semantic events emitted by the module.</param>
/// <param name="SubmittedCommands">Number of commands submitted by the module.</param>
/// <param name="EmittedFrames">Number of struct frames emitted by the module.</param>
/// <param name="Counters">Module counters.</param>
/// <param name="Metrics">Module metrics.</param>
/// <param name="Messages">Module diagnostic messages.</param>
public sealed record SimulationModuleDiagnostics(
    string ModuleName,
    int PreProcessCalls,
    int ProcessCalls,
    int EmittedEvents,
    int SubmittedCommands,
    int EmittedFrames,
    IReadOnlyList<SimulationModuleCounter> Counters,
    IReadOnlyList<SimulationModuleMetric> Metrics,
    IReadOnlyList<SimulationModuleMessage> Messages);

/// <summary>Provenance for one simulation data source.</summary>
/// <param name="SourceId">Stable source identifier.</param>
/// <param name="Priority">Source priority used for deterministic replay ordering.</param>
/// <param name="SourceOrdinal">Source ordinal in the simulation data plan.</param>
/// <param name="SourceKind">Logical source kind.</param>
/// <param name="From">Inclusive lower replay bound.</param>
/// <param name="To">Exclusive upper replay bound.</param>
/// <param name="EventFlowId">Event-flow filter.</param>
/// <param name="Limit">Maximum events emitted from the effective read.</param>
public sealed record SimulationDataProvenance(
    string SourceId,
    int Priority,
    int SourceOrdinal,
    string SourceKind,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? EventFlowId,
    int? Limit);

/// <summary>Order rejection diagnostic emitted by the simulator.</summary>
/// <param name="Venue">Venue that rejected the order.</param>
/// <param name="Instrument">Instrument for the rejected order.</param>
/// <param name="OrderId">Client order id for the rejected order.</param>
/// <param name="Reason">Human-readable rejection reason.</param>
public sealed record SimulationRejectionDiagnostic(
    Venue Venue,
    Instrument Instrument,
    OrderId OrderId,
    string Reason);

/// <summary>Complete simulator diagnostics for a run.</summary>
public sealed class SimulationDiagnostics
{
    /// <summary>Empty diagnostics instance.</summary>
    public static SimulationDiagnostics Empty { get; } = new(
        [],
        [],
        new QuiescenceDiagnostics(0, 0),
        new LatencyDiagnostics(0, Duration.Zero, Duration.Zero, Duration.Zero),
        new RunTimingDiagnostics(null, null, Instant.Epoch, 0),
        [],
        new StructEventHubStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        [],
        []);

    /// <summary>Create complete simulator diagnostics for a run.</summary>
    public SimulationDiagnostics(
        IReadOnlyList<VenueSimulationDiagnostics> venues,
        IReadOnlyList<InstrumentSimulationDiagnostics> instruments,
        QuiescenceDiagnostics quiescence,
        LatencyDiagnostics latency,
        RunTimingDiagnostics timing,
        IReadOnlyList<SimulationModuleDiagnostics> modules,
        StructEventHubStats frameStats,
        IReadOnlyList<SimulationDataProvenance> dataSources,
        IReadOnlyList<SimulationRejectionDiagnostic> rejections)
    {
        Venues = venues;
        Instruments = instruments;
        Quiescence = quiescence;
        Latency = latency;
        Timing = timing;
        Modules = modules;
        FrameStats = frameStats;
        DataSources = dataSources;
        Rejections = rejections;
    }

    /// <summary>Venue-level diagnostics.</summary>
    public IReadOnlyList<VenueSimulationDiagnostics> Venues { get; }

    /// <summary>Instrument-level diagnostics.</summary>
    public IReadOnlyList<InstrumentSimulationDiagnostics> Instruments { get; }

    /// <summary>Replay-turn quiescence diagnostics.</summary>
    public QuiescenceDiagnostics Quiescence { get; }

    /// <summary>Command latency diagnostics.</summary>
    public LatencyDiagnostics Latency { get; }

    /// <summary>Replay timing diagnostics.</summary>
    public RunTimingDiagnostics Timing { get; }

    /// <summary>Module diagnostics.</summary>
    public IReadOnlyList<SimulationModuleDiagnostics> Modules { get; }

    /// <summary>Local struct frame bus diagnostics.</summary>
    public StructEventHubStats FrameStats { get; }

    /// <summary>Replay data source provenance.</summary>
    public IReadOnlyList<SimulationDataProvenance> DataSources { get; }

    /// <summary>Order rejection diagnostics.</summary>
    public IReadOnlyList<SimulationRejectionDiagnostic> Rejections { get; }
}
