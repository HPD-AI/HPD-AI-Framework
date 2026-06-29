namespace HPD.AI.Studio;

public static class HPDGraphStudioBuilderExtensions
{
    public static HPDAIStudioBuilder AddGraphStudio(this HPDAIStudioBuilder builder)
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
