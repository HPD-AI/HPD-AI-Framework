using FluentAssertions;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Core.Artifacts;
using Xunit;

#pragma warning disable IDE0005
using System.Linq;
#pragma warning restore IDE0005

namespace HPD.Graph.Tests.Artifacts;

/// <summary>
/// Tests for InMemoryArtifactRegistry - in-memory artifact metadata storage.
/// </summary>
public class InMemoryArtifactRegistryTests
{
    [Fact]
    public async Task RegisterAsync_NewArtifact_StoresMetadata()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");
        var version = "fingerprint123";
        var metadata = new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>(),
            ProducedByNodeId = "node1"
        };

        // Act
        await registry.RegisterAsync(key, version, metadata);

        // Assert
        var latestVersion = await registry.GetLatestVersionAsync(key);
        latestVersion.Should().Be(version);
    }

    [Fact]
    public async Task GetLatestVersionAsync_NoArtifact_ReturnsNull()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");

        // Act
        var result = await registry.GetLatestVersionAsync(key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMetadataAsync_ExistingVersion_ReturnsMetadata()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");
        var version = "fingerprint123";
        var metadata = new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>(),
            ProducedByNodeId = "node1",
            ExecutionId = "exec-123"
        };

        await registry.RegisterAsync(key, version, metadata);

        // Act
        var result = await registry.GetMetadataAsync(key, version);

        // Assert
        result.Should().NotBeNull();
        result!.ProducedByNodeId.Should().Be("node1");
        result.ExecutionId.Should().Be("exec-123");
    }

    [Fact]
    public async Task GetMetadataAsync_NonExistentVersion_ReturnsNull()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");

        // Act
        var result = await registry.GetMetadataAsync(key, "nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task RegisterAsync_MultipleVersions_UpdatesLatest()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");
        var version1 = "fingerprint1";
        var version2 = "fingerprint2";
        var metadata1 = new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            InputVersions = new Dictionary<ArtifactKey, string>()
        };
        var metadata2 = new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>()
        };

        // Act
        await registry.RegisterAsync(key, version1, metadata1);
        await registry.RegisterAsync(key, version2, metadata2);

        // Assert
        var latestVersion = await registry.GetLatestVersionAsync(key);
        latestVersion.Should().Be(version2);

        // Both versions should be retrievable
        (await registry.GetMetadataAsync(key, version1)).Should().NotBeNull();
        (await registry.GetMetadataAsync(key, version2)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetProducingNodeIdsAsync_SingleProducer_ReturnsNodeId()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");
        var metadata = new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>(),
            ProducedByNodeId = "node1"
        };

        await registry.RegisterAsync(key, "v1", metadata);

        // Act
        var producers = await registry.GetProducingNodeIdsAsync(key);

        // Assert
        producers.Should().ContainSingle()
            .Which.Should().Be("node1");
    }

    [Fact]
    public async Task GetProducingNodeIdsAsync_MultipleProducers_ThrowsException()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");

        await registry.RegisterAsync(key, "v1", new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>(),
            ProducedByNodeId = "node1"
        });

        await registry.RegisterAsync(key, "v2", new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>(),
            ProducedByNodeId = "node2"  // Different producer
        });

        // Act
        Func<Task> act = async () => await registry.GetProducingNodeIdsAsync(key);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Multiple nodes produce artifact*");
    }

    [Fact]
    public async Task GetProducingNodeIdsAsync_NoProducers_ReturnsEmpty()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");

        // Act
        var producers = await registry.GetProducingNodeIdsAsync(key);

        // Assert
        producers.Should().BeEmpty();
    }

    [Fact]
    public async Task ListArtifactsAsync_MultipleArtifacts_ReturnsAll()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key1 = ArtifactKey.FromPath("database", "users");
        var key2 = ArtifactKey.FromPath("database", "orders");
        var metadata = new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>()
        };

        await registry.RegisterAsync(key1, "v1", metadata);
        await registry.RegisterAsync(key2, "v1", metadata);

        // Act
        var artifacts = new List<ArtifactKey>();
        await foreach (var artifact in registry.ListArtifactsAsync())
        {
            artifacts.Add(artifact);
        }

        // Assert
        artifacts.Should().HaveCount(2);
        artifacts.Should().Contain(key1);
        artifacts.Should().Contain(key2);
    }

    [Fact]
    public async Task GetLineageAsync_WithInputVersions_ReturnsLineage()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("clean", "users");
        var inputKey = ArtifactKey.FromPath("raw", "users");
        var inputVersions = new Dictionary<ArtifactKey, string>
        {
            [inputKey] = "input-fingerprint-123"
        };
        var metadata = new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = inputVersions,
            ProducedByNodeId = "transform"
        };

        await registry.RegisterAsync(key, "v1", metadata);

        // Act
        var lineage = await registry.GetLineageAsync(key, "v1");

        // Assert
        lineage.Should().ContainKey(inputKey);
        lineage[inputKey].Should().Be("input-fingerprint-123");
    }

    [Fact]
    public async Task TryAcquireMaterializationLockAsync_FirstAcquire_Succeeds()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");

        // Act
        var lockHandle = await registry.TryAcquireMaterializationLockAsync(
            key, null, TimeSpan.FromSeconds(5));

        // Assert
        lockHandle.Should().NotBeNull();

        // Cleanup
        if (lockHandle != null)
            await lockHandle.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireMaterializationLockAsync_Concurrent_SecondFails()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");

        var lock1 = await registry.TryAcquireMaterializationLockAsync(
            key, null, TimeSpan.FromSeconds(5));

        try
        {
            // Act
            var lock2 = await registry.TryAcquireMaterializationLockAsync(
                key, null, TimeSpan.FromMilliseconds(100));

            // Assert
            lock2.Should().BeNull();
        }
        finally
        {
            if (lock1 != null)
                await lock1.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryAcquireMaterializationLockAsync_AfterRelease_Succeeds()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");

        var lock1 = await registry.TryAcquireMaterializationLockAsync(
            key, null, TimeSpan.FromSeconds(5));
        await lock1!.DisposeAsync();

        // Act
        var lock2 = await registry.TryAcquireMaterializationLockAsync(
            key, null, TimeSpan.FromSeconds(5));

        // Assert
        lock2.Should().NotBeNull();

        // Cleanup
        if (lock2 != null)
            await lock2.DisposeAsync();
    }

    [Fact]
    public async Task PruneOldVersionsAsync_KeepLast2_RemovesOldVersions()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");

        // Register 5 versions
        for (int i = 1; i <= 5; i++)
        {
            await registry.RegisterAsync(key, $"v{i}", new ArtifactMetadata
            {
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10 + i),
                InputVersions = new Dictionary<ArtifactKey, string>()
            });
        }

        // Act
        var prunedCount = await registry.PruneOldVersionsAsync(key, RetentionPolicy.KeepLast(2));

        // Assert
        prunedCount.Should().Be(3);

        // Latest should be v5
        var latestVersion = await registry.GetLatestVersionAsync(key);
        latestVersion.Should().Be("v5");

        // v5 and v4 should exist, v1-v3 should be gone
        (await registry.GetMetadataAsync(key, "v5")).Should().NotBeNull();
        (await registry.GetMetadataAsync(key, "v4")).Should().NotBeNull();
        (await registry.GetMetadataAsync(key, "v3")).Should().BeNull();
        (await registry.GetMetadataAsync(key, "v2")).Should().BeNull();
        (await registry.GetMetadataAsync(key, "v1")).Should().BeNull();
    }

    [Fact]
    public async Task PruneOldVersionsAsync_KeepRecent_RemovesOldVersions()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");

        // Register versions with different ages
        await registry.RegisterAsync(key, "v1", new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            InputVersions = new Dictionary<ArtifactKey, string>()
        });

        await registry.RegisterAsync(key, "v2", new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            InputVersions = new Dictionary<ArtifactKey, string>()
        });

        // Act
        var prunedCount = await registry.PruneOldVersionsAsync(
            key, RetentionPolicy.KeepRecent(TimeSpan.FromDays(1)));

        // Assert
        prunedCount.Should().Be(1);

        // v2 should exist, v1 should be gone
        (await registry.GetMetadataAsync(key, "v2")).Should().NotBeNull();
        (await registry.GetMetadataAsync(key, "v1")).Should().BeNull();
    }

    [Fact]
    public async Task ValidateConsistencyAsync_AllConsistent_ReturnsEmpty()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = ArtifactKey.FromPath("database", "users");

        await registry.RegisterAsync(key, "v1", new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>(),
            ProducedByNodeId = "node1"
        });

        // Act
        var orphaned = await registry.ValidateConsistencyAsync();

        // Assert
        orphaned.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterAsync_WithPartition_StoresCorrectly()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var key = new ArtifactKey
        {
            Path = new[] { "database", "users" },
            Partition = new PartitionKey { Dimensions = new[] { "2025-01-15" } }
        };
        var metadata = new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>()
        };

        // Act
        await registry.RegisterAsync(key, "v1", metadata);

        // Assert
        var latestVersion = await registry.GetLatestVersionAsync(key);
        latestVersion.Should().Be("v1");
    }

    [Fact]
    public async Task GetLatestVersionAsync_WithPartitionOverride_UsesOverride()
    {
        // Arrange
        var registry = new InMemoryArtifactRegistry();
        var baseKey = ArtifactKey.FromPath("database", "users");
        var partition1 = new PartitionKey { Dimensions = new[] { "2025-01-15" } };
        var partition2 = new PartitionKey { Dimensions = new[] { "2025-01-16" } };

        var key1 = new ArtifactKey { Path = baseKey.Path, Partition = partition1 };
        var key2 = new ArtifactKey { Path = baseKey.Path, Partition = partition2 };

        var metadata = new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>()
        };

        await registry.RegisterAsync(key1, "v1", metadata);
        await registry.RegisterAsync(key2, "v2", metadata);

        // Act - query with partition override
        var version1 = await registry.GetLatestVersionAsync(baseKey, partition1);
        var version2 = await registry.GetLatestVersionAsync(baseKey, partition2);

        // Assert
        version1.Should().Be("v1");
        version2.Should().Be("v2");
    }
}
