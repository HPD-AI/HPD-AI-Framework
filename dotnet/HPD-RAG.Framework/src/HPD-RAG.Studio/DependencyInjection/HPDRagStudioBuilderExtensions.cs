namespace HPD.AI.Studio;

public static class HPDRagStudioBuilderExtensions
{
    public static HPDAIStudioBuilder AddRagStudio(this HPDAIStudioBuilder builder)
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
