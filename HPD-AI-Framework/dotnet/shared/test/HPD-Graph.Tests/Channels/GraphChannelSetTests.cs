using FluentAssertions;
using HPDAgent.Graph.Abstractions.Channels;
using HPDAgent.Graph.Core.Channels;
using Xunit;

namespace HPD.Graph.Tests.Channels;

/// <summary>
/// Tests for GraphChannelSet thread-safe channel management.
/// </summary>
public class GraphChannelSetTests
{
    #region Indexer Tests

    [Fact]
    public void Indexer_CreatesLastValueChannelByDefault()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var channel = channelSet["test"];

        // Assert
        channel.Should().NotBeNull();
        channel.Should().BeOfType<LastValueChannel>();
        channel.Name.Should().Be("test");
    }

    [Fact]
    public void Indexer_GetSameChannelTwice_ReturnsSameInstance()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var channel1 = channelSet["test"];
        var channel2 = channelSet["test"];

        // Assert
        channel1.Should().BeSameAs(channel2);
    }

    [Fact]
    public void Indexer_WithNullName_ThrowsArgumentException()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var act = () => channelSet[null!];

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Channel name cannot be null or whitespace*");
    }

    [Fact]
    public void Indexer_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var act = () => channelSet[""];

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Channel name cannot be null or whitespace*");
    }

    [Fact]
    public void Indexer_WithWhitespaceName_ThrowsArgumentException()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var act = () => channelSet["   "];

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Channel name cannot be null or whitespace*");
    }

    #endregion

    #region Add Tests

    [Fact]
    public void Add_WithCustomChannel_StoresChannel()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var customChannel = new AppendChannel<string>("custom");

        // Act
        channelSet.Add(customChannel);

        // Assert
        var retrieved = channelSet["custom"];
        retrieved.Should().BeSameAs(customChannel);
        retrieved.Should().BeOfType<AppendChannel<string>>();
    }

    [Fact]
    public void Add_WithDuplicateName_ThrowsArgumentException()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var channel1 = new LastValueChannel("duplicate");
        var channel2 = new LastValueChannel("duplicate");

        // Act
        channelSet.Add(channel1);
        var act = () => channelSet.Add(channel2);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Channel with name 'duplicate' already exists*");
    }

    [Fact]
    public void Add_WithNullChannel_ThrowsArgumentNullException()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var act = () => channelSet.Add(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Contains Tests

    [Fact]
    public void Contains_ExistingChannel_ReturnsTrue()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        _ = channelSet["test"]; // Create channel via indexer

        // Act
        var result = channelSet.Contains("test");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Contains_NonExistentChannel_ReturnsFalse()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var result = channelSet.Contains("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Contains_WithNullName_ReturnsFalse()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var result = channelSet.Contains(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Contains_WithEmptyName_ReturnsFalse()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var result = channelSet.Contains("");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Remove Tests

    [Fact]
    public void Remove_ExistingChannel_ReturnsTrueAndRemovesChannel()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        _ = channelSet["test"];

        // Act
        var result = channelSet.Remove("test");

        // Assert
        result.Should().BeTrue();
        channelSet.Contains("test").Should().BeFalse();
    }

    [Fact]
    public void Remove_NonExistentChannel_ReturnsFalse()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var result = channelSet.Remove("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Remove_WithNullName_ReturnsFalse()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var result = channelSet.Remove(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Remove_AfterRemoval_CanCreateNewChannelWithSameName()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var channel1 = channelSet["test"];
        channel1.Set("value1");

        // Act
        channelSet.Remove("test");
        var channel2 = channelSet["test"];

        // Assert
        channel2.Should().NotBeSameAs(channel1);
        // New channel should be empty
        channel2.Get<string>().Should().BeNull();
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_RemovesAllChannels()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        _ = channelSet["channel1"];
        _ = channelSet["channel2"];
        _ = channelSet["channel3"];

        // Act
        channelSet.Clear();

        // Assert
        channelSet.Contains("channel1").Should().BeFalse();
        channelSet.Contains("channel2").Should().BeFalse();
        channelSet.Contains("channel3").Should().BeFalse();
        channelSet.ChannelNames.Should().BeEmpty();
    }

    #endregion

    #region ChannelNames Tests

    [Fact]
    public void ChannelNames_ReturnsSortedList()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        _ = channelSet["zebra"];
        _ = channelSet["alpha"];
        _ = channelSet["beta"];

        // Act
        var names = channelSet.ChannelNames;

        // Assert
        names.Should().HaveCount(3);
        names.Should().BeInAscendingOrder();
        names.Should().Equal("alpha", "beta", "zebra");
    }

    [Fact]
    public void ChannelNames_EmptySet_ReturnsEmptyList()
    {
        // Arrange
        var channelSet = new GraphChannelSet();

        // Act
        var names = channelSet.ChannelNames;

        // Assert
        names.Should().BeEmpty();
    }

    [Fact]
    public void ChannelNames_AfterRemoval_UpdatesCorrectly()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        _ = channelSet["a"];
        _ = channelSet["b"];
        _ = channelSet["c"];

        // Act
        channelSet.Remove("b");
        var names = channelSet.ChannelNames;

        // Assert
        names.Should().HaveCount(2);
        names.Should().Equal("a", "c");
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void ConcurrentAccess_DifferentChannels_ThreadSafe()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var tasks = new List<Task>();

        // Act - Create 100 different channels concurrently
        for (int i = 0; i < 100; i++)
        {
            var index = i; // Capture for closure
            var channelName = $"channel_{index}";
            tasks.Add(Task.Run(() =>
            {
                var channel = channelSet[channelName];
                channel.Set(index);
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        channelSet.ChannelNames.Should().HaveCount(100);
        for (int i = 0; i < 100; i++)
        {
            var channel = channelSet[$"channel_{i}"];
            // Each channel should have its index value stored
            var value = channel.Get<int>();
            value.Should().Be(i, $"channel_{i} should have value {i}");
        }
    }

    [Fact]
    public void ConcurrentAddAndRemove_ThreadSafe()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var tasks = new List<Task>();

        // Act - Concurrent add and remove operations
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var channel = new LastValueChannel($"temp_{index}");
                channelSet.Add(channel);
            }));

            tasks.Add(Task.Run(() =>
            {
                channelSet.Remove($"temp_{index}");
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - Should not throw and should be in valid state
        var count = channelSet.ChannelNames.Count;
        count.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(50);
    }

    #endregion
}
