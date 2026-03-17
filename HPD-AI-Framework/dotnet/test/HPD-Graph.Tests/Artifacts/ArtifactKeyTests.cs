using FluentAssertions;
using HPDAgent.Graph.Abstractions.Artifacts;
using Xunit;

namespace HPD.Graph.Tests.Artifacts;

/// <summary>
/// Tests for ArtifactKey - hierarchical artifact identifier.
/// </summary>
public class ArtifactKeyTests
{
    [Fact]
    public void ToString_SimpleKey_ReturnsPathWithSlashes()
    {
        // Arrange
        var key = new ArtifactKey { Path = new[] { "database", "users" } };

        // Act
        var result = key.ToString();

        // Assert
        result.Should().Be("database/users");
    }

    [Fact]
    public void ToString_WithPartition_ReturnsPathWithPartition()
    {
        // Arrange
        var key = new ArtifactKey
        {
            Path = new[] { "database", "users" },
            Partition = new PartitionKey { Dimensions = new[] { "2025-01-15" } }
        };

        // Act
        var result = key.ToString();

        // Assert
        result.Should().Be("database/users@2025-01-15");
    }

    [Fact]
    public void ToString_WithMultiDimensionalPartition_ReturnsPathWithPartition()
    {
        // Arrange
        var key = new ArtifactKey
        {
            Path = new[] { "warehouse", "dim_users" },
            Partition = new PartitionKey { Dimensions = new[] { "2025-01-15", "us-west" } }
        };

        // Act
        var result = key.ToString();

        // Assert
        result.Should().Be("warehouse/dim_users@2025-01-15|us-west");
    }

    [Fact]
    public void Parse_SimpleKey_ParsesCorrectly()
    {
        // Act
        var key = ArtifactKey.Parse("database/users");

        // Assert
        key.Path.Should().Equal("database", "users");
        key.Partition.Should().BeNull();
    }

    [Fact]
    public void Parse_KeyWithPartition_ParsesCorrectly()
    {
        // Act
        var key = ArtifactKey.Parse("database/users@2025-01-15");

        // Assert
        key.Path.Should().Equal("database", "users");
        key.Partition.Should().NotBeNull();
        key.Partition!.Dimensions.Should().Equal("2025-01-15");
    }

    [Fact]
    public void Parse_KeyWithMultiDimensionalPartition_ParsesCorrectly()
    {
        // Act
        var key = ArtifactKey.Parse("warehouse/dim@2025-01-15|us-west");

        // Assert
        key.Path.Should().Equal("warehouse", "dim");
        key.Partition.Should().NotBeNull();
        key.Partition!.Dimensions.Should().Equal("2025-01-15", "us-west");
    }

    [Fact]
    public void Parse_EmptyString_ThrowsArgumentException()
    {
        // Act
        var act = () => ArtifactKey.Parse("");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact]
    public void FromPath_CreatesKeyCorrectly()
    {
        // Act
        var key = ArtifactKey.FromPath("database", "users");

        // Assert
        key.Path.Should().Equal("database", "users");
        key.Partition.Should().BeNull();
    }

    [Fact]
    public void FromPath_WithPartition_CreatesKeyCorrectly()
    {
        // Arrange
        var partition = new PartitionKey { Dimensions = new[] { "2025-01-15" } };

        // Act
        var key = ArtifactKey.FromPath(new[] { "database", "users" }, partition);

        // Assert
        key.Path.Should().Equal("database", "users");
        key.Partition.Should().Be(partition);
    }

    [Fact]
    public void Equals_SamePathAndPartition_ReturnsTrue()
    {
        // Arrange
        var key1 = new ArtifactKey
        {
            Path = new[] { "database", "users" },
            Partition = new PartitionKey { Dimensions = new[] { "2025-01-15" } }
        };
        var key2 = new ArtifactKey
        {
            Path = new[] { "database", "users" },
            Partition = new PartitionKey { Dimensions = new[] { "2025-01-15" } }
        };

        // Act
        var result = key1.Equals(key2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentPath_ReturnsFalse()
    {
        // Arrange
        var key1 = new ArtifactKey { Path = new[] { "database", "users" } };
        var key2 = new ArtifactKey { Path = new[] { "database", "orders" } };

        // Act
        var result = key1.Equals(key2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentPartition_ReturnsFalse()
    {
        // Arrange
        var key1 = new ArtifactKey
        {
            Path = new[] { "database", "users" },
            Partition = new PartitionKey { Dimensions = new[] { "2025-01-15" } }
        };
        var key2 = new ArtifactKey
        {
            Path = new[] { "database", "users" },
            Partition = new PartitionKey { Dimensions = new[] { "2025-01-16" } }
        };

        // Act
        var result = key1.Equals(key2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SameKeys_ReturnsSameHashCode()
    {
        // Arrange
        var key1 = new ArtifactKey
        {
            Path = new[] { "database", "users" },
            Partition = new PartitionKey { Dimensions = new[] { "2025-01-15" } }
        };
        var key2 = new ArtifactKey
        {
            Path = new[] { "database", "users" },
            Partition = new PartitionKey { Dimensions = new[] { "2025-01-15" } }
        };

        // Act
        var hash1 = key1.GetHashCode();
        var hash2 = key2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void RoundTrip_ComplexKey_PreservesData()
    {
        // Arrange
        var original = ArtifactKey.Parse("warehouse/sales/daily@2025-01-15|us-west");

        // Act
        var serialized = original.ToString();
        var deserialized = ArtifactKey.Parse(serialized);

        // Assert
        deserialized.Should().Be(original);
        deserialized.Path.Should().Equal("warehouse", "sales", "daily");
        deserialized.Partition!.Dimensions.Should().Equal("2025-01-15", "us-west");
    }
}
