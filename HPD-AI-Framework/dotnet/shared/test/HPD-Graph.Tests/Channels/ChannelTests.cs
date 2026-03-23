using FluentAssertions;
using HPDAgent.Graph.Core.Channels;
using Xunit;

namespace HPD.Graph.Tests.Channels;

/// <summary>
/// Tests for all channel types (LastValue, Append, Reducer).
/// </summary>
public class ChannelTests
{
    #region LastValueChannel Tests

    [Fact]
    public void LastValueChannel_Set_ShouldStoreValue()
    {
        // Arrange
        var channel = new LastValueChannel("test");

        // Act
        channel.Set("hello");

        // Assert
        channel.Get<string>().Should().Be("hello");
    }

    [Fact]
    public void LastValueChannel_Set_ShouldOverwritePreviousValue()
    {
        // Arrange
        var channel = new LastValueChannel("test");
        channel.Set(10);

        // Act
        channel.Set(20);

        // Assert
        channel.Get<int>().Should().Be(20);
    }

    [Fact]
    public void LastValueChannel_Get_WithoutSet_ShouldReturnDefault()
    {
        // Arrange
        var channel = new LastValueChannel("test");

        // Act & Assert
        channel.Get<string>().Should().BeNull();
        channel.Get<int>().Should().Be(0);
    }

    [Fact]
    public void LastValueChannel_Version_ShouldIncrementOnSet()
    {
        // Arrange
        var channel = new LastValueChannel("test");
        var initialVersion = channel.Version;

        // Act
        channel.Set("value1");
        var version1 = channel.Version;
        channel.Set("value2");
        var version2 = channel.Version;

        // Assert
        version1.Should().BeGreaterThan(initialVersion);
        version2.Should().BeGreaterThan(version1);
    }

    #endregion

    #region AppendChannel Tests

    [Fact]
    public void AppendChannel_Set_ShouldAppendToList()
    {
        // Arrange
        var channel = new AppendChannel<string>("test");

        // Act
        channel.Set("first");
        channel.Set("second");
        channel.Set("third");

        // Assert
        var values = channel.Get<List<string>>();
        values.Should().HaveCount(3);
        values.Should().ContainInOrder("first", "second", "third");
    }

    [Fact]
    public void AppendChannel_Get_WithoutSet_ShouldReturnEmptyList()
    {
        // Arrange
        var channel = new AppendChannel<string>("test");

        // Act
        var values = channel.Get<List<string>>();

        // Assert
        values.Should().NotBeNull();
        values.Should().BeEmpty();
    }

    [Fact]
    public void AppendChannel_Set_ShouldMaintainOrder()
    {
        // Arrange
        var channel = new AppendChannel<int>("test");

        // Act
        for (int i = 1; i <= 5; i++)
        {
            channel.Set(i);
        }

        // Assert
        var values = channel.Get<List<int>>();
        values.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void AppendChannel_Update_ShouldAppendMultipleValues()
    {
        // Arrange
        var channel = new AppendChannel<int>("test");

        // Act
        channel.Update(new[] { 1, 2, 3 });
        channel.Update(new[] { 4, 5 });

        // Assert
        var values = channel.Get<List<int>>();
        values.Should().Equal(1, 2, 3, 4, 5);
    }

    #endregion

    #region ReducerChannel Tests

    [Fact]
    public void ReducerChannel_Set_WithSumReducer_ShouldAccumulate()
    {
        // Arrange
        var channel = new ReducerChannel<int>("test", (acc, val) => acc + val, 0);

        // Act
        channel.Set(10);
        channel.Set(20);
        channel.Set(30);

        // Assert
        channel.Get<int>().Should().Be(60);
    }

    [Fact]
    public void ReducerChannel_Set_WithProductReducer_ShouldMultiply()
    {
        // Arrange
        var channel = new ReducerChannel<int>("test", (acc, val) => acc * val, 1);

        // Act
        channel.Set(2);
        channel.Set(3);
        channel.Set(4);

        // Assert
        channel.Get<int>().Should().Be(24);
    }

    [Fact]
    public void ReducerChannel_Set_WithStringConcatenation_ShouldCombine()
    {
        // Arrange
        var channel = new ReducerChannel<string>("test", (acc, val) => acc + val, "");

        // Act
        channel.Set("Hello");
        channel.Set(" ");
        channel.Set("World");

        // Assert
        channel.Get<string>().Should().Be("Hello World");
    }

    [Fact]
    public void ReducerChannel_Get_WithoutSet_ShouldReturnInitialValue()
    {
        // Arrange
        var channel = new ReducerChannel<int>("test", (acc, val) => acc + val, 100);

        // Act & Assert
        channel.Get<int>().Should().Be(100);
    }

    [Fact]
    public void ReducerChannel_Update_ShouldReduceMultipleValues()
    {
        // Arrange
        var channel = new ReducerChannel<int>("test", (acc, val) => acc + val, 0);

        // Act
        channel.Update(new[] { 1, 2, 3, 4, 5 });

        // Assert
        channel.Get<int>().Should().Be(15);
    }

    #endregion

    #region Type Safety Tests

    [Fact]
    public void LastValueChannel_Get_WrongType_ShouldThrow()
    {
        // Arrange
        var channel = new LastValueChannel("test");
        channel.Set("string value");

        // Act & Assert
        var act = () => channel.Get<int>();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AppendChannel_Get_WrongType_ShouldThrow()
    {
        // Arrange
        var channel = new AppendChannel<string>("test");
        channel.Set("value");

        // Act & Assert
        var act = () => channel.Get<List<int>>();
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion
}
