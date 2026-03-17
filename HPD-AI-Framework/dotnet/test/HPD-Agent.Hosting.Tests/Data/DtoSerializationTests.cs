using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Serialization;

namespace HPD.Agent.Hosting.Tests.Data;

/// <summary>
/// Tests for all DTOs to ensure proper JSON serialization/deserialization.
/// Critical for Native AOT compatibility and cross-platform type safety.
/// </summary>
public class DtoSerializationTests
{
    private readonly JsonSerializerOptions _options;

    public DtoSerializationTests()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        _options.TypeInfoResolverChain.Add(HPDAgentApiJsonSerializerContext.Default);
    }

    #region Serialization Round-Trip Tests

    [Fact]
    public void SessionDto_SerializesAndDeserializes_WithAllProperties()
    {
        // Arrange
        var original = new SessionDto(
            "session-123",
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 42,
                ["key3"] = true
            });

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<SessionDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(original.Id);
        deserialized.CreatedAt.Should().BeCloseTo(original.CreatedAt, TimeSpan.FromMilliseconds(1));
        deserialized.LastActivity.Should().BeCloseTo(original.LastActivity, TimeSpan.FromMilliseconds(1));
        deserialized.Metadata.Should().NotBeNull();
        deserialized.Metadata!.Count.Should().Be(3);
    }

    [Fact]
    public void SessionDto_SerializesAndDeserializes_WithNullMetadata()
    {
        // Arrange
        var original = new SessionDto(
            "session-123",
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<SessionDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Metadata.Should().BeNull();
    }

    [Fact]
    public void BranchDto_SerializesAndDeserializes_WithAllProperties()
    {
        // Arrange
        var original = new BranchDto(
            "branch-1",
            "session-123",
            "Main Branch",
            "Primary conversation branch",
            "parent-branch",
            5,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(10),
            25,
            new List<string> { "tag1", "tag2" },
            new Dictionary<string, string> { ["0"] = "root", ["1"] = "parent-branch" },
            0, 1, true, null, null, null, 0);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<BranchDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(original.Id);
        deserialized.Id.Should().Be(original.Id);
        deserialized.Name.Should().Be(original.Name);
        deserialized.Description.Should().Be(original.Description);
        deserialized.ForkedFrom.Should().Be(original.ForkedFrom);
        deserialized.ForkedAtMessageIndex.Should().Be(original.ForkedAtMessageIndex);
        deserialized.MessageCount.Should().Be(original.MessageCount);
        deserialized.Tags.Should().BeEquivalentTo(original.Tags);
        deserialized.Ancestors.Should().BeEquivalentTo(original.Ancestors);
    }

    [Fact]
    public void BranchDto_SerializesAndDeserializes_WithNullOptionalFields()
    {
        // Arrange
        var original = new BranchDto(
            "branch-1",
            "session-123",
            "Main",
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            0,
            new List<string>(),
            new Dictionary<string, string>(),
            0, 1, true, null, null, null, 0);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<BranchDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Description.Should().BeNull();
        deserialized.ForkedFrom.Should().BeNull();
        deserialized.ForkedAtMessageIndex.Should().BeNull();
    }

    [Fact]
    public void MessageDto_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new MessageDto(
            "msg-1",
            "user",
            new List<Microsoft.Extensions.AI.AIContent> { new Microsoft.Extensions.AI.TextContent("Hello, how can I help?") },
            null,
            DateTime.UtcNow.ToString("O"));

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<MessageDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(original.Id);
        deserialized.Role.Should().Be(original.Role);
        deserialized.Contents.Should().HaveCount(1);
        deserialized.Timestamp.Should().Be(original.Timestamp);
    }

    [Fact]
    public void AssetDto_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new AssetDto(
            "asset-123",
            "image/png",
            1024000,
            DateTime.UtcNow.ToString("O"));

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<AssetDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.AssetId.Should().Be(original.AssetId);
        deserialized.ContentType.Should().Be(original.ContentType);
        deserialized.SizeBytes.Should().Be(original.SizeBytes);
        deserialized.CreatedAt.Should().Be(original.CreatedAt);
    }

    [Fact]
    public void CreateSessionRequest_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new CreateSessionRequest(
            "custom-session-id",
            new Dictionary<string, object> { ["project"] = "test" });

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<CreateSessionRequest>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.SessionId.Should().Be(original.SessionId);
        deserialized.Metadata.Should().NotBeNull();
    }

    [Fact]
    public void UpdateSessionRequest_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new UpdateSessionRequest(
            new Dictionary<string, object?> { ["name"] = "Updated", ["archived"] = null });

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<UpdateSessionRequest>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Metadata.Should().NotBeNull();
        deserialized.Metadata!.Count.Should().Be(2);
    }

    [Fact]
    public void SearchSessionsRequest_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new SearchSessionsRequest(
            new Dictionary<string, object> { ["project"] = "acme" },
            10,
            50);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<SearchSessionsRequest>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Offset.Should().Be(original.Offset);
        deserialized.Limit.Should().Be(original.Limit);
    }

    [Fact]
    public void CreateBranchRequest_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new CreateBranchRequest(
            "new-branch",
            "New Branch",
            "Branch description",
            null);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<CreateBranchRequest>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.BranchId.Should().Be(original.BranchId);
        deserialized.Name.Should().Be(original.Name);
        deserialized.Description.Should().Be(original.Description);
    }

    [Fact]
    public void ForkBranchRequest_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new ForkBranchRequest(
            "forked-branch",
            5,
            "Forked Branch",
            "Fork description",
            null);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<ForkBranchRequest>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.NewBranchId.Should().Be(original.NewBranchId);
        deserialized.FromMessageIndex.Should().Be(original.FromMessageIndex);
        deserialized.Name.Should().Be(original.Name);
    }

    [Fact]
    public void StreamRequest_SerializesAndDeserializes_WithAllFields()
    {
        // Arrange
        var original = new StreamRequest(
            new List<StreamMessage> { new StreamMessage("Hello", "user") },
            new List<System.Text.Json.JsonElement>(),
            new List<System.Text.Json.JsonElement>(),
            null,
            new List<string>(),
            new List<string>(),
            false,
            new StreamRunConfigDto(
                new ChatRunConfigDto(0.7, 4000, null, null, null),
                "anthropic",
                "claude-sonnet-4-5",
                "Be concise",
                new Dictionary<string, object> { ["key"] = "value" },
                new Dictionary<string, bool> { ["file_write"] = true },
                true,
                false,
                TimeSpan.FromMinutes(5).ToString()));

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<StreamRequest>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Messages.Should().HaveCount(1);
        deserialized.RunConfig.Should().NotBeNull();
        deserialized.RunConfig!.Chat.Should().NotBeNull();
        deserialized.RunConfig.Chat!.Temperature.Should().Be(0.7);
        deserialized.RunConfig.ModelId.Should().Be("claude-sonnet-4-5");
    }

    [Fact]
    public void StreamMessage_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new StreamMessage("Hello, world!", "user");

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<StreamMessage>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Content.Should().Be(original.Content);
        deserialized.Role.Should().Be(original.Role);
    }

    [Fact]
    public void StreamRunConfigDto_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new StreamRunConfigDto(
            new ChatRunConfigDto(0.8, 2000, null, null, null),
            "openai",
            "gpt-4",
            "System instructions",
            null,
            null,
            false,
            true,
            TimeSpan.FromMinutes(10).ToString());

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<StreamRunConfigDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.ProviderKey.Should().Be(original.ProviderKey);
        deserialized.ModelId.Should().Be(original.ModelId);
        deserialized.RunTimeout.Should().Be(original.RunTimeout);
    }

    [Fact]
    public void ChatRunConfigDto_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new ChatRunConfigDto(0.9, 1000, null, null, null);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<ChatRunConfigDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Temperature.Should().Be(original.Temperature);
        deserialized.MaxOutputTokens.Should().Be(original.MaxOutputTokens);
    }

    [Fact]
    public void PermissionResponseRequest_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new PermissionResponseRequest(
            null,
            "perm-123",
            true,
            "Approved for testing",
            "option-1");

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<PermissionResponseRequest>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.PermissionId.Should().Be(original.PermissionId);
        deserialized.Approved.Should().Be(original.Approved);
        deserialized.Reason.Should().Be(original.Reason);
        deserialized.Choice.Should().Be(original.Choice);
    }

    [Fact]
    public void ClientToolResponseRequest_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new ClientToolResponseRequest(
            null,
            "tool-req-123",
            true,
            new List<ClientToolContentDto> { new ClientToolContentDto("text", "Result data", null, null) },
            null);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<ClientToolResponseRequest>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.RequestId.Should().Be(original.RequestId);
        deserialized.Success.Should().Be(original.Success);
        deserialized.Content.Should().HaveCount(1);
        deserialized.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ClientToolContentDto_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new ClientToolContentDto("text", "Content value", null, null);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<ClientToolContentDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be(original.Type);
        deserialized.Text.Should().Be(original.Text);
    }

    #endregion

    #region JSON Naming Conventions

    [Fact]
    public void AllDtos_UseCamelCase_InJsonOutput()
    {
        // Arrange
        var sessionDto = new SessionDto("s1", DateTime.UtcNow, DateTime.UtcNow, null);

        // Act
        var json = JsonSerializer.Serialize(sessionDto, _options);

        // Assert
        json.Should().Contain("\"id\""); // camelCase, not Id
        json.Should().Contain("\"createdAt\"");
        json.Should().Contain("\"lastActivity\"");
    }

    [Fact]
    public void AllDtos_OmitNullValues_InJsonOutput()
    {
        // Arrange
        var sessionDto = new SessionDto("s1", DateTime.UtcNow, DateTime.UtcNow, null);

        // Act
        var json = JsonSerializer.Serialize(sessionDto, _options);

        // Assert
        json.Should().NotContain("metadata"); // Null values should be omitted
    }

    #endregion
}
