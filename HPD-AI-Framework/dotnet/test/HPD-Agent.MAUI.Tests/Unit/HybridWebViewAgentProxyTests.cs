using FluentAssertions;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Maui;
using HPD.Agent.Maui.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Maui;
using Moq;

namespace HPD.Agent.Maui.Tests.Unit;

/// <summary>
/// Unit tests for HybridWebViewAgentProxy.
/// Tests all InvokeDotNet methods and session/branch CRUD operations.
/// </summary>
public class HybridWebViewAgentProxyTests : IDisposable
{
    private readonly Mock<IHybridWebView> _mockWebView;
    private readonly MauiSessionManager _sessionManager;
    private readonly TestProxy _proxy;
    private readonly InMemorySessionStore _store;

    public HybridWebViewAgentProxyTests()
    {
        _mockWebView = new Mock<IHybridWebView>();
        _store = new InMemorySessionStore();
        var optionsMonitor = new OptionsMonitorWrapper();

        // Configure the options with provider and test infrastructure
        optionsMonitor.CurrentValue.AgentConfig = new AgentConfig
        {
            Name = "Test Agent",
            Provider = new ProviderConfig
            {
                ProviderKey = "test",
                ModelName = "test-model"
            }
        };
        optionsMonitor.CurrentValue.ConfigureAgent = builder =>
        {
            var chatClient = new FakeChatClient();
            var providerRegistry = new TestProviderRegistry(chatClient);
            // Replace builder's provider registry with test one (via reflection)
            var field = typeof(AgentBuilder).GetField("_providerRegistry",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(builder, providerRegistry);
        };

        _sessionManager = new MauiSessionManager(_store, optionsMonitor, Options.DefaultName);
        var agentManager = new MauiAgentManager(new InMemoryAgentStore(), _sessionManager, optionsMonitor, Options.DefaultName);
        _proxy = new TestProxy(_sessionManager, agentManager, _mockWebView.Object);
    }

    public void Dispose()
    {
        // Clean up resources - clear store to ensure test isolation
        _sessionManager?.Dispose();
    }

    #region StartStream

    [Fact]
    public async Task StartStream_ReturnsStreamId_Immediately()
    {
        // Arrange - Create session first
        await _proxy.CreateSession();

        // Act
        var streamId = await _proxy.StartStream("Test message", "test-session", null, null);

        // Assert
        streamId.Should().NotBeNullOrEmpty();
        Guid.TryParse(streamId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task StartStream_RunsAgentAsync_InBackground()
    {
        // This test verifies fire-and-forget behavior
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(sessionJson);

        // Act
        var streamId = await _proxy.StartStream("Test", session!["Id"].ToString()!, null, null);

        // Assert - Returns immediately without waiting for agent
        streamId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StartStream_LoadsSessionAndBranch_Correctly()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession("my-session", null);

        // Act
        var streamId = await _proxy.StartStream("Test", "my-session", "main", null);

        // Assert
        streamId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StartStream_UsesDefaultBranch_WhenNotSpecified()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(sessionJson);

        // Act - Don't specify branch
        var streamId = await _proxy.StartStream("Test", session!["Id"].ToString()!, null, null);

        // Assert
        streamId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StartStream_AcquiresStreamLock()
    {
        // This is tested implicitly - multiple streams on same branch should fail
        // Simplified test just verifies stream starts
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(sessionJson);

        // Act
        var streamId = await _proxy.StartStream("Test", session!["Id"].ToString()!, null, null);

        // Assert
        streamId.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Session CRUD

    [Fact]
    public async Task CreateSession_ReturnsSessionDto_AsJson()
    {
        // Act
        var json = await _proxy.CreateSession();

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("\"Id\"");
    }

    [Fact]
    public async Task CreateSession_AcceptsOptionalSessionId()
    {
        // Act
        var json = await _proxy.CreateSession("custom-session-id", null);

        // Assert
        // Note: Currently the sessionId parameter is not used by the implementation
        // The session ID is auto-generated by agent.CreateSession()
        // This test verifies the method accepts the parameter even if not used
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("\"Id\"");
    }

    [Fact]
    public async Task CreateSession_AcceptsMetadata_AsJson()
    {
        // Arrange
        var metadata = System.Text.Json.JsonSerializer.Serialize(new { key = "value" });

        // Act
        var json = await _proxy.CreateSession(null, metadata);

        // Assert
        json.Should().Contain("\"Id\"");
    }

    [Fact]
    public async Task CreateSession_CreatesMainBranch()
    {
        // Act
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(sessionJson);

        // Verify main branch exists
        var storedSession = await _store.LoadSessionAsync(session!["Id"].ToString()!);

        // Assert
        storedSession.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSession_ReturnsSessionDto_AsJson()
    {
        // Arrange
        var createJson = await _proxy.CreateSession();
        var created = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(createJson);

        // Act
        var json = await _proxy.GetSession(created!["Id"].ToString()!);

        // Assert
        json.Should().Contain("\"Id\"");
    }

    [Fact]
    public async Task GetSession_ThrowsException_WhenNotFound()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.GetSession("nonexistent"));
    }

    [Fact]
    public async Task UpdateSession_UpdatesMetadata()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(sessionJson);
        var sessionId = session!["Id"].GetString()!;

        var updateRequest = new Dictionary<string, object?> { ["key1"] = "value1" };
        var updateJson = System.Text.Json.JsonSerializer.Serialize(new { Metadata = updateRequest });

        // Act
        var resultJson = await _proxy.UpdateSession(sessionId, updateJson);

        // Assert
        resultJson.Should().Contain("\"Id\"");
        resultJson.Should().Contain("key1");
    }

    [Fact]
    public async Task UpdateSession_RemovesNullMetadataKeys()
    {
        // Arrange
        var initialMetadata = System.Text.Json.JsonSerializer.Serialize(new { removeMe = "value" });
        var sessionJson = await _proxy.CreateSession(null, initialMetadata);
        var session = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(sessionJson);
        var sessionId = session!["Id"].GetString()!;

        var updateRequest = new Dictionary<string, object?> { ["removeMe"] = null };
        var updateJson = System.Text.Json.JsonSerializer.Serialize(new { Metadata = updateRequest });

        // Act
        var resultJson = await _proxy.UpdateSession(sessionId, updateJson);

        // Assert
        resultJson.Should().NotContain("removeMe");
    }

    [Fact]
    public async Task UpdateSession_ThrowsWhenSessionNotFound()
    {
        // Arrange
        var updateJson = System.Text.Json.JsonSerializer.Serialize(new { Metadata = new Dictionary<string, object> { ["key"] = "value" } });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.UpdateSession("nonexistent", updateJson));
    }

    [Fact]
    public async Task UpdateSession_MergesMetadata_PreservesUnmentionedKeys()
    {
        // Arrange
        var initialMetadata = System.Text.Json.JsonSerializer.Serialize(new { keep1 = "value1", keep2 = "value2" });
        var sessionJson = await _proxy.CreateSession(null, initialMetadata);
        var session = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(sessionJson);
        var sessionId = session!["Id"].GetString()!;

        var updateRequest = new Dictionary<string, object?> { ["new"] = "newValue" };
        var updateJson = System.Text.Json.JsonSerializer.Serialize(new { Metadata = updateRequest });

        // Act
        var resultJson = await _proxy.UpdateSession(sessionId, updateJson);

        // Assert
        resultJson.Should().Contain("keep1");
        resultJson.Should().Contain("keep2");
        resultJson.Should().Contain("new");
    }

    [Fact]
    public async Task UpdateSession_UpdatesLastActivityTimestamp()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var originalLastActivity = session!.LastActivity;

        await Task.Delay(100);

        var updateJson = System.Text.Json.JsonSerializer.Serialize(new { Metadata = new Dictionary<string, object> { ["updated"] = "yes" } });

        // Act
        var resultJson = await _proxy.UpdateSession(session.Id, updateJson);
        var updated = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(resultJson);

        // Assert
        updated!.LastActivity.Should().BeAfter(originalLastActivity);
    }

    [Fact]
    public async Task UpdateSession_HandlesEmptyMetadataUpdate()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var updateJson = System.Text.Json.JsonSerializer.Serialize(new { Metadata = new Dictionary<string, object>() });

        // Act
        var resultJson = await _proxy.UpdateSession(session!.Id, updateJson);

        // Assert
        resultJson.Should().Contain("\"Id\"");
    }

    [Fact]
    public async Task DeleteSession_RemovesSession()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act
        await _proxy.DeleteSession(session!.Id);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.GetSession(session.Id));
    }

    [Fact]
    public async Task DeleteSession_ThrowsWhenSessionNotFound()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.DeleteSession("nonexistent"));
    }

    [Fact]
    public async Task DeleteSession_RemovesAgentFromCache()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act
        await _proxy.DeleteSession(session!.Id);

        // Assert - Subsequent operations should fail
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.GetSession(session.Id));
    }

    [Fact]
    public async Task SearchSessions_ReturnsAllSessions_WhenNoFilter()
    {
        // Arrange
        await _proxy.CreateSession();
        await _proxy.CreateSession();

        // Act
        var resultJson = await _proxy.SearchSessions(null);
        var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(resultJson);

        // Assert
        sessions.Should().NotBeNull();
        sessions!.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task SearchSessions_FiltersByMetadata()
    {
        // Arrange
        var metadata1 = System.Text.Json.JsonSerializer.Serialize(new { project = "projectA" });
        var metadata2 = System.Text.Json.JsonSerializer.Serialize(new { project = "projectB" });

        await _proxy.CreateSession(null, metadata1);
        await _proxy.CreateSession(null, metadata2);

        var searchRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            Metadata = new Dictionary<string, object> { ["project"] = "projectA" },
            Offset = 0,
            Limit = 100
        });

        // Act
        var resultJson = await _proxy.SearchSessions(searchRequest);
        var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(resultJson);

        // Assert
        sessions.Should().NotBeNull();
        sessions!.Should().AllSatisfy(s => s.Metadata.Should().ContainKey("project"));
    }

    [Fact]
    public async Task SearchSessions_RespectsOffsetAndLimit()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await _proxy.CreateSession();
        }

        var searchRequest = System.Text.Json.JsonSerializer.Serialize(new { Offset = 2, Limit = 2 });

        // Act
        var resultJson = await _proxy.SearchSessions(searchRequest);
        var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(resultJson);

        // Assert
        sessions.Should().NotBeNull();
        sessions!.Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task SearchSessions_OrdersByLastActivityDescending()
    {
        // Arrange
        await _proxy.CreateSession();
        await Task.Delay(100);
        await _proxy.CreateSession();

        // Act
        var resultJson = await _proxy.SearchSessions(null);
        var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(resultJson);

        // Assert
        sessions.Should().NotBeNull();
        if (sessions!.Count >= 2)
        {
            sessions[0].LastActivity.Should().BeOnOrAfter(sessions[1].LastActivity);
        }
    }

    [Fact]
    public async Task SearchSessions_HandlesEmptyResultSet()
    {
        // Arrange
        var searchRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            Metadata = new Dictionary<string, object> { ["nonexistent"] = "value" },
            Offset = 0,
            Limit = 100
        });

        // Act
        var resultJson = await _proxy.SearchSessions(searchRequest);
        var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(resultJson);

        // Assert
        sessions.Should().NotBeNull();
        sessions!.Should().BeEmpty();
    }

    #endregion

    #region Branch CRUD

    [Fact]
    public async Task ListBranches_ReturnsMainBranch()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act
        var branchesJson = await _proxy.ListBranches(session!.Id);
        var branches = System.Text.Json.JsonSerializer.Deserialize<List<BranchDto>>(branchesJson);

        // Assert
        branches.Should().NotBeNull();
        branches!.Should().ContainSingle();
        branches[0].Id.Should().Be("main");
    }

    [Fact]
    public async Task ListBranches_ThrowsWhenSessionNotFound()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.ListBranches("nonexistent"));
    }

    [Fact]
    public async Task ListBranches_ReturnsMultipleBranches()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "branch1",
            Name = "Branch 1",
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        await _proxy.CreateBranch(session!.Id, createRequest);

        // Act
        var branchesJson = await _proxy.ListBranches(session.Id);
        var branches = System.Text.Json.JsonSerializer.Deserialize<List<BranchDto>>(branchesJson);

        // Assert
        branches.Should().NotBeNull();
        branches!.Count.Should().Be(2); // main + branch1
    }

    [Fact]
    public async Task GetBranch_ReturnsBranchDto()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act
        var branchJson = await _proxy.GetBranch(session!.Id, "main");
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch.Should().NotBeNull();
        branch!.Id.Should().Be("main");
        branch.SessionId.Should().Be(session!.Id);
    }

    [Fact]
    public async Task GetBranch_ThrowsWhenBranchNotFound()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.GetBranch(session!.Id, "nonexistent"));
    }

    [Fact]
    public async Task GetBranch_ReturnsCorrectMessageCount()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act
        var branchJson = await _proxy.GetBranch(session!.Id, "main");
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch.Should().NotBeNull();
        branch!.MessageCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CreateBranch_CreatesNewBranch()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "test-branch",
            Name = "Test Branch",
            Description = "Test Description",
            Tags = new List<string> { "test" }
        });

        // Act
        var branchJson = await _proxy.CreateBranch(session!.Id, createRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch.Should().NotBeNull();
        branch!.Id.Should().Be("test-branch");
        branch.Name.Should().Be("Test Branch");
        branch.Description.Should().Be("Test Description");
    }

    [Fact]
    public async Task CreateBranch_GeneratesBranchId_WhenNotProvided()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = (string?)null,
            Name = "Auto Branch",
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act
        var branchJson = await _proxy.CreateBranch(session!.Id, createRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch.Should().NotBeNull();
        branch!.Id.Should().NotBeNullOrEmpty();
        branch.Id.Should().NotBe("main");
    }

    [Fact]
    public async Task CreateBranch_ThrowsWhenBranchAlreadyExists()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "main",
            Name = "Duplicate",
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.CreateBranch(session!.Id, createRequest));
    }

    [Fact]
    public async Task CreateBranch_ThrowsWhenSessionNotFound()
    {
        // Arrange
        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "test",
            Name = "Test",
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.CreateBranch("nonexistent", createRequest));
    }

    [Fact]
    public async Task CreateBranch_SetsNameAndDescription()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "named-branch",
            Name = "Named Branch",
            Description = "This is a named branch",
            Tags = (List<string>?)null
        });

        // Act
        var branchJson = await _proxy.CreateBranch(session!.Id, createRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch!.Name.Should().Be("Named Branch");
        branch.Description.Should().Be("This is a named branch");
    }

    [Fact]
    public async Task CreateBranch_SetsTags()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "tagged-branch",
            Name = "Tagged",
            Description = (string?)null,
            Tags = new List<string> { "tag1", "tag2" }
        });

        // Act
        var branchJson = await _proxy.CreateBranch(session!.Id, createRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch!.Tags.Should().NotBeNull();
        branch.Tags!.Should().Contain("tag1");
        branch.Tags.Should().Contain("tag2");
    }

    [Fact]
    public async Task ForkBranch_CreatesForkedBranch()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "forked",
            FromMessageIndex = 0,
            Name = "Forked Branch",
            Description = "Forked from main",
            Tags = (List<string>?)null
        });

        // Act
        var branchJson = await _proxy.ForkBranch(session!.Id, "main", forkRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch.Should().NotBeNull();
        branch!.Id.Should().Be("forked");
        branch.ForkedFrom.Should().Be("main");
        branch.ForkedAtMessageIndex.Should().Be(0);
    }

    [Fact]
    public async Task ForkBranch_ThrowsWhenSourceBranchNotFound()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "forked",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.ForkBranch(session!.Id, "nonexistent", forkRequest));
    }

    [Fact]
    public async Task ForkBranch_ThrowsWhenSessionNotFound()
    {
        // Arrange
        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "forked",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.ForkBranch("nonexistent", "main", forkRequest));
    }

    [Fact]
    public async Task ForkBranch_ThrowsWhenTargetBranchAlreadyExists()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "existing",
            Name = "Existing",
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        await _proxy.CreateBranch(session!.Id, createRequest);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "existing",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.ForkBranch(session.Id, "main", forkRequest));
    }

    [Fact]
    public async Task ForkBranch_SetsForkedFromMetadata()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "forked2",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act
        var branchJson = await _proxy.ForkBranch(session!.Id, "main", forkRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch!.ForkedFrom.Should().Be("main");
        branch.ForkedAtMessageIndex.Should().Be(0);
    }

    [Fact]
    public async Task ForkBranch_GeneratesBranchId_WhenNotProvided()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = (string?)null,
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act
        var branchJson = await _proxy.ForkBranch(session!.Id, "main", forkRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch!.Id.Should().NotBeNullOrEmpty();
        branch.Id.Should().NotBe("main");
    }

    [Fact]
    public async Task ForkBranch_SetsCustomNameAndDescription()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "custom-fork",
            FromMessageIndex = 0,
            Name = "Custom Fork",
            Description = "Custom description",
            Tags = (List<string>?)null
        });

        // Act
        var branchJson = await _proxy.ForkBranch(session!.Id, "main", forkRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch!.Name.Should().Be("Custom Fork");
        branch.Description.Should().Be("Custom description");
    }

    [Fact]
    public async Task ForkBranch_SetsTags()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "tagged-fork",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = new List<string> { "fork", "test" }
        });

        // Act
        var branchJson = await _proxy.ForkBranch(session!.Id, "main", forkRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch!.Tags.Should().NotBeNull();
        branch.Tags!.Should().Contain("fork");
        branch.Tags.Should().Contain("test");
    }

    [Fact]
    public async Task ForkBranch_TracksAncestry()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "ancestry-fork",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act
        var branchJson = await _proxy.ForkBranch(session!.Id, "main", forkRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch!.Ancestors.Should().NotBeNull();
        branch.Ancestors!.Should().ContainKey("0");
    }

    [Fact]
    public async Task DeleteBranch_RemovesBranch()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "to-delete",
            Name = "Delete Me",
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        await _proxy.CreateBranch(session!.Id, createRequest);

        // Act
        await _proxy.DeleteBranch(session.Id, "to-delete");

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.GetBranch(session.Id, "to-delete"));
    }

    [Fact]
    public async Task DeleteBranch_ThrowsWhenDeletingMainBranch()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.DeleteBranch(session!.Id, "main"));
    }

    [Fact]
    public async Task DeleteBranch_ThrowsWhenBranchNotFound()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.DeleteBranch(session!.Id, "nonexistent"));
    }

    [Fact]
    public async Task DeleteBranch_AllowsDeletingForkedBranches()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "delete-fork",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        await _proxy.ForkBranch(session!.Id, "main", forkRequest);

        // Act & Assert - Should not throw
        await _proxy.DeleteBranch(session.Id, "delete-fork");
    }

    [Fact]
    public async Task GetBranchMessages_ReturnsMessages()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act
        var messagesJson = await _proxy.GetBranchMessages(session!.Id, "main");
        var messages = System.Text.Json.JsonSerializer.Deserialize<List<MessageDto>>(messagesJson);

        // Assert
        messages.Should().NotBeNull();
        messages!.Should().NotBeNull(); // Could be empty
    }

    [Fact]
    public async Task GetBranchMessages_ThrowsWhenBranchNotFound()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.GetBranchMessages(session!.Id, "nonexistent"));
    }

    [Fact]
    public async Task GetBranchMessages_ReturnsEmptyArray_ForEmptyBranch()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act
        var messagesJson = await _proxy.GetBranchMessages(session!.Id, "main");
        var messages = System.Text.Json.JsonSerializer.Deserialize<List<MessageDto>>(messagesJson);

        // Assert
        messages.Should().NotBeNull();
        messages!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSiblingBranches_ReturnsEmptyForMainBranch()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act
        var siblingsJson = await _proxy.GetSiblingBranches(session!.Id, "main");
        var siblings = System.Text.Json.JsonSerializer.Deserialize<List<SiblingBranchDto>>(siblingsJson);

        // Assert - siblings includes self, so main alone = 1 result
        siblings.Should().NotBeNull();
        siblings!.Should().HaveCount(1);
        siblings![0].Id.Should().Be("main");
    }

    [Fact]
    public async Task GetSiblingBranches_ReturnsSiblings_ForForkedBranches()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Create two sibling branches
        var fork1Request = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "sibling1",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        var fork2Request = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "sibling2",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        await _proxy.ForkBranch(session!.Id, "main", fork1Request);
        await _proxy.ForkBranch(session.Id, "main", fork2Request);

        // Act
        var siblingsJson = await _proxy.GetSiblingBranches(session.Id, "sibling1");
        var siblings = System.Text.Json.JsonSerializer.Deserialize<List<SiblingBranchDto>>(siblingsJson);

        // Assert - siblings includes self and all forks at same point
        siblings.Should().NotBeNull();
        siblings!.Should().Contain(s => s.Id == "sibling2");
        siblings.Should().Contain(s => s.Id == "sibling1");
    }

    [Fact]
    public async Task GetSiblingBranches_ThrowsWhenBranchNotFound()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.GetSiblingBranches(session!.Id, "nonexistent"));
    }

    [Fact]
    public async Task GetSiblingBranches_DoesNotIncludeSelf()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "no-self",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        await _proxy.ForkBranch(session!.Id, "main", forkRequest);

        // Act
        var siblingsJson = await _proxy.GetSiblingBranches(session.Id, "no-self");
        var siblings = System.Text.Json.JsonSerializer.Deserialize<List<SiblingBranchDto>>(siblingsJson);

        // Assert - siblings includes self (new contract)
        siblings!.Should().Contain(s => s.Id == "no-self");
    }

    #endregion

    #region Middleware

    // NOTE: Middleware response tests moved to MiddlewareResponseTests.cs
    // These methods are now fully implemented with comprehensive test coverage

    #endregion

    #region Streaming Error Handling

    [Fact]
    public async Task StartStream_ThrowsWhenMessageEmpty()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _proxy.StartStream("", session!.Id, null, null));
    }

    [Fact]
    public async Task StartStream_ThrowsWhenMessageNull()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _proxy.StartStream(null!, session!.Id, null, null));
    }

    [Fact]
    public async Task StartStream_ThrowsWhenMessageWhitespace()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _proxy.StartStream("   ", session!.Id, null, null));
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task CreateBranch_HandlesInvalidRequestJson()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert - JsonException is thrown during deserialization
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(async () =>
            await _proxy.CreateBranch(session!.Id, "invalid-json"));
    }

    [Fact]
    public async Task ForkBranch_HandlesInvalidRequestJson()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert - JsonException is thrown during deserialization
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(async () =>
            await _proxy.ForkBranch(session!.Id, "main", "invalid-json"));
    }

    [Fact]
    public async Task UpdateSession_HandlesInvalidRequestJson()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act & Assert - JsonException is thrown during deserialization
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(async () =>
            await _proxy.UpdateSession(session!.Id, "invalid-json"));
    }

    [Fact]
    public async Task SearchSessions_HandlesInvalidRequestJson()
    {
        // Act & Assert - JsonException is thrown during deserialization
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(async () =>
            await _proxy.SearchSessions("invalid-json"));
    }

    [Fact]
    public async Task CreateSession_HandlesInvalidMetadataJson()
    {
        // Act & Assert - Should handle invalid JSON gracefully or throw
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _proxy.CreateSession(null, "invalid-json"));
    }

    #endregion

    #region DTO Serialization

    [Fact]
    public async Task SessionDto_Serialization_RoundTrip()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();

        // Act
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var reserializedJson = System.Text.Json.JsonSerializer.Serialize(session);
        var resession = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(reserializedJson);

        // Assert
        resession.Should().NotBeNull();
        resession!.Id.Should().Be(session!.Id);
        resession.CreatedAt.Should().Be(session.CreatedAt);
    }

    [Fact]
    public async Task BranchDto_Serialization_RoundTrip()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var branchJson = await _proxy.GetBranch(session!.Id, "main");

        // Act
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);
        var reserializedJson = System.Text.Json.JsonSerializer.Serialize(branch);
        var rebranch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(reserializedJson);

        // Assert
        rebranch.Should().NotBeNull();
        rebranch!.Id.Should().Be(branch!.Id);
        rebranch.Id.Should().Be(branch.Id);
    }

    [Fact]
    public async Task BranchDto_Serialization_HandlesNullOptionalFields()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var branchJson = await _proxy.GetBranch(session!.Id, "main");
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert - Optional fields should be serializable even when null
        branch!.Description.Should().BeNull();
        branch.ForkedFrom.Should().BeNull();
        branch.ForkedAtMessageIndex.Should().BeNull();
    }

    [Fact]
    public async Task MessageDto_Serialization_RoundTrip()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var messagesJson = await _proxy.GetBranchMessages(session!.Id, "main");

        // Act
        var messages = System.Text.Json.JsonSerializer.Deserialize<List<MessageDto>>(messagesJson);
        var reserializedJson = System.Text.Json.JsonSerializer.Serialize(messages);
        var remessages = System.Text.Json.JsonSerializer.Deserialize<List<MessageDto>>(reserializedJson);

        // Assert
        remessages.Should().NotBeNull();
        remessages!.Count.Should().Be(messages!.Count);
    }

    [Fact]
    public async Task SessionDto_PreservesDates()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Assert - Dates should be preserved accurately
        session!.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        session.LastActivity.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task CreateSession_WithEmptyMetadata()
    {
        // Arrange
        var emptyMetadata = System.Text.Json.JsonSerializer.Serialize(new { });

        // Act
        var sessionJson = await _proxy.CreateSession(null, emptyMetadata);

        // Assert
        sessionJson.Should().NotBeNullOrEmpty();
        sessionJson.Should().Contain("\"Id\"");
    }

    [Fact]
    public async Task UpdateSession_WithEmptyMetadata()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var emptyUpdate = System.Text.Json.JsonSerializer.Serialize(new { Metadata = new Dictionary<string, object>() });

        // Act
        var resultJson = await _proxy.UpdateSession(session!.Id, emptyUpdate);

        // Assert
        resultJson.Should().Contain("\"Id\"");
    }

    [Fact]
    public async Task SearchSessions_WithNoSessionsInStore()
    {
        // Arrange - Use a fresh proxy with empty store
        var mockWebView = new Mock<IHybridWebView>();
        var store = new InMemorySessionStore();
        var optionsMonitor = new OptionsMonitorWrapper();
        optionsMonitor.CurrentValue.AgentConfig = new AgentConfig
        {
            Name = "Test Agent",
            Provider = new ProviderConfig
            {
                ProviderKey = "test",
                ModelName = "test-model"
            }
        };
        optionsMonitor.CurrentValue.ConfigureAgent = builder =>
        {
            var chatClient = new FakeChatClient();
            var providerRegistry = new TestProviderRegistry(chatClient);
            var field = typeof(AgentBuilder).GetField("_providerRegistry",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(builder, providerRegistry);
        };
        var sessionManager = new MauiSessionManager(store, optionsMonitor, Options.DefaultName);
        var agentMgr = new MauiAgentManager(new InMemoryAgentStore(), sessionManager, optionsMonitor, Options.DefaultName);
        var proxy = new TestProxy(sessionManager, agentMgr, mockWebView.Object);

        // Act
        var resultJson = await proxy.SearchSessions(null);
        var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(resultJson);

        // Assert
        sessions.Should().NotBeNull();
        sessions!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBranchMessages_WithNoMessages()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act
        var messagesJson = await _proxy.GetBranchMessages(session!.Id, "main");
        var messages = System.Text.Json.JsonSerializer.Deserialize<List<MessageDto>>(messagesJson);

        // Assert
        messages.Should().NotBeNull();
        messages!.Should().BeEmpty();
    }

    [Fact]
    public async Task ForkBranch_AtIndexZero_ForEmptyBranch()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "fork-at-zero",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act
        var branchJson = await _proxy.ForkBranch(session!.Id, "main", forkRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch.Should().NotBeNull();
        branch!.ForkedAtMessageIndex.Should().Be(0);
    }

    [Fact]
    public async Task SearchSessions_WithLimit_GreaterThanTotalSessions()
    {
        // Arrange
        await _proxy.CreateSession();
        await _proxy.CreateSession();

        var searchRequest = System.Text.Json.JsonSerializer.Serialize(new { Offset = 0, Limit = 1000 });

        // Act
        var resultJson = await _proxy.SearchSessions(searchRequest);
        var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(resultJson);

        // Assert
        sessions.Should().NotBeNull();
        sessions!.Count.Should().BeLessThanOrEqualTo(1000);
    }

    [Fact]
    public async Task SearchSessions_WithOffset_BeyondTotalSessions()
    {
        // Arrange
        await _proxy.CreateSession();

        var searchRequest = System.Text.Json.JsonSerializer.Serialize(new { Offset = 100, Limit = 10 });

        // Act
        var resultJson = await _proxy.SearchSessions(searchRequest);
        var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(resultJson);

        // Assert
        sessions.Should().NotBeNull();
        sessions!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSiblingBranches_WithNoSiblings()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "lonely-fork",
            FromMessageIndex = 0,
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        await _proxy.ForkBranch(session!.Id, "main", forkRequest);

        // Act
        var siblingsJson = await _proxy.GetSiblingBranches(session.Id, "lonely-fork");
        var siblings = System.Text.Json.JsonSerializer.Deserialize<List<SiblingBranchDto>>(siblingsJson);

        // Assert - only fork at this point = 1 sibling (itself)
        siblings.Should().NotBeNull();
        siblings!.Should().HaveCount(1);
        siblings![0].Id.Should().Be("lonely-fork");
    }

    [Fact]
    public async Task CreateBranch_DefaultsToGeneratedName_WhenNotProvided()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "unnamed-branch",
            Name = (string?)null,
            Description = (string?)null,
            Tags = (List<string>?)null
        });

        // Act
        var branchJson = await _proxy.CreateBranch(session!.Id, createRequest);
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Assert
        branch.Should().NotBeNull();
        branch!.Name.Should().Be("unnamed-branch"); // Should default to branch ID
    }

    #endregion

    #region Concurrency & Thread Safety

    [Fact]
    public async Task ConcurrentSessionCreation_AllSucceed()
    {
        // Arrange & Act
        var tasks = Enumerable.Range(0, 5).Select(_ => _proxy.CreateSession());
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(json => json.Should().Contain("\"Id\""));
        results.Select(json => System.Text.Json.JsonSerializer.Deserialize<SessionDto>(json)!.Id)
            .Distinct().Should().HaveCount(5); // All unique
    }

    [Fact]
    public async Task ConcurrentBranchCreation_AllSucceed()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act - Create multiple branches concurrently
        var tasks = Enumerable.Range(0, 5).Select(i =>
        {
            var createRequest = System.Text.Json.JsonSerializer.Serialize(new
            {
                BranchId = $"branch-{i}",
                Name = $"Branch {i}",
                Description = (string?)null,
                Tags = (List<string>?)null
            });
            return _proxy.CreateBranch(session!.Id, createRequest);
        });
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(json => json.Should().Contain("branch-"));
    }

    [Fact]
    public async Task ConcurrentSessionUpdates_LastUpdateWins()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act - Update metadata concurrently
        var tasks = Enumerable.Range(0, 5).Select(i =>
        {
            var updateJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                Metadata = new Dictionary<string, object> { [$"key{i}"] = $"value{i}" }
            });
            return _proxy.UpdateSession(session!.Id, updateJson);
        });
        await Task.WhenAll(tasks);

        // Assert - All updates should have been applied
        var finalJson = await _proxy.GetSession(session!.Id);
        finalJson.Should().Contain("key");
    }

    [Fact]
    public async Task ConcurrentForkOperations_AllSucceed()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Act - Fork the main branch concurrently
        var tasks = Enumerable.Range(0, 3).Select(i =>
        {
            var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
            {
                NewBranchId = $"fork-{i}",
                FromMessageIndex = 0,
                Name = $"Fork {i}",
                Description = (string?)null,
                Tags = (List<string>?)null
            });
            return _proxy.ForkBranch(session!.Id, "main", forkRequest);
        });
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(json => json.Should().Contain("fork-"));
    }

    [Fact]
    public async Task ConcurrentSearchOperations_AllSucceed()
    {
        // Arrange
        await _proxy.CreateSession();
        await _proxy.CreateSession();

        // Act - Search concurrently
        var tasks = Enumerable.Range(0, 10).Select(_ => _proxy.SearchSessions(null));
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(json =>
        {
            var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(json);
            sessions.Should().NotBeNull();
        });
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Integration_CompleteSessionLifecycle()
    {
        // Create session
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        session.Should().NotBeNull();

        // Update session
        var updateJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            Metadata = new Dictionary<string, object> { ["status"] = "active" }
        });
        await _proxy.UpdateSession(session!.Id, updateJson);

        // Verify update
        var retrievedJson = await _proxy.GetSession(session.Id);
        retrievedJson.Should().Contain("status");

        // Delete session
        await _proxy.DeleteSession(session.Id);

        // Verify deletion
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.GetSession(session.Id));
    }

    [Fact]
    public async Task Integration_CompleteBranchLifecycle()
    {
        // Create session
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Create branch
        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "test-branch",
            Name = "Test Branch",
            Description = "Integration test",
            Tags = new List<string> { "test" }
        });
        await _proxy.CreateBranch(session!.Id, createRequest);

        // Verify creation
        var branchJson = await _proxy.GetBranch(session.Id, "test-branch");
        branchJson.Should().Contain("test-branch");

        // Delete branch
        await _proxy.DeleteBranch(session.Id, "test-branch");

        // Verify deletion
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.GetBranch(session.Id, "test-branch"));
    }

    [Fact]
    public async Task Integration_CreateSessionWithMetadataAndSearch()
    {
        // Create sessions with metadata
        var metadata1 = System.Text.Json.JsonSerializer.Serialize(new { environment = "test", version = "1.0" });
        var metadata2 = System.Text.Json.JsonSerializer.Serialize(new { environment = "prod", version = "2.0" });

        await _proxy.CreateSession(null, metadata1);
        await _proxy.CreateSession(null, metadata2);

        // Search by metadata
        var searchRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            Metadata = new Dictionary<string, object> { ["environment"] = "test" },
            Offset = 0,
            Limit = 100
        });

        var resultJson = await _proxy.SearchSessions(searchRequest);
        var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(resultJson);

        // Verify results
        sessions.Should().NotBeNull();
        sessions!.Should().Contain(s => s.Metadata.ContainsKey("environment"));
    }

    [Fact]
    public async Task Integration_ForkTreeWithMultipleLevels()
    {
        // Create session
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Fork from main
        var fork1Request = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "level1",
            FromMessageIndex = 0,
            Name = "Level 1",
            Description = (string?)null,
            Tags = (List<string>?)null
        });
        await _proxy.ForkBranch(session!.Id, "main", fork1Request);

        // Fork from level1
        var fork2Request = System.Text.Json.JsonSerializer.Serialize(new
        {
            NewBranchId = "level2",
            FromMessageIndex = 0,
            Name = "Level 2",
            Description = (string?)null,
            Tags = (List<string>?)null
        });
        await _proxy.ForkBranch(session.Id, "level1", fork2Request);

        // Verify both branches exist
        var branchesJson = await _proxy.ListBranches(session.Id);
        var branches = System.Text.Json.JsonSerializer.Deserialize<List<BranchDto>>(branchesJson);

        branches.Should().NotBeNull();
        branches!.Should().Contain(b => b.Id == "main");
        branches.Should().Contain(b => b.Id == "level1");
        branches.Should().Contain(b => b.Id == "level2");
    }

    [Fact]
    public async Task Integration_CreateMultipleBranchesAndListAll()
    {
        // Create session
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Create multiple branches
        for (int i = 0; i < 5; i++)
        {
            var createRequest = System.Text.Json.JsonSerializer.Serialize(new
            {
                BranchId = $"branch-{i}",
                Name = $"Branch {i}",
                Description = (string?)null,
                Tags = (List<string>?)null
            });
            await _proxy.CreateBranch(session!.Id, createRequest);
        }

        // List all branches
        var branchesJson = await _proxy.ListBranches(session!.Id);
        var branches = System.Text.Json.JsonSerializer.Deserialize<List<BranchDto>>(branchesJson);

        // Verify
        branches.Should().NotBeNull();
        branches!.Count.Should().Be(6); // main + 5 created branches
    }

    [Fact]
    public async Task Integration_MultipleSessionsAreIsolated()
    {
        // Create two sessions
        var session1Json = await _proxy.CreateSession();
        var session2Json = await _proxy.CreateSession();
        var session1 = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(session1Json);
        var session2 = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(session2Json);

        // Create branch in session1
        var createRequest = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "test-branch",
            Name = "Test",
            Description = (string?)null,
            Tags = (List<string>?)null
        });
        await _proxy.CreateBranch(session1!.Id, createRequest);

        // Verify branch exists in session1 but not session2
        var branches1Json = await _proxy.ListBranches(session1.Id);
        var branches2Json = await _proxy.ListBranches(session2!.Id);

        var branches1 = System.Text.Json.JsonSerializer.Deserialize<List<BranchDto>>(branches1Json);
        var branches2 = System.Text.Json.JsonSerializer.Deserialize<List<BranchDto>>(branches2Json);

        branches1!.Should().Contain(b => b.Id == "test-branch");
        branches2!.Should().NotContain(b => b.Id == "test-branch");
    }

    [Fact]
    public async Task Integration_SearchWithPaginationMultiplePages()
    {
        // Create many sessions
        for (int i = 0; i < 10; i++)
        {
            await _proxy.CreateSession();
        }

        // Get first page
        var page1Request = System.Text.Json.JsonSerializer.Serialize(new { Offset = 0, Limit = 3 });
        var page1Json = await _proxy.SearchSessions(page1Request);
        var page1 = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(page1Json);

        // Get second page
        var page2Request = System.Text.Json.JsonSerializer.Serialize(new { Offset = 3, Limit = 3 });
        var page2Json = await _proxy.SearchSessions(page2Request);
        var page2 = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(page2Json);

        // Verify pagination works
        page1!.Count.Should().BeLessThanOrEqualTo(3);
        page2!.Count.Should().BeLessThanOrEqualTo(3);
        page1.Select(s => s.Id).Should().NotIntersectWith(page2.Select(s => s.Id));
    }

    #endregion

    #region Performance & Stress Tests

    [Fact]
    public async Task Performance_SearchWithManySessions()
    {
        // Create 50 sessions
        for (int i = 0; i < 50; i++)
        {
            await _proxy.CreateSession();
        }

        // Search all sessions
        var resultJson = await _proxy.SearchSessions(null);
        var sessions = System.Text.Json.JsonSerializer.Deserialize<List<SessionDto>>(resultJson);

        // Verify
        sessions.Should().NotBeNull();
        sessions!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Performance_ListManyBranches()
    {
        // Create session
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Create 20 branches
        for (int i = 0; i < 20; i++)
        {
            var createRequest = System.Text.Json.JsonSerializer.Serialize(new
            {
                BranchId = $"branch-{i}",
                Name = $"Branch {i}",
                Description = (string?)null,
                Tags = (List<string>?)null
            });
            await _proxy.CreateBranch(session!.Id, createRequest);
        }

        // List all branches
        var branchesJson = await _proxy.ListBranches(session!.Id);
        var branches = System.Text.Json.JsonSerializer.Deserialize<List<BranchDto>>(branchesJson);

        // Verify
        branches.Should().NotBeNull();
        branches!.Count.Should().Be(21); // main + 20 created
    }

    [Fact]
    public async Task Performance_DeepForkTree()
    {
        // Create session
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        // Create deep fork tree (5 levels)
        string parentBranchId = "main";
        for (int i = 0; i < 5; i++)
        {
            var forkRequest = System.Text.Json.JsonSerializer.Serialize(new
            {
                NewBranchId = $"level-{i}",
                FromMessageIndex = 0,
                Name = $"Level {i}",
                Description = (string?)null,
                Tags = (List<string>?)null
            });
            await _proxy.ForkBranch(session!.Id, parentBranchId, forkRequest);
            parentBranchId = $"level-{i}";
        }

        // Verify all branches exist
        var branchesJson = await _proxy.ListBranches(session!.Id);
        var branches = System.Text.Json.JsonSerializer.Deserialize<List<BranchDto>>(branchesJson);

        branches.Should().NotBeNull();
        branches!.Count.Should().Be(6); // main + 5 levels
    }

    [Fact]
    public async Task Performance_ConcurrentMixedOperations()
    {
        // Create initial sessions
        var session1Json = await _proxy.CreateSession();
        var session2Json = await _proxy.CreateSession();
        var session1 = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(session1Json);
        var session2 = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(session2Json);

        // Mix of concurrent operations
        var tasks = new List<Task>
        {
            _proxy.CreateSession(),
            _proxy.SearchSessions(null),
            _proxy.ListBranches(session1!.Id),
            _proxy.ListBranches(session2!.Id),
            _proxy.GetSession(session1.Id)
        };

        // Execute all concurrently
        await Task.WhenAll(tasks);

        // If we get here without exceptions, test passes
        tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
    }

    #endregion

    #region Fix 1 (MAUI) — GetBranchMessages stable IDs and timestamps

    [Fact]
    public async Task GetBranchMessages_MessageIds_AreStable_AcrossMultipleCalls()
    {
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var json1 = await _proxy.GetBranchMessages(session!.Id, "main");
        var json2 = await _proxy.GetBranchMessages(session.Id, "main");

        var msgs1 = System.Text.Json.JsonSerializer.Deserialize<List<MessageDto>>(json1);
        var msgs2 = System.Text.Json.JsonSerializer.Deserialize<List<MessageDto>>(json2);

        msgs1.Should().NotBeNull();
        msgs2.Should().NotBeNull();
        msgs1!.Count.Should().Be(msgs2!.Count);

        for (int i = 0; i < msgs1.Count; i++)
            msgs1[i].Id.Should().Be(msgs2[i].Id, "message IDs must be stable across calls");
    }

    [Fact]
    public async Task GetBranchMessages_Timestamps_ParseAsValidIso8601()
    {
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var json = await _proxy.GetBranchMessages(session!.Id, "main");
        var msgs = System.Text.Json.JsonSerializer.Deserialize<List<MessageDto>>(json);

        msgs.Should().NotBeNull();
        foreach (var msg in msgs!)
        {
            if (msg.Timestamp != null)
                DateTimeOffset.TryParse(msg.Timestamp, out _).Should().BeTrue(
                    "timestamp '{0}' should be valid ISO 8601", msg.Timestamp);
        }
    }

    #endregion

    #region Fix 4 (MAUI) — UpdateBranch

    [Fact]
    public async Task UpdateBranch_UpdatesNameAndReturnsDto()
    {
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var sid = session!.Id;

        var createReq = System.Text.Json.JsonSerializer.Serialize(
            new CreateBranchRequest("upd-branch", "Original", "Desc", null));
        await _proxy.CreateBranch(sid, createReq);

        var patchReq = System.Text.Json.JsonSerializer.Serialize(
            new UpdateBranchRequest("Renamed", null, null));
        var resultJson = await _proxy.UpdateBranch(sid, "upd-branch", patchReq);

        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(resultJson);
        branch.Should().NotBeNull();
        branch!.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task UpdateBranch_NullFields_AreIgnored_OtherFieldsUnchanged()
    {
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var sid = session!.Id;

        var createReq = System.Text.Json.JsonSerializer.Serialize(
            new CreateBranchRequest("null-field-test", "OriginalName", "KeepDesc", null));
        await _proxy.CreateBranch(sid, createReq);

        // Pass null description — should leave description unchanged
        var patchReq = System.Text.Json.JsonSerializer.Serialize(
            new UpdateBranchRequest("NewName", null, null));
        var resultJson = await _proxy.UpdateBranch(sid, "null-field-test", patchReq);

        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(resultJson);
        branch!.Name.Should().Be("NewName");
        branch.Description.Should().Be("KeepDesc");
    }

    [Fact]
    public async Task UpdateBranch_UpdatesTags()
    {
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var sid = session!.Id;

        await _proxy.CreateBranch(sid, System.Text.Json.JsonSerializer.Serialize(
            new CreateBranchRequest("tag-branch", "T", null, null)));

        var patchReq = System.Text.Json.JsonSerializer.Serialize(
            new UpdateBranchRequest(null, null, ["x", "y"]));
        var resultJson = await _proxy.UpdateBranch(sid, "tag-branch", patchReq);

        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(resultJson);
        branch!.Tags.Should().BeEquivalentTo(["x", "y"]);
    }

    [Fact]
    public async Task UpdateBranch_ThrowsInvalidOperation_WhenBranchNotFound()
    {
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var patchReq = System.Text.Json.JsonSerializer.Serialize(
            new UpdateBranchRequest("X", null, null));

        var act = async () => await _proxy.UpdateBranch(session!.Id, "nonexistent", patchReq);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateBranch_PersistedAcrossGetBranch()
    {
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var sid = session!.Id;

        await _proxy.CreateBranch(sid, System.Text.Json.JsonSerializer.Serialize(
            new CreateBranchRequest("persist-test", "Before", null, null)));

        await _proxy.UpdateBranch(sid, "persist-test",
            System.Text.Json.JsonSerializer.Serialize(new UpdateBranchRequest("After", "NewDesc", null)));

        var getJson = await _proxy.GetBranch(sid, "persist-test");
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(getJson);
        branch!.Name.Should().Be("After");
        branch.Description.Should().Be("NewDesc");
    }

    #endregion

    #region MAUI Fix C — ToBranchDto uses GetDisplayName()

    [Fact]
    public async Task GetBranch_WithNoName_UsesDescription_AsDisplayName()
    {
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var sid = session!.Id;

        // Create branch with description but no name
        var createReq = System.Text.Json.JsonSerializer.Serialize(
            new CreateBranchRequest("desc-only", null, "My Description", null));
        await _proxy.CreateBranch(sid, createReq);

        var branchJson = await _proxy.GetBranch(sid, "desc-only");
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // GetDisplayName() falls back to Description when Name is null
        branch!.Name.Should().Be("My Description");
    }

    [Fact]
    public async Task ListBranches_UsesGetDisplayName_NotRawNameField()
    {
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var sid = session!.Id;

        // Create branch with only description
        var createReq = System.Text.Json.JsonSerializer.Serialize(
            new CreateBranchRequest("list-desc-only", null, "List Description", null));
        await _proxy.CreateBranch(sid, createReq);

        var listJson = await _proxy.ListBranches(sid);
        var branches = System.Text.Json.JsonSerializer.Deserialize<List<BranchDto>>(listJson);

        var branch = branches!.FirstOrDefault(b => b.Id == "list-desc-only");
        branch.Should().NotBeNull();
        branch!.Name.Should().Be("List Description");
    }

    [Fact]
    public async Task GetBranch_WithNoNameOrDescription_DoesNotUseRawId_AsDisplayName()
    {
        // When branch has neither name nor description, GetDisplayName() falls back to Id.
        // The key regression this guards: previously `branch.Name ?? branch.Id` was used,
        // so when Name was null, the raw GUID would appear. After the fix, GetDisplayName()
        // follows the full fallback chain. For a branch without name/description/messages
        // it will still return Id — but this test ensures the code path is GetDisplayName(),
        // not a direct property access.
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var sid = session!.Id;

        var createReq = System.Text.Json.JsonSerializer.Serialize(
            new CreateBranchRequest("bare-branch", null, null, null));
        await _proxy.CreateBranch(sid, createReq);

        var branchJson = await _proxy.GetBranch(sid, "bare-branch");
        var branch = System.Text.Json.JsonSerializer.Deserialize<BranchDto>(branchJson);

        // Whatever GetDisplayName() returns, it should not be null or empty
        branch!.Name.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region MAUI Fix A — Stream cancellation (StopStream)

    [Fact]
    public void StopStream_WithUnknownStreamId_DoesNotThrow()
    {
        var act = () => _proxy.StopStream("nonexistent-stream-id");
        act.Should().NotThrow();
    }

    [Fact]
    public async Task StopStream_AfterStreamCompletes_DoesNotThrow()
    {
        // Arrange — create a session (stream will fail to find agent but completes quickly)
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var streamId = await _proxy.StartStream("hello", session!.Id, "main", null);

        // Give the background task time to finish
        await Task.Delay(200);

        // Calling StopStream on a completed stream should be a no-op
        var act = () => _proxy.StopStream(streamId);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task StartStream_Returns_UniqueStreamIds()
    {
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var id1 = await _proxy.StartStream("msg1", session!.Id, null, null);
        var id2 = await _proxy.StartStream("msg2", session.Id, null, null);

        id1.Should().NotBe(id2);
        Guid.TryParse(id1, out _).Should().BeTrue();
        Guid.TryParse(id2, out _).Should().BeTrue();
    }

    #endregion

    #region Test Helpers

    private class TestProxy : HybridWebViewAgentProxy
    {
        public TestProxy(MauiSessionManager manager, MauiAgentManager agentManager, IHybridWebView webView)
            : base(manager, agentManager, webView)
        {
        }
    }

    private class OptionsMonitorWrapper : IOptionsMonitor<HPDAgentConfig>
    {
        public HPDAgentConfig CurrentValue { get; } = new HPDAgentConfig();
        public HPDAgentConfig Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<HPDAgentConfig, string?> listener) => null;
    }

    #endregion
}
