using FluentAssertions;
using HPD.Agent.Hosting.Extensions;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Tests.Extensions;

/// <summary>
/// Tests for DTO mapping extension methods.
/// Ensures proper conversion between domain objects and DTOs.
/// </summary>
public class DtoMappingExtensionsTests
{
    #region Session Mapping

    [Fact]
    public void ToDto_MapsSessionCorrectly_WithMetadata()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-123");
        session.AddMetadata("key1", "value1");
        session.AddMetadata("key2", 42);

        // Act
        var dto = session.ToDto();

        // Assert
        dto.Id.Should().Be("session-123");
        dto.CreatedAt.Should().BeCloseTo(session.CreatedAt, TimeSpan.FromMilliseconds(100));
        dto.LastActivity.Should().BeCloseTo(session.LastActivity, TimeSpan.FromMilliseconds(100));
        dto.Metadata.Should().NotBeNull();
        dto.Metadata!.Count.Should().Be(2);
        dto.Metadata.Should().ContainKey("key1");
        dto.Metadata.Should().ContainKey("key2");
    }

    [Fact]
    public void ToDto_MapsSessionCorrectly_WithEmptyMetadata()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-123");

        // Act
        var dto = session.ToDto();

        // Assert
        dto.Id.Should().Be("session-123");
        dto.Metadata.Should().BeNull(); // Empty metadata should map to null
    }

    [Fact]
    public void ToDto_MapsSessionCorrectly_WithNullMetadata()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-123");

        // Act
        var dto = session.ToDto();

        // Assert
        dto.Metadata.Should().BeNull();
    }

    #endregion

    #region Thread Mapping

    [Fact]
    public void ToDto_MapsThreadCorrectly_WithAllProperties()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-123");
        var mainThread = session.CreateThread("test-agent", "main");
        mainThread.Description = "Main Thread - Primary conversation";

        // Add some messages to test message count
        mainThread.AddMessage(new ChatMessage(ChatRole.User, "Hello"));
        mainThread.AddMessage(new ChatMessage(ChatRole.Assistant, "Hi there!"));

        // Act
        var dto = mainThread.ToDto("session-123");

        // Assert
        dto.Id.Should().Be("main");
        dto.SessionId.Should().Be("session-123");
        dto.Description.Should().Be("Main Thread - Primary conversation");
        dto.MessageCount.Should().Be(2);
        dto.CreatedAt.Should().BeCloseTo(mainThread.CreatedAt, TimeSpan.FromMilliseconds(100));
        dto.LastActivity.Should().BeCloseTo(mainThread.LastActivity, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void ToDto_MapsThreadCorrectly_WithNullOptionalFields()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-123");
        var thread = session.CreateThread("test-agent", "thread-1");

        // Act
        var dto = thread.ToDto("session-123");

        // Assert
        dto.Description.Should().BeNull();
        dto.ForkedFrom.Should().BeNull();
        dto.ForkedAtMessageIndex.Should().BeNull();
    }

    [Fact]
    public void ToDto_IncludesSessionId_InThreadDto()
    {
        // Arrange
        var session = new HPD.Agent.Session("my-session");
        var thread = session.CreateThread("test-agent", "thread-1");

        // Act
        var dto = thread.ToDto("my-session");

        // Assert
        dto.SessionId.Should().Be("my-session");
    }

    [Fact]
    public void ToDto_MapsForkedThread_Correctly()
    {
        // Arrange - Create a forked thread manually since Fork is on Agent, not Thread
        var forkedThread = new HPD.Agent.Thread(
            id: "forked",
            sessionId: "session-123",
            messages: new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "Message 1"),
                new ChatMessage(ChatRole.Assistant, "Response 1")
            },
            forkedFrom: "main",
            forkedAtMessageId: "message-1",
            forkedAtMessageIndex: 1,
            createdAt: DateTime.UtcNow,
            lastActivity: DateTime.UtcNow,
            name: "Forked Thread",
            description: null,
            tags: null,
            ancestors: new Dictionary<string, string> { ["0"] = "root", ["1"] = "main" },
            middlewareState: new Dictionary<string, string>(),
            metadata: new Dictionary<string, object> { ["surface"] = "hpdos" },
            childThreads: [],
            defaultAgentId: "agent-1");

        // Act
        var dto = forkedThread.ToDto("session-123");

        // Assert
        dto.ForkedFrom.Should().Be("main");
        dto.ForkedAtMessageId.Should().Be("message-1");
        dto.ForkedAtMessageIndex.Should().Be(1);
        dto.Ancestors.Should().ContainKey("0");
        dto.Ancestors.Should().ContainKey("1");
        dto.Metadata.Should().ContainKey("surface");
    }

    #endregion

    #region Content Mapping

    [Fact]
    public void ToDto_MapsContentCorrectly_WithAllProperties()
    {
        // Arrange
        var metadata = new HPD.Agent.ContentInfo
        {
            Id = "content-123",
            Version = "rev:123",
            Name = "content-123",
            ContentType = "image/png",
            SizeBytes = 1024000,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var dto = metadata.ToDto();

        // Assert
        dto.ContentId.Should().Be("content-123");
        dto.ContentType.Should().Be("image/png");
        dto.SizeBytes.Should().Be(1024000);
        DateTime.TryParse(dto.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var createdAt).Should().BeTrue();
        createdAt.ToUniversalTime().Should().BeCloseTo(metadata.CreatedAt, TimeSpan.FromSeconds(1));
    }

    #endregion
}
