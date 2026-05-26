using Rhodium.Simulation;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Tests.Models;

/// <summary>
/// Tests for SimulationConfig and all presets.
/// </summary>
public class SimulationConfigTests
{
    [Fact]
    public void CryptoFuturesRealistic_HasCorrectSettings()
    {
        var config = SimulationConfig.CryptoFuturesRealistic();

        Assert.Equal(Duration.FromMicros(500), config.Latency.EntryMean);
        Assert.Equal(Duration.FromMicros(500), config.Latency.ResponseMean);
        Assert.Equal(0.2, config.Latency.StdDevFraction);

        Assert.Equal(QueueModelType.PowerProbabilistic, config.QueueModel.Model);
        Assert.True(config.QueueModel.ProbabilisticEntry);

        Assert.Equal(2m, config.Fees.MakerBps);
        Assert.Equal(4m, config.Fees.TakerBps);

        Assert.Equal(SlippageModelType.None, config.Slippage.Model);
        Assert.Equal(FillBehavior.PartialFillOnTrade, config.FillBehavior);
    }

    [Fact]
    public void Conservative_HasHigherFeesAndLatency()
    {
        var config = SimulationConfig.Conservative();

        Assert.Equal(Duration.FromMillis(10), config.Latency.EntryMean);
        Assert.Equal(Duration.FromMillis(10), config.Latency.ResponseMean);

        Assert.Equal(QueueModelType.RiskAverse, config.QueueModel.Model);

        Assert.Equal(5m, config.Fees.MakerBps);
        Assert.Equal(15m, config.Fees.TakerBps);

        Assert.Equal(SlippageModelType.VolumeProportional, config.Slippage.Model);
        Assert.Equal(1m, config.Slippage.BpsPerLotSize);
        Assert.Equal(FillBehavior.NoPartialFill, config.FillBehavior);
    }

    [Fact]
    public void IlliquidMarket_UsesCubicQueueProfile()
    {
        var config = SimulationConfig.IlliquidMarket();

        Assert.Equal(QueueModelType.PowerProbabilistic, config.QueueModel.Model);
        Assert.Equal(3.0, config.QueueModel.Alpha);
        Assert.False(config.QueueModel.ProbabilisticEntry);

        Assert.Equal(10m, config.Fees.MakerBps);
        Assert.Equal(20m, config.Fees.TakerBps);

        Assert.Equal(2m, config.Slippage.BpsPerLotSize);
        Assert.Equal(FillBehavior.PartialFillOnTrade, config.FillBehavior);
    }

    [Fact]
    public void USEquities_UsesFixedFees()
    {
        var config = SimulationConfig.USEquities();

        Assert.Equal(Duration.FromMicros(100), config.Latency.EntryMean);

        Assert.Equal(QueueModelType.PowerProbabilistic, config.QueueModel.Model);
        Assert.Equal(2.0, config.QueueModel.Alpha);

        Assert.Equal(FeeModelType.PerTrade, config.Fees.Model);
        Assert.Equal(0.50m, config.Fees.FixedFee.Amount);

        Assert.Equal(SlippageModelType.None, config.Slippage.Model);
        Assert.Equal(Duration.FromDays(1), config.Settlement.CashProceedsDelay);
    }

    [Fact]
    public void Instant_HasZeroLatencyAndFees()
    {
        var config = SimulationConfig.Instant();

        Assert.Equal(Duration.Zero, config.Latency.EntryMean);
        Assert.Equal(Duration.Zero, config.Latency.ResponseMean);

        Assert.Equal(QueueModelType.AlwaysFront, config.QueueModel.Model);

        Assert.Equal(0m, config.Fees.MakerBps);
        Assert.Equal(0m, config.Fees.TakerBps);

        Assert.Equal(SlippageModelType.None, config.Slippage.Model);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new SimulationConfig
        {
            Latency = new(Duration.FromMillis(1), Duration.FromMillis(1)),
            QueueModel = QueueParams.PowerQuadratic(),
            Fees = FeeParams.Zero
        };

        Assert.Equal(FillBehavior.NoPartialFill, config.FillBehavior);
        Assert.Equal(DepthLevel.L2_MarketByPrice, config.RequiredDepth);
        Assert.Equal(AccountType.Cash, config.AccountType);
        Assert.Equal(0.50m, config.Margin.InitialMarginFraction);
        Assert.Equal(0.25m, config.Margin.MaintenanceMarginFraction);
        Assert.Equal(Duration.Zero, config.Settlement.CashProceedsDelay);
        Assert.Equal(UnsettledSalePolicy.Reject, config.Settlement.UnsettledSalePolicy);
        Assert.Equal(MarketStatus.Open, config.InitialMarketStatus);
        Assert.Equal(BarOrderingMode.Fixed, config.BarOrdering);
        Assert.IsType<DefaultFillModel>(config.FillModel);
        Assert.Equal(0, config.Seed);
    }

    [Fact]
    public void CustomConfig_AllowsOverrides()
    {
        var config = new SimulationConfig
        {
            Latency = new(Duration.FromMicros(200), Duration.FromMicros(300), 0.15),
            QueueModel = QueueParams.Logarithmic(scale: 20.0),
            Fees = FeeParams.Directional(buyBps: 5m, sellBps: 7m),
            Slippage = new(SlippageModelType.VolumeProportional, BpsPerLotSize: 0.5m),
            FillBehavior = FillBehavior.PartialFillOnTrade,
            RequiredDepth = DepthLevel.L1_TopOfBook,
            AccountType = AccountType.Margin,
            Margin = MarginParams.Leverage(4m),
            Settlement = SettlementParams.TPlus(2),
            InitialMarketStatus = MarketStatus.PreOpen,
            BarOrdering = BarOrderingMode.Adaptive,
            FillModel = new SizeAwareFillModel(baseQty: 20m, slippageBpsPerLot: 1m),
            Seed = 12345
        };

        Assert.Equal(0.15, config.Latency.StdDevFraction);
        Assert.Equal(20.0, config.QueueModel.Scale);
        Assert.Equal(5m, config.Fees.BuyFeeBps);
        Assert.Equal(0.5m, config.Slippage.BpsPerLotSize);
        Assert.Equal(FillBehavior.PartialFillOnTrade, config.FillBehavior);
        Assert.Equal(DepthLevel.L1_TopOfBook, config.RequiredDepth);
        Assert.Equal(AccountType.Margin, config.AccountType);
        Assert.Equal(0.25m, config.Margin.InitialMarginFraction);
        Assert.Equal(0.125m, config.Margin.MaintenanceMarginFraction);
        Assert.Equal(Duration.Zero, config.Margin.MarginCallGracePeriod);
        Assert.Equal(Duration.FromDays(2), config.Settlement.CashProceedsDelay);
        Assert.Equal(UnsettledSalePolicy.Reject, config.Settlement.UnsettledSalePolicy);
        Assert.Equal(MarketStatus.PreOpen, config.InitialMarketStatus);
        Assert.Equal(BarOrderingMode.Adaptive, config.BarOrdering);
        Assert.IsType<SizeAwareFillModel>(config.FillModel);
        Assert.Equal(12345, config.Seed);
    }

    [Fact]
    public void AllDepthLevels_AreDefined()
    {
        var levels = Enum.GetValues<DepthLevel>();

        Assert.Equal(2, levels.Length);
        Assert.Contains(DepthLevel.L1_TopOfBook, levels);
        Assert.Contains(DepthLevel.L2_MarketByPrice, levels);
    }

    [Fact]
    public void AllAccountTypes_AreDefined()
    {
        var types = Enum.GetValues<AccountType>();

        Assert.Equal(2, types.Length);
        Assert.Contains(AccountType.Cash, types);
        Assert.Contains(AccountType.Margin, types);
    }

    [Fact]
    public void MarginParams_LeverageConvertsToInitialAndMaintenanceFractions()
    {
        var margin = MarginParams.Leverage(5m);

        Assert.Equal(0.20m, margin.InitialMarginFraction);
        Assert.Equal(0.10m, margin.MaintenanceMarginFraction);
        Assert.Equal(Duration.Zero, margin.MarginCallGracePeriod);
        Assert.Equal(LiquidationPolicy.CancelOpenOrdersAndFlatten, margin.LiquidationPolicy);
        Assert.Equal(ShortSalePolicy.AllowNakedShort, margin.ShortSalePolicy);
        Assert.Equal(RehypothecationPolicy.Allowed, margin.RehypothecationPolicy);
        Assert.Empty(margin.BorrowAvailability);
        Assert.Empty(margin.RehypothecationAvailability);
        Assert.Equal(ShortSalePolicy.RequireBorrow, MarginParams.RegT().ShortSalePolicy);
        Assert.Equal(RehypothecationPolicy.Prohibited, MarginParams.RegT().RehypothecationPolicy);
    }

    [Fact]
    public void MarginParams_WithMarginCallGracePeriodConfiguresGracePeriod()
    {
        var margin = MarginParams.Leverage(5m).WithMarginCallGracePeriod(Duration.FromMinutes(30));

        Assert.Equal(Duration.FromMinutes(30), margin.MarginCallGracePeriod);
    }

    [Fact]
    public void MarginParams_WithMarginCallGracePeriodRejectsNegativeValues()
    {
        var margin = MarginParams.Leverage(5m);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            margin.WithMarginCallGracePeriod(Duration.FromNanos(-1)));
    }

    [Fact]
    public void MarginParams_LeverageRejectsNonPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MarginParams.Leverage(0m));
    }

    [Fact]
    public void MarginParams_WithLiquidationPolicyConfiguresPolicy()
    {
        var margin = MarginParams.Leverage(5m)
            .WithLiquidationPolicy(LiquidationPolicy.CancelOpenOrdersAndReduceToMaintenance);

        Assert.Equal(LiquidationPolicy.CancelOpenOrdersAndReduceToMaintenance, margin.LiquidationPolicy);
    }

    [Fact]
    public void MarginParams_WithLiquidationPolicyRejectsUnsupportedPolicy()
    {
        var margin = MarginParams.Leverage(5m);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            margin.WithLiquidationPolicy((LiquidationPolicy)255));
    }

    [Fact]
    public void MarginParams_WithShortSalePolicyConfiguresPolicy()
    {
        var margin = MarginParams.Leverage(5m)
            .WithShortSalePolicy(ShortSalePolicy.RequireBorrow);

        Assert.Equal(ShortSalePolicy.RequireBorrow, margin.ShortSalePolicy);
    }

    [Fact]
    public void MarginParams_WithShortSalePolicyRejectsUnsupportedPolicy()
    {
        var margin = MarginParams.Leverage(5m);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            margin.WithShortSalePolicy((ShortSalePolicy)255));
    }

    [Fact]
    public void MarginParams_WithRehypothecationPolicyConfiguresPolicy()
    {
        var margin = MarginParams.RegT()
            .WithRehypothecationPolicy(RehypothecationPolicy.Allowed);

        Assert.Equal(RehypothecationPolicy.Allowed, margin.RehypothecationPolicy);
    }

    [Fact]
    public void MarginParams_WithRehypothecationPolicyRejectsUnsupportedPolicy()
    {
        var margin = MarginParams.RegT();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            margin.WithRehypothecationPolicy((RehypothecationPolicy)255));
    }

    [Fact]
    public void MarginParams_WithBorrowAvailabilityConfiguresInstrumentBorrow()
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var margin = MarginParams.RegT()
            .WithBorrowAvailability(instrument, new Qty(12m));

        Assert.Equal(new Qty(12m), margin.BorrowAvailability[instrument]);

        margin = margin.WithBorrowAvailability(instrument, Qty.Zero);

        Assert.Empty(margin.BorrowAvailability);
    }

    [Fact]
    public void MarginParams_WithBorrowAvailabilityRejectsNegativeQuantity()
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var margin = MarginParams.RegT();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            margin.WithBorrowAvailability(instrument, new Qty(-1m)));
    }

    [Fact]
    public void MarginParams_WithRehypothecationAvailabilityConfiguresInstrumentReuse()
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var margin = MarginParams.Leverage(2m)
            .WithRehypothecationAvailability(instrument, new Qty(7m));

        Assert.Equal(new Qty(7m), margin.RehypothecationAvailability[instrument]);

        margin = margin.WithRehypothecationAvailability(instrument, Qty.Zero);

        Assert.Empty(margin.RehypothecationAvailability);
    }

    [Fact]
    public void MarginParams_WithRehypothecationAvailabilityRejectsNegativeQuantity()
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var margin = MarginParams.Leverage(2m);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            margin.WithRehypothecationAvailability(instrument, new Qty(-1m)));
    }

    [Fact]
    public void SettlementParams_TPlusConvertsDaysToDuration()
    {
        var settlement = SettlementParams.TPlus(2);

        Assert.Equal(Duration.FromDays(2), settlement.CashProceedsDelay);
        Assert.Equal(2, settlement.BusinessDays);
        Assert.True(settlement.UsesBusinessDayCalendar);
        Assert.Equal(UnsettledSalePolicy.Reject, settlement.UnsettledSalePolicy);
    }

    [Fact]
    public void SettlementParams_CalendarDaysUsesPlainDuration()
    {
        var settlement = SettlementParams.CalendarDays(2);

        Assert.Equal(Duration.FromDays(2), settlement.CashProceedsDelay);
        Assert.Equal(0, settlement.BusinessDays);
        Assert.False(settlement.UsesBusinessDayCalendar);
    }

    [Fact]
    public void SettlementParams_WithUnsettledSalePolicyOverridesPolicy()
    {
        var settlement = SettlementParams.TPlus(2)
            .WithUnsettledSalePolicy(UnsettledSalePolicy.Allow);

        Assert.Equal(Duration.FromDays(2), settlement.CashProceedsDelay);
        Assert.Equal(2, settlement.BusinessDays);
        Assert.Equal(UnsettledSalePolicy.Allow, settlement.UnsettledSalePolicy);
        Assert.NotNull(settlement.Calendar);
    }

    [Fact]
    public void SettlementParams_WithUnsettledSalePolicyRejectsUnsupportedPolicy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SettlementParams.TPlus(2).WithUnsettledSalePolicy((UnsettledSalePolicy)99));
    }

    [Fact]
    public void SettlementParams_TPlusSkipsWeekends()
    {
        var tradeTime = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 5, 14, 30, 0, TimeSpan.Zero));
        var expected = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 8, 14, 30, 0, TimeSpan.Zero));

        var settlement = SettlementParams.TPlus(1);

        Assert.Equal(expected, settlement.GetSettlementTime(tradeTime));
    }

    [Fact]
    public void SettlementParams_TPlusSkipsConfiguredHolidays()
    {
        var tradeTime = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 5, 14, 30, 0, TimeSpan.Zero));
        var expected = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 9, 14, 30, 0, TimeSpan.Zero));

        var settlement = SettlementParams.TPlus(1, [new DateOnly(2024, 1, 8)]);

        Assert.Equal(expected, settlement.GetSettlementTime(tradeTime));
    }

    [Fact]
    public void SettlementParams_TPlusUsesSuppliedClearingCalendar()
    {
        var tradeTime = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 5, 14, 30, 0, TimeSpan.Zero));
        var expected = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 6, 14, 30, 0, TimeSpan.Zero));

        var settlement = SettlementParams.TPlus(1, ClearingCalendar.Crypto());

        Assert.Equal(expected, settlement.GetSettlementTime(tradeTime));
        Assert.Equal("Crypto", settlement.Calendar?.Name);
    }

    [Fact]
    public void SettlementParams_TPlusForVenueUsesCatalogHolidays()
    {
        var tradeTime = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 12, 14, 30, 0, TimeSpan.Zero));
        var expected = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 16, 14, 30, 0, TimeSpan.Zero));

        var settlement = SettlementParams.TPlusForVenue(1, Venue.NYSE, 2024);

        Assert.Equal(expected, settlement.GetSettlementTime(tradeTime));
        Assert.Equal("US Equities", settlement.Calendar?.Name);
    }

    [Fact]
    public void SettlementParams_TPlusForVenueAppliesBrokerOverrides()
    {
        var tradeTime = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 16, 14, 30, 0, TimeSpan.Zero));
        var expected = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 18, 14, 30, 0, TimeSpan.Zero));

        var settlement = SettlementParams.TPlusForVenue(
            1,
            Venue.NYSE,
            2024,
            additionalHolidays: [new DateOnly(2024, 1, 17)]);

        Assert.Equal(expected, settlement.GetSettlementTime(tradeTime));
    }

    [Fact]
    public void ClearingCalendar_ForVenueUsesCryptoCalendarForCryptoVenues()
    {
        var calendar = ClearingCalendar.ForVenue(Venue.Binance);

        Assert.True(calendar.IsBusinessDay(new DateOnly(2024, 1, 6)));
    }

    [Fact]
    public void ClearingCalendar_ForVenueUsesWeekdayCalendarForUsEquities()
    {
        var calendar = ClearingCalendar.ForVenue(Venue.NYSE, [new DateOnly(2024, 1, 8)]);

        Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 1, 6)));
        Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 1, 8)));
        Assert.True(calendar.IsBusinessDay(new DateOnly(2024, 1, 9)));
    }

    [Fact]
    public void ClearingCalendarCatalog_ForVenueIncludesUsMarketHolidays()
    {
        var calendar = ClearingCalendarCatalog.ForVenue(Venue.NYSE, 2024);

        Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 1, 15)));
        Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 3, 29)));
        Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 6, 19)));
        Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 11, 28)));
        Assert.True(calendar.IsBusinessDay(new DateOnly(2024, 1, 16)));
    }

    [Fact]
    public void ClearingCalendarCatalog_ForVenueIncludesBundledSpecialClosures()
    {
        var calendar = ClearingCalendarCatalog.ForVenue(Venue.NYSE, 2025);

        Assert.False(calendar.IsBusinessDay(new DateOnly(2025, 1, 9)));
        Assert.True(calendar.IsBusinessDay(new DateOnly(2025, 1, 10)));
    }

    [Fact]
    public void ClearingCalendarCatalog_BundledHolidayDatasetLoadsEmbeddedDataset()
    {
        var holidays = ClearingCalendarCatalog.BundledHolidayDataset(
            "us-market",
            start: new DateOnly(2025, 1, 1),
            end: new DateOnly(2025, 1, 31));

        Assert.Contains("us-market", ClearingCalendarCatalog.BundledDatasetIds);
        Assert.Contains(new DateOnly(2025, 1, 9), holidays);
        Assert.Contains(new DateOnly(2025, 1, 20), holidays);
        Assert.DoesNotContain(new DateOnly(2025, 2, 17), holidays);
    }

    [Fact]
    public void ClearingCalendarCatalog_FromBundledHolidayDatasetCreatesCustomCalendar()
    {
        var calendar = ClearingCalendarCatalog.FromBundledHolidayDataset(
            "Weekday Dataset",
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            "us-market",
            start: new DateOnly(2026, 7, 1),
            end: new DateOnly(2026, 7, 31));

        Assert.False(calendar.IsBusinessDay(new DateOnly(2026, 7, 3)));
        Assert.True(calendar.IsBusinessDay(new DateOnly(2026, 7, 6)));
    }

    [Fact]
    public void ClearingCalendarCatalog_ForVenueKeepsCryptoAlwaysOpen()
    {
        var calendar = ClearingCalendarCatalog.ForVenue(Venue.Binance, 2024);

        Assert.True(calendar.IsBusinessDay(new DateOnly(2024, 1, 15)));
        Assert.True(calendar.IsBusinessDay(new DateOnly(2024, 1, 14)));
    }

    [Fact]
    public void ClearingCalendarCatalog_ParseHolidayFeedSupportsCsvHeadersCommentsAndDateFormats()
    {
        var feed = """
            date,name
            # broker special closure
            2024-01-02,Special closure
            20240103,Exchange holiday
            1/4/2024,Broker holiday
            """;

        var holidays = ClearingCalendarCatalog.ParseHolidayFeed(feed);

        Assert.Contains(new DateOnly(2024, 1, 2), holidays);
        Assert.Contains(new DateOnly(2024, 1, 3), holidays);
        Assert.Contains(new DateOnly(2024, 1, 4), holidays);
    }

    [Fact]
    public void ClearingCalendarCatalog_ParseHolidayFeedFiltersDateRange()
    {
        var feed = """
            date
            2024-01-02
            2024-01-03
            2024-01-04
            """;

        var holidays = ClearingCalendarCatalog.ParseHolidayFeed(
            feed,
            start: new DateOnly(2024, 1, 3),
            end: new DateOnly(2024, 1, 3));

        var holiday = Assert.Single(holidays);
        Assert.Equal(new DateOnly(2024, 1, 3), holiday);
    }

    [Fact]
    public void ClearingCalendarCatalog_ForVenueWithHolidayFeedUsesVenueBusinessDaysAndFeedHolidays()
    {
        var feed = """
            date
            2024-01-08
            """;

        var calendar = ClearingCalendarCatalog.ForVenueWithHolidayFeed(
            Venue.NYSE,
            feed,
            start: new DateOnly(2024, 1, 1),
            end: new DateOnly(2024, 1, 31));

        Assert.Equal("NYSE Feed", calendar.Name);
        Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 1, 6)));
        Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 1, 8)));
        Assert.True(calendar.IsBusinessDay(new DateOnly(2024, 1, 9)));
    }

    [Fact]
    public void ClearingCalendarCatalog_FromHolidayFeedCreatesCustomCalendar()
    {
        var feed = """
            2024-01-06
            """;

        var calendar = ClearingCalendarCatalog.FromHolidayFeed(
            "Saturday Clearing",
            [DayOfWeek.Saturday],
            feed);

        Assert.Equal("Saturday Clearing", calendar.Name);
        Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 1, 6)));
        Assert.True(calendar.IsBusinessDay(new DateOnly(2024, 1, 13)));
    }

    [Fact]
    public void ClearingCalendarCatalog_ForVenueWithHolidayFeedFileLoadsProviderDataset()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rhodium-holidays-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "date,name\n2024-01-08,Broker closure\n");

        try
        {
            var calendar = ClearingCalendarCatalog.ForVenueWithHolidayFeedFile(
                Venue.NYSE,
                path,
                start: new DateOnly(2024, 1, 1),
                end: new DateOnly(2024, 1, 31));

            Assert.Equal("NYSE Feed", calendar.Name);
            Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 1, 8)));
            Assert.True(calendar.IsBusinessDay(new DateOnly(2024, 1, 9)));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ClearingCalendarCatalog_FromHolidayFeedFileLoadsCustomCalendar()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rhodium-holidays-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "2024-01-06\n");

        try
        {
            var calendar = ClearingCalendarCatalog.FromHolidayFeedFile(
                "Saturday Clearing",
                [DayOfWeek.Saturday],
                path);

            Assert.Equal("Saturday Clearing", calendar.Name);
            Assert.False(calendar.IsBusinessDay(new DateOnly(2024, 1, 6)));
            Assert.True(calendar.IsBusinessDay(new DateOnly(2024, 1, 13)));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ClearingCalendarCatalog_ParseHolidayFeedRejectsInvalidRows()
    {
        var ex = Assert.Throws<FormatException>(() =>
            ClearingCalendarCatalog.ParseHolidayFeed("date\nnot-a-date"));

        Assert.Contains("line 2", ex.Message);
    }

    [Fact]
    public void ClearingCalendarCatalog_ForVenueRejectsInvalidRange()
    {
        Assert.Throws<ArgumentException>(() => ClearingCalendarCatalog.ForVenue(
            Venue.NYSE,
            new DateOnly(2024, 1, 2),
            new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public void ClearingCalendar_RejectsEmptyBusinessDays()
    {
        Assert.Throws<ArgumentException>(() => new ClearingCalendar("Never", []));
    }

    [Fact]
    public void SettlementParams_TPlusRejectsNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SettlementParams.TPlus(-1));
    }

    [Fact]
    public void SettlementParams_CalendarDaysRejectsNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SettlementParams.CalendarDays(-1));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(0.0)]
    [InlineData(1.1)]
    public void SimulationOptionAssignmentInput_RejectsInvalidProRataRatio(double ratio)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationOptionAssignmentInput(proRataAssignmentRatio: (decimal)ratio));

        Assert.Equal("proRataAssignmentRatio", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SimulationOptionAssignmentInput_RejectsEmptyReason(string reason)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new SimulationOptionAssignmentInput(reason: reason));

        Assert.Equal("reason", exception.ParamName);
    }

    [Fact]
    public void SimulationLifecycleConfig_SnapshotsSettlementReferencePrices()
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var prices = new Dictionary<Instrument, Price>
        {
            [instrument] = new Price(100m, Currency.USD)
        };
        var config = new SimulationLifecycleConfig(settlementReferencePrices: prices);

        prices[instrument] = new Price(200m, Currency.USD);

        Assert.Equal(new Price(100m, Currency.USD), config.SettlementReferencePrices[instrument]);
    }

    [Fact]
    public void SimulationLifecycleConfig_SnapshotsAssignmentInputs()
    {
        var strategyId = new StrategyId(1);
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var key = new SimulationOptionAssignmentKey(strategyId, 0, instrument);
        var first = new SimulationOptionAssignmentInput(isSelectedForRandomAssignment: true);
        var second = new SimulationOptionAssignmentInput(isSelectedForRandomAssignment: false);
        var inputs = new Dictionary<SimulationOptionAssignmentKey, SimulationOptionAssignmentInput>
        {
            [key] = first
        };
        var config = new SimulationLifecycleConfig(assignmentInputs: inputs);

        inputs[key] = second;

        Assert.True(config.TryGetAssignmentInput(strategyId, 0, instrument, out var resolved));
        Assert.Same(first, resolved);
    }

    [Fact]
    public void SimulationLifecycleConfig_RejectsNullAssignmentInput()
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var inputs = new Dictionary<SimulationOptionAssignmentKey, SimulationOptionAssignmentInput>
        {
            [new SimulationOptionAssignmentKey(new StrategyId(1), 0, instrument)] = null!
        };

        var exception = Assert.Throws<ArgumentException>(() => new SimulationLifecycleConfig(assignmentInputs: inputs));

        Assert.Equal("assignmentInputs", exception.ParamName);
    }

    [Fact]
    public void SimulationLifecycleConfig_RejectsUnknownMissingReferencePolicy()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationLifecycleConfig(missingReferencePricePolicy: (MissingReferencePricePolicy)99));

        Assert.Equal("missingReferencePricePolicy", exception.ParamName);
    }

    [Fact]
    public void SimulationLifecycleConfig_WithMissingReferencePricePolicy_ReturnsUpdatedPolicy()
    {
        var config = SimulationLifecycleConfig.Default.WithMissingReferencePricePolicy(MissingReferencePricePolicy.Throw);

        Assert.Equal(MissingReferencePricePolicy.Throw, config.MissingReferencePricePolicy);
    }

    [Fact]
    public void AllMarketStatuses_AreDefined()
    {
        var statuses = Enum.GetValues<MarketStatus>();

        Assert.Equal(4, statuses.Length);
        Assert.Contains(MarketStatus.PreOpen, statuses);
        Assert.Contains(MarketStatus.Open, statuses);
        Assert.Contains(MarketStatus.Closed, statuses);
        Assert.Contains(MarketStatus.Halted, statuses);
    }

    [Fact]
    public void AllBarOrderingModes_AreDefined()
    {
        var modes = Enum.GetValues<BarOrderingMode>();

        Assert.Equal(2, modes.Length);
        Assert.Contains(BarOrderingMode.Fixed, modes);
        Assert.Contains(BarOrderingMode.Adaptive, modes);
    }

    [Fact]
    public void AllFillBehaviors_AreDefined()
    {
        var behaviors = Enum.GetValues<FillBehavior>();

        Assert.Equal(3, behaviors.Length);
        Assert.Contains(FillBehavior.NoPartialFill, behaviors);
        Assert.Contains(FillBehavior.FillOnTouch, behaviors);
        Assert.Contains(FillBehavior.PartialFillOnTrade, behaviors);
    }
}
