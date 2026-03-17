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
    public void MarketEvent_ShouldHaveNormalPriority()
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
        Assert.Equal(EventPriority.Normal, evt.Priority);
    }
}

public class ExecutionEventTests
{
    [Fact]
    public void ExecutionEvent_ShouldHaveContentKind()
    {
        // Arrange & Act
        var evt = new OrderAccepted(OrderId.New(), 1);

        // Assert
        Assert.Equal(EventKind.Content, evt.Kind);
    }

    [Fact]
    public void ExecutionEvent_ShouldHaveControlPriority()
    {
        // Arrange & Act
        var evt = new OrderAccepted(OrderId.New(), 1);

        // Assert
        Assert.Equal(EventPriority.Control, evt.Priority);
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
    public void ControlEvent_ShouldHaveImmediatePriority()
    {
        // Arrange & Act
        var evt = new UserCancellation("User requested");

        // Assert
        Assert.Equal(EventPriority.Immediate, evt.Priority);
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
    public void LifecycleEvent_ShouldHaveNormalPriority()
    {
        // Arrange & Act
        var evt = new SessionStarted();

        // Assert
        Assert.Equal(EventPriority.Normal, evt.Priority);
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
    public void DiagnosticEvent_ShouldHaveBackgroundPriority()
    {
        // Arrange & Act
        var evt = new TestDiagnosticEvent();

        // Assert
        Assert.Equal(EventPriority.Background, evt.Priority);
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
    public void LatencyMeasured_CreatesCorrectly()
    {
        var latency = Duration.FromMicros(250);
        var latencyEvent = new LatencyMeasured("OrderSubmit", latency);

        Assert.Equal("OrderSubmit", latencyEvent.Operation);
        Assert.Equal(latency, latencyEvent.Latency);
    }
}
