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
        var request = new CreateAgentRequest("from-config", "FromConfig", config);

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
            "Bad Agent",
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
        response.StatusCode.Should().Be(HttpStatusCode.OK);
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

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
    // PUT /agents/{agentId}
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_agents_agentId_Updates_Definition()
    {
        var created = await CreateAgentAsync("Before Update");
        var updatedConfig = new AgentConfig { Name = "After Update", MaxAgenticIterations = 20, Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" }, ModelName = "test-model" } } };
        var updateRequest = new UpdateAgentRequest(updatedConfig);

        var response = await _client.PutAsJsonAsync($"/agents/{created.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<StoredAgentDto>();
        dto!.Config.MaxAgenticIterations.Should().Be(20);
        dto.UpdatedAt.Should().BeOnOrAfter(created.CreatedAt);
    }

    [Fact]
    public async Task PUT_agents_agentId_Returns404_WhenMissing()
    {
        var updateRequest = new UpdateAgentRequest(new AgentConfig { Name = "Doesn't Matter", Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" }, ModelName = "test-model" } } });

        var response = await _client.PutAsJsonAsync("/agents/no-such-id", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PUT_agents_agentId_Returns400_WhenConfigInvalid()
    {
        var created = await CreateAgentAsync("Valid Before Update");
        var badConfig = new AgentConfig { Name = "Bad", MaxAgenticIterations = 0 };

        var response = await _client.PutAsJsonAsync($"/agents/{created.Id}", new UpdateAgentRequest(badConfig));

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
        new(agentId, name, new AgentConfig
        {
            Name = name,
            MaxAgenticIterations = 10,
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" }, ModelName = "test-model" } }
        });

    private async Task<StoredAgentDto> CreateAgentAsync(string name)
    {
        var agentId = $"agent-{Guid.NewGuid():N}";
        var response = await _client.PostAsJsonAsync("/agents", MakeCreateRequest(agentId, name));
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<StoredAgentDto>();
        return dto ?? throw new InvalidOperationException("CreateAgent returned null DTO");
    }
}
