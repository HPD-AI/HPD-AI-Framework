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

    #region Branch Mapping

    [Fact]
    public void ToDto_MapsBranchCorrectly_WithAllProperties()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-123");
        var mainBranch = session.CreateBranch("main");
        mainBranch.Description = "Main Branch - Primary conversation";

        // Add some messages to test message count
        mainBranch.AddMessage(new ChatMessage(ChatRole.User, "Hello"));
        mainBranch.AddMessage(new ChatMessage(ChatRole.Assistant, "Hi there!"));

        // Act
        var dto = mainBranch.ToDto("session-123");

        // Assert
        dto.Id.Should().Be("main");
        dto.SessionId.Should().Be("session-123");
        dto.Description.Should().Be("Main Branch - Primary conversation");
        dto.MessageCount.Should().Be(2);
        dto.CreatedAt.Should().BeCloseTo(mainBranch.CreatedAt, TimeSpan.FromMilliseconds(100));
        dto.LastActivity.Should().BeCloseTo(mainBranch.LastActivity, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void ToDto_MapsBranchCorrectly_WithNullOptionalFields()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-123");
        var branch = session.CreateBranch("branch-1");

        // Act
        var dto = branch.ToDto("session-123");

        // Assert
        dto.Description.Should().BeNull();
        dto.ForkedFrom.Should().BeNull();
        dto.ForkedAtMessageIndex.Should().BeNull();
    }

    [Fact]
    public void ToDto_IncludesSessionId_InBranchDto()
    {
        // Arrange
        var session = new HPD.Agent.Session("my-session");
        var branch = session.CreateBranch("branch-1");

        // Act
        var dto = branch.ToDto("my-session");

        // Assert
        dto.SessionId.Should().Be("my-session");
    }

    [Fact]
    public void ToDto_MapsForkedBranch_Correctly()
    {
        // Arrange - Create a forked branch manually since Fork is on Agent, not Branch
        var forkedBranch = new HPD.Agent.Branch(
            id: "forked",
            sessionId: "session-123",
            messages: new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "Message 1"),
                new ChatMessage(ChatRole.Assistant, "Response 1")
            },
            forkedFrom: "main",
            forkedAtMessageIndex: 1,
            createdAt: DateTime.UtcNow,
            lastActivity: DateTime.UtcNow,
            name: "Forked Branch",
            description: null,
            tags: null,
            ancestors: new Dictionary<string, string> { ["0"] = "root", ["1"] = "main" },
            middlewareState: new Dictionary<string, string>());

        // Act
        var dto = forkedBranch.ToDto("session-123");

        // Assert
        dto.ForkedFrom.Should().Be("main");
        dto.ForkedAtMessageIndex.Should().Be(1);
        dto.Ancestors.Should().ContainKey("0");
        dto.Ancestors.Should().ContainKey("1");
    }

    #endregion

    #region Message Mapping

    [Fact]
    public void ToDto_MapsMessageCorrectly_WithAllRoles()
    {
        // Arrange
        var userMessage = new ChatMessage(ChatRole.User, "User message");
        var assistantMessage = new ChatMessage(ChatRole.Assistant, "Assistant message");
        var timestamp = DateTime.UtcNow;

        // Act
        var userDto = userMessage.ToDto(0, timestamp);
        var assistantDto = assistantMessage.ToDto(1, timestamp);

        // Assert
        userDto.Id.Should().Be("msg-0");
        userDto.Role.Should().Be("user");
        userDto.Contents.Should().ContainSingle();
        userDto.Contents.OfType<Microsoft.Extensions.AI.TextContent>().Single().Text.Should().Be("User message");

        assistantDto.Id.Should().Be("msg-1");
        assistantDto.Role.Should().Be("assistant");
        assistantDto.Contents.Should().ContainSingle();
        assistantDto.Contents.OfType<Microsoft.Extensions.AI.TextContent>().Single().Text.Should().Be("Assistant message");
    }

    [Fact]
    public void ToDto_IncludesMessageIndex_InDto()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.User, "Test");
        var timestamp = DateTime.UtcNow;

        // Act
        var dto = message.ToDto(42, timestamp);

        // Assert
        dto.Id.Should().Be("msg-42");
    }

    [Fact]
    public void ToDto_FormatsTimestamp_AsISO8601()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.User, "Test");
        var timestamp = new DateTime(2026, 2, 15, 10, 30, 45, DateTimeKind.Utc);

        // Act
        var dto = message.ToDto(0, timestamp);

        // Assert
        dto.Timestamp.Should().Be("2026-02-15T10:30:45.0000000Z");
        DateTime.TryParse(dto.Timestamp, out var parsed).Should().BeTrue();
    }

    #endregion

    #region Asset Mapping

    [Fact]
    public void ToDto_MapsAssetCorrectly_WithAllProperties()
    {
        // Arrange
        var metadata = new HPD.Agent.ContentInfo
        {
            Id = "asset-123",
            Name = "asset-123",
            ContentType = "image/png",
            SizeBytes = 1024000,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var dto = metadata.ToDto();

        // Assert
        dto.AssetId.Should().Be("asset-123");
        dto.ContentType.Should().Be("image/png");
        dto.SizeBytes.Should().Be(1024000);
        DateTime.TryParse(dto.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var createdAt).Should().BeTrue();
        createdAt.ToUniversalTime().Should().BeCloseTo(metadata.CreatedAt, TimeSpan.FromSeconds(1));
    }

    #endregion
}
