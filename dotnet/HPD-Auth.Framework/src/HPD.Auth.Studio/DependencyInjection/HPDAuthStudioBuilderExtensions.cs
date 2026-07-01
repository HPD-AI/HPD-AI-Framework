namespace HPD.AI.Platform;

public static class HPDAuthStudioBuilderExtensions
{
    public static HPDAIPlatformBuilder AddAuthStudio(this HPDAIPlatformBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddModule(
            "auth",
            "Auth",
            "HPD Auth Studio",
            "active",
            "auth",
            "identity",
            "access-control");
    }
}
