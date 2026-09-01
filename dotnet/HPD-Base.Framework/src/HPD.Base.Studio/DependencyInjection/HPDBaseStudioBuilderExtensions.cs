using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Studio;

/// <summary>Configures the server-enforced BASE Studio interaction mode.</summary>
public sealed class HPDBaseStudioOptions
{
    /// <summary>Gets or sets whether commands may be disclosed and dispatched.</summary>
    public BaseStudioMode Mode { get; set; } = BaseStudioMode.Inspect;
}

/// <summary>Installs the graph-owned BASE Studio module.</summary>
public static class HPDBaseStudioBuilderExtensions
{
    /// <summary>Adds the immutable BASE Studio contribution to the application Studio graph.</summary>
    /// <remarks>
    /// The contribution is materialized from the finalized BASE application identity. It does not
    /// register legacy capability strings, routes, clients, or mutable module options.
    /// </remarks>
    public static HPDAIPlatformBuilder AddBaseStudio(this HPDAIPlatformBuilder builder,
        Func<IServiceProvider, IBaseStudioPrincipalContextResolver> principalResolver,
        Action<HPDBaseStudioOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder); ArgumentNullException.ThrowIfNull(principalResolver);
        if (builder.Services.Any(static descriptor => descriptor.ServiceType == typeof(IBaseStudioPrincipalContextResolver)))
            throw new InvalidOperationException("A BASE Studio principal resolver is already installed.");
        builder.Services.AddSingleton(principalResolver);
        var options = new HPDBaseStudioOptions(); configure?.Invoke(options);
        if (!Enum.IsDefined(options.Mode)) throw new ArgumentOutOfRangeException(nameof(configure));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<BaseStudioAuthorization>();
        builder.Services.AddSingleton<IBaseStudioModuleRuntimeContributionFactory, BaseStudioRuntimeContributionFactory>();
        builder.Services.AddSingleton<IBaseStudioBootstrapRuntime, BaseStudioBootstrapRuntime>();
        builder.Services.AddSingleton<IBaseStudioResponseAuthorityValidator, BaseStudioResponseAuthorityValidator>();
        builder.AddStudioEditionModuleAsset(BaseStudioModuleRegistry.CreateEditionAssetContribution());
        return builder.AddStudioModule<BaseStudioModuleContribution>();
    }
}
