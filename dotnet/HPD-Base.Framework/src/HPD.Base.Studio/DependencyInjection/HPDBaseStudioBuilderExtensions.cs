using HPD.AI.Platform;

namespace HPD.Base.Studio;

public static class HPDBaseStudioBuilderExtensions
{
    public static HPDAIPlatformBuilder AddBaseStudio(this HPDAIPlatformBuilder builder)
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
