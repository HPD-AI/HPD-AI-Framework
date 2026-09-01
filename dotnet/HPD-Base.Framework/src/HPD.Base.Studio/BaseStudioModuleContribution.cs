using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Studio;

/// <summary>Contributes BASE's fixed Studio page and resource graph.</summary>
public sealed class BaseStudioModuleContribution : IBaseStudioModuleContribution
{
    /// <inheritdoc />
    public string ModuleId => "base";
    /// <inheritdoc />
    public BaseStudioModuleRegistration Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return BaseStudioModuleRegistry.Create(services.GetRequiredService<HPDBaseStudioAuthoritySnapshot>());
    }
}
