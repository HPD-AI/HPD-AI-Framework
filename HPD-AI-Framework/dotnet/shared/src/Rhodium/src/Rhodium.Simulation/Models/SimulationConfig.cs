using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Configuration for replay-based backtesting simulation.
/// Simple parameters, not complex model objects.
/// </summary>
public sealed record SimulationConfig
{
    public SimulationFidelity Fidelity { get; init; } = SimulationFidelity.Queue;
    public required LatencyParams Latency { get; init; }
    public required QueueParams QueueModel { get; init; }
    public required FeeParams Fees { get; init; }
    public SlippageParams Slippage { get; init; } = SlippageParams.None;
    public PriceImprovementParams PriceImprovement { get; init; } = PriceImprovementParams.None;
    public FillBehavior FillBehavior { get; init; } = FillBehavior.NoPartialFill;
    public DepthLevel RequiredDepth { get; init; } = DepthLevel.L2_MarketByPrice;

    /// <summary>
    /// Account type for capital constraint simulation.
    /// Cash accounts lock full notional, margin accounts use leverage.
    /// Default: Cash (conservative).
    /// </summary>
    public AccountType AccountType { get; init; } = AccountType.Cash;

    /// <summary>
    /// Margin requirements used when <see cref="AccountType"/> is Margin.
    /// </summary>
    public MarginParams Margin { get; init; } = MarginParams.RegT();

    /// <summary>
    /// Settlement behavior for cash-account proceeds.
    /// </summary>
    public SettlementParams Settlement { get; init; } = SettlementParams.Immediate();

    /// <summary>
    /// Initial market status.
    /// If not Open, orders will be rejected during simulation.
    /// Default: Open.
    /// </summary>
    public MarketStatus InitialMarketStatus { get; init; } = MarketStatus.Open;

    /// <summary>
    /// Bar OHLC processing order.
    /// Fixed = always O→H→L→C (50% accuracy).
    /// Adaptive = smart ordering based on bar structure (75-85% accuracy).
    /// Default: Fixed.
    /// </summary>
    public BarOrderingMode BarOrdering { get; init; } = BarOrderingMode.Fixed;

    /// <summary>
    /// Fill model for custom fill logic.
    /// Default: DefaultFillModel (standard price crossing + queue logic).
    /// </summary>
    public IFillModel FillModel { get; init; } = new DefaultFillModel();

    /// <summary>
    /// Deterministic PRNG seed for latency sampling, queue advancement.
    /// Default: hash(batchMapVersion) for deterministic replay.
    /// </summary>
    public int Seed { get; init; } = 0;

    // ==================== PRESETS ====================

    public static SimulationConfig Vector() => Instant() with
    {
        Fidelity = SimulationFidelity.Vector,
        FillBehavior = FillBehavior.FillOnTouch
    };

    public static SimulationConfig Queue() => Instant() with
    {
        Fidelity = SimulationFidelity.Queue
    };

    /// <summary>
    /// Realistic preset for liquid crypto futures (Binance/Bybit).
    /// Power quadratic queue, standard maker/taker fees, moderate latency.
    /// </summary>
    public static SimulationConfig CryptoFuturesRealistic() => new()
    {
        Fidelity = SimulationFidelity.Queue,
        Latency = new(Duration.FromMicros(500), Duration.FromMicros(500), StdDevFraction: 0.2),
        QueueModel = QueueParams.RealisticLiquid(),
        Fees = FeeParams.BinanceFutures(),
        Slippage = SlippageParams.None,
        FillBehavior = FillBehavior.PartialFillOnTrade
    };

    /// <summary>
    /// Conservative preset for risk assessment.
    /// Risk-averse queue, higher fees, higher latency, slippage included.
    /// </summary>
    public static SimulationConfig Conservative() => new()
    {
        Fidelity = SimulationFidelity.Queue,
        Latency = new(Duration.FromMillis(10), Duration.FromMillis(10)),
        QueueModel = QueueParams.RiskAverse(),
        Fees = new()
        {
            Model = FeeModelType.PercentageOfValue,
            MakerBps = 5m,
            TakerBps = 15m
        },
        Slippage = new(SlippageModelType.VolumeProportional, BpsPerLotSize: 1m),
        FillBehavior = FillBehavior.NoPartialFill
    };

    /// <summary>
    /// Illiquid market preset (altcoins, low-volume pairs).
    /// Cubic queue profile, higher fees, partial fills required.
    /// </summary>
    public static SimulationConfig IlliquidMarket() => new()
    {
        Fidelity = SimulationFidelity.Queue,
        Latency = new(Duration.FromMillis(1), Duration.FromMillis(1), StdDevFraction: 0.3),
        QueueModel = QueueParams.RealisticIlliquid(),
        Fees = FeeParams.MakerTaker(makerBps: 10m, takerBps: 20m),
        Slippage = new(SlippageModelType.VolumeProportional, BpsPerLotSize: 2m),
        FillBehavior = FillBehavior.PartialFillOnTrade
    };

    /// <summary>
    /// Equity market preset (US stocks).
    /// Fixed fee per trade, moderate queue, no slippage.
    /// </summary>
    public static SimulationConfig USEquities() => new()
    {
        Fidelity = SimulationFidelity.Queue,
        Latency = new(Duration.FromMicros(100), Duration.FromMicros(100), StdDevFraction: 0.15),
        QueueModel = QueueParams.PowerQuadratic(),
        Fees = FeeParams.Fixed(new Money(0.50m, Currency.USD)),
        Slippage = SlippageParams.None,
        FillBehavior = FillBehavior.NoPartialFill,
        Settlement = SettlementParams.TPlus(1, ClearingCalendar.ForVenue(Venue.NYSE))
    };

    /// <summary>
    /// Instant preset: zero latency, always front of queue, no fees.
    /// Use for strategy logic testing only (unrealistic).
    /// </summary>
    public static SimulationConfig Instant() => new()
    {
        Fidelity = SimulationFidelity.Queue,
        Latency = new(Duration.Zero, Duration.Zero),
        QueueModel = QueueParams.AlwaysFront(),
        Fees = FeeParams.Zero,
        Slippage = SlippageParams.None,
        FillBehavior = FillBehavior.NoPartialFill
    };
}

/// <summary>
/// Market depth level requirement.
/// </summary>
public enum DepthLevel : byte
{
    /// <summary>
    /// Level 1: Top-of-book only (best bid/ask).
    /// Queue simulation only applies when order is at best.
    /// </summary>
    L1_TopOfBook = 1,

    /// <summary>
    /// Level 2: Market-By-Price (full depth).
    /// Queue simulation works at all price levels.
    /// </summary>
    L2_MarketByPrice = 2
}

/// <summary>
/// Account type for capital constraints simulation.
/// Determines how order notional value affects available capital.
/// </summary>
public enum AccountType : byte
{
    /// <summary>
    /// Cash account: Locks full notional value when order is submitted.
    /// No leverage - cannot trade beyond available cash balance.
    /// Common for spot trading (stocks, crypto spot).
    /// </summary>
    Cash = 1,

    /// <summary>
    /// Margin account: Locks margin based on leverage.
    /// Allows leveraged trading - can control larger positions with less capital.
    /// Common for derivatives (futures, options, CFDs).
    /// Note: Margin calculation requires IMarginModel (future specification).
    /// </summary>
    Margin = 2
}

/// <summary>
/// Basic margin requirements for replay capital checks.
/// </summary>
public readonly record struct MarginParams(
    decimal InitialMarginFraction,
    decimal MaintenanceMarginFraction,
    Duration MarginCallGracePeriod,
    LiquidationPolicy LiquidationPolicy,
    ShortSalePolicy ShortSalePolicy,
    RehypothecationPolicy RehypothecationPolicy,
    IReadOnlyDictionary<Instrument, Qty> BorrowAvailability,
    IReadOnlyDictionary<Instrument, Qty> RehypothecationAvailability)
{
    public static MarginParams RegT()
        => new(
            0.50m,
            0.25m,
            Duration.Zero,
            LiquidationPolicy.CancelOpenOrdersAndFlatten,
            ShortSalePolicy.RequireBorrow,
            RehypothecationPolicy.Prohibited,
            new Dictionary<Instrument, Qty>(),
            new Dictionary<Instrument, Qty>());

    public static MarginParams Leverage(decimal leverage)
    {
        if (leverage <= 0m)
            throw new ArgumentOutOfRangeException(nameof(leverage), "Leverage must be positive.");

        var initial = 1m / leverage;
        return new(
            initial,
            initial / 2m,
            Duration.Zero,
            LiquidationPolicy.CancelOpenOrdersAndFlatten,
            ShortSalePolicy.AllowNakedShort,
            RehypothecationPolicy.Allowed,
            new Dictionary<Instrument, Qty>(),
            new Dictionary<Instrument, Qty>());
    }

    public MarginParams WithMarginCallGracePeriod(Duration gracePeriod)
    {
        if (gracePeriod < Duration.Zero)
            throw new ArgumentOutOfRangeException(nameof(gracePeriod), "Margin call grace period cannot be negative.");

        return this with { MarginCallGracePeriod = gracePeriod };
    }

    public MarginParams WithLiquidationPolicy(LiquidationPolicy policy)
    {
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy), "Liquidation policy is not supported.");

        return this with { LiquidationPolicy = policy };
    }

    public MarginParams WithShortSalePolicy(ShortSalePolicy policy)
    {
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy), "Short sale policy is not supported.");

        return this with { ShortSalePolicy = policy };
    }

    public MarginParams WithRehypothecationPolicy(RehypothecationPolicy policy)
    {
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy), "Rehypothecation policy is not supported.");

        return this with { RehypothecationPolicy = policy };
    }

    public MarginParams WithBorrowAvailability(Instrument instrument, Qty quantity)
    {
        if (quantity.IsNegative)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Borrow availability cannot be negative.");

        var availability = new Dictionary<Instrument, Qty>(BorrowAvailability);
        if (quantity.IsZero)
            availability.Remove(instrument);
        else
            availability[instrument] = quantity;

        return this with { BorrowAvailability = availability };
    }

    public MarginParams WithRehypothecationAvailability(Instrument instrument, Qty quantity)
    {
        if (quantity.IsNegative)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Rehypothecation availability cannot be negative.");

        var availability = new Dictionary<Instrument, Qty>(RehypothecationAvailability);
        if (quantity.IsZero)
            availability.Remove(instrument);
        else
            availability[instrument] = quantity;

        return this with { RehypothecationAvailability = availability };
    }
}

/// <summary>
/// Replay margin-call action when a breached account reaches its due time.
/// </summary>
public enum LiquidationPolicy : byte
{
    /// <summary>
    /// Cancel open orders, emit the margin breach, and leave positions open for external handling.
    /// </summary>
    CancelOpenOrdersOnly = 1,

    /// <summary>
    /// Cancel open orders and flatten breached positions at the current mark.
    /// </summary>
    CancelOpenOrdersAndFlatten = 2,

    /// <summary>
    /// Cancel open orders and reduce marked exposure only until maintenance coverage is restored.
    /// </summary>
    CancelOpenOrdersAndReduceToMaintenance = 3
}

/// <summary>
/// Replay margin-account rule for sell orders that would create or increase a short position.
/// </summary>
public enum ShortSalePolicy : byte
{
    /// <summary>
    /// Sell orders cannot exceed currently available long inventory.
    /// </summary>
    RequireBorrow = 1,

    /// <summary>
    /// Sell orders may create short inventory when margin buying power is sufficient.
    /// </summary>
    AllowNakedShort = 2
}

/// <summary>
/// Replay margin-account rule for whether settled long custody can be reused as lendable collateral.
/// </summary>
public enum RehypothecationPolicy : byte
{
    /// <summary>
    /// Settled custody remains segregated and is not reported as reusable collateral.
    /// </summary>
    Prohibited = 1,

    /// <summary>
    /// Settled long custody is reported as reusable collateral for broker-style margin simulations.
    /// </summary>
    Allowed = 2
}

/// <summary>
/// Settlement behavior for replayed cash-account proceeds.
/// </summary>
public readonly record struct SettlementParams
{
    public Duration CashProceedsDelay { get; }
    public int BusinessDays { get; }
    public ClearingCalendar? Calendar { get; }
    public UnsettledSalePolicy UnsettledSalePolicy { get; }
    public IReadOnlySet<DateOnly>? Holidays => Calendar?.Holidays;
    public bool UsesBusinessDayCalendar => BusinessDays > 0;

    public SettlementParams(
        Duration cashProceedsDelay,
        UnsettledSalePolicy unsettledSalePolicy = UnsettledSalePolicy.Reject)
    {
        if (cashProceedsDelay < Duration.Zero)
            throw new ArgumentOutOfRangeException(nameof(cashProceedsDelay), "Settlement delay cannot be negative.");
        if (!Enum.IsDefined(unsettledSalePolicy))
            throw new ArgumentOutOfRangeException(nameof(unsettledSalePolicy), "Unsettled sale policy is not supported.");

        CashProceedsDelay = cashProceedsDelay;
        BusinessDays = 0;
        Calendar = null;
        UnsettledSalePolicy = unsettledSalePolicy;
    }

    private SettlementParams(
        int businessDays,
        ClearingCalendar calendar,
        UnsettledSalePolicy unsettledSalePolicy = UnsettledSalePolicy.Reject)
    {
        if (businessDays < 0)
            throw new ArgumentOutOfRangeException(nameof(businessDays), "Settlement delay cannot be negative.");
        if (!Enum.IsDefined(unsettledSalePolicy))
            throw new ArgumentOutOfRangeException(nameof(unsettledSalePolicy), "Unsettled sale policy is not supported.");

        CashProceedsDelay = Duration.FromDays(businessDays);
        BusinessDays = businessDays;
        Calendar = calendar;
        UnsettledSalePolicy = unsettledSalePolicy;
    }

    public static SettlementParams Immediate() => new(Duration.Zero);

    public static SettlementParams CalendarDays(int calendarDays) => new(Duration.FromDays(calendarDays));

    public SettlementParams WithUnsettledSalePolicy(UnsettledSalePolicy policy)
    {
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy), "Unsettled sale policy is not supported.");

        return Calendar is null
            ? new SettlementParams(CashProceedsDelay, policy)
            : new SettlementParams(BusinessDays, Calendar, policy);
    }

    public static SettlementParams TPlus(int businessDays, IEnumerable<DateOnly>? holidays = null)
    {
        if (businessDays < 0)
            throw new ArgumentOutOfRangeException(nameof(businessDays), "Settlement delay cannot be negative.");

        return TPlus(businessDays, ClearingCalendar.Weekdays(holidays));
    }

    public static SettlementParams TPlus(int businessDays, ClearingCalendar calendar)
    {
        if (businessDays < 0)
            throw new ArgumentOutOfRangeException(nameof(businessDays), "Settlement delay cannot be negative.");
        ArgumentNullException.ThrowIfNull(calendar);

        return new SettlementParams(businessDays, calendar);
    }

    public static SettlementParams TPlusForVenue(
        int businessDays,
        Venue venue,
        int year,
        IEnumerable<DateOnly>? additionalHolidays = null)
        => TPlus(businessDays, ClearingCalendarCatalog.ForVenue(venue, year, additionalHolidays));

    public static SettlementParams TPlusForVenue(
        int businessDays,
        Venue venue,
        DateOnly start,
        DateOnly end,
        IEnumerable<DateOnly>? additionalHolidays = null)
        => TPlus(businessDays, ClearingCalendarCatalog.ForVenue(venue, start, end, additionalHolidays));

    public Instant GetSettlementTime(Instant tradeTime)
    {
        if (!UsesBusinessDayCalendar)
            return tradeTime + CashProceedsDelay;

        var dto = tradeTime.ToDateTimeOffset();
        var settlementDate = DateOnly.FromDateTime(dto.UtcDateTime);
        var remaining = BusinessDays;
        while (remaining > 0)
        {
            settlementDate = settlementDate.AddDays(1);
            if (Calendar!.IsBusinessDay(settlementDate))
                remaining--;
        }

        var settlementDateTime = settlementDate.ToDateTime(TimeOnly.FromDateTime(dto.UtcDateTime), DateTimeKind.Utc);
        return Instant.FromDateTimeOffset(new DateTimeOffset(settlementDateTime, TimeSpan.Zero));
    }

}

/// <summary>
/// Cash-account policy for selling assets whose custody delivery has not settled.
/// </summary>
public enum UnsettledSalePolicy : byte
{
    /// <summary>
    /// Allow sales against total economic position, including pending delivery.
    /// </summary>
    Allow = 1,

    /// <summary>
    /// Reject sales that exceed settled custody quantity.
    /// </summary>
    Reject = 2
}

/// <summary>
/// Business-day calendar used by replay settlement.
/// Presets define trading-day shape; callers provide the exact holiday dates for their replay horizon.
/// </summary>
public sealed record ClearingCalendar
{
    private static readonly IReadOnlySet<DayOfWeek> WeekdaySet = new HashSet<DayOfWeek>
    {
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    };

    private static readonly IReadOnlySet<DayOfWeek> AllDaysSet = new HashSet<DayOfWeek>
    {
        DayOfWeek.Sunday,
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday
    };

    public string Name { get; }
    public IReadOnlySet<DayOfWeek> BusinessDays { get; }
    public IReadOnlySet<DateOnly> Holidays { get; }

    public ClearingCalendar(
        string name,
        IEnumerable<DayOfWeek> businessDays,
        IEnumerable<DateOnly>? holidays = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Clearing calendar name is required.", nameof(name));

        var businessDaySet = new HashSet<DayOfWeek>(businessDays);
        if (businessDaySet.Count == 0)
            throw new ArgumentException("Clearing calendar must contain at least one business day.", nameof(businessDays));

        Name = name;
        BusinessDays = businessDaySet;
        Holidays = holidays is null
            ? new HashSet<DateOnly>()
            : new HashSet<DateOnly>(holidays);
    }

    public static ClearingCalendar Weekdays(IEnumerable<DateOnly>? holidays = null)
        => new("Weekdays", WeekdaySet, holidays);

    public static ClearingCalendar AlwaysOpen(IEnumerable<DateOnly>? holidays = null)
        => new("AlwaysOpen", AllDaysSet, holidays);

    public static ClearingCalendar USEquities(IEnumerable<DateOnly>? holidays = null)
        => new("US Equities", WeekdaySet, holidays);

    public static ClearingCalendar USFutures(IEnumerable<DateOnly>? holidays = null)
        => new("US Futures", WeekdaySet, holidays);

    public static ClearingCalendar Crypto(IEnumerable<DateOnly>? holidays = null)
        => new("Crypto", AllDaysSet, holidays);

    public static ClearingCalendar ForVenue(Venue venue, IEnumerable<DateOnly>? holidays = null)
    {
        var venueName = venue.Name.ToUpperInvariant();
        return venueName switch
        {
            "BINANCE" or "COINBASE" or "KRAKEN" => Crypto(holidays),
            "CME" => USFutures(holidays),
            "NYSE" or "NASDAQ" => USEquities(holidays),
            _ => new ClearingCalendar($"{venue.Name} Clearing", WeekdaySet, holidays)
        };
    }

    public bool IsBusinessDay(DateOnly date)
        => BusinessDays.Contains(date.DayOfWeek) && !Holidays.Contains(date);
}
