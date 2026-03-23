using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HPD.Agent.AspNetCore;
using HPD.Agent.Hosting.Configuration;

namespace HPD.Agent.AspNetCore.Tests.TestInfrastructure;

/// <summary>
/// Test web application factory for integration testing HPD-Agent.AspNetCore endpoints.
/// Creates a minimal test server with the HPD-Agent API configured without relying on solution root detection.
/// </summary>
public class TestWebApplicationFactory : IDisposable
{
    private TestServer? _server;
    private HttpClient? _client;
    private readonly FakeChatClient _fakeChatClient = new();

    /// <summary>
    /// Gets the shared FakeChatClient used by all agents in tests.
    /// Use this to queue responses before making requests.
    /// </summary>
    public FakeChatClient FakeChatClient => _fakeChatClient;

    public TestServer Server
    {
        get
        {
            EnsureServer();
            return _server!;
        }
    }

    public HttpClient CreateClient()
    {
        if (_client == null)
        {
            EnsureServer();
            _client = new HttpClient(_server!.CreateHandler());
            _client.BaseAddress = new Uri("http://localhost");
        }
        return _client;
    }

    public TestServer CreateServer()
    {
        EnsureServer();
        return _server!;
    }

    private void EnsureServer()
    {
        if (_server != null)
            return;

        var contentRoot = Path.Combine(Path.GetTempPath(), $"hpd-agent-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(contentRoot);

        var builder = new WebHostBuilder()
            .UseContentRoot(contentRoot)
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                // Register shared FakeChatClient so factory can use it
                services.AddSingleton(_fakeChatClient);
                // Register IAgentFactory to create test agents
                services.AddSingleton<IAgentFactory, TestWebApplicationAgentFactory>();
                services.AddHPDAgent("test-agent", options =>
                {
                    options.SessionStorePath = Path.Combine(Path.GetTempPath(), $"hpd-agent-tests-{Guid.NewGuid()}");
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    // Map at root level (no prefix) for integration tests
                    endpoints.MapGroup("").MapHPDAgentApi("test-agent");
                });
            });

        _server = new TestServer(builder);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _server?.Dispose();
    }
}

/// <summary>
/// Agent factory for integration tests that creates agents with test provider registry.
/// Uses the shared FakeChatClient from DI.
/// </summary>
internal class TestWebApplicationAgentFactory : IAgentFactory
{
    private readonly FakeChatClient _fakeChatClient;

    public TestWebApplicationAgentFactory(FakeChatClient fakeChatClient)
    {
        _fakeChatClient = fakeChatClient ?? throw new ArgumentNullException(nameof(fakeChatClient));
    }

    public async Task<HPD.Agent.Agent> CreateAgentAsync(
        string sessionId,
        ISessionStore store,
        CancellationToken ct = default)
    {
        var config = new AgentConfig
        {
            Name = "TestAgent",
            MaxAgenticIterations = 50,
            Provider = new ProviderConfig
            {
                ProviderKey = "test",
                ModelName = "test-model"
            }
        };

        var providerRegistry = new TestProviderRegistry(_fakeChatClient);

        return await new AgentBuilder(config, providerRegistry)
            .WithSessionStore(store, options =>
            {
                options.PersistAfterTurn = true;
            })
            .WithCircuitBreaker(5)
            .BuildAsync(ct);
    }
}
