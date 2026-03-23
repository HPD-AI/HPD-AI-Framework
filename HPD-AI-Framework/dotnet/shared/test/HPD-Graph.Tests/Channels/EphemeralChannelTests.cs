using FluentAssertions;
using HPDAgent.Graph.Core.Channels;
using Xunit;

namespace HPD.Graph.Tests.Channels;

/// <summary>
/// Tests for EphemeralChannel temporary value storage.
/// </summary>
public class EphemeralChannelTests
{
    #region Basic Set/Get Tests

    [Fact]
    public void Set_AndGet_WorksCorrectly()
    {
        // Arrange
        var channel = new EphemeralChannel("test");

        // Act
        channel.Set("value");
        var result = channel.Get<string>();

        // Assert
        result.Should().Be("value");
        channel.HasValue.Should().BeTrue();
    }

    [Fact]
    public void Set_IntValue_StoresAndRetrievesCorrectly()
    {
        // Arrange
        var channel = new EphemeralChannel("test");

        // Act
        channel.Set(42);
        var result = channel.Get<int>();

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public void Set_ComplexObject_StoresCorrectly()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        var obj = new { Name = "Alice", Age = 30 };

        // Act
        channel.Set(obj);
        var result = channel.Get<object>();

        // Assert
        result.Should().Be(obj);
    }

    #endregion

    #region HasValue Tests

    [Fact]
    public void HasValue_ReturnsFalseInitially()
    {
        // Arrange
        var channel = new EphemeralChannel("test");

        // Act & Assert
        channel.HasValue.Should().BeFalse();
    }

    [Fact]
    public void HasValue_ReturnsTrueAfterSet()
    {
        // Arrange
        var channel = new EphemeralChannel("test");

        // Act
        channel.Set("value");

        // Assert
        channel.HasValue.Should().BeTrue();
    }

    [Fact]
    public void HasValue_ReturnsFalseAfterClear()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        channel.Set("value");

        // Act
        channel.Clear();

        // Assert
        channel.HasValue.Should().BeFalse();
    }

    #endregion

    #region Get Without Value Tests

    [Fact]
    public void Get_WhenNoValueSet_ReturnsDefault()
    {
        // Arrange
        var channel = new EphemeralChannel("test");

        // Act
        var result = channel.Get<string>();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Get_WhenNoValueSet_ReturnsDefaultForValueType()
    {
        // Arrange
        var channel = new EphemeralChannel("test");

        // Act
        var result = channel.Get<int>();

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_RemovesValue()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        channel.Set("value");

        // Act
        channel.Clear();

        // Assert
        channel.HasValue.Should().BeFalse();
        channel.Get<string>().Should().BeNull();
    }

    [Fact]
    public void Clear_IncrementsVersion()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        channel.Set("value");
        var versionAfterSet = channel.Version;

        // Act
        channel.Clear();

        // Assert
        channel.Version.Should().Be(versionAfterSet + 1);
    }

    #endregion

    #region Version Tests

    [Fact]
    public void Set_IncrementsVersion()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        var initialVersion = channel.Version;

        // Act
        channel.Set("value1");

        // Assert
        channel.Version.Should().Be(initialVersion + 1);
    }

    [Fact]
    public void MultipleOperations_IncrementVersionCorrectly()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        var initialVersion = channel.Version;

        // Act
        channel.Set("a");         // +1
        channel.Set("b");         // +1
        channel.Clear();          // +1
        channel.Set("c");         // +1

        // Assert
        channel.Version.Should().Be(initialVersion + 4);
    }

    #endregion

    #region Update Tests

    [Fact]
    public void Update_SetsToLastValue()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        var values = new[] { "first", "second", "third" };

        // Act
        channel.Update(values);

        // Assert
        channel.Get<string>().Should().Be("third");
        channel.HasValue.Should().BeTrue();
    }

    [Fact]
    public void Update_WithEmptyEnumerable_SetsToDefault()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        channel.Set("existing");

        // Act
        channel.Update(Enumerable.Empty<string>());

        // Assert
        channel.Get<string>().Should().BeNull();
        channel.HasValue.Should().BeTrue();
    }

    [Fact]
    public void Update_IncrementsVersion()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        var initialVersion = channel.Version;

        // Act
        channel.Update(new[] { 1, 2, 3 });

        // Assert
        channel.Version.Should().Be(initialVersion + 1);
    }

    #endregion

    #region Type Conversion Tests

    [Fact]
    public void Get_WithWrongType_ThrowsInvalidCastException()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        channel.Set("string value");

        // Act
        var act = () => channel.Get<int>();

        // Assert
        act.Should().Throw<InvalidCastException>()
            .WithMessage("*Cannot convert channel value*");
    }

    [Fact]
    public void Set_NullValue_WorksCorrectly()
    {
        // Arrange
        var channel = new EphemeralChannel("test");

        // Act
        channel.Set<string?>(null);

        // Assert
        channel.HasValue.Should().BeTrue();
        channel.Get<string?>().Should().BeNull();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void ConcurrentSetAndClear_DoesNotCorruptState()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        var tasks = new List<Task>();

        // Act - 50 sets, 50 clears interleaved
        for (int i = 0; i < 50; i++)
        {
            var value = i;
            tasks.Add(Task.Run(() => channel.Set(value)));
            tasks.Add(Task.Run(() => channel.Clear()));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - Should not throw and should be in valid state
        var hasValue = channel.HasValue;
        var version = channel.Version;

        // Version should have incremented (100 operations)
        version.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ConcurrentGet_AfterSet_ReturnsValue()
    {
        // Arrange
        var channel = new EphemeralChannel("test");
        channel.Set(42);
        var tasks = new List<Task<int>>();

        // Act - Multiple concurrent reads
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => channel.Get<int>()));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - All reads should return 42
        foreach (var task in tasks)
        {
            task.Result.Should().Be(42);
        }
    }

    #endregion
}
