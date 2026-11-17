using HPD.AI.Platform.Studio;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Studio;

/// <summary>Contributes the fixed Agent Studio semantic graph.</summary>
public sealed class AgentStudioModuleContribution : IStudioModuleContribution
{
    /// <inheritdoc />
    public string ModuleId => "agents";

    /// <inheritdoc />
    public StudioModuleRegistration Create(IServiceProvider services) =>
        AgentStudioModuleRegistry.Create(services.GetRequiredService<HPDBaseStudioAuthoritySnapshot>());
}
