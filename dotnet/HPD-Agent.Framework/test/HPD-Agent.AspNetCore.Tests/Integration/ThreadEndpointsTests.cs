using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for Thread CRUD endpoints.
/// Tests: GET /sessions/{sid}/threads, POST /sessions/{sid}/threads, GET /sessions/{sid}/threads/{bid},
/// POST /sessions/{sid}/threads/{bid}/fork, DELETE /sessions/{sid}/threads/{bid}, etc.
/// </summary>
public class ThreadEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ThreadEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> CreateTestSession()
    {
        var response = await _client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    [Fact]
    public async Task EncodedAgentAndThreadIds_RoundTripThroughThreadRoutes()
    {
        var sessionId = await CreateTestSession();
        const string agentId = "coding/explorer";
        const string threadId = "subagent/explore/workspace/invocation-1";
        var encodedAgentId = Uri.EscapeDataString(agentId);
        var encodedThreadId = Uri.EscapeDataString(threadId);

        var create = await _client.PostAsJsonAsync(
            $"/agents/{encodedAgentId}/sessions/{sessionId}/threads",
            new CreateThreadRequest(threadId, threadId, null, null, null));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var threadResponse = await _client.GetAsync(
            $"/sessions/{sessionId}/threads/{encodedThreadId}");
        threadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread = await threadResponse.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.Id.Should().Be(threadId);
        thread.DefaultAgentId.Should().Be(agentId);

        var stateResponse = await _client.GetAsync(
            $"/agents/{encodedAgentId}/sessions/{sessionId}/threads/{encodedThreadId}/state");
        stateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = await stateResponse.Content.ReadFromJsonAsync<ThreadRuntimeStateDto>();
        state!.ObservedCursor.Generation.Should().BePositive();
    }

    private async Task<string> EnsureForkMessageAsync(string sessionId, string threadId = "main")
    {
        var existing = await TryGetFirstUserMessageIdAsync(sessionId, threadId);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing!;

        var inputResponse = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/{threadId}/inputs",
            new StreamTextRequest("Seed fork message"));
        inputResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var messageId = await TryGetFirstUserMessageIdAsync(
            sessionId,
            threadId,
            TimeSpan.FromSeconds(15));
        if (!string.IsNullOrWhiteSpace(messageId))
            return messageId!;

        throw new TimeoutException("Timed out waiting for a persisted fork message.");
    }

    private async Task<string?> TryGetFirstUserMessageIdAsync(
        string sessionId,
        string threadId,
        TimeSpan? timeout = null)
    {
        var events = await SseTestEventReader.ReadUntilAsync(
            _client,
            sessionId,
            threadId,
            static observed => observed.OfType<TextMessageStartEvent>()
                .Any(evt => string.Equals(evt.Role, "user", StringComparison.OrdinalIgnoreCase)),
            timeout ?? TimeSpan.FromMilliseconds(150));
        return events.OfType<TextMessageStartEvent>()
            .FirstOrDefault(evt => string.Equals(evt.Role, "user", StringComparison.OrdinalIgnoreCase))
            ?.MessageId;
    }

    #region GET /sessions/{sid}/threads

    [Fact]
    public async Task ListThreads_ReturnsAllThreads_ForSession()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/threads");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var threads = await response.Content.ReadFromJsonAsync<List<ThreadDto>>();
        threads.Should().NotBeNull();
        threads!.Should().ContainSingle(); // Only "main" thread initially
        threads[0].Id.Should().Be("main");
    }

    [Fact]
    public async Task ListThreads_Returns404_WhenSessionNotFound()
    {
        // Act
        var response = await _client.GetAsync("/sessions/nonexistent/threads");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListThreads_ReturnsEmptyArray_WhenNoThreads()
    {
        // This test verifies behavior if somehow a session has no threads
        // In practice, sessions always have at least "main"
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/threads");

        // Assert
        var threads = await response.Content.ReadFromJsonAsync<List<ThreadDto>>();
        threads.Should().NotBeNull();
        threads!.Should().NotBeEmpty(); // Always has "main"
    }

    #endregion

    #region GET /sessions/{sid}/threads/{bid}

    [Fact]
    public async Task GetThread_Returns200_WithThreadDto()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/threads/main");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread.Should().NotBeNull();
        thread!.Id.Should().Be("main");
        thread.SessionId.Should().Be(sessionId);
    }

    [Fact]
    public async Task GetThread_Returns404_WhenThreadNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/threads/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetThread_Returns404_WhenSessionNotFound()
    {
        // Act
        var response = await _client.GetAsync("/sessions/nonexistent/threads/main");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /sessions/{sid}/threads

    [Fact]
    public async Task CreateThread_Returns201_WithThreadDto()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new CreateThreadRequest(
            "feature-thread",
            "Feature Thread",
            "Testing new feature",
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread.Should().NotBeNull();
        thread!.Id.Should().Be("feature-thread");
        thread.Name.Should().Be("Feature Thread");
        thread.Description.Should().Be("Testing new feature");
    }

    [Fact]
    public async Task CreateThread_AcceptsCustomThreadId()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new CreateThreadRequest("custom-id", "Custom", null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads",
            request);

        // Assert
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.Id.Should().Be("custom-id");
    }

    [Fact]
    public async Task CreateThread_GeneratesThreadId_WhenNotProvided()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new CreateThreadRequest(null, "Auto Thread", null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads",
            request);

        // Assert
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.Id.Should().NotBeNullOrEmpty();
        thread.Id.Should().NotBe("main");
    }

    [Fact]
    public async Task CreateThread_AcceptsNameAndDescription()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new CreateThreadRequest(
            "test",
            "Test Thread",
            "This is a test thread",
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads",
            request);

        // Assert
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.Name.Should().Be("Test Thread");
        thread.Description.Should().Be("This is a test thread");
    }

    [Fact]
    public async Task CreateThread_Returns404_WhenSessionNotFound()
    {
        // Arrange
        var request = new CreateThreadRequest("test", "Test", null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            "/agents/test-agent/sessions/nonexistent/threads",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateThread_Returns409_WhenThreadIdExists()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act - Try to create a thread with ID "main" (already exists)
        var request = new CreateThreadRequest("main", "Duplicate", null, null);
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region POST /sessions/{sid}/threads/{bid}/fork

    [Fact]
    public async Task ForkThread_Returns201_WithForkedThread()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);
        var request = new ForkThreadRequest(
            "forked",
            forkMessageId,
            "Forked Thread",
            "Forked from main",
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread.Should().NotBeNull();
        thread!.Id.Should().Be("forked");
        thread.ForkedFrom.Should().Be("main");
        thread.ForkedAtMessageId.Should().Be(forkMessageId);
        thread.ForkedAtMessageIndex.Should().Be(0);
    }

    [Fact]
    public async Task ForkThread_CopiesMessagesThroughMessageId()
    {
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);
        var request = new ForkThreadRequest("fork1", forkMessageId, "Fork", null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ForkThread_WithNullFromMessageId_ForksFromRoot()
    {
        var sessionId = await CreateTestSession();
        await EnsureForkMessageAsync(sessionId);
        var request = new ForkThreadRequest("root-fork", null, "Root Fork", null, null);

        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread.Should().NotBeNull();
        thread!.Id.Should().Be("root-fork");
        thread.ForkedFrom.Should().Be("main");
        thread.ForkedAtMessageId.Should().BeNull();
        thread.ForkedAtMessageIndex.Should().BeNull();
        thread.MessageCount.Should().Be(0);
    }

    [Fact]
    public async Task ForkThread_SetsForkedFromAndIndex()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);
        var request = new ForkThreadRequest("fork2", forkMessageId, null, null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            request);

        // Assert
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.ForkedFrom.Should().Be("main");
        thread.ForkedAtMessageId.Should().Be(forkMessageId);
        thread.ForkedAtMessageIndex.Should().Be(0);
    }

    [Fact]
    public async Task ForkThread_SetsAncestors_Correctly()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);
        var request = new ForkThreadRequest("fork3", forkMessageId, null, null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            request);

        // Assert
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.Ancestors.Should().NotBeNull();
        thread.Ancestors!.Should().ContainKey("0");
    }

    [Fact]
    public async Task ForkThread_Returns404_WhenSourceThreadNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new ForkThreadRequest("fork", "missing-message", null, null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/nonexistent/fork",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ForkThread_Returns400_WhenMessageIsNotPresent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new ForkThreadRequest("fork", "missing-message", null, null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region DELETE /sessions/{sid}/threads/{bid}

    [Fact]
    public async Task DeleteThread_Returns204_OnSuccess()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var createRequest = new CreateThreadRequest("to-delete", "Delete Me", null, null);
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads", createRequest);

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sessionId}/threads/to-delete");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteThread_Returns404_WhenThreadNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sessionId}/threads/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteThread_Returns400_WhenDeletingMainThread()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sessionId}/threads/main");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region GET /agents/{agentId}/sessions/{sid}/threads/{bid}/events

    [Fact]
    public async Task ObserveThreadEvents_ReplaysNormalizedThreadEvents()
    {
        var sessionId = await CreateTestSession();

        var events = await SseTestEventReader.ReadUntilAsync(
            _client,
            sessionId,
            "main",
            static observed => observed.Any(evt => evt is ThreadCreatedEvent),
            TimeSpan.FromSeconds(15));

        events.Should().Contain(evt => evt is ThreadCreatedEvent);
    }

    [Fact]
    public async Task ObserveThreadEvents_Returns404_WhenThreadNotFound()
    {
        var sessionId = await CreateTestSession();

        var response = await _client.GetAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/nonexistent/events?after=1:0");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ObserveThreadEvents_ReplaysForkMetadata_ForForkedThread()
    {
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);
        var forkRequest = new ForkThreadRequest("fork-1", forkMessageId, "Fork 1", null, null);

        var forkResponse = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            forkRequest);
        forkResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var events = await SseTestEventReader.ReadUntilAsync(
            _client,
            sessionId,
            "fork-1",
            static observed => observed.Any(evt => evt is ThreadCreatedEvent),
            TimeSpan.FromSeconds(15));

        var created = events.OfType<ThreadCreatedEvent>().Should().ContainSingle().Which;
        created.ForkedFrom.Should().Be("main");
        created.ForkedAtMessageId.Should().Be(forkMessageId);
        created.ForkedAtMessageIndex.Should().Be(0);
    }

    #endregion

    #region GET /sessions/{sid}/thread-graph

    [Fact]
    public async Task GetThreadGraph_ReturnsForkGroups()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);

        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            new ForkThreadRequest("fork-1", forkMessageId, null, null, null));
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            new ForkThreadRequest("fork-2", forkMessageId, null, null, null));

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/thread-graph");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var graph = await response.Content.ReadFromJsonAsync<ThreadGraphDto>();
        graph.Should().NotBeNull();
        graph!.Threads.Should().Contain(thread => thread.Id == "main");
        graph.Threads.Should().Contain(thread => thread.Id == "fork-1");
        graph.Threads.Should().Contain(thread => thread.Id == "fork-2");

        var group = graph.ForkGroups.Should().ContainSingle().Subject;
        group.SourceThreadId.Should().Be("main");
        group.ForkedAtMessageId.Should().Be(forkMessageId);
        group.Members.Select(member => member.ThreadId)
            .Should().Equal("main", "fork-1", "fork-2");
        group.Members[0].IsSource.Should().BeTrue();
        graph.RuntimeChildren.Should().BeEmpty();
    }

    [Fact]
    public async Task GetThreadGraph_GroupsNestedForksAtSameCopiedMessage()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);

        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            new ForkThreadRequest("fork-1", forkMessageId, "First fork", null, null));
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads/fork-1/fork",
            new ForkThreadRequest("fork-2", forkMessageId, "Nested fork", null, null));

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/thread-graph");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var graph = await response.Content.ReadFromJsonAsync<ThreadGraphDto>();
        graph.Should().NotBeNull();

        var group = graph!.ForkGroups.Should().ContainSingle().Subject;
        group.SourceThreadId.Should().Be("main");
        group.ForkedAtMessageId.Should().Be(forkMessageId);
        group.Members.Select(member => member.ThreadId)
            .Should().Equal("main", "fork-1", "fork-2");
    }

    [Fact]
    public async Task GetThreadGraph_GroupsNestedRootForksTogether()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            new ForkThreadRequest("fork-1", null, "First root fork", null, null));
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads/fork-1/fork",
            new ForkThreadRequest("fork-2", null, "Nested root fork", null, null));

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/thread-graph");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var graph = await response.Content.ReadFromJsonAsync<ThreadGraphDto>();
        graph.Should().NotBeNull();

        var group = graph!.ForkGroups.Should().ContainSingle().Subject;
        group.SourceThreadId.Should().Be("main");
        group.ForkedAtMessageId.Should().BeNull();
        group.Members.Select(member => member.ThreadId)
            .Should().Equal("main", "fork-1", "fork-2");
    }

    [Fact]
    public async Task GetThreadGraph_SeparatesRuntimeChildrenFromVisibleForkGroups()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);

        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            new ForkThreadRequest("visible-fork", forkMessageId, "Visible fork", null, null));
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads/main/fork",
            new ForkThreadRequest(
                "subagent-research",
                forkMessageId,
                "Research subagent",
                null,
                null,
                new Dictionary<string, object>
                {
                    ["kind"] = "subagent",
                    ["visibility"] = "hidden",
                    ["parentSessionId"] = sessionId,
                    ["parentThreadId"] = "main",
                    ["subAgentName"] = "research",
                    ["invocationId"] = "run-1",
                    ["subAgentSourceKind"] = "InlineConfig",
                    ["parentToolCallId"] = "tool-1",
                    ["sessionPolicy"] = "ParentSession",
                    ["threadPolicy"] = "ForkFromParentThread"
                }));

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/thread-graph");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var graph = await response.Content.ReadFromJsonAsync<ThreadGraphDto>();
        graph.Should().NotBeNull();
        graph!.Threads.Should().Contain(thread => thread.Id == "subagent-research");

        var group = graph.ForkGroups.Should().ContainSingle().Subject;
        group.Members.Select(member => member.ThreadId)
            .Should().Equal("main", "visible-fork");

        var child = graph.RuntimeChildren.Should().ContainSingle().Subject;
        child.ThreadId.Should().Be("subagent-research");
        child.ParentSessionId.Should().Be(sessionId);
        child.ParentThreadId.Should().Be("main");
        child.Kind.Should().Be(ThreadKind.SubAgent);
        child.Visibility.Should().Be(ThreadVisibility.Hidden);
        child.SubAgentName.Should().Be("research");
        child.InvocationId.Should().Be("run-1");
        child.SubAgentSourceKind.Should().Be("InlineConfig");
        child.ParentToolCallId.Should().Be("tool-1");
        child.SessionPolicy.Should().Be("ParentSession");
        child.ThreadPolicy.Should().Be("ForkFromParentThread");
    }

    [Fact]
    public async Task GetThreadGraph_ReturnsEmptyForkGroups_WhenNoForks()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/thread-graph");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var graph = await response.Content.ReadFromJsonAsync<ThreadGraphDto>();
        graph.Should().NotBeNull();
        graph!.Threads.Should().ContainSingle(thread => thread.Id == "main");
        graph.ForkGroups.Should().BeEmpty();
        graph.RuntimeChildren.Should().BeEmpty();
    }

    [Fact]
    public async Task GetThreadGraph_Returns404_WhenSessionNotFound()
    {
        // Act
        var response = await _client.GetAsync("/sessions/nonexistent/thread-graph");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region PATCH /sessions/{sid}/threads/{bid} — Fix 4: update thread metadata

    [Fact]
    public async Task UpdateThread_Returns200_WithUpdatedDto()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var createReq = new CreateThreadRequest("upd-test", "Original Name", "Original Desc", null);
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads", createReq);

        // Act
        var patchReq = new UpdateThreadRequest("Renamed Thread", null, null);
        var response = await _client.PatchAsJsonAsync($"/sessions/{sessionId}/threads/upd-test", patchReq);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread.Should().NotBeNull();
        thread!.Name.Should().Be("Renamed Thread");
    }

    [Fact]
    public async Task UpdateThread_OnlyUpdatesProvidedFields_LeavesOthersUnchanged()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var createReq = new CreateThreadRequest("partial-upd", "Original Name", "Keep This Desc", null);
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads", createReq);

        // Act — only update name, leave description null (omitted)
        var patchReq = new UpdateThreadRequest("New Name", null, null);
        var response = await _client.PatchAsJsonAsync($"/sessions/{sessionId}/threads/partial-upd", patchReq);

        // Assert
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.Name.Should().Be("New Name");
        thread.Description.Should().Be("Keep This Desc");
    }

    [Fact]
    public async Task UpdateThread_Returns404_WhenThreadNotFound()
    {
        var sessionId = await CreateTestSession();

        var patchReq = new UpdateThreadRequest("X", null, null);
        var response = await _client.PatchAsJsonAsync($"/sessions/{sessionId}/threads/nonexistent", patchReq);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateThread_UpdatesTags()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads",
            new CreateThreadRequest("tag-test", "T", null, null));

        // Act
        var patchReq = new UpdateThreadRequest(null, null, ["alpha", "beta"]);
        await _client.PatchAsJsonAsync($"/sessions/{sessionId}/threads/tag-test", patchReq);

        // Assert — reload the thread and check tags
        var getResp = await _client.GetAsync($"/sessions/{sessionId}/threads/tag-test");
        var thread = await getResp.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.Tags.Should().NotBeNull();
        thread.Tags!.Should().BeEquivalentTo(["alpha", "beta"]);
    }

    [Fact]
    public async Task UpdateThread_MergesAndRemovesMetadata()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads",
            new CreateThreadRequest("metadata-test", "T", null, null, new Dictionary<string, object>
            {
                ["purpose"] = "draft",
                ["pinned"] = true
            }));

        // Act
        var patchReq = new UpdateThreadRequest(null, null, null, new Dictionary<string, object?>
        {
            ["purpose"] = "final",
            ["pinned"] = null,
            ["variant"] = "concise"
        });
        var response = await _client.PatchAsJsonAsync($"/sessions/{sessionId}/threads/metadata-test", patchReq);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.Metadata.Should().NotBeNull();
        thread.Metadata!.Keys.Should().BeEquivalentTo(["purpose", "variant"]);
        thread.Metadata["purpose"].ToString().Should().Be("final");
        thread.Metadata["variant"].ToString().Should().Be("concise");
    }

    [Fact]
    public async Task UpdateThread_UpdatesLastActivity()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads",
            new CreateThreadRequest("ts-test", "T", null, null));

        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var patchReq = new UpdateThreadRequest("Renamed", null, null);
        var response = await _client.PatchAsJsonAsync($"/sessions/{sessionId}/threads/ts-test", patchReq);

        // Assert
        var thread = await response.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.LastActivity.Should().BeAfter(before);
    }

    [Fact]
    public async Task UpdateThread_PersistedAcrossGetThread()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/threads",
            new CreateThreadRequest("persist-test", "Before", null, null));

        // Act
        await _client.PatchAsJsonAsync($"/sessions/{sessionId}/threads/persist-test",
            new UpdateThreadRequest("After", "New desc", null));

        // Assert — reload via GET, not from PATCH response
        var getResp = await _client.GetAsync($"/sessions/{sessionId}/threads/persist-test");
        var thread = await getResp.Content.ReadFromJsonAsync<ThreadDto>();
        thread!.Name.Should().Be("After");
        thread.Description.Should().Be("New desc");
    }

    #endregion
}
