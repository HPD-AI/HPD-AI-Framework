using Rhodium.Connectivity;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests;

public class ProviderInstrumentNormalizerTests
{
    [Fact]
    public void BinanceSpot_NormalizesToCryptoSpotContract()
    {
        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = ExchangeId.Binance,
            ProviderKind = "SPOT",
            Kind = ProviderInstrumentKind.CryptoSpot,
            Symbol = "BTCUSDT",
            Venue = Venue.Binance,
            RawSymbol = "BTCUSDT",
            BaseCurrency = Currency.BTC,
            QuoteCurrency = Currency.USDT,
            TickSize = 0.01m,
            LotSize = 0.0001m,
            PricePrecision = 2,
            SizePrecision = 4,
            MinNotional = new Money(5m, Currency.USDT)
        });

        Assert.True(result.IsSuccess);
        var contract = AssertContract(result);
        Assert.Equal(AssetClass.Crypto, contract.Instrument.Asset.Class);
        Assert.IsType<EconomicExposure.Spot>(contract.Exposure);
        Assert.IsType<ContractLifecycle.Cash>(contract.Lifecycle);
        Assert.IsType<SettlementTerms.Cash>(contract.Settlement);
        Assert.IsType<FinancingTerms.NoFinancing>(contract.Financing);
        Assert.Equal(0.01m, contract.Grid.PriceIncrement);
        Assert.Equal(4, contract.Grid.SizePrecision);
        Assert.Equal(new Money(5m, Currency.USDT), contract.Constraints.MinNotional);
        Assert.Equal("BINANCE", contract.Tags["provider"]);
    }

    [Fact]
    public void DeribitInverseOption_NormalizesToCryptoOptionDimensions()
    {
        var underlying = new Instrument(new Asset("BTC-PERPETUAL", AssetClass.Crypto), "DERIBIT");
        var expiry = Instant.FromUnixSeconds(1_783_036_800);

        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = new ExchangeId("DERIBIT"),
            ProviderKind = "option",
            Kind = ProviderInstrumentKind.InverseCryptoOption,
            Symbol = "BTC-26JUN26-100000-C",
            Venue = "DERIBIT",
            RawSymbol = "BTC-26JUN26-100000-C",
            UnderlyingSymbol = underlying.Asset.Symbol,
            UnderlyingAssetClass = AssetClass.Crypto,
            UnderlyingVenue = underlying.Venue,
            BaseCurrency = Currency.BTC,
            QuoteCurrency = Currency.USD,
            SettlementCurrency = Currency.BTC,
            TickSize = 0.0005m,
            LotSize = 0.1m,
            Multiplier = 1m,
            ContractUnitOfTrade = 1m,
            Strike = new Price(100_000m, Currency.USD),
            Expiry = expiry,
            OptionRight = OptionRight.Call,
            ExerciseStyle = ExerciseStyle.European,
            ExercisePolicy = OptionExercisePolicy.AutoExerciseInTheMoney,
            AssignmentPolicy = OptionAssignmentPolicy.VenueDefined
        });

        Assert.True(result.IsSuccess);
        var contract = AssertContract(result);
        var exposure = Assert.IsType<EconomicExposure.Inverse>(contract.Exposure);
        Assert.Equal(Currency.BTC, exposure.BaseCurrency);
        Assert.Equal(Currency.USD, exposure.QuoteCurrency);
        Assert.Equal(Currency.BTC, exposure.SettlementCurrency);

        var payoff = Assert.IsType<PayoffTerms.Option>(contract.Payoff);
        Assert.Equal(underlying, payoff.Terms.Underlying);
        Assert.Equal(OptionRight.Call, payoff.Terms.Right);
        Assert.Equal(ExerciseStyle.European, payoff.Terms.ExerciseStyle);
        Assert.Equal(expiry, payoff.Terms.Expiration);
        Assert.Contains(contract.Legs, leg => leg.Role == LegRole.Underlying && leg.Instrument == underlying);
        Assert.IsType<MarginTerms.Portfolio>(contract.Margin);
    }

    [Fact]
    public void OsiListedOption_ParsesCoreContractTermsFromSymbol()
    {
        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = new ExchangeId("OPRA"),
            ProviderKind = "OSI",
            Kind = ProviderInstrumentKind.Option,
            Symbol = "AAPL  260117C00250000",
            Venue = "OPRA",
            RawSymbol = "AAPL  260117C00250000",
            UnderlyingVenue = Venue.NASDAQ,
            QuoteCurrency = Currency.USD,
            TickSize = 0.01m,
            LotSize = 1m,
            Multiplier = 100m,
            ContractUnitOfTrade = 100m,
            ExerciseStyle = ExerciseStyle.American
        });

        Assert.True(result.IsSuccess);
        var contract = AssertContract(result);
        Assert.Equal("AAPL260117C00250000", contract.Identity?.CanonicalSymbol);
        var payoff = Assert.IsType<PayoffTerms.Option>(contract.Payoff);
        Assert.Equal(new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ), payoff.Terms.Underlying);
        Assert.Equal(OptionRight.Call, payoff.Terms.Right);
        Assert.Equal(new Price(250m, Currency.USD), payoff.Terms.Strike.ScaledStrike);
        Assert.Equal(new DateTimeOffset(2026, 1, 17, 0, 0, 0, TimeSpan.Zero), payoff.Terms.Expiration.ToDateTimeOffset());
    }

    [Fact]
    public void DeribitOption_ParsesCoreContractTermsFromNativeSymbol()
    {
        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = new ExchangeId("DERIBIT"),
            ProviderKind = "option",
            Kind = ProviderInstrumentKind.InverseCryptoOption,
            Symbol = "BTC-26JUN26-100000-P",
            Venue = "DERIBIT",
            RawSymbol = "BTC-26JUN26-100000-P",
            UnderlyingAssetClass = AssetClass.Crypto,
            UnderlyingVenue = "DERIBIT",
            BaseCurrency = Currency.BTC,
            QuoteCurrency = Currency.USD,
            SettlementCurrency = Currency.BTC,
            TickSize = 0.0005m,
            LotSize = 0.1m,
            Multiplier = 1m,
            ContractUnitOfTrade = 1m,
            ExerciseStyle = ExerciseStyle.European
        });

        Assert.True(result.IsSuccess);
        var contract = AssertContract(result);
        Assert.Equal("BTC-26JUN26-100000-P", contract.Identity?.CanonicalSymbol);
        var payoff = Assert.IsType<PayoffTerms.Option>(contract.Payoff);
        Assert.Equal(new Instrument(new Asset("BTC-PERPETUAL", AssetClass.Crypto), "DERIBIT"), payoff.Terms.Underlying);
        Assert.Equal(OptionRight.Put, payoff.Terms.Right);
        Assert.Equal(new Price(100_000m, Currency.USD), payoff.Terms.Strike.ScaledStrike);
        Assert.Equal(new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero), payoff.Terms.Expiration.ToDateTimeOffset());
    }

    [Fact]
    public void IbkrEquity_NormalizesWithNativeIdentity()
    {
        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = ExchangeId.InteractiveBrokers,
            ProviderKind = "STK",
            Kind = ProviderInstrumentKind.Equity,
            Symbol = "AAPL",
            Venue = Venue.NASDAQ,
            RawSymbol = "AAPL",
            ExchangeMic = "XNAS",
            QuoteCurrency = Currency.USD,
            TickSize = 0.01m,
            LotSize = 1m,
            ExternalIds = new Dictionary<string, string> { ["isin"] = "US0378331005", ["conid"] = "265598" }
        });

        Assert.True(result.IsSuccess);
        var contract = AssertContract(result);
        Assert.Equal(AssetClass.Equity, contract.Instrument.Asset.Class);
        Assert.Equal("XNAS", contract.Identity?.ExchangeMic);
        Assert.Equal("US0378331005", contract.Tags["isin"]);
        Assert.Equal("265598", contract.Tags["conid"]);
        Assert.IsType<FinancingTerms.Borrow>(contract.Financing);
    }

    [Fact]
    public void DatabentoFuture_NormalizesToUnderlyingFutureContract()
    {
        var expiry = Instant.FromUnixSeconds(1_783_036_800);

        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = new ExchangeId("DATABENTO"),
            ProviderKind = "FUT",
            Kind = ProviderInstrumentKind.Future,
            Symbol = "ESM6",
            Venue = Venue.CME,
            ExchangeMic = "XCME",
            UnderlyingSymbol = "ES",
            UnderlyingAssetClass = AssetClass.Index,
            QuoteCurrency = Currency.USD,
            TickSize = 0.25m,
            LotSize = 1m,
            Multiplier = 50m,
            Expiry = expiry
        });

        Assert.True(result.IsSuccess);
        var contract = AssertContract(result);
        Assert.Equal(AssetClass.Future, contract.Instrument.Asset.Class);
        var exposure = Assert.IsType<EconomicExposure.Linear>(contract.Exposure);
        Assert.Equal(50m, exposure.Multiplier);
        Assert.IsType<ContractLifecycle.Expiring>(contract.Lifecycle);
        Assert.Contains(contract.Legs, leg => leg.Instrument.Asset.Symbol == "ES" && leg.Role == LegRole.Underlying);
    }

    [Fact]
    public void LeanBaseData_NormalizesToObservableContract()
    {
        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = new ExchangeId("LEAN"),
            ProviderKind = "Base",
            Kind = ProviderInstrumentKind.Observable,
            Symbol = "FRED.DFF",
            Venue = "LEAN",
            QuoteCurrency = Currency.USD,
            ObservableKind = ObservableKind.ReferenceRate,
            SchemaId = "fred-rate-v1"
        });

        Assert.True(result.IsSuccess);
        var contract = AssertContract(result);
        Assert.Equal(AssetClass.Observable, contract.Instrument.Asset.Class);
        Assert.IsType<DataSemantics.Observable>(contract.Data);
        Assert.False(contract.VenueRules.SupportsExecution);
        Assert.Equal("fred-rate-v1", contract.Tags["schema"]);
    }

    [Fact]
    public void PolymarketEvent_NormalizesToBettingContract()
    {
        var eventTime = Instant.FromUnixSeconds(1_783_036_800);

        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = new ExchangeId("POLYMARKET"),
            ProviderKind = "event-market",
            Kind = ProviderInstrumentKind.BettingInstrument,
            Symbol = "us-election-2028:yes",
            Venue = "POLYMARKET",
            QuoteCurrency = Currency.USD,
            SettlementCurrency = Currency.USD,
            MarketId = "us-election-2028",
            SelectionId = "yes",
            TickSize = 0.01m,
            EventTime = eventTime
        });

        Assert.True(result.IsSuccess);
        var contract = AssertContract(result);
        Assert.Equal(AssetClass.Option, contract.Instrument.Asset.Class);
        Assert.IsType<ContractLifecycle.EventSettled>(contract.Lifecycle);
        var payoff = Assert.IsType<PayoffTerms.Betting>(contract.Payoff);
        Assert.Equal("us-election-2028", payoff.MarketId);
        Assert.Equal("yes", payoff.SelectionId);
    }

    [Fact]
    public void UnsupportedProviderKind_ReturnsExplicitDiagnostic()
    {
        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = new ExchangeId("UNKNOWN"),
            ProviderKind = "weather-swaplet",
            Kind = ProviderInstrumentKind.Unsupported,
            Symbol = "WX-CHI",
            Venue = Venue.Unknown
        });

        Assert.Null(result.Contract);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "provider.kind.unsupported");
    }

    [Fact]
    public void MissingRequiredFields_ReturnsExplicitDiagnostics()
    {
        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = new ExchangeId("DERIBIT"),
            Kind = ProviderInstrumentKind.InverseCryptoOption,
            Symbol = "BTC-26JUN26-100000-C",
            Venue = "DERIBIT",
            QuoteCurrency = Currency.USD,
            SettlementCurrency = Currency.BTC,
            Strike = new Price(100_000m, Currency.USD),
            Expiry = Instant.FromUnixSeconds(1_783_036_800)
        });

        Assert.Null(result.Contract);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Message.Contains(nameof(ProviderInstrumentMetadata.UnderlyingSymbol), StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Message.Contains(nameof(ProviderInstrumentMetadata.OptionRight), StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains(nameof(ProviderInstrumentMetadata.BaseCurrency), StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedOsiOptionSymbol_ReturnsParseDiagnostic()
    {
        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = new ExchangeId("OPRA"),
            ProviderKind = "OSI",
            Kind = ProviderInstrumentKind.Option,
            Symbol = "AAPL  991399C00BAD000",
            Venue = "OPRA",
            RawSymbol = "AAPL  991399C00BAD000",
            UnderlyingVenue = Venue.NASDAQ,
            QuoteCurrency = Currency.USD
        });

        Assert.Null(result.Contract);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "provider.optionSymbol.parseFailed");
    }

    [Fact]
    public void MalformedDeribitOptionSymbol_ReturnsParseDiagnostic()
    {
        var result = ProviderInstrumentNormalizer.Normalize(new ProviderInstrumentMetadata
        {
            Provider = new ExchangeId("DERIBIT"),
            ProviderKind = "option",
            Kind = ProviderInstrumentKind.InverseCryptoOption,
            Symbol = "BTC-99FOO26-100000-C",
            Venue = "DERIBIT",
            RawSymbol = "BTC-99FOO26-100000-C",
            UnderlyingAssetClass = AssetClass.Crypto,
            UnderlyingVenue = "DERIBIT",
            BaseCurrency = Currency.BTC,
            QuoteCurrency = Currency.USD,
            SettlementCurrency = Currency.BTC
        });

        Assert.Null(result.Contract);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "provider.optionSymbol.parseFailed");
    }

    private static InstrumentContract AssertContract(ProviderInstrumentNormalizationResult result)
    {
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<InstrumentContract>(result.Contract);
    }
}
