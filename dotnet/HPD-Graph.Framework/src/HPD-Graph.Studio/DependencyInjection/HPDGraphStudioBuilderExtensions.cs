using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.AI.Platform;

public static class HPDGraphStudioBuilderExtensions
{
    public static HPDAIPlatformBuilder AddGraphStudio(this HPDAIPlatformBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddStudioEditionModuleAsset(HPD.Graph.Studio.GraphStudioModuleRegistry.CreateEditionAssetContribution());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseStudioFrameworkEndpointSurface, HPD.Graph.Studio.GraphStudioEndpointSurface>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseStudioModuleRuntimeContributionFactory, HPD.Graph.Studio.GraphStudioRuntimeContributionFactory>());
        return builder.AddStudioModule<HPD.Graph.Studio.GraphStudioModuleContribution>();
    }
}
