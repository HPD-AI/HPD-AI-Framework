using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for Agent definition CRUD endpoints.
/// Tests: POST /agents, GET /agents, GET /agents/{id}, PUT /agents/{id}, DELETE /agents/{id}
/// </summary>
public class AgentEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AgentEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // POST /agents
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_agents_Creates_StoredAgent()
    {
        var request = MakeCreateRequest("my-agent", "My Agent");

        var response = await _client.PostAsJsonAsync("/agents", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<StoredAgentDto>();
        dto.Should().NotBeNull();
        dto!.Id.Should().Be("my-agent");
        dto.Name.Should().Be("My Agent");
        dto.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task POST_agents_NoName_Uses_ConfigName()
    {
        var config = new AgentConfig { Name = "FromConfig", Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" }, ModelName = "test-model" } } };
        var request = new CreateAgentRequest("from-config", config);

        var response = await _client.PostAsJsonAsync("/agents", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<StoredAgentDto>();
        dto!.Name.Should().Be("FromConfig");
    }

    [Fact]
    public async Task POST_agents_Returns_LocationHeader()
    {
        var request = MakeCreateRequest("located-agent", "Located Agent");

        var response = await _client.PostAsJsonAsync("/agents", request);

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/agents/");
    }

    [Fact]
    public async Task POST_agents_Returns400_WhenConfigInvalid()
    {
        // MaxAgenticIterations = 0 is invalid
        var request = new CreateAgentRequest(
            "bad-agent",
            new AgentConfig { Name = "Bad Agent", MaxAgenticIterations = 0 });

        var response = await _client.PostAsJsonAsync("/agents", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_agents_Returns409_WhenIdAlreadyExists()
    {
        var request = MakeCreateRequest("duplicate-agent", "Duplicate Agent");
        (await _client.PostAsJsonAsync("/agents", request)).EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync("/agents", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /agents
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_agents_Returns_AllDefinitions()
    {
        // Arrange — create two agents to ensure at least some exist
        await _client.PostAsJsonAsync("/agents", MakeCreateRequest("list-agent-a", "List Agent A"));
        await _client.PostAsJsonAsync("/agents", MakeCreateRequest("list-agent-b", "List Agent B"));

        // Act
        var response = await _client.GetAsync("/agents");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var agents = await response.Content.ReadFromJsonAsync<List<AgentSummaryDto>>();
        agents.Should().NotBeNull();
        agents!.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GET_agents_Returns_SummaryDto_WithoutConfigBody()
    {
        await _client.PostAsJsonAsync("/agents", MakeCreateRequest("summary-agent", "Summary Shape Agent"));

        var response = await _client.GetAsync("/agents");
        var json = await response.Content.ReadAsStringAsync();

        // Config body should NOT appear in the list response
        json.Should().NotContain("\"config\"");
        // But id, name, createdAt, updatedAt should be present
        json.Should().Contain("\"id\"");
        json.Should().Contain("\"name\"");
        json.Should().Contain("\"createdAt\"");
        json.Should().Contain("\"updatedAt\"");
    }

    [Fact]
    public async Task GET_agents_Includes_Definition_Declared_By_Host_Registration()
    {
        var response = await _client.GetAsync("/agents/test-agent");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var dto = await response.Content.ReadFromJsonAsync<StoredAgentDto>();
        dto!.Id.Should().Be("test-agent");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /agents/{agentId}
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_agents_agentId_Returns_Definition()
    {
        var created = await CreateAgentAsync("Get By Id Agent");

        var response = await _client.GetAsync($"/agents/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var dto = await response.Content.ReadFromJsonAsync<StoredAgentDto>();
        dto!.Id.Should().Be(created.Id);
        dto.Name.Should().Be("Get By Id Agent");
    }

    [Fact]
    public async Task GET_agents_agentId_Returns404_WhenMissing()
    {
        var response = await _client.GetAsync("/agents/nonexistent-agent-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PATCH /agents/{agentId}
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_agents_agentId_Updates_Supplied_Config_Fields()
    {
        var created = await CreateAgentAsync("Before Update");
        var updateRequest = new UpdateAgentRequest(new AgentConfigUpdate
        {
            Name = "After Update",
            MaxAgenticIterations = 20
        });

        var response = await _client.PatchAsJsonAsync($"/agents/{created.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var dto = await response.Content.ReadFromJsonAsync<StoredAgentDto>();
        dto!.Config.MaxAgenticIterations.Should().Be(20);
        dto.Name.Should().Be("After Update");
        dto.Config.SystemInstructions.Should().Be(created.Config.SystemInstructions);
        dto.Config.Clients.Chat!.ModelName.Should().Be("test-model");
        dto.UpdatedAt.Should().BeOnOrAfter(created.CreatedAt);
    }

    [Fact]
    public async Task PATCH_agents_agentId_Replaces_And_Clears_Arrays()
    {
        var config = MakeConfig("Array Agent");
        config.ToolHarnesses = ["FirstHarness", "SecondHarness"];
        var created = await CreateAgentAsync($"array-{Guid.NewGuid():N}", config);

        var replaced = await _client.PatchAsJsonAsync(
            $"/agents/{created.Id}",
            new UpdateAgentRequest(new AgentConfigUpdate
            {
                ToolHarnesses = ["ReplacementHarness"]
            }));
        var replacedDto = await replaced.Content.ReadFromJsonAsync<StoredAgentDto>();
        replacedDto!.Config.ToolHarnesses.Should().ContainSingle()
            .Which.Name.Should().Be("ReplacementHarness");

        var cleared = await _client.PatchAsJsonAsync(
            $"/agents/{created.Id}",
            new UpdateAgentRequest(new AgentConfigUpdate
            {
                ToolHarnesses = []
            }));
        var clearedDto = await cleared.Content.ReadFromJsonAsync<StoredAgentDto>();
        clearedDto!.Config.ToolHarnesses.Should().BeEmpty();
    }

    [Fact]
    public async Task PATCH_agents_agentId_Clears_Nullable_Sections_With_Explicit_Null()
    {
        var config = MakeConfig("Nullable Agent");
        config.Validation = new ValidationConfig { TimeoutMs = 1234 };
        var created = await CreateAgentAsync($"nullable-{Guid.NewGuid():N}", config);

        var response = await _client.PatchAsJsonAsync(
            $"/agents/{created.Id}",
            new UpdateAgentRequest(new AgentConfigUpdate
            {
                Clear = ["validation"]
            }));

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        var dto = await response.Content.ReadFromJsonAsync<StoredAgentDto>();
        dto!.Config.Validation.Should().BeNull();
    }

    [Fact]
    public async Task PATCH_agents_agentId_Updates_And_Clears_Metadata_Independently()
    {
        var request = MakeCreateRequest($"metadata-{Guid.NewGuid():N}", "Metadata Agent");
        request = request with { Metadata = new() { ["team"] = "support" } };
        var createdResponse = await _client.PostAsJsonAsync("/agents", request);
        var created = (await createdResponse.Content.ReadFromJsonAsync<StoredAgentDto>())!;

        var metadataResponse = await _client.PatchAsJsonAsync(
            $"/agents/{created.Id}",
            new UpdateAgentRequest
            {
                Metadata = new() { ["team"] = "platform", ["environment"] = "production" }
            });
        metadataResponse.StatusCode.Should().Be(HttpStatusCode.OK, await metadataResponse.Content.ReadAsStringAsync());
        var metadataDto = await metadataResponse.Content.ReadFromJsonAsync<StoredAgentDto>();
        metadataDto!.Metadata.Should().NotBeNull();
        metadataDto.Metadata!["team"]!.ToString().Should().Be("platform");
        metadataDto.Config.Should().BeEquivalentTo(created.Config);

        var clearResponse = await _client.PatchAsJsonAsync(
            $"/agents/{created.Id}",
            new UpdateAgentRequest { ClearMetadata = true });
        var cleared = await clearResponse.Content.ReadFromJsonAsync<StoredAgentDto>();
        cleared!.Metadata.Should().BeNull();
        cleared.Config.Should().BeEquivalentTo(created.Config);
    }

    [Fact]
    public async Task PATCH_agents_agentId_Returns400_For_Empty_Patch()
    {
        var created = await CreateAgentAsync("Empty Patch Agent");

        var response = await _client.PatchAsJsonAsync(
            $"/agents/{created.Id}",
            new UpdateAgentRequest());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PATCH_agents_agentId_Returns400_For_Unknown_Fields()
    {
        var created = await CreateAgentAsync("Unknown Patch Agent");
        using var content = JsonContent.Create(new { unexpected = true });

        var response = await _client.PatchAsync($"/agents/{created.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PATCH_agents_agentId_Returns400_For_Unknown_Config_Fields()
    {
        var created = await CreateAgentAsync("Unknown Config Patch Agent");
        using var content = JsonContent.Create(new
        {
            config = new
            {
                name = "Renamed Agent",
                unexpected = true
            }
        });

        var response = await _client.PatchAsync($"/agents/{created.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PATCH_agents_agentId_Returns404_WhenMissing()
    {
        var updateRequest = new UpdateAgentRequest(new AgentConfigUpdate
        {
            Name = "Doesn't Matter"
        });

        var response = await _client.PatchAsJsonAsync("/agents/no-such-id", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PATCH_agents_agentId_Returns400_WhenMergedConfigInvalid()
    {
        var created = await CreateAgentAsync("Valid Before Update");

        var response = await _client.PatchAsJsonAsync(
            $"/agents/{created.Id}",
            new UpdateAgentRequest(new AgentConfigUpdate { MaxAgenticIterations = 0 }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DELETE /agents/{agentId}
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DELETE_agents_agentId_Removes_Definition()
    {
        var created = await CreateAgentAsync("To Delete Agent");

        var deleteResponse = await _client.DeleteAsync($"/agents/{created.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await _client.GetAsync($"/agents/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DELETE_agents_agentId_Returns404_WhenMissing()
    {
        var response = await _client.DeleteAsync("/agents/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static CreateAgentRequest MakeCreateRequest(string agentId, string name) =>
        new(agentId, MakeConfig(name));

    private static AgentConfig MakeConfig(string name) =>
        new()
        {
            Name = name,
            MaxAgenticIterations = 10,
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" }, ModelName = "test-model" } }
        };

    private async Task<StoredAgentDto> CreateAgentAsync(string name)
        => await CreateAgentAsync($"agent-{Guid.NewGuid():N}", MakeConfig(name));

    private async Task<StoredAgentDto> CreateAgentAsync(string agentId, AgentConfig config)
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest(agentId, config));
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<StoredAgentDto>();
        return dto ?? throw new InvalidOperationException("CreateAgent returned null DTO");
    }
}
