using FluentAssertions;
using HPDAgent.Graph.Core.Channels;
using Xunit;

namespace HPD.Graph.Tests.Channels;

/// <summary>
/// Tests for BarrierChannel synchronization primitive.
/// </summary>
public class BarrierChannelTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesChannel()
    {
        // Act
        var channel = new BarrierChannel<string>("test-barrier", 3);

        // Assert
        channel.Name.Should().Be("test-barrier");
        channel.RequiredCount.Should().Be(3);
        channel.CurrentCount.Should().Be(0);
        channel.IsSatisfied.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithZeroRequiredCount_ThrowsArgumentException()
    {
        // Act
        var act = () => new BarrierChannel<string>("test", 0);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Required count must be greater than 0*");
    }

    [Fact]
    public void Constructor_WithNegativeRequiredCount_ThrowsArgumentException()
    {
        // Act
        var act = () => new BarrierChannel<int>("test", -5);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Required count must be greater than 0*");
    }

    #endregion

    #region Set and CurrentCount Tests

    [Fact]
    public void Set_AddsValueToBarrier()
    {
        // Arrange
        var channel = new BarrierChannel<string>("test", 3);

        // Act
        channel.Set("value1");

        // Assert
        channel.CurrentCount.Should().Be(1);
        channel.IsSatisfied.Should().BeFalse();
    }

    [Fact]
    public void Set_MultipleValues_IncrementsCount()
    {
        // Arrange
        var channel = new BarrierChannel<int>("test", 3);

        // Act
        channel.Set(10);
        channel.Set(20);
        channel.Set(30);

        // Assert
        channel.CurrentCount.Should().Be(3);
        channel.IsSatisfied.Should().BeTrue();
    }

    [Fact]
    public void Set_IncrementsVersion()
    {
        // Arrange
        var channel = new BarrierChannel<string>("test", 2);
        var initialVersion = channel.Version;

        // Act
        channel.Set("value1");

        // Assert
        channel.Version.Should().Be(initialVersion + 1);
    }

    #endregion

    #region IsSatisfied Tests

    [Fact]
    public void IsSatisfied_ReturnsFalseBeforeNWrites()
    {
        // Arrange
        var channel = new BarrierChannel<string>("test", 3);

        // Act
        channel.Set("a");
        channel.Set("b");

        // Assert
        channel.CurrentCount.Should().Be(2);
        channel.IsSatisfied.Should().BeFalse();
    }

    [Fact]
    public void IsSatisfied_ReturnsTrueAfterNWrites()
    {
        // Arrange
        var channel = new BarrierChannel<string>("test", 3);

        // Act
        channel.Set("a");
        channel.Set("b");
        channel.Set("c");

        // Assert
        channel.CurrentCount.Should().Be(3);
        channel.IsSatisfied.Should().BeTrue();
    }

    [Fact]
    public void IsSatisfied_ReturnsTrueWhenExceedingRequiredCount()
    {
        // Arrange
        var channel = new BarrierChannel<int>("test", 2);

        // Act
        channel.Set(1);
        channel.Set(2);
        channel.Set(3);
        channel.Set(4);

        // Assert
        channel.CurrentCount.Should().Be(4);
        channel.IsSatisfied.Should().BeTrue();
    }

    #endregion

    #region Get Tests

    [Fact]
    public void Get_WhenBarrierNotSatisfied_ThrowsInvalidOperationException()
    {
        // Arrange
        var channel = new BarrierChannel<string>("test", 3);
        channel.Set("value1");
        channel.Set("value2");

        // Act
        var act = () => channel.Get<List<string>>();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Barrier not satisfied. Required: 3, Current: 2*");
    }

    [Fact]
    public void Get_WhenBarrierSatisfied_ReturnsAllCollectedValues()
    {
        // Arrange
        var channel = new BarrierChannel<string>("test", 3);
        channel.Set("a");
        channel.Set("b");
        channel.Set("c");

        // Act
        var result = channel.Get<List<string>>();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain("a");
        result.Should().Contain("b");
        result.Should().Contain("c");
    }

    [Fact]
    public void Get_ReturnsDefensiveCopy()
    {
        // Arrange
        var channel = new BarrierChannel<string>("test", 2);
        channel.Set("a");
        channel.Set("b");

        // Act
        var result1 = channel.Get<List<string>>();
        var result2 = channel.Get<List<string>>();

        // Assert - Different list instances
        result1.Should().NotBeSameAs(result2);
        result1.Should().BeEquivalentTo(result2);
    }

    [Fact]
    public void Get_WithWrongType_ThrowsInvalidCastException()
    {
        // Arrange
        var channel = new BarrierChannel<string>("test", 1);
        channel.Set("value");

        // Act
        var act = () => channel.Get<string>();

        // Assert
        act.Should().Throw<InvalidCastException>()
            .WithMessage("*Cannot convert barrier channel values to String*");
    }

    #endregion

    #region Update Tests

    [Fact]
    public void Update_AddsMultipleValuesAtOnce()
    {
        // Arrange
        var channel = new BarrierChannel<int>("test", 5);
        var values = new[] { 1, 2, 3 };

        // Act
        channel.Update(values);

        // Assert
        channel.CurrentCount.Should().Be(3);
        channel.IsSatisfied.Should().BeFalse();
    }

    [Fact]
    public void Update_IncrementsVersionOnce()
    {
        // Arrange
        var channel = new BarrierChannel<int>("test", 5);
        var initialVersion = channel.Version;

        // Act
        channel.Update(new[] { 1, 2, 3 });

        // Assert
        channel.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public void Update_CanSatisfyBarrier()
    {
        // Arrange
        var channel = new BarrierChannel<string>("test", 3);

        // Act
        channel.Update(new[] { "a", "b", "c" });

        // Assert
        channel.CurrentCount.Should().Be(3);
        channel.IsSatisfied.Should().BeTrue();
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void Reset_ClearsCollectedValues()
    {
        // Arrange
        var channel = new BarrierChannel<string>("test", 3);
        channel.Set("a");
        channel.Set("b");
        channel.Set("c");

        // Act
        channel.Reset();

        // Assert
        channel.CurrentCount.Should().Be(0);
        channel.IsSatisfied.Should().BeFalse();
    }

    [Fact]
    public void Reset_IncrementsVersion()
    {
        // Arrange
        var channel = new BarrierChannel<int>("test", 2);
        channel.Set(1);
        var versionAfterSet = channel.Version;

        // Act
        channel.Reset();

        // Assert
        channel.Version.Should().Be(versionAfterSet + 1);
    }

    [Fact]
    public void Reset_AllowsNewBarrierCycle()
    {
        // Arrange
        var channel = new BarrierChannel<int>("test", 2);
        channel.Set(1);
        channel.Set(2);
        channel.IsSatisfied.Should().BeTrue();

        // Act
        channel.Reset();
        channel.Set(10);
        channel.Set(20);

        // Assert
        var result = channel.Get<List<int>>();
        result.Should().Equal(10, 20);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void ConcurrentSet_DoesNotLoseValues()
    {
        // Arrange
        var channel = new BarrierChannel<int>("test", 100);
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var value = i;
            tasks.Add(Task.Run(() => channel.Set(value)));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        channel.CurrentCount.Should().Be(100);
        channel.IsSatisfied.Should().BeTrue();
        var result = channel.Get<List<int>>();
        result.Should().HaveCount(100);
    }

    #endregion
}
