namespace HPD.AI.Platform;

public static class HPDRagStudioBuilderExtensions
{
    public static HPDAIPlatformBuilder AddRagStudio(this HPDAIPlatformBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddModule(
            "rag",
            "RAG",
            "HPD RAG Studio",
            "active",
            "rag",
            "retrieval",
            "indexes");
    }
}
