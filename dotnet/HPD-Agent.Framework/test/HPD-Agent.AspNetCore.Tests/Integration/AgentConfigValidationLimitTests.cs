using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.Tests.Integration;

public sealed class AgentConfigValidationLimitTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgentConfigValidationLimitTests()
    {
        _factory = new TestWebApplicationFactory(services =>
            services.Configure<HPDAgentHostOptions>("test-agent", options =>
                options.ValidationLimits = new AgentConfigValidationLimits
                {
                    MaximumAgenticIterations = 50
                }));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task CreateAgent_Returns400_WhenHostLimitIsExceeded()
    {
        var request = new CreateAgentRequest(
            "host-limited-agent",
            new AgentConfig
            {
                Name = "Host Limited Agent",
                MaxAgenticIterations = 51
            });

        var response = await _client.PostAsJsonAsync("/agents", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("MaxFunctionCallTurns exceeds host limit of 50");
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
