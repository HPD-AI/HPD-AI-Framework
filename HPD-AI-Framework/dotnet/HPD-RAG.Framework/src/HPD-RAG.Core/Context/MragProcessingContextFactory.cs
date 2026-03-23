namespace HPD.RAG.Core.Context;

/// <summary>
/// Public factory for <see cref="MragProcessingContext"/>.
/// Exposes the internal constructor to the Pipeline layer without requiring
/// <c>InternalsVisibleTo</c> (which would couple the Core assembly to the Pipeline assembly name).
/// </summary>
public static class MragProcessingContextFactory
{
    /// <summary>
    /// Creates a <see cref="MragProcessingContext"/> wrapper over the supplied
    /// <paramref name="pipelineContext"/>.
    /// </summary>
    public static MragProcessingContext Create(MragPipelineContext pipelineContext)
        => new MragProcessingContext(pipelineContext);
}
