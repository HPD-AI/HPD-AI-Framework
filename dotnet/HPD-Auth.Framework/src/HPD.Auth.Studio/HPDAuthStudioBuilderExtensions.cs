using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Studio;

public static class HPDAuthStudioBuilderExtensions
{
    /// <summary>Installs HPD.Auth as the Studio session and transport authority.</summary>
    public static HPDAIPlatformBuilder AddHPDAuthStudioAuthentication(
        this HPDAIPlatformBuilder builder, Action<HPDAuthStudioOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new HPDAuthStudioOptions();
        configure(options);
        builder.Services.AddSingleton(options);
        return builder.AddStudioAuthentication(services =>
            new HPDAuthStudioAuthenticationIntegration(services, options));
    }
}
