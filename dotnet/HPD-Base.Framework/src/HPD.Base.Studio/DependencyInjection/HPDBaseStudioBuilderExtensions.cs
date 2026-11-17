using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Studio;

/// <summary>Configures the server-enforced BASE Studio interaction mode.</summary>
public sealed class HPDBaseStudioOptions
{
    /// <summary>Gets or sets whether commands may be disclosed and dispatched.</summary>
    public StudioMode Mode { get; set; } = StudioMode.Inspect;
    internal bool ShowBaseModule { get; init; } = true;
}

/// <summary>Installs the graph-owned BASE Studio module.</summary>
public static class HPDBaseStudioBuilderExtensions
{
    /// <summary>Adds the immutable BASE Studio contribution to the application Studio graph.</summary>
    /// <remarks>
    /// The contribution is materialized from the finalized BASE application identity. It does not
    /// register legacy capability strings, routes, clients, or mutable module options.
    /// </remarks>
    public static HPDAIPlatformBuilder AddHPDBaseStudio(this HPDAIPlatformBuilder builder,
        Func<IServiceProvider, IStudioPrincipalContextResolver> principalResolver,
        Action<HPDBaseStudioOptions>? configure = null) =>
        AddHPDBaseStudioGraph(builder, principalResolver, configure, showBaseModule: true);

    /// <summary>Adds BASE's required private Studio foundation without exposing BASE as a navigation module.</summary>
    public static HPDAIPlatformBuilder AddHPDBaseStudioHosting(this HPDAIPlatformBuilder builder,
        Func<IServiceProvider, IStudioPrincipalContextResolver> principalResolver,
        Action<HPDBaseStudioOptions>? configure = null) =>
        AddHPDBaseStudioGraph(builder, principalResolver, configure, showBaseModule: false);

    private static HPDAIPlatformBuilder AddHPDBaseStudioGraph(HPDAIPlatformBuilder builder,
        Func<IServiceProvider, IStudioPrincipalContextResolver> principalResolver,
        Action<HPDBaseStudioOptions>? configure, bool showBaseModule)
    {
        ArgumentNullException.ThrowIfNull(builder); ArgumentNullException.ThrowIfNull(principalResolver);
        if (builder.Services.Any(static descriptor => descriptor.ServiceType == typeof(IStudioPrincipalContextResolver)))
            throw new InvalidOperationException("A BASE Studio principal resolver is already installed.");
        builder.Services.AddSingleton(principalResolver);
        var options = new HPDBaseStudioOptions { ShowBaseModule = showBaseModule }; configure?.Invoke(options);
        if (!Enum.IsDefined(options.Mode)) throw new ArgumentOutOfRangeException(nameof(configure));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<HPDBaseStudioAuthorization>();
        builder.Services.AddSingleton<IStudioModuleRuntimeContributionFactory, HPDBaseStudioRuntimeContributionFactory>();
        builder.Services.AddSingleton<IStudioBootstrapRuntime, HPDBaseStudioBootstrapRuntime>();
        builder.Services.AddSingleton<IStudioResponseAuthorityValidator, HPDBaseStudioResponseAuthorityValidator>();
        builder.AddStudioEditionModuleAsset(HPDBaseStudioModuleRegistry.CreateEditionAssetContribution());
        return builder.AddStudioModule<HPDBaseStudioModuleContribution>();
    }
}
