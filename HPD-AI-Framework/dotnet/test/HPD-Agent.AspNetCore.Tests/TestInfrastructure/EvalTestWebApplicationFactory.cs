// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.AspNetCore;
using HPD.Agent.Evaluations.Storage;
using HPD.Agent;
using HPD.Agent.Hosting.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.Tests.TestInfrastructure;

/// <summary>
/// Test web application factory that registers an InMemoryScoreStore as IScoreStore,
/// enabling integration tests for the /evals endpoint group.
/// </summary>
public class EvalTestWebApplicationFactory : IDisposable
{
    private TestServer? _server;
    private HttpClient? _client;
    private readonly FakeChatClient _fakeChatClient = new();

    /// <summary>The in-memory score store shared between all requests in this test instance.</summary>
    public InMemoryScoreStore ScoreStore { get; } = new InMemoryScoreStore();

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

    private void EnsureServer()
    {
        if (_server != null)
            return;

        var contentRoot = Path.Combine(Path.GetTempPath(), $"hpd-eval-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(contentRoot);

        var builder = new WebHostBuilder()
            .UseContentRoot(contentRoot)
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(_fakeChatClient);
                services.AddSingleton<IAgentFactory, TestWebApplicationAgentFactory>();
                // Register the shared score store so EvalEndpoints can resolve it
                services.AddSingleton<IScoreStore>(ScoreStore);
                services.AddHPDAgent("test-agent", options =>
                {
                    options.SessionStore = new JsonSessionStore(Path.Combine(Path.GetTempPath(), $"hpd-eval-tests-{Guid.NewGuid()}"));
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
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
