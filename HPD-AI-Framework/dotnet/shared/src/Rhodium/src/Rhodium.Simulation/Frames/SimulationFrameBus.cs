using HPD.Events;
using HPD.Events.Struct;

namespace Rhodium.Simulation.Frames;

/// <summary>
/// Session-owned wrapper around HPD struct-event routes for Rhodium simulation frames.
/// </summary>
public sealed class SimulationFrameBus : IDisposable
{
    private readonly IStructEventHub _structEvents;
    private readonly bool _ownsBus;
    private readonly SequencedStructEventEmitter<QuoteFrame> _quoteEmitter;
    private readonly SequencedStructEventEmitter<TradeFrame> _tradeEmitter;
    private readonly SequencedStructEventEmitter<BookLevelDeltaFrame> _bookLevelDeltaEmitter;
    private readonly SequencedStructEventEmitter<BookDepthLevelFrame> _bookDepthLevelEmitter;
    private readonly SequencedStructEventEmitter<BookOrderAddedFrame> _bookOrderAddedEmitter;
    private readonly SequencedStructEventEmitter<BookOrderModifiedFrame> _bookOrderModifiedEmitter;
    private readonly SequencedStructEventEmitter<BookOrderDeletedFrame> _bookOrderDeletedEmitter;
    private readonly SequencedStructEventEmitter<BookOrderExecutedFrame> _bookOrderExecutedEmitter;
    private readonly SequencedStructEventEmitter<ExecutionFillFrame> _fillEmitter;
    private readonly SequencedStructEventEmitter<RiskMetricFrame> _riskMetricEmitter;
    private readonly SequencedStructEventEmitter<TensorProjectionFrame> _tensorProjectionEmitter;

    public SimulationFrameBus(IStructEventHub? structEvents = null)
    {
        _structEvents = structEvents ?? new StructEventHub();
        _ownsBus = structEvents is null;

        Quotes = _structEvents.Route<QuoteFrame>();
        Trades = _structEvents.Route<TradeFrame>();
        BookLevelDeltas = _structEvents.Route<BookLevelDeltaFrame>();
        BookDepthLevels = _structEvents.Route<BookDepthLevelFrame>();
        BookOrderAdds = _structEvents.Route<BookOrderAddedFrame>();
        BookOrderModifies = _structEvents.Route<BookOrderModifiedFrame>();
        BookOrderDeletes = _structEvents.Route<BookOrderDeletedFrame>();
        BookOrderExecutions = _structEvents.Route<BookOrderExecutedFrame>();
        Fills = _structEvents.Route<ExecutionFillFrame>();
        RiskMetrics = _structEvents.Route<RiskMetricFrame>();
        TensorProjections = _structEvents.Route<TensorProjectionFrame>();

        _quoteEmitter = Quotes.CreateSequencedEmitter();
        _tradeEmitter = Trades.CreateSequencedEmitter();
        _bookLevelDeltaEmitter = BookLevelDeltas.CreateSequencedEmitter();
        _bookDepthLevelEmitter = BookDepthLevels.CreateSequencedEmitter();
        _bookOrderAddedEmitter = BookOrderAdds.CreateSequencedEmitter();
        _bookOrderModifiedEmitter = BookOrderModifies.CreateSequencedEmitter();
        _bookOrderDeletedEmitter = BookOrderDeletes.CreateSequencedEmitter();
        _bookOrderExecutedEmitter = BookOrderExecutions.CreateSequencedEmitter();
        _fillEmitter = Fills.CreateSequencedEmitter();
        _riskMetricEmitter = RiskMetrics.CreateSequencedEmitter();
        _tensorProjectionEmitter = TensorProjections.CreateSequencedEmitter();
    }

    public StructEventRoute<QuoteFrame> Quotes { get; }
    public StructEventRoute<TradeFrame> Trades { get; }
    public StructEventRoute<BookLevelDeltaFrame> BookLevelDeltas { get; }
    public StructEventRoute<BookDepthLevelFrame> BookDepthLevels { get; }
    public StructEventRoute<BookOrderAddedFrame> BookOrderAdds { get; }
    public StructEventRoute<BookOrderModifiedFrame> BookOrderModifies { get; }
    public StructEventRoute<BookOrderDeletedFrame> BookOrderDeletes { get; }
    public StructEventRoute<BookOrderExecutedFrame> BookOrderExecutions { get; }
    public StructEventRoute<ExecutionFillFrame> Fills { get; }
    public StructEventRoute<RiskMetricFrame> RiskMetrics { get; }
    public StructEventRoute<TensorProjectionFrame> TensorProjections { get; }

    public StructEventEmitResult Emit(in QuoteFrame frame) => _quoteEmitter.Emit(in frame);
    public StructEventEmitResult Emit(in TradeFrame frame) => _tradeEmitter.Emit(in frame);
    public StructEventEmitResult Emit(in BookLevelDeltaFrame frame) => _bookLevelDeltaEmitter.Emit(in frame);
    public StructEventEmitResult Emit(in BookDepthLevelFrame frame) => _bookDepthLevelEmitter.Emit(in frame);
    public StructEventEmitResult Emit(in BookOrderAddedFrame frame) => _bookOrderAddedEmitter.Emit(in frame);
    public StructEventEmitResult Emit(in BookOrderModifiedFrame frame) => _bookOrderModifiedEmitter.Emit(in frame);
    public StructEventEmitResult Emit(in BookOrderDeletedFrame frame) => _bookOrderDeletedEmitter.Emit(in frame);
    public StructEventEmitResult Emit(in BookOrderExecutedFrame frame) => _bookOrderExecutedEmitter.Emit(in frame);
    public StructEventEmitResult Emit(in ExecutionFillFrame frame) => _fillEmitter.Emit(in frame);
    public StructEventEmitResult Emit(in RiskMetricFrame frame) => _riskMetricEmitter.Emit(in frame);
    public StructEventEmitResult Emit(in TensorProjectionFrame frame) => _tensorProjectionEmitter.Emit(in frame);

    public StructEventHubStats GetStats() => _structEvents.GetStats();

    public IReadOnlyList<StructEventRouteStats> GetRouteStats() => _structEvents.GetRouteStats();

    public void Dispose()
    {
        if (_ownsBus && _structEvents is IDisposable disposable)
            disposable.Dispose();
    }
}
