using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Rhodium.Primitives;

/// <summary>
/// Canonical instrument definition. This describes what an instrument is;
/// simulation profiles describe how a specific run behaves.
/// </summary>
public sealed record InstrumentContract
{
    public required Instrument Instrument { get; init; }
    public ContractIdentity? Identity { get; init; }
    public required TradingGrid Grid { get; init; }
    public TradingConstraints Constraints { get; init; } = TradingConstraints.None;
    public required EconomicExposure Exposure { get; init; }
    public required ContractLifecycle Lifecycle { get; init; }
    public required SettlementTerms Settlement { get; init; }
    public required MarginTerms Margin { get; init; }
    public required FeeTerms Fees { get; init; }
    public FinancingTerms Financing { get; init; } = FinancingTerms.None;
    public PayoffTerms Payoff { get; init; } = PayoffTerms.Linear;
    public VenueRules VenueRules { get; init; } = VenueRules.Default;
    public DataSemantics Data { get; init; } = DataSemantics.TradablePrice;
    public IReadOnlyList<InstrumentLeg> Legs { get; init; } = [];
    public PackageTerms? Package { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
}

public sealed record ContractIdentity(
    string Symbol,
    Venue Venue,
    string? RawSymbol = null,
    string? ExchangeMic = null,
    string? CanonicalSymbol = null,
    string? SeriesId = null)
{
    public static ContractIdentity FromInstrument(Instrument instrument) =>
        new(instrument.Asset.Symbol, instrument.Venue);
}

public readonly record struct TradingGrid(
    decimal PriceIncrement,
    decimal SizeIncrement,
    int PricePrecision = 0,
    int SizePrecision = 0,
    decimal LotSize = 1m,
    PriceIncrementRule? PriceIncrementRule = null);

public readonly record struct TradingConstraints(
    Qty? MinQuantity = null,
    Qty? MaxQuantity = null,
    Money? MinNotional = null,
    Money? MaxNotional = null,
    Price? MinPrice = null,
    Price? MaxPrice = null)
{
    public static readonly TradingConstraints None = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(PriceIncrementRule.Fixed), "fixed")]
[JsonDerivedType(typeof(PriceIncrementRule.Piecewise), "piecewise")]
public abstract record PriceIncrementRule
{
    public sealed record Fixed(decimal Increment) : PriceIncrementRule;
    public sealed record Piecewise(IReadOnlyList<PriceIncrementBand> Bands) : PriceIncrementRule;
}

public readonly record struct PriceIncrementBand(
    Price? MinPrice,
    Price? MaxPrice,
    decimal Increment);

public sealed record PackageTerms(
    PackageKind Kind,
    bool IsAtomicExecution = true,
    bool NetPremium = true,
    bool IsRecognizedStrategy = false);

public enum PackageKind : byte
{
    Generic,
    FuturesSpread,
    OptionSpread
}

public static class TradingGridExtensions
{
    public static TickPrice ToTick(this TradingGrid grid, Price price) =>
        TickPrice.FromPrice(price, grid.PriceIncrement);

    public static Price FromTick(this TradingGrid grid, TickPrice tick, Currency currency) =>
        new(tick.Ticks * grid.PriceIncrement, currency);

    public static decimal PriceIncrementFor(this TradingGrid grid, Price price)
    {
        if (grid.PriceIncrementRule is null)
            return grid.PriceIncrement;

        return grid.PriceIncrementRule switch
        {
            PriceIncrementRule.Fixed fixedRule => fixedRule.Increment,
            PriceIncrementRule.Piecewise piecewise => EffectivePiecewiseIncrement(piecewise, price) ?? grid.PriceIncrement,
            _ => grid.PriceIncrement
        };
    }

    public static TickPrice ToTickUsingRule(this TradingGrid grid, Price price) =>
        TickPrice.FromPrice(price, grid.PriceIncrementFor(price));

    private static decimal? EffectivePiecewiseIncrement(PriceIncrementRule.Piecewise rule, Price price)
    {
        foreach (var band in rule.Bands)
        {
            if (band.MinPrice is { } min && price.Value < min.Value)
                continue;
            if (band.MaxPrice is { } max && price.Value >= max.Value)
                continue;

            return band.Increment;
        }

        return null;
    }

    public static Qty RoundSize(this TradingGrid grid, Qty qty)
    {
        if (grid.SizeIncrement <= 0m) throw new InvalidOperationException("SizeIncrement must be positive.");
        return new(Math.Floor(qty.Value / grid.SizeIncrement) * grid.SizeIncrement);
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(EconomicExposure.Spot), "spot")]
[JsonDerivedType(typeof(EconomicExposure.Linear), "linear")]
[JsonDerivedType(typeof(EconomicExposure.Inverse), "inverse")]
[JsonDerivedType(typeof(EconomicExposure.Quanto), "quanto")]
[JsonDerivedType(typeof(EconomicExposure.Reference), "reference")]
[JsonDerivedType(typeof(EconomicExposure.Formula), "formula")]
public abstract record EconomicExposure
{
    public sealed record Spot(Currency QuoteCurrency, Currency? BaseCurrency = null) : EconomicExposure;
    public sealed record Linear(Currency QuoteCurrency, decimal Multiplier = 1m, Currency? BaseCurrency = null) : EconomicExposure;
    public sealed record Inverse(Currency BaseCurrency, Currency QuoteCurrency, Currency SettlementCurrency, decimal Multiplier = 1m) : EconomicExposure;
    public sealed record Quanto(Currency UnderlyingCurrency, Currency QuoteCurrency, Currency SettlementCurrency, decimal Multiplier, decimal ConversionRate) : EconomicExposure;
    public sealed record Reference(Currency QuoteCurrency) : EconomicExposure;
    public sealed record Formula(string Expression, Currency QuoteCurrency) : EconomicExposure;
}

public enum EconomicExposureKind : byte
{
    Spot,
    Linear,
    Inverse,
    Quanto,
    Reference,
    Formula
}

public static class EconomicExposureExtensions
{
    public static EconomicExposureKind Kind(this EconomicExposure exposure) => exposure switch
    {
        EconomicExposure.Spot => EconomicExposureKind.Spot,
        EconomicExposure.Linear => EconomicExposureKind.Linear,
        EconomicExposure.Inverse => EconomicExposureKind.Inverse,
        EconomicExposure.Quanto => EconomicExposureKind.Quanto,
        EconomicExposure.Reference => EconomicExposureKind.Reference,
        EconomicExposure.Formula => EconomicExposureKind.Formula,
        _ => throw new ArgumentOutOfRangeException(nameof(exposure), exposure, "Unknown economic exposure.")
    };

    public static Currency QuoteCurrency(this EconomicExposure exposure) => exposure switch
    {
        EconomicExposure.Spot spot => spot.QuoteCurrency,
        EconomicExposure.Linear linear => linear.QuoteCurrency,
        EconomicExposure.Inverse inverse => inverse.QuoteCurrency,
        EconomicExposure.Quanto quanto => quanto.QuoteCurrency,
        EconomicExposure.Reference reference => reference.QuoteCurrency,
        EconomicExposure.Formula formula => formula.QuoteCurrency,
        _ => Currency.None
    };

    public static Currency SettlementCurrency(this EconomicExposure exposure) => exposure switch
    {
        EconomicExposure.Inverse inverse => inverse.SettlementCurrency,
        EconomicExposure.Quanto quanto => quanto.SettlementCurrency,
        _ => exposure.QuoteCurrency()
    };

    public static decimal Multiplier(this EconomicExposure exposure) => exposure switch
    {
        EconomicExposure.Linear linear => linear.Multiplier,
        EconomicExposure.Inverse inverse => inverse.Multiplier,
        EconomicExposure.Quanto quanto => quanto.Multiplier,
        _ => 1m
    };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(ContractLifecycle.Cash), "cash")]
[JsonDerivedType(typeof(ContractLifecycle.Expiring), "expiring")]
[JsonDerivedType(typeof(ContractLifecycle.Perpetual), "perpetual")]
[JsonDerivedType(typeof(ContractLifecycle.EventSettled), "event-settled")]
public abstract record ContractLifecycle
{
    public sealed record Cash() : ContractLifecycle;
    public sealed record Expiring(Instant Expiry, ExpiryAction Action) : ContractLifecycle;
    public sealed record Perpetual(FundingSchedule? FundingSchedule = null) : ContractLifecycle;
    public sealed record EventSettled(Instant? EventTime, string EventKey) : ContractLifecycle;
}

public enum ExpiryAction : byte
{
    CashSettle,
    PhysicalDelivery,
    Exercise,
    ExpireWorthless,
    RollRequired
}

public readonly record struct FundingSchedule(Duration Interval);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(SettlementTerms.Immediate), "immediate")]
[JsonDerivedType(typeof(SettlementTerms.Cash), "cash")]
[JsonDerivedType(typeof(SettlementTerms.Physical), "physical")]
[JsonDerivedType(typeof(SettlementTerms.Binary), "binary")]
public abstract record SettlementTerms
{
    public sealed record Immediate(Currency Currency) : SettlementTerms;
    public sealed record Cash(Currency Currency, SettlementDelay Delay) : SettlementTerms;
    public sealed record Physical(Currency CashCurrency, Instrument Deliverable, SettlementDelay Delay) : SettlementTerms;
    public sealed record Binary(Currency Currency, Money Payout, SettlementDelay Delay) : SettlementTerms;
}

public readonly record struct SettlementDelay(int BusinessDays, string CalendarCode)
{
    public static SettlementDelay Immediate(string calendarCode = "ALWAYS") => new(0, calendarCode);
    public static SettlementDelay TPlus(int days, string calendarCode) => new(days, calendarCode);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(MarginTerms.CashMargin), "cash")]
[JsonDerivedType(typeof(MarginTerms.RegT), "reg-t")]
[JsonDerivedType(typeof(MarginTerms.FixedFraction), "fixed-fraction")]
[JsonDerivedType(typeof(MarginTerms.Portfolio), "portfolio")]
public abstract record MarginTerms
{
    public static readonly MarginTerms Cash = new CashMargin();

    public sealed record CashMargin() : MarginTerms;
    public sealed record RegT(decimal Initial = 0.5m, decimal Maintenance = 0.25m) : MarginTerms;
    public sealed record FixedFraction(decimal Initial, decimal Maintenance) : MarginTerms;
    public sealed record Portfolio(string ModelId) : MarginTerms;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(FeeTerms.NoFees), "none")]
[JsonDerivedType(typeof(FeeTerms.MakerTaker), "maker-taker")]
[JsonDerivedType(typeof(FeeTerms.PerUnit), "per-unit")]
[JsonDerivedType(typeof(FeeTerms.PerTrade), "per-trade")]
public abstract record FeeTerms
{
    public static readonly FeeTerms None = new NoFees();

    public sealed record NoFees() : FeeTerms;
    public sealed record MakerTaker(decimal MakerBps, decimal TakerBps) : FeeTerms;
    public sealed record PerUnit(Money Maker, Money Taker) : FeeTerms;
    public sealed record PerTrade(Money Amount) : FeeTerms;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(FinancingTerms.NoFinancing), "none")]
[JsonDerivedType(typeof(FinancingTerms.PerpetualFunding), "perpetual-funding")]
[JsonDerivedType(typeof(FinancingTerms.ForexRollover), "forex-rollover")]
[JsonDerivedType(typeof(FinancingTerms.Borrow), "borrow")]
public abstract record FinancingTerms
{
    public static readonly FinancingTerms None = new NoFinancing();

    public sealed record NoFinancing() : FinancingTerms;
    public sealed record PerpetualFunding(FundingSchedule Schedule, string RateSource) : FinancingTerms;
    public sealed record ForexRollover(string RateSource, DayCountBasis Basis) : FinancingTerms;
    public sealed record Borrow(string RateSource, DayCountBasis Basis) : FinancingTerms;
}

public enum DayCountBasis : byte
{
    Act360,
    Act365
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(PayoffTerms.LinearPayoff), "linear")]
[JsonDerivedType(typeof(PayoffTerms.Option), "option")]
[JsonDerivedType(typeof(PayoffTerms.Binary), "binary")]
[JsonDerivedType(typeof(PayoffTerms.Cfd), "cfd")]
[JsonDerivedType(typeof(PayoffTerms.Betting), "betting")]
public abstract record PayoffTerms
{
    public static readonly PayoffTerms Linear = new LinearPayoff();

    public sealed record LinearPayoff() : PayoffTerms;
    public sealed record Option : PayoffTerms
    {
        [JsonConstructor]
        public Option(OptionTerms terms)
        {
            ArgumentNullException.ThrowIfNull(terms);
            Terms = terms;
        }

        public OptionTerms Terms { get; }
    }

    public sealed record Binary(string OutcomeKey, Money Payout, BinaryOutcomeConvention Convention) : PayoffTerms;
    public sealed record Cfd(Instrument Underlying) : PayoffTerms;
    public sealed record Betting(string MarketId, string SelectionId, OddsConvention OddsConvention) : PayoffTerms;
}

public sealed record OptionTerms
{
    [JsonConstructor]
    public OptionTerms(
        Instrument underlying,
        OptionStrikeTerms strike,
        OptionRight right,
        ExerciseStyle exerciseStyle,
        OptionSettlementStyle settlementStyle,
        Instant activation,
        Instant expiration,
        decimal contractMultiplier,
        decimal contractUnitOfTrade,
        OptionExpirationCycle expirationCycle,
        OptionPremiumStyle premiumStyle,
        OptionExercisePolicy exercisePolicy,
        OptionAssignmentPolicy assignmentPolicy,
        IReadOnlyList<Instant>? exerciseDates)
    {
        if (!Enum.IsDefined(right))
            throw new ArgumentOutOfRangeException(nameof(right), right, "Unknown option right.");
        if (!Enum.IsDefined(exerciseStyle))
            throw new ArgumentOutOfRangeException(nameof(exerciseStyle), exerciseStyle, "Unknown exercise style.");
        if (!Enum.IsDefined(settlementStyle))
            throw new ArgumentOutOfRangeException(nameof(settlementStyle), settlementStyle, "Unknown option settlement style.");
        if (!Enum.IsDefined(expirationCycle))
            throw new ArgumentOutOfRangeException(nameof(expirationCycle), expirationCycle, "Unknown option expiration cycle.");
        if (!Enum.IsDefined(premiumStyle))
            throw new ArgumentOutOfRangeException(nameof(premiumStyle), premiumStyle, "Unknown option premium style.");
        if (!Enum.IsDefined(exercisePolicy))
            throw new ArgumentOutOfRangeException(nameof(exercisePolicy), exercisePolicy, "Unknown option exercise policy.");
        if (!Enum.IsDefined(assignmentPolicy))
            throw new ArgumentOutOfRangeException(nameof(assignmentPolicy), assignmentPolicy, "Unknown option assignment policy.");
        if (contractMultiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(contractMultiplier), contractMultiplier, "Option contract multiplier must be positive.");
        if (contractUnitOfTrade <= 0m)
            throw new ArgumentOutOfRangeException(nameof(contractUnitOfTrade), contractUnitOfTrade, "Option contract unit of trade must be positive.");
        if (activation > expiration)
            throw new ArgumentException("Option activation must be before or equal to expiration.", nameof(activation));

        var dates = SnapshotExerciseDates(exerciseDates);
        if (exerciseStyle == ExerciseStyle.Bermudan && dates.Count == 0)
            throw new ArgumentException("Bermudan options require explicit exercise dates.", nameof(exerciseDates));
        if (exerciseStyle != ExerciseStyle.Bermudan && dates.Count > 0)
            throw new ArgumentException("Exercise dates are only valid for Bermudan options.", nameof(exerciseDates));
        ValidateExerciseDates(dates, activation, expiration, nameof(exerciseDates));

        Underlying = underlying;
        Strike = strike;
        Right = right;
        ExerciseStyle = exerciseStyle;
        SettlementStyle = settlementStyle;
        Activation = activation;
        Expiration = expiration;
        ContractMultiplier = contractMultiplier;
        ContractUnitOfTrade = contractUnitOfTrade;
        ExpirationCycle = expirationCycle;
        PremiumStyle = premiumStyle;
        ExercisePolicy = exercisePolicy;
        AssignmentPolicy = assignmentPolicy;
        ExerciseDates = dates;
    }

    public Instrument Underlying { get; }
    public OptionStrikeTerms Strike { get; }
    public OptionRight Right { get; }
    public ExerciseStyle ExerciseStyle { get; }
    public OptionSettlementStyle SettlementStyle { get; }
    public Instant Activation { get; }
    public Instant Expiration { get; }
    public decimal ContractMultiplier { get; }
    public decimal ContractUnitOfTrade { get; }
    public OptionExpirationCycle ExpirationCycle { get; }
    public OptionPremiumStyle PremiumStyle { get; }
    public OptionExercisePolicy ExercisePolicy { get; }
    public OptionAssignmentPolicy AssignmentPolicy { get; }
    public IReadOnlyList<Instant> ExerciseDates { get; }

    public OptionTerms With(
        OptionRight? right = null,
        ExerciseStyle? exerciseStyle = null,
        OptionSettlementStyle? settlementStyle = null,
        OptionExpirationCycle? expirationCycle = null,
        OptionPremiumStyle? premiumStyle = null,
        OptionExercisePolicy? exercisePolicy = null,
        OptionAssignmentPolicy? assignmentPolicy = null,
        IReadOnlyList<Instant>? exerciseDates = null)
        => new(
            Underlying,
            Strike,
            right ?? Right,
            exerciseStyle ?? ExerciseStyle,
            settlementStyle ?? SettlementStyle,
            Activation,
            Expiration,
            ContractMultiplier,
            ContractUnitOfTrade,
            expirationCycle ?? ExpirationCycle,
            premiumStyle ?? PremiumStyle,
            exercisePolicy ?? ExercisePolicy,
            assignmentPolicy ?? AssignmentPolicy,
            exerciseDates ?? ExerciseDates);

    private static IReadOnlyList<Instant> SnapshotExerciseDates(IReadOnlyList<Instant>? exerciseDates)
    {
        if (exerciseDates is null || exerciseDates.Count == 0)
            return Array.Empty<Instant>();

        return new ReadOnlyCollection<Instant>(exerciseDates.ToArray());
    }

    private static void ValidateExerciseDates(
        IReadOnlyList<Instant> exerciseDates,
        Instant activation,
        Instant expiration,
        string paramName)
    {
        Instant? previous = null;
        foreach (var date in exerciseDates)
        {
            if (date < activation || date > expiration)
                throw new ArgumentException("Option exercise dates must fall between activation and expiration.", paramName);

            if (previous is { } last && date <= last)
                throw new ArgumentException("Option exercise dates must be strictly ascending.", paramName);

            previous = date;
        }
    }
}

public readonly record struct OptionStrikeTerms
{
    [JsonConstructor]
    public OptionStrikeTerms(Price strike, decimal strikeMultiplier = 1m)
    {
        if (strike.Value <= 0m)
            throw new ArgumentOutOfRangeException(nameof(strike), strike, "Option strike must be positive.");
        if (strikeMultiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(strikeMultiplier), strikeMultiplier, "Option strike multiplier must be positive.");

        Strike = strike;
        StrikeMultiplier = strikeMultiplier;
    }

    public Price Strike { get; }
    public decimal StrikeMultiplier { get; }
    public Price ScaledStrike => new(Strike.Value * StrikeMultiplier, Strike.Currency);
}

public enum OptionRight : byte { Call, Put }
public enum ExerciseStyle : byte { American, European, Bermudan }
public enum OptionSettlementStyle : byte { Cash, Physical }
public enum OptionExpirationCycle : byte { Standard, Weekly, Quarterly, Serial, Flex }
public enum OptionPremiumStyle : byte { Upfront, FuturesStyle, Deferred }
public enum OptionExercisePolicy : byte { Manual, AutoExerciseInTheMoney, CashSettledAtExpiry, VenueDefined }
public enum OptionAssignmentPolicy : byte { None, Random, ProRata, NoArbitrageHeuristic, VenueDefined }
public enum BinaryOutcomeConvention : byte { PaysOneOrZero }
public enum OddsConvention : byte { Decimal }

public readonly record struct InstrumentLeg(
    Instrument Instrument,
    decimal Ratio,
    Side Side,
    LegRole Role = LegRole.Component);

public enum LegRole : byte
{
    Component,
    Underlying,
    Hedge,
    Deliverable,
    Reference
}

public sealed record VenueRules
{
    public static readonly VenueRules Default = new();

    public bool IsTradable { get; init; } = true;
    public bool SupportsMarketData { get; init; } = true;
    public bool SupportsExecution { get; init; } = true;
    public bool AllowShorting { get; init; } = true;
    public OrderTypeMask AllowedOrderTypes { get; init; } = OrderTypeMask.All;
    public TimeInForceMask AllowedTimeInForce { get; init; } = TimeInForceMask.All;
}

[Flags]
public enum OrderTypeMask : ushort
{
    None = 0,
    Market = 1 << 0,
    Limit = 1 << 1,
    StopMarket = 1 << 2,
    StopLimit = 1 << 3,
    All = Market | Limit | StopMarket | StopLimit
}

[Flags]
public enum TimeInForceMask : ushort
{
    None = 0,
    Day = 1 << 0,
    Gtc = 1 << 1,
    Ioc = 1 << 2,
    Fok = 1 << 3,
    Gtd = 1 << 4,
    All = Day | Gtc | Ioc | Fok | Gtd
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(DataSemantics.Tradable), "tradable")]
[JsonDerivedType(typeof(DataSemantics.Observable), "observable")]
[JsonDerivedType(typeof(DataSemantics.Custom), "custom")]
public abstract record DataSemantics
{
    public static readonly DataSemantics TradablePrice = new Tradable(MarketDataKind.Trade);

    public sealed record Tradable(
        MarketDataKind PrimaryKind,
        bool SupportsQuotes = true,
        bool SupportsTrades = true,
        bool SupportsBars = true) : DataSemantics;

    public sealed record Observable(
        ObservableKind Kind,
        Currency? QuoteCurrency = null,
        bool CanDriveMarks = true,
        bool CanBeUnderlying = true) : DataSemantics;

    public sealed record Custom(
        string DataType,
        string? SchemaId = null,
        bool CanDriveSignals = true,
        bool CanDriveMarks = false) : DataSemantics;
}

public enum MarketDataKind : byte { Trade, Quote, Bar, Book }

public enum ObservableKind : byte
{
    IndexLevel,
    ReferenceRate,
    FundingRate,
    BorrowRate,
    VolatilitySurface,
    EventOutcome,
    Benchmark,
    AlternativeData
}

public readonly record struct ContractValidationIssue(
    string Code,
    string Message);

public sealed record ContractValidationResult(IReadOnlyList<ContractValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public void ThrowIfInvalid()
    {
        if (IsValid) return;

        throw new InvalidOperationException(string.Join("; ", Issues.Select(static issue => $"{issue.Code}: {issue.Message}")));
    }
}

public static class InstrumentContractValidator
{
    public static ContractValidationResult Validate(InstrumentContract contract)
    {
        var issues = new List<ContractValidationIssue>();

        if (string.IsNullOrWhiteSpace(contract.Instrument.Asset.Symbol))
            issues.Add(new ContractValidationIssue("instrument.symbol.empty", "Instrument symbol is required."));

        if (string.IsNullOrWhiteSpace(contract.Instrument.Venue.Name))
            issues.Add(new ContractValidationIssue("instrument.venue.empty", "Instrument venue is required."));

        if (contract.Grid.PriceIncrement <= 0m)
            issues.Add(new ContractValidationIssue("grid.priceIncrement.nonPositive", "Price increment must be positive."));

        if (contract.Grid.SizeIncrement <= 0m)
            issues.Add(new ContractValidationIssue("grid.sizeIncrement.nonPositive", "Size increment must be positive."));

        if (contract.Grid.LotSize <= 0m)
            issues.Add(new ContractValidationIssue("grid.lotSize.nonPositive", "Lot size must be positive."));

        if (contract.Constraints.MinPrice is { } minPrice &&
            contract.Constraints.MaxPrice is { } maxPrice &&
            minPrice.Value > maxPrice.Value)
            issues.Add(new ContractValidationIssue("constraints.price.range.invalid", "Minimum price must be less than or equal to maximum price."));

        if (contract.Constraints.MinQuantity is { } minQuantity &&
            contract.Constraints.MaxQuantity is { } maxQuantity &&
            minQuantity.Value > maxQuantity.Value)
            issues.Add(new ContractValidationIssue("constraints.quantity.range.invalid", "Minimum quantity must be less than or equal to maximum quantity."));

        if (contract.Constraints.MinNotional is { } minNotional &&
            contract.Constraints.MaxNotional is { } maxNotional &&
            minNotional.Amount > maxNotional.Amount)
            issues.Add(new ContractValidationIssue("constraints.notional.range.invalid", "Minimum notional must be less than or equal to maximum notional."));

        ValidateExposure(contract, issues);
        ValidateLifecycle(contract, issues);
        ValidatePayoff(contract, issues);
        ValidateDataSemantics(contract, issues);
        ValidateLegs(contract, issues);

        return new ContractValidationResult(issues);
    }

    private static void ValidateExposure(InstrumentContract contract, List<ContractValidationIssue> issues)
    {
        switch (contract.Exposure)
        {
            case EconomicExposure.Linear { Multiplier: <= 0m }:
            case EconomicExposure.Inverse { Multiplier: <= 0m }:
            case EconomicExposure.Quanto { Multiplier: <= 0m }:
                issues.Add(new ContractValidationIssue("exposure.multiplier.nonPositive", "Exposure multiplier must be positive."));
                break;
        }

        if (contract.Exposure is EconomicExposure.Quanto { ConversionRate: <= 0m })
            issues.Add(new ContractValidationIssue("exposure.quanto.conversionRate.nonPositive", "Quanto conversion rate must be positive."));

        if (contract.Exposure is EconomicExposure.Formula formula && string.IsNullOrWhiteSpace(formula.Expression))
            issues.Add(new ContractValidationIssue("exposure.formula.empty", "Formula exposure requires an expression."));
    }

    private static void ValidateLifecycle(InstrumentContract contract, List<ContractValidationIssue> issues)
    {
        if (contract.Lifecycle is ContractLifecycle.EventSettled eventSettled &&
            string.IsNullOrWhiteSpace(eventSettled.EventKey))
            issues.Add(new ContractValidationIssue("lifecycle.event.key.empty", "Event-settled contracts require an event key."));

        if (contract.Lifecycle is ContractLifecycle.Perpetual &&
            contract.Financing is not FinancingTerms.PerpetualFunding)
            issues.Add(new ContractValidationIssue("lifecycle.perpetual.funding.missing", "Perpetual contracts require perpetual funding terms."));
    }

    private static void ValidatePayoff(InstrumentContract contract, List<ContractValidationIssue> issues)
    {
        switch (contract.Payoff)
        {
            case PayoffTerms.Option option:
                var terms = option.Terms;
                if (contract.Lifecycle is not ContractLifecycle.Expiring)
                    issues.Add(new ContractValidationIssue("payoff.option.lifecycle.invalid", "Option payoff requires expiring lifecycle."));
                if (contract.Lifecycle is ContractLifecycle.Expiring expiring && expiring.Expiry != terms.Expiration)
                    issues.Add(new ContractValidationIssue("payoff.option.expiration.lifecycleMismatch", "Option expiration must match contract lifecycle expiry."));
                if (!HasLeg(contract, terms.Underlying, LegRole.Underlying))
                    issues.Add(new ContractValidationIssue("payoff.option.underlying.missing", "Option payoff requires an underlying leg."));
                break;

            case PayoffTerms.Binary binary:
                if (contract.Settlement is not SettlementTerms.Binary)
                    issues.Add(new ContractValidationIssue("payoff.binary.settlement.invalid", "Binary payoff requires binary settlement terms."));
                if (string.IsNullOrWhiteSpace(binary.OutcomeKey))
                    issues.Add(new ContractValidationIssue("payoff.binary.outcome.empty", "Binary payoff requires an outcome key."));
                if (binary.Payout.Amount <= 0m)
                    issues.Add(new ContractValidationIssue("payoff.binary.payout.nonPositive", "Binary payout must be positive."));
                break;

            case PayoffTerms.Cfd cfd:
                if (contract.Settlement is not SettlementTerms.Cash)
                    issues.Add(new ContractValidationIssue("payoff.cfd.settlement.invalid", "CFD payoff requires cash settlement."));
                if (!HasLeg(contract, cfd.Underlying, LegRole.Reference))
                    issues.Add(new ContractValidationIssue("payoff.cfd.reference.missing", "CFD payoff requires a reference leg."));
                break;

            case PayoffTerms.Betting betting:
                if (contract.Lifecycle is not ContractLifecycle.EventSettled)
                    issues.Add(new ContractValidationIssue("payoff.betting.lifecycle.invalid", "Betting payoff requires event-settled lifecycle."));
                if (string.IsNullOrWhiteSpace(betting.MarketId) || string.IsNullOrWhiteSpace(betting.SelectionId))
                    issues.Add(new ContractValidationIssue("payoff.betting.ids.empty", "Betting payoff requires market and selection identifiers."));
                break;
        }
    }

    private static void ValidateDataSemantics(InstrumentContract contract, List<ContractValidationIssue> issues)
    {
        if (contract.Data is DataSemantics.Observable && contract.VenueRules.SupportsExecution)
            issues.Add(new ContractValidationIssue("data.observable.execution.enabled", "Observable contracts must not support execution."));

        if (!contract.VenueRules.SupportsMarketData)
            issues.Add(new ContractValidationIssue("venue.marketData.disabled", "Contracts must support market data."));
    }

    private static void ValidateLegs(InstrumentContract contract, List<ContractValidationIssue> issues)
    {
        foreach (var leg in contract.Legs)
        {
            if (leg.Ratio == 0m)
                issues.Add(new ContractValidationIssue("legs.ratio.zero", "Leg ratios must be non-zero."));
        }

        if (contract.Exposure is EconomicExposure.Formula &&
            contract.Payoff is PayoffTerms.LinearPayoff &&
            contract.Data is not DataSemantics.Observable and not DataSemantics.Custom &&
            contract.Legs.Count == 0)
            issues.Add(new ContractValidationIssue("legs.formula.empty", "Formula exposure requires component legs unless it is custom observable data."));

        if (contract.Package?.Kind == PackageKind.OptionSpread &&
            !contract.Legs.Any(leg => leg.Instrument.Asset.Class == AssetClass.Option))
            issues.Add(new ContractValidationIssue("package.optionLeg.missing", "Option packages must contain at least one option leg."));
    }

    private static bool HasLeg(InstrumentContract contract, Instrument instrument, LegRole role)
        => contract.Legs.Any(leg => leg.Instrument == instrument && leg.Role == role);
}

public static class Contracts
{
    public static InstrumentContract FromIdentity(Instrument instrument, Currency quoteCurrency) =>
        instrument.Asset.Class switch
        {
            AssetClass.Equity => Equity(instrument.Asset.Symbol, instrument.Venue, quoteCurrency),
            AssetClass.Index => Index(instrument.Asset.Symbol, instrument.Venue, quoteCurrency, tick: 0.01m),
            AssetClass.Observable => Observable(instrument.Asset.Symbol, instrument.Venue, quoteCurrency, ObservableKind.AlternativeData),
            _ => throw new InvalidOperationException(
                $"Instrument {instrument} cannot be converted to an InstrumentContract from identity alone. Use an explicit product recipe.")
        };

    public static InstrumentContract Equity(
        string symbol,
        Venue venue,
        Currency currency,
        decimal tick = 0.01m,
        decimal lot = 1m,
        string? isin = null) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Equity), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, lot, PricePrecision: 2, LotSize: lot),
            Exposure = new EconomicExposure.Spot(currency),
            Lifecycle = new ContractLifecycle.Cash(),
            Settlement = new SettlementTerms.Cash(currency, SettlementDelay.TPlus(1, venue.Name)),
            Margin = new MarginTerms.RegT(),
            Fees = FeeTerms.None,
            Financing = new FinancingTerms.Borrow($"{venue.Name}:{symbol}:borrow", DayCountBasis.Act360),
            Tags = OptionalTag("isin", isin)
        };

    public static InstrumentContract CurrencyPair(
        string symbol,
        Venue venue,
        Currency baseCurrency,
        Currency quoteCurrency,
        decimal pip,
        decimal lot) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Forex), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(pip, lot, LotSize: lot),
            Exposure = new EconomicExposure.Spot(quoteCurrency, baseCurrency),
            Lifecycle = new ContractLifecycle.Cash(),
            Settlement = new SettlementTerms.Cash(quoteCurrency, SettlementDelay.TPlus(2, venue.Name)),
            Margin = new MarginTerms.FixedFraction(0.02m, 0.01m),
            Fees = FeeTerms.None,
            Financing = new FinancingTerms.ForexRollover("default-fx-rollover", DayCountBasis.Act360)
        };

    public static InstrumentContract CryptoSpot(
        string symbol,
        Venue venue,
        Currency baseCurrency,
        Currency quoteCurrency,
        decimal tick,
        decimal lot) =>
        CurrencyPair(symbol, venue, baseCurrency, quoteCurrency, tick, lot) with
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Crypto), venue),
            Settlement = new SettlementTerms.Cash(quoteCurrency, SettlementDelay.Immediate("CRYPTO")),
            Margin = MarginTerms.Cash,
            Fees = new FeeTerms.MakerTaker(2m, 4m),
            Financing = FinancingTerms.None
        };

    public static InstrumentContract CommoditySpot(
        string symbol,
        Venue venue,
        Currency quoteCurrency,
        decimal tick,
        decimal lot) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Commodity), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, lot, LotSize: lot),
            Exposure = new EconomicExposure.Spot(quoteCurrency),
            Lifecycle = new ContractLifecycle.Cash(),
            Settlement = new SettlementTerms.Cash(quoteCurrency, SettlementDelay.TPlus(2, venue.Name)),
            Margin = MarginTerms.Cash,
            Fees = FeeTerms.None
        };

    public static InstrumentContract Future(
        string symbol,
        Venue venue,
        Instrument underlying,
        Currency quoteCurrency,
        decimal tick,
        decimal lot,
        decimal multiplier,
        Instant expiry) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Future), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, lot, LotSize: lot),
            Exposure = new EconomicExposure.Linear(quoteCurrency, multiplier),
            Lifecycle = new ContractLifecycle.Expiring(expiry, ExpiryAction.CashSettle),
            Settlement = new SettlementTerms.Cash(quoteCurrency, SettlementDelay.TPlus(1, venue.Name)),
            Margin = new MarginTerms.FixedFraction(0.10m, 0.05m),
            Fees = FeeTerms.None,
            Legs = [new InstrumentLeg(underlying, 1m, Side.Buy, LegRole.Underlying)]
        };

    public static InstrumentContract CryptoFuture(
        string symbol,
        Venue venue,
        Currency baseCurrency,
        Currency quoteCurrency,
        Currency settlementCurrency,
        decimal tick,
        decimal lot,
        decimal multiplier,
        Instant expiry,
        bool inverse) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Crypto), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, lot, LotSize: lot),
            Exposure = inverse
                ? new EconomicExposure.Inverse(baseCurrency, quoteCurrency, settlementCurrency, multiplier)
                : new EconomicExposure.Linear(quoteCurrency, multiplier, baseCurrency),
            Lifecycle = new ContractLifecycle.Expiring(expiry, ExpiryAction.CashSettle),
            Settlement = new SettlementTerms.Cash(settlementCurrency, SettlementDelay.Immediate("CRYPTO")),
            Margin = new MarginTerms.FixedFraction(0.05m, 0.025m),
            Fees = new FeeTerms.MakerTaker(2m, 4m)
        };

    public static InstrumentContract CryptoPerpetual(
        string symbol,
        Venue venue,
        Currency baseCurrency,
        Currency quoteCurrency,
        Currency settlementCurrency,
        decimal tick,
        decimal lot,
        decimal multiplier,
        bool inverse) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Crypto), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, lot, LotSize: lot),
            Exposure = inverse
                ? new EconomicExposure.Inverse(baseCurrency, quoteCurrency, settlementCurrency, multiplier)
                : new EconomicExposure.Linear(quoteCurrency, multiplier, baseCurrency),
            Lifecycle = new ContractLifecycle.Perpetual(new FundingSchedule(Duration.FromHours(8))),
            Settlement = new SettlementTerms.Cash(settlementCurrency, SettlementDelay.Immediate("CRYPTO")),
            Margin = new MarginTerms.FixedFraction(0.05m, 0.025m),
            Fees = new FeeTerms.MakerTaker(2m, 4m),
            Financing = new FinancingTerms.PerpetualFunding(new FundingSchedule(Duration.FromHours(8)), $"{venue.Name}:{symbol}:funding")
        };

    public static InstrumentContract Perpetual(
        string symbol,
        Venue venue,
        AssetClass assetClass,
        Currency baseCurrency,
        Currency quoteCurrency,
        Currency settlementCurrency,
        decimal tick,
        decimal lot,
        decimal multiplier,
        bool inverse,
        string fundingRateSource) =>
        CryptoPerpetual(symbol, venue, baseCurrency, quoteCurrency, settlementCurrency, tick, lot, multiplier, inverse) with
        {
            Instrument = new Instrument(new Asset(symbol, assetClass), venue),
            Financing = new FinancingTerms.PerpetualFunding(new FundingSchedule(Duration.FromHours(8)), fundingRateSource)
        };

    public static InstrumentContract OptionContract(
        string symbol,
        Venue venue,
        Instrument underlying,
        Currency quoteCurrency,
        decimal tick,
        decimal lot,
        decimal multiplier,
        Price strike,
        Instant expiry,
        OptionRight right,
        ExerciseStyle exercise,
        decimal? unitOfTrade = null,
        Instant? activation = null,
        OptionExpirationCycle expirationCycle = OptionExpirationCycle.Standard,
        OptionPremiumStyle premiumStyle = OptionPremiumStyle.Upfront,
        OptionExercisePolicy exercisePolicy = OptionExercisePolicy.Manual,
        OptionAssignmentPolicy assignmentPolicy = OptionAssignmentPolicy.VenueDefined,
        IReadOnlyList<Instant>? exerciseDates = null) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Option), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, lot, LotSize: lot),
            Exposure = new EconomicExposure.Linear(quoteCurrency, multiplier),
            Lifecycle = new ContractLifecycle.Expiring(expiry, ExpiryAction.Exercise),
            Settlement = new SettlementTerms.Cash(quoteCurrency, SettlementDelay.TPlus(1, venue.Name)),
            Margin = new MarginTerms.FixedFraction(1m, 1m),
            Fees = FeeTerms.None,
            Payoff = new PayoffTerms.Option(new OptionTerms(
                underlying,
                new OptionStrikeTerms(strike),
                right,
                exercise,
                OptionSettlementStyle.Cash,
                activation ?? Instant.MinValue,
                expiry,
                multiplier,
                unitOfTrade ?? multiplier,
                expirationCycle,
                premiumStyle,
                exercisePolicy,
                assignmentPolicy,
                exerciseDates ?? [])),
            Legs = [new InstrumentLeg(underlying, 1m, Side.Buy, LegRole.Underlying)]
        };

    public static InstrumentContract IndexOption(
        string symbol,
        Venue venue,
        Instrument index,
        Currency quoteCurrency,
        decimal tick,
        decimal lot,
        decimal multiplier,
        Price strike,
        Instant expiry,
        OptionRight right,
        ExerciseStyle exercise = ExerciseStyle.European,
        decimal? unitOfTrade = null,
        Instant? activation = null,
        OptionExpirationCycle expirationCycle = OptionExpirationCycle.Standard,
        OptionPremiumStyle premiumStyle = OptionPremiumStyle.Upfront,
        OptionExercisePolicy exercisePolicy = OptionExercisePolicy.CashSettledAtExpiry,
        OptionAssignmentPolicy assignmentPolicy = OptionAssignmentPolicy.None,
        PriceIncrementRule? priceIncrementRule = null,
        IReadOnlyList<Instant>? exerciseDates = null) =>
        OptionContract(symbol, venue, index, quoteCurrency, tick, lot, multiplier, strike, expiry, right, exercise, unitOfTrade, activation, expirationCycle, premiumStyle, exercisePolicy, assignmentPolicy, exerciseDates) with
        {
            Grid = new TradingGrid(tick, lot, LotSize: lot, PriceIncrementRule: priceIncrementRule)
        };

    public static InstrumentContract FutureOption(
        string symbol,
        Venue venue,
        Instrument future,
        Currency quoteCurrency,
        decimal tick,
        decimal lot,
        decimal multiplier,
        Price strike,
        Instant expiry,
        OptionRight right,
        ExerciseStyle exercise = ExerciseStyle.American,
        decimal? unitOfTrade = null,
        Instant? activation = null,
        OptionExpirationCycle expirationCycle = OptionExpirationCycle.Standard,
        OptionPremiumStyle premiumStyle = OptionPremiumStyle.FuturesStyle,
        OptionExercisePolicy exercisePolicy = OptionExercisePolicy.AutoExerciseInTheMoney,
        OptionAssignmentPolicy assignmentPolicy = OptionAssignmentPolicy.VenueDefined,
        IReadOnlyList<Instant>? exerciseDates = null) =>
        OptionContract(symbol, venue, future, quoteCurrency, tick, lot, multiplier, strike, expiry, right, exercise, unitOfTrade, activation, expirationCycle, premiumStyle, exercisePolicy, assignmentPolicy, exerciseDates) with
        {
            Margin = new MarginTerms.Portfolio("future-option-margin")
        };

    public static InstrumentContract LinearCryptoOption(
        string symbol,
        Venue venue,
        Instrument cryptoUnderlying,
        Currency quoteCurrency,
        Currency settlementCurrency,
        decimal tick,
        decimal lot,
        decimal multiplier,
        Price strike,
        Instant expiry,
        OptionRight right,
        ExerciseStyle exercise = ExerciseStyle.European,
        decimal? unitOfTrade = null,
        Instant? activation = null,
        OptionExpirationCycle expirationCycle = OptionExpirationCycle.Standard,
        OptionPremiumStyle premiumStyle = OptionPremiumStyle.Upfront,
        OptionExercisePolicy exercisePolicy = OptionExercisePolicy.AutoExerciseInTheMoney,
        OptionAssignmentPolicy assignmentPolicy = OptionAssignmentPolicy.VenueDefined,
        IReadOnlyList<Instant>? exerciseDates = null) =>
        OptionContract(symbol, venue, cryptoUnderlying, quoteCurrency, tick, lot, multiplier, strike, expiry, right, exercise, unitOfTrade, activation, expirationCycle, premiumStyle, exercisePolicy, assignmentPolicy, exerciseDates) with
        {
            Exposure = new EconomicExposure.Linear(quoteCurrency, multiplier),
            Settlement = new SettlementTerms.Cash(settlementCurrency, SettlementDelay.Immediate("CRYPTO")),
            Margin = new MarginTerms.Portfolio("linear-crypto-option-margin"),
            Fees = new FeeTerms.MakerTaker(2m, 4m)
        };

    public static InstrumentContract InverseCryptoOption(
        string symbol,
        Venue venue,
        Instrument cryptoUnderlying,
        Currency baseCurrency,
        Currency quoteCurrency,
        Currency settlementCurrency,
        decimal tick,
        decimal lot,
        decimal multiplier,
        Price strike,
        Instant expiry,
        OptionRight right,
        ExerciseStyle exercise = ExerciseStyle.European,
        decimal? unitOfTrade = null,
        Instant? activation = null,
        OptionExpirationCycle expirationCycle = OptionExpirationCycle.Standard,
        OptionPremiumStyle premiumStyle = OptionPremiumStyle.Upfront,
        OptionExercisePolicy exercisePolicy = OptionExercisePolicy.AutoExerciseInTheMoney,
        OptionAssignmentPolicy assignmentPolicy = OptionAssignmentPolicy.VenueDefined,
        IReadOnlyList<Instant>? exerciseDates = null) =>
        LinearCryptoOption(symbol, venue, cryptoUnderlying, quoteCurrency, settlementCurrency, tick, lot, multiplier, strike, expiry, right, exercise, unitOfTrade, activation, expirationCycle, premiumStyle, exercisePolicy, assignmentPolicy, exerciseDates) with
        {
            Exposure = new EconomicExposure.Inverse(baseCurrency, quoteCurrency, settlementCurrency, multiplier),
            Margin = new MarginTerms.Portfolio("inverse-crypto-option-margin")
        };

    public static InstrumentContract QuantoCryptoOption(
        string symbol,
        Venue venue,
        Instrument cryptoUnderlying,
        Currency underlyingCurrency,
        Currency quoteCurrency,
        Currency settlementCurrency,
        decimal conversionRate,
        decimal tick,
        decimal lot,
        decimal multiplier,
        Price strike,
        Instant expiry,
        OptionRight right,
        ExerciseStyle exercise = ExerciseStyle.European,
        decimal? unitOfTrade = null,
        Instant? activation = null,
        OptionExpirationCycle expirationCycle = OptionExpirationCycle.Standard,
        OptionPremiumStyle premiumStyle = OptionPremiumStyle.Upfront,
        OptionExercisePolicy exercisePolicy = OptionExercisePolicy.AutoExerciseInTheMoney,
        OptionAssignmentPolicy assignmentPolicy = OptionAssignmentPolicy.VenueDefined,
        IReadOnlyList<Instant>? exerciseDates = null) =>
        LinearCryptoOption(symbol, venue, cryptoUnderlying, quoteCurrency, settlementCurrency, tick, lot, multiplier, strike, expiry, right, exercise, unitOfTrade, activation, expirationCycle, premiumStyle, exercisePolicy, assignmentPolicy, exerciseDates) with
        {
            Exposure = new EconomicExposure.Quanto(underlyingCurrency, quoteCurrency, settlementCurrency, multiplier, conversionRate),
            Margin = new MarginTerms.Portfolio("quanto-crypto-option-margin")
        };

    public static InstrumentContract FuturesSpread(
        string symbol,
        Venue venue,
        Currency quoteCurrency,
        decimal tick,
        decimal lot,
        IReadOnlyList<InstrumentLeg> legs) =>
        Spread(symbol, venue, AssetClass.Future, quoteCurrency, tick, lot, legs) with
        {
            Margin = new MarginTerms.Portfolio("futures-spread-margin"),
            Package = new PackageTerms(PackageKind.FuturesSpread, IsRecognizedStrategy: true)
        };

    public static InstrumentContract OptionSpread(
        string symbol,
        Venue venue,
        Currency quoteCurrency,
        decimal tick,
        decimal lot,
        IReadOnlyList<InstrumentLeg> legs) =>
        Spread(symbol, venue, AssetClass.Option, quoteCurrency, tick, lot, legs) with
        {
            Margin = new MarginTerms.Portfolio("option-spread-margin"),
            Package = new PackageTerms(PackageKind.OptionSpread, IsRecognizedStrategy: true)
        };

    public static InstrumentContract Spread(
        string symbol,
        Venue venue,
        AssetClass assetClass,
        Currency quoteCurrency,
        decimal tick,
        decimal lot,
        IReadOnlyList<InstrumentLeg> legs) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, assetClass), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, lot, LotSize: lot),
            Exposure = new EconomicExposure.Formula(BuildSpreadFormula(legs), quoteCurrency),
            Lifecycle = DeriveLifecycleFromLegs(legs),
            Settlement = new SettlementTerms.Cash(quoteCurrency, SettlementDelay.TPlus(1, venue.Name)),
            Margin = new MarginTerms.Portfolio("spread-margin"),
            Fees = FeeTerms.None,
            Legs = legs
        };

    public static InstrumentContract Synthetic(
        string symbol,
        Venue venue,
        Currency quoteCurrency,
        decimal tick,
        decimal lot,
        string expression,
        IReadOnlyList<InstrumentLeg> legs) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Observable), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, lot, LotSize: lot),
            Exposure = new EconomicExposure.Formula(expression, quoteCurrency),
            Lifecycle = new ContractLifecycle.Cash(),
            Settlement = new SettlementTerms.Immediate(quoteCurrency),
            Margin = MarginTerms.Cash,
            Fees = FeeTerms.None,
            Data = new DataSemantics.Custom("synthetic", CanDriveMarks: true),
            VenueRules = VenueRules.Default with { IsTradable = false, SupportsExecution = false },
            Legs = legs
        };

    public static InstrumentContract BinaryOption(
        string symbol,
        Venue venue,
        string outcomeKey,
        Currency currency,
        Money payout,
        Instant? eventTime) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Option), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(0.01m, 1m, LotSize: 1m),
            Exposure = new EconomicExposure.Linear(currency),
            Lifecycle = new ContractLifecycle.EventSettled(eventTime, outcomeKey),
            Settlement = new SettlementTerms.Binary(currency, payout, SettlementDelay.Immediate()),
            Margin = MarginTerms.Cash,
            Fees = FeeTerms.None,
            Payoff = new PayoffTerms.Binary(outcomeKey, payout, BinaryOutcomeConvention.PaysOneOrZero)
        };

    public static InstrumentContract Cfd(
        string symbol,
        Venue venue,
        Instrument underlying,
        Currency quoteCurrency,
        decimal tick,
        decimal lot,
        decimal multiplier) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, underlying.Asset.Class), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, lot, LotSize: lot),
            Exposure = new EconomicExposure.Linear(quoteCurrency, multiplier),
            Lifecycle = new ContractLifecycle.Cash(),
            Settlement = new SettlementTerms.Cash(quoteCurrency, SettlementDelay.Immediate(venue.Name)),
            Margin = new MarginTerms.FixedFraction(0.05m, 0.025m),
            Fees = FeeTerms.None,
            Payoff = new PayoffTerms.Cfd(underlying),
            Legs = [new InstrumentLeg(underlying, 1m, Side.Buy, LegRole.Reference)]
        };

    public static InstrumentContract BettingInstrument(
        string symbol,
        Venue venue,
        string marketId,
        string selectionId,
        Currency currency,
        decimal tick,
        Instant? eventTime) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Option), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, 1m, LotSize: 1m),
            Exposure = new EconomicExposure.Linear(currency),
            Lifecycle = new ContractLifecycle.EventSettled(eventTime, marketId),
            Settlement = new SettlementTerms.Cash(currency, SettlementDelay.Immediate()),
            Margin = MarginTerms.Cash,
            Fees = FeeTerms.None,
            Payoff = new PayoffTerms.Betting(marketId, selectionId, OddsConvention.Decimal)
        };

    public static InstrumentContract TokenizedAsset(
        string symbol,
        Venue venue,
        AssetClass assetClass,
        Currency quoteCurrency,
        decimal tick,
        decimal lot,
        string chainId,
        string contractAddress) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, assetClass), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, lot, LotSize: lot),
            Exposure = new EconomicExposure.Spot(quoteCurrency),
            Lifecycle = new ContractLifecycle.Cash(),
            Settlement = new SettlementTerms.Physical(
                quoteCurrency,
                new Instrument(new Asset(contractAddress, AssetClass.Crypto), venue),
                SettlementDelay.Immediate()),
            Margin = MarginTerms.Cash,
            Fees = FeeTerms.None,
            Tags = new Dictionary<string, string>
            {
                ["chain"] = chainId,
                ["contract"] = contractAddress
            }
        };

    public static InstrumentContract Index(
        string symbol,
        Venue venue,
        Currency quoteCurrency,
        decimal tick) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Index), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(tick, 1m, LotSize: 1m),
            Exposure = new EconomicExposure.Reference(quoteCurrency),
            Lifecycle = new ContractLifecycle.Cash(),
            Settlement = new SettlementTerms.Immediate(quoteCurrency),
            Margin = MarginTerms.Cash,
            Fees = FeeTerms.None,
            Data = new DataSemantics.Observable(ObservableKind.IndexLevel, quoteCurrency),
            VenueRules = VenueRules.Default with { IsTradable = false, SupportsExecution = false, AllowShorting = false }
        };

    public static InstrumentContract Observable(
        string symbol,
        Venue venue,
        Currency? quoteCurrency,
        ObservableKind kind,
        string? schemaId = null) =>
        new()
        {
            Instrument = new Instrument(new Asset(symbol, AssetClass.Observable), venue),
            Identity = new ContractIdentity(symbol, venue),
            Grid = new TradingGrid(1m, 1m, LotSize: 1m),
            Exposure = quoteCurrency is null
                ? new EconomicExposure.Formula(symbol, Currency.None)
                : new EconomicExposure.Reference(quoteCurrency.Value),
            Lifecycle = new ContractLifecycle.Cash(),
            Settlement = quoteCurrency is null
                ? new SettlementTerms.Immediate(Currency.None)
                : new SettlementTerms.Immediate(quoteCurrency.Value),
            Margin = MarginTerms.Cash,
            Fees = FeeTerms.None,
            Data = new DataSemantics.Observable(kind, quoteCurrency),
            VenueRules = VenueRules.Default with { IsTradable = false, SupportsExecution = false, AllowShorting = false },
            Tags = OptionalTag("schema", schemaId)
        };

    private static IReadOnlyDictionary<string, string> OptionalTag(string key, string? value) =>
        value is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [key] = value };

    private static string BuildSpreadFormula(IReadOnlyList<InstrumentLeg> legs)
        => string.Join(" + ", legs.Select(static leg => $"{SignedRatio(leg)}*{leg.Instrument}"));

    private static string SignedRatio(InstrumentLeg leg)
    {
        var signed = leg.Side == Side.Sell ? -leg.Ratio : leg.Ratio;
        return signed.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ContractLifecycle DeriveLifecycleFromLegs(IReadOnlyList<InstrumentLeg> legs)
        => new ContractLifecycle.Cash();
}
