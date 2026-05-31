using HPD.Events;
using HPD.Events.Struct;
using Rhodium.Events;
using Rhodium.Simulation.Exchange;
using Rhodium.Simulation.Frames;

namespace Rhodium.Simulation.Modules;

/// <summary>
/// Explicit effect sinks exposed to simulation modules.
/// </summary>
public ref struct SimulationModuleSinks
{
    internal SimulationModuleSinks(
        List<FinanceEvent> pendingEvents,
        List<SimulationModuleCommand> pendingCommands,
        SimulationFrameBus frames,
        SimulationFrameMode frameMode,
        int[] moduleFrameCounts,
        int moduleIndex)
    {
        Events = new SimulationEventSink(pendingEvents);
        Frames = new SimulationStructFrameSink(frames, frameMode, moduleFrameCounts, moduleIndex);
        Commands = new SimulationCommandSink(pendingCommands);
    }

    /// <summary>Sink for semantic module-produced events.</summary>
    public SimulationEventSink Events { get; }

    /// <summary>Sink for local struct frames produced by modules.</summary>
    public SimulationStructFrameSink Frames { get; }

    /// <summary>Sink for exchange commands produced by modules.</summary>
    public SimulationCommandSink Commands { get; }
}

/// <summary>
/// Semantic event sink for module-produced replayable simulator effects.
/// </summary>
public readonly ref struct SimulationEventSink
{
    private readonly List<FinanceEvent> _pendingEvents;

    internal SimulationEventSink(List<FinanceEvent> pendingEvents)
        => _pendingEvents = pendingEvents;

    /// <summary>Emit a semantic finance event back into the session turn.</summary>
    public void Emit(FinanceEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        _pendingEvents.Add(evt);
    }
}

/// <summary>
/// Local struct-frame sink for module-produced hot-path frames.
/// </summary>
public readonly ref struct SimulationStructFrameSink
{
    private readonly SimulationFrameBus _frames;
    private readonly SimulationFrameMode _frameMode;
    private readonly int[] _moduleFrameCounts;
    private readonly int _moduleIndex;

    internal SimulationStructFrameSink(
        SimulationFrameBus frames,
        SimulationFrameMode frameMode,
        int[] moduleFrameCounts,
        int moduleIndex)
    {
        _frames = frames;
        _frameMode = frameMode;
        _moduleFrameCounts = moduleFrameCounts;
        _moduleIndex = moduleIndex;
    }

    /// <summary>Emit one quote frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in QuoteFrame frame) => EmitMarketData(in frame);

    /// <summary>Emit one trade frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in TradeFrame frame) => EmitMarketData(in frame);

    /// <summary>Emit one book level delta frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in BookLevelDeltaFrame frame) => EmitMarketData(in frame);

    /// <summary>Emit one book depth level frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in BookDepthLevelFrame frame) => EmitMarketData(in frame);

    /// <summary>Emit one book order add frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in BookOrderAddedFrame frame) => EmitMarketData(in frame);

    /// <summary>Emit one book order modify frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in BookOrderModifiedFrame frame) => EmitMarketData(in frame);

    /// <summary>Emit one book order delete frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in BookOrderDeletedFrame frame) => EmitMarketData(in frame);

    /// <summary>Emit one book order execution frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in BookOrderExecutedFrame frame) => EmitMarketData(in frame);

    /// <summary>Emit one execution fill frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in ExecutionFillFrame frame) => EmitExecution(in frame);

    /// <summary>Emit one risk metric frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in RiskMetricFrame frame) => EmitDiagnostics(in frame);

    /// <summary>Emit one tensor projection frame when frame emission is enabled.</summary>
    public StructEventEmitResult Emit(in TensorProjectionFrame frame) => EmitDiagnostics(in frame);

    private StructEventEmitResult EmitMarketData(in QuoteFrame frame)
    {
        if (!CanEmitMarketData())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private StructEventEmitResult EmitMarketData(in TradeFrame frame)
    {
        if (!CanEmitMarketData())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private StructEventEmitResult EmitMarketData(in BookLevelDeltaFrame frame)
    {
        if (!CanEmitMarketData())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private StructEventEmitResult EmitMarketData(in BookDepthLevelFrame frame)
    {
        if (!CanEmitMarketData())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private StructEventEmitResult EmitMarketData(in BookOrderAddedFrame frame)
    {
        if (!CanEmitMarketData())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private StructEventEmitResult EmitMarketData(in BookOrderModifiedFrame frame)
    {
        if (!CanEmitMarketData())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private StructEventEmitResult EmitMarketData(in BookOrderDeletedFrame frame)
    {
        if (!CanEmitMarketData())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private StructEventEmitResult EmitMarketData(in BookOrderExecutedFrame frame)
    {
        if (!CanEmitMarketData())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private StructEventEmitResult EmitExecution(in ExecutionFillFrame frame)
    {
        if (!CanEmitExecution())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private StructEventEmitResult EmitDiagnostics(in RiskMetricFrame frame)
    {
        if (!CanEmitDiagnostics())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private StructEventEmitResult EmitDiagnostics(in TensorProjectionFrame frame)
    {
        if (!CanEmitDiagnostics())
            return Filtered();

        _moduleFrameCounts[_moduleIndex]++;
        return _frames.Emit(in frame);
    }

    private bool CanEmitMarketData()
        => _frameMode is SimulationFrameMode.MarketData or SimulationFrameMode.All;

    private bool CanEmitExecution()
        => _frameMode is SimulationFrameMode.Execution or SimulationFrameMode.All;

    private bool CanEmitDiagnostics()
        => _frameMode is SimulationFrameMode.Diagnostics or SimulationFrameMode.All;

    private static StructEventEmitResult Filtered()
        => new(StructEventEmitStatus.Filtered, 0, 0, 0);
}

/// <summary>
/// Explicit exchange-command sink for module-produced simulator commands.
/// </summary>
public readonly ref struct SimulationCommandSink
{
    private readonly List<SimulationModuleCommand> _pendingCommands;

    internal SimulationCommandSink(List<SimulationModuleCommand> pendingCommands)
        => _pendingCommands = pendingCommands;

    /// <summary>Submit an order command into the session turn.</summary>
    public void Submit(in SimulationOrderCommand command)
        => _pendingCommands.Add(SimulationModuleCommand.ForSubmit(command));

    /// <summary>Submit a cancel command into the session turn.</summary>
    public void Cancel(in SimulationCancelCommand command)
        => _pendingCommands.Add(SimulationModuleCommand.ForCancel(command));

    /// <summary>Submit a modify command into the session turn.</summary>
    public void Modify(in SimulationModifyCommand command)
        => _pendingCommands.Add(SimulationModuleCommand.ForModify(command));
}

internal enum SimulationModuleCommandKind
{
    Submit,
    Cancel,
    Modify
}

internal readonly record struct SimulationModuleCommand(
    SimulationModuleCommandKind Kind,
    SimulationOrderCommand Submit,
    SimulationCancelCommand Cancel,
    SimulationModifyCommand Modify)
{
    public static SimulationModuleCommand ForSubmit(SimulationOrderCommand command)
        => new(SimulationModuleCommandKind.Submit, command, default, default);

    public static SimulationModuleCommand ForCancel(SimulationCancelCommand command)
        => new(SimulationModuleCommandKind.Cancel, default, command, default);

    public static SimulationModuleCommand ForModify(SimulationModifyCommand command)
        => new(SimulationModuleCommandKind.Modify, default, default, command);
}
