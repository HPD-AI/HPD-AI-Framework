using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Fee calculation logic for simulation.
/// </summary>
public static class FeeCalculator
{
    /// <summary>
    /// Calculate commission for a fill using an instrument contract for notional-sensitive fee models.
    /// </summary>
    public static Money Calculate(
        this FeeParams fees,
        InstrumentContract contract,
        Qty quantity,
        Price price,
        Side side,
        bool isMaker,
        Money thirtyDayVolume,
        IInstrumentValuationModel? valuation = null)
    {
        valuation ??= DefaultInstrumentValuationModel.Instance;
        return fees.Model switch
        {
            FeeModelType.PercentageOfValue => CalculatePercentage(fees, contract, quantity, price, isMaker, valuation),
            FeeModelType.PerQuantity => CalculatePerQuantity(fees, quantity, isMaker),
            FeeModelType.PerTrade => fees.FixedFee,
            FeeModelType.TieredByVolume => CalculateTiered(fees, contract, quantity, price, isMaker, thirtyDayVolume, valuation),
            FeeModelType.Directional => CalculateDirectional(fees, contract, quantity, price, side, valuation),
            FeeModelType.ContractTerms => CalculateContractTerms(contract, quantity, price, isMaker, valuation),
            _ => Money.Zero(price.Currency)
        };
    }

    private static Money CalculatePercentage(
        FeeParams fees,
        InstrumentContract contract,
        Qty quantity,
        Price price,
        bool isMaker,
        IInstrumentValuationModel valuation)
    {
        var bps = isMaker ? fees.MakerBps : fees.TakerBps;
        var notional = valuation.Notional(contract, quantity, price);
        return new Money(notional.Amount * bps / 10000m, notional.Currency);
    }

    private static Money CalculatePerQuantity(FeeParams fees, Qty quantity, bool isMaker)
    {
        var feePerLot = isMaker ? fees.MakerFeePerLot : fees.TakerFeePerLot;
        return new Money(quantity.Value * feePerLot.Amount, feePerLot.Currency);
    }

    private static Money CalculateTiered(
        FeeParams fees,
        InstrumentContract contract,
        Qty quantity,
        Price price,
        bool isMaker,
        Money thirtyDayVolume,
        IInstrumentValuationModel valuation)
    {
        var notional = valuation.Notional(contract, quantity, price);
        var (makerBps, takerBps) = fees.TieredSchedule?.GetFeeRate(thirtyDayVolume)
            ?? (fees.MakerBps, fees.TakerBps);
        var bps = isMaker ? makerBps : takerBps;
        return new Money(notional.Amount * bps / 10000m, notional.Currency);
    }

    private static Money CalculateDirectional(
        FeeParams fees,
        InstrumentContract contract,
        Qty quantity,
        Price price,
        Side side,
        IInstrumentValuationModel valuation)
    {
        var notional = valuation.Notional(contract, quantity, price);
        var bps = side switch
        {
            Side.Buy => fees.BuyFeeBps,
            Side.Sell => fees.SellFeeBps,
            _ => Math.Max(fees.BuyFeeBps, fees.SellFeeBps)
        };
        return new Money(notional.Amount * bps / 10000m, notional.Currency);
    }

    private static Money CalculateContractTerms(
        InstrumentContract contract,
        Qty quantity,
        Price price,
        bool isMaker,
        IInstrumentValuationModel valuation)
    {
        return contract.Fees switch
        {
            FeeTerms.NoFees => Money.Zero(contract.Exposure.SettlementCurrency()),
            FeeTerms.MakerTaker makerTaker => CalculateContractMakerTaker(makerTaker, contract, quantity, price, isMaker, valuation),
            FeeTerms.PerUnit perUnit => isMaker
                ? new Money(quantity.Abs.Value * perUnit.Maker.Amount, perUnit.Maker.Currency)
                : new Money(quantity.Abs.Value * perUnit.Taker.Amount, perUnit.Taker.Currency),
            FeeTerms.PerTrade perTrade => perTrade.Amount,
            _ => Money.Zero(contract.Exposure.SettlementCurrency())
        };
    }

    private static Money CalculateContractMakerTaker(
        FeeTerms.MakerTaker fees,
        InstrumentContract contract,
        Qty quantity,
        Price price,
        bool isMaker,
        IInstrumentValuationModel valuation)
    {
        var bps = isMaker ? fees.MakerBps : fees.TakerBps;
        var notional = valuation.Notional(contract, quantity, price);
        return new Money(notional.Amount * bps / 10000m, notional.Currency);
    }
}
