using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Studio;

/// <summary>Contributes BASE's fixed Studio page and resource graph.</summary>
public sealed class HPDBaseStudioModuleContribution : IStudioModuleContribution
{
    /// <inheritdoc />
    public string ModuleId => "base";
    /// <inheritdoc />
    public StudioModuleRegistration Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return HPDBaseStudioModuleRegistry.Create(services.GetRequiredService<HPDBaseStudioAuthoritySnapshot>());
    }
}
