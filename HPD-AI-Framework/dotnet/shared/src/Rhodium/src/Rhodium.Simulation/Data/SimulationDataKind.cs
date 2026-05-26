namespace Rhodium.Simulation.Data;

/// <summary>
/// Finance event families available to simulation data queries.
/// </summary>
[Flags]
public enum SimulationDataKind
{
    /// <summary>No event families.</summary>
    None = 0,

    /// <summary>Completed bar events.</summary>
    Bars = 1 << 0,

    /// <summary>Trade print events.</summary>
    Trades = 1 << 1,

    /// <summary>Top-of-book quote events.</summary>
    Quotes = 1 << 2,

    /// <summary>Full or fixed-depth book snapshot events.</summary>
    Books = 1 << 3,

    /// <summary>Market-by-price book level delta events.</summary>
    BookLevelDeltas = 1 << 4,

    /// <summary>Market-by-order book events.</summary>
    BookOrders = 1 << 5,

    /// <summary>Venue and instrument status events.</summary>
    Status = 1 << 6,

    /// <summary>Execution events such as order acknowledgements and fills.</summary>
    Execution = 1 << 7,

    /// <summary>Lifecycle events.</summary>
    Lifecycle = 1 << 8,

    /// <summary>Diagnostic events.</summary>
    Diagnostics = 1 << 9,

    /// <summary>Control events.</summary>
    Control = 1 << 10,

    /// <summary>All simulation data event families.</summary>
    All = Bars
        | Trades
        | Quotes
        | Books
        | BookLevelDeltas
        | BookOrders
        | Status
        | Execution
        | Lifecycle
        | Diagnostics
        | Control
}
