namespace Rhodium.Primitives;

/// <summary>
/// Compact position projection for contract-aware valuation.
/// </summary>
public readonly record struct PositionValuationInput(
    Instrument Instrument,
    Qty Quantity,
    Price AveragePrice,
    Money RealizedPnL);

/// <summary>
/// Result of valuing a position at a mark.
/// </summary>
public readonly record struct PositionValuation(
    Money Notional,
    Money MarketValue,
    Money UnrealizedPnL,
    Money RealizedPnL);

/// <summary>
/// Contract-aware valuation surface shared by accounting, risk, and simulation.
/// </summary>
public interface IInstrumentValuationModel
{
    Money Notional(InstrumentContract contract, Qty quantity, Price price);
    Money MarketValue(InstrumentContract contract, Qty quantity, Price mark);
    Money UnrealizedPnL(InstrumentContract contract, Qty quantity, Price averagePrice, Price mark);
    Money RealizedPnL(InstrumentContract contract, Qty closingQuantity, Price entry, Price exit);
    Money ExpiryPayoff(InstrumentContract contract, Qty quantity, Price referencePrice, bool outcome = false);
    PositionValuation ValuePosition(InstrumentContract contract, PositionValuationInput position, Price mark);
}

/// <summary>
/// Default financial semantics for Rhodium instrument contracts.
/// </summary>
public sealed class DefaultInstrumentValuationModel : IInstrumentValuationModel
{
    public static readonly DefaultInstrumentValuationModel Instance = new();

    private DefaultInstrumentValuationModel()
    {
    }

    public Money Notional(InstrumentContract contract, Qty quantity, Price price)
    {
        if (contract.Payoff is PayoffTerms.Betting)
            return new Money(quantity.Abs.Value, contract.Exposure.SettlementCurrency());

        var amount = contract.Exposure switch
        {
            EconomicExposure.Spot => quantity.Abs.Value * price.Value,
            EconomicExposure.Linear linear => quantity.Abs.Value * price.Value * linear.Multiplier,
            EconomicExposure.Inverse inverse => price.Value == 0m
                ? 0m
                : quantity.Abs.Value * inverse.Multiplier / price.Value,
            EconomicExposure.Quanto quanto => quantity.Abs.Value * price.Value * quanto.Multiplier * quanto.ConversionRate,
            EconomicExposure.Formula => quantity.Abs.Value * price.Value,
            EconomicExposure.Reference => 0m,
            _ => 0m
        };

        return new Money(amount, contract.Exposure.SettlementCurrency());
    }

    public Money MarketValue(InstrumentContract contract, Qty quantity, Price mark)
    {
        if (contract.Data is DataSemantics.Observable && !contract.VenueRules.SupportsExecution)
            return Money.Zero(contract.Exposure.SettlementCurrency());
        if (contract.Payoff is PayoffTerms.Betting)
            return Money.Zero(contract.Exposure.SettlementCurrency());

        var amount = contract.Exposure switch
        {
            EconomicExposure.Spot => quantity.Value * mark.Value,
            EconomicExposure.Linear linear => quantity.Value * mark.Value * linear.Multiplier,
            EconomicExposure.Inverse inverse => mark.Value == 0m
                ? 0m
                : quantity.Value * inverse.Multiplier / mark.Value,
            EconomicExposure.Quanto quanto => quantity.Value * mark.Value * quanto.Multiplier * quanto.ConversionRate,
            EconomicExposure.Formula => quantity.Value * mark.Value,
            EconomicExposure.Reference => 0m,
            _ => 0m
        };

        return new Money(amount, contract.Exposure.SettlementCurrency());
    }

    public Money UnrealizedPnL(InstrumentContract contract, Qty quantity, Price averagePrice, Price mark) =>
        RealizedPnL(contract, quantity, averagePrice, mark);

    public Money RealizedPnL(InstrumentContract contract, Qty closingQuantity, Price entry, Price exit)
    {
        if (contract.Data is DataSemantics.Observable && !contract.VenueRules.SupportsExecution)
            return Money.Zero(contract.Exposure.SettlementCurrency());
        if (contract.Payoff is PayoffTerms.Betting)
            return Money.Zero(contract.Exposure.SettlementCurrency());

        return CalculateExposurePnL(contract, entry, exit, closingQuantity);
    }

    public Money ExpiryPayoff(InstrumentContract contract, Qty quantity, Price referencePrice, bool outcome = false)
    {
        var amount = contract.Payoff switch
        {
            PayoffTerms.Option option => OptionExpiryPayoff(contract.Exposure, option.Terms, referencePrice, quantity),
            PayoffTerms.Binary binary => outcome ? binary.Payout.Amount * quantity.Value : 0m,
            PayoffTerms.Betting betting => BettingPayoff(betting, quantity, referencePrice, outcome),
            _ => MarketValue(contract, quantity, referencePrice).Amount
        };

        return new Money(amount, PayoffCurrency(contract));
    }

    public PositionValuation ValuePosition(InstrumentContract contract, PositionValuationInput position, Price mark)
    {
        var notional = Notional(contract, position.Quantity, mark);
        var marketValue = MarketValue(contract, position.Quantity, mark);
        var unrealized = UnrealizedPnL(contract, position.Quantity, position.AveragePrice, mark);
        var realized = position.RealizedPnL.Currency == default
            ? Money.Zero(contract.Exposure.SettlementCurrency())
            : position.RealizedPnL;

        return new PositionValuation(notional, marketValue, unrealized, realized);
    }

    private static decimal OptionIntrinsic(OptionTerms option, Price underlyingMark)
    {
        var scaledStrike = option.Strike.ScaledStrike.Value;
        var intrinsic = option.Right == OptionRight.Call
            ? underlyingMark.Value - scaledStrike
            : scaledStrike - underlyingMark.Value;

        return Math.Max(0m, intrinsic);
    }

    private static decimal OptionExpiryPayoff(
        EconomicExposure exposure,
        OptionTerms option,
        Price referencePrice,
        Qty quantity)
    {
        var scaledStrike = option.Strike.ScaledStrike.Value;
        return exposure switch
        {
            EconomicExposure.Inverse inverse => InverseOptionPayoff(option, referencePrice.Value, scaledStrike, quantity, inverse.Multiplier),
            EconomicExposure.Quanto quanto => OptionIntrinsic(option, referencePrice) * quantity.Value * quanto.Multiplier * quanto.ConversionRate,
            EconomicExposure.Linear linear => OptionIntrinsic(option, referencePrice) * quantity.Value * linear.Multiplier,
            _ => OptionIntrinsic(option, referencePrice) * quantity.Value * option.ContractMultiplier
        };
    }

    private static decimal InverseOptionPayoff(
        OptionTerms option,
        decimal referencePrice,
        decimal strike,
        Qty quantity,
        decimal multiplier)
    {
        if (referencePrice <= 0m || strike <= 0m)
            return 0m;

        var intrinsic = option.Right == OptionRight.Call
            ? (1m / strike) - (1m / referencePrice)
            : (1m / referencePrice) - (1m / strike);

        return Math.Max(0m, intrinsic) * quantity.Value * multiplier;
    }

    private static decimal BettingPayoff(
        PayoffTerms.Betting betting,
        Qty quantity,
        Price entryOdds,
        bool outcome) =>
        betting.OddsConvention switch
        {
            OddsConvention.Decimal => outcome ? quantity.Value * entryOdds.Value : 0m,
            _ => throw new InvalidOperationException($"Unsupported betting odds convention {betting.OddsConvention}.")
        };

    private static Currency PayoffCurrency(InstrumentContract contract) => contract.Settlement switch
    {
        SettlementTerms.Immediate immediate => immediate.Currency,
        SettlementTerms.Cash cash => cash.Currency,
        SettlementTerms.Physical physical => physical.CashCurrency,
        SettlementTerms.Binary binary => binary.Currency,
        _ => contract.Exposure.SettlementCurrency()
    };

    private static Money CalculateExposurePnL(
        InstrumentContract contract,
        Price entry,
        Price exit,
        Qty qty)
    {
        var pnl = contract.Exposure switch
        {
            EconomicExposure.Spot =>
                (exit.Value - entry.Value) * qty.Value,

            EconomicExposure.Linear linear =>
                (exit.Value - entry.Value) * qty.Value * linear.Multiplier,

            EconomicExposure.Inverse inverse =>
                entry.Value == 0 || exit.Value == 0
                    ? 0m
                    : ((1m / entry.Value) - (1m / exit.Value)) * qty.Value * inverse.Multiplier,

            EconomicExposure.Quanto quanto =>
                (exit.Value - entry.Value) * qty.Value * quanto.Multiplier * quanto.ConversionRate,

            EconomicExposure.Formula =>
                (exit.Value - entry.Value) * qty.Value,

            EconomicExposure.Reference =>
                0m,

            _ => 0m
        };

        return new Money(pnl, contract.Exposure.SettlementCurrency());
    }
}
