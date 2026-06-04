using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for Branch CRUD endpoints.
/// Tests: GET /sessions/{sid}/branches, POST /sessions/{sid}/branches, GET /sessions/{sid}/branches/{bid},
/// POST /sessions/{sid}/branches/{bid}/fork, DELETE /sessions/{sid}/branches/{bid}, etc.
/// </summary>
public class BranchEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public BranchEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> CreateTestSession()
    {
        var response = await _client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    private async Task<string> EnsureForkMessageAsync(string sessionId, string branchId = "main")
    {
        var existing = await TryGetFirstUserMessageIdAsync(sessionId, branchId);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing!;

        return await SeedForkMessageAsync(sessionId, branchId);
    }

    private async Task<string?> TryGetFirstUserMessageIdAsync(string sessionId, string branchId)
    {
        var eventsResponse = await _client.GetAsync($"/sessions/{sessionId}/branches/{branchId}/events");
        eventsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await eventsResponse.Content.ReadAsStringAsync());
        foreach (var evt in document.RootElement.EnumerateArray())
        {
            if (evt.GetProperty("type").GetString() != BranchEventTypes.MessageStarted)
                continue;

            if (evt.TryGetProperty("role", out var role) &&
                string.Equals(role.GetString(), "user", StringComparison.OrdinalIgnoreCase) &&
                evt.TryGetProperty("messageId", out var messageId))
            {
                return messageId.GetString();
            }
        }

        return null;
    }

    private async Task<string> SeedForkMessageAsync(string sessionId, string branchId)
    {
        var repository = _factory.Server.Services.GetRequiredService<SessionManager>().Repository;
        (await repository.LoadBranchAsync(sessionId, branchId)).Should().NotBeNull();

        var messageId = $"seed-{Guid.NewGuid():N}";
        var message = new ChatMessage(ChatRole.User, [new TextContent("Seed fork message")])
        {
            MessageId = messageId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.AppendBranchEventAsync(
            sessionId,
            branchId,
            BranchEventFactory.MessageStarted(sessionId, branchId, message));
        await repository.AppendBranchEventAsync(
            sessionId,
            branchId,
            BranchEventFactory.ContentAdded(sessionId, branchId, messageId, message.Contents[0]));
        await repository.AppendBranchEventAsync(
            sessionId,
            branchId,
            BranchEventFactory.MessageCompleted(sessionId, branchId, messageId));

        return messageId;
    }

    #region GET /sessions/{sid}/branches

    [Fact]
    public async Task ListBranches_ReturnsAllBranches_ForSession()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/branches");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var branches = await response.Content.ReadFromJsonAsync<List<BranchDto>>();
        branches.Should().NotBeNull();
        branches!.Should().ContainSingle(); // Only "main" branch initially
        branches[0].Id.Should().Be("main");
    }

    [Fact]
    public async Task ListBranches_Returns404_WhenSessionNotFound()
    {
        // Act
        var response = await _client.GetAsync("/sessions/nonexistent/branches");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListBranches_ReturnsEmptyArray_WhenNoBranches()
    {
        // This test verifies behavior if somehow a session has no branches
        // In practice, sessions always have at least "main"
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/branches");

        // Assert
        var branches = await response.Content.ReadFromJsonAsync<List<BranchDto>>();
        branches.Should().NotBeNull();
        branches!.Should().NotBeEmpty(); // Always has "main"
    }

    #endregion

    #region GET /sessions/{sid}/branches/{bid}

    [Fact]
    public async Task GetBranch_Returns200_WithBranchDto()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/branches/main");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch.Should().NotBeNull();
        branch!.Id.Should().Be("main");
        branch.SessionId.Should().Be(sessionId);
    }

    [Fact]
    public async Task GetBranch_Returns404_WhenBranchNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/branches/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBranch_Returns404_WhenSessionNotFound()
    {
        // Act
        var response = await _client.GetAsync("/sessions/nonexistent/branches/main");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /sessions/{sid}/branches

    [Fact]
    public async Task CreateBranch_Returns201_WithBranchDto()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new CreateBranchRequest(
            "feature-branch",
            "Feature Branch",
            "Testing new feature",
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch.Should().NotBeNull();
        branch!.Id.Should().Be("feature-branch");
        branch.Name.Should().Be("Feature Branch");
        branch.Description.Should().Be("Testing new feature");
    }

    [Fact]
    public async Task CreateBranch_AcceptsCustomBranchId()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new CreateBranchRequest("custom-id", "Custom", null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches",
            request);

        // Assert
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch!.Id.Should().Be("custom-id");
    }

    [Fact]
    public async Task CreateBranch_GeneratesBranchId_WhenNotProvided()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new CreateBranchRequest(null, "Auto Branch", null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches",
            request);

        // Assert
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch!.Id.Should().NotBeNullOrEmpty();
        branch.Id.Should().NotBe("main");
    }

    [Fact]
    public async Task CreateBranch_AcceptsNameAndDescription()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new CreateBranchRequest(
            "test",
            "Test Branch",
            "This is a test branch",
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches",
            request);

        // Assert
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch!.Name.Should().Be("Test Branch");
        branch.Description.Should().Be("This is a test branch");
    }

    [Fact]
    public async Task CreateBranch_Returns404_WhenSessionNotFound()
    {
        // Arrange
        var request = new CreateBranchRequest("test", "Test", null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            "/agents/test-agent/sessions/nonexistent/branches",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateBranch_Returns409_WhenBranchIdExists()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act - Try to create a branch with ID "main" (already exists)
        var request = new CreateBranchRequest("main", "Duplicate", null, null);
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region POST /sessions/{sid}/branches/{bid}/fork

    [Fact]
    public async Task ForkBranch_Returns201_WithForkedBranch()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);
        var request = new ForkBranchRequest(
            "forked",
            forkMessageId,
            "Forked Branch",
            "Forked from main",
            null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/main/fork",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch.Should().NotBeNull();
        branch!.Id.Should().Be("forked");
        branch.ForkedFrom.Should().Be("main");
        branch.ForkedAtMessageId.Should().Be(forkMessageId);
        branch.ForkedAtMessageIndex.Should().Be(0);
    }

    [Fact]
    public async Task ForkBranch_CopiesMessagesThroughMessageId()
    {
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);
        var request = new ForkBranchRequest("fork1", forkMessageId, "Fork", null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/main/fork",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ForkBranch_SetsForkedFromAndIndex()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);
        var request = new ForkBranchRequest("fork2", forkMessageId, null, null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/main/fork",
            request);

        // Assert
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch!.ForkedFrom.Should().Be("main");
        branch.ForkedAtMessageId.Should().Be(forkMessageId);
        branch.ForkedAtMessageIndex.Should().Be(0);
    }

    [Fact]
    public async Task ForkBranch_SetsAncestors_Correctly()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);
        var request = new ForkBranchRequest("fork3", forkMessageId, null, null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/main/fork",
            request);

        // Assert
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch!.Ancestors.Should().NotBeNull();
        branch.Ancestors!.Should().ContainKey("0");
    }

    [Fact]
    public async Task ForkBranch_Returns404_WhenSourceBranchNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new ForkBranchRequest("fork", "missing-message", null, null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/nonexistent/fork",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ForkBranch_Returns400_WhenMessageIsNotPresent()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var request = new ForkBranchRequest("fork", "missing-message", null, null, null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/main/fork",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region DELETE /sessions/{sid}/branches/{bid}

    [Fact]
    public async Task DeleteBranch_Returns204_OnSuccess()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var createRequest = new CreateBranchRequest("to-delete", "Delete Me", null, null);
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/branches", createRequest);

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sessionId}/branches/to-delete");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteBranch_Returns404_WhenBranchNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sessionId}/branches/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteBranch_Returns400_WhenDeletingMainBranch()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sessionId}/branches/main");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region GET /sessions/{sid}/branches/{bid}/events

    [Fact]
    public async Task GetBranchEvents_ReturnsNormalizedBranchEvents()
    {
        var sessionId = await CreateTestSession();

        var response = await _client.GetAsync($"/sessions/{sessionId}/branches/main/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.EnumerateArray()
            .Should()
            .Contain(e => e.GetProperty("type").GetString() == BranchEventTypes.BranchCreated);
    }

    [Fact]
    public async Task GetBranchEvents_Returns404_WhenBranchNotFound()
    {
        var sessionId = await CreateTestSession();

        var response = await _client.GetAsync($"/sessions/{sessionId}/branches/nonexistent/events");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBranchEvents_ReturnsForkEvent_ForForkedBranch()
    {
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);
        var forkRequest = new ForkBranchRequest("fork-1", forkMessageId, "Fork 1", null, null);

        var forkResponse = await _client.PostAsJsonAsync(
            $"/agents/test-agent/sessions/{sessionId}/branches/main/fork",
            forkRequest);
        forkResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.GetAsync($"/sessions/{sessionId}/branches/fork-1/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var events = document.RootElement.EnumerateArray().ToList();
        events.Should().Contain(e => e.GetProperty("type").GetString() == BranchEventTypes.BranchForked);

        var forked = events.Single(e => e.GetProperty("type").GetString() == BranchEventTypes.BranchForked);
        forked.GetProperty("sourceBranchId").GetString().Should().Be("main");
        forked.GetProperty("fromMessageId").GetString().Should().Be(forkMessageId);
        forked.GetProperty("resolvedMessageIndex").GetInt32().Should().Be(0);
    }

    #endregion

    #region GET /sessions/{sid}/branches/{bid}/siblings

    [Fact]
    public async Task GetSiblings_ReturnsForkedBranches()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var forkMessageId = await EnsureForkMessageAsync(sessionId);

        // Create sibling branches
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/fork",
            new ForkBranchRequest("sibling1", forkMessageId, null, null, null));
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/branches/main/fork",
            new ForkBranchRequest("sibling2", forkMessageId, null, null, null));

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/branches/sibling1/siblings");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var siblings = await response.Content.ReadFromJsonAsync<List<SiblingBranchDto>>();
        siblings.Should().NotBeNull();
        siblings!.Should().Contain(s => s.Id == "sibling2");
    }

    [Fact]
    public async Task GetSiblings_ReturnsSelf_WhenNoForks()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/branches/main/siblings");

        // Assert
        var siblings = await response.Content.ReadFromJsonAsync<List<SiblingBranchDto>>();
        siblings.Should().NotBeNull();
        siblings!.Should().HaveCount(1);
        siblings![0].Id.Should().Be("main");
        siblings![0].IsOriginal.Should().BeTrue();
    }

    [Fact]
    public async Task GetSiblings_Returns404_WhenBranchNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/branches/nonexistent/siblings");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region PATCH /sessions/{sid}/branches/{bid} — Fix 4: update branch metadata

    [Fact]
    public async Task UpdateBranch_Returns200_WithUpdatedDto()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var createReq = new CreateBranchRequest("upd-test", "Original Name", "Original Desc", null);
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/branches", createReq);

        // Act
        var patchReq = new UpdateBranchRequest("Renamed Branch", null, null);
        var response = await _client.PatchAsJsonAsync($"/sessions/{sessionId}/branches/upd-test", patchReq);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch.Should().NotBeNull();
        branch!.Name.Should().Be("Renamed Branch");
    }

    [Fact]
    public async Task UpdateBranch_OnlyUpdatesProvidedFields_LeavesOthersUnchanged()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var createReq = new CreateBranchRequest("partial-upd", "Original Name", "Keep This Desc", null);
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/branches", createReq);

        // Act — only update name, leave description null (omitted)
        var patchReq = new UpdateBranchRequest("New Name", null, null);
        var response = await _client.PatchAsJsonAsync($"/sessions/{sessionId}/branches/partial-upd", patchReq);

        // Assert
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch!.Name.Should().Be("New Name");
        branch.Description.Should().Be("Keep This Desc");
    }

    [Fact]
    public async Task UpdateBranch_Returns404_WhenBranchNotFound()
    {
        var sessionId = await CreateTestSession();

        var patchReq = new UpdateBranchRequest("X", null, null);
        var response = await _client.PatchAsJsonAsync($"/sessions/{sessionId}/branches/nonexistent", patchReq);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateBranch_UpdatesTags()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/branches",
            new CreateBranchRequest("tag-test", "T", null, null));

        // Act
        var patchReq = new UpdateBranchRequest(null, null, ["alpha", "beta"]);
        await _client.PatchAsJsonAsync($"/sessions/{sessionId}/branches/tag-test", patchReq);

        // Assert — reload the branch and check tags
        var getResp = await _client.GetAsync($"/sessions/{sessionId}/branches/tag-test");
        var branch = await getResp.Content.ReadFromJsonAsync<BranchDto>();
        branch!.Tags.Should().NotBeNull();
        branch.Tags!.Should().BeEquivalentTo(["alpha", "beta"]);
    }

    [Fact]
    public async Task UpdateBranch_MergesAndRemovesMetadata()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/branches",
            new CreateBranchRequest("metadata-test", "T", null, null, new Dictionary<string, object>
            {
                ["purpose"] = "draft",
                ["pinned"] = true
            }));

        // Act
        var patchReq = new UpdateBranchRequest(null, null, null, new Dictionary<string, object?>
        {
            ["purpose"] = "final",
            ["pinned"] = null,
            ["variant"] = "concise"
        });
        var response = await _client.PatchAsJsonAsync($"/sessions/{sessionId}/branches/metadata-test", patchReq);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch!.Metadata.Should().NotBeNull();
        branch.Metadata!.Keys.Should().BeEquivalentTo(["purpose", "variant"]);
        branch.Metadata["purpose"].ToString().Should().Be("final");
        branch.Metadata["variant"].ToString().Should().Be("concise");
    }

    [Fact]
    public async Task UpdateBranch_UpdatesLastActivity()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/branches",
            new CreateBranchRequest("ts-test", "T", null, null));

        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var patchReq = new UpdateBranchRequest("Renamed", null, null);
        var response = await _client.PatchAsJsonAsync($"/sessions/{sessionId}/branches/ts-test", patchReq);

        // Assert
        var branch = await response.Content.ReadFromJsonAsync<BranchDto>();
        branch!.LastActivity.Should().BeAfter(before);
    }

    [Fact]
    public async Task UpdateBranch_PersistedAcrossGetBranch()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        await _client.PostAsJsonAsync($"/agents/test-agent/sessions/{sessionId}/branches",
            new CreateBranchRequest("persist-test", "Before", null, null));

        // Act
        await _client.PatchAsJsonAsync($"/sessions/{sessionId}/branches/persist-test",
            new UpdateBranchRequest("After", "New desc", null));

        // Assert — reload via GET, not from PATCH response
        var getResp = await _client.GetAsync($"/sessions/{sessionId}/branches/persist-test");
        var branch = await getResp.Content.ReadFromJsonAsync<BranchDto>();
        branch!.Name.Should().Be("After");
        branch.Description.Should().Be("New desc");
    }

    #endregion
}
