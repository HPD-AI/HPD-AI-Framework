namespace HPD.AI.Studio;

public static class HPDMLStudioBuilderExtensions
{
    public static HPDAIStudioBuilder AddMLStudio(this HPDAIStudioBuilder builder)
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
