using HPD.Events;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Events.Tests;

// Concrete test implementation of FinanceEvent
public sealed record TestFinanceEvent : FinanceEvent;

public class FinanceEventTests
{
    [Fact]
    public void FinanceEvent_ShouldInheritFromHPDEvent()
    {
        // Arrange & Act
        var evt = new TestFinanceEvent();

        // Assert
        Assert.IsAssignableFrom<Event>(evt);
    }

    [Fact]
    public void FinanceEvent_Time_ShouldConvertFromTimestamp()
    {
        // Arrange
        var before = Instant.Now;
        var evt = new TestFinanceEvent();
        var after = Instant.Now;

        // Act
        var time = evt.Time;

        // Assert
        Assert.True(time >= before);
        Assert.True(time <= after);
    }

    [Fact]
    public void FinanceEvent_Time_ShouldBeInitSettable()
    {
        var time = new Instant(1_700_000_000_123_456_789L);

        var evt = new TestFinanceEvent { Time = time };

        Assert.Equal(time, evt.Time);
        Assert.Equal(time.Nanos, evt.ExchangeTimestampNs);
        Assert.Equal(time.ToDateTimeOffset(), evt.Timestamp);
    }

    [Fact]
    public void FinanceEvent_Sequence_ShouldBeOptional()
    {
        // Arrange & Act
        var evt1 = new TestFinanceEvent();
        var evt2 = new TestFinanceEvent { Sequence = new Sequence(42) };

        // Assert
        Assert.Null(evt1.Sequence);
        Assert.Equal(42UL, evt2.Sequence!.Value.Value);
    }

    [Fact]
    public void FinanceEvent_ShouldHaveTimestampFromBase()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var evt = new TestFinanceEvent();

        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.True(evt.Timestamp >= before);
        Assert.True(evt.Timestamp <= after);
    }

    [Fact]
    public void OptionLifecycleApplied_BlockedWithResolvedReferenceSource_Throws()
    {
        var instrument = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));

        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleApplied(
            new StrategyId(1),
            VariantId: 0,
            instrument,
            OptionLifecycleKind.Blocked,
            new Qty(1m),
            Money.Zero(Currency.USD),
            Instant.FromUnixSeconds(1),
            UnderlyingMark: new Price(105m, Currency.USD),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark));

        Assert.Equal("ReferenceSource", exception.ParamName);
    }

    [Fact]
    public void OptionLifecycleApplied_ResolvedWithNoReferenceSource_Throws()
    {
        var instrument = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));

        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleApplied(
            new StrategyId(1),
            VariantId: 0,
            instrument,
            OptionLifecycleKind.CashSettlement,
            new Qty(1m),
            Money.Zero(Currency.USD),
            Instant.FromUnixSeconds(1)));

        Assert.Equal("ReferenceSource", exception.ParamName);
    }

    [Fact]
    public void OptionLifecycleApplied_ResolvedWithoutUnderlyingMark_Throws()
    {
        var instrument = new Instrument(new Asset("SPX261218C00100000", AssetClass.Option), new Venue("CBOE"));

        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleApplied(
            new StrategyId(1),
            VariantId: 0,
            instrument,
            OptionLifecycleKind.CashSettlement,
            new Qty(1m),
            Money.Zero(Currency.USD),
            Instant.FromUnixSeconds(1),
            SettlementPrice: new Price(105m, Currency.USD),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark));

        Assert.Equal("UnderlyingMark", exception.ParamName);
    }

    [Fact]
    public void OptionLifecycleApplied_PhysicalDeliveryWithoutDeliverable_Throws()
    {
        var instrument = new Instrument(new Asset("SPY261218C00100000", AssetClass.Option), new Venue("CBOE"));

        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleApplied(
            new StrategyId(1),
            VariantId: 0,
            instrument,
            OptionLifecycleKind.PhysicalDelivery,
            new Qty(1m),
            Money.Zero(Currency.USD),
            Instant.FromUnixSeconds(1),
            UnderlyingMark: new Price(105m, Currency.USD),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark));

        Assert.Equal("Deliverable", exception.ParamName);
    }

    [Fact]
    public void OptionLifecycleApplied_PhysicalDeliveryWithoutSettlementPrice_Throws()
    {
        var instrument = new Instrument(new Asset("SPY261218C00100000", AssetClass.Option), new Venue("CBOE"));
        var deliverable = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);

        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleApplied(
            new StrategyId(1),
            VariantId: 0,
            instrument,
            OptionLifecycleKind.PhysicalDelivery,
            new Qty(1m),
            Money.Zero(Currency.USD),
            Instant.FromUnixSeconds(1),
            UnderlyingMark: new Price(105m, Currency.USD),
            Deliverable: deliverable,
            DeliverableQuantity: new Qty(100m),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark));

        Assert.Equal("SettlementPrice", exception.ParamName);
    }

    [Fact]
    public void OptionLifecycleApplied_NonPhysicalDeliveryWithDeliverable_Throws()
    {
        var instrument = new Instrument(new Asset("SPX261218C00100000", AssetClass.Option), new Venue("CBOE"));
        var deliverable = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));

        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleApplied(
            new StrategyId(1),
            VariantId: 0,
            instrument,
            OptionLifecycleKind.CashSettlement,
            new Qty(1m),
            Money.Zero(Currency.USD),
            Instant.FromUnixSeconds(1),
            UnderlyingMark: new Price(105m, Currency.USD),
            Deliverable: deliverable,
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark));

        Assert.Equal("Deliverable", exception.ParamName);
    }

    [Fact]
    public void OptionLifecycleApplied_BlockedWithCashFlow_Throws()
    {
        var instrument = new Instrument(new Asset("SPX261218C00100000", AssetClass.Option), new Venue("CBOE"));

        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleApplied(
            new StrategyId(1),
            VariantId: 0,
            instrument,
            OptionLifecycleKind.Blocked,
            new Qty(1m),
            Money.USD(1m),
            Instant.FromUnixSeconds(1)));

        Assert.Equal("CashFlow", exception.ParamName);
    }

    [Fact]
    public void OptionLifecycleApplied_ExerciseWithCashFlow_Throws()
    {
        var instrument = new Instrument(new Asset("SPX261218C00100000", AssetClass.Option), new Venue("CBOE"));

        var exception = Assert.Throws<ArgumentException>(() => new OptionLifecycleApplied(
            new StrategyId(1),
            VariantId: 0,
            instrument,
            OptionLifecycleKind.Exercise,
            new Qty(1m),
            Money.USD(1m),
            Instant.FromUnixSeconds(1),
            UnderlyingMark: new Price(105m, Currency.USD),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark));

        Assert.Equal("CashFlow", exception.ParamName);
    }
}

public class MarketEventTests
{
    [Fact]
    public void MarketEvent_ShouldHaveContentKind()
    {
        // Arrange
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var quote = new Quote(
            new Price(100m),
            new Price(101m),
            new Qty(500m),
            new Qty(300m),
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var evt = new QuoteReceived(instrument, quote);

        // Assert
        Assert.Equal(EventKind.Content, evt.Kind);
    }

    [Fact]
    public void MarketEvent_ShouldHaveStreamingChannel()
    {
        // Arrange
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var quote = new Quote(
            new Price(100m),
            new Price(101m),
            new Qty(500m),
            new Qty(300m),
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var evt = new QuoteReceived(instrument, quote);

        // Assert
        Assert.Equal(EventChannel.Streaming, evt.Channel);
    }
}

public class ExecutionEventTests
{
    [Fact]
    public void ExecutionEvent_ShouldHaveContentKind()
    {
        // Arrange & Act
        var evt = new OrderAccepted(OrderId.New(), new StrategyId(1), 1);

        // Assert
        Assert.Equal(EventKind.Content, evt.Kind);
    }

    [Fact]
    public void ExecutionEvent_ShouldHaveSynchronousChannel()
    {
        // Arrange & Act
        var evt = new OrderAccepted(OrderId.New(), new StrategyId(1), 1);

        // Assert
        Assert.Equal(EventChannel.Synchronous, evt.Channel);
    }
}

public class ControlEventTests
{
    [Fact]
    public void ControlEvent_ShouldHaveControlKind()
    {
        // Arrange & Act
        var evt = new UserCancellation("User requested");

        // Assert
        Assert.Equal(EventKind.Control, evt.Kind);
    }

    [Fact]
    public void ControlEvent_ShouldHaveControlChannel()
    {
        // Arrange & Act
        var evt = new UserCancellation("User requested");

        // Assert
        Assert.Equal(EventChannel.Control, evt.Channel);
    }
}

public class LifecycleEventTests
{
    [Fact]
    public void LifecycleEvent_ShouldHaveLifecycleKind()
    {
        // Arrange & Act
        var evt = new SessionStarted();

        // Assert
        Assert.Equal(EventKind.Lifecycle, evt.Kind);
    }

    [Fact]
    public void LifecycleEvent_ShouldHaveSynchronousChannel()
    {
        // Arrange & Act
        var evt = new SessionStarted();

        // Assert
        Assert.Equal(EventChannel.Synchronous, evt.Channel);
    }
}

public class DiagnosticEventTests
{
    // Concrete test implementation since DiagnosticEvent is abstract
    public sealed record TestDiagnosticEvent : DiagnosticEvent;

    [Fact]
    public void DiagnosticEvent_ShouldHaveDiagnosticKind()
    {
        // Arrange & Act
        var evt = new TestDiagnosticEvent();

        // Assert
        Assert.Equal(EventKind.Diagnostic, evt.Kind);
    }

    [Fact]
    public void DiagnosticEvent_ShouldHaveStreamingChannel()
    {
        // Arrange & Act
        var evt = new TestDiagnosticEvent();

        // Assert
        Assert.Equal(EventChannel.Streaming, evt.Channel);
    }

    [Fact]
    public void PerformanceSnapshot_CreatesCorrectly()
    {
        var equity = new Money(100000m, Currency.USD);
        var cash = new Money(50000m, Currency.USD);
        var unrealizedPnL = new Money(5000m, Currency.USD);
        var realizedPnL = new Money(2000m, Currency.USD);

        var snapshot = new PerformanceSnapshot(
            equity,
            cash,
            unrealizedPnL,
            realizedPnL,
            OpenPositions: 5,
            OpenOrders: 3
        );

        Assert.Equal(equity, snapshot.Equity);
        Assert.Equal(cash, snapshot.Cash);
        Assert.Equal(unrealizedPnL, snapshot.UnrealizedPnL);
        Assert.Equal(realizedPnL, snapshot.RealizedPnL);
        Assert.Equal(5, snapshot.OpenPositions);
        Assert.Equal(3, snapshot.OpenOrders);
    }

    [Fact]
    public void AccountStatementSnapshot_CreatesCorrectly()
    {
        var strategyId = new StrategyId(7);
        var snapshot = new AccountStatementSnapshot(
            strategyId,
            VariantId: 2,
            Currency.USD,
            Cash: Money.USD(1_000m),
            AvailableCash: Money.USD(850m),
            PendingSettlement: Money.USD(25m),
            ReservedCash: Money.USD(150m),
            MarketValue: Money.USD(500m),
            Equity: Money.USD(1_525m),
            UnrealizedPnL: Money.USD(10m),
            RealizedPnL: Money.USD(-5m),
            OpenPositions: 1,
            OpenOrders: 2);

        Assert.Equal(strategyId, snapshot.StrategyId);
        Assert.Equal(2, snapshot.VariantId);
        Assert.Equal(Currency.USD, snapshot.Currency);
        Assert.Equal(Money.USD(1_000m), snapshot.Cash);
        Assert.Equal(Money.USD(850m), snapshot.AvailableCash);
        Assert.Equal(Money.USD(25m), snapshot.PendingSettlement);
        Assert.Equal(Money.USD(150m), snapshot.ReservedCash);
        Assert.Equal(Money.USD(500m), snapshot.MarketValue);
        Assert.Equal(Money.USD(1_525m), snapshot.Equity);
        Assert.Equal(Money.USD(10m), snapshot.UnrealizedPnL);
        Assert.Equal(Money.USD(-5m), snapshot.RealizedPnL);
        Assert.Equal(1, snapshot.OpenPositions);
        Assert.Equal(2, snapshot.OpenOrders);
    }

    [Fact]
    public void CustodyPositionSnapshot_CreatesCorrectly()
    {
        var strategyId = new StrategyId(7);
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var snapshot = new CustodyPositionSnapshot(
            strategyId,
            VariantId: 2,
            instrument,
            Quantity: new Qty(3m),
            SettledQuantity: new Qty(2m),
            PendingDeliveryQuantity: new Qty(1m),
            RehypothecatableQuantity: new Qty(2m),
            AvgEntryPrice: new Price(100m, Currency.USD),
            MarkPrice: new Price(101m, Currency.USD),
            MarketValue: Money.USD(303m),
            UnrealizedPnL: Money.USD(3m),
            RealizedPnL: Money.USD(-1m),
            IsOpen: true);

        Assert.Equal(strategyId, snapshot.StrategyId);
        Assert.Equal(2, snapshot.VariantId);
        Assert.Equal(instrument, snapshot.Instrument);
        Assert.Equal(new Qty(3m), snapshot.Quantity);
        Assert.Equal(new Qty(2m), snapshot.SettledQuantity);
        Assert.Equal(new Qty(1m), snapshot.PendingDeliveryQuantity);
        Assert.Equal(new Qty(2m), snapshot.RehypothecatableQuantity);
        Assert.Equal(new Price(100m, Currency.USD), snapshot.AvgEntryPrice);
        Assert.Equal(new Price(101m, Currency.USD), snapshot.MarkPrice);
        Assert.Equal(Money.USD(303m), snapshot.MarketValue);
        Assert.Equal(Money.USD(3m), snapshot.UnrealizedPnL);
        Assert.Equal(Money.USD(-1m), snapshot.RealizedPnL);
        Assert.True(snapshot.IsOpen);
    }

    [Fact]
    public void AssetDeliveryEvents_CreateCorrectly()
    {
        var deliveryId = new AssetDeliveryId(17);
        var strategyId = new StrategyId(7);
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var deliversAt = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 8, 14, 30, 0, TimeSpan.Zero));

        var scheduled = new AssetDeliveryScheduled(
            deliveryId,
            strategyId,
            VariantId: 2,
            instrument,
            Quantity: new Qty(3m),
            deliversAt);
        var delivered = new AssetDelivered(
            deliveryId,
            strategyId,
            VariantId: 2,
            instrument,
            Quantity: new Qty(3m),
            DeliveredAt: deliversAt);
        var canceled = new AssetDeliveryCanceled(
            deliveryId,
            strategyId,
            VariantId: 2,
            instrument,
            Quantity: new Qty(1m),
            CanceledAt: deliversAt);
        var status = new AssetDeliveryStatusSnapshot(
            deliveryId,
            strategyId,
            VariantId: 2,
            instrument,
            Quantity: new Qty(3m),
            AssetDeliveryStatus.Scheduled,
            DeliversAt: deliversAt,
            StatusAt: deliversAt);

        Assert.Equal(deliveryId, scheduled.DeliveryId);
        Assert.Equal(deliveryId, delivered.DeliveryId);
        Assert.Equal(deliveryId, canceled.DeliveryId);
        Assert.Equal(deliveryId, status.DeliveryId);
        Assert.Equal(AssetDeliveryStatus.Scheduled, status.Status);
        Assert.Equal(instrument, status.Instrument);
    }

    [Fact]
    public void SettlementScheduled_CreatesCorrectly()
    {
        var settlementId = new SettlementId(11);
        var strategyId = new StrategyId(7);
        var settlesAt = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 8, 14, 30, 0, TimeSpan.Zero));

        var evt = new SettlementScheduled(
            settlementId,
            strategyId,
            VariantId: 2,
            Amount: Money.USD(125m),
            settlesAt);

        Assert.Equal(settlementId, evt.SettlementId);
        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(2, evt.VariantId);
        Assert.Equal(Money.USD(125m), evt.Amount);
        Assert.Equal(settlesAt, evt.SettlesAt);
    }

    [Fact]
    public void SettlementReleased_CreatesCorrectly()
    {
        var settlementId = new SettlementId(11);
        var strategyId = new StrategyId(7);
        var settledAt = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 8, 14, 30, 0, TimeSpan.Zero));

        var evt = new SettlementReleased(
            settlementId,
            strategyId,
            VariantId: 2,
            Amount: Money.USD(125m),
            settledAt);

        Assert.Equal(settlementId, evt.SettlementId);
        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(2, evt.VariantId);
        Assert.Equal(Money.USD(125m), evt.Amount);
        Assert.Equal(settledAt, evt.SettledAt);
    }

    [Fact]
    public void SettlementStatusSnapshot_CreatesCorrectly()
    {
        var settlementId = new SettlementId(11);
        var strategyId = new StrategyId(7);
        var settlesAt = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 8, 14, 30, 0, TimeSpan.Zero));
        var statusAt = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 5, 14, 30, 0, TimeSpan.Zero));

        var evt = new SettlementStatusSnapshot(
            settlementId,
            strategyId,
            VariantId: 2,
            SettlementStatus.Scheduled,
            Amount: Money.USD(125m),
            settlesAt,
            statusAt);

        Assert.Equal(settlementId, evt.SettlementId);
        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(2, evt.VariantId);
        Assert.Equal(SettlementStatus.Scheduled, evt.Status);
        Assert.Equal(Money.USD(125m), evt.Amount);
        Assert.Equal(settlesAt, evt.SettlesAt);
        Assert.Equal(statusAt, evt.StatusAt);
    }

    [Fact]
    public void AccountTransferEvents_CreateCorrectly()
    {
        var transferId = new AccountTransferId(23);
        var strategyId = new StrategyId(7);
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var time = Instant.FromDateTimeOffset(
            new DateTimeOffset(2024, 1, 8, 14, 30, 0, TimeSpan.Zero));

        var requested = new AccountTransferRequested(
            transferId,
            strategyId,
            VariantId: 2,
            AccountTransferType.AssetDeposit,
            CashAmount: null,
            instrument,
            Quantity: new Qty(3m),
            RequestedAt: time,
            ExternalReference: "broker-1");
        var completed = new AccountTransferCompleted(
            transferId,
            strategyId,
            VariantId: 2,
            AccountTransferType.AssetDeposit,
            CashAmount: null,
            instrument,
            Quantity: new Qty(3m),
            CompletedAt: time,
            ExternalReference: "broker-1");
        var canceled = new AccountTransferCanceled(
            transferId,
            strategyId,
            VariantId: 2,
            AccountTransferType.AssetDeposit,
            CashAmount: null,
            instrument,
            Quantity: new Qty(3m),
            CanceledAt: time,
            Reason: "duplicate",
            ExternalReference: "broker-1");
        var failed = new AccountTransferFailed(
            transferId,
            strategyId,
            VariantId: 2,
            AccountTransferType.AssetDeposit,
            CashAmount: null,
            instrument,
            Quantity: new Qty(3m),
            FailedAt: time,
            Reason: "rejected",
            ExternalReference: "broker-1");
        var status = new AccountTransferStatusSnapshot(
            transferId,
            strategyId,
            VariantId: 2,
            AccountTransferType.AssetDeposit,
            AccountTransferStatus.Completed,
            CashAmount: null,
            instrument,
            Quantity: new Qty(3m),
            StatusAt: time,
            Reason: null,
            ExternalReference: "broker-1");

        Assert.Equal(transferId, requested.TransferId);
        Assert.Equal(transferId, completed.TransferId);
        Assert.Equal(transferId, canceled.TransferId);
        Assert.Equal(transferId, failed.TransferId);
        Assert.Equal(transferId, status.TransferId);
        Assert.Equal(AccountTransferStatus.Completed, status.Status);
        Assert.Equal(AccountTransferType.AssetDeposit, status.TransferType);
        Assert.Equal(instrument, status.Instrument);
        Assert.Equal(new Qty(3m), status.Quantity);
    }

    [Fact]
    public void LatencyMeasured_CreatesCorrectly()
    {
        var latency = Duration.FromMicros(250);
        var latencyEvent = new LatencyMeasured("OrderSubmit", latency);

        Assert.Equal("OrderSubmit", latencyEvent.Operation);
        Assert.Equal(latency, latencyEvent.Latency);
    }

    [Fact]
    public void MarginStatusSnapshot_CreatesCorrectly()
    {
        var strategyId = new StrategyId(7);
        var equity = Money.USD(750m);
        var requirement = Money.USD(800m);

        var snapshot = new MarginStatusSnapshot(
            strategyId,
            VariantId: 3,
            equity,
            requirement,
            IsMaintenanceBreached: true);

        Assert.Equal(strategyId, snapshot.StrategyId);
        Assert.Equal(3, snapshot.VariantId);
        Assert.Equal(equity, snapshot.Equity);
        Assert.Equal(requirement, snapshot.MaintenanceRequirement);
        Assert.True(snapshot.IsMaintenanceBreached);
    }

    [Fact]
    public void MarginCallIssued_CreatesCorrectly()
    {
        var strategyId = new StrategyId(7);
        var dueAt = new Instant(1_700_000_000_000_000_000L);
        var evt = new MarginCallIssued(
            strategyId,
            VariantId: 3,
            Equity: Money.USD(750m),
            MaintenanceRequirement: Money.USD(800m),
            DueAt: dueAt);

        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(3, evt.VariantId);
        Assert.Equal(Money.USD(750m), evt.Equity);
        Assert.Equal(Money.USD(800m), evt.MaintenanceRequirement);
        Assert.Equal(dueAt, evt.DueAt);
    }

    [Fact]
    public void MarginCallResolved_CreatesCorrectly()
    {
        var strategyId = new StrategyId(7);
        var evt = new MarginCallResolved(
            strategyId,
            VariantId: 3,
            Equity: Money.USD(900m),
            MaintenanceRequirement: Money.USD(800m));

        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(3, evt.VariantId);
        Assert.Equal(Money.USD(900m), evt.Equity);
        Assert.Equal(Money.USD(800m), evt.MaintenanceRequirement);
    }
}

public class OptionLifecycleEventTests
{
    [Fact]
    public void OptionLifecycleKind_SeparatesWorthlessUnexercisedAndUnassignedExpiry()
    {
        Assert.Contains(OptionLifecycleKind.ExpireWorthless, Enum.GetValues<OptionLifecycleKind>());
        Assert.Contains(OptionLifecycleKind.ExpireUnexercised, Enum.GetValues<OptionLifecycleKind>());
        Assert.Contains(OptionLifecycleKind.ExpireUnassigned, Enum.GetValues<OptionLifecycleKind>());
    }
}
