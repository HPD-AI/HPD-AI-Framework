namespace HPD.AI.Platform;

public static class HPDRagStudioBuilderExtensions
{
    public static HPDAIPlatformBuilder AddRagStudio(this HPDAIPlatformBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The platform no longer exposes the former string-based AddModule API.
        // RAG Studio currently contributes no typed Studio module; retain this
        // fluent registration point until that contribution is introduced.
        return builder;
    }
}
