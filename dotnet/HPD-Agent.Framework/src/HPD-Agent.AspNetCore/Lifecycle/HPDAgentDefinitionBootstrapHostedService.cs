using HPD.Agent.AspNetCore.DependencyInjection;
using HPD.Agent.Hosting.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Lifecycle;

/// <summary>
/// Defines each hosting profile's configured agent before requests are served.
/// Runtime builds never synthesize or persist additional definitions.
/// </summary>
internal sealed class HPDAgentDefinitionBootstrapHostedService(
    IEnumerable<HPDAgentHostRegistration> registrations,
    HPDAgentRegistry registry,
    IOptionsMonitor<HPDAgentHostOptions> optionsMonitor) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var name in registrations
                     .Select(static registration => registration.Name)
                     .Distinct(StringComparer.Ordinal))
        {
            var options = optionsMonitor.Get(name);
            var agentId = string.IsNullOrWhiteSpace(options.DefaultAgentId)
                ? string.IsNullOrWhiteSpace(name) ? "default" : name
                : options.DefaultAgentId;
            await registry.Get(name).AgentManager
                .EnsureConfiguredDefinitionAsync(agentId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
