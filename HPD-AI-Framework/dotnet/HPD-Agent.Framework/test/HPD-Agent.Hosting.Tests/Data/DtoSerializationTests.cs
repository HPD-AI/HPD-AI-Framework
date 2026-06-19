using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Serialization;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

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
        _options.TypeInfoResolverChain.Add(AgentEventJsonContext.Default);
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
    public void ThreadDto_SerializesAndDeserializes_WithAllProperties()
    {
        // Arrange
        var original = new ThreadDto(
            Id: "thread-1",
            SessionId: "session-123",
            Name: "Main Thread",
            Description: "Primary conversation thread",
            ForkedFrom: "parent-thread",
            ForkedAtMessageId: "message-5",
            ForkedAtMessageIndex: 5,
            CreatedAt: DateTime.UtcNow,
            LastActivity: DateTime.UtcNow.AddMinutes(10),
            MessageCount: 25,
            Tags: new List<string> { "tag1", "tag2" },
            Ancestors: new Dictionary<string, string> { ["0"] = "root", ["1"] = "parent-thread" },
            TotalForks: 0,
            Metadata: new Dictionary<string, object> { ["purpose"] = "draft", ["priority"] = 2 });

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<ThreadDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(original.Id);
        deserialized.Id.Should().Be(original.Id);
        deserialized.Name.Should().Be(original.Name);
        deserialized.Description.Should().Be(original.Description);
        deserialized.ForkedFrom.Should().Be(original.ForkedFrom);
        deserialized.ForkedAtMessageId.Should().Be(original.ForkedAtMessageId);
        deserialized.ForkedAtMessageIndex.Should().Be(original.ForkedAtMessageIndex);
        deserialized.MessageCount.Should().Be(original.MessageCount);
        deserialized.Tags.Should().BeEquivalentTo(original.Tags);
        deserialized.Ancestors.Should().BeEquivalentTo(original.Ancestors);
        deserialized.Metadata.Should().NotBeNull();
        deserialized.Metadata!["purpose"].ToString().Should().Be("draft");
        deserialized.Metadata["priority"].ToString().Should().Be("2");
    }

    [Fact]
    public void ThreadDto_SerializesAndDeserializes_WithNullOptionalFields()
    {
        // Arrange
        var original = new ThreadDto(
            Id: "thread-1",
            SessionId: "session-123",
            Name: "Main",
            Description: null,
            ForkedFrom: null,
            ForkedAtMessageId: null,
            ForkedAtMessageIndex: null,
            CreatedAt: DateTime.UtcNow,
            LastActivity: DateTime.UtcNow,
            MessageCount: 0,
            Tags: new List<string>(),
            Ancestors: new Dictionary<string, string>(),
            TotalForks: 0);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<ThreadDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Description.Should().BeNull();
        deserialized.ForkedFrom.Should().BeNull();
        deserialized.ForkedAtMessageIndex.Should().BeNull();
    }

    [Fact]
    public void ContentDto_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new ContentDto(
            "content-123",
            "rev:123",
            "image/png",
            1024000,
            DateTime.UtcNow.ToString("O"));

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<ContentDto>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.ContentId.Should().Be(original.ContentId);
        deserialized.Version.Should().Be(original.Version);
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
    public void CreateThreadRequest_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new CreateThreadRequest(
            "new-thread",
            "New Thread",
            "Thread description",
            null,
            new Dictionary<string, object> { ["workspaceId"] = "workspace-1" });

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<CreateThreadRequest>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.ThreadId.Should().Be(original.ThreadId);
        deserialized.Name.Should().Be(original.Name);
        deserialized.Description.Should().Be(original.Description);
        deserialized.Metadata.Should().NotBeNull();
        deserialized.Metadata!["workspaceId"].ToString().Should().Be("workspace-1");
    }

    [Fact]
    public void ForkThreadRequest_SerializesAndDeserializes_Correctly()
    {
        // Arrange
        var original = new ForkThreadRequest(
            "forked-thread",
            "message-5",
            "Forked Thread",
            "Fork description",
            null,
            new Dictionary<string, object> { ["variant"] = "formal" });

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<ForkThreadRequest>(json, _options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.NewThreadId.Should().Be(original.NewThreadId);
        deserialized.FromMessageId.Should().Be(original.FromMessageId);
        deserialized.Name.Should().Be(original.Name);
        deserialized.Metadata.Should().NotBeNull();
        deserialized.Metadata!["variant"].ToString().Should().Be("formal");
    }

    [Fact]
    public void UserMessagesInputEvent_SerializesAndDeserializes_WithRunConfig()
    {
        // Arrange
        var original = new UserMessagesInputEvent([new ChatMessage(ChatRole.User, "Hello")])
        {
            SessionId = "session-123",
            ThreadId = "main",
            AgentId = "default",
            RunConfig = new AgentRunConfig
            {
                ProviderKey = "anthropic",
                ModelId = "claude-sonnet-4-5",
                AdditionalSystemInstructions = "Be concise",
                ContextOverrides = new Dictionary<string, object> { ["key"] = "value" },
                PermissionOverrides = new Dictionary<string, bool> { ["file_write"] = true },
                CoalesceDeltas = true,
                SkipTools = false,
                Chat = new ChatRunConfig
                {
                    Temperature = 0.7,
                    MaxOutputTokens = 4000
                }
            }
        };

        // Act
        var json = AgentEventSerializer.ToJson(original);
        var deserialized = AgentEventSerializer.FromJson(json) as UserMessagesInputEvent;

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Messages.Should().ContainSingle();
        deserialized.Messages[0].Text.Should().Be("Hello");
        deserialized.SessionId.Should().Be(original.SessionId);
        deserialized.ThreadId.Should().Be(original.ThreadId);
        deserialized.AgentId.Should().Be(original.AgentId);
        deserialized.RunConfig.Should().NotBeNull();
        deserialized.RunConfig!.Chat.Should().NotBeNull();
        deserialized.RunConfig.Chat!.Temperature.Should().Be(0.7);
        deserialized.RunConfig.Chat.MaxOutputTokens.Should().Be(4000);
        deserialized.RunConfig.ModelId.Should().Be("claude-sonnet-4-5");
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
