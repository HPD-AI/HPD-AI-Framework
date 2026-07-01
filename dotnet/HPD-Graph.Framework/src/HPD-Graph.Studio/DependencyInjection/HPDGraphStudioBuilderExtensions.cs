namespace HPD.AI.Platform;

public static class HPDGraphStudioBuilderExtensions
{
    public static HPDAIPlatformBuilder AddGraphStudio(this HPDAIPlatformBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddModule(
            "workflows",
            "Workflows",
            "HPD Graph Studio",
            "active",
            "graphs",
            "workflows",
            "multi-agent");
    }
}
