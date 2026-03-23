using FluentAssertions;
using HPDAgent.Graph.Abstractions.Artifacts;
using Xunit;

namespace HPD.Graph.Tests.Artifacts;

/// <summary>
/// Tests for PartitionKey - multi-dimensional partition identifier.
/// </summary>
public class PartitionKeyTests
{
    [Fact]
    public void ToString_SingleDimension_ReturnsDimension()
    {
        // Arrange
        var key = new PartitionKey { Dimensions = new[] { "2025-01-15" } };

        // Act
        var result = key.ToString();

        // Assert
        result.Should().Be("2025-01-15");
    }

    [Fact]
    public void ToString_MultiDimension_ReturnsPipeSeparated()
    {
        // Arrange
        var key = new PartitionKey { Dimensions = new[] { "2025-01-15", "us-west", "production" } };

        // Act
        var result = key.ToString();

        // Assert
        result.Should().Be("2025-01-15|us-west|production");
    }

    [Fact]
    public void Parse_SingleDimension_ParsesCorrectly()
    {
        // Act
        var key = PartitionKey.Parse("2025-01-15");

        // Assert
        key.Dimensions.Should().Equal("2025-01-15");
    }

    [Fact]
    public void Parse_MultiDimension_ParsesCorrectly()
    {
        // Act
        var key = PartitionKey.Parse("2025-01-15|us-west|production");

        // Assert
        key.Dimensions.Should().Equal("2025-01-15", "us-west", "production");
    }

    [Fact]
    public void Parse_EmptyString_ThrowsArgumentException()
    {
        // Act
        var act = () => PartitionKey.Parse("");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact]
    public void ImplicitConversion_String_CreatesPartitionKey()
    {
        // Act
        PartitionKey key = "2025-01-15";

        // Assert
        key.Dimensions.Should().Equal("2025-01-15");
    }

    [Fact]
    public void Equals_SameDimensions_ReturnsTrue()
    {
        // Arrange
        var key1 = new PartitionKey { Dimensions = new[] { "2025-01-15", "us-west" } };
        var key2 = new PartitionKey { Dimensions = new[] { "2025-01-15", "us-west" } };

        // Act
        var result = key1.Equals(key2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentDimensions_ReturnsFalse()
    {
        // Arrange
        var key1 = new PartitionKey { Dimensions = new[] { "2025-01-15", "us-west" } };
        var key2 = new PartitionKey { Dimensions = new[] { "2025-01-15", "us-east" } };

        // Act
        var result = key1.Equals(key2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentDimensionCount_ReturnsFalse()
    {
        // Arrange
        var key1 = new PartitionKey { Dimensions = new[] { "2025-01-15" } };
        var key2 = new PartitionKey { Dimensions = new[] { "2025-01-15", "us-west" } };

        // Act
        var result = key1.Equals(key2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SameKeys_ReturnsSameHashCode()
    {
        // Arrange
        var key1 = new PartitionKey { Dimensions = new[] { "2025-01-15", "us-west" } };
        var key2 = new PartitionKey { Dimensions = new[] { "2025-01-15", "us-west" } };

        // Act
        var hash1 = key1.GetHashCode();
        var hash2 = key2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void RoundTrip_MultiDimensional_PreservesData()
    {
        // Arrange
        var original = PartitionKey.Parse("2025-01-15|us-west|production");

        // Act
        var serialized = original.ToString();
        var deserialized = PartitionKey.Parse(serialized);

        // Assert
        deserialized.Should().Be(original);
        deserialized.Dimensions.Should().Equal("2025-01-15", "us-west", "production");
    }
}
