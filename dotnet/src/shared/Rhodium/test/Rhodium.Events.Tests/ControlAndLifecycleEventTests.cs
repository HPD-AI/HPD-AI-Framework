using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Events.Tests;

// ==================== CONTROL EVENT TESTS ====================

public class UserCancellationTests
{
    [Fact]
    public void UserCancellation_ShouldStoreReason()
    {
        // Arrange
        var reason = "Emergency stop";

        // Act
        var evt = new UserCancellation(reason);

        // Assert
        Assert.Equal(reason, evt.Reason);
    }

    [Fact]
    public void UserCancellation_ReasonShouldBeOptional()
    {
        // Arrange & Act
        var evt = new UserCancellation();

        // Assert
        Assert.Null(evt.Reason);
    }

    [Fact]
    public void UserCancellation_ShouldBeControlEvent()
    {
        // Arrange & Act
        var evt = new UserCancellation("User requested");

        // Assert
        Assert.IsAssignableFrom<ControlEvent>(evt);
        Assert.IsAssignableFrom<FinanceEvent>(evt);
    }

    [Fact]
    public void UserCancellation_ShouldHaveImmediatePriority()
    {
        // Arrange & Act
        var evt = new UserCancellation("Stop");

        // Assert
        Assert.Equal(HPD.Events.EventPriority.Immediate, evt.Priority);
    }
}

public class RiskLimitBreachedTests
{
    [Fact]
    public void RiskLimitBreached_ShouldStoreAllValues()
    {
        // Arrange
        var limitName = "MaxDrawdown";
        var currentValue = 15.5m;
        var limitValue = 10m;

        // Act
        var evt = new RiskLimitBreached(limitName, currentValue, limitValue);

        // Assert
        Assert.Equal(limitName, evt.LimitName);
        Assert.Equal(currentValue, evt.CurrentValue);
        Assert.Equal(limitValue, evt.LimitValue);
    }

    [Fact]
    public void RiskLimitBreached_ShouldBeControlEvent()
    {
        // Arrange & Act
        var evt = new RiskLimitBreached("MaxPosition", 1000m, 500m);

        // Assert
        Assert.IsAssignableFrom<ControlEvent>(evt);
    }

    [Fact]
    public void RiskLimitBreached_ShouldHaveImmediatePriority()
    {
        // Arrange & Act
        var evt = new RiskLimitBreached("DailyLoss", 50000m, 25000m);

        // Assert
        Assert.Equal(HPD.Events.EventPriority.Immediate, evt.Priority);
    }
}

public class EmergencyStopTests
{
    [Fact]
    public void EmergencyStop_ShouldStoreReason()
    {
        // Arrange
        var reason = "System critical error";

        // Act
        var evt = new EmergencyStop(reason);

        // Assert
        Assert.Equal(reason, evt.Reason);
    }

    [Fact]
    public void EmergencyStop_ShouldBeControlEvent()
    {
        // Arrange & Act
        var evt = new EmergencyStop("Data feed lost");

        // Assert
        Assert.IsAssignableFrom<ControlEvent>(evt);
    }
}

// ==================== LIFECYCLE EVENT TESTS ====================

public class ScheduledTests
{
    [Fact]
    public void Scheduled_ShouldStoreName()
    {
        // Arrange
        var name = "DailyRebalance";

        // Act
        var evt = new Scheduled(name);

        // Assert
        Assert.Equal(name, evt.Name);
    }

    [Fact]
    public void Scheduled_ShouldBeLifecycleEvent()
    {
        // Arrange & Act
        var evt = new Scheduled("EndOfDay");

        // Assert
        Assert.IsAssignableFrom<LifecycleEvent>(evt);
    }
}

public class SessionStartedTests
{
    [Fact]
    public void SessionStarted_ShouldBeLifecycleEvent()
    {
        // Arrange & Act
        var evt = new SessionStarted();

        // Assert
        Assert.IsAssignableFrom<LifecycleEvent>(evt);
        Assert.IsAssignableFrom<FinanceEvent>(evt);
    }

    [Fact]
    public void SessionStarted_ShouldHaveNormalPriority()
    {
        // Arrange & Act
        var evt = new SessionStarted();

        // Assert
        Assert.Equal(HPD.Events.EventPriority.Normal, evt.Priority);
    }
}

public class SessionEndedTests
{
    [Fact]
    public void SessionEnded_ShouldBeLifecycleEvent()
    {
        // Arrange & Act
        var evt = new SessionEnded();

        // Assert
        Assert.IsAssignableFrom<LifecycleEvent>(evt);
    }
}

public class MarketOpenedTests
{
    [Fact]
    public void MarketOpened_ShouldStoreVenue()
    {
        // Arrange
        var venue = Venue.NYSE;

        // Act
        var evt = new MarketOpened(venue);

        // Assert
        Assert.Equal(venue, evt.Venue);
    }

    [Fact]
    public void MarketOpened_ShouldBeLifecycleEvent()
    {
        // Arrange & Act
        var evt = new MarketOpened(Venue.NASDAQ);

        // Assert
        Assert.IsAssignableFrom<LifecycleEvent>(evt);
    }
}

public class MarketClosedTests
{
    [Fact]
    public void MarketClosed_ShouldStoreVenue()
    {
        // Arrange
        var venue = Venue.CME;

        // Act
        var evt = new MarketClosed(venue);

        // Assert
        Assert.Equal(venue, evt.Venue);
    }

    [Fact]
    public void MarketClosed_ShouldBeLifecycleEvent()
    {
        // Arrange & Act
        var evt = new MarketClosed(Venue.Binance);

        // Assert
        Assert.IsAssignableFrom<LifecycleEvent>(evt);
    }
}

public class PreMarketOpenedTests
{
    [Fact]
    public void PreMarketOpened_ShouldStoreVenue()
    {
        // Arrange
        var venue = Venue.NYSE;

        // Act
        var evt = new PreMarketOpened(venue);

        // Assert
        Assert.Equal(venue, evt.Venue);
    }

    [Fact]
    public void PreMarketOpened_ShouldBeLifecycleEvent()
    {
        // Arrange & Act
        var evt = new PreMarketOpened(Venue.NASDAQ);

        // Assert
        Assert.IsAssignableFrom<LifecycleEvent>(evt);
    }
}

public class PostMarketOpenedTests
{
    [Fact]
    public void PostMarketOpened_ShouldStoreVenue()
    {
        // Arrange
        var venue = Venue.NASDAQ;

        // Act
        var evt = new PostMarketOpened(venue);

        // Assert
        Assert.Equal(venue, evt.Venue);
    }

    [Fact]
    public void PostMarketOpened_ShouldBeLifecycleEvent()
    {
        // Arrange & Act
        var evt = new PostMarketOpened(Venue.NYSE);

        // Assert
        Assert.IsAssignableFrom<LifecycleEvent>(evt);
    }
}

public class UniverseChangedTests
{
    [Fact]
    public void UniverseChanged_ShouldStoreAddedRemovedAndName()
    {
        // Arrange
        var added = new HashSet<Instrument>
        {
            new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ),
            new Instrument(new Asset("MSFT", AssetClass.Equity), Venue.NASDAQ)
        };
        var removed = new HashSet<Instrument>
        {
            new Instrument(new Asset("TSLA", AssetClass.Equity), Venue.NASDAQ)
        };
        var universeName = "SP500";

        // Act
        var evt = new UniverseChanged(added, removed, universeName);

        // Assert
        Assert.Equal(added, evt.Added);
        Assert.Equal(removed, evt.Removed);
        Assert.Equal(universeName, evt.UniverseName);
    }

    [Fact]
    public void UniverseChanged_HasChanges_ShouldReturnTrueWhenThereAreChanges()
    {
        // Arrange
        var added = new HashSet<Instrument>
        {
            new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ)
        };
        var removed = new HashSet<Instrument>();

        // Act
        var evt = new UniverseChanged(added, removed, "Test");

        // Assert
        Assert.True(evt.HasChanges);
    }

    [Fact]
    public void UniverseChanged_HasChanges_ShouldReturnFalseWhenNoChanges()
    {
        // Arrange
        var added = new HashSet<Instrument>();
        var removed = new HashSet<Instrument>();

        // Act
        var evt = new UniverseChanged(added, removed, "Test");

        // Assert
        Assert.False(evt.HasChanges);
    }

    [Fact]
    public void UniverseChanged_ShouldBeLifecycleEvent()
    {
        // Arrange
        var added = new HashSet<Instrument>();
        var removed = new HashSet<Instrument>();

        // Act
        var evt = new UniverseChanged(added, removed, "Universe1");

        // Assert
        Assert.IsAssignableFrom<LifecycleEvent>(evt);
    }
}
