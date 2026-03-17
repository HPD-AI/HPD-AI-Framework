using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using HPD.VCS.Core;

namespace HPD.VCS.Tests;

public class OperationMetadataTests
{
    [Fact]
    public void Constructor_WithValidInputs_CreatesOperationMetadata()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);
        var description = "Test operation";
        var username = "testuser";
        var hostname = "testhost";
        var tags = new Dictionary<string, string> { { "type", "test" } };

        // Act
        var metadata = new OperationMetadata(startTime, endTime, description, username, hostname, tags);

        // Assert
        Assert.Equal(startTime, metadata.StartTime);
        Assert.Equal(endTime, metadata.EndTime);
        Assert.Equal(description, metadata.Description);
        Assert.Equal(username, metadata.Username);
        Assert.Equal(hostname, metadata.Hostname);
        Assert.Equal(tags, metadata.Tags);
    }

    [Fact]
    public void Constructor_WithNullDescription_ThrowsArgumentNullException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);
        var tags = new Dictionary<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new OperationMetadata(startTime, endTime, null!, "user", "host", tags));
    }

    [Fact]
    public void Constructor_WithTooLongDescription_ThrowsArgumentException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);
        var longDescription = new string('a', 1025); // Exceeds 1024 character limit
        var tags = new Dictionary<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            new OperationMetadata(startTime, endTime, longDescription, "user", "host", tags));
    }

    [Fact]
    public void GetBytesForHashing_WithSortedTags_ProducesCanonicalOutput()
    {
        // Arrange
        var startTime = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var endTime = new DateTimeOffset(2023, 1, 1, 0, 0, 10, TimeSpan.Zero);
        var tags = new Dictionary<string, string>
        {
            { "zebra", "last" },
            { "alpha", "first" },
            { "beta", "middle" }
        };

        var metadata = new OperationMetadata(startTime, endTime, "test", "user", "host", tags);

        // Act
        var bytes = metadata.GetBytesForHashing();
        var content = System.Text.Encoding.UTF8.GetString(bytes);

        // Assert - Tags should be sorted by key
        var lines = content.Split('\n');
        Assert.Contains("alpha", content);
        Assert.Contains("beta", content);
        Assert.Contains("zebra", content);
        
        // Check that alpha comes before beta which comes before zebra
        var alphaIndex = content.IndexOf("alpha");
        var betaIndex = content.IndexOf("beta");
        var zebraIndex = content.IndexOf("zebra");
        
        Assert.True(alphaIndex < betaIndex);
        Assert.True(betaIndex < zebraIndex);
    }

    [Fact]
    public void GetBytesForHashing_WithUnsortedTags_ProducesSameOutputAsSorted()
    {
        // Arrange
        var startTime = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var endTime = new DateTimeOffset(2023, 1, 1, 0, 0, 10, TimeSpan.Zero);
        
        var unsortedTags = new Dictionary<string, string>
        {
            { "zebra", "last" },
            { "alpha", "first" },
            { "beta", "middle" }
        };
        
        var sortedTags = new Dictionary<string, string>
        {
            { "alpha", "first" },
            { "beta", "middle" },
            { "zebra", "last" }
        };

        var metadata1 = new OperationMetadata(startTime, endTime, "test", "user", "host", unsortedTags);
        var metadata2 = new OperationMetadata(startTime, endTime, "test", "user", "host", sortedTags);

        // Act
        var bytes1 = metadata1.GetBytesForHashing();
        var bytes2 = metadata2.GetBytesForHashing();

        // Assert
        Assert.Equal(bytes1, bytes2);
    }    [Fact]
    public void GetBytesForHashing_UsesUnixTimeMilliseconds()
    {
        // Arrange
        var startTime = new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var endTime = new DateTimeOffset(2023, 1, 1, 12, 0, 30, TimeSpan.Zero);
        var metadata = new OperationMetadata(startTime, endTime, "test", "user", "host", new Dictionary<string, string>());

        // Act
        var bytes = metadata.GetBytesForHashing();
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        var lines = content.Split('\n');

        // Assert
        var expectedStartUnix = startTime.ToUnixTimeMilliseconds().ToString();
        var expectedEndUnix = endTime.ToUnixTimeMilliseconds().ToString();
        
        Assert.Equal(expectedStartUnix, lines[0]);
        Assert.Equal(expectedEndUnix, lines[1]);
    }

    [Fact]
    public void ParseFromCanonicalBytes_RoundTripConsistency()
    {
        // Arrange
        var startTime = new DateTimeOffset(2023, 6, 15, 14, 30, 45, TimeSpan.Zero);
        var endTime = startTime.AddSeconds(120);
        var tags = new Dictionary<string, string>
        {
            { "operation", "commit" },
            { "branch", "main" },
            { "user-id", "12345" }
        };
        
        var original = new OperationMetadata(startTime, endTime, "Test commit operation", "testuser", "testhost", tags);

        // Act
        var bytes = original.GetBytesForHashing();
        var parsed = OperationMetadata.ParseFromCanonicalBytes(bytes);

        // Assert
        Assert.Equal(original.StartTime, parsed.StartTime);
        Assert.Equal(original.EndTime, parsed.EndTime);
        Assert.Equal(original.Description, parsed.Description);
        Assert.Equal(original.Username, parsed.Username);
        Assert.Equal(original.Hostname, parsed.Hostname);
        Assert.Equal(original.Tags.Count, parsed.Tags.Count);
        Assert.All(original.Tags, kvp => Assert.Equal(kvp.Value, parsed.Tags[kvp.Key]));
    }

    [Fact]
    public void ParseFromCanonicalBytes_WithWindowsLineEndings_ParsesCorrectly()
    {
        // Arrange
        var metadata = new OperationMetadata(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(10),
            "test",
            "user", 
            "host",
            new Dictionary<string, string>());
            
        var bytes = metadata.GetBytesForHashing();
        var contentWithCRLF = System.Text.Encoding.UTF8.GetString(bytes).Replace("\n", "\r\n");
        var bytesWithCRLF = System.Text.Encoding.UTF8.GetBytes(contentWithCRLF);

        // Act
        var parsed = OperationMetadata.ParseFromCanonicalBytes(bytesWithCRLF);

        // Assert
        Assert.Equal(metadata.StartTime, parsed.StartTime);
        Assert.Equal(metadata.EndTime, parsed.EndTime);
        Assert.Equal(metadata.Description, parsed.Description);
        Assert.Equal(metadata.Username, parsed.Username);
        Assert.Equal(metadata.Hostname, parsed.Hostname);
    }

    [Fact]
    public void ParseFromCanonicalBytes_WithInvalidFormat_ThrowsArgumentException()
    {
        // Arrange
        var invalidBytes = System.Text.Encoding.UTF8.GetBytes("invalid format");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => OperationMetadata.ParseFromCanonicalBytes(invalidBytes));
    }

    [Fact]
    public void ParseFromCanonicalBytes_WithNullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => OperationMetadata.ParseFromCanonicalBytes(null!));
    }
}
