namespace HPD.AI.Platform;

public static class HPDAgentStudioBuilderExtensions
{
    public static HPDAIPlatformBuilder AddAgentStudio(this HPDAIPlatformBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The platform no longer exposes the former string-based AddModule API.
        // Agent Studio's contribution is currently packaged as frontend assets;
        // keep this fluent registration method source-compatible until the
        // corresponding typed Studio contribution is available.
        return builder;
    }
}
