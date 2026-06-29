namespace HPD.AI.Studio;

public static class HPDAuthStudioBuilderExtensions
{
    public static HPDAIStudioBuilder AddAuthStudio(this HPDAIStudioBuilder builder)
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
