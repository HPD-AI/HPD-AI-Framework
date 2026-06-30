namespace HPD.AI.Studio;

public static class HPDBaseStudioBuilderExtensions
{
    public static HPDAIStudioBuilder AddBaseStudio(this HPDAIStudioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddModule(
            "base",
            "BASE",
            "HPD BASE Studio",
            "active",
            "base",
            "records",
            "collections",
            "schemas",
            "stores",
            "files",
            "realtime",
            "policy",
            "health",
            "diagnostics");
    }
}
