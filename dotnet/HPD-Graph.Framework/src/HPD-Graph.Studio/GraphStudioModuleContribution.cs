using HPD.AI.Platform.Studio;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Studio;

/// <summary>Contributes Graph's fixed semantic Studio graph.</summary>
public sealed class GraphStudioModuleContribution : IStudioModuleContribution
{
    /// <inheritdoc />
    public string ModuleId => "graph";
    /// <inheritdoc />
    public StudioModuleRegistration Create(IServiceProvider services)
        => GraphStudioModuleRegistry.Create(services.GetRequiredService<HPDBaseStudioAuthoritySnapshot>());
}
