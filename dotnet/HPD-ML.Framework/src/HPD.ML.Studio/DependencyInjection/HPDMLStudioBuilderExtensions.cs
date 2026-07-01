namespace HPD.AI.Platform;

public static class HPDMLStudioBuilderExtensions
{
    public static HPDAIPlatformBuilder AddMLStudio(this HPDAIPlatformBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddModule(
            "ml",
            "ML",
            "HPD ML Studio",
            "active",
            "ml",
            "models",
            "training",
            "evaluations");
    }
}
