using HPD.Events;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests;

public class FinancingChargeFeedTests
{
    [Fact]
    public void BundledDatasetIds_IncludesFinancingFeeds()
    {
        Assert.Contains("financing-crypto-funding", FinancingChargeFeed.BundledDatasetIds);
        Assert.Contains("financing-cash-borrow", FinancingChargeFeed.BundledDatasetIds);
        Assert.Contains("financing-rate-curves", FinancingChargeFeed.BundledDatasetIds);
    }

    [Fact]
    public void BundledFinancingFeedDataset_ReturnsEmbeddedCsv()
    {
        var feed = FinancingChargeFeed.BundledFinancingFeedDataset("financing-crypto-funding");

        Assert.Contains("PerpetualFunding", feed);
        Assert.Contains("BTCUSDT", feed);
    }

    [Fact]
    public void FromBundledFinancingFeed_LoadsSignedFundingCommands()
    {
        var commands = FinancingChargeFeed.FromBundledFinancingFeed("financing-crypto-funding");

        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Equal(FinancingChargeType.PerpetualFunding, command.ChargeType));
        Assert.Equal(new StrategyId(7), commands[0].StrategyId);
        Assert.Equal(Money.USD(-12.34m), commands[0].Amount);
        Assert.Equal(Money.USD(8.90m), commands[1].Amount);
        Assert.Equal(new Instrument(new Asset("BTCUSDT", AssetClass.Crypto), Venue.Binance), commands[0].Instrument);
        Assert.Equal("binance-funding-20260501-0800", commands[0].ExternalReference);
    }

    [Fact]
    public void FromBundledRateCurveFeed_ComputesCommandsFromBaseAmountAndRate()
    {
        var commands = FinancingChargeFeed.FromBundledRateCurveFeed("financing-rate-curves");

        Assert.Equal(3, commands.Count);

        Assert.Equal(FinancingChargeType.PerpetualFunding, commands[0].ChargeType);
        Assert.Equal(Money.USD(-12.3400m), commands[0].Amount);
        Assert.Equal(new Qty(1.5m), commands[0].Quantity);
        Assert.Equal(new Instrument(new Asset("BTCUSDT", AssetClass.Crypto), Venue.Binance), commands[0].Instrument);

        Assert.Equal(FinancingChargeType.BorrowFee, commands[1].ChargeType);
        Assert.Equal(Money.USD(-3.500m), commands[1].Amount);

        Assert.Equal(FinancingChargeType.CashInterestCredit, commands[2].ChargeType);
        Assert.Equal(Money.USD(1.25000m), commands[2].Amount);
    }

    [Fact]
    public void FromFinancingFeed_UsesDefaultStrategyAndVariantWhenCellsAreBlank()
    {
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,amount,currency,venue,symbol,asset_class,quantity,rate,external_reference
            ForexRollover,,,2026-05-01T21:00:00Z,-1.10,USD,NYSE,EURUSD,Forex,100000,0.001,rollover
            """;

        var command = Assert.Single(FinancingChargeFeed.FromFinancingFeed(
            feed,
            new StrategyId(42),
            defaultVariantId: 3));

        Assert.Equal(FinancingChargeType.ForexRollover, command.ChargeType);
        Assert.Equal(new StrategyId(42), command.StrategyId);
        Assert.Equal(3, command.VariantId);
        Assert.Equal(Money.USD(-1.10m), command.Amount);
        Assert.Equal(new Instrument(new Asset("EURUSD", AssetClass.Forex), Venue.NYSE), command.Instrument);
    }

    [Fact]
    public void FromFinancingFeed_CreatesDebitHelpersFromNegativeCashFlows()
    {
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,amount,currency,venue,symbol,asset_class,quantity,rate,external_reference
            CashInterestDebit,7,0,2026-05-01T21:00:00Z,-2.00,USD,,,,,0.05,debit
            BorrowFee,7,0,2026-05-01T21:00:00Z,-3.50,USD,NASDAQ,AAPL,Equity,10,0.02,borrow
            """;

        var commands = FinancingChargeFeed.FromFinancingFeed(feed);

        Assert.Equal(FinancingChargeType.CashInterestDebit, commands[0].ChargeType);
        Assert.Equal(Money.USD(-2.00m), commands[0].Amount);
        Assert.Equal(FinancingChargeType.BorrowFee, commands[1].ChargeType);
        Assert.Equal(Money.USD(-3.50m), commands[1].Amount);
    }

    [Fact]
    public void FromFinancingFeedFile_LoadsProviderCommands()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                path,
                "charge_type,strategy_id,variant_id,effective_at,amount,currency,venue,symbol,asset_class,quantity,rate,external_reference\nCashInterestCredit,9,2,2026-05-01T21:00:00Z,4.25,USD,,,,,0.04,cash");

            var command = Assert.Single(FinancingChargeFeed.FromFinancingFeedFile(path));

            Assert.Equal(FinancingChargeType.CashInterestCredit, command.ChargeType);
            Assert.Equal(new StrategyId(9), command.StrategyId);
            Assert.Equal(2, command.VariantId);
            Assert.Equal(Money.USD(4.25m), command.Amount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromRateCurveFeed_UsesDefaultStrategyAndVariant()
    {
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,quantity,base_amount,rate,external_reference
            ForexRollover,,,2026-05-01T21:00:00Z,USD,NYSE,EURUSD,Forex,100000,200000,-0.00001,rollover-rate
            """;

        var command = Assert.Single(FinancingChargeFeed.FromRateCurveFeed(
            feed,
            new StrategyId(42),
            defaultVariantId: 3));

        Assert.Equal(FinancingChargeType.ForexRollover, command.ChargeType);
        Assert.Equal(new StrategyId(42), command.StrategyId);
        Assert.Equal(3, command.VariantId);
        Assert.Equal(Money.USD(-2.00000m), command.Amount);
        Assert.Equal(0.00001m * -1m, command.Rate);
    }

    [Fact]
    public void FromRateCurveFeedFile_LoadsProviderRateCommands()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                path,
                "charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,quantity,base_amount,rate,external_reference\nCashInterestDebit,9,2,2026-05-01T21:00:00Z,USD,,,,,1000,0.001,debit-rate");

            var command = Assert.Single(FinancingChargeFeed.FromRateCurveFeedFile(path));

            Assert.Equal(FinancingChargeType.CashInterestDebit, command.ChargeType);
            Assert.Equal(new StrategyId(9), command.StrategyId);
            Assert.Equal(2, command.VariantId);
            Assert.Equal(Money.USD(-1.000m), command.Amount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromRateCurveFeed_SupportsAnnualizedDayCountRates()
    {
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,quantity,base_amount,rate,external_reference,accrual_days,day_count_basis
            CashInterestDebit,7,0,2026-05-01T21:00:00Z,USD,,,,,1000,0.10,debit-annualized,36,ACT/360
            """;

        var command = Assert.Single(FinancingChargeFeed.FromRateCurveFeed(feed));

        Assert.Equal(FinancingChargeType.CashInterestDebit, command.ChargeType);
        Assert.Equal(Currency.USD, command.Amount.Currency);
        Assert.Equal(-10m, command.Amount.Amount);
        Assert.Equal(0.01m, command.Rate);
    }

    [Fact]
    public void FromRateCurveFeed_CalculatesCalendarAccrualDaysFromDateWindow()
    {
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,quantity,base_amount,rate,external_reference,accrual_days,day_count_basis,accrual_start,accrual_end,accrual_day_mode
            CashInterestDebit,7,0,2026-05-04T21:00:00Z,USD,,,,,1000,0.10,debit-calendar,,ACT/360,2026-05-01,2026-05-04,Calendar
            """;

        var command = Assert.Single(FinancingChargeFeed.FromRateCurveFeed(feed));

        Assert.Equal(Currency.USD, command.Amount.Currency);
        Assert.Equal(Math.Round(-0.8333333333333333333333333330m, 24), Math.Round(command.Amount.Amount, 24));
        Assert.Equal(0.10m * 3m / 360m, command.Rate);
    }

    [Fact]
    public void FromPositionRateFeed_ComputesCommandsFromCustodyPositionNotional()
    {
        var instrument = new Instrument(new Asset("BTCUSDT", AssetClass.Crypto), Venue.Binance);
        var position = new CustodyPositionSnapshot(
            new StrategyId(7),
            VariantId: 0,
            instrument,
            Quantity: new Qty(1.5m),
            SettledQuantity: new Qty(1.5m),
            PendingDeliveryQuantity: Qty.Zero,
            RehypothecatableQuantity: Qty.Zero,
            AvgEntryPrice: new Price(80_000m, Currency.USD),
            MarkPrice: new Price(82_000m, Currency.USD),
            MarketValue: Money.USD(123_000m),
            UnrealizedPnL: Money.USD(3_000m),
            RealizedPnL: Money.Zero(Currency.USD),
            IsOpen: true);
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,rate,external_reference
            PerpetualFunding,7,0,2026-05-01T08:00:00Z,USD,Binance,BTCUSDT,Crypto,-0.0001,position-funding
            """;

        var command = Assert.Single(FinancingChargeFeed.FromPositionRateFeed(feed, [position]));

        Assert.Equal(FinancingChargeType.PerpetualFunding, command.ChargeType);
        Assert.Equal(Money.USD(-12.3000m), command.Amount);
        Assert.Equal(new Qty(1.5m), command.Quantity);
        Assert.Equal(instrument, command.Instrument);
    }

    [Fact]
    public void FromPositionRateFeed_ComputesBorrowFeeFromExplicitBasis()
    {
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var basis = new FinancingRateBasis(
            new StrategyId(7),
            VariantId: 0,
            Currency.USD,
            Money.USD(1_750m),
            instrument,
            new Qty(-10m));
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,rate,external_reference
            BorrowFee,7,0,2026-05-01T21:00:00Z,USD,NASDAQ,AAPL,Equity,0.002,borrow-position
            """;

        var command = Assert.Single(FinancingChargeFeed.FromPositionRateFeed(feed, [basis]));

        Assert.Equal(FinancingChargeType.BorrowFee, command.ChargeType);
        Assert.Equal(Money.USD(-3.500m), command.Amount);
        Assert.Equal(new Qty(-10m), command.Quantity);
    }

    [Fact]
    public void FromPositionRateFeed_SupportsAnnualizedDayCountRates()
    {
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var basis = new FinancingRateBasis(
            new StrategyId(7),
            VariantId: 0,
            Currency.USD,
            Money.USD(1_000m),
            instrument,
            new Qty(-100m));
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,rate,external_reference,accrual_days,day_count_basis
            BorrowFee,7,0,2026-05-01T21:00:00Z,USD,NASDAQ,AAPL,Equity,0.10,annualized-borrow,36,ACT/360
            """;

        var command = Assert.Single(FinancingChargeFeed.FromPositionRateFeed(feed, [basis]));

        Assert.Equal(FinancingChargeType.BorrowFee, command.ChargeType);
        Assert.Equal(Currency.USD, command.Amount.Currency);
        Assert.Equal(-10m, command.Amount.Amount);
        Assert.Equal(0.01m, command.Rate);
    }

    [Fact]
    public void FromPositionRateFeed_CalculatesVenueBusinessAccrualDaysFromDateWindow()
    {
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var basis = new FinancingRateBasis(
            new StrategyId(7),
            VariantId: 0,
            Currency.USD,
            Money.USD(1_000m),
            instrument,
            new Qty(-100m));
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,rate,external_reference,accrual_days,day_count_basis,accrual_start,accrual_end,accrual_day_mode
            BorrowFee,7,0,2026-05-04T21:00:00Z,USD,NASDAQ,AAPL,Equity,0.10,business-accrual,,ACT/360,2026-05-01,2026-05-04,Business
            """;

        var command = Assert.Single(FinancingChargeFeed.FromPositionRateFeed(feed, [basis]));

        Assert.Equal(Currency.USD, command.Amount.Currency);
        Assert.Equal(Math.Round(-0.277777777777777777777777778m, 24), Math.Round(command.Amount.Amount, 24));
        Assert.Equal(0.10m / 360m, command.Rate);
    }

    [Fact]
    public void FromPositionRateFeed_RejectsRowsWithoutMatchingPositionBasis()
    {
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,rate,external_reference
            PerpetualFunding,7,0,2026-05-01T08:00:00Z,USD,Binance,BTCUSDT,Crypto,-0.0001,position-funding
            """;

        Assert.Throws<FormatException>(() => FinancingChargeFeed.FromPositionRateFeed(feed, Array.Empty<FinancingRateBasis>()));
    }

    [Fact]
    public void FromPositionRateFeed_RejectsCashInterestRows()
    {
        var basis = new FinancingRateBasis(
            new StrategyId(7),
            VariantId: 0,
            Currency.USD,
            Money.USD(10_000m));
        var feed = """
            charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,rate,external_reference
            CashInterestCredit,7,0,2026-05-01T21:00:00Z,USD,,,,0.0001,cash
            """;

        Assert.Throws<FormatException>(() => FinancingChargeFeed.FromPositionRateFeed(feed, [basis]));
    }

    [Fact]
    public void FromFinancingFeed_RejectsMissingStrategyWithoutDefault()
    {
        var feed = "charge_type,strategy_id,variant_id,effective_at,amount,currency\nCashInterestCredit,,,2026-05-01T21:00:00Z,1.00,USD";

        Assert.Throws<FormatException>(() => FinancingChargeFeed.FromFinancingFeed(feed));
    }

    [Fact]
    public void FromFinancingFeed_RejectsPositiveBorrowFeeCashFlow()
    {
        var feed = "charge_type,strategy_id,variant_id,effective_at,amount,currency,venue,symbol,asset_class\nBorrowFee,7,0,2026-05-01T21:00:00Z,3.50,USD,NASDAQ,AAPL,Equity";

        Assert.Throws<FormatException>(() => FinancingChargeFeed.FromFinancingFeed(feed));
    }

    [Fact]
    public void FromFinancingFeed_RejectsInstrumentChargeWithoutInstrument()
    {
        var feed = "charge_type,strategy_id,variant_id,effective_at,amount,currency\nPerpetualFunding,7,0,2026-05-01T21:00:00Z,1.00,USD";

        Assert.Throws<FormatException>(() => FinancingChargeFeed.FromFinancingFeed(feed));
    }

    [Fact]
    public void FromRateCurveFeed_RejectsZeroComputedAmount()
    {
        var feed = "charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,quantity,base_amount,rate\nCashInterestCredit,7,0,2026-05-01T21:00:00Z,USD,,,,,1000,0";

        Assert.Throws<FormatException>(() => FinancingChargeFeed.FromRateCurveFeed(feed));
    }

    [Fact]
    public void FromRateCurveFeed_RejectsIncompleteAnnualizedRateConvention()
    {
        var feed = "charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,quantity,base_amount,rate,external_reference,accrual_days\nCashInterestDebit,7,0,2026-05-01T21:00:00Z,USD,,,,,1000,0.10,debit,1";

        Assert.Throws<FormatException>(() => FinancingChargeFeed.FromRateCurveFeed(feed));
    }

    [Fact]
    public void FromRateCurveFeed_RejectsIncompleteAccrualDateWindow()
    {
        var feed = "charge_type,strategy_id,variant_id,effective_at,currency,venue,symbol,asset_class,quantity,base_amount,rate,external_reference,accrual_days,day_count_basis,accrual_start\nCashInterestDebit,7,0,2026-05-04T21:00:00Z,USD,,,,,1000,0.10,debit,,ACT/360,2026-05-01";

        Assert.Throws<FormatException>(() => FinancingChargeFeed.FromRateCurveFeed(feed));
    }

    [Fact]
    public void FromBundledFinancingFeed_RejectsUnknownDataset()
    {
        Assert.Throws<ArgumentException>(() => FinancingChargeFeed.FromBundledFinancingFeed("missing"));
    }

    [Fact]
    public async Task FeedCommands_AreApplyableByReplayConnector()
    {
        var connector = new ReplayConnector(
            CreateHistoryWithOneQuote(),
            initialCash: Money.USD(100m));
        var events = new TestEventPublisher();
        var commands = FinancingChargeFeed.FromBundledFinancingFeed("financing-cash-borrow");
        var submitted = false;
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };
        events.OnEmit = evt =>
        {
            if (evt is QuoteReceived && !submitted)
            {
                submitted = true;
                foreach (var command in commands)
                    connector.ApplyFinancingChargeAsync(command, CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        await connector.StartAsync(subscriptions, events, CancellationToken.None);

        var applied = events.EmittedEvents.OfType<FinancingChargeApplied>().ToArray();
        Assert.Equal(2, applied.Length);
        Assert.Contains(applied, charge => charge.ChargeType == FinancingChargeType.CashInterestCredit);
        Assert.Contains(applied, charge => charge.ChargeType == FinancingChargeType.BorrowFee);

        var statement = events.EmittedEvents
            .OfType<AccountStatementSnapshot>()
            .Last(snapshot => snapshot.StrategyId == new StrategyId(7));
        Assert.Equal(Money.USD(97.75m), statement.Cash);
    }

    private static readonly Instrument TestInstrument = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithOneQuote()
    {
        yield return new QuoteReceived(
            TestInstrument,
            new Quote(
                new Price(100m, Currency.USD),
                new Price(101m, Currency.USD),
                new Qty(100m),
                new Qty(100m),
                DualTimestamp.Synchronized(Instant.FromUnixSeconds(1))));
        await Task.Yield();
    }

    private sealed class TestEventPublisher : IEventPublisher
    {
        public List<Event> EmittedEvents { get; } = [];
        public Action<Event>? OnEmit { get; set; }

        public void Emit(Event evt)
        {
            EmittedEvents.Add(evt);
            OnEmit?.Invoke(evt);
        }

        public ValueTask EmitAsync(Event evt, CancellationToken ct = default)
        {
            Emit(evt);
            return ValueTask.CompletedTask;
        }
    }
}
