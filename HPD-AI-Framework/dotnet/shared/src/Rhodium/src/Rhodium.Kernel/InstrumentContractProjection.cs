using Rhodium.Primitives;

namespace Rhodium.Kernel;

/// <summary>
/// Compact contract fields safe for strategy and matching hot paths.
/// </summary>
public readonly record struct InstrumentContractProjection(
    decimal PriceIncrement,
    decimal SizeIncrement,
    int PricePrecision,
    int SizePrecision,
    decimal LotSize,
    decimal Multiplier,
    decimal ContractUnitOfTrade,
    EconomicExposureKind ExposureKind,
    Currency QuoteCurrency,
    Currency SettlementCurrency,
    bool IsTradable,
    bool SupportsExecution,
    bool IsOption,
    bool IsPackage,
    OrderTypeMask AllowedOrderTypes,
    TimeInForceMask AllowedTimeInForce)
{
    public static InstrumentContractProjection From(InstrumentContract contract) =>
        new(
            contract.Grid.PriceIncrement,
            contract.Grid.SizeIncrement,
            contract.Grid.PricePrecision,
            contract.Grid.SizePrecision,
            contract.Grid.LotSize,
            contract.Exposure.Multiplier(),
            ResolveContractUnitOfTrade(contract),
            contract.Exposure.Kind(),
            contract.Exposure.QuoteCurrency(),
            contract.Exposure.SettlementCurrency(),
            contract.VenueRules.IsTradable,
            contract.VenueRules.SupportsExecution,
            contract.Payoff is PayoffTerms.Option,
            contract.Package is not null,
            contract.VenueRules.AllowedOrderTypes,
            contract.VenueRules.AllowedTimeInForce);

    private static decimal ResolveContractUnitOfTrade(InstrumentContract contract) =>
        contract.Payoff is PayoffTerms.Option option
            ? option.Terms.ContractUnitOfTrade
            : contract.Grid.LotSize;
}
