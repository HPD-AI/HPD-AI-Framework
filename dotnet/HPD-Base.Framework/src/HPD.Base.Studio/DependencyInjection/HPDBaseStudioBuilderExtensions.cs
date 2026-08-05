using HPD.AI.Platform;

namespace HPD.Base.Studio;

/// <summary>Represents a hpdbase studio builder extensions.</summary>
public static class HPDBaseStudioBuilderExtensions
{
    /// <summary>Executes the add base studio operation.</summary>
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
