using HPD.Events;

namespace Rhodium.Simulation.Frames;

/// <summary>
/// Session-owned wrapper around HPD local struct routes for Rhodium simulation frames.
/// </summary>
public sealed class SimulationFrameBus : IDisposable
{
    private readonly ILocalStructEventBus _localStructs;
    private readonly bool _ownsBus;
    private readonly LocalSequencedStructEmitter<QuoteFrame> _quoteEmitter;
    private readonly LocalSequencedStructEmitter<TradeFrame> _tradeEmitter;
    private readonly LocalSequencedStructEmitter<BookLevelDeltaFrame> _bookLevelDeltaEmitter;
    private readonly LocalSequencedStructEmitter<BookDepthLevelFrame> _bookDepthLevelEmitter;
    private readonly LocalSequencedStructEmitter<BookOrderAddedFrame> _bookOrderAddedEmitter;
    private readonly LocalSequencedStructEmitter<BookOrderModifiedFrame> _bookOrderModifiedEmitter;
    private readonly LocalSequencedStructEmitter<BookOrderDeletedFrame> _bookOrderDeletedEmitter;
    private readonly LocalSequencedStructEmitter<BookOrderExecutedFrame> _bookOrderExecutedEmitter;
    private readonly LocalSequencedStructEmitter<ExecutionFillFrame> _fillEmitter;
    private readonly LocalSequencedStructEmitter<RiskMetricFrame> _riskMetricEmitter;
    private readonly LocalSequencedStructEmitter<TensorProjectionFrame> _tensorProjectionEmitter;

    public SimulationFrameBus(ILocalStructEventBus? localStructs = null)
    {
        _localStructs = localStructs ?? new LocalStructEventBus();
        _ownsBus = localStructs is null;

        Quotes = _localStructs.Route<QuoteFrame>();
        Trades = _localStructs.Route<TradeFrame>();
        BookLevelDeltas = _localStructs.Route<BookLevelDeltaFrame>();
        BookDepthLevels = _localStructs.Route<BookDepthLevelFrame>();
        BookOrderAdds = _localStructs.Route<BookOrderAddedFrame>();
        BookOrderModifies = _localStructs.Route<BookOrderModifiedFrame>();
        BookOrderDeletes = _localStructs.Route<BookOrderDeletedFrame>();
        BookOrderExecutions = _localStructs.Route<BookOrderExecutedFrame>();
        Fills = _localStructs.Route<ExecutionFillFrame>();
        RiskMetrics = _localStructs.Route<RiskMetricFrame>();
        TensorProjections = _localStructs.Route<TensorProjectionFrame>();

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

    public LocalStructEventRoute<QuoteFrame> Quotes { get; }
    public LocalStructEventRoute<TradeFrame> Trades { get; }
    public LocalStructEventRoute<BookLevelDeltaFrame> BookLevelDeltas { get; }
    public LocalStructEventRoute<BookDepthLevelFrame> BookDepthLevels { get; }
    public LocalStructEventRoute<BookOrderAddedFrame> BookOrderAdds { get; }
    public LocalStructEventRoute<BookOrderModifiedFrame> BookOrderModifies { get; }
    public LocalStructEventRoute<BookOrderDeletedFrame> BookOrderDeletes { get; }
    public LocalStructEventRoute<BookOrderExecutedFrame> BookOrderExecutions { get; }
    public LocalStructEventRoute<ExecutionFillFrame> Fills { get; }
    public LocalStructEventRoute<RiskMetricFrame> RiskMetrics { get; }
    public LocalStructEventRoute<TensorProjectionFrame> TensorProjections { get; }

    public LocalStructEmitResult Emit(in QuoteFrame frame) => _quoteEmitter.Emit(in frame);
    public LocalStructEmitResult Emit(in TradeFrame frame) => _tradeEmitter.Emit(in frame);
    public LocalStructEmitResult Emit(in BookLevelDeltaFrame frame) => _bookLevelDeltaEmitter.Emit(in frame);
    public LocalStructEmitResult Emit(in BookDepthLevelFrame frame) => _bookDepthLevelEmitter.Emit(in frame);
    public LocalStructEmitResult Emit(in BookOrderAddedFrame frame) => _bookOrderAddedEmitter.Emit(in frame);
    public LocalStructEmitResult Emit(in BookOrderModifiedFrame frame) => _bookOrderModifiedEmitter.Emit(in frame);
    public LocalStructEmitResult Emit(in BookOrderDeletedFrame frame) => _bookOrderDeletedEmitter.Emit(in frame);
    public LocalStructEmitResult Emit(in BookOrderExecutedFrame frame) => _bookOrderExecutedEmitter.Emit(in frame);
    public LocalStructEmitResult Emit(in ExecutionFillFrame frame) => _fillEmitter.Emit(in frame);
    public LocalStructEmitResult Emit(in RiskMetricFrame frame) => _riskMetricEmitter.Emit(in frame);
    public LocalStructEmitResult Emit(in TensorProjectionFrame frame) => _tensorProjectionEmitter.Emit(in frame);

    public LocalStructEventBusStats GetStats() => _localStructs.GetStats();

    public IReadOnlyList<LocalStructEventTypeStats> GetRouteStats() => _localStructs.GetRouteStats();

    public void Dispose()
    {
        if (_ownsBus && _localStructs is IDisposable disposable)
            disposable.Dispose();
    }
}
