using Rhodium.Primitives;

namespace Rhodium.Options;

public sealed record OptionSeries(
    string SeriesId,
    Instrument Underlying,
    Venue Venue,
    Instant Expiration,
    ExerciseStyle ExerciseStyle,
    OptionSettlementStyle SettlementStyle,
    OptionExpirationCycle ExpirationCycle,
    Currency QuoteCurrency,
    decimal ContractMultiplier,
    decimal ContractUnitOfTrade);

public sealed record OptionChain(
    string ChainId,
    Instrument Underlying,
    Venue Venue,
    IReadOnlyList<OptionSeries> Series);

public interface IOptionChainProvider
{
    Task<OptionChain?> GetChainAsync(
        Instrument underlying,
        Venue venue,
        CancellationToken ct = default);

    IAsyncEnumerable<InstrumentContract> SearchContractsAsync(
        OptionChainQuery query,
        CancellationToken ct = default);
}

public sealed record OptionChainQuery
{
    public Instrument? Underlying { get; init; }
    public Venue? Venue { get; init; }
    public DateRange? Expiration { get; init; }
    public OptionRight? Right { get; init; }
    public Price? MinStrike { get; init; }
    public Price? MaxStrike { get; init; }
    public IReadOnlySet<OptionExpirationCycle>? ExpirationCycles { get; init; }
    public bool IncludeExpired { get; init; }
}

public sealed record OptionMarketState(
    Instrument Instrument,
    Instant Timestamp,
    Price? Bid = null,
    Price? Ask = null,
    Price? Last = null,
    Qty? BidSize = null,
    Qty? AskSize = null,
    Qty? Volume = null,
    decimal? OpenInterest = null,
    Price? UnderlyingMark = null,
    decimal? ObservedImpliedVolatility = null,
    OptionGreeks? ObservedGreeks = null,
    Money? TheoreticalValue = null);

public sealed record OptionChainSnapshot(
    string ChainId,
    Instant Timestamp,
    Price? UnderlyingMark,
    IReadOnlyList<OptionMarketState> Contracts);

public sealed record OptionChainWorkflowRequest(
    OptionChainQuery Query,
    Instant Timestamp,
    Price? UnderlyingMark,
    IReadOnlyDictionary<Instrument, OptionMarketState> MarketState,
    OptionPricingScenario PricingScenario,
    bool EnrichAnalytics = true);

public sealed record OptionChainWorkflowSnapshot(
    OptionChainSnapshot Chain,
    IReadOnlyDictionary<Instrument, InstrumentContract> Contracts,
    IReadOnlyDictionary<Instrument, LazyOptionAnalyticsResult> Analytics)
{
    public PortfolioGreeks AggregatePortfolioGreeks(
        IReadOnlyDictionary<Instrument, Qty> quantities,
        IPortfolioGreeksModel? model = null,
        OptionPortfolioScenario? scenario = null)
    {
        var positions = new List<OptionPositionRisk>();
        foreach (var (instrument, quantity) in quantities)
        {
            if (quantity.IsZero)
                continue;
            if (!Contracts.TryGetValue(instrument, out var contract))
                continue;

            var analytics = Analytics.TryGetValue(instrument, out var lazy)
                ? lazy
                : throw new InvalidOperationException($"No option analytics are available for held contract {instrument}.");
            var marketValue = analytics.Valuation.TheoreticalValue * quantity.Value;
            positions.Add(new OptionPositionRisk(contract, quantity, analytics.Greeks, marketValue));
        }

        return (model ?? DefaultPortfolioGreeksModel.Instance).Aggregate(
            positions,
            scenario ?? new OptionPortfolioScenario());
    }
}

public sealed class OptionChainWorkflow
{
    private readonly IOptionChainProvider _provider;
    private readonly IOptionAnalyticsModelSelector _selector;

    public OptionChainWorkflow(
        IOptionChainProvider provider,
        IOptionAnalyticsModelSelector? selector = null)
    {
        _provider = provider;
        _selector = selector ?? DefaultOptionAnalyticsModelSelector.Instance;
    }

    public async Task<OptionChainWorkflowSnapshot> BuildSnapshotAsync(
        OptionChainWorkflowRequest request,
        CancellationToken ct = default)
    {
        var contracts = new List<InstrumentContract>();
        await foreach (var contract in _provider.SearchContractsAsync(request.Query, ct).ConfigureAwait(false))
            contracts.Add(contract);

        var state = new List<OptionMarketState>(contracts.Count);
        var contractMap = new Dictionary<Instrument, InstrumentContract>();
        var analytics = new Dictionary<Instrument, LazyOptionAnalyticsResult>();
        foreach (var contract in contracts)
        {
            ct.ThrowIfCancellationRequested();
            contractMap[contract.Instrument] = contract;
            var market = MergeMarketState(contract, request);
            if (request.EnrichAnalytics)
            {
                var selection = _selector.Select(contract, request.PricingScenario);
                var result = LazyOptionAnalyticsResult.Create(
                    selection.Model,
                    contract,
                    market,
                    request.PricingScenario,
                    PriceToMoney(market.Last));
                market = market with
                {
                    ObservedImpliedVolatility = market.ObservedImpliedVolatility ?? result.Valuation.ImpliedVolatility,
                    ObservedGreeks = market.ObservedGreeks ?? result.Greeks,
                    TheoreticalValue = market.TheoreticalValue ?? result.Valuation.TheoreticalValue
                };
                analytics[contract.Instrument] = result;
            }

            state.Add(market);
        }

        var chainId = request.Query.Underlying is { } underlying && request.Query.Venue is { } venue
            ? $"{underlying.Asset.Symbol}@{venue.Name}:options"
            : "options";
        return new OptionChainWorkflowSnapshot(
            new OptionChainSnapshot(chainId, request.Timestamp, request.UnderlyingMark, state),
            contractMap,
            analytics);
    }

    private static OptionMarketState MergeMarketState(
        InstrumentContract contract,
        OptionChainWorkflowRequest request)
    {
        var market = request.MarketState.TryGetValue(contract.Instrument, out var existing)
            ? existing
            : new OptionMarketState(contract.Instrument, request.Timestamp);

        return market with
        {
            Timestamp = market.Timestamp == default ? request.Timestamp : market.Timestamp,
            UnderlyingMark = market.UnderlyingMark ?? request.UnderlyingMark
        };
    }

    private static Money? PriceToMoney(Price? price) =>
        price is { } value ? new Money(value.Value, value.Currency) : null;
}

public sealed class InMemoryOptionChainProvider : IOptionChainProvider
{
    private readonly Dictionary<Instrument, InstrumentContract> _contracts = [];

    public InMemoryOptionChainProvider(IEnumerable<InstrumentContract> contracts)
    {
        foreach (var contract in contracts)
            Upsert(contract);
    }

    public void Upsert(InstrumentContract contract)
    {
        if (contract.Payoff is not PayoffTerms.Option)
            throw new InvalidOperationException($"Contract {contract.Instrument} is not an option contract.");

        InstrumentContractValidator.Validate(contract).ThrowIfInvalid();
        _contracts[contract.Instrument] = contract;
    }

    public Task<OptionChain?> GetChainAsync(
        Instrument underlying,
        Venue venue,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var contracts = _contracts.Values
            .Where(contract => contract.Instrument.Venue == venue &&
                               contract.Payoff is PayoffTerms.Option option &&
                               option.Terms.Underlying == underlying)
            .ToArray();
        if (contracts.Length == 0)
            return Task.FromResult<OptionChain?>(null);

        var series = contracts
            .Select(static contract => ((PayoffTerms.Option)contract.Payoff).Terms)
            .GroupBy(terms => new
            {
                terms.Underlying,
                Venue = venue,
                terms.Expiration,
                terms.ExerciseStyle,
                terms.SettlementStyle,
                terms.ExpirationCycle,
                QuoteCurrency = terms.Strike.Strike.Currency,
                terms.ContractMultiplier,
                terms.ContractUnitOfTrade
            })
            .OrderBy(static group => group.Key.Expiration)
            .ThenBy(static group => group.Key.ExerciseStyle)
            .ThenBy(static group => group.Key.SettlementStyle)
            .Select(group => new OptionSeries(
                SeriesId(group.Key.Underlying, group.Key.Venue, group.Key.Expiration, group.Key.ExerciseStyle, group.Key.SettlementStyle, group.Key.ExpirationCycle),
                group.Key.Underlying,
                group.Key.Venue,
                group.Key.Expiration,
                group.Key.ExerciseStyle,
                group.Key.SettlementStyle,
                group.Key.ExpirationCycle,
                group.Key.QuoteCurrency,
                group.Key.ContractMultiplier,
                group.Key.ContractUnitOfTrade))
            .ToArray();

        return Task.FromResult<OptionChain?>(new OptionChain(
            ChainId(underlying, venue),
            underlying,
            venue,
            series));
    }

    public async IAsyncEnumerable<InstrumentContract> SearchContractsAsync(
        OptionChainQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var contract in _contracts.Values
            .Where(contract => Matches(contract, query))
            .OrderBy(static contract => ((PayoffTerms.Option)contract.Payoff).Terms.Expiration)
            .ThenBy(static contract => ((PayoffTerms.Option)contract.Payoff).Terms.Strike.ScaledStrike.Value)
            .ThenBy(static contract => ((PayoffTerms.Option)contract.Payoff).Terms.Right)
            .ThenBy(static contract => contract.Instrument.Asset.Symbol, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            yield return contract;
            await Task.Yield();
        }
    }

    private static bool Matches(InstrumentContract contract, OptionChainQuery query)
    {
        if (contract.Payoff is not PayoffTerms.Option option)
            return false;

        var terms = option.Terms;
        if (query.Underlying is { } underlying && terms.Underlying != underlying)
            return false;
        if (query.Venue is { } venue && contract.Instrument.Venue != venue)
            return false;
        if (query.Expiration is { } expiration && !expiration.Contains(terms.Expiration))
            return false;
        if (query.Right is { } right && terms.Right != right)
            return false;
        if (query.MinStrike is { } minStrike && terms.Strike.ScaledStrike.Value < minStrike.Value)
            return false;
        if (query.MaxStrike is { } maxStrike && terms.Strike.ScaledStrike.Value > maxStrike.Value)
            return false;
        if (query.ExpirationCycles is { Count: > 0 } cycles && !cycles.Contains(terms.ExpirationCycle))
            return false;
        if (!query.IncludeExpired && terms.Expiration < Instant.Now)
            return false;

        return true;
    }

    private static string ChainId(Instrument underlying, Venue venue) =>
        $"{underlying.Asset.Symbol}@{venue.Name}:options";

    private static string SeriesId(
        Instrument underlying,
        Venue venue,
        Instant expiration,
        ExerciseStyle exerciseStyle,
        OptionSettlementStyle settlementStyle,
        OptionExpirationCycle expirationCycle) =>
        $"{underlying.Asset.Symbol}@{venue.Name}:{expiration.Nanos}:{exerciseStyle}:{settlementStyle}:{expirationCycle}";
}

public interface IOptionAnalyticsModel
{
    OptionPricingModelCapabilities Capabilities { get; }

    bool Supports(InstrumentContract contract);

    OptionValuation Price(
        InstrumentContract contract,
        OptionMarketState market,
        OptionPricingScenario scenario);

    OptionGreeks Greeks(
        InstrumentContract contract,
        OptionMarketState market,
        OptionPricingScenario scenario);

    decimal ImpliedVolatility(
        InstrumentContract contract,
        OptionMarketState market,
        Money marketPremium,
        OptionPricingScenario scenario);
}

public interface IOptionAnalyticsModelSelector
{
    OptionAnalyticsModelSelection Select(
        InstrumentContract contract,
        OptionPricingScenario scenario);
}

public sealed record OptionAnalyticsModelSelection(
    IOptionAnalyticsModel Model,
    bool IsApproximation,
    string Reason);

public sealed class DefaultOptionAnalyticsModelSelector : IOptionAnalyticsModelSelector
{
    private readonly IReadOnlyList<IOptionAnalyticsModel> _models;

    public static readonly DefaultOptionAnalyticsModelSelector Instance = new(
        [BlackScholesOptionAnalyticsModel.Instance, BinomialOptionAnalyticsModel.Instance]);

    public DefaultOptionAnalyticsModelSelector(IReadOnlyList<IOptionAnalyticsModel> models)
    {
        if (models.Count == 0)
            throw new ArgumentException("At least one option analytics model is required.", nameof(models));

        _models = models;
    }

    public OptionAnalyticsModelSelection Select(
        InstrumentContract contract,
        OptionPricingScenario scenario)
    {
        foreach (var model in _models)
        {
            if (model.Supports(contract) && SupportsScenario(model, scenario))
                return new OptionAnalyticsModelSelection(model, IsApproximation: false, "Exact model capability match.");
        }

        if (scenario.AllowApproximation)
        {
            foreach (var model in _models)
            {
                if (CanApproximate(model, contract) && SupportsScenario(model, scenario))
                    return new OptionAnalyticsModelSelection(model, IsApproximation: true, "Selected by explicit approximation allowance.");
            }
        }

        var requested = Describe(contract);
        var available = string.Join(", ", _models.Select(static model => model.Capabilities.ModelId));
        throw new InvalidOperationException($"No option analytics model supports {requested}. Available models: {available}.");
    }

    private static bool CanApproximate(IOptionAnalyticsModel model, InstrumentContract contract)
    {
        if (contract.Payoff is not PayoffTerms.Option option)
            return false;

        return model.Capabilities.SupportedSettlementStyles.Contains(option.Terms.SettlementStyle);
    }

    private static bool SupportsScenario(IOptionAnalyticsModel model, OptionPricingScenario scenario) =>
        !scenario.HasDiscreteDividends || model.Capabilities.SupportsDiscreteDividends;

    private static string Describe(InstrumentContract contract)
    {
        if (contract.Payoff is PayoffTerms.Option option)
            return $"{option.Terms.ExerciseStyle}/{option.Terms.SettlementStyle} for {contract.Instrument}";

        return $"non-option contract {contract.Instrument}";
    }
}

public sealed class LazyOptionAnalyticsResult
{
    private readonly Lazy<OptionValuation> _valuation;
    private readonly Lazy<OptionGreeks> _greeks;
    private readonly Lazy<decimal> _impliedVolatility;

    private LazyOptionAnalyticsResult(
        IOptionAnalyticsModel model,
        InstrumentContract contract,
        OptionMarketState market,
        OptionPricingScenario scenario,
        Money? marketPremium)
    {
        ModelId = model.Capabilities.ModelId;
        Contract = contract;
        Market = market;
        Scenario = scenario;
        _valuation = new Lazy<OptionValuation>(() => model.Price(contract, market, scenario));
        _greeks = new Lazy<OptionGreeks>(() => model.Greeks(contract, market, scenario));
        _impliedVolatility = new Lazy<decimal>(() => model.ImpliedVolatility(
            contract,
            market,
            marketPremium ?? PriceToMoney(market.Last) ?? throw new InvalidOperationException("Implied volatility requires a market premium or last option price."),
            scenario));
    }

    public string ModelId { get; }
    public InstrumentContract Contract { get; }
    public OptionMarketState Market { get; }
    public OptionPricingScenario Scenario { get; }
    public bool HasValuation => _valuation.IsValueCreated;
    public bool HasGreeks => _greeks.IsValueCreated;
    public bool HasImpliedVolatility => _impliedVolatility.IsValueCreated;
    public OptionValuation Valuation => _valuation.Value;
    public OptionGreeks Greeks => _greeks.Value;
    public decimal ImpliedVolatility => _impliedVolatility.Value;

    public static LazyOptionAnalyticsResult Create(
        IOptionAnalyticsModel model,
        InstrumentContract contract,
        OptionMarketState market,
        OptionPricingScenario scenario,
        Money? marketPremium = null) =>
        new(model, contract, market, scenario, marketPremium);

    private static Money? PriceToMoney(Price? price) =>
        price is { } value ? new Money(value.Value, value.Currency) : null;
}

public sealed record OptionPricingModelCapabilities(
    string ModelId,
    IReadOnlySet<ExerciseStyle> SupportedExerciseStyles,
    IReadOnlySet<OptionSettlementStyle> SupportedSettlementStyles,
    bool SupportsGreeks,
    bool SupportsImpliedVolatility,
    bool SupportsDiscreteDividends,
    bool SupportsEarlyExercise);

public sealed record DiscreteDividend(
    Instant ExDate,
    Money Amount);

public sealed record OptionPricingScenario(
    decimal RiskFreeRate,
    decimal DividendYield = 0m,
    decimal? VolatilityOverride = null,
    decimal SpotShock = 0m,
    decimal VolShock = 0m,
    Duration? TimeToExpiryShock = null,
    GreeksConvention GreeksConvention = GreeksConvention.BlackScholes,
    bool AllowApproximation = false)
{
    public IReadOnlyList<DiscreteDividend> DiscreteDividends { get; init; } = [];

    public bool HasDiscreteDividends => DiscreteDividends.Count > 0;
}

public readonly record struct OptionValuation(
    Money TheoreticalValue,
    Money IntrinsicValue,
    Money TimeValue,
    decimal ImpliedVolatility);

public readonly record struct OptionGreeks(
    decimal Delta,
    decimal Gamma,
    decimal Theta,
    decimal Vega,
    decimal Rho,
    decimal? Vanna = null,
    decimal? Charm = null,
    decimal? Vomma = null);

public enum GreeksConvention : byte
{
    BlackScholes,
    PriceAdjusted,
    Percent,
    BetaWeighted,
    VolIndexWeighted
}

public interface IPortfolioGreeksModel
{
    PortfolioGreeks Aggregate(
        IReadOnlyList<OptionPositionRisk> positions,
        OptionPortfolioScenario scenario);
}

public readonly record struct OptionPositionRisk(
    InstrumentContract Contract,
    Qty Quantity,
    OptionGreeks Greeks,
    Money MarketValue);

public readonly record struct OptionPortfolioScenario(
    GreeksConvention GreeksConvention = GreeksConvention.BlackScholes);

public readonly record struct PortfolioGreeks(
    decimal Delta,
    decimal Gamma,
    decimal Theta,
    decimal Vega,
    decimal Rho,
    Money MarketValue,
    decimal? Vanna = null,
    decimal? Charm = null,
    decimal? Vomma = null);

public sealed class DefaultPortfolioGreeksModel : IPortfolioGreeksModel
{
    public static readonly DefaultPortfolioGreeksModel Instance = new();

    public PortfolioGreeks Aggregate(
        IReadOnlyList<OptionPositionRisk> positions,
        OptionPortfolioScenario scenario)
    {
        if (scenario.GreeksConvention is GreeksConvention.BetaWeighted or GreeksConvention.VolIndexWeighted)
            throw new InvalidOperationException(
                $"{scenario.GreeksConvention} portfolio Greeks require explicit reference weights and should not be inferred.");

        if (positions.Count == 0)
            return new PortfolioGreeks(0m, 0m, 0m, 0m, 0m, Money.Zero(Currency.None));

        var currency = positions[0].MarketValue.Currency;
        var delta = 0m;
        var gamma = 0m;
        var theta = 0m;
        var vega = 0m;
        var rho = 0m;
        decimal? vanna = 0m;
        decimal? charm = 0m;
        decimal? vomma = 0m;
        var marketValue = 0m;

        foreach (var position in positions)
        {
            if (position.MarketValue.Currency != currency)
                throw new InvalidOperationException("Portfolio Greeks aggregation requires a single market value currency.");

            var scale = Scale(position.Contract, position.Quantity);
            delta += position.Greeks.Delta * scale;
            gamma += position.Greeks.Gamma * scale;
            theta += position.Greeks.Theta * scale;
            vega += position.Greeks.Vega * scale;
            rho += position.Greeks.Rho * scale;
            vanna = SumOptional(vanna, position.Greeks.Vanna, scale);
            charm = SumOptional(charm, position.Greeks.Charm, scale);
            vomma = SumOptional(vomma, position.Greeks.Vomma, scale);
            marketValue += position.MarketValue.Amount;
        }

        return new PortfolioGreeks(
            delta,
            gamma,
            theta,
            vega,
            rho,
            new Money(marketValue, currency),
            vanna,
            charm,
            vomma);
    }

    private static decimal Scale(InstrumentContract contract, Qty quantity)
    {
        if (contract.Payoff is PayoffTerms.Option option)
            return quantity.Value * option.Terms.ContractMultiplier;

        return quantity.Value * contract.Exposure.Multiplier();
    }

    private static decimal? SumOptional(decimal? current, decimal? value, decimal scale)
    {
        if (current is null || value is null)
            return null;

        return current.Value + value.Value * scale;
    }
}

public interface IOptionAssignmentModel
{
    OptionAssignmentDecision GetAssignment(OptionAssignmentContext context);
}

public sealed record OptionAssignmentContext
{
    public OptionAssignmentContext(
        InstrumentContract Contract,
        Qty ShortQuantity,
        OptionMarketState Market,
        Instant Timestamp,
        OptionAssignmentRule? AssignmentRule = null,
        bool? IsSelectedForRandomAssignment = null,
        decimal? ProRataAssignmentRatio = null)
    {
        ArgumentNullException.ThrowIfNull(Contract);

        if (!ShortQuantity.IsPositive)
            throw new ArgumentException("Option assignment context requires positive short quantity.", nameof(ShortQuantity));

        if (ProRataAssignmentRatio is { } ratio && (ratio <= 0m || ratio > 1m))
            throw new ArgumentOutOfRangeException(nameof(ProRataAssignmentRatio), ProRataAssignmentRatio, "Pro-rata assignment ratio must be greater than zero and less than or equal to one.");

        this.Contract = Contract;
        this.ShortQuantity = ShortQuantity;
        this.Market = Market;
        this.Timestamp = Timestamp;
        this.AssignmentRule = AssignmentRule;
        this.IsSelectedForRandomAssignment = IsSelectedForRandomAssignment;
        this.ProRataAssignmentRatio = ProRataAssignmentRatio;
    }

    public InstrumentContract Contract { get; }
    public Qty ShortQuantity { get; }
    public OptionMarketState Market { get; }
    public Instant Timestamp { get; }
    public OptionAssignmentRule? AssignmentRule { get; }
    public bool? IsSelectedForRandomAssignment { get; }
    public decimal? ProRataAssignmentRatio { get; }
}

public sealed record OptionAssignmentDecision
{
    public OptionAssignmentDecision(
        bool isAssigned,
        Qty quantity,
        string? reason = null)
    {
        if (isAssigned && quantity.Value <= 0m)
            throw new ArgumentException("Assigned option decisions require a positive quantity.", nameof(quantity));

        if (!isAssigned && !quantity.IsZero)
            throw new ArgumentException("Unassigned option decisions must carry zero quantity.", nameof(quantity));

        IsAssigned = isAssigned;
        Quantity = quantity;
        Reason = reason;
    }

    public bool IsAssigned { get; }

    public Qty Quantity { get; }

    public string? Reason { get; }
}

public sealed record OptionAssignmentRule
{
    public OptionAssignmentRule(Money MinimumIntrinsicValue, bool AssignShortPositions = true)
    {
        if (MinimumIntrinsicValue.Amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(MinimumIntrinsicValue), MinimumIntrinsicValue, "Minimum intrinsic value must be nonnegative.");

        this.MinimumIntrinsicValue = MinimumIntrinsicValue;
        this.AssignShortPositions = AssignShortPositions;
    }

    public Money MinimumIntrinsicValue { get; }
    public bool AssignShortPositions { get; }
}

public sealed class DefaultOptionAssignmentModel : IOptionAssignmentModel
{
    public static readonly DefaultOptionAssignmentModel Instance = new();

    public OptionAssignmentDecision GetAssignment(OptionAssignmentContext context)
    {
        var terms = OptionLifecycleMath.GetTerms(context.Contract);
        if (terms.AssignmentPolicy == OptionAssignmentPolicy.None)
            return new OptionAssignmentDecision(false, Qty.Zero, "Contract is not assignable.");

        var currency = OptionLifecycleMath.SettlementCurrency(context.Contract);
        var rule = context.AssignmentRule ?? new OptionAssignmentRule(Money.Zero(currency));
        if (!rule.AssignShortPositions)
            return new OptionAssignmentDecision(false, Qty.Zero, "Assignment disabled by scenario rule.");

        var underlyingMark = OptionLifecycleMath.RequireUnderlyingMark(context.Market);
        var intrinsicPerUnit = OptionLifecycleMath.IntrinsicPerUnit(terms, underlyingMark);
        var intrinsicValue = OptionLifecycleMath.IntrinsicValue(terms, context.ShortQuantity, intrinsicPerUnit, currency);

        if (intrinsicPerUnit <= 0m || intrinsicValue.Amount < rule.MinimumIntrinsicValue.Amount)
            return new OptionAssignmentDecision(false, Qty.Zero, "Below assignment threshold.");

        return terms.AssignmentPolicy switch
        {
            OptionAssignmentPolicy.VenueDefined => new OptionAssignmentDecision(
                true,
                context.ShortQuantity,
                "Venue-defined assignment for in-the-money short option."),

            OptionAssignmentPolicy.NoArbitrageHeuristic => new OptionAssignmentDecision(
                true,
                context.ShortQuantity,
                "No-arbitrage heuristic assigned in-the-money short option."),

            OptionAssignmentPolicy.Random => RandomAssignment(context),
            OptionAssignmentPolicy.ProRata => ProRataAssignment(context),
            _ => throw new InvalidOperationException($"Unknown option assignment policy {terms.AssignmentPolicy}.")
        };
    }

    private static OptionAssignmentDecision RandomAssignment(OptionAssignmentContext context)
    {
        return context.IsSelectedForRandomAssignment switch
        {
            true => new OptionAssignmentDecision(true, context.ShortQuantity, "Random assignment selected this short position."),
            false => new OptionAssignmentDecision(false, Qty.Zero, "Random assignment did not select this short position."),
            null => new OptionAssignmentDecision(false, Qty.Zero, "Random assignment requires explicit selection input.")
        };
    }

    private static OptionAssignmentDecision ProRataAssignment(OptionAssignmentContext context)
    {
        if (context.ProRataAssignmentRatio is not { } ratio)
            return new OptionAssignmentDecision(false, Qty.Zero, "Pro-rata assignment requires an assignment ratio.");

        return new OptionAssignmentDecision(
            true,
            new Qty(context.ShortQuantity.Value * ratio),
            "Pro-rata assignment applied to short option.");
    }
}

internal static class OptionLifecycleMath
{
    public static OptionTerms GetTerms(InstrumentContract contract)
    {
        if (contract.Payoff is PayoffTerms.Option option)
            return option.Terms;

        throw new InvalidOperationException($"Contract {contract.Instrument} is not an option contract.");
    }

    public static decimal RequireUnderlyingMark(OptionMarketState market) =>
        market.UnderlyingMark?.Value
        ?? throw new InvalidOperationException("Option lifecycle models require an underlying mark.");

    public static decimal IntrinsicPerUnit(OptionTerms terms, decimal underlyingMark)
    {
        var strike = terms.Strike.ScaledStrike.Value;
        return Math.Max(
            0m,
            terms.Right == OptionRight.Call
                ? underlyingMark - strike
                : strike - underlyingMark);
    }

    public static Money IntrinsicValue(
        OptionTerms terms,
        Qty quantity,
        decimal intrinsicPerUnit,
        Currency currency) =>
        new(intrinsicPerUnit * quantity.Abs.Value * terms.ContractMultiplier, currency);

    public static Currency SettlementCurrency(InstrumentContract contract) => contract.Settlement switch
    {
        SettlementTerms.Immediate immediate => immediate.Currency,
        SettlementTerms.Cash cash => cash.Currency,
        SettlementTerms.Physical physical => physical.CashCurrency,
        SettlementTerms.Binary binary => binary.Currency,
        _ => contract.Exposure.SettlementCurrency()
    };
}

public interface IOptionMarginModel
{
    OptionMarginResult InitialMargin(OptionMarginRequest request);

    OptionMarginResult MaintenanceMargin(OptionMarginRequest request);
}

public interface IOptionStrategyMarginModel
{
    OptionMarginResult MarginForPackage(
        OptionStrategyPackage package,
        OptionMarginContext context);
}

public sealed record OptionMarginRequest(
    InstrumentContract Contract,
    Qty Quantity,
    OptionMarketState Market,
    OptionPricingScenario Scenario);

public sealed record OptionMarginContext(
    IReadOnlyDictionary<Instrument, InstrumentContract> Contracts,
    IReadOnlyDictionary<Instrument, OptionMarketState> MarketState,
    OptionPricingScenario Scenario);

public sealed record OptionMarginResult(
    Money Requirement,
    Money Premium,
    Money ShortOptionRisk,
    Money SpreadRisk,
    string ModelId,
    Money NetPremium = default);

public sealed record OptionStrategyPackage(
    OptionStrategyKind Kind,
    IReadOnlyList<InstrumentLeg> OptionLegs,
    IReadOnlyList<InstrumentLeg> UnderlyingLegs,
    bool NetPremium = true);

public enum OptionStrategyKind : byte
{
    VerticalCallSpread,
    VerticalPutSpread,
    Straddle
}

public static class OptionStrategies
{
    public static OptionStrategyPackage VerticalCallSpread(
        Instrument shortCall,
        Instrument longCall,
        decimal quantity,
        bool netPremium = true) =>
        Vertical(OptionStrategyKind.VerticalCallSpread, shortCall, longCall, quantity, netPremium);

    public static OptionStrategyPackage VerticalPutSpread(
        Instrument shortPut,
        Instrument longPut,
        decimal quantity,
        bool netPremium = true) =>
        Vertical(OptionStrategyKind.VerticalPutSpread, shortPut, longPut, quantity, netPremium);

    public static OptionStrategyPackage LongStraddle(
        Instrument call,
        Instrument put,
        decimal quantity,
        bool netPremium = true) =>
        Straddle(call, put, quantity, Side.Buy, netPremium);

    public static OptionStrategyPackage ShortStraddle(
        Instrument call,
        Instrument put,
        decimal quantity,
        bool netPremium = true) =>
        Straddle(call, put, quantity, Side.Sell, netPremium);

    private static OptionStrategyPackage Vertical(
        OptionStrategyKind kind,
        Instrument shortInstrument,
        Instrument longInstrument,
        decimal quantity,
        bool netPremium)
    {
        if (quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Strategy quantity must be positive.");

        return new OptionStrategyPackage(
            kind,
            [
                new InstrumentLeg(shortInstrument, quantity, Side.Sell),
                new InstrumentLeg(longInstrument, quantity, Side.Buy)
            ],
            [],
            netPremium);
    }

    private static OptionStrategyPackage Straddle(
        Instrument call,
        Instrument put,
        decimal quantity,
        Side side,
        bool netPremium)
    {
        if (quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Strategy quantity must be positive.");

        return new OptionStrategyPackage(
            OptionStrategyKind.Straddle,
            [
                new InstrumentLeg(call, quantity, side),
                new InstrumentLeg(put, quantity, side)
            ],
            [],
            netPremium);
    }
}

public interface IOptionStrategyRecognizer
{
    IReadOnlyList<OptionStrategyPackage> Recognize(
        IReadOnlyList<PositionValuationInput> positions,
        IReadOnlyDictionary<Instrument, InstrumentContract> contracts);
}

public sealed class DefaultOptionMarginModel : IOptionMarginModel
{
    public static readonly DefaultOptionMarginModel Instance = new();

    public OptionMarginResult InitialMargin(OptionMarginRequest request) =>
        Calculate(request, includeLongPremium: true);

    public OptionMarginResult MaintenanceMargin(OptionMarginRequest request) =>
        Calculate(request, includeLongPremium: false);

    private static OptionMarginResult Calculate(OptionMarginRequest request, bool includeLongPremium)
    {
        var terms = OptionLifecycleMath.GetTerms(request.Contract);
        var currency = OptionLifecycleMath.SettlementCurrency(request.Contract);
        if (request.Quantity.IsZero)
            return Zero(currency);

        var premium = Premium(request, terms, currency);
        if (request.Quantity.IsPositive)
        {
            var requirement = includeLongPremium ? premium : Money.Zero(currency);
            return new OptionMarginResult(
                requirement,
                premium,
                Money.Zero(currency),
                Money.Zero(currency),
                "default-option-margin",
                premium);
        }

        var valuation = DefaultInstrumentValuationModel.Instance;
        var underlying = request.Market.UnderlyingMark ?? terms.Strike.ScaledStrike;
        var quantity = request.Quantity.Abs.Value;
        var underlyingNotional = valuation.Notional(request.Contract, new Qty(quantity), underlying).Amount;
        var outOfTheMoney = OutOfTheMoneyAmount(request.Contract, terms, underlying, new Qty(quantity));
        var broadRisk = Math.Max(0m, underlyingNotional * 0.20m - outOfTheMoney);
        var minimumRisk = underlyingNotional * 0.10m;
        var shortRisk = new Money(Math.Max(broadRisk, minimumRisk), currency);

        return new OptionMarginResult(
            premium + shortRisk,
            premium,
            shortRisk,
            Money.Zero(currency),
            "default-option-margin",
            new Money(-premium.Amount, premium.Currency));
    }

    private static Money Premium(OptionMarginRequest request, OptionTerms terms, Currency currency)
    {
        if (terms.PremiumStyle is OptionPremiumStyle.FuturesStyle or OptionPremiumStyle.Deferred)
            return Money.Zero(currency);

        var mark = request.Market.Last
            ?? Mid(request.Market.Bid, request.Market.Ask)
            ?? Price.Zero;
        var premium = DefaultInstrumentValuationModel.Instance.MarketValue(
            request.Contract,
            request.Quantity.Abs,
            mark);
        return premium.Currency == currency
            ? premium
            : new Money(premium.Amount, currency);
    }

    private static decimal OutOfTheMoneyAmount(
        InstrumentContract contract,
        OptionTerms terms,
        Price underlying,
        Qty quantity)
    {
        if (terms.Right == OptionRight.Call && underlying.Value >= terms.Strike.ScaledStrike.Value)
            return 0m;
        if (terms.Right == OptionRight.Put && underlying.Value <= terms.Strike.ScaledStrike.Value)
            return 0m;

        var distance = DefaultInstrumentValuationModel.Instance.RealizedPnL(
            contract,
            quantity,
            underlying,
            terms.Strike.ScaledStrike);
        return Math.Abs(distance.Amount);
    }

    private static Price? Mid(Price? bid, Price? ask) =>
        bid is { } b && ask is { } a
            ? new Price((b.Value + a.Value) / 2m, b.Currency)
            : null;

    private static OptionMarginResult Zero(Currency currency) =>
        new(
            Money.Zero(currency),
            Money.Zero(currency),
            Money.Zero(currency),
            Money.Zero(currency),
            "default-option-margin");
}

public sealed class DefaultOptionStrategyMarginModel : IOptionStrategyMarginModel
{
    public static readonly DefaultOptionStrategyMarginModel Instance = new();

    public OptionMarginResult MarginForPackage(
        OptionStrategyPackage package,
        OptionMarginContext context)
    {
        return package.Kind switch
        {
            OptionStrategyKind.VerticalCallSpread or OptionStrategyKind.VerticalPutSpread =>
                MarginVerticalSpread(package, context),
            OptionStrategyKind.Straddle =>
                MarginStraddle(package, context),
            _ => throw new InvalidOperationException($"Unsupported option strategy package kind {package.Kind}.")
        };
    }

    private static OptionMarginResult MarginVerticalSpread(
        OptionStrategyPackage package,
        OptionMarginContext context)
    {
        if (!TryGetVerticalSpread(
                package,
                context,
                out var shortContract,
                out _,
                out var shortTerms,
                out var longTerms,
                out var quantity))
        {
            throw new InvalidOperationException($"Package {package.Kind} does not satisfy the vertical spread definition.");
        }

        var currencyForSpread = OptionLifecycleMath.SettlementCurrency(shortContract);
        var width = Math.Abs(shortTerms.Strike.ScaledStrike.Value - longTerms.Strike.ScaledStrike.Value);
        var unitOfTrade = Math.Max(shortTerms.ContractUnitOfTrade, longTerms.ContractUnitOfTrade);
        var spreadRisk = new Money(width * unitOfTrade * quantity, currencyForSpread);
        var netPremium = package.NetPremium
            ? NetOptionPackagePremium(package, context, currencyForSpread)
            : Money.Zero(currencyForSpread);
        var requirement = new Money(Math.Max(0m, spreadRisk.Amount + netPremium.Amount), currencyForSpread);

        return new OptionMarginResult(
            requirement,
            new Money(Math.Abs(netPremium.Amount), currencyForSpread),
            Money.Zero(currencyForSpread),
            spreadRisk,
            "default-option-strategy-margin",
            netPremium);
    }

    private static OptionMarginResult MarginStraddle(
        OptionStrategyPackage package,
        OptionMarginContext context)
    {
        if (!TryGetStraddle(package, context, out var call, out var put, out var quantity, out var side))
            throw new InvalidOperationException("Package Straddle does not satisfy the straddle definition.");

        var callMarket = GetMarketState(context, call.Contract.Instrument);
        var putMarket = GetMarketState(context, put.Contract.Instrument);
        var signedQuantity = side == Side.Buy ? quantity : -quantity;
        var callMargin = DefaultOptionMarginModel.Instance.InitialMargin(
            new OptionMarginRequest(call.Contract, new Qty(signedQuantity), callMarket, context.Scenario));
        var putMargin = DefaultOptionMarginModel.Instance.InitialMargin(
            new OptionMarginRequest(put.Contract, new Qty(signedQuantity), putMarket, context.Scenario));
        var currency = OptionLifecycleMath.SettlementCurrency(call.Contract);

        return new OptionMarginResult(
            new Money(callMargin.Requirement.Amount + putMargin.Requirement.Amount, currency),
            new Money(callMargin.Premium.Amount + putMargin.Premium.Amount, currency),
            new Money(callMargin.ShortOptionRisk.Amount + putMargin.ShortOptionRisk.Amount, currency),
            Money.Zero(currency),
            "default-option-strategy-margin",
            package.NetPremium
                ? NetOptionPackagePremium(package, context, currency)
                : Money.Zero(currency));
    }

    private static Money NetOptionPackagePremium(
        OptionStrategyPackage package,
        OptionMarginContext context,
        Currency currency)
    {
        var amount = 0m;
        foreach (var leg in package.OptionLegs)
        {
            if (!context.Contracts.TryGetValue(leg.Instrument, out var contract) ||
                contract.Payoff is not PayoffTerms.Option option)
            {
                throw new InvalidOperationException($"Option strategy package {package.Kind} contains a leg without an option contract.");
            }

            var market = GetMarketState(context, leg.Instrument);
            var mark = market.Last ?? Mid(market.Bid, market.Ask) ?? Price.Zero;
            var signedLegPremium = mark.Value * Math.Abs(leg.Ratio) * option.Terms.ContractMultiplier;
            amount += leg.Side == Side.Buy ? signedLegPremium : -signedLegPremium;
        }

        return new Money(amount, currency);
    }

    private static bool TryGetVerticalSpread(
        OptionStrategyPackage package,
        OptionMarginContext context,
        out InstrumentContract shortContract,
        out InstrumentContract longContract,
        out OptionTerms shortTerms,
        out OptionTerms longTerms,
        out decimal quantity)
    {
        shortContract = default!;
        longContract = default!;
        shortTerms = default!;
        longTerms = default!;
        quantity = 0m;

        var shortLeg = package.OptionLegs.FirstOrDefault(static leg => leg.Side == Side.Sell);
        var longLeg = package.OptionLegs.FirstOrDefault(static leg => leg.Side == Side.Buy);
        if (shortLeg == default || longLeg == default)
            return false;

        if (!context.Contracts.TryGetValue(shortLeg.Instrument, out shortContract) ||
            !context.Contracts.TryGetValue(longLeg.Instrument, out longContract) ||
            shortContract.Payoff is not PayoffTerms.Option shortOption ||
            longContract.Payoff is not PayoffTerms.Option longOption)
        {
            return false;
        }

        shortTerms = shortOption.Terms;
        longTerms = longOption.Terms;
        if (!OptionStrategyMath.CanFormVertical(shortContract, longContract, shortTerms, longTerms))
            return false;

        quantity = Math.Min(Math.Abs(shortLeg.Ratio), Math.Abs(longLeg.Ratio));
        return quantity > 0m;
    }

    private static bool TryGetStraddle(
        OptionStrategyPackage package,
        OptionMarginContext context,
        out StrategyOptionLeg call,
        out StrategyOptionLeg put,
        out decimal quantity,
        out Side side)
    {
        call = default!;
        put = default!;
        quantity = 0m;
        side = default;

        if (package.OptionLegs.Count != 2)
            return false;

        var firstSide = package.OptionLegs[0].Side;
        if (package.OptionLegs.Any(leg => leg.Side != firstSide))
            return false;

        var optionLegs = new List<StrategyOptionLeg>(2);
        foreach (var leg in package.OptionLegs)
        {
            if (!context.Contracts.TryGetValue(leg.Instrument, out var contract) ||
                contract.Payoff is not PayoffTerms.Option option)
            {
                return false;
            }

            optionLegs.Add(new StrategyOptionLeg(leg, contract, option.Terms));
        }

        call = optionLegs.FirstOrDefault(static leg => leg.Terms.Right == OptionRight.Call);
        put = optionLegs.FirstOrDefault(static leg => leg.Terms.Right == OptionRight.Put);
        if (call == default || put == default)
            return false;

        if (call.Terms.Underlying != put.Terms.Underlying ||
            call.Terms.Expiration != put.Terms.Expiration ||
            call.Terms.Strike.ScaledStrike != put.Terms.Strike.ScaledStrike ||
            OptionLifecycleMath.SettlementCurrency(call.Contract) != OptionLifecycleMath.SettlementCurrency(put.Contract))
        {
            return false;
        }

        quantity = Math.Min(Math.Abs(call.Leg.Ratio), Math.Abs(put.Leg.Ratio));
        side = firstSide;
        return quantity > 0m;
    }

    private static OptionMarketState GetMarketState(
        OptionMarginContext context,
        Instrument instrument) =>
        context.MarketState.TryGetValue(instrument, out var market)
            ? market
            : throw new InvalidOperationException($"Option strategy margin requires market state for {instrument}.");

    private static Price? Mid(Price? bid, Price? ask) =>
        bid is { } b && ask is { } a
            ? new Price((b.Value + a.Value) / 2m, b.Currency)
            : null;

    private sealed record StrategyOptionLeg(
        InstrumentLeg Leg,
        InstrumentContract Contract,
        OptionTerms Terms);
}

public sealed class DefaultOptionStrategyRecognizer : IOptionStrategyRecognizer
{
    public static readonly DefaultOptionStrategyRecognizer Instance = new();

    public IReadOnlyList<OptionStrategyPackage> Recognize(
        IReadOnlyList<PositionValuationInput> positions,
        IReadOnlyDictionary<Instrument, InstrumentContract> contracts)
    {
        var packages = new List<OptionStrategyPackage>();
        var remaining = positions.Select(static position => position.Quantity.Abs.Value).ToArray();

        for (var callIndex = 0; callIndex < positions.Count; callIndex++)
        {
            var callPosition = positions[callIndex];
            if (remaining[callIndex] <= 0m ||
                !TryGetOption(contracts, callPosition.Instrument, out var callContract, out var callTerms) ||
                callTerms.Right != OptionRight.Call)
            {
                continue;
            }

            for (var putIndex = 0; putIndex < positions.Count; putIndex++)
            {
                var putPosition = positions[putIndex];
                if (remaining[putIndex] <= 0m ||
                    Math.Sign(callPosition.Quantity.Value) != Math.Sign(putPosition.Quantity.Value) ||
                    !TryGetOption(contracts, putPosition.Instrument, out var putContract, out var putTerms) ||
                    putTerms.Right != OptionRight.Put ||
                    !OptionStrategyMath.CanFormStraddle(callContract, putContract, callTerms, putTerms))
                {
                    continue;
                }

                var quantity = Math.Min(remaining[callIndex], remaining[putIndex]);
                var side = callPosition.Quantity.Value > 0m ? Side.Buy : Side.Sell;
                packages.Add(new OptionStrategyPackage(
                    OptionStrategyKind.Straddle,
                    [
                        new InstrumentLeg(callPosition.Instrument, quantity, side),
                        new InstrumentLeg(putPosition.Instrument, quantity, side)
                    ],
                    []));

                remaining[callIndex] -= quantity;
                remaining[putIndex] -= quantity;
                break;
            }
        }

        for (var shortIndex = 0; shortIndex < positions.Count; shortIndex++)
        {
            var shortPosition = positions[shortIndex];
            if (remaining[shortIndex] <= 0m ||
                shortPosition.Quantity.Value >= 0m ||
                !TryGetOption(contracts, shortPosition.Instrument, out var shortContract, out var shortTerms))
            {
                continue;
            }

            var shortRemaining = remaining[shortIndex];
            for (var longIndex = 0; longIndex < positions.Count && shortRemaining > 0m; longIndex++)
            {
                var longPosition = positions[longIndex];
                if (remaining[longIndex] <= 0m ||
                    longPosition.Quantity.Value <= 0m ||
                    !TryGetOption(contracts, longPosition.Instrument, out var longContract, out var longTerms) ||
                    !OptionStrategyMath.CanFormVertical(shortContract, longContract, shortTerms, longTerms))
                {
                    continue;
                }

                var coveredQuantity = Math.Min(shortRemaining, remaining[longIndex]);
                packages.Add(new OptionStrategyPackage(
                    OptionStrategyMath.VerticalStrategyKind(shortTerms),
                    [
                        new InstrumentLeg(shortPosition.Instrument, coveredQuantity, Side.Sell),
                        new InstrumentLeg(longPosition.Instrument, coveredQuantity, Side.Buy)
                    ],
                    []));

                shortRemaining -= coveredQuantity;
                remaining[longIndex] -= coveredQuantity;
                remaining[shortIndex] -= coveredQuantity;
            }
        }

        return packages;
    }

    private static bool TryGetOption(
        IReadOnlyDictionary<Instrument, InstrumentContract> contracts,
        Instrument instrument,
        out InstrumentContract contract,
        out OptionTerms terms)
    {
        terms = default!;
        if (contracts.TryGetValue(instrument, out contract!) &&
            contract.Payoff is PayoffTerms.Option option)
        {
            terms = option.Terms;
            return true;
        }

        contract = default!;
        return false;
    }
}

internal static class OptionStrategyMath
{
    public static bool CanFormVertical(
        InstrumentContract shortContract,
        InstrumentContract longContract,
        OptionTerms shortTerms,
        OptionTerms longTerms)
    {
        if (shortTerms.Underlying != longTerms.Underlying ||
            shortTerms.Right != longTerms.Right ||
            shortTerms.Expiration != longTerms.Expiration ||
            OptionLifecycleMath.SettlementCurrency(shortContract) != OptionLifecycleMath.SettlementCurrency(longContract))
        {
            return false;
        }

        var shortStrike = shortTerms.Strike.ScaledStrike.Value;
        var longStrike = longTerms.Strike.ScaledStrike.Value;
        return shortTerms.Right == OptionRight.Call
            ? longStrike > shortStrike
            : longStrike < shortStrike;
    }

    public static bool CanFormStraddle(
        InstrumentContract callContract,
        InstrumentContract putContract,
        OptionTerms callTerms,
        OptionTerms putTerms)
    {
        if (callTerms.Right != OptionRight.Call ||
            putTerms.Right != OptionRight.Put ||
            callTerms.Underlying != putTerms.Underlying ||
            callTerms.Expiration != putTerms.Expiration ||
            callTerms.Strike.ScaledStrike != putTerms.Strike.ScaledStrike ||
            OptionLifecycleMath.SettlementCurrency(callContract) != OptionLifecycleMath.SettlementCurrency(putContract))
        {
            return false;
        }

        return true;
    }

    public static OptionStrategyKind VerticalStrategyKind(OptionTerms terms) =>
        terms.Right == OptionRight.Call ? OptionStrategyKind.VerticalCallSpread : OptionStrategyKind.VerticalPutSpread;
}

public sealed class BinomialOptionAnalyticsModel : IOptionAnalyticsModel
{
    private const int DefaultSteps = 200;

    public static readonly BinomialOptionAnalyticsModel Instance = new();

    public OptionPricingModelCapabilities Capabilities { get; } = new(
        "binomial-crr",
        new HashSet<ExerciseStyle> { ExerciseStyle.American, ExerciseStyle.European },
        new HashSet<OptionSettlementStyle> { OptionSettlementStyle.Cash, OptionSettlementStyle.Physical },
        SupportsGreeks: true,
        SupportsImpliedVolatility: true,
        SupportsDiscreteDividends: false,
        SupportsEarlyExercise: true);

    public bool Supports(InstrumentContract contract) =>
        contract.Payoff is PayoffTerms.Option option &&
        Capabilities.SupportedExerciseStyles.Contains(option.Terms.ExerciseStyle) &&
        Capabilities.SupportedSettlementStyles.Contains(option.Terms.SettlementStyle);

    public OptionValuation Price(
        InstrumentContract contract,
        OptionMarketState market,
        OptionPricingScenario scenario)
    {
        EnsureSupportedScenario(scenario);
        var terms = GetSupportedTerms(contract);
        var inputs = GetInputs(terms, market, scenario);
        var price = PricePerUnit(terms, inputs, DefaultSteps);
        var intrinsic = Intrinsic(terms, inputs.UnderlyingPrice);
        var currency = contract.Exposure.SettlementCurrency();

        return new OptionValuation(
            new Money(price * terms.ContractMultiplier, currency),
            new Money(intrinsic * terms.ContractMultiplier, currency),
            new Money(Math.Max(0m, price - intrinsic) * terms.ContractMultiplier, currency),
            inputs.Volatility);
    }

    public OptionGreeks Greeks(
        InstrumentContract contract,
        OptionMarketState market,
        OptionPricingScenario scenario)
    {
        EnsureSupportedScenario(scenario);
        var terms = GetSupportedTerms(contract);
        var inputs = GetInputs(terms, market, scenario);
        var spotBump = Math.Max(0.01m, inputs.UnderlyingPrice * 0.01m);
        var volBump = 0.01m;
        var rateBump = 0.0001m;
        var day = 1m / 365m;

        var basePrice = PricePerUnit(terms, inputs, DefaultSteps);
        var upSpot = PricePerUnit(terms, inputs with { UnderlyingPrice = inputs.UnderlyingPrice + spotBump }, DefaultSteps);
        var downSpot = PricePerUnit(terms, inputs with { UnderlyingPrice = Math.Max(0.0001m, inputs.UnderlyingPrice - spotBump) }, DefaultSteps);
        var upVol = PricePerUnit(terms, inputs with { Volatility = inputs.Volatility + volBump }, DefaultSteps);
        var downVol = PricePerUnit(terms, inputs with { Volatility = Math.Max(0.0001m, inputs.Volatility - volBump) }, DefaultSteps);
        var upRate = PricePerUnit(terms, inputs with { RiskFreeRate = inputs.RiskFreeRate + rateBump }, DefaultSteps);
        var downRate = PricePerUnit(terms, inputs with { RiskFreeRate = inputs.RiskFreeRate - rateBump }, DefaultSteps);
        var lessTime = PricePerUnit(terms, inputs with { TimeToExpiryYears = Math.Max(0m, inputs.TimeToExpiryYears - day) }, DefaultSteps);

        return new OptionGreeks(
            (upSpot - downSpot) / (2m * spotBump),
            (upSpot - (2m * basePrice) + downSpot) / (spotBump * spotBump),
            (lessTime - basePrice),
            (upVol - downVol) / (2m * volBump * 100m),
            (upRate - downRate) / (2m * rateBump * 100m));
    }

    public decimal ImpliedVolatility(
        InstrumentContract contract,
        OptionMarketState market,
        Money marketPremium,
        OptionPricingScenario scenario)
    {
        EnsureSupportedScenario(scenario);
        var terms = GetSupportedTerms(contract);
        var inputs = GetInputs(terms, market, scenario, requireVolatility: false);
        var target = marketPremium.Amount / terms.ContractMultiplier;
        var low = 0.0001m;
        var high = 5m;

        for (var i = 0; i < 100; i++)
        {
            var mid = (low + high) / 2m;
            var price = PricePerUnit(terms, inputs with { Volatility = mid }, DefaultSteps);
            if (Math.Abs(price - target) < 0.0001m)
                return mid;

            if (price > target)
                high = mid;
            else
                low = mid;
        }

        return (low + high) / 2m;
    }

    private void EnsureSupportedScenario(OptionPricingScenario scenario)
    {
        if (scenario.HasDiscreteDividends && !Capabilities.SupportsDiscreteDividends)
            throw new InvalidOperationException($"Model {Capabilities.ModelId} does not support discrete dividend scenarios.");
    }

    private OptionTerms GetSupportedTerms(InstrumentContract contract)
    {
        if (contract.Payoff is not PayoffTerms.Option option)
            throw new InvalidOperationException($"Contract {contract.Instrument} is not an option contract.");

        var terms = option.Terms;
        if (!Supports(contract))
            throw new InvalidOperationException(
                $"Model {Capabilities.ModelId} does not support {terms.ExerciseStyle}/{terms.SettlementStyle} for {contract.Instrument}.");

        return terms;
    }

    private static BinomialInputs GetInputs(
        OptionTerms terms,
        OptionMarketState market,
        OptionPricingScenario scenario,
        bool requireVolatility = true)
    {
        var underlyingMark = market.UnderlyingMark
            ?? throw new InvalidOperationException("Option market state requires an underlying mark for analytics.");
        var volatility = scenario.VolatilityOverride
            ?? market.ObservedImpliedVolatility
            ?? (requireVolatility
                ? throw new InvalidOperationException("Option analytics requires a scenario volatility override or observed implied volatility.")
                : 0.20m);
        volatility += scenario.VolShock;
        if (requireVolatility && volatility <= 0m)
            throw new InvalidOperationException("Option volatility must be positive.");

        var timeToExpiry = terms.Expiration - market.Timestamp;
        if (scenario.TimeToExpiryShock is { } shock)
            timeToExpiry += shock;
        var years = Math.Max(0m, (decimal)(timeToExpiry.TotalDays / 365.0));

        return new BinomialInputs(
            Math.Max(0m, underlyingMark.Value + scenario.SpotShock),
            years,
            Math.Max(0.0001m, volatility),
            scenario.RiskFreeRate,
            scenario.DividendYield);
    }

    private static decimal PricePerUnit(OptionTerms terms, BinomialInputs inputs, int steps)
    {
        if (inputs.TimeToExpiryYears <= 0m)
            return Intrinsic(terms, inputs.UnderlyingPrice);

        var n = Math.Max(1, steps);
        var s = (double)inputs.UnderlyingPrice;
        var k = (double)terms.Strike.ScaledStrike.Value;
        var t = (double)inputs.TimeToExpiryYears;
        var v = (double)inputs.Volatility;
        var r = (double)inputs.RiskFreeRate;
        var q = (double)inputs.DividendYield;
        var dt = t / n;
        var u = Math.Exp(v * Math.Sqrt(dt));
        var d = 1.0 / u;
        var disc = Math.Exp(-r * dt);
        var p = (Math.Exp((r - q) * dt) - d) / (u - d);
        if (p is < 0.0 or > 1.0 || double.IsNaN(p))
            throw new InvalidOperationException("Binomial option inputs produced invalid risk-neutral probability.");

        var values = new double[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var spot = s * Math.Pow(u, n - i) * Math.Pow(d, i);
            values[i] = Payoff(terms.Right, spot, k);
        }

        for (var step = n - 1; step >= 0; step--)
        {
            for (var i = 0; i <= step; i++)
            {
                var continuation = disc * (p * values[i] + (1.0 - p) * values[i + 1]);
                if (terms.ExerciseStyle == ExerciseStyle.American)
                {
                    var spot = s * Math.Pow(u, step - i) * Math.Pow(d, i);
                    values[i] = Math.Max(continuation, Payoff(terms.Right, spot, k));
                }
                else
                {
                    values[i] = continuation;
                }
            }
        }

        return (decimal)values[0];
    }

    private static double Payoff(OptionRight right, double spot, double strike) =>
        Math.Max(0.0, right == OptionRight.Call ? spot - strike : strike - spot);

    private static decimal Intrinsic(OptionTerms terms, decimal underlyingPrice)
    {
        var strike = terms.Strike.ScaledStrike.Value;
        return Math.Max(
            0m,
            terms.Right == OptionRight.Call
                ? underlyingPrice - strike
                : strike - underlyingPrice);
    }

    private readonly record struct BinomialInputs(
        decimal UnderlyingPrice,
        decimal TimeToExpiryYears,
        decimal Volatility,
        decimal RiskFreeRate,
        decimal DividendYield);
}

public sealed class BlackScholesOptionAnalyticsModel : IOptionAnalyticsModel
{
    public static readonly BlackScholesOptionAnalyticsModel Instance = new();

    public OptionPricingModelCapabilities Capabilities { get; } = new(
        "black-scholes",
        new HashSet<ExerciseStyle> { ExerciseStyle.European },
        new HashSet<OptionSettlementStyle> { OptionSettlementStyle.Cash, OptionSettlementStyle.Physical },
        SupportsGreeks: true,
        SupportsImpliedVolatility: true,
        SupportsDiscreteDividends: false,
        SupportsEarlyExercise: false);

    public bool Supports(InstrumentContract contract) =>
        contract.Payoff is PayoffTerms.Option option &&
        Capabilities.SupportedExerciseStyles.Contains(option.Terms.ExerciseStyle) &&
        Capabilities.SupportedSettlementStyles.Contains(option.Terms.SettlementStyle);

    public OptionValuation Price(
        InstrumentContract contract,
        OptionMarketState market,
        OptionPricingScenario scenario)
    {
        EnsureSupportedScenario(scenario);
        var terms = GetSupportedTerms(contract, scenario);
        var inputs = GetInputs(terms, market, scenario);
        var result = Rhodium.Options.Greeks.Calculate(
            terms.Right,
            inputs.UnderlyingPrice,
            terms.Strike.ScaledStrike.Value,
            inputs.TimeToExpiryYears,
            inputs.Volatility,
            scenario.RiskFreeRate,
            scenario.DividendYield);

        var intrinsic = Intrinsic(terms, inputs.UnderlyingPrice) * terms.ContractMultiplier;
        var theoretical = result.TheoreticalPrice * terms.ContractMultiplier;
        var currency = contract.Exposure.SettlementCurrency();

        return new OptionValuation(
            new Money(theoretical, currency),
            new Money(intrinsic, currency),
            new Money(Math.Max(0m, theoretical - intrinsic), currency),
            inputs.Volatility);
    }

    public OptionGreeks Greeks(
        InstrumentContract contract,
        OptionMarketState market,
        OptionPricingScenario scenario)
    {
        EnsureSupportedScenario(scenario);
        var terms = GetSupportedTerms(contract, scenario);
        var inputs = GetInputs(terms, market, scenario);
        var result = Rhodium.Options.Greeks.Calculate(
            terms.Right,
            inputs.UnderlyingPrice,
            terms.Strike.ScaledStrike.Value,
            inputs.TimeToExpiryYears,
            inputs.Volatility,
            scenario.RiskFreeRate,
            scenario.DividendYield);

        return new OptionGreeks(
            result.Delta,
            result.Gamma,
            result.Theta,
            result.Vega,
            result.Rho,
            Rhodium.Options.Greeks.Vanna(inputs.UnderlyingPrice, terms.Strike.ScaledStrike.Value, inputs.TimeToExpiryYears, inputs.Volatility, scenario.RiskFreeRate, scenario.DividendYield),
            Rhodium.Options.Greeks.Charm(terms.Right, inputs.UnderlyingPrice, terms.Strike.ScaledStrike.Value, inputs.TimeToExpiryYears, inputs.Volatility, scenario.RiskFreeRate, scenario.DividendYield),
            Rhodium.Options.Greeks.Vomma(inputs.UnderlyingPrice, terms.Strike.ScaledStrike.Value, inputs.TimeToExpiryYears, inputs.Volatility, scenario.RiskFreeRate, scenario.DividendYield));
    }

    public decimal ImpliedVolatility(
        InstrumentContract contract,
        OptionMarketState market,
        Money marketPremium,
        OptionPricingScenario scenario)
    {
        EnsureSupportedScenario(scenario);
        var terms = GetSupportedTerms(contract, scenario);
        var inputs = GetInputs(terms, market, scenario, useVolOverride: false);
        var premiumPerContract = marketPremium.Amount / terms.ContractMultiplier;
        return Rhodium.Options.Greeks.ImpliedVolatility(
            terms.Right,
            premiumPerContract,
            inputs.UnderlyingPrice,
            terms.Strike.ScaledStrike.Value,
            inputs.TimeToExpiryYears,
            scenario.RiskFreeRate,
            scenario.DividendYield);
    }

    private void EnsureSupportedScenario(OptionPricingScenario scenario)
    {
        if (scenario.HasDiscreteDividends && !Capabilities.SupportsDiscreteDividends)
            throw new InvalidOperationException($"Model {Capabilities.ModelId} does not support discrete dividend scenarios.");
    }

    private OptionTerms GetSupportedTerms(InstrumentContract contract, OptionPricingScenario scenario)
    {
        if (contract.Payoff is not PayoffTerms.Option option)
            throw new InvalidOperationException($"Contract {contract.Instrument} is not an option contract.");

        var terms = option.Terms;
        var supported = Capabilities.SupportedExerciseStyles.Contains(terms.ExerciseStyle)
            && Capabilities.SupportedSettlementStyles.Contains(terms.SettlementStyle);
        if (!supported && !scenario.AllowApproximation)
            throw new InvalidOperationException(
                $"Model {Capabilities.ModelId} does not support {terms.ExerciseStyle}/{terms.SettlementStyle} for {contract.Instrument}.");

        return terms;
    }

    private static OptionPricingInputs GetInputs(
        OptionTerms terms,
        OptionMarketState market,
        OptionPricingScenario scenario,
        bool useVolOverride = true)
    {
        var underlyingMark = market.UnderlyingMark
            ?? throw new InvalidOperationException("Option market state requires an underlying mark for analytics.");
        var volatility = useVolOverride && scenario.VolatilityOverride.HasValue
            ? scenario.VolatilityOverride.Value
            : market.ObservedImpliedVolatility
                ?? throw new InvalidOperationException("Option analytics requires a scenario volatility override or observed implied volatility.");
        volatility += scenario.VolShock;
        if (volatility <= 0m)
            throw new InvalidOperationException("Option volatility must be positive.");

        var timeToExpiry = terms.Expiration - market.Timestamp;
        if (scenario.TimeToExpiryShock is { } shock)
            timeToExpiry += shock;
        var years = Math.Max(0m, (decimal)(timeToExpiry.TotalDays / 365.0));

        return new OptionPricingInputs(
            Math.Max(0m, underlyingMark.Value + scenario.SpotShock),
            years,
            volatility);
    }

    private static decimal Intrinsic(OptionTerms terms, decimal underlyingPrice)
    {
        var strike = terms.Strike.ScaledStrike.Value;
        return Math.Max(
            0m,
            terms.Right == OptionRight.Call
                ? underlyingPrice - strike
                : strike - underlyingPrice);
    }

    private readonly record struct OptionPricingInputs(
        decimal UnderlyingPrice,
        decimal TimeToExpiryYears,
        decimal Volatility);
}
