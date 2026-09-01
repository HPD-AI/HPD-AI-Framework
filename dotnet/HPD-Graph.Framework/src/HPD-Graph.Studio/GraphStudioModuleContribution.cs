using HPD.AI.Platform.Studio;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Studio;

/// <summary>Contributes Graph's fixed semantic Studio graph.</summary>
public sealed class GraphStudioModuleContribution : IBaseStudioModuleContribution
{
    /// <inheritdoc />
    public string ModuleId => "graph";
    /// <inheritdoc />
    public BaseStudioModuleRegistration Create(IServiceProvider services)
        => GraphStudioModuleRegistry.Create(services.GetRequiredService<HPDBaseStudioAuthoritySnapshot>());
}
